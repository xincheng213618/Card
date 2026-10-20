namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ShenwuActivities
{
    internal static int ShenwuPlayerHealthBonus(ShenwuActivityDefinition activity) =>
        (activity.PlayerTier >= (activity.Edition == 0 ? 2 : 3) ? 1 : 0) + (activity.PlayerTier >= 5 ? 1 : 0);
}
