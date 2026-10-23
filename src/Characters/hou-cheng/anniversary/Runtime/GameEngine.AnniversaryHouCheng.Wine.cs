namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidHouChengMovement(AnniversaryHouChengMovementEvent e)
    {
        if (e.Source.SkillId != AnniversaryHouChengComposition.Skill || e.Source.BindingId is not ("foreign-discard" or "after-damage") || e.Hash != _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash || e.Round < 1 || e.Round > _roundNumber || e.Before < 0 || e.After < e.Before || e.After > _movementSequence || e.BatchId <= 0 || e.ActualCount is < 0 or > 2 || !IsValidPlayerSeat(e.CardOwner) || !IsValidPlayerSeat(e.Recipient) ||
            (e.Giving ? e.CardOwner != e.Source.OwnerSeat || e.Recipient == e.Source.OwnerSeat : e.Recipient != e.Source.OwnerSeat) ||
            ProgramEventHistory<AnniversaryHouChengStartedEvent>().Count(s => s.FrameId == e.FrameId && s.Source == e.Source && s.Hash == e.Hash && s.Round == e.Round && s.Turn == e.Turn && s.TurnOwner == e.TurnOwner) != 1 ||
            ProgramEventHistory<AnniversaryHouChengMovementEvent>().Count(m => m.FrameId == e.FrameId && m.Giving == e.Giving) != 1) return false;
        var moves = _cardMovements.Where(m => m.Sequence > e.Before && m.Sequence <= e.After && m.Reason.Value == (e.Giving ? HouChengGiveReason : HouChengObtainReason)).ToArray();
        return moves.Length is >= 1 and <= 2 && moves.Select(m => m.CardId).Distinct().Count() == moves.Length && moves.All(m => m.TurnNumber == e.Turn && m.From.OwnerSeat == e.CardOwner && m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
            (m.To == CardLocation.Hand(e.Recipient) || m.To == CardLocation.OutsideGame && GetAdvancedCard(m.CardId).IsGeneralWeapon || IsHappyWeiYanRedirectedGain(m, CardLocation.Hand(e.Recipient)))) &&
            moves.Count(m => m.To == CardLocation.Hand(e.Recipient) && m.From != m.To) == e.ActualCount;
    }
    // Its exact completed movement marks the entity. Later transfers never revoke the mark.
    private AnniversaryHouChengMovementEvent? HouChengWineMark(int cardId) => !HasHouChengContent || cardId <= 0 || !MatchesSkillProgramCardCategory(GetAdvancedCard(cardId).Kind, SkillProgramCardCategory.Basic) ? null :
        ProgramEventHistory<AnniversaryHouChengMovementEvent>().LastOrDefault(e => ValidHouChengMovement(e) && _cardMovements.Any(m => m.CardId == cardId && m.Sequence > e.Before && m.Sequence <= e.After && m.To == CardLocation.Hand(e.Recipient) && m.From != m.To && m.Reason.Value == (e.Giving ? HouChengGiveReason : HouChengObtainReason)));
    private bool IsAnniversaryHouChengWineCard(int cardId) => HouChengWineMark(cardId) is not null;
    private IReadOnlyList<CardConversionSource> AnniversaryHouChengWineSources(CharacterState owner, Card card, CardKind kind, bool forResponse, bool dyingUse)
    {
        if (kind != CardKind.Alcohol || forResponse && (!dyingUse || ActiveDying?.VictimSeat != owner.Seat) || HouChengWineMark(card.Id) is not { } mark ||
            !GetPlayableCards(owner).Any(c => c.Id == card.Id) || IsTurnHandCardRestricted(owner, card) || IsResponseEntityRestricted(owner.Seat, card.Id) || IsProgramEntityPhysicalRestricted(owner.Seat, card.Id) ||
            IsProgramMaterialUseForbidden(owner.Seat, kind, [card.Id], forResponse && !dyingUse) || !IsProgramNativeResponseAllowed(owner, kind, [card.Id], dyingUse: dyingUse)) return [];
        return [mark.Source];
    }
    private bool HouChengUseBanned(int actor, CardKind kind, IEnumerable<int> ids, bool response, bool dying = false) => kind != CardKind.Alcohol && (!response || dying || kind == CardKind.Nullification || IsProgramResponseCardUse(_players[actor], kind) || IsSlashCard(kind) &&
        (IsJuewuTrueSlashUse(_players[actor]) || ActiveFactionCardRequest is { AwaitingProviders: true } request && request.CurrentCandidateSeat == actor && IsFactionSlashUse(request))) && ids.Any(IsAnniversaryHouChengWineCard);
    // IronChain conversion enumeration also supplies native Recast. Let that
    // enumeration proceed; the final action and submitted-Use guards below still
    // reject every real non-Wine Use, including converted IronChain.
    [ProgramCardLegality(ProgramCardLegalityStage.MaterialUseBan, 47100)] private bool HouChengMaterialBan(ProgramCardMaterialQuery q) => q.Kind != CardKind.IronChain && HouChengUseBanned(q.ActorSeat, q.Kind, q.CardIds, q.IsResponse);
    [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan, 47100)] private bool HouChengResponseBan(ProgramNativeResponseQuery q) => HouChengUseBanned(q.Provider.Seat, q.Kind, q.CardIds, true, q.DyingUse);
    [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan, 47100)] private bool HouChengViewAsBan(ProgramViewAsLegalityQuery q) => q.Kind != CardKind.IronChain && HouChengUseBanned(q.Owner.Seat, q.Kind, q.CardIds, q.ForResponse && q.RequestedUseTarget is null, q.DyingUse);
    [ProgramCardLegality(ProgramCardLegalityStage.AfterLegalActionKind, 47100)] private bool HouChengLegalUseBan(ProgramLegalActionQuery q) => q.Action.Kind is not (LegalActionKind.Recast or LegalActionKind.UseProgramSkill or LegalActionKind.EndPlay or LegalActionKind.UseEquipmentEffect or LegalActionKind.RevealGeneral) && q.Action.CardId is { } id && HouChengUseBanned(q.Actor.Seat, q.Action.PlayedCardKind ?? GetAdvancedCard(id).Kind, [id], false);
    [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation, 47100)] private void HouChengSubmittedBan(ProgramSubmittedUseQuery q)
    { if (HouChengUseBanned(q.ActorSeat, q.Kind, q.CardIds, false)) throw new InvalidOperationException("Xianniang marked Basic entities may only participate in actual Alcohol uses."); }
    [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 47100)]
    private void AddHouChengWineActions(List<LegalAction> actions, CharacterState actor, IReadOnlyList<Card> playable, int? selectedPhysicalCardId = null)
    {
        if (!HasHouChengContent || !actor.HasHpValue || actor.HasAlcoholEffect) return;
        foreach (var card in playable.Where(c => selectedPhysicalCardId is null || selectedPhysicalCardId == c.Id))
        foreach (var source in AnniversaryHouChengWineSources(actor, card, CardKind.Alcohol, false, false))
        {
            // Ordinary native Wine/conversion enumeration already supplies this
            // exact action. Only an existing whole-action identity needs a bridge.
            if (!HasProgramCardIdentity(actor, card)) continue;
            if (!(CanUseJianjiOrdinaryAlcohol(actor, card.Id) || HasTargetCardQuotaAllowance(actor.Seat, actor.Seat) || HasNextUnlimitedCard(actor) || HasMouWujingAllowance(actor) || HasFriendQihuiAllowance(actor) || HasCardPolicy(actor, SkillProgramCardPolicyKind.UnlimitedAlcoholUse, CardKind.Alcohol) || HasRoundGainedBasicBonus(actor, CardKind.Alcohol, [card.Id]) || HasWuHfOrDixianMaterialPrivilege(actor, CardKind.Alcohol, [card.Id], source)) || IsCardUseForbidden(actor.Seat, CardKind.Alcohol, CardActionType.Use)) continue;
            actions.Add(new(LegalActionKind.Alcohol, card.Id, null, $"献酿：将【{card.DisplayName}】当【酒】使用", CardKind.Alcohol) { ConversionSource = source });
        }
    }
}
