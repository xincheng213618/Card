namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_ma_cheng_ol_ChenglieLedger
{
    internal static CardConversionSource ChenglieSource(ProgramSkillFrame f)=>new(f.SkillId,CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
}
