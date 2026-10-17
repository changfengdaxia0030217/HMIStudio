using HMI.Core.Interface;
// HMI.Core.Model —— 工程 / 画面 / 运行时状态（§2.3 / §8.1 / §12）。
namespace HMI.Core.Model;

/// <summary>运行时统计信息（§9.3 诊断面板数据源）。</summary>
public sealed class RuntimeStats
{
    /// <summary>变量总数。</summary>
    public int VariableCount { get; set; }
    /// <summary>当前激活画面 Id。</summary>
    public string? ActiveScreenId { get; set; }
    /// <summary>运行累计时长。</summary>
    public TimeSpan Uptime { get; set; }
}

/// <summary>HMI 组态工程（设计时模型与运行时渲染模型的契约承载，§1.2 设计/运行隔离）。</summary>
public sealed class HmiProject
{
    public const int CurrentFormatVersion = 1;
    /// <summary>工程版本标记（§9.4）。</summary>
    public string Version { get; set; } = "1.0.0";
    /// <summary>工程文件格式版本，与面向用户的 Version 分离；当前格式为 1。</summary>
    public int FormatVersion { get; set; } = 1;
    /// <summary>启动画面 Id（§8.1）。</summary>
    public string StartScreenId { get; set; } = string.Empty;
    /// <summary>工程全局基准分辨率宽（普通/导航画面沿用，§8.1）。</summary>
    public int BaseWidth { get; set; } = 1280;
    /// <summary>工程全局基准分辨率高（普通/导航画面沿用，§8.1）。</summary>
    public int BaseHeight { get; set; } = 720;
    /// <summary>全局变量清单。</summary>
    public List<VariableMeta> Variables { get; set; } = new();
    /// <summary>画面清单。</summary>
    public List<HmiScreen> Screens { get; set; } = new();
    /// <summary>报警规则清单；缺省为空以兼容已有工程。</summary>
    public List<AlarmRule> AlarmRules { get; set; } = new();
    /// <summary>动作链清单（多个控件/任务复用，§6.2）。</summary>
    public List<ActionChain> ActionChains { get; set; } = new();
    /// <summary>运行统计。</summary>
    public RuntimeStats Stats { get; set; } = new();
}