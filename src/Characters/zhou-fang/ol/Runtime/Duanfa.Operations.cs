namespace CardGame.Core;

// 断发: the black hand cards selected through the private owned-card prompt are
// discarded and replaced one-for-one; the play phase may not recycle more than
// the owner's maximum health in total across uses.
internal sealed class DuanfaDiscardAndDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DuanfaDiscardAndDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new DuanfaDiscardAndDrawHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Move,
        static (effect, context) => context.Move(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadCardSet(effect.SourceBind!)];
}

public sealed class DuanfaDiscardAndDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DuanfaDiscardAndDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IZhouFangProgramHost>().DuanfaDiscardAndDraw(f, e);
}
