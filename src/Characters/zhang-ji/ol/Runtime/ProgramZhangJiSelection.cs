namespace CardGame.Core;

internal interface IZhangJiSelectionQueries : IProgramSelectionQuery
{ bool Allows(int ownerSeat, int targetSeat, SkillProgramActivation activation); }

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 14)]
internal sealed class ZhangJiSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var target = queries.Get<IZhangJiSelectionQueries>();
        candidates.Targets = candidates.Targets.Where(seat => target.Allows(context.OwnerSeat, seat, context.Activation)).ToArray();
        return true;
    }
}
