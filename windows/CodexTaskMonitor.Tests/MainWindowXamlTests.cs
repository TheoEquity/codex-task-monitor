using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using CodexTaskMonitor.Core.Monitoring;
using CodexTaskMonitor.Windows;
using CodexTaskMonitor.Windows.ViewModels;

namespace CodexTaskMonitor.Tests;

public sealed class MainWindowXamlTests
{
    [Fact]
    public void TaskList_ShowsOneGroupPerProjectInRecentActivityOrder()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow
                {
                    DataContext = new TaskListModel(
                    [
                        Item("new-b", "项目 B"),
                        Item("only-a", "项目 A"),
                        Item("old-b", "项目 B"),
                        Item("projectless", "没项目")
                    ])
                };
                var taskList = Assert.IsType<ListBox>(window.FindName("TaskList"));
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

                var groups = Assert.IsAssignableFrom<System.Collections.IEnumerable>(taskList.Items.Groups)
                    .Cast<CollectionViewGroup>()
                    .ToArray();

                Assert.Equal(["项目 B", "项目 A", "没项目"], groups.Select(group => group.Name));
                Assert.Equal(
                    ["new-b:turn", "old-b:turn"],
                    groups[0].Items.Cast<MonitorItemViewModel>().Select(item => item.Id));
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA project-grouping thread did not finish.");
        Assert.Null(failure);
    }

    [Fact]
    public void HeaderBlankArea_IsHitTestableForDragging()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow();
                var border = Assert.IsType<Border>(window.Content);
                var root = Assert.IsType<Grid>(border.Child);
                var titleBar = Assert.Single(root.Children.OfType<Grid>(), child => Grid.GetRow(child) == 0);

                titleBar.Measure(new Size(330, 48));
                titleBar.Arrange(new Rect(0, 0, 330, 48));
                titleBar.UpdateLayout();

                Assert.Same(titleBar, VisualTreeHelper.HitTest(titleBar, new Point(200, 24))?.VisualHit);
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA hit-test thread did not finish.");
        Assert.Null(failure);
    }

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

    [Fact]
    public void LongTaskTitle_StaysBeforeVisibleDismissButtonAtFixedWidth()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow();
                var taskList = Assert.IsType<ListBox>(window.FindName("TaskList"));
                var longTitle = new string('W', 200);
                taskList.ItemsSource = new[]
                {
                    new MonitorItemViewModel(new MonitorItem(
                        "thread",
                        "turn",
                        longTitle,
                        @"C:\work",
                        "work",
                        DateTimeOffset.UtcNow,
                        TaskState.Waiting))
                };

                taskList.Measure(new Size(330, 63));
                taskList.Arrange(new Rect(0, 0, 330, 63));
                taskList.ApplyTemplate();
                taskList.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                taskList.UpdateLayout();

                var item = Assert.IsType<ListBoxItem>(taskList.ItemContainerGenerator.ContainerFromIndex(0));
                var title = Assert.Single(
                    VisualDescendants<TextBlock>(item),
                    element => element.Text == longTitle);
                var dismiss = Assert.Single(
                    VisualDescendants<Button>(item),
                    element => Equals(element.Content, "已处理"));
                var titleBounds = title.TransformToAncestor(taskList).TransformBounds(
                    new Rect(new Point(), title.RenderSize));
                var dismissBounds = dismiss.TransformToAncestor(taskList).TransformBounds(
                    new Rect(new Point(), dismiss.RenderSize));

                Assert.Equal(Visibility.Visible, dismiss.Visibility);
                Assert.True(dismiss.ActualWidth > 0);
                Assert.True(
                    dismissBounds.Right <= taskList.ActualWidth,
                    $"Dismiss button right edge {dismissBounds.Right} exceeded list width {taskList.ActualWidth}.");
                Assert.True(
                    titleBounds.Right <= dismissBounds.Left,
                    $"Title right edge {titleBounds.Right} overlapped dismiss button at {dismissBounds.Left}.");
                Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
                Assert.Equal(TextWrapping.NoWrap, title.TextWrapping);
                Assert.Equal(
                    ScrollBarVisibility.Disabled,
                    ScrollViewer.GetHorizontalScrollBarVisibility(taskList));
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA layout thread did not finish.");
        Assert.Null(failure);
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;

            foreach (var descendant in VisualDescendants<T>(child))
                yield return descendant;
        }
    }

    private static MonitorItemViewModel Item(string threadId, string projectName) =>
        new(new MonitorItem(
            threadId,
            "turn",
            threadId,
            @"C:\work",
            projectName,
            DateTimeOffset.UtcNow,
            TaskState.Waiting));

    private sealed record TaskListModel(ObservableCollection<MonitorItemViewModel> Items)
    {
        public TaskListModel(IEnumerable<MonitorItemViewModel> items) : this(new ObservableCollection<MonitorItemViewModel>(items))
        {
        }
    }
}
