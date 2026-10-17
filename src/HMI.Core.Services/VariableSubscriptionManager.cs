using System.Collections.Concurrent;
using HMI.Core.Interface;

namespace HMI.Core.Services;

/// <summary>
/// 变量订阅管理器：按变量引用计数和 CycleMs 分组轮询。
/// 仅活动变量进入轮询表；永久变量始终活动。单个消费者故障不能中断其他消费者或轮询循环。
/// </summary>
public sealed class VariableSubscriptionManager : IVariableSubscribeManager, IAsyncDisposable
{
    private readonly MemoryVariablePool _pool;
    private readonly ConcurrentDictionary<string, int> _refCount = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastEmit = new();
    private readonly ConcurrentDictionary<int, PollGroup> _groups = new();
    private readonly ConcurrentDictionary<string, int> _varToCycle = new();
    private readonly ConcurrentDictionary<int, List<Action<VariableValuePackage>>> _consumers = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly object _disposeLock = new();
    private Task? _disposeTask;
    private readonly Action<Exception>? _consumerError;
    private bool _disposed;

    public VariableSubscriptionManager(MemoryVariablePool pool, Action<Exception>? consumerError = null)
    {
        _pool = pool;
        _consumerError = consumerError;

        // 永久变量不依赖任何画面订阅，服务启动后即进入采集。
        foreach (var meta in _pool.Metas.Values.Where(m => m.IsPermanent))
            ActivateVariable(meta.VarId);
    }

    private sealed class PollGroup
    {
        public required int CycleMs { get; init; }
        public CancellationTokenSource? Cts;
        public Task? Loop;
    }

    public async Task SubscribeAsync(IReadOnlyCollection<string> varIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(varIds);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var id in varIds.Distinct(StringComparer.Ordinal))
            {
                if (_pool.GetQuality(id) == VariableQuality.BadNotExist) continue;
                var count = _refCount.AddOrUpdate(id, 1, (_, current) => current + 1);
                if (count == 1) ActivateVariable(id);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task UnsubscribeAsync(IReadOnlyCollection<string> varIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(varIds);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var id in varIds.Distinct(StringComparer.Ordinal))
            {
                if (!_refCount.TryGetValue(id, out var current)) continue;
                if (current > 1)
                {
                    _refCount[id] = current - 1;
                    continue;
                }

                _refCount.TryRemove(id, out _);
                if (_pool.Metas.TryGetValue(id, out var meta) && meta.IsPermanent) continue;
                DeactivateVariable(id);
            }
        }
        finally { _gate.Release(); }
    }

    public IDisposable RegisterPollingConsumer(Action<VariableValuePackage> onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        ObjectDisposedException.ThrowIf(_disposed, this);
        const int key = 0; // 当前 UI 值通道；后续可扩展按会话隔离。
        var list = _consumers.GetOrAdd(key, _ => new List<Action<VariableValuePackage>>());
        lock (list) list.Add(onChanged);
        return new ConsumerUnsub(list, onChanged);
    }

    private void ActivateVariable(string varId)
    {
        if (!_pool.Contains(varId)) return;
        var cycle = _pool.Metas.TryGetValue(varId, out var meta) && meta.CycleMs > 0 ? meta.CycleMs : 100;
        _varToCycle[varId] = cycle;

        var group = _groups.GetOrAdd(cycle, c => new PollGroup { CycleMs = c });
        lock (group)
        {
            if (group.Loop is null || group.Loop.IsCompleted || group.Cts?.IsCancellationRequested == true)
            {
                group.Cts?.Dispose();
                group.Cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                group.Loop = Task.Run(() => RunGroupAsync(group));
            }
        }
    }

    private void DeactivateVariable(string varId)
    {
        if (!_varToCycle.TryRemove(varId, out var cycle)) return;
        _lastEmit.TryRemove(varId, out _);

        if (!_groups.TryGetValue(cycle, out var group)) return;
        // _gate serializes activation/deactivation. The worker only reads snapshots and never takes _gate.
        if (_varToCycle.Any(kv => kv.Value == cycle)) return;

        if (_groups.TryGetValue(cycle, out var current) && ReferenceEquals(current, group))
            _groups.TryRemove(cycle, out _);

        lock (group)
        {
            group.Cts?.Cancel();
        }

        // The group has been removed from _groups, so dispose its linked CTS after
        // the worker exits instead of leaving its cancellation registration behind.
        _ = DisposeInactiveGroupAsync(group);
    }

    private static async Task DisposeInactiveGroupAsync(PollGroup group)
    {
        var loop = group.Loop;
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        lock (group)
        {
            group.Cts?.Dispose();
            group.Cts = null;
        }
    }

    private async Task RunGroupAsync(PollGroup group)
    {
        var token = group.Cts!.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(group.CycleMs, token).ConfigureAwait(false);
                var varIds = _varToCycle
                    .Where(kv => kv.Value == group.CycleMs)
                    .Select(kv => kv.Key)
                    .ToArray();

                await _pool.RefreshFromDriversAsync(varIds, token).ConfigureAwait(false);
                foreach (var id in varIds)
                {
                    if (token.IsCancellationRequested) break;
                    if (!_varToCycle.TryGetValue(id, out var activeCycle) || activeCycle != group.CycleMs)
                        continue;
                    if (!_pool.TryGetValue(id, out var value)) continue;

                    var quality = _pool.GetQuality(id);
                    if (ShouldDebounce(id)) continue;
                    _lastEmit[id] = DateTimeOffset.UtcNow;
                    Emit(id, value, quality);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A bad value or unexpected pool error must not permanently kill this polling group.
                try { _consumerError?.Invoke(ex); } catch { /* diagnostics are isolated too */ }
                // Avoid a tight retry loop if a driver or pool repeatedly fails.
                try { await Task.Delay(100, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            }
        }
    }

    // DebounceMs currently means a minimum interval between emitted samples for this variable.
    private bool ShouldDebounce(string varId)
    {
        if (!_pool.Metas.TryGetValue(varId, out var meta) || meta.DebounceMs <= 0) return false;
        if (!_lastEmit.TryGetValue(varId, out var last)) return false;
        return DateTimeOffset.UtcNow - last < TimeSpan.FromMilliseconds(meta.DebounceMs);
    }

    private void Emit(string varId, object? value, VariableQuality quality)
    {
        var package = new VariableValuePackage
        {
            VarId = varId,
            Value = value,
            Quality = quality,
            ServerTimestamp = DateTimeOffset.UtcNow
        };

        foreach (var list in _consumers.Values)
        {
            Action<VariableValuePackage>[] snapshot;
            lock (list) snapshot = list.ToArray();
            foreach (var callback in snapshot)
            {
                try { callback(package); }
                catch (Exception ex)
                {
                    try { _consumerError?.Invoke(ex); } catch { /* isolate diagnostic callbacks */ }
                }
            }
        }
    }

    private sealed class ConsumerUnsub(List<Action<VariableValuePackage>> list, Action<VariableValuePackage> handler) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (list) list.Remove(handler);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            _cts.Cancel();
            foreach (var group in _groups.Values)
            {
                lock (group) group.Cts?.Cancel();
            }
        }
        finally { _gate.Release(); }

        var loops = _groups.Values.Select(g => g.Loop).Where(t => t is not null).Cast<Task>().ToArray();
        try { await Task.WhenAll(loops).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        foreach (var group in _groups.Values) group.Cts?.Dispose();
        _cts.Dispose();
        _gate.Dispose();
    }
}