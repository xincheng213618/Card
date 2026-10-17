namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    private bool TryScoreWumiaoFlood(GameSnapshot view, PlayerSnapshot self, Role role,
        LegalAction action, out (double Score, string Reason) score)
    {
        score = default;
        if (action.Kind != LegalActionKind.WumiaoFlood) return false;
        var value = -4d;
        for (var index = 0; index < action.TargetSeats.Count; index++)
        {
            var target = view.Players.Single(player => player.Seat == action.TargetSeats[index]);
            var hostility = GetHostility(view, role, target);
            var direction = target.Seat == self.Seat ? -1d : Math.Clamp(hostility / 35d, -1d, 1d);
            var damage = 25d + (target.Hp <= 1 ? 30d : 0d);
            var followup = index == 0 ? target.HandCount + target.Equipment.Count > 0 ? 10d : 0d : -9d;
            value += direction * (damage + followup);
        }
        score = (value, "按已选目标顺序评估雷电伤害：首目标弃牌、后目标摸牌；只读取公开体力、手牌数量和装备，不读取暗牌。");
        return true;
    }
}
