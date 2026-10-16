using HMI.Core.Interface;
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
    public async Task ValidateReportsInvalidScreensAndControls()
    {
        var directory = CreateTempDirectory();

        try
        {
            var path = Path.Combine(directory, "invalid-layout.hmijob");
            var project = CreateValidProject();
            project.Screens[0].Width = 0;
            project.Screens[0].Controls.Add(new ConfigControlItem { Id = "duplicate", Width = 10, Height = 10 });
            project.Screens[0].Controls.Add(new ConfigControlItem { Id = "duplicate", Width = 0, Height = 10 });
            project.Screens.Add(new HmiScreen { Id = "main", Title = "Duplicate screen" });

            await File.WriteAllTextAsync(path, HmiJson.Serialize(project));

            var errors = await new ProjectStorage().ValidateAsync(path);

            Assert.Contains(errors, e => e.Contains("画面 Id 重复"));
            Assert.Contains(errors, e => e.Contains("宽高必须在 1 到 8192 之间"));
            Assert.Contains(errors, e => e.Contains("控件 Id 重复"));
            Assert.Contains(errors, e => e.Contains("控件 duplicate 坐标或尺寸无效"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateReportsInvalidVariableMetadata()
    {
        var directory = CreateTempDirectory();

        try
        {
            var path = Path.Combine(directory, "invalid-variables.hmijob");
            var project = CreateValidProject();
            project.Variables.Add(new VariableMeta
            {
                VarId = "",
                Name = "",
                DataType = "mystery",
                CycleMs = 10,
                DebounceMs = -1
            });

            await File.WriteAllTextAsync(path, HmiJson.Serialize(project));

            var errors = await new ProjectStorage().ValidateAsync(path);

            Assert.Contains(errors, e => e.Contains("变量 Id 不能为空"));
            Assert.Contains(errors, e => e.Contains("名称不能为空"));
            Assert.Contains(errors, e => e.Contains("数据类型无效"));
            Assert.Contains(errors, e => e.Contains("防抖时间不能小于 0"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateReportsBindingsToUnknownVariables()
    {
        var directory = CreateTempDirectory();

        try
        {
            var path = Path.Combine(directory, "invalid-binding.hmijob");
            var project = CreateValidProject();
            project.Screens[0].Controls.Add(new ConfigControlItem
            {
                Id = "bound-text",
                Properties =
                {
                    ["text"] = new PropertyBindingInfo
                    {
                        Source = new VariablePropertySource { VarId = "missing.variable" }
                    }
                }
            });

            await File.WriteAllTextAsync(path, HmiJson.Serialize(project));

            var errors = await new ProjectStorage().ValidateAsync(path);

            Assert.Contains(errors, e => e.Contains("引用了不存在的变量: missing.variable"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateReportsInvalidEnabledAlarmRules()
    {
        var directory = CreateTempDirectory();

        try
        {
            var path = Path.Combine(directory, "invalid-alarms.hmijob");
            var project = CreateValidProject();
            project.AlarmRules.Add(new AlarmRule
            {
                Id = "invalid-rule",
                Name = "",
                VarId = "missing.variable",
                Operator = "LIKE",
                Value = new { nested = true },
                Enabled = true
            });

            await File.WriteAllTextAsync(path, HmiJson.Serialize(project));

            var errors = await new ProjectStorage().ValidateAsync(path);

            Assert.Contains(errors, e => e.Contains("报警规则 invalid-rule 的名称不能为空"));
            Assert.Contains(errors, e => e.Contains("引用了不存在的变量: missing.variable"));
            Assert.Contains(errors, e => e.Contains("比较运算符无效"));
            Assert.Contains(errors, e => e.Contains("比较值必须是字符串、布尔值或有限数值"));
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
