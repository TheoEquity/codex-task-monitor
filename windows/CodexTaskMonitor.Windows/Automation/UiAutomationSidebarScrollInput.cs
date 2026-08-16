using System.Windows;
using System.Windows.Automation;

namespace CodexTaskMonitor.Windows.Automation;

public sealed class UiAutomationSidebarScrollInput : ISidebarScrollInput
{
    public Task<bool> ScrollAsync(nint handle, Rect region, ScrollDirection direction, SidebarInputMode mode, CancellationToken token)
    {
        if (mode != SidebarInputMode.AutomationPattern)
            return Task.FromResult(false);

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(Scroll(handle, region, direction, token));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(token);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        })
        {
            IsBackground = true,
            Name = "CodexTaskMonitor.UIAScroll"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static bool Scroll(nint handle, Rect region, ScrollDirection direction, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var root = AutomationElement.FromHandle(handle);
            var condition = new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true);
            var center = new Point(region.Left + region.Width / 2, region.Top + region.Height / 2);
            var candidates = root.FindAll(TreeScope.Descendants, condition).Cast<AutomationElement>()
                .Select(element => (Element: element, Bounds: element.Current.BoundingRectangle))
                .Where(item => !item.Bounds.IsEmpty && item.Bounds.Contains(center))
                .OrderBy(item => item.Bounds.Width * item.Bounds.Height);
            foreach (var candidate in candidates)
            {
                token.ThrowIfCancellationRequested();
                if (!candidate.Element.TryGetCurrentPattern(ScrollPattern.Pattern, out var value) ||
                    value is not ScrollPattern pattern || !pattern.Current.VerticallyScrollable)
                    continue;
                pattern.Scroll(ScrollAmount.NoAmount,
                    direction == ScrollDirection.Up ? ScrollAmount.SmallDecrement : ScrollAmount.SmallIncrement);
                return true;
            }

            return false;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
