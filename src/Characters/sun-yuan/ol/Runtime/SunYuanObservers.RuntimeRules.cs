namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_sun_yuan_ol_SunYuanObservers
{
    internal static bool SunYuanMovementStage(SunYuanStage s)=>s is SunYuanStage.ObtainChildren or SunYuanStage.DiscardChildren or SunYuanStage.ReclaimChildren or SunYuanStage.NativeChildren;
}
