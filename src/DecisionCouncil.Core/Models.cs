namespace DecisionCouncil.Core;

public enum DebateRound
{
    Round1 = 1,
    Round2 = 2,
    Synthesis = 3,
}

public enum RunStatus
{
    Done,
    Failed,
    Cancelled,
}

public enum DebateStatus
{
    Running,
    Completed,
    CompletedWithErrors,
    Cancelled,
    StoppedAtLimit,
    Failed,
}

public sealed class Idea
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string ExtraContext { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class AgentRun
{
    public long Id { get; set; }
    /// <summary>Null for a test run from the Agents screen, which belongs to no debate but still counts towards usage.</summary>
    public long? DebateId { get; set; }
    public string AgentId { get; set; } = "";
    public string AgentName { get; set; } = "";
    public DebateRound Round { get; set; }
    public string SystemPrompt { get; set; } = "";
    public string UserPrompt { get; set; } = "";
    public string? Answer { get; set; }
    public string Model { get; set; } = "";
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? CostUsd { get; set; }
    public long DurationMs { get; set; }
    public RunStatus Status { get; set; }
    public string? Error { get; set; }
    public bool Simulated { get; set; }
    public DateTime CreatedAt { get; set; }

    public int? Confidence => AnswerParser.Confidence(Answer);
    public string? Verdict => AnswerParser.Verdict(Answer);
}

public sealed class Decision
{
    public long Id { get; set; }
    public long DebateId { get; set; }
    public string Choice { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime DecidedAt { get; set; }
    public DateOnly? RevisitOn { get; set; }
    public string? Outcome { get; set; }
    public List<string> WarningsCameTrue { get; set; } = [];
    public DateTime? OutcomeLoggedAt { get; set; }

    public bool IsDue => RevisitOn is { } d && OutcomeLoggedAt is null && d <= DateOnly.FromDateTime(DateTime.Today);
}

public sealed class DebateSummary
{
    public long DebateId { get; set; }
    public string IdeaTitle { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DebateStatus Status { get; set; }
    public string Provider { get; set; } = "";
    public int AgentCount { get; set; }
    public Decision? Decision { get; set; }
}

public sealed class DebateDetail
{
    public long Id { get; set; }
    public Idea Idea { get; set; } = new();
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DebateStatus Status { get; set; }
    public string? StatusMessage { get; set; }
    public bool Round2Enabled { get; set; }
    public string Provider { get; set; } = "";
    public string Language { get; set; } = "";
    public List<string> AgentIds { get; set; } = [];
    public List<AgentRun> Runs { get; set; } = [];
    public Decision? Decision { get; set; }

    public AgentRun? Synthesis => Runs.FirstOrDefault(r => r.Round == DebateRound.Synthesis);
    public int TotalTokens => Runs.Sum(r => (r.InputTokens ?? 0) + (r.OutputTokens ?? 0));
    public decimal TotalCost => Runs.Where(r => !r.Simulated).Sum(r => r.CostUsd ?? 0);
}

public sealed record UsageTotals(int Calls, long Tokens, decimal CostUsd);
