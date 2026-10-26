using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using DecisionCouncil.App.ViewModels;
using DecisionCouncil.Core;
using DecisionCouncil.Core.Settings;

namespace DecisionCouncil.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        // Single instance, like Session Deck: a second launch brings the first window up. Runs against another
        // data folder (--data, used by the tour and tests) are separate and may run beside it.
        if (!e.Args.Contains("--data") && !ClaimSingleInstance(e.Args.Contains("--updated")))
        {
            Shutdown();
            return;
        }
        // WPF formats dates and numbers as en-US unless told to follow the Windows regional settings.
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        Council council;
        try
        {
            var settings = AppSettings.Load();
            // --data <folder> runs against another data folder without touching saved settings (handy for demos and tests).
            var i = Array.IndexOf(e.Args, "--data");
            var overridden = i >= 0 && i + 1 < e.Args.Length;
            if (settings.UpgradedOnLoad && !overridden) settings.Save();
            if (overridden) settings.DataFolder = System.IO.Path.GetFullPath(e.Args[i + 1]);
            council = new Council(settings) { PersistSettings = !overridden };
        }
        catch (Exception ex)
        {
            MessageBox.Show("Decision Council could not open its data folder:\n\n" + ex.Message, "Decision Council",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var vm = new MainViewModel(council);
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        ListenForShow(window);
        vm.StartUpdateChecks(justUpdated: e.Args.Contains("--updated"));

        var tour = Array.IndexOf(e.Args, "--tour");
        if (tour >= 0 && tour + 1 < e.Args.Length)
            window.Loaded += async (_, _) => await Infrastructure.Tour.RunAsync(window, vm, e.Args[tour + 1]);

        window.Show();
    }

    /// <summary>
    /// True when this is the only copy running. A copy started by the updater (--updated) waits for the old one,
    /// which is still closing, to let go first.
    /// </summary>
    private bool ClaimSingleInstance(bool justUpdated)
    {
        _mutex = new Mutex(true, "DecisionCouncil.SingleInstance", out var created);
        if (!created && justUpdated)
        {
            try { created = _mutex.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { created = true; } // it exited without letting go; it's ours now
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "DecisionCouncil.Show");
        if (!created) _showEvent.Set();
        return created;
    }

    /// <summary>Brings the window to the front when a second copy is started.</summary>
    private void ListenForShow(Window window)
    {
        if (_showEvent is not { } show) return;
        new Thread(() =>
        {
            while (show.WaitOne())
                window.Dispatcher.Invoke(() =>
                {
                    if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                    window.Activate();
                });
        }) { IsBackground = true }.Start();
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "Decision Council", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
