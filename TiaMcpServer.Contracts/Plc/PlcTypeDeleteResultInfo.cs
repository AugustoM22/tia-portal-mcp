namespace TiaMcpServer.Contracts.Plc;

/// <summary>Payload of a completed <c>delete_type</c>.</summary>
public sealed class PlcTypeDeleteResultInfo
{
    public bool Success { get; set; } = true;

    public string Operation { get; set; } = string.Empty;

    public string? ProjectPath { get; set; }

    public string PlcName { get; set; } = string.Empty;

    /// <summary>The caller's path, as submitted.</summary>
    public string TypePath { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;
}
