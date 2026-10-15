namespace CardGame.Core;

internal interface ISpPangTongSelectionQueries : IProgramSelectionQuery
{ bool Allows(int targetSeat, SkillProgramActivation activation); }

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 11)]
internal sealed class SpPangTongSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var target = queries.Get<ISpPangTongSelectionQueries>();
        candidates.Targets = candidates.Targets.Where(seat => target.Allows(seat, context.Activation)).ToArray();
        return true;
    }
}
