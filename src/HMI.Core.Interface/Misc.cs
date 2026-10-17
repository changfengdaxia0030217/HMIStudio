// 其余业务模块契约（§8/§9/§10 / §11 其余接口）。首切片仅声明，业务实现在后续切片实现。
namespace HMI.Core.Interface;

/// <summary>周期任务调度器（§9.1 / §11 IPeriodicTaskScheduler）。</summary>
public interface IPeriodicTaskScheduler
{
    /// <summary>注册周期任务（执行周期 + 动作链 Id）。</summary>
    Task RegisterAsync(string taskId, TimeSpan period, string actionChainId, CancellationToken ct = default);
}

/// <summary>历史数据本地异步存储（§9.2 / §11 IHistoryStorage）。</summary>
public interface IHistoryStorage
{
    /// <summary>写入一条历史采样。</summary>
    Task AppendAsync(string varId, object? value, DateTimeOffset ts, CancellationToken ct = default);
}

/// <summary>配方管理（§10.1 / §11 IRecipeManager）。</summary>
public interface IRecipeManager
{
    /// <summary>异步加载全部配方名。</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);
}

/// <summary>触摸屏虚拟数字键盘服务（§10.2 / §11 IVirtualKeyboardService）。</summary>
public interface IVirtualKeyboardService
{
    /// <summary>弹出数字键盘，返回输入值。</summary>
    Task<decimal?> ShowAsync(CancellationToken ct = default);
}

/// <summary>基础权限管理（§11 IPermissionManager）。</summary>
public interface IPermissionManager
{
    /// <summary>是否有权限。</summary>
    bool HasPermission(string permissionKey);
}

/// <summary>全局异常统一处理（§11 IGlobalExceptionHandler）。</summary>
public interface IGlobalExceptionHandler
{
    /// <summary>捕获并处理未观察异常。</summary>
    void Handle(Exception exception);
}

/// <summary>数据记录服务（§2.1 / §11 IDataLoggingService）。</summary>
public interface IDataLoggingService
{
    /// <summary>记录一条业务数据。</summary>
    Task LogAsync(object record, CancellationToken ct = default);
}

/// <summary>设计/运行/停止状态管理（§2.3 / §11 IRuntimeStateManager）。</summary>
public interface IRuntimeStateManager
{
    /// <summary>当前模式。</summary>
    RuntimeMode Mode { get; }
    /// <summary>切换模式。</summary>
    Task SwitchAsync(RuntimeMode mode, CancellationToken ct = default);
}