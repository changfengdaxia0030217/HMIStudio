using HMI.Core.Model;
using HMI.Core.Services;
using HMI.Server;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HMI.Core.Tests;

public sealed class TrendHistoryServiceTests
{
    [Fact]
    public async Task SamplesValuesAndReturnsOrderedTrendHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hmistudio-trends-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "trends.sqlite");
        var variable = new VariableMeta
        {
            VarId = "temperature",
            Name = "Temperature",
            DataType = "int",
            InitialValue = 25
        };

        await using var pool = new MemoryVariablePool();
        pool.Register(new[] { variable });
        await using var trends = new TrendHistoryService(pool, new[] { variable }, databasePath, NullLogger<TrendHistoryService>.Instance);

        try
        {
            await trends.StartAsync();
            IReadOnlyList<TrendSample> samples = Array.Empty<TrendSample>();
            for (var attempt = 0; attempt < 40 && samples.Count == 0; attempt++)
            {
                await Task.Delay(100);
                samples = await trends.QueryAsync("temperature", null, null, 10);
            }

            Assert.NotEmpty(samples);
            Assert.Equal("temperature", samples[0].VarId);
            Assert.Equal("25", samples[0].Value);
            Assert.Equal("Good", samples[0].Quality);
            Assert.True(samples[0].SampledAtUtc <= DateTimeOffset.UtcNow);
            Assert.True(await trends.GetCountAsync("temperature") >= samples.Count);
        }
        finally
        {
            await trends.DisposeAsync();
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task QueryRejectsUnknownVariablesAndInvalidRanges()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hmistudio-trends-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var variable = new VariableMeta { VarId = "pressure", Name = "Pressure", DataType = "int", InitialValue = 10 };
        await using var pool = new MemoryVariablePool();
        pool.Register(new[] { variable });
        await using var trends = new TrendHistoryService(pool, new[] { variable }, Path.Combine(directory, "trends.sqlite"), NullLogger<TrendHistoryService>.Instance);

        try
        {
            await trends.StartAsync();
            await Assert.ThrowsAsync<KeyNotFoundException>(() => trends.QueryAsync("missing", null, null));
            await Assert.ThrowsAsync<ArgumentException>(() => trends.QueryAsync("pressure", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        finally
        {
            await trends.DisposeAsync();
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }
}