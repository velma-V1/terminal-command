using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Terminal.Core.Evidence;

public static partial class EvidenceSanitizer
{
    public static NormalizedEvidence Normalize(
        EvidenceKind kind,
        ReadOnlySpan<byte> raw,
        Provenance provenance,
        int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        if (maxBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        var decoded = Encoding.UTF8.GetString(raw);
        var redacted = BearerTokenRegex().Replace(decoded, "Authorization: Bearer [REDACTED]");
        redacted = NamedSecretRegex().Replace(redacted, match => $"{match.Groups[1].Value}{match.Groups[2].Value}[REDACTED]");

        var bounded = BoundUtf8(redacted, maxBytes);
        var boundedBytes = Encoding.UTF8.GetBytes(bounded);
        var digest = Convert.ToHexString(SHA256.HashData(boundedBytes)).ToLowerInvariant();
        var redactedBytes = Encoding.UTF8.GetByteCount(redacted);

        return new NormalizedEvidence(
            kind,
            bounded,
            provenance,
            redactedBytes > maxBytes,
            raw.Length,
            digest);
    }

    private static string BoundUtf8(string value, int maxBytes)
    {
        if (maxBytes == 0 || value.Length == 0)
        {
            return string.Empty;
        }

        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var used = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;
            if (used + runeBytes > maxBytes)
            {
                break;
            }

            builder.Append(rune.ToString());
            used += runeBytes;
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"(?i)Authorization\s*:\s*Bearer\s+[^\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?i)\b(token|password|secret|api[_-]?key)(\s*[:=]\s*)[^\s,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex NamedSecretRegex();
}
