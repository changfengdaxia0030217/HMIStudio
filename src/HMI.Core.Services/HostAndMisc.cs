using System.Collections.Concurrent;
using System.IO;
using HMI.Core.Interface;

namespace HMI.Core.Services;

/// <summary>宿主生命周期：按优先级初始化/启动，逆序停止（§2 / §11 IHostLifetime）。</summary>
public sealed class HostLifetime : IHostLifetime
{
    private readonly List<(IHostModule Module, int Priority)> _modules = new();

    /// <inheritdoc/>
    public void AddModule(IHostModule module, int priority) => _modules.Add((module, priority));

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken ct = default)
    {
        foreach (var m in _modules.OrderBy(x => x.Priority))
            await m.Module.InitializeAsync(ct);
        foreach (var m in _modules.OrderBy(x => x.Priority))
            await m.Module.StartAsync(ct);
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct = default)
    {
        foreach (var m in _modules.OrderByDescending(x => x.Priority))
            await m.Module.StopAsync(ct);
    }
}

/// <summary>运行时状态管理（设计/运行/停止，§11 IRuntimeStateManager）。</summary>
public sealed class RuntimeStateManager : IRuntimeStateManager
{
    /// <inheritdoc/>
    public RuntimeMode Mode { get; private set; } = RuntimeMode.Stopped;

    /// <inheritdoc/>
    public Task SwitchAsync(RuntimeMode mode, CancellationToken ct = default)
    {
        Mode = mode;
        return Task.CompletedTask;
    }
}

/// <summary>控制台日志实现（§11 ILogger）。</summary>
public sealed class ConsoleLogger : ILogger
{
    private readonly string? _logFile;
    private readonly object _fileLock = new();

    /// <summary>仅控制台输出。</summary>
    public ConsoleLogger() { }

    /// <summary>控制台 + 落盘文件双写（本地定位用）。</summary>
    public ConsoleLogger(string logFile) => _logFile = logFile;

    /// <inheritdoc/>
    public Task LogAsync(string level, string message, Exception? ex = null, CancellationToken ct = default)
    {
        var line = $"[{DateTimeOffset.Now:HH:mm:ss.fff}] {level}: {message}{(ex is null ? "" : " | " + ex)}";
        if (level == "ERROR") Console.Error.WriteLine(line);
        else Console.WriteLine(line);
        if (_logFile is not null)
        {
            lock (_fileLock)
            {
                try { File.AppendAllText(_logFile, line + Environment.NewLine); }
                catch { /* 文件不可写时仅控制台 */ }
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> QueryAsync(int skip, int take, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}

/// <summary>PLC 占位驱动：本切片不连硬件，读/写均返回 BadComm 语义的失败或空，供质量位演示。</summary>
public sealed class StubPlcDriver : IPlcDriver
{
    private readonly string _protocol;
    private LinkStatus _status = LinkStatus.Disconnected;

    public StubPlcDriver(string protocol) => _protocol = protocol;

    /// <inheritdoc/>
    public string Protocol => _protocol;

    /// <inheritdoc/>
    public LinkStatus Status => _status;

    /// <inheritdoc/>
    public Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        _status = LinkStatus.Disconnected; // 占位：始终不可达，用于演示 BadComm
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _status = LinkStatus.Disconnected;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<string, object?>> ReadBatchAsync(IReadOnlyCollection<string> addresses, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());

    /// <inheritdoc/>
    public Task<bool> WriteAsync(string address, object? value, CancellationToken ct = default)
        => Task.FromResult(false);
}

/// <summary>PLC 管理内存实现（§11 IPlcDriverManager）：占位注册 Stub 驱动。</summary>
public sealed class StubPlcDriverManager : IPlcDriverManager
{
    private readonly ConcurrentDictionary<string, IPlcDriver> _drivers = new();

    /// <inheritdoc/>
    public void RegisterFactory(IPlcDriverFactory factory) { /* 占位，工厂加载在插件切片实现 */ }

    /// <inheritdoc/>
    public IPlcDriver? Resolve(string protocol) => _drivers.GetOrAdd(protocol, p => new StubPlcDriver(p));
}