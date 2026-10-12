// HMI.Core.Model —— 变量元数据（§3 / §12 / §14.6）。
using HMI.Core.Interface;

namespace HMI.Core.Model;

/// <summary>数据源标记：PLC 变量绑定到哪个协议通道；相机状态变量绑定到相机通道。</summary>
public sealed class VariableSource
{
    /// <summary>来源类型。</summary>
    public VariableKind Kind { get; set; } = VariableKind.Memory;
    /// <summary>PLC：协议标识（S7/EIP/MODBUSTCP）；相机：通道号转字符串。</summary>
    public string Channel { get; set; } = string.Empty;
    /// <summary>PLC：实际地址。</summary>
    public string? Address { get; set; }
}

/// <summary>单个变量的运行时配置元数据（§3 变量系统 / §12 / §14.6 新字段）。</summary>
public sealed class VariableMeta
{
    /// <summary>变量 Id（表达式 `${VarId}` 引用，全局唯一）。</summary>
    public string VarId { get; set; } = string.Empty;
    /// <summary>显示名称（属性面板展示）。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>变量分类：内存 / PLC / 相机状态（§3.8）。</summary>
    public VariableKind Kind { get; set; } = VariableKind.Memory;
    /// <summary>数据类型：byte / int / dint / float / bool / string（§3.8）。</summary>
    public string DataType { get; set; } = "int";
    /// <summary>数据源绑定（PLC 协议通道 / 相机通道）。</summary>
    public VariableSource? Source { get; set; }
    /// <summary>独立刷新周期 ms（§3.1，Load 60 秒）。</summary>
    public int CycleMs { get; set; } = 100;
    /// <summary>永久常驻变量，不参与画面引用计数（§3.3）。</summary>
    public bool IsPermanent { get; set; }
    /// <summary>时间窗防抖 ms，合并高频变化（§14.4）。</summary>
    public int DebounceMs { get; set; }
    /// <summary>写语义：Strict 默认 / Coalesce 可选（§14.2）。</summary>
    public WriteSemantics WriteSemantics { get; set; } = WriteSemantics.Strict;
    /// <summary>只读标记：只读 PLC 变量禁止写并跳过写入重试（§3.8）。</summary>
    public bool IsReadOnly { get; set; }
    /// <summary>初始值（内存变量）。</summary>
    public object? InitialValue { get; set; }
    /// <summary>变量别名（§3.8）。</summary>
    public string? Alias { get; set; }
    /// <summary>备注（属性面板展示）。</summary>
    public string? Comment { get; set; }
}