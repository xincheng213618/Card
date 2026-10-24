namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<ProgramPublicRuleStateSnapshot>? GetSkillPublicRuleStates(CharacterState owner, string skillId)
    {
        var existing = GetHandComparisonPublicRuleStates(owner, skillId);
        List<ProgramPublicRuleStateSnapshot>? states = null;
        foreach (var instance in GetSkillBindingShard(owner).ProgramInstances)
        {
            if (instance.SkillId == skillId)
            foreach (var policy in instance.Program.ViewAs.Where(rule => rule.RoundOnceCardlessBasic is not null)
                .Select(rule => rule.RoundOnceCardlessBasic!).DistinctBy(policy => policy.UsageGroup))
                (states ??= []).Add(new(instance.SkillInstanceId, policy.UsageGroup, ProgramPublicRuleStateKind.ActivationLimit,
                    policy.UsesPerRound, RoundOnceCardlessBasicUsed(owner.Seat, skillId, policy.UsageGroup) ? 1 : 0, SkillUsageScope.Round));
            if (instance.SkillId == skillId && MouLiuChengComposition.IsProgram(instance.Program) &&
                CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } liuChengPhase &&
                IsRecordedActualDiscardRecoveryPhase(liuChengPhase))
                (states ??= []).Add(new(instance.SkillInstanceId, "after-designation", ProgramPublicRuleStateKind.ActivationLimit,
                    2, MouLiuChengPhaseUses(owner.Seat, skillId, liuChengPhase), SkillUsageScope.Phase));
            if (instance.SkillId == skillId)
            foreach (var activation in instance.Program.Activations)
            {
                var plan = ProgramInstructionResolver.Default.Resolve(instance.Program, ProgramInstructionSourceKind.Activation, activation.Id);
                var phaseLimit = ProgramSelections.PhaseLimit(new(owner.Seat, instance.Program, plan));
                AddLimit(phaseLimit, SkillUsageScope.Phase, PhaseUsedProjections.Apply(owner.Seat, skillId,
                    _programPhaseUses.GetValueOrDefault((owner.Seat, skillId, activation.UsageGroup))));
                AddLimit(activation.UsesPerTurn, SkillUsageScope.Turn, _programUses.GetValueOrDefault((owner.Seat, skillId, activation.UsageGroup)));
                AddLimit(activation.UsesPerGame, SkillUsageScope.Game, _skillRuntimeState.GetUsage(owner.Seat, skillId, activation.UsageGroup, SkillUsageScope.Game));

                void AddLimit(int? limit, SkillUsageScope scope, int used)
                {
                    if (limit is not > 0 || (existing ?? []).Any(state => state.Kind == ProgramPublicRuleStateKind.ActivationLimit &&
                        state.SkillInstanceId == instance.SkillInstanceId && state.StateId == activation.Id && state.Scope == scope)) return;
                    (states ??= []).Add(new(instance.SkillInstanceId, activation.Id, ProgramPublicRuleStateKind.ActivationLimit, limit.Value, used, scope));
                }
            }
            foreach (var effect in instance.Program.Triggers.SelectMany(trigger => trigger.Effects))
                if (effect.Op == SkillProgramEffectOp.AwakenAfterGameActivations && effect.SourceBind == skillId)
                    (states ??= []).Add(new(instance.SkillInstanceId, effect.StateId!, ProgramPublicRuleStateKind.GameActivationProgress,
                        effect.MinimumValue, GameProgramActivationCount(owner.Seat, skillId, effect.StateId!), SkillUsageScope.Game));
        }
        var weiYanStates = MobileShiWeiYanPublicStates(owner, skillId);
        if (weiYanStates.Count > 0) (states ??= []).AddRange(weiYanStates);
        var chenQunStates = MobileShiChenQunPublicStates(owner, skillId);
        if (chenQunStates.Count > 0) (states ??= []).AddRange(chenQunStates);
        var wangChangStates = MobileShiWangChangPublicStates(owner, skillId);
        if (wangChangStates.Count > 0) (states ??= []).AddRange(wangChangStates);
        var tianFengStates = MobileShiTianFengPublicStates(owner, skillId);
        if (tianFengStates.Count > 0) (states ??= []).AddRange(tianFengStates);
        var chenZhiStates = MobileShiChenZhiPublicStates(owner, skillId);
        if (chenZhiStates.Count > 0) (states ??= []).AddRange(chenZhiStates);
        var dongZhaoStates = MobileShiDongZhaoPublicStates(owner, skillId);
        if (dongZhaoStates.Count > 0) (states ??= []).AddRange(dongZhaoStates);
        var chenDaoStates = MobileShiChenDaoPublicStates(owner, skillId);
        if (chenDaoStates.Count > 0) (states ??= []).AddRange(chenDaoStates);
        var huangZuStates = MobileShiHuangZuPublicStates(owner, skillId);
        if (huangZuStates.Count > 0) (states ??= []).AddRange(huangZuStates);
        var guoYuanStates = MobileShiGuoYuanPublicStates(owner, skillId);
        if (guoYuanStates.Count > 0) (states ??= []).AddRange(guoYuanStates);
        var xinXianYingStates = ShiXinXianYingPublicStates(owner, skillId);
        if (xinXianYingStates.Count > 0) (states ??= []).AddRange(xinXianYingStates);
        var heQiStates = MobileShiHeQiPublicStates(owner, skillId);
        if (heQiStates.Count > 0) (states ??= []).AddRange(heQiStates);
        var luSuStates = MobileShiLuSuPublicStates(owner, skillId);
        if (luSuStates.Count > 0) (states ??= []).AddRange(luSuStates);
        var luYuShengStates = MobileShiLuYuShengPublicStates(owner, skillId);
        if (luYuShengStates.Count > 0) (states ??= []).AddRange(luYuShengStates);
        var sunChenStates = MobileShiSunChenPublicStates(owner, skillId);
        if (sunChenStates.Count > 0) (states ??= []).AddRange(sunChenStates);
        var sunJunStates = MobileShiSunJunPublicStates(owner, skillId);
        if (sunJunStates.Count > 0)
        {
            states?.RemoveAll(state => sunJunStates.Any(actual => actual.SkillInstanceId == state.SkillInstanceId &&
                actual.StateId == state.StateId && actual.Kind == state.Kind && actual.Scope == state.Scope));
            (states ??= []).AddRange(sunJunStates);
        }
        var xiaoQiaoStates = MobileShiXiaoQiaoPublicStates(owner, skillId);
        if (xiaoQiaoStates.Count > 0) (states ??= []).AddRange(xiaoQiaoStates);
        var zhangYanStates = MobileShiZhangYanPublicStates(owner, skillId);
        if (zhangYanStates.Count > 0) (states ??= []).AddRange(zhangYanStates);
        var dengAiStates = MobileShiDengAiPublicStates(owner, skillId);
        if (dengAiStates.Count > 0) (states ??= []).AddRange(dengAiStates);
        var taiShiCiStates = ShiTaiShiCiPublicStates(owner, skillId);
        if (taiShiCiStates.Count > 0) (states ??= []).AddRange(taiShiCiStates);
        return states is null ? existing : Array.AsReadOnly((existing ?? []).Concat(states).ToArray());
    }
}
