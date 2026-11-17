using Siemens.Engineering;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

/// <summary>
/// Deletes one existing PlcType, resolved exactly as <see cref="PlcTypeImporter"/> resolves an
/// update target (missing is target_not_found, an ambiguous legacy name is target_ambiguous).
///
/// <para>
/// No compile runs afterwards: blocks that still use the type stop compiling, and that is the
/// caller's to see through compile_check. The postcondition only proves the type is gone.
/// </para>
/// </summary>
internal static class PlcTypeDeleter
{
    public static PlcTypeDeleteResultInfo Delete(ProjectBase project, string typePath)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        var address = PlcTypeAddress.Parse(typePath);
        var target = PlcTypeTargetResolver.ResolveForImport(project, address);
        var type = target.Type ?? throw new WorkerOperationException(
            WorkerFailureCategories.TargetNotFound,
            $"No PLC data type exists at '{address.ToDisplayPath()}'.");
        var typeName = type.Name;

        type.Delete();

        if (target.Group.Types.Find(typeName) is not null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.PostconditionFailed,
                $"PLC data type '{typeName}' is still present after delete_type.",
                new[] { "Project state may have changed; inspect the project before retrying." });
        }

        return new PlcTypeDeleteResultInfo
        {
            Operation = "delete_type",
            ProjectPath = project.Path?.FullName,
            PlcName = address.PlcName ?? string.Empty,
            TypePath = typePath,
            TypeName = typeName
        };
    }
}
