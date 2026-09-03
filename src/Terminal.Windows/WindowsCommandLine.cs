using System.Text;

namespace Terminal.Windows;

public static class WindowsCommandLine
{
    public static string QuoteArgument(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (argument.Length > 0 && !argument.Any(static character => char.IsWhiteSpace(character) || character == '"'))
        {
            return argument;
        }

        var builder = new StringBuilder(argument.Length + 2);
        builder.Append('"');
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            builder.Append('\\', backslashes);
            backslashes = 0;
            builder.Append(character);
        }

        builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }

    public static string Build(string executable, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        var builder = new StringBuilder(QuoteArgument(executable));
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            builder.Append(QuoteArgument(argument));
        }

        return builder.ToString();
    }
}

public static class WindowsEnvironmentBlock
{
    public static string Build(
        IReadOnlyDictionary<string, string> parent,
        IReadOnlyDictionary<string, string?> delta)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(delta);
        var merged = new Dictionary<string, string>(parent, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in delta)
        {
            if (string.IsNullOrEmpty(key) || key.Contains('=', StringComparison.Ordinal))
            {
                throw new ArgumentException("Environment variable names must be non-empty and cannot contain '='.", nameof(delta));
            }

            if (value is null)
            {
                merged.Remove(key);
            }
            else
            {
                merged[key] = value;
            }
        }

        var builder = new StringBuilder();
        foreach (var pair in merged.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
        }

        builder.Append('\0');
        return builder.ToString();
    }
}
