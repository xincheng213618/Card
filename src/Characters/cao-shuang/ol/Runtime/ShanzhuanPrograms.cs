using System.Text.Json.Serialization;

namespace CardGame.Core;
internal sealed class PlaceDamagedCardAsDelayedTrickDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PlaceDamagedCardAsDelayedTrick;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new CaoShuangHandler(SkillProgramEffectOp.PlaceDamagedCardAsDelayedTrick);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(4));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
public sealed record CaoShuangShanzhuanReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long DamageWindowId, int TargetSeat, bool Placed = false, int? CardId = null, CardLocation? From = null,
    CardKind? EffectiveKind = null, int SequenceBefore = 0, int SequenceAfter = 0);
public sealed record CaoShuangShanzhuanPlacedEvent(long FrameId, CardConversionSource Source, int TargetSeat,
    int CardId, CardLocation From, CardKind PrintedKind, CardKind EffectiveKind, Suit EffectiveSuit,
    int SequenceBefore, int SequenceAfter) : IGameEvent;
