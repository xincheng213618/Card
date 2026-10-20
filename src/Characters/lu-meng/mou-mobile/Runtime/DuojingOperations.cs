using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouLuMengDuojingStage { Choosing, Taking, MovementChildren, Complete }
public sealed record MouLuMengDuojingReceipt(CardConversionSource Source, string GameplayHash,
    long WindowId, long CardUseId, long ActionId, ActualDiscardRecoveryPhaseKey Phase,
    IReadOnlyList<int> Targets, int TargetIndex = 0, MouLuMengDuojingStage Stage = MouLuMengDuojingStage.Choosing,
    int? TakenCardId = null, int? MovementSequence = null);
public sealed record MouLuMengDuojingStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long WindowId, long CardUseId, long ActionId, ActualDiscardRecoveryPhaseKey Phase, int TargetCount) : IGameEvent;
public sealed record MouLuMengDuojingOfferedEvent(long FrameId, int TargetIndex, int TargetSeat) : IGameEvent;
public sealed record MouLuMengDuojingSkippedEvent(long FrameId, int TargetIndex, int TargetSeat) : IGameEvent;
public sealed record MouLuMengDuojingArmorPaidEvent(long FrameId, int TargetIndex, int TargetSeat,
    long CardUseId, long ActionId, int ArmorBefore, int ArmorAfter) : IGameEvent;
public sealed record MouLuMengDuojingTakenEvent(long FrameId, int TargetIndex, int TargetSeat,
    int? CardId, int? MovementSequence) : IGameEvent;
public sealed record MouLuMengDuojingPhaseCreditEvent(long FrameId, int TargetIndex, int OwnerSeat,
    long CardUseId, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record MouLuMengDuojingCompletedEvent(long FrameId) : IGameEvent;
internal sealed class ResolveMouLuMengDuojingDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuMengDuojing;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new RequireCardActionActor()];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(0));
    public override ISkillProgramEffectHandler Handler { get; } = new MouLuMengHandler(SkillProgramEffectOp.ResolveMouLuMengDuojing);
}
