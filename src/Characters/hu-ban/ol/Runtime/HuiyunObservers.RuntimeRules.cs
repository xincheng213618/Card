namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_hu_ban_ol_HuiyunObservers
{
    internal static CardMoveReason HuiyunNativePaymentReason(HuiyunNativeInvoice n)=>n.Plan.Kind==LegalActionKind.Equip?CardMoveReasons.EquipmentUse:n.Plan.Kind==LegalActionKind.IronChain?CardMoveReasons.IronChainUse:CardMoveReasons.Use;
}
