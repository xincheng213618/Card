namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_SourceFactionPreventions
{
    internal static string SourceFactionPreventionUsage(string stateId, string faction) => $"source-faction:{stateId}:{faction}";

    internal static bool IsSourceFactionYieldTransfer(SkillProgramEffect effect) => TurnDrawDebtComposition.IsSourceFactionYieldTransfer(effect);
}
