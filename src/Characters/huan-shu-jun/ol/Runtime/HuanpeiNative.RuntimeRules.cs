namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_huan_shu_jun_ol_HuanpeiNative
{
    internal static bool SameHuanpeiPayment(HuanpeiPaidOrigin a, HuanpeiPaidOrigin b) => a.ProgramFrameId == b.ProgramFrameId && a.Source == b.Source && a.GameplayHash == b.GameplayHash && a.Round == b.Round && a.ActualTurn == b.ActualTurn && a.TurnOwner == b.TurnOwner && a.Request == b.Request && a.FrozenHandCount == b.FrozenHandCount && a.FrozenHp == b.FrozenHp && a.AdjustmentCount == b.AdjustmentCount && a.SequenceBefore == b.SequenceBefore && a.SequenceAfter == b.SequenceAfter && a.Kind == b.Kind && a.Targets.SequenceEqual(b.Targets);
}
