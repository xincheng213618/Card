namespace CardGame.Core;

// 清靓: reveal the whole hand against a single-target damage card and pick
// mutual draws or a suit discard that nullifies the card against the owner.
internal sealed class QingliangChooseOptionDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.QingliangChooseOption;
    public override ISkillProgramEffectHandler Handler { get; } = new QingliangChooseOptionHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.NullifyCurrentCardEffect,
        static (_, context) => context.NullifyCurrentCardEffect());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
    [
        new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized),
        new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Target,
            [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Duel,
             CardKind.BarbarianAssault, CardKind.ArrowBarrage, CardKind.FireAttack])
    ];
}

public sealed class QingliangChooseOptionHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.QingliangChooseOption;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IRuiJiProgramHost>().QingliangChooseOption(f, e);
}
