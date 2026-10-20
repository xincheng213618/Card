namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<ProgramPublicRuleStateSnapshot>? GetSkillPublicRuleStates(CharacterState owner, string skillId)
    {
        var existing = GetHandComparisonPublicRuleStates(owner, skillId);
        List<ProgramPublicRuleStateSnapshot>? states = null;
        foreach (var instance in GetSkillBindingShard(owner).ProgramInstances)
        {
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
                AddLimit(phaseLimit, SkillUsageScope.Phase, MouLuZhiProjectedUses(owner.Seat, skillId, MouXiahouDunProjectedUses(owner.Seat, skillId, MouHanDangProjectedUses(owner.Seat, skillId, MouXiahouShiProjectedPhaseUses(owner.Seat, skillId, MouChenGongProjectedPhaseUses(owner.Seat, skillId, MouXuHuangProjectedPhaseUses(owner.Seat, skillId, MouLuMengProjectedPhaseUses(owner.Seat, skillId, _programPhaseUses.GetValueOrDefault((owner.Seat, skillId, activation.UsageGroup))))))))));
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
        return states is null ? existing : Array.AsReadOnly((existing ?? []).Concat(states).ToArray());
    }
}
