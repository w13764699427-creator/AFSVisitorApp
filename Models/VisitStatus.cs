namespace VisitorApp.Models;

public enum VisitStatus
{
    CheckedIn = 0,
    CheckedOut = 1,
    Cancelled = 2
}

public static class VisitStatusExtensions
{
    public static string ToDisplay(this VisitStatus status) => status switch
    {
        VisitStatus.CheckedIn => "在场",
        VisitStatus.CheckedOut => "已离场",
        VisitStatus.Cancelled => "已取消",
        _ => "未知"
    };

    public static string ToBadgeClass(this VisitStatus status) => status switch
    {
        VisitStatus.CheckedIn => "va-badge va-badge-success",
        VisitStatus.CheckedOut => "va-badge va-badge-muted",
        VisitStatus.Cancelled => "va-badge va-badge-warning",
        _ => "va-badge"
    };
}
