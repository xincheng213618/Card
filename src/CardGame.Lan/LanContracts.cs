using System.Text.Json;
using System.Text.Json.Serialization;
using CardGame.Core;

namespace CardGame.Lan;

public static class LanProtocol
{
    public const int Version = 1;
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }, MaxDepth = 64
    };
}

public enum RoomSeatKind { Empty, Human, Ai }
public enum RoomPhase { Waiting, Playing, Completed, Faulted }
public sealed record LanMode(string Id, string Name, int PlayerCount);
public sealed record RoomSeat(int Seat, RoomSeatKind Kind, string Name, bool Ready, bool IsHost);
public sealed record RoomSummary(string Id, string Name, string ModeId, string ModeName, int Capacity,
    int Humans, int Ai, RoomPhase Phase);
public sealed record RoomConnection(string RoomId, string Token, int Seat);
public sealed record JoinRoomResult(RoomConnection Connection, RoomView View);
public sealed record RoomView(RoomSummary Room, long Version, int ViewerSeat, bool IsHost,
    IReadOnlyList<RoomSeat> Seats, GameSnapshot? Game, IReadOnlyList<LanAction> Actions,
    IReadOnlyList<string> Messages);
public sealed record LanAction(string Id, string Label, int MinCards = 0, int MaxCards = 0,
    int MinTargets = 0, int MaxTargets = 0, IReadOnlyList<int>? CardIds = null,
    IReadOnlyList<int>? TargetSeats = null);
public sealed record CreateRoomRequest(string Name, string PlayerName, string ModeId, int ProtocolVersion = LanProtocol.Version);
public sealed record JoinRoomRequest(string PlayerName, int? Seat = null, int ProtocolVersion = LanProtocol.Version);
public sealed record RoomEditRequest(long ExpectedVersion, string Operation, int? Seat = null, bool? Ready = null);
public sealed record RoomInput(Guid RequestId, long ExpectedRevision, long PromptId, string ActionId,
    int[]? CardIds = null, int[]? TargetSeats = null);
public sealed record RoomReply(bool Accepted, string? Error, RoomView View);
public sealed record LanError(string Error);

public sealed class RoomException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
