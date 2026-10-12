using System.Collections.Concurrent;
using System.Threading.Channels;
using HMI.Core.Interface;
using HMI.Core.Model;

namespace HMI.Core.Services;

/// <summary>
/// 内存变量池：变量值/质量位存储、细粒度变量锁（§3.6）、全量串行写队列（§3.4/§14.2）。
/// PLC / 相机变量在本切片为占位：加载时标记 BadComm，不连硬件。
/// <b>本类不做任何订阅广播</b>（原 Subscribe/Broadcast 已于 2026-10-12 删除）：UI 唯一出口 =
/// IVariableSubscribeManager.RegisterPollingConsumer。服务端内部如需"值一变就反应"的边沿触发，
/// 须另建内部通道，勿恢复本类广播。
/// </summary>
public sealed class MemoryVariablePool : IVariablePool, IAsyncDisposable
{
    /// <summary>变量槽：可变（写时真正落值），内置细粒度锁对象（§3.6）。</summary>
    private sealed class Slot
    {
        public object? Value;
        public VariableQuality Quality;
        public readonly object Sync = new();
    }

    private readonly ConcurrentDictionary<string, Slot> _slots = new();
    private readonly ConcurrentDictionary<string, VariableMeta> _metas = new();

    // 全量串行写队列（§14.2）：所有变量写汇入同一队列，保序、保重试。
    private readonly Channel<WriteRequest> _writeQueue = Channel.CreateUnbounded<WriteRequest>();
    private readonly Task _writeLoop;

    private sealed record WriteRequest(
        string VarId, object? Value, VariableQuality Quality, TaskCompletionSource Tcs)
    {
        public DateTimeOffset EnqueuedAt { get; } = DateTimeOffset.Now;
    }

    public MemoryVariablePool()
    {
        _writeLoop = Task.Run(RunWriteLoopAsync);
    }

    /// <inheritdoc/>
    public bool Contains(string varId) => _slots.ContainsKey(varId);

    /// <summary>登记一批变量（工程加载时调用）。内存变量取 InitialValue；PLC/相机占位标记 BadComm。</summary>
    public void Register(IEnumerable<VariableMeta> metas)
    {
        foreach (var meta in metas)
        {
            var slot = new Slot
            {
                Value = NormalizeValue(meta.InitialValue),
                Quality = meta.Kind == VariableKind.Memory ? VariableQuality.Good : VariableQuality.BadComm,
            };
            _slots[meta.VarId] = slot;
            _metas[meta.VarId] = meta;
        }
    }

    // 初始值归一为运行形态：JSON 反序列化进来的 JsonElement 在模型入口即转掉，避免 JsonElement 泄漏到变量池/比较/UI（曾致条件求值失败）。
    private static object? NormalizeValue(object? v) => v switch
    {
        System.Text.Json.JsonElement je => je.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number when je.TryGetInt32(out var i) => i,
            System.Text.Json.JsonValueKind.String => je.GetString(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            _ => je,
        },
        _ => v,
    };

    /// <summary>按变量 Id 读取元数据（供订阅管理器按 CycleMs 分组）。</summary>
    public IReadOnlyDictionary<string, VariableMeta> Metas => _metas;

    /// <inheritdoc/>
    public bool TryGetValue(string varId, out object? value)
    {
        if (_slots.TryGetValue(varId, out var slot))
        {
            lock (slot.Sync)
            {
                value = slot.Value;
                return true;
            }
        }
        value = null;
        return false;
    }

    /// <inheritdoc/>
    public Task<object?> ReadAsync(string varId, CancellationToken ct = default)
    {
        TryGetValue(varId, out var value);
        return Task.FromResult(value);
    }

    /// <inheritdoc/>
    public async Task WriteAsync(string varId, object? value, CancellationToken ct = default)
    {
        if (!_slots.TryGetValue(varId, out _))
            return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _writeQueue.Writer.WriteAsync(new WriteRequest(varId, value, VariableQuality.Good, tcs), ct);
        await tcs.Task.WaitAsync(ct);
    }

    /// <inheritdoc/>
    public VariableQuality GetQuality(string varId)
        => _slots.TryGetValue(varId, out var slot) ? slot.Quality : VariableQuality.BadNotExist;

    /// <summary>标记指定变量质量位（通讯断/恢复用）。只落内存、不广播——变化由订阅层按 CycleMs 轮询送出（UI 唯一出口）。</summary>
    public void SetQuality(string varId, VariableQuality quality)
    {
        if (!_slots.TryGetValue(varId, out var slot)) return;
        lock (slot.Sync)
        {
            if (slot.Quality == quality) return;
            slot.Quality = quality;
        }
    }

    /// <summary>写循环（§14.2）：消费写队列，逐条落值并广播。全量串行、保序（默认 Strict 语义，每项都下发）。</summary>
    private async Task RunWriteLoopAsync()
    {
        while (await _writeQueue.Reader.WaitToReadAsync())
        {
            while (_writeQueue.Reader.TryRead(out var req))
            {
                try
                {
                    HandleWrite(req);
                }
                catch (Exception ex)
                {
                    req.Tcs.TrySetException(ex);
                }
            }
        }
    }

    /// <summary>处理单条写请求：直接落值并广播（纯 FIFO 串行，§14.2 默认 Strict）。</summary>
    private void HandleWrite(WriteRequest req)
    {
        ApplyWrite(req.VarId, req.Value, req.Quality);
        req.Tcs.TrySetResult();
    }

    private void ApplyWrite(string varId, object? value, VariableQuality quality)
    {
        if (!_slots.TryGetValue(varId, out var slot)) return;
        lock (slot.Sync)
        {
            slot.Value = value;
            slot.Quality = quality;
            // 不广播：通知统一由订阅层按 CycleMs 轮询送出（UI 唯一出口）。
        }
    }

    public async ValueTask DisposeAsync()
    {
        _writeQueue.Writer.TryComplete();
        await _writeLoop;
    }
}