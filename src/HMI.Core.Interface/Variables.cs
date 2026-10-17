// 变量系统契约（§3 变量系统 / §11 IVariablePool、IVariableValueProvider、IVariableSubscribeManager）。
namespace HMI.Core.Interface;

/// <summary>变量单次值的快照，由订阅层（UI 唯一出口 `IVariableSubscribeManager.RegisterPollingConsumer`）分发；变量池本身不再广播。</summary>
public sealed class VariableValuePackage
{
    /// <summary>变量 Id。</summary>
    public required string VarId { get; init; }
    /// <summary>变量值（可能为 null）。</summary>
    public object? Value { get; init; }
    /// <summary>质量位（§3.7）。</summary>
    public VariableQuality Quality { get; init; }
    /// <summary>采集时间戳。</summary>
    public DateTimeOffset? DeviceTimestamp { get; init; }
    /// <summary>服务端收到/更新该值的时间戳（UTC）。</summary>
    public DateTimeOffset ServerTimestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>表达式引擎读取变量值的隔离接口（§11 IVariableValueProvider）。</summary>
public interface IVariableValueProvider
{
    /// <summary>尝试读取变量值；不存在返回 false。</summary>
    bool TryGetValue(string varId, out object? value);
}

/// <summary>变量池：变量值读写、细粒度锁、质量位管理；写队列集成（§11 IVariablePool，§14.2）。</summary>
public interface IVariablePool : IVariableValueProvider
{
    /// <summary>异步读变量当前值。</summary>
    Task<object?> ReadAsync(string varId, CancellationToken ct = default);

    /// <summary>异步写变量。写请求进入全量串行写队列，保序、保重试（§3.4 / §14.2）。</summary>
    Task<WriteResult> WriteAsync(string varId, object? value, CancellationToken ct = default, WriteSource source = WriteSource.Runtime, string? requestId = null);

    /// <summary>读取变量当前质量位。</summary>
    VariableQuality GetQuality(string varId);

    // 池级 Subscribe 已于 2026-10-12 删除：变量池不做任何订阅广播。
    // UI 唯一出口 = IVariableSubscribeManager.RegisterPollingConsumer；服务端内部如需边沿触发，须另建内部通道，勿在本接口恢复广播。

    /// <summary>变量是否存在。</summary>
    bool Contains(string varId);
}

/// <summary>变量订阅管理：引用计数、分组轮询调度；防抖分发挂订阅层（§3.2 / §11 IVariableSubscribeManager / §14.4）。</summary>
public interface IVariableSubscribeManager
{
    /// <summary>订阅一组变量（并发画面打开计数 +1）。返回释放函数（计数 -1）。</summary>
    Task SubscribeAsync(IReadOnlyCollection<string> varIds, CancellationToken ct = default);

    /// <summary>退订一组变量（引用计数 -1）。</summary>
    Task UnsubscribeAsync(IReadOnlyCollection<string> varIds, CancellationToken ct = default);

    /// <summary>注册一个按周期轮询的订阅消费者（内部按 CycleMs 分组调度读取）。<b>本通道为运行时 UI 的唯一出口</b>（池级 Subscribe 已删除）；服务端内部模块请勿复用。</summary>
    IDisposable RegisterPollingConsumer(Action<VariableValuePackage> onChanged);
}