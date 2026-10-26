using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using DecisionCouncil.App.Infrastructure;
using DecisionCouncil.Core;
using DecisionCouncil.Core.Providers;
using DecisionCouncil.Core.Settings;
using DecisionCouncil.Core.Usage;

namespace DecisionCouncil.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IPage
{
    private readonly MainViewModel _main;
    private AppSettings _edit;
    private string _limitText = "";
    private string _checkResult = "";
    private string _resolvedCli = "";
    private string _signInStatus = "";
    private bool _isSignedIn;
    private int _signInRequest;
    private UsageState? _usage;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        _edit = main.Council.Settings.Clone();
        SaveCommand = new RelayCommand(Save);
        RevertCommand = new RelayCommand(Load);
        CheckCommand = new AsyncCommand(Check);
        SignInCommand = new AsyncCommand(SignIn);
        RefreshSignInCommand = new AsyncCommand(RefreshSignIn);
        UpdateCommand = new RelayCommand(() =>
        {
            if (Updater.Available is not null) _ = _main.InstallUpdate();
            else _ = Updater.CheckAsync();
        });
        Updater.Changed += RaiseUpdate;
        OpenDataFolderCommand = new RelayCommand(() => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_main.Council.Settings.DataFolder}\"")));
        Load();
    }

    public List<string> ModelSuggestions { get; } = ["haiku", "sonnet", "opus"];
    public List<string> LanguageSuggestions { get; } = ["English", "Romanian", "Italian", "German", "French", "Spanish"];

    public ProviderKind Provider
    {
        get => _edit.Provider;
        set
        {
            _edit.Provider = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCli));
            OnPropertyChanged(nameof(IsDemoProvider));
            if (IsCli && SignInStatus.Length == 0) _ = RefreshSignIn();
        }
    }
    public bool IsCli => _edit.Provider == ProviderKind.ClaudeCli;

    // Two-way segment buttons (Demo | Claude Code CLI, On | Off)
    public bool IsDemoProvider { get => !IsCli; set { if (value) Provider = ProviderKind.Demo; } }
    public bool IsCliProvider { get => IsCli; set { if (value) Provider = ProviderKind.ClaudeCli; } }
    public bool Round2Off { get => !Round2Default; set { if (value) Round2Default = false; } }

    public string UsageFigure => $"${_usage?.Month.CostUsd ?? 0:0.00}";
    public string UsageDetail => _usage is null ? "" :
        _usage.HasLimit ? $"of ${_usage.LimitUsd:0.00} this month · {_usage.Month.Calls} calls" : $"this month · {_usage.Month.Calls} calls · no limit set";

    public string ClaudeExecutable { get => _edit.ClaudeExecutable; set { _edit.ClaudeExecutable = value; OnPropertyChanged(); } }
    public string ClaudeExtraArgs { get => _edit.ClaudeExtraArgs; set { _edit.ClaudeExtraArgs = value; OnPropertyChanged(); } }
    public string AgentModel { get => _edit.AgentModel; set { _edit.AgentModel = value; OnPropertyChanged(); } }
    public string SynthesizerModel { get => _edit.SynthesizerModel; set { _edit.SynthesizerModel = value; OnPropertyChanged(); } }
    public string AnswerLanguage { get => _edit.AnswerLanguage; set { _edit.AnswerLanguage = value; OnPropertyChanged(); } }
    public bool Round2Default { get => _edit.Round2Default; set { _edit.Round2Default = value; OnPropertyChanged(); OnPropertyChanged(nameof(Round2Off)); } }
    public int WarnAtPercent { get => _edit.WarnAtPercent; set { _edit.WarnAtPercent = Math.Clamp(value, 1, 100); OnPropertyChanged(); } }
    public int MaxConcurrency { get => _edit.MaxConcurrency; set { _edit.MaxConcurrency = Math.Clamp(value, 1, 8); OnPropertyChanged(); } }
    public int MaxRetries { get => _edit.MaxRetries; set { _edit.MaxRetries = Math.Clamp(value, 0, 5); OnPropertyChanged(); } }
    public int CallTimeoutSeconds { get => _edit.CallTimeoutSeconds; set { _edit.CallTimeoutSeconds = Math.Clamp(value, 30, 1800); OnPropertyChanged(); } }
    public double DemoSpeed { get => _edit.DemoSpeed; set { _edit.DemoSpeed = Math.Clamp(value, 0.5, 20); OnPropertyChanged(); } }
    public string DataFolder { get => _edit.DataFolder; set { _edit.DataFolder = value; OnPropertyChanged(); } }

    /// <summary>Text box for the limit so an empty box means "no limit".</summary>
    public string MonthlyLimitText { get => _limitText; set => Set(ref _limitText, value); }

    public string CheckResult { get => _checkResult; private set => Set(ref _checkResult, value); }
    public string ResolvedCli { get => _resolvedCli; private set => Set(ref _resolvedCli, value); }
    public string SignInStatus { get => _signInStatus; private set => Set(ref _signInStatus, value); }
    public bool IsSignedIn
    {
        get => _isSignedIn;
        private set { if (Set(ref _isSignedIn, value)) OnPropertyChanged(nameof(SignInButtonText)); }
    }
    public string SignInButtonText => IsSignedIn ? "Sign in again" : "Sign in";
    public string UsageText => _usage?.Describe() ?? "";
    public double UsagePercent => Math.Min(100, _usage?.Percent ?? 0);
    public bool HasLimit => _usage?.HasLimit ?? false;
    public string SettingsFile => AppSettings.SettingsFile;

    public ICommand SaveCommand { get; }
    public ICommand RevertCommand { get; }
    public ICommand CheckCommand { get; }
    public ICommand SignInCommand { get; }
    public ICommand RefreshSignInCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand UpdateCommand { get; }
    public ICommand ReleaseNotesCommand => _main.ReleaseNotesCommand;

    private Updater Updater => _main.Updater;
    public string VersionText => $"Version {Updater.Current}";
    public bool UpdateButtonEnabled => !Updater.Checking && !Updater.Installing;
    public string UpdateButtonText => Updater.Available is not null ? (Updater.CanInstall ? "Update now" : "Download") : "Check for updates";
    public string UpdateNote =>
        Updater.Installing ? $"Downloading {Updater.Available?.Version}… {Updater.Progress:P0}"
        : Updater.Checking ? "Checking GitHub…"
        : Updater.Available is { } release ? $"Version {release.Version} is available. Updating restarts the app and keeps your data and settings."
        : Updater.Error ?? (Updater.LastChecked is { } at ? $"You're up to date. Checked at {at:t}." : "Checks GitHub a minute after start, then twice a day.");

    private void RaiseUpdate()
    {
        foreach (var name in new[] { nameof(UpdateButtonEnabled), nameof(UpdateButtonText), nameof(UpdateNote) })
            OnPropertyChanged(name);
    }

    public void OnNavigatedTo()
    {
        Load();
        if (Updater.LastChecked is not { } at || DateTime.Now - at > TimeSpan.FromHours(1)) _ = Updater.CheckAsync();
    }

    private void Load()
    {
        _edit = _main.Council.Settings.Clone();
        _limitText = _edit.MonthlyLimitUsd > 0 ? _edit.MonthlyLimitUsd.ToString("0.##", CultureInfo.InvariantCulture) : "";
        _usage = _main.Council.Usage.Current();
        var resolved = ClaudeCliProvider.ResolveExecutable(_edit.ClaudeExecutable);
        ResolvedCli = resolved is null ? "Not found on PATH." : "Found: " + resolved;
        CheckResult = "";
        OnPropertyChanged(string.Empty);
        if (IsCli) _ = RefreshSignIn();
    }

    private void Save()
    {
        var limitText = MonthlyLimitText.Trim().TrimStart('$', '€');
        if (limitText.Length == 0)
        {
            _edit.MonthlyLimitUsd = 0;
        }
        else if (decimal.TryParse(limitText.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var limit) && limit >= 0)
        {
            _edit.MonthlyLimitUsd = limit;
        }
        else
        {
            Dialogs.Error("The monthly limit must be a number in USD, for example 50, or empty for no limit.");
            return;
        }

        var folderChanged = !string.Equals(_edit.DataFolder.TrimEnd('\\'), _main.Council.Settings.DataFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        if (_edit.Provider == ProviderKind.ClaudeCli && _main.Council.Settings.Provider == ProviderKind.Demo &&
            !Dialogs.Confirm("Switch to the Claude Code CLI? From now on, debates and test runs use your Claude plan's credit for programmatic use."))
            return;

        _main.Council.UpdateSettings(_edit.Clone());
        _main.RefreshStatus();
        Load();
        Dialogs.Info(folderChanged
            ? "Settings saved. The new data folder is used after you restart the app."
            : "Settings saved.");
    }

    private async Task Check()
    {
        CheckResult = "Checking…";
        var provider = Council.CreateProvider(_edit);
        var result = await provider.CheckAsync(CancellationToken.None);
        CheckResult = (result.Ok ? "✓ " : "✗ ") + result.Message;
        var resolved = ClaudeCliProvider.ResolveExecutable(_edit.ClaudeExecutable);
        ResolvedCli = resolved is null ? "Not found on PATH." : "Found: " + resolved;
    }

    private ClaudeCliProvider SignInProvider() => new(new ClaudeCliOptions { Executable = _edit.ClaudeExecutable });

    private async Task RefreshSignIn()
    {
        // Settings can ask more than once (open page, switch provider, button); only the latest answer counts.
        var request = ++_signInRequest;
        SignInStatus = "Checking sign-in…";
        var result = await SignInProvider().GetSignInAsync(CancellationToken.None);
        if (request != _signInRequest) return;
        IsSignedIn = result.SignedIn;
        SignInStatus = (result.SignedIn ? "✓ " : "✗ ") + result.Message;
    }

    private async Task SignIn()
    {
        SignInStatus = "Finish signing in in the console window and your browser, then close the window.";
        await SignInProvider().SignInAsync(CancellationToken.None);
        await RefreshSignIn();
    }
}
