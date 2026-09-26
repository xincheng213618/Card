namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    public (int? CardId, AiThoughtRecord Thought) ChooseJudgmentReplacement(
        GameSnapshot view, int targetSeat, string reason, IReadOnlyList<int> validCardIds,
        CardKind currentKind, Suit currentSuit, int currentRank,
        IReadOnlyList<Suit>? configuredSuccessSuits, int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var target = view.Players.Single(player => player.Seat == targetSeat);
        var support = GetTacticalSupport(view, self.Role ?? Role.Renegade, target);
        var wantsSuccess = reason == JudgmentReasons.Lightning ||
            reason.Contains("leiji", StringComparison.OrdinalIgnoreCase)
            ? support < 0 : support > 0;
        bool Succeeds(Suit suit, int rank) => configuredSuccessSuits is { Count: > 0 }
            ? configuredSuccessSuits.Contains(suit)
            : reason switch
            {
                JudgmentReasons.Lightning => suit == Suit.Spade && rank is >= 2 and <= 9,
                JudgmentReasons.Indulgence => suit == Suit.Heart,
                JudgmentReasons.SupplyShortage => suit == Suit.Club,
                _ => suit is Suit.Heart or Suit.Diamond
            };
        var currentDesired = Succeeds(currentSuit, currentRank) == wantsSuccess;
        var candidates = validCardIds.Select(id =>
        {
            var card = self.Hand.Concat(self.Equipment).Single(item => item.Id == id);
            var desired = Succeeds(card.Suit, card.Rank) == wantsSuccess;
            var score = currentDesired ? -CardCatalog.Get(card.Kind).HandKeepValue :
                desired ? 42d - CardCatalog.Get(card.Kind).HandKeepValue :
                -18d - CardCatalog.Get(card.Kind).HandKeepValue;
            return new AiCandidateScore(
                new LegalAction(LegalActionKind.SkillChoice, card.Id, targetSeat,
                    $"使用【{card.DisplayName}】替换判定牌"), score,
                desired ? "这张手牌可改变公开判定的阵营收益。" : "保留不能改善判定的手牌。");
        }).ToList();
        candidates.Add(new AiCandidateScore(
            new LegalAction(LegalActionKind.SkillChoice, null, targetSeat, "不替换判定牌"),
            currentDesired ? 30d : 1d, "保留手牌并接受当前公开判定。"));
        var selected = candidates.OrderByDescending(item => item.Score)
            .ThenBy(item => item.Action.CardId ?? int.MaxValue).First();
        var thought = new AiThoughtRecord(thoughtSequence, view.TurnNumber, Seat,
            selected.Action.Description, candidates,
            $"判定替换：{CardCatalog.Get(currentKind).DisplayName}（{currentSuit}，{reason}），选择已发布选项。");
        return (selected.Action.CardId, thought);
    }
}
