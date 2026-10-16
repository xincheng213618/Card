namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsXiangChongGuyingEquipmentRide(ResolutionFrame ride, ResolutionFrame beneath)
    {
        if (ride is not CardUseFrame { XiangChongGuyingEquipment: { } origin } use ||
            beneath is not ProgramSkillFrame parent || !MatchesXiangChongEquipment(parent, origin)) return false;
        ValidateXiangChongGuyingEquipment(use);
        return true;
    }

    private bool CanIssueXiangChongGuyingEquipment(ProgramSkillFrame frame, int cardId, CardLocation from)
    {
        if (frame.XiangChong is not { Lost: { } lost, LossDestination: { } destination } ||
            lost.CardId != cardId || destination != from || !ValidXiang(frame) || !XiangLostStillAvailable(frame) ||
            !_players[frame.OwnerSeat].IsAlive || _winner != Winner.None ||
            from == CardLocation.OutsideGame || _cardZones.GetLocation(cardId) != from ||
            !EquipmentCatalog.IsEquipment(lost.Kind)) return false;
        var owner = _players[frame.OwnerSeat];
        return owner.EquipmentSlotCapacity(EquipmentCatalog.Get(lost.Kind).Slot) > 0 &&
            !IsCardUseForbidden(owner.Seat, lost.Kind, CardActionType.Use) &&
            !IsTurnPhysicalUseForbidden(owner.Seat, [cardId]);
    }

    private void BeginXiangChongGuyingEquipment(ProgramSkillFrame frame, XiangChongEquipmentOrigin origin)
    {
        if (!MatchesXiangChongEquipment(frame, origin) ||
            !CanIssueXiangChongGuyingEquipment(frame, origin.CardId, origin.From) ||
            origin.CardUseFrameId != _resolutionSequence + 1)
            throw new InvalidOperationException("Guying equipment requires its exact still-available original entity and native issuer.");
        var card = GetAdvancedCard(origin.CardId);
        var id = BeginCardUse(card, origin.ActorSeat, [], physicalCardIds: [card.Id]);
        if (id != origin.CardUseFrameId)
            throw new InvalidOperationException("Guying equipment lost its exact native Use identity.");
        UpdateLifecycleCardUse(id, use => use with { XiangChongGuyingEquipment = origin });
        RecordXiangChongEquipmentMaterialClaim(frame, origin);
        if (origin.From != CardLocation.Processing)
            MoveCard(card, origin.From, CardLocation.Processing, CardMoveReasons.EquipmentUse);
        if (!TryBeginEquipmentTargetPrograms(_players[origin.ActorSeat], card, id))
            CompleteEquipmentUse(_players[origin.ActorSeat], card, id);
    }

    [NativeUseValidation(27140)]
    private void ValidateXiangChongGuyingEquipment(CardUseFrame use)
    {
        if (use.XiangChongGuyingEquipment is not { } origin) return;
        if (use.Id != origin.CardUseFrameId || use.CardId != origin.CardId || use.CardKind != origin.CardKind ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards: [var cost] } action ||
            action.EffectiveKind != origin.CardKind || cost.CardId != origin.CardId ||
            cost.CardKind != origin.CardKind || cost.From != origin.From ||
            !ShownEntityUseActorMatches(use, origin.ActorSeat, origin.ActorSeat) ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(frame => frame.Id == origin.ProgramFrameId) is not { } parent ||
            !MatchesXiangChongEquipment(parent, origin))
            throw new InvalidOperationException("Guying equipment lost its original material, actor or owning return.");
    }
}
