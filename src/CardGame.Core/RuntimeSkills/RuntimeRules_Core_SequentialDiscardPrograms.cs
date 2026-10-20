using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_SequentialDiscardPrograms
{
    internal static bool IsSequentialDiscardOp(SkillProgramEffectOp op) => op is
        SkillProgramEffectOp.ChooseCategoryOrSequentialDiscard or SkillProgramEffectOp.EscalatingDiscardOrDamageFromSelected;
}
