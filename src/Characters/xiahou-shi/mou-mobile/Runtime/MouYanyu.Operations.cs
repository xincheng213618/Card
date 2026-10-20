using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouXiahouShiYanyuStage { Discarding, DiscardChildren, TargetChoice, Drawing, DrawChildren }
public sealed record MouXiahouShiYanyuReceipt(CardConversionSource Source, string GameplayHash,
    MouXiahouShiTurnKey Turn, ActualDiscardRecoveryPhaseKey Phase, bool Support)
{
    public MouXiahouShiYanyuStage Stage { get; init; }
    public long WindowId { get; init; }
    public MouXiahouShiDiscardPayment? Payment { get; init; }
    public int FrozenDiscardCount { get; init; }
    public int? TargetSeat { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
}
public sealed record MouXiahouShiYanyuStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    MouXiahouShiTurnKey Turn, ActualDiscardRecoveryPhaseKey Phase, bool Support, long WindowId, int FrozenDiscardCount) : IGameEvent;
public sealed record MouXiahouShiYanyuDiscardPaidEvent(long FrameId, CardConversionSource Source,
    MouXiahouShiTurnKey Turn, ActualDiscardRecoveryPhaseKey Phase, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouXiahouShiYanyuTargetAcceptedEvent(long FrameId, int TargetSeat, int FrozenDiscardCount, int RequestedDraw) : IGameEvent;
internal sealed class ResolveMouXiahouShiYanyuDiscardDescriptor : MouXiahouShiDescriptor
{
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Activation;
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXiahouShiYanyuDiscard;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ConsumeSelectedCards(1)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DiscardSelected, static (e, c) =>
    { c.DiscardSelected(new(SkillProgramEffectOp.DiscardSelected, SkillProgramEffectTarget.Owner, 1, e.Condition)); c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, e.Condition)); c.PublicControlValue(3); });
    public override ISkillProgramEffectHandler Handler { get; } = new MouXiahouShiDiscardHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class ResolveMouXiahouShiYanyuSupportDescriptor : MouXiahouShiDescriptor
{
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXiahouShiYanyuSupport;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(6));
    public override ISkillProgramEffectHandler Handler { get; } = new MouXiahouShiSupportHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class MouXiahouShiDiscardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXiahouShiYanyuDiscard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouXiahouShiHost>().Discard(f);
}
internal sealed class MouXiahouShiSupportHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXiahouShiYanyuSupport;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouXiahouShiHost>().Support(f);
}
