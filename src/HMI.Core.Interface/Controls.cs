// 控件 / 数据源契约（§4 / §7 / §11 IConfigControl、IControlFactory、IDataSource、IDataAdapter、ITrendChartRuntime）。
namespace HMI.Core.Interface;

/// <summary>可绑定数据源（表格 / 趋势共用，§11 IDataSource）。</summary>
public interface IDataSource
{
    /// <summary>数据变化事件。</summary>
    event Action<object>? DataChanged;
    /// <summary>异步加载数据。</summary>
    Task<IReadOnlyList<object>> LoadAsync(CancellationToken ct = default);
}

/// <summary>组态画布控件统一接口，所有可拖拽控件实现（§11 IConfigControl）。</summary>
public interface IConfigControl
{
    /// <summary>控件 Id。</summary>
    string Id { get; }
    /// <summary>控件类型标识。</summary>
    string ControlType { get; }
}

/// <summary>组态控件运行时/设计时创建与资源释放（§11 IControlFactory）。</summary>
public interface IControlFactory
{
    /// <summary>按类型创建控件实例。</summary>
    object Create(string controlType);
}

/// <summary>表格/趋势绑定数据的数据适配器（§11 IDataAdapter）。</summary>
public interface IDataAdapter
{
    /// <summary>数据源。</summary>
    IDataSource Source { get; }
}

/// <summary>趋势曲线运行时接口（§9.2 / §11 ITrendChartRuntime）。</summary>
public interface ITrendChartRuntime
{
    /// <summary>绑定变量曲线。</summary>
    Task AddSeriesAsync(string varId, CancellationToken ct = default);
}