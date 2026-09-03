using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Terminal.Core.Evidence;

namespace Terminal.Core.Actions;

public static class ActionCanonicalizer
{
    private const int SchemaVersion = 2;

    public static string Canonicalize(TerminalAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, SkipValidation = false });

        writer.WriteStartObject();
        writer.WriteNumber("schema", SchemaVersion);
        writer.WriteString("origin", action.Origin);
        if (action.CapabilityId is null)
        {
            writer.WriteNull("capabilityId");
        }
        else
        {
            writer.WriteString("capabilityId", action.CapabilityId);
        }

        writer.WriteString("operation", action.Operation);
        WriteStrings(writer, "arguments", action.Arguments);
        writer.WriteString("backend", action.Backend.ToString());
        writer.WritePropertyName("workingDirectory");
        WriteResource(writer, action.WorkingDirectory);

        writer.WritePropertyName("environmentDelta");
        writer.WriteStartObject();
        foreach (var pair in action.EnvironmentDelta.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (pair.Value is null)
            {
                writer.WriteNull(pair.Key);
            }
            else
            {
                writer.WriteString(pair.Key, pair.Value);
            }
        }
        writer.WriteEndObject();

        writer.WritePropertyName("targets");
        writer.WriteStartArray();
        foreach (var target in action.Targets.OrderBy(ResourceSortKey, StringComparer.Ordinal))
        {
            WriteResource(writer, target);
        }
        writer.WriteEndArray();

        writer.WritePropertyName("scope");
        writer.WriteStartObject();
        WriteStrings(writer, "entries", action.Scope.CanonicalEntries);
        WriteNullableTicks(writer, "maxDurationTicks", action.Scope.MaxDuration);
        WriteNullableLong(writer, "maxMemoryBytes", action.Scope.MaxMemoryBytes);
        writer.WriteEndObject();

        WriteNullableTicks(writer, "timeoutTicks", action.Timeout);
        WriteNullableLong(writer, "memoryLimitBytes", action.MemoryLimitBytes);
        writer.WriteString("mutation", action.Mutation.ToString());
        writer.WriteString("recovery", action.Recovery.ToString());
        writer.WritePropertyName("provenance");
        WriteProvenance(writer, action.Provenance);
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static TerminalAction Parse(
        string canonical,
        Guid actionId,
        DateTimeOffset createdAt)
    {
        if (actionId == Guid.Empty)
        {
            throw new ArgumentException("Action ID must not be empty.", nameof(actionId));
        }

        if (string.IsNullOrWhiteSpace(canonical))
        {
            throw new ArgumentException("Canonical Action material must not be empty.", nameof(canonical));
        }

        try
        {
            using var document = JsonDocument.Parse(canonical);
            var root = document.RootElement;
            RequireKind(root, JsonValueKind.Object, "Action root");

            var schema = RequiredProperty(root, "schema");
            if (schema.ValueKind != JsonValueKind.Number ||
                !schema.TryGetInt32(out var schemaVersion) ||
                schemaVersion != SchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported canonical Action schema; expected {SchemaVersion}.");
            }

            var origin = RequiredString(root, "origin");
            var capabilityId = NullableString(root, "capabilityId");
            var operation = RequiredString(root, "operation");
            var arguments = StringArray(root, "arguments");
            var backend = RequiredEnum<ActionBackend>(root, "backend");
            var workingDirectory = ParseResource(RequiredProperty(root, "workingDirectory"));
            var environmentDelta = ParseEnvironmentDelta(RequiredProperty(root, "environmentDelta"));
            var targets = ParseResources(RequiredProperty(root, "targets"));
            var scope = ParseScope(RequiredProperty(root, "scope"));
            var timeout = NullableTimeSpan(root, "timeoutTicks");
            var memoryLimitBytes = NullableInt64(root, "memoryLimitBytes");
            var mutation = RequiredEnum<MutationClass>(root, "mutation");
            var recovery = RequiredEnum<RecoveryClass>(root, "recovery");
            var provenance = ParseProvenance(RequiredProperty(root, "provenance"));

            return new TerminalAction(
                actionId,
                origin,
                capabilityId,
                operation,
                arguments,
                backend,
                workingDirectory,
                environmentDelta,
                targets,
                scope,
                timeout,
                memoryLimitBytes,
                mutation,
                recovery,
                provenance,
                createdAt);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Canonical Action material is not valid JSON.", exception);
        }
        catch (ArgumentException exception) when (exception.ParamName != nameof(actionId))
        {
            throw new InvalidDataException("Canonical Action material violates the typed Action contract.", exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Canonical Action material contains an out-of-range value.", exception);
        }
    }

    private static ResourceRef ParseResource(JsonElement element)
    {
        RequireKind(element, JsonValueKind.Object, "ResourceRef");
        return new ResourceRef(
            RequiredEnum<ResourceEnvironment>(element, "environment"),
            RequiredEnum<ResourceKind>(element, "kind"),
            RequiredString(element, "canonicalIdentity"),
            RequiredString(element, "displayIdentity"),
            NullableString(element, "stableIdentity"),
            NullableString(element, "ownerContext"),
            NullableString(element, "observedVersion"),
            RequiredDateTimeOffset(element, "observedAt"),
            RequiredEnum<RevalidationMethod>(element, "revalidationMethod"));
    }

    private static IReadOnlyList<ResourceRef> ParseResources(JsonElement element)
    {
        RequireKind(element, JsonValueKind.Array, "targets");
        var resources = new List<ResourceRef>();
        foreach (var item in element.EnumerateArray())
        {
            resources.Add(ParseResource(item));
        }

        return resources.AsReadOnly();
    }

    private static IReadOnlyDictionary<string, string?> ParseEnvironmentDelta(JsonElement element)
    {
        RequireKind(element, JsonValueKind.Object, "environmentDelta");
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
            {
                throw new InvalidDataException("Environment variable names must not be empty.");
            }

            values[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => property.Value.GetString(),
                _ => throw new InvalidDataException(
                    $"Environment value '{property.Name}' must be a string or null.")
            };
        }

        return values;
    }

    private static ScopeContract ParseScope(JsonElement element)
    {
        RequireKind(element, JsonValueKind.Object, "scope");
        var canonicalEntries = StringArray(element, "entries");
        var entries = new List<ScopeEntry>(canonicalEntries.Count);
        foreach (var canonicalEntry in canonicalEntries)
        {
            var separator = canonicalEntry.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator == canonicalEntry.Length - 1)
            {
                throw new InvalidDataException($"Invalid canonical scope entry '{canonicalEntry}'.");
            }

            var dimensionText = canonicalEntry[..separator];
            if (!Enum.TryParse<ScopeDimension>(dimensionText, ignoreCase: false, out var dimension) ||
                !Enum.IsDefined(dimension))
            {
                throw new InvalidDataException($"Unknown scope dimension '{dimensionText}'.");
            }

            entries.Add(new ScopeEntry(dimension, canonicalEntry[(separator + 1)..]));
        }

        return new ScopeContract(
            entries,
            NullableTimeSpan(element, "maxDurationTicks"),
            NullableInt64(element, "maxMemoryBytes"));
    }

    private static Provenance ParseProvenance(JsonElement element)
    {
        RequireKind(element, JsonValueKind.Object, "provenance");
        return new Provenance(
            RequiredEnum<ProvenanceSourceType>(element, "sourceType"),
            RequiredString(element, "sourceIdentity"),
            RequiredEnum<TrustClass>(element, "trustClass"),
            RequiredDateTimeOffset(element, "observedAt"),
            NullableString(element, "evidenceReference"),
            StringArray(element, "transformations"));
    }

    private static JsonElement RequiredProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            throw new InvalidDataException($"Canonical Action is missing required field '{propertyName}'.");
        }

        return property;
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        var property = RequiredProperty(element, propertyName);
        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException($"Field '{propertyName}' must be a non-empty string.");
        }

        return property.GetString()!;
    }

    private static string? NullableString(JsonElement element, string propertyName)
    {
        var property = RequiredProperty(element, propertyName);
        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException($"Field '{propertyName}' must be null or a non-empty string.");
        }

        return property.GetString();
    }

    private static IReadOnlyList<string> StringArray(JsonElement element, string propertyName)
    {
        var property = RequiredProperty(element, propertyName);
        RequireKind(property, JsonValueKind.Array, propertyName);
        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } value)
            {
                throw new InvalidDataException($"Field '{propertyName}' must contain only strings.");
            }

            values.Add(value);
        }

        return values.AsReadOnly();
    }

    private static TEnum RequiredEnum<TEnum>(JsonElement element, string propertyName)
        where TEnum : struct, Enum
    {
        var value = RequiredString(element, propertyName);
        if (!Enum.TryParse<TEnum>(value, ignoreCase: false, out var result) || !Enum.IsDefined(result))
        {
            throw new InvalidDataException($"Field '{propertyName}' has unknown value '{value}'.");
        }

        return result;
    }

    private static DateTimeOffset RequiredDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = RequiredString(element, propertyName);
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var result))
        {
            throw new InvalidDataException($"Field '{propertyName}' is not a valid timestamp.");
        }

        return result;
    }

    private static TimeSpan? NullableTimeSpan(JsonElement element, string propertyName)
    {
        var ticks = NullableInt64(element, propertyName);
        return ticks is null ? null : TimeSpan.FromTicks(ticks.Value);
    }

    private static long? NullableInt64(JsonElement element, string propertyName)
    {
        var property = RequiredProperty(element, propertyName);
        if (property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out var value))
        {
            throw new InvalidDataException($"Field '{propertyName}' must be an integer or null.");
        }

        return value;
    }

    private static void RequireKind(JsonElement element, JsonValueKind kind, string context)
    {
        if (element.ValueKind != kind)
        {
            throw new InvalidDataException($"{context} must be JSON {kind}.");
        }
    }

    private static void WriteResource(Utf8JsonWriter writer, ResourceRef resource)
    {
        writer.WriteStartObject();
        writer.WriteString("environment", resource.Environment.ToString());
        writer.WriteString("kind", resource.Kind.ToString());
        writer.WriteString("canonicalIdentity", resource.CanonicalIdentity);
        writer.WriteString("displayIdentity", resource.DisplayIdentity);
        WriteNullableString(writer, "stableIdentity", resource.StableIdentity);
        WriteNullableString(writer, "ownerContext", resource.OwnerContext);
        WriteNullableString(writer, "observedVersion", resource.ObservedVersion);
        writer.WriteString("observedAt", resource.ObservedAt.ToUniversalTime().ToString("O"));
        writer.WriteString("revalidationMethod", resource.RevalidationMethod.ToString());
        writer.WriteEndObject();
    }

    private static void WriteProvenance(Utf8JsonWriter writer, Provenance provenance)
    {
        writer.WriteStartObject();
        writer.WriteString("sourceType", provenance.SourceType.ToString());
        writer.WriteString("sourceIdentity", provenance.SourceIdentity);
        writer.WriteString("trustClass", provenance.TrustClass.ToString());
        writer.WriteString("observedAt", provenance.ObservedAt.ToUniversalTime().ToString("O"));
        WriteNullableString(writer, "evidenceReference", provenance.EvidenceReference);
        WriteStrings(writer, "transformations", provenance.Transformations);
        writer.WriteEndObject();
    }

    private static string ResourceSortKey(ResourceRef resource)
        => string.Join(
            "\u001f",
            resource.Environment,
            resource.Kind,
            resource.CanonicalIdentity,
            resource.StableIdentity ?? string.Empty,
            resource.OwnerContext ?? string.Empty,
            resource.ObservedVersion ?? string.Empty,
            resource.ObservedAt.ToUniversalTime().ToString("O"),
            resource.RevalidationMethod);

    private static void WriteStrings(Utf8JsonWriter writer, string propertyName, IEnumerable<string> values)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }
        writer.WriteEndArray();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteNullableTicks(Utf8JsonWriter writer, string propertyName, TimeSpan? value)
    {
        if (value is { } duration)
        {
            writer.WriteNumber(propertyName, duration.Ticks);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    private static void WriteNullableLong(Utf8JsonWriter writer, string propertyName, long? value)
    {
        if (value is { } number)
        {
            writer.WriteNumber(propertyName, number);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }
}
