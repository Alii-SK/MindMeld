using System.Collections.Concurrent;

namespace MindMeld;

using MindMeld.Hubs;
using StarFederation.Datastar.DependencyInjection;
using System.Collections.Concurrent;
using System.Numerics;
public enum GameState
{
    Waiting,
    InProgress,
    RoundEnd,
    GameEnd
}

public class GameRound
{
    public int RoundNumber { get; init; } = 1;
    public Dictionary<Guid, string> PlayerSubmissions { get; } = new();
    public DateTime StartTime { get; init; } = DateTime.UtcNow;
    public DateTime EndTime => StartTime.AddSeconds(15);
    public bool IsTimeUp => DateTime.UtcNow >= EndTime;
    public bool AllPlayersSubmitted(int totalPlayers) => PlayerSubmissions.Count == totalPlayers;
}
public class Player
{
    public Guid Id { get; init; }
    public string Name { get; init; }

    public Player(Guid id, string name)
    {
        Id = id;
        Name = name;
    }
}

public class GameRoom
{
    public string Id { get; init; }
    public List<Player> Players { get; } = new();
    public Player? Host { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public GameState State { get; set; } = GameState.Waiting;
    public GameRound? CurrentRound { get; set; } = new GameRound();
    public int MaxRounds { get; set; } = 5;
    public List<GameRound> CompletedRounds { get; } = new();
    public bool GameWon { get; set; } = false;
    public GameRoom(string id)
    {
        Id = id;
    }
    // Add cleanup logic
    public bool IsExpired => DateTime.UtcNow - CreatedAt > TimeSpan.FromMinutes(15);

    // Helper methods
    public void AddPlayer(Player player)
    {
        Players.Add(player);

        // Set as host if first player
        if (Host == null)
        {
            Host = player;
        }
    }

    public void RemovePlayer(Guid playerId)
    {
        var player = Players.FirstOrDefault(p => p.Id == playerId);
        if (player != null)
        {
            Players.Remove(player);

            // If host left, assign new host
            if (Host?.Id == playerId && Players.Count > 0)
            {
                Host = Players.First();
            }
            else if (Players.Count == 0)
            {
                Host = null;
            }
        }
    }
    public void StartNewRound()
    {
        var roundNumber = CompletedRounds.Count + 1;
        CurrentRound = new GameRound { RoundNumber = roundNumber };
        State = GameState.InProgress;
    }
    public void SubmitWord(Guid playerId, string word)
    {
        if (CurrentRound != null && !CurrentRound.IsTimeUp)
        {
            CurrentRound.PlayerSubmissions[playerId] = word.Trim().ToLowerInvariant();
        }
    }
    public bool CheckWinCondition()
    {
        if (CurrentRound?.PlayerSubmissions.Count == 0) return false;

        var words = CurrentRound.PlayerSubmissions.Values.ToList();
        return words.All(w => w == words.First());
    }

    public void EndRound()
    {
        if (CurrentRound == null) return;

        if (CheckWinCondition())
        {
            GameWon = true;
            State = GameState.GameEnd;
        }
        else if (CompletedRounds.Count >= MaxRounds - 1)
        {
            State = GameState.GameEnd;
        }
        else
        {
            State = GameState.RoundEnd;
        }

        CompletedRounds.Add(CurrentRound);
        CurrentRound = null;
    }
}
public class GameService
{
    private static readonly ConcurrentDictionary<string, Timer> _gameTimers = new();

    public static void StartGame(string roomId, GameHub hub)
    {
        var room = GameRoomManager.GetRoom(roomId);
        if (room == null) return;

        room.StartNewRound();
        StartRoundTimer(roomId, hub);
    }

    private static void StartRoundTimer(string roomId, GameHub hub)
    {
        // Cancel existing timer if any
        if (_gameTimers.TryRemove(roomId, out var existingTimer))
        {
            existingTimer.Dispose();
        }

        var timer = new Timer(async _ =>
        {
            await EndRound(roomId, hub);
        }, null, TimeSpan.FromSeconds(15), Timeout.InfiniteTimeSpan);

        _gameTimers[roomId] = timer;
    }

    private static async Task EndRound(string roomId, GameHub hub)
    {
        var room = GameRoomManager.GetRoom(roomId);
        if (room?.CurrentRound == null) return;

        var submissions = room.CurrentRound.PlayerSubmissions;
        room.EndRound();

        // The hub will handle the broadcasting
        // Clean up timer
        if (_gameTimers.TryRemove(roomId, out var timer))
        {
            timer.Dispose();
        }
    }

}
public class GameRoomManager
{
    private static readonly ConcurrentDictionary<string, GameRoom> _rooms = new();

    public static GameRoom CreateRoom()
    {
        var roomId = GenerateRoomId();
        var room = new GameRoom(roomId);
        _rooms[roomId] = room;
        return room;
    }

    public static GameRoom? GetRoom(string roomId)
    {
        return _rooms.TryGetValue(roomId, out var room) ? room : null;
    }

    public static void CleanupExpiredRooms()
    {
        var expiredRooms = _rooms.Where(kvp => kvp.Value.IsExpired).ToList();
        foreach (var (roomId, _) in expiredRooms)
        {
            _rooms.TryRemove(roomId, out _);
        }
    }
    public static bool IsGameHost(string roomId, Guid playerId)
    {
        var room = GetRoom(roomId);
        if (room == null)
        {
            return false; // Room doesn't exist
        }

        return room.Host?.Id == playerId;
    }
    public static bool RemovePlayerFromRoom(string roomId, Guid playerId)
    {
        var room = GetRoom(roomId);
        if (room == null)
        {
            return false; // Room doesn't exist
        }
        room.RemovePlayer(playerId);
        // If room is empty, remove it
        if (room.Players.Count == 0)
        {
            _rooms.TryRemove(roomId, out _);
        }
        return true;
    }   
    private static string GenerateRoomId()
    {
        // Generate a simple 4-digit room code
        var random = new Random();
        return random.Next(1000, 9999).ToString();
    }
}