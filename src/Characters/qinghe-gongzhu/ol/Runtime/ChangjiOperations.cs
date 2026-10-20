namespace CardGame.Core;

// 长姬: at any character's ending phase the owner may have that character draw
// two cards (if she dealt damage this turn) or discard two cards (if she took
// damage this turn); the availability choice is made inside the operation.
internal sealed class ChangjiEndingDamageChoiceDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChangjiEndingDamageChoice;
    public override ISkillProgramEffectHandler Handler { get; } = new ChangjiEndingDamageChoiceHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}

public sealed class ChangjiEndingDamageChoiceHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChangjiEndingDamageChoice;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IQingHeGongZhuProgramHost>().ChangjiEndingDamageChoice(f, e);
}
