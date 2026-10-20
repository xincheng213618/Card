namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_liu_qi_ol_Wenji_Observers
{
    internal static string WenjiName(CardKind kind)=>CardCatalog.Get(kind).DisplayName;
}
