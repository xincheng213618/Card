namespace CardGame.Core;

/// <summary>
/// Binds the current turn owner's discard-phase hand discards that are still in
/// the discard pile (Guzheng). The engine keeps the per-turn ledger; this
/// operation only freezes a public view of it into a named card set.
/// </summary>
internal sealed class BindDiscardPhaseDiscardsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.BindDiscardPhaseDiscards;
    public override ISkillProgramEffectHandler Handler { get; } = new BindDiscardPhaseDiscardsSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.BindDiscardPhaseDiscards,
        static (effect, context) => context.BindDiscardPhaseDiscards(effect));

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "resultBind", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(),
            resultBind: r.RequiredIdentifier("resultBind"));
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        // The pool is public discard-pile knowledge, not owner-held cards: cleanup
        // is required so the composition consumes every card it may move, while
        // unclaimed cards simply stay in the discard pile. The subset-return
        // primitive enumerates at most eight candidates, so the engine binds the
        // earliest eight ledger entries (in discard order).
        [new CreateCardSet(effect.ResultBind!, CardSubsetSelector.MaximumCandidateCount, true)];
}

public sealed class BindDiscardPhaseDiscardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.BindDiscardPhaseDiscards;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) => host.BindDiscardPhaseDiscards(
        frame, effect.ResultBind ?? throw new InvalidOperationException(
            "bindDiscardPhaseDiscards has no resultBind."));
}
