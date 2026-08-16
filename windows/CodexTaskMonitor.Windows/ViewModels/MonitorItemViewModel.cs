using CodexTaskMonitor.Core.Monitoring;

namespace CodexTaskMonitor.Windows.ViewModels;

public sealed record MonitorItemViewModel(MonitorItem Item)
{
    public string Id => Item.Id;

    public string Title => Item.Title;

    public string ProjectName => Item.ProjectName;

    public string StateText => Item.State == TaskState.Running ? "运行中" : "等待处理";

    public string DotColor => Item.State == TaskState.Running ? "#3B82F6" : "#22C55E";

    public bool CanDismiss => Item.State == TaskState.Waiting;
}
