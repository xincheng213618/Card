namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_cui_yan_ol_CuiYanLedger
{
    // This is a skill-local semantic projection. Older consumers retain the
    // same-suit-multiple-material CaptureUsedCardSuit contract unchanged.
    internal static Suit CuiYanTrueUseSuit(IReadOnlyList<int> ids,Suit? nativeSuit)=>ids.Count==1?nativeSuit??Suit.None:Suit.None;
}
