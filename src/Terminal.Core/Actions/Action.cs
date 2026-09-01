using System.Collections.ObjectModel;

namespace Terminal.Core.Actions;

public enum ActionBackend
{
    Windows,
    Wsl,
    Container,
    Remote
}

public enum MutationClass
{
    Observe,
    Ephemeral,
    LocalMutation,
    Containment,
    Consequential,
    Irreversible
}

public enum RecoveryClass
{
    None,
    Reversible,
    Checkpointable,
    Compensatable,
    Irreversible
}

public sealed class TerminalAction
{
    public TerminalAction(
        Guid actionId,
        string origin,
        string? capabilityId,
        string operation,
        IReadOnlyList<string> arguments,
        ActionBackend backend,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environmentDelta,
        string targetIdentity,
        IReadOnlyDictionary<string, string> scope,
        TimeSpan? timeout,
        long? memoryLimitBytes,
        MutationClass mutation,
        RecoveryClass recovery,
        string provenance,
        DateTimeOffset createdAt)
    {
        if (actionId == Guid.Empty)
        {
            throw new ArgumentException("Action ID must not be empty.", nameof(actionId));
        }

        if (capabilityId is not null && string.IsNullOrWhiteSpace(capabilityId))
        {
            throw new ArgumentException("Capability ID must be null or non-empty.", nameof(capabilityId));
        }

        if (timeout is { } duration && duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive when supplied.");
        }

        if (memoryLimitBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memoryLimitBytes), "Memory limit must be positive when supplied.");
        }

        ActionId = actionId;
        Origin = Required(origin, nameof(origin));
        CapabilityId = capabilityId;
        Operation = Required(operation, nameof(operation));
        Arguments = Array.AsReadOnly((arguments ?? throw new ArgumentNullException(nameof(arguments))).ToArray());
        Backend = backend;
        WorkingDirectory = Required(workingDirectory, nameof(workingDirectory));
        EnvironmentDelta = CopyNullableValues(environmentDelta ?? throw new ArgumentNullException(nameof(environmentDelta)));
        TargetIdentity = Required(targetIdentity, nameof(targetIdentity));
        Scope = CopyRequiredValues(scope ?? throw new ArgumentNullException(nameof(scope)));
        Timeout = timeout;
        MemoryLimitBytes = memoryLimitBytes;
        Mutation = mutation;
        Recovery = recovery;
        Provenance = Required(provenance, nameof(provenance));
        CreatedAt = createdAt;
    }

    public Guid ActionId { get; }
    public string Origin { get; }
    public string? CapabilityId { get; }
    public string Operation { get; }
    public IReadOnlyList<string> Arguments { get; }
    public ActionBackend Backend { get; }
    public string WorkingDirectory { get; }
    public IReadOnlyDictionary<string, string?> EnvironmentDelta { get; }
    public string TargetIdentity { get; }
    public IReadOnlyDictionary<string, string> Scope { get; }
    public TimeSpan? Timeout { get; }
    public long? MemoryLimitBytes { get; }
    public MutationClass Mutation { get; }
    public RecoveryClass Recovery { get; }
    public string Provenance { get; }
    public DateTimeOffset CreatedAt { get; }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be empty.", name)
            : value;

    private static IReadOnlyDictionary<string, string?> CopyNullableValues(IReadOnlyDictionary<string, string?> source)
        => new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(source, StringComparer.Ordinal));

    private static IReadOnlyDictionary<string, string> CopyRequiredValues(IReadOnlyDictionary<string, string> source)
        => new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(source, StringComparer.Ordinal));
}
