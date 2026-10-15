using HMI.Core.Model;
using HMI.Core.Services;
using Xunit;

namespace HMI.Core.Tests;

public sealed class ProjectStorageTests
{
    [Fact]
    public async Task SaveAndLoadRoundTripPreservesProject()
    {
        var directory = CreateTempDirectory();

        try
        {
            var path = Path.Combine(directory, "roundtrip.hmijob");
            var project = CreateValidProject();
            project.Screens[0].Title = "主画面";

            var storage = new ProjectStorage();
            await storage.SaveAsync(path, project);

            var loaded = Assert.IsType<HmiProject>(
                await storage.LoadAsync(path));

            Assert.Equal(project.FormatVersion, loaded.FormatVersion);
            Assert.Equal(project.StartScreenId, loaded.StartScreenId);
            Assert.Single(loaded.Screens);
            Assert.Equal("主画面", loaded.Screens[0].Title);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateReportsInvalidProjectContents()
    {
        var directory = CreateTempDirectory();

        try
        {
            var path = Path.Combine(directory, "invalid.hmijob");
            var project = CreateValidProject();
            project.StartScreenId = "missing-screen";
            project.Variables.Add(new VariableMeta
            {
                VarId = "duplicate",
                Name = "变量 A",
                Min = 10,
                Max = 0,
                CycleMs = 0
            });
            project.Variables.Add(new VariableMeta
            {
                VarId = "duplicate",
                Name = "变量 B",
                CycleMs = 20
            });

            await File.WriteAllTextAsync(path, HmiJson.Serialize(project));

            var errors = await new ProjectStorage().ValidateAsync(path);

            Assert.Contains(errors, e => e.Contains("启动画面缺失"));
            Assert.Contains(errors, e => e.Contains("变量 Id 重复"));
            Assert.Contains(errors, e => e.Contains("最小值不能大于最大值"));
            Assert.Contains(errors, e => e.Contains("刷新周期必须大于 0"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ValidatePropagatesCancellation()
    {
        var directory = CreateTempDirectory();

        try
        {
            var path = Path.Combine(directory, "valid.hmijob");
            await File.WriteAllTextAsync(path, HmiJson.Serialize(CreateValidProject()));

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await new ProjectStorage().ValidateAsync(path, cts.Token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledSavePreservesExistingFileAndCleansTemporaryFile()
    {
        var directory = CreateTempDirectory();

        try
        {
            var path = Path.Combine(directory, "existing.hmijob");
            const string originalContent = "existing project content";
            await File.WriteAllTextAsync(path, originalContent);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await new ProjectStorage().SaveAsync(
                    path, CreateValidProject(), cts.Token));

            Assert.Equal(originalContent, await File.ReadAllTextAsync(path));
            Assert.Equal(
                new[] { path },
                Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static HmiProject CreateValidProject()
    {
        return new HmiProject
        {
            StartScreenId = "main",
            Screens =
            {
                new HmiScreen
                {
                    Id = "main",
                    Title = "Main"
                }
            }
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "HMIStudio.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);
        return path;
    }
}
