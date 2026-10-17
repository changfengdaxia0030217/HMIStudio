using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using System.Threading.Channels;
using HMI.Core.Interface;
using HMI.Core.Model;
using HMI.Server;

var currentDirectory = Directory.GetCurrentDirectory();
var projectDirectory = Path.Combine(currentDirectory, "src", "HMI.Server");
var contentRoot = Directory.Exists(projectDirectory) ? projectDirectory : currentDirectory;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = contentRoot });
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("HMISTUDIO_URLS") ?? "http://127.0.0.1:5080");
builder.Services.AddSingleton<RuntimeProjectService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RuntimeProjectService>());
builder.Services.AddCors(options => options.AddPolicy("LocalEditor", policy =>
    policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors("LocalEditor");
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", (RuntimeProjectService runtime) => Results.Ok(new
{
    status = "ok",
    service = "HMIStudio",
    runtime = "running",
    uptimeSeconds = (long)runtime.Uptime.TotalSeconds
}));

app.MapGet("/api/project", (RuntimeProjectService runtime) => Results.Ok(runtime.GetProjectSummary()));

app.MapGet("/api/backup", async (RuntimeProjectService runtime, CancellationToken ct) =>
{
    var temporaryDatabase = Path.Combine(Path.GetTempPath(), $"hmistudio-backup-{Guid.NewGuid():N}.sqlite");
    try
    {
        await using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var projectEntry = archive.CreateEntry($"project/{Path.GetFileName(runtime.ProjectPath)}", CompressionLevel.Optimal);
            await using (var entryStream = projectEntry.Open())
            await using (var projectStream = new FileStream(runtime.ProjectPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, useAsync: true))
                await projectStream.CopyToAsync(entryStream, ct);

            var alarmHistoryIncluded = false;
            if (File.Exists(runtime.AlarmDatabasePath))
            {
                await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = runtime.AlarmDatabasePath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString()))
                await using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = temporaryDatabase,
                    Pooling = false
                }.ToString()))
                {
                    await source.OpenAsync(ct);
                    await destination.OpenAsync(ct);
                    source.BackupDatabase(destination);
                }

                var alarmEntry = archive.CreateEntry("data/alarms.sqlite", CompressionLevel.Optimal);
                await using var entryStream = alarmEntry.Open();
                await using var databaseStream = new FileStream(temporaryDatabase, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                await databaseStream.CopyToAsync(entryStream, ct);
                alarmHistoryIncluded = true;
            }

            var manifest = new
            {
                product = "HMIStudio",
                backupFormatVersion = 1,
                createdAtUtc = DateTimeOffset.UtcNow,
                projectFile = Path.GetFileName(runtime.ProjectPath),
                alarmHistoryIncluded
            };
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using (var manifestStream = manifestEntry.Open())
                await JsonSerializer.SerializeAsync(manifestStream, manifest, HmiJson.Options, ct);
        }

        return Results.File(output.ToArray(), "application/zip", $"HMIStudio-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
    {
        return Results.Problem(title: "创建工程备份失败", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
    finally
    {
        try { if (File.Exists(temporaryDatabase)) File.Delete(temporaryDatabase); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
});
app.MapGet("/api/project/validate", async (RuntimeProjectService runtime, CancellationToken ct) =>
{
    try
    {
        var errors = await runtime.ValidateProjectAsync(ct);
        return Results.Ok(new { ok = errors.Count == 0, errors });
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        return Results.Problem(title: "工程校验失败", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});
app.MapPut("/api/project/start-screen", async (SetStartScreenRequest request, RuntimeProjectService runtime, CancellationToken ct) =>
{
    try
    {
        var error = await runtime.SetStartScreenAsync(request.ScreenId, ct);
        if (error is not null) return (IResult)Results.BadRequest(new { error });
        return (IResult)Results.Ok(new { ok = true, startScreenId = request.ScreenId });
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        return (IResult)Results.Problem(title: "设置启动画面失败", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});
app.MapGet("/api/drivers", (RuntimeProjectService runtime) =>
{
    var drivers = runtime.Project.Variables
        .Where(variable => variable.Kind == VariableKind.Plc &&
                           variable.Source is not null &&
                           !string.IsNullOrWhiteSpace(variable.Source.Channel))
        .GroupBy(variable => variable.Source!.Channel, StringComparer.OrdinalIgnoreCase)
        .Select(group =>
        {
            var variables = group.ToArray();
            var driver = runtime.GetMockDriver(group.Key);
            var variableDetails = variables.Select(variable => new
            {
                variable.VarId,
                variable.Name,
                variable.CycleMs,
                Address = variable.Source?.Address,
                Quality = runtime.Pool.GetQuality(variable.VarId).ToString()
            }).ToArray();
            var goodCount = variableDetails.Count(item => item.Quality == "Good");
            return new
            {
                protocol = group.Key,
                driverType = "模拟 PLC 驱动",
                status = driver?.Status.ToString() ?? "Unavailable",
                variableCount = variables.Length,
                goodVariableCount = goodCount,
                badVariableCount = variables.Length - goodCount,
                variables = variableDetails
            };
        }).ToArray();
    return Results.Ok(drivers);
});
app.MapGet("/api/screens", (RuntimeProjectService runtime) => Results.Ok(runtime.Project.Screens.Select(screen => new
{
    screen.Id, screen.Title, screen.Background, screen.ScreenType, screen.Width, screen.Height,
    ControlCount = screen.Controls.Count
})));
app.MapPost("/api/screens", async (CreateScreenRequest request, RuntimeProjectService runtime, CancellationToken ct) =>
{
    var screen = new HmiScreen
    {
        Id = request.Id,
        Title = request.Title,
        Background = request.Background ?? "#111827",
        ScreenType = ScreenType.Normal,
        Width = request.Width,
        Height = request.Height,
        Controls = new List<ConfigControlItem>()
    };
    try
    {
        var errors = await runtime.CreateScreenAsync(screen, ct);
        if (errors.Count > 0) return (IResult)Results.BadRequest(new { error = string.Join("；", errors), errors });
        return (IResult)Results.Created($"/api/screens/{Uri.EscapeDataString(screen.Id)}", new { ok = true, screenId = screen.Id });
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        return (IResult)Results.Problem(title: "新建画面失败", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});
app.MapDelete("/api/screens/{screenId}", async (string screenId, RuntimeProjectService runtime, CancellationToken ct) =>
{
    try
    {
        var error = await runtime.DeleteScreenAsync(screenId, ct);
        if (error is not null)
        {
            if (error == "画面不存在") return (IResult)Results.NotFound(new { error, screenId });
            return (IResult)Results.BadRequest(new { error, screenId });
        }
        return (IResult)Results.Ok(new { ok = true, screenId, deleted = true });
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        return (IResult)Results.Problem(title: "删除画面失败", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});
app.MapGet("/api/screens/{screenId}", (string screenId, RuntimeProjectService runtime) =>
{
    var screen = runtime.Project.Screens.FirstOrDefault(s => string.Equals(s.Id, screenId, StringComparison.Ordinal));
    if (screen is null) return (IResult)Results.NotFound(new { error = "画面不存在", screenId });
    return (IResult)Results.Ok(screen);
});
app.MapPut("/api/screens/{screenId}", async (string screenId, HmiScreen screen, RuntimeProjectService runtime, CancellationToken ct) =>
{
    try
    {
        var errors = await runtime.SaveScreenAsync(screenId, screen, ct);
        if (errors.Count > 0) return (IResult)Results.BadRequest(new { error = string.Join("；", errors), errors });
        return (IResult)Results.Ok(new { ok = true, screenId, saved = true });
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
    {
        return (IResult)Results.Problem(title: "工程保存失败", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});
app.MapGet("/api/alarms/active", async (RuntimeProjectService runtime, CancellationToken ct) =>
    Results.Ok(await runtime.Alarms.GetActiveAsync(ct)));

app.MapGet("/api/alarms/history", async (int? limit, RuntimeProjectService runtime, CancellationToken ct) =>
    Results.Ok(await runtime.Alarms.GetHistoryAsync(limit ?? 100, ct)));

app.MapPost("/api/alarms/{alarmId}/acknowledge", async (string alarmId, RuntimeProjectService runtime, CancellationToken ct) =>
{
    try
    {
        await runtime.Alarms.AcknowledgeAsync(alarmId, ct);
        return (IResult)Results.Ok(new { ok = true, alarmId, acknowledged = true });
    }
    catch (KeyNotFoundException)
    {
        return (IResult)Results.NotFound(new { error = "活动报警不存在", alarmId });
    }
});

app.MapGet("/api/variables", (RuntimeProjectService runtime) => Results.Ok(runtime.GetVariableSnapshot()));
app.MapGet("/api/variables/{varId}", (string varId, RuntimeProjectService runtime) =>
{
    var meta = runtime.Project.Variables.FirstOrDefault(v => v.VarId == varId);
    if (meta is null) return (IResult)Results.NotFound(new { error = "变量不存在", varId });
    runtime.Pool.TryGetValue(varId, out var value);
    return (IResult)Results.Ok(new { meta.VarId, meta.Name, meta.DataType, meta.Min, meta.Max, Writable = !meta.IsReadOnly, Value = value, Quality = runtime.Pool.GetQuality(varId).ToString() });
});
app.MapPost("/api/variables/{varId}/write", async (string varId, WriteVariableRequest request, RuntimeProjectService runtime, CancellationToken ct) =>
{
    var result = await runtime.Pool.WriteAsync(varId, NormalizeJsonValue(request.Value), ct, WriteSource.Runtime, request.RequestId);
    if (result.Ok) return (IResult)Results.Ok(result);
    return (IResult)Results.BadRequest(result);
});
app.MapGet("/api/runtime/stream", async (HttpContext context, RuntimeProjectService runtime, CancellationToken ct) =>
{
    var requested = context.Request.Query["varIds"].ToString();
    var ids = string.IsNullOrWhiteSpace(requested)
        ? runtime.Project.Variables.Select(v => v.VarId).ToArray()
        : requested.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToArray();
    var unknown = ids.Where(id => !runtime.Pool.Contains(id)).ToArray();
    if (unknown.Length > 0)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = "包含不存在的变量", varIds = unknown }, ct);
        return;
    }

    context.Response.ContentType = "text/event-stream";
    context.Response.Headers["Cache-Control"] = "no-cache";
    context.Response.Headers["Connection"] = "keep-alive";
    var channel = Channel.CreateBounded<VariableValuePackage>(new BoundedChannelOptions(128)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    using var consumer = runtime.Subscriptions.RegisterPollingConsumer(item =>
    {
        if (ids.Contains(item.VarId, StringComparer.Ordinal))
            channel.Writer.TryWrite(item);
    });
    await runtime.Subscriptions.SubscribeAsync(ids, ct);
    try
    {
        await context.Response.WriteAsync("event: ready\ndata: {}\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
        await foreach (var item in channel.Reader.ReadAllAsync(ct))
        {
            var json = JsonSerializer.Serialize(new
            {
                item.VarId, item.Value, Quality = item.Quality.ToString(),
                item.DeviceTimestamp, item.ServerTimestamp
            }, HmiJson.Options);
            await context.Response.WriteAsync($"event: variable\ndata: {json}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    finally
    {
        channel.Writer.TryComplete();
        await runtime.Subscriptions.UnsubscribeAsync(ids, CancellationToken.None);
    }
});

app.MapPost("/api/runtime/mock-values", (MockValueRequest request, RuntimeProjectService runtime) =>
{
    if (string.IsNullOrWhiteSpace(request.Protocol))
        return (IResult)Results.BadRequest(new { error = "协议名称不能为空" });

    if (string.IsNullOrWhiteSpace(request.Address))
        return (IResult)Results.BadRequest(new { error = "地址不能为空" });

    var protocol = request.Protocol.Trim();
    var address = request.Address.Trim();

    var driver = runtime.GetMockDriver(protocol);
    if (driver is null)
        return (IResult)Results.BadRequest(new { error = "模拟驱动不可用", protocol });

    var mappedVariables = runtime.Project.Variables
        .Where(variable => variable.Kind == VariableKind.Plc &&
                           string.Equals(variable.Source?.Channel, protocol, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(variable.Source?.Address, address, StringComparison.OrdinalIgnoreCase))
        .ToArray();
    if (mappedVariables.Length == 0)
        return (IResult)Results.BadRequest(new { error = "地址未映射到任何 PLC 变量", protocol, address });

    var value = NormalizeJsonValue(request.Value);
    foreach (var variable in mappedVariables)
    {
        if (!IsMockValueCompatible(variable.DataType, value, variable.Min, variable.Max, out var validationError))
            return (IResult)Results.BadRequest(new { error = validationError, protocol, address, varId = variable.VarId });
    }

    driver.SetValue(address, value);
    return (IResult)Results.Ok(new { ok = true, protocol, address, request.Value });
});

app.Run();

static bool IsMockValueCompatible(string dataType, object? value, double? min, double? max, out string error)
{
    var type = dataType.Trim().ToLowerInvariant();
    error = $"注入值与变量数据类型 {dataType} 不匹配";

    if (type == "bool") return value is bool;
    if (type == "string") return value is string;

    if (type is not ("byte" or "int" or "dint" or "float" or "double" or "real"))
        return false;

    if (value is not (byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal))
        return false;

    var number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
    if (!double.IsFinite(number)) return false;

    if ((type is "byte" or "int" or "dint") && Math.Truncate(number) != number)
    {
        error = $"数据类型 {dataType} 需要整数";
        return false;
    }

    var typeMin = type switch
    {
        "byte" => (double)byte.MinValue,
        "int" => (double)short.MinValue,
        "dint" => (double)int.MinValue,
        "float" => -(double)float.MaxValue,
        _ => double.NegativeInfinity
    };
    var typeMax = type switch
    {
        "byte" => (double)byte.MaxValue,
        "int" => (double)short.MaxValue,
        "dint" => (double)int.MaxValue,
        "float" => (double)float.MaxValue,
        _ => double.PositiveInfinity
    };

    if (number < typeMin || number > typeMax || (min.HasValue && number < min.Value) || (max.HasValue && number > max.Value))
    {
        error = $"注入值 {number} 超出变量数据类型或配置量程";
        return false;
    }

    return true;
}

static object? NormalizeJsonValue(JsonElement value) => value.ValueKind switch
{
    JsonValueKind.String => value.GetString(),
    JsonValueKind.True => true,
    JsonValueKind.False => false,
    JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
    JsonValueKind.Number when value.TryGetDouble(out var number) => number,
    JsonValueKind.Null or JsonValueKind.Undefined => null,
    _ => value.Clone()
};

public sealed record WriteVariableRequest(JsonElement Value, string? RequestId = null);
public sealed record MockValueRequest(string Protocol, string Address, JsonElement Value);
public sealed record CreateScreenRequest(string Id, string Title, string? Background = null, int Width = 1280, int Height = 720);
public sealed record SetStartScreenRequest(string ScreenId);
