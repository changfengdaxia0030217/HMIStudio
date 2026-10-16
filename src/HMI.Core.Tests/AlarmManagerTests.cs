using HMI.Core.Interface;
using HMI.Core.Model;
using HMI.Core.Services;
using Xunit;

namespace HMI.Core.Tests;

public sealed class AlarmManagerTests
{
    [Fact]
    public async Task TriggerRecoverAndRepeatedUpdatesProduceSingleLifecycle()
    {
        var directory = CreateTempDirectory();
        try
        {
            await using var pool = CreatePool();
            await using var alarms = await AlarmManager.CreateAsync(pool, CreateRules(), Path.Combine(directory, "alarms.sqlite"));

            Assert.True((await pool.WriteAsync("temperature", 95.0)).Ok);
            await WaitForHistoryCountAsync(alarms, 1);

            Assert.True((await pool.WriteAsync("temperature", 96.0)).Ok);
            Assert.True((await pool.WriteAsync("temperature", 97.0)).Ok);
            await Task.Delay(100);

            var active = Assert.Single(await alarms.GetActiveAsync());
            Assert.Equal("high-temperature", active.RuleId);
            Assert.False(active.Acknowledged);
            Assert.Single(await alarms.GetHistoryAsync());

            await alarms.AcknowledgeAsync(active.AlarmId);
            var acknowledged = Assert.Single(await alarms.GetActiveAsync());
            Assert.True(acknowledged.Acknowledged);
            Assert.NotNull(acknowledged.AcknowledgedAt);

            Assert.True((await pool.WriteAsync("temperature", 70.0)).Ok);
            await WaitForHistoryCountAsync(alarms, 3);

            Assert.Empty(await alarms.GetActiveAsync());
            var history = await alarms.GetHistoryAsync(10);
            Assert.Equal(new[] { "Recovered", "Acknowledged", "Triggered" }, history.Select(item => item.EventType));
            Assert.All(history, item => Assert.True(item.Sequence > 0));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ActiveAlarmAcknowledgementAndHistorySurviveManagerRestart()
    {
        var directory = CreateTempDirectory();
        var databasePath = Path.Combine(directory, "alarms.sqlite");
        try
        {
            await using var pool = CreatePool();
            Assert.True((await pool.WriteAsync("temperature", 95.0)).Ok);

            string alarmId;
            await using (var first = await AlarmManager.CreateAsync(pool, CreateRules(), databasePath))
            {
                await WaitForHistoryCountAsync(first, 1);
                var active = Assert.Single(await first.GetActiveAsync());
                alarmId = active.AlarmId;
                await first.AcknowledgeAsync(alarmId);
                Assert.True(Assert.Single(await first.GetActiveAsync()).Acknowledged);
            }

            await using (var restarted = await AlarmManager.CreateAsync(pool, CreateRules(), databasePath))
            {
                var restored = Assert.Single(await restarted.GetActiveAsync());
                Assert.Equal(alarmId, restored.AlarmId);
                Assert.True(restored.Acknowledged);
                Assert.NotNull(restored.AcknowledgedAt);
                Assert.Equal(new[] { "Acknowledged", "Triggered" },
                    (await restarted.GetHistoryAsync(10)).Select(item => item.EventType));

                Assert.True((await pool.WriteAsync("temperature", 70.0)).Ok);
                await WaitForHistoryCountAsync(restarted, 3);
                Assert.Empty(await restarted.GetActiveAsync());
                Assert.Equal("Recovered", (await restarted.GetHistoryAsync(1)).Single().EventType);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BadQualityDoesNotClearActiveAlarmUntilGoodQualityReturns()
    {
        var directory = CreateTempDirectory();
        try
        {
            await using var pool = CreatePool();
            await using var alarms = await AlarmManager.CreateAsync(pool, CreateRules(), Path.Combine(directory, "alarms.sqlite"));

            Assert.True((await pool.WriteAsync("temperature", 95.0)).Ok);
            await WaitForHistoryCountAsync(alarms, 1);
            pool.SetQuality("temperature", VariableQuality.BadComm);
            Assert.True((await pool.WriteAsync("temperature", 70.0)).Ok);
            await Task.Delay(100);

            Assert.Single(await alarms.GetActiveAsync());
            Assert.Single(await alarms.GetHistoryAsync());

            pool.SetQuality("temperature", VariableQuality.Good);
            await WaitForHistoryCountAsync(alarms, 2);
            Assert.Empty(await alarms.GetActiveAsync());
            Assert.Equal(new[] { "Recovered", "Triggered" },
                (await alarms.GetHistoryAsync(10)).Select(item => item.EventType));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EnabledRuleWithUnknownVariableIsRejected()
    {
        var rule = CreateRules()[0];
        rule.VarId = "missing-variable";
        await AssertRulesRejectedAsync(rule);
    }

    [Fact]
    public async Task EnabledRuleWithUnsupportedOperatorIsRejected()
    {
        var rule = CreateRules()[0];
        rule.Operator = "contains";
        await AssertRulesRejectedAsync(rule);
    }

    [Fact]
    public async Task DuplicateEnabledRuleIdsAreRejected()
    {
        var first = CreateRules()[0];
        var second = CreateRules()[0];
        second.Name = "Duplicate";
        await AssertRulesRejectedAsync(first, second);
    }

    [Fact]
    public async Task NonFiniteNumericThresholdIsRejected()
    {
        var rule = CreateRules()[0];
        rule.Value = double.NaN;
        await AssertRulesRejectedAsync(rule);
    }

    [Fact]
    public async Task NumericVariableRejectsNonNumericThreshold()
    {
        var rule = CreateRules()[0];
        rule.Value = "hot";
        await AssertRulesRejectedAsync(rule);
    }

    [Fact]
    public async Task BooleanVariableRejectsRelationalOperator()
    {
        var rule = CreateRules()[0];
        rule.VarId = "running";
        rule.Operator = ">";
        rule.Value = true;
        await AssertRulesRejectedAsync(rule);
    }

    [Fact]
    public async Task BooleanVariableRejectsNonBooleanThreshold()
    {
        var rule = CreateRules()[0];
        rule.VarId = "running";
        rule.Operator = "==";
        rule.Value = 1;
        await AssertRulesRejectedAsync(rule);
    }

    [Fact]
    public async Task StringVariableRejectsNumericThreshold()
    {
        var rule = CreateRules()[0];
        rule.VarId = "machine-state";
        rule.Operator = "==";
        rule.Value = 1;
        await AssertRulesRejectedAsync(rule);
    }

    [Fact]
    public async Task DisabledInvalidRuleIsIgnored()
    {
        var directory = CreateTempDirectory();
        try
        {
            await using var pool = CreatePool();
            var disabled = new AlarmRule
            {
                Id = "",
                Name = "",
                VarId = "missing-variable",
                Operator = "invalid",
                Value = null,
                Enabled = false
            };
            await using var alarms = await AlarmManager.CreateAsync(pool, [disabled], Path.Combine(directory, "alarms.sqlite"));
            Assert.Empty(await alarms.GetActiveAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task AssertRulesRejectedAsync(params AlarmRule[] rules)
    {
        var directory = CreateTempDirectory();
        try
        {
            await using var pool = CreatePool();
            await Assert.ThrowsAsync<ArgumentException>(() =>
                AlarmManager.CreateAsync(pool, rules, Path.Combine(directory, "alarms.sqlite")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcknowledgingUnknownAlarmFailsExplicitly()
    {
        var directory = CreateTempDirectory();
        try
        {
            await using var pool = CreatePool();
            await using var alarms = await AlarmManager.CreateAsync(pool, CreateRules(), Path.Combine(directory, "alarms.sqlite"));
            await Assert.ThrowsAsync<KeyNotFoundException>(() => alarms.AcknowledgeAsync("missing-alarm"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static MemoryVariablePool CreatePool()
    {
        var pool = new MemoryVariablePool();
        pool.Register(new[]
        {
            new VariableMeta
            {
                VarId = "temperature",
                Name = "Temperature",
                DataType = "float",
                Kind = VariableKind.Memory,
                InitialValue = 70.0,
                Min = 0,
                Max = 150,
                CycleMs = 50
            },
            new VariableMeta
            {
                VarId = "running",
                Name = "Running",
                DataType = "bool",
                Kind = VariableKind.Memory,
                InitialValue = false,
                CycleMs = 50
            },
            new VariableMeta
            {
                VarId = "machine-state",
                Name = "Machine state",
                DataType = "string",
                Kind = VariableKind.Memory,
                InitialValue = "Idle",
                CycleMs = 50
            }
        });
        return pool;
    }

    private static AlarmRule[] CreateRules() =>
    [
        new AlarmRule
        {
            Id = "high-temperature",
            Name = "High temperature",
            VarId = "temperature",
            Operator = ">",
            Value = 90.0,
            Enabled = true,
            Message = "Temperature exceeds limit"
        }
    ];

    private static async Task WaitForHistoryCountAsync(AlarmManager alarms, int expectedCount)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((await alarms.GetHistoryAsync(100)).Count >= expectedCount)
                return;
            await Task.Delay(20);
        }

        Assert.True((await alarms.GetHistoryAsync(100)).Count >= expectedCount,
            $"Expected at least {expectedCount} alarm history records.");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "HMIStudio.AlarmTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
