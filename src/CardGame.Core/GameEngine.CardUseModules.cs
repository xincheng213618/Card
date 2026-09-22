namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsCardUseForbidden(int actorSeat, CardKind effectiveKind, CardActionType actionType) =>
        _turnCardUseEffects.IsCardUseForbidden(
            _turnNumber,
            _currentSeat,
            actorSeat,
            effectiveKind,
            actionType);

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
            return !IsCardUseForbidden(actor.Seat, effectiveKind, CardActionType.Use);
        }).ToArray();

    private bool HasTurnCardTargetRestriction(
        int actorSeat,
        SkillProgramCardTargetRestriction restriction) =>
        _turnCardUseEffects.HasTargetRestriction(
            _turnNumber, _currentSeat, actorSeat, restriction);

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
        var expired = _turnCardUseEffects.ExpireTurn(turnNumber, turnSeat);
        if (expired.Count == 0) return;
        QueueGameEvent(new TurnCardUseEffectsExpiredEvent(turnNumber, turnSeat, expired));
    }

    private void GrantProgramTurnCardDamageModifier(
        ProgramSkillFrame frame,
        IReadOnlyList<CardKind> cardKinds,
        int amount)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.DrawPhaseStarting } ||
            frame.TriggerId is null || frame.OwnerSeat != _currentSeat || _phase != TurnPhase.Draw ||
            amount <= 0 || cardKinds.Count == 0)
        {
            throw new InvalidOperationException(
                "A turn card-damage modifier requires an active draw-phase program.");
        }

        var source = new CardUseEffectSource(
            frame.SkillId,
            frame.TriggerId,
            frame.OwnerSeat,
            frame.SkillInstanceId);
        var granted = _turnCardUseEffects.GrantDamageModifier(
            _turnNumber,
            _currentSeat,
            frame.Id,
            frame.InstructionIndex - 1,
            source,
            cardKinds,
            amount);
        QueueGameEvent(new CardDamageModifierGrantedEvent(granted));
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
        QueueGameEvent(new CardActionProhibitionGrantedEvent(granted));
    }

    private void GrantProgramTurnRuleModifier(
        ProgramSkillFrame frame,
        SkillRuleQuery query,
        SkillRuleOperation operation,
        int amount)
    {
        ValidateProgramTurnEffectGrant(frame);
        var valid = query == SkillRuleQuery.SlashLimit && operation == SkillRuleOperation.Add && amount > 0 ||
                    query == SkillRuleQuery.SlashDistanceLimit && operation == SkillRuleOperation.Unlimited && amount == 0;
        if (!valid) throw new InvalidOperationException("The turn rule modifier is unsupported.");
        var granted = _turnCardUseEffects.GrantRuleModifier(
            _turnNumber, _currentSeat, frame.Id, frame.InstructionIndex - 1,
            CreateProgramTurnEffectSource(frame), query, operation, amount);
        QueueGameEvent(new TurnRuleModifierGrantedEvent(granted));
    }

    private void GrantProgramTurnCardTargetRestriction(
        ProgramSkillFrame frame,
        SkillProgramCardTargetRestriction restriction)
    {
        ValidateProgramTurnEffectGrant(frame);
        if (restriction != SkillProgramCardTargetRestriction.SelfOnly)
            throw new InvalidOperationException("The turn card-target restriction is unsupported.");
        var granted = _turnCardUseEffects.GrantTargetRestriction(
            _turnNumber, _currentSeat, frame.Id, frame.InstructionIndex - 1,
            CreateProgramTurnEffectSource(frame), restriction);
        QueueGameEvent(new CardTargetRestrictionGrantedEvent(granted));
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
        QueueGameEvent(new CardConversionGrantedEvent(granted));
    }

    private void ValidateProgramTurnEffectGrant(ProgramSkillFrame frame)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame current || current.Id != frame.Id ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.DrawPhaseStarting } ||
            frame.TriggerId is null || frame.OwnerSeat != _currentSeat || _phase != TurnPhase.Draw)
            throw new InvalidOperationException("A turn effect requires an active draw-phase program.");
    }

    private static CardUseEffectSource CreateProgramTurnEffectSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, frame.TriggerId!, frame.OwnerSeat, frame.SkillInstanceId);
}
