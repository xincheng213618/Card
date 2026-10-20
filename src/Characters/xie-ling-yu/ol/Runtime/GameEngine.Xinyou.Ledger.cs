namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ActualDiscardRecoveryPhaseKey? XinyouActualPlay(int owner) => CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p && p.ActorSeat == owner && p.TurnNumber == _turnNumber && IsRecordedActualDiscardRecoveryPhase(p) ? p : null;
    private bool XinyouPhaseSpent(int owner, ActualDiscardRecoveryPhaseKey phase) => ProgramEventHistory<XinyouPaidEvent>().Any(e => e.OwnerSeat == owner && e.PhaseToken == phase.Token);
    private IReadOnlyList<Card> XinyouCostCards(int owner) => GetSelfDiscardableOwnedHand(_players[owner]).Where(c => GetProgramCardCategory(AdvancedEffectiveHandKind(_players[owner], c)) == SkillProgramCardCategory.Basic).ToArray();
    private bool IsXinyouHandLimitExempt(int ownerSeat, int cardId) => HasXieLingYuCapability && ProgramEventHistory<XinyouGainedEntityEvent>().Any(e => e.OwnerSeat == ownerSeat && e.CardId == cardId && e.ActualTurn == _turnNumber && e.TurnOwner == _turnProgression.OwnerSeat) && _cardZones.GetLocation(cardId) == CardLocation.Hand(ownerSeat);
    private bool HasXinyouCardMark(int ownerSeat, int cardId) => IsXinyouHandLimitExempt(ownerSeat, cardId);
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 27582, SkillProgramEffectOp.ResolveXinyouBasicCycle)]
    private bool CanActivateXinyou(ProgramActivationLegalityContext c) => c.Owner.IsAlive && _phase == TurnPhase.Play && c.OwnerSeat == _currentSeat && XinyouActualPlay(c.OwnerSeat) is { } phase && !XinyouPhaseSpent(c.OwnerSeat, phase) && XinyouCostCards(c.OwnerSeat).Count > 0;
}
