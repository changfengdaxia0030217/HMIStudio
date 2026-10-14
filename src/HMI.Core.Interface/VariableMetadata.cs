namespace HMI.Core.Interface;

/// <summary>变量元数据快照。与高频值增量分开传输；客户端收到快照时应整体替换旧元数据。</summary>
public sealed record VariableMetadataSnapshot(
    string VarId,
    string Name,
    string DataType,
    double? Min,
    double? Max,
    bool Writable,
    int CycleMs);

/// <summary>提供权威变量元数据快照，与实时值通道解耦。</summary>
public interface IVariableMetadataProvider
{
    IReadOnlyCollection<VariableMetadataSnapshot> GetMetadataSnapshot();
}
