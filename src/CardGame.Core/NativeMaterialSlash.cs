using System.Text.Json.Serialization;

namespace CardGame.Core;

// This receipt belongs to the real equipment Use. It authorizes only the frozen
// target expansion; the native Slash still owns payment, responses and return.
public sealed record NativeMaterialSlashExtraTargetsOrigin(long CardUseFrameId, long ActionId,
    int ActorSeat, int ProviderSeat, int OrdinaryMaximum, long PaidFirst, long PaidLast = 0)
{
    private readonly IReadOnlyList<int> _physicalCardIds = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<int> _targetSeats = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<ProgramSlashTargetExpansion> _claims = Array.AsReadOnly(Array.Empty<ProgramSlashTargetExpansion>());
    public IReadOnlyList<int> PhysicalCardIds
    { get => _physicalCardIds; init => _physicalCardIds = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> TargetSeats
    { get => _targetSeats; init => _targetSeats = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<ProgramSlashTargetExpansion> Claims
    { get => _claims; init => _claims = Array.AsReadOnly(value.Select(claim => claim with { TargetSeats = claim.TargetSeats }).ToArray()); }
}

public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeMaterialSlashExtraTargetsOrigin? NativeMaterialSlashExtraTargets { get; init; }
}

public sealed record NativeMaterialSlashExtraTargetsIssuedEvent(NativeMaterialSlashExtraTargetsOrigin Origin) : IGameEvent;

public sealed partial class GameEngine
{
    // The two mature producers install their own typed return before entering
    // this tail. No grant or selected-program identity is manufactured here.
    private void CompleteNativeMaterialSlash(long id, CharacterState owner, IReadOnlyList<Card> cards,
        CardKind kind, IReadOnlyList<int> seats, CardConversionSource? conversion, bool armor)
    {
        var nuzhan = GetNuzhanModifiers(id, owner);
        foreach (var card in cards)
            MoveCard(card, FindOwnedCardLocation(owner, card), CardLocation.Processing, CardMoveReasons.Use,
                disclosure: CardMovementDisclosure.FaceUp);
        var use = LifecycleCardUse(id)!;
        if (use.AdjustedSlashReturn is { } adjusted)
            UpdateLifecycleCardUse(id, current => current with
            { AdjustedSlashReturn = adjusted with { PaidMovementLastSequence = CardMovements[^1].Sequence } });
        if (use.HpLossMaterialSlashReturn is { } hp)
        {
            hp = hp with { PaidLast = SlashTargetPenaltySequence };
            UpdateLifecycleCardUse(id, current => current with { HpLossMaterialSlashReturn = hp });
            AdvanceEventRulesAndQueueFact(new HpLossMaterialSlashIssuedEvent(id, hp.ActionId, hp.PolicySource,
                hp.PolicyHash, hp.FrozenMaximum, hp.ParentProgramFrameId, hp.IsZhangba));
        }
        if (use.NativeMaterialSlashExtraTargets is { } extra)
        {
            extra = extra with { PaidLast = SlashTargetPenaltySequence };
            UpdateLifecycleCardUse(id, current => current with { NativeMaterialSlashExtraTargets = extra });
            AdvanceEventRulesAndQueueFact(new NativeMaterialSlashExtraTargetsIssuedEvent(extra));
        }
        var counted = LifecycleCardUse(id)?.UnlimitedUse != true && !HasIssuedJudgedRankSlashQuota(id, owner.Seat) &&
            !HasWuHfOrDixianIssuedUse(id, owner.Seat) && !nuzhan.IgnoresSlashLimit && !IgnoresProgramSlashLimit(owner, conversion) &&
            _phase == TurnPhase.Play && owner.Seat == _currentSeat;
        if (counted) RecordSlashUseDebit(id, owner.Seat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(owner.Seat, kind);
        var damage = (1 + owner.AlcoholDamageBonus) + nuzhan.DamageBonus;
        CaptureProgramAlcoholConsumption(id, owner); owner.HasAlcoholEffect = false;
        var pending = new FangtianHalberdHandle(this, id, owner.Seat, cards[0], kind,
            armor, damage, seats, false, counted, conversion);
        ActiveFangtianHalberd = pending;
        if (conversion is not null)
            AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(id, conversion.SkillId, conversion.BindingId,
                owner.Seat, Array.AsReadOnly(cards.Select(c => c.Id).ToArray()), kind, IsUse: true, seats));
        else AdvanceEventRulesAndQueueFact(new ZhangbaSerpentSpearConvertedEvent(id, owner.Seat,
            Array.AsReadOnly(cards.Select(c => c.Id).ToArray()), IsUse: true, seats[0]));
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(cards[0].Id, kind, owner.Seat, seats[0], IgnoresArmor: armor));
        foreach (var seat in seats) NotifyAiOfSlash(owner, _players[seat]);
        BeginNextFangtianHalberdTarget(pending);
    }

    private (IReadOnlyList<int> LegalTargets, int OrdinaryMaximum, IReadOnlyList<ProgramSlashTargetExpansion> Claims)
        NativeMaterialSlashExpansion(CharacterState owner, IReadOnlyList<Card> cards)
    {
        var ids = cards.Select(c => c.Id).ToArray();
        var suit = PhysicalGroupSuit(owner, cards);
        var color = PhysicalGroupColor(owner, cards);
        var rank = ZhangbaSpecificSlashRank(owner, cards);
        var legal = Array.AsReadOnly(_players.Where(target =>
            CanUseVirtualSlashTarget(owner, target, CardKind.Slash, suit, color, rank, ids) &&
            !IsDirectedCardTargetProhibited(owner.Seat, target.Seat, CardKind.Slash) &&
            !IsCardTargetProhibited(target, CardKind.Slash, suit, color) &&
            !HasBeneficiarySuitShield(owner.Seat, target.Seat, suit)).Select(target => target.Seat).ToArray());
        var ordinary = Math.Max(1, ConvertRuleValue(EvaluateCardTargetCount(owner, CardKind.Slash)));
        return (legal, ordinary, TargetRules.ExtraSlashTargetClaims(new(owner.Seat, CardKind.Slash, suit ?? Suit.None, legal, color)));
    }

    [ProgramCardAction(ProgramCardActionPhase.AfterProgram, 31)]
    private void AddNativeMaterialSlashEquipmentActions(List<LegalAction> actions, CharacterState owner)
    {
        var index = actions.FindIndex(action => action.Kind == LegalActionKind.UseEquipmentEffect &&
            action.EquipmentKind == CardKind.ZhangbaSerpentSpear);
        if (index < 0) return;
        var maximum = actions[index].MaxTargetCount;
        foreach (var pair in GetZhangbaSlashPairs(owner))
        {
            var expansion = NativeMaterialSlashExpansion(owner, pair);
            if (expansion.Claims.Count == 0) continue;
            maximum = Math.Max(maximum, Math.Min(expansion.LegalTargets.Count, expansion.OrdinaryMaximum +
                expansion.Claims.SelectMany(claim => claim.TargetSeats).Distinct().Count()));
        }
        actions[index] = actions[index] with { MaxTargetCount = maximum };
    }

    private bool TryGetNativeMaterialSlashExpansion(CharacterState owner, IReadOnlyList<int> ids,
        IReadOnlyList<int> targets, out int ordinaryMaximum, out IReadOnlyList<ProgramSlashTargetExpansion> claims)
    {
        ordinaryMaximum = 0; claims = [];
        if (targets.Count < 2 || targets.Distinct().Count() != targets.Count ||
            !CanUseZhangbaSerpentSpear(owner)) return false;
        var cards = GetZhangbaSlashPairs(owner).FirstOrDefault(pair => pair.Select(c => c.Id).Order().SequenceEqual(ids.Order()));
        if (cards is null || !IsMoQuanyuCompleteZhangbaQuotaLegal(owner, targets, cards)) return false;
        var expansion = NativeMaterialSlashExpansion(owner, cards);
        var extra = expansion.Claims.SelectMany(claim => claim.TargetSeats).ToHashSet();
        if (extra.Count == 0 || targets.Any(target => !expansion.LegalTargets.Contains(target)) ||
            targets.Count(target => !extra.Contains(target)) > expansion.OrdinaryMaximum) return false;
        ordinaryMaximum = expansion.OrdinaryMaximum; claims = expansion.Claims; return true;
    }

    private void BeginNativeMaterialSlashExpansion(CharacterState owner, IReadOnlyList<Card> cards, IReadOnlyList<int> seats)
    {
        var ids = cards.Select(card => card.Id).ToArray();
        if (!TryGetNativeMaterialSlashExpansion(owner, ids, seats, out var ordinary, out var claims))
            throw new InvalidOperationException("The native material Slash lost its exact expanded target authorization.");
        var armor = HasArmorBypass(owner);
        var id = BeginCardUse(cards[0], owner.Seat, seats, CardKind.Slash,
            ignoresArmor: armor, physicalCardIds: ids, isTrueZhangbaSlash: true);
        var action = LifecycleCardUse(id)!.Action!;
        var origin = new NativeMaterialSlashExtraTargetsOrigin(id, action.ActionId, action.ActorSeat,
            action.ProviderSeat, ordinary, SlashTargetPenaltySequence + 1)
            { PhysicalCardIds = ids, TargetSeats = seats, Claims = claims };
        UpdateLifecycleCardUse(id, use => use with { NativeMaterialSlashExtraTargets = origin, TargetsAdjusted = true });
        CompleteNativeMaterialSlash(id, owner, cards, CardKind.Slash, seats, null, armor);
    }

    private static bool SameNativeMaterialSlashOrigin(NativeMaterialSlashExtraTargetsOrigin left,
        NativeMaterialSlashExtraTargetsOrigin right) =>
        left.CardUseFrameId == right.CardUseFrameId && left.ActionId == right.ActionId &&
        left.ActorSeat == right.ActorSeat && left.ProviderSeat == right.ProviderSeat &&
        left.OrdinaryMaximum == right.OrdinaryMaximum && left.PaidFirst == right.PaidFirst && left.PaidLast == right.PaidLast &&
        left.PhysicalCardIds.SequenceEqual(right.PhysicalCardIds) && left.TargetSeats.SequenceEqual(right.TargetSeats) &&
        left.Claims.Count == right.Claims.Count && left.Claims.Zip(right.Claims).All(pair =>
            pair.First.Source == pair.Second.Source && pair.First.GameplayHash == pair.Second.GameplayHash &&
            pair.First.TargetSeats.SequenceEqual(pair.Second.TargetSeats));

    private bool MatchesNativeMaterialSlashExpansion(CardUseFrame use)
    {
        if (use.NativeMaterialSlashExtraTargets is not { } origin || use.Action is not { Type: CardActionType.Use } action ||
            origin.CardUseFrameId != use.Id || origin.ActionId != action.ActionId || origin.ActorSeat != action.ActorSeat ||
            origin.ProviderSeat != action.ProviderSeat || action.ActorSeat != action.ProviderSeat || use.SourceSeat != action.ActorSeat ||
            origin.PhysicalCardIds is not { Count: 2 } ids || ids.Distinct().Count() != 2 ||
            !ids.SequenceEqual(use.PhysicalCardIds ?? []) || !ids.SequenceEqual(action.PhysicalCards.Select(cost => cost.CardId)) ||
            origin.OrdinaryMaximum < 1 || origin.TargetSeats.Count < 2 ||
            origin.TargetSeats.Distinct().Count() != origin.TargetSeats.Count || origin.Claims.Count == 0 ||
            origin.Claims.Any(claim => claim.Source.OwnerSeat != origin.ActorSeat ||
                !TargetRules.IsExtraSlashTargetSource(claim.Source, claim.GameplayHash) || claim.TargetSeats.Count == 0 ||
                claim.TargetSeats.Distinct().Count() != claim.TargetSeats.Count) ||
            origin.TargetSeats.Count(target => !origin.Claims.Any(claim => claim.TargetSeats.Contains(target))) > origin.OrdinaryMaximum ||
            origin.PaidFirst <= 0 || origin.PaidLast < origin.PaidFirst ||
            action.PhysicalCards.Any(cost => CardMovements.Count(m => m.Sequence >= origin.PaidFirst && m.Sequence <= origin.PaidLast &&
                m.CardId == cost.CardId && m.From == cost.From && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) != 1) ||
            CompleteProgramEventHistory().OfType<NativeMaterialSlashExtraTargetsIssuedEvent>().Count(e =>
                SameNativeMaterialSlashOrigin(e.Origin, origin)) != 1) return false;
        var issued = action;
        if (use.CurrentSlashFirePolicy is { Converted: true } fire)
        {
            if (!IsCurrentSlashFireChangedUse(use)) return false;
            AssertCurrentSlashFirePolicy(use); issued = fire.OriginalAction;
        }
        return IsSlashCard(use.CardKind) && action.EffectiveKind == use.CardKind &&
            issued.EffectiveKind == CardKind.Slash && issued.ConversionChain.Count == 0 &&
            CompleteProgramEventHistory().OfType<TargetsConfirmedEvent>().Any(e =>
                e.ResolutionId == use.Id && e.TargetSeats.SequenceEqual(origin.TargetSeats)) &&
            (!use.ProgramUseAccepted || CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Any(e =>
                e.Action.ActionId == origin.ActionId && e.Action.ActorSeat == origin.ActorSeat && e.Action.ProviderSeat == origin.ProviderSeat &&
                e.Action.EffectiveKind == CardKind.Slash &&
                e.Action.PhysicalCards.SequenceEqual(action.PhysicalCards) && e.Action.ConversionChain.Count == 0)) &&
            CompleteProgramEventHistory().OfType<ZhangbaSerpentSpearConvertedEvent>().Any(e => e.ResolutionId == use.Id &&
                e.UserSeat == origin.ActorSeat && e.IsUse && e.PhysicalCardIds.SequenceEqual(ids));
    }

    private bool IsExactNativeMaterialSlashExpansion(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { NativeMaterialSlashExtraTargets: not null } use ||
            !MatchesNativeMaterialSlashExpansion(use) || attack.CardUserSeat != use.Action!.ActorSeat ||
            !(attack.SourceSeat == use.SourceSeat && attack.EffectiveCardKind == use.CardKind && use.TargetSeats.Contains(attack.TargetSeat) ||
              IsNativeSlashTargetDuelAttack(attack)) ||
            !attack.PhysicalCards.Select(card => card.Id).SequenceEqual(use.PhysicalCardIds!)) return false;
        var retained = use.PhysicalCardIds!.Where(id => !IsCurrentUsePhysicalCardClaim(use.Id, id)).ToHashSet();
        return processing.All(card => retained.Contains(card.Id)) && retained.All(id =>
            _cardZones.GetLocation(id).Zone is CardZoneKind.Processing or CardZoneKind.Hand or CardZoneKind.DrawPile or CardZoneKind.DiscardPile);
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 33730)]
    private void AssertNativeMaterialSlashExpansions()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(use => use.NativeMaterialSlashExtraTargets is not null))
            if (!MatchesNativeMaterialSlashExpansion(use))
                throw new InvalidOperationException("The native material Slash lost its issued target source or real payment.");
    }
}
