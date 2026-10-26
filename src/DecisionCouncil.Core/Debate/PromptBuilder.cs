using System.Text;
using DecisionCouncil.Core.Agents;

namespace DecisionCouncil.Core.Debate;

/// <summary>The editable prompt files in the data folder's <c>prompts</c> folder.</summary>
public sealed class PromptLibrary
{
    public string SharedRules { get; init; } = "";
    public string Round2 { get; init; } = "";
    public string Synthesizer { get; init; } = "";

    public const string SharedRulesFile = "shared-rules.md";
    public const string Round2File = "round2.md";
    public const string SynthesizerFile = "synthesizer.md";

    public static PromptLibrary Load(string promptsFolder) => new()
    {
        SharedRules = Read(promptsFolder, SharedRulesFile),
        Round2 = Read(promptsFolder, Round2File),
        Synthesizer = Read(promptsFolder, SynthesizerFile),
    };

    private static string Read(string folder, string file)
    {
        var path = Path.Combine(folder, file);
        if (!File.Exists(path)) throw new FileNotFoundException($"Prompt file missing: {path}", path);
        return File.ReadAllText(path).Trim();
    }
}

public sealed record AgentAnswer(AgentDefinition Agent, string Answer);

/// <summary>Builds the system and user prompts for each call. Pure string work, no I/O.</summary>
public sealed class PromptBuilder(PromptLibrary prompts, string businessContext, string language, int synthesisWordLimit = 600)
{
    public (string System, string User) Round1(AgentDefinition agent, Idea idea)
    {
        var system = AgentSystem(agent) + "\n\n" + Fill(prompts.SharedRules, agent.WordLimit, agent.Name);
        var user = new StringBuilder()
            .Append(ContextAndIdea(idea))
            .AppendLine()
            .AppendLine($"Answer as the {agent.Name} member of the council, in the required format.")
            .ToString();
        return (system, user);
    }

    public (string System, string User) Round2(AgentDefinition agent, Idea idea, string ownRound1, IReadOnlyList<AgentAnswer> others)
    {
        var system = AgentSystem(agent) + "\n\n" + Fill(prompts.SharedRules, agent.WordLimit, agent.Name)
                     + "\n\n" + Fill(prompts.Round2, agent.WordLimit, agent.Name);
        var sb = new StringBuilder()
            .Append(ContextAndIdea(idea))
            .AppendLine()
            .AppendLine("# Your round-1 answer")
            .AppendLine(ownRound1.Trim())
            .AppendLine()
            .AppendLine("# The other members' round-1 answers");
        foreach (var o in others)
            sb.AppendLine().AppendLine($"## {o.Agent.Name}").AppendLine(o.Answer.Trim());
        sb.AppendLine().AppendLine($"Cross-examine as the {agent.Name} member, in the round-2 format.");
        return (system, sb.ToString());
    }

    public (string System, string User) Synthesis(Idea idea, IReadOnlyList<AgentAnswer> round1, IReadOnlyList<AgentAnswer> round2)
    {
        var system = Fill(prompts.Synthesizer, synthesisWordLimit, "Synthesizer");
        var sb = new StringBuilder()
            .Append(ContextAndIdea(idea))
            .AppendLine()
            .AppendLine("# Round 1: independent answers");
        foreach (var a in round1)
            sb.AppendLine().AppendLine($"## {a.Agent.Name}").AppendLine(a.Answer.Trim());

        sb.AppendLine();
        if (round2.Count == 0)
        {
            sb.AppendLine("# Round 2: not run for this debate. Leave out the section on moved positions.");
        }
        else
        {
            sb.AppendLine("# Round 2: cross-examination");
            foreach (var a in round2)
                sb.AppendLine().AppendLine($"## {a.Agent.Name}").AppendLine(a.Answer.Trim());
        }
        sb.AppendLine().AppendLine("Write the synthesis in the required format. Do not give a verdict.");
        return (system, sb.ToString());
    }

    private static string AgentSystem(AgentDefinition agent) => agent.Body.Trim();

    private string ContextAndIdea(Idea idea)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Business context (about the person deciding)");
        sb.AppendLine(string.IsNullOrWhiteSpace(businessContext) ? "(No context profile given. State the assumptions you make about the business.)" : businessContext.Trim());
        sb.AppendLine();
        sb.AppendLine("# The idea");
        if (!string.IsNullOrWhiteSpace(idea.Title)) sb.AppendLine($"**{idea.Title.Trim()}**").AppendLine();
        sb.AppendLine(idea.Text.Trim());
        if (!string.IsNullOrWhiteSpace(idea.ExtraContext))
        {
            sb.AppendLine();
            sb.AppendLine("# Extra context for this idea");
            sb.AppendLine(idea.ExtraContext.Trim());
        }
        return sb.ToString();
    }

    private string Fill(string template, int wordLimit, string name) => template
        .Replace("{{language}}", language)
        .Replace("{{word_limit}}", wordLimit.ToString())
        .Replace("{{agent_name}}", name);
}
