using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int MouTianYuRangeBonus(CharacterState p) => HasMouTianYu && p.IsAlive
        ? ProgramEventHistory<MouTianYuRangeIncreasedEvent>().Count(e => e.OwnerSeat == p.Seat) : 0;

    [ProgramDistanceRule(ProgramDistanceStage.AttackRangeContributions, 30)]
    private void AddMouTianYuRangeBonusAttackRangeContribution(CharacterState player, List<RuleQueryContribution> contributions) =>
        AddFiniteContribution(contributions, $"issued:{player.Seat}:mou-tian-yu:yangshi:attack-range", MouTianYuRangeBonus(player));

}
