using System.Text;
using Terminal.Core.Evidence;

namespace Terminal.Core.Tests.Evidence;

public sealed class EvidenceSanitizerTests
{
    [Fact]
    public void Sanitizer_redacts_secret_patterns_before_persistence()
    {
        var provenance = new Provenance(
            ProvenanceSourceType.Tool,
            "build-tool",
            TrustClass.UnverifiedExternal,
            DateTimeOffset.Parse("2026-09-01T20:00:00Z"),
            "raw:1",
            []);
        var raw = Encoding.UTF8.GetBytes("token=secret-value Authorization: Bearer abc123 normal-output");

        var evidence = EvidenceSanitizer.Normalize(
            EvidenceKind.ToolOutput,
            raw,
            provenance,
            maxBytes: 4096);

        Assert.DoesNotContain("secret-value", evidence.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", evidence.Content, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", evidence.Content, StringComparison.Ordinal);
        Assert.Equal(provenance, evidence.Provenance);
    }

    [Fact]
    public void Sanitizer_bounds_external_output_and_records_truncation()
    {
        var provenance = new Provenance(
            ProvenanceSourceType.ExternalApi,
            "api",
            TrustClass.UnverifiedExternal,
            DateTimeOffset.UtcNow,
            null,
            []);
        var raw = Encoding.UTF8.GetBytes(new string('x', 100));

        var evidence = EvidenceSanitizer.Normalize(EvidenceKind.ToolOutput, raw, provenance, maxBytes: 16);

        Assert.True(evidence.Truncated);
        Assert.Equal(100, evidence.OriginalByteCount);
        Assert.True(Encoding.UTF8.GetByteCount(evidence.Content) <= 16);
    }
}
