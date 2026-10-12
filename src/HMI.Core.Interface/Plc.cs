// PLC 驱动契约（§8.3 / §11 IPlcDriver、IPlcDriverFactory、IPlcDriverManager / §14.1 多协议）。
namespace HMI.Core.Interface;

/// <summary>PLC 与外部设备的底层连接状态。</summary>
public enum LinkStatus
{
    Initialized,
    Connected,
    Disconnected,
    Reconnecting,
}

/// <summary>PLC 驱动实例接口（§11 IPlcDriver；S7 / 汇川 EIP(CIP) / 汇川 ModbusTCP 各为一个实现）。</summary>
public interface IPlcDriver
{
    /// <summary>协议标识："S7" / "EIP" / "MODBUSTCP"。</summary>
    string Protocol { get; }

    /// <summary>当前连接状态。</summary>
    LinkStatus Status { get; }

    /// <summary>异步连接。</summary>
    Task<bool> ConnectAsync(CancellationToken ct = default);

    /// <summary>异步断开。</summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>异步批量读（返回按变量地址映射的值字典）。</summary>
    Task<IReadOnlyDictionary<string, object?>> ReadBatchAsync(IReadOnlyCollection<string> addresses, CancellationToken ct = default);

    /// <summary>异步单点写。</summary>
    Task<bool> WriteAsync(string address, object? value, CancellationToken ct = default);
}

/// <summary>PLC 协议插件工厂（§14.1，按协议名选型）。</summary>
public interface IPlcDriverFactory
{
    /// <summary>创建指定协议的驱动实例。</summary>
    IPlcDriver Create(string protocol, IReadOnlyDictionary<string, object?> options);
}

/// <summary>PLC 插件加载与驱动实例管理（§11 IPlcDriverManager）。</summary>
public interface IPlcDriverManager
{
    /// <summary>注册工厂。</summary>
    void RegisterFactory(IPlcDriverFactory factory);
    /// <summary>按协议解析驱动实例。</summary>
    IPlcDriver? Resolve(string protocol);
}