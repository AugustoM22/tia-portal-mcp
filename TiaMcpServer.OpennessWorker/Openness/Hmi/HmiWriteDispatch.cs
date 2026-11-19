using Siemens.Engineering;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;
namespace TiaMcpServer.OpennessWorker.Openness.Hmi;
public static class HmiWriteDispatch
{
    public static HmiWriteOutcomeInfo Write(ProjectBase project, string method, HmiWriteRequestInfo write)
    {
        var software = HmiSoftwareLocator.FindUnique(project, write.HmiName).Software;
        try
        {
            return method switch
            {
                "hmi_create_tag_table" => HmiTagTableWriter.Create(software, write),
                "hmi_update_tag_table" => HmiTagTableWriter.Update(software, write),
                "hmi_delete_tag_table" => HmiTagTableWriter.Delete(software, write),
                "hmi_create_tag" => HmiTagWriter.Create(software, write),
                "hmi_update_tag" => HmiTagWriter.Update(software, write),
                "hmi_delete_tag" => HmiTagWriter.Delete(software, write),
                "hmi_create_alarm" => HmiAlarmWriter.Create(software, write),
                "hmi_update_alarm" => HmiAlarmWriter.Update(software, write),
                "hmi_delete_alarm" => HmiAlarmWriter.Delete(software, write),
                "hmi_create_connection" => HmiConnectionWriter.Create(project, software, write),
                "hmi_delete_connection" => HmiConnectionWriter.Delete(software, write),
                "hmi_create_screen" => HmiScreenWriter.CreateScreen(software, write),
                "hmi_update_screen" => HmiScreenWriter.UpdateScreen(software, write),
                "hmi_delete_screen" => HmiScreenWriter.DeleteScreen(software, write),
                "hmi_create_screen_item" => HmiScreenWriter.CreateItem(software, write),
                "hmi_update_screen_item" => HmiScreenWriter.UpdateItem(software, write),
                "hmi_delete_screen_item" => HmiScreenWriter.DeleteItem(software, write),
                _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Unsupported HMI worker method '{method}'."),
            };
        }
        finally { HmiSnapshotStore.Shared.DropFor(project, software); }
    }
}
