using System.Text.Json;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>
/// create_type and delete_type through the guarded pipeline against the stateful
/// plc-write-roundtrip FakeWorker (PLC_1 on device Station_1 holds UDT_Settings; "PLC_1" alone is ambiguous, so paths use the device name).
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcTypeCreateDeleteTests
{
    private const string Roundtrip = "plc-write-roundtrip";

    private static PlcOperationRequest CreateType(string id, string typePath, string? content = null)
        => new() { OperationId = id, Operation = "create_type", TypePath = typePath, Content = content };

    private static PlcOperationRequest DeleteType(string id, string typePath)
        => new() { OperationId = id, Operation = "delete_type", TypePath = typePath };

    [Fact]
    public async Task CreateWithoutContent_CreatesAnEmptyTypeAndReadsItBack()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, CreateType("create", "Station_1/Types/UDT_New"));

        Assert.True(response.Success, JsonSerializer.Serialize(response));
        Assert.Equal("applied", response.Phase);
        var check = Assert.Single(response.Verification!.Operations);
        Assert.Equal(("contentHash", true), (check.Check, check.Success));
        var read = await fixture.Client.GetTypeContentAsync("Station_1/Types/UDT_New", format: "source");
        Assert.True(read.Success, read.Error);
        Assert.Contains("TYPE \"UDT_New\"", read.Payload);
    }

    [Fact]
    public async Task CreateWithContent_StoresTheSubmittedDocument()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        const string Source = "TYPE \"UDT_Valve\"\r\nVERSION : 0.1\r\n   STRUCT\r\n      Open : Bool;\r\n   END_STRUCT;\r\n\r\nEND_TYPE\r\n";

        var response = await fixture.RunAsync(false, CreateType("create", "Station_1/Types/UDT_Valve", Source));

        Assert.True(response.Success, JsonSerializer.Serialize(response));
        var read = await fixture.Client.GetTypeContentAsync("Station_1/Types/UDT_Valve", format: "source");
        Assert.Equal(Source, read.Payload);
    }

    [Fact]
    public async Task CreateOfAnExistingType_FailsWithoutReplacingIt()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, CreateType("create", "Station_1/Types/UDT_Settings"));

        Assert.False(response.Success);
        var failure = Assert.Single(response.Batch!.Operations).Failure!;
        Assert.Equal(WorkerFailureCategories.ValidationError, failure.Category);
        Assert.Contains("already exists", failure.Message);
    }

    [Fact]
    public async Task CreateWithAShortPath_IsRefused()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, CreateType("create", "Station_1/UDT_Short"));

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.ValidationError, Assert.Single(response.Batch!.Operations).Failure!.Category);
    }

    [Fact]
    public async Task DryRunCreate_PreviewsTheNameWithoutWriting()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(true, CreateType("create", "Station_1/Types/UDT_New"));

        Assert.Equal("preview", response.Phase);
        Assert.DoesNotContain("create_type", requests.Methods());
        var effect = Assert.Single(response.Effects!).Effect!;
        Assert.Equal("Type", effect.Target.Kind);
        Assert.Equal("UDT_New", effect.Target.Name);
    }

    [Fact]
    public async Task DeleteOfAnExistingType_FiresTheInfoGuardAndRemovesIt()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, DeleteType("delete", "Station_1/Types/UDT_Settings"));

        Assert.True(response.Success, JsonSerializer.Serialize(response));
        Assert.Contains(response.Guards, g => g.Id == PlcGuardDefinitions.DeletesType && g.Severity == "info");
        var read = await fixture.Client.GetTypeContentAsync("Station_1/Types/UDT_Settings", format: "source");
        Assert.False(read.Success);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, read.FailureCategory);
    }

    [Fact]
    public async Task DeleteOfAMissingType_FailsAtPlanningBeforeAnyMutation()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, DeleteType("delete", "Station_1/Types/UDT_Missing"));

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, response.Error!.Category);
        Assert.DoesNotContain("delete_type", requests.Methods());
    }

    [Theory]
    [InlineData("update_type_content")]
    [InlineData("delete_type")]
    public void ATypeCreatedEarlierInTheCall_CannotBeUpdatedOrDeletedInTheSameCall(string operation)
    {
        var later = new PlcOperationRequest { OperationId = "later", Operation = operation, TypePath = "Station_1/Types/UDT_New" };
        if (operation == "update_type_content")
        {
            later.Content = "TYPE \"UDT_New\" END_TYPE";
            later.ExpectedContentHash = "source:sha256:" + new string('0', 64);
        }

        var result = PlcOperationCatalog.ValidateWrite(new[] { CreateType("create", "Station_1/Types/UDT_New"), later });

        Assert.False(result.IsValid);
        Assert.Contains("is created earlier in this call", result.Error);
    }

    [Fact]
    public void CreateTypeTakesNoContentHash()
    {
        var item = CreateType("create", "Station_1/Types/UDT_New");
        item.ExpectedContentHash = "source:sha256:" + new string('0', 64);

        var result = PlcOperationCatalog.ValidateWrite(new[] { item });

        Assert.False(result.IsValid);
        Assert.Contains("'expectedContentHash' is not valid for create_type", result.Error);
    }

    [Fact]
    public void CreateTypeDefaultsToTheSourceFormat()
        => Assert.Equal("source", PlcFormatNames.Normalize("create_type", null));
}
