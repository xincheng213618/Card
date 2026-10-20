namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_OrderedPrintedSkillLoss
{
    internal static bool IsOrderedPrintedSkillLossTrigger(SkillProgramTrigger trigger) =>
        trigger.Effects is [{ Op: SkillProgramEffectOp.LoseFirstPrintedSkillsUntilTurnEndAndDraw }];

    internal static string OrderedPrintedLossDrawReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.ordered-printed-skill-loss.draw";
}
