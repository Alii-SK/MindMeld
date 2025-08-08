using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MindMeld.Pages
{
    public class RoomModel : PageModel
    {
        public string RoomId { get; set; } = string.Empty;
        public Guid PlayerId { get; set; } = Guid.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public void OnGet()
        {
            if (RouteData.Values.TryGetValue("RoomId", out var idValue))
            {
                RoomId = (string)idValue!;
            }
            if (RouteData.Values.TryGetValue("PlayerId", out var playerIdValue))
            {
                PlayerId = Guid.TryParse(playerIdValue as string, out var guid) ? guid : Guid.Empty;
            }
            GameRoom gameRoom = GameRoomManager.GetRoom(RoomId);
            PlayerName = gameRoom?.Players.FirstOrDefault(p => p.Id == PlayerId)?.Name ?? "Unknown Player";
        }

    }
}
