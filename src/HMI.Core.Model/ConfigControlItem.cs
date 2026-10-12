// HMI.Core.Model —— 控件模型与属性绑定（§4 / §7 / §12）。
using HMI.Core.Interface;

namespace HMI.Core.Model;

/// <summary>控件单个属性的绑定信息（§4.2 双模式：静态默认值 或 变量/表达式绑定）。</summary>
public sealed class PropertyBindingInfo
{
    /// <summary>默认静态值。</summary>
    public object? DefaultValue { get; set; }
    /// <summary>是否为绑定模式。</summary>
    public bool IsBound { get; set; }
    /// <summary>绑定表达式或变量 Id（如 `${VarId}`，§4.4）。</summary>
    public string? Binding { get; set; }
    /// <summary>依赖的变量列表（运行时表达式依赖分析结果）。</summary>
    public List<string> Dependencies { get; set; } = new();
    /// <summary>条件映射「值→目标显示」列表：绑定模式下按变量值命中某行则套用该行 Target。<br/>
    /// 未命中且从未命中过 → 用静态默认值；未命中且已命中过 → 保持上一次命中值（§4.2 状态机配色/文字）。</summary>
    public List<ValueMappingItem>? Mapping { get; set; }
}

/// <summary>条件映射条目：变量值 → 该属性的目标显示值。<br/>
/// Target 语义按属性定：文字内容→文字串；背景色/前景色→Hex 色；可见性→"Visible"/"Collapsed"；可控性→"Enabled"/"Disabled"。</summary>
public sealed class ValueMappingItem
{
    /// <summary>触发条件：变量值（字符串；与当前值做数值/布尔归一比较）。</summary>
    public string? Value { get; set; }
    /// <summary>命中后套用的目标显示值（文字串 / Hex 色 / 可见性 / 可控性）。</summary>
    public string? Target { get; set; }
    /// <summary>文本动画专属：命中后目标文本的显示颜色（Hex），留空沿用该控件前景/样式默认色。</summary>
    public string? Color { get; set; }
}

/// <summary>画布控件模型（§12 IConfigControl 的模型承载）。</summary>
public sealed class ConfigControlItem
{
    /// <summary>控件 Id（只读自动生成、全局唯一；动作/引用用它，改名不破坏）。</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>控件名称（可编辑显示标签，区别于唯一 Id；旧工程未填则回退用 Id）。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>控件类型：Text / Button / Table / Trend ...</summary>
    public string ControlType { get; set; } = "Text";
    /// <summary>所在画面坐标与尺寸（仅静态设置，不开放绑定，§4.3）。</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 120;
    public double Height { get; set; } = 32;
    /// <summary>按属性名→绑定信息。</summary>
    public Dictionary<string, PropertyBindingInfo> Properties { get; set; } = new();
    /// <summary>控件事件（Press 按下 / Release 抬起）绑定的动作节点列表（可多条，§4.5 / §6.2）。
    /// 事件键直接映射到可多条动作，运行时逐条执行。</summary>
    public Dictionary<string, List<ActionNode>> EventActions { get; set; } = new();
}