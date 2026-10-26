using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using DecisionCouncil.App.Infrastructure;
using DecisionCouncil.Core;
using DecisionCouncil.Core.Agents;
using DecisionCouncil.Core.Debate;

namespace DecisionCouncil.App.ViewModels;

public enum ColumnState
{
    Waiting,
    Running,
    Retrying,
    Done,
    Failed,
    Cancelled,
    Skipped,
}

/// <summary>One agent's column in a round tab, or its seat on the council stage.</summary>
public sealed class AgentColumnViewModel(string agentId, string name, string color, string avatar = "") : ObservableObject
{
    private ColumnState _state;
    private string? _answer;
    private string _meta = "";
    private string? _error;
    private DateTime? _startedAt;

    public string AgentId { get; } = agentId;
    public string Name { get; } = name;
    public string Color { get; } = color;
    public string Avatar { get; } = avatar;

    public ColumnState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    public string StateText => State switch
    {
        ColumnState.Waiting => "Waiting",
        ColumnState.Running => "Thinking…",
        ColumnState.Retrying => "Retrying…",
        ColumnState.Done => "Done",
        ColumnState.Failed => "Failed",
        ColumnState.Cancelled => "Cancelled",
        _ => "Skipped",
    };

    public bool IsBusy => State is ColumnState.Running or ColumnState.Retrying;

    public string? Answer
    {
        get => _answer;
        set
        {
            if (!Set(ref _answer, value)) return;
            OnPropertyChanged(nameof(Confidence));
            OnPropertyChanged(nameof(Verdict));
        }
    }

    public string? Confidence => AnswerParser.Confidence(Answer) is { } c ? $"{c}/5" : null;
    public string? Verdict => AnswerParser.Verdict(Answer);
    public string Meta { get => _meta; set => Set(ref _meta, value); }
    public string? Error { get => _error; set => Set(ref _error, value); }

    public void Started()
    {
        _startedAt = DateTime.Now;
        State = ColumnState.Running;
        Tick();
    }

    public void Tick()
    {
        if (IsBusy && _startedAt is { } t) Meta = $"{(DateTime.Now - t).TotalSeconds:0} s";
    }
}

public sealed class RoundViewModel(string title) : ObservableObject
{
    private string _note = "";
    public string Title { get; } = title;
    public ObservableCollection<AgentColumnViewModel> Columns { get; } = [];
    public string Note { get => _note; set => Set(ref _note, value); }
}

public sealed class LiveDebateViewModel : ObservableObject, IPage
{
    private readonly MainViewModel _main;
    private readonly IReadOnlyList<AgentDefinition> _agents;
    private readonly bool _round2;
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _timer;
    private int _selectedTab;
    private int _doneCalls;
    private int _totalCalls;
    private bool _isRunning;
    private string _status = "Starting…";
    private long? _debateId;
    private bool _finishedOk;

    public LiveDebateViewModel(MainViewModel main, Idea idea, IReadOnlyList<AgentDefinition> agents, bool round2)
    {
        _main = main;
        _agents = agents;
        _round2 = round2;
        Idea = idea;
        _totalCalls = DebateEngine.CallCount(agents.Count, round2);

        foreach (var a in agents)
        {
            Round1.Columns.Add(new AgentColumnViewModel(a.Id, a.Name, a.Color, a.Avatar));
            Round2.Columns.Add(new AgentColumnViewModel(a.Id, a.Name, a.Color, a.Avatar));
            Stage.Add(new AgentColumnViewModel(a.Id, a.Name, a.Color, a.Avatar));
        }
        Synthesis.Columns.Add(new AgentColumnViewModel(DebateEngine.SynthesizerId, DebateEngine.SynthesizerName, "#C15F3C", "dib"));
        Stage.Add(new AgentColumnViewModel(DebateEngine.SynthesizerId, DebateEngine.SynthesizerName, "#C15F3C", "dib"));
        if (!round2) Round2.Note = "Round 2 is switched off for this debate.";

        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        OpenResultCommand = new RelayCommand(() => _main.ShowResult(_debateId!.Value), () => _debateId is not null && !IsRunning);
        NewIdeaCommand = new RelayCommand(() => _main.GoTo("new"), () => !IsRunning);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => { foreach (var c in AllColumns) c.Tick(); };
    }

    public Idea Idea { get; }
    public string ProviderName => _main.Council.CreateProvider().Name;
    public RoundViewModel Round1 { get; } = new("Round 1 · alone");
    public RoundViewModel Round2 { get; } = new("Round 2 · cross-examination");
    public RoundViewModel Synthesis { get; } = new("Synthesis");

    /// <summary>The council on stage: one seat per agent plus the synthesizer, showing what each is doing right now.</summary>
    public ObservableCollection<AgentColumnViewModel> Stage { get; } = [];

    public int SelectedTab { get => _selectedTab; set => Set(ref _selectedTab, value); }
    public int DoneCalls { get => _doneCalls; private set { Set(ref _doneCalls, value); OnPropertyChanged(nameof(ProgressText)); } }
    public int TotalCalls { get => _totalCalls; private set { Set(ref _totalCalls, value); OnPropertyChanged(nameof(ProgressText)); } }
    public string ProgressText => $"{DoneCalls} of {TotalCalls} calls";
    public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool FinishedOk { get => _finishedOk; private set => Set(ref _finishedOk, value); }

