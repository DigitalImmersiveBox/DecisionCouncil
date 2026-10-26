using System.IO;
using System.Windows.Input;
using DecisionCouncil.App.Infrastructure;
using DecisionCouncil.Core;
using DecisionCouncil.Core.Debate;
using DecisionCouncil.Core.Export;
using Microsoft.Win32;

namespace DecisionCouncil.App.ViewModels;

/// <summary>One agent's answers across both rounds, shown as an expandable card on the result screen.</summary>
public sealed class AgentResultViewModel
{
    public required string Name { get; init; }
    public required string Color { get; init; }
    public required string AgentId { get; init; }
    public string Avatar { get; init; } = "";
    public ColumnState AvatarState =>
        new[] { Round1, Round2 }.Any(r => r is { Status: RunStatus.Done }) ? ColumnState.Done : ColumnState.Failed;
    public AgentRun? Round1 { get; init; }
    public AgentRun? Round2 { get; init; }

    public string? Round1Answer => Round1 is null ? null : Round1.Status == RunStatus.Done ? Round1.Answer : $"*{Round1.Status}: {Round1.Error}*";
    public string? Round2Answer => Round2 is null ? null : Round2.Status == RunStatus.Done ? Round2.Answer : $"*{Round2.Status}: {Round2.Error}*";
    public string? Verdict => Round2?.Verdict ?? Round1?.Verdict;

    /// <summary>"3/5 → 4/5" when round 2 changed it.</summary>
    public string ConfidenceText
    {
        get
        {
            var a = Round1?.Confidence;
            var b = Round2?.Confidence;
            if (a is null && b is null) return "";
            if (b is null || a == b) return $"confidence {a ?? b}/5";
            return $"confidence {a?.ToString() ?? "?"}/5 → {b}/5";
        }
    }

    public string VerdictText
    {
        get
        {
            var a = Round1?.Verdict;
            var b = Round2?.Verdict;
            return a is not null && b is not null && a != b ? $"{a} → {b}" : b ?? a ?? "";
        }
    }

    public string PromptText => Round1 is null ? "" :
        "SYSTEM PROMPT\n" + new string('─', 40) + "\n" + Round1.SystemPrompt + "\n\nUSER PROMPT\n" + new string('─', 40) + "\n" + Round1.UserPrompt;

    public string MetaText
    {
        get
        {
            var runs = new[] { Round1, Round2 }.OfType<AgentRun>().ToList();
            var secs = runs.Sum(r => r.DurationMs) / 1000.0;
            var tokens = runs.Sum(r => (r.InputTokens ?? 0) + (r.OutputTokens ?? 0));
            var model = runs.FirstOrDefault()?.Model ?? "";
            return $"{model} · {secs:0.0} s · {tokens:N0} tokens";
        }
    }
}

public sealed class ResultViewModel : ObservableObject, IPage
{
    public static readonly string[] Choices = ["Go", "Go with changes", "Wait / test first", "No-go"];

    private readonly MainViewModel _main;
    private DebateDetail _detail;
    private bool _isDecisionFormOpen;
    private string _choice = "";
    private string _reason = "";
    private DateTime? _revisitOn = DateTime.Today.AddMonths(3);

    public ResultViewModel(MainViewModel main, DebateDetail detail)
    {
        _main = main;
        _detail = detail;
        OpenDecisionFormCommand = new RelayCommand(OpenForm);
        CancelDecisionCommand = new RelayCommand(() => IsDecisionFormOpen = false);
        SaveDecisionCommand = new RelayCommand(SaveDecision, () => !string.IsNullOrWhiteSpace(Choice));
        ExportCommand = new RelayCommand(Export);
        BackCommand = new RelayCommand(() => _main.GoTo("journal"));
        SetRevisitCommand = new RelayCommand(p =>
            RevisitOn = int.TryParse(p as string, out var months) && months > 0 ? DateTime.Today.AddMonths(months) : null);
        Build();
    }

    public long DebateId => _detail.Id;
    public string Title => _detail.Idea.Title;
    public string IdeaText => _detail.Idea.Text;
    public string ExtraContext => _detail.Idea.ExtraContext;
    public string Subtitle
    {
        get
        {
            var usage = _detail.Runs.All(r => r.Simulated)
                ? "simulated, no credit used"
                : $"{_detail.TotalTokens:N0} tokens · ≈${_detail.TotalCost:0.0000} at API prices";
            return $"{_detail.StartedAt:d MMM yyyy, HH:mm} · {_detail.Provider} · {_detail.Runs.Count} calls · {usage}";
        }
    }

