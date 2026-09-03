namespace Terminal.Core.Evidence;

public enum EvidenceKind
{
    SystemFact,
    Finding,
    TraceEvent,
    TestObservation,
    Vulnerability,
    PropertyViolation,
    PerformanceObservation,
    ArtifactEvidence,
    VerificationResult,
    ToolOutput,
    ModelContext,
    ModelCandidate
}

public sealed record NormalizedEvidence(
    EvidenceKind Kind,
    string Content,
    Provenance Provenance,
    bool Truncated,
    long OriginalByteCount,
    string ContentHash);
