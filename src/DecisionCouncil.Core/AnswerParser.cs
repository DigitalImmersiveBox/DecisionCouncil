using System.Text.RegularExpressions;

namespace DecisionCouncil.Core;

/// <summary>Pulls the fixed-format fields (confidence, verdict) out of an agent answer for display.</summary>
public static partial class AnswerParser
{
    [GeneratedRegex(@"confidence:?\**\s*:?\s*(\d)\s*/\s*5", RegexOptions.IgnoreCase)]
    private static partial Regex ConfidenceRegex();

    [GeneratedRegex(@"verdict:?\**\s*:?\s*\*?\s*(go with changes|not yet|no-go|go)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VerdictRegex();

    public static int? Confidence(string? answer)
    {
        if (string.IsNullOrEmpty(answer)) return null;
        // Round 2 says "Updated confidence"; take the last match so the updated value wins.
        var matches = ConfidenceRegex().Matches(answer);
        return matches.Count == 0 ? null : int.Parse(matches[^1].Groups[1].Value);
    }

    public static string? Verdict(string? answer)
    {
        if (string.IsNullOrEmpty(answer)) return null;
        var matches = VerdictRegex().Matches(answer);
        if (matches.Count == 0) return null;
        var v = matches[^1].Groups[1].Value.ToLowerInvariant();
        return v switch
        {
            "go with changes" => "Go with changes",
            "not yet" => "Not yet",
            "no-go" => "No-go",
            _ => "Go",
        };
    }
}
