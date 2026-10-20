namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhuge_liang_wumiao_WuZhugeLiang_Copies
{
    internal static bool WuZglNativeInputReason(CardMoveReason r) => r == CardMoveReasons.Use || r == CardMoveReasons.Respond || r == CardMoveReasons.Nullification ||
        r == CardMoveReasons.IronChainUse || r == CardMoveReasons.EquipmentUse || r.Value == "conversion.declaration.pay";
}
