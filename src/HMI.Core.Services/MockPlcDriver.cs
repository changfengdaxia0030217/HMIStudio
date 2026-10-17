using System.Collections.Concurrent;
using HMI.Core.Interface;

namespace HMI.Core.Services;

/// <summary>
/// Deterministic PLC simulator for development and automated tests. It never opens sockets or touches hardware.
/// Addresses are opaque keys; the driver can be seeded through options and changed through SetValue.
/// </summary>
public sealed class MockPlcDriver : IPlcDriver
{
    private static readonly object NullSentinel = new();
    private readonly ConcurrentDictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);
    private volatile LinkStatus _status = LinkStatus.Initialized;

    public MockPlcDriver(string protocol = "MOCK", IReadOnlyDictionary<string, object?>? initialValues = null)
    {
        Protocol = string.IsNullOrWhiteSpace(protocol) ? "MOCK" : protocol.Trim().ToUpperInvariant();
        if (initialValues is not null)
        {
            foreach (var item in initialValues)
                _values[item.Key] = item.Value ?? NullSentinel;
        }
    }

    public string Protocol { get; }
    public LinkStatus Status => _status;

    public Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _status = LinkStatus.Connected;
        return Task.FromResult(true);
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _status = LinkStatus.Disconnected;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, object?>> ReadBatchAsync(
        IReadOnlyCollection<string> addresses, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_status != LinkStatus.Connected)
            throw new InvalidOperationException($"模拟驱动 {Protocol} 尚未连接");

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in addresses.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (_values.TryGetValue(address, out var value))
                result[address] = ReferenceEquals(value, NullSentinel) ? null : value;
        }
        return Task.FromResult<IReadOnlyDictionary<string, object?>>(result);
    }

    public Task<bool> WriteAsync(string address, object? value, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_status != LinkStatus.Connected) return Task.FromResult(false);
        if (string.IsNullOrWhiteSpace(address)) return Task.FromResult(false);
        _values[address] = value ?? NullSentinel;
        return Task.FromResult(true);
    }

    /// <summary>为模拟场景注入 PLC 侧变化；适合演示传感器值和报警状态。</summary>
    public void SetValue(string address, object? value)
    {
        if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("地址不能为空", nameof(address));
        _values[address] = value ?? NullSentinel;
    }

    public bool TryGetValue(string address, out object? value)
    {
        if (_values.TryGetValue(address, out var stored))
        {
            value = ReferenceEquals(stored, NullSentinel) ? null : stored;
            return true;
        }
        value = null;
        return false;
    }
}

/// <summary>Creates simulator instances without requiring a real PLC protocol package.</summary>
public sealed class MockPlcDriverFactory : IPlcDriverFactory
{
    public IPlcDriver Create(string protocol, IReadOnlyDictionary<string, object?> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        IReadOnlyDictionary<string, object?>? initialValues = null;
        if (options.TryGetValue("initialValues", out var configured) &&
            configured is IReadOnlyDictionary<string, object?> dictionary)
        {
            initialValues = dictionary;
        }

        return new MockPlcDriver(protocol, initialValues);
    }
}
