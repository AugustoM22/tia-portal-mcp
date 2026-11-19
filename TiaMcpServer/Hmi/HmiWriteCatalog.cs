using System.Globalization;
using System.Numerics;
using System.Text.Json;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Hmi;

/// <summary>Pure request validation and normalization before any worker activity.</summary>
public static class HmiWriteCatalog
{
    private sealed record Spec(string[] Required, string[] Optional);
    private static Spec Op(string required, string optional = "") =>
        new(required.Split(' ', StringSplitOptions.RemoveEmptyEntries), optional.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static readonly IReadOnlyDictionary<string, Spec> Specs = new Dictionary<string, Spec>(StringComparer.Ordinal)
    {
        ["create_tag_table"] = Op("tableName", "groupPath"),
        ["update_tag_table"] = Op("tableName newName"),
        ["delete_tag_table"] = Op("tableName"),
        ["create_tag"] = Op("tableName tagName", "connection plcTag dataType comment displayName acquisitionCycle"),
        ["update_tag"] = Op("tagName", "newName plcTag connection dataType comment displayName acquisitionCycle"),
        ["delete_tag"] = Op("tagName"),
        ["create_alarm"] = Op("alarmKind alarmName triggerTag", "condition conditionValue alarmClass priority eventText infoText bitNumber triggerMode"),
        ["update_alarm"] = Op("alarmKind alarmName", "newName triggerTag condition conditionValue alarmClass priority eventText infoText bitNumber triggerMode"),
        ["delete_alarm"] = Op("alarmKind alarmName"),
        ["create_connection"] = Op("connection partnerDevice", "localInterface partnerInterface"),
        ["delete_connection"] = Op("connection"),
        ["create_screen"] = Op("screenName", "properties events startScreen"),
        ["update_screen"] = Op("screenName", "newName properties events startScreen"),
        ["delete_screen"] = Op("screenName"),
        ["create_screen_item"] = Op("screenName itemName itemType", "properties dynamizations events"),
        ["update_screen_item"] = Op("screenName itemName", "newName properties dynamizations events"),
        ["delete_screen_item"] = Op("screenName itemName"),
    };

    /// <summary>Screen-item types the worker resolves in the Widgets, Shapes, Controls and Screens namespaces.</summary>
    public static IReadOnlyList<string> ScreenItemTypes { get; } = new[]
    {
        "HmiButton", "HmiText", "HmiIOField", "HmiSymbolicIOField", "HmiToggleSwitch", "HmiCheckBoxGroup", "HmiTextBox",
        "HmiListBox", "HmiBar", "HmiClock", "HmiRectangle", "HmiCircle", "HmiLine", "HmiAlarmControl", "HmiScreenWindow",
    };

    private static readonly (string Name, Func<HmiWriteOperationRequest, object?> Get)[] Fields =
    {
        ("tableName", r => r.TableName),
        ("groupPath", r => r.GroupPath),
        ("tagName", r => r.TagName),
        ("newName", r => r.NewName),
        ("connection", r => r.Connection),
        ("plcTag", r => r.PlcTag),
        ("dataType", r => r.DataType),
        ("acquisitionCycle", r => r.AcquisitionCycle),
        ("alarmName", r => r.AlarmName),
        ("alarmKind", r => r.AlarmKind),
        ("triggerTag", r => r.TriggerTag),
        ("triggerMode", r => r.TriggerMode),
        ("condition", r => r.Condition),
        ("alarmClass", r => r.AlarmClass),
        ("comment", r => r.Comment),
        ("displayName", r => r.DisplayName),
        ("eventText", r => r.EventText),
        ("infoText", r => r.InfoText),
        ("bitNumber", r => r.BitNumber),
        ("priority", r => r.Priority),
        ("conditionValue", r => r.ConditionValue),
        ("screenName", r => r.ScreenName),
        ("itemName", r => r.ItemName),
        ("itemType", r => r.ItemType),
        ("properties", r => r.Properties),
        ("dynamizations", r => r.Dynamizations),
        ("events", r => r.Events),
        ("partnerDevice", r => r.PartnerDevice),
        ("localInterface", r => r.LocalInterface),
        ("partnerInterface", r => r.PartnerInterface),
        ("startScreen", r => r.StartScreen),
    };

    public static IReadOnlyList<string> OperationNames { get; } = Specs.Keys.ToArray();

    public static OperationValidation ValidateWrite(IReadOnlyList<HmiWriteOperationRequest>? operations)
    {
        if (OperationBatchRules.CheckCount(operations?.Count ?? 0) is { } countError)
            return OperationValidation.Invalid(WorkerFailureCategories.ValidationError, countError);
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in operations!)
        {
            if (OperationBatchRules.CheckItem(r, ids) is { } itemError) { errors.Add(itemError); continue; }
            void Error(string message) => errors.Add($"Operation '{r.Operation}' (operationId '{r.OperationId}'): {message}");
            if (r.Operation is null || !Specs.TryGetValue(r.Operation, out var spec))
            { Error("Unknown operation."); continue; }
            foreach (var (name, get) in Fields)
            {
                var value = get(r);
                if (value is not null && !spec.Required.Contains(name) && !spec.Optional.Contains(name))
                    Error($"'{name}' is not valid for this operation.");
                if (spec.Required.Contains(name) && (value is null || value is string s && string.IsNullOrWhiteSpace(s)))
                    Error($"'{name}' is required.");
                if (value is string str && string.IsNullOrWhiteSpace(str))
                    Error($"'{name}' must be nonblank when supplied.");
            }
            if (r.HmiName is not null && string.IsNullOrWhiteSpace(r.HmiName)) Error("'hmiName' must be nonblank.");
            if (r.GroupPath is not null && r.GroupPath.Split('/').Any(string.IsNullOrWhiteSpace))
                Error("'groupPath' must have no empty segment.");
            if (r.NewName is not null && !ValidName(r.NewName)) Error("'newName' is invalid.");
            var createdName = r.Operation switch
            {
                "create_tag_table" => r.TableName, "create_tag" => r.TagName, "create_alarm" => r.AlarmName,
                "create_connection" => r.Connection, "create_screen" => r.ScreenName, "create_screen_item" => r.ItemName, _ => null,
            };
            if (createdName is not null && !ValidName(createdName)) Error("New object name is invalid.");
            ValidateScreenFields(r, Error);
            if (r.Operation is "create_tag" or "update_tag")
            {
                if (r.Connection is not null && r.PlcTag is null) Error("'connection' requires 'plcTag'.");
                if (r.Operation == "create_tag" && r.PlcTag is null && r.DataType is null) Error("Create requires plcTag or dataType.");
            }
            if (r.AlarmKind is not null && r.AlarmKind is not ("discrete" or "analog")) Error("'alarmKind' must be discrete or analog.");
            if (r.AlarmKind == "analog")
            {
                if (r.BitNumber is not null || r.TriggerMode is not null) Error("Analog alarms cannot specify bitNumber or triggerMode.");
                if (r.Operation == "create_alarm" && (r.Condition is null || r.ConditionValue is null))
                    Error("Analog create requires condition and conditionValue.");
            }
            if (r.AlarmKind == "discrete" && (r.Condition is not null || r.ConditionValue is not null))
                Error("Discrete alarms cannot specify condition or conditionValue.");
            if (r.AcquisitionCycle is not null && r.AcquisitionCycle is not ("T100ms" or "T250ms" or "T500ms" or "T1s" or "T2s" or "T5s" or "T10s"))
                Error("'acquisitionCycle' is invalid.");
            if (r.TriggerMode is not null && r.TriggerMode is not ("OnRisingEdge" or "OnFallingEdge")) Error("'triggerMode' is invalid.");
            if (r.Condition is not null && r.Condition is not ("LowerLimit" or "UpperLimit" or "Equal" or "NotEqual" or "LowerLimitOrEqual" or "UpperLimitOrEqual"))
                Error("'condition' is invalid.");
            if (r.Priority is < 0 or > 255) Error("'priority' must be 0 to 255.");
            if (r.BitNumber is < 0) Error("'bitNumber' must be nonnegative.");
            if (r.ConditionValue is { } number && !TryNumber(number, out _)) Error("'conditionValue' must be a finite JSON number.");
            foreach (var (name, texts) in new[] { ("comment", r.Comment), ("displayName", r.DisplayName), ("eventText", r.EventText), ("infoText", r.InfoText) })
            {
                if (texts is null) continue;
                if (texts.Count == 0) Error($"'{name}' must not be empty.");
                var cultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var text in texts)
                {
                    if (text is null || text.Text is null || CultureNameRules.CanonicalName(text.Culture) is not { } culture)
                    { Error($"'{name}' requires culture and text."); continue; }
                    if (!cultures.Add(culture)) Error($"'{name}' contains duplicate culture.");
                    if (text.Text.StartsWith("<body", StringComparison.OrdinalIgnoreCase)) Error("Send plain text; the server adds markup.");
                    if (name == "eventText" && text.Text.IndexOfAny(['\r', '\n']) >= 0) Error("'eventText' cannot contain line breaks.");
                }
            }
        }
        return errors.Count == 0
            ? OperationValidation.Valid
            : OperationValidation.Invalid(WorkerFailureCategories.ValidationError, string.Join("\n", errors));
    }

    private static readonly System.Text.RegularExpressions.Regex AttributePath =
        new(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex Identifier =
        new(@"^[A-Za-z][A-Za-z0-9_]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static void ValidateScreenFields(HmiWriteOperationRequest r, Action<string> error)
    {
        if (r.ItemType is not null && !ScreenItemTypes.Contains(r.ItemType, StringComparer.Ordinal))
            error($"'itemType' must be one of: {string.Join(", ", ScreenItemTypes)}.");
        if (r.Properties is { } properties)
        {
            if (properties.ValueKind != JsonValueKind.Object) error("'properties' must be a JSON object of attribute paths to values.");
            else
            {
                if (!properties.EnumerateObject().Any()) error("'properties' must not be empty.");
                foreach (var property in properties.EnumerateObject())
                {
                    if (!AttributePath.IsMatch(property.Name)) error($"Property '{property.Name}' is not an attribute path such as 'Left' or 'Font.Size'.");
                    if (property.Name == "Name") error("Rename with 'newName', not properties.Name.");
                    var value = property.Value;
                    var ok = value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                        || value.ValueKind == JsonValueKind.Object && value.EnumerateObject().All(t => t.Value.ValueKind == JsonValueKind.String && CultureNameRules.CanonicalName(t.Name) is not null);
                    if (!ok) error($"Property '{property.Name}' needs a string, number, boolean or culture-to-text object.");
                }
            }
        }
        if (r.Dynamizations is { } dynamizations)
        {
            if (dynamizations.Count == 0) error("'dynamizations' must not be empty.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in dynamizations)
            {
                if (d is null || d.Property is null || !Identifier.IsMatch(d.Property) || string.IsNullOrWhiteSpace(d.Tag))
                { error("Each dynamization needs 'property' (an attribute name) and a nonblank 'tag'."); continue; }
                if (!seen.Add(d.Property)) error($"Dynamization property '{d.Property}' is repeated.");
            }
        }
        if (r.Events is { } events)
        {
            if (events.Count == 0) error("'events' must not be empty.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in events)
            {
                if (e is null || e.Event is null || !Identifier.IsMatch(e.Event) || e.Script is null)
                { error("Each event needs 'event' (for example Tapped or Loaded) and 'script'."); continue; }
                if (!seen.Add(e.Event)) error($"Event '{e.Event}' is repeated.");
            }
        }
        if (r.StartScreen == false) error("'startScreen' can only be true; make another screen the start screen instead.");
        if (r.Operation == "update_screen" && r.NewName is null && r.Properties is null && r.Events is null && r.StartScreen is null)
            error("update_screen needs newName, properties, events or startScreen.");
        if (r.Operation == "update_screen_item" && r.NewName is null && r.Properties is null && r.Dynamizations is null && r.Events is null)
            error("update_screen_item needs newName, properties, dynamizations or events.");
    }

    private static bool ValidName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name == name.Trim() && !name.Any(c => char.IsControl(c) || c is '.' or '"');

    private static bool TryNumber(JsonElement number, out double value)
    {
        value = 0;
        return number.ValueKind == JsonValueKind.Number && number.TryGetDouble(out value) && double.IsFinite(value);
    }

    public static IReadOnlyList<string> ValidateAccessMode(IReadOnlyList<HmiWriteOperationRequest> operations, McpAccessMode mode) =>
        operations.Where(r => r is not null && r.Operation is not null && Specs.ContainsKey(r.Operation)
            && !OperationPolicyCatalog.IsAllowed(mode, HmiOperationCatalog.WorkerMethod(r.Operation)))
            .Select(r => $"Operation '{r.Operation}' (operationId '{r.OperationId}') is not permitted in {McpAccessModeNames.ToName(mode)} mode.").ToArray();

    public static string WrapEventText(string plain) => "<body><p>" + plain.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;") + "</p></body>";

    internal static string? NormalizePlcTag(string? value)
    {
        if (value is null || !value.StartsWith('"')) return value;
        var end = value.IndexOf('"', 1);
        return end > 1 && end + 1 < value.Length && value[end + 1] == '.'
            ? value[1..end] + value[(end + 1)..] : value;
    }

    public static HmiWriteRequestInfo ToWorkerRequest(HmiWriteOperationRequest r, string resolvedHmiName)
    {
        var result = new HmiWriteRequestInfo
        {
            HmiName = resolvedHmiName,
            TableName = r.TableName,
            GroupPath = r.GroupPath,
            TagName = r.TagName,
            NewName = r.NewName,
            Connection = r.Connection,
            PlcTag = r.PlcTag,
            DataType = r.DataType,
            AcquisitionCycle = r.AcquisitionCycle,
            AlarmName = r.AlarmName,
            AlarmKind = r.AlarmKind,
            TriggerTag = r.TriggerTag,
            TriggerMode = r.TriggerMode,
            Condition = r.Condition,
            AlarmClass = r.AlarmClass,
            Comment = r.Comment,
            DisplayName = r.DisplayName,
            InfoText = r.InfoText,
            EventText = r.EventText?.Select(t => new HmiText(t.Culture, WrapEventText(t.Text))).ToList(),
            BitNumber = r.BitNumber,
            Priority = r.Priority,
            ScreenName = r.ScreenName,
            ItemName = r.ItemName,
            ItemType = r.ItemType,
            PropertiesJson = r.Properties?.GetRawText(),
            Dynamizations = r.Dynamizations?.Select(d => new HmiDynamizationInfo { Property = d.Property!, Tag = d.Tag!, ReadOnly = d.ReadOnly }).ToList(),
            Events = r.Events?.Select(e => new HmiEventInfo { Event = e.Event!, Script = e.Script!, Async = e.Async }).ToList(),
            PartnerDevice = r.PartnerDevice,
            LocalInterface = r.LocalInterface,
            PartnerInterface = r.PartnerInterface,
            StartScreen = r.StartScreen,
        };
        if (r.ConditionValue is { } number)
        {
            if (!TryNumber(number, out var value)) throw new ArgumentException("conditionValue must be a finite JSON number.");
            // Classify the exact token before Double rounding can hide a fractional component.
            if (BigInteger.TryParse(number.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var integer)
                && integer >= int.MinValue && integer <= int.MaxValue)
                result.ConditionValueInt32 = (int)integer;
            else result.ConditionValueDouble = value;
        }
        return result;
    }
}
