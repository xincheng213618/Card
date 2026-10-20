namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_cao_yu_ol_CaoYuObservers
{
    internal static bool CaoYuChildren(CaoYuStage stage)=>stage is CaoYuStage.GongjieGiftChildren or CaoYuStage.GongjieDrawChildren or CaoYuStage.XiangxuDrawChildren or CaoYuStage.XiangxuDiscardChildren or CaoYuStage.XiangxuRecoveryChildren or CaoYuStage.XiangzuGiftChildren or CaoYuStage.XiangzuRecoveryChildren;
}
