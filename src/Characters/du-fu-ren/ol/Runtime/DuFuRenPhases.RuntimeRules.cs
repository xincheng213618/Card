namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_du_fu_ren_ol_DuFuRenPhases
{
    internal static SkillRuleQuery? DuFuRenPhaseQuery(ActualDiscardRecoveryPhaseKind kind) => kind switch
    { ActualDiscardRecoveryPhaseKind.Draw => SkillRuleQuery.DrawCount, ActualDiscardRecoveryPhaseKind.Play => SkillRuleQuery.SlashLimit,
        ActualDiscardRecoveryPhaseKind.Discard => SkillRuleQuery.HandLimit, _ => null };
}
