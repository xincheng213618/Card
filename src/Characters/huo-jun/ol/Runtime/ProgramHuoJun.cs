using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IHuoJunProgramHost { SkillProgramStepOutcome RestoreSlotAndUseMatchingEquipment(ProgramSkillFrame frame); }
internal sealed class RestoreSlotAndUseMatchingEquipmentDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RestoreSlotAndUseMatchingEquipment;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new RestoreSlotAndUseMatchingEquipmentHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(5));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
internal sealed class RestoreSlotAndUseMatchingEquipmentHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RestoreSlotAndUseMatchingEquipment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) =>
        h.GetCapability<IHuoJunProgramHost>().RestoreSlotAndUseMatchingEquipment(f);
}
public enum HuoJunFenruiStage { Cost, CostChildren, Restore, RestoreChildren, NativeEquipment, EquipmentChildren, DamageChoice, DamageIssued }
public sealed record HuoJunFenruiReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long EndingWindowId, int TurnNumber, int TurnOwnerSeat, HuoJunFenruiStage Stage,
    int? CostCardId = null, CardLocation? CostFrom = null, EquipmentSlot? RestoredSlot = null,
    int SequenceBefore = 0, int SequenceAfter = 0, int? EquipmentCardId = null, CardKind? EquipmentKind = null,
    long NativeUseId = 0, long NativeActionId = 0, int? DamageTargetSeat = null, int DamageAmount = 0);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HuoJunFenruiReceipt? HuoJunFenrui { get; init; }
}
public sealed record HuoJunFenruiCostPaidEvent(long FrameId, CardConversionSource Source, int CardId, CardLocation From,
    int SequenceBefore, int SequenceAfter) : IGameEvent;
public sealed record HuoJunFenruiSlotRestoredEvent(long FrameId, int OwnerSeat, EquipmentSlot Slot) : IGameEvent;
public sealed record HuoJunFenruiEquipmentIssuedEvent(long FrameId, CardConversionSource Source, EquipmentSlot Slot,
    int CardId, CardKind CardKind, long NativeUseId, long NativeActionId) : IGameEvent;
public sealed record HuoJunFenruiEquipmentReturnedEvent(long FrameId, long NativeUseId, long NativeActionId) : IGameEvent;
public sealed record HuoJunFenruiDamageCommittedEvent(long FrameId, CardConversionSource Source, int TargetSeat,
    int OwnEquipmentCount, int TargetEquipmentCount, int DamageAmount) : IGameEvent;
