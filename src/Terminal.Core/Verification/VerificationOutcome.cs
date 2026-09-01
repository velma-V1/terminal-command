namespace Terminal.Core.Verification;

public enum VerificationOutcome
{
    Verified,
    Failed,
    Partial,
    Unverified,
    NotReproduced,
    Flaky,
    EnvironmentFailure,
    OracleFailure,
    Cancelled,
    Indeterminate,
    RolledBack
}

public static class VerificationOutcomeExtensions
{
    public static bool IsFullSuccess(this VerificationOutcome outcome)
        => outcome == VerificationOutcome.Verified;
}
