using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DecisionCouncil.Core.Providers;

public sealed class ClaudeCliOptions
{
    /// <summary>"claude" to search PATH, or a full path to claude.exe / claude.cmd.</summary>
    public string Executable { get; set; } = "claude";

    /// <summary>Extra arguments appended to every call, split on spaces. Lets you follow CLI flag changes without a rebuild.</summary>
    public string ExtraArgs { get; set; } = "";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Reaches Claude through the local Claude Code CLI (<c>claude -p</c>), logged in with the user's own plan.
/// Every call is a fresh process in an empty temp folder: no shared session, tools off, MCP servers off,
/// session not persisted. The prompt goes in on stdin and the agent's instructions in a system-prompt file,
/// so nothing long or quoted is ever put on the command line.
/// All CLI-specific knowledge lives in this class.
/// </summary>
public sealed class ClaudeCliProvider(ClaudeCliOptions options) : IModelProvider
{
    public string Name => "Claude Code CLI";
    public bool IsSimulated => false;

    public async Task<ModelResult> CompleteAsync(ModelRequest request, CancellationToken ct)
    {
        var workDir = Path.Combine(Path.GetTempPath(), "DecisionCouncil", "call-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            var systemFile = Path.Combine(workDir, "system-prompt.md");
            await File.WriteAllTextAsync(systemFile, request.SystemPrompt, new UTF8Encoding(false), ct);

            var args = new List<string>
            {
                "-p",
                "--output-format", "json",
                "--model", request.Model,
                "--system-prompt-file", systemFile,
                "--tools", "",
                "--disallowedTools", "mcp__*",
                "--strict-mcp-config",
                "--no-session-persistence",
            };
            args.AddRange(options.ExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            var sw = Stopwatch.StartNew();
            var (exitCode, stdout, stderr) = await RunAsync(args, request.UserPrompt, workDir, options.Timeout, ct);
            sw.Stop();

            return Parse(exitCode, stdout, stderr, request.Model, sw.Elapsed);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { /* temp folder, best effort */ }
        }
    }

    public async Task<ProviderCheck> CheckAsync(CancellationToken ct)
    {
        try
        {
            var (exit, stdout, stderr) = await RunAsync(["--version"], null, Path.GetTempPath(), TimeSpan.FromSeconds(30), ct);
            return exit == 0
                ? new ProviderCheck(true, "Claude Code CLI found: " + stdout.Trim() + ". Login is checked on the first real call.")
                : new ProviderCheck(false, "Claude Code CLI did not start: " + FirstLine(stderr, stdout));
        }
        catch (ProviderException ex)
        {
            return new ProviderCheck(false, ex.Message);
        }
    }

    /// <summary>Runs <c>claude auth status --json</c>. Reads local login state only; never calls a model.</summary>
    public async Task<ClaudeSignIn> GetSignInAsync(CancellationToken ct)
    {
        try
        {
            // Signed out exits 1 but still prints the JSON, so parse before looking at the exit code.
            var (exit, stdout, stderr) = await RunAsync(["auth", "status", "--json"], null, Path.GetTempPath(), TimeSpan.FromSeconds(30), ct);
            try
            {
                using var doc = JsonDocument.Parse(stdout);
                var root = doc.RootElement;
                string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

                if (!(root.TryGetProperty("loggedIn", out var li) && li.ValueKind == JsonValueKind.True))
                    return new ClaudeSignIn(false, "Not signed in.");

                var method = Str("authMethod");
                var text = new StringBuilder("Signed in");
                if (Str("email") is { Length: > 0 } email) text.Append(" as ").Append(email);
                if (method is { Length: > 0 } && method != "none") text.Append(" with ").Append(method);
                if (Str("subscriptionType") is { Length: > 0 } plan) text.Append(" (").Append(char.ToUpperInvariant(plan[0])).Append(plan[1..]).Append(" plan)");
                return new ClaudeSignIn(true, text.Append('.').ToString(), OnPlan: method == "claude.ai");
            }
            catch (JsonException)
            {
                return new ClaudeSignIn(false, exit == 0
                    ? "Could not read the sign-in status: " + FirstLine(stdout)
                    : "Could not read the sign-in status: " + FirstLine(stderr, stdout));
            }
        }
        catch (ProviderException ex)
        {
            return new ClaudeSignIn(false, ex.Message);
        }
    }

    /// <summary>
    /// Opens a console window running <c>claude auth login</c>, which continues in the browser.
    /// The window waits for a key at the end so any error stays readable. Completes when the window closes.
    /// </summary>
    public async Task SignInAsync(CancellationToken ct)
    {
        var exe = ResolveExecutable(options.Executable)
            ?? throw new ProviderException(ProviderErrorKind.NotInstalled,
                "Claude Code CLI not found. Install it (https://code.claude.com) or set its full path in Settings.");

        // cmd /c strips the outer pair of quotes, leaving: "<exe>" auth login & pause
        var psi = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = $"/c \"\"{exe}\" auth login & echo. & pause\"",
            UseShellExecute = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        using var process = Process.Start(psi)
            ?? throw new ProviderException(ProviderErrorKind.NotInstalled, $"Could not start Claude Code CLI at '{exe}'.");
        await process.WaitForExitAsync(ct);
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        IReadOnlyList<string> args, string? stdin, string workDir, TimeSpan timeout, CancellationToken ct)
    {
        var exe = ResolveExecutable(options.Executable)
            ?? throw new ProviderException(ProviderErrorKind.NotInstalled,
                "Claude Code CLI not found. Install it (https://code.claude.com) or set its full path in Settings.");

        var psi = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workDir,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };

        // npm installs a .cmd shim; that has to go through cmd.exe.
        if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(exe);
        }
        else
        {
            psi.FileName = exe;
        }
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new ProviderException(ProviderErrorKind.NotInstalled, $"Could not start Claude Code CLI at '{exe}': {ex.Message}", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        if (stdin is not null) await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            if (ct.IsCancellationRequested) throw;
            throw new ProviderException(ProviderErrorKind.Timeout, $"The call took longer than {timeout.TotalSeconds:0} s and was stopped.");
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    internal static ModelResult Parse(int exitCode, string stdout, string stderr, string requestedModel, TimeSpan elapsed)
    {
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(stdout); } catch (JsonException) { }

        if (doc is null)
            throw Classify(FirstLine(stderr, stdout), exitCode == 0 ? ProviderErrorKind.BadOutput : ProviderErrorKind.Other);

        using (doc)
        {
            var root = doc.RootElement;
            var text = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
            var isError = root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
            if (isError || exitCode != 0)
                throw Classify(string.IsNullOrWhiteSpace(text) ? FirstLine(stderr, stdout) : text, ProviderErrorKind.Other);

            int? input = null, output = null;
            if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
            {
                input = SumInts(u, "input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens");
                output = SumInts(u, "output_tokens");
            }

            decimal? cost = root.TryGetProperty("total_cost_usd", out var c) && c.TryGetDecimal(out var cv) ? cv : null;

            var model = requestedModel;
            if (root.TryGetProperty("modelUsage", out var mu) && mu.ValueKind == JsonValueKind.Object)
                model = mu.EnumerateObject().Select(p => p.Name).FirstOrDefault() ?? requestedModel;

            return new ModelResult(text.Trim(), model, input, output, cost, elapsed, stdout);
        }
    }

