using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MindMeld.Pages
{
    public class EnterPlayerNameModel : PageModel
    {
        public string RoomOwnerName { get; set; } = string.Empty;
        public string RoomId { get; set; } = string.Empty;
        public void OnGet()
        {
            if (RouteData.Values.TryGetValue("RoomId", out var roomId))
            {
                RoomId = roomId as string ?? string.Empty;
            }
            RoomOwnerName = GameRoomManager.GetRoom(RoomId)?.Host?.Name ?? "Unknown Host";
        }
    }
}
