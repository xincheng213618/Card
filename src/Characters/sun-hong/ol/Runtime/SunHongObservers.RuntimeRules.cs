namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_sun_hong_ol_SunHongObservers
{
    internal static bool XianbiChildren(XianbiStage stage) => stage is XianbiStage.DiscardChildren or XianbiStage.DrawChildren or XianbiStage.ObtainChildren;
}
