using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using System.Threading.Channels;
using HMI.Core.Interface;
using HMI.Core.Model;
using HMI.Core.Services;
using HMI.Server;

var currentDirectory = Directory.GetCurrentDirectory();
var projectDirectory = Path.Combine(currentDirectory, "src", "HMI.Server");
var contentRoot = Directory.Exists(projectDirectory) ? projectDirectory : currentDirectory;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = contentRoot });
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("HMISTUDIO_URLS") ?? "http://127.0.0.1:5080");
builder.Services.AddSingleton<RuntimeProjectService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RuntimeProjectService>());
builder.Services.AddCors(options => options.AddPolicy("LocalEditor", policy =>
    policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors("LocalEditor");
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", (RuntimeProjectService runtime) => Results.Ok(new
{
    status = "ok",
    service = "HMIStudio",
    runtime = "running",
    uptimeSeconds = (long)runtime.Uptime.TotalSeconds
}));

app.MapGet("/api/project", (RuntimeProjectService runtime) => Results.Ok(runtime.GetProjectSummary()));

app.MapGet("/api/backup", async (RuntimeProjectService runtime, CancellationToken ct) =>
{
    var temporaryDatabase = Path.Combine(Path.GetTempPath(), $"hmistudio-backup-{Guid.NewGuid():N}.sqlite");
    try
    {
        await using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var projectEntry = archive.CreateEntry($"project/{Path.GetFileName(runtime.ProjectPath)}", CompressionLevel.Optimal);
            await using (var entryStream = projectEntry.Open())
            await using (var projectStream = new FileStream(runtime.ProjectPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, useAsync: true))
                await projectStream.CopyToAsync(entryStream, ct);

            var alarmHistoryIncluded = false;
            if (File.Exists(runtime.AlarmDatabasePath))
            {
                await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = runtime.AlarmDatabasePath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString()))
                await using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = temporaryDatabase,
                    Pooling = false
                }.ToString()))
                {
                    await source.OpenAsync(ct);
                    await destination.OpenAsync(ct);
                    source.BackupDatabase(destination);
                }

                var alarmEntry = archive.CreateEntry("data/alarms.sqlite", CompressionLevel.Optimal);
                await using var entryStream = alarmEntry.Open();
                await using var databaseStream = new FileStream(temporaryDatabase, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                await databaseStream.CopyToAsync(entryStream, ct);
                alarmHistoryIncluded = true;
            }

            var manifest = new
            {
                product = "HMIStudio",
                backupFormatVersion = 1,
                createdAtUtc = DateTimeOffset.UtcNow,
                projectFile = Path.GetFileName(runtime.ProjectPath),
                alarmHistoryIncluded
            };
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using (var manifestStream = manifestEntry.Open())
                await JsonSerializer.SerializeAsync(manifestStream, manifest, HmiJson.Options, ct);
        }

        return Results.File(output.ToArray(), "application/zip", $"HMIStudio-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
    {
        return Results.Problem(title: "创建工程备份失败", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
    finally
    {
        try { if (File.Exists(temporaryDatabase)) File.Delete(temporaryDatabase); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
});
app.MapPost("/api/backup/inspect", async (IFormFile file, CancellationToken ct) =>
{
    const long maxBackupBytes = 25L * 1024 * 1024;
    if (file.Length <= 0 || file.Length > maxBackupBytes)
        return (IResult)Results.BadRequest(new { ok = false, error = "备份文件为空或超过 25 MB 限制" });

    var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"hmistudio-inspect-{Guid.NewGuid():N}");
    Directory.CreateDirectory(temporaryDirectory);
    try
    {
        await using var upload = file.OpenReadStream();
        using var archive = new ZipArchive(upload, ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntry = archive.GetEntry("manifest.json");
        var projectEntries = archive.Entries
            .Where(entry => entry.FullName.StartsWith("project/", StringComparison.Ordinal) &&
                            entry.FullName.EndsWith(".hmijob", StringComparison.Ordinal) &&
                            !entry.FullName["project/".Length..].Contains('/'))
            .ToArray();
        var alarmEntry = archive.GetEntry("data/alarms.sqlite");
        if (manifestEntry is null || projectEntries.Length != 1)
            return (IResult)Results.BadRequest(new { ok = false, error = "备份包结构无效：必须包含 manifest.json 和唯一的 project/*.hmijob" });
        if (manifestEntry.Length > 64 * 1024 || projectEntries[0].Length > 10 * 1024 * 1024 ||
            (alarmEntry is not null && alarmEntry.Length > maxBackupBytes))
            return (IResult)Results.BadRequest(new { ok = false, error = "备份包中的文件超过允许大小" });

        using var manifestStream = manifestEntry.Open();
        using var manifestDocument = await JsonDocument.ParseAsync(manifestStream, cancellationToken: ct);
        var manifest = manifestDocument.RootElement;
        if (!manifest.TryGetProperty("product", out var product) || product.GetString() != "HMIStudio" ||
            !manifest.TryGetProperty("backupFormatVersion", out var formatVersion) || formatVersion.GetInt32() != 1)
            return (IResult)Results.BadRequest(new { ok = false, error = "不支持的 HMIStudio 备份格式" });

        var projectPath = Path.Combine(temporaryDirectory, "inspect.hmijob");
        await using (var input = projectEntries[0].Open())
        await using (var output = new FileStream(projectPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await CopyWithLimitAsync(input, output, 10 * 1024 * 1024, ct);

        var projectErrors = await new ProjectStorage().ValidateAsync(projectPath, ct);
        HmiProject? project = null;
        if (projectErrors.Count == 0)
        {
            var json = await File.ReadAllTextAsync(projectPath, ct);
            project = HmiJson.Deserialize(json);
        }

        string? alarmDatabaseCheck = null;
        if (alarmEntry is not null)
        {
            var alarmPath = Path.Combine(temporaryDirectory, "alarms.sqlite");
            await using (var input = alarmEntry.Open())
            await using (var output = new FileStream(alarmPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await CopyWithLimitAsync(input, output, maxBackupBytes, ct);

            await using var database = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = alarmPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            await database.OpenAsync(ct);
            await using var command = database.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            alarmDatabaseCheck = Convert.ToString(await command.ExecuteScalarAsync(ct));
        }

        var projectFileName = projectEntries[0].FullName["project/".Length..];
        var valid = projectErrors.Count == 0 && (alarmEntry is null || alarmDatabaseCheck == "ok");
        return (IResult)Results.Ok(new
        {
            ok = valid,
            backupFormatVersion = 1,
            projectFile = projectFileName,
            projectValid = projectErrors.Count == 0,
            projectErrors,
            screenCount = project?.Screens.Count ?? 0,
            variableCount = project?.Variables.Count ?? 0,
            startScreenId = project?.StartScreenId,
            alarmHistoryIncluded = alarmEntry is not null,
            alarmDatabaseIntegrity = alarmDatabaseCheck,
            message = valid ? "备份包结构和内容校验通过；此操作未修改当前工程。" : "备份包校验未通过；当前工程未被修改。"
        });
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or SqliteException or InvalidOperationException or ArgumentException)
    {
        return (IResult)Results.BadRequest(new { ok = false, error = "无法读取备份包：" + ex.Message });
    }
    finally
    {
        try { Directory.Delete(temporaryDirectory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}).DisableAntiforgery();

static async Task CopyWithLimitAsync(Stream source, Stream destination, long maxBytes, CancellationToken ct)
{
    var buffer = new byte[81920];
    long total = 0;
    while (true)
    {
        var read = await source.ReadAsync(buffer, ct);
        if (read == 0) break;
        total += read;
        if (total > maxBytes) throw new InvalidDataException("解压后的文件超过大小限制");
        await destination.WriteAsync(buffer.AsMemory(0, read), ct);
    }
}

app.MapGet("/api/project/validate", async (RuntimeProjectService runtime, CancellationToken ct) =>
{
    try
    {
        var errors = await runtime.ValidateProjectAsync(ct);
        return Results.Ok(new { ok = errors.Count == 0, errors });
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        return Results.Problem(title: "工程校验失败", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});
app.MapPut("/api/project/start-screen", async (SetStartScreenRequest request, RuntimeProjectService runtime, CancellationToken ct) =>
{
    try
    {
        var error = await runtime.SetStartScreenAsync(request.ScreenId, ct);
        if (error is not null) return (IResult)Results.BadRequest(new { error });
        return (IResult)Results.Ok(new { ok = true, startScreenId = request.ScreenId });
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        return (IResult)Results.Problem(title: "设置启动画面失败", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});
app.MapGet("/api/drivers", (RuntimeProjectService runtime) =>
{
    var drivers = runtime.Project.Variables
        .Where(variable => variable.Kind == VariableKind.Plc &&
                           variable.Source is not null &&
                           !string.IsNullOrWhiteSpace(variable.Source.Channel))
        .GroupBy(variable => variable.Source!.Channel, StringComparer.OrdinalIgnoreCase)
        .Select(group =>
        {
            var variables = group.ToArray();
            var driver = runtime.GetMockDriver(group.Key);
            var variableDetails = variables.Select(variable => new
            {
                variable.VarId,
                variable.Name,
                variable.CycleMs,
                Address = variable.Source?.Address,
                Quality = runtime.Pool.GetQuality(variable.VarId).ToString()
            }).ToArray();
            var goodCount = variableDetails.Count(item => item.Quality == "Good");
            return new
            {
                protocol = group.Key,
                driverType = "模拟 PLC 驱动",
                status = driver?.Status.ToString() ?? "Unavailable",
                variableCount = variables.Length,
                goodVariableCount = goodCount,
                badVariableCount = variables.Length - goodCount,
                variables = variableDetails
            };
        }).ToArray();
    return Results.Ok(drivers);
});
app.MapGet("/api/screens", (RuntimeProjectService runtime) => Results.Ok(runtime.Project.Screens.Select(screen => new
{
    screen.Id, screen.Title, screen.Background, screen.ScreenType, screen.Width, screen.Height,
    ControlCount = screen.Controls.Count
})));
app.MapPost("/api/screens", async (CreateScreenRequest request, RuntimeProjectService runtime, CancellationToken ct) =>
{
    var screen = new HmiScreen
    {
        Id = request.Id,
        Title = request.Title,
        Background = request.Background ?? "#111827",
        ScreenType = ScreenType.Normal,
        Width = request.Width,
        Height = request.Height,
        Controls = new List<ConfigControlItem>()
    };
    try
    {
        var errors = await runtime.CreateScreenAsync(screen, ct);
        if (errors.Count > 0) return (IResult)Results.BadRequest(new { error = string.Join("；", errors), errors });
        return (IResult)Results.Created($"/api/screens/{Uri.EscapeDataString(screen.Id)}", new { ok = true, screenId = screen.Id });
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        return (IResult)Results.Problem(title: "新建画面失败", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});
app.MapDelete("/api/screens/{screenId}", async (string screenId, RuntimeProjectService runtime, CancellationToken ct) =>
{
    try
    {
        var error = await runtime.DeleteScreenAsync(screenId, ct);
        if (error is not null)
        {
            if (error == "画面不存在") return (IResult)Results.NotFound(new { error, screenId });
            return (IResult)Results.BadRequest(new { error, screenId });
        }
        return (IResult)Results.Ok(new { ok = true, screenId, deleted = true });
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        return (IResult)Results.Problem(title: "删除画面失败", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});
app.MapGet("/api/screens/{screenId}", (string screenId, RuntimeProjectService runtime) =>
{
    var screen = runtime.Project.Screens.FirstOrDefault(s => string.Equals(s.Id, screenId, StringComparison.Ordinal));
    if (screen is null) return (IResult)Results.NotFound(new { error = "画面不存在", screenId });
    return (IResult)Results.Ok(screen);
});
app.MapPut("/api/screens/{screenId}", async (string screenId, HmiScreen screen, RuntimeProjectService runtime, CancellationToken ct) =>
{
    try
    {
        var errors = await runtime.SaveScreenAsync(screenId, screen, ct);
        if (errors.Count > 0) return (IResult)Results.BadRequest(new { error = string.Join("；", errors), errors });
        return (IResult)Results.Ok(new { ok = true, screenId, saved = true });
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        return (IResult)Results.Problem(title: "工程保存失败", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});
app.MapGet("/api/alarms/active", async (RuntimeProjectService runtime, CancellationToken ct) =>
    Results.Ok(await runtime.Alarms.GetActiveAsync(ct)));

app.MapGet("/api/alarms/history", async (int? limit, RuntimeProjectService runtime, CancellationToken ct) =>
    Results.Ok(await runtime.Alarms.GetHistoryAsync(limit ?? 100, ct)));

app.MapPost("/api/alarms/{alarmId}/acknowledge", async (string alarmId, RuntimeProjectService runtime, CancellationToken ct) =>
{
    try
    {
        await runtime.Alarms.AcknowledgeAsync(alarmId, ct);
        return (IResult)Results.Ok(new { ok = true, alarmId, acknowledged = true });
    }
    catch (KeyNotFoundException)
    {
        return (IResult)Results.NotFound(new { error = "活动报警不存在", alarmId });
    }
});

app.MapGet("/api/variables", (RuntimeProjectService runtime) => Results.Ok(runtime.GetVariableSnapshot()));
app.MapGet("/api/variables/{varId}", (string varId, RuntimeProjectService runtime) =>
{
    var meta = runtime.Project.Variables.FirstOrDefault(v => v.VarId == varId);
    if (meta is null) return (IResult)Results.NotFound(new { error = "变量不存在", varId });
    runtime.Pool.TryGetValue(varId, out var value);
    return (IResult)Results.Ok(new { meta.VarId, meta.Name, meta.DataType, meta.Min, meta.Max, Writable = !meta.IsReadOnly, Value = value, Quality = runtime.Pool.GetQuality(varId).ToString() });
});
app.MapPost("/api/variables/{varId}/write", async (string varId, WriteVariableRequest request, RuntimeProjectService runtime, CancellationToken ct) =>
{
    var result = await runtime.Pool.WriteAsync(varId, NormalizeJsonValue(request.Value), ct, WriteSource.Runtime, request.RequestId);
    if (result.Ok) return (IResult)Results.Ok(result);
    return (IResult)Results.BadRequest(result);
});
app.MapGet("/api/runtime/stream", async (HttpContext context, RuntimeProjectService runtime, CancellationToken ct) =>
{
    var requested = context.Request.Query["varIds"].ToString();
    var ids = string.IsNullOrWhiteSpace(requested)
        ? runtime.Project.Variables.Select(v => v.VarId).ToArray()
        : requested.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToArray();
    var unknown = ids.Where(id => !runtime.Pool.Contains(id)).ToArray();
    if (unknown.Length > 0)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = "包含不存在的变量", varIds = unknown }, ct);
        return;
    }

    context.Response.ContentType = "text/event-stream";
    context.Response.Headers["Cache-Control"] = "no-cache";
    context.Response.Headers["Connection"] = "keep-alive";
    var channel = Channel.CreateBounded<VariableValuePackage>(new BoundedChannelOptions(128)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    using var consumer = runtime.Subscriptions.RegisterPollingConsumer(item =>
    {
        if (ids.Contains(item.VarId, StringComparer.Ordinal))
            channel.Writer.TryWrite(item);
    });
    await runtime.Subscriptions.SubscribeAsync(ids, ct);
    try
    {
        await context.Response.WriteAsync("event: ready\ndata: {}\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
        await foreach (var item in channel.Reader.ReadAllAsync(ct))
        {
            var json = JsonSerializer.Serialize(new
            {
                item.VarId, item.Value, Quality = item.Quality.ToString(),
                item.DeviceTimestamp, item.ServerTimestamp
            }, HmiJson.Options);
            await context.Response.WriteAsync($"event: variable\ndata: {json}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    finally
    {
        channel.Writer.TryComplete();
        await runtime.Subscriptions.UnsubscribeAsync(ids, CancellationToken.None);
    }
});

app.MapPost("/api/runtime/mock-values", (MockValueRequest request, RuntimeProjectService runtime) =>
{
    if (string.IsNullOrWhiteSpace(request.Protocol))
        return (IResult)Results.BadRequest(new { error = "协议名称不能为空" });

    if (string.IsNullOrWhiteSpace(request.Address))
        return (IResult)Results.BadRequest(new { error = "地址不能为空" });

    var protocol = request.Protocol.Trim();
    var address = request.Address.Trim();

    var driver = runtime.GetMockDriver(protocol);
    if (driver is null)
        return (IResult)Results.BadRequest(new { error = "模拟驱动不可用", protocol });

    var mappedVariables = runtime.Project.Variables
        .Where(variable => variable.Kind == VariableKind.Plc &&
                           string.Equals(variable.Source?.Channel, protocol, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(variable.Source?.Address, address, StringComparison.OrdinalIgnoreCase))
        .ToArray();
    if (mappedVariables.Length == 0)
        return (IResult)Results.BadRequest(new { error = "地址未映射到任何 PLC 变量", protocol, address });

    var value = NormalizeJsonValue(request.Value);
    foreach (var variable in mappedVariables)
    {
        if (!IsMockValueCompatible(variable.DataType, value, variable.Min, variable.Max, out var validationError))
            return (IResult)Results.BadRequest(new { error = validationError, protocol, address, varId = variable.VarId });
    }

    driver.SetValue(address, value);
    return (IResult)Results.Ok(new { ok = true, protocol, address, request.Value });
});

app.Run();

static bool IsMockValueCompatible(string dataType, object? value, double? min, double? max, out string error)
{
    var type = dataType.Trim().ToLowerInvariant();
    error = $"注入值与变量数据类型 {dataType} 不匹配";

    if (type == "bool") return value is bool;
    if (type == "string") return value is string;

    if (type is not ("byte" or "int" or "dint" or "float" or "double" or "real"))
        return false;

    if (value is not (byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal))
        return false;

    var number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
    if (!double.IsFinite(number)) return false;

    if ((type is "byte" or "int" or "dint") && Math.Truncate(number) != number)
    {
        error = $"数据类型 {dataType} 需要整数";
        return false;
    }

    var typeMin = type switch
    {
        "byte" => (double)byte.MinValue,
        "int" => (double)short.MinValue,
        "dint" => (double)int.MinValue,
        "float" => -(double)float.MaxValue,
        _ => double.NegativeInfinity
    };
    var typeMax = type switch
    {
        "byte" => (double)byte.MaxValue,
        "int" => (double)short.MaxValue,
        "dint" => (double)int.MaxValue,
        "float" => (double)float.MaxValue,
        _ => double.PositiveInfinity
    };

    if (number < typeMin || number > typeMax || (min.HasValue && number < min.Value) || (max.HasValue && number > max.Value))
    {
        error = $"注入值 {number} 超出变量数据类型或配置量程";
        return false;
    }

    return true;
}

static object? NormalizeJsonValue(JsonElement value) => value.ValueKind switch
{
    JsonValueKind.String => value.GetString(),
    JsonValueKind.True => true,
    JsonValueKind.False => false,
    JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
    JsonValueKind.Number when value.TryGetDouble(out var number) => number,
    JsonValueKind.Null or JsonValueKind.Undefined => null,
    _ => value.Clone()
};

public sealed record WriteVariableRequest(JsonElement Value, string? RequestId = null);
public sealed record MockValueRequest(string Protocol, string Address, JsonElement Value);
public sealed record CreateScreenRequest(string Id, string Title, string? Background = null, int Width = 1280, int Height = 720);
public sealed record SetStartScreenRequest(string ScreenId);
