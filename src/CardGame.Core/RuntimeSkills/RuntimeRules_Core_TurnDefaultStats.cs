using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_TurnDefaultStats
{
    internal static IReadOnlyList<int> NewTurnDefaultSlots() => Array.AsReadOnly(new[] { 1, 2, 3, 4 });

    internal static TurnDefaultStatKind FirstTurnDefaultStat(SkillProgramTriggerWindow window) => window switch {
        SkillProgramTriggerWindow.DrawPhaseStarting => TurnDefaultStatKind.DrawCount,
        SkillProgramTriggerWindow.PlayPhaseStarting => TurnDefaultStatKind.AttackRange,
        SkillProgramTriggerWindow.DiscardPhaseStarting => TurnDefaultStatKind.HandLimit,
        _ => throw new InvalidOperationException("A default-stat allocation has no phase boundary.") };

    internal static string TurnDefaultStatName(TurnDefaultStatKind stat) => stat switch {
        TurnDefaultStatKind.DrawCount => "摸牌数", TurnDefaultStatKind.AttackRange => "攻击范围",
        TurnDefaultStatKind.SlashLimit => "杀使用次数", TurnDefaultStatKind.HandLimit => "手牌上限",
        _ => throw new InvalidOperationException("Unknown default-stat kind.") };
}
