namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    private (double Score, string Reason) ScoreDrawFundedDistinctBasicAction(GameSnapshot view, PlayerSnapshot self, Role role, LegalAction action)
    {
        if (action.CardId != 0 || action.DrawFundedDistinctBasicUse is null || action.ConversionSource is null || action.PlayedCardKind is not { } kind)
            return (-1000, "摸牌转换缺少已发布的真实用牌动作。");
        var drawValue = view.DrawPileCount + view.DiscardPileCount > 0 ? 5d : 0d;
        var profile = CardCatalog.Get(kind);
        if (kind == CardKind.Peach) return (profile.AiPlayValue * (self.MaxHp - self.Hp) + (self.Hp <= 1 ? 50 : 0) + drawValue, "按公开受伤程度评估真实桃和一次摸牌。");
        if (kind == CardKind.Alcohol)
            return (profile.AiPlayValue + drawValue + (self.Hand.Any(c => IsSlashCard(c.Kind)) ? 12 : -8), "按自己的可见杀和公开状态评估酒，不读取他人暗牌或牌堆顺序。");
        if (action.Kind != LegalActionKind.Slash || action.TargetSeats.Count == 0) return (-1000, "基本牌转换缺少成熟合法方向。");
        var pressure = action.TargetSeats.Select(seat => view.Players.Single(p => p.Seat == seat)).Sum(target =>
            GetHostility(view, role, target) + (target.Hp <= 1 ? 28 : 0) - (target.Equipment.Any(c => c.Kind == CardKind.SilverLion) && self.HasAlcoholEffect ? 36 : 0));
        return (profile.AiPlayValue + drawValue + pressure, "按公开目标、体力、防具及本回合独立牌名额度评分。");
    }
}
