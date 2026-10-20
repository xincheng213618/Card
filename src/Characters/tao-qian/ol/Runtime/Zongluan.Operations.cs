using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record ZongluanSlashOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int ActorSeat, long UseId, long ActionId, int ActualTurn, int TurnOwner);
public sealed record ZongluanCommittedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActorSeat, int ActualTurn) : IGameEvent;
public sealed record ZongluanSlashIssuedEvent(ZongluanSlashOrigin Origin, int TargetCount) : IGameEvent;
public sealed record ZongluanInitialTargetEvent(long FrameId, long UseId, int Index, int TargetSeat) : IGameEvent;
public sealed record ZongluanDirectDamageEvent(long FrameId, long UseId, long DamageFrameId, int SourceSeat, int TargetSeat, int Amount) : IGameEvent;
public sealed record ZongluanSlashReturnedEvent(long FrameId, long UseId, long ActionId, int DamageRecipientCount) : IGameEvent;
public sealed record ZongluanDiscardIssuedEvent(long FrameId, int DamageRecipientCount, int RequiredCount, long Before) : IGameEvent;
public sealed record ZongluanDiscardEntityEvent(long FrameId, int Index, long BatchId, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
internal sealed class ResolveZongluanDescriptor() : TaoQianDescriptor(SkillProgramEffectOp.ResolveZongluanPreparationSlashAndDiscard){
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
