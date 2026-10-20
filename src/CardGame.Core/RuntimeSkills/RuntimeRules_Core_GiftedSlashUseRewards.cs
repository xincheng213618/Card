namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_GiftedSlashUseRewards
{
    internal static string GiftedSlashGiftReason(string skill) => $"skill-program.{skill}.{SkillProgramEffectOp.GiveBoundHandAsSlashWithUseReward}.gift";

    internal static string GiftedSlashDrawReason(string skill) => $"skill-program.{skill}.{SkillProgramEffectOp.RewardGiftedSlashUse}.draw";
}
