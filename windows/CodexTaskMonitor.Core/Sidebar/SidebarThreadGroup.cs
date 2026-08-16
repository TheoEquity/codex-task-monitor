namespace CodexTaskMonitor.Core.Sidebar;

public enum SidebarThreadGroupKind
{
    Pinned,
    Section,
    Project,
    Projectless
}

public sealed record SidebarThreadGroup(SidebarThreadGroupKind Kind, string? Name)
{
    public static SidebarThreadGroup Pinned() => new(SidebarThreadGroupKind.Pinned, null);

    public static SidebarThreadGroup Section(string name) => new(SidebarThreadGroupKind.Section, name);

    public static SidebarThreadGroup Project(string name) => new(SidebarThreadGroupKind.Project, name);

    public static SidebarThreadGroup Projectless() => new(SidebarThreadGroupKind.Projectless, null);
}

public sealed record ThreadGroupingInfo(bool IsPinned, string? SectionName, string Cwd);

public sealed record SidebarTarget(string Title, SidebarThreadGroup Group);
