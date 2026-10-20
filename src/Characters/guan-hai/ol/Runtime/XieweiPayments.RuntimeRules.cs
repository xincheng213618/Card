namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_guan_hai_ol_XieweiPayments
{
    internal static bool XieweiActionCosts(XieweiNativePayment p, CardActionContext a) => a.ConversionChain.Contains(p.Source) && a.PhysicalCards.Select(c => (c.CardId, c.CardKind, c.From)).OrderBy(c => c.CardId).SequenceEqual(p.Materials.Select(c => (c.CardId, c.Kind, c.From)).OrderBy(c => c.CardId));
}
