using Terminal.Core.Actions;
using Terminal.Core.Evidence;
using Terminal.Core.Intelligence;
using Terminal.Core.Recovery;
using Terminal.Core.SystemState;
using Terminal.State;

namespace Terminal.State.Tests;

public sealed class SqliteLearningStateTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-02T02:30:00Z");
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "terminal-v3-learning", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SystemGraph_round_trips_typed_facts_dependencies_and_durable_invalidation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = DatabasePath();
        await using var operational = new SqliteOperationalStore(path);
        await operational.InitializeAsync(cancellationToken);
        var store = new SqliteSystemGraphStore(operational);

        var head = Fact("repo.head", "abc123", []);
        var build = Fact("repo.build", "green", ["repo.head"]);
        await store.UpsertAsync(head, cancellationToken);
        await store.UpsertAsync(build, cancellationToken);
        await store.InvalidateAsync("repo.head", cancellationToken);

        var graph = await store.LoadAsync(cancellationToken);

        Assert.Equal(2, graph.Count);
        Assert.False(graph.TryGetFresh("repo.head", Now, out _));
        Assert.False(graph.TryGetFresh("repo.build", Now, out _));
    }

    [Fact]
    public async Task SystemGraph_reopen_preserves_provenance_resource_identity_and_freshness()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = DatabasePath();
        await using var operational = new SqliteOperationalStore(path);
        await operational.InitializeAsync(cancellationToken);
        var store = new SqliteSystemGraphStore(operational);
        var original = Fact("repo.head", "abc123", []);
        await store.UpsertAsync(original, cancellationToken);

        var graph = await store.LoadAsync(cancellationToken);

        Assert.True(graph.TryGetFresh("repo.head", Now.AddMinutes(4), out var restored));
        Assert.NotNull(restored);
        Assert.Equal(original.Subject, restored.Subject);
        Assert.Equal(original.Provenance, restored.Provenance);
        Assert.Equal(original.Dependencies, restored.Dependencies);
        Assert.False(graph.TryGetFresh("repo.head", Now.AddMinutes(6), out _));
    }

    [Fact]
    public async Task Verified_knowledge_round_trips_and_is_queryable_by_failure_signature()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = DatabasePath();
        await using var operational = new SqliteOperationalStore(path);
        await operational.InitializeAsync(cancellationToken);
        var store = new SqliteLearningStore(operational);
        var knowledge = new VerifiedKnowledgeRecord(
            Guid.NewGuid(),
            KnowledgeKind.Recipe,
            "NU1605:package-version-conflict",
            "Pin package A to compatible version.",
            Guid.NewGuid(),
            KnowledgeTrustClass.Verified,
            Now,
            [
                new ModelEvidenceReference("https://docs.example/package", "official-doc"),
                new ModelEvidenceReference("https://github.example/issue/1", "issue")
            ]);

        await store.SaveAsync(knowledge, cancellationToken);
        var found = await store.FindByTriggerAsync(knowledge.TriggerSignature, cancellationToken);

        var restored = Assert.Single(found);
        Assert.Equal(knowledge.KnowledgeId, restored.KnowledgeId);
        Assert.Equal(knowledge.Kind, restored.Kind);
        Assert.Equal(knowledge.TriggerSignature, restored.TriggerSignature);
        Assert.Equal(knowledge.Content, restored.Content);
        Assert.Equal(knowledge.SourceCandidateId, restored.SourceCandidateId);
        Assert.Equal(knowledge.TrustClass, restored.TrustClass);
        Assert.Equal(knowledge.PromotedAt, restored.PromotedAt);
        Assert.Equal(knowledge.Evidence, restored.Evidence);
    }

    [Fact]
    public async Task Saving_same_verified_knowledge_id_is_idempotent_but_conflicting_content_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = DatabasePath();
        await using var operational = new SqliteOperationalStore(path);
        await operational.InitializeAsync(cancellationToken);
        var store = new SqliteLearningStore(operational);
        var knowledge = new VerifiedKnowledgeRecord(
            Guid.NewGuid(),
            KnowledgeKind.Detector,
            "failure:signature",
            "detect exact signature",
            Guid.NewGuid(),
            KnowledgeTrustClass.Verified,
            Now,
            [new ModelEvidenceReference("local:test", "verification")]);

        await store.SaveAsync(knowledge, cancellationToken);
        await store.SaveAsync(knowledge, cancellationToken);

        var conflicting = knowledge with { Content = "different content" };
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveAsync(conflicting, cancellationToken).AsTask());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string DatabasePath()
    {
        Directory.CreateDirectory(_directory);
        return Path.Combine(_directory, "terminal.db");
    }

    private static SystemFact Fact(
        string key,
        string value,
        IReadOnlyList<string> dependencies)
        => new(
            key,
            new ResourceRef(
                ResourceEnvironment.Windows,
                ResourceKind.Repository,
                "C:\\repo",
                "C:\\repo",
                "repo:test",
                "windows:test",
                "abc123",
                Now,
                RevalidationMethod.RepositoryHead),
            value,
            new Provenance(
                ProvenanceSourceType.System,
                "git-probe",
                TrustClass.TrustedLocal,
                Now,
                "evidence:git-head",
                ["normalized"]),
            Now,
            TimeSpan.FromMinutes(5),
            generation: 7,
            dependencies);
}
