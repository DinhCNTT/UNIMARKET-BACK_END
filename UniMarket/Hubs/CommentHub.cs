using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;

public class CommentHub : Hub
{
    public async Task SendComment(string tinDangId, object comment)
    {
        await Clients.Group(tinDangId).SendAsync("ReceiveComment", comment);
    }

    public override async Task OnConnectedAsync()
    {
        var tinDangId = Context.GetHttpContext().Request.Query["tinDangId"];
        await Groups.AddToGroupAsync(Context.ConnectionId, tinDangId);
        await base.OnConnectedAsync();
    }
}
