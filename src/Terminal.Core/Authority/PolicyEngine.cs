using Terminal.Core.Actions;

namespace Terminal.Core.Authority;

public sealed class PolicyEngine
{
    public PolicyDecision Evaluate(TerminalAction action, PolicyFacts facts)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.CatastrophicDenied)
        {
            return new PolicyDecision(PolicyDecisionKind.Deny, "catastrophic-deny");
        }

        if (!facts.TargetRevalidated)
        {
            return new PolicyDecision(
                PolicyDecisionKind.RequireApproval,
                "target-revalidation-required",
                RequiresTargetRevalidation: true);
        }

        if (facts.PrivilegeRequired)
        {
            return new PolicyDecision(PolicyDecisionKind.RequireApproval, "privilege-required");
        }

        if (facts.RootOfTrustChange)
        {
            return new PolicyDecision(PolicyDecisionKind.RequireApproval, "root-of-trust-change");
        }

        if (action.Backend == ActionBackend.Remote)
        {
            return new PolicyDecision(PolicyDecisionKind.RequireApproval, "remote-target");
        }

        if (action.Mutation is MutationClass.Consequential or MutationClass.Irreversible)
        {
            return new PolicyDecision(PolicyDecisionKind.RequireApproval, "consequential-change");
        }

        if (action.Mutation is MutationClass.Observe or MutationClass.Ephemeral)
        {
            return new PolicyDecision(PolicyDecisionKind.AllowAuto, "low-consequence");
        }

        if (action.Mutation is MutationClass.LocalMutation or MutationClass.Containment)
        {
            var recoveryIsUsable = action.Recovery is RecoveryClass.Reversible or RecoveryClass.Checkpointable or RecoveryClass.Compensatable;
            if (facts.VerifierAvailable && facts.RecoveryPrepared && recoveryIsUsable)
            {
                return new PolicyDecision(PolicyDecisionKind.AllowAuto, "verified-recoverable-local-change");
            }

            return new PolicyDecision(PolicyDecisionKind.RequireApproval, "insufficient-proof-for-auto-mutation");
        }

        return new PolicyDecision(PolicyDecisionKind.RequireApproval, "unclassified-action");
    }
}
