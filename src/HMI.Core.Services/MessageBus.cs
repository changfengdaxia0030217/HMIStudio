using System.Collections.Concurrent;
using HMI.Core.Interface;

namespace HMI.Core.Services;

/// <summary>全异步内存消息总线（INotifyPropertyChanged 之外的全局事件发布订阅）。</summary>
public sealed class InMemoryMessageBus : IMessageBusTyped
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

    /// <inheritdoc/>
    public Task PublishAsync<TEvent>(TEvent e, CancellationToken ct = default)
    {
        if (_handlers.TryGetValue(typeof(TEvent), out var list))
        {
            var snapshot = list.ToArray();
            foreach (var handler in snapshot)
            {
                if (handler is Func<TEvent, Task> f)
                    _ = f.Invoke(e);
            }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler)
    {
        var list = _handlers.GetOrAdd(typeof(TEvent), _ => new List<Delegate>());
        lock (list)
        {
            list.Add(handler);
        }
        return new Subscription(list, handler);
    }

    /// <inheritdoc/>
    public IDisposable Subscribe<TEvent>(Func<TEvent, bool> predicate, Func<TEvent, Task> handler)
        => Subscribe<TEvent>(async e => { if (predicate(e)) await handler(e); });

    private sealed class Subscription(List<Delegate> list, Delegate handler) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (list) list.Remove(handler);
        }
    }
}