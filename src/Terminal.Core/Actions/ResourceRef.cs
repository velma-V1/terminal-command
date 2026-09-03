namespace Terminal.Core.Actions;

public enum ResourceEnvironment
{
    Windows,
    Wsl,
    Container,
    DisposableVm,
    Remote
}

public enum ResourceKind
{
    File,
    Directory,
    Process,
    Service,
    Package,
    Repository,
    Host,
    NetworkEndpoint,
    Device,
    Configuration,
    Container,
    VirtualMachine,
    Artifact,
    Other
}

public enum RevalidationMethod
{
    None,
    FileIdentity,
    DirectoryIdentity,
    ProcessIdentity,
    ServiceIdentity,
    PackageVersion,
    RepositoryHead,
    NetworkResolution,
    DeviceIdentity,
    Custom
}

public sealed record ResourceRef
{
    public ResourceRef(
        ResourceEnvironment environment,
        ResourceKind kind,
        string canonicalIdentity,
        string displayIdentity,
        string? stableIdentity,
        string? ownerContext,
        string? observedVersion,
        DateTimeOffset observedAt,
        RevalidationMethod revalidationMethod)
    {
        Environment = environment;
        Kind = kind;
        CanonicalIdentity = Required(canonicalIdentity, nameof(canonicalIdentity));
        DisplayIdentity = Required(displayIdentity, nameof(displayIdentity));
        StableIdentity = Optional(stableIdentity, nameof(stableIdentity));
        OwnerContext = Optional(ownerContext, nameof(ownerContext));
        ObservedVersion = Optional(observedVersion, nameof(observedVersion));
        ObservedAt = observedAt;
        RevalidationMethod = revalidationMethod;
    }

    public ResourceEnvironment Environment { get; }
    public ResourceKind Kind { get; }
    public string CanonicalIdentity { get; }
    public string DisplayIdentity { get; }
    public string? StableIdentity { get; }
    public string? OwnerContext { get; }
    public string? ObservedVersion { get; }
    public DateTimeOffset ObservedAt { get; }
    public RevalidationMethod RevalidationMethod { get; }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be empty.", name)
            : value;

    private static string? Optional(string? value, string name)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must be null or non-empty.", name);
        }

        return value;
    }
}
