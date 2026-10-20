namespace CardGame.Core;

public sealed partial class GameEngine
{
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
