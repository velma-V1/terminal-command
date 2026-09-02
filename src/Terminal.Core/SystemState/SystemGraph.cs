using System.Collections.ObjectModel;
using Terminal.Core.Actions;
using Terminal.Core.Evidence;

namespace Terminal.Core.SystemState;

public sealed record SystemFact
{
    private readonly IReadOnlyList<string> _dependencies;

    public SystemFact(
        string key,
        ResourceRef subject,
        string value,
        Provenance provenance,
        DateTimeOffset observedAt,
        TimeSpan maxAge,
        long generation,
        IReadOnlyList<string> dependencies)
    {
        Key = Required(key, nameof(key));
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        Value = Required(value, nameof(value));
        Provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        if (maxAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAge), "Fact freshness duration must be positive.");
        }

        if (generation <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), "Fact generation must be positive.");
        }

        ArgumentNullException.ThrowIfNull(dependencies);
        if (dependencies.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Fact dependencies must be non-empty keys.", nameof(dependencies));
        }

        ObservedAt = observedAt;
        MaxAge = maxAge;
        Generation = generation;
        _dependencies = Array.AsReadOnly(dependencies.Distinct(StringComparer.Ordinal).ToArray());
    }

    public string Key { get; }
    public ResourceRef Subject { get; }
    public string Value { get; }
    public Provenance Provenance { get; }
    public DateTimeOffset ObservedAt { get; init; }
    public TimeSpan MaxAge { get; }
    public long Generation { get; }
    public IReadOnlyList<string> Dependencies => _dependencies;

    public bool IsFresh(DateTimeOffset now)
        => now >= ObservedAt && now - ObservedAt <= MaxAge;

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be empty.", name)
            : value;
}

public sealed class SystemGraph
{
    private readonly Dictionary<string, SystemFact> _facts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _invalidated = new(StringComparer.Ordinal);

    public int Count => _facts.Count;

    public void Upsert(SystemFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        if (_facts.TryGetValue(fact.Key, out var previous) && MateriallyChanged(previous, fact))
        {
            InvalidateDependents(fact.Key);
        }

        _facts[fact.Key] = fact;
        _invalidated.Remove(fact.Key);
    }

    public bool TryGetFresh(string key, DateTimeOffset now, out SystemFact? fact)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Fact key must not be empty.", nameof(key));
        }

        if (_invalidated.Contains(key) || !_facts.TryGetValue(key, out var candidate) || !candidate.IsFresh(now))
        {
            fact = null;
            return false;
        }

        fact = candidate;
        return true;
    }

    public bool Invalidate(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Fact key must not be empty.", nameof(key));
        }

        if (!_facts.ContainsKey(key))
        {
            return false;
        }

        _invalidated.Add(key);
        InvalidateDependents(key);
        return true;
    }

    public SystemGraphSnapshot Snapshot(DateTimeOffset now)
    {
        var fresh = new Dictionary<string, SystemFact>(StringComparer.Ordinal);
        foreach (var (key, fact) in _facts)
        {
            if (!_invalidated.Contains(key) && fact.IsFresh(now))
            {
                fresh[key] = fact;
            }
        }

        return new SystemGraphSnapshot(fresh, now);
    }

    private void InvalidateDependents(string dependencyKey)
    {
        var queue = new Queue<string>();
        queue.Enqueue(dependencyKey);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var fact in _facts.Values)
            {
                if (_invalidated.Contains(fact.Key) ||
                    !fact.Dependencies.Contains(current, StringComparer.Ordinal))
                {
                    continue;
                }

                _invalidated.Add(fact.Key);
                queue.Enqueue(fact.Key);
            }
        }
    }

    private static bool MateriallyChanged(SystemFact previous, SystemFact current)
        => previous.Generation != current.Generation ||
           !string.Equals(previous.Value, current.Value, StringComparison.Ordinal) ||
           previous.Subject != current.Subject;
}

public sealed class SystemGraphSnapshot
{
    private readonly IReadOnlyDictionary<string, SystemFact> _facts;

    internal SystemGraphSnapshot(IReadOnlyDictionary<string, SystemFact> facts, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(facts);
        _facts = new ReadOnlyDictionary<string, SystemFact>(
            new Dictionary<string, SystemFact>(facts, StringComparer.Ordinal));
        CapturedAt = capturedAt;
    }

    public DateTimeOffset CapturedAt { get; }
    public IReadOnlyDictionary<string, SystemFact> Facts => _facts;

    public bool TryGet(string key, out SystemFact? fact)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Fact key must not be empty.", nameof(key));
        }

        return _facts.TryGetValue(key, out fact);
    }

    public bool HasValue(string key, string expectedValue)
        => _facts.TryGetValue(key, out var fact) &&
           string.Equals(fact.Value, expectedValue, StringComparison.Ordinal);
}
