using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using HMI.Core.Model;
using HMI.Core.Services;

namespace HMI.Server;

public static class BackupRestoreService
{
    private const long MaxBackupBytes = 25L * 1024 * 1024;
    private const long MaxProjectBytes = 10L * 1024 * 1024;
    private static readonly string[] RequiredAlarmColumns =
    [
        "Sequence", "AlarmId", "RuleId", "RuleName", "VarId",
        "EventType", "Message", "Value", "Quality", "OccurredAt"
    ];

    public static async Task<(bool Ok, string Message)> StageAsync(
        Stream upload, long length, string contentRoot, CancellationToken ct)
    {
        if (length <= 0 || length > MaxBackupBytes)
            return (false, "备份文件为空或超过 25 MB 限制");
        var data = Path.Combine(contentRoot, "data");
        var pending = Path.Combine(data, "restore-pending");
        var temp = Path.Combine(data, $"restore-staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(data);
        if (Directory.Exists(pending))
            return (false, "已有待应用的恢复任务；请先重启 HMIStudio 完成恢复");
        Directory.CreateDirectory(temp);
        try
        {
            using var archive = new ZipArchive(upload, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.GroupBy(e => e.FullName, StringComparer.Ordinal).Any(g => g.Count() > 1))
                return (false, "备份包包含重复条目");
            var manifestEntry = archive.GetEntry("manifest.json");
            var projects = archive.Entries.Where(e => e.FullName.StartsWith("project/", StringComparison.Ordinal) &&
                e.FullName.EndsWith(".hmijob", StringComparison.Ordinal) &&
                !e.FullName["project/".Length..].Contains('/')).ToArray();
            var dbEntry = archive.GetEntry("data/alarms.sqlite");
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "manifest.json", "data/alarms.sqlite" };
            foreach (var e in projects) allowed.Add(e.FullName);
            if (archive.Entries.Any(e => e.FullName.EndsWith("/", StringComparison.Ordinal) || !allowed.Contains(e.FullName)))
                return (false, "备份包包含不支持的条目");
            if (manifestEntry is null || projects.Length != 1)
                return (false, "备份包结构无效：必须包含清单和唯一工程文件");
            if (manifestEntry.Length > 64 * 1024 || projects[0].Length > MaxProjectBytes ||
                (dbEntry is not null && dbEntry.Length > MaxBackupBytes))
                return (false, "备份包中的文件超过允许大小");

            using var manifestStream = manifestEntry.Open();
            using var document = await JsonDocument.ParseAsync(manifestStream, cancellationToken: ct);
            var root = document.RootElement;
            if (!root.TryGetProperty("product", out var product) || product.ValueKind != JsonValueKind.String ||
                product.GetString() != "HMIStudio" ||
                !root.TryGetProperty("backupFormatVersion", out var version) || !version.TryGetInt32(out var v) || v != 1)
                return (false, "不支持的 HMIStudio 备份格式");
            var projectName = projects[0].FullName["project/".Length..];
            if (!root.TryGetProperty("projectFile", out var declaredName) ||
                declaredName.ValueKind != JsonValueKind.String || declaredName.GetString() != projectName ||
                !root.TryGetProperty("alarmHistoryIncluded", out var hasDb) ||
                hasDb.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                hasDb.GetBoolean() != (dbEntry is not null))
                return (false, "备份清单与实际文件不一致");

            var stagedProject = Path.Combine(temp, "project.hmijob");
            await using (var src = projects[0].Open())
            await using (var dst = new FileStream(stagedProject, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await CopyLimitedAsync(src, dst, MaxProjectBytes, ct);
            var storage = new ProjectStorage();
            var errors = await storage.ValidateAsync(stagedProject, ct);
            if (errors.Count != 0) return (false, "工程文件校验失败：" + string.Join("；", errors));
            var project = HmiJson.Deserialize(await File.ReadAllTextAsync(stagedProject, ct));
            if (project is null) return (false, "工程文件无法解析");

            if (dbEntry is not null)
            {
                var stagedDb = Path.Combine(temp, "alarms.sqlite");
                await using (var src = dbEntry.Open())
                await using (var dst = new FileStream(stagedDb, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await CopyLimitedAsync(src, dst, MaxBackupBytes, ct);
                var dbResult = await ValidateAlarmDatabaseAsync(stagedDb, ct);
                if (!dbResult.Ok) return (false, dbResult.Message);
            }
            await File.WriteAllTextAsync(Path.Combine(temp, "manifest.json"), JsonSerializer.Serialize(new
            {
                projectFile = projectName,
                alarmHistoryIncluded = dbEntry is not null
            }), ct);
            Directory.Move(temp, pending);
            return (true, dbEntry is null
                ? "备份已通过二次校验并暂存。该备份不含报警历史，因此现有报警历史将保留。请重启 HMIStudio 应用恢复。"
                : "备份已通过二次校验并暂存。请重启 HMIStudio 应用工程与报警历史恢复。");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or SqliteException or InvalidOperationException or ArgumentException)
        {
            return (false, "无法暂存备份：" + ex.Message);
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public static async Task ApplyPendingAsync(string contentRoot, string projectPath, string alarmPath, CancellationToken ct)
    {
        var data = Path.Combine(contentRoot, "data");
        var pending = Path.Combine(data, "restore-pending");
        if (!Directory.Exists(pending)) return;
        var rollback = Path.Combine(data, "restore-rollback");
        Directory.CreateDirectory(data);

        // If a previous startup was interrupted mid-apply, restore the original files first,
        // then retry the staged transaction from its immutable payload.
        if (Directory.Exists(rollback))
            await RestoreRollbackAsync(rollback, projectPath, alarmPath, ct);

        var markerPath = Path.Combine(pending, "manifest.json");
        using var marker = JsonDocument.Parse(await File.ReadAllTextAsync(markerPath, ct));
        var markerRoot = marker.RootElement;
        var hasDb = markerRoot.GetProperty("alarmHistoryIncluded").GetBoolean();
        var stagedProject = Path.Combine(pending, "project.hmijob");
        var stagedDb = Path.Combine(pending, "alarms.sqlite");
        var errors = await new ProjectStorage().ValidateAsync(stagedProject, ct);
        if (errors.Count != 0) throw new InvalidDataException("待恢复工程校验失败：" + string.Join("；", errors));
        if (hasDb)
        {
            var check = await ValidateAlarmDatabaseAsync(stagedDb, ct);
            if (!check.Ok) throw new InvalidDataException("待恢复报警数据库校验失败：" + check.Message);
        }

        Directory.CreateDirectory(rollback);
        var projectExisted = File.Exists(projectPath);
        var dbExisted = File.Exists(alarmPath);
        if (projectExisted) File.Copy(projectPath, Path.Combine(rollback, "project.original"), true);
        if (dbExisted && hasDb) File.Copy(alarmPath, Path.Combine(rollback, "alarms.original"), true);
        await File.WriteAllTextAsync(Path.Combine(rollback, "state.json"), JsonSerializer.Serialize(new { projectExisted, alarmDatabaseWasTouched = hasDb, dbExisted }), ct);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            await ReplaceFileAsync(stagedProject, projectPath, ct);
            if (hasDb) await ReplaceFileAsync(stagedDb, alarmPath, ct);
        }
        catch
        {
            await RestoreRollbackAsync(rollback, projectPath, alarmPath, CancellationToken.None);
            throw;
        }

        // Remove the rollback journal first. If shutdown interrupts cleanup, the pending
        // payload can safely be applied again on the next startup.
        Directory.Delete(rollback, true);
        Directory.Delete(pending, true);
    }

    private static async Task RestoreRollbackAsync(string rollback, string projectPath, string alarmPath, CancellationToken ct)
    {
        var statePath = Path.Combine(rollback, "state.json");
        if (!File.Exists(statePath)) { Directory.Delete(rollback, true); return; }
        using var state = JsonDocument.Parse(await File.ReadAllTextAsync(statePath, ct));
        var root = state.RootElement;
        if (root.GetProperty("projectExisted").GetBoolean())
        {
            var original = Path.Combine(rollback, "project.original");
            if (File.Exists(original)) await ReplaceFileAsync(original, projectPath, ct);
        }
        else if (File.Exists(projectPath)) File.Delete(projectPath);
        if (root.GetProperty("alarmDatabaseWasTouched").GetBoolean())
        {
            if (root.GetProperty("dbExisted").GetBoolean())
            {
                var original = Path.Combine(rollback, "alarms.original");
                if (File.Exists(original)) await ReplaceFileAsync(original, alarmPath, ct);
            }
            else if (File.Exists(alarmPath))
            {
                File.Delete(alarmPath);
            }
        }
        // If the restore payload did not include alarms, it never touched the live database.
        Directory.Delete(rollback, true);
    }

    private static async Task ReplaceFileAsync(string source, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var tempTarget = target + ".restore-" + Guid.NewGuid().ToString("N");
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
        await using (var output = new FileStream(tempTarget, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await input.CopyToAsync(output, ct);
        File.Move(tempTarget, target, true);
    }

    private static async Task<(bool Ok, string Message)> ValidateAlarmDatabaseAsync(string path, CancellationToken ct)
    {
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        await db.OpenAsync(ct);
        await using var integrity = db.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";
        if (Convert.ToString(await integrity.ExecuteScalarAsync(ct)) != "ok")
            return (false, "SQLite 完整性检查未通过");
        await using var schema = db.CreateCommand();
        schema.CommandText = "SELECT name FROM pragma_table_info('AlarmHistory');";
        var columns = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await schema.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(0));
        if (RequiredAlarmColumns.Any(name => !columns.Contains(name)))
            return (false, "报警历史表缺少必要字段");
        return (true, "报警数据库校验通过");
    }

    private static async Task CopyLimitedAsync(Stream source, Stream destination, long maxBytes, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0) return;
            total += read;
            if (total > maxBytes) throw new InvalidDataException("解压后的文件超过大小限制");
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
}
