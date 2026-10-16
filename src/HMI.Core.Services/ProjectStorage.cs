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
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(project);

        // 先序列化，再写入目标文件同目录的临时文件，避免写入中断破坏原工程。
        var json = HmiJson.Serialize((HmiProject)project);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // 临时文件与目标文件位于同一目录，避免跨卷移动。
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            // 清理失败不应覆盖原始保存异常。
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ValidateAsync(string path, CancellationToken ct = default)
    {
        var errors = new List<string>();
        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            var p = HmiJson.Deserialize(json);
            if (p is null)
            {
                errors.Add("工程为空或格式错误");
                return errors;
            }

            if (p.FormatVersion > HmiProject.CurrentFormatVersion)
                errors.Add($"工程格式版本 {p.FormatVersion} 高于当前支持版本 {HmiProject.CurrentFormatVersion}");
            if (p.FormatVersion < 1)
                errors.Add($"工程格式版本无效: {p.FormatVersion}");

            if (p.BaseWidth <= 0 || p.BaseHeight <= 0 || p.BaseWidth > 8192 || p.BaseHeight > 8192)
                errors.Add("工程基准宽高必须在 1 到 8192 之间");

            var screenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var screen in p.Screens)
            {
                if (string.IsNullOrWhiteSpace(screen.Id))
                    errors.Add("画面 ID 不能为空");
                else if (!screenIds.Add(screen.Id))
                    errors.Add($"画面 Id 重复: {screen.Id}");

                if (string.IsNullOrWhiteSpace(screen.Title))
                    errors.Add($"画面 {screen.Id} 名称不能为空");
                if (screen.Width <= 0 || screen.Height <= 0 || screen.Width > 8192 || screen.Height > 8192)
                    errors.Add($"画面 {screen.Id} 的宽高必须在 1 到 8192 之间");

                var controlIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var control in screen.Controls)
                {
                    if (string.IsNullOrWhiteSpace(control.Id))
                        errors.Add($"画面 {screen.Id} 存在控件 ID 为空");
                    else if (!controlIds.Add(control.Id))
                        errors.Add($"画面 {screen.Id} 的控件 Id 重复: {control.Id}");

                    if (!double.IsFinite(control.X) || !double.IsFinite(control.Y) ||
                        !double.IsFinite(control.Width) || !double.IsFinite(control.Height) ||
                        control.Width <= 0 || control.Height <= 0)
                        errors.Add($"画面 {screen.Id} 的控件 {control.Id} 坐标或尺寸无效");
                }
            }

            if (string.IsNullOrWhiteSpace(p.StartScreenId) || !p.Screens.Any(s => s.Id == p.StartScreenId))
                errors.Add("启动画面缺失");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var allowedDataTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "byte", "int", "dint", "float", "double", "real", "bool", "string" };
            foreach (var v in p.Variables)
            {
                if (string.IsNullOrWhiteSpace(v.VarId))
                    errors.Add("变量 Id 不能为空");
                else if (!ids.Add(v.VarId))
                    errors.Add($"变量 Id 重复: {v.VarId}");

                if (string.IsNullOrWhiteSpace(v.Name))
                    errors.Add($"变量 {v.VarId} 的名称不能为空");
                if (string.IsNullOrWhiteSpace(v.DataType) || !allowedDataTypes.Contains(v.DataType.Trim()))
                    errors.Add($"变量 {v.VarId} 的数据类型无效: {v.DataType}");
                if (v.Min.HasValue && !double.IsFinite(v.Min.Value))
                    errors.Add($"变量 {v.VarId} 的最小值必须是有限数值");
                if (v.Max.HasValue && !double.IsFinite(v.Max.Value))
                    errors.Add($"变量 {v.VarId} 的最大值必须是有限数值");
                if (v.Min.HasValue && v.Max.HasValue && v.Min.Value > v.Max.Value)
                    errors.Add($"变量 {v.VarId} 的最小值不能大于最大值");
                if (v.CycleMs <= 0)
                    errors.Add($"变量 {v.VarId} 的刷新周期必须大于 0");
                if (v.DebounceMs < 0)
                    errors.Add($"变量 {v.VarId} 的防抖时间不能小于 0");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        return errors;
    }
}
