using HMI.Core.Interface;
using HMI.Core.Model;
using HMI.Core.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HMI.Core.Tests;

public sealed class CoreRuntimeTests
{
    [Fact]
    public void PropertySourcesRoundTripThroughStableJsonDiscriminator()
    {
        IPropertySource source = new VariablePropertySource { VarId = "line.speed", Index = 2 };
        var json = System.Text.Json.JsonSerializer.Serialize<IPropertySource>(source);

        Assert.Contains("\"type\":\"$var\"", json);
        var restored = System.Text.Json.JsonSerializer.Deserialize<IPropertySource>(json);

        var variable = Assert.IsType<VariablePropertySource>(restored);
        Assert.Equal("line.speed", variable.VarId);
        Assert.Equal(2, variable.Index);
    }

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
    public async Task WriteRejectsTypeMismatchAndConfiguredRangeViolation()
    {
        await using var pool = new MemoryVariablePool();
        pool.Register(new[]
        {
            new VariableMeta { VarId = "speed", Name = "Speed", DataType = "float", Min = 0, Max = 100, InitialValue = 10.0 }
        });

        var outOfRange = await pool.WriteAsync("speed", 101.0);
        var invalidType = await pool.WriteAsync("speed", "fast");

        Assert.False(outOfRange.Ok);
        Assert.Equal(WriteFailureReason.ValueOutOfRange, outOfRange.Reason);
        Assert.False(invalidType.Ok);
        Assert.Equal(WriteFailureReason.InvalidValue, invalidType.Reason);
        Assert.Equal(10.0, await pool.ReadAsync("speed"));
    }

