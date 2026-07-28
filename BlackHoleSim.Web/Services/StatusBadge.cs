using BlackHoleSim.Shared;

namespace BlackHoleSim.Web.Services;

public static class StatusBadge
{
    public static string CssClass(this RenderJobStatus status) => status switch
    {
        RenderJobStatus.Pending   => "badge-pending",
        RenderJobStatus.Running   => "badge-running",
        RenderJobStatus.Completed => "badge-completed",
        RenderJobStatus.Failed    => "badge-failed",
        RenderJobStatus.Cancelled => "badge-cancelled",
        _ => "badge-pending"
    };
}
