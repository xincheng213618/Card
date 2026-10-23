namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    // These estimates use public HP, hand counts and equipment only. The native
    // card resolver, rather than this scorer, owns all actual random allocations.
    private bool TryScoreWenheCard(GameSnapshot view, PlayerSnapshot self, Role role,
        LegalAction action, CardDefinition profile, out (double Score, string Reason) result)
    {
        result = default;
        double EnemyWeight(PlayerSnapshot p) => Math.Clamp(GetHostility(view, role, p) / 100d, -1d, 1d);
        (double Score, string Reason) Score(double value, string reason) => (Math.Clamp(value, -150d, 150d), reason);
        switch (action.Kind)
        {
            case LegalActionKind.StarShift:
            {
                var target = view.Players.Single(p => p.Seat == action.TargetSeat);
                var sum = self.Hp + target.Hp;
                var minimum = Math.Max(1, sum - target.MaxHp);
                var maximum = Math.Min(self.MaxHp, sum - 1);
                if (minimum > maximum) { result = Score(-150, "公开体力不存在合法重新分配区间。"); return true; }
                var expected = (minimum + maximum) / 2d;
                var delta = expected - self.Hp;
                var targetDelta = sum - expected - target.Hp;
                result = Score(delta * 20d - targetDelta * EnemyWeight(target) * 20d - 2d,
                    $"斗转星移按公开体力的可行区间估计自身期望变化 {delta:0.#}，同时考虑目标敌我；不读取随机结果。");
                return true;
            }
            case LegalActionKind.HandRedistribution:
            {
                var target = view.Players.Single(p => p.Seat == action.TargetSeat);
                var ownAfterPayment = Math.Max(0, self.HandCount - (self.Hand.Any(c => c.Id == action.CardId) ? 1 : 0));
                var expected = (ownAfterPayment + target.HandCount) / 2d;
                result = Score((expected - ownAfterPayment) * 6d - (expected - target.HandCount) * EnemyWeight(target) * 6d - 2d,
                    "李代桃僵仅按付款后的自身牌数与对方公开手牌数估计随机分配收益，不读取他人暗牌。");
                return true;
            }
            case LegalActionKind.BeamSwap:
            {
                var living = view.Players.Where(p => p.IsAlive).ToArray();
                var equipment = living.SelectMany(p => p.Equipment.Select(c => (Owner: p.Seat, Card: c))).ToArray();
                var movable = equipment.Where(item => item.Card.Kind is not (CardKind.GeneralWeapon or CardKind.Xuanjian)).ToArray();
                if (movable.Length == 0) { result = Score(-40, "场上没有可重新分配的普通装备，保留偷梁换柱。"); return true; }
                var value = 0d;
                foreach (var group in movable.GroupBy(item => item.Card.OccupiedEquipmentSlot ?? EquipmentCatalog.Get(item.Card.Kind).Slot))
                {
                    int Capacity(PlayerSnapshot p) => p.IsEquipmentAreaAbolished ? 0 : Math.Max(0,
                        (p.EquipmentSlotCapacities?.GetValueOrDefault(group.Key, 1) ?? 1) - equipment.Count(item => item.Owner == p.Seat &&
                            (item.Card.Kind is CardKind.GeneralWeapon or CardKind.Xuanjian) && (item.Card.OccupiedEquipmentSlot ?? EquipmentCatalog.Get(item.Card.Kind).Slot) == group.Key));
                    var capacity = living.Sum(Capacity);
                    if (capacity <= 0) continue;
                    var total = group.Sum(item => (double)CardCatalog.Get(item.Card.Kind).AiPlayValue);
                    foreach (var p in living)
                    {
                        var expected = total * Capacity(p) / capacity;
                        var current = group.Where(item => item.Owner == p.Seat).Sum(item => (double)CardCatalog.Get(item.Card.Kind).AiPlayValue);
                        value += (expected - current) * (p.Seat == self.Seat ? 1d : -EnemyWeight(p));
                    }
                }
                result = Score(value * .6d - 2d, "偷梁换柱按公开装备价值、合法栏位容量和敌我估计收益；保留不可转移的武将武器，不预测随机落点。");
                return true;
            }
            case LegalActionKind.WenheChaos:
            {
                var pressure = view.Players.Where(p => p.IsAlive && p.Seat != self.Seat)
                    .Sum(p => EnemyWeight(p) * (10d + (p.Hp <= 2 ? 10d : 0d) + (p.HandCount == 0 ? 6d : 0d)));
                var ownRisk = self.Hp <= 1 ? 50d : self.Hp == 2 ? 24d : 8d;
                result = Score(profile.AiPlayValue * .25d + pressure - ownRisk,
                    "文和乱武按其他存活者的公开敌我、体力和牌数评估全场压力，并预留自己被反击的风险；不读取他人杀或暗牌。");
                return true;
            }
            default: return false;
        }
    }
}
