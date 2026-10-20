namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_hu_ban_ol_HuiyunLedger
{
    internal static CardConversionSource HuiyunSource(ProgramSkillFrame f)=>new(f.SkillId,CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
}
