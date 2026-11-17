using Siemens.Engineering;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

/// <summary>
/// Proves a PLC data type write actually landed, mirroring <see cref="BlockPostconditionVerifier"/>
/// and reusing its <see cref="BlockPostconditionEvidence"/>.
///
/// <para>
/// Compiling is the point: a type change silently invalidates every block that uses it, and the
/// compiler is the only thing that knows which. That is deliberately cheaper and truer than
/// pre-counting cross-references at preview time.
/// </para>
/// </summary>
internal static class PlcTypePostconditionVerifier
{
    private const string PublicFailureDetail = "verification did not complete.";

    private const string UncertainStateWarning =
        "Project state may have changed; inspect the project before retrying.";

    private const string ResidualNodeWarning =
        "A temporary external source node could not be removed and is still in the project. "
        + "Delete it in TIA Portal under the PLC's external source files.";

    /// <summary>
    /// Compiles the PLC (unless <paramref name="compile"/> is false) and re-exports the type,
    /// recording what it observed. A newly created type is used by nothing, so it cannot break
    /// the PLC: its creation skips the compile, which would also reject a deliberately empty type
    /// ("A structure without components is not allowed") or fail on unrelated existing errors.
    /// </summary>
    public static BlockPostconditionEvidence BuildEvidence(
        ProjectBase project,
        PlcTypeAddress address,
        string format,
        bool projectNodeRemoved,
        bool compile = true,
        IEnumerable<string>? extraWarnings = null)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        if (address is null) throw new ArgumentNullException(nameof(address));

        var warnings = new List<string>();
        if (!projectNodeRemoved)
        {
            warnings.Add(ResidualNodeWarning);
        }

        if (extraWarnings is not null)
        {
            warnings.AddRange(extraWarnings);
        }

        if (compile)
        {
            var compileFailure = CompileFailure(project, address, warnings);
            if (compileFailure is not null)
            {
                return compileFailure;
            }
        }

        return ReExport(project, address, format, warnings);
    }

    private static BlockPostconditionEvidence? CompileFailure(ProjectBase project, PlcTypeAddress address, List<string> warnings)
    {
        try
        {
            var report = CompileChecker.Compile(project, address.PlcName, blockPath: null);
            if (report.TotalErrorCount != 0
                || string.Equals(report.OverallState, "Error", StringComparison.OrdinalIgnoreCase))
            {
                return new BlockPostconditionEvidence(
                    compileSucceeded: false,
                    reExportSucceeded: false,
                    diagnosticMessage: "Compilation reported errors after the PLC data type update.",
                    warnings: warnings);
            }
        }
        catch (Exception exception)
        {
            return new BlockPostconditionEvidence(
                compileSucceeded: false,
                reExportSucceeded: false,
                diagnosticMessage: "Compilation could not complete after the PLC data type update: " + exception.Message,
                warnings: warnings);
        }

        return null;
    }

    private static BlockPostconditionEvidence ReExport(ProjectBase project, PlcTypeAddress address, string format, List<string> warnings)
    {
        try
        {
            var reExported = PlcTypeExporter.Export(project, address.ToDisplayPath(), format);
            var reExportSucceeded = !string.IsNullOrWhiteSpace(reExported);

            return new BlockPostconditionEvidence(
                compileSucceeded: true,
                reExportSucceeded: reExportSucceeded,
                diagnosticMessage: reExportSucceeded
                    ? "Verified."
                    : "Re-export produced an empty document after the PLC data type update.",
                warnings: warnings);
        }
        catch (Exception exception)
        {
            return new BlockPostconditionEvidence(
                compileSucceeded: true,
                reExportSucceeded: false,
                diagnosticMessage: "Re-export could not complete after the PLC data type update: " + exception.Message,
                warnings: warnings);
        }
    }

    /// <summary>
    /// Surfaces the recorded warnings and throws when the evidence does not support success.
    ///
    /// <para>
    /// Each warning is emitted exactly once. On success it goes to stderr, which
    /// <c>Program.HandleLineWithCapturedStderr</c> turns into the response's warnings — the same
    /// route every other worker degradation takes, and the only one available to a response built
    /// by <c>Program.Success</c>. On failure it rides the exception instead, because
    /// <c>WorkerWarningMerger</c> concatenates rather than de-duplicates.
    /// </para>
    /// </summary>
    public static void Verify(BlockPostconditionEvidence evidence)
    {
        if (evidence is null) throw new ArgumentNullException(nameof(evidence));

        if (evidence.CompileSucceeded && evidence.ReExportSucceeded)
        {
            foreach (var warning in evidence.Warnings)
            {
                Console.Error.WriteLine(warning);
            }

            return;
        }

        var failureWarnings = new List<string>(evidence.Warnings) { UncertainStateWarning };

        throw new WorkerOperationException(
            WorkerFailureCategories.PostconditionFailed,
            "PLC data type update postcondition failed: " + PublicFailureDetail,
            failureWarnings);
    }
}
