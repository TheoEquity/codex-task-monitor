using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Windows;
using CodexTaskMonitor.Windows.ViewModels;

namespace CodexTaskMonitor.Tests;

public sealed class MainWindowXamlTests
{
    [Fact]
    public void TaskRow_ReadOnlyDisplayBindings_RenderWithoutWritingToViewModel()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow();
                var taskList = Assert.IsType<ListBox>(window.FindName("TaskList"));
                var row = Assert.IsAssignableFrom<FrameworkElement>(taskList.ItemTemplate.LoadContent());
                row.DataContext = new MonitorItemViewModel(new MonitorItem(
                    "thread",
                    "turn",
                    "Task",
                    @"C:\work",
                    "work",
                    DateTimeOffset.UtcNow,
                    TaskState.Waiting));

                row.Measure(new Size(330, 63));
                row.Arrange(new Rect(0, 0, 330, 63));
                row.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                window?.RequestExit();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA rendering thread did not finish.");
        Assert.Null(failure);
    }
}
