using Terminal.Core.Verification;

namespace Terminal.Core.Tests.Verification;

public sealed class VerificationOutcomeTests
{
    [Fact]
    public void Only_verified_is_full_success()
    {
        Assert.True(VerificationOutcome.Verified.IsFullSuccess());

        foreach (var outcome in Enum.GetValues<VerificationOutcome>().Where(static value => value != VerificationOutcome.Verified))
        {
            Assert.False(outcome.IsFullSuccess());
        }
    }

    [Theory]
    [InlineData(VerificationOutcome.Failed)]
    [InlineData(VerificationOutcome.Partial)]
    [InlineData(VerificationOutcome.Unverified)]
    [InlineData(VerificationOutcome.NotReproduced)]
    [InlineData(VerificationOutcome.Flaky)]
    [InlineData(VerificationOutcome.EnvironmentFailure)]
    [InlineData(VerificationOutcome.OracleFailure)]
    [InlineData(VerificationOutcome.Cancelled)]
    [InlineData(VerificationOutcome.Indeterminate)]
    [InlineData(VerificationOutcome.RolledBack)]
    public void Non_verified_outcome_cannot_be_promoted_to_success(VerificationOutcome outcome)
    {
        Assert.False(outcome.IsFullSuccess());
    }
}
