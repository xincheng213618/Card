namespace CardGame.Core;

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 26710, SkillProgramEffectOp.ResolveMouzhuHandDemandAndAssault)]
internal sealed class HeJinTargetFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var q = queries.Get<ISelectionParticipantQueries>();
        candidates.Targets = candidates.Targets.Where(s => s != context.OwnerSeat && q.IsAlive(s) && q.HandCount(s) > 0).ToArray();
        return true;
    }
}
[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 26710, SkillProgramEffectOp.ResolveMouzhuHandDemandAndAssault)]
internal sealed class HeJinTargetValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries)
    {
        var q = queries.Get<ISelectionParticipantQueries>();
        return request.Cards.Count != 0 || request.Targets is not [var s] || !q.IsValidSeat(s) || s == request.Context.OwnerSeat || !q.IsAlive(s) || q.HandCount(s) == 0
            ? new(CommandErrorCode.InvalidTarget, "谋诛须选择一名有手牌的其他存活角色。") : null;
    }
}
