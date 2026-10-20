namespace CardGame.Core;

public sealed record HulaoInitialEquipmentPlacedEvent(int BossSeat) : IGameEvent;

public sealed partial class GameEngine
{
    private static string? HulaoEquipmentSkillId(CardKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_HulaoEquipment.HulaoEquipmentSkillId(kind);

    private static string? HulaoEquipmentSkillGrantId(Card card) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_HulaoEquipment.HulaoEquipmentSkillGrantId(card);

    private bool ResolveHulaoEquipmentSkillGrant(Card card, CardLocation from, CardLocation to)
    {
        if (HulaoEquipmentSkillId(card.Kind) is not { } skillId) return false;
        var grantId = HulaoEquipmentSkillGrantId(card)!;
        if (from is { Zone: CardZoneKind.Equipment, OwnerSeat: { } oldSeat })
            _players[oldSeat].SkillGrants.RemoveGrant(grantId);
        if (to is { Zone: CardZoneKind.Equipment, OwnerSeat: { } newSeat } &&
            _contentRegistry.Skills.ContainsKey(skillId))
            _players[newSeat].SkillGrants.Grant(new SkillGrant(grantId, skillId, grantId, grantId));
        return true;
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 27500)]
    private bool AllowsHulaoEquipmentTrigger(ProgramTriggerLegalityContext gate)
    {
        var kind = gate.SkillId switch
        {
            "hulao:wushuang-halberd" => CardKind.WushuangHalberd,
            "hulao:purple-gold-crown" => CardKind.PurpleGoldCrown,
            "hulao:red-cotton-robe" => CardKind.RedCottonRobe,
            "hulao:lion-belt" => CardKind.LionBelt,
            _ => (CardKind?)null
        };
        if (kind is null) return true;
        if (!GetEquipment(gate.Owner).Any(card => card.Kind == kind &&
            HulaoEquipmentSkillGrantId(card) == gate.Candidate.SkillInstanceId)) return false;
        if (kind == CardKind.PurpleGoldCrown) return true;
        if (kind == CardKind.WushuangHalberd)
        {
            // A damage transfer or chained elemental copy is not damage to this
            // Slash's target. Virtual Slash still has a genuine native use/action.
            return CurrentDamageAttempt is { IsChainPropagation: false, IsSourceLess: false } attack &&
                attack.SourceSeat == gate.OwnerSeat && attack.EffectiveCardKind is { } slash && IsSlashCard(slash) &&
                LifecycleCardUse(attack.ResolutionId) is { Action: { Type: CardActionType.Use } action } &&
                action.ActorSeat == gate.OwnerSeat && IsSlashCard(action.EffectiveKind) &&
                action.EffectiveDesignatedTargetSeats.Contains(attack.TargetSeat);
        }
        if (IsArmorIneffectiveForTurn(gate.Owner)) return false;
        if (kind == CardKind.RedCottonRobe)
            return CurrentDamageAttempt is { } damage && damage.TargetSeat == gate.OwnerSeat &&
                !damage.IgnoresArmor && !HasDirectedCardArmorBypass(damage.ResolutionId, gate.OwnerSeat);
        if (gate.WindowContext?.CardUse is not { } cardUse ||
            LifecycleCardUse(cardUse.ParentCardUseFrameId) is not { Action: { } useAction } use ||
            useAction.ActionId != cardUse.CardActionId || useAction.ActorSeat == gate.OwnerSeat ||
            !useAction.EffectiveDesignatedTargetSeats.SequenceEqual(new[] { gate.OwnerSeat }) ||
            use.IgnoresArmor || HasDirectedCardArmorBypass(use.Id, gate.OwnerSeat)) return false;
        return !IsSlashCard(useAction.EffectiveKind) ||
            !HasCardArmorBypass(_players[useAction.ActorSeat], gate.Owner, useAction.EffectiveKind);
    }

    private IReadOnlyList<Card> PrepareHulaoActivityDeck(IReadOnlyList<Card> original)
    {
        if (_modeDefinition.HulaoActivity is null) return original;
        var candidates = new List<CardKind> { CardKind.WushuangHalberd, CardKind.PurpleGoldCrown,
            CardKind.RedCottonRobe, CardKind.LionBelt };
        _random.Shuffle(candidates);
        var selected = candidates.Take(_random.Next(4) + 1).ToHashSet();
        var cards = original.Select(card => card.Kind switch
        {
            CardKind.FiveGrains or CardKind.PeachGarden => card with { Kind = CardKind.AllianceFeast },
            CardKind.FangtianHalberd when selected.Contains(CardKind.WushuangHalberd) =>
                card with { Kind = CardKind.WushuangHalberd },
            CardKind.BaguaFormation when selected.Contains(CardKind.LionBelt) =>
                card with { Kind = CardKind.LionBelt },
            _ => card
        }).ToList();
        var id = cards.Select(card => card.Id).DefaultIfEmpty(0).Max();
        // Added card faces use explicit local printed suits/ranks; replacement
        // entities retain the military deck's exact physical identity and face.
        if (selected.Contains(CardKind.PurpleGoldCrown)) cards.Add(new(++id, CardKind.PurpleGoldCrown, Suit.Club, 1));
        if (selected.Contains(CardKind.RedCottonRobe)) cards.Add(new(++id, CardKind.RedCottonRobe, Suit.Diamond, 1));
        return Array.AsReadOnly(cards.ToArray());
    }

    private void EquipHulaoBossInitialEquipment()
    {
        if (_modeDefinition.HulaoActivity is null || CompleteProgramEventHistory()
            .OfType<HulaoInitialEquipmentPlacedEvent>().Any()) return;
        var boss = _players[BossActivitySeat];
        if (!boss.GeneralSelected || !boss.IsAlive)
            throw new InvalidOperationException("Hulao initial equipment requires the selected living Boss.");
        var selected = _cardZones.CardsAt(CardLocation.DrawPile)
            .Where(card => HulaoEquipmentSkillId(card.Kind) is not null).ToList();
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            // The source does not define how two selected armors share one slot.
            // This local choice equips one uniformly; every other entity remains
            // in the draw pile, rather than creating or destroying any equipment.
            var slotCards = selected.Where(card => EquipmentCatalog.Get(card.Kind).Slot == slot).ToArray();
            var kinds = slotCards.Select(card => card.Kind).Distinct().ToArray();
            if (kinds.Length == 0) continue;
            var kind = kinds[_random.Next(kinds.Length)];
            var entities = slotCards.Where(card => card.Kind == kind).ToArray();
            var card = entities[_random.Next(entities.Length)];
            if (card is not null) PlaceAdvancedEquipment(card, boss.Seat);
        }
        AdvanceEventRulesAndQueueFact(new HulaoInitialEquipmentPlacedEvent(boss.Seat));
    }
}
