using System.Collections.ObjectModel;
using System.Windows.Input;
using DecisionCouncil.App.Infrastructure;
using DecisionCouncil.Core;
using DecisionCouncil.Core.Agents;
using DecisionCouncil.Core.Debate;
using DecisionCouncil.Core.Settings;
using DecisionCouncil.Core.Usage;

namespace DecisionCouncil.App.ViewModels;

public sealed class AgentChoice(AgentDefinition agent, bool selected, Action changed) : ObservableObject
{
    private bool _isSelected = selected;

    public AgentDefinition Agent { get; } = agent;
    public string Name => Agent.Name;
    public string Summary => Agent.Summary;
    public string CoreQuestion => Agent.CoreQuestion;
    public string Color => Agent.Color;
    public string Avatar => Agent.Avatar;
    public string Id => Agent.Id;

    /// <summary>Picked members stand ready; left-out ones doze.</summary>
    public ColumnState AvatarState => IsSelected ? ColumnState.Waiting : ColumnState.Skipped;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!Set(ref _isSelected, value)) return;
            OnPropertyChanged(nameof(AvatarState));
            changed();
        }
    }
}

public sealed class NewIdeaViewModel : ObservableObject, IPage
{
    private readonly MainViewModel _main;
    private string _title = "";
    private string _ideaText = "";
    private string _extraContext = "";
    private bool _round2;
    private string _agentErrors = "";
    private bool _contextIsTemplate;

    public NewIdeaViewModel(MainViewModel main)
    {
        _main = main;
        _round2 = main.Council.Settings.Round2Default;
        StartCommand = new RelayCommand(Start, () => !string.IsNullOrWhiteSpace(IdeaText) && Agents.Any(a => a.IsSelected));
        ApplyPresetCommand = new RelayCommand(p => ApplyPreset(p as AgentPreset));
        SelectNoneCommand = new RelayCommand(() => { foreach (var a in Agents) a.IsSelected = false; });
        LoadSampleCommand = new RelayCommand(LoadSample);
        ClearCommand = new RelayCommand(() => { Title = ""; IdeaText = ""; ExtraContext = ""; });
        EditContextCommand = new RelayCommand(() => _main.GoTo("context"));
        ReloadAgents();
    }

    public ObservableCollection<AgentChoice> Agents { get; } = [];
    public List<AgentPreset> Presets => _main.Council.Settings.Presets;

    public string Title { get => _title; set => Set(ref _title, value); }
    public string IdeaText { get => _ideaText; set => Set(ref _ideaText, value); }
    public string ExtraContext { get => _extraContext; set => Set(ref _extraContext, value); }

    public bool Round2
    {
        get => _round2;
        set { if (Set(ref _round2, value)) OnPropertyChanged(nameof(CallSummary)); }
    }

    public string AgentErrors { get => _agentErrors; private set => Set(ref _agentErrors, value); }
    public bool ContextIsTemplate { get => _contextIsTemplate; private set => Set(ref _contextIsTemplate, value); }

    public string CallSummary
    {
        get
        {
            var n = Agents.Count(a => a.IsSelected);
            if (n == 0) return "Pick at least one agent.";
            var calls = DebateEngine.CallCount(n, Round2);
            var cost = _main.IsDemo ? "simulated, no credit used" : "uses your Claude plan";
            return $"{n} agent{(n == 1 ? "" : "s")} · {calls} model calls ({cost})";
        }
    }

    public ICommand StartCommand { get; }
    public ICommand ApplyPresetCommand { get; }
    public ICommand SelectNoneCommand { get; }
    public ICommand LoadSampleCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand EditContextCommand { get; }

    public void OnNavigatedTo()
    {
        ReloadAgents();
        var context = _main.Council.LoadContext();
        ContextIsTemplate = string.IsNullOrWhiteSpace(context) || context.Contains("[name, country, legal form]");
        OnPropertyChanged(nameof(CallSummary));
    }

    private void ReloadAgents()
    {
        var previouslySelected = Agents.Count == 0 ? null : Agents.Where(a => a.IsSelected).Select(a => a.Agent.Id).ToHashSet();
        var errors = new List<string>();
        var loaded = _main.Council.LoadAgents(errors).Where(a => a.Enabled).ToList();
        Agents.Clear();
        foreach (var a in loaded)
            Agents.Add(new AgentChoice(a, previouslySelected?.Contains(a.Id) ?? true, () => OnPropertyChanged(nameof(CallSummary))));
        AgentErrors = errors.Count == 0 ? "" : "Some agent files could not be read: " + string.Join("; ", errors);
        OnPropertyChanged(nameof(Presets));
    }

    private void ApplyPreset(AgentPreset? preset)
    {
        if (preset is null) return;
        foreach (var a in Agents)
            a.IsSelected = preset.AgentIds.Count == 0 || preset.AgentIds.Contains(a.Agent.Id, StringComparer.OrdinalIgnoreCase);
    }

    private void LoadSample()
    {
        Title = "VR crane training subscription";
        IdeaText = "Turn our VR tower-crane training prototype into a product: a yearly subscription for construction "
                   + "companies in Romania. Each customer gets 4 headsets with crane scenarios (load swings, wind, signalling, "
                   + "blind lifts), a trainer dashboard and content updates. Price idea: €6,000 per year per company.";
        ExtraContext = "(Sample) The crane scenario started as a client project. About 10 hours a week are free until March.";
        CommandManager.InvalidateRequerySuggested();
    }

    private void Start()
    {
        var agents = Agents.Where(a => a.IsSelected).Select(a => a.Agent).ToList();
        if (agents.Count == 0 || string.IsNullOrWhiteSpace(IdeaText)) return;

        if (!_main.IsDemo)
        {
            var usage = _main.Council.Usage.Current();
            if (usage.Level == UsageLevel.Blocked)
            {
                Dialogs.Error($"Your monthly limit is reached ({usage.Describe()}). Raise it in Settings to start a debate.");
                return;
            }
            if (usage.Level == UsageLevel.Warning &&
                !Dialogs.Confirm($"You are close to your monthly limit: {usage.Describe()}.\n\nThis debate makes {DebateEngine.CallCount(agents.Count, Round2)} calls. Start anyway?"))
                return;
        }

        var text = IdeaText.Trim();
        var idea = new Idea
        {
            Title = string.IsNullOrWhiteSpace(Title) ? (text.Length > 60 ? text[..60].Trim() + "…" : text) : Title.Trim(),
            Text = text,
            ExtraContext = ExtraContext.Trim(),
        };
        _main.StartDebate(idea, agents, Round2);
    }
}
