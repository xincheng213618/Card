namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HengyeSkillId = "mou:hengye";
    private const string HengyeGrowthUsageId = "growth";

    private int GetHengyeGrowth(CharacterState player) =>
        UsesFormalMouLuMeng && HasRuntimeSkill(player, HengyeSkillId)
            ? _skillRuntimeState.GetUsage(
                player.Seat,
                HengyeSkillId,
                HengyeGrowthUsageId,
                SkillUsageScope.Game)
            : 0;

    private void ApplyHengyeGrowth(
        long damageFrameId,
        CharacterState source,
        CharacterState target,
        int amount)
    {
        if (!UsesFormalMouLuMeng || amount <= 0 || !source.IsAlive ||
            !HasRuntimeSkill(source, HengyeSkillId))
            return;

        var previous = GetHengyeGrowth(source);
        if (!_skillRuntimeState.TryConsumeUsage(
                source.Seat,
                HengyeSkillId,
                HengyeGrowthUsageId,
                SkillUsageScope.Game,
                limit: 3))
            return;

        var current = previous + 1;
        AdvanceEventRulesAndQueueFact(new SkillUsageConsumedEvent(
            source.Seat,
            HengyeSkillId,
            HengyeGrowthUsageId,
            SkillUsageScope.Game,
            current));
        AdvanceEventRulesAndQueueFact(new HengyeGrowthChangedEvent(
            damageFrameId,
            source.Seat,
            previous,
            current));
        AddLog(
            "SkillTriggered",
            $"{source.Name} 的【横野】成长至 {current}/3（摸牌数、出杀次数、攻击范围、手牌上限同步增加）。",
            source.Seat,
            target.Seat);
    }

    private void ResolveHengyeTurnStart(CharacterState player)
    {
        if (GetHengyeGrowth(player) < 3 || player.Hp >= player.MaxHp) return;

        var recoveryFrameId = BeginRecovery(0, player.Seat, player.Seat, 1);
        try
        {
            player.Hp++;
            AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(player.Seat, player.Seat, 1, player.Hp));
            AddLog("SkillTriggered", $"{player.Name} 的【横野】已成长至 3，本回合开始时回复 1 点体力。", player.Seat);
        }
        finally
        {
            PopResolutionFrame(recoveryFrameId, ResolutionFrameKind.Recovery);
        }
    }

    private void ResetHengyeAfterKill(CharacterState? killer)
    {
        if (!UsesFormalMouLuMeng || killer is not { IsAlive: true } ||
            !HasRuntimeSkill(killer, HengyeSkillId))
            return;

        var previous = GetHengyeGrowth(killer);
        _skillRuntimeState.ResetSkill(killer.Seat, HengyeSkillId);
        AdvanceEventRulesAndQueueFact(new SkillResetEvent(killer.Seat, HengyeSkillId, previous));
        AddLog("SkillTriggered", $"{killer.Name} 杀死一名角色，重置【横野】至游戏开始时状态。", killer.Seat);
    }

    [ProgramDistanceRule(ProgramDistanceStage.AttackRangeContributions, 50)]
    private void AddGetHengyeGrowthAttackRangeContribution(CharacterState player, List<RuleQueryContribution> contributions) =>
        AddFiniteContribution(contributions, $"state:{player.Seat}:hengye:growth", GetHengyeGrowth(player));

}
