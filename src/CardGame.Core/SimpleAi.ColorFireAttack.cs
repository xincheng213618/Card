namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    public (int? CardId, AiThoughtRecord Thought) ChooseColorFireAttackDiscard(GameSnapshot view,
        int targetSeat, bool isRed, IReadOnlyList<int> validIds, int thoughtSequence)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var target = view.Players.Single(player => player.Seat == targetSeat);
        var hostility = GetHostility(view, self.Role ?? Role.Renegade, target);
        var candidates = validIds.Select(id => self.Hand.Concat(self.Equipment).Single(card => card.Id == id))
            .Select(card => new AiCandidateScore(new LegalAction(LegalActionKind.FireAttackDiscard,
                card.Id, targetSeat, $"弃置【{card.DisplayName}】造成火攻伤害"),
                Math.Round(54d + hostility + (target.Hp <= 1 ? 18d : 0d) - CardCatalog.Get(card.Kind).HandKeepValue * .35d +
                    (card.Kind == CardKind.SilverLion && self.Equipment.Any(e => e.Id == card.Id) && self.Hp < self.MaxHp ? 12d : 0d), 3),
                $"只使用自己可见的{(isRed ? "红色" : "黑色")}HE候选和目标公开状态。"))
            .Append(new AiCandidateScore(new LegalAction(LegalActionKind.SkipFireAttack, null, targetSeat, "放弃火攻傷害"),
                20d, "保留实际候选牌。"))
            .OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Action.CardId ?? int.MaxValue).ToArray();
        var selected = candidates[0];
        return (selected.Action.Kind == LegalActionKind.FireAttackDiscard ? selected.Action.CardId : null,
            new(thoughtSequence, view.TurnNumber, Seat, selected.Action.Description, candidates,
                $"颜色火攻：{selected.Action.Description}（{selected.Score:0.###}分）。"));
    }
}
