namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_li_yi_ol_LiYiObservers
{
    internal static bool ChanshuangChildren(ChanshuangStage s)=>s is ChanshuangStage.CostChildren or ChanshuangStage.DrawChildren or ChanshuangStage.NativeChildren;
}
