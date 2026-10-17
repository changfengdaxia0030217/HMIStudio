using System.Globalization;
using Microsoft.Data.Sqlite;
using HMI.Core.Model;
using HMI.Core.Services;

namespace HMI.Server;

/// <summary>
/// Persistent, one-second trend sampling for the first-release runtime.
/// Each sample captures value, quality and UTC timestamp in a dedicated SQLite database.
/// </summary>
public sealed class TrendHistoryService : IAsyncDisposable
{
    private readonly MemoryVariablePool _pool;
    private readonly IReadOnlyList<VariableMeta> _variables;
    private readonly string _connectionString;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _samplingTask;
    private int _disposed;
    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;

    public TrendHistoryService(
        MemoryVariablePool pool,
        IReadOnlyList<VariableMeta> variables,
        string databasePath,
        ILogger logger)
    {
        _pool = pool;
        _variables = variables;
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            DefaultTimeout = 5
        }.ToString();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS TrendSamples (
                Sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                VarId TEXT NOT NULL,
                Value TEXT NULL,
                Quality TEXT NOT NULL,
                SampledAtUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_TrendSamples_VarId_SampledAtUtc
                ON TrendSamples(VarId, SampledAtUtc);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        _samplingTask ??= Task.Run(() => RunSamplerAsync(_shutdown.Token), CancellationToken.None);
    }

    public async Task<IReadOnlyList<TrendSample>> QueryAsync(
        string varId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(varId))
            throw new ArgumentException("变量 ID 不能为空", nameof(varId));
        if (!_variables.Any(variable => string.Equals(variable.VarId, varId, StringComparison.Ordinal)))
            throw new KeyNotFoundException($"变量不存在: {varId}");
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            throw new ArgumentException("开始时间不能晚于结束时间");
        limit = Math.Clamp(limit, 1, 10000);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var predicates = new List<string> { "VarId = $varId" };
        command.Parameters.AddWithValue("$varId", varId);
        if (from.HasValue)
        {
            predicates.Add("SampledAtUtc >= $from");
            command.Parameters.AddWithValue("$from", from.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        }
        if (to.HasValue)
        {
            predicates.Add("SampledAtUtc <= $to");
            command.Parameters.AddWithValue("$to", to.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        }
        command.Parameters.AddWithValue("$limit", limit);
        command.CommandText = $"""
            SELECT Sequence, VarId, Value, Quality, SampledAtUtc
            FROM TrendSamples
            WHERE {string.Join(" AND ", predicates)}
            ORDER BY SampledAtUtc DESC, Sequence DESC
            LIMIT $limit;
            """;

        var samples = new List<TrendSample>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var timestampText = reader.GetString(4);
            samples.Add(new TrendSample(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }
        samples.Reverse();
        return samples;
    }

    public async Task<long> GetCountAsync(string? varId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(varId)
            ? "SELECT COUNT(*) FROM TrendSamples;"
            : "SELECT COUNT(*) FROM TrendSamples WHERE VarId = $varId;";
        if (!string.IsNullOrWhiteSpace(varId))
            command.Parameters.AddWithValue("$varId", varId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private async Task RunSamplerAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await SampleOnceAsync(cancellationToken).ConfigureAwait(false);
                if (DateTimeOffset.UtcNow - _lastPrune >= TimeSpan.FromHours(1))
                {
                    await PruneAsync(cancellationToken).ConfigureAwait(false);
                    _lastPrune = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "变量趋势采样失败");
            }
        }
    }

    private async Task SampleOnceAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO TrendSamples (VarId, Value, Quality, SampledAtUtc)
            VALUES ($varId, $value, $quality, $sampledAtUtc);
            """;
        var varId = command.Parameters.Add("$varId", SqliteType.Text);
        var value = command.Parameters.Add("$value", SqliteType.Text);
        var quality = command.Parameters.Add("$quality", SqliteType.Text);
        var sampledAt = command.Parameters.Add("$sampledAtUtc", SqliteType.Text);

        foreach (var variable in _variables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _pool.TryGetValue(variable.VarId, out var currentValue);
            varId.Value = variable.VarId;
            value.Value = currentValue is null ? DBNull.Value : FormatValue(currentValue);
            quality.Value = _pool.GetQuality(variable.VarId).ToString();
            sampledAt.Value = now;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM TrendSamples WHERE SampledAtUtc < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-30).ToString("O", CultureInfo.InvariantCulture));
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        if (deleted > 0) _logger.LogInformation("趋势历史保留策略清理了 {DeletedCount} 条过期记录", deleted);
    }

    private static string FormatValue(object value) => value switch
    {
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shutdown.Cancel();
        if (_samplingTask is not null)
        {
            try { await _samplingTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _shutdown.Dispose();
    }
}

public sealed record TrendSample(
    long Sequence,
    string VarId,
    string? Value,
    string Quality,
    DateTimeOffset SampledAtUtc);
