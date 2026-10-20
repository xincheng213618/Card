using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_DyingSuitsPeachPrograms
{
    internal static CardConversionSource DyingSuitsSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);

    internal static string DyingSuitsDrawReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.{SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach}.draw";

    internal static string DyingSuitsDiscardReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.{SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach}.discard";
}
