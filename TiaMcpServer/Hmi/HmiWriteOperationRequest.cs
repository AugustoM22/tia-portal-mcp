using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Hmi;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HmiWriteOperationRequest : IOperationBatchItem
{
    public string OperationId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
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
    public JsonElement? ConditionValue { get; set; }
    public string? ScreenName { get; set; }
    public string? ItemName { get; set; }
    public string? ItemType { get; set; }
    public JsonElement? Properties { get; set; }
    public List<HmiDynamizationRequest>? Dynamizations { get; set; }
    public List<HmiEventRequest>? Events { get; set; }
    public string? PartnerDevice { get; set; }
    public string? LocalInterface { get; set; }
    public string? PartnerInterface { get; set; }
    public bool? StartScreen { get; set; }
}

/// <summary>Binds one screen-item property to an HMI tag.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HmiDynamizationRequest
{
    public string? Property { get; set; }
    public string? Tag { get; set; }
    public bool? ReadOnly { get; set; }
}

/// <summary>A JavaScript handler for one event of a screen or screen item.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HmiEventRequest
{
    public string? Event { get; set; }
    public string? Script { get; set; }
    public bool? Async { get; set; }
}
