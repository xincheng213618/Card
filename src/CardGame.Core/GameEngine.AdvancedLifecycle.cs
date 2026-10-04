namespace CardGame.Core;

public sealed record EquipmentSlotCapacityChangedEvent(int Seat, EquipmentSlot Slot, int Capacity) : IGameEvent;
public sealed record DeckRankCardsObtainedEvent(int Seat, string SkillId, IReadOnlyList<int> CardIds) : IGameEvent;

public sealed partial class GameEngine
{
    private readonly Dictionary<int, IReadOnlyList<CardKind>> _inheritedWeaponAbilities = [];
    private readonly Dictionary<int, IReadOnlyList<CardKind>> _copiedWeaponAbilities = [];
    private readonly HashSet<int> _weaponCopyConsumed = [];
    private int GetWeaponAttackRange(CharacterState owner, Card weapon) =>
        weapon.Kind == CardKind.RedBloodBlade && !EnabledContentSkillIds(owner).Contains("ol:shenyu", StringComparer.Ordinal)
            ? 0 : weapon.PrintedAttackRange ?? EquipmentCatalog.Get(weapon.Kind).WeaponAttackRange ?? 1;

    private SkillProgramStepOutcome ExecuteAdvancedLifecycle(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat)
    {
        var owner = _players[frame.OwnerSeat];
        var target = _players[targetSeat];
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        switch (effect.Op)
        {
            case SkillProgramEffectOp.BalanceHandAttackTricks:
                var slashes = GetHand(owner).Where(card => SlashKinds.Contains(AdvancedEffectiveHandKind(owner, card))).ToArray();
                var tricks = GetHand(owner).Where(card => AdvancedIsOrdinaryTrick(AdvancedEffectiveHandKind(owner, card))).ToArray();
                if (slashes.Length == tricks.Length) { DrawCards(owner, 1, true); return SkillProgramStepOutcome.Continue; }
                var balancing = (slashes.Length > tricks.Length ? slashes : tricks).Where(card => !IsSelfHandCategoryDiscardForbidden(owner.Seat, card, CardLocation.Hand(owner.Seat), OwnedCardMoveIntent.Discard)).ToArray();
                if (balancing.Length < Math.Abs(slashes.Length - tricks.Length)) return SkillProgramStepOutcome.Continue;
                return BeginAdvancedSelection(frame, effect, balancing.Select(card => card.Id.ToString()).ToArray(), Math.Abs(slashes.Length - tricks.Length));
            case SkillProgramEffectOp.ReplaceJudgmentPhase:
                _pendingTurnDelayedEffects |= DelayedTurnEffects.SkipJudgmentPhase;
                var polarity = _skillRuntimeState.GetConversionState(owner.Seat, frame.SkillId);
                _skillRuntimeState.ToggleConversionState(owner.Seat, frame.SkillId);
                return ScheduleProgramPhase(frame, polarity == SkillPolarity.Yang ? TurnPhase.Draw : TurnPhase.Play, SkillProgramPhaseContinuation.BeforeNormalPreparation);
            case SkillProgramEffectOp.AlterEquipmentSlots:
                foreach (var slot in effect.EquipmentSlots)
                    SetEquipmentSlotCapacity(target, slot, effect.Amount);
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.AbolishRandomEquipmentSlot:
                var available = Enum.GetValues<EquipmentSlot>().Where(slot => target.EquipmentSlotCapacity(slot) > 0).ToArray();
                if (available.Length > 0) SetEquipmentSlotCapacity(target, available[_random.Next(available.Length)], 0);
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.SampleFactionSkills:
                return BeginAdvancedSelection(frame, effect, GetHand(owner).Concat(GetEquipment(owner)).Where(card => !IsSelfHandCategoryDiscardForbidden(owner.Seat, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard)).Select(card => card.Id.ToString()).ToArray());
            case SkillProgramEffectOp.ExpireSampledSkills:
                var source = $"acquired:{effect.StateId ?? frame.SkillId}:sample:";
                var grants = owner.SkillGrants.Grants.Where(grant => grant.SourceId.StartsWith(source, StringComparison.Ordinal)).ToArray();
                foreach (var grant in grants) owner.SkillGrants.RemoveGrant(grant.GrantId);
                DrawCards(owner, grants.Select(grant => grant.SkillId).Distinct(StringComparer.Ordinal).Count(), true);
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.ReplaceSkillsOnAwakening:
                if (AdvancedOwnedSkillIds(owner).Count <= owner.MaxHp ||
                    !_skillRuntimeState.TryConsumeUsage(owner.Seat, frame.SkillId, "awakening", SkillUsageScope.Game, 1))
                    return SkillProgramStepOutcome.Continue;
                ChangeProgramMaximumHp(frame, -1);
                return BeginAdvancedSelection(frame, effect, AdvancedOwnedSkillIds(owner).Where(id => id != frame.SkillId).ToArray());
            case SkillProgramEffectOp.AccumulateCardRank:
                var context = frame.WindowContext?.CardUse;
                var action = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault()?.Action ??
                    _resolutionStack.OfType<CardUseFrame>().LastOrDefault(item => item.Action?.ActionId == context?.CardActionId)?.Action;
                var rank = action?.EffectiveRank ?? (action?.PhysicalCards.Count == 1 ? GetAdvancedCard(action.PhysicalCards[0].CardId).Rank : 0);
                if (effect.Marker is not { } marker || rank <= 0) return SkillProgramStepOutcome.Continue;
                var before = owner.Markers.GetValueOrDefault(marker);
                SetStrategicMarker(owner.Seat, marker, owner.Seat, checked(before + rank), frame);
                var tens = owner.Markers[marker] / 10 % 10;
                if (before / 10 % 10 != tens)
                {
                    var matches = _cardZones.CardsAt(CardLocation.DrawPile).Where(card => card.Rank == tens).ToArray();
                    if (matches.Length > 0) ObtainAdvancedCards(frame, [matches[_random.Next(matches.Length)]]);
                }
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.ObtainDeckRankSum:
                if (effect.Marker is not { } sumMarker || owner.Markers.GetValueOrDefault(sumMarker) <= _cardZones.Count(CardLocation.DrawPile))
                    return SkillProgramStepOutcome.Continue;
                var deck = _cardZones.CardsAt(CardLocation.DrawPile);
                if (!RankSubsetSearch.CanComplete(deck.Select(card => card.Rank), effect.MaximumRankSum))
                    return SkillProgramStepOutcome.Continue;
                SetStrategicMarker(owner.Seat, sumMarker, owner.Seat, 0, frame);
                return BeginAdvancedSelection(frame, effect, deck.Select(card => card.Id.ToString()).ToArray(), effect.MaximumRankSum);
            case SkillProgramEffectOp.DamageAfterDeckShuffle:
                if (!EventsSinceLastBoundary(item => item is TurnStartedEvent).OfType<CardMovedEvent>().Any(item => item.Reason == CardMoveReasons.Reshuffle))
                    return SkillProgramStepOutcome.Continue;
                return BeginAdvancedSelection(frame, effect, _players.Where(player => player.IsAlive && player.Seat != owner.Seat).Select(player => player.Seat.ToString()).ToArray());
            case SkillProgramEffectOp.EquipSampledGenerals:
                var selectedGeneralIds = _players.SelectMany(player => new[] { player.General.Id, player.SecondaryGeneral?.Id })
                    .OfType<string>().ToHashSet(StringComparer.Ordinal);
                var generals = _generalPool.Select(general => general.Id)
                    .Where(id => !selectedGeneralIds.Contains(id) && !_usedGeneralWeaponSources.Contains(id))
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                _random.Shuffle(generals);
                return BeginAdvancedSelection(frame, effect, generals.Take(Math.Max(1, effect.Amount)).ToArray());
            case SkillProgramEffectOp.DamageFarthestCharacter:
                if (frame.SelectedTargetSeats.Count == 0)
                    return BeginAdvancedSelection(frame, effect, _players.Where(player => player.IsAlive && player.Seat != owner.Seat &&
                        IsFarthestInRange(owner, player) && _skillRuntimeState.GetUsage(owner.Seat, frame.SkillId, $"target:{player.Seat}", SkillUsageScope.Turn) == 0)
                        .Select(player => player.Seat.ToString()).ToArray());
                if (frame.SelectedTargetSeats.Count != 1 || !IsFarthestInRange(owner, target) ||
                    !_skillRuntimeState.TryConsumeUsage(owner.Seat, frame.SkillId, $"target:{targetSeat}", SkillUsageScope.Turn, 1))
                    throw new InvalidOperationException("The damage recipient is not an unused farthest character in attack range.");
                return BeginProgramSkillDamage(frame, targetSeat, 1);
            case SkillProgramEffectOp.PlaceNamedWeapon:
                PlaceAdvancedWeapon(frame, targetSeat, effect.OutputKind!.Value);
                return SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.ReclaimNamedWeapon:
                return BeginWeaponReclaim(frame, effect);
            case SkillProgramEffectOp.InheritWeapon:
                var weapons = _players.Where(player => player.IsAlive).SelectMany(GetEquipment)
                    .Concat(GetHand(owner)).Where(card => EquipmentCatalog.IsEquipment(card.Kind) && EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon)
                    .DistinctBy(card => card.Id).Select(card => card.Id.ToString()).ToArray();
                return BeginAdvancedSelection(frame, effect, weapons);
            default: throw new InvalidOperationException("Unsupported advanced lifecycle operation.");
        }
    }

