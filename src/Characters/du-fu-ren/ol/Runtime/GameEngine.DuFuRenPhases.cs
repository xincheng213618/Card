namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static SkillRuleQuery? DuFuRenPhaseQuery(ActualDiscardRecoveryPhaseKind kind) => kind switch
    { ActualDiscardRecoveryPhaseKind.Draw => SkillRuleQuery.DrawCount, ActualDiscardRecoveryPhaseKind.Play => SkillRuleQuery.SlashLimit,
        ActualDiscardRecoveryPhaseKind.Discard => SkillRuleQuery.HandLimit, _ => null };
    private bool DuFuRenGrantConsumed(long id, ActualDiscardRecoveryPhaseKind kind) => ProgramEventHistory<DuFuRenPhaseIssuedEvent>()
        .Any(e => e.GrantId == id && e.Phase.Kind == kind);
    private void ConsumeDuFuRenPhase(ActualDiscardRecoveryPhaseKind kind, int ownerSeat)
    {
        if (!HasDuFuRenShunshi || DuFuRenPhaseQuery(kind) is null) return;
        if (CurrentActualDiscardRecoveryPhase() is not { } phase || phase.Kind != kind || phase.ActorSeat != ownerSeat || !IsRecordedActualDiscardRecoveryPhase(phase))
            throw new InvalidOperationException("Shunshi can only consume the current confirmed actual phase token: "+System.Text.Json.JsonSerializer.Serialize(new
            { Requested=kind,OwnerSeat=ownerSeat,Alive=IsValidPlayerSeat(ownerSeat)&&_players[ownerSeat].IsAlive,Winner=_winner,Status=_status,Phase=_phase,Turn=_turnNumber,
                ActualOwner=_turnProgression.OwnerSeat,CurrentSeat=_currentSeat,LastActual=CompleteProgramEventHistory().Where(e=>e is ActualDiscardRecoveryPhaseStartedEvent or ActualDiscardRecoveryPhaseRestoredEvent or ActualDiscardRecoveryPhaseClearedEvent)
                    .TakeLast(5).Select(e=>new{Type=e.GetType().Name,Payload=System.Text.Json.JsonSerializer.Serialize(e,e.GetType())}).ToArray() }));
        if (ProgramEventHistory<DuFuRenPhaseConfirmedEvent>().Any(e => e.Phase == phase)) return;
        AdvanceEventRulesAndQueueFact(new DuFuRenPhaseConfirmedEvent(phase));
        foreach (var gift in ProgramEventHistory<DuFuRenGiftIssuedEvent>().Where(e => e.Source.OwnerSeat == ownerSeat && !DuFuRenGrantConsumed(e.FrameId, kind)).ToArray())
            AdvanceEventRulesAndQueueFact(new DuFuRenPhaseIssuedEvent(gift.FrameId, phase));
    }
    private int DuFuRenPhaseBonus(int ownerSeat, SkillRuleQuery query)
    {
        if (!HasDuFuRenShunshi || !_players[ownerSeat].IsAlive || CurrentActualDiscardRecoveryPhase() is not { } phase || phase.ActorSeat != ownerSeat || DuFuRenPhaseQuery(phase.Kind) != query) return 0;
        if (ProgramEventHistory<DuFuRenPhaseConfirmedEvent>().Any(e => e.Phase == phase))
            return ProgramEventHistory<DuFuRenPhaseIssuedEvent>().Count(e => e.Phase == phase);
        // A native Draw-starting instruction can freeze this count before the
        // ordinary draw is confirmed. Shunshi grants only its owner, and actual
        // own-turn damage cannot create another off-turn gift in this window.
        return query == SkillRuleQuery.DrawCount ? ProgramEventHistory<DuFuRenGiftIssuedEvent>().Count(e => e.Source.OwnerSeat == ownerSeat && !DuFuRenGrantConsumed(e.FrameId, phase.Kind)) : 0;
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 26521)]
    private void ConfirmDuFuRenPlayPhase(IGameEvent payload)
    {
        if (HasDuFuRenShunshi && payload is ActualDiscardRecoveryPhaseStartedEvent { Phase.Kind: ActualDiscardRecoveryPhaseKind.Play } e)
            ConsumeDuFuRenPhase(e.Phase.Kind,e.Phase.ActorSeat);
    }
}
