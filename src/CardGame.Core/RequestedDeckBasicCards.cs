using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum RequestedDeckBasicIntent
{
    Dodge, Group, Duel, Dying, FactionSlash, FactionDodge,
    BorrowedSword, Qinglong, ProgramSlash, ProgramNearestSlash, AssistedSlash, NearestLegalSlash
}

/// <summary>One exact existing need, never an independent pending/use-ID table.</summary>
public sealed record RequestedDeckBasicMaterial(
    long OwnerFrameId, long RequestFrameId, PromptId OriginalPromptId, long OriginalRevision,
    RequestedDeckBasicIntent Intent, int ActorSeat, int Cursor,
    CardConversionSource Source, string GameplayHash, int ViewedCount,
    int? SelectedCardId, CardKind? SelectedKind, bool Claiming = false)
{
    public long ViewFrameId { get; init; }
    public long? PaidMovementSequence { get; init; }
}

public sealed record RequestedDeckBasicFrame : ResolutionFrame
{
    private IReadOnlyList<int> _cardIds = Array.AsReadOnly(Array.Empty<int>());
    private PendingDecision _originalDecision = new(DecisionKind.RespondDodge, 0, "", [], []);
    [JsonConstructor]
    public RequestedDeckBasicFrame(long id, long parentFrameId, RequestedDeckBasicMaterial receipt,
        PendingDecision originalDecision, IReadOnlyList<int> cardIds,
        ResolutionFrameStep step = ResolutionFrameStep.AwaitingResponse)
        : base(id, ResolutionFrameKind.RequestedDeckBasic, step)
    { ParentFrameId = parentFrameId; Receipt = receipt; OriginalDecision = originalDecision; CardIds = cardIds; }
    public long ParentFrameId { get; init; }
    public RequestedDeckBasicMaterial Receipt { get; init; }
    public PendingDecision OriginalDecision
    {
        get => _originalDecision;
        init => _originalDecision = FreezeDecision(value);
    }
    public IReadOnlyList<int> CardIds { get => _cardIds; init => _cardIds = Array.AsReadOnly(value.ToArray()); }
    internal static PendingDecision FreezeDecision(PendingDecision d) => d with
    {
        ValidCardIds = Array.AsReadOnly(d.ValidCardIds.ToArray()), ValidTargetSeats = Array.AsReadOnly(d.ValidTargetSeats.ToArray()),
        ValidContentIds = Array.AsReadOnly(d.ValidContentIds.ToArray()),
        Choices = Array.AsReadOnly(d.Choices.Select(c => new PromptChoice(c.Id, c.Description,
            Array.AsReadOnly(c.Cards.ToArray()), Array.AsReadOnly(c.Targets.ToArray()),
            new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(new Dictionary<string,string>(c.Parameters)))
            { ContentIds = Array.AsReadOnly(c.ContentIds.ToArray()) }).ToArray())
    };
}

// No viewed IDs enter public facts. A selected ID becomes public only after its real payment.
public sealed record RequestedDeckBasicViewedEvent(long FrameId, long OwnerFrameId, int ActorSeat,
    string SkillId, int Count, RequestedDeckBasicIntent Intent) : IGameEvent;
public sealed record RequestedDeckBasicPaidEvent(long OwnerFrameId, int ActorSeat, int CardId,
    CardKind CardKind, string SkillId, RequestedDeckBasicIntent Intent) : IGameEvent;
