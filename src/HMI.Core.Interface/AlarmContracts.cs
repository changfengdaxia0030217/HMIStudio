namespace HMI.Core.Interface;

public sealed class AlarmRule
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string VarId { get; set; } = string.Empty;
    public string Operator { get; set; } = "==";
    public object? Value { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public string? Message { get; set; }
}

public sealed class AlarmEventRecord
{
    public long Sequence { get; set; }
    public string AlarmId { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string VarId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Value { get; set; }
    public string Quality { get; set; } = "Good";
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class ActiveAlarm
{
    public string AlarmId { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string VarId { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Value { get; set; }
    public DateTimeOffset TriggeredAt { get; set; }
    public bool Acknowledged { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
}
