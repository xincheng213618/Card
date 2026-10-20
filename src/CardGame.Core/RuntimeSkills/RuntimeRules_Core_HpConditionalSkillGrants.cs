namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_HpConditionalSkillGrants
{
    internal static string HpConditionalGrantId(int ownerSeat, HpConditionalSkillGrantSource source) =>
        $"hp-conditional:{ownerSeat}:{source.CapabilityGrantId.Length}:{source.CapabilityGrantId}:{source.CapabilitySkillInstanceId.Length}:{source.CapabilitySkillInstanceId}:{source.RuleId}";
}
