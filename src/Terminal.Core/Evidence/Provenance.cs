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

    public bool Equals(Provenance? other)
        => ReferenceEquals(this, other) ||
           other is not null &&
           SourceType == other.SourceType &&
           string.Equals(SourceIdentity, other.SourceIdentity, StringComparison.Ordinal) &&
           TrustClass == other.TrustClass &&
           ObservedAt.Equals(other.ObservedAt) &&
           string.Equals(EvidenceReference, other.EvidenceReference, StringComparison.Ordinal) &&
           Transformations.SequenceEqual(other.Transformations, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SourceType);
        hash.Add(SourceIdentity, StringComparer.Ordinal);
        hash.Add(TrustClass);
        hash.Add(ObservedAt);
        hash.Add(EvidenceReference, StringComparer.Ordinal);
        foreach (var transformation in Transformations)
        {
            hash.Add(transformation, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