    [Fact]
    public async Task FloatVariableRejectsValuesOutsideSinglePrecisionRange()
    {
        await using var pool = new MemoryVariablePool();
        pool.Register(new[]
        {
            new VariableMeta { VarId = "floatValue", Name = "Float value", DataType = "float", InitialValue = 12.5f }
        });

        var tooLarge = await pool.WriteAsync("floatValue", (double)float.MaxValue * 2);

        Assert.False(tooLarge.Ok);
        Assert.Equal(WriteFailureReason.ValueOutOfRange, tooLarge.Reason);
        Assert.Equal(12.5f, await pool.ReadAsync("floatValue"));
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
    public async Task PlcVariablesUseSimulationDriverAndTrackCommunicationQuality()
    {
        var drivers = new MockPlcDriverManager();
        await using var pool = new MemoryVariablePool(drivers);
        pool.Register(new[]
        {
            new VariableMeta
            {
                VarId = "plcSpeed",
                Name = "PLC Speed",
                Kind = VariableKind.Plc,
                InitialValue = 12,
                Source = new VariableSource { Kind = VariableKind.Plc, Channel = "S7", Address = "DB1.DBW0" }
            }
        });

        Assert.Equal(VariableQuality.BadComm, pool.GetQuality("plcSpeed"));
        await pool.RefreshFromDriversAsync(new[] { "plcSpeed" });
        Assert.Equal(VariableQuality.Good, pool.GetQuality("plcSpeed"));
        Assert.Equal(12, await pool.ReadAsync("plcSpeed"));

        var write = await pool.WriteAsync("plcSpeed", 25);
        Assert.True(write.Ok);
        await pool.RefreshFromDriversAsync(new[] { "plcSpeed" });
        Assert.Equal(25, await pool.ReadAsync("plcSpeed"));
        Assert.Equal(VariableQuality.Good, pool.GetQuality("plcSpeed"));
        await drivers.DisposeAsync();
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
    public async Task SharedVariableRemainsPolledUntilLastSubscriptionIsReleased()
    {
        await using var pool = new MemoryVariablePool();
        pool.Register(new[]
        {
            new VariableMeta { VarId = "temperature", Name = "Temperature", InitialValue = 21, CycleMs = 20 }
        });
        await using var manager = new VariableSubscriptionManager(pool);
        var received = 0;
        using var consumer = manager.RegisterPollingConsumer(_ => Interlocked.Increment(ref received));

        await manager.SubscribeAsync(new[] { "temperature" });
        await manager.SubscribeAsync(new[] { "temperature" });
        await Task.Delay(100);
        Assert.True(Volatile.Read(ref received) > 0);

        await manager.UnsubscribeAsync(new[] { "temperature" });
        await Task.Delay(80);
        var countAfterFirstUnsubscribe = Volatile.Read(ref received);
        await Task.Delay(80);
        Assert.True(Volatile.Read(ref received) > countAfterFirstUnsubscribe);

        await manager.UnsubscribeAsync(new[] { "temperature" });
        await Task.Delay(60);
        var countAfterLastUnsubscribe = Volatile.Read(ref received);
        await Task.Delay(100);
        Assert.Equal(countAfterLastUnsubscribe, Volatile.Read(ref received));
    }

    [Fact]
    public async Task DisposeAsyncCanBeCalledConcurrentlyAndRepeatedly()
    {
        await using var pool = new MemoryVariablePool();
        pool.Register(new[]
        {
            new VariableMeta { VarId = "temperature", Name = "Temperature", InitialValue = 21, CycleMs = 20 }
        });
        var manager = new VariableSubscriptionManager(pool);

        await manager.SubscribeAsync(new[] { "temperature" });
        var disposeCalls = Enumerable.Range(0, 8)
            .Select(_ => manager.DisposeAsync().AsTask())
            .ToArray();

        await Task.WhenAll(disposeCalls);
        await manager.DisposeAsync();
    }

    [Fact]
    public async Task VariableCanBeResubscribedImmediatelyAfterLastUnsubscribe()
    {
        await using var pool = new MemoryVariablePool();
        pool.Register(new[]
        {
            new VariableMeta { VarId = "temperature", Name = "Temperature", InitialValue = 21, CycleMs = 20 }
        });
        await using var manager = new VariableSubscriptionManager(pool);
        var received = 0;
        using var consumer = manager.RegisterPollingConsumer(_ => Interlocked.Increment(ref received));

        await manager.SubscribeAsync(new[] { "temperature" });
        await Task.Delay(80);
        Assert.True(Volatile.Read(ref received) > 0);

        await manager.UnsubscribeAsync(new[] { "temperature" });
        var countAfterUnsubscribe = Volatile.Read(ref received);
        await manager.SubscribeAsync(new[] { "temperature" });
        await Task.Delay(100);

        Assert.True(Volatile.Read(ref received) > countAfterUnsubscribe);
        await manager.UnsubscribeAsync(new[] { "temperature" });
    }

    [Fact]
    public async Task InternalChangeConsumersReceiveOnlyChangesAndCanUnregisterSafely()
    {
        await using var pool = new MemoryVariablePool();
        pool.Register(new[]
        {
            new VariableMeta { VarId = "temperature", Name = "Temperature", InitialValue = 21, CycleMs = 20 }
        });

        var firstConsumerEvents = new List<VariableValuePackage>();
        var secondConsumerEvents = new List<VariableValuePackage>();
        using var first = pool.RegisterInternalChangeConsumer(firstConsumerEvents.Add);
        using var broken = pool.RegisterInternalChangeConsumer(_ => throw new InvalidOperationException("internal consumer failed"));
        using var second = pool.RegisterInternalChangeConsumer(secondConsumerEvents.Add);

        Assert.True((await pool.WriteAsync("temperature", 21)).Ok);
        Assert.Empty(firstConsumerEvents);
        Assert.Empty(secondConsumerEvents);

        Assert.True((await pool.WriteAsync("temperature", 22)).Ok);
        Assert.Single(firstConsumerEvents);
        Assert.Single(secondConsumerEvents);
        Assert.Equal(22, secondConsumerEvents[0].Value);

        first.Dispose();
        Assert.True((await pool.WriteAsync("temperature", 23)).Ok);
        Assert.Single(firstConsumerEvents);
        Assert.Equal(new object?[] { 22, 23 }, secondConsumerEvents.Select(item => item.Value));

        pool.SetQuality("temperature", VariableQuality.BadComm);
        Assert.Equal(3, secondConsumerEvents.Count);
        Assert.Equal(VariableQuality.BadComm, secondConsumerEvents[^1].Quality);
    }

    [Fact]
    public async Task PermanentVariableRemainsPolledAfterSubscriptionIsReleased()
    {
        await using var pool = new MemoryVariablePool();
        pool.Register(new[]
        {
            new VariableMeta { VarId = "heartbeat", Name = "Heartbeat", InitialValue = true, CycleMs = 20, IsPermanent = true }
        });
        await using var manager = new VariableSubscriptionManager(pool);
        var received = 0;
        using var consumer = manager.RegisterPollingConsumer(_ => Interlocked.Increment(ref received));

        await manager.SubscribeAsync(new[] { "heartbeat" });
        await Task.Delay(100);
        Assert.True(Volatile.Read(ref received) > 0);

        await manager.UnsubscribeAsync(new[] { "heartbeat" });
        await Task.Delay(80);
        var countAfterUnsubscribe = Volatile.Read(ref received);
        await Task.Delay(80);
        Assert.True(Volatile.Read(ref received) > countAfterUnsubscribe);
    }

    [Fact]
    public async Task RuntimeCanStartRepeatedlyAndReloadPersistedProjectAfterShutdown()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hmistudio-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var environment = new TestWebHostEnvironment(root);
            await using var runtime = new HMI.Server.RuntimeProjectService(
                environment,
                NullLogger<HMI.Server.RuntimeProjectService>.Instance);

            await runtime.StartAsync(CancellationToken.None);
            var projectPath = runtime.ProjectPath;
            Assert.True(File.Exists(projectPath));
            var originalScreenIds = runtime.Project.Screens.Select(screen => screen.Id).ToArray();
            var originalVariableIds = runtime.Project.Variables.Select(variable => variable.VarId).ToArray();

            // Repeated host start notifications must not create duplicate runtime services.
            await runtime.StartAsync(CancellationToken.None);
            Assert.Equal(originalScreenIds, runtime.Project.Screens.Select(screen => screen.Id));
            Assert.Equal(originalVariableIds, runtime.Project.Variables.Select(variable => variable.VarId));

            await runtime.StopAsync(CancellationToken.None);
            await runtime.DisposeAsync();

            // A later start on the same service instance must load the saved project again.
            await runtime.StartAsync(CancellationToken.None);
            Assert.Equal(projectPath, runtime.ProjectPath);
            Assert.Equal(originalScreenIds, runtime.Project.Screens.Select(screen => screen.Id));
            Assert.Equal(originalVariableIds, runtime.Project.Variables.Select(variable => variable.VarId));
            Assert.Empty(await runtime.ValidateProjectAsync());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = typeof(TestWebHostEnvironment).Assembly.FullName!;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(contentRootPath, "wwwroot");
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
