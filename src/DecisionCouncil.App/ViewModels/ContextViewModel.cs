using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using DecisionCouncil.App.Infrastructure;
using DecisionCouncil.Core.Settings;

namespace DecisionCouncil.App.ViewModels;

/// <summary>Editor for context.md, the business profile sent to every agent.</summary>
public sealed class ContextViewModel : ObservableObject, IPage
{
    private readonly MainViewModel _main;
    private string _text = "";
    private string _saved = "";

    public ContextViewModel(MainViewModel main)
    {
        _main = main;
        SaveCommand = new RelayCommand(() => Save(), () => IsDirty);
        RevertCommand = new RelayCommand(() => Text = _saved, () => IsDirty);
        ResetCommand = new RelayCommand(ResetToTemplate);
    }

    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value)) return;
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(WordCount));
        }
    }

    public bool IsDirty => _text != _saved;
    public string WordCount => $"{Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length} words";
    public string FilePath => _main.Council.Settings.ContextFile;

    public ICommand SaveCommand { get; }
    public ICommand RevertCommand { get; }
    public ICommand ResetCommand { get; }

    public void OnNavigatedTo()
    {
        if (IsDirty) return;
        _saved = File.Exists(FilePath) ? File.ReadAllText(FilePath) : "";
        Text = _saved;
        OnPropertyChanged(nameof(IsDirty));
    }

    public bool CanLeave()
    {
        if (!IsDirty) return true;
        return Dialogs.SaveDiscardCancel("Save your changes to the context profile?") switch
        {
            MessageBoxResult.Yes => Save(),
            MessageBoxResult.No => Discard(),
            _ => false,
        };
    }

    private bool Save()
    {
        File.WriteAllText(FilePath, Text, new UTF8Encoding(false));
        _saved = Text;
        OnPropertyChanged(nameof(IsDirty));
        return true;
    }

    private bool Discard()
    {
        Text = _saved;
        return true;
    }

    private void ResetToTemplate()
    {
        if (!Dialogs.Confirm("Replace your context profile with the empty template? Your current text is lost.")) return;
        DataFolder.RestoreDefault(_main.Council.Settings, "context.md");
        _saved = File.ReadAllText(FilePath);
        Text = _saved;
        OnPropertyChanged(nameof(IsDirty));
    }
}
