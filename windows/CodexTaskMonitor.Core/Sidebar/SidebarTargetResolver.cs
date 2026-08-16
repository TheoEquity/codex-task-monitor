using System.Text.Json;

namespace CodexTaskMonitor.Core.Sidebar;

public static class SidebarTargetResolver
{
    public static SidebarTarget? Resolve(
        string threadId,
        ThreadGroupingInfo grouping,
        ReadOnlySpan<byte> sessionIndex,
        ReadOnlySpan<byte> globalState)
    {
        var title = SessionIndex.LatestTitle(threadId, sessionIndex);
        if (string.IsNullOrWhiteSpace(title)) return null;
        if (grouping.IsPinned) return new SidebarTarget(title, SidebarThreadGroup.Pinned());
        if (!string.IsNullOrWhiteSpace(grouping.SectionName))
            return new SidebarTarget(title, SidebarThreadGroup.Section(grouping.SectionName));

        using var document = JsonDocument.Parse(globalState.IsEmpty ? "{}"u8.ToArray() : globalState.ToArray());
        var root = document.RootElement;
        if (TryProjectName(root, threadId, out var projectName))
            return new SidebarTarget(title, SidebarThreadGroup.Project(projectName));
        if (Contains(root, "projectless-thread-ids", threadId))
            return new SidebarTarget(title, SidebarThreadGroup.Projectless());

        var cwdProject = UniqueProjectForCwd(root, grouping.Cwd);
        return cwdProject is null ? null : new SidebarTarget(title, SidebarThreadGroup.Project(cwdProject));
    }

    private static bool TryProjectName(JsonElement root, string threadId, out string projectName)
    {
        projectName = string.Empty;
        if (!root.TryGetProperty("thread-project-assignments", out var assignments) ||
            assignments.ValueKind != JsonValueKind.Object ||
            !assignments.TryGetProperty(threadId, out var assignment) ||
            assignment.ValueKind != JsonValueKind.Object ||
            !assignment.TryGetProperty("projectId", out var projectIdElement) ||
            projectIdElement.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("local-projects", out var projects) ||
            projects.ValueKind != JsonValueKind.Object ||
            !projects.TryGetProperty(projectIdElement.GetString()!, out var project) ||
            project.ValueKind != JsonValueKind.Object ||
            !project.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String) return false;

        projectName = nameElement.GetString()!;
        return !string.IsNullOrWhiteSpace(projectName);
    }

    private static bool Contains(JsonElement root, string property, string value) =>
        root.TryGetProperty(property, out var array) &&
        array.ValueKind == JsonValueKind.Array &&
        array.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == value);

    private static string? UniqueProjectForCwd(JsonElement root, string cwd)
    {
        if (!root.TryGetProperty("local-projects", out var projects) || projects.ValueKind != JsonValueKind.Object) return null;

        var normalizedCwd = NormalizePath(cwd);
        if (normalizedCwd is null) return null;

        string? match = null;
        foreach (var property in projects.EnumerateObject())
        {
            var project = property.Value;
            if (project.ValueKind != JsonValueKind.Object ||
                !project.TryGetProperty("name", out var nameElement) ||
                nameElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(nameElement.GetString()) ||
                !project.TryGetProperty("rootPaths", out var roots) ||
                roots.ValueKind != JsonValueKind.Array) continue;

            if (roots.EnumerateArray().Any(rootPath =>
                    rootPath.ValueKind == JsonValueKind.String &&
                    NormalizePath(rootPath.GetString()!) is { } normalizedRoot &&
                    string.Equals(normalizedRoot, normalizedCwd, StringComparison.OrdinalIgnoreCase)))
            {
                if (match is not null) return null;
                match = nameElement.GetString();
            }
        }

        return match;
    }

    private static string? NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
