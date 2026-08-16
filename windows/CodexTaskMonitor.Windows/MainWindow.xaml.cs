using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CodexTaskMonitor.Windows.ViewModels;

namespace CodexTaskMonitor.Windows;

public partial class MainWindow : Window
{
    private CancellationTokenSource? positionSave;
    private bool exitRequested;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        LocationChanged += OnLocationChanged;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MonitorViewModel model)
            return;

        model.ItemInserted += OnItemInserted;
        if (model.SavedWindowLeft is { } left && model.SavedWindowTop is { } top &&
            left >= SystemParameters.VirtualScreenLeft && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40 &&
            top >= SystemParameters.VirtualScreenTop && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40)
        {
            Left = left;
            Top = top;
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - Width - 20;
            Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - ActualHeight) / 2;
        }
    }

    private void OnItemInserted(object? sender, string itemId)
    {
        if (DataContext is not MonitorViewModel model)
            return;

        var item = model.Items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is not null)
            TaskList.ScrollIntoView(item);
    }

    private async void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded || DataContext is not MonitorViewModel model)
            return;

        positionSave?.Cancel();
        positionSave?.Dispose();
        positionSave = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, positionSave.Token);
            await model.SaveWindowPositionAsync(Left, Top, positionSave.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MonitorItemViewModel item } && DataContext is MonitorViewModel model)
            await model.OpenAsync(item, CancellationToken.None);
    }

    private async void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MonitorItemViewModel item } && DataContext is MonitorViewModel model)
            await model.DismissAsync(item, CancellationToken.None);
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
            return;

        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exitRequested)
            return;

        e.Cancel = true;
        Hide();
    }

    public void RequestExit()
    {
        positionSave?.Cancel();
        positionSave?.Dispose();
        if (DataContext is MonitorViewModel model)
            model.ItemInserted -= OnItemInserted;
        exitRequested = true;
        Close();
    }
}
