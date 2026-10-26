using DecisionCouncil.Core;
using DecisionCouncil.Core.Debate;
using DecisionCouncil.Core.Export;
using DecisionCouncil.Core.Settings;

// Command-line runner for the engine (milestone M1): run a debate without the UI and inspect stored debates.
Console.OutputEncoding = System.Text.Encoding.UTF8;

var opts = Args.Parse(args);
if (opts.Command is null or "help" || opts.Has("help"))
{
    Console.WriteLine("""
        Decision Council: command-line runner

          run     --idea "text" [--title "t"] [--extra "facts"] [--agents finance,market] [--no-round2]
                  [--provider demo|cli] [--speed 5]
          agents  list the agents found in the data folder
          list    list stored debates
          show    <debate id>   print a debate as Markdown
          check   check the configured provider (no model call)

        Common options:
          --data <folder>   use this data folder instead of the one in settings (settings are not saved)
        """);
    return 0;
}

var settings = AppSettings.Load();
if (opts.Get("data") is { } dataFolder) settings.DataFolder = Path.GetFullPath(dataFolder);
else if (settings.UpgradedOnLoad) settings.Save();
if (opts.Get("provider") is { } p) settings.Provider = p.Equals("cli", StringComparison.OrdinalIgnoreCase) ? ProviderKind.ClaudeCli : ProviderKind.Demo;
if (opts.Get("speed") is { } speed && double.TryParse(speed, System.Globalization.CultureInfo.InvariantCulture, out var sp)) settings.DemoSpeed = sp;

var council = new Council(settings);
Console.WriteLine($"Data folder: {settings.DataFolder}");
if (council.SeededFiles.Count > 0) Console.WriteLine($"Seeded {council.SeededFiles.Count} default files.");
if (council.UpdatedFiles.Count > 0) Console.WriteLine($"Updated {council.UpdatedFiles.Count} unedited default files: {string.Join(", ", council.UpdatedFiles)}");

switch (opts.Command)
{
    case "agents":
    {
        var errors = new List<string>();
        foreach (var a in council.LoadAgents(errors))
            Console.WriteLine($"  {(a.Enabled ? "[x]" : "[ ]")} {a.Id,-18} {a.Name,-26} {a.Category,-18} {a.WordLimit} words  {Path.GetFileName(a.FilePath)}");
        foreach (var e in errors) Console.WriteLine("  ! " + e);
        return 0;
    }
    case "list":
        foreach (var d in council.Store.ListDebates())
            Console.WriteLine($"  #{d.DebateId,-4} {d.StartedAt:yyyy-MM-dd HH:mm}  {d.Status,-20} {d.IdeaTitle}{(d.Decision is { } x ? "  → " + x.Choice : "")}");
        return 0;
    case "show":
    {
        if (opts.Positional.Count == 0 || !long.TryParse(opts.Positional[0], out var id)) { Console.Error.WriteLine("show needs a debate id."); return 2; }
        var d = council.Store.GetDebate(id);
        if (d is null) { Console.Error.WriteLine($"No debate #{id}."); return 1; }
        Console.WriteLine(MarkdownExporter.Export(d));
        return 0;
    }
    case "check":
    {
        var check = await council.CreateProvider().CheckAsync(CancellationToken.None);
        Console.WriteLine((check.Ok ? "OK: " : "PROBLEM: ") + check.Message);
        return check.Ok ? 0 : 1;
    }
    case "run":
        return await RunDebate(council, opts);
    default:
        Console.Error.WriteLine($"Unknown command '{opts.Command}'. Try 'help'.");
        return 2;
}

static async Task<int> RunDebate(Council council, Args opts)
{
    var ideaText = opts.Get("idea");
    if (string.IsNullOrWhiteSpace(ideaText)) { Console.Error.WriteLine("run needs --idea \"...\""); return 2; }

    var all = council.LoadAgents();
    var wanted = opts.Get("agents")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var agents = wanted is null ? all.Where(a => a.Enabled).ToList() : all.Where(a => wanted.Contains(a.Id, StringComparer.OrdinalIgnoreCase)).ToList();
    if (agents.Count == 0) { Console.Error.WriteLine("No agents selected."); return 2; }

    var round2 = !opts.Has("no-round2") && council.Settings.Round2Default;
    var provider = council.CreateProvider();
    var usage = council.Usage.Current();
    Console.WriteLine($"Provider: {provider.Name} · agents: {string.Join(", ", agents.Select(a => a.Name))} · round 2: {(round2 ? "on" : "off")} · {DebateEngine.CallCount(agents.Count, round2)} calls");
    if (!provider.IsSimulated) Console.WriteLine($"Usage: {usage.Describe()}");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); Console.WriteLine("Cancelling…"); };

    var idea = new Idea
    {
        Title = opts.Get("title") ?? (ideaText.Length > 60 ? ideaText[..60].Trim() + "…" : ideaText),
        Text = ideaText,
        ExtraContext = opts.Get("extra") ?? "",
    };

    var progress = new SyncProgress<DebateEvent>(e =>
    {
        switch (e)
        {
            case PhaseStarted ps: Console.WriteLine($"\n== {ps.Round} ({ps.AgentIds.Count} calls)"); break;
            case PhaseSkipped sk: Console.WriteLine($"\n== {sk.Round} skipped: {sk.Reason}"); break;
            case CallStarted cs: Console.WriteLine($"   … {cs.AgentId}"); break;
            case CallRetrying cr: Console.WriteLine($"   ↻ {cr.AgentId} retry {cr.Attempt}: {cr.Reason}"); break;
            case CallFinished cf:
                var r = cf.Run;
                Console.WriteLine(r.Status == RunStatus.Done
                    ? $"   ✓ {r.AgentName,-26} {r.DurationMs / 1000.0,5:0.0} s  {r.OutputTokens} out-tokens  conf {r.Confidence?.ToString() ?? "-"}  {r.Verdict}"
                    : $"   ✗ {r.AgentName,-26} {r.Status}: {r.Error}");
                break;
            case DebateFinished df: Console.WriteLine($"\nDebate #{df.DebateId}: {df.Status}{(df.Message is null ? "" : " · " + df.Message)}"); break;
        }
    });

    var debateId = await council.CreateEngine(provider).RunAsync(new DebateRequest { Idea = idea, Agents = agents, Round2 = round2 }, progress, cts.Token);

    var detail = council.Store.GetDebate(debateId)!;
    if (detail.Synthesis?.Answer is { } synthesis) Console.WriteLine("\n" + synthesis);
    Console.WriteLine($"\nSaved. Show it with: show {debateId}");
    return detail.Status is DebateStatus.Completed ? 0 : 1;
}

/// <summary>Reports on the calling thread right away; Progress&lt;T&gt; would post to the thread pool and reorder lines.</summary>
sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly object _gate = new();
    public void Report(T value) { lock (_gate) handler(value); }
}

sealed class Args
{
    public string? Command { get; private set; }
    public List<string> Positional { get; } = [];
    private readonly Dictionary<string, string?> _named = new(StringComparer.OrdinalIgnoreCase);

    public bool Has(string name) => _named.ContainsKey(name);
    public string? Get(string name) => _named.TryGetValue(name, out var v) ? v : null;

    public static Args Parse(string[] args)
    {
        var a = new Args();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                var name = args[i][2..];
                var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--");
                a._named[name] = hasValue ? args[++i] : null;
            }
            else if (a.Command is null) a.Command = args[i].ToLowerInvariant();
            else a.Positional.Add(args[i]);
        }
        return a;
    }
}
