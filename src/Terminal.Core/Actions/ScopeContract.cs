using System.Collections.ObjectModel;

namespace Terminal.Core.Actions;

public enum ScopeDimension
{
    FilesystemRead,
    FilesystemWrite,
    Process,
    Service,
    Network,
    DataEgress,
    PackageRepository,
    RemoteAccount,
    Privilege,
    Device,
    SecurityTarget
}

public readonly record struct ScopeEntry
{
    public ScopeEntry(ScopeDimension dimension, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Scope value must not be empty.", nameof(value));
        }

        Dimension = dimension;
        Value = value;
    }

    public ScopeDimension Dimension { get; }
    public string Value { get; }

    public string Canonical => $"{Dimension}:{Value}";
}

public sealed class ScopeContract
{
    public ScopeContract(
        IReadOnlyList<ScopeEntry> entries,
        TimeSpan? maxDuration = null,
        long? maxMemoryBytes = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (maxDuration is { } duration && duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDuration), "Duration must be positive when supplied.");
        }

        if (maxMemoryBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMemoryBytes), "Memory limit must be positive when supplied.");
        }

        Entries = Array.AsReadOnly(entries.ToArray());
        CanonicalEntries = Array.AsReadOnly(
            entries.Select(static entry => entry.Canonical)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray());
        MaxDuration = maxDuration;
        MaxMemoryBytes = maxMemoryBytes;
    }

    public IReadOnlyList<ScopeEntry> Entries { get; }
    public IReadOnlyList<string> CanonicalEntries { get; }
    public TimeSpan? MaxDuration { get; }
    public long? MaxMemoryBytes { get; }

    public bool Contains(ScopeDimension dimension, string value)
        => Entries.Any(entry => entry.Dimension == dimension && string.Equals(entry.Value, value, StringComparison.Ordinal));
}