    public ICommand CancelCommand { get; }
    public ICommand OpenResultCommand { get; }
    public ICommand NewIdeaCommand { get; }

    private IEnumerable<AgentColumnViewModel> AllColumns => Round1.Columns.Concat(Round2.Columns).Concat(Synthesis.Columns);

    public async void Start()
    {
        IsRunning = true;
        _timer.Start();
        var progress = new Progress<DebateEvent>(OnEvent);
        try
        {
            var engine = _main.Council.CreateEngine();
            _debateId = await engine.RunAsync(new DebateRequest { Idea = Idea, Agents = _agents, Round2 = _round2 }, progress, _cts.Token);
        }
        catch (Exception ex)
        {
            Status = "Could not run the debate: " + ex.Message;
            Dialogs.Error(Status);
        }
        finally
        {
            _timer.Stop();
            IsRunning = false;
            _main.RefreshStatus();
            _main.Journal.Refresh();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public void Cancel()
    {
        if (!IsRunning) return;
        Status = "Cancelling…";
        _cts.Cancel();
    }

    private void OnEvent(DebateEvent e)
    {
        switch (e)
        {
            case DebateCreated created:
                _debateId = created.DebateId;
                break;

            case PhaseStarted ps:
                SelectedTab = (int)ps.Round - 1;
                Status = ps.Round switch
                {
                    DebateRound.Round1 => "Round 1: every agent answers alone.",
                    DebateRound.Round2 => "Round 2: agents read each other and cross-examine.",
                    _ => "Synthesis: summarising agreements, splits and assumptions to test.",
                };
                if (ps.Round == DebateRound.Round2)
                {
                    // Agents that failed in round 1 do not take part in round 2.
                    foreach (var col in Round2.Columns.Where(c => !ps.AgentIds.Contains(c.AgentId)))
                        col.State = ColumnState.Skipped;
                    TotalCalls = Round1.Columns.Count + ps.AgentIds.Count + 1;
                }
                break;

            case PhaseSkipped sk:
                if (sk.Round == DebateRound.Round2)
                {
                    Round2.Note = sk.Reason;
                    foreach (var col in Round2.Columns) col.State = ColumnState.Skipped;
                    TotalCalls = Round1.Columns.Count + 1;
                }
                break;

            case CallStarted cs:
                Column(cs.Round, cs.AgentId)?.Started();
                Seat(cs.AgentId)?.Started();
                break;

            case CallRetrying cr when Column(cr.Round, cr.AgentId) is { } col:
                col.State = ColumnState.Retrying;
                col.Error = $"Attempt {cr.Attempt} failed: {cr.Reason}";
                if (Seat(cr.AgentId) is { } retrying) retrying.State = ColumnState.Retrying;
                break;

            case CallFinished cf when Column(cf.Run.Round, cf.Run.AgentId) is { } col:
                var r = cf.Run;
                col.Answer = r.Answer;
                col.Error = r.Error;
                col.State = r.Status switch
                {
                    RunStatus.Done => ColumnState.Done,
                    RunStatus.Cancelled => ColumnState.Cancelled,
                    _ => ColumnState.Failed,
                };
                if (Seat(r.AgentId) is { } seat)
                {
                    // A failed round 2 keeps the round-1 verdict on the flag
                    if (r.Status == RunStatus.Done) seat.Answer = r.Answer;
                    seat.State = col.State;
                }
                var tokens = r.OutputTokens is { } o ? $" · {o} tokens out" : "";
                var cost = r.Simulated ? "" : r.CostUsd is { } c ? $" · ${c:0.0000}" : "";
                col.Meta = $"{r.DurationMs / 1000.0:0.0} s{tokens}{cost}";
                DoneCalls++;
                break;

            case DebateFinished df:
                foreach (var col in AllColumns.Concat(Stage).Where(c => c.State is ColumnState.Waiting or ColumnState.Running))
                    col.State = ColumnState.Cancelled;
                FinishedOk = df.Status is DebateStatus.Completed or DebateStatus.CompletedWithErrors;
                Status = df.Status switch
                {
                    DebateStatus.Completed => "Debate complete. Open the result to read the synthesis and record your decision.",
                    DebateStatus.CompletedWithErrors => "Debate complete with some failed calls. " + df.Message,
                    DebateStatus.Cancelled => "Cancelled. Answers that finished are saved in the journal.",
                    DebateStatus.StoppedAtLimit => "Stopped: " + df.Message,
                    _ => "Failed: " + df.Message,
                };
                if (FinishedOk) SelectedTab = 2;
                break;
        }
    }

    private AgentColumnViewModel? Seat(string agentId) => Stage.FirstOrDefault(s => s.AgentId == agentId);

    private AgentColumnViewModel? Column(DebateRound round, string agentId) => round switch
    {
        DebateRound.Round1 => Round1.Columns.FirstOrDefault(c => c.AgentId == agentId),
        DebateRound.Round2 => Round2.Columns.FirstOrDefault(c => c.AgentId == agentId),
        _ => Synthesis.Columns.FirstOrDefault(),
    };
}
