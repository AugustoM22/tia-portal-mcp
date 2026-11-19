using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Siemens.Engineering;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>
/// Late-bound access to Unified screen objects. Screen, item, dynamization and event compositions are reached
/// through the runtime types so the worker needs no compile-time declaration for every widget type; native
/// exceptions are unwrapped from <see cref="TargetInvocationException"/> so failure classification is unchanged.
/// Attribute values are converted from JSON to the attribute's current runtime type.
/// </summary>
internal static class HmiReflection
{
    private static readonly string[] ItemNamespaces =
    {
        "Siemens.Engineering.HmiUnified.UI.Widgets.",
        "Siemens.Engineering.HmiUnified.UI.Shapes.",
        "Siemens.Engineering.HmiUnified.UI.Controls.",
        "Siemens.Engineering.HmiUnified.UI.Screens.",
    };

    /// <summary>Invokes a public instance method by name and argument count, rethrowing the native exception.</summary>
    internal static object? Call(object target, string method, params object?[] args)
    {
        var candidates = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == method && !m.IsGenericMethodDefinition && m.GetParameters().Length == args.Length).ToArray();
        if (candidates.Length == 0)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"'{target.GetType().Name}' has no method '{method}'.");
        return Invoke(candidates[0], target, args);
    }

    /// <summary>Invokes <c>Create&lt;T&gt;(args)</c> on a composition with <paramref name="type"/> as T.</summary>
    internal static object CreateGeneric(object composition, Type type, params object?[] args)
    {
        var method = composition.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetParameters().Length == args.Length)
            ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"'{composition.GetType().Name}' has no generic Create.");
        return Invoke(method.MakeGenericMethod(type), composition, args)
            ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "Create returned no object.");
    }

    internal static object? Property(object target, string name)
    {
        var property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"'{target.GetType().Name}' has no property '{name}'.");
        try { return property.GetValue(target); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static object? Invoke(MethodInfo method, object target, object?[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw; // unreachable
        }
    }

    /// <summary>Resolves an allowed screen-item type name (for example <c>HmiButton</c>) to its runtime type.</summary>
    internal static Type ItemType(object anyUnifiedObject, string typeName)
    {
        var assembly = anyUnifiedObject.GetType().Assembly;
        foreach (var ns in ItemNamespaces)
            if (assembly.GetType(ns + typeName, false) is { } type) return type;
        throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Screen item type '{typeName}' is not available in this TIA Portal.");
    }

    /// <summary>Finds a direct screen item by exact name (case-insensitive).</summary>
    internal static IEngineeringObject FindItem(object screen, string screenName, string itemName)
    {
        var items = (System.Collections.IEnumerable)Property(screen, "ScreenItems")!;
        foreach (var item in items)
            if (item is IEngineeringObject engineering && string.Equals(engineering.GetAttribute("Name") as string, itemName, StringComparison.OrdinalIgnoreCase))
                return engineering;
        throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Screen item '{itemName}' was not found on screen '{screenName}'.");
    }

    /// <summary>Applies each attribute path of a JSON object, recording one applied stage per path.</summary>
    internal static void ApplyProperties(IEngineeringObject target, string? propertiesJson, HmiWriteOutcomeInfo outcome, ProjectBase? project)
    {
        if (propertiesJson is null) return;
        using var document = JsonDocument.Parse(propertiesJson);
        foreach (var property in document.RootElement.EnumerateObject())
            HmiWriteRun.Set(outcome, "properties." + property.Name, () => SetPath(target, property.Name, property.Value));
    }

    /// <summary>Sets a dotted attribute path such as <c>Font.Size</c>; intermediate segments must be object parts.</summary>
    internal static void SetPath(IEngineeringObject target, string path, JsonElement value)
    {
        var segments = path.Split('.');
        var owner = target;
        for (var i = 0; i < segments.Length - 1; i++)
            owner = owner.GetAttribute(segments[i]) as IEngineeringObject
                ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"'{string.Join(".", segments.Take(i + 1))}' is not an object part.");
        var name = segments[segments.Length - 1];
        // Attribute metadata is read late-bound: AccessMode and SupportedTypes are not part of every declaration set.
        var info = ((System.Collections.IEnumerable)Call(owner, "GetAttributeInfos")!).Cast<object>()
            .FirstOrDefault(a => string.Equals(Property(a, "Name") as string, name, StringComparison.Ordinal))
            ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Attribute '{path}' does not exist on '{owner.GetType().Name}'.");
        var current = owner.GetAttribute(name);
        if (current is MultilingualText text)
        {
            HmiTextMapper.Write(text, Texts(value, text, path));
            return;
        }
        if (Property(info, "AccessMode")?.ToString() is not { } access || access.IndexOf("Write", StringComparison.Ordinal) < 0)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Attribute '{path}' is read-only.");
        var type = current?.GetType() ?? (Property(info, "SupportedTypes") as IEnumerable<Type>)?.FirstOrDefault(t => t is not null)
            ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"The type of attribute '{path}' is unknown.");
        owner.SetAttribute(name, Convert(value, type, path));
    }

    private static IReadOnlyList<HmiText> Texts(JsonElement value, MultilingualText text, string path)
    {
        if (value.ValueKind == JsonValueKind.Object)
            return value.EnumerateObject().Select(p => new HmiText(p.Name, p.Value.GetString() ?? string.Empty)).ToList();
        if (value.ValueKind == JsonValueKind.String)
        {
            // A plain string sets every culture of the text, which keeps single-language projects simple.
            var plain = value.GetString() ?? string.Empty;
            return text.Items.Select(i => new HmiText(i.Language.Culture.Name, plain)).ToList();
        }
        throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Text attribute '{path}' needs a string or a culture object.");
    }

    private static object Convert(JsonElement value, Type type, string path)
    {
        try
        {
            if (type == typeof(string)) return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
            if (type == typeof(bool)) return value.GetBoolean();
            if (type.IsEnum)
            {
                if (value.ValueKind == JsonValueKind.Number) return Enum.ToObject(type, value.GetInt64());
                return Enum.Parse(type, value.GetString()!.Replace("|", ","), ignoreCase: true);
            }
            if (type.FullName == "System.Drawing.Color") return Color(type, value.GetString()!, path);
            if (value.ValueKind == JsonValueKind.Number)
                return System.Convert.ChangeType(value.GetDouble(), type, CultureInfo.InvariantCulture);
            if (value.ValueKind == JsonValueKind.String)
                return System.Convert.ChangeType(value.GetString(), type, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException or InvalidOperationException)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Value for '{path}' cannot be converted to {type.Name}: {ex.Message}");
        }
        throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Value for '{path}' cannot be converted to {type.Name}.");
    }

    /// <summary>Parses #RRGGBB or #AARRGGBB through the runtime Color type.</summary>
    private static object Color(Type colorType, string text, string path)
    {
        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 6) hex = "FF" + hex;
        if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Color for '{path}' must be #RRGGBB or #AARRGGBB.");
        var fromArgb = colorType.GetMethod("FromArgb", new[] { typeof(int) })!;
        return fromArgb.Invoke(null, new object[] { unchecked((int)argb) })!;
    }

    /// <summary>Creates or replaces tag dynamizations on an item.</summary>
    internal static void ApplyDynamizations(IEngineeringObject item, IReadOnlyList<HmiDynamizationInfo>? dynamizations, HmiWriteOutcomeInfo outcome)
    {
        if (dynamizations is null) return;
        foreach (var d in dynamizations)
        {
            HmiWriteRun.Set(outcome, $"dynamization[{d.Property}]", () =>
            {
                var composition = Property(item, "Dynamizations")!;
                foreach (var existing in ((System.Collections.IEnumerable)composition).Cast<object>().ToList())
                    if (existing is IEngineeringObject e && string.Equals(e.GetAttribute("PropertyName") as string, d.Property, StringComparison.Ordinal))
                        Call(existing, "Delete");
                var tagDynamization = item.GetType().Assembly.GetType("Siemens.Engineering.HmiUnified.UI.Dynamization.TagDynamization", true)!;
                var created = (IEngineeringObject)CreateGeneric(composition, tagDynamization, d.Property);
                created.SetAttribute("Tag", d.Tag);
                if (d.ReadOnly is { } readOnly) created.SetAttribute("ReadOnly", readOnly);
            });
        }
    }

    /// <summary>Creates or reuses event handlers and writes their script.</summary>
    internal static void ApplyEvents(IEngineeringObject target, IReadOnlyList<HmiEventInfo>? events, HmiWriteOutcomeInfo outcome)
    {
        if (events is null) return;
        foreach (var e in events)
        {
            HmiWriteRun.Set(outcome, $"event[{e.Event}]", () =>
            {
                var composition = Property(target, "EventHandlers")!;
                var create = composition.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .First(m => m.Name == "Create" && !m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                var eventType = create.GetParameters()[0].ParameterType;
                object key;
                try { key = eventType.IsEnum ? Enum.Parse(eventType, e.Event, ignoreCase: true) : e.Event; }
                catch (ArgumentException) { throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Event '{e.Event}' is not valid; valid: {string.Join(", ", eventType.IsEnum ? Enum.GetNames(eventType) : Array.Empty<string>())}."); }
                var find = composition.GetType().GetMethod("Find", new[] { eventType });
                var handler = (find is null ? null : Invoke(find, composition, new[] { key })) ?? Invoke(create, composition, new[] { key });
                var script = Property(handler!, "Script") as IEngineeringObject
                    ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"Event '{e.Event}' has no script object.");
                script.SetAttribute("ScriptCode", e.Script);
                if (e.Async is { } async) script.SetAttribute("Async", async);
            });
        }
    }
}
