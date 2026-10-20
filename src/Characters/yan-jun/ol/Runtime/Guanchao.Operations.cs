namespace CardGame.Core;

// 观潮 launch: at the play phase start the owner commits this phase to strictly
// ascending or strictly descending used-card ranks.
internal sealed class GuanchaoChoosePatternDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GuanchaoChoosePattern;
    public override ISkillProgramEffectHandler Handler { get; } = new GuanchaoChoosePatternHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}

public sealed class GuanchaoChoosePatternHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GuanchaoChoosePattern;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IYanJunProgramHost>().GuanchaoChoosePattern(f, e);
}

// 观潮 draw: every committed own play-phase use checks the whole rank ledger of
// this phase (including the current card) against the committed pattern.
internal sealed class GuanchaoRankDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GuanchaoRankDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new GuanchaoRankDrawHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.AdjustNormalDraw,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
    [
        new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted),
        new RequireCardActionActor()
    ];
}

public sealed class GuanchaoRankDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GuanchaoRankDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IYanJunProgramHost>().GuanchaoRankDraw(f, e);
}
