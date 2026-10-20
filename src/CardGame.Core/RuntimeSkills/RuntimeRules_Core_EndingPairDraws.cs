namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_EndingPairDraws
{
    internal static string EndingPairRoundUsage(string state) => "ending-hand-divergence:" + state;
}
