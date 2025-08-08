using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace MindMeld.Hubs;

public class GameHub : Hub
{
    private static readonly ConcurrentDictionary<string, HashSet<string>> _roomConnections = new();

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

        // Notify others in the room that a player joined
        await Clients.Group($"room_{roomId}")
            .SendAsync("PlayerJoined", playerId, playerName);

        // Send current game state to the new connection
        var room = GameRoomManager.GetRoom(roomId);
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
            }
        }

        await Clients.Group($"room_{roomId}")
            .SendAsync("PlayerLeft", playerId);
    }

    public async Task SubmitWord(string roomId, string playerId,string playerName, string word)
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
            await EndRound(roomId);
        }
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
    }

    private async Task EndRound(string roomId)
    {
        var room = GameRoomManager.GetRoom(roomId);
        if (room?.CurrentRound == null) return;

        var submissions = room.CurrentRound.PlayerSubmissions;
        room.EndRound();

        // Send round results
        await Clients.Group($"room_{roomId}")
            .SendAsync("RoundEnded", new
            {
                roundNumber = room.CompletedRounds.Last().RoundNumber,
                submissions = submissions.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => kvp.Value
                ),
                gameWon = room.GameWon,
                gameState = room.State.ToString(),
                allWords = submissions.Values.ToArray(),
                winCondition = room.CheckWinCondition()
            });

        // Auto-start next round after delay
        if (room.State == GameState.RoundEnd)
        {
            await Task.Delay(3000);
            room.StartNewRound();

            await Clients.Group($"room_{roomId}")
                .SendAsync("NextRoundStarted", new
                {
                    gameState = room.State.ToString(),
                    currentRound = room.CurrentRound?.RoundNumber ?? 0,
                    timeRemaining = 15
                });
        }
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