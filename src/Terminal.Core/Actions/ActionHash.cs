using System.Security.Cryptography;
using System.Text;

namespace Terminal.Core.Actions;

public static class ActionHash
{
    public static string Compute(TerminalAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var canonical = ActionCanonicalizer.Canonicalize(action);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
