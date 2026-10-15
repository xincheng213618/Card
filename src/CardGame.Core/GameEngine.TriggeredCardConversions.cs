namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanUseTriggeredSlashMaterial(int ownerSeat, int targetSeat, Card card, CardLocation location,
        CardConversionSource source) =>
        IsValidPlayerSeat(ownerSeat) && IsValidPlayerSeat(targetSeat) && ownerSeat != targetSeat &&
        _players[ownerSeat].IsAlive && _players[targetSeat].IsAlive &&
        location.OwnerSeat == ownerSeat && location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
        _cardZones.GetLocation(card.Id) == location &&
        !IsTurnPhysicalUseForbidden(ownerSeat, [card.Id]) &&
        (location.Zone != CardZoneKind.Hand || !IsTurnHandCardRestricted(_players[ownerSeat], card)) &&
        !HasTurnCardTargetRestriction(ownerSeat, SkillProgramCardTargetRestriction.SelfOnly) &&
        CanUseSlashTarget(_players[ownerSeat], _players[targetSeat], card, source, CardKind.Slash,
            physicalCardIds: [card.Id], excludedEquipmentId: location.Zone == CardZoneKind.Equipment ? card.Id : null) &&
        CanSpendSlashUse(_players[ownerSeat], _players[targetSeat], false, CardKind.Slash, card,
            excludedEquipmentId: location.Zone == CardZoneKind.Equipment ? card.Id : null, physicalCardIds: [card.Id]);

    private bool MatchesTriggeredSlashMaterial(ProgramSkillFrame frame, string bind, int ownerSeat, Card card, CardLocation location)
    {
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        if (!plan.Instructions.Any(effect => effect.Op == SkillProgramEffectOp.UseBoundCardsAsSlash && effect.SourceBind == bind)) return true;
        var selection = plan.Instructions[0];
        return ownerSeat == frame.OwnerSeat && frame.TriggerId is not null &&
            frame.WindowContext is { Window: SkillProgramTriggerWindow.TurnEnding, TargetSeat: { } target } &&
            TriggeredSlashSelectionAccepts(_players[ownerSeat], selection, card, location) &&
            CanUseTriggeredSlashMaterial(ownerSeat, target, card, location,
                new(frame.SkillId, GetProgramBindingId(frame), ownerSeat, frame.SkillInstanceId));
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.AfterInitialPayments, 50, SkillProgramEffectOp.UseBoundCardsAsSlash)]
    private bool CanOfferTriggeredSlashConversion(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        var consumer = trigger.Effects.FirstOrDefault(effect => effect.Op == SkillProgramEffectOp.UseBoundCardsAsSlash);
        if (consumer is null) return true;
        if (context is not { Window: SkillProgramTriggerWindow.TurnEnding, TargetSeat: { } target }) return false;
        var selection = trigger.Effects.Single(effect => effect.Op == SkillProgramEffectOp.SelectOwnedCards && effect.ResultBind == consumer.SourceBind);
        var owner = _players[candidate.OwnerSeat];
        var source = new CardConversionSource(candidate.SkillId, candidate.BindingId, owner.Seat, candidate.SkillInstanceId);
        return selection.Zones.Any(zone => _cardZones.CardsAt(new(zone, owner.Seat)).Any(card =>
            TriggeredSlashSelectionAccepts(owner, selection, card, new(zone, owner.Seat)) &&
            CanUseTriggeredSlashMaterial(owner.Seat, target, card, new(zone, owner.Seat), source)));
    }

    private bool TriggeredSlashSelectionAccepts(CharacterState owner, SkillProgramEffect selection, Card card, CardLocation location) =>
        selection.Zones.Contains(location.Zone) &&
        (selection.Suits.Count == 0 || selection.Suits.Contains(GetProgramEffectiveSuit(owner, card))) &&
        (selection.CardKinds.Count == 0 || selection.CardKinds.Contains(card.Kind));

    private SkillProgramStepOutcome UseTriggeredBoundCardsAsSlash(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.TriggerId is null || frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding, TargetSeat: { } target } ||
            _resolutionStack.LastOrDefault()?.Id != frame.Id)
            throw new InvalidOperationException("A triggered physical Slash requires its owning ending-phase program.");
        var binding = GetProgramCardSet(frame, effect.SourceBind!);
        if (binding.CardIds is not [var cardId] || binding.SourceLocations is not [var location])
            throw new InvalidOperationException("A triggered physical Slash requires exactly one owned bound entity.");
        var card = GetAttackCard(cardId);
        if (!HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            !MatchesTriggeredSlashMaterial(frame, effect.SourceBind!, frame.OwnerSeat, card, location))
        {
            CancelProgramBindingAndCleanup(frame, "转化的实体牌、技能或目标已失效，未支付实体牌。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var source = new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
        ResolveSlashCore(_players[frame.OwnerSeat], _players[target], card, CardKind.Slash, frame.OwnerSeat,
            physicalCards: [card], countsTowardSlashLimit: false, conversionSource: source, programSkillCardUseFrameId: frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    [ProgramCapability]
    private sealed class TriggeredCardConversionCapability(GameEngine engine) : ITriggeredCardConversionHost
    {
        public SkillProgramStepOutcome UseBoundCardsAsSlash(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.UseTriggeredBoundCardsAsSlash(frame, effect);
    }
}
