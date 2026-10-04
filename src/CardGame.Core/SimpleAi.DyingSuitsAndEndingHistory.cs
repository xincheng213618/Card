namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    internal (PromptChoice Choice, AiThoughtRecord Thought) ChooseDyingSuits(
        GameSnapshot view, IReadOnlyList<PromptChoice> choices, int victimSeat, bool recipientChoice,
        IReadOnlyDictionary<int, Suit> authorizedOwnSuits, IReadOnlyList<Suit> selectedOwnSuits,
        int requiredDiscardCount, int thoughtSequence)
    {
        var self = view.Players.Single(p => p.Seat == Seat);
        var role = self.Role ?? Role.Renegade;
        var victim = view.Players.Single(p => p.Seat == victimSeat);
        var rescueValue = _policyVersion >= 2 ? ScoreTacticalDyingResponse(view, role, victim) : ScoreDyingResponse(role, Seat, victim);
        // Cards and effective suits are supplied only for the actual discard responder's
        // own published legal cards. Recipient ranking never receives hidden identities.
        var canCompleteSuits = !recipientChoice && requiredDiscardCount == 4 && rescueValue > 0 &&
            !selectedOwnSuits.Contains(Suit.None) && selectedOwnSuits.Distinct().Count() == selectedOwnSuits.Count &&
            selectedOwnSuits.Concat(authorizedOwnSuits.Values).Where(s => s != Suit.None).Distinct().Count() >= 4;
        var ownValues = self.Hand.Concat(self.Equipment).ToDictionary(c => c.Id, c => CardCatalog.Get(c.Kind).HandKeepValue);
        var scored = choices.Select(choice =>
        {
            double score;
            if (recipientChoice)
            {
                var recipient = view.Players.Single(p => p.Seat == choice.Targets.Single());
                score = -GetHostility(view, role, recipient) * .2d + Math.Min(12, recipient.HandCount + recipient.Equipment.Count) * 2d +
                    Math.Max(0, recipient.MaxHp - recipient.Hp);
            }
            else
            {
                var id = choice.Cards.Single(); var suit = authorizedOwnSuits[id];
                score = -ownValues[id] + (canCompleteSuits && suit != Suit.None && !selectedOwnSuits.Contains(suit) ? 100d : 0d);
            }
            return (Choice: choice, Candidate: new AiCandidateScore(
                new LegalAction(LegalActionKind.SkillChoice, choice.Cards.Count == 1 ? choice.Cards[0] : null,
                    choice.Targets.Count == 1 ? choice.Targets[0] : null, choice.Description), Math.Round(score, 3),
                recipientChoice ? "按公开关系、体力、手牌数和装备数选择受益者。" : "仅从自己已授权的合法牌中尽量完成四花色，并保留高价值牌。"));
        }).ToArray();
        var selected = scored.OrderByDescending(x => x.Candidate.Score).ThenBy(x => x.Choice.Id.Value, StringComparer.Ordinal).First();
        return (selected.Choice, new(thoughtSequence, view.TurnNumber, Seat, selected.Choice.Description,
            scored.Select(x => x.Candidate).OrderByDescending(x => x.Score).ToArray(), "陈情的具名公开目标与自身弃牌评分。"));
    }

    internal (int ActionIndex, AiThoughtRecord Thought) ChooseHistoricalEndingUse(
        GameSnapshot view, IReadOnlyList<LegalAction> authorizedActions, int thoughtSequence)
    {
        var self = view.Players.Single(p => p.Seat == Seat); var role = self.Role ?? Role.Renegade;
        var actions = authorizedActions.Append(new LegalAction(LegalActionKind.EndPlay, null, null, "结束本次默识")).ToArray();
        var scored = actions.Select((action, index) =>
        {
            var (score, reason) = _policyVersion >= 2 ? ScoreTacticalAction(view, self, role, action, actions) : ScoreAction(view, self, role, action);
            if (action.CardId is { } id) score -= CardCatalog.Get(self.Hand.Single(c => c.Id == id).Kind).HandKeepValue * .2d;
            return (Index: index, Candidate: new AiCandidateScore(action, Math.Round(score, 3), reason));
        }).ToArray();
        var selected = scored.OrderByDescending(x => x.Candidate.Score).ThenBy(x => x.Index).First();
        return (selected.Index == authorizedActions.Count ? -1 : selected.Index,
            new(thoughtSequence, view.TurnNumber, Seat, selected.Candidate.Action.Description,
                scored.Select(x => x.Candidate).OrderByDescending(x => x.Score).ToArray(), "默识复用成熟用牌与公开目标评分，并计入实际手牌材料价值。"));
    }
}
