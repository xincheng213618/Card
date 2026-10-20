namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_PublicPileCashOut
{
    internal static string PublicPileCashOutReason(ProgramSkillFrame frame, string step) =>
        $"skill-program.{frame.SkillId}.{frame.PublicPileCashOut!.Operation}.{step}";
}
