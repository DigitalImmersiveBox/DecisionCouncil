using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DecisionCouncil.App.Infrastructure;

/// <summary>A newer release on GitHub: its version, the zip for this kind of build, and its notes page.</summary>
public sealed record Release(Version Version, string Page, string ZipName, string ZipUrl, long ZipSize, string? Sha256);

/// <summary>
/// Looks for a newer release on GitHub and installs it over the running exe (the same updater as Session Deck).
/// A running exe can't be overwritten but it can be renamed, so the old file becomes *.old (deleted on the next
/// start) and the new one takes its place; then the app restarts with --updated.
/// </summary>
public sealed class Updater
{
    private const string Repo = "DigitalImmersiveBox/DecisionCouncil";
    private const string ExeName = "DecisionCouncil.exe";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private readonly string _exePath;

    public Version Current { get; }
    /// <summary>The newer release found by the last check, if any.</summary>
    public Release? Available { get; private set; }
    public DateTime? LastChecked { get; private set; }
    public bool Checking { get; private set; }
    public bool Installing { get; private set; }
    /// <summary>0 to 1 while the zip downloads.</summary>
    public double Progress { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Something above changed. Always raised on the thread that called CheckAsync or InstallAsync.</summary>
    public event Action? Changed;

    public Updater(Version? current = null, string? exePath = null)
    {
        Current = Trim(current ?? Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0));
        _exePath = exePath ?? Environment.ProcessPath ?? "";
    }

    /// <summary>
    /// A single-file DecisionCouncil.exe. A dotnet run or Debug build is a loose dll and friends,
    /// so it only gets the link to the releases page.
    /// </summary>
#pragma warning disable IL3000 // an empty Location is exactly how a single-file exe is recognised here
    public bool CanInstall =>
        string.IsNullOrEmpty(typeof(Updater).Assembly.Location) &&
        Path.GetFileName(_exePath).Equals(ExeName, StringComparison.OrdinalIgnoreCase);
#pragma warning restore IL3000

    /// <summary>The zip that matches this build: self-contained, or the small one that needs the .NET runtime.</summary>
    private static bool SelfContained =>
        Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "SelfContained")?.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? true;

    public async Task CheckAsync(CancellationToken cancel = default)
    {
        if (Checking || Installing) return;
        Checking = true;
        Error = null;
        Changed?.Invoke();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            Available = await FindNewer(timeout.Token);
            LastChecked = DateTime.Now;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            Error = "Couldn't reach GitHub to check for updates.";
        }
        finally
        {
            Checking = false;
            Changed?.Invoke();
        }
    }

    private async Task<Release?> FindNewer(CancellationToken cancel)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", cancel));
        var root = doc.RootElement;
        if (!Version.TryParse(root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V'), out var latest))
            throw new InvalidDataException("The latest release has no version tag.");
        latest = Trim(latest);
        if (latest <= Current) return null;

        var suffix = SelfContained ? "-win-x64.zip" : "-win-x64-framework.zip";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            return new Release(latest, root.GetProperty("html_url").GetString() ?? ReleasesPage, name,
                asset.GetProperty("browser_download_url").GetString()!, asset.GetProperty("size").GetInt64(),
                digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? digest[7..] : null);
        }
        throw new InvalidDataException($"Release {latest} has no {suffix} download.");
    }

    /// <summary>Downloads the release and swaps it in. Returns true when it's in place and the app should restart.</summary>
    public async Task<bool> InstallAsync(CancellationToken cancel = default)
    {
        if (Available is not { } release || Installing || !CanInstall) return false;
        Installing = true;
        Progress = 0;
        Error = null;
        Changed?.Invoke();
        var zip = Path.Combine(Path.GetTempPath(), "DecisionCouncil-update", release.ZipName);
        try
        {
            await Download(release, zip, cancel);
            Swap(zip, release.Version);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            Error = $"Decision Council can't replace its own files in {Path.GetDirectoryName(_exePath)}.";
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
        {
            Error = "The update failed: " + ex.Message;
            return false;
        }
        finally
        {
            try { File.Delete(zip); } catch { /* temp file, best effort */ }
            Installing = false;
            Changed?.Invoke();
        }
    }

    private async Task Download(Release release, string zip, CancellationToken cancel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        using var response = await Http.GetAsync(release.ZipUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? release.ZipSize;
        await using (var source = await response.Content.ReadAsStreamAsync(cancel))
        await using (var file = File.Create(zip))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancel)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                done += read;
                var progress = total > 0 ? Math.Min(1, (double)done / total) : 0;
                if (progress - Progress >= 0.01 || progress == 1)
                {
                    Progress = progress;
                    Changed?.Invoke();
                }
            }
        }
        if (release.Sha256 != null)
        {
            await using var file = File.OpenRead(zip);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancel));
            if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("the download doesn't match the release's checksum.");
        }
    }

    /// <summary>
    /// Unpacks the exe next to the old one as DecisionCouncil.exe.new, checks its version, then renames the old one
    /// to DecisionCouncil.exe.old and the new one into its place. Other files in the zip (the licence) are only
    /// refreshed where they're already there.
    /// </summary>
    private void Swap(string zip, Version expected)
    {
        var dir = Path.GetDirectoryName(_exePath)!;
        using var archive = ZipFile.OpenRead(zip);
        var exe = archive.Entries.FirstOrDefault(e => e.FullName.Equals(ExeName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"the download has no {ExeName}.");

        string newExe = _exePath + ".new", oldExe = _exePath + ".old";
        exe.ExtractToFile(newExe, overwrite: true);
        var version = Version.TryParse(FileVersionInfo.GetVersionInfo(newExe).FileVersion, out var v) ? Trim(v) : null;
        if (version != expected)
        {
            File.Delete(newExe);
            throw new InvalidDataException($"the downloaded exe is version {version?.ToString() ?? "unknown"}, not {expected}.");
        }

        File.Delete(oldExe); // left over from the last update if that copy couldn't delete it
        File.Move(_exePath, oldExe);
        try { File.Move(newExe, _exePath); }
        catch
        {
            File.Move(oldExe, _exePath); // put the old one back
            throw;
        }

        foreach (var entry in archive.Entries)
        {
            var target = Path.Combine(dir, entry.Name);
            if (entry != exe && entry.FullName == entry.Name && entry.Name.Length > 0 && File.Exists(target))
                try { entry.ExtractToFile(target, overwrite: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Starts the new exe. It waits for this one to exit before it takes over.</summary>
    public void Restart() =>
        Process.Start(new ProcessStartInfo(_exePath, "--updated") { UseShellExecute = false });

    /// <summary>Deletes the DecisionCouncil.exe.old an update left behind; the previous copy may take a moment to let go of it.</summary>
    public async Task CleanUpAsync()
    {
        var old = _exePath + ".old";
        for (var attempt = 0; attempt < 10 && File.Exists(old); attempt++)
        {
            try { File.Delete(old); return; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { return; }
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    /// <summary>1.0.1.0 → 1.0.1, so the assembly version and the release tag compare equal.</summary>
    private static Version Trim(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DecisionCouncil-Updater"); // GitHub's API refuses requests without one
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
