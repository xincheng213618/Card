namespace CardGame.Core;

internal interface IBuZhiSelectionQueries : IProgramSelectionQuery
{ bool Allows(int targetSeat, SkillProgramActivation activation); }

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 12)]
internal sealed class BuZhiSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var target = queries.Get<IBuZhiSelectionQueries>();
        candidates.Targets = candidates.Targets.Where(seat => target.Allows(seat, context.Activation)).ToArray();
        return true;
    }
}
