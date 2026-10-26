using System.Globalization;
using System.Text;

namespace DecisionCouncil.Core.Export;

public static class MarkdownExporter
{
    public static string Export(DebateDetail d)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {d.Idea.Title}");
        sb.AppendLine();
        sb.AppendLine($"Debate on {d.StartedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} · {d.Provider} · status: {d.Status}");
        if (!string.IsNullOrEmpty(d.StatusMessage)) sb.AppendLine().AppendLine($"> {d.StatusMessage}");
        sb.AppendLine();
        sb.AppendLine("## The idea").AppendLine().AppendLine(d.Idea.Text.Trim());
        if (!string.IsNullOrWhiteSpace(d.Idea.ExtraContext))
            sb.AppendLine().AppendLine("### Extra context").AppendLine().AppendLine(d.Idea.ExtraContext.Trim());

        if (d.Synthesis is { Answer: { } synthesis })
            sb.AppendLine().AppendLine("# Synthesis").AppendLine().AppendLine(DemoteHeadings(synthesis));

        foreach (var (round, title) in new[] { (DebateRound.Round1, "Round 1: independent answers"), (DebateRound.Round2, "Round 2: cross-examination") })
        {
            var runs = d.Runs.Where(r => r.Round == round).ToList();
            if (runs.Count == 0) continue;
            sb.AppendLine().AppendLine($"# {title}");
            foreach (var r in runs)
            {
                sb.AppendLine().AppendLine($"## {r.AgentName}");
                sb.AppendLine().AppendLine(r.Status == RunStatus.Done ? DemoteHeadings(r.Answer ?? "") : $"*{r.Status}: {r.Error}*");
            }
        }

        if (d.Decision is { } dec)
        {
            sb.AppendLine().AppendLine("# My decision").AppendLine();
            sb.AppendLine($"**{dec.Choice}** ({dec.DecidedAt:yyyy-MM-dd})").AppendLine();
            if (!string.IsNullOrWhiteSpace(dec.Reason)) sb.AppendLine(dec.Reason.Trim()).AppendLine();
            if (dec.RevisitOn is { } rv) sb.AppendLine($"Revisit on {rv:yyyy-MM-dd}.");
            if (dec.OutcomeLoggedAt is not null)
            {
                sb.AppendLine().AppendLine("## Outcome").AppendLine().AppendLine(dec.Outcome ?? "");
                if (dec.WarningsCameTrue.Count > 0)
                    sb.AppendLine().AppendLine("Warnings that came true: " + string.Join(", ", dec.WarningsCameTrue.Select(id => NameFor(d, id))));
            }
        }

        var real = d.Runs.Where(r => !r.Simulated).ToList();
        sb.AppendLine().AppendLine("---").AppendLine();
        sb.AppendLine(real.Count == 0
            ? $"{d.Runs.Count} calls, simulated (no credit used)."
            : $"{d.Runs.Count} calls · {d.TotalTokens:N0} tokens · ≈${d.TotalCost:0.0000} at API prices, as estimated by Claude Code (not charged on a Claude plan).");
        return sb.ToString();
    }

    public static string SuggestFileName(DebateDetail d)
    {
        var safe = new string(d.Idea.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray()).Trim();
        if (safe.Length > 60) safe = safe[..60].Trim();
        return $"{d.StartedAt:yyyy-MM-dd} {safe}.md";
    }

    private static string NameFor(DebateDetail d, string agentId) =>
        d.Runs.FirstOrDefault(r => r.AgentId == agentId)?.AgentName ?? agentId;

    /// <summary>Pushes headings inside an answer two levels down so they nest under the export's own headings.</summary>
    private static string DemoteHeadings(string md) => string.Join('\n', md.Replace("\r\n", "\n").Split('\n')
        .Select(l => l.StartsWith('#') ? "##" + l : l)).Trim();
}
