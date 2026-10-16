namespace CardGame.Core;

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 26500, SkillProgramEffectOp.ResolveZhenyingSimultaneousHands)]
internal sealed class HaoPuTargetFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var q = queries.Get<ISelectionParticipantQueries>();
        candidates.Targets = candidates.Targets.Where(s => s != context.OwnerSeat && q.IsAlive(s) && q.HandCount(s) <= q.HandCount(context.OwnerSeat)).ToArray();
        return true;
    }
}
[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 26500, SkillProgramEffectOp.ResolveZhenyingSimultaneousHands)]
internal sealed class HaoPuTargetValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries)
    {
        var q = queries.Get<ISelectionParticipantQueries>();
        return request.Cards.Count != 0 || request.Targets is not [var s] || !q.IsValidSeat(s) || s == request.Context.OwnerSeat ||
            !q.IsAlive(s) || q.HandCount(s) > q.HandCount(request.Context.OwnerSeat)
            ? new(CommandErrorCode.InvalidTarget, "镇荧的另一名角色须存活且手牌数不大于发动者。") : null;
    }
}
