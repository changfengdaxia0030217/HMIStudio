// 相机契约（§8.3 / §11 ICameraDevice、ICameraFactory、ICameraManager / §14.5 多路）。
namespace HMI.Core.Interface;

/// <summary>一帧相机图像回调数据。</summary>
public sealed class CameraFrame
{
    /// <summary>通道号。</summary>
    public required int Channel { get; init; }
    /// <summary>图像数据。</summary>
    public required byte[] Data { get; init; }
    /// <summary>采集时间戳。</summary>
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>相机实例接口，图像回调推送（§11 ICameraDevice）。</summary>
public interface ICameraDevice
{
    /// <summary>图像到达事件。</summary>
    event Action<CameraFrame>? FrameReceived;
    /// <summary>异步启动采集。</summary>
    Task StartAsync(CancellationToken ct = default);
    /// <summary>异步停止采集。</summary>
    Task StopAsync(CancellationToken ct = default);
}

/// <summary>相机插件工厂（§11 ICameraFactory）。</summary>
public interface ICameraFactory
{
    /// <summary>创建设备实例。</summary>
    ICameraDevice Create(IReadOnlyDictionary<string, object?> options);
}

/// <summary>相机插件加载与实例管理，约束并行通道上限 ChannelCount（§11 ICameraManager，§14.5 默认 5，断流隔离）。</summary>
public interface ICameraManager
{
    /// <summary>最大并行通道数（默认 5）。</summary>
    int ChannelCount { get; }
    /// <summary>注册通道。</summary>
    ICameraDevice RegisterChannel(int channel, ICameraDevice device);
    /// <summary>启动全部通道。</summary>
    Task StartAllAsync(CancellationToken ct = default);
    /// <summary>停止全部通道。</summary>
    Task StopAllAsync(CancellationToken ct = default);
}