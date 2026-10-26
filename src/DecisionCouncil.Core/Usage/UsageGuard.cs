using DecisionCouncil.Core.Storage;

namespace DecisionCouncil.Core.Usage;

public enum UsageLevel
{
    Ok,
    Warning,
    Blocked,
}

public sealed record UsageState(UsageTotals Month, decimal LimitUsd, int WarnAtPercent)
{
    public bool HasLimit => LimitUsd > 0;
    public double Percent => HasLimit ? (double)(Month.CostUsd / LimitUsd * 100m) : 0;

    public UsageLevel Level => !HasLimit ? UsageLevel.Ok
        : Month.CostUsd >= LimitUsd ? UsageLevel.Blocked
        : Percent >= WarnAtPercent ? UsageLevel.Warning
        : UsageLevel.Ok;

    public string Describe() => HasLimit
        ? $"${Month.CostUsd:0.00} of ${LimitUsd:0.00} this month ({Percent:0}%), {Month.Calls} calls"
        : $"${Month.CostUsd:0.00} this month, {Month.Calls} calls (no limit set)";
}

public sealed class UsageLimitReachedException(UsageState state)
    : Exception($"Monthly usage limit reached: {state.Describe()}. Raise the limit in Settings or wait for next month.")
{
    public UsageState State { get; } = state;
}

/// <summary>Checks the user's own monthly limit before every real call. Simulated calls are never blocked.</summary>
public sealed class UsageGuard(CouncilStore store, Func<decimal> limitUsd, Func<int> warnAtPercent)
{
    public UsageState Current() => new(store.MonthlyUsage(DateTime.Now), limitUsd(), warnAtPercent());

    public void EnsureCanCall(bool simulated)
    {
        if (simulated) return;
        var state = Current();
        if (state.Level == UsageLevel.Blocked) throw new UsageLimitReachedException(state);
    }
}
