using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum FriendPangTongStage { Select, Placement, Exchange, Use, Issued, Returned, Cleanup, Reward, Complete, Movement }
public sealed record FriendPangTongReceipt(int InstructionIndex, SkillProgramEffectOp Operation,
    FriendPangTongStage Stage, IReadOnlyList<int> Cards, IReadOnlyList<int> Selected, IReadOnlyList<Suit> UsedSuits,
    FriendPangTongStage NextStage = FriendPangTongStage.Complete, int ExchangeIndex = 0,
    long Before = 0, long After = 0, long? UseId = null)
{
    private readonly IReadOnlyList<int> _cards = Array.AsReadOnly(Cards.ToArray());
    private readonly IReadOnlyList<int> _selected = Array.AsReadOnly(Selected.ToArray());
    private readonly IReadOnlyList<Suit> _usedSuits = Array.AsReadOnly(UsedSuits.ToArray());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<Suit> UsedSuits { get => _usedSuits; init => _usedSuits = Array.AsReadOnly(value.ToArray()); }
}
public sealed record FriendYangmingUseReturn(long ProgramFrameId, int InstructionIndex, int CardId, Suit Suit,
    string SkillInstanceId, string GameplayHash, long CardActionId);
public sealed record FriendPangTongMovementPaidEvent(long ProgramFrameId, SkillProgramEffectOp Operation,
    long Before, long After) : IGameEvent;
public sealed record FriendYangmingStoredEvent(long ProgramFrameId, int OwnerSeat, int Count) : IGameEvent;
public sealed record FriendYangmingUseIssuedEvent(long CardUseFrameId, int OwnerSeat, FriendYangmingUseReturn Return) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FriendPangTongReceipt? FriendPangTong { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FriendYangmingUseReturn? FriendYangmingReturn { get; init; }
}
