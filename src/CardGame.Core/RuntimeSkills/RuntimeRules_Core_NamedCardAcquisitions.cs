using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_NamedCardAcquisitions
{
    internal static string NamedAcquisitionReason(ProgramSkillFrame f) =>
        $"skill-program.{f.SkillId}.{SkillProgramEffectOp.LoseSkillsAndObtainNamedCard}";
}
