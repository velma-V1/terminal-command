namespace Terminal.Core.Evidence;

public enum ProvenanceSourceType
{
    User,
    Automation,
    Model,
    Tool,
    System,
    ExternalApi,
    Website,
    Repository,
    Document,
    Derived
}

public enum TrustClass
{
    TrustedLocal,
    Authenticated,
    VerifiedExternal,
    UnverifiedExternal,
    ModelGenerated,
    Derived
}

public sealed record Provenance
{
    public Provenance(
        ProvenanceSourceType sourceType,
        string sourceIdentity,
        TrustClass trustClass,
        DateTimeOffset observedAt,
        string? evidenceReference,
        IReadOnlyList<string> transformations)
    {
        if (string.IsNullOrWhiteSpace(sourceIdentity))
        {
            throw new ArgumentException("Source identity must not be empty.", nameof(sourceIdentity));
        }

        if (evidenceReference is not null && string.IsNullOrWhiteSpace(evidenceReference))
        {
            throw new ArgumentException("Evidence reference must be null or non-empty.", nameof(evidenceReference));
        }

        ArgumentNullException.ThrowIfNull(transformations);
        if (transformations.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Transformations must be non-empty strings.", nameof(transformations));
        }

        SourceType = sourceType;
        SourceIdentity = sourceIdentity;
        TrustClass = trustClass;
        ObservedAt = observedAt;
        EvidenceReference = evidenceReference;
        Transformations = Array.AsReadOnly(transformations.ToArray());
    }

    public ProvenanceSourceType SourceType { get; }
    public string SourceIdentity { get; }
    public TrustClass TrustClass { get; }
    public DateTimeOffset ObservedAt { get; }
    public string? EvidenceReference { get; }
    public IReadOnlyList<string> Transformations { get; }
}
