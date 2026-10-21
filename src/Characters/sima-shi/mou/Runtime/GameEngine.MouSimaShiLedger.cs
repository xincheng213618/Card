namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramPipelineHook(ProgramPipelinePhase.CardEntitySkillLabel, 36400)] private string? MouSimaShiCardLabel(int cardId) => HasMouSimaShi && ProgramEventHistory<MouSimaShiMarkedEvent>().Any(e => e.CardId == cardId) ? "死士" : null;
    private CardConversionSource? MouSimaShiNoResponseSource(CharacterState actor, IReadOnlyList<int> ids) => HasMouSimaShi && actor.IsAlive && HasRuntimeSkill(actor, MouSimaShiRuntime.Sanshi) && ids.Count > 0 && MouSimaShiRules.HasMarked(actor.Seat, ids) ? new(MouSimaShiRuntime.Sanshi, "dead-soldier-no-response", actor.Seat, GetRuntimeSkillInstanceId(actor, MouSimaShiRuntime.Sanshi)) : null;
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 36400)] private void ObserveMouSimaShiAction(IGameEvent payload)
    {
        if (!HasMouSimaShi) return;
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { Action: { Type: CardActionType.Use } a } use && MouSimaShiNoResponseSource(_players[a.ActorSeat], a.PhysicalCards.Select(c => c.CardId).ToArray()) is { } source && use.IssuedNoResponse is null)
        {
            var issued = new IssuedCardNoResponse(a.ActionId, use.Id, source, _turnNumber, _cardUseDebitPhaseInstanceId); UpdateLifecycleCardUse(use.Id, u => u with { IssuedNoResponse = issued }); AdvanceEventRulesAndQueueFact(new IssuedCardNoResponseEvent(issued));
        }
        if (payload is NativeTrueUseProcessingEntryEvent { Kind: NativeTrueUseDiscardKind.Use } native && LifecycleCardUse(native.ParentFrameId) is { Action: { Type: CardActionType.Use } nativeAction } && nativeAction.ActionId == native.ParentActionId && nativeAction.ProviderSeat == native.ProviderSeat && nativeAction.PhysicalCards.Any(c => c.CardId == native.CardId && c.CardKind == native.CardKind && c.From == native.From) && !ProgramEventHistory<MouSimaShiMaterialEvent>().Any(e => e.ActionId == nativeAction.ActionId && e.CardId == native.CardId))
            AdvanceEventRulesAndQueueFact(new MouSimaShiMaterialEvent(nativeAction.ActionId, nativeAction.ActorSeat, native.ParentFrameId, native.CardId, native.CardKind, native.From, native.EntrySequence, 0));
        if (payload is not CardActionAcceptedEvent { Action: { } action }) return;
        // An accepted native action supplies the physical invoice, including a
        // true Played response with no CardUseFrame (Mengmou/Pingliao).
        var parent = action.Type == CardActionType.Use ? LifecycleCardUse(_resolutionStack.OfType<CardUseFrame>().LastOrDefault(u => u.Action?.ActionId == action.ActionId)?.Id ?? 0)?.Id ?? 0 : _resolutionStack.LastOrDefault()?.Id ?? 0;
        foreach (var cost in action.PhysicalCards)
        {
            if (ProgramEventHistory<MouSimaShiMaterialEvent>().Any(e => e.ActionId == action.ActionId && e.CardId == cost.CardId)) continue;
            var entry = _cardMovements.LastOrDefault(m => m.CardId == cost.CardId);
            if (entry is null || entry.CardKind != cost.CardKind || entry.From != cost.From) continue;
            var processing = entry.To == CardLocation.Processing && (action.Type == CardActionType.Use || entry.Reason == CardMoveReasons.Respond || entry.Reason == CardMoveReasons.Nullification || entry.Reason.Value == "conversion.declaration.pay");
            var direct = entry.To == CardLocation.DiscardPile && IsProgramAlternativeCost(action, cost.CardId);
            if (parent > 0 && (processing || direct)) AdvanceEventRulesAndQueueFact(new MouSimaShiMaterialEvent(action.ActionId, action.ActorSeat, parent, cost.CardId, cost.CardKind, cost.From, processing ? entry.Sequence : 0, direct ? entry.Sequence : 0));
        }
    }
    private void CaptureMouSimaShiDiscardBatch(CardMovementBatchContext batch)
    {
        if (!HasMouSimaShi) return;
        foreach (var m in batch.Movements.Where(m => m.To == CardLocation.DiscardPile && m.From != m.To && ProgramEventHistory<MouSimaShiMarkedEvent>().Any(e => e.CardId == m.CardId)))
        {
            var previous = _cardMovements.LastOrDefault(x => x.CardId == m.CardId && x.Sequence < m.Sequence);
            var finish = m.From == CardLocation.Processing && (IsNativeUseFinishReason(m.Reason) || m.Reason == CardMoveReasons.ResponseFinished || m.Reason == CardMoveReasons.NullificationFinished);
            AdvanceEventRulesAndQueueFact(new MouSimaShiDiscardEvent(batch.TurnNumber, m.CardId, m.CardKind, m.Sequence, batch.ParentFrameId ?? 0, previous?.To == CardLocation.Processing ? previous.Sequence : 0, finish));
        }
    }
    private bool ValidMouSimaShiDebt(MouSimaShiClaimIssuedEvent d) => d.Source.SkillId == MouSimaShiRuntime.Chenlve && d.Source.BindingId == "claim-dead-soldiers" && IsRecordedActualDiscardRecoveryPhase(d.Phase) && d.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && d.Phase.ActorSeat == d.Source.OwnerSeat && _contentRegistry.GetSkill(d.Source.SkillId).Program?.GameplayHash == d.GameplayHash && ProgramEventHistory<MouSimaShiStartedEvent>().Count(e => e.FrameId == d.FrameId && e.Source == d.Source && e.GameplayHash == d.GameplayHash && e.Operation == SkillProgramEffectOp.ResolveMouSimaShiChenlve) == 1;
    private IReadOnlyList<ActualPhaseEndItem> CollectMouSimaShiPhaseEndCandidates(ActualDiscardRecoveryPhaseKey phase) => !HasMouSimaShi ? [] : Array.AsReadOnly(ProgramEventHistory<MouSimaShiClaimIssuedEvent>().Where(d => d.Phase == phase && ValidMouSimaShiDebt(d) && !ProgramEventHistory<MouSimaShiPhaseSettledEvent>().Any(e => e.DebtId == d.FrameId)).Select(d => { var t = _contentRegistry.GetSkill(d.Source.SkillId).Program!.Triggers.Single(t => t.Id == "remove-issued-entities"); return new ActualPhaseEndItem(new(d.Source.OwnerSeat, d.Source.SkillId, t.Id, d.Source.SkillInstanceId, d.GameplayHash, t.Priority), d.FrameId); }).ToArray());
    private IReadOnlyList<ProgramTriggerCandidate> IncludeMouSimaShiDeathCandidates(CharacterState owner, IReadOnlyList<ProgramTriggerCandidate> existing)
    {
        if (!HasMouSimaShi) return existing;
        var extra = ProgramEventHistory<MouSimaShiClaimIssuedEvent>().Where(d => d.Source.OwnerSeat == owner.Seat && ValidMouSimaShiDebt(d) && !ProgramEventHistory<MouSimaShiDeathSettledEvent>().Any(e => e.DebtId == d.FrameId)).Select(d => { var t = _contentRegistry.GetSkill(d.Source.SkillId).Program!.Triggers.Single(t => t.Id == "death-return-shuffle"); return new ProgramTriggerCandidate(owner.Seat, d.Source.SkillId, t.Id, d.Source.SkillInstanceId, d.GameplayHash, t.Priority); });
        return Array.AsReadOnly(existing.Where(c => c.SkillId != MouSimaShiRuntime.Chenlve || c.BindingId != "death-return-shuffle").Concat(extra).Distinct().ToArray());
    }
    private MouSimaShiClaimIssuedEvent? MouSimaShiDue(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
    {
        if (!HasMouSimaShi || c.SkillId != MouSimaShiRuntime.Chenlve || w.OwnerSeat != c.OwnerSeat) return null;
        var debt = ProgramEventHistory<MouSimaShiClaimIssuedEvent>().SingleOrDefault(d => d.Source.OwnerSeat == c.OwnerSeat && d.Source.SkillInstanceId == c.SkillInstanceId && d.GameplayHash == c.GameplayHash && ValidMouSimaShiDebt(d)); if (debt is null) return null;
        var paid = _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MouSimaShi?.DebtId == debt.FrameId && f.WindowContext == w && MountObserverCandidateMatches(f, c));
        if (c.BindingId == "remove-issued-entities" && w.Window == SkillProgramTriggerWindow.ActualPhaseEnded && w.ActualPhaseEnd is { } due && due.EntitlementId == debt.FrameId && due.Phase == debt.Phase && IsExactActualPhaseEndCandidate(c, w) && (paid || !ProgramEventHistory<MouSimaShiPhaseSettledEvent>().Any(e => e.DebtId == debt.FrameId))) return debt;
        if (c.BindingId != "death-return-shuffle" || w.Window != SkillProgramTriggerWindow.OwnerDied || _players[c.OwnerSeat].IsAlive || _resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is not ProgramDeathTriggerWindowFrame p || p.OwnerSeat != c.OwnerSeat || p.CandidateIndex < 0 || p.CandidateIndex >= p.Candidates.Count || p.Candidates[p.CandidateIndex] != c || _resolutionStack.SingleOrDefault(f => f.Id == p.DeathFrameId) is not DeathFrame death || death.VictimSeat != c.OwnerSeat) return null;
        return paid || !ProgramEventHistory<MouSimaShiDeathSettledEvent>().Any(e => e.DebtId == debt.FrameId) ? debt : null;
    }
    private bool IsIssuedMouSimaShiCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext w) => MouSimaShiDue(c, w) is not null;
}
