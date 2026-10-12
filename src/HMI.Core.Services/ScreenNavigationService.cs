using HMI.Core.Interface;

namespace HMI.Core.Services;

/// <summary>画面栈（§8.1 / §11 IScreenStack）。</summary>
public sealed class InMemoryScreenStack : IScreenStack
{
    private readonly Stack<string> _stack = new();

    /// <inheritdoc/>
    public void Push(string screenId) => _stack.Push(screenId);

    /// <inheritdoc/>
    public string? Pop() => _stack.Count > 0 ? _stack.Pop() : null;

    /// <inheritdoc/>
    public string? Peek() => _stack.Count > 0 ? _stack.Peek() : null;

    /// <inheritdoc/>
    public void Clear() => _stack.Clear();
}

/// <summary>画面导航服务（§8.1）：单画面独占，跳转/返回。供 WPF 宿主订阅画面切换。</summary>
public sealed class ScreenNavigationService : IScreenNavigationService
{
    private readonly IScreenStack _stack;
    private readonly Func<string, Task> _onNavigate;
    private string? _current;

    public ScreenNavigationService(IScreenStack stack, Func<string, Task> onNavigate)
    {
        _stack = stack;
        _onNavigate = onNavigate;
    }

    /// <summary>记录当前画面（宿主启动加载初始画面后调用），供返回回到来源画面（§8.1）。</summary>
    public void SetCurrent(string screenId) => _current = screenId;

    /// <inheritdoc/>
    public async Task NavigateToAsync(string screenId, CancellationToken ct = default)
    {
        // 压来源而非目标：返回时才能回到上一画面（曾误压目标致栈空、返回失效）。
        if (!string.IsNullOrEmpty(_current)) _stack.Push(_current);
        _current = screenId;
        await _onNavigate(screenId);
    }

    /// <inheritdoc/>
    public async Task NavigateBackAsync(CancellationToken ct = default)
    {
        // 返回 = 弹出来源，回到它，并更新当前画面。
        var target = _stack.Pop();
        if (target is null) return; // 栈空不执行（§8.1）
        _current = target;
        await _onNavigate(target);
    }
}