namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_ma_cheng_ol_ChenglieObservers
{
    internal static bool ChenglieChildren(ChenglieStage s)=>s is ChenglieStage.RevealChildren or ChenglieStage.ExchangeChildren or ChenglieStage.AssignmentChildren or ChenglieStage.GiftChildren or ChenglieStage.RecoveryChildren or ChenglieStage.CleanupChildren;
}
