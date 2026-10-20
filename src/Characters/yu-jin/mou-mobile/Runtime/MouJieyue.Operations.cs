using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouYuJinJieyueStage { ArmorReserved, GiftChoice, GiftChildren, Complete }
public sealed record MouYuJinJieyueReceipt(CardConversionSource Source, string GameplayHash,
    long OriginalEndingWindowId, ActualDiscardRecoveryPhaseKey Phase, int RecipientSeat,
    MouYuJinJieyueStage Stage = MouYuJinJieyueStage.ArmorReserved, int? GiftCardId = null,
    CardLocation? GiftFrom = null, CardLocation? GiftTo = null, int? GiftMovementSequence = null);
public sealed record MouYuJinJieyueReservedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long OriginalEndingWindowId, ActualDiscardRecoveryPhaseKey Phase, int RecipientSeat) : IGameEvent;
public sealed record MouYuJinJieyueGiftPaidEvent(long FrameId, int RecipientSeat, int CardId,
    CardLocation From, CardLocation To, int MovementSequence) : IGameEvent;
public sealed record MouYuJinJieyueGiftDeclinedEvent(long FrameId, int RecipientSeat) : IGameEvent;
public sealed record MouYuJinJieyueCompletedEvent(long FrameId) : IGameEvent;
internal sealed class ResolveMouYuJinJieyueDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouYuJinJieyue;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new ReadSelectedTarget()];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift, static (_, c) => c.PublicControlValue(0));
    public override ISkillProgramEffectHandler Handler { get; } = new MouYuJinHandler(SkillProgramEffectOp.ResolveMouYuJinJieyue);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
