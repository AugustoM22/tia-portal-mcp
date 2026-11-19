using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Screens;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;
using static TiaMcpServer.OpennessWorker.Openness.Hmi.HmiWriteRun;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>Unified screens and screen items: create, update (attributes, tag dynamizations, event scripts) and delete.</summary>
public static class HmiScreenWriter
{
    private static HmiScreen FindScreen(HmiSoftware software, string name)
        => software.Screens.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Screen '{name}' was not found.");

    private static HmiScreen? TryScreen(HmiSoftware software, string name)
        => software.Screens.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    private static void SetStartScreen(HmiSoftware software, string screen, HmiWriteOutcomeInfo outcome)
        => Set(outcome, "startScreen", () =>
        {
            var settings = HmiReflection.Property(software, "RuntimeSettings") as IEngineeringObject
                ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "The runtime settings could not be read.");
            settings.SetAttribute("StartScreen", screen);
        });

    public static HmiWriteOutcomeInfo CreateScreen(HmiSoftware software, HmiWriteRequestInfo write)
        => Create(write.ScreenName!, "screen",
            () => (HmiScreen)HmiReflection.Call(software.Screens, "Create", write.ScreenName!)!,
            () => TryScreen(software, write.ScreenName!),
            s => HmiReflection.Call(s, "Delete"),
            (s, outcome) =>
            {
                HmiReflection.ApplyProperties(s, write.PropertiesJson, outcome, null);
                HmiReflection.ApplyEvents(s, write.Events, outcome);
                if (write.StartScreen == true) SetStartScreen(software, write.ScreenName!, outcome);
            });

    public static HmiWriteOutcomeInfo UpdateScreen(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.NewName ?? write.ScreenName! };
        try
        {
            IEngineeringObject screen = FindScreen(software, write.ScreenName!);
            if (write.NewName is not null) Set(outcome, "Name", () => screen.SetAttribute("Name", write.NewName));
            HmiReflection.ApplyProperties(screen, write.PropertiesJson, outcome, null);
            HmiReflection.ApplyEvents(screen, write.Events, outcome);
            if (write.StartScreen == true) SetStartScreen(software, write.NewName ?? write.ScreenName!, outcome);
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    public static HmiWriteOutcomeInfo DeleteScreen(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.ScreenName! };
        try { var screen = FindScreen(software, write.ScreenName!); Set(outcome, "Delete", () => HmiReflection.Call(screen, "Delete")); return outcome; }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    public static HmiWriteOutcomeInfo CreateItem(HmiSoftware software, HmiWriteRequestInfo write)
    {
        HmiScreen screen;
        try { screen = FindScreen(software, write.ScreenName!); }
        catch (WorkerOperationException ex) { throw Failure(ex, new HmiWriteOutcomeInfo { ObjectName = write.ItemName!, Cleanup = HmiCleanupStates.NotNeeded }); }
        IEngineeringObject? TryItem()
        {
            try { return HmiReflection.FindItem(screen, write.ScreenName!, write.ItemName!); }
            catch (WorkerOperationException) { return null; }
        }
        return Create(write.ItemName!, "screen item",
            () => (IEngineeringObject)HmiReflection.CreateGeneric(HmiReflection.Property(screen, "ScreenItems")!, HmiReflection.ItemType(screen, write.ItemType!), write.ItemName!),
            TryItem,
            item => HmiReflection.Call(item, "Delete"),
            (item, outcome) => Apply(item, write, outcome));
    }

    public static HmiWriteOutcomeInfo UpdateItem(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.NewName ?? write.ItemName! };
        try
        {
            var item = HmiReflection.FindItem(FindScreen(software, write.ScreenName!), write.ScreenName!, write.ItemName!);
            if (write.NewName is not null) Set(outcome, "Name", () => item.SetAttribute("Name", write.NewName));
            Apply(item, write, outcome);
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    public static HmiWriteOutcomeInfo DeleteItem(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.ItemName! };
        try
        {
            var item = HmiReflection.FindItem(FindScreen(software, write.ScreenName!), write.ScreenName!, write.ItemName!);
            Set(outcome, "Delete", () => HmiReflection.Call(item, "Delete"));
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    private static void Apply(IEngineeringObject item, HmiWriteRequestInfo write, HmiWriteOutcomeInfo outcome)
    {
        HmiReflection.ApplyProperties(item, write.PropertiesJson, outcome, null);
        HmiReflection.ApplyDynamizations(item, write.Dynamizations, outcome);
        HmiReflection.ApplyEvents(item, write.Events, outcome);
    }
}
