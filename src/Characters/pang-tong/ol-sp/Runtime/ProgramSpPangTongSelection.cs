namespace CardGame.Core;

internal static class GuolunRuntime
{
    internal static bool AllowsTarget(int targetSeat, SkillProgramActivation activation, IProgramSelectionQueries queries) =>
        !ProgramInstructionResolver.Default.Features(activation).HasOperation(SkillProgramEffectOp.ResolveGuolunRevealOptionalAtomicExchange) ||
        HasLivingHandTarget(targetSeat, queries.Get<ISelectionParticipantQueries>());

    internal static bool HasLivingHandTarget(int targetSeat, ISelectionParticipantQueries players) =>
        players.IsAlive(targetSeat) && players.HandCount(targetSeat) > 0;
}

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 11)]
internal sealed class SpPangTongSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        candidates.Targets = candidates.Targets.Where(seat => GuolunRuntime.AllowsTarget(seat, context.Activation, queries)).ToArray();
        return true;
    }
}
