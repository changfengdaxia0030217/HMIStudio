using System.IO.Compression;
using System.Text;
using HMI.Server;
using Xunit;

namespace HMI.Core.Tests;

public sealed class BackupRestoreServiceTests
{
    [Fact]
    public async Task StageRejectsEmptyBackupWithoutCreatingStagingFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            var result = await BackupRestoreService.StageAsync(
                new MemoryStream(), 0, root, CancellationToken.None);

            Assert.False(result.Ok);
            Assert.False(Directory.Exists(Path.Combine(root, "data")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StageRejectsMalformedArchiveAndCleansTemporaryDirectory()
    {
        var root = CreateTempDirectory();
        try
        {
            var bytes = Encoding.UTF8.GetBytes("this is not a zip archive");
            await using var upload = new MemoryStream(bytes);

            var result = await BackupRestoreService.StageAsync(
                upload, bytes.Length, root, CancellationToken.None);

            Assert.False(result.Ok);
            var dataDirectory = Path.Combine(root, "data");
            Assert.True(Directory.Exists(dataDirectory));
            Assert.Empty(Directory.GetFileSystemEntries(dataDirectory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StageRejectsArchiveWithUnexpectedEntries()
    {
        var root = CreateTempDirectory();
        try
        {
            await using var upload = new MemoryStream();
            using (var archive = new ZipArchive(upload, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry("unexpected.txt");
                await using var writer = new StreamWriter(entry.Open());
                await writer.WriteAsync("unexpected content");
            }
            upload.Position = 0;

            var result = await BackupRestoreService.StageAsync(
                upload, upload.Length, root, CancellationToken.None);

            Assert.False(result.Ok);
            Assert.Contains("不支持的条目", result.Message);
            Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(root, "data")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StageCancellationCleansTemporaryDirectoryAndPropagatesCancellation()
    {
        var root = CreateTempDirectory();
        try
        {
            await using var upload = new MemoryStream();
            using (var archive = new ZipArchive(upload, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifest = archive.CreateEntry("manifest.json");
                await using (var writer = new StreamWriter(manifest.Open()))
                    await writer.WriteAsync("{}");

                var project = archive.CreateEntry("project/test.hmijob");
                await using (var writer = new StreamWriter(project.Open()))
                    await writer.WriteAsync("{}");
            }

            upload.Position = 0;
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                BackupRestoreService.StageAsync(upload, upload.Length, root, cts.Token));

            var dataDirectory = Path.Combine(root, "data");
            Assert.True(Directory.Exists(dataDirectory));
            Assert.Empty(Directory.GetFileSystemEntries(dataDirectory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyPendingPreservesRollbackJournalWhenOriginalProjectBackupIsMissing()
    {
        var root = CreateTempDirectory();
        try
        {
            var data = Path.Combine(root, "data");
            var pending = Path.Combine(data, "restore-pending");
            var rollback = Path.Combine(data, "restore-rollback");
            Directory.CreateDirectory(pending);
            Directory.CreateDirectory(rollback);
            await File.WriteAllTextAsync(
                Path.Combine(rollback, "state.json"),
                """{"projectExisted":true,"alarmDatabaseWasTouched":false,"dbExisted":false}""");

            var projectPath = Path.Combine(root, "project.hmijob");
            await File.WriteAllTextAsync(projectPath, "original-live-project");

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                BackupRestoreService.ApplyPendingAsync(
                    root, projectPath, Path.Combine(data, "alarms.sqlite"), CancellationToken.None));

            Assert.True(Directory.Exists(rollback));
            Assert.True(File.Exists(Path.Combine(rollback, "state.json")));
            Assert.Equal("original-live-project", await File.ReadAllTextAsync(projectPath));
            Assert.True(Directory.Exists(pending));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyPendingPreservesJournalWhenOriginalAlarmBackupIsMissing()
    {
        var root = CreateTempDirectory();
        try
        {
            var data = Path.Combine(root, "data");
            var pending = Path.Combine(data, "restore-pending");
            var rollback = Path.Combine(data, "restore-rollback");
            Directory.CreateDirectory(pending);
            Directory.CreateDirectory(rollback);
            await File.WriteAllTextAsync(Path.Combine(rollback, "state.json"),
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    projectExisted = true,
                    alarmDatabaseWasTouched = true,
                    dbExisted = true
                }));
            var projectPath = Path.Combine(root, "project.hmijob");
            await File.WriteAllTextAsync(projectPath, "partially-replaced-project");
            await File.WriteAllTextAsync(Path.Combine(rollback, "project.original"), "original-project");
            var alarmPath = Path.Combine(data, "alarms.sqlite");
            await File.WriteAllTextAsync(alarmPath, "partially-replaced-database");

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                BackupRestoreService.ApplyPendingAsync(root, projectPath, alarmPath, CancellationToken.None));

            Assert.Equal("original-project", await File.ReadAllTextAsync(projectPath));
            Assert.Equal("partially-replaced-database", await File.ReadAllTextAsync(alarmPath));
            Assert.True(File.Exists(Path.Combine(rollback, "state.json")));
            Assert.True(Directory.Exists(pending));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "HMIStudio.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
