namespace CardGame.Core;

/// <summary>A serializable ordered subset of the frozen, public discard batch.</summary>
public sealed record ProgramDiscardTopPlacement(
    IReadOnlyList<int> CandidateCardIds,
    IReadOnlyList<int> SelectedCardIds);

public interface IProgramDiscardTopPlacementHost
{
    SkillProgramStepOutcome PutDiscardedCardsOnDrawPileTop(ProgramSkillFrame frame);
}

internal sealed class PutDiscardedCardsOnDrawPileTopProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PutDiscardedCardsOnDrawPileTop;
    public override ISkillProgramEffectHandler Handler { get; } = new PutDiscardedCardsOnDrawPileTopSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Move, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class PutDiscardedCardsOnDrawPileTopSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PutDiscardedCardsOnDrawPileTop;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        (host as IProgramDiscardTopPlacementHost ?? throw new NotSupportedException("Discard placement requires the rules host."))
            .PutDiscardedCardsOnDrawPileTop(frame);
}
