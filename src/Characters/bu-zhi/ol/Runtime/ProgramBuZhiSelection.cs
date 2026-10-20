namespace CardGame.Core;

internal interface ISelectionRoleQueries : IProgramSelectionQuery
{ Role Role(int seat); }

internal static class DingpanRuntime
{
    internal static bool IsActivation(SkillProgramActivation activation) =>
        ProgramInstructionResolver.Default.Features(activation).HasOperation(SkillProgramEffectOp.ResolveRebelCountEquipmentChoice);

    internal static bool AllowsTarget(int targetSeat, SkillProgramActivation activation, IProgramSelectionQueries queries) =>
        !IsActivation(activation) || HasEquippedLivingTarget(targetSeat, queries.Get<ISelectionParticipantQueries>());

    internal static bool HasEquippedLivingTarget(int targetSeat, ISelectionParticipantQueries players) =>
        players.IsAlive(targetSeat) && players.EquipmentCount(targetSeat) > 0;

    internal static int? PhaseLimit(SkillProgramActivation activation, IProgramSelectionQueries queries)
    {
        if (!IsActivation(activation)) return activation.UsesPerPhase;
        var players = queries.Get<ISelectionParticipantQueries>();
        var roles = queries.Get<ISelectionRoleQueries>();
        return players.Seats.Count(seat => players.IsAlive(seat) && roles.Role(seat) == Role.Rebel);
    }
}

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 12)]
internal sealed class BuZhiSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        candidates.Targets = candidates.Targets.Where(seat => DingpanRuntime.AllowsTarget(seat, context.Activation, queries)).ToArray();
        return true;
    }
}
