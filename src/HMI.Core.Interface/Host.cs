// 宿主生命周期 / 消息总线 / 日志 契约（§2.1 / §11 IHostLifetime、IHostModule、IMessageBus、IMessageBusTyped、ILogger）。
namespace HMI.Core.Interface;

/// <summary>模块生命周期接口（§11 IHostModule）。</summary>
public interface IHostModule
{
    /// <summary>异步初始化。</summary>
    Task InitializeAsync(CancellationToken ct = default);
    /// <summary>异步启动。</summary>
    Task StartAsync(CancellationToken ct = default);
    /// <summary>异步停止。</summary>
    Task StopAsync(CancellationToken ct = default);
}

/// <summary>宿主生命周期管理，按优先级启停全部模块（§11 IHostLifetime）。</summary>
public interface IHostLifetime
{
    /// <summary>注册一个模块与启停优先级（数值小者先初始化、后停止）。</summary>
    void AddModule(IHostModule module, int priority);
    /// <summary>按优先级顺序初始化并启动全部模块。</summary>
    Task StartAsync(CancellationToken ct = default);
    /// <summary>逆序停止全部模块。</summary>
    Task StopAsync(CancellationToken ct = default);
}

/// <summary>全局事件发布订阅（全异步，§11 IMessageBus）。</summary>
public interface IMessageBus
{
    /// <summary>发布事件。</summary>
    Task PublishAsync<TEvent>(TEvent e, CancellationToken ct = default);
    /// <summary>订阅事件。</summary>
    IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler);
}

/// <summary>强类型消息总线扩展（§11 IMessageBusTyped）。</summary>
public interface IMessageBusTyped : IMessageBus
{
    /// <summary>订阅并自动按谓词分发。</summary>
    IDisposable Subscribe<TEvent>(Func<TEvent, bool> predicate, Func<TEvent, Task> handler);
}

/// <summary>全异步日志写入与历史查询（§11 ILogger）。</summary>
public interface ILogger
{
    /// <summary>写入一条日志。</summary>
    Task LogAsync(string level, string message, Exception? ex = null, CancellationToken ct = default);
    /// <summary>查询日志历史。</summary>
    IAsyncEnumerable<string> QueryAsync(int skip, int take, CancellationToken ct = default);
}