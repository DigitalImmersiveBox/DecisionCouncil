using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DecisionCouncil.App.Infrastructure;
using DecisionCouncil.Core;
using DecisionCouncil.Core.Agents;
using DecisionCouncil.Core.Settings;
using DecisionCouncil.Core.Usage;

namespace DecisionCouncil.App.ViewModels;

/// <summary>A row in the left panel; the badge is the small count on the right, like Session Deck's environments.</summary>
public sealed class NavItem(string key, string title, string glyph) : ObservableObject
{
    private string _badge = "";
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public string Badge { get => _badge; set => Set(ref _badge, value); }
}

/// <summary>Placeholder page with a short message (for example "no debate running").</summary>
public sealed class MessagePageViewModel(string title, string message) : ObservableObject, IPage
{
    public string Title { get; } = title;
    public string Message { get; } = message;
}

public sealed class MainViewModel : ObservableObject
{
    private object? _currentPage;
    private NavItem? _selectedNav;
    private bool _suppressNav;
    private string _usageText = "";
    private string _providerText = "";
    private UsageLevel _usageLevel;
    private Version? _announced;
    private bool _justUpdated;
    private bool _bannerDismissed;
    private bool _closingForUpdate;

    public Council Council { get; }
    public Updater Updater { get; } = new();
    public ObservableCollection<NavItem> Nav { get; } =
    [
        new("new", "New idea", ""),
        new("live", "Live debate", ""),
        new("journal", "Journal", ""),
        new("agents", "Agents", ""),
        new("context", "My context", ""),
        new("settings", "Settings", ""),
    ];

    public NewIdeaViewModel NewIdea { get; }
    public JournalViewModel Journal { get; }
    public AgentsViewModel Agents { get; }
    public ContextViewModel Context { get; }
    public SettingsViewModel SettingsPage { get; }
    public LiveDebateViewModel? Live { get; private set; }

    public MainViewModel(Council council)
    {
        Council = council;
        NewIdea = new NewIdeaViewModel(this);
        Journal = new JournalViewModel(this);
        Agents = new AgentsViewModel(this);
        Context = new ContextViewModel(this);
        SettingsPage = new SettingsViewModel(this);
        UpdateNowCommand = new AsyncCommand(InstallUpdate, () => Updater.Available is not null && !Updater.Installing);
        ReleaseNotesCommand = new RelayCommand(() => OpenLink(Updater.Available?.Page ?? Updater.ReleasesPage));
        DismissUpdateCommand = new RelayCommand(() => { _bannerDismissed = true; _justUpdated = false; RaiseUpdateBanner(); });
        Updater.Changed += RaiseUpdateBanner;
        RefreshStatus();
        SelectedNav = Nav[Journal.DueCount > 0 ? 2 : 0];
    }

    public object? CurrentPage
    {
        get => _currentPage;
        private set => Set(ref _currentPage, value);
    }

