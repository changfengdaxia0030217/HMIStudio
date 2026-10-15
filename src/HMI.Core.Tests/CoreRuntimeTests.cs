using HMI.Core.Interface;
using HMI.Core.Model;
using HMI.Core.Services;
using Xunit;

namespace HMI.Core.Tests;

public sealed class CoreRuntimeTests
{
    [Fact]
    public async Task WriteReturnsExplicitFailureForMissingAndReadOnlyVariables()
    {
        await using var pool = new MemoryVariablePool();
        pool.Register(new[]
        {
            new VariableMeta { VarId = "locked", Name = "Locked", IsReadOnly = true, InitialValue = 1 }
        });

        var missing = await pool.WriteAsync("missing", 2);
        var locked = await pool.WriteAsync("locked", 2);

        Assert.False(missing.Ok);
        Assert.Equal(WriteFailureReason.NotExist, missing.Reason);
        Assert.False(locked.Ok);
        Assert.Equal(WriteFailureReason.ReadOnly, locked.Reason);
        Assert.Equal(1, await pool.ReadAsync("locked"));
    }

    [Fact]
    public async Task MockDriverReadsAndWritesOnlyAfterConnect()
    {
        var driver = new MockPlcDriver("SIM", new Dictionary<string, object?> { ["DB1.DBW0"] = 12 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.ReadBatchAsync(new[] { "DB1.DBW0" }));
        Assert.True(await driver.ConnectAsync());

        var read = await driver.ReadBatchAsync(new[] { "DB1.DBW0" });
        Assert.Equal(12, read["DB1.DBW0"]);
        Assert.True(await driver.WriteAsync("DB1.DBW0", 99));

        read = await driver.ReadBatchAsync(new[] { "DB1.DBW0" });
        Assert.Equal(99, read["DB1.DBW0"]);
        await driver.DisconnectAsync();
        Assert.Equal(LinkStatus.Disconnected, driver.Status);
    }

    [Fact]
    public async Task SubscriberExceptionsDoNotBreakOtherConsumers()
    {
        await using var pool = new MemoryVariablePool();
        pool.Register(new[]
        {
            new VariableMeta { VarId = "temperature", Name = "Temperature", InitialValue = 21, CycleMs = 20 }
        });
        await using var manager = new VariableSubscriptionManager(pool);
        var received = 0;
        using var broken = manager.RegisterPollingConsumer(_ => throw new InvalidOperationException("UI consumer failed"));
        using var healthy = manager.RegisterPollingConsumer(_ => Interlocked.Increment(ref received));

        await manager.SubscribeAsync(new[] { "temperature" });
        await Task.Delay(120);
        Assert.True(Volatile.Read(ref received) > 0);

        await manager.UnsubscribeAsync(new[] { "temperature" });
        await Task.Delay(50); // allow an already-started poll iteration to finish
        var countAfterUnsubscribe = Volatile.Read(ref received);
        await Task.Delay(100);
        Assert.Equal(countAfterUnsubscribe, Volatile.Read(ref received));
    }

    [Fact]
    public async Task PermanentVariableRemainsPolledWithoutScreenSubscription()
    {
        await using var pool = new MemoryVariablePool();
        pool.Register(new[]
        {
            new VariableMeta { VarId = "heartbeat", Name = "Heartbeat", InitialValue = true, CycleMs = 20, IsPermanent = true }
        });
        await using var manager = new VariableSubscriptionManager(pool);
        var received = 0;
        using var consumer = manager.RegisterPollingConsumer(_ => Interlocked.Increment(ref received));

        await Task.Delay(100);

        Assert.True(Volatile.Read(ref received) > 0);
        await manager.UnsubscribeAsync(new[] { "heartbeat" });
        var countAfterUnsubscribe = Volatile.Read(ref received);
        await Task.Delay(80);
        Assert.True(Volatile.Read(ref received) > countAfterUnsubscribe);
    }
}
