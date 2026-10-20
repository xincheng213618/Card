using System.Text.Json.Serialization;

namespace CardGame.Core;
internal sealed class InheritDeadGeneralChosenSkillDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.InheritDeadGeneralChosenSkill;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new CaoShuangHandler(SkillProgramEffectOp.InheritDeadGeneralChosenSkill);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(5));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CharacterDied)];
}
public sealed record CaoShuangTuoguReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long DeathFrameId, long KillWindowId, int VictimSeat, bool Granted = false, int SequenceBefore = 0, int SequenceAfter = 0)
{
    private readonly IReadOnlyList<string> _skillIds = Array.AsReadOnly(Array.Empty<string>());
    public IReadOnlyList<string> SkillIds { get => _skillIds; init => _skillIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed record CaoShuangTuoguInheritedEvent(long FrameId, long DeathFrameId, int OwnerSeat, int VictimSeat,
    CardConversionSource Source, string SelectedSkillId, string GrantId, string SkillInstanceId, string? PreviousGrantId) : IGameEvent;
