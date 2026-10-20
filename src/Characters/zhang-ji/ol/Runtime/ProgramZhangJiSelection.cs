namespace CardGame.Core;

internal static class LuemingRuntime
{
    internal static bool AllowsTarget(int ownerSeat, int targetSeat, SkillProgramActivation activation, IProgramSelectionQueries queries) =>
        !ProgramInstructionResolver.Default.Features(activation).HasOperation(SkillProgramEffectOp.ResolveTargetChosenRankJudgment) ||
        HasFewerEquipment(ownerSeat, targetSeat, queries.Get<ISelectionParticipantQueries>());

    private static bool HasFewerEquipment(int ownerSeat, int targetSeat, ISelectionParticipantQueries players) =>
        targetSeat != ownerSeat && players.EquipmentCount(targetSeat) < players.EquipmentCount(ownerSeat);
}

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 14)]
internal sealed class ZhangJiSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        candidates.Targets = candidates.Targets.Where(seat => LuemingRuntime.AllowsTarget(context.OwnerSeat, seat, context.Activation, queries)).ToArray();
        return true;
    }
}
