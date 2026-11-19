namespace TiaMcpServer.Contracts.Hmi;

public static class HmiWriteMethods
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        "hmi_create_tag_table",
        "hmi_update_tag_table",
        "hmi_delete_tag_table",
        "hmi_create_tag",
        "hmi_update_tag",
        "hmi_delete_tag",
        "hmi_create_alarm",
        "hmi_update_alarm",
        "hmi_delete_alarm",
        "hmi_create_connection",
        "hmi_delete_connection",
        "hmi_create_screen",
        "hmi_update_screen",
        "hmi_delete_screen",
        "hmi_create_screen_item",
        "hmi_update_screen_item",
        "hmi_delete_screen_item",
    });
}
