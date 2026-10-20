namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_lv_kuang_lv_xiang_ol_LvKuangLvXiangNativeUses
{
    internal static bool SameQigongNativeReceipt(QigongNativeReceipt? left,QigongNativeReceipt? right)
    {
     if(left is null||right is null)return left is null&&right is null;
     return CardGame.Core.RuntimeSkills.RuntimeRules_Core_OverflowTargetCancellationObservers.SameOverflowAction(left.OriginalAction,right.OriginalAction)&&
      (left.OriginalAction.DesignatedTargetSeats is null)==(right.OriginalAction.DesignatedTargetSeats is null)&&
      (left.OriginalAction.DesignatedTargetSeats is null||left.OriginalAction.DesignatedTargetSeats.SequenceEqual(right.OriginalAction.DesignatedTargetSeats!))&&
      (left with{OriginalAction=right.OriginalAction})==right;
    }
}
