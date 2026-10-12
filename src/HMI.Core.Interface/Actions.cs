// 动作契约（§6 动作脚本引擎 / §11 IAction、IActionEngine、IActionContextFactory、IActionPluginFactory、IActionPluginManager）。
namespace HMI.Core.Interface;

/// <summary>动作上下文：一次触发所需的输入信息（§5.3 含 InputSource）。也是表达式前置条件的取值上下文（§4.4）。</summary>
public sealed class ActionContext : IExpressionRuntimeContext
{
    /// <summary>触发来源（鼠标 / 触摸）。</summary>
    public required InputSource InputSource { get; init; }

    /// <summary>触发的变量池（动作写变量、表达式读变量的目标）。</summary>
    public required IVariablePool VariablePool { get; init; }

    /// <summary>表达式前置条件从变量池读取 ${varId} 的值，而非返回自身（§4.4）。</summary>
    public bool TryGetValue(string varId, out object? value) => VariablePool.TryGetValue(varId, out value);
}

/// <summary>动作节点统一接口：内置动作与外挂 DLL 均实现（§11 IAction）。</summary>
public interface IAction
{
    /// <summary>动作类型的稳定标识。</summary>
    string Type { get; }

    /// <summary>异步执行单个动作节点。</summary>
    Task ExecuteAsync(ActionContext context, CancellationToken ct);
}

/// <summary>动作上下文创建工厂（§11 IActionContextFactory）。</summary>
public interface IActionContextFactory
{
    /// <summary>从输入来源创建动作上下文。</summary>
    ActionContext Create(InputSource inputSource);
}

/// <summary>动作链执行引擎（§11 IActionEngine）：按序执行节点，超时、失败策略、后台异步。</summary>
public interface IActionEngine
{
    /// <summary>执行指定动作链（通过 ActionChainId 引用，§6.2）。</summary>
    Task ExecuteChainAsync(string actionChainId, ActionContext context, CancellationToken ct = default);
}

/// <summary>外挂动作 DLL 插件工厂（§11 IActionPluginFactory）。</summary>
public interface IActionPluginFactory
{
    /// <summary>按类型名创建外挂动作实现。</summary>
    IAction Create(string typeName);
}

/// <summary>外挂动作插件加载与管理（含签名校验，§11 IActionPluginManager）。</summary>
public interface IActionPluginManager
{
    /// <summary>注册外挂动作插件。</summary>
    void Register(IAction plugin);
    /// <summary>按类型名获取外挂动作插件。</summary>
    IAction? Resolve(string typeName);
}