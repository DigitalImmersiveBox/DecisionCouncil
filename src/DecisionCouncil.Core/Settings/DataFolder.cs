using System.Reflection;
using DecisionCouncil.Core.Agents;

namespace DecisionCouncil.Core.Settings;

/// <summary>
/// Seeds the user's data folder from the bundled defaults. Never overwrites a file the user already has.
/// The defaults are embedded in Core as "defaults/&lt;relative path&gt;", so a single-file exe needs nothing beside it.
/// </summary>
public static class DataFolder
{
    private const string Prefix = "defaults/";
    private static readonly Assembly Assembly = typeof(DataFolder).Assembly;

    /// <summary>Resource name by relative path (agents\01-finance.md); MSBuild may write either slash.</summary>
    private static readonly Dictionary<string, string> Defaults = Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
        .ToDictionary(n => Normalize(n[Prefix.Length..]), n => n, StringComparer.OrdinalIgnoreCase);

    public static bool HasDefault(string relativePath) => Defaults.ContainsKey(Normalize(relativePath));

    /// <summary>Lists the default agents already offered to an agents folder, so a deleted one is not copied again.</summary>
    public const string OfferedAgentsFile = "_offered-defaults.txt";

    /// <summary>The agents that shipped before <see cref="OfferedAgentsFile"/> existed: a folder without that file got exactly these.</summary>
    private static readonly string[] OriginalAgentIds = ["finance", "market", "technical", "operations", "legal", "premortem"];

    public static List<string> EnsureSeeded(AppSettings settings)
    {
        var copied = new List<string>();
        Directory.CreateDirectory(settings.DataFolder);
        Directory.CreateDirectory(settings.ExportsFolder);

        var agentsPrefix = "agents" + Path.DirectorySeparatorChar;
        foreach (var rel in Defaults.Keys.Where(k => !k.StartsWith(agentsPrefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
        {
            var dst = Path.Combine(settings.DataFolder, rel);
            if (File.Exists(dst)) continue;
            Write(rel, dst);
            copied.Add(rel);
        }

        SeedAgents(settings, agentsPrefix, copied);
        return copied;
    }

    /// <summary>
    /// Copies each default agent the folder has not been offered yet. An empty folder gets them all again;
    /// otherwise an agent the user deleted stays deleted, and agents added in a later version still arrive.
    /// </summary>
    private static void SeedAgents(AppSettings settings, string agentsPrefix, List<string> copied)
    {
        Directory.CreateDirectory(settings.AgentsFolder);
        var offeredPath = Path.Combine(settings.AgentsFolder, OfferedAgentsFile);
        var folderHasAgents = Directory.EnumerateFiles(settings.AgentsFolder, "*.md").Any();

        var offered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (folderHasAgents)
        {
            if (File.Exists(offeredPath))
                offered.UnionWith(File.ReadAllLines(offeredPath).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')));
            else
                offered.UnionWith(OriginalAgentIds);
        }
        var present = AgentLoader.LoadAll(settings.AgentsFolder).Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = !File.Exists(offeredPath);
        foreach (var rel in Defaults.Keys.Where(k => k.StartsWith(agentsPrefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
        {
            var id = AgentLoader.Parse(ReadText(rel), rel).Id;
            if (!offered.Add(id)) continue;
            changed = true;

            // The user already has a file by that name or an agent with that id: count it as offered, never overwrite.
            var dst = Path.Combine(settings.DataFolder, rel);
            if (File.Exists(dst) || present.Contains(id)) continue;
            Write(rel, dst);
            copied.Add(rel);
        }

        if (changed)
            File.WriteAllLines(offeredPath,
                ["# Default agents already offered to this folder. Remove a line to get that agent back at the next start.",
                 .. offered.Order(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>Restores one default file (used by "Reset to default" in the editors).</summary>
    public static bool RestoreDefault(AppSettings settings, string relativePath)
    {
        if (!HasDefault(relativePath)) return false;
        Write(relativePath, Path.Combine(settings.DataFolder, relativePath));
        return true;
    }

    private static string ReadText(string relativePath)
    {
        using var reader = new StreamReader(Assembly.GetManifestResourceStream(Defaults[Normalize(relativePath)])!);
        return reader.ReadToEnd();
    }

    private static void Write(string relativePath, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var source = Assembly.GetManifestResourceStream(Defaults[Normalize(relativePath)])!;
        using var target = File.Create(destination);
        source.CopyTo(target);
    }

    private static string Normalize(string path) => path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
}
