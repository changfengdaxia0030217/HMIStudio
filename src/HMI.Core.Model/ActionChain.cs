// HMI.Core.Model —— 动作链模型（§6 / §12）。
using HMI.Core.Interface;

namespace HMI.Core.Model;

/// <summary>动作链中的单个节点（§6.1 / §6.2）。</summary>
public sealed class ActionNode
{
    /// <summary>动作类型标识（内置动作或外挂 DLL 类型名）。</summary>
    public string ActionType { get; set; } = "WriteVariable";
    /// <summary>前置条件表达式；成立才执行该节点（§6.1）。</summary>
    public string? Condition { get; set; }
    /// <summary>失败策略：Stop / Continue（§6.1）。</summary>
    public ActionFailurePolicy FailurePolicy { get; set; } = ActionFailurePolicy.Stop;
    /// <summary>节点独立超时 ms；0 表示不设超时（§6.1）。</summary>
    public int TimeoutMs { get; set; }
    /// <summary>动作参数（写目标变量 / 目标画面 / 延时毫秒等）。</summary>
    public Dictionary<string, object?> Parameters { get; set; } = new();
}

/// <summary>动作链：有序节点列表；按钮事件/周期任务通过 ActionChainId 引用（§6.2）。</summary>
public sealed class ActionChain
{
    /// <summary>动作链 Id。</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>有序动作节点。</summary>
    public List<ActionNode> Nodes { get; set; } = new();
}