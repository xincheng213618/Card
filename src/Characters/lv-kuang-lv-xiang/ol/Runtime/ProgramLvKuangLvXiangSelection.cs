namespace CardGame.Core;

internal interface ILvKuangLvXiangSelectionQueries : IProgramSelectionQuery
{ bool Allows(int ownerSeat, int targetSeat, SkillProgramActivation activation); }

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 10)]
internal sealed class LvKuangLvXiangSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var target = queries.Get<ILvKuangLvXiangSelectionQueries>();
        candidates.Targets = candidates.Targets.Where(seat => target.Allows(context.OwnerSeat, seat, context.Activation)).ToArray();
        return true;
    }
}
