namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsCuiYanSingleUseForbidden(CharacterState actor, Card card, CardConversionSource? conversion = null) =>
        HasCuiYanCapability && IsCuiYanNextUseForbidden(actor.Seat, [card.Id],
            EffectiveSuit(actor, ApplyProgramUseAppearance(actor, card, conversion)));

    private bool IsCuiYanSlashChoiceForbidden(int actorSeat, PromptChoice choice)
    {
        if (!HasCuiYanCapability || choice.Cards is not [var id]) return false;
        var actor = _players[actorSeat];
        TryReadConversionSource(choice.Parameters, out var conversion);
        var kind = choice.Parameters.TryGetValue("effective-kind", out var name) &&
            Enum.TryParse<CardKind>(name, out var parsed) ? parsed : CardKind.Slash;
        var card = ApplyProgramUseAppearance(actor, GetAdvancedCard(id), conversion);
        var suit = WuJingLiubingUsedSuit(actorSeat, card, kind, choice.Cards,
            conversion is null ? [] : [conversion], CaptureUsedCardSuit(actorSeat, choice.Cards, card));
        return IsCuiYanNextUseForbidden(actorSeat, choice.Cards, suit);
    }

    // A provider pays the materials; the requester is the actual user. Inspect
    // a complete conversion variant, never the first card of an unresolved pool.
    private bool IsCuiYanFactionSlashUseForbidden(FactionCardRequestHandle request, CardKind kind,
        IReadOnlyList<Card> materials, CardConversionSource? conversion = null,
        IReadOnlyList<CardConversionSource>? additionalConversions = null)
    {
        if (!HasCuiYanCapability || !IsFactionSlashUse(request) || materials.Count != 1) return false;
        var actor = _players[request.OwnerSeat];
        var appearance = ApplyProgramUseAppearance(actor, materials[0], conversion);
        var ids = materials.Select(c => c.Id).ToArray();
        var sources = (conversion is null ? Array.Empty<CardConversionSource>() : [conversion])
            .Concat(additionalConversions ?? []).ToArray();
        var suit = WuJingLiubingUsedSuit(actor.Seat, appearance, kind, ids, sources,
            CaptureUsedCardSuit(actor.Seat, ids, appearance));
        return IsCuiYanNextUseForbidden(actor.Seat, ids, suit);
    }

    private bool IsCuiYanFactionSlashChoiceForbidden(FactionCardRequestHandle request, PromptChoice choice)
    {
        if (!HasCuiYanCapability || !IsFactionSlashUse(request) || choice.Cards is not [var id]) return false;
        TryReadConversionSource(choice.Parameters, out var source);
        var kind = ReadResponseCardKind(choice) ?? CardKind.Slash;
        return !CanSupplyCuiYanFactionSlashVariant(request, _players[request.CurrentCandidateSeat], kind, [GetAdvancedCard(id)], source);
    }

    private bool CanSupplyCuiYanFactionSlashVariant(FactionCardRequestHandle request, CharacterState provider,
        CardKind kind, IReadOnlyList<Card> materials, CardConversionSource? source = null)
    {
        if (!IsCuiYanFactionSlashUseForbidden(request, kind, materials, source)) return true;
        // The native fan decision belongs to the requester and occurs after the
        // provider has selected its complete material/conversion variant.
        return HasCuiYanCapability && (request.IsProgramSkillUse || request.IsBorrowedSwordUse) &&
            kind == CardKind.Slash && materials is [var primary] &&
            (primary.Kind == CardKind.Slash || IsOwnPublicPileSlashMaterial(provider, primary)) &&
            HasZhuqueFan(_players[request.OwnerSeat]) &&
            !IsCuiYanFactionSlashUseForbidden(request, CardKind.FireSlash, materials, source);
    }

    private bool HasCuiYanAllowedFactionSlashVariant(FactionCardRequestHandle request, CharacterState provider, Card card)
    {
        if (!HasCuiYanCapability || !IsFactionSlashUse(request)) return true;
        var kind = GetFactionSlashEffectiveSlashKind(request, provider, card);
        return CreateConversionChoiceVariants(provider, card, kind, false, "faction-suit-query", "", [card.Id], [],
            new Dictionary<string, string> { ["response-card-kind"] = kind.ToString() })
            .Any(choice => !IsCuiYanFactionSlashChoiceForbidden(request, choice));
    }

    [ProgramCardLegality(ProgramCardLegalityStage.DyingConversionBan, 10)]
    private bool CuiYanDyingConversionForbidden(ProgramViewAsSourceQuery query) =>
        query.DyingUse && IsCuiYanSingleUseForbidden(query.Owner, query.Card, query.Source);
}
