using System.Text.Json;
using HMI.Core.Interface;
using HMI.Core.Model;

namespace HMI.Core.Services;

/// <summary>动作上下文创建工厂。</summary>
public sealed class ActionContextFactory : IActionContextFactory
{
    private readonly IVariablePool _pool;
    public ActionContextFactory(IVariablePool pool) => _pool = pool;
    public ActionContext Create(InputSource inputSource) => new() { InputSource = inputSource, VariablePool = _pool };
}

/// <summary>动作链执行引擎（§6）：按序执行节点，前置条件、独立超时、失败策略、后台异步。</summary>
public sealed class ActionEngine : IActionEngine
{
    private readonly IReadOnlyDictionary<string, ActionChain> _chains;
    private readonly IExpressionEngine _expression;
    private readonly IScreenNavigationService _navigation;
    private readonly ILogger _log;
    private readonly IActionContextFactory _contextFactory;

    public ActionEngine(
        IEnumerable<ActionChain> chains,
        IExpressionEngine expression,
        IScreenNavigationService navigation,
        IActionContextFactory contextFactory,
        ILogger log)
    {
        _chains = chains.ToDictionary(c => c.Id);
        _expression = expression;
        _navigation = navigation;
        _contextFactory = contextFactory;
        _log = log;
    }

    /// <inheritdoc/>
    public async Task ExecuteChainAsync(string actionChainId, ActionContext context, CancellationToken ct = default)
    {
        if (!_chains.TryGetValue(actionChainId, out var chain))
        {
            await _log.LogAsync("WARN", $"动作链不存在: {actionChainId}");
            return;
        }
        await ExecuteNodesAsync(chain.Nodes, context, ct);
    }

    /// <summary>直接执行一组动作节点（控件内联事件 Press/Release 使用）：按序执行，含前置条件、独立超时、失败策略。</summary>
    public async Task ExecuteNodesAsync(IReadOnlyList<ActionNode> nodes, ActionContext context, CancellationToken ct = default)
    {
        if (nodes is null) return;
        foreach (var node in nodes)
        {
            if (node is null) continue;
            ct.ThrowIfCancellationRequested();

            // 前置条件（§6.1）：成立才执行。
            if (!string.IsNullOrWhiteSpace(node.Condition)
                && !_expression.Evaluate(node.Condition, context))
                continue;

            var nodeCt = ct;
            using var timeoutCts = node.TimeoutMs > 0
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;
            if (timeoutCts is not null) timeoutCts.CancelAfter(node.TimeoutMs);
            if (timeoutCts is not null) nodeCt = timeoutCts.Token;

            try
            {
                await ExecuteNodeAsync(node, context, nodeCt);
            }
            // Caller cancellation is a control-flow signal, not a node failure. A linked
            // timeout token is also cancelled when the caller cancels, so distinguish the
            // caller token first to avoid swallowing shutdown/request cancellation.
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
            {
                await _log.LogAsync("WARN", $"动作节点超时: {node.ActionType}");
                if (node.FailurePolicy == ActionFailurePolicy.Stop) return;
            }
            catch (Exception ex)
            {
                await _log.LogAsync("ERROR", $"动作节点失败: {node.ActionType}", ex);
                if (node.FailurePolicy == ActionFailurePolicy.Stop) return;
            }
        }
    }

    private Task ExecuteNodeAsync(ActionNode node, ActionContext context, CancellationToken ct) => node.ActionType switch
    {
        "WriteVariable" => WriteVariableAsync(node, context, ct),
        "SetBoolean" => SetBooleanAsync(node, context, ct),
        "NavigateScreen" => NavigateScreenAsync(node, ct),
        "NavigateBack" => _navigation.NavigateBackAsync(ct),
        "Delay" => DelayAsync(node, ct),
        _ => _log.LogAsync("WARN", $"未注册动作类型: {node.ActionType}"),
    };

    private async Task WriteVariableAsync(ActionNode node, ActionContext context, CancellationToken ct)
    {
        var varId = GetStr(node.Parameters, "VarId");
        var value = GetVal(node.Parameters, "Value");
        if (string.IsNullOrWhiteSpace(varId)) return;
        var result = await context.VariablePool.WriteAsync(varId, value, ct, WriteSource.Action);
        if (!result.Ok)
            await _log.LogAsync("WARN", $"变量写入失败: {varId}; reason={result.Reason}; requestId={result.RequestId}; {result.Message}");
    }

    private async Task SetBooleanAsync(ActionNode node, ActionContext context, CancellationToken ct)
    {
        var varId = GetStr(node.Parameters, "VarId");
        var enabled = GetBool(node.Parameters, "Enable") ?? false;
        if (string.IsNullOrWhiteSpace(varId)) return;
        var result = await context.VariablePool.WriteAsync(varId, enabled, ct, WriteSource.Action);
        if (!result.Ok)
            await _log.LogAsync("WARN", $"变量写入失败: {varId}; reason={result.Reason}; requestId={result.RequestId}; {result.Message}");
    }

    private Task NavigateScreenAsync(ActionNode node, CancellationToken ct)
    {
        var screenId = GetStr(node.Parameters, "ScreenId");
        if (string.IsNullOrWhiteSpace(screenId)) return Task.CompletedTask;
        return _navigation.NavigateToAsync(screenId, ct);
    }

    private static async Task DelayAsync(ActionNode node, CancellationToken ct)
    {
        var ms = GetInt(node.Parameters, "Ms") ?? 0;
        if (ms > 0) await Task.Delay(ms, ct);
    }

    // ---- 动作参数取值：反序列化后值为 JsonElement，不能 `as string`，须按 JSON 值类型提取（曾致写入被静默跳过）。----
    private static object? GetVal(IReadOnlyDictionary<string, object?> p, string key)
        => p.TryGetValue(key, out var v) ? v is JsonElement je ? ToRuntime(je) : v : null;

    private static string? GetStr(IReadOnlyDictionary<string, object?> p, string key)
    {
        if (!p.TryGetValue(key, out var v) || v is null) return null;
        return v switch
        {
            JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString(),
            string s => s,
            _ => v.ToString(),
        };
    }

    private static bool? GetBool(IReadOnlyDictionary<string, object?> p, string key)
    {
        if (!p.TryGetValue(key, out var v) || v is null) return null;
        return v switch
        {
            JsonElement je => je.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(je.GetString(), out var b) ? b : null,
                _ => null,
            },
            bool b => b,
            _ => null,
        };
    }

    private static int? GetInt(IReadOnlyDictionary<string, object?> p, string key)
    {
        if (!p.TryGetValue(key, out var v) || v is null) return null;
        switch (v)
        {
            case JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var ni):
                return ni;
            case JsonElement je when je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var si):
                return si;
            case int i:
                return i;
            case string s when int.TryParse(s, out var si):
                return si;
            default:
                return null;
        }
    }

    // 把 JsonElement 还原为最小运行形态；数字尽量转为整数（避免 JsonElement 传播）。
    private static object? ToRuntime(JsonElement je) => je.ValueKind switch
    {
        JsonValueKind.Number when je.TryGetInt32(out var i) => i,
        JsonValueKind.String => je.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => je,
    };
}