using System.Text.Json.Serialization;

namespace CardGame.Core;
public enum HuanpeiIntent { Play, OwnSlashDodge, Dying, BorrowedSword, Qinglong, ProgramSlash, ProgramNearestSlash, AssistedSlash, NearestLegalSlash, WenyiPeach, FixedSlash, KeBiNengBasic }
public sealed record HuanpeiNativeRequest(HuanpeiIntent Intent, long? ParentFrameId, long? RequestFrameId,
    long? ParentActionId, int ActorSeat, int Cursor, CardKind? RequiredKind, int? FixedTargetSeat,
    PromptId OriginalPromptId, long OriginalRevision)
{
    public string? ParentSkillId { get; init; }
    public string? ParentSkillInstanceId { get; init; }
    public string? ParentGameplayHash { get; init; }
    public SkillProgramEffectOp? ParentOperation { get; init; }
}
public sealed record HuanpeiPaidOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int Round, int ActualTurn, int TurnOwner, HuanpeiNativeRequest Request, int FrozenHandCount,
    int FrozenHp, int AdjustmentCount, long SequenceBefore, long SequenceAfter, CardKind Kind)
{
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record HuanpeiUseReceipt(HuanpeiPaidOrigin Payment, long OwnerFrameId, long CardActionId);
public sealed record HuanpeiProgramReturn(HuanpeiPaidOrigin Payment, long UseId, long ActionId, bool Returned = false);
public sealed record HuanpeiAdjustmentAcceptedEvent(long FrameId, int OwnerSeat, int Round, int HandCount, int Hp, int RequestedCount, long Before) : IGameEvent;
public sealed record HuanpeiCostEntityEvent(long FrameId, int Index, int CardId, CardKind Kind, CardLocation From, long Sequence) : IGameEvent;
public sealed record HuanpeiAdjustmentCompletedEvent(long FrameId, long Before, long After, int ActualDrawCount, int ReplacementCount) : IGameEvent;
public sealed record HuanpeiOutputChosenEvent(long FrameId, CardKind Kind, int TargetCount) : IGameEvent;
public sealed record HuanpeiTargetChosenEvent(long FrameId, int Index, int TargetSeat) : IGameEvent;
public sealed record HuanpeiIssuedEvent(long PaymentFrameId, long UseId, long ActionId, int ActorSeat, CardKind Kind, HuanpeiIntent Intent) : IGameEvent;
public sealed record HuanpeiReturnedEvent(long PaymentFrameId, long UseId, long ActionId) : IGameEvent;
public sealed record HuanpeiRequestCapturedEvent(long FrameId, HuanpeiNativeRequest Request, string DecisionHash) : IGameEvent;
public sealed record HuanpeiKeActionAcceptedEvent(long ParentFrameId, int Cursor, CardConversionSource ParentSource, string ParentHash, long PaymentFrameId, CardKind Kind) : IGameEvent;
public sealed record HuanpeiCancelledEvent(long FrameId, bool Paid) : IGameEvent;
internal sealed class ResolveHuanpeiHandAdjustmentAndBasicUseDescriptor() : HuanShuJunDescriptor(SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse){
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
