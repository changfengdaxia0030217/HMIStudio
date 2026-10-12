// HMI.Core.Interface
// 契约层次（§V1.3 §11）：所有模块与插件仅依赖本层。本层不依赖 Model，任何被接口签名引用的共享枚举/DTO 均置于此处。
namespace HMI.Core.Interface;

/// <summary>变量质量位（§3.7）。</summary>
public enum VariableQuality
{
    /// <summary>正常通讯，数据有效。</summary>
    Good,
    /// <summary>通讯失败。</summary>
    BadComm,
    /// <summary>变量不存在。</summary>
    BadNotExist,
    /// <summary>不确定。</summary>
    Uncertain,
}

/// <summary>写语义（§14.2）。</summary>
public enum WriteSemantics
{
    /// <summary>严格：逐条保序、保重试（默认）。</summary>
    Strict,
    /// <summary>合并：同变量同目标合并为一次写（用于快速连续置数）。</summary>
    Coalesce,
}

/// <summary>变量分类（§3.8）。</summary>
public enum VariableKind
{
    /// <summary>内存变量。</summary>
    Memory,
    /// <summary>PLC 变量。</summary>
    Plc,
    /// <summary>相机状态变量（相机推送，不做重试）。</summary>
    CameraStatus,
}

/// <summary>动作节点失败策略（§6.1）。</summary>
public enum ActionFailurePolicy
{
    /// <summary>整条动作链终止。</summary>
    Stop,
    /// <summary>跳过当前节点继续下一个。</summary>
    Continue,
}

/// <summary>输入来源（§5.3）：鼠标 / 触摸。</summary>
public enum InputSource
{
    Mouse,
    Touch,
}

/// <summary>运行状态（§设计/运行/停止）。</summary>
public enum RuntimeMode
{
    Design,
    Running,
    Stopped,
}

/// <summary>画面类型（§8.1）：普通画面沿用全局基准分辨率；导航/普通同基准；弹出画面用自身尺寸。</summary>
public enum ScreenType
{
    /// <summary>普通画面（主画面，全屏填满）。</summary>
    Normal,
    /// <summary>导航画面（页面跳转承载）。</summary>
    Navigation,
    /// <summary>弹出画面（叠加窗口，独立尺寸）。</summary>
    Popup,
}