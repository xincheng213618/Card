using CardGame.Core;

internal static class WenheAiChecks
{
    public static void FourPublishedTricksScorePublicSurvivalViewsWithoutSelfTargetCrash()
    {
        foreach (var (kind, actionKind) in new[] { (CardKind.StarShift, LegalActionKind.StarShift),
                     (CardKind.BeamSwap, LegalActionKind.BeamSwap), (CardKind.HandRedistribution, LegalActionKind.HandRedistribution),
                     (CardKind.WenheChaos, LegalActionKind.WenheChaos) })
        {
            var game = WenheCardChecks.CreateAiFixture(kind);
            // The native legal entities/targets are unchanged by public team
            // relationships. Use a read view of independent opponents to audit
            // the survival scorer without simulating a complete eight-seat game.
            var view = game.CreateSnapshot(0) with { ModeKind = ContentModeKind.Survival };
            var actions = game.GetHumanLegalActions().Where(a => a.Kind == actionKind || a.Kind == LegalActionKind.EndPlay).ToArray();
            Need(actions.Any(a => a.Kind == actionKind) && view.Players.Skip(1).All(p => p.Hand.Count == 0),
                "Every smoke case must use genuine published native trick actions and a viewer-filtered snapshot with no foreign hand identities.");
            foreach (var policy in new[] { 1, AiDecisionPolicy.CurrentVersion }.Distinct())
            {
                var (chosen, thought) = new SimpleAiBrain(0, 7, policy).ChoosePlay(view, actions, 1);
                Need(actions.Contains(chosen) && thought.Candidates.Count == actions.Length &&
                    thought.Candidates.All(c => double.IsFinite(c.Score) && Math.Abs(c.Score) <= 151 && !string.IsNullOrWhiteSpace(c.Reason)),
                    $"Native {kind} must score every exact published candidate without inventing a self-target seat, reading hidden cards or returning an unbounded value (policy {policy}).");
                Need(kind == CardKind.WenheChaos ? chosen.Kind == actionKind : chosen.Kind == LegalActionKind.EndPlay,
                    "The public fixture conserves full HP, avoids surrendering a larger hand or playing an empty equipment redistribution, and applies Chaos pressure to independent opponents.");
            }
        }
    }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
