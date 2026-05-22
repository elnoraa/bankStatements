using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Statements.WebAPI.Hubs;

/// <summary>
/// SignalR hub for pushing statement processing status updates to connected clients.
/// Clients are identified by their user ID (from the JWT sub claim).
/// </summary>
[Authorize]
public sealed class StatementProcessingHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }
}
