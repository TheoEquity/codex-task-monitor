using CodexTaskMonitor.Core.Sidebar;

namespace CodexTaskMonitor.Windows.Automation;

public static class SidebarMatcher
{
    public static SidebarMatchResult Match(AutomationSnapshot snapshot, SidebarTarget target)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(target);

        var leftLimit = snapshot.WindowBounds.Left + snapshot.WindowBounds.Width * 0.45;
        var candidates = snapshot.Nodes.Where(node =>
            node.ControlType == "ControlType.ListItem" &&
            string.Equals(node.Name, target.Title, StringComparison.Ordinal) &&
            (node.IsOffscreen || (!node.Bounds.IsEmpty && node.Bounds.Left < leftLimit))).ToArray();

        if (candidates.Length == 0)
            return new SidebarMatchResult(SidebarMatchStatus.NotFound, null);

        if (candidates.Length == 1)
            return candidates[0].IsOffscreen
                ? new SidebarMatchResult(SidebarMatchStatus.NotFound, null)
                : new SidebarMatchResult(SidebarMatchStatus.Found, candidates[0]);

        var label = GroupLabel(target.Group);
        if (string.IsNullOrEmpty(label))
            return new SidebarMatchResult(SidebarMatchStatus.Ambiguous, null);

        var headings = snapshot.Nodes.Where(node =>
            (node.ControlType is "ControlType.Text" or "ControlType.Button") &&
            string.Equals(node.Name, label, StringComparison.Ordinal)).ToArray();
        if (headings.Length == 0)
            return new SidebarMatchResult(SidebarMatchStatus.Ambiguous, null);

        var scored = candidates.Select(candidate => new CandidateScore(
            candidate,
            GroupEvidenceScore(candidate, headings)))
            .OrderByDescending(item => item.Score)
            .ToArray();

        if (scored[0].Score < 2 || scored[0].Score == scored[1].Score)
            return new SidebarMatchResult(SidebarMatchStatus.Ambiguous, null);

        return scored[0].Candidate.IsOffscreen
            ? new SidebarMatchResult(SidebarMatchStatus.NotFound, null)
            : new SidebarMatchResult(SidebarMatchStatus.Found, scored[0].Candidate);
    }

    private static string? GroupLabel(SidebarThreadGroup group) => group.Kind switch
    {
        SidebarThreadGroupKind.Pinned => "置顶",
        SidebarThreadGroupKind.Section or SidebarThreadGroupKind.Project => group.Name,
        _ => null
    };

    private static int CommonPrefix(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var count = 0;
        while (count < left.Count && count < right.Count && left[count] == right[count])
            count++;
        return count;
    }

    private static int GroupEvidenceScore(AutomationNode candidate, IReadOnlyList<AutomationNode> headings) =>
        headings.Where(heading => heading.TraversalIndex < candidate.TraversalIndex)
            .Select(heading => new HeadingScore(
                heading,
                CommonPrefix(candidate.AncestorRuntimeIds, heading.AncestorRuntimeIds)))
            .Where(item => item.Score >= 2 && item.Score == item.Heading.AncestorRuntimeIds.Count)
            .Select(item => item.Score)
            .DefaultIfEmpty(0)
            .Max();

    private sealed record CandidateScore(AutomationNode Candidate, int Score);
    private sealed record HeadingScore(AutomationNode Heading, int Score);
}
