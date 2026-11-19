using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.CommunicationConnections;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;
using static TiaMcpServer.OpennessWorker.Openness.Hmi.HmiWriteRun;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>
/// Integrated HMI connections. Verified on V21: the connection is created on the network side from the device
/// item that hosts the HMI software (local node, partner CPU item, partner node), and then appears in the Unified
/// software with the S7-1200/1500 driver and symbolic partner. The local connection name becomes the HMI connection name.
/// </summary>
public static class HmiConnectionWriter
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    public static HmiWriteOutcomeInfo Create(ProjectBase project, HmiSoftware software, HmiWriteRequestInfo write)
    {
        var name = write.Connection!;
        var devices = ProjectDeviceEnumerator.Enumerate(project).ToList();
        var (hmiDevice, owner) = Host(devices, software);
        var partner = devices.FirstOrDefault(d => Names.Equals(d.Name, write.PartnerDevice))
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Partner device '{write.PartnerDevice}' was not found.");
        var partnerTarget = All(partner.DeviceItems).FirstOrDefault(i => i.GetService<SoftwareContainer>()?.Software is not null)
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, $"Device '{partner.Name}' has no software (CPU) item to connect to.");
        var (localNode, partnerNode) = Nodes(hmiDevice, partner, write.LocalInterface, write.PartnerInterface);
        var management = owner.GetService<CommunicationManagement>()
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "The HMI software item offers no connection management.");
        var connectionType = typeof(Connection).Assembly.GetType("Siemens.Engineering.HW.CommunicationConnections.HmiConnection", true)!;

        Connection? created = null;
        return HmiWriteRun.Create(name, "connection",
            () => created = (Connection)HmiReflection.CreateGeneric(management.Connections, connectionType, localNode, partnerTarget, partnerNode),
            () => created,
            c => HmiReflection.Call(c, "Delete"),
            (c, outcome) =>
            {
                Set(outcome, "LocalConnectionName", () => ((IEngineeringObject)c).SetAttribute("LocalConnectionName", name));
                Set(outcome, "HmiConnection", () =>
                {
                    // Keep the DisabledAtStartup value Openness assigns (true on V21). A connection drawn in the
                    // TIA editor carries the same value and communicates; forcing it to false (as 131b20a4 did)
                    // left the Unified runtime never opening the connection ("Connection lost", bad tag quality).
                    if (!software.Connections.Any(x => Names.Equals(x.Name, name)))
                        throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                            $"The network connection was created but no HMI connection named '{name}' appeared.");
                });
            });
    }

    public static HmiWriteOutcomeInfo Delete(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.Connection! };
        try
        {
            var connection = software.Connections.FirstOrDefault(c => Names.Equals(c.Name, write.Connection))
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Connection '{write.Connection}' was not found.");
            Set(outcome, "Delete", () => HmiReflection.Call(connection, "Delete"));
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    private static (Device Device, DeviceItem Owner) Host(IEnumerable<Device> devices, HmiSoftware software)
    {
        foreach (var device in devices)
            foreach (var item in All(device.DeviceItems))
                if (item.GetService<SoftwareContainer>()?.Software is HmiSoftware hosted && Equals(hosted, software))
                    return (device, item);
        throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, "The device item hosting the HMI software was not found.");
    }

    /// <summary>Explicit interface names win; otherwise the first pair of nodes on the same subnet.</summary>
    private static (Node Local, Node Partner) Nodes(Device hmi, Device partner, string? localName, string? partnerName)
    {
        var locals = NodesOf(hmi).Where(n => localName is null || Names.Equals(n.Name, localName)).ToList();
        var partners = NodesOf(partner).Where(n => partnerName is null || Names.Equals(n.Name, partnerName)).ToList();
        foreach (var local in locals)
            foreach (var remote in partners)
                if (HmiReflection.Property(local, "ConnectedSubnet") is { } subnet && HmiReflection.Property(remote, "ConnectedSubnet") is { } other && Equals(subnet, other))
                    return (local, remote);
        throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
            $"No HMI interface{(localName is null ? "" : $" '{localName}'")} shares a subnet with a '{partner.Name}' interface{(partnerName is null ? "" : $" '{partnerName}'")}. Connect both to the same subnet first.");
    }

    private static IEnumerable<Node> NodesOf(Device device)
    {
        foreach (var item in All(device.DeviceItems))
            if (item.GetService<NetworkInterface>() is { } network)
                foreach (Node node in network.Nodes) yield return node;
    }

    private static IEnumerable<DeviceItem> All(DeviceItemComposition items)
    {
        foreach (DeviceItem item in items)
        {
            yield return item;
            foreach (var child in All(item.DeviceItems)) yield return child;
        }
    }
}
