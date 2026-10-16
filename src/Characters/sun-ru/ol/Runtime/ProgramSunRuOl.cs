using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface ISunRuOlProgramHost
{
    SkillProgramStepOutcome RewardCurrentTurnLastZoneLoss(ProgramSkillFrame frame);
    SkillProgramStepOutcome AbolishEquipmentSlotsForDistinctBenefits(ProgramSkillFrame frame);
}
internal sealed class RewardCurrentTurnLastZoneLossDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RewardCurrentTurnLastZoneLoss;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new SunRuOlHandler(SkillProgramEffectOp.RewardCurrentTurnLastZoneLoss);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(5));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)];
}
internal sealed class AbolishEquipmentSlotsForDistinctBenefitsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AbolishEquipmentSlotsForDistinctBenefits;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new SunRuOlHandler(SkillProgramEffectOp.AbolishEquipmentSlotsForDistinctBenefits);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(4));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
}
internal sealed class SunRuOlHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.RewardCurrentTurnLastZoneLoss ? host.GetCapability<ISunRuOlProgramHost>().RewardCurrentTurnLastZoneLoss(f)
            : host.GetCapability<ISunRuOlProgramHost>().AbolishEquipmentSlotsForDistinctBenefits(f);
}
public sealed record SunRuOlTurnToken(int TurnNumber, int OwnerSeat);
public sealed partial record CardMovementBatchContext
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SunRuOlTurnToken? SunRuOlLastZoneLoss { get; init; }
}
public sealed record SunRuOlChishiReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long MovementWindowId, SunRuOlTurnToken Turn, int SequenceBefore, int SequenceAfter);
public enum SunRuOlWeimianStage { Slots, CostChildren, Benefits, Applying, RestoreSlot, RestoreChildren, RecoveryChildren, DiscardChildren, DrawChildren }
public sealed record SunRuOlWeimianReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    SunRuOlTurnToken Turn, int RecipientSeat, SunRuOlWeimianStage Stage, int SlotsMask = 0, int BenefitsMask = 0,
    int NextBenefit = 1, bool Paid = false, int SequenceBefore = 0, int SequenceAfter = 0);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SunRuOlChishiReceipt? SunRuOlChishi { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SunRuOlWeimianReceipt? SunRuOlWeimian { get; init; }
}
// All newly exposed payloads are scalar or recursively immutable records.
public sealed record SunRuOlChishiAppliedEvent(long FrameId, CardConversionSource Source, long MovementWindowId,
    SunRuOlTurnToken Turn) : IGameEvent;
public sealed record SunRuOlWeimianPaidEvent(long FrameId, CardConversionSource Source, int RecipientSeat,
    SunRuOlTurnToken Turn, int SlotsMask, int SequenceBefore, int SequenceAfter) : IGameEvent;
public sealed record SunRuOlWeimianBenefitsChosenEvent(long FrameId, int RecipientSeat, int BenefitsMask) : IGameEvent;
public sealed record SunRuOlWeimianBenefitIssuedEvent(long FrameId, int RecipientSeat, int Benefit,
    EquipmentSlot? RestoredSlot, int SequenceBefore, int SequenceAfter) : IGameEvent;
