using System.Text.Json.Serialization;

namespace CardGame.Core;

// Direction is the rule meaning of this request. A native Response action can
// represent a true use (Dodge/Nullification), or a card merely played for Duel.
public enum NativePublicPileDirection { Use, Played }

public sealed record NativePublicPileCardRequest(long? ParentFrameId, PromptId? OriginalPromptId,
    int ActorSeat, int ProviderSeat, int? RequesterSeat, int? ResponderSeat,
    CardKind? RequiredKind, ProgramCardContinuation? Continuation, NativePublicPileDirection Direction)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? RequestCursor { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? RequestEpoch { get; init; }
}

// AccessSource authorizes the original entity. It is not a conversion and must
// never be inserted in CardActionContext.ConversionChain.
public sealed record NativePublicPileCardSelection(PublicPersistentPileSource Pile,
    CardConversionSource AccessSource, Card Card, NativePublicPileCardRequest Request);

public sealed record NativePublicPileCardPayment(long OwnerFrameId, NativePublicPileCardSelection Selection,
    int ActualTurn, int TurnOwnerSeat, long SequenceBefore, long SequenceAfter = 0, long? ActionId = null)
{
    public CardConversionSource Source => Selection.AccessSource;
    public PublicPersistentPileSource Pile => Selection.Pile;
    public Card Card => Selection.Card;
    public NativePublicPileCardRequest Request => Selection.Request;
}

public abstract partial record ResolutionFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativePublicPileCardPayment? NativePublicPilePayment { get; init; }
}

public sealed record NativePublicPileOriginalPaidEvent(long ParentFrameId, int CardId,
    CardKind CardKind, CardLocation From, long EntrySequence, CardMoveReason Reason,
    CardConversionSource Source, int ActorSeat, int ProviderSeat, int? RequesterSeat,
    int? ResponderSeat, NativePublicPileDirection Direction, int ActualTurn, int TurnOwnerSeat,
    int? RequestCursor = null, int? RequestEpoch = null) : IGameEvent;

public sealed record NativePublicPileOriginalBoundEvent(long ParentFrameId, long ActionId,
    int CardId, long EntrySequence) : IGameEvent;
