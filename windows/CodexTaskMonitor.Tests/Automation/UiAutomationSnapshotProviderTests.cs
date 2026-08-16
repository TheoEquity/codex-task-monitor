using System.Windows;
using CodexTaskMonitor.Windows.Automation;

namespace CodexTaskMonitor.Tests.Automation;

public sealed class UiAutomationSnapshotProviderTests
{
    [Fact]
    public void ProjectTree_StopsAtFiveThousandAndPreservesFlagsAndBounds()
    {
        var root = FakeAutomationTreeNode.Chain(length: 5_100, offscreenIndex: 4_999, new Rect(7, 8, 9, 10));

        var snapshot = UiAutomationSnapshotProvider.ProjectTree(root, new Rect(0, 0, 100, 100), default);

        Assert.Equal(5_000, snapshot.Nodes.Count);
        Assert.True(snapshot.Nodes[^1].IsOffscreen);
        Assert.Equal(new Rect(7, 8, 9, 10), snapshot.Nodes[^1].Bounds);
        Assert.True(snapshot.IsTruncated);
    }

    [Fact]
    public void ProjectTree_ExactlyFiveThousandElements_IsNotTruncated()
    {
        var root = FakeAutomationTreeNode.Chain(length: 5_000, offscreenIndex: -1, Rect.Empty);

        var snapshot = UiAutomationSnapshotProvider.ProjectTree(root, new Rect(0, 0, 100, 100), default);

        Assert.Equal(5_000, snapshot.Nodes.Count);
        Assert.False(snapshot.IsTruncated);
    }

    [Fact]
    public void ProjectTree_ProbesOneAdditionalChildToDetectTruncation()
    {
        var snapshot = UiAutomationSnapshotProvider.ProjectTree(new WideAutomationTreeNode(), new Rect(0, 0, 100, 100), default);

        Assert.Equal(5_000, snapshot.Nodes.Count);
        Assert.True(snapshot.IsTruncated);
    }

    private sealed class FakeAutomationTreeNode(
        string id,
        bool offscreen,
        Rect bounds,
        IReadOnlyList<IAutomationTreeNode> children) : IAutomationTreeNode
    {
        public string RuntimeId => id;
        public string ControlType => "ControlType.ListItem";
        public string Name => id;
        public string ClassName => "fake";
        public Rect Bounds => bounds;
        public bool IsOffscreen => offscreen;
        public IEnumerable<IAutomationTreeNode> Children => children;

        public static FakeAutomationTreeNode Chain(int length, int offscreenIndex, Rect offscreenBounds)
        {
            IAutomationTreeNode? next = null;
            for (var index = length - 1; index >= 0; index--)
            {
                var marked = index == offscreenIndex;
                next = new FakeAutomationTreeNode(
                    $"node-{index}", marked, marked ? offscreenBounds : new Rect(1, 1, 2, 2),
                    next is null ? [] : [next]);
            }

            return (FakeAutomationTreeNode)next!;
        }
    }

    private sealed class WideAutomationTreeNode : IAutomationTreeNode
    {
        public string RuntimeId => "root";
        public string ControlType => "ControlType.Pane";
        public string Name => "";
        public string ClassName => "fake";
        public Rect Bounds => new(1, 1, 2, 2);
        public bool IsOffscreen => false;
        public IEnumerable<IAutomationTreeNode> Children => new ThrowBeyondCapChildren();
    }

    private sealed class ThrowBeyondCapChildren : IReadOnlyList<IAutomationTreeNode>
    {
        public int Count => 5_002;

        public IAutomationTreeNode this[int index] => Child(index);

        public IEnumerator<IAutomationTreeNode> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                if (index == 5_000)
                    throw new InvalidOperationException("Children were enumerated past the truncation probe.");
                yield return Child(index);
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private static IAutomationTreeNode Child(int index) =>
            new FakeAutomationTreeNode($"child-{index}", false, new Rect(1, 1, 2, 2), []);
    }
}
