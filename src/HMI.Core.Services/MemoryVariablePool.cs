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
public sealed class MemoryVariablePool : IVariablePool, IVariableMetadataProvider, IAsyncDisposable
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
    private readonly IPlcDriverManager? _driverManager;

    // 全量串行写队列（§14.2）：所有变量写汇入同一队列，保序、保重试。
    private readonly Channel<WriteRequest> _writeQueue = Channel.CreateUnbounded<WriteRequest>();
    private readonly Task _writeLoop;

    private sealed record WriteRequest(
        string VarId, object? Value, WriteSource Source, string RequestId, TaskCompletionSource<WriteResult> Tcs)
    {
        public DateTimeOffset EnqueuedAt { get; } = DateTimeOffset.Now;
    }

    public MemoryVariablePool(IPlcDriverManager? driverManager = null)
    {
        _driverManager = driverManager;
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

            // Seed the first-release mock PLC from project initial values. This remains simulation-only;
            // real protocol drivers must be explicitly introduced by a future release.
            if (meta.Kind == VariableKind.Plc &&
                _driverManager is MockPlcDriverManager mockManager &&
                !string.IsNullOrWhiteSpace(meta.Source?.Address))
            {
                var driver = mockManager.Resolve(string.IsNullOrWhiteSpace(meta.Source.Channel) ? "MOCK" : meta.Source.Channel);
                if (driver is MockPlcDriver mockDriver)
                    mockDriver.SetValue(meta.Source.Address!, NormalizeValue(meta.InitialValue));
            }
        }
    }

    // 初始值归一为运行形态：JSON 反序列化进来的 JsonElement 在模型入口即转掉，避免 JsonElement 泄漏到变量池/比较/UI（曾致条件求值失败）。
    private static object? NormalizeValue(object? v) => v switch
    {
        System.Text.Json.JsonElement je => je.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number when je.TryGetInt64(out var l) => l,
            System.Text.Json.JsonValueKind.Number when je.TryGetDouble(out var d) => d,
            System.Text.Json.JsonValueKind.String => je.GetString(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            _ => je,
        },
        _ => v,
    };

    /// <summary>按变量 Id 读取元数据（供订阅管理器按 CycleMs 分组）。</summary>
    public IReadOnlyDictionary<string, VariableMeta> Metas => _metas;

    /// <summary>生成权威元数据快照；Writable 从 IsReadOnly 派生，避免重复持久化状态。</summary>
    public IReadOnlyCollection<VariableMetadataSnapshot> GetMetadataSnapshot()
        => _metas.Values
            .OrderBy(meta => meta.VarId, StringComparer.Ordinal)
            .Select(meta => new VariableMetadataSnapshot(meta.VarId, meta.Name, meta.DataType, meta.Min, meta.Max, !meta.IsReadOnly, meta.CycleMs))
            .ToArray();

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
    public async Task<WriteResult> WriteAsync(string varId, object? value, CancellationToken ct = default, WriteSource source = WriteSource.Runtime, string? requestId = null)
    {
        var id = requestId ?? Guid.NewGuid().ToString("N");
        if (!_slots.ContainsKey(varId))
            return new WriteResult(false, WriteFailureReason.NotExist, id, $"变量不存在: {varId}");
        if (_metas.TryGetValue(varId, out var meta) && meta.IsReadOnly)
            return new WriteResult(false, WriteFailureReason.ReadOnly, id, $"变量只读: {varId}");

        value = NormalizeValue(value);
        if (meta is not null && !TryValidateWriteValue(meta, value, out var failureReason, out var validationMessage))
            return new WriteResult(false, failureReason, id, validationMessage);

        var tcs = new TaskCompletionSource<WriteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new WriteRequest(varId, value, source, id, tcs);

        // Cancellation can prevent a request from entering the queue. Once accepted, however,
        // the write may have a physical side effect and cannot safely be reported as cancelled
        // while it continues in the background. After enqueue, await the definitive receipt.
        try
        {
            await _writeQueue.Writer.WriteAsync(request, ct);
        }
        catch (OperationCanceledException)
        {
            return new WriteResult(false, WriteFailureReason.Cancelled, id, "写入在进入队列前已取消");
        }
        catch (Exception ex)
        {
            return new WriteResult(false, WriteFailureReason.DriverError, id, ex.Message);
        }

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new WriteResult(false, WriteFailureReason.DriverError, id, ex.Message);
        }
    }

    private static bool TryValidateWriteValue(VariableMeta meta, object? value, out WriteFailureReason reason, out string message)
    {
        reason = WriteFailureReason.InvalidValue;
        message = $"变量 {meta.VarId} 的值与数据类型 {meta.DataType} 不匹配";

        var type = meta.DataType.Trim().ToLowerInvariant();
        if (type == "bool")
        {
            if (value is bool) return true;
            return false;
        }
        if (type == "string")
        {
            if (value is string) return true;
            return false;
        }

        if (type is not ("byte" or "int" or "dint" or "float" or "double" or "real"))
            return false;

        if (!TryGetNumericValue(value, out var number) || !double.IsFinite(number))
            return false;

        double? typeMin = type switch
        {
            "byte" => byte.MinValue,
            "int" => short.MinValue,
            "dint" => int.MinValue,
            _ => null
        };
        double? typeMax = type switch
        {
            "byte" => byte.MaxValue,
            "int" => short.MaxValue,
            "dint" => int.MaxValue,
            _ => null
        };

        if (type is "byte" or "int" or "dint" && Math.Truncate(number) != number)
        {
            message = $"变量 {meta.VarId} 的数据类型 {meta.DataType} 需要整数";
            return false;
        }

        if ((typeMin.HasValue && number < typeMin.Value) || (typeMax.HasValue && number > typeMax.Value) ||
            (meta.Min.HasValue && number < meta.Min.Value) || (meta.Max.HasValue && number > meta.Max.Value))
        {
            reason = WriteFailureReason.ValueOutOfRange;
            message = $"变量 {meta.VarId} 的值 {number} 超出允许范围";
            return false;
        }

        return true;
    }

    private static bool TryGetNumericValue(object? value, out double number)
    {
        switch (value)
        {
            case byte v: number = v; return true;
            case sbyte v: number = v; return true;
            case short v: number = v; return true;
            case ushort v: number = v; return true;
            case int v: number = v; return true;
            case uint v: number = v; return true;
            case long v: number = v; return true;
            case ulong v: number = v; return true;
            case float v: number = v; return true;
            case double v: number = v; return true;
            case decimal v: number = (double)v; return true;
            default: number = 0; return false;
        }
    }

    /// <inheritdoc/>
    public VariableQuality GetQuality(string varId)
        => _slots.TryGetValue(varId, out var slot) ? slot.Quality : VariableQuality.BadNotExist;

    /// <summary>
    /// Refresh PLC variables in batches by protocol. Called by the subscription scheduler before emitting a cycle.
    /// Without a configured driver manager, PLC values remain BadComm and are never treated as healthy memory values.
    /// </summary>
    public async Task RefreshFromDriversAsync(IReadOnlyCollection<string> varIds, CancellationToken ct = default)
    {
        if (_driverManager is null) return;

        var groups = varIds
            .Distinct(StringComparer.Ordinal)
            .Select(id => _metas.TryGetValue(id, out var meta) ? (Id: id, Meta: meta) : default)
            .Where(x => x.Meta is not null && x.Meta.Kind == VariableKind.Plc && !string.IsNullOrWhiteSpace(x.Meta.Source?.Address))
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Meta!.Source!.Channel) ? "MOCK" : x.Meta.Source.Channel, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            var entries = group.ToArray();
            try
            {
                var driver = _driverManager.Resolve(group.Key);
                if (driver is null || (driver.Status != LinkStatus.Connected && !await driver.ConnectAsync(ct).ConfigureAwait(false)))
                {
                    foreach (var entry in entries) SetQuality(entry.Id, VariableQuality.BadComm);
                    continue;
                }

                var addresses = entries.Select(x => x.Meta!.Source!.Address!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var values = await driver.ReadBatchAsync(addresses, ct).ConfigureAwait(false);
                foreach (var entry in entries)
                {
                    if (values.TryGetValue(entry.Meta!.Source!.Address!, out var value))
                    {
                        ApplyWrite(entry.Id, value);
                        SetQuality(entry.Id, VariableQuality.Good);
                    }
                    else
                    {
                        SetQuality(entry.Id, VariableQuality.BadComm);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                foreach (var entry in entries) SetQuality(entry.Id, VariableQuality.BadComm);
            }
        }
    }

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
                    await HandleWriteAsync(req).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    req.Tcs.TrySetResult(new WriteResult(false, WriteFailureReason.DriverError, req.RequestId, ex.Message));
                }
            }
        }
    }

    /// <summary>处理单条写请求：直接落值并广播（纯 FIFO 串行，§14.2 默认 Strict）。</summary>
    private async Task HandleWriteAsync(WriteRequest req)
    {
        if (!_slots.ContainsKey(req.VarId))
        {
            req.Tcs.TrySetResult(new WriteResult(false, WriteFailureReason.NotExist, req.RequestId, $"变量不存在: {req.VarId}"));
            return;
        }
        if (_metas.TryGetValue(req.VarId, out var meta) && meta.IsReadOnly)
        {
            req.Tcs.TrySetResult(new WriteResult(false, WriteFailureReason.ReadOnly, req.RequestId, $"变量只读: {req.VarId}"));
            return;
        }
        if (_metas.TryGetValue(req.VarId, out meta) && meta.Kind == VariableKind.Plc)
        {
            if (_driverManager is null || string.IsNullOrWhiteSpace(meta.Source?.Address))
            {
                SetQuality(req.VarId, VariableQuality.BadComm);
                req.Tcs.TrySetResult(new WriteResult(false, WriteFailureReason.DriverError, req.RequestId, "PLC 模拟驱动未配置或变量地址为空"));
                return;
            }

            try
            {
                var protocol = string.IsNullOrWhiteSpace(meta.Source.Channel) ? "MOCK" : meta.Source.Channel;
                var driver = _driverManager.Resolve(protocol);
                if (driver is null || (driver.Status != LinkStatus.Connected && !await driver.ConnectAsync().ConfigureAwait(false)))
                {
                    SetQuality(req.VarId, VariableQuality.BadComm);
                    req.Tcs.TrySetResult(new WriteResult(false, WriteFailureReason.DriverError, req.RequestId, "PLC 驱动连接失败"));
                    return;
                }

                if (!await driver.WriteAsync(meta.Source.Address, req.Value).ConfigureAwait(false))
                {
                    SetQuality(req.VarId, VariableQuality.BadComm);
                    req.Tcs.TrySetResult(new WriteResult(false, WriteFailureReason.DriverError, req.RequestId, "PLC 驱动拒绝写入"));
                    return;
                }

                ApplyWrite(req.VarId, req.Value);
                SetQuality(req.VarId, VariableQuality.Good);
                req.Tcs.TrySetResult(new WriteResult(true, WriteFailureReason.None, req.RequestId, "模拟 PLC 写入成功"));
                return;
            }
            catch (Exception ex)
            {
                SetQuality(req.VarId, VariableQuality.BadComm);
                req.Tcs.TrySetResult(new WriteResult(false, WriteFailureReason.DriverError, req.RequestId, ex.Message));
                return;
            }
        }

        ApplyWrite(req.VarId, req.Value);
        req.Tcs.TrySetResult(new WriteResult(true, WriteFailureReason.None, req.RequestId, "写入成功"));
    }

    private void ApplyWrite(string varId, object? value)
    {
        if (!_slots.TryGetValue(varId, out var slot)) return;
        lock (slot.Sync)
        {
            slot.Value = value;
            // 不广播：通知统一由订阅层按 CycleMs 轮询送出（UI 唯一出口）。
        }
    }

    public async ValueTask DisposeAsync()
    {
        _writeQueue.Writer.TryComplete();
        await _writeLoop;
    }
}