    private Card GetAdvancedCard(int id) => _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(card => card.Id == id);
    private CardKind AdvancedEffectiveHandKind(CharacterState owner, Card card) => GetProgramCardIdentityMatches(owner, card).FirstOrDefault()?.Identity.OutputKind ?? card.Kind;
    private static bool AdvancedIsOrdinaryTrick(CardKind kind) => CardCatalog.Get(kind).CategoryName == "锦囊牌" && !IsDelayedCard(kind);
    private void SetEquipmentSlotCapacity(CharacterState player, EquipmentSlot slot, int capacity)
    {
        if (capacity < 0 || capacity > 5) throw new InvalidOperationException("Equipment slot capacity must be between zero and five.");
        player.EquipmentSlotCapacities[slot] = capacity;
        foreach (var card in GetEquipment(player).Where(card => EquipmentCatalog.Get(card.Kind).Slot == slot).Skip(capacity).ToArray())
            MoveCard(card, CardLocation.Equipment(player.Seat), CardLocation.DiscardPile, new("equipment.slot-abolished"));
        AdvanceEventRulesAndQueueFact(new EquipmentSlotCapacityChangedEvent(player.Seat, slot, capacity));
    }
    private bool IsFarthestInRange(CharacterState owner, CharacterState target)
    {
        var range = GetAttackRange(owner.Seat);
        var legal = _players.Where(player => player.IsAlive && player.Seat != owner.Seat && IsWithinAttackRange(owner.Seat, player.Seat)).ToArray();
        return legal.Contains(target) && GetCombatDistance(owner.Seat, target.Seat) == legal.Max(player => GetCombatDistance(owner.Seat, player.Seat));
    }
    private void ObtainAdvancedCards(ProgramSkillFrame frame, IReadOnlyList<Card> cards)
    {
        if (cards.Count == 0) return;
        MoveCards(cards, CardLocation.DrawPile, CardLocation.Hand(frame.OwnerSeat), new("skill.deck-search.obtain"));
        AdvanceEventRulesAndQueueFact(new DeckRankCardsObtainedEvent(frame.OwnerSeat, frame.SkillId, cards.Select(card => card.Id).ToArray()));
    }
    private SkillProgramStepOutcome BeginAdvancedSelection(ProgramSkillFrame frame, SkillProgramEffect effect, IReadOnlyList<string> candidates, int rankSum = 0)
    {
        if (candidates.Count == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(frame with { AdvancedSelection = new(effect.Op, candidates, [], rankSum) });
        PromptAdvancedSelection(frame.Id);
        return SkillProgramStepOutcome.AwaitChoice;
    }
}

internal static class RankSubsetSearch
{
    internal static bool CanComplete(IEnumerable<int> ranks, int target)
    {
        if (target < 0) return false;
        var possible = new bool[target + 1]; possible[0] = true;
        foreach (var rank in ranks.Where(rank => rank > 0 && rank <= target))
            for (var sum = target; sum >= rank; sum--) possible[sum] |= possible[sum - rank];
        return possible[target];
    }
}
