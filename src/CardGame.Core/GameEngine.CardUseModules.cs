namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsTurnHandCardRestricted(CharacterState player, Card card)
    {
        if (IsResponseEntityRestricted(player.Seat, card.Id)) return true;
        if (IsPlayPhasePhysicalCardRestricted(player, card)) return true;
        if (_cardZones.GetLocation(card.Id) != CardLocation.Hand(player.Seat)) return false;
        if (ActiveCardAttack is { ProhibitsTargetHandResponses: true } attack &&
            attack.TargetSeat == player.Seat && attack.CardUserSeat != player.Seat)
            return true;
        var suit = EffectiveSuit(player, card);
        return _turnCardUseEffects.IsHandColorRestricted(
            _turnNumber,
            _currentSeat,
            player.Seat,
            IsRedSuit(suit),
            isColorless: suit == Suit.None);
    }

    private bool IsCardUseForbidden(int actorSeat, CardKind effectiveKind, CardActionType actionType, bool ignoreIssuedPlayBan=false) =>
        !ignoreIssuedPlayBan && (actionType == CardActionType.Use || actionType == CardActionType.Response && (effectiveKind == CardKind.Nullification || IsProgramResponseCardUse(_players[actorSeat], effectiveKind))) && HasIssuedPlayPhaseUseBan(actorSeat) ||
        _turnCardUseEffects.IsCardUseForbidden(
            _turnNumber,
            _currentSeat,
            actorSeat,
            effectiveKind,
            actionType) || IsExclusiveTurnPeachUseForbidden(actorSeat, effectiveKind, actionType) ||
        IsNextActualUseCounterspellForbidden(actorSeat, effectiveKind, actionType) || IsForeignTurnAlcoholUseForbidden(actorSeat, effectiveKind, actionType);

    private IReadOnlyList<LegalAction> FilterTurnCardUseRestrictions(
        CharacterState actor,
        IEnumerable<LegalAction> actions) =>
        actions.Where(action =>
        {
            if (HasTurnCardTargetRestriction(actor.Seat, SkillProgramCardTargetRestriction.SelfOnly))
            {
                if (action.Kind is LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage)
                    return false;
                if (action.Kind == LegalActionKind.UseEquipmentEffect && action.MaxTargetCount > 0)
                    return false;
                if (action.TargetSeats.Any(targetSeat => targetSeat != actor.Seat))
                    return false;
            }
            if (!TryGetLegalActionEffectiveCardKind(actor, action, out var effectiveKind)) return true;
            if (IsSelfTargetForbiddenAction(actor, effectiveKind, action.Kind == LegalActionKind.BorrowedSword
                    ? action.TargetSeats : GetSelfProhibitionPolicyTargets(actor, action))) return false;
            return !(action.CardId is { } id && IsPlayPhasePhysicalCardRestricted(actor,
                _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(card => card.Id == id))) &&
                !IsCardUseForbidden(actor.Seat, effectiveKind, CardActionType.Use) &&
                GetSelfProhibitionPolicyTargets(actor, action).All(targetSeat =>
                    !IsDirectedCardTargetProhibited(actor.Seat, targetSeat, effectiveKind));
        }).ToArray();

    private bool HasTurnCardTargetRestriction(
        int actorSeat,
        SkillProgramCardTargetRestriction restriction,
        int? targetSeat = null) =>
        _turnCardUseEffects.HasTargetRestriction(
            _turnNumber, _currentSeat, actorSeat, restriction, targetSeat);

    private IReadOnlyList<int> ApplyTurnCardGroupTargetRestrictions(
        CharacterState source,
        IReadOnlyList<int> targets) =>
        HasTurnCardTargetRestriction(source.Seat, SkillProgramCardTargetRestriction.SelfOnly)
            ? targets.Where(seat => seat == source.Seat).ToArray()
            : targets;

    private bool HasUnlimitedTurnRuleModifier(int actorSeat, SkillRuleQuery query) =>
        _turnCardUseEffects.GetRuleModifiers(_turnNumber, _currentSeat, actorSeat, query)
            .Any(item => item.Operation == SkillRuleOperation.Unlimited);

    private int GetAdditiveTurnRuleModifier(int actorSeat, SkillRuleQuery query) =>
        _turnCardUseEffects.GetRuleModifiers(_turnNumber, _currentSeat, actorSeat, query)
            .Where(item => item.Operation == SkillRuleOperation.Add)
            .Sum(item => item.Amount);

    private bool TryGetLegalActionEffectiveCardKind(
        CharacterState actor,
        LegalAction action,
        out CardKind effectiveKind)
    {
        effectiveKind = default;
        if (action.Kind is LegalActionKind.EndPlay or LegalActionKind.Recast ||
            action.CardId is not { } cardId ||
            action.Kind is not (LegalActionKind.Slash or LegalActionKind.Peach or
                LegalActionKind.Duel or LegalActionKind.DrawTwo or LegalActionKind.BarbarianAssault or
                LegalActionKind.ArrowBarrage or LegalActionKind.PeachGarden or LegalActionKind.FiveGrains or
                LegalActionKind.Dismantlement or LegalActionKind.Snatch or LegalActionKind.FireAttack or
                LegalActionKind.Alcohol or LegalActionKind.Equip or LegalActionKind.IronChain or
                LegalActionKind.Indulgence or LegalActionKind.SupplyShortage or LegalActionKind.Lightning or
                LegalActionKind.BorrowedSword))
        {
            return false;
        }

        effectiveKind = action.PlayedCardKind ??
            GetPlayableCards(actor).Single(card => card.Id == cardId).Kind;
        return true;
    }

    private void ExpireTurnCardUseEffects(int turnNumber, int turnSeat)
    {
        _currentTurnHeartSlashBonuses.RemoveAll(b => b.TurnNumber == turnNumber && b.TurnOwnerSeat == turnSeat);
        ExpireCurrentTurnNonLockedSkillSuppressions(turnNumber, turnSeat);
        ExpireCurrentTurnOwnSkillSuppressions(turnNumber, turnSeat);
        ExpireNextSlashDamage(turnNumber, turnSeat);
        ExpireDirectedTurnCardPolicies(turnNumber, turnSeat);
        ExpireOriginalTargetAdditionGrants(turnNumber, turnSeat);
        ResolveFirstRoundGameUsageRefunds(turnNumber, turnSeat);
        var expired = _turnCardUseEffects.ExpireTurn(turnNumber, turnSeat);
        if (expired.Count == 0) return;
        AdvanceEventRulesAndQueueFact(new TurnCardUseEffectsExpiredEvent(turnNumber, turnSeat, expired));
    }

    private void ExpireOwnerTurnStartDamageModifiers(int ownerSeat)
    {
        ExpireAttributedNatureMarkers(ownerSeat);
        var expired = _turnCardUseEffects.ExpireDamageModifiersAtOwnerTurnStart(ownerSeat, _turnNumber);
        if (expired.Count > 0)
            AdvanceEventRulesAndQueueFact(new TurnCardUseEffectsExpiredEvent(_turnNumber, ownerSeat, expired));
    }

    private void ExpireDeadOwnerDamageModifiers(int ownerSeat)
    {
        var expired = _turnCardUseEffects.ExpireDamageModifiersOnDeath(ownerSeat);
        if (expired.Count > 0)
            AdvanceEventRulesAndQueueFact(new TurnCardUseEffectsExpiredEvent(_turnNumber, _currentSeat, expired));
    }

    private void GrantProgramTurnCardDamageModifier(
        ProgramSkillFrame frame,
        IReadOnlyList<CardKind> cardKinds,
        int amount,
        SkillProgramDamageModifierExpiration expiration,
        SkillProgramDamageModifierSourceScope sourceScope)
    {
        ValidateProgramTurnEffectGrant(frame);
        if (amount <= 0 || cardKinds.Count == 0 ||
            !Enum.IsDefined(expiration) || !Enum.IsDefined(sourceScope))
        {
            throw new InvalidOperationException(
                "A turn card-damage modifier requires positive damage and card kinds.");
        }

        var source = CreateProgramTurnEffectSource(frame);
        var granted = _turnCardUseEffects.GrantDamageModifier(
            _turnNumber,
            _currentSeat,
            frame.Id,
            frame.InstructionIndex - 1,
            source,
            cardKinds,
            amount,
            expiration,
            sourceScope);
        AdvanceEventRulesAndQueueFact(new CardDamageModifierGrantedEvent(granted));
    }

    private void GrantProgramTurnCardActionProhibition(
        ProgramSkillFrame frame,
        IReadOnlyList<CardKind> cardKinds,
        IReadOnlyList<CardActionType> actionTypes)
    {
        ValidateProgramTurnEffectGrant(frame);
        if (cardKinds.Count == 0 || actionTypes.Count == 0)
            throw new InvalidOperationException("A turn card-action prohibition requires kinds and actions.");
        var granted = _turnCardUseEffects.GrantActionProhibition(
            _turnNumber, _currentSeat, frame.Id, frame.InstructionIndex - 1,
            CreateProgramTurnEffectSource(frame), cardKinds, actionTypes);
        AdvanceEventRulesAndQueueFact(new CardActionProhibitionGrantedEvent(granted));
    }

    private void GrantProgramTurnHandColorRestriction(
        ProgramSkillFrame frame,
        string sourceBind,
        int targetSeat, bool useFrozenSuit = false)
    {
        ValidateProgramTurnEffectGrant(frame);
        if (!IsValidPlayerSeat(targetSeat) || targetSeat == frame.OwnerSeat || !_players[targetSeat].IsAlive)
            throw new InvalidOperationException("A hand-color restriction requires one living other character.");
        var binding = GetProgramCardSet(frame, sourceBind);
        if (binding.CardIds.Count != 1 || binding.SourceLocations.Count != 1 ||
            (!useFrozenSuit && _cardZones.GetLocation(binding.CardIds[0]) != binding.SourceLocations[0]))
            throw new InvalidOperationException("A hand-color restriction requires one stable bound card.");
        var effectiveSuit = useFrozenSuit
            ? binding.FrozenRevealedSuit ?? throw new InvalidOperationException("A frozen hand-color restriction requires its captured suit.")
            : EffectiveSuit(_players[frame.OwnerSeat], _cardZones.CardsAt(binding.SourceLocations[0])
                .Single(item => item.Id == binding.CardIds[0]));
        var granted = _turnCardUseEffects.GrantHandColorRestriction(
            _turnNumber,
            _currentSeat,
            frame.Id,
            frame.InstructionIndex - 1,
            CreateProgramTurnEffectSource(frame),
            targetSeat,
            IsRedSuit(effectiveSuit));
        AdvanceEventRulesAndQueueFact(new HandCardColorRestrictionGrantedEvent(granted));
    }

    private void GrantProgramTurnHandCardProhibition(ProgramSkillFrame frame, int targetSeat)
    {
        ValidateProgramTurnEffectGrant(frame);
        if (!IsValidPlayerSeat(targetSeat) || targetSeat == frame.OwnerSeat || !_players[targetSeat].IsAlive)
            throw new InvalidOperationException("A hand-card prohibition requires one living other character.");
        var effectIndex = frame.InstructionIndex - 1;
        foreach (var isRed in new[] { false, true })
        {
            var granted = _turnCardUseEffects.GrantHandColorRestriction(
                _turnNumber, _currentSeat, frame.Id, -2 * effectIndex - (isRed ? 2 : 1),
                CreateProgramTurnEffectSource(frame), targetSeat, isRed);
            AdvanceEventRulesAndQueueFact(new HandCardColorRestrictionGrantedEvent(granted));
        }
    }

    private void GrantProgramTurnRuleModifier(
        ProgramSkillFrame frame,
        SkillRuleQuery query,
        SkillRuleOperation operation,
        int amount,
        IReadOnlyList<CardKind> cardKinds)
    {
        ValidateProgramTurnEffectGrant(frame);
        var valid = query == SkillRuleQuery.SlashLimit && operation == SkillRuleOperation.Add && (amount > 0 || amount is >= -20 and <= -1) ||
                    query == SkillRuleQuery.HandLimit && operation == SkillRuleOperation.Add && amount is >= -20 and <= 20 && amount != 0 ||
                    query == SkillRuleQuery.OutgoingDistance && operation == SkillRuleOperation.Add && amount is >= -20 and <= 20 && amount != 0 ||
                    (query is SkillRuleQuery.CardUseDistanceLimit or SkillRuleQuery.SlashDistanceLimit or SkillRuleQuery.AttackRange) &&
                    operation == SkillRuleOperation.Unlimited && amount == 0 ||
                    query == SkillRuleQuery.CardTargetCount && operation == SkillRuleOperation.Add &&
                    amount > 0 && cardKinds.Count > 0;
        if (query != SkillRuleQuery.CardTargetCount && cardKinds.Count > 0)
            throw new InvalidOperationException("Only card target-count modifiers accept card kinds.");
        if (!valid) throw new InvalidOperationException("The turn rule modifier is unsupported.");
        var granted = _turnCardUseEffects.GrantRuleModifier(
            _turnNumber, _currentSeat, frame.Id, frame.InstructionIndex - 1,
            CreateProgramTurnEffectSource(frame), query, operation, amount, cardKinds);
        AdvanceEventRulesAndQueueFact(new TurnRuleModifierGrantedEvent(granted));
    }

    private void GrantProgramTurnCardTargetRestriction(
        ProgramSkillFrame frame,
        SkillProgramCardTargetRestriction restriction,
        int targetSeat)
    {
        ValidateProgramTurnEffectGrant(frame);
        var targetScoped = restriction != SkillProgramCardTargetRestriction.SelfOnly;
        // A self-only restriction binds the character named by the instruction, which lets a skill
        // pin another character to self-targeting instead of only its own owner.
        if (!IsValidPlayerSeat(targetSeat) || targetScoped && targetSeat == frame.OwnerSeat)
            throw new InvalidOperationException("The turn card-target restriction has an invalid subject.");
        var granted = _turnCardUseEffects.GrantTargetRestriction(
            _turnNumber, _currentSeat, frame.Id, frame.InstructionIndex - 1,
            CreateProgramTurnEffectSource(frame), restriction, targetScoped ? targetSeat : null,
            targetScoped || targetSeat == frame.OwnerSeat ? null : targetSeat);
        AdvanceEventRulesAndQueueFact(new CardTargetRestrictionGrantedEvent(granted));
    }

    private void GrantProgramTurnCardConversion(
        ProgramSkillFrame frame,
        string sourceBind,
        SkillProgramCardColorRelation colorRelation,
        CardKind outputKind)
    {
        ValidateProgramTurnEffectGrant(frame);
        if (colorRelation != SkillProgramCardColorRelation.OppositeBoundCard ||
            outputKind != CardKind.Duel)
            throw new InvalidOperationException("The turn card conversion is unsupported.");
        var binding = GetProgramCardSet(frame, sourceBind);
        if (binding.CardIds.Count == 0) return;
        if (binding.CardIds.Count != 1 || binding.SourceLocations.Count != 1 ||
            _cardZones.GetLocation(binding.CardIds[0]) != binding.SourceLocations[0])
            throw new InvalidOperationException("A turn card conversion requires one stable bound card.");
        var card = _cardZones.CardsAt(binding.SourceLocations[0])
            .Single(item => item.Id == binding.CardIds[0]);
        var granted = _turnCardUseEffects.GrantConversion(
            _turnNumber,
            _currentSeat,
            frame.Id,
            frame.InstructionIndex - 1,
            CreateProgramTurnEffectSource(frame),
            sourceBind,
            colorRelation,
            IsRedSuit(EffectiveSuit(_players[frame.OwnerSeat], card)),
            outputKind);
        AdvanceEventRulesAndQueueFact(new CardConversionGrantedEvent(granted));
    }

    private void ValidateProgramTurnEffectGrant(ProgramSkillFrame frame)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id ||
            frame.OwnerSeat < 0 || frame.OwnerSeat >= _players.Count || _turnNumber <= 0 ||
            (frame.TriggerId is null && string.IsNullOrWhiteSpace(frame.ActivationId)))
            throw new InvalidOperationException("A turn effect requires an active program frame in an ongoing turn.");
    }

    private static CardUseEffectSource CreateProgramTurnEffectSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
}
