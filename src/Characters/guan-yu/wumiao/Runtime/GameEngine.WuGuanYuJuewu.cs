namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasWuGuanYuJuewuCapability => _contentRegistry.ProgramDependencies.UsesRankTwoDamageUse;
    private bool IsJuewuSource(CardConversionSource? source) => source is { SkillId: "wumiao:juewu" } &&
        ViewAsRule(source) is { RankTwoDamageUse: true } rule && source.BindingId == JuewuComposition.Binding(rule.OutputKind);

    // Foreign acquisition is one uninterrupted actual Hand residency, not a permanent entity mark.
    private CardMovementRecord? JuewuForeignHandEntry(int ownerSeat, int cardId, long beforeSequence = long.MaxValue)
    {
        var entry = _cardMovements.LastOrDefault(m => m.CardId == cardId && m.Sequence < beforeSequence &&
            m.To == CardLocation.Hand(ownerSeat));
        if (entry is null) return null;
        var origin = ActualHandGainOriginalSource(entry);
        return origin.OwnerSeat is { } foreign && foreign != ownerSeat && IsValidPlayerSeat(foreign)
            ? entry : null;
    }

    private int? JuewuForeignHandRank(CharacterState owner, Card card) =>
        HasWuGuanYuJuewuCapability && HasRuntimeSkill(owner, "wumiao:juewu") && card.Id > 0 &&
        _cardZones.GetLocation(card.Id) == CardLocation.Hand(owner.Seat) &&
        JuewuForeignHandEntry(owner.Seat, card.Id) is not null ? 2 : null;

    private Card ApplyJuewuOwnedAppearance(CharacterState owner, Card card) =>
        JuewuForeignHandRank(owner, card) is { } rank ? card with { Rank = rank } :
        LiveDeclarationPayment(card.Id) is { JuewuEffectiveRank: 2 } paid && paid.ProviderSeat == owner.Seat
            ? card with { Rank = 2 } : card;

    private int? CaptureJuewuActionRank(int providerSeat, IReadOnlyList<int> ids)
    {
        if (!HasWuGuanYuJuewuCapability || ids is not [var id]) return null;
        if (LiveDeclarationPayment(id) is { JuewuEffectiveRank: 2 } declared && declared.ProviderSeat == providerSeat)
            return 2;
        var card = _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c => c.Id == id);
        return JuewuForeignHandRank(_players[providerSeat], card);
    }

    // Native response actions are built immediately after the actual payment. Read only
    // that exact departure, before any later Hand entry can supply another entitlement.
    private int? CaptureJuewuResponseRank(CharacterState provider, IReadOnlyList<CardActionCost> costs)
    {
        if (!HasWuGuanYuJuewuCapability || !HasRuntimeSkill(provider, "wumiao:juewu") ||
            costs is not [var cost] || cost.From != CardLocation.Hand(provider.Seat)) return null;
        var departure = _cardMovements.LastOrDefault(m => m.CardId == cost.CardId && m.From == cost.From);
        return departure is not null && departure.From == cost.From && departure.CardKind == cost.CardKind &&
            departure.To == CardLocation.Processing &&
            (departure.Reason == CardMoveReasons.Respond || departure.Reason == CardMoveReasons.Nullification) &&
            !_cardMovements.Any(m => m.CardId == cost.CardId && m.Sequence > departure.Sequence &&
                m.To == CardLocation.Hand(provider.Seat)) &&
            JuewuForeignHandEntry(provider.Seat, cost.CardId, departure.Sequence) is not null ? 2 : null;
    }

    private bool JuewuPureProviderFrontier(int actorSeat)
    {
        var top = _resolutionStack.LastOrDefault();
        bool At(long id) => top?.Id == id || top is ResponseWindowFrame response && response.ParentFrameId == id;
        return ActiveFactionCardRequest is { AwaitingProviders: true } slash &&
                slash.CurrentCandidateSeat == actorSeat && At(slash.OwnerFrameId) ||
            ActiveFactionDefense is { } defense && defense.CurrentCandidateSeat == actorSeat && At(defense.Attack.ResolutionId);
    }

    private bool IsJuewuTrueSlashUse(CharacterState owner)
    {
        if (!owner.IsAlive || JuewuPureProviderFrontier(owner.Seat)) return false;
        var top = _resolutionStack.LastOrDefault();
        bool At(long id) => top?.Id == id || top is ResponseWindowFrame response && response.ParentFrameId == id;
        if (ActiveBorrowedSword is { ActiveAttack: null, AwaitingSlashChoice: true } borrowed &&
            borrowed.WeaponOwnerSeat == owner.Seat && At(borrowed.ResolutionId)) return true;
        if (ActiveQinglongCrescentBlade is { } qinglong && qinglong.Attack.SourceSeat == owner.Seat &&
            SameAttackOwner(qinglong.Attack, ActiveCardAttack) && At(qinglong.Attack.ResolutionId)) return true;
        if (top is not ProgramSkillFrame program || GetPausedPrivateOfferEffect(program) is not { } effect) return false;
        if (IsQigongRequest(program)) return program.LvKuangLvXiang!.Actor == owner.Seat;
        if (IsCaoChunInitialRequest(program) || IsWuJingHejiRequest(program)) return program.OwnerSeat == owner.Seat;
        return RoundOnceCardlessProgramActor(program, effect.Op, owner.Seat);
    }

    private bool CanUseJuewuConversion(CharacterState owner, Card card, IndexedSkillProgramInstance instance,
        SkillProgramViewAs rule, bool forResponse, bool dyingUse = false, int? requestedUseTarget = null)
    {
        if (!rule.RankTwoDamageUse) return true;
        var source = new CardConversionSource(instance.SkillId, rule.Id, owner.Seat, instance.SkillInstanceId);
        var location = _cardZones.GetLocation(card.Id);
        return HasWuGuanYuJuewuCapability && IsJuewuSource(source) && owner.IsAlive && _turnNumber > 0 &&
            HasRuntimeSkillInstance(owner, source.SkillId, source.SkillInstanceId) &&
            location.OwnerSeat == owner.Seat &&
            (location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment ||
                location.Zone == CardZoneKind.WoodenOxGrain && UsesFormalWoodenOx &&
                GetEquipment(owner).Any(c => c.Kind == CardKind.WoodenOx)) &&
            EffectiveOwnedCardRank(owner, card) == 2 && !IsTurnHandCardRestricted(owner, card) &&
            !dyingUse && requestedUseTarget is null && !JuewuPureProviderFrontier(owner.Seat) &&
            IsActivityOrdinaryTrickNameAvailable(rule.OutputKind) &&
            (!forResponse || JuewuComposition.IsSlash(rule.OutputKind) && IsJuewuTrueSlashUse(owner));
    }

    private bool JuewuSlashAliasAllowed(CharacterState owner, bool forResponse) =>
        !forResponse || IsJuewuTrueSlashUse(owner);

    private bool JuewuNativeConversionMatchesOutput(CharacterState owner, CardConversionSource source, CardKind kind) =>
        !IsJuewuSource(source) || !JuewuExplicitForcedSlashFrontier(owner) || ViewAsRule(source)!.OutputKind == kind;

    private bool JuewuExplicitForcedSlashFrontier(CharacterState owner)
    {
        var top = _resolutionStack.LastOrDefault();
        bool At(long id) => top?.Id == id || top is ResponseWindowFrame response && response.ParentFrameId == id;
        return ActiveBorrowedSword is { ActiveAttack: null, AwaitingSlashChoice: true } borrowed &&
                borrowed.WeaponOwnerSeat == owner.Seat && At(borrowed.ResolutionId) ||
            ActiveQinglongCrescentBlade is { } qinglong && qinglong.Attack.SourceSeat == owner.Seat &&
                SameAttackOwner(qinglong.Attack, ActiveCardAttack) && At(qinglong.Attack.ResolutionId);
    }

    private IReadOnlyList<SlashUseVariant> JuewuPhysicalSlashVariants(CharacterState owner, Card card) =>
        SlashKinds.Where(kind => GetProgramViewAsConversions(owner, card, kind, false)
                .Any(source => IsJuewuSource(source) && ViewAsRule(source)!.OutputKind == kind))
            .Select(kind => new SlashUseVariant(kind, AdditionalConversionSource: null, UsesZhuqueFan: false)).ToArray();

    private CardKind JuewuSelectedSlashKind(CharacterState owner, Card card, CardKind fallback,
        CardConversionSource? explicitSource = null)
    {
        var selected = explicitSource ?? _selectedUseConversion ?? _selectedResponseConversion;
        return IsJuewuSource(selected) && ViewAsRule(selected!) is { } rule && IsSlashCard(rule.OutputKind) &&
            GetProgramViewAsConversions(owner, card, rule.OutputKind, false).Contains(selected!)
            ? rule.OutputKind : fallback;
    }

    [ProgramCardLegality(ProgramCardLegalityStage.ConfiguredConversionBan, 50)]
    private bool JuewuConversionUnavailable(ProgramConfiguredViewAsQuery query) =>
        !CanUseJuewuConversion(query.Owner, query.Card, query.Instance, query.Rule, query.ForResponse, query.DyingUse, query.RequestedUseTarget);
}
