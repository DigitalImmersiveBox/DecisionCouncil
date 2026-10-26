using System.Text;
using System.Text.RegularExpressions;

namespace DecisionCouncil.Core.Agents;

/// <summary>
/// Loads every <c>*.md</c> in the agents folder. Files starting with "_" are ignored, so drafts can sit there.
/// Front matter is simple <c>key: value</c> lines between <c>---</c> markers.
/// </summary>
public static partial class AgentLoader
{
    public static List<AgentDefinition> LoadAll(string agentsFolder, List<string>? errors = null)
    {
        var list = new List<AgentDefinition>();
        if (!Directory.Exists(agentsFolder)) return list;

        foreach (var file in Directory.EnumerateFiles(agentsFolder, "*.md").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(file).StartsWith('_')) continue;
            try
            {
                list.Add(Parse(File.ReadAllText(file), file));
            }
            catch (Exception ex)
            {
                errors?.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        // Duplicate ids would mix up runs; keep the first file and report the rest.
        var unique = new List<AgentDefinition>();
        foreach (var g in list.GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase))
        {
            unique.Add(g.First());
            foreach (var dup in g.Skip(1))
                errors?.Add($"{Path.GetFileName(dup.FilePath)}: id '{dup.Id}' is already used by {Path.GetFileName(g.First().FilePath)}; skipped.");
        }
        return unique
            .OrderBy(a => AgentCategories.Rank(a.Category))
            .ThenBy(a => a.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Order)
            .ThenBy(a => a.Name)
            .ToList();
    }

    public static AgentDefinition Parse(string text, string filePath)
    {
        var agent = new AgentDefinition { FilePath = filePath };
        var (front, body) = SplitFrontMatter(text);
        var category = "";

        foreach (var (key, value) in front)
        {
            switch (key)
            {
                case "id": agent.Id = value; break;
                case "name": agent.Name = value; break;
                case "category": category = value; break;
                case "order": if (int.TryParse(value, out var o)) agent.Order = o; break;
                case "enabled": agent.Enabled = !value.Equals("false", StringComparison.OrdinalIgnoreCase); break;
                case "word_limit": if (int.TryParse(value, out var w) && w > 0) agent.WordLimit = w; break;
                case "color": agent.Color = value; break;
                case "summary": agent.Summary = value; break;
                case "core_question": agent.CoreQuestion = value; break;
                case "avatar": agent.Avatar = value.ToLowerInvariant(); break;
            }
        }

        if (string.IsNullOrWhiteSpace(agent.Id))
            agent.Id = Slug(Path.GetFileNameWithoutExtension(filePath));
        if (string.IsNullOrWhiteSpace(agent.Name))
            agent.Name = HeadingRegex().Match(body) is { Success: true } m ? m.Groups[1].Value.Trim() : agent.Id;
        if (string.IsNullOrWhiteSpace(body))
            throw new FormatException("the file has no instructions after the front matter.");

        agent.Category = AgentCategories.Resolve(category, agent.Id);
        agent.Body = body.Trim();
        return agent;
    }

    /// <summary>Flips the <c>enabled:</c> line in place so the rest of the user's file stays exactly as written.</summary>
    public static void SetEnabled(AgentDefinition agent, bool enabled)
    {
        var text = File.ReadAllText(agent.FilePath);
        var value = enabled ? "true" : "false";
        string updated;
        if (EnabledLineRegex().IsMatch(text))
        {
            updated = EnabledLineRegex().Replace(text, "${1}" + value, 1);
        }
        else if (text.StartsWith("---"))
        {
            var firstBreak = text.IndexOf('\n');
            updated = text[..(firstBreak + 1)] + "enabled: " + value + "\n" + text[(firstBreak + 1)..];
        }
        else
        {
            updated = "---\nenabled: " + value + "\n---\n" + text;
        }
        File.WriteAllText(agent.FilePath, updated, new UTF8Encoding(false));
        agent.Enabled = enabled;
    }

    public static string NewAgentTemplate(string id, string name) => $"""
        ---
        id: {id}
        name: {name}
        category: {AgentCategories.Other}
        order: 100
        enabled: true
        word_limit: 250
        color: #2DD4BF
        avatar: antenna
        summary: One line on what this agent looks at.
        core_question: The one question this agent always asks.
        ---
        # {name}

        ## Mandate
        What this agent is responsible for judging.

        ## What you push back on
        - The failure this agent hunts for.
        - Another one.

        ## Core question
        The one question this agent always asks.

        ## Tone
        Direct and specific. No praise.
        """;

    public static string Slug(string s)
    {
        var slug = SlugRegex().Replace(s.ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "agent" : slug;
    }

    private static (List<(string Key, string Value)> Front, string Body) SplitFrontMatter(string text)
    {
        var front = new List<(string, string)>();
        text = text.Replace("\r\n", "\n").TrimStart('﻿');
        if (!text.StartsWith("---\n")) return (front, text);

        var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0) return (front, text);

        foreach (var line in text[4..end].Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim().Trim('"', '\'');
            front.Add((key, value));
        }

        var bodyStart = text.IndexOf('\n', end + 1);
        return (front, bodyStart < 0 ? "" : text[(bodyStart + 1)..]);
    }

    [GeneratedRegex(@"^#\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(enabled:\s*)\S+", RegexOptions.Multiline)]
    private static partial Regex EnabledLineRegex();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugRegex();
}
