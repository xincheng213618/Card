namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_DrawAdvicePrograms
{
    internal static string DrawAdviceReason(string skill, string action) =>
        $"skill-program.{skill}.{SkillProgramEffectOp.DrawTwoThenDiscardTwoIfOverMaxHp}.{action}";
}
