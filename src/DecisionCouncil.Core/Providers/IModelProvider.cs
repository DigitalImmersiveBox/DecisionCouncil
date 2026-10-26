namespace DecisionCouncil.Core.Providers;

/// <summary>One isolated model call. Metadata (agent, round, idea) is only used for logging and by the demo provider.</summary>
public sealed record ModelRequest(
    string SystemPrompt,
    string UserPrompt,
    string Model,
    string AgentId,
    DebateRound Round,
    string IdeaText = "");

public sealed record ModelResult(
    string Text,
    string Model,
    int? InputTokens,
    int? OutputTokens,
    decimal? CostUsd,
    TimeSpan Duration,
    string? RawOutput);

public sealed record ProviderCheck(bool Ok, string Message);

/// <summary>Claude Code login state, from <c>claude auth status</c>.</summary>
public sealed record ClaudeSignIn(bool SignedIn, string Message);

/// <summary>
/// The only thing that knows how to reach a model. The engine and the screens talk to this interface,
/// so an API-key provider can be added later without touching them.
/// </summary>
public interface IModelProvider
{
    string Name { get; }

    /// <summary>True when answers are canned (demo). Simulated calls never count against the usage limit.</summary>
    bool IsSimulated { get; }

    Task<ModelResult> CompleteAsync(ModelRequest request, CancellationToken ct);

    /// <summary>Cheap health check that does not call a model.</summary>
    Task<ProviderCheck> CheckAsync(CancellationToken ct);
}

public enum ProviderErrorKind
{
    NotInstalled,
    NotLoggedIn,
    CreditExhausted,
    RateLimited,
    Timeout,
    BadOutput,
    Other,
}

public sealed class ProviderException(ProviderErrorKind kind, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public ProviderErrorKind Kind { get; } = kind;

    /// <summary>Worth retrying with backoff.</summary>
    public bool IsTransient => Kind is ProviderErrorKind.RateLimited or ProviderErrorKind.Timeout;

    /// <summary>Stops the whole debate, because every other call would fail the same way.</summary>
    public bool IsFatal => Kind is ProviderErrorKind.NotInstalled or ProviderErrorKind.NotLoggedIn or ProviderErrorKind.CreditExhausted;
}
