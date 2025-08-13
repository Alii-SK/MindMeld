using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;

namespace MindMeld.Hubs;

public class GameHub : Hub
{
    private static readonly ConcurrentDictionary<string, HashSet<string>> _roomConnections = new();
    private static readonly ConcurrentDictionary<string, Timer> _roomTimers = new();
    private static readonly ConcurrentDictionary<string, int> _roomTimerCounts = new();
    private static IHubContext<GameHub>? _hubContext;

    public GameHub(IHubContext<GameHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task JoinRoom(string roomId, string playerId, string playerName)
    {
        // Add connection to room group
        await Groups.AddToGroupAsync(Context.ConnectionId, $"room_{roomId}");

        // Track connection for this room
        _roomConnections.AddOrUpdate(
            roomId,
            new HashSet<string> { Context.ConnectionId },
            (key, existing) =>
            {
                existing.Add(Context.ConnectionId);
                return existing;
            });

        // Send current game state to the new connection
        var room = GameRoomManager.GetRoom(roomId);
        int playerCount = room!.Players.Count;
        if (room != null)
        {
            await Clients.Caller.SendAsync("GameStateUpdate", new
            {
                players = room.Players.Select(p => new { id = p.Id.ToString(), name = p.Name }).ToArray(),
                playerCount = room.Players.Count,
                gameState = room.State.ToString(),
                currentRound = room.CurrentRound?.RoundNumber ?? 0,
                hostId = room.Host?.Id.ToString(),
                gameWon = room.GameWon
            });
        }
        // Notify others in the room that a player joined
        await Clients.Group($"room_{roomId}")
                .SendAsync("PlayerJoined", playerId, playerName, playerCount);
    }

    public async Task LeaveRoom(string roomId, string playerId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"room_{roomId}");
        GameRoomManager.RemovePlayerFromRoom(roomId, Guid.Parse(playerId));

        // Remove connection tracking
        if (_roomConnections.TryGetValue(roomId, out var connections))
        {
            connections.Remove(Context.ConnectionId);
            if (connections.Count == 0)
            {
                _roomConnections.TryRemove(roomId, out _);
                // Clean up timer if room is empty
                StopRoomTimer(roomId);
            }
        }

        await Clients.Group($"room_{roomId}")
            .SendAsync("PlayerLeft", playerId);
    }

    public async Task SubmitWord(string roomId, string playerId, string playerName, string word)
    {
        var room = GameRoomManager.GetRoom(roomId);
        if (room == null) return;

        room.SubmitWord(Guid.Parse(playerId), word);

        // Notify all players about the submission
        await Clients.Group($"room_{roomId}")
            .SendAsync("WordSubmitted", playerId, playerName, word);

        // Check if round should end (all players submitted or time up)
        if (room.CurrentRound?.AllPlayersSubmitted(room.Players.Count) == true)
        {
            StopRoomTimer(roomId);
            await EndRound(roomId);
        }
    }

    public async Task PreStartGame(string roomId, string playerId)
    {
        var room = GameRoomManager.GetRoom(roomId);
        if (room == null || room.Host?.Id.ToString() != playerId) return;

        for (int countdown = 5; countdown > 0; countdown--)
        {
            await Clients.Group($"room_{roomId}").SendAsync("CountdownUpdate", countdown);
            await Task.Delay(1000); // Wait 1 second
        }

        // Start the actual game
        await StartGame(roomId, playerId);
    }

    public async Task StartGame(string roomId, string hostId)
    {
        var room = GameRoomManager.GetRoom(roomId);
        if (room == null || room.Host?.Id.ToString() != hostId) return;

        GameService.StartGame(roomId, this);

        await Clients.Group($"room_{roomId}")
            .SendAsync("GameStarted", new
            {
                gameState = room.State.ToString(),
                currentRound = room.CurrentRound?.RoundNumber ?? 0,
                timeRemaining = 15
            });

        // Start the round timer
        StartRoundTimer(roomId);
    }

    public async Task StartNextRound(string roomId, string playerId)
    {
        var room = GameRoomManager.GetRoom(roomId);
        if (room == null || room.Host?.Id.ToString() != playerId) return;

        room.StartNewRound();

        await Clients.Group($"room_{roomId}")
            .SendAsync("NextRoundStarted", new
            {
                gameState = room.State.ToString(),
                currentRound = room.CurrentRound?.RoundNumber ?? 0,
                timeRemaining = 15
            });

        // Start the timer for the new round
        StartRoundTimer(roomId);
    }

