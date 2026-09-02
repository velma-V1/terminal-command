using Terminal.Core.Actions;
using Terminal.Core.Intelligence;
using Terminal.Core.SystemState;

namespace Terminal.Core.Capabilities;

public enum AutonomyTier
{
    T0Observe,
    T1Ephemeral,
    T2VerifiedReversibleLocalMutation,
    T3VerifiedReversibleContainment,
    T4Consequential,
    T5IrreversibleOrUnknown
}

public readonly record struct FactRequirement
{
    public FactRequirement(string key, string expectedValue)
    {
        Key = Required(key, nameof(key));
        ExpectedValue = Required(expectedValue, nameof(expectedValue));
    }

    public string Key { get; }
    public string ExpectedValue { get; }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be empty.", name)
            : value;
}

public readonly record struct FactEffect
{
    public FactEffect(string key, string value)
    {
        Key = Required(key, nameof(key));
        Value = Required(value, nameof(value));
    }

    public string Key { get; }
    public string Value { get; }

    public bool Satisfies(FactRequirement requirement)
        => string.Equals(Key, requirement.Key, StringComparison.Ordinal) &&
           string.Equals(Value, requirement.ExpectedValue, StringComparison.Ordinal);

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be empty.", name)
            : value;
}

public readonly record struct ResourceBudget
{
    public ResourceBudget(TimeSpan maxDuration, long maxMemoryBytes, int maxExternalActions)
    {
        if (maxDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDuration));
        }

        if (maxMemoryBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMemoryBytes));
        }

        if (maxExternalActions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExternalActions));
        }

        MaxDuration = maxDuration;
        MaxMemoryBytes = maxMemoryBytes;
        MaxExternalActions = maxExternalActions;
    }

    public TimeSpan MaxDuration { get; }
    public long MaxMemoryBytes { get; }
    public int MaxExternalActions { get; }
}

public sealed class CapabilityManifest
{
    public CapabilityManifest(
        string capabilityId,
        string version,
        IReadOnlyList<FactRequirement> preconditions,
        IReadOnlyList<FactEffect> effects,
        ActionBackend backend,
        AutonomyTier autonomyTier,
        RecoveryClass recovery,
        string verifierId,
        ScopeContract scope,
        ResourceBudget resourceBudget)
    {
        CapabilityId = Required(capabilityId, nameof(capabilityId));
        Version = Required(version, nameof(version));
        ArgumentNullException.ThrowIfNull(preconditions);
        ArgumentNullException.ThrowIfNull(effects);
        if (effects.Count == 0)
        {
            throw new ArgumentException("Capability must declare at least one effect.", nameof(effects));
        }

        Preconditions = Array.AsReadOnly(preconditions.ToArray());
        Effects = Array.AsReadOnly(effects.ToArray());
        Backend = backend;
        AutonomyTier = autonomyTier;
        Recovery = recovery;
        VerifierId = Required(verifierId, nameof(verifierId));
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        ResourceBudget = resourceBudget;
    }

    public string CapabilityId { get; }
    public string Version { get; }
    public IReadOnlyList<FactRequirement> Preconditions { get; }
    public IReadOnlyList<FactEffect> Effects { get; }
    public ActionBackend Backend { get; }
    public AutonomyTier AutonomyTier { get; }
    public RecoveryClass Recovery { get; }
    public string VerifierId { get; }
    public ScopeContract Scope { get; }
    public ResourceBudget ResourceBudget { get; }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be empty.", name)
            : value;
}

public sealed class CapabilityCatalog
{
    private readonly IReadOnlyList<CapabilityManifest> _capabilities;

    public CapabilityCatalog(IReadOnlyList<CapabilityManifest> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var duplicate = capabilities
            .GroupBy(static capability => capability.CapabilityId, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Duplicate capability ID '{duplicate.Key}'.", nameof(capabilities));
        }

        _capabilities = Array.AsReadOnly(capabilities.ToArray());
    }

    public IReadOnlyList<CapabilityManifest> Capabilities => _capabilities;

    public IReadOnlyList<CapabilityManifest> FindProducers(FactRequirement requirement)
        => _capabilities.Where(capability => capability.Effects.Any(effect => effect.Satisfies(requirement))).ToArray();
}

public enum DeterministicPlanStatus
{
    Success,
    AlreadySatisfied,
    Unknown,
    Ambiguous,
    Cycle
}

public sealed record PlanStep(string CapabilityId, CapabilityManifest Manifest);

public sealed class DeterministicPlan
{
    public DeterministicPlan(FactRequirement goal, IReadOnlyList<PlanStep> steps)
    {
        Goal = goal;
        ArgumentNullException.ThrowIfNull(steps);
        Steps = Array.AsReadOnly(steps.ToArray());
    }

    public FactRequirement Goal { get; }
    public IReadOnlyList<PlanStep> Steps { get; }
}

