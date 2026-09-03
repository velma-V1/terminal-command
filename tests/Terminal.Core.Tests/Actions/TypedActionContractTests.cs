using Terminal.Core.Actions;
using Terminal.Core.Evidence;

namespace Terminal.Core.Tests.Actions;

public sealed class TypedActionContractTests
{
    [Fact]
    public void Typed_resource_scope_and_provenance_are_material_action_identity()
    {
        var baseline = CreateAction();
        var changedTarget = CreateAction(targetStableIdentity: "file-id:2");
        var changedScope = CreateAction(networkDestination: "api.example.net:443");
        var changedProvenance = CreateAction(sourceIdentity: "qwen3.5-9b");

        Assert.NotEqual(ActionHash.Compute(baseline), ActionHash.Compute(changedTarget));
        Assert.NotEqual(ActionHash.Compute(baseline), ActionHash.Compute(changedScope));
        Assert.NotEqual(ActionHash.Compute(baseline), ActionHash.Compute(changedProvenance));
    }

    [Fact]
    public void Scope_contract_is_order_independent_but_dimension_sensitive()
    {
        var first = new ScopeContract([
            new ScopeEntry(ScopeDimension.FilesystemRead, "C:\\repo"),
            new ScopeEntry(ScopeDimension.Network, "api.example.com:443")
        ]);
        var second = new ScopeContract([
            new ScopeEntry(ScopeDimension.Network, "api.example.com:443"),
            new ScopeEntry(ScopeDimension.FilesystemRead, "C:\\repo")
        ]);
        var different = new ScopeContract([
            new ScopeEntry(ScopeDimension.DataEgress, "api.example.com:443"),
            new ScopeEntry(ScopeDimension.FilesystemRead, "C:\\repo")
        ]);

        Assert.Equal(first.CanonicalEntries, second.CanonicalEntries);
        Assert.NotEqual(first.CanonicalEntries, different.CanonicalEntries);
    }

    private static TerminalAction CreateAction(
        string targetStableIdentity = "file-id:1",
        string networkDestination = "api.example.com:443",
        string sourceIdentity = "user")
    {
        var observedAt = DateTimeOffset.Parse("2026-09-01T20:00:00Z");
        var workingDirectory = new ResourceRef(
            ResourceEnvironment.Windows,
            ResourceKind.Directory,
            "C:\\repo",
            "repo",
            "dir-id:1",
            "windows-host",
            "generation:1",
            observedAt,
            RevalidationMethod.DirectoryIdentity);
        var target = new ResourceRef(
            ResourceEnvironment.Windows,
            ResourceKind.File,
            "C:\\repo\\app.cs",
            "app.cs",
            targetStableIdentity,
            "windows-host",
            "version:1",
            observedAt,
            RevalidationMethod.FileIdentity);
        var scope = new ScopeContract([
            new ScopeEntry(ScopeDimension.FilesystemRead, "C:\\repo"),
            new ScopeEntry(ScopeDimension.Network, networkDestination)
        ], TimeSpan.FromMinutes(1), 512 * 1024 * 1024);
        var provenance = new Provenance(
            ProvenanceSourceType.User,
            sourceIdentity,
            TrustClass.Authenticated,
            observedAt,
            "evidence:1",
            []);

        return new TerminalAction(
            Guid.NewGuid(),
            "terminal",
            "build.inspect",
            "dotnet",
            ["build"],
            ActionBackend.Windows,
            workingDirectory,
            new Dictionary<string, string?>(),
            [target],
            scope,
            TimeSpan.FromSeconds(30),
            256 * 1024 * 1024,
            MutationClass.Observe,
            RecoveryClass.None,
            provenance,
            observedAt);
    }
}
