using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record DongxinUseReceipt(long GrantFrameId,int CardId,long AcquiredSequence,int OriginalActor,int Provider,long UseId,long ActionId);
public sealed record DongxinDamageIssuedEvent(long FrameId,int SourceSeat,int TargetSeat):IGameEvent;
public sealed record DongxinEntityAcquiredEvent(long FrameId,CardConversionSource Source,int RecipientSeat,int CardId,CardKind EffectiveKind,long DiscardSequence,long AcquiredSequence):IGameEvent;
public sealed record DongxinEntityDepartedEvent(long GrantFrameId,int RecipientSeat,int CardId,long Sequence):IGameEvent;
public sealed record DongxinEntityUseBoundEvent(DongxinUseReceipt Receipt):IGameEvent;
internal sealed class ResolveDongxinDamageDiscardAndReclaimDescriptor():SunYuanDescriptor(SkillProgramEffectOp.ResolveDongxinDamageDiscardAndReclaim,SkillProgramTriggerWindow.AfterDamageApplied) {
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.SunYuan is { Paid: true };
}
