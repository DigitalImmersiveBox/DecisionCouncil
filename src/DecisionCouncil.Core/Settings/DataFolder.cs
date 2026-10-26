using System.Reflection;

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

    public static List<string> EnsureSeeded(AppSettings settings)
    {
        var copied = new List<string>();
        Directory.CreateDirectory(settings.DataFolder);
        Directory.CreateDirectory(settings.ExportsFolder);

        // Only seed agents into a new or empty agents folder, so an agent the user deleted does not come back.
        var agentsAlreadyThere = Directory.Exists(settings.AgentsFolder)
                                 && Directory.EnumerateFiles(settings.AgentsFolder, "*.md").Any();

        foreach (var rel in Defaults.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            var dst = Path.Combine(settings.DataFolder, rel);
            if (File.Exists(dst)) continue;
            if (agentsAlreadyThere && rel.StartsWith("agents" + Path.DirectorySeparatorChar)) continue;
            Write(rel, dst);
            copied.Add(rel);
        }
        return copied;
    }

    /// <summary>Restores one default file (used by "Reset to default" in the editors).</summary>
    public static bool RestoreDefault(AppSettings settings, string relativePath)
    {
        if (!HasDefault(relativePath)) return false;
        Write(relativePath, Path.Combine(settings.DataFolder, relativePath));
        return true;
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
