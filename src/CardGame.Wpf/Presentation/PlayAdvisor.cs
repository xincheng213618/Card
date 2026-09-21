using CardGame.Core;
using System.Text.RegularExpressions;

namespace CardGame.Wpf.Presentation;

public sealed record PlayAdvice(long Revision, string Action, string Selection, string Reason);

/// <summary>A separate, reproducible decision from player-visible inputs; never advances an engine or its AI.</summary>
public static class PlayAdvisor
{
    public static PlayAdvice? Recommend(
        GameSnapshot view,
        IReadOnlyList<LegalAction> actions,
        IEnumerable<int> recastCardIds,
        bool usesFormalRende = false)
    {
        if (view.Status != EngineStatus.AwaitingHumanPlay || view.PendingDecision is not { Kind: DecisionKind.PlayCard } prompt ||
            prompt.PlayerSeat != view.HumanSeat || actions.Count == 0) return null;
        // Refuse trusted/debug snapshots: this boundary accepts only a player's private view.
        if (view.Seed is not null || view.Players.Any(player => player.Seat != view.HumanSeat && player.Hand.Count != 0))
            throw new ArgumentException("Advice requires a player-filtered snapshot.", nameof(view));
        var brain = new SimpleAiBrain(
            view.HumanSeat,
            721019,
            policyVersion: 2,
            usesFormalRende: usesFormalRende);
        foreach (var id in recastCardIds) brain.ObserveRecast(view.TurnNumber, id);
        var (action, thought) = brain.ChoosePlay(view, actions, 1);
        var self = view.Players.Single(player => player.Seat == view.HumanSeat);
        var cards = action.Kind == LegalActionKind.UseSkill ? brain.ChooseActiveSkillCards(view, action)
            : action.CardId is { } cardId ? new[] { cardId } : [];
        var targets = action.Kind == LegalActionKind.UseSkill ? brain.ChooseActiveSkillTargets(view, action) : action.TargetSeats;
        var parts = new List<string>();
        if (cards.Count > 0)
            parts.Add("选牌：" + string.Join("、", cards.Select(id =>
            {
                var card = self.Hand.Single(card => card.Id == id);
                var suit = card.Suit switch { Suit.Spade => "♠", Suit.Heart => "♥", Suit.Club => "♣", _ => "♦" };
                return $"【{card.DisplayName}】{suit}{card.RankText}";
            })));
        if (targets.Count > 0)
            parts.Add("目标：" + string.Join("、", targets.Select(seat =>
                $"{seat + 1:00} {view.Players.Single(player => player.Seat == seat).GeneralName}")));
        if (action.Kind == LegalActionKind.Recast) parts.Add("不选目标，返回牌桌后点击「重铸换牌」。");
        else if (action.Kind == LegalActionKind.RevealGeneral) parts.Add("返回牌桌后点击对应的明置武将按钮，启用该武将技能并公开势力。");
        else if (action.Kind == LegalActionKind.EndPlay) parts.Add("返回牌桌后点击「结束出牌」；仍可自行选择其他合法行动。");
        else if (action.Kind == LegalActionKind.UseSkill) parts.Add("返回牌桌后发动对应技能，再按提示选牌和目标。");
        else if (action.PlayedCardKind is { } effective && action.CardId is { } physicalId && self.Hand.Single(card => card.Id == physicalId).Kind != effective)
            parts.Add($"通过转化技能当作【{CardCatalog.Get(effective).DisplayName}】使用。");
        var reason = thought.Candidates.Single(candidate => candidate.Action == action).Reason.Trim();
        if (reason.StartsWith("卡牌策略值", StringComparison.Ordinal) && action.TargetSeat is { } targetSeat)
        {
            var target = view.Players.Single(player => player.Seat == targetSeat);
            reason = $"该目标当前可合法选择，体力 {target.Hp}/{target.MaxHp}、手牌 {target.HandCount} 张；建议结合公开阵营判断是否施压。";
        }
        reason = Regex.Replace(reason, @"；(?:[^；，。]*策略值|(?:综合)?支持收益) [-\d.]+，[^。]*", string.Empty);
        reason = Regex.Replace(reason, @"(净收益|回复收益) [-\d.]+", "$1");
        reason = Regex.Replace(reason, @"，扣除 [-\d.]+ 分", string.Empty);
        return new(view.Revision, action.Description, string.Join("\n", parts), reason);
    }
}
