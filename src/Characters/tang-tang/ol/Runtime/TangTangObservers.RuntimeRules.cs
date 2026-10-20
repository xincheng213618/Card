namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_tang_tang_ol_TangTangObservers
{
    internal static bool TangTangMoving(TangTangStage stage) => stage is TangTangStage.TakeChildren or TangTangStage.GiftChildren or TangTangStage.DrawChildren;
}
