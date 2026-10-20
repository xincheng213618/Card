namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramDistanceRule(ProgramDistanceStage.AttackRangeContributions, 30)]
    private void AddMouTianYuRangeBonusAttackRangeContribution(CharacterState player, List<RuleQueryContribution> contributions) =>
        ((MouYangshiRangeRuntime)GetRuntimeModuleInstance(typeof(MouYangshiRangeRuntime))).AddRangeContribution(player.Seat, contributions);

}
