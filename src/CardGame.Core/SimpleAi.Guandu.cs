namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    private bool TryScoreGuanduCard(GameSnapshot view, PlayerSnapshot self, Role role, LegalAction action,
        out (double Score, string Reason) result)
    {
        result = default;
        if (action.Kind is not (LegalActionKind.Intoxication or LegalActionKind.StoreGrain or LegalActionKind.Reinforcements or LegalActionKind.UnitedEffort)) return false;
        bool Ally(PlayerSnapshot p) => p.TeamId is { } id && id == self.TeamId;
        var targets = action.TargetSeats.Select(seat => view.Players.Single(p => p.Seat == seat)).ToArray();
        double score;
        if (action.Kind == LegalActionKind.StoreGrain) score = targets.Sum(p => Ally(p) ? 16d : -18d) - 2;
        else if (action.Kind == LegalActionKind.Reinforcements)
            score = targets.Sum(p => (Ally(p) ? 1 : -1) * (32d + (p.Hp <= 1 ? 48 : p.Hp == 2 ? 18 : 0)));
        else if (action.Kind == LegalActionKind.UnitedEffort)
        {
            var visibleFire = view.GuanduActivity?.Event == GuanduBattleEvent.WuchaoFire ||
                self.Hand.Any(card => card.Id != action.CardId && (card.IdentityKind ?? card.Kind) is CardKind.FireSlash or CardKind.FireAttack);
            score = targets.Sum(p => Ally(p) ? p.IsChained ? 25d : 0 : p.IsChained ? 0 : visibleFire ? 15d : 5d) - 2;
        }
        else
        {
            var slashes = self.Hand.Count(card => card.Id != action.CardId && IsSlashCard(card.IdentityKind ?? card.Kind));
            score = targets.Sum(p => Ally(p) ? -100d : (p.Hp <= 2 ? 30 : 18) + Math.Min(3, slashes) * 8) - 4;
        }
        result = (Math.Clamp(score, -150d, 150d), "官渡专牌按已公布的目标、公开队伍、体力、横置状态及自己的可见手牌估值，不读取敌方暗牌。");
        return true;
    }
}