    private static int? SumInts(JsonElement obj, params string[] names)
    {
        int? sum = null;
        foreach (var n in names)
            if (obj.TryGetProperty(n, out var v) && v.TryGetInt32(out var i))
                sum = (sum ?? 0) + i;
        return sum;
    }

    private static ProviderException Classify(string message, ProviderErrorKind fallback)
    {
        var m = message.ToLowerInvariant();
        if (m.Contains("is not recognized") || m.Contains("cannot find the path") || m.Contains("no such file"))
            return new ProviderException(ProviderErrorKind.NotInstalled, "Claude Code CLI is not installed correctly: " + message + " Reinstall it, or set the full path to claude.exe in Settings.");
        if (m.Contains("not logged in") || m.Contains("/login") || m.Contains("authenticat") || m.Contains("invalid api key"))
            return new ProviderException(ProviderErrorKind.NotLoggedIn, "Claude Code is not logged in. Run `claude` once in a terminal and log in with your plan. (" + message + ")");
        if (m.Contains("credit") || m.Contains("usage limit") || m.Contains("limit reached") || m.Contains("out of extra usage"))
            return new ProviderException(ProviderErrorKind.CreditExhausted, "Your Claude plan's usage limit seems to be reached. The debate so far is saved. (" + message + ")");
        if (m.Contains("rate limit") || m.Contains("429") || m.Contains("overloaded") || m.Contains("529"))
            return new ProviderException(ProviderErrorKind.RateLimited, "Rate limited or overloaded: " + message);
        return new ProviderException(fallback, string.IsNullOrWhiteSpace(message) ? "Claude Code CLI returned no output." : message);
    }

    private static string FirstLine(params string[] candidates)
    {
        foreach (var c in candidates)
        {
            var line = c.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(line)) return line.Length > 400 ? line[..400] + "…" : line;
        }
        return "";
    }

    /// <summary>
    /// Finds claude.exe, then claude.cmd, on PATH, or checks the configured full path.
    /// Also reads the saved user and machine PATH (the process copy is stale if PATH changed after the
    /// app or its parent started) and the native installer's folder, ~/.local/bin.
    /// </summary>
    public static string? ResolveExecutable(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) configured = "claude";
        if (Path.IsPathRooted(configured)) return File.Exists(configured) ? configured : null;

        var names = Path.HasExtension(configured) ? [configured] : new[] { configured + ".exe", configured + ".cmd" };
        var dirs = new[]
            {
                Environment.GetEnvironmentVariable("PATH"),
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"),
            }
            .SelectMany(p => (p ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var name in names)
            foreach (var dir in dirs)
            {
                try
                {
                    var full = Path.Combine(dir.Trim('"'), name);
                    if (File.Exists(full)) return full;
                }
                catch (ArgumentException) { /* malformed PATH entry */ }
            }
        return null;
    }
}
