using System.Text.Json;
using HMI.Core.Interface;
using HMI.Core.Model;
using HMI.Core.Services;

namespace HMI.Server;

public sealed class RuntimeProjectService : IHostedService, IAsyncDisposable
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<RuntimeProjectService> _logger;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private MockPlcDriverManager? _drivers;
    private MemoryVariablePool? _pool;
    private VariableSubscriptionManager? _subscriptions;
    private AlarmManager? _alarms;
    private HmiProject? _project;
    private DateTimeOffset _startedAt;

    public RuntimeProjectService(IWebHostEnvironment environment, ILogger<RuntimeProjectService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public string ProjectPath { get; private set; } = string.Empty;
    public HmiProject Project => _project ?? throw new InvalidOperationException("工程尚未加载");
    public MemoryVariablePool Pool => _pool ?? throw new InvalidOperationException("变量池尚未初始化");
    public VariableSubscriptionManager Subscriptions => _subscriptions ?? throw new InvalidOperationException("订阅服务尚未初始化");
    public IAlarmManager Alarms => _alarms ?? throw new InvalidOperationException("报警服务尚未初始化");
    public TimeSpan Uptime => DateTimeOffset.UtcNow - _startedAt;

    public MockPlcDriver? GetMockDriver(string protocol) => _drivers?.Resolve(protocol) as MockPlcDriver;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _startGate.WaitAsync(cancellationToken);
        try
        {
            if (_pool is not null) return;

            var configuredPath = Environment.GetEnvironmentVariable("HMISTUDIO_PROJECT");
            var dataDirectory = Path.Combine(_environment.ContentRootPath, "data");
            Directory.CreateDirectory(dataDirectory);
            ProjectPath = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(dataDirectory, "demo.hmijob")
                : Path.GetFullPath(configuredPath);

            var storage = new ProjectStorage();
            if (File.Exists(ProjectPath))
            {
                var loaded = await storage.LoadAsync(ProjectPath, cancellationToken);
                _project = loaded as HmiProject ?? throw new InvalidDataException("工程文件不是有效的 HMIStudio 工程");
                var errors = await storage.ValidateAsync(ProjectPath, cancellationToken);
                if (errors.Count > 0)
                    throw new InvalidDataException("工程校验失败: " + string.Join("; ", errors));
            }
            else
            {
                _project = CreateDemoProject();
                await storage.SaveAsync(ProjectPath, _project, cancellationToken);
            }

            _drivers = new MockPlcDriverManager();
            _pool = new MemoryVariablePool(_drivers);
            _pool.Register(_project.Variables);
            _subscriptions = new VariableSubscriptionManager(_pool, ex => _logger.LogWarning(ex, "变量订阅消费者异常"));
            _alarms = await AlarmManager.CreateAsync(_pool, _project.AlarmRules, Path.Combine(dataDirectory, "alarms.sqlite"), cancellationToken, ex => _logger.LogError(ex, "报警事件处理失败"));
            _startedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation("HMIStudio 工程已加载: {ProjectPath}; variables={VariableCount}; screens={ScreenCount}",
                ProjectPath, _project.Variables.Count, _project.Screens.Count);
        }
        finally { _startGate.Release(); }
    }

    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();

    public async Task<IReadOnlyList<string>> ValidateProjectAsync(CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            return await new ProjectStorage().ValidateAsync(ProjectPath, cancellationToken);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public object GetProjectSummary() => new
    {
        Project.Version,
        Project.FormatVersion,
        Project.StartScreenId,
        Project.BaseWidth,
        Project.BaseHeight,
        ProjectPath,
        ScreenCount = Project.Screens.Count,
        VariableCount = Project.Variables.Count,
        RuntimeMode = "Running",
        UptimeSeconds = (long)Uptime.TotalSeconds
    };

    public object GetVariableSnapshot()
    {
        var metadata = (_pool as IVariableMetadataProvider)?.GetMetadataSnapshot()
            ?? Array.Empty<VariableMetadataSnapshot>();
        return metadata.Select(item => new
        {
            item.VarId,
            item.Name,
            item.DataType,
            item.Min,
            item.Max,
            item.Writable,
            item.CycleMs,
            Value = Pool.TryGetValue(item.VarId, out var value) ? value : null,
            Quality = Pool.GetQuality(item.VarId).ToString()
        }).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (_alarms is not null) await _alarms.DisposeAsync();
        if (_subscriptions is not null) await _subscriptions.DisposeAsync();
        if (_pool is not null) await _pool.DisposeAsync();
        if (_drivers is not null) await _drivers.DisposeAsync();
        _alarms = null;
        _subscriptions = null;
        _pool = null;
        _drivers = null;
    }

    public async Task<string?> DeleteScreenAsync(string screenId, CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            var index = Project.Screens.FindIndex(item => string.Equals(item.Id, screenId, StringComparison.Ordinal));
            if (index < 0) return "画面不存在";
            if (Project.Screens.Count <= 1) return "不能删除工程中的最后一个画面";
            if (string.Equals(Project.StartScreenId, screenId, StringComparison.Ordinal))
                return "不能删除启动画面，请先将其他画面设为启动画面";

            var removed = Project.Screens[index];
            Project.Screens.RemoveAt(index);
            try
            {
                await new ProjectStorage().SaveAsync(ProjectPath, Project, cancellationToken);
                _logger.LogInformation("画面已删除: {ScreenId}", screenId);
                return null;
            }
            catch
            {
                Project.Screens.Insert(index, removed);
                throw;
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public async Task<string?> SetStartScreenAsync(string screenId, CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            if (!Project.Screens.Any(item => string.Equals(item.Id, screenId, StringComparison.Ordinal)))
                return "启动画面不存在";
            var previous = Project.StartScreenId;
            Project.StartScreenId = screenId;
            try
            {
                await new ProjectStorage().SaveAsync(ProjectPath, Project, cancellationToken);
                _logger.LogInformation("工程启动画面已更新: {ScreenId}", screenId);
                return null;
            }
            catch
            {
                Project.StartScreenId = previous;
                throw;
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> CreateScreenAsync(HmiScreen screen, CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(screen.Id))
                errors.Add("画面 ID 不能为空");
            else if (screen.Id.Length > 128 || screen.Id.Any(char.IsControl) || screen.Id.Contains('/'))
                errors.Add("画面 ID 长度或字符无效");
            else if (Project.Screens.Any(item => string.Equals(item.Id, screen.Id, StringComparison.Ordinal)))
                errors.Add($"画面 ID 已存在: {screen.Id}");
            if (string.IsNullOrWhiteSpace(screen.Title))
                errors.Add("画面名称不能为空");
            if (screen.Width <= 0 || screen.Height <= 0 || screen.Width > 8192 || screen.Height > 8192)
                errors.Add("画面宽高必须在 1 到 8192 之间");
            if (errors.Count > 0) return errors;

            screen.Background = string.IsNullOrWhiteSpace(screen.Background) ? "#111827" : screen.Background;
            screen.Controls ??= new List<ConfigControlItem>();
            Project.Screens.Add(screen);
            try
            {
                await new ProjectStorage().SaveAsync(ProjectPath, Project, cancellationToken);
                _logger.LogInformation("新画面已创建: {ScreenId}; title={ScreenTitle}", screen.Id, screen.Title);
                return Array.Empty<string>();
            }
            catch
            {
                Project.Screens.Remove(screen);
                throw;
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> SaveScreenAsync(string screenId, HmiScreen screen, CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            var errors = new List<string>();
            if (!string.Equals(screen.Id, screenId, StringComparison.Ordinal))
                errors.Add("画面 ID 与请求地址不一致");
            if (string.IsNullOrWhiteSpace(screen.Title))
                errors.Add("画面名称不能为空");
            if (screen.Width <= 0 || screen.Height <= 0)
                errors.Add("画面宽高必须大于 0");

            var existingIndex = Project.Screens.FindIndex(item => string.Equals(item.Id, screenId, StringComparison.Ordinal));
            if (existingIndex < 0)
                errors.Add("画面不存在");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var control in screen.Controls)
            {
                if (string.IsNullOrWhiteSpace(control.Id))
                    errors.Add("控件 ID 不能为空");
                else if (!ids.Add(control.Id))
                    errors.Add($"控件 ID 重复: {control.Id}");
                if (!double.IsFinite(control.X) || !double.IsFinite(control.Y) ||
                    !double.IsFinite(control.Width) || !double.IsFinite(control.Height))
                    errors.Add($"控件 {control.Id} 的坐标或尺寸不是有效数字");
                else if (control.X < 0 || control.Y < 0 || control.Width <= 0 || control.Height <= 0 ||
                         control.X + control.Width > Project.BaseWidth || control.Y + control.Height > Project.BaseHeight)
                    errors.Add($"控件 {control.Id} 超出画布边界或尺寸无效");

                foreach (var property in control.Properties)
                {
                    if (property.Value?.Source is VariablePropertySource variableSource &&
                        (string.IsNullOrWhiteSpace(variableSource.VarId) ||
                         !Project.Variables.Any(variable => string.Equals(variable.VarId, variableSource.VarId, StringComparison.Ordinal))))
                        errors.Add($"控件 {control.Id} 的属性 {property.Key} 引用了不存在的变量: {variableSource.VarId}");
                }

                if (control.Properties.TryGetValue("writeVarId", out var writeBinding) &&
                    TryGetStaticPropertyString(writeBinding?.Source, out var writeVarId) &&
                    !string.IsNullOrWhiteSpace(writeVarId))
                {
                    var writeVariable = Project.Variables.FirstOrDefault(variable =>
                        string.Equals(variable.VarId, writeVarId, StringComparison.Ordinal));
                    if (writeVariable is null)
                    {
                        errors.Add($"按钮 {control.Id} 写入了不存在的变量: {writeVarId}");
                    }
                    else
                    {
                        if (writeVariable.IsReadOnly)
                            errors.Add($"按钮 {control.Id} 不能写入只读变量: {writeVarId}");

                        if (control.Properties.TryGetValue("writeValue", out var valueBinding) &&
                            TryGetStaticPropertyValue(valueBinding?.Source, out var writeValue) &&
                            !IsCompatibleWriteValue(writeVariable, writeValue))
                            errors.Add($"按钮 {control.Id} 的写入值与变量 {writeVarId} 的数据类型或范围不匹配");
                    }
                }

                if (control.Properties.TryGetValue("navigateToScreenId", out var navigationBinding) &&
                    TryGetStaticPropertyString(navigationBinding?.Source, out var targetScreenId) &&
                    !string.IsNullOrWhiteSpace(targetScreenId) &&
                    !Project.Screens.Any(target => string.Equals(target.Id, targetScreenId, StringComparison.Ordinal)))
                    errors.Add($"控件 {control.Id} 跳转目标不存在: {targetScreenId}");
            }

            if (errors.Count > 0) return errors;
            var previous = Project.Screens[existingIndex];
            Project.Screens[existingIndex] = screen;
            try
            {
                await new ProjectStorage().SaveAsync(ProjectPath, Project, cancellationToken);
                _logger.LogInformation("画面已保存: {ScreenId}; controls={ControlCount}", screenId, screen.Controls.Count);
                return Array.Empty<string>();
            }
            catch
            {
                Project.Screens[existingIndex] = previous;
                throw;
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private static bool TryGetStaticPropertyValue(IPropertySource? source, out object? value)
    {
        value = null;
        if (source is not StaticPropertySource staticSource) return false;
        value = staticSource.Value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetDouble(out var number) => number,
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
            JsonElement element => element.ToString(),
            _ => staticSource.Value
        };
        return true;
    }

    private static bool IsCompatibleWriteValue(VariableMeta variable, object? value)
    {
        var type = variable.DataType.Trim().ToLowerInvariant();
        if (value is string text)
        {
            if (string.Equals(text, "toggle", StringComparison.OrdinalIgnoreCase))
                return type == "bool";
            if (type == "bool")
                return bool.TryParse(text, out _);
            if (type is "byte" or "int" or "dint" or "float" or "double" or "real")
            {
                if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    return false;
                return IsNumericWriteValueInRange(variable, type, parsed);
            }
            return type == "string";
        }

        if (type == "bool") return value is bool;
        if (type == "string") return value is string;
        if (type is not ("byte" or "int" or "dint" or "float" or "double" or "real") ||
            value is not (byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal))
            return false;

        var number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        return IsNumericWriteValueInRange(variable, type, number);
    }

    private static bool IsNumericWriteValueInRange(VariableMeta variable, string type, double value)
    {
        if (!double.IsFinite(value)) return false;
        if ((type is "byte" or "int" or "dint") && Math.Truncate(value) != value) return false;

        var min = type switch { "byte" => 0d, "int" => (double)short.MinValue, "dint" => (double)int.MinValue, _ => double.NegativeInfinity };
        var max = type switch { "byte" => (double)byte.MaxValue, "int" => (double)short.MaxValue, "dint" => (double)int.MaxValue, _ => double.PositiveInfinity };
        return value >= min && value <= max &&
               (!variable.Min.HasValue || value >= variable.Min.Value) &&
               (!variable.Max.HasValue || value <= variable.Max.Value);
    }

    private static bool TryGetStaticPropertyString(IPropertySource? source, out string value)
    {
        value = string.Empty;
        if (source is not StaticPropertySource staticSource) return false;
        switch (staticSource.Value)
        {
            case string text:
                value = text;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } element:
                value = element.GetString() ?? string.Empty;
                return true;
            default:
                return false;
        }
    }

    private static HmiProject CreateDemoProject()
    {
        var project = new HmiProject
        {
            Version = "0.1.0",
            FormatVersion = HmiProject.CurrentFormatVersion,
            StartScreenId = "main",
            BaseWidth = 1280,
            BaseHeight = 720,
            AlarmRules = new List<AlarmRule>
            {
                new() { Id = "target-speed-high", Name = "目标速度过高", VarId = "line.targetSpeed", Operator = ">", Value = 90.0, Enabled = true, Message = "目标速度超过 90 Hz" }
            },
            Variables = new List<VariableMeta>
            {
                new() { VarId = "line.running", Name = "设备运行", Kind = VariableKind.Memory, DataType = "bool", InitialValue = true, CycleMs = 100, IsPermanent = true },
                new() { VarId = "line.speed", Name = "当前速度", Kind = VariableKind.Plc, DataType = "float", InitialValue = 42.5, Min = 0, Max = 100, CycleMs = 200, Source = new VariableSource { Kind = VariableKind.Plc, Channel = "MOCK", Address = "line.speed" } },
                new() { VarId = "line.targetSpeed", Name = "目标速度", Kind = VariableKind.Memory, DataType = "float", InitialValue = 55.0, Min = 0, Max = 100, CycleMs = 100, Source = null },
                new() { VarId = "line.alarm", Name = "报警状态", Kind = VariableKind.Plc, DataType = "bool", InitialValue = false, CycleMs = 250, Source = new VariableSource { Kind = VariableKind.Plc, Channel = "MOCK", Address = "line.alarm" } }
            },
            Screens = new List<HmiScreen>
            {
                new()
                {
                    Id = "main",
                    Title = "设备总览",
                    Background = "#111827",
                    ScreenType = ScreenType.Normal,
                    Controls = new List<ConfigControlItem>
                    {
                        new() { Id = "title", Name = "标题", ControlType = "Text", X = 48, Y = 32, Width = 500, Height = 48,
                            Properties = new Dictionary<string, PropertyBindingInfo>
                            {
                                ["text"] = new() { Source = new StaticPropertySource { Value = "HMIStudio 设备运行总览" } },
                                ["color"] = new() { Source = new StaticPropertySource { Value = "#F9FAFB" } },
                                ["fontSize"] = new() { Source = new StaticPropertySource { Value = 28 } }
                            }
                        },
                        new() { Id = "speed-label", Name = "速度标签", ControlType = "Text", X = 48, Y = 150, Width = 180, Height = 32,
                            Properties = new Dictionary<string, PropertyBindingInfo> { ["text"] = new() { Source = new StaticPropertySource { Value = "当前速度" } }, ["color"] = new() { Source = new StaticPropertySource { Value = "#9CA3AF" } } } },
                        new() { Id = "speed-value", Name = "速度值", ControlType = "Text", X = 48, Y = 190, Width = 300, Height = 72,
                            Properties = new Dictionary<string, PropertyBindingInfo> { ["text"] = new() { Source = new VariablePropertySource { VarId = "line.speed" } }, ["color"] = new() { Source = new StaticPropertySource { Value = "#34D399" } }, ["fontSize"] = new() { Source = new StaticPropertySource { Value = 52 } } } },
                        new() { Id = "alarm-label", Name = "报警标签", ControlType = "Text", X = 420, Y = 150, Width = 180, Height = 32,
                            Properties = new Dictionary<string, PropertyBindingInfo> { ["text"] = new() { Source = new StaticPropertySource { Value = "报警状态" } }, ["color"] = new() { Source = new StaticPropertySource { Value = "#9CA3AF" } } },
                        },
                        new() { Id = "alarm-value", Name = "报警值", ControlType = "Text", X = 420, Y = 190, Width = 300, Height = 72,
                            Properties = new Dictionary<string, PropertyBindingInfo> { ["text"] = new() { Source = new VariablePropertySource { VarId = "line.alarm" } }, ["color"] = new() { Source = new StaticPropertySource { Value = "#FBBF24" } }, ["fontSize"] = new() { Source = new StaticPropertySource { Value = 44 } } } },
                        new() { Id = "run-toggle", Name = "运行切换", ControlType = "Button", X = 48, Y = 330, Width = 180, Height = 48,
                            Properties = new Dictionary<string, PropertyBindingInfo>
                            {
                                ["text"] = new() { Source = new StaticPropertySource { Value = "切换设备运行" } },
                                ["color"] = new() { Source = new StaticPropertySource { Value = "#FFFFFF" } },
                                ["background"] = new() { Source = new StaticPropertySource { Value = "#2563EB" } },
                                ["writeVarId"] = new() { Source = new StaticPropertySource { Value = "line.running" } },
                                ["writeValue"] = new() { Source = new StaticPropertySource { Value = "toggle" } }
                            }
                        },
                        new() { Id = "alarm-toggle", Name = "报警切换", ControlType = "Button", X = 48, Y = 394, Width = 180, Height = 48,
                            Properties = new Dictionary<string, PropertyBindingInfo>
                            {
                                ["text"] = new() { Source = new StaticPropertySource { Value = "触发 / 清除报警" } },
                                ["color"] = new() { Source = new StaticPropertySource { Value = "#FFFFFF" } },
                                ["background"] = new() { Source = new StaticPropertySource { Value = "#B45309" } },
                                ["writeVarId"] = new() { Source = new StaticPropertySource { Value = "line.alarm" } },
                                ["writeValue"] = new() { Source = new StaticPropertySource { Value = "toggle" } }
                            }
                        },
                        new() { Id = "target-speed-label", Name = "目标速度标签", ControlType = "Text", X = 260, Y = 294, Width = 180, Height = 28,
                            Properties = new Dictionary<string, PropertyBindingInfo> { ["text"] = new() { Source = new StaticPropertySource { Value = "目标速度（Hz）" } }, ["color"] = new() { Source = new StaticPropertySource { Value = "#CBD5E1" } } } },
                        new() { Id = "target-speed-input", Name = "目标速度输入", ControlType = "NumericInput", X = 260, Y = 330, Width = 180, Height = 48,
                            Properties = new Dictionary<string, PropertyBindingInfo>
                            {
                                ["text"] = new() { Source = new VariablePropertySource { VarId = "line.targetSpeed" }, Direction = PropertyBindingDirection.TwoWay },
                                ["color"] = new() { Source = new StaticPropertySource { Value = "#38D9A9" } },
                                ["fontSize"] = new() { Source = new StaticPropertySource { Value = 24 } }
                            }
                        },
                        new() { Id = "target-speed-display", Name = "目标速度显示", ControlType = "NumericDisplay", X = 260, Y = 394, Width = 180, Height = 48,
                            Properties = new Dictionary<string, PropertyBindingInfo>
                            {
                                ["text"] = new() { Source = new VariablePropertySource { VarId = "line.targetSpeed" } },
                                ["color"] = new() { Source = new StaticPropertySource { Value = "#93C5FD" } },
                                ["fontSize"] = new() { Source = new StaticPropertySource { Value = 24 } }
                            }
                        },
                        new() { Id = "alarm-lamp", Name = "报警指示灯", ControlType = "Lamp", X = 520, Y = 330, Width = 64, Height = 64,
                            Properties = new Dictionary<string, PropertyBindingInfo>
                            {
                                ["color"] = new() { Source = new VariablePropertySource { VarId = "line.alarm" } }
                            }
                        },
                        new() { Id = "alarm-lamp-label", Name = "报警指示灯标签", ControlType = "Text", X = 600, Y = 346, Width = 180, Height = 32,
                            Properties = new Dictionary<string, PropertyBindingInfo> { ["text"] = new() { Source = new StaticPropertySource { Value = "报警指示灯" } }, ["color"] = new() { Source = new StaticPropertySource { Value = "#CBD5E1" } } } }
                    }
                }
            }
        };
        return project;
    }
}
