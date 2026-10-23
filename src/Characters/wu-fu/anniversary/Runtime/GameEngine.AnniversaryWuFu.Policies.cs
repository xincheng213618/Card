namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool WuFuPhaseOpen(ActualDiscardRecoveryPhaseKey phase) => phase.Kind == ActualDiscardRecoveryPhaseKind.Play && CurrentActualDiscardRecoveryPhase() == phase &&
        IsRecordedActualDiscardRecoveryPhase(phase) && !ProgramEventHistory<ActualPhaseEndedEvent>().Any(e => e.Phase == phase);
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 46601)] private void CaptureWuFuPhaseGain(IGameEvent fact)
    {
        if (!_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveAnniversaryWuFuJuekai) || fact is not CardMovedEvent { To.Zone: CardZoneKind.Hand, To.OwnerSeat: { } seat } moved ||
            CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || !WuFuPhaseOpen(phase) ||
            ProgramEventHistory<AnniversaryWuFuPhaseGainEvent>().Any(e => e.Phase == phase && e.Recipient == seat)) return;
        var movement = _cardMovements.LastOrDefault(m => m.CardId == moved.CardId && m.From == moved.From && m.To == moved.To && m.Reason == moved.Reason);
        if (movement is null || ActualHandGainOriginalSource(movement) == CardLocation.Hand(seat)) return;
        AdvanceEventRulesAndQueueFact(new AnniversaryWuFuPhaseGainEvent(phase, seat, movement.Sequence));
    }
    private bool IsAnniversaryWuFuHandUseForbidden(int seat, IReadOnlyList<int> ids) =>
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveAnniversaryWuFuJuekai) &&
        ProgramEventHistory<AnniversaryWuFuJuekaiEvent>().Any(e => WuFuPhaseOpen(e.Phase) && ProgramEventHistory<AnniversaryWuFuPhaseGainEvent>().Any(g => g.Phase == e.Phase && g.Recipient == seat)) &&
        ids.Any(id => id > 0 && _cardZones.GetLocation(id) == CardLocation.Hand(seat));
    private bool WuFuResponseIsUse(int actor, CardKind kind, bool dying = false) => dying || kind == CardKind.Nullification || IsProgramResponseCardUse(_players[actor], kind);
    [ProgramCardLegality(ProgramCardLegalityStage.MaterialUseBan, 46601)] private bool WuFuMaterialBan(ProgramCardMaterialQuery q) =>
        (!q.IsResponse || WuFuResponseIsUse(q.ActorSeat, q.Kind)) && IsAnniversaryWuFuHandUseForbidden(q.ActorSeat, q.CardIds.ToArray());
    [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan, 46601)] private bool WuFuNativeResponseBan(ProgramNativeResponseQuery q) =>
        WuFuResponseIsUse(q.Provider.Seat, q.Kind, q.DyingUse) && IsAnniversaryWuFuHandUseForbidden(q.Provider.Seat, q.CardIds);
    [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan, 46601)] private bool WuFuViewAsBan(ProgramViewAsLegalityQuery q) =>
        (!q.ForResponse || q.RequestedUseTarget is not null || WuFuResponseIsUse(q.Owner.Seat, q.Kind, q.DyingUse)) && IsAnniversaryWuFuHandUseForbidden(q.Owner.Seat, q.CardIds);
    [ProgramCardLegality(ProgramCardLegalityStage.AfterLegalActionKind, 46601)] private bool WuFuLegalUseBan(ProgramLegalActionQuery q) =>
        q.Action.Kind is not (LegalActionKind.Recast or LegalActionKind.UseProgramSkill or LegalActionKind.EndPlay or LegalActionKind.UseEquipmentEffect) &&
        q.Action.CardId is { } id && IsAnniversaryWuFuHandUseForbidden(q.Actor.Seat, [id]);
    [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation, 46601)] private void WuFuSubmittedUseBan(ProgramSubmittedUseQuery q)
    { if (IsAnniversaryWuFuHandUseForbidden(q.ActorSeat, q.CardIds)) throw new InvalidOperationException("Juekai prohibits Hand use by actual recipients until this actual Play phase ends."); }
}
