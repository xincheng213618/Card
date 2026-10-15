namespace CardGame.Core;

internal interface IHeQiSelectionQueries : IProgramSelectionQuery
{ bool Allows(int ownerSeat, int targetSeat, SkillProgramActivation activation); }

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 13)]
internal sealed class HeQiSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var target = queries.Get<IHeQiSelectionQueries>();
        candidates.Targets = candidates.Targets.Where(seat => target.Allows(context.OwnerSeat, seat, context.Activation)).ToArray();
        return true;
    }
}
