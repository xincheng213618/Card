namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhao_zhong_ol_ZhaoZhongLedger
{
    internal static CardConversionSource ZhaoZhongSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
}
