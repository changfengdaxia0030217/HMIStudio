namespace HMI.Core.Interface;

public interface IAlarmManager
{
    Task AcknowledgeAsync(string alarmId, CancellationToken ct = default);

    Task<IReadOnlyList<ActiveAlarm>> GetActiveAsync(
        CancellationToken ct = default);

    Task<int> GetHistoryCountAsync(
        CancellationToken ct = default,
        string? eventType = null,
        string? search = null);

    Task<IReadOnlyList<AlarmEventRecord>> GetHistoryAsync(
        int limit = 100,
        CancellationToken ct = default,
        string? eventType = null,
        string? search = null,
        int offset = 0);
}
