using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace YtpdWeb.Api.Hubs;

[Authorize]
public class ProgressHub : Hub
{
    // Clients join a group per job so progress broadcasts only go to
    // whoever has that job's page open.
    public Task JoinJob(string jobId) => Groups.AddToGroupAsync(Context.ConnectionId, GroupName(jobId));

    public Task LeaveJob(string jobId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(jobId));

    public static string GroupName(string jobId) => $"job:{jobId}";
}
