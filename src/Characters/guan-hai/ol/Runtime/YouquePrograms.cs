using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record YouqueSlashOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int ActorSeat, int TargetSeat, long UseId, long ActionId);
public sealed record YouquePindianIssuedEvent(long FrameId, long PindianId, CardConversionSource Source, string GameplayHash, PublicPersistentPileSource Pile, int CardId, int Rank, int OpponentSeat, ActualDiscardRecoveryPhaseKey Phase, long Before) : IGameEvent;
public sealed record YouquePindianReturnedEvent(long FrameId, long PindianId, int SourceCardId, int OpponentCardId, int SourceRank, int OpponentRank) : IGameEvent;
public sealed record YouqueDiscardMaterialEvent(long FrameId, int Index, int LoserSeat, int CardId, CardKind Kind, CardLocation From) : IGameEvent;
public sealed record YouqueDiscardPaidEvent(long FrameId, int Index, long BatchId, int CardId, long Sequence) : IGameEvent;
public sealed record YouqueSlashIssuedEvent(YouqueSlashOrigin Origin) : IGameEvent;
public sealed record YouqueSlashReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record YouqueBenefitIssuedEvent(long FrameId, long ParentProgramId, long UseId, long DamageFrameId, int RecipientSeat) : IGameEvent;
public sealed record YouqueRewardDrawReturnedEvent(long FrameId, long UseId, long DamageFrameId, int RecipientSeat, int Requested, int Actual, long Before, long After, bool Replaced) : IGameEvent;
public sealed record YouquePhaseDisabledEvent(long FrameId, int OwnerSeat, ActualDiscardRecoveryPhaseKey Phase, CardConversionSource Source) : IGameEvent;
internal sealed class ResolveYouqueMinimumBaitPindianDescriptor() : GuanHaiDescriptor(SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian){
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.GuanHai is { Paid: true };
}
internal sealed class ResolveYouqueIssuedSlashDamageBenefitDescriptor() : GuanHaiDescriptor(SkillProgramEffectOp.ResolveYouqueIssuedSlashDamageBenefit){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.GuanHai is { Paid: true };
}
