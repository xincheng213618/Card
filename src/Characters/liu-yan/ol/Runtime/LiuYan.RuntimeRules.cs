namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_liu_yan_ol_LiuYan
{
    internal static CardConversionSource LiuYanSource(ProgramSkillFrame f)=>new(f.SkillId,CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);

    internal static bool LiuYanBasic(CardKind k)=>CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsSlashCard(k)||k is CardKind.Dodge or CardKind.Peach or CardKind.Alcohol;
}
