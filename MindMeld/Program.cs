using Microsoft.AspNetCore.Mvc;
using MindMeld;
using MindMeld.Hubs;
using StarFederation.Datastar.DependencyInjection;
using StarFederation.Datastar.ModelBinding;
using System.Runtime.Intrinsics.X86;
using System.Text.Json.Serialization;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddDatastar();
builder.Services.AddSignalR();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
// Map SignalR hub
app.MapHub<GameHub>("/gameHub");

app.MapPost("/api/new-room", async (HttpContext context, IDatastarService dataStarService, [FromSignals] Signals signals) =>
{
    try
    {
        GameRoomManager.CleanupExpiredRooms();
        Player player = new Player(Guid.NewGuid(), signals.PlayerName);
        GameRoom gameRoom = GameRoomManager.CreateRoom();
        gameRoom.AddPlayer(player);
        return Results.Content($"window.location.href = '/room/{gameRoom.Id}/{player.Id}';", "text/javascript");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            error = "Failed to create room",
            message = ex.Message
        });
    }
});
app.MapPost("/api/join-room/{roomId}", async (string roomId, HttpContext context, IDatastarService datastarService, [FromSignals] Signals signals) =>
{
    try
    {
        GameRoom gameRoom = GameRoomManager.GetRoom(roomId)!;
        Player player = new Player(Guid.NewGuid(), signals.PlayerName);
        gameRoom.AddPlayer(player);
        return Results.Content($"window.location.href = '/room/{gameRoom.Id}/{player.Id}';", "text/javascript");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new
        {
            error = "Failed to join room",
            message = ex.Message
        });
    }

});
app.MapGet("/api/check-host/{roomId}/{playerId:guid}", async (string roomId, Guid playerId, HttpContext context, IDatastarService dataStarService) =>
{
    bool isHost = GameRoomManager.IsGameHost(roomId, playerId);
    GameRoom room = GameRoomManager.GetRoom(roomId)!;
    var playerCount = room.Players.Count;

    await dataStarService.PatchSignalsAsync($"{{\"playerCount\":\"{playerCount}\"}}");

    if (isHost)
    {
        await dataStarService.PatchElementsAsync("""
        <div id="host-control"
                class="text-sm p-4">
            <div data-show="$playerCount <= 1"> Waiting for more players to join...</div>
            <button class="pixel-button"
            data-show="$gameState=='Waiting'"
            data-attr-disabled="$playerCount<=1"
            data-class-opacity-50="$playerCount<=1"
            data-class-cursor-not-allowed="$playerCount<=1"
            data-class-hover:bg-[#00ddaa]="$playerCount<=1"
            data-on-click="startGame()">
                Start Game
            </button>
        </div>
        """);
    }
    else
    {
        await dataStarService.PatchElementsAsync("""
        <div id="host-control"
                class="text-sm p-4">
            <div data-show="$gameState=='Waiting'">You are not the host. Waiting for host to start the game...</div>
        </div>
        """);
    }
});
app.MapGet("/api/players/{roomId}", async (string roomId, IDatastarService datastarService) =>
{
    var room = GameRoomManager.GetRoom(roomId);
    if (room == null) return;

    var playerListHtml = GeneratePlayerListHtml(room);

    await datastarService.PatchElementsAsync(playerListHtml);
});


app.Run();
static string GeneratePlayerListHtml(GameRoom room)
{
    var playersHtml = string.Join("", room.Players.Select(player =>
    {
        var hasGuessed = room.CurrentRound?.PlayerSubmissions.ContainsKey(player.Id) ?? false;
        var playerIdClean = player.Id.ToString().Replace("-", ""); // Remove hyphens
        var statusIcon = hasGuessed ? "✓" : "⏳";

        return $"""
            <div id="player-{player.Id}" class="mb-2 p-2 bg-black border border-[#00ffaa] text-[#00ffaa] text-lg">
                <div class="flex items-center justify-between text-lg">
                    <span>{player.Name}</span>
                    <div class="ml-2">
                        <span class="status-icon">{statusIcon}</span>
                    </div>
                    <div id="player-{player.Id}-word" class="ml-2">
                        <span class="text-gray-400">Waiting...</span>
                    </div>
                </div>
            </div>
            """;
    }));

    return $"""
        <div id="player-list" class="mb-4 text-center fixed top-4 left-4 z-10">
            {playersHtml}
        </div>
        """;
}
public record Signals
{
    public string PlayerName { get; set; } = string.Empty;
}
