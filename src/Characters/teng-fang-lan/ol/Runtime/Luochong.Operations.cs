namespace CardGame.Core;

// 落宠: one optional invocation offers every available (option, target) pair in a
// single public prompt; the per-round option/target ledgers and the removed
// options live in committed history, so the prompt is the only private-free state.
internal sealed class LuochongResolveDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.LuochongResolve;
    public override ISkillProgramEffectHandler Handler { get; } = new LuochongResolveHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 2, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindows([SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
         SkillProgramTriggerWindow.AfterDamageApplied])];
}

public sealed class LuochongResolveHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.LuochongResolve;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ITengFangLanProgramHost>().LuochongResolve(f, e);
}
