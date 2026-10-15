using System.Collections.Concurrent;
using HMI.Core.Interface;

namespace HMI.Core.Services;

/// <summary>Driver manager for the first-release simulation-only mode.</summary>
public sealed class MockPlcDriverManager : IPlcDriverManager, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, IPlcDriver> _drivers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IPlcDriverFactory> _factories = new();
    private readonly object _factoryLock = new();

    public MockPlcDriverManager()
    {
        RegisterFactory(new MockPlcDriverFactory());
    }

    public void RegisterFactory(IPlcDriverFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (_factoryLock) _factories.Add(factory);
    }

    public IPlcDriver? Resolve(string protocol)
    {
        if (string.IsNullOrWhiteSpace(protocol)) return null;
        return _drivers.GetOrAdd(protocol.Trim(), key =>
        {
            IPlcDriverFactory[] factories;
            lock (_factoryLock) factories = _factories.ToArray();
            // In M0/M1 only the explicitly named mock factory is installed by default.
            // Real protocol factories must be registered deliberately in a later release.
            var options = new Dictionary<string, object?>();
            foreach (var factory in factories)
            {
                try { return factory.Create(key, options); }
                catch (NotSupportedException) { /* try next registered protocol factory */ }
            }
            throw new InvalidOperationException($"没有为协议注册驱动工厂: {key}");
        });
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var driver in _drivers.Values)
        {
            try { await driver.DisconnectAsync().ConfigureAwait(false); }
            catch { /* shutdown should continue for remaining drivers */ }
        }
        _drivers.Clear();
    }
}
