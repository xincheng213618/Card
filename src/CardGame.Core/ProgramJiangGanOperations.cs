namespace CardGame.Core;

internal interface IJiangGanProgramHost
{
    SkillProgramStepOutcome DaoshuGuessAndTake(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 盗书: the owner declares a suit, takes one random concealed hand card of the
// selected target and settles branchwise — the same suit deals one damage and
// refunds the per-turn activation, a different suit hands one other-suit hand
// card back or publicly reveals the whole hand.
internal sealed class DaoshuGuessAndTakeDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DaoshuGuessAndTake;
    public override ISkillProgramEffectHandler Handler { get; } = new DaoshuGuessAndTakeHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.TakeRandomHandCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget()];
}

public sealed class DaoshuGuessAndTakeHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DaoshuGuessAndTake;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IJiangGanProgramHost)host).DaoshuGuessAndTake(f, e);
}
