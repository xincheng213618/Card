namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MatchesLastSourceZoneConversion(
        CharacterState owner, Card card, SkillProgramViewAs rule)
    {
        if (rule.LastInSourceZone != true) return true;
        if (!owner.IsAlive || rule.InputCount != 1 || rule.SourceZones.Count != 1 || card.IsGeneralWeapon)
            return false;
        var zone = rule.SourceZones[0];
        if (zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment))
            return false;
        var source = new CardLocation(zone, owner.Seat);
        return _cardZones.GetLocation(card.Id) == source && _cardZones.Count(source) == 1;
    }

    private bool IsLastZoneJudgmentCard(CharacterState owner, Card card) =>
        _cardZones.GetLocation(card.Id) == CardLocation.Judgment(owner.Seat);

    private IReadOnlyList<Card> GetLastZoneJudgmentConversionCards(CharacterState owner, bool forResponse)
    {
        var cards = GetJudgment(owner);
        if (!owner.IsAlive || cards.Count != 1) return [];
        var card = cards[0];
        return GetProgramViewAsConversions(owner, card, CardKind.Slash, forResponse)
            .Any(source => ViewAsRule(source)?.LastInSourceZone == true)
            ? [card] : [];
    }

    private Card? FindLastZoneJudgmentConversionCard(CharacterState owner, int cardId) =>
        GetLastZoneJudgmentConversionCards(owner, forResponse: false)
            .Concat(GetLastZoneJudgmentConversionCards(owner, forResponse: true))
            .FirstOrDefault(card => card.Id == cardId);

    private bool TryGetLastZoneJudgmentConversionLocation(
        CharacterState owner, Card card, out CardLocation location)
    {
        location = CardLocation.Judgment(owner.Seat);
        return IsLastZoneJudgmentCard(owner, card) &&
               FindLastZoneJudgmentConversionCard(owner, card.Id) is not null;
    }

    private void ValidateLastZoneConversionCommit(
        CardConversionSource? source, CharacterState provider,
        IReadOnlyList<Card> materials, bool forResponse)
    {
        if (source is null || ViewAsRule(source) is not { LastInSourceZone: true } rule) return;
        if (source.OwnerSeat != provider.Seat || materials.Count != 1 ||
            !GetProgramViewAsConversions(provider, materials[0], rule.OutputKind, forResponse).Contains(source))
            throw new InvalidOperationException("The final source-region entity or its configured conversion is no longer legal at payment.");
    }
}
