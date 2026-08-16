using System.Windows;
using CodexTaskMonitor.Core.Data;
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Core.Preferences;
using CodexTaskMonitor.Windows.Automation;
using CodexTaskMonitor.Windows.Interop;
using CodexTaskMonitor.Windows.Services;
using CodexTaskMonitor.Windows.ViewModels;

namespace CodexTaskMonitor.Windows;

public partial class App : Application
{
    private ThreadActivationService? activation;
    private bool activationPending;
    private MonitorViewModel? model;
    private SingleInstanceService? singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // SingleInstanceService scopes this name to the current user's SID itself.
        singleInstance = SingleInstanceService.TryAcquire("CodexTaskMonitor");
        if (!singleInstance.IsOwner)
        {
            Shutdown();
            return;
        }

        singleInstance.ActivationRequested += OnActivationRequested;

        var paths = CodexDataPaths.ForHome(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var threads = new SqliteThreadStore(paths.DatabasePath);
        var monitor = new TaskMonitor(threads);
        var preferences = new MonitorPreferencesStore(paths.PreferencesPath);
        var diagnostics = new LocalDiagnostics(paths.LogDirectory);
        var snapshots = new UiAutomationSnapshotProvider();
        var scroll = new SidebarScrollController(
            snapshots,
            new SidebarScrollInput(new UiAutomationSidebarScrollInput(), new NativeSidebarWheelInput()),
            TimeProvider.System,
            TimeSpan.FromMilliseconds(100),
            80,
            TimeSpan.FromSeconds(8));
        var revealer = new WindowsSidebarRevealer(
            new ChatGptWindowLocator(),
            scroll,
            paths.SessionIndexPath,
            paths.GlobalStatePath,
            threads,
            TimeProvider.System);
        activation = new ThreadActivationService(
            new CodexDeepLinkLauncher(), revealer, diagnostics, TimeProvider.System);
        var startup = new StartupRegistration(
            new RegistryRunValueStore(),
            Environment.ProcessPath ?? throw new InvalidOperationException("The application executable path is unavailable."));
        model = new MonitorViewModel(
            monitor,
            preferences,
            activation,
            startup,
            new CodexLaunchTimeProvider(),
            TimeProvider.System);

        var window = new MainWindow { DataContext = model };
        MainWindow = window;
        model.QuitRequested += OnQuitRequested;
        window.Show();

        if (activationPending)
            ActivateMainWindow();

        await model.StartAsync(startPollingLoop: true, CancellationToken.None);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (model is not null)
        {
            model.QuitRequested -= OnQuitRequested;
            await model.DisposeAsync();
        }

        if (activation is not null)
            await activation.DisposeAsync();
        if (singleInstance is not null)
            singleInstance.ActivationRequested -= OnActivationRequested;
        singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnActivationRequested(object? sender, EventArgs e) =>
        _ = Dispatcher.InvokeAsync(ActivateMainWindow);

    private void OnQuitRequested(object? sender, EventArgs e)
    {
        if (MainWindow is MainWindow window)
            window.RequestExit();

        Shutdown();
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is not Window window)
        {
            activationPending = true;
            return;
        }

        activationPending = false;
        if (!window.IsVisible)
            window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
