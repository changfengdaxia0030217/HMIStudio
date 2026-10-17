using HMI.Core.Interface;
using HMI.Core.Model;
using HMI.Core.Services;
using Xunit;

namespace HMI.Core.Tests;

public sealed class AlarmManagerConcurrencyTests
{
    [Fact]
    public async Task ConcurrentThresholdWritesCreateOnlyOneActiveAlarm()
    {
        var directory = CreateTempDirectory();
        try
        {
            await using var pool = CreatePool();
            await using var alarms = await AlarmManager.CreateAsync(
                pool, CreateRules(), Path.Combine(directory, "alarms.sqlite"));

            var writes = Enumerable.Range(0, 32)
                .Select(index => pool.WriteAsync("temperature", 95.0 + index));
            var results = await Task.WhenAll(writes);
            Assert.All(results, result => Assert.True(result.Ok));

            await WaitForHistoryCountAsync(alarms, 1);

            Assert.Single(await alarms.GetActiveAsync());
            var history = await alarms.GetHistoryAsync(100);
            Assert.Single(history);
            Assert.Equal("Triggered", history[0].EventType);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentAcknowledgementsPersistExactlyOneAcknowledgement()
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

            await Task.WhenAll(Enumerable.Range(0, 32)
                .Select(_ => alarms.AcknowledgeAsync(active.AlarmId)));

            var restored = Assert.Single(await alarms.GetActiveAsync());
            Assert.True(restored.Acknowledged);
            Assert.NotNull(restored.AcknowledgedAt);

            var history = await alarms.GetHistoryAsync(100);
            Assert.Equal(2, history.Count);
            Assert.Equal(1, history.Count(item => item.EventType == "Triggered"));
            Assert.Equal(1, history.Count(item => item.EventType == "Acknowledged"));
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
        var path = Path.Combine(Path.GetTempPath(), "HMIStudio.AlarmConcurrencyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}