namespace Terminal.Core.Intelligence;

public sealed record ModelProviderDescriptor(
    string ProviderId,
    string Family,
    string SizeClass,
    bool Local,
    bool CanExecute,
    bool CanAuthorize)
{
    public static ModelProviderDescriptor Qwen35NineB()
        => new(
            "qwen3.5-9b",
            "Qwen3.5",
            "9B",
            Local: true,
            CanExecute: false,
            CanAuthorize: false);
}

public sealed class ModelProviderPolicy
{
    private readonly IReadOnlyList<ModelProviderDescriptor> _allowedProviders;

    public ModelProviderPolicy(IReadOnlyList<ModelProviderDescriptor> allowedProviders)
    {
        ArgumentNullException.ThrowIfNull(allowedProviders);
        if (allowedProviders.Any(static provider => provider.CanExecute || provider.CanAuthorize))
        {
            throw new ArgumentException("Model providers can never receive execution or authorization authority.", nameof(allowedProviders));
        }

        var duplicate = allowedProviders
            .GroupBy(static provider => provider.ProviderId, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Duplicate model provider ID '{duplicate.Key}'.", nameof(allowedProviders));
        }

        _allowedProviders = Array.AsReadOnly(allowedProviders.ToArray());
    }

    public IReadOnlyList<ModelProviderDescriptor> AllowedProviders => _allowedProviders;

    public bool IsAllowed(string providerId)
        => !string.IsNullOrWhiteSpace(providerId) &&
           _allowedProviders.Any(provider => string.Equals(provider.ProviderId, providerId, StringComparison.Ordinal));

    public static ModelProviderPolicy Qwen35NineBOnly()
        => new([ModelProviderDescriptor.Qwen35NineB()]);
}

public sealed record ModelRequest
{
    public ModelRequest(
        Guid requestId,
        string purpose,
        string problem,
        IReadOnlyList<string> context,
        int maxOutputTokens)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("Model request ID must not be empty.", nameof(requestId));
        }

        RequestId = requestId;
        Purpose = Required(purpose, nameof(purpose));
        Problem = Required(problem, nameof(problem));
        ArgumentNullException.ThrowIfNull(context);
        Context = Array.AsReadOnly(context.ToArray());
        if (maxOutputTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOutputTokens));
        }

        MaxOutputTokens = maxOutputTokens;
    }

    public Guid RequestId { get; }
    public string Purpose { get; }
    public string Problem { get; }
    public IReadOnlyList<string> Context { get; }
    public int MaxOutputTokens { get; }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be empty.", name)
            : value;
}

public readonly record struct ModelEvidenceReference
{
    public ModelEvidenceReference(string source, string sourceClass)
    {
        Source = Required(source, nameof(source));
        SourceClass = Required(sourceClass, nameof(sourceClass));
    }

    public string Source { get; }
    public string SourceClass { get; }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be empty.", name)
            : value;
}

public enum ModelOutputClass
{
    UntrustedCandidate
}

public sealed record ModelResponse
{
    public ModelResponse(
        string providerId,
        string responseId,
        string summary,
        IReadOnlyList<ModelEvidenceReference> evidence,
        IReadOnlyList<string> proposedSteps)
    {
        ProviderId = Required(providerId, nameof(providerId));
        ResponseId = Required(responseId, nameof(responseId));
        Summary = Required(summary, nameof(summary));
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(proposedSteps);
        if (proposedSteps.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Proposed steps must be non-empty strings.", nameof(proposedSteps));
        }

        Evidence = Array.AsReadOnly(evidence.ToArray());
        ProposedSteps = Array.AsReadOnly(proposedSteps.ToArray());
    }

    public string ProviderId { get; }
    public string ResponseId { get; }
    public string Summary { get; }
    public IReadOnlyList<ModelEvidenceReference> Evidence { get; }
    public IReadOnlyList<string> ProposedSteps { get; }
    public bool Authoritative => false;
    public ModelOutputClass OutputClass => ModelOutputClass.UntrustedCandidate;

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must not be empty.", name)
            : value;
}

public interface IModelProvider
{
    ModelProviderDescriptor Descriptor { get; }

    ValueTask<ModelResponse> CompleteAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ModelEscalationRouter
{
    private readonly IReadOnlyList<IModelProvider> _providers;
    private readonly ModelProviderPolicy _policy;

    public ModelEscalationRouter(
        IReadOnlyList<IModelProvider> providers,
        ModelProviderPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _policy = policy ?? ModelProviderPolicy.Qwen35NineBOnly();

        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (provider.Descriptor.CanExecute || provider.Descriptor.CanAuthorize)
            {
                throw new ArgumentException("Model provider violates the zero-authority invariant.", nameof(providers));
            }
        }

        _providers = Array.AsReadOnly(
            providers.Where(provider => _policy.IsAllowed(provider.Descriptor.ProviderId)).ToArray());
    }

    public bool HasAvailableProvider => _providers.Count > 0;

    public IModelProvider? Select()
        => _providers.FirstOrDefault();

    public ValueTask<ModelResponse> CompleteAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var provider = Select() ?? throw new InvalidOperationException("No allowed model provider is available.");
        return provider.CompleteAsync(request, cancellationToken);
    }
}
