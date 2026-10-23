using System.Security.Cryptography;

namespace CardGame.Lan;

public sealed class RoomDirectory(Func<int>? seedSource = null)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, GameRoom> _rooms = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<RoomSummary> List(string? modeId = null)
    {
        lock (_gate) return _rooms.Values.Select(room => room.Summary())
            .Where(room => modeId is null || room.ModeId == modeId).ToArray();
    }
    public JoinRoomResult Create(CreateRoomRequest request)
    {
        CheckProtocol(request.ProtocolVersion);
        var mode = LanContent.Modes.SingleOrDefault(mode => mode.Id == request.ModeId)
            ?? throw new RoomException("当前服务器不支持这个模式。");
        lock (_gate)
        {
            foreach (var emptyId in _rooms.Where(pair => pair.Value.Summary().Humans == 0).Select(pair => pair.Key).ToArray())
                _rooms.Remove(emptyId);
            if (_rooms.Count >= 64) throw new RoomException("原型服务器最多保留 64 个房间，请重启服务器清理旧房间。", 409);
            string id;
            do { id = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(); } while (_rooms.ContainsKey(id));
            var room = new GameRoom(id, request.Name, mode, seedSource);
            var result = room.Join(request.PlayerName);
            _rooms.Add(id, room);
            return result;
        }
    }
    public GameRoom Get(string id)
    {
        lock (_gate) return _rooms.GetValueOrDefault(id) ?? throw new RoomException("房间不存在，或服务器已经重启。", 404);
    }
    public static void CheckProtocol(int version)
    { if (version != LanProtocol.Version) throw new RoomException("客户端与服务器协议不兼容，请使用同一批代码构建。", 409); }
}
