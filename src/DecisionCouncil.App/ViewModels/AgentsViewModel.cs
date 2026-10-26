using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using DecisionCouncil.App.Infrastructure;
using DecisionCouncil.Core;
using DecisionCouncil.Core.Agents;
using DecisionCouncil.Core.Debate;
using DecisionCouncil.Core.Settings;

namespace DecisionCouncil.App.ViewModels;

/// <summary>An editable markdown file on the Agents screen: an agent, or one of the shared prompts.</summary>
public sealed class EditableFileViewModel(string path, string group, AgentDefinition? agent, Action<EditableFileViewModel, bool> enabledChanged)
    : ObservableObject
{
    private bool _isEnabled = agent?.Enabled ?? true;

    public string Path { get; } = path;
    public string Group { get; } = group;
    public AgentDefinition? Agent { get; } = agent;
    public bool IsAgent => Agent is not null;
    public string Title => Agent?.Name ?? System.IO.Path.GetFileNameWithoutExtension(Path);
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Color => Agent?.Color ?? "#6B707B";
    public string Avatar => Agent?.Avatar ?? "";
    public string AgentId => Agent?.Id ?? "";
    public ColumnState AvatarState => IsEnabled ? ColumnState.Waiting : ColumnState.Skipped;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (!Set(ref _isEnabled, value)) return;
            OnPropertyChanged(nameof(AvatarState));
            enabledChanged(this, value);
        }
    }
}

public sealed class AgentsViewModel : ObservableObject, IPage
{
    private readonly MainViewModel _main;
    private EditableFileViewModel? _selected;
    private string _editorText = "";
    private string _savedText = "";
    private string _loadErrors = "";
    private string _testIdea = "Open a small VR showroom in Bucharest where companies can try our training simulations before they buy.";
    private string? _testOutput;
    private bool _isTesting;
    private CancellationTokenSource? _testCts;

