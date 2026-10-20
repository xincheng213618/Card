namespace CardGame.Core;

[ProgramRuntimeModule]
internal sealed class BihuoRoundDistanceRuntime(IDistanceRuleRuntimeQueries queries, ISelectionParticipantQueries participants)
{
    [ProgramDistanceRule(ProgramDistanceStage.IncomingContributions, 20)]
    private IEnumerable<RuleQueryContribution> YanPuIncomingDistanceContributions(int target) =>
        queries.History<YanPuRoundDistanceIssuedEvent>().Where(fact => fact.Round == queries.RoundNumber && fact.TargetSeat == target &&
            participants.IsAlive(target) && queries.History<DyingResolvedEvent>().Any(dying =>
                dying.ResolutionId == fact.DyingFrameId && dying.VictimSeat == target && dying.Survived))
        .Select(fact => (RuleQueryContribution)new FiniteRuleQueryContribution($"yanpu-bihuo:{fact.ProgramFrameId}",
            SkillRuleOperation.Add, queries.AliveCount));
}
