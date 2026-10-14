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
        if (project.FormatVersion > HmiProject.CurrentFormatVersion)
            throw new InvalidDataException($"工程格式版本 {project.FormatVersion} 高于当前支持版本 {HmiProject.CurrentFormatVersion}，请使用较新版本的 HMIStudio 打开。");
        if (project.FormatVersion < 1)
            throw new InvalidDataException($"工程格式版本无效: {project.FormatVersion}");
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
            if (p.FormatVersion > HmiProject.CurrentFormatVersion) errors.Add($"工程格式版本 {p.FormatVersion} 高于当前支持版本 {HmiProject.CurrentFormatVersion}");
            if (p.FormatVersion < 1) errors.Add($"工程格式版本无效: {p.FormatVersion}");
            if (string.IsNullOrWhiteSpace(p.StartScreenId) || !p.Screens.Any(s => s.Id == p.StartScreenId))
                errors.Add("启动画面缺失");
            var ids = new HashSet<string>();
            foreach (var v in p.Variables)
            {
                if (!ids.Add(v.VarId)) errors.Add($"变量 Id 重复: {v.VarId}");
                if (v.Min.HasValue && v.Max.HasValue && v.Min.Value > v.Max.Value)
                    errors.Add($"变量 {v.VarId} 的最小值不能大于最大值");
                if (v.CycleMs <= 0) errors.Add($"变量 {v.VarId} 的刷新周期必须大于 0");
            }
        }
        catch (Exception ex) { errors.Add(ex.Message); }
        return Task.FromResult<IReadOnlyList<string>>(errors);
    }
}