using System.Text.Json;
using System.Text.Json.Serialization;

namespace DecisionCouncil.Core.Settings;

public enum ProviderKind
{
    Demo,
    ClaudeCli,
}

public sealed class AgentPreset
{
    public string Name { get; set; } = "";

    /// <summary>Agent ids. Empty means "every enabled agent".</summary>
    public List<string> AgentIds { get; set; } = [];
}

/// <summary>User settings, stored as JSON in %LOCALAPPDATA%\DecisionCouncil\settings.json.</summary>
public sealed class AppSettings
{
    public string DataFolder { get; set; } = DefaultDataFolder;

    /// <summary>Demo is the default so a fresh install never spends credit until the user switches.</summary>
    public ProviderKind Provider { get; set; } = ProviderKind.Demo;

    public string ClaudeExecutable { get; set; } = "claude";
    public string ClaudeExtraArgs { get; set; } = "";

    /// <summary>CLI model aliases or full ids. Defaults to be decided in M2.</summary>
    public string AgentModel { get; set; } = "sonnet";
    public string SynthesizerModel { get; set; } = "opus";

    public string AnswerLanguage { get; set; } = "English";
    public bool Round2Default { get; set; } = true;

    /// <summary>Monthly limit in USD at API prices, as reported by the CLI (total_cost_usd). 0 means no limit.</summary>
    public decimal MonthlyLimitUsd { get; set; } = 0m;
    public int WarnAtPercent { get; set; } = 80;

    public int MaxConcurrency { get; set; } = 3;
    public int MaxRetries { get; set; } = 2;
    public int CallTimeoutSeconds { get; set; } = 300;
    public int SynthesisWordLimit { get; set; } = 600;

    /// <summary>Speed-up for the demo provider (1 = realistic delays).</summary>
    public double DemoSpeed { get; set; } = 1.0;

    public List<AgentPreset> Presets { get; set; } =
    [
        new() { Name = "Quick check", AgentIds = ["finance", "market", "premortem"] },
        new() { Name = "Full council", AgentIds = [] },
    ];

    [JsonIgnore] public string AgentsFolder => Path.Combine(DataFolder, "agents");
    [JsonIgnore] public string PromptsFolder => Path.Combine(DataFolder, "prompts");
    [JsonIgnore] public string ContextFile => Path.Combine(DataFolder, "context.md");
    [JsonIgnore] public string DatabaseFile => Path.Combine(DataFolder, "council.db");
    [JsonIgnore] public string ExportsFolder => Path.Combine(DataFolder, "exports");

    public static string AppFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DecisionCouncil");
    public static string DefaultDataFolder => Path.Combine(AppFolder, "Data");
    public static string SettingsFile => Path.Combine(AppFolder, "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load(string? path = null)
    {
        path ??= SettingsFile;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // A broken settings file should not stop the app; keep a copy so nothing is lost.
            File.Copy(path, path + ".broken", overwrite: true);
        }
        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= SettingsFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, Json), Json)!;
}
