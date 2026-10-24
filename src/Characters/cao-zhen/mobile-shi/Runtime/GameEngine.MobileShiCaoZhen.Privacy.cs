namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsShiCaoZPrivateProcessing(int id) => HasShiCaoZ && _cardZones.GetLocation(id) == CardLocation.Processing &&
        _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.ShiCaoZhen is { Operation: SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengPlace } r && r.Pool.Contains(id));
    [ProgramViewContributor(ProgramViewContribution.InvariantProcessing, 49900)]
    [ProgramViewContributor(ProgramViewContribution.PublicProcessing, 49900)]
    private IReadOnlyList<Card> WithoutShiCaoZPrivateProcessing(IReadOnlyList<Card> cards) => !HasShiCaoZ ? cards : Array.AsReadOnly(cards.Where(c => !IsShiCaoZPrivateProcessing(c.Id)).ToArray());
    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 49900)]
    private IReadOnlyList<CardSnapshot> ShiCaoZPrivateCards(int viewer) => !HasShiCaoZ ? [] : Array.AsReadOnly(
        _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.OwnerSeat == viewer && f.ShiCaoZhen is { Operation: SkillProgramEffectOp.ResolveMobileShiCaoZhenSifengPlace }).SelectMany(f => f.ShiCaoZhen!.Pool).Where(IsShiCaoZPrivateProcessing)
            .Concat(LiveShiCaoZMaterials().Where(m => m.Source.OwnerSeat == viewer).Select(m => m.CardId)).Distinct().Select(id => ToSnapshot(GetAdvancedCard(id))).ToArray());
    [ProgramViewContributor(ProgramViewContribution.NamedAmbushPiles, 49900)]
    private IReadOnlyList<NamedAmbushPileSnapshot>? IncludeShiCaoZPileViews(IReadOnlyList<NamedAmbushPileSnapshot>? ordinary, int holder, int viewer, bool revealAll)
    {
        if (!HasShiCaoZ) return ordinary;
        var piles = LiveShiCaoZMaterials().Where(m => m.Holder == holder).GroupBy(m => m.Source.OwnerSeat).Select(group => new NamedAmbushPileSnapshot(ShiCaoZSifeng, "伺锋", group.Count(),
            revealAll || group.Key == viewer ? Array.AsReadOnly(group.OrderBy(m => m.PlacementId).Select(m => new NamedAmbushPrivateEntrySnapshot(ToSnapshot(GetAdvancedCard(m.CardId)), m.Kind)).ToArray()) : null)).ToArray();
        return piles.Length == 0 ? ordinary : Array.AsReadOnly((ordinary ?? []).Concat(piles).ToArray());
    }
}
