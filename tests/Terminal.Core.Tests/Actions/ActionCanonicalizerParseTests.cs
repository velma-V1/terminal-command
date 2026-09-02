using Terminal.Core.Actions;
using Terminal.Core.Evidence;

namespace Terminal.Core.Tests.Actions;

public sealed class ActionCanonicalizerParseTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T20:00:00Z");

    [Fact]
    public void Canonical_Action_round_trips_to_the_same_material_hash()
    {
        var original = CreateAction();
        var canonical = ActionCanonicalizer.Canonicalize(original);

        var parsed = ActionCanonicalizer.Parse(canonical, original.ActionId, original.CreatedAt);

        Assert.Equal(original.ActionId, parsed.ActionId);
        Assert.Equal(original.CreatedAt, parsed.CreatedAt);
        Assert.Equal(canonical, ActionCanonicalizer.Canonicalize(parsed));
        Assert.Equal(ActionHash.Compute(original), ActionHash.Compute(parsed));
    }

    [Fact]
    public void Parser_rejects_unknown_schema_instead_of_guessing()
    {
        var original = CreateAction();
        var canonical = ActionCanonicalizer.Canonicalize(original)
            .Replace("\"schema\":2", "\"schema\":999", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(
            () => ActionCanonicalizer.Parse(canonical, original.ActionId, original.CreatedAt));
    }

    [Fact]
    public void Parser_rejects_missing_material_field()
    {
        const string incomplete = "{\"schema\":2,\"origin\":\"test\"}";

        Assert.Throws<InvalidDataException>(
            () => ActionCanonicalizer.Parse(incomplete, Guid.NewGuid(), Now));
    }

    [Fact]
    public void Parser_rejects_empty_action_identity()
    {
        var canonical = ActionCanonicalizer.Canonicalize(CreateAction());

        Assert.Throws<ArgumentException>(
            () => ActionCanonicalizer.Parse(canonical, Guid.Empty, Now));
    }

    private static TerminalAction CreateAction()
        => new(
            Guid.NewGuid(),
            "test-origin",
            "terminal.session",
            "/bin/sh",
            ["-c", "printf ready"],
            ActionBackend.Wsl,
            new ResourceRef(
                ResourceEnvironment.Wsl,
                ResourceKind.Directory,
                "/workspace/project",
                "project",
                "inode:123",
                "wsl:ubuntu",
                "generation:7",
                Now,
                RevalidationMethod.DirectoryIdentity),
            new Dictionary<string, string?>
            {
                ["SET_ME"] = "value",
                ["REMOVE_ME"] = null
            },
            [
                new ResourceRef(
                    ResourceEnvironment.Wsl,
                    ResourceKind.Repository,
                    "/workspace/project",
                    "repo",
                    "repo:123",
                    "wsl:ubuntu",
                    "head:abc",
                    Now,
                    RevalidationMethod.RepositoryHead)
            ],
            new ScopeContract(
                [
                    new ScopeEntry(ScopeDimension.FilesystemRead, "/workspace/project"),
                    new ScopeEntry(ScopeDimension.Process, "child")
                ],
                TimeSpan.FromMinutes(2),
                128 * 1024 * 1024),
            TimeSpan.FromSeconds(30),
            64 * 1024 * 1024,
            MutationClass.Ephemeral,
            RecoveryClass.Reversible,
            new Provenance(
                ProvenanceSourceType.System,
                "terminal",
                TrustClass.TrustedLocal,
                Now,
                "evidence:1",
                ["normalized"]),
            Now.AddSeconds(1));
}
