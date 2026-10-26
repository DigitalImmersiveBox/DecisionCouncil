using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DecisionCouncil.Core.Agents;

namespace DecisionCouncil.Core.Settings;

/// <summary>
/// Seeds the user's data folder from the bundled defaults, and updates default files the user never changed when a newer
/// version ships. A file the user edited is never overwritten.
/// The defaults are embedded in Core as "defaults/&lt;relative path&gt;", so a single-file exe needs nothing beside it.
/// </summary>
public static partial class DataFolder
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

    /// <summary>The content hash of each default file as the app last wrote it, so an untouched file can be updated later.</summary>
    public const string InstalledDefaultsFile = "_installed-defaults.txt";

    /// <summary>The agents that shipped before <see cref="OfferedAgentsFile"/> existed: a folder without that file got exactly these.</summary>
    private static readonly string[] OriginalAgentIds = ["finance", "market", "technical", "operations", "legal", "premortem"];

    /// <summary>
    /// Content hashes of the defaults shipped in 1.0.x, before <see cref="InstalledDefaultsFile"/> existed. A file still
    /// matching one of these was never edited, so it is safe to update.
    /// </summary>
    private static readonly Dictionary<string, string> Version1Hashes = new Dictionary<string, string>
    {
        ["agents/01-finance.md"] = "403a176725bea4c0",
        ["agents/02-market.md"] = "342dbc4f1f90d971",
        ["agents/03-technical.md"] = "7413147167fea16e",
        ["agents/04-operations.md"] = "b25924a848e5e4fa",
        ["agents/05-legal.md"] = "a2321604aa4c458d",
        ["agents/06-premortem.md"] = "3b3ac8870bb46ab3",
        ["context.md"] = "66b6d0754a62c11e",
        ["prompts/round2.md"] = "5ed2f5374761f94b",
        ["prompts/shared-rules.md"] = "3b62cbae22922d52",
        ["prompts/synthesizer.md"] = "53b44d4a27aba586",
    }.ToDictionary(kv => Normalize(kv.Key), kv => kv.Value, StringComparer.OrdinalIgnoreCase);

    /// <param name="updated">Receives the default files that were replaced by a newer version.</param>
    /// <returns>The default files that were copied because they were missing.</returns>
    public static List<string> EnsureSeeded(AppSettings settings, List<string>? updated = null)
    {
        var copied = new List<string>();
        Directory.CreateDirectory(settings.DataFolder);
        Directory.CreateDirectory(settings.ExportsFolder);
        var installed = ReadInstalled(settings);

        foreach (var rel in Defaults.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            var dst = Path.Combine(settings.DataFolder, rel);
            if (File.Exists(dst) && UpdateIfUntouched(rel, dst, installed)) updated?.Add(rel);
        }

        var agentsPrefix = "agents" + Path.DirectorySeparatorChar;
        foreach (var rel in Defaults.Keys.Where(k => !k.StartsWith(agentsPrefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
        {
            var dst = Path.Combine(settings.DataFolder, rel);
            if (File.Exists(dst)) continue;
            Write(rel, dst, installed);
            copied.Add(rel);
        }

        SeedAgents(settings, agentsPrefix, copied, installed);
        WriteInstalled(settings, installed);
        return copied;
    }

    /// <summary>
    /// Copies each default agent the folder has not been offered yet. An empty folder gets them all again;
    /// otherwise an agent the user deleted stays deleted, and agents added in a later version still arrive.
    /// </summary>
    private static void SeedAgents(AppSettings settings, string agentsPrefix, List<string> copied, Dictionary<string, string> installed)
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
            Write(rel, dst, installed);
            copied.Add(rel);
        }

        if (changed)
            File.WriteAllLines(offeredPath,
                ["# Default agents already offered to this folder. Remove a line to get that agent back at the next start.",
                 .. offered.Order(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>
    /// Replaces a default file with the current version when the user never edited it: its content still matches what
    /// the app last wrote, or a 1.0.x default. Switching an agent on or off does not count as an edit and is kept.
    /// </summary>
    private static bool UpdateIfUntouched(string rel, string dst, Dictionary<string, string> installed)
    {
        var current = ContentHash(ReadText(rel));
        var userText = File.ReadAllText(dst);
        var user = ContentHash(userText);
        if (user == current)
        {
            installed[rel] = current;
            return false;
        }

        var untouched = installed.TryGetValue(rel, out var written)
            ? written == user
            : Version1Hashes.TryGetValue(rel, out var shipped) && shipped == user;
        if (!untouched) return false;

        var wasDisabled = EnabledLineRegex().Match(userText) is { Success: true } m && m.Groups[1].Value.Equals("false", StringComparison.OrdinalIgnoreCase);
        Write(rel, dst, installed);
        if (wasDisabled) AgentLoader.SetEnabled(AgentLoader.Parse(File.ReadAllText(dst), dst), false);
        return true;
    }

    /// <summary>Restores one default file (used by "Reset to default" in the editors).</summary>
    public static bool RestoreDefault(AppSettings settings, string relativePath)
    {
        if (!HasDefault(relativePath)) return false;
        var installed = ReadInstalled(settings);
        Write(relativePath, Path.Combine(settings.DataFolder, relativePath), installed);
        WriteInstalled(settings, installed);
        return true;
    }

    /// <summary>
    /// Hash of a default file's content, ignoring what a copy can differ in without being edited: a byte-order mark,
    /// line endings, trailing white space and the agent's <c>enabled:</c> line.
    /// </summary>
    public static string ContentHash(string text)
    {
        var normal = EnabledLineRegex().Replace(text.TrimStart('﻿').Replace("\r\n", "\n"), "").TrimEnd();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normal)))[..16].ToLowerInvariant();
    }

    private static Dictionary<string, string> ReadInstalled(AppSettings settings)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(settings.DataFolder, InstalledDefaultsFile);
        if (!File.Exists(path)) return map;
        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split('\t');
            if (parts.Length == 2 && !line.StartsWith('#')) map[Normalize(parts[0].Trim())] = parts[1].Trim();
        }
        return map;
    }

    private static void WriteInstalled(AppSettings settings, Dictionary<string, string> installed) =>
        File.WriteAllLines(Path.Combine(settings.DataFolder, InstalledDefaultsFile),
            ["# Default files as the app last wrote them. A file that still matches is updated when a newer default ships.",
             .. installed.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => $"{kv.Key.Replace('\\', '/')}\t{kv.Value}")]);

    private static string ReadText(string relativePath)
    {
        using var reader = new StreamReader(Assembly.GetManifestResourceStream(Defaults[Normalize(relativePath)])!);
        return reader.ReadToEnd();
    }

    private static void Write(string relativePath, string destination, Dictionary<string, string> installed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using (var source = Assembly.GetManifestResourceStream(Defaults[Normalize(relativePath)])!)
        using (var target = File.Create(destination))
            source.CopyTo(target);
        installed[Normalize(relativePath)] = ContentHash(ReadText(relativePath));
    }

    private static string Normalize(string path) => path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

    [GeneratedRegex(@"^enabled:[ \t]*(\S*)[ \t]*\n?", RegexOptions.Multiline)]
    private static partial Regex EnabledLineRegex();
}
