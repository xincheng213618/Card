namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 45000)]
    private void AddGuanduBasicConversionActions(List<LegalAction> actions, CharacterState actor,
        IReadOnlyList<Card> playable, int? selectedPhysicalCardId = null)
    {
        if (!IsGuanduActivity) return;
        foreach (var card in playable.Concat(GetEquipment(actor)).DistinctBy(card => card.Id)
            .Where(card => (selectedPhysicalCardId is null || card.Id == selectedPhysicalCardId) && !IsTurnHandCardRestricted(actor, card)))
        foreach (var source in GetProgramViewAsConversions(actor, card, CardKind.Intoxication, false))
            AddGuanduPhysicalOrConvertedActions(actions, actor, card, CardKind.Intoxication, source);
    }
    private void AddGuanduZeroBasicActions(List<LegalAction> actions, CharacterState actor, CardConversionSource source) =>
        AddGuanduPhysicalOrConvertedActions(actions, actor, new(0, CardKind.Intoxication, Suit.None, 0), CardKind.Intoxication, source);
}
