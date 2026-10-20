using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_OutsidePhaseDrawDiscard
{
    internal static bool IsOutsidePhaseOperation(SkillProgramEffectOp op) => OutsidePhaseDrawDiscardComposition.IsOperation(op);

    internal static string OutsidePhaseMovementReason(ProgramSkillFrame f, bool draw) =>
        $"skill-program.{f.SkillId}.outside-phase-{(draw ? "draw" : "discard")}";
}
