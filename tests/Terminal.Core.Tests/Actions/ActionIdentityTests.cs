using Terminal.Core.Actions;

namespace Terminal.Core.Tests.Actions;

public sealed class ActionIdentityTests
{
    [Fact]
    public void Same_material_action_has_same_hash_despite_different_id_and_timestamp()
    {
        var first = Fixtures.ReadOnly("git", ["status"], Guid.NewGuid(), DateTimeOffset.Parse("2026-09-01T12:00:00Z"));
        var second = Fixtures.ReadOnly("git", ["status"], Guid.NewGuid(), DateTimeOffset.Parse("2026-09-01T13:00:00Z"));

        Assert.Equal(ActionHash.Compute(first), ActionHash.Compute(second));
    }

    [Theory]
    [InlineData("arguments")]
    [InlineData("workingDirectory")]
    [InlineData("backend")]
    [InlineData("capability")]
    [InlineData("environment")]
    [InlineData("scope")]
    [InlineData("target")]
    [InlineData("timeout")]
    [InlineData("mutation")]
    [InlineData("recovery")]
    [InlineData("provenance")]
    public void Every_material_change_changes_hash(string field)
    {
        var baseline = Fixtures.ReadOnly("git", ["status"], Guid.NewGuid(), DateTimeOffset.UtcNow);
        var changed = Fixtures.Change(baseline, field);

        Assert.NotEqual(ActionHash.Compute(baseline), ActionHash.Compute(changed));
    }

    [Fact]
    public void Dictionary_insertion_order_does_not_change_identity()
    {
        var first = Fixtures.ReadOnly(
            "git",
            ["status"],
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            environment: new Dictionary<string, string?> { ["B"] = "2", ["A"] = "1" },
            scope: new Dictionary<string, string> { ["Z"] = "last", ["A"] = "first" });

        var second = Fixtures.ReadOnly(
            "git",
            ["status"],
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(1),
            environment: new Dictionary<string, string?> { ["A"] = "1", ["B"] = "2" },
            scope: new Dictionary<string, string> { ["A"] = "first", ["Z"] = "last" });

        Assert.Equal(ActionHash.Compute(first), ActionHash.Compute(second));
        Assert.Equal(ActionCanonicalizer.Canonicalize(first), ActionCanonicalizer.Canonicalize(second));
    }

    private static class Fixtures
    {
        public static TerminalAction ReadOnly(
            string operation,
            IReadOnlyList<string> arguments,
            Guid actionId,
            DateTimeOffset createdAt,
            IReadOnlyDictionary<string, string?>? environment = null,
            IReadOnlyDictionary<string, string>? scope = null)
            => new(
                actionId: actionId,
                origin: "terminal",
                capabilityId: "git.status",
                operation: operation,
                arguments: arguments,
                backend: ActionBackend.Windows,
                workingDirectory: "C:\\repo",
                environmentDelta: environment ?? new Dictionary<string, string?> { ["TERM"] = "xterm" },
                targetIdentity: "repo:123",
                scope: scope ?? new Dictionary<string, string> { ["filesystem"] = "read:C:\\repo" },
                timeout: TimeSpan.FromSeconds(30),
                memoryLimitBytes: 256 * 1024 * 1024,
                mutation: MutationClass.Observe,
                recovery: RecoveryClass.None,
                provenance: "user",
                createdAt: createdAt);

        public static TerminalAction Change(TerminalAction action, string field) => field switch
        {
            "arguments" => action.With(arguments: ["status", "--short"]),
            "workingDirectory" => action.With(workingDirectory: "C:\\other"),
            "backend" => action.With(backend: ActionBackend.Wsl),
            "capability" => action.With(capabilityId: "git.diff"),
            "environment" => action.With(environmentDelta: new Dictionary<string, string?> { ["TERM"] = "vt100" }),
            "scope" => action.With(scope: new Dictionary<string, string> { ["filesystem"] = "read:C:\\other" }),
            "target" => action.With(targetIdentity: "repo:456"),
            "timeout" => action.With(timeout: TimeSpan.FromSeconds(31)),
            "mutation" => action.With(mutation: MutationClass.LocalMutation),
            "recovery" => action.With(recovery: RecoveryClass.Checkpointable),
            "provenance" => action.With(provenance: "workflow"),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
        };
    }
}
