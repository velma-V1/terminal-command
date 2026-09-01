using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Terminal.Core.Actions;

public static class ActionCanonicalizer
{
    private const int SchemaVersion = 1;

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

        writer.WritePropertyName("arguments");
        writer.WriteStartArray();
        foreach (var argument in action.Arguments)
        {
            writer.WriteStringValue(argument);
        }
        writer.WriteEndArray();

        writer.WriteString("backend", action.Backend.ToString());
        writer.WriteString("workingDirectory", action.WorkingDirectory);

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

        writer.WriteString("targetIdentity", action.TargetIdentity);

        writer.WritePropertyName("scope");
        writer.WriteStartObject();
        foreach (var pair in action.Scope.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WriteString(pair.Key, pair.Value);
        }
        writer.WriteEndObject();

        if (action.Timeout is { } timeout)
        {
            writer.WriteNumber("timeoutTicks", timeout.Ticks);
        }
        else
        {
            writer.WriteNull("timeoutTicks");
        }

        if (action.MemoryLimitBytes is { } memoryLimitBytes)
        {
            writer.WriteNumber("memoryLimitBytes", memoryLimitBytes);
        }
        else
        {
            writer.WriteNull("memoryLimitBytes");
        }

        writer.WriteString("mutation", action.Mutation.ToString());
        writer.WriteString("recovery", action.Recovery.ToString());
        writer.WriteString("provenance", action.Provenance);
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
