using System.Collections.Concurrent;

namespace MindMeld;

using System.Collections.Concurrent;
using System.Numerics;

public class Player
{
    public string Id { get; init; }
    public string Name { get; init; }

    public Player(string id, string name)
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

    public void RemovePlayer(string playerId)
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

    private static string GenerateRoomId()
    {
        // Generate a simple 4-digit room code
        var random = new Random();
        return random.Next(1000, 9999).ToString();
    }
}