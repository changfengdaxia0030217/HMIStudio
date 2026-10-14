using System.Text.Json.Serialization;

namespace HMI.Core.Model;

/// <summary>
/// 属性值的统一来源契约。判别字段 type 使用稳定的序列化标识；具体字段类型约束由控件属性注册表在设计期执行。
/// 这里只定义数据契约，解析器和设计器 UI 在后续里程碑实现。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StaticPropertySource), "$static")]
[JsonDerivedType(typeof(VariablePropertySource), "$var")]
[JsonDerivedType(typeof(ExpressionPropertySource), "$expr")]
[JsonDerivedType(typeof(ConditionalPropertySource), "$if")]
[JsonDerivedType(typeof(FormulaPropertySource), "$formula")]
[JsonDerivedType(typeof(ComponentPropertySource), "$componentProp")]
[JsonDerivedType(typeof(WidgetPropertySource), "$widgetProp")]
[JsonDerivedType(typeof(RepeatItemPropertySource), "$repeatItem")]
public abstract class PropertySource
{
}

/// <summary>常量/字面值来源。</summary>
public sealed class StaticPropertySource : PropertySource
{
    public object? Value { get; set; }
}

/// <summary>变量值来源，可选数组/集合索引。</summary>
public sealed class VariablePropertySource : PropertySource
{
    public string VarId { get; set; } = string.Empty;
    public int? Index { get; set; }
}

/// <summary>布尔表达式来源，沿用现有表达式语法。</summary>
public sealed class ExpressionPropertySource : PropertySource
{
    public string Expression { get; set; } = string.Empty;
}

/// <summary>条件选择来源。</summary>
public sealed class ConditionalPropertySource : PropertySource
{
    public PropertySource? Condition { get; set; }
    public PropertySource? WhenTrue { get; set; }
    public PropertySource? WhenFalse { get; set; }
}

/// <summary>数值算术来源；表达式求值器将在后续阶段实现并限制可用运算。</summary>
public sealed class FormulaPropertySource : PropertySource
{
    public string Expression { get; set; } = string.Empty;
}

/// <summary>读取父组件或动作传入的组件参数。</summary>
public sealed class ComponentPropertySource : PropertySource
{
    public string PropertyName { get; set; } = string.Empty;
}

/// <summary>读取可复用控件/组件导出的属性。</summary>
public sealed class WidgetPropertySource : PropertySource
{
    public string WidgetId { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
}

/// <summary>读取 Repeater 当前复本的数据项或索引。</summary>
public sealed class RepeatItemPropertySource : PropertySource
{
    public string Path { get; set; } = string.Empty;
    public bool RepeatIndex { get; set; }
}

/// <summary>属性绑定方向。M0 定义契约，运行时双向写回在后续阶段实现。</summary>
public enum PropertyBindingDirection
{
    OneWay = 0,
    TwoWay = 1,
}
