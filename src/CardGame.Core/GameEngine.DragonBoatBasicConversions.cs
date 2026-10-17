namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static readonly IReadOnlySet<CardKind> DragonBoatTraditionalBasicKinds = new HashSet<CardKind>
        { CardKind.Slash, CardKind.Dodge, CardKind.Peach, CardKind.Alcohol };
    private static string DragonBoatBasicViewAsFamily(SkillProgramViewAs rule)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(rule))!.AsObject();
        foreach (var name in new[] { nameof(SkillProgramViewAs.Id), nameof(SkillProgramViewAs.OutputKind),
            nameof(SkillProgramViewAs.ForPlay), nameof(SkillProgramViewAs.ForResponse) }) node.Remove(name);
        return node.ToJsonString();
    }

    [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 43000)]
    private void AddDragonBoatBasicConversionActions(List<LegalAction> actions, CharacterState actor,
        IReadOnlyList<Card> playable, int? selectedPhysicalCardId = null)
    {
        if (_modeDefinition.DragonBoatActivity is null) return;
        foreach (var card in playable.Concat(GetEquipment(actor)).DistinctBy(card => card.Id)
            .Where(card => (selectedPhysicalCardId is null || card.Id == selectedPhysicalCardId) && !IsTurnHandCardRestricted(actor, card)))
        foreach (var kind in new[] { CardKind.Zong, CardKind.RealgarWine })
        foreach (var source in GetProgramViewAsConversions(actor, card, kind, false))
            AddDragonBoatPhysicalOrConvertedActions(actions, actor, card, kind, source);
    }
    private void AddDragonBoatZeroBasicActions(List<LegalAction> actions, CharacterState actor,
        CardConversionSource source, CardKind kind) =>
        AddDragonBoatPhysicalOrConvertedActions(actions, actor, new(0, kind, Suit.None, 0), kind, source);
}
