using HMI.Core.Interface;
using HMI.Core.Model;

namespace HMI.Core.Services;

/// <summary>工程文件异步加载/保存/校验（§11 IProjectStorage）。</summary>
public sealed class ProjectStorage : IProjectStorage
{
    /// <inheritdoc/>
    public async Task<object> LoadAsync(string path, CancellationToken ct = default)
    {
        // ConfigureAwait(false)：库方法不应回呼叫方同步上下文，避免 UI 线程 GetResult 死锁（§1.2 全异步）。
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var project = HmiJson.Deserialize(json)
            ?? throw new InvalidDataException($"工程解析失败: {path}");
        return project;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(string path, object project, CancellationToken ct = default)
    {
        var json = HmiJson.Serialize((HmiProject)project);
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ValidateAsync(string path, CancellationToken ct = default)
    {
        var errors = new List<string>();
        try
        {
            var json = File.ReadAllText(path);
            var p = HmiJson.Deserialize(json);
            if (p is null) { errors.Add("工程为空或格式错误"); return Task.FromResult<IReadOnlyList<string>>(errors); }
            if (string.IsNullOrWhiteSpace(p.StartScreenId) || !p.Screens.Any(s => s.Id == p.StartScreenId))
                errors.Add("启动画面缺失");
            var ids = new HashSet<string>();
            foreach (var v in p.Variables)
                if (!ids.Add(v.VarId)) errors.Add($"变量 Id 重复: {v.VarId}");
        }
        catch (Exception ex) { errors.Add(ex.Message); }
        return Task.FromResult<IReadOnlyList<string>>(errors);
    }
}