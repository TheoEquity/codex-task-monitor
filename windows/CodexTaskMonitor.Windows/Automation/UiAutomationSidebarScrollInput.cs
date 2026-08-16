using System.Windows;
using System.Windows.Automation;

namespace CodexTaskMonitor.Windows.Automation;

public sealed class UiAutomationSidebarScrollInput
{
    internal Task<bool> ScrollAsync(nint handle, SidebarScrollRegion region, ScrollDirection direction, SidebarInputMode mode, IScrollEffectPermit permit, CancellationToken token)
    {
        if (mode != SidebarInputMode.AutomationPattern)
            return Task.FromResult(false);

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(Scroll(handle, region, direction, permit, token));
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

    private static bool Scroll(nint handle, SidebarScrollRegion region, ScrollDirection direction, IScrollEffectPermit permit, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var root = AutomationElement.FromHandle(handle);
            var condition = new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true);
            var center = region.InputPoint;
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
                var didScroll = false;
                if (!permit.TryExecute(() =>
                    {
                        pattern.Scroll(ScrollAmount.NoAmount,
                            direction == ScrollDirection.Up ? ScrollAmount.SmallDecrement : ScrollAmount.SmallIncrement);
                        didScroll = true;
                    }))
                    return false;
                return didScroll;
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
