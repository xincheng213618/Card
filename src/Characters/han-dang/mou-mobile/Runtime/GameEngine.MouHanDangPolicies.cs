namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidMouHanDang(ProgramSkillFrame f)
    {
        if (f.MouHanDang is not { } r || r.Source != MouHanDangSource(f) || r.GameplayHash != f.GameplayHash || f.InstructionIndex != 1 ||
            !Enum.IsDefined(r.Stage) || r.Phase.Kind != ActualDiscardRecoveryPhaseKind.Play || r.Phase.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(r.Phase) ||
            r.Before < 0 || r.Before > r.After || r.After > _movementSequence || r.Index < 0 || r.Index > r.Participants.Count ||
            r.Participants is not System.Collections.IList { IsReadOnly: true } || r.Participants.Distinct().Count() != r.Participants.Count ||
            r.Participants.Any(s => !IsValidPlayerSeat(s) || s == r.TargetSeat) ||
            ProgramEventHistory<MouHanDangStartedEvent>().Count(e => e == new MouHanDangStartedEvent(f.Id, r.Source, r.GameplayHash, r.Phase, r.Gongqi, r.TargetSeat)) != 1) return false;
        if (r.Gongqi)
        {
            if (f.SkillId != MouHanDangGongqi || !MouHanDangPlayStartParent(f) || r.TargetSeat != f.OwnerSeat || r.Participants.Count != 0 || r.Branch is not null ||
                r.Stage is not (MouHanDangStage.CostChoice or MouHanDangStage.Paying or MouHanDangStage.DiscardChildren)) return false;
        }
        else
        {
            if (f.SkillId != MouHanDangJiefan || f.TriggerId is not null || f.WindowContext is not null || f.ActivationId != "aid-by-range" || f.SelectedTargetSeats is not [var target] || target != r.TargetSeat ||
                !IsValidPlayerSeat(target) || r.Stage == MouHanDangStage.CostChoice) return false;
            var branches = ProgramEventHistory<MouHanDangBranchEvent>().Where(e => e.FrameId == f.Id).ToArray();
            if (r.Branch is null) return r.Stage == MouHanDangStage.ModeChoice && branches.Length == 0 && r.Participants.Count == 0 && r.Draw is null;
            if (r.Branch is not ("discard" or "draw" or "backwater") || branches is not [var branch] || branch != new MouHanDangBranchEvent(f.Id, r.TargetSeat, r.Branch) ||
                r.Stage == MouHanDangStage.ModeChoice || r.Participants.Where((s, i) => ProgramEventHistory<MouHanDangParticipantEvent>().Count(e => e == new MouHanDangParticipantEvent(f.Id, i, s)) != 1).Any()) return false;
        }
        if (r.PaidCardId is { } id)
        {
            var seat = r.Gongqi ? f.OwnerSeat : r.Index < r.Participants.Count ? r.Participants[r.Index] : -1;
            if (seat < 0 || r.Stage != MouHanDangStage.DiscardChildren ||
                ProgramEventHistory<MouHanDangDiscardedEvent>().Where(e => e.FrameId == f.Id && e.Index == r.Index && e.Sequence == r.PaymentSequence).ToArray() is not [var cost] ||
                cost.Seat != seat || cost.CardId != id || cost.EffectiveSuit != r.PaidSuit || cost.From.OwnerSeat != seat || cost.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                !_cardMovements.Any(m => m.Sequence == cost.Sequence && m.CardId == id && m.From == cost.From && m.To == cost.To && m.Reason.Value == MouHanDangDiscardReason)) return false;
        }
        else if (r.PaymentSequence != 0 || r.Gongqi && r.Stage is not (MouHanDangStage.CostChoice or MouHanDangStage.Paying)) return false;
        return r.Stage == MouHanDangStage.DrawChildren ? r.Draw is { } d && d.RecipientSeat == r.TargetSeat && d.RequestedCount == r.RequestedDraw &&
            ProgramEventHistory<MouHanDangDrawReturnedEvent>().Count(e => e == new MouHanDangDrawReturnedEvent(f.Id, r.TargetSeat, d.RequestedCount, d.ActualDrawIds.Count, d.ReplacementGainedIds.Count, d.AttemptFrameId)) == 1 : r.Draw is null;
    }
    private IEnumerable<MouHanDangGongqiGrantedEvent> MouHanDangActionRestrictions(long action) =>
        ProgramEventHistory<MouHanDangUseRestrictedEvent>().Where(e => e.ActionId == action).SelectMany(e => ProgramEventHistory<MouHanDangGongqiGrantedEvent>().Where(g => g.FrameId == e.GrantFrameId));
    private CardActionContext? CurrentMouHanDangRespondedAction()
    {
        var use = _resolutionStack.OfType<CardUseFrame>().LastOrDefault();
        if (ActiveNullificationWindow is { ChainDepth: > 0 } n)
        {
            var original = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Id == n.ParentFrameId)?.Action;
            return ProgramEventHistory<CardActionAcceptedEvent>().Select(e => e.Action).LastOrDefault(a => a.Type == CardActionType.Response && a.EffectiveKind == CardKind.Nullification && a.ParentActionId == original?.ActionId) ?? original;
        }
        return use?.Action;
    }
    private bool IsMouHanDangNativeResponseAllowed(CharacterState provider, Card card, bool dyingUse = false)
    {
        if (!HasMouHanDangCapability || dyingUse || CurrentMouHanDangRespondedAction() is not { } original || original.ActorSeat == provider.Seat) return true;
        var grants = MouHanDangActionRestrictions(original.ActionId).ToArray();
        return grants.Length == 0 || _cardZones.GetLocation(card.Id) == CardLocation.Hand(provider.Seat) &&
            CapturePhysicalCardColor(provider.Seat, card) is { } red && grants.All(g => g.Suit is Suit.Heart or Suit.Diamond or Suit.Spade or Suit.Club && IsRedSuit(g.Suit) == red);
    }
    // The physical-card frontier may also represent one or more genuinely
    // virtual choices. Keep those materials available, then filter only the
    // native choice at its actual published source/commit boundary.
    private bool IsMouHanDangResponseAvailable(CharacterState provider, CardKind kind, IReadOnlyList<int> ids, bool dyingUse = false) =>
        !HasMouHanDangCapability || dyingUse || ids.Count != 1 || IsMouHanDangNativeResponseAllowed(provider, GetAdvancedCard(ids[0])) ||
        GetProgramCardIdentitySources(provider, GetAdvancedCard(ids[0]), kind, true).Count != 0 ||
        GetProgramViewAsConversions(provider, GetAdvancedCard(ids[0]), kind, true).Count != 0;
    private bool IsMouHanDangPublishedResponseAllowed(CharacterState provider, Card card, CardConversionSource? source, bool forResponse, bool dyingUse = false) =>
        !forResponse || dyingUse || source is not null || IsMouHanDangNativeResponseAllowed(provider, card);
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 30700)]
    private void ObserveMouHanDang(IGameEvent payload)
    {
        if (!HasMouHanDangCapability) return;
        if (payload is PlayerDiedEvent { KillerSeat: { } killer } death && killer != death.VictimSeat && MouHanDangJiefanDisabled(killer))
        {
            AdvanceEventRulesAndQueueFact(new MouHanDangJiefanRestoredEvent(killer, death.VictimSeat));
            AddLog("SkillTriggered", $"{_players[killer].Name} 击杀角色，【解烦】恢复。", killer);
        }
        // Native ordinary tricks may have no committed/finalized subscriber,
        // so their real Use declaration is the authoritative owning entry even
        // when no CardActionAccepted notification is needed on that path.
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { Action: { Type: CardActionType.Use } native } use &&
            use.SourceSeat == declared.SourceSeat && native.ActorSeat == declared.SourceSeat && use.CardId == declared.CardId && use.CardKind == declared.CardKind)
            SignMouHanDangUseRestrictions(native);
        if (payload is not CardActionAcceptedEvent { Action: { } a }) return;
        if (a.Type == CardActionType.Response && ActualRespondedAction(a) is { } addressed && addressed.ActorSeat != a.ActorSeat && a.ConversionChain.Count == 0 && a.PhysicalCards.Count == 1)
        {
            var grants = MouHanDangActionRestrictions(addressed.ActionId).ToArray();
            if (grants.Length > 0 && (a.PhysicalCards is not [var cost] || cost.From != CardLocation.Hand(a.ProviderSeat) ||
                cost.EffectiveIsRed is not { } color || grants.Any(g => g.Suit is not (Suit.Heart or Suit.Diamond or Suit.Spade or Suit.Club) || IsRedSuit(g.Suit) != color)))
                throw new InvalidOperationException("Gongqi only accepts genuine virtual cards or the matching-color original Hand response.");
        }
        SignMouHanDangUseRestrictions(a);
    }
    private void SignMouHanDangUseRestrictions(CardActionContext a)
    {
        var isUse = a.Type == CardActionType.Use || a.Type == CardActionType.Response && a.ActorSeat == a.ProviderSeat && a.RequesterSeat is null &&
            (a.EffectiveKind == CardKind.Nullification || IsProgramResponseCardUse(_players[a.ActorSeat], a.EffectiveKind));
        if (!isUse || CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != a.ActorSeat) return;
        foreach (var grant in ProgramEventHistory<MouHanDangGongqiGrantedEvent>().Where(g => g.Source.OwnerSeat == a.ActorSeat && g.Phase == phase).ToArray())
            if (!ProgramEventHistory<MouHanDangUseRestrictedEvent>().Any(e => e.ActionId == a.ActionId && e.GrantFrameId == grant.FrameId))
                AdvanceEventRulesAndQueueFact(new MouHanDangUseRestrictedEvent(a.ActionId, a.ActorSeat, grant.FrameId));
    }
}
