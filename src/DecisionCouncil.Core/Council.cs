using DecisionCouncil.Core.Agents;
using DecisionCouncil.Core.Debate;
using DecisionCouncil.Core.Providers;
using DecisionCouncil.Core.Settings;
using DecisionCouncil.Core.Storage;
using DecisionCouncil.Core.Usage;

namespace DecisionCouncil.Core;

/// <summary>Wires settings, data folder, store and provider together. Shared by the WPF app and the CLI runner.</summary>
public sealed class Council
{
    public AppSettings Settings { get; private set; }
    public CouncilStore Store { get; }
    public UsageGuard Usage { get; }
    public List<string> SeededFiles { get; }

    /// <summary>Default files the user never edited that were replaced by a newer version at start-up.</summary>
    public List<string> UpdatedFiles { get; } = [];

    /// <summary>False when the data folder was overridden for one run (--data), so saving never writes that folder into settings.json.</summary>
    public bool PersistSettings { get; init; } = true;

    public Council(AppSettings settings)
    {
        Settings = settings;
        SeededFiles = DataFolder.EnsureSeeded(settings, UpdatedFiles);
        Store = new CouncilStore(settings.DatabaseFile);
        Store.CloseAbandonedDebates();
        Usage = new UsageGuard(Store, () => Settings.MonthlyLimitUsd, () => Settings.WarnAtPercent);
    }

    /// <summary>Applies changed settings. The data folder is only read at start-up, so changing it needs a restart.</summary>
    public void UpdateSettings(AppSettings settings)
    {
        Settings = settings;
        if (PersistSettings) settings.Save();
    }

    public IModelProvider CreateProvider() => CreateProvider(Settings);

    public static IModelProvider CreateProvider(AppSettings s) => s.Provider switch
    {
        ProviderKind.ClaudeCli => new ClaudeCliProvider(new ClaudeCliOptions
        {
            Executable = s.ClaudeExecutable,
            ExtraArgs = s.ClaudeExtraArgs,
            Timeout = TimeSpan.FromSeconds(Math.Max(30, s.CallTimeoutSeconds)),
        }),
        _ => new DemoProvider(s.DemoSpeed),
    };

    public List<AgentDefinition> LoadAgents(List<string>? errors = null) => AgentLoader.LoadAll(Settings.AgentsFolder, errors);

    public string LoadContext() => File.Exists(Settings.ContextFile) ? File.ReadAllText(Settings.ContextFile) : "";

    public DebateEngine CreateEngine(IModelProvider? provider = null)
    {
        var s = Settings;
        var prompts = new PromptBuilder(PromptLibrary.Load(s.PromptsFolder), LoadContext(), s.AnswerLanguage, s.SynthesisWordLimit);
        return new DebateEngine(provider ?? CreateProvider(), Store, prompts, Usage, new DebateOptions
        {
            AgentModel = s.AgentModel,
            SynthesizerModel = s.SynthesizerModel,
            Language = s.AnswerLanguage,
            MaxConcurrency = s.MaxConcurrency,
            MaxRetries = s.MaxRetries,
        });
    }
}
