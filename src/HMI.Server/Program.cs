using System.Text.Json;
using System.Threading.Channels;
using HMI.Core.Interface;
using HMI.Core.Model;
using HMI.Server;

var builder = WebApplication.CreateBuilder(args);
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
app.MapGet("/api/screens", (RuntimeProjectService runtime) => Results.Ok(runtime.Project.Screens.Select(screen => new
{
    screen.Id, screen.Title, screen.Background, screen.ScreenType, screen.Width, screen.Height,
    ControlCount = screen.Controls.Count
})));
app.MapGet("/api/screens/{screenId}", (string screenId, RuntimeProjectService runtime) =>
{
    var screen = runtime.Project.Screens.FirstOrDefault(s => string.Equals(s.Id, screenId, StringComparison.Ordinal));
    if (screen is null) return (IResult)Results.NotFound(new { error = "画面不存在", screenId });
    return (IResult)Results.Ok(screen);
});
app.MapGet("/api/variables", (RuntimeProjectService runtime) => Results.Ok(runtime.GetVariableSnapshot()));
app.MapGet("/api/variables/{varId}", (string varId, RuntimeProjectService runtime) =>
{
    var meta = runtime.Project.Variables.FirstOrDefault(v => v.VarId == varId);
    if (meta is null) return Results.NotFound(new { error = "变量不存在", varId });
    runtime.Pool.TryGetValue(varId, out var value);
    return Results.Ok(new { meta.VarId, meta.Name, meta.DataType, meta.Min, meta.Max, Writable = !meta.IsReadOnly, Value = value, Quality = runtime.Pool.GetQuality(varId).ToString() });
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
    var driver = runtime.GetMockDriver(request.Protocol);
    if (driver is null) return (IResult)Results.BadRequest(new { error = "模拟驱动不可用" });
    driver.SetValue(request.Address, NormalizeJsonValue(request.Value));
    return (IResult)Results.Ok(new { ok = true, request.Protocol, request.Address, request.Value });
});

app.Run();

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
