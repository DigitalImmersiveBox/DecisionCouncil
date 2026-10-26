using System.Collections.ObjectModel;
using System.Windows.Input;
using DecisionCouncil.App.Infrastructure;
using DecisionCouncil.Core;

namespace DecisionCouncil.App.ViewModels;

public sealed class JournalItemViewModel(DebateSummary s)
{
    public DebateSummary Summary { get; } = s;
    public long DebateId => Summary.DebateId;
    public string Title => Summary.IdeaTitle;
    public string Date => Summary.StartedAt.ToString("d MMM yyyy");
    public string StatusText => Summary.Status switch
    {
        DebateStatus.Completed => $"{Summary.AgentCount} agents",
        DebateStatus.CompletedWithErrors => $"{Summary.AgentCount} agents · some calls failed",
        DebateStatus.StoppedAtLimit => "stopped at limit",
        _ => Summary.Status.ToString().ToLowerInvariant(),
    };
    public string Decision => Summary.Decision?.Choice ?? "No decision yet";
    public bool HasDecision => Summary.Decision is not null;
    public string Revisit => Summary.Decision switch
    {
        { OutcomeLoggedAt: not null } => "Outcome logged",
        { RevisitOn: { } r } => $"Revisit {r:d MMM yyyy}",
        _ => "",
    };
    public bool IsDue => Summary.Decision?.IsDue ?? false;

    /// <summary>Status chip, in Session Deck's colours: orange needs you, amber open, green decided, grey closed.</summary>
    public string StateText => Summary.Decision switch
    {
        { OutcomeLoggedAt: not null } => "Closed",
        { IsDue: true } => "Review due",
        not null => "Decided",
        _ => "Open",
    };

    public string StateColor => StateText switch
    {
        "Review due" => "#FF7A59",
        "Open" => "#F5B83D",
        "Decided" => "#4ADE80",
        _ => "#6B707B",
    };
    public bool IsSimulated => Summary.Provider.Contains("Demo", StringComparison.OrdinalIgnoreCase);
}

public sealed class WarningOption(string agentId, string name, bool isChecked) : ObservableObject
{
    private bool _isChecked = isChecked;
    public string AgentId { get; } = agentId;
    public string Name { get; } = name;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
}

public sealed class JournalViewModel : ObservableObject, IPage
{
    private readonly MainViewModel _main;
    private JournalItemViewModel? _selected;
    private DebateDetail? _detail;
    private string _outcome = "";
    private int _dueCount;

    public JournalViewModel(MainViewModel main)
    {
        _main = main;
        OpenCommand = new RelayCommand(() => { if (Selected is { } s) _main.ShowResult(s.DebateId); }, () => Selected is not null);
        SaveOutcomeCommand = new RelayCommand(SaveOutcome, () => _detail?.Decision is not null && !string.IsNullOrWhiteSpace(Outcome));
        Refresh();
    }

    public ObservableCollection<JournalItemViewModel> Items { get; } = [];
    public ObservableCollection<WarningOption> Warnings { get; } = [];

    public JournalItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            LoadDetail();
        }
    }

    public bool HasItems => Items.Count > 0;
    public bool HasSelection => Selected is not null;
    public int DueCount { get => _dueCount; private set => Set(ref _dueCount, value); }
    public string DueText => DueCount == 1 ? "1 decision is due for review." : $"{DueCount} decisions are due for review.";

    public string IdeaText => _detail?.Idea.Text ?? "";
    public bool HasDecision => _detail?.Decision is not null;
    public string DecisionText => _detail?.Decision is { } d
        ? $"**{d.Choice}** on {d.DecidedAt:d MMM yyyy}" + (string.IsNullOrWhiteSpace(d.Reason) ? "" : $"\n\n{d.Reason}")
          + (d.RevisitOn is { } r ? $"\n\nRevisit on {r:d MMM yyyy}." : "")
        : "";
    public string OutcomeLoggedText => _detail?.Decision?.OutcomeLoggedAt is { } t ? $"Outcome logged {t:d MMM yyyy}. You can update it." : "";

    public string Outcome { get => _outcome; set => Set(ref _outcome, value); }

    public ICommand OpenCommand { get; }
    public ICommand SaveOutcomeCommand { get; }

    public void OnNavigatedTo() => Refresh();

    public void Refresh()
    {
        var keep = Selected?.DebateId;
        Items.Clear();
        foreach (var s in _main.Council.Store.ListDebates()) Items.Add(new JournalItemViewModel(s));
        DueCount = Items.Count(i => i.IsDue);
        BuildTrackRecord();
        OnPropertyChanged(nameof(DueText));
        OnPropertyChanged(nameof(HasItems));
        _selected = null;
        Selected = Items.FirstOrDefault(i => i.DebateId == keep) ?? Items.FirstOrDefault(i => i.IsDue) ?? Items.FirstOrDefault();
    }

    /// <summary>"Which agents earn their place": how often each agent's warning came true in logged outcomes.</summary>
    public string TrackRecord { get; private set; } = "";

    private void BuildTrackRecord()
    {
        var logged = Items.Select(i => i.Summary.Decision).Where(d => d?.OutcomeLoggedAt is not null).ToList();
        if (logged.Count == 0)
        {
            TrackRecord = "";
        }
        else
        {
            var names = _main.Council.LoadAgents().ToDictionary(a => a.Id, a => a.Name);
            var counts = logged.SelectMany(d => d!.WarningsCameTrue).GroupBy(id => id)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{names.GetValueOrDefault(g.Key, g.Key)} {g.Count()}");
            TrackRecord = $"Warnings that came true, over {logged.Count} logged outcome{(logged.Count == 1 ? "" : "s")}: " + string.Join(" · ", counts);
        }
        OnPropertyChanged(nameof(TrackRecord));
    }

    private void LoadDetail()
    {
        _detail = Selected is null ? null : _main.Council.Store.GetDebate(Selected.DebateId);
        Warnings.Clear();
        if (_detail is not null)
        {
            var came = _detail.Decision?.WarningsCameTrue ?? [];
            foreach (var g in _detail.Runs.Where(r => r.Round == DebateRound.Round1).GroupBy(r => r.AgentId))
                Warnings.Add(new WarningOption(g.Key, g.First().AgentName, came.Contains(g.Key)));
        }
        Outcome = _detail?.Decision?.Outcome ?? "";
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IdeaText));
        OnPropertyChanged(nameof(HasDecision));
        OnPropertyChanged(nameof(DecisionText));
        OnPropertyChanged(nameof(OutcomeLoggedText));
    }

    private void SaveOutcome()
    {
        if (_detail is null) return;
        _main.Council.Store.LogOutcome(_detail.Id, Outcome.Trim(), Warnings.Where(w => w.IsChecked).Select(w => w.AgentId));
        Refresh();
    }
}
