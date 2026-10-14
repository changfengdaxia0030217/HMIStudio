namespace HMI.Core.Interface;

/// <summary>写入调用来源，用于后续按来源实施写入门禁。</summary>
public enum WriteSource
{
    Runtime = 0,
    Action = 1,
    Editor = 2,
    Automation = 3,
    System = 4,
}

/// <summary>稳定的写入失败原因码。</summary>
public enum WriteFailureReason
{
    None = 0,
    NotExist = 1,
    ReadOnly = 2,
    InvalidValue = 3,
    ValueOutOfRange = 4,
    DriverError = 5,
    PermissionDenied = 6,
    Cancelled = 7,
}

/// <summary>写入操作结果；RequestId 用于关联异步请求与回执。</summary>
public sealed record WriteResult(bool Ok, WriteFailureReason Reason, string RequestId, string Message)
{
    public static WriteResult Success(string requestId) => new(true, WriteFailureReason.None, requestId, "写入成功");
}
