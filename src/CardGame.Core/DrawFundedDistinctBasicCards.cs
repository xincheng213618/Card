using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum DrawFundedDistinctBasicIntent
{
    Play = 0, BorrowedSword = 1, Qinglong = 2, OwnSlashDodge = 3, Dying = 4,
    ProgramSlash = 5, ProgramNearestSlash = 6, AssistedSlash = 7, NearestLegalSlash = 8
}
public enum DrawFundedDistinctBasicStage { Drawing = 0, DrawChildren = 1 }

/// <summary>An accepted need and one actual draw attempt. Private hand identities/colors are never copied here.</summary>
public sealed record DrawFundedDistinctBasicPayment(long PaymentFrameId, CardConversionSource Source,
    string GameplayHash, string MethodLedgerId, int ActualTurnNumber, int ActualTurnOwnerSeat,
    DrawFundedDistinctBasicIntent Intent, CardKind EffectiveKind, CardKind NormalizedName,
    long? ParentFrameId, long? RequestFrameId, long? ParentActionId, int? TargetSeat, int Cursor,
    PromptId OriginalPromptId, long OriginalRevision, long SequenceBefore, long SequenceAfter, int ActualDrawCount);

public sealed record DrawFundedDistinctBasicUseReceipt(DrawFundedDistinctBasicPayment Payment, long OwnerFrameId, long CardActionId);

public sealed record DrawFundedDistinctBasicFrame : ResolutionFrame
{
    private PendingDecision _originalDecision = new(DecisionKind.PlayCard, 0, "", [], []);
    private IReadOnlyList<int> _targetSeats = Array.AsReadOnly(Array.Empty<int>());
    [JsonConstructor]
    public DrawFundedDistinctBasicFrame(long id, DrawFundedDistinctBasicPayment payment, PendingDecision originalDecision,
        IReadOnlyList<int> targetSeats, DrawFundedDistinctBasicStage stage = DrawFundedDistinctBasicStage.Drawing,
        ResolutionFrameStep step = ResolutionFrameStep.ResolvingEffect)
        : base(id, ResolutionFrameKind.DrawFundedDistinctBasic, step)
    { Payment = payment; OriginalDecision = originalDecision; TargetSeats = targetSeats; Stage = stage; }
    public DrawFundedDistinctBasicPayment Payment { get; init; }
    public PendingDecision OriginalDecision { get => _originalDecision; init => _originalDecision = RequestedDeckBasicFrame.FreezeDecision(value); }
    public IReadOnlyList<int> TargetSeats { get => _targetSeats; init => _targetSeats = Array.AsReadOnly(value.ToArray()); }
    public DrawFundedDistinctBasicStage Stage { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? ActiveChildFrameId { get; init; }
}

// Scalar facts: no hand cards, hand colors, chosen draw identity or nested mutable collections.
public sealed record DrawFundedDistinctBasicStartedEvent(long PaymentFrameId, int ActorSeat, CardConversionSource Source,
    string GameplayHash, string MethodLedgerId, int ActualTurnNumber, int ActualTurnOwnerSeat, DrawFundedDistinctBasicIntent Intent,
    CardKind EffectiveKind, long? ParentFrameId, long? RequestFrameId, long? ParentActionId, int? TargetSeat, int Cursor,
    PromptId OriginalPromptId, long OriginalRevision) : IGameEvent;
public sealed record DrawFundedDistinctBasicPaidEvent(long PaymentFrameId, int ActorSeat, long SequenceBefore, long SequenceAfter,
    int ActualDrawCount) : IGameEvent;
public sealed record DrawFundedDistinctBasicIssuedEvent(long PaymentFrameId, long OwnerFrameId, long CardActionId, int ActorSeat,
    string MethodLedgerId, int ActualTurnNumber, int ActualTurnOwnerSeat, CardKind NormalizedName, CardKind EffectiveKind,
    DrawFundedDistinctBasicIntent Intent, CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record DrawFundedDistinctBasicCancelledEvent(long PaymentFrameId, int ActorSeat, DrawFundedDistinctBasicIntent Intent,
    CardKind EffectiveKind) : IGameEvent;
public sealed record DrawFundedDistinctBasicReturnedEvent(long PaymentFrameId, long OwnerFrameId, long CardActionId,
    int ActorSeat, DrawFundedDistinctBasicIntent Intent, CardKind EffectiveKind) : IGameEvent;
