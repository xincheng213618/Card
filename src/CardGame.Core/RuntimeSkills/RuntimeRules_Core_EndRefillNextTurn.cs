using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_EndRefillNextTurn
{
    internal static string EndRefillReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.end-refill-next-turn.draw";

    internal static bool SameEndRefillReceipt(EndRefillNextTurnReceipt a, EndRefillNextTurnReceipt b) =>
        a.WindowFrameId == b.WindowFrameId && a.Source == b.Source && a.GameplayHash == b.GameplayHash &&
        a.ActualTurnNumber == b.ActualTurnNumber && a.ActualTurnOwnerSeat == b.ActualTurnOwnerSeat && a.Stage == b.Stage &&
        a.TargetSeat == b.TargetSeat && a.FrozenHandCount == b.FrozenHandCount && a.FrozenMaxHp == b.FrozenMaxHp &&
        a.DrawRequested == b.DrawRequested && a.DrawActual == b.DrawActual && a.MovementBefore == b.MovementBefore &&
        a.MovementAfter == b.MovementAfter && a.Targets.SequenceEqual(b.Targets);
}
