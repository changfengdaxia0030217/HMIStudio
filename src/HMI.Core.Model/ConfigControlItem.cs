// HMI.Core.Model —— 控件模型与属性绑定（§4 / §7 / §12）。
namespace HMI.Core.Model;

/// <summary>控件单个属性的值来源与映射规则。</summary>
public sealed class PropertyBindingInfo
{
    /// <summary>唯一属性值源；静态值也通过 $static 表达，不再为不同类型维护多套绑定字段。</summary>
    public PropertySource? Source { get; set; } = new StaticPropertySource();
    /// <summary>绑定方向预留：单向读值或双向写回。</summary>
    public PropertyBindingDirection Direction { get; set; } = PropertyBindingDirection.OneWay;
    /// <summary>依赖变量列表，可由设计器/编译校验器生成或刷新。</summary>
    public List<string> Dependencies { get; set; } = new();
    /// <summary>条件映射独立于值来源，供状态颜色/文字等属性使用。</summary>
    public List<ValueMappingItem>? Mapping { get; set; }
}

/// <summary>条件映射条目：变量值 → 目标显示值。</summary>
public sealed class ValueMappingItem
{
    public string? Value { get; set; }
    public string? Target { get; set; }
    public string? Color { get; set; }
}

/// <summary>画布控件模型（§12 IConfigControl 的模型承载）。</summary>
public sealed class ConfigControlItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ControlType { get; set; } = "Text";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 120;
    public double Height { get; set; } = 32;
    public Dictionary<string, PropertyBindingInfo> Properties { get; set; } = new();
    public Dictionary<string, List<ActionNode>> EventActions { get; set; } = new();
}
