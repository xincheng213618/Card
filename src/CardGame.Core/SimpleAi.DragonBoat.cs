namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    private bool TryScoreDragonBoatCard(GameSnapshot view, PlayerSnapshot self, Role role,
        LegalAction action, out (double Score, string Reason) result)
    {
        result = default;
        if (action.Kind is not (LegalActionKind.Zong or LegalActionKind.RealgarWine or LegalActionKind.SameBoat or
            LegalActionKind.StriveUpstream or LegalActionKind.AgainstCurrent)) return false;
        double Bounded(double value) => Math.Clamp(value, -150d, 150d);
        bool Ally(PlayerSnapshot p) => p.TeamId is { } team && team == self.TeamId;
        var rows = view.DragonBoatStandings ?? [];
        var score = rows.FirstOrDefault(row => row.TeamId == self.TeamId)?.Treasures ?? 0;
        var most = rows.Count == 0 || score == rows.Max(row => row.Treasures);
        var least = rows.Count == 0 || score == rows.Min(row => row.Treasures);
        if (action.Kind == LegalActionKind.RealgarWine)
        {
            var slashes = self.Hand.Count(card => card.Id != action.CardId && (IsSlashCard(card.IdentityKind ?? card.Kind) ||
                self.Skills?.Any(skill => CanViewAs(skill, card, CardKind.Slash, forPlay: true)) == true));
            result = (slashes == 0 ? -10 : 18 + Math.Min(2, slashes) * 10,
                "雄黄酒按自己可见且付款后仍持有的杀评估下一次伤害加值，不推测其他角色暗牌。");
        }
        else if (action.Kind == LegalActionKind.Zong)
        {
            var target = view.Players.Single(p => p.Seat == action.TargetSeat);
            result = (Bounded((Ally(target) ? 1 : -1) * (36 + (target.Hp <= 1 ? 45 : target.Hp == 2 ? 18 : 0))),
                "粽仅治疗已发布的受伤友方目标，按公开体力评估急迫性；不把粽当作救援牌。");
        }
        else if (action.Kind == LegalActionKind.SameBoat)
            result = (Bounded(view.Players.Count(p => p.IsAlive && Ally(p)) * (most ? 2 : 1) * 12 - 2),
                "同舟共济按公开宝数并列最多条件及己队存活人数评估整队摸牌。");
        else if (action.Kind == LegalActionKind.StriveUpstream)
        {
            var targets = action.TargetSeats.Count == 0 ? view.Players.Where(p => p.IsAlive).ToArray() :
                action.TargetSeats.Select(seat => view.Players.Single(p => p.Seat == seat)).ToArray();
            result = (Bounded(targets.Sum(p => (p.Seat == self.Seat ? Math.Min(5, score) : 1) * (Ally(p) ? 8d : -5d)) - 2),
                "力争上游按已发布全体目标、己队当前宝数和公开队伍计算收益，接受使用者摸0张的规则。");
        }
        else
        {
            var target = view.Players.Single(p => p.Seat == action.TargetSeat);
            var after = Math.Min(12, score + 1);
            var afterMost = rows.Count == 0 || after >= rows.Where(row => row.TeamId != self.TeamId).Select(row => row.Treasures).DefaultIfEmpty(0).Max();
            var afterLeast = rows.Count == 0 || after <= rows.Where(row => row.TeamId != self.TeamId).Select(row => row.Treasures).DefaultIfEmpty(0).Min();
            result = (Bounded((Ally(target) ? -70 : 40) + (target.Hp <= 1 ? 6 : 0) + (score < 12 ? 15 : 0) -
                (afterMost ? 8 : 0) + (afterLeast ? 6 : 0)),
                "逆水行舟按公开敌我及伤害后预计宝数估计收益，并计入领先弃牌/落后摸牌；实际结算由原生伤害和实时排名决定。");
        }
        return true;
    }
}
