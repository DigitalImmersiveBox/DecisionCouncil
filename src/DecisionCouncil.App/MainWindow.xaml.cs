using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using DecisionCouncil.App.Infrastructure;
using DecisionCouncil.App.ViewModels;

namespace DecisionCouncil.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void NewIdea_Click(object sender, RoutedEventArgs e) => Vm?.GoTo("new");

    private void Settings_Click(object sender, RoutedEventArgs e) => Vm?.GoTo("settings");

    private void DataFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{vm.DataFolder}\""));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (Vm is { } vm && !vm.CanClose()) e.Cancel = true;
        base.OnClosing(e);
    }
}
