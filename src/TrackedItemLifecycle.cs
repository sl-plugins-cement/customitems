using System;

namespace CustomItems;

/// <summary>The observable stages in a tracked serial's lifetime.</summary>
public enum TrackedItemLifecycleStage
{
    Grant,
    Track,
    Transfer,
    Drop,
    Destroy,
    Untrack,
}

/// <summary>
/// One attributable lifecycle transition. The registry records identity and current holder only; callers
/// still own gameplay policy and decide when a transfer, drop, destruction, or retirement occurred.
/// </summary>
public readonly struct TrackedItemLifecycleEvent<TKind>
    where TKind : struct, Enum
{
    internal TrackedItemLifecycleEvent(
        ushort serial,
        TKind kind,
        TrackedItemLifecycleStage stage,
        string source,
        string? fromUserId,
        string? toUserId)
    {
        Serial = serial;
        Kind = kind;
        Stage = stage;
        Source = source;
        FromUserId = fromUserId;
        ToUserId = toUserId;
    }

    public ushort Serial { get; }

    public TKind Kind { get; }

    public TrackedItemLifecycleStage Stage { get; }

    /// <summary>Caller-supplied operation name used to attribute diagnostics to one lifecycle seam.</summary>
    public string Source { get; }

    public string? FromUserId { get; }

    public string? ToUserId { get; }
}
