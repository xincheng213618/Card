namespace CardGame.Core;

// 扫狄: one optional finalized-target offer turns every character standing
// between the owner and the single designated target into targets of the same
// card. The estimate prices the owner's expanded attack as a one-card gain so
// the optional trigger path accepts it.
internal sealed class SaodiExpandTargetsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SaodiExpandTargets;
    public override ISkillProgramEffectHandler Handler { get; } = new SaodiExpandTargetsHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
    [
        new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized),
        new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Actor,
            [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Duel,
             CardKind.Dismantlement, CardKind.Snatch, CardKind.FireAttack, CardKind.IronChain,
             CardKind.BorrowedSword])
    ];
}

public sealed class SaodiExpandTargetsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SaodiExpandTargets;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ITianYuProgramHost>().SaodiExpandTargets(f, e);
}
