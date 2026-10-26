namespace DecisionCouncil.Core.Agents;

/// <summary>The council's categories and their display order. Any other category name works too and is listed after these.</summary>
public static class AgentCategories
{
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> Known = ["Finance", "Market", "Technical", "Operations", "Legal", "Strategy and risk"];

    // Agent files written before categories existed have no category line. The six original agents still land in their group.
    private static readonly Dictionary<string, string> OriginalAgents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["finance"] = "Finance",
        ["market"] = "Market",
        ["technical"] = "Technical",
        ["operations"] = "Operations",
        ["legal"] = "Legal",
        ["premortem"] = "Strategy and risk",
    };

    /// <summary>The category to show: the file's value with known names in their usual spelling, or a fallback when it is empty.</summary>
    public static string Resolve(string? category, string agentId)
    {
        if (string.IsNullOrWhiteSpace(category))
            return OriginalAgents.GetValueOrDefault(agentId, Other);
        var trimmed = category.Trim();
        return Known.FirstOrDefault(k => k.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
               ?? (trimmed.Equals(Other, StringComparison.OrdinalIgnoreCase) ? Other : trimmed);
    }

    /// <summary>Sort key: known categories in their order, then custom ones, then Other.</summary>
    public static int Rank(string category)
    {
        for (var i = 0; i < Known.Count; i++)
            if (Known[i].Equals(category, StringComparison.OrdinalIgnoreCase)) return i;
        return category.Equals(Other, StringComparison.OrdinalIgnoreCase) ? int.MaxValue : Known.Count;
    }
}