    public NavItem? SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (_suppressNav || value is null || value == _selectedNav) { Set(ref _selectedNav, value); return; }
            if (!Navigate(PageFor(value.Key)))
            {
                // Navigation refused (unsaved edits): make the list snap back to the current item.
                OnPropertyChanged();
                return;
            }
            Set(ref _selectedNav, value);
        }
    }

    public string UsageText { get => _usageText; private set => Set(ref _usageText, value); }
    public string ProviderText { get => _providerText; private set => Set(ref _providerText, value); }
    public UsageLevel UsageLevel { get => _usageLevel; private set => Set(ref _usageLevel, value); }
    public bool IsDemo => Council.Settings.Provider == ProviderKind.Demo;
    public string DataFolder => Council.Settings.DataFolder;

    // Header summary, like Session Deck's "● 4 need you ● 2 working ● 2 done"
    public string ModeText => IsDemo ? "demo mode" : "Claude Code CLI";
    public string DueText { get; private set; } = "";
    public string DebatesText { get; private set; } = "";
    public bool HasLimit { get; private set; }

    /// <summary>The dib mascot in the header thinks while a debate runs.</summary>
    public ColumnState MascotState => Live is { IsRunning: true } ? ColumnState.Running : ColumnState.Waiting;
    public double UsagePercent { get; private set; }

    public void RefreshStatus()
    {
        var state = Council.Usage.Current();
        UsageText = IsDemo && state.Month.Calls == 0
            ? "no real calls yet"
            : state.HasLimit ? $"≈${state.Month.CostUsd:0.00} of ${state.LimitUsd:0.00} API · {state.Month.Calls} calls" : $"≈${state.Month.CostUsd:0.00} at API prices · {state.Month.Calls} calls";
        UsageLevel = state.Level;
        HasLimit = state.HasLimit;
        UsagePercent = Math.Min(100, state.Percent);
        ProviderText = IsDemo ? "Demo, simulated" : $"CLI · {Council.Settings.AgentModel} / {Council.Settings.SynthesizerModel}";

        var debates = Journal.Items.Count;
        var due = Journal.DueCount;
        DueText = due == 0 ? "" : $"{due} due for review";
        DebatesText = debates == 1 ? "1 debate" : $"{debates} debates";

        Nav.First(n => n.Key == "journal").Badge = due > 0 ? $"{due} due" : debates == 0 ? "" : debates.ToString();
        Nav.First(n => n.Key == "agents").Badge = Council.LoadAgents().Count(a => a.Enabled).ToString();
        Nav.First(n => n.Key == "live").Badge = Live is { IsRunning: true } ? "running" : "";

        foreach (var name in new[] { nameof(IsDemo), nameof(ModeText), nameof(DueText), nameof(DebatesText), nameof(HasLimit), nameof(UsagePercent), nameof(MascotState) })
            OnPropertyChanged(name);
    }

    public void StartDebate(Idea idea, IReadOnlyList<AgentDefinition> agents, bool round2)
    {
        if (Live is { IsRunning: true })
        {
            Dialogs.Info("A debate is already running. Wait for it to finish or cancel it first.");
            GoTo("live");
            return;
        }
        Live = new LiveDebateViewModel(this, idea, agents, round2);
        OnPropertyChanged(nameof(Live));
        GoTo("live");
        Live.Start();
        RefreshStatus();
    }

    public void ShowResult(long debateId)
    {
        var detail = Council.Store.GetDebate(debateId);
        if (detail is null) { Dialogs.Error($"Debate #{debateId} was not found."); return; }
        if (!Navigate(new ResultViewModel(this, detail))) return;
        _suppressNav = true;
        SelectedNav = Nav.First(n => n.Key == "journal");
        _suppressNav = false;
    }

    public void GoTo(string key)
    {
        var item = Nav.First(n => n.Key == key);
        if (item == _selectedNav) { Navigate(PageFor(key)); return; }
        SelectedNav = item;
    }

    // ---- Updates (the same flow as Session Deck, with a banner in place of its tray pop-up) ----

    public ICommand UpdateNowCommand { get; }
    public ICommand ReleaseNotesCommand { get; }
    public ICommand DismissUpdateCommand { get; }

    /// <summary>Shown under the header when a release is available, downloading, or was just installed.</summary>
    public bool ShowUpdateBanner => Updater.Installing || (!_bannerDismissed && (_justUpdated || Updater.Available is not null));
    public bool CanUpdateNow => Updater.Available is not null && !Updater.Installing;
    public string UpdateBannerText =>
        Updater.Installing ? $"Downloading Decision Council {Updater.Available?.Version}… {Updater.Progress:P0}"
        : Updater.Available is { } release ? $"Decision Council {release.Version} is available. Updating restarts the app and keeps your data and settings."
        : _justUpdated ? $"Decision Council was updated to {Updater.Current}."
        : "";

    /// <summary>Checks GitHub a minute after start and then twice a day, and shows the banner once for each new release.</summary>
    public void StartUpdateChecks(bool justUpdated)
    {
        _justUpdated = justUpdated;
        _ = Updater.CleanUpAsync();
        RaiseUpdateBanner();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Tick += async (_, _) =>
        {
            timer.Interval = TimeSpan.FromHours(12);
            await Updater.CheckAsync();
            if (Updater.Available is { } release && release.Version != _announced)
            {
                _announced = release.Version;
                _bannerDismissed = false;
                _justUpdated = false;
                RaiseUpdateBanner();
            }
        };
        timer.Start();
    }

    /// <summary>Downloads and swaps in the new version, then hands over to it. A build that can't update itself gets the download page.</summary>
    public async Task InstallUpdate()
    {
        if (Updater.Available is not { } release) return;
        if (!Updater.CanInstall)
        {
            OpenLink(release.Page);
            return;
        }
        if (Live is { IsRunning: true })
        {
            Dialogs.Info("A debate is running. Let it finish or cancel it, then update.");
            return;
        }
        if (CurrentPage is IPage page && !page.CanLeave()) return;
        if (!Dialogs.Confirm($"Update Decision Council from {Updater.Current} to {release.Version}?\n\n" +
                             $"It downloads {release.ZipSize / 1048576.0:0.#} MB, then restarts. Your data and settings stay as they are."))
            return;

        if (await Updater.InstallAsync())
        {
            _closingForUpdate = true;
            Updater.Restart();
            Application.Current.Shutdown();
        }
        else if (Updater.Error is { } error && Dialogs.Confirm(error + "\n\nOpen the download page instead?"))
        {
            OpenLink(release.Page);
        }
    }

    public static void OpenLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Dialogs.Error("Could not open the browser: " + ex.Message); }
    }

    private void RaiseUpdateBanner()
    {
        OnPropertyChanged(nameof(ShowUpdateBanner));
        OnPropertyChanged(nameof(CanUpdateNow));
        OnPropertyChanged(nameof(UpdateBannerText));
    }

    /// <summary>Asks the window whether it may close: confirms when a debate is running or edits are unsaved.</summary>
    public bool CanClose()
    {
        if (_closingForUpdate) return true; // already checked before the update started
        if (CurrentPage is IPage page && !page.CanLeave()) return false;
        if (Live is { IsRunning: true })
        {
            if (!Dialogs.Confirm("A debate is still running. Close anyway? Answers that finished are already saved.")) return false;
            Live.Cancel();
        }
        return true;
    }

    private object PageFor(string key) => key switch
    {
        "new" => NewIdea,
        "live" => (object?)Live ?? new MessagePageViewModel("Live debate", "No debate yet. Write an idea on the New idea screen and press Start."),
        "journal" => Journal,
        "agents" => Agents,
        "context" => Context,
        "settings" => SettingsPage,
        _ => NewIdea,
    };

    private bool Navigate(object page)
    {
        if (ReferenceEquals(page, CurrentPage)) { (page as IPage)?.OnNavigatedTo(); return true; }
        if (CurrentPage is IPage current && !current.CanLeave()) return false;
        CurrentPage = page;
        (page as IPage)?.OnNavigatedTo();
        RefreshStatus();
        return true;
    }
}
