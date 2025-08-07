using Microsoft.AspNetCore.Mvc;
using MindMeld;
using StarFederation.Datastar.DependencyInjection;
using StarFederation.Datastar.ModelBinding;
using System.Runtime.Intrinsics.X86;
using System.Text.Json.Serialization;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddDatastar();
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


app.MapPost("/api/new-room", async (HttpContext context, IDatastarService dataStarService, [FromSignals] Signals signals) =>
{
    GameRoomManager.CleanupExpiredRooms();
    Player player = new Player(Guid.NewGuid().ToString(), "Player1");
    GameRoom gameRoom = GameRoomManager.CreateRoom();
    gameRoom.AddPlayer(player);
    return Results.Redirect($"/room/{gameRoom.Id}");
});
app.MapGet("/api/home-screen", async () =>
{
    string html = """

    """;
    return Results.Content(html, "text/html");
});
app.Run();
public record Signals
{
    public string PlayerName { get; set; } = string.Empty;
}