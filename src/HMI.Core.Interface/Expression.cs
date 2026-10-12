// 表达式引擎契约（§4.4 / §11 IExpressionEngine、ICompiledExpression、IExpressionRuntimeContext）。
namespace HMI.Core.Interface;

/// <summary>已编译表达式（§11 ICompiledExpression），记录依赖变量列表，可重复求值。</summary>
public interface ICompiledExpression
{
    /// <summary>依赖的变量 Id 列表（供订阅管理与增量计算）。</summary>
    IReadOnlyList<string> DependencyVariableIds { get; }
}

/// <summary>表达式运行时上下文（§11 IExpressionRuntimeContext）：在求值时提供变量读取能力。</summary>
public interface IExpressionRuntimeContext
{
    /// <summary>按变量 Id 读取当前值。</summary>
    bool TryGetValue(string varId, out object? value);
}

/// <summary>表达式编译与求值（§11 IExpressionEngine）。仅暴露对齐契约的 Evaluate 单一入口。</summary>
public interface IExpressionEngine
{
    /// <summary>编译表达式为可复用实例，供增量计算与依赖分析。</summary>
    ICompiledExpression Compile(string expression);

    /// <summary>求值布尔结果。支持 `${VarId}` 变量引用、比较（&gt; &lt; &gt;= &lt;= == !=）、逻辑（&amp;&amp; ||）、括号优先级。异常视为 false（§4.4）。</summary>
    bool Evaluate(string expression, object? context);

    /// <summary>基于统计运行时上下文求值。</summary>
    bool Evaluate(string expression, IExpressionRuntimeContext context);
}