    private static void StartRoundTimer(string roomId)
    {
        // Stop any existing timer for this room
        StopRoomTimer(roomId);

        // Initialize timer count
        _roomTimerCounts[roomId] = 15;

        // Create new timer that calls the static method
        var timer = new Timer(async _ => await OnTimerTickStatic(roomId), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        _roomTimers[roomId] = timer;
    }

    private static async Task OnTimerTickStatic(string roomId)
    {
        if (!_roomTimerCounts.ContainsKey(roomId) || _hubContext == null)
            return;

        var timeRemaining = _roomTimerCounts[roomId];

        try
        {
            // Send timer update to clients using static hub context
            await _hubContext.Clients.Group($"room_{roomId}")
                .SendAsync("RoundTimerUpdate", timeRemaining);
        }
        catch (Exception ex)
        {
            // Log the error and stop the timer
            Console.WriteLine($"Error sending timer update: {ex.Message}");
            StopRoomTimer(roomId);
            return;
        }

        // Decrement timer
        _roomTimerCounts[roomId] = timeRemaining - 1;

        // Check if time is up
        if (timeRemaining <= 0)
        {
            StopRoomTimer(roomId);
            await ForceEndRoundStatic(roomId);
        }
    }

    private static async Task ForceEndRoundStatic(string roomId)
    {
        var room = GameRoomManager.GetRoom(roomId);
        if (room?.CurrentRound == null || _hubContext == null) return;

        // Auto-submit empty words for players who haven't submitted
        foreach (var player in room.Players)
        {
            if (!room.CurrentRound.PlayerSubmissions.ContainsKey(player.Id))
            {
                room.SubmitWord(player.Id, ""); // Submit empty word
            }
        }
        // End the round
        await EndRoundStatic(roomId);
    }

    private static async Task EndRoundStatic(string roomId)
    {
        var room = GameRoomManager.GetRoom(roomId);
        if (room?.CurrentRound == null || _hubContext == null) return;

        var submissions = room.CurrentRound.PlayerSubmissions;
        room.EndRound();

        try
        {
            // Send round results using static hub context
            await _hubContext.Clients.Group($"room_{roomId}")
                .SendAsync("RoundEnded", new
                {
                    roundNumber = room.CompletedRounds.Last().RoundNumber,
                    submissions = submissions.ToDictionary(
                        kvp => kvp.Key.ToString(),
                        kvp => kvp.Value
                    ),
                    gameWon = room.GameWon,
                    gameState = room.State.ToString(),
                    allWords = submissions.Values.ToArray()
                });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending round ended: {ex.Message}");
        }
        // If game has ended (won or max rounds reached), clean up the room
        if (room.State == GameState.GameEnd)
        {
            // Stop any running timers
            StopRoomTimer(roomId);

            // Clean up SignalR group memberships
            if (_roomConnections.TryGetValue(roomId, out var connections))
            {
                foreach (var connectionId in connections.ToList())
                {
                    try
                    {
                        await _hubContext.Groups.RemoveFromGroupAsync(connectionId, $"room_{roomId}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error removing connection {connectionId} from group: {ex.Message}");
                    }
                }
            }

            // Clean up room connections
            _roomConnections.TryRemove(roomId, out _);

            // Delete the room
            GameRoomManager.DeleteRoom(roomId);

            Console.WriteLine($"Room {roomId} deleted - game ended");
        }
    }

    private async Task EndRound(string roomId)
    {
        await EndRoundStatic(roomId);
    }

    private static void StopRoomTimer(string roomId)
    {
        if (_roomTimers.TryRemove(roomId, out var timer))
        {
            timer.Dispose();
        }
        _roomTimerCounts.TryRemove(roomId, out _);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Clean up connection tracking
        foreach (var room in _roomConnections.ToList())
        {
            if (room.Value.Contains(Context.ConnectionId))
            {
                room.Value.Remove(Context.ConnectionId);
                if (room.Value.Count == 0)
                {
                    _roomConnections.TryRemove(room.Key, out _);
                    // Clean up timer when last person leaves
                    StopRoomTimer(room.Key);
                }
                break;
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    // Helper method to get connection count for a room
    public static int GetRoomConnectionCount(string roomId)
    {
        return _roomConnections.TryGetValue(roomId, out var connections) ? connections.Count : 0;
    }
}