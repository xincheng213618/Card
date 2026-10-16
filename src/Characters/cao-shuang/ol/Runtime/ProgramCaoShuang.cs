using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface ICaoShuangProgramHost
{
    SkillProgramStepOutcome InheritDeadGeneralChosenSkill(ProgramSkillFrame frame);
    SkillProgramStepOutcome PlaceDamagedCardAsDelayedTrick(ProgramSkillFrame frame);
}
internal sealed class InheritDeadGeneralChosenSkillDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.InheritDeadGeneralChosenSkill;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new CaoShuangHandler(SkillProgramEffectOp.InheritDeadGeneralChosenSkill);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(5));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CharacterDied)];
}
internal sealed class PlaceDamagedCardAsDelayedTrickDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PlaceDamagedCardAsDelayedTrick;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new CaoShuangHandler(SkillProgramEffectOp.PlaceDamagedCardAsDelayedTrick);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(4));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class CaoShuangHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int targetSeat, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.InheritDeadGeneralChosenSkill ? host.GetCapability<ICaoShuangProgramHost>().InheritDeadGeneralChosenSkill(f)
            : host.GetCapability<ICaoShuangProgramHost>().PlaceDamagedCardAsDelayedTrick(f);
}
public sealed record CaoShuangTuoguReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long DeathFrameId, long KillWindowId, int VictimSeat, bool Granted = false, int SequenceBefore = 0, int SequenceAfter = 0)
{
    private readonly IReadOnlyList<string> _skillIds = Array.AsReadOnly(Array.Empty<string>());
    public IReadOnlyList<string> SkillIds { get => _skillIds; init => _skillIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed record CaoShuangShanzhuanReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long DamageWindowId, int TargetSeat, bool Placed = false, int? CardId = null, CardLocation? From = null,
    CardKind? EffectiveKind = null, int SequenceBefore = 0, int SequenceAfter = 0);
public sealed record CaoShuangTuoguInheritedEvent(long FrameId, long DeathFrameId, int OwnerSeat, int VictimSeat,
    CardConversionSource Source, string SelectedSkillId, string GrantId, string SkillInstanceId, string? PreviousGrantId) : IGameEvent;
public sealed record CaoShuangShanzhuanPlacedEvent(long FrameId, CardConversionSource Source, int TargetSeat,
    int CardId, CardLocation From, CardKind PrintedKind, CardKind EffectiveKind, Suit EffectiveSuit,
    int SequenceBefore, int SequenceAfter) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CaoShuangTuoguReceipt? CaoShuangTuogu { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CaoShuangShanzhuanReceipt? CaoShuangShanzhuan { get; init; }
}
