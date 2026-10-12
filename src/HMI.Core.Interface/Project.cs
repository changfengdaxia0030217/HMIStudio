// 工程 / 画面导航 / 渲染 契约（§2.3 / §8.1 / §11 IProjectStorage、IScreenNavigationService、IScreenStack、IRuntimeCanvasRenderer）。
namespace HMI.Core.Interface;

/// <summary>工程文件异步加载/保存/校验（§11 IProjectStorage）。</summary>
public interface IProjectStorage
{
    /// <summary>从文件异步加载工程。</summary>
    Task<object> LoadAsync(string path, CancellationToken ct = default);
    /// <summary>异步保存工程。</summary>
    Task SaveAsync(string path, object project, CancellationToken ct = default);
    /// <summary>工程完整性校验。</summary>
    Task<IReadOnlyList<string>> ValidateAsync(string path, CancellationToken ct = default);
}

/// <summary>画面导航（跳转/返回/弹窗，§8.1 / §11 IScreenNavigationService）。</summary>
public interface IScreenNavigationService
{
    /// <summary>跳转至指定画面。</summary>
    Task NavigateToAsync(string screenId, CancellationToken ct = default);
    /// <summary>返回上一画面；栈空不执行。</summary>
    Task NavigateBackAsync(CancellationToken ct = default);
}

/// <summary>画面栈管理（§11 IScreenStack）。</summary>
public interface IScreenStack
{
    /// <summary>压栈。</summary>
    void Push(string screenId);
    /// <summary>弹栈；空返回 null。</summary>
    string? Pop();
    /// <summary>当前栈顶。</summary>
    string? Peek();
    /// <summary>清空。</summary>
    void Clear();
}

/// <summary>运行时画布渲染器，加载画面模型并映射到 WPF 控件树（§11 IRuntimeCanvasRenderer）。</summary>
public interface IRuntimeCanvasRenderer
{
    /// <summary>根据工程模型渲染目标画面，返回 UI 元素根。</summary>
    Task<object> RenderAsync(object screenModel, CancellationToken ct = default);
}