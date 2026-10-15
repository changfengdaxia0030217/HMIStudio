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
    private MockPlcDriverManager? _drivers;
    private MemoryVariablePool? _pool;
    private VariableSubscriptionManager? _subscriptions;
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
            _startedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation("HMIStudio 工程已加载: {ProjectPath}; variables={VariableCount}; screens={ScreenCount}",
                ProjectPath, _project.Variables.Count, _project.Screens.Count);
        }
        finally { _startGate.Release(); }
    }

    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();

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
        if (_subscriptions is not null) await _subscriptions.DisposeAsync();
        if (_pool is not null) await _pool.DisposeAsync();
        if (_drivers is not null) await _drivers.DisposeAsync();
        _subscriptions = null;
        _pool = null;
        _drivers = null;
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
                            Properties = new Dictionary<string, PropertyBindingInfo> { ["text"] = new() { Source = new VariablePropertySource { VarId = "line.alarm" } }, ["color"] = new() { Source = new StaticPropertySource { Value = "#FBBF24" } }, ["fontSize"] = new() { Source = new StaticPropertySource { Value = 44 } } } }
                    }
                }
            }
        };
        return project;
    }
}