    public string StatusText => _detail.Status switch
    {
        DebateStatus.Completed => "",
        DebateStatus.CompletedWithErrors => "Some calls failed: " + _detail.StatusMessage,
        DebateStatus.Running => "This debate is still running.",
        _ => $"{_detail.Status}: {_detail.StatusMessage}",
    };

    public string? Synthesis => _detail.Synthesis is { Status: RunStatus.Done } s ? s.Answer : null;
    public bool HasSynthesis => Synthesis is not null;
    public bool Round2Ran => _detail.Runs.Any(r => r.Round == DebateRound.Round2);
    public List<AgentResultViewModel> Agents { get; private set; } = [];

    public Decision? Decision => _detail.Decision;
    public bool HasDecision => Decision is not null;
    public string DecisionSummary => Decision is null ? "" :
        $"{Decision.Choice} · decided {Decision.DecidedAt:d MMM yyyy}" + (Decision.RevisitOn is { } r ? $" · revisit {r:d MMM yyyy}" : "");

    public bool IsDecisionFormOpen { get => _isDecisionFormOpen; set => Set(ref _isDecisionFormOpen, value); }
    public string[] ChoiceOptions => Choices;
    public string Choice { get => _choice; set => Set(ref _choice, value); }
    public string Reason { get => _reason; set => Set(ref _reason, value); }
    public DateTime? RevisitOn
    {
        get => _revisitOn;
        set { if (Set(ref _revisitOn, value)) OnPropertyChanged(nameof(RevisitText)); }
    }

    public string RevisitText => RevisitOn is { } r ? $"Revisit on {r:d MMM yyyy}" : "No revisit date";
    public ICommand SetRevisitCommand { get; }

    public ICommand OpenDecisionFormCommand { get; }
    public ICommand CancelDecisionCommand { get; }
    public ICommand SaveDecisionCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand BackCommand { get; }

    private void Build()
    {
        var names = _detail.Runs.Where(r => r.Round != DebateRound.Synthesis)
            .GroupBy(r => r.AgentId)
            .Select(g => g.Key)
            .ToList();
        var known = _main.Council.LoadAgents().ToDictionary(a => a.Id);
        Agents = _detail.AgentIds.Union(names).Select(id => new AgentResultViewModel
        {
            Name = _detail.Runs.FirstOrDefault(r => r.AgentId == id)?.AgentName ?? id,
            AgentId = id,
            Color = known.GetValueOrDefault(id)?.Color ?? "#6B707B",
            Avatar = known.GetValueOrDefault(id)?.Avatar ?? "",
            Round1 = _detail.Runs.FirstOrDefault(r => r.AgentId == id && r.Round == DebateRound.Round1),
            Round2 = _detail.Runs.FirstOrDefault(r => r.AgentId == id && r.Round == DebateRound.Round2),
        }).Where(a => a.Round1 is not null || a.Round2 is not null).ToList();
        OnPropertyChanged(string.Empty);
    }

    private void OpenForm()
    {
        if (Decision is { } d)
        {
            Choice = d.Choice;
            Reason = d.Reason;
            RevisitOn = d.RevisitOn?.ToDateTime(TimeOnly.MinValue);
        }
        IsDecisionFormOpen = true;
    }

    private void SaveDecision()
    {
        _main.Council.Store.SaveDecision(new Decision
        {
            DebateId = _detail.Id,
            Choice = Choice.Trim(),
            Reason = Reason.Trim(),
            DecidedAt = DateTime.Now,
            RevisitOn = RevisitOn is { } r ? DateOnly.FromDateTime(r) : null,
        });
        _detail = _main.Council.Store.GetDebate(_detail.Id) ?? _detail;
        IsDecisionFormOpen = false;
        Build();
        _main.Journal.Refresh();
    }

    private void Export()
    {
        Directory.CreateDirectory(_main.Council.Settings.ExportsFolder);
        var dialog = new SaveFileDialog
        {
            Title = "Export debate as Markdown",
            Filter = "Markdown (*.md)|*.md",
            FileName = MarkdownExporter.SuggestFileName(_detail),
            InitialDirectory = _main.Council.Settings.ExportsFolder,
        };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllText(dialog.FileName, MarkdownExporter.Export(_detail));
        Dialogs.Info($"Exported to {dialog.FileName}");
    }
}
