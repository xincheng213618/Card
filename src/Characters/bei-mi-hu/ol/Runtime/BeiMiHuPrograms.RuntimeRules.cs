using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_bei_mi_hu_ol_BeiMiHuPrograms
{
    internal static bool IsBeiMiHuOp(SkillProgramEffectOp op)=>(int)op is >=23200 and <=23205;

    internal static SkillProgramEffectOp? BeiMiHuOperation(SkillProgramTrigger t)=>t.Effects.Count==1 && CardGame.Core.RuntimeSkills.RuntimeRules_bei_mi_hu_ol_BeiMiHuPrograms.IsBeiMiHuOp(t.Effects[0].Op)?t.Effects[0].Op:null;
}
