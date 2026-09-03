namespace Terminal.Core.Authority;

public enum PolicyDecisionKind
{
    AllowAuto,
    RequireApproval,
    Deny
}

public sealed record PolicyFacts(
    bool TargetRevalidated,
    bool VerifierAvailable,
    bool RecoveryPrepared,
    bool PrivilegeRequired,
    bool RootOfTrustChange,
    bool CatastrophicDenied);

public sealed record PolicyDecision(
    PolicyDecisionKind Kind,
    string ReasonCode,
    bool RequiresTargetRevalidation = false);
