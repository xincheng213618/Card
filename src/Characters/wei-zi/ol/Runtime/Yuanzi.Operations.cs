namespace CardGame.Core;

// 援资 arm: the accepted whole-hand gift rides the owner's own preparation-less
// turn-start window; the estimate prices the recipient's gain and the owner's
// whole-hand loss through the shared target scoring path.
internal sealed class YuanziResolveDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YuanziResolve;
    public override ISkillProgramEffectHandler Handler { get; } = new YuanziResolveHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift,
        static (_, context) => context.AllHandTurnOwnerGift());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

public sealed class YuanziResolveHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YuanziResolve;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IWeiZiProgramHost>().YuanziResolve(f, e);
}

// 援资 payoff: the locked after-damage sweep turns into an optional draw prompt
// only when the armed recipient dealt the damage inside the armed turn; the
// estimate stays neutral because the prompt carries its own decline.
internal sealed class YuanziDamageDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YuanziDamageDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new YuanziDamageDrawHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}

public sealed class YuanziDamageDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YuanziDamageDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IWeiZiProgramHost>().YuanziDamageDraw(f, e);
}
