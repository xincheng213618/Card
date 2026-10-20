namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_ya_dan_ol_YaDanLedger
{
    internal static bool IsYaDanOperation(SkillProgramEffectOp op)=>op is SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards or SkillProgramEffectOp.ResolveQingyaNextActualPhaseUse;

    internal static CardConversionSource YaDanSource(ProgramSkillFrame f)=>new(f.SkillId,CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
}
