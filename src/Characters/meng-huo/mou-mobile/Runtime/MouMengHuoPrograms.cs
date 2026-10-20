using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record MouMengHuoTurnKey(int TurnNumber, int OwnerSeat, int Ordinal);
public sealed record MouMengHuoAttribution(CardConversionSource Source, string GameplayHash, long UseId, long ActionId, int OriginalActorSeat);
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouMengHuoAttribution? MouMengHuoDamageSource { get; init; } }
public enum MouMengHuoStage { Selecting, RecipientChoice, DiscardChildren, RecoveryChildren, Drawing, DrawChildren, ObtainChildren }
public sealed record MouMengHuoPayment(int CardId, CardKind PrintedKind, CardLocation From, CardLocation To, long Sequence, long InvocationId);
public sealed record MouMengHuoReceipt(CardConversionSource Source, string GameplayHash, long WindowId, bool Obtain, int Budget)
{
    private readonly IReadOnlyList<int> _targets = Array.Empty<int>();
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public MouMengHuoStage Stage { get; init; }
    public bool Accepted { get; init; }
    public int Cursor { get; init; }
    public MouMengHuoPayment? Payment { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouMengHuoReceipt? MouMengHuo { get; init; } }
public sealed record MouMengHuoAttributionIssuedEvent(long FrameId, MouMengHuoAttribution Attribution) : IGameEvent;
public sealed record MouMengHuoBarbarianUsedEvent(int ActorSeat, ActualDiscardRecoveryPhaseKey Phase, long UseId, long ActionId) : IGameEvent;
public sealed record MouMengHuoChargeGainedEvent(long FrameId, CardConversionSource Source, MouMengHuoTurnKey Turn, long DamageFrameId, int Before, int After) : IGameEvent;
public sealed record MouMengHuoStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long WindowId, bool Obtain, int Budget) : IGameEvent;
public sealed record MouMengHuoTargetSelectedEvent(long FrameId, int Index, int TargetSeat) : IGameEvent;
public sealed record MouMengHuoChargeSpentEvent(long FrameId, int OwnerSeat, int Count, int Before, int After) : IGameEvent;
public sealed record MouMengHuoRecipientDecidedEvent(long FrameId, int Index, int RecipientSeat, string Branch) : IGameEvent;
public sealed record MouMengHuoDiscardPaidEvent(long FrameId, int Index, int RecipientSeat, MouMengHuoPayment Payment) : IGameEvent;
public sealed record MouMengHuoRecoveryIssuedEvent(long FrameId, int Index, int SourceSeat, int TargetSeat, int Requested, int HpBefore) : IGameEvent;
public sealed record MouMengHuoObtainEvent(long FrameId, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouMengHuoDrawReturnedEvent(long FrameId, int Index, int Requested, int Actual, int Replacement, long Before, long After, long? AttemptId) : IGameEvent;
public sealed record MouMengHuoMovementSignedEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record MouMengHuoCompletedEvent(long FrameId, bool Obtain, bool Accepted, int Recipients) : IGameEvent;

internal interface IMouMengHuoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class MouMengHuoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.ResolveMouMengHuoZaiqi ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(Op switch
    {
        SkillProgramEffectOp.ResolveMouMengHuoAttribution => SkillProgramTriggerWindow.CardUseTargetsFinalized,
        SkillProgramEffectOp.ObtainMouMengHuoBarbarian => SkillProgramTriggerWindow.PlayPhaseStarting,
        SkillProgramEffectOp.GainMouMengHuoCharge => SkillProgramTriggerWindow.AfterDamageApplied,
        _ => SkillProgramTriggerWindow.DiscardPhaseEnded
    })];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler => new MouMengHuoHandler(Op);
}
internal sealed class ResolveMouMengHuoAttributionDescriptor : MouMengHuoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouMengHuoAttribution; }
internal sealed class ObtainMouMengHuoBarbarianDescriptor : MouMengHuoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainMouMengHuoBarbarian;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class GainMouMengHuoChargeDescriptor : MouMengHuoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.GainMouMengHuoCharge; }
internal sealed class ResolveMouMengHuoZaiqiDescriptor : MouMengHuoDescriptor {
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouMengHuoZaiqi;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class MouMengHuoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouMengHuoHost>().Resolve(f, op);
}
