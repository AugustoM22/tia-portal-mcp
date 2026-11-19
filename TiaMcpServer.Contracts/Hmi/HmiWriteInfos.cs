namespace TiaMcpServer.Contracts.Hmi;

public sealed class HmiWriteRequestInfo
{
    public string? HmiName { get; set; }
    public string? TableName { get; set; }
    public string? GroupPath { get; set; }
    public string? TagName { get; set; }
    public string? NewName { get; set; }
    public string? Connection { get; set; }
    public string? PlcTag { get; set; }
    public string? DataType { get; set; }
    public string? AcquisitionCycle { get; set; }
    public string? AlarmName { get; set; }
    public string? AlarmKind { get; set; }
    public string? TriggerTag { get; set; }
    public string? TriggerMode { get; set; }
    public string? Condition { get; set; }
    public string? AlarmClass { get; set; }
    public List<HmiText>? Comment { get; set; }
    public List<HmiText>? DisplayName { get; set; }
    public List<HmiText>? EventText { get; set; }
    public List<HmiText>? InfoText { get; set; }
    public int? BitNumber { get; set; }
    public int? Priority { get; set; }
    public int? ConditionValueInt32 { get; set; }
    public double? ConditionValueDouble { get; set; }
    // Screens, screen items and connections.
    public string? ScreenName { get; set; }
    public string? ItemName { get; set; }
    public string? ItemType { get; set; }
    /// <summary>Canonical JSON object of attribute paths to values (for example <c>{"Left":10,"Font.Size":20}</c>).</summary>
    public string? PropertiesJson { get; set; }
    public List<HmiDynamizationInfo>? Dynamizations { get; set; }
    public List<HmiEventInfo>? Events { get; set; }
    public string? PartnerDevice { get; set; }
    public string? LocalInterface { get; set; }
    public string? PartnerInterface { get; set; }
    public bool? StartScreen { get; set; }
}

/// <summary>A tag dynamization of one screen-item property.</summary>
public sealed class HmiDynamizationInfo
{
    public string Property { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public bool? ReadOnly { get; set; }
}

/// <summary>A JavaScript event handler of a screen or screen item.</summary>
public sealed class HmiEventInfo
{
    public string Event { get; set; } = string.Empty;
    public string Script { get; set; } = string.Empty;
    public bool? Async { get; set; }
}

public sealed record HmiWriteOutcomeInfo
{
    public string ObjectName { get; set; } = string.Empty;
    public List<string> AppliedProperties { get; set; } = new();
    public string? FailedProperty { get; set; }
    public string? Cleanup { get; set; }
    public string? CleanupMessage { get; set; }
}

public static class HmiCleanupStates
{
    public const string NotNeeded = "notNeeded";
    public const string Deleted = "deleted";
    public const string Failed = "failed";
}
