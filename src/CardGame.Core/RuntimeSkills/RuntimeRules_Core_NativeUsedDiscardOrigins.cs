namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_NativeUsedDiscardOrigins
{
    internal static bool IsNativeUseFinishReason(CardMoveReason reason) =>
        reason == CardMoveReasons.UseFinished || reason == CardMoveReasons.IronChainFinished ||
        reason == CardMoveReasons.FireAttackFinished || reason == CardMoveReasons.DelayedCardFinish;
}
