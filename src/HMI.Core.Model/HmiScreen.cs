// HMI.Core.Model —— 画面模型与共享 JSON 序列化配置。
using System.Text.Json;
using System.Text.Json.Serialization;
using HMI.Core.Interface;

namespace HMI.Core.Model;

/// <summary>组态画面模型（§8.1 单画面独占模式）。</summary>
public sealed class HmiScreen
{
    /// <summary>画面 Id。</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>画面标题。</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>画面背景色（Hex，如 #FFFFFF；空=默认白，Transparent=透明）。</summary>
    public string Background { get; set; } = "#FFFFFF";
    /// <summary>画面类型：普通 / 导航 / 弹出（普通/导航沿用全局基准，弹出用自身尺寸）。</summary>
    public ScreenType ScreenType { get; set; } = ScreenType.Normal;
    /// <summary>弹出画面独立宽度（普通/导航沿用全局基准，不据此渲染）。</summary>
    public int Width { get; set; } = 1280;
    /// <summary>弹出画面独立高度（普通/导航沿用全局基准，不据此渲染）。</summary>
    public int Height { get; set; } = 720;
    /// <summary>画面内控件。</summary>
    public List<ConfigControlItem> Controls { get; set; } = new();
    /// <summary>画面级事件动作（§8.1）：Enter=进入页面 / Leave=离开页面。运行时切换画面时触发，键名复用控件事件约定。</summary>
    public Dictionary<string, List<ActionNode>> ScreenActions { get; set; } = new();

    /// <summary>有效渲染尺寸：弹出画面用自身尺寸，普通/导航沿用工程基准尺寸。</summary>
    public (int W, int H) EffectiveSize(int baseW, int baseH)
        => ScreenType == ScreenType.Popup ? (Width, Height) : (baseW, baseH);
}

/// <summary>工程 JSON 序列化统一入口（System.Text.Json，MIT）。</summary>
public static class HmiJson
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>反序列化工程。</summary>
    public static HmiProject? Deserialize(string json) => JsonSerializer.Deserialize<HmiProject>(json, _options);

    /// <summary>序列化工程。</summary>
    public static string Serialize(HmiProject project) => JsonSerializer.Serialize(project, _options);

    /// <summary>反序列化配置选项（沿用同一策略）。</summary>
    public static JsonSerializerOptions Options => _options;
}