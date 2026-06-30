using Microsoft.AspNetCore.SignalR;

namespace SnakeGA.Server.Hubs;

public class SnakeHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        Console.WriteLine($"Client connected: {Context.ConnectionId}");
        await base.OnConnectedAsync();
    }
}
