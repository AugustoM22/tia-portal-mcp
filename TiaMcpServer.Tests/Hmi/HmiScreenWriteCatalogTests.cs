using System.Text.Json;
using TiaMcpServer.Hmi;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

/// <summary>Validation, worker payload and planned effects of the screen, screen-item and connection operations.</summary>
public class HmiScreenWriteCatalogTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static HmiWriteOperationRequest Item(string operation, Action<HmiWriteOperationRequest>? edit = null)
    {
        var r = new HmiWriteOperationRequest { OperationId = "one", Operation = operation };
        if (operation.EndsWith("_connection", StringComparison.Ordinal)) { r.Connection = "HMI_Conn"; if (operation.StartsWith("create", StringComparison.Ordinal)) r.PartnerDevice = "PLC_3"; }
        if (operation.Contains("screen", StringComparison.Ordinal)) r.ScreenName = "Producao";
        if (operation.EndsWith("_screen_item", StringComparison.Ordinal)) { r.ItemName = "btnStart"; if (operation.StartsWith("create", StringComparison.Ordinal)) r.ItemType = "HmiButton"; }
        edit?.Invoke(r);
        return r;
    }

    [Theory]
    [InlineData("create_connection")]
    [InlineData("delete_connection")]
    [InlineData("create_screen")]
    [InlineData("delete_screen")]
    [InlineData("create_screen_item")]
    [InlineData("delete_screen_item")]
    public void MinimalRequests_AreValid(string operation)
        => Assert.True(HmiWriteCatalog.ValidateWrite([Item(operation)]).IsValid);

    [Fact]
    public void FullScreenItem_IsValid_AndMapsToWorkerRequest()
    {
        var r = Item("create_screen_item", i =>
        {
            i.Properties = Json("""{"Left":20,"Top":40,"Text":{"en-US":"Start"},"BackColor":"#2E7D32","Font.Size":20,"Visible":true}""");
            i.Dynamizations = [new() { Property = "ProcessValue", Tag = "Paletes", ReadOnly = true }];
            i.Events = [new() { Event = "Tapped", Script = "HMIRuntime.UI.SysFct.ChangeScreen('Receitas', null);" }];
        });
        Assert.True(HmiWriteCatalog.ValidateWrite([r]).IsValid);
        var worker = HmiWriteCatalog.ToWorkerRequest(r, "HMI_RT_1");
        Assert.Equal("HMI_RT_1", worker.HmiName);
        Assert.Equal("HmiButton", worker.ItemType);
        Assert.Equal(20, JsonDocument.Parse(worker.PropertiesJson!).RootElement.GetProperty("Font.Size").GetInt32());
        Assert.Equal("Paletes", Assert.Single(worker.Dynamizations!).Tag);
        Assert.True(worker.Dynamizations![0].ReadOnly);
        Assert.Equal("Tapped", Assert.Single(worker.Events!).Event);
    }

    [Theory]
    [InlineData("""{"itemType":"HmiWidgetX"}""")]
    [InlineData("""{"properties":[1]}""")]
    [InlineData("""{"properties":{}}""")]
    [InlineData("""{"properties":{"Name":"x"}}""")]
    [InlineData("""{"properties":{"Font..Size":1}}""")]
    [InlineData("""{"properties":{"Left":[1]}}""")]
    [InlineData("""{"properties":{"Text":{"en-US":1}}}""")]
    [InlineData("""{"dynamizations":[]}""")]
    [InlineData("""{"dynamizations":[{"property":"Process Value","tag":"T"}]}""")]
    [InlineData("""{"dynamizations":[{"property":"ProcessValue","tag":" "}]}""")]
    [InlineData("""{"dynamizations":[{"property":"ProcessValue","tag":"A"},{"property":"ProcessValue","tag":"B"}]}""")]
    [InlineData("""{"events":[{"event":"Tapped"}]}""")]
    [InlineData("""{"events":[{"event":"Tapped","script":""},{"event":"tapped","script":""}]}""")]
    public void InvalidScreenItemFields_AreRejected(string patch)
    {
        var r = Item("create_screen_item");
        var p = JsonDocument.Parse(patch).RootElement;
        if (p.TryGetProperty("itemType", out var t)) r.ItemType = t.GetString();
        if (p.TryGetProperty("properties", out var props)) r.Properties = props.Clone();
        if (p.TryGetProperty("dynamizations", out var d)) r.Dynamizations = d.Deserialize<List<HmiDynamizationRequest>>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (p.TryGetProperty("events", out var e)) r.Events = e.Deserialize<List<HmiEventRequest>>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(HmiWriteCatalog.ValidateWrite([r]).IsValid, patch);
    }

    [Fact]
    public void UpdatesWithoutChanges_AreRejected()
    {
        Assert.False(HmiWriteCatalog.ValidateWrite([Item("update_screen")]).IsValid);
        Assert.False(HmiWriteCatalog.ValidateWrite([Item("update_screen_item")]).IsValid);
        Assert.True(HmiWriteCatalog.ValidateWrite([Item("update_screen", i => i.StartScreen = true)]).IsValid);
        Assert.False(HmiWriteCatalog.ValidateWrite([Item("update_screen", i => i.StartScreen = false)]).IsValid);
        Assert.True(HmiWriteCatalog.ValidateWrite([Item("update_screen_item", i => i.NewName = "btnGo")]).IsValid);
    }

    [Fact]
    public void ConnectionCreate_RequiresPartner_AndValidName()
    {
        Assert.False(HmiWriteCatalog.ValidateWrite([Item("create_connection", i => i.PartnerDevice = null)]).IsValid);
        Assert.False(HmiWriteCatalog.ValidateWrite([Item("create_connection", i => i.Connection = "bad.name")]).IsValid);
        Assert.False(HmiWriteCatalog.ValidateWrite([Item("delete_connection", i => i.PartnerDevice = "PLC_3")]).IsValid);
    }

    [Fact]
    public void Effects_NameTargets_AndOrderItemsAfterTheirScreen()
    {
        var state = HmiWorkingState.Create(new HmiEvidence { HmiName = "HMI_RT_1", Complete = true });
        var screen = Item("create_screen", i => { i.OperationId = "s"; i.StartScreen = true; });
        var item = Item("create_screen_item", i => { i.OperationId = "b"; i.Properties = Json("""{"Left":5}"""); i.Dynamizations = [new() { Property = "ProcessValue", Tag = "T" }]; });
        var first = state.Resolve(screen);
        Assert.Null(first.Error);
        Assert.Equal("Screen", first.Effect!.Target.Kind);
        Assert.Contains(first.Effect.Changes, c => c.Field == "startScreen");
        state.Apply(screen);
        var second = state.Resolve(item);
        Assert.Null(second.Error);
        Assert.Equal("ScreenItem", second.Effect!.Target.Kind);
        Assert.Equal("Producao", second.Effect.Target.TableName);
        Assert.Equal("s", second.Effect.DependsOn);
        Assert.Contains(second.Effect.Changes, c => c.Field == "properties.Left" && c.Requested == "5");
        Assert.Contains(second.Effect.Changes, c => c.Field == "dynamization[ProcessValue]" && c.Requested == "T");
        Assert.Empty(second.Guards);
    }

    [Fact]
    public void Verification_KeysSeparateScreensItemsAndConnections()
    {
        var state = HmiWorkingState.Create(new HmiEvidence { HmiName = "HMI_RT_1", Complete = true });
        var ops = new[] { Item("create_screen", i => i.OperationId = "a"), Item("create_screen_item", i => i.OperationId = "b"), Item("create_connection", i => i.OperationId = "c") };
        var effects = new Dictionary<string, HmiWriteEffect>();
        foreach (var op in ops) { effects[op.OperationId] = state.Resolve(op).Effect!; state.Apply(op); }
        var policy = new HmiVerificationPolicy(effects);
        var keys = ops.SelectMany(o => policy.ObjectKeys(o, false)).ToArray();
        Assert.Equal(3, keys.Distinct(StringComparer.Ordinal).Count());
    }
}
