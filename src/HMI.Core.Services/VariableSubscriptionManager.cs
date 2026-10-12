using System.Collections.Concurrent;
using HMI.Core.Interface;

namespace HMI.Core.Services;

/// <summary>
/// 变量订阅管理器（§3.2/§3.3）：画面激活引用计数 +1、切离 -1；按变量独立周期 CycleMs 分组轮询读取并广播；§14.4 防抖分发。
/// 永久常驻变量（IsPermanent=true）不参与引用计数，始终采集。
/// </summary>
public sealed class VariableSubscriptionManager : IVariableSubscribeManager, IAsyncDisposable
{
    private readonly MemoryVariablePool _pool;
    private readonly ConcurrentDictionary<string, int> _refCount = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastEmit = new();

    // 按 CycleMs 分组的轮询组：cycle -> 组运行状态
    private readonly ConcurrentDictionary<int, PollGroup> _groups = new();
    private readonly ConcurrentDictionary<string, int> _varToCycle = new();

    private readonly ConcurrentDictionary<int, List<Action<VariableValuePackage>>> _consumers = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public VariableSubscriptionManager(MemoryVariablePool pool) => _pool = pool;

    private sealed class PollGroup
    {
        public required int CycleMs { get; init; }
        public int RefCount;
        public CancellationTokenSource? Cts;
        public Task? Loop;
    }

    /// <inheritdoc/>
    public async Task SubscribeAsync(IReadOnlyCollection<string> varIds, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            foreach (var id in varIds)
            {
                if (_pool.GetQuality(id) == VariableQuality.BadNotExist) continue;
                AddRef(id);
            }
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc/>
    public async Task UnsubscribeAsync(IReadOnlyCollection<string> varIds, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            foreach (var id in varIds) RemoveRef(id);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc/>
    public IDisposable RegisterPollingConsumer(Action<VariableValuePackage> onChanged)
    {
        var key = 0; // 运行时 UI 的唯一出口（池级 Subscribe 已于 2026-10-12 删除）；服务端内部模块请勿复用。扩多路再细分。
        var list = _consumers.GetOrAdd(key, _ => new List<Action<VariableValuePackage>>());
        lock (list) list.Add(onChanged);
        return new ConsumerUnsub(list, onChanged);
    }

    private void AddRef(string varId)
    {
        _refCount.AddOrUpdate(varId, 1, (_, c) => c + 1);
        EnsureGroupRunning(varId);
    }

    private void RemoveRef(string varId)
    {
        if (!_refCount.TryGetValue(varId, out _)) return;
        var c = _refCount.AddOrUpdate(varId, 0, (_, v) => Math.Max(0, v - 1));
        if (c <= 0)
        {
            _refCount.TryRemove(varId, out _);
            // 停该变量所在组（真实框架按需停止；本切片保留组方便演示）
        }
    }

    private void EnsureGroupRunning(string varId)
    {
        int cycle = _pool.Metas.TryGetValue(varId, out var meta) && meta.CycleMs > 0 ? meta.CycleMs : 100;
        _varToCycle[varId] = cycle;
        var group = _groups.GetOrAdd(cycle, c => new PollGroup { CycleMs = c });
        lock (group)
        {
            group.RefCount++;
            if (group.Loop is null or { IsCompleted: true })
            {
                group.Cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                group.Loop = Task.Run(() => RunGroupAsync(group));
            }
        }
    }

    private async Task RunGroupAsync(PollGroup group)
    {
        while (!group.Cts!.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(group.CycleMs, group.Cts.Token);
                var varIds = _varToCycle.Where(kv => kv.Value == group.CycleMs).Select(kv => kv.Key).ToArray();
                foreach (var id in varIds)
                {
                    if (_pool.TryGetValue(id, out var value))
                    {
                        var quality = _pool.GetQuality(id);
                        if (ShouldDebounce(id)) continue;
                        _lastEmit[id] = DateTimeOffset.Now;
                        Emit(id, value, quality);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // §14.4：DebounceMs 时间窗内合并高频变化。
    private bool ShouldDebounce(string varId)
    {
        if (!_pool.Metas.TryGetValue(varId, out var meta) || meta.DebounceMs <= 0) return false;
        if (!_lastEmit.TryGetValue(varId, out var t)) return false;
        return DateTimeOffset.Now - t < TimeSpan.FromMilliseconds(meta.DebounceMs);
    }

    private void Emit(string varId, object? value, VariableQuality quality)
    {
        var package = new VariableValuePackage { VarId = varId, Value = value, Quality = quality, Timestamp = DateTimeOffset.Now };
        foreach (var list in _consumers.Values)
        {
            Action<VariableValuePackage>[] snapshot;
            lock (list) snapshot = list.ToArray();
            foreach (var cb in snapshot) cb(package);
        }
    }

    private sealed class ConsumerUnsub(List<Action<VariableValuePackage>> list, Action<VariableValuePackage> handler) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (list) list.Remove(handler);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        foreach (var g in _groups.Values)
        {
            g.Cts?.Cancel();
            if (g.Loop != null) await g.Loop;
        }
        _cts.Dispose();
        _gate.Dispose();
    }
}