using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using HMI.Core.Interface;
using Microsoft.Data.Sqlite;

namespace HMI.Core.Services;

/// <summary>
/// Evaluates alarm rules from the variable pool's internal change stream and persists every state
/// transition to SQLite. UI polling is deliberately not used as the alarm event source.
/// </summary>
public sealed class AlarmManager : IAlarmManager, IAsyncDisposable
{
    private readonly MemoryVariablePool _pool;
    private readonly AlarmRule[] _rules;
    private readonly string _connectionString;
    private readonly Action<Exception>? _onError;
    private readonly IDisposable _changeLease;
    private readonly Channel<VariableValuePackage> _changes = Channel.CreateUnbounded<VariableValuePackage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly Dictionary<string, ActiveAlarm> _activeById = new(StringComparer.Ordinal);
    private Task _worker = Task.CompletedTask;
    private bool _disposed;

    private AlarmManager(MemoryVariablePool pool, IEnumerable<AlarmRule> rules, string databasePath, Action<Exception>? onError)
    {
        _pool = pool;
        _rules = ValidateRules(pool, rules);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath), Pooling = false }.ToString();
        _onError = onError;
        _changeLease = _pool.RegisterInternalChangeConsumer(package => _changes.Writer.TryWrite(package));
    }

    public static async Task<AlarmManager> CreateAsync(
        MemoryVariablePool pool,
        IEnumerable<AlarmRule> rules,
        string databasePath,
        CancellationToken ct = default,
        Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);

        var manager = new AlarmManager(pool, rules, databasePath, onError);
        try
        {
            await manager.InitializeAsync(ct).ConfigureAwait(false);
            manager._worker = Task.Run(manager.ProcessChangesAsync);
            foreach (var rule in manager._rules)
            {
                if (pool.TryGetValue(rule.VarId, out var value))
                    manager._changes.Writer.TryWrite(new VariableValuePackage
                    {
                        VarId = rule.VarId,
                        Value = value,
                        Quality = pool.GetQuality(rule.VarId),
                        ServerTimestamp = DateTimeOffset.UtcNow
                    });
            }
            return manager;
        }
        catch
        {
            await manager.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static AlarmRule[] ValidateRules(MemoryVariablePool pool, IEnumerable<AlarmRule> rules)
    {
        var enabledRules = rules.Where(rule => rule.Enabled).ToArray();
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var allowedOperators = new HashSet<string>(StringComparer.Ordinal)
            { ">", ">=", "<", "<=", "==", "=", "!=", "<>" };

        foreach (var rule in enabledRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
                errors.Add("启用的报警规则必须设置 Id");
            else if (!ids.Add(rule.Id.Trim()))
                errors.Add($"报警规则 Id 重复: {rule.Id}");

            if (string.IsNullOrWhiteSpace(rule.Name))
                errors.Add($"报警规则 {rule.Id} 的名称不能为空");
            if (string.IsNullOrWhiteSpace(rule.VarId))
                errors.Add($"报警规则 {rule.Id} 的变量 Id 不能为空");
            else if (!pool.Contains(rule.VarId))
                errors.Add($"报警规则 {rule.Id} 引用了不存在的变量: {rule.VarId}");

            if (string.IsNullOrWhiteSpace(rule.Operator) || !allowedOperators.Contains(rule.Operator.Trim()))
                errors.Add($"报警规则 {rule.Id} 的比较运算符无效: {rule.Operator}");

            var value = NormalizeRuleValue(rule.Value);
            if (value is null || value is JsonElement { ValueKind: JsonValueKind.Array or JsonValueKind.Object })
                errors.Add($"报警规则 {rule.Id} 的比较值必须是字符串、布尔值或数字");
            else if (TryNumber(value, out var number) && !double.IsFinite(number))
                errors.Add($"报警规则 {rule.Id} 的数字比较值必须是有限数值");
        }

        if (errors.Count > 0)
            throw new ArgumentException("报警规则校验失败: " + string.Join("；", errors), nameof(rules));

        return enabledRules;
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS AlarmHistory (
                Sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                AlarmId TEXT NOT NULL,
                RuleId TEXT NOT NULL,
                RuleName TEXT NOT NULL,
                VarId TEXT NOT NULL,
                EventType TEXT NOT NULL,
                Message TEXT NULL,
                Value TEXT NULL,
                Quality TEXT NOT NULL,
                OccurredAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_AlarmHistory_AlarmId_Sequence
                ON AlarmHistory (AlarmId, Sequence);
            CREATE INDEX IF NOT EXISTS IX_AlarmHistory_OccurredAt
                ON AlarmHistory (OccurredAt DESC);
            """;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        var history = new List<AlarmEventRecord>();
        command = connection.CreateCommand();
        command.CommandText = """
            SELECT Sequence, AlarmId, RuleId, RuleName, VarId, EventType, Message, Value, Quality, OccurredAt
            FROM AlarmHistory ORDER BY Sequence;
            """;
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            history.Add(ReadEvent(reader));

        foreach (var item in history)
        {
            switch (item.EventType)
            {
                case "Triggered":
                    _activeById[item.AlarmId] = new ActiveAlarm
                    {
                        AlarmId = item.AlarmId,
                        RuleId = item.RuleId,
                        RuleName = item.RuleName,
                        VarId = item.VarId,
                        Message = item.Message,
                        Value = item.Value,
                        TriggeredAt = item.OccurredAt
                    };
                    break;
                case "Acknowledged":
                    if (_activeById.TryGetValue(item.AlarmId, out var active))
                    {
                        active.Acknowledged = true;
                        active.AcknowledgedAt = item.OccurredAt;
                    }
                    break;
                case "Recovered":
                    _activeById.Remove(item.AlarmId);
                    break;
            }
        }
    }

    public async Task<IReadOnlyList<ActiveAlarm>> GetActiveAsync(CancellationToken ct = default)
    {
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _activeById.Values
                .OrderByDescending(alarm => alarm.TriggeredAt)
                .Select(Clone)
                .ToArray();
        }
        finally { _stateGate.Release(); }
    }

    public async Task<IReadOnlyList<AlarmEventRecord>> GetHistoryAsync(int limit = 100, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 1000);
        var result = new List<AlarmEventRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Sequence, AlarmId, RuleId, RuleName, VarId, EventType, Message, Value, Quality, OccurredAt
            FROM AlarmHistory ORDER BY Sequence DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add(ReadEvent(reader));
        return result;
    }

    public async Task AcknowledgeAsync(string alarmId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alarmId);
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_activeById.TryGetValue(alarmId, out var active))
                throw new KeyNotFoundException($"活动报警不存在: {alarmId}");
            if (active.Acknowledged) return;

            var occurredAt = DateTimeOffset.UtcNow;
            await InsertEventAsync(new AlarmEventRecord
            {
                AlarmId = active.AlarmId,
                RuleId = active.RuleId,
                RuleName = active.RuleName,
                VarId = active.VarId,
                EventType = "Acknowledged",
                Message = active.Message,
                Value = active.Value,
                Quality = VariableQuality.Good.ToString(),
                OccurredAt = occurredAt
            }, ct).ConfigureAwait(false);
            active.Acknowledged = true;
            active.AcknowledgedAt = occurredAt;
        }
        finally { _stateGate.Release(); }
    }

    private async Task ProcessChangesAsync()
    {
        await foreach (var package in _changes.Reader.ReadAllAsync())
        {
            await _stateGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (package.Quality != VariableQuality.Good) continue;
                foreach (var rule in _rules.Where(item => string.Equals(item.VarId, package.VarId, StringComparison.Ordinal)))
                    await EvaluateRuleAsync(rule, package).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A malformed rule or transient database error must not terminate the event consumer.
                try { _onError?.Invoke(ex); } catch { /* Logging must not break alarm processing. */ }
            }
            finally { _stateGate.Release(); }
        }
    }

    private async Task EvaluateRuleAsync(AlarmRule rule, VariableValuePackage package)
    {
        var active = _activeById.Values.FirstOrDefault(item => item.RuleId == rule.Id);
        var condition = Compare(package.Value, rule.Operator, NormalizeRuleValue(rule.Value));
        if (condition && active is null)
        {
            var item = new ActiveAlarm
            {
                AlarmId = Guid.NewGuid().ToString("N"),
                RuleId = rule.Id,
                RuleName = rule.Name,
                VarId = rule.VarId,
                Message = string.IsNullOrWhiteSpace(rule.Message) ? rule.Name : rule.Message,
                Value = FormatValue(package.Value),
                TriggeredAt = DateTimeOffset.UtcNow
            };
            await InsertEventAsync(new AlarmEventRecord
            {
                AlarmId = item.AlarmId,
                RuleId = item.RuleId,
                RuleName = item.RuleName,
                VarId = item.VarId,
                EventType = "Triggered",
                Message = item.Message,
                Value = item.Value,
                Quality = package.Quality.ToString(),
                OccurredAt = item.TriggeredAt
            }, CancellationToken.None).ConfigureAwait(false);
            _activeById[item.AlarmId] = item;
        }
        else if (!condition && active is not null)
        {
            var recoveredAt = DateTimeOffset.UtcNow;
            await InsertEventAsync(new AlarmEventRecord
            {
                AlarmId = active.AlarmId,
                RuleId = active.RuleId,
                RuleName = active.RuleName,
                VarId = active.VarId,
                EventType = "Recovered",
                Message = active.Message,
                Value = FormatValue(package.Value),
                Quality = package.Quality.ToString(),
                OccurredAt = recoveredAt
            }, CancellationToken.None).ConfigureAwait(false);
            _activeById.Remove(active.AlarmId);
        }
        else if (condition && active is not null)
        {
            active.Value = FormatValue(package.Value);
        }
    }

    private async Task InsertEventAsync(AlarmEventRecord item, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AlarmHistory
                (AlarmId, RuleId, RuleName, VarId, EventType, Message, Value, Quality, OccurredAt)
            VALUES
                ($alarmId, $ruleId, $ruleName, $varId, $eventType, $message, $value, $quality, $occurredAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$alarmId", item.AlarmId);
        command.Parameters.AddWithValue("$ruleId", item.RuleId);
        command.Parameters.AddWithValue("$ruleName", item.RuleName);
        command.Parameters.AddWithValue("$varId", item.VarId);
        command.Parameters.AddWithValue("$eventType", item.EventType);
        command.Parameters.AddWithValue("$message", (object?)item.Message ?? DBNull.Value);
        command.Parameters.AddWithValue("$value", (object?)item.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("$quality", item.Quality);
        command.Parameters.AddWithValue("$occurredAt", item.OccurredAt.ToString("O", CultureInfo.InvariantCulture));
        item.Sequence = (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L);
    }

    private static AlarmEventRecord ReadEvent(SqliteDataReader reader) => new()
    {
        Sequence = reader.GetInt64(0),
        AlarmId = reader.GetString(1),
        RuleId = reader.GetString(2),
        RuleName = reader.GetString(3),
        VarId = reader.GetString(4),
        EventType = reader.GetString(5),
        Message = reader.IsDBNull(6) ? null : reader.GetString(6),
        Value = reader.IsDBNull(7) ? null : reader.GetString(7),
        Quality = reader.GetString(8),
        OccurredAt = DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.None)
    };

    private static ActiveAlarm Clone(ActiveAlarm item) => new()
    {
        AlarmId = item.AlarmId,
        RuleId = item.RuleId,
        RuleName = item.RuleName,
        VarId = item.VarId,
        Message = item.Message,
        Value = item.Value,
        TriggeredAt = item.TriggeredAt,
        Acknowledged = item.Acknowledged,
        AcknowledgedAt = item.AcknowledgedAt
    };

    private static object? NormalizeRuleValue(object? value) => value is JsonElement element ? element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when element.TryGetDouble(out var number) => number,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.ToString()
    } : value;

    private static string? FormatValue(object? value) => value switch
    {
        null => null,
        JsonElement element => element.ToString(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static bool Compare(object? left, string operation, object? right)
    {
        if (left is JsonElement element) left = NormalizeRuleValue(element);
        if (right is JsonElement rightElement) right = NormalizeRuleValue(rightElement);
        var op = operation.Trim();
        if (TryNumber(left, out var a) && TryNumber(right, out var b))
            return op switch
            {
                ">" => a > b, ">=" => a >= b, "<" => a < b, "<=" => a <= b,
                "==" or "=" => a == b, "!=" or "<>" => a != b, _ => false
            };
        if (left is bool leftBool && right is bool rightBool)
            return op switch
            {
                "==" or "=" => leftBool == rightBool, "!=" or "<>" => leftBool != rightBool, _ => false
            };
        if (left is string leftText && right is string rightText)
        {
            var comparison = string.Compare(leftText, rightText, StringComparison.Ordinal);
            return op switch
            {
                ">" => comparison > 0, ">=" => comparison >= 0, "<" => comparison < 0, "<=" => comparison <= 0,
                "==" or "=" => comparison == 0, "!=" or "<>" => comparison != 0, _ => false
            };
        }
        return op switch
        {
            "==" or "=" => Equals(left, right),
            "!=" or "<>" => !Equals(left, right),
            _ => false
        };
    }

    private static bool TryNumber(object? value, out double number)
    {
        switch (value)
        {
            case byte v: number = v; return true;
            case sbyte v: number = v; return true;
            case short v: number = v; return true;
            case ushort v: number = v; return true;
            case int v: number = v; return true;
            case uint v: number = v; return true;
            case long v: number = v; return true;
            case ulong v: number = v; return true;
            case float v: number = v; return true;
            case double v: number = v; return true;
            case decimal v: number = (double)v; return true;
            case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                number = parsed; return true;
            default: number = 0; return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _changeLease.Dispose();
        _changes.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
        _stateGate.Dispose();
    }
}
