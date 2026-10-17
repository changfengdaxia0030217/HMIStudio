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
                var restoredHistory = await restarted.GetHistoryAsync(10);
                Assert.Equal(new[] { "Acknowledged", "Triggered" },
                    restoredHistory.Select(item => item.EventType));
                var acknowledgement = Assert.Single(restoredHistory, item => item.EventType == "Acknowledged");
                Assert.Equal(alarmId, acknowledgement.AlarmId);
                Assert.Equal(acknowledgement.OccurredAt, restored.AcknowledgedAt);

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
    public async Task StartupReconcilesPersistedAlarmAgainstCurrentGoodValue()
    {
        var directory = CreateTempDirectory();
        var databasePath = Path.Combine(directory, "alarms.sqlite");
        try
        {
            await using var pool = CreatePool();
            await using (var first = await AlarmManager.CreateAsync(pool, CreateRules(), databasePath))
            {
                Assert.True((await pool.WriteAsync("temperature", 95.0)).Ok);
                await WaitForHistoryCountAsync(first, 1);
                Assert.Single(await first.GetActiveAsync());
            }

            // The process was stopped while the alarm was active, but the current
            // variable value is already back below the configured threshold.
            Assert.True((await pool.WriteAsync("temperature", 70.0)).Ok);

            await using var restarted = await AlarmManager.CreateAsync(pool, CreateRules(), databasePath);
            await WaitForHistoryCountAsync(restarted, 2);

            Assert.Empty(await restarted.GetActiveAsync());
            var history = await restarted.GetHistoryAsync(10);
            Assert.Equal(new[] { "Recovered", "Triggered" }, history.Select(item => item.EventType));
            Assert.Equal(history[0].AlarmId, history[1].AlarmId);
            Assert.Equal("70", history[0].Value);
            Assert.Equal("Good", history[0].Quality);
            Assert.Equal(2, await restarted.GetHistoryCountAsync());
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
    public async Task StartupEvaluationWaitsForGoodQuality()
    {
        var directory = CreateTempDirectory();
        try
        {
            await using var pool = CreatePool();
            Assert.True((await pool.WriteAsync("temperature", 95.0)).Ok);
            pool.SetQuality("temperature", VariableQuality.BadComm);

            await using var alarms = await AlarmManager.CreateAsync(
                pool, CreateRules(), Path.Combine(directory, "alarms.sqlite"));
            await Task.Delay(100);

            Assert.Empty(await alarms.GetActiveAsync());
            Assert.Equal(0, await alarms.GetHistoryCountAsync());

            pool.SetQuality("temperature", VariableQuality.Good);
            await WaitForHistoryCountAsync(alarms, 1);

            var active = Assert.Single(await alarms.GetActiveAsync());
            Assert.Equal("high-temperature", active.RuleId);
            Assert.Equal("Triggered", Assert.Single(await alarms.GetHistoryAsync()).EventType);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DisposedManagerUnsubscribesFromVariableChanges()
    {
        var directory = CreateTempDirectory();
        try
        {
            await using var pool = CreatePool();
            var alarms = await AlarmManager.CreateAsync(
                pool, CreateRules(), Path.Combine(directory, "alarms.sqlite"));

            Assert.True((await pool.WriteAsync("temperature", 95.0)).Ok);
            await WaitForHistoryCountAsync(alarms, 1);

            await alarms.DisposeAsync();

            // A disposed manager rejects queries; reopen the same database to verify
            // that the post-disposal variable write did not create a recovery event.
            await Assert.ThrowsAsync<ObjectDisposedException>(() => alarms.GetHistoryAsync(10));
            Assert.True((await pool.WriteAsync("temperature", 95.0)).Ok);

            await using var reopened = await AlarmManager.CreateAsync(
                pool, CreateRules(), Path.Combine(directory, "alarms.sqlite"));
            var history = await reopened.GetHistoryAsync(10);
            Assert.Single(history);
            Assert.Equal("Triggered", history[0].EventType);
            Assert.Equal(1, await reopened.GetHistoryCountAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentDisposeCallsAreSafeAndWaitForShutdown()
    {
        var directory = CreateTempDirectory();
        try
        {
            await using var pool = CreatePool();
            var alarms = await AlarmManager.CreateAsync(
                pool, CreateRules(), Path.Combine(directory, "alarms.sqlite"));

            Assert.True((await pool.WriteAsync("temperature", 95.0)).Ok);
            await WaitForHistoryCountAsync(alarms, 1);

            var disposeCalls = Enumerable.Range(0, 8)
                .Select(_ => alarms.DisposeAsync().AsTask())
                .ToArray();
            await Task.WhenAll(disposeCalls);

            Assert.True((await pool.WriteAsync("temperature", 95.0)).Ok);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => alarms.GetHistoryAsync(10));

            await using var reopened = await AlarmManager.CreateAsync(
                pool, CreateRules(), Path.Combine(directory, "alarms.sqlite"));
            var history = await reopened.GetHistoryAsync(10);
            Assert.Single(history);
            Assert.Equal("Triggered", history[0].EventType);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DisposedManagerRejectsFurtherOperations()
    {
        var directory = CreateTempDirectory();
        try
        {
            await using var pool = CreatePool();
            var alarms = await AlarmManager.CreateAsync(
                pool, CreateRules(), Path.Combine(directory, "alarms.sqlite"));
            await alarms.DisposeAsync();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => alarms.GetActiveAsync());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => alarms.GetHistoryAsync());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => alarms.GetHistoryCountAsync());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => alarms.AcknowledgeAsync("missing-alarm"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MultipleRulesForOneVariableMaintainIndependentLifecycles()
    {
        var directory = CreateTempDirectory();
        try
        {
            await using var pool = CreatePool();
            var rules = new[]
            {
                new AlarmRule
                {
                    Id = "temperature-warning",
                    Name = "Temperature warning",
                    VarId = "temperature",
                    Operator = ">",
                    Value = 80.0,
                    Enabled = true
                },
                new AlarmRule
                {
                    Id = "temperature-critical",
                    Name = "Temperature critical",
                    VarId = "temperature",
                    Operator = ">",
                    Value = 90.0,
                    Enabled = true
                }
            };
            await using var alarms = await AlarmManager.CreateAsync(
                pool, rules, Path.Combine(directory, "alarms.sqlite"));

            Assert.True((await pool.WriteAsync("temperature", 95.0)).Ok);
            await WaitForHistoryCountAsync(alarms, 2);

            var activeAtPeak = await alarms.GetActiveAsync();
            Assert.Equal(2, activeAtPeak.Count);
            Assert.Equal(
                new[] { "temperature-critical", "temperature-warning" },
                activeAtPeak.Select(alarm => alarm.RuleId).OrderBy(id => id, StringComparer.Ordinal).ToArray());

            Assert.True((await pool.WriteAsync("temperature", 85.0)).Ok);
            await WaitForHistoryCountAsync(alarms, 3);

            var remaining = Assert.Single(await alarms.GetActiveAsync());
            Assert.Equal("temperature-warning", remaining.RuleId);
            var history = await alarms.GetHistoryAsync(10);
            Assert.Equal(3, history.Count);
            Assert.Equal(1, history.Count(item => item.EventType == "Recovered"));
            Assert.Equal(2, history.Count(item => item.EventType == "Triggered"));
            Assert.Single(history, item =>
                item.RuleId == "temperature-critical" && item.EventType == "Recovered");
            Assert.Single(history, item =>
                item.RuleId == "temperature-warning" && item.EventType == "Triggered");
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
    public async Task EnabledRuleWithNullOperatorIsRejected()
    {
        var rule = CreateRules()[0];
        rule.Operator = null!;
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

    [Fact]
    public async Task RepeatedAcknowledgementIsIdempotentAndDoesNotDuplicateHistory()
    {
        var directory = CreateTempDirectory();
        try
        {
            await using var pool = CreatePool();
            await using var alarms = await AlarmManager.CreateAsync(
                pool, CreateRules(), Path.Combine(directory, "alarms.sqlite"));

            Assert.True((await pool.WriteAsync("temperature", 95.0)).Ok);
            await WaitForHistoryCountAsync(alarms, 1);
            var active = Assert.Single(await alarms.GetActiveAsync());

            await Task.WhenAll(
                alarms.AcknowledgeAsync(active.AlarmId),
                alarms.AcknowledgeAsync(active.AlarmId));

            var history = await alarms.GetHistoryAsync(10);
            Assert.Equal(2, history.Count);
            Assert.Equal(new[] { "Acknowledged", "Triggered" }, history.Select(item => item.EventType));
            Assert.True(Assert.Single(await alarms.GetActiveAsync()).Acknowledged);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcknowledgingRecoveredAlarmFailsWithoutAddingHistory()
    {
        var directory = CreateTempDirectory();
        try
        {
            await using var pool = CreatePool();
            await using var alarms = await AlarmManager.CreateAsync(
                pool, CreateRules(), Path.Combine(directory, "alarms.sqlite"));

            Assert.True((await pool.WriteAsync("temperature", 95.0)).Ok);
            await WaitForHistoryCountAsync(alarms, 1);
            var alarmId = Assert.Single(await alarms.GetActiveAsync()).AlarmId;

            Assert.True((await pool.WriteAsync("temperature", 70.0)).Ok);
            await WaitForHistoryCountAsync(alarms, 2);
            Assert.Empty(await alarms.GetActiveAsync());

            await Assert.ThrowsAsync<KeyNotFoundException>(() => alarms.AcknowledgeAsync(alarmId));
            var history = await alarms.GetHistoryAsync(10);
            Assert.Equal(2, history.Count);
            Assert.Equal(new[] { "Recovered", "Triggered" }, history.Select(item => item.EventType));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledAcknowledgementDoesNotChangeAlarmStateOrHistory()
    {
        var directory = CreateTempDirectory();
        try
        {
            await using var pool = CreatePool();
            await using var alarms = await AlarmManager.CreateAsync(
                pool, CreateRules(), Path.Combine(directory, "alarms.sqlite"));

            Assert.True((await pool.WriteAsync("temperature", 95.0)).Ok);
            await WaitForHistoryCountAsync(alarms, 1);
            var active = Assert.Single(await alarms.GetActiveAsync());

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                alarms.AcknowledgeAsync(active.AlarmId, cancellation.Token));

            var stillActive = Assert.Single(await alarms.GetActiveAsync());
            Assert.False(stillActive.Acknowledged);
            Assert.Null(stillActive.AcknowledgedAt);
            var history = await alarms.GetHistoryAsync(10);
            Assert.Single(history);
            Assert.Equal("Triggered", history[0].EventType);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RemovedRuleClosesPersistedActiveAlarmOnRestart()
    {
        var directory = CreateTempDirectory();
        var databasePath = Path.Combine(directory, "alarms.sqlite");
        try
        {
            await using var pool = CreatePool();
            await using (var first = await AlarmManager.CreateAsync(pool, CreateRules(), databasePath))
            {
                Assert.True((await pool.WriteAsync("temperature", 95.0)).Ok);
                await WaitForHistoryCountAsync(first, 1);
                Assert.Single(await first.GetActiveAsync());
            }

            await using (var restarted = await AlarmManager.CreateAsync(pool, [], databasePath))
            {
                Assert.Empty(await restarted.GetActiveAsync());
                var history = await restarted.GetHistoryAsync(10);
                Assert.Equal(new[] { "Recovered", "Triggered" }, history.Select(item => item.EventType));
                Assert.Equal(2, await restarted.GetHistoryCountAsync());
                Assert.Equal(history[0].AlarmId, history[1].AlarmId);
            }
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
    public async Task HistoryFiltersAndCountsRemainConsistent()
    {
        var directory = CreateTempDirectory();
        try
        {
            await using var pool = CreatePool();
            await using var alarms = await AlarmManager.CreateAsync(
                pool, CreateRules(), Path.Combine(directory, "alarms.sqlite"));

            Assert.True((await pool.WriteAsync("temperature", 95.0)).Ok);
            await WaitForHistoryCountAsync(alarms, 1);
            var alarmId = Assert.Single(await alarms.GetActiveAsync()).AlarmId;
            await alarms.AcknowledgeAsync(alarmId);

            Assert.True((await pool.WriteAsync("temperature", 70.0)).Ok);
            await WaitForHistoryCountAsync(alarms, 3);

            var allHistory = await alarms.GetHistoryAsync(10);
            Assert.Equal(3, allHistory.Count);
            var fromDate = allHistory.Min(item => item.OccurredAt).ToString("yyyy-MM-dd");
            var toDate = allHistory.Max(item => item.OccurredAt).ToString("yyyy-MM-dd");

            var triggered = await alarms.GetHistoryAsync(10, eventType: "Triggered");
            Assert.Single(triggered);
            Assert.Equal(1, await alarms.GetHistoryCountAsync(eventType: "Triggered"));

            var matchingMessage = await alarms.GetHistoryAsync(10, search: "temperature exceeds limit");
            Assert.Equal(3, matchingMessage.Count);
            Assert.Equal(matchingMessage.Count,
                await alarms.GetHistoryCountAsync(search: "temperature exceeds limit"));

            var sameDay = await alarms.GetHistoryAsync(10, fromDate: fromDate, toDate: toDate);
            Assert.Equal(3, sameDay.Count);
            Assert.Equal(sameDay.Count,
                await alarms.GetHistoryCountAsync(fromDate: fromDate, toDate: toDate));

            Assert.Empty(await alarms.GetHistoryAsync(10, eventType: "Missing"));
            Assert.Equal(0, await alarms.GetHistoryCountAsync(eventType: "Missing"));
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

    [Fact]
    public async Task HistoryPaginationReturnsStableNonOverlappingPages()
    {
        var directory = CreateTempDirectory();
        try
        {
            await using var pool = CreatePool();
            await using var alarms = await AlarmManager.CreateAsync(
                pool, CreateRules(), Path.Combine(directory, "alarms.sqlite"));

            Assert.True((await pool.WriteAsync("temperature", 95.0)).Ok);
            await WaitForHistoryCountAsync(alarms, 1);
            var alarmId = Assert.Single(await alarms.GetActiveAsync()).AlarmId;
            await alarms.AcknowledgeAsync(alarmId);
            Assert.True((await pool.WriteAsync("temperature", 70.0)).Ok);
            await WaitForHistoryCountAsync(alarms, 3);

            var firstPage = await alarms.GetHistoryAsync(limit: 2, offset: 0);
            var secondPage = await alarms.GetHistoryAsync(limit: 2, offset: 2);
            var beyondEnd = await alarms.GetHistoryAsync(limit: 2, offset: 3);

            Assert.Equal(2, firstPage.Count);
            Assert.Single(secondPage);
            Assert.Empty(beyondEnd);
            Assert.Equal(new[] { "Recovered", "Acknowledged", "Triggered" },
                firstPage.Concat(secondPage).Select(item => item.EventType));
            Assert.Equal(3, firstPage.Concat(secondPage).Select(item => item.Sequence).Distinct().Count());
            Assert.Equal(3, await alarms.GetHistoryCountAsync());
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