    public AgentsViewModel(MainViewModel main)
    {
        _main = main;
        FilesView = CollectionViewSource.GetDefaultView(Files);
        FilesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(EditableFileViewModel.Group)));
        SaveCommand = new RelayCommand(() => Save(), () => IsDirty);
        RevertCommand = new RelayCommand(() => EditorText = _savedText, () => IsDirty);
        NewAgentCommand = new RelayCommand(NewAgent);
        ResetCommand = new RelayCommand(ResetToDefault, () => Selected is not null && DataFolder.HasDefault(DefaultPathFor(Selected)));
        OpenFolderCommand = new RelayCommand(() => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_main.Council.Settings.AgentsFolder}\"")));
        TestRunCommand = new AsyncCommand(TestRun, () => Selected is { IsAgent: true } && !IsDirty && !string.IsNullOrWhiteSpace(TestIdea));
        CancelTestCommand = new RelayCommand(() => _testCts?.Cancel(), () => IsTesting);
    }

    public ObservableCollection<EditableFileViewModel> Files { get; } = [];
    public ICollectionView FilesView { get; }

    public EditableFileViewModel? Selected
    {
        get => _selected;
        set
        {
            if (value == _selected) return;
            if (!ConfirmLeaveFile())
            {
                OnPropertyChanged();
                return;
            }
            _selected = value;
            OnPropertyChanged();
            LoadSelected();
        }
    }

    public string EditorText
    {
        get => _editorText;
        set { if (Set(ref _editorText, value)) OnPropertyChanged(nameof(IsDirty)); }
    }

    public bool IsDirty => Selected is not null && _editorText != _savedText;
    public string LoadErrors { get => _loadErrors; private set => Set(ref _loadErrors, value); }
    public string TestIdea { get => _testIdea; set => Set(ref _testIdea, value); }
    public string? TestOutput { get => _testOutput; private set => Set(ref _testOutput, value); }
    public bool IsTesting { get => _isTesting; private set => Set(ref _isTesting, value); }
    public string FolderPath => _main.Council.Settings.AgentsFolder;
    public string ProviderNote => _main.IsDemo
        ? "Demo mode: the test answer is simulated. Switch to the Claude Code CLI in Settings for a real answer."
        : "Uses your Claude plan: one model call.";

    public ICommand SaveCommand { get; }
    public ICommand RevertCommand { get; }
    public ICommand NewAgentCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand TestRunCommand { get; }
    public ICommand CancelTestCommand { get; }

    public void OnNavigatedTo()
    {
        Reload(Selected?.Path);
        OnPropertyChanged(nameof(ProviderNote));
    }

    public bool CanLeave() => ConfirmLeaveFile();

    private void Reload(string? selectPath)
    {
        var errors = new List<string>();
        var agents = _main.Council.LoadAgents(errors);
        _selected = null;
        Files.Clear();
        foreach (var a in agents)
            Files.Add(new EditableFileViewModel(a.FilePath, a.Category, a, OnEnabledChanged));
        foreach (var f in new[] { PromptLibrary.SharedRulesFile, PromptLibrary.Round2File, PromptLibrary.SynthesizerFile })
        {
            var path = System.IO.Path.Combine(_main.Council.Settings.PromptsFolder, f);
            if (File.Exists(path)) Files.Add(new EditableFileViewModel(path, "Shared prompts", null, OnEnabledChanged));
        }
        LoadErrors = string.Join("\n", errors);
        _selected = Files.FirstOrDefault(f => string.Equals(f.Path, selectPath, StringComparison.OrdinalIgnoreCase)) ?? Files.FirstOrDefault();
        OnPropertyChanged(nameof(Selected));
        LoadSelected();
    }

    private void LoadSelected()
    {
        _savedText = Selected is null ? "" : File.ReadAllText(Selected.Path);
        _editorText = _savedText;
        OnPropertyChanged(nameof(EditorText));
        OnPropertyChanged(nameof(IsDirty));
        TestOutput = null;
    }

    private bool Save()
    {
        if (Selected is null) return true;
        if (Selected.IsAgent)
        {
            try
            {
                AgentLoader.Parse(EditorText, Selected.Path);
            }
            catch (Exception ex)
            {
                Dialogs.Error("This agent file has a problem and was not saved: " + ex.Message);
                return false;
            }
        }
        File.WriteAllText(Selected.Path, EditorText, new UTF8Encoding(false));
        _savedText = EditorText;
        var path = Selected.Path;
        Reload(path);
        return true;
    }

    private bool ConfirmLeaveFile()
    {
        if (!IsDirty) return true;
        return Dialogs.SaveDiscardCancel($"Save your changes to {Selected!.FileName}?") switch
        {
            MessageBoxResult.Yes => Save(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private void OnEnabledChanged(EditableFileViewModel file, bool enabled)
    {
        if (file.Agent is null) return;
        if (file == Selected && IsDirty)
        {
            Dialogs.Info("Save or revert your edits to this agent first.");
            return;
        }
        AgentLoader.SetEnabled(file.Agent, enabled);
        if (file == Selected) LoadSelected();
    }

    private void NewAgent()
    {
        if (!ConfirmLeaveFile()) return;
        var folder = _main.Council.Settings.AgentsFolder;
        var n = 1;
        string id, path;
        do
        {
            id = n == 1 ? "new-agent" : $"new-agent-{n}";
            path = System.IO.Path.Combine(folder, $"{90 + n:00}-{id}.md");
            n++;
        } while (File.Exists(path) || Files.Any(f => f.Agent?.Id == id));

        File.WriteAllText(path, AgentLoader.NewAgentTemplate(id, "New agent"), new UTF8Encoding(false));
        Reload(path);
        Dialogs.Info($"Created {System.IO.Path.GetFileName(path)}. Give it a unique id and name, write its mandate, and save. It then appears on the New idea screen.");
    }

    /// <summary>The file's path inside the shipped defaults (agents\x.md or prompts\x.md).</summary>
    private static string DefaultPathFor(EditableFileViewModel file) =>
        System.IO.Path.Combine(file.IsAgent ? "agents" : "prompts", file.FileName);

    private void ResetToDefault()
    {
        if (Selected is null) return;
        if (!Dialogs.Confirm($"Replace {Selected.FileName} with the version that ships with the app? Your edits to this file are lost.")) return;
        var rel = System.IO.Path.Combine(Selected.IsAgent ? "agents" : "prompts", Selected.FileName);
        DataFolder.RestoreDefault(_main.Council.Settings, rel);
        _savedText = EditorText; // skip the unsaved-changes prompt
        Reload(Selected.Path);
    }

    private async Task TestRun()
    {
        if (Selected?.Agent is not { } agent) return;
        if (!_main.IsDemo && !Dialogs.Confirm($"Run {agent.Name} once on the test idea? This is one call on your Claude plan.")) return;

        IsTesting = true;
        TestOutput = "*Thinking…*";
        _testCts = new CancellationTokenSource();
        try
        {
            var fresh = AgentLoader.Parse(File.ReadAllText(agent.FilePath), agent.FilePath);
            var run = await _main.Council.CreateEngine().TestAgentAsync(fresh, new Idea { Title = "Test idea", Text = TestIdea }, _testCts.Token);
            TestOutput = run.Status == RunStatus.Done
                ? run.Answer + $"\n\n---\n\n*{run.Model} · {run.DurationMs / 1000.0:0.0} s · {run.OutputTokens} tokens out*"
                : $"*{run.Status}: {run.Error}*";
        }
        catch (OperationCanceledException)
        {
            TestOutput = "*Cancelled.*";
        }
        catch (Exception ex)
        {
            TestOutput = "*Failed: " + ex.Message + "*";
        }
        finally
        {
            IsTesting = false;
            _main.RefreshStatus();
        }
    }
}
