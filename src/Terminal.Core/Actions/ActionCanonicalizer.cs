using System.Buffers;
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
