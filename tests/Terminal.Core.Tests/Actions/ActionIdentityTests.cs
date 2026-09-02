using Terminal.Core.Actions;
using Terminal.Core.Evidence;

namespace Terminal.Core.Tests.Actions;

public sealed class ActionIdentityTests
{
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse("2026-09-01T12:00:00Z");

    [Fact]
    public void Same_material_action_has_same_hash_despite_different_id_and_timestamp()
    {
        var first = Fixtures.ReadOnly("git", ["status"], Guid.NewGuid(), DateTimeOffset.Parse("2026-09-01T12:00:00Z"));
        var second = Fixtures.ReadOnly("git", ["status"], Guid.NewGuid(), DateTimeOffset.Parse("2026-09-01T13:00:00Z"));

        Assert.Equal(ActionHash.Compute(first), ActionHash.Compute(second));
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("capability")]
    [InlineData("operation")]
    [InlineData("arguments")]
    [InlineData("workingDirectory")]
    [InlineData("backend")]
    [InlineData("environment")]
    [InlineData("scope")]
    [InlineData("target")]
    [InlineData("timeout")]
    [InlineData("memory")]
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
    public void Collection_insertion_order_does_not_change_identity()
    {
        var first = Fixtures.ReadOnly(
            "git",
            ["status"],
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            environment: new Dictionary<string, string?> { ["B"] = "2", ["A"] = "1" },
            scopeEntries:
            [
                new ScopeEntry(ScopeDimension.Network, "example.com:443"),
                new ScopeEntry(ScopeDimension.FilesystemRead, "C:\\repo")
            ]);

        var second = Fixtures.ReadOnly(
            "git",
            ["status"],
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(1),
            environment: new Dictionary<string, string?> { ["A"] = "1", ["B"] = "2" },
            scopeEntries:
            [
                new ScopeEntry(ScopeDimension.FilesystemRead, "C:\\repo"),
                new ScopeEntry(ScopeDimension.Network, "example.com:443")
            ]);

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
            IReadOnlyList<ScopeEntry>? scopeEntries = null)
            => new(
                actionId: actionId,
                origin: "terminal",
                capabilityId: "git.status",
                operation: operation,
                arguments: arguments,
                backend: ActionBackend.Windows,
                workingDirectory: Directory("C:\\repo", "dir:repo"),
                environmentDelta: environment ?? new Dictionary<string, string?> { ["TERM"] = "xterm" },
                targets: [Repository("C:\\repo", "repo:123")],
                scope: new ScopeContract(scopeEntries ?? [new ScopeEntry(ScopeDimension.FilesystemRead, "C:\\repo")]),
                timeout: TimeSpan.FromSeconds(30),
                memoryLimitBytes: 256 * 1024 * 1024,
                mutation: MutationClass.Observe,
                recovery: RecoveryClass.None,
                provenance: Provenance("user"),
                createdAt: createdAt);

        public static TerminalAction Change(TerminalAction action, string field)
            => new(
                actionId: Guid.NewGuid(),
                origin: field == "origin" ? "workflow" : action.Origin,
                capabilityId: field == "capability" ? "git.diff" : action.CapabilityId,
                operation: field == "operation" ? "git-other" : action.Operation,
                arguments: field == "arguments" ? ["status", "--short"] : action.Arguments,
                backend: field == "backend" ? ActionBackend.Wsl : action.Backend,
                workingDirectory: field == "workingDirectory"
                    ? Directory("C:\\other", "dir:other")
                    : action.WorkingDirectory,
                environmentDelta: field == "environment"
                    ? new Dictionary<string, string?> { ["TERM"] = "vt100" }
                    : action.EnvironmentDelta,
                targets: field == "target"
                    ? [Repository("C:\\repo", "repo:456")]
                    : action.Targets,
                scope: field == "scope"
                    ? new ScopeContract([new ScopeEntry(ScopeDimension.FilesystemRead, "C:\\other")])
                    : action.Scope,
                timeout: field == "timeout" ? TimeSpan.FromSeconds(31) : action.Timeout,
                memoryLimitBytes: field == "memory" ? action.MemoryLimitBytes + 1 : action.MemoryLimitBytes,
                mutation: field == "mutation" ? MutationClass.LocalMutation : action.Mutation,
                recovery: field == "recovery" ? RecoveryClass.Checkpointable : action.Recovery,
                provenance: field == "provenance" ? Provenance("automation") : action.Provenance,
                createdAt: action.CreatedAt.AddSeconds(1));

        private static ResourceRef Directory(string identity, string stableIdentity)
            => new(
                ResourceEnvironment.Windows,
                ResourceKind.Directory,
                identity,
                identity,
                stableIdentity,
                "windows-host",
                "generation:1",
                ObservedAt,
                RevalidationMethod.DirectoryIdentity);

        private static ResourceRef Repository(string identity, string stableIdentity)
            => new(
                ResourceEnvironment.Windows,
                ResourceKind.Repository,
                identity,
                identity,
                stableIdentity,
                "windows-host",
                "head:abc",
                ObservedAt,
                RevalidationMethod.RepositoryHead);

        private static Provenance Provenance(string source)
            => new(
                ProvenanceSourceType.User,
                source,
                TrustClass.Authenticated,
                ObservedAt,
                "evidence:fixture",
                []);
    }
}