public sealed record DeterministicPlanResult(
    DeterministicPlanStatus Status,
    DeterministicPlan? Plan,
    string? Reason)
{
    internal static DeterministicPlanResult Success(FactRequirement goal, IReadOnlyList<PlanStep> steps)
        => new(DeterministicPlanStatus.Success, new DeterministicPlan(goal, steps), null);

    internal static DeterministicPlanResult Failure(DeterministicPlanStatus status, string reason)
        => new(status, null, reason);
}

public sealed class DeterministicPlanner
{
    public DeterministicPlanResult TryPlan(
        FactRequirement goal,
        SystemGraphSnapshot graph,
        CapabilityCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(catalog);

        if (graph.HasValue(goal.Key, goal.ExpectedValue))
        {
            return new DeterministicPlanResult(
                DeterministicPlanStatus.AlreadySatisfied,
                new DeterministicPlan(goal, []),
                null);
        }

        var facts = graph.Facts.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Value,
            StringComparer.Ordinal);
        var steps = new List<PlanStep>();
        var applied = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        var status = Satisfy(goal, catalog, facts, steps, applied, visiting, out var reason);
        return status == DeterministicPlanStatus.Success
            ? DeterministicPlanResult.Success(goal, steps)
            : DeterministicPlanResult.Failure(status, reason ?? status.ToString());
    }

    private static DeterministicPlanStatus Satisfy(
        FactRequirement requirement,
        CapabilityCatalog catalog,
        IDictionary<string, string> facts,
        IList<PlanStep> steps,
        ISet<string> applied,
        ISet<string> visiting,
        out string? reason)
    {
        if (facts.TryGetValue(requirement.Key, out var value) &&
            string.Equals(value, requirement.ExpectedValue, StringComparison.Ordinal))
        {
            reason = null;
            return DeterministicPlanStatus.Success;
        }

        var requirementIdentity = $"{requirement.Key}\u001f{requirement.ExpectedValue}";
        if (!visiting.Add(requirementIdentity))
        {
            reason = $"Planning cycle detected while satisfying '{requirement.Key}'.";
            return DeterministicPlanStatus.Cycle;
        }

        try
        {
            var producers = catalog.FindProducers(requirement);
            if (producers.Count == 0)
            {
                reason = $"No deterministic capability produces '{requirement.Key}={requirement.ExpectedValue}'.";
                return DeterministicPlanStatus.Unknown;
            }

            if (producers.Count > 1)
            {
                reason = $"Multiple deterministic capabilities produce '{requirement.Key}={requirement.ExpectedValue}'; refusing to guess.";
                return DeterministicPlanStatus.Ambiguous;
            }

            var producer = producers[0];
            foreach (var precondition in producer.Preconditions)
            {
                var preconditionStatus = Satisfy(
                    precondition,
                    catalog,
                    facts,
                    steps,
                    applied,
                    visiting,
                    out reason);
                if (preconditionStatus != DeterministicPlanStatus.Success)
                {
                    return preconditionStatus;
                }
            }

            if (applied.Add(producer.CapabilityId))
            {
                steps.Add(new PlanStep(producer.CapabilityId, producer));
                foreach (var effect in producer.Effects)
                {
                    facts[effect.Key] = effect.Value;
                }
            }

            reason = null;
            return facts.TryGetValue(requirement.Key, out var produced) &&
                   string.Equals(produced, requirement.ExpectedValue, StringComparison.Ordinal)
                ? DeterministicPlanStatus.Success
                : DeterministicPlanStatus.Unknown;
        }
        finally
        {
            visiting.Remove(requirementIdentity);
        }
    }
}

public enum GoalResolutionKind
{
    AlreadySatisfied,
    DeterministicPlan,
    ModelEscalationRequired,
    Unresolved
}

public sealed record GoalResolution(
    GoalResolutionKind Kind,
    DeterministicPlan? Plan,
    DeterministicPlanStatus DeterministicStatus,
    string? Reason);

public sealed class GoalCoordinator
{
    private readonly DeterministicPlanner _planner;
    private readonly ModelEscalationRouter _models;

    public GoalCoordinator(DeterministicPlanner planner, ModelEscalationRouter models)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _models = models ?? throw new ArgumentNullException(nameof(models));
    }

    public GoalResolution Resolve(
        FactRequirement goal,
        SystemGraphSnapshot graph,
        CapabilityCatalog catalog)
    {
        var deterministic = _planner.TryPlan(goal, graph, catalog);
        return deterministic.Status switch
        {
            DeterministicPlanStatus.Success => new GoalResolution(
                GoalResolutionKind.DeterministicPlan,
                deterministic.Plan,
                deterministic.Status,
                null),
            DeterministicPlanStatus.AlreadySatisfied => new GoalResolution(
                GoalResolutionKind.AlreadySatisfied,
                deterministic.Plan,
                deterministic.Status,
                null),
            _ when _models.HasAvailableProvider => new GoalResolution(
                GoalResolutionKind.ModelEscalationRequired,
                null,
                deterministic.Status,
                deterministic.Reason),
            _ => new GoalResolution(
                GoalResolutionKind.Unresolved,
                null,
                deterministic.Status,
                deterministic.Reason)
        };
    }
}
