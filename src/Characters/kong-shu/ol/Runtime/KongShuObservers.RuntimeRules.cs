namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_kong_shu_ol_KongShuObservers
{
    internal static bool KongShuMoving(KongShuStage stage) => stage is KongShuStage.BenefitDrawChildren or KongShuStage.BenefitGainChildren or KongShuStage.RoundDrawChildren or KongShuStage.RoundDiscardChildren;
}
