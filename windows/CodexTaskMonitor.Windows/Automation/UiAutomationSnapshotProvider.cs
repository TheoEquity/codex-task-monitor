using System.Windows;
using System.Windows.Automation;

namespace CodexTaskMonitor.Windows.Automation;

internal interface IAutomationTreeNode
{
    string RuntimeId { get; }
    string ControlType { get; }
    string Name { get; }
    string ClassName { get; }
    Rect Bounds { get; }
    bool IsOffscreen { get; }
    nint NativeWindowHandle => 0;
    IEnumerable<IAutomationTreeNode> Children { get; }
}

public sealed class UiAutomationSnapshotProvider : IUiAutomationSnapshotProvider
{
    private const int MaximumElementCount = 5_000;

    public Task<AutomationSnapshot> CaptureAsync(nint windowHandle, CancellationToken token)
    {
        var completion = new TaskCompletionSource<AutomationSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                completion.TrySetResult(Capture(windowHandle, token));
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
            Name = "CodexTaskMonitor.UIA"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static AutomationSnapshot Capture(nint windowHandle, CancellationToken token)
    {
        var root = AutomationElement.FromHandle(windowHandle)
            ?? throw new InvalidOperationException("ChatGPT UIA root is unavailable");
        return ProjectTree(new AutomationElementTreeNode(root), root.Current.BoundingRectangle, token);
    }

    internal static AutomationSnapshot ProjectTree(
        IAutomationTreeNode root,
        Rect windowBounds,
        CancellationToken token)
    {
        var queue = new Queue<(IAutomationTreeNode Element, string[] Ancestors)>();
        queue.Enqueue((root, []));
        var nodes = new List<AutomationNode>();
        var isTruncated = false;

        while (queue.Count > 0 && nodes.Count < MaximumElementCount)
        {
            token.ThrowIfCancellationRequested();
            var (element, ancestors) = queue.Dequeue();
            try
            {
                var runtimeId = string.IsNullOrEmpty(element.RuntimeId) ? $"fallback-{nodes.Count}" : element.RuntimeId;
                nodes.Add(new AutomationNode(
                    runtimeId,
                    element.ControlType,
                    element.Name,
                    element.ClassName,
                    element.Bounds,
                    element.IsOffscreen,
                    ancestors,
                    nodes.Count,
                    element.NativeWindowHandle));

                var childAncestors = ancestors.Append(runtimeId).ToArray();
                using var children = element.Children.GetEnumerator();
                while (children.MoveNext())
                {
                    token.ThrowIfCancellationRequested();
                    if (nodes.Count + queue.Count >= MaximumElementCount)
                    {
                        isTruncated = true;
                        break;
                    }
                    queue.Enqueue((children.Current, childAncestors));
                }
            }
            catch (ElementNotAvailableException)
            {
            }
        }

        return new AutomationSnapshot(windowBounds, nodes, isTruncated || queue.Count > 0);
    }

    private sealed class AutomationElementTreeNode(AutomationElement element) : IAutomationTreeNode
    {
        public string RuntimeId => string.Join('.', element.GetRuntimeId());
        public string ControlType => element.Current.ControlType.ProgrammaticName;
        public string Name => element.Current.Name ?? string.Empty;
        public string ClassName => element.Current.ClassName ?? string.Empty;
        public Rect Bounds => element.Current.BoundingRectangle;
        public bool IsOffscreen => element.Current.IsOffscreen;
        public nint NativeWindowHandle => (nint)element.Current.NativeWindowHandle;

        public IEnumerable<IAutomationTreeNode> Children
        {
            get
            {
                var walker = TreeWalker.RawViewWalker;
                for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
                    yield return new AutomationElementTreeNode(child);
            }
        }
    }
}
