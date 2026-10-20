using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouGongsunZanStage { Obtain, ObtainChildren, Discard, DiscardChildren, ExtraTarget, RangeChoice }
public sealed record MouGongsunZanReceipt(CardConversionSource Source, string GameplayHash, long WindowId, long UseId,
    long ActionId, int OriginalTarget, int OwnerRange, int TargetRange)
{
    public MouGongsunZanStage Stage { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public ActualDiscardRecoveryPhaseKey? Phase { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouGongsunZanReceipt? MouGongsunZan { get; init; } }
public sealed record MouGongsunZanStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, long UseId, long ActionId, int OriginalTarget, int OwnerRange, int TargetRange) : IGameEvent;
public sealed record MouGongsunZanDamageGrantedEvent(long FrameId, long UseId, long ActionId, int Before, int After) : IGameEvent;
public sealed record MouGongsunZanMovedEvent(long FrameId, string Purpose, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, CardMoveReason Reason, int Sequence) : IGameEvent;
public sealed record MouGongsunZanExtraTargetEvent(long FrameId, long UseId, long ActionId, int TargetSeat) : IGameEvent;
public sealed record MouGongsunZanBaojingGrantedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int TargetSeat, int Amount, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record MouGongsunZanCompletedEvent(long FrameId, long UseId) : IGameEvent;

internal interface IMouGongsunZanHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MouGongsunZanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler => new MouGongsunZanHandler(Op);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectTarget, static (_, c) => c.PublicControlValue(1));
}
internal sealed class ResolveMouGongsunZanJiaodiDescriptor : MouGongsunZanDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouGongsunZanJiaodi;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new RequireCardActionActor()];
}
internal sealed class ResolveMouGongsunZanBaojingDescriptor : MouGongsunZanDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouGongsunZanBaojing;
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)];
}
internal sealed class MouGongsunZanHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouGongsunZanHost>().Resolve(f, op);
}
