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

        if (target.Group.Kind == SidebarThreadGroupKind.Projectless)
            return candidates.Length == 1
                ? VisibleResult(candidates[0])
                : new SidebarMatchResult(SidebarMatchStatus.Ambiguous, null);

        var label = GroupLabel(target.Group);
        if (string.IsNullOrWhiteSpace(label))
            return new SidebarMatchResult(SidebarMatchStatus.Ambiguous, null);

        var headings = snapshot.Nodes.Where(node =>
            (node.ControlType is "ControlType.Text" or "ControlType.Button") &&
            string.Equals(node.Name, label, StringComparison.Ordinal)).ToArray();
        if (headings.Length == 0)
            return new SidebarMatchResult(SidebarMatchStatus.Ambiguous, null);

        var scored = candidates.Select(candidate => new CandidateScore(
            candidate,
            CalculateGroupEvidence(candidate, headings)))
            .OrderByDescending(item => item.Score)
            .ToArray();

        if (candidates.Length == 1)
        {
            var only = scored[0];
            if (only.Evidence.ValidHeadingCount == 1)
                return VisibleResult(only.Candidate);
            return only.Evidence.HasLaterHeading
                ? new SidebarMatchResult(SidebarMatchStatus.Ambiguous, null)
                : only.Evidence.ValidHeadingCount == 0
                    ? new SidebarMatchResult(SidebarMatchStatus.NotFound, null)
                    : new SidebarMatchResult(SidebarMatchStatus.Ambiguous, null);
        }

        if (scored[0].Score < 2 || scored[0].Score == scored[1].Score)
            return new SidebarMatchResult(SidebarMatchStatus.Ambiguous, null);

        return VisibleResult(scored[0].Candidate);
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

    private static GroupEvidence CalculateGroupEvidence(AutomationNode candidate, IReadOnlyList<AutomationNode> headings)
    {
        var headingScores = headings
            .Select(heading => new HeadingScore(
                heading,
                CommonPrefix(candidate.AncestorRuntimeIds, heading.AncestorRuntimeIds)))
            .Where(item => item.Score >= 2 && item.Score == item.Heading.AncestorRuntimeIds.Count)
            .ToArray();
        var preceding = headingScores
            .Where(item => item.Heading.TraversalIndex < candidate.TraversalIndex)
            .ToArray();
        var hasLaterHeading = headingScores.Any(item => item.Heading.TraversalIndex >= candidate.TraversalIndex);
        return preceding.Length == 1
            ? new GroupEvidence(preceding[0].Score, preceding.Length, hasLaterHeading)
            : new GroupEvidence(0, preceding.Length, hasLaterHeading);
    }

    private static SidebarMatchResult VisibleResult(AutomationNode candidate) => candidate.IsOffscreen
        ? new SidebarMatchResult(SidebarMatchStatus.NotFound, null)
        : new SidebarMatchResult(SidebarMatchStatus.Found, candidate);

    private sealed record CandidateScore(AutomationNode Candidate, GroupEvidence Evidence)
    {
        public int Score => Evidence.Score;
    }

    private sealed record GroupEvidence(int Score, int ValidHeadingCount, bool HasLaterHeading);
    private sealed record HeadingScore(AutomationNode Heading, int Score);
}
