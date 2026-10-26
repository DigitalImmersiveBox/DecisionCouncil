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

    /// <summary>Agent selections on the New idea screen. The first one is the starting selection.</summary>
    public List<AgentPreset> Presets { get; set; } = DefaultPresets();

    /// <summary>The version of the default presets this file has seen; <see cref="Load"/> adds newer defaults to older files.</summary>
    public int PresetsVersion { get; set; }

    /// <summary>True when <see cref="Load"/> changed the file's contents, so the caller can save them back.</summary>
    [JsonIgnore] public bool UpgradedOnLoad { get; private set; }

    private const int CurrentPresetsVersion = 2;
    private static readonly string[] OldQuickCheck = ["finance", "market", "premortem"];

    public static List<AgentPreset> DefaultPresets() =>
    [
        new() { Name = "Quick check", AgentIds = ["finance", "market", "technical", "operations", "legal", "premortem"] },
        new() { Name = "Healthcare idea", AgentIds = ["finance", "market-healthcare", "technical", "tech-learning", "tech-deploy", "legal-sector", "premortem"] },
        new() { Name = "Culture and tourism idea", AgentIds = ["finance", "finance-funding", "market-culture", "tech-art", "tech-deploy", "operations", "premortem"] },
        new() { Name = "Partner deal", AgentIds = ["finance", "finance-cash", "market-partners", "ops-delivery", "legal", "strategy"] },
        new() { Name = "Product build", AgentIds = ["finance", "market", "technical", "tech-art", "operations", "strategy", "premortem"] },
        new() { Name = "Full council", AgentIds = [] },
    ];

    /// <summary>
    /// Adds default presets that are missing by name, before "Full council". Presets the user changed are left alone;
    /// a "Quick check" still holding the version-1 agents gets the new list.
    /// </summary>
    private bool UpgradePresets()
    {
        if (PresetsVersion >= CurrentPresetsVersion) return false;
        foreach (var preset in DefaultPresets())
        {
            var existing = Presets.FirstOrDefault(p => p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                var full = Presets.FindIndex(p => p.AgentIds.Count == 0);
                Presets.Insert(full < 0 ? Presets.Count : full, preset);
            }
            else if (preset.Name == "Quick check" && existing.AgentIds.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(OldQuickCheck))
            {
                existing.AgentIds = preset.AgentIds;
            }
        }
        PresetsVersion = CurrentPresetsVersion;
        return true;
    }

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
            if (File.Exists(path) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) is { } loaded)
            {
                loaded.UpgradedOnLoad = loaded.UpgradePresets();
                return loaded;
            }
        }
        catch (JsonException)
        {
            // A broken settings file should not stop the app; keep a copy so nothing is lost.
            File.Copy(path, path + ".broken", overwrite: true);
        }
        return new AppSettings { PresetsVersion = CurrentPresetsVersion };
    }

    public void Save(string? path = null)
    {
        path ??= SettingsFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, Json), Json)!;
}
