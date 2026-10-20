namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasMouPangTongCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouPangTongLianhuanTargets);
    private static CardConversionSource MouPtSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool MouPtUpgraded(int owner) => ProgramEventHistory<MouPangTongUpgradedEvent>().Any(e => e.Source.OwnerSeat == owner);
    private bool HasMouPangTongExpandedTargets(CharacterState owner) => HasRuntimeSkill(owner, MouPangTongComposition.Lianhuan) && MouPtUpgraded(owner.Seat);
    private CardUseFrame? MouPtUse(ProgramSkillWindowContext c) => c.CardUse is { } card ? _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Action?.ActionId == card.CardActionId) : null;
    private bool MouPtPaidFor(CardUseFrame use, int owner) => use.Action is { Type: CardActionType.Use, EffectiveKind: CardKind.IronChain } a && a.ActorSeat == owner &&
        ProgramEventHistory<MouPangTongLianhuanPaidEvent>().Any(e => e.UseId == use.Id && e.ActionId == a.ActionId && e.Source.OwnerSeat == owner);
    private bool MouPtPaidBySource(CardUseFrame use, CardConversionSource source, string gameplayHash) => MouPtPaidFor(use, source.OwnerSeat) &&
        ProgramEventHistory<MouPangTongLianhuanPaidEvent>().Any(e => e.UseId == use.Id && e.ActionId == use.Action!.ActionId && e.GameplayHash == gameplayHash && e.Source == (source with { BindingId = "pay-for-chain" }));
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 30500)]
    private void CaptureMouPtConversion(CardUseDeclaredEvent e)
    {
        if (!HasMouPangTongCapability || LifecycleCardUse(e.ResolutionId) is not { Action: { Type: CardActionType.Use, EffectiveKind: CardKind.IronChain } a } ||
            CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != a.ActorSeat ||
            a.ConversionChain.FirstOrDefault(s => s.SkillId == MouPangTongComposition.Lianhuan && s.BindingId == "club-hand-chain" && s.OwnerSeat == a.ActorSeat) is not { } source ||
            ProgramEventHistory<MouPangTongConversionUsedEvent>().Any(f => f.UseId == e.ResolutionId)) return;
        AdvanceEventRulesAndQueueFact(new MouPangTongConversionUsedEvent(source, phase, e.ResolutionId, a.ActionId));
    }
    [ProgramCardAction(ProgramCardActionPhase.AfterProgram, 30500)]
    private void FilterMouPtConversion(List<LegalAction> actions, CharacterState actor, IReadOnlyList<Card> playable, int? selectedPhysicalCardId = null)
    {
        if (!HasMouPangTongCapability || CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase) return;
        if (ProgramEventHistory<MouPangTongConversionUsedEvent>().Any(e => e.Source.OwnerSeat == actor.Seat && e.Phase == phase))
            actions.RemoveAll(a => a.Kind == LegalActionKind.IronChain && a.ConversionSource?.SkillId == MouPangTongComposition.Lianhuan);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 30500, SkillProgramEffectOp.ArmMouPangTongLianhuan, SkillProgramEffectOp.ResolveMouPangTongLianhuanTargets)]
    private bool CanOfferMouPt(ProgramTriggerLegalityContext gate)
    {
        if (MouPtUse(gate.Context) is not { Action: { Type: CardActionType.Use, EffectiveKind: CardKind.IronChain } a } use || a.ActorSeat != gate.OwnerSeat) return false;
        if (gate.Features.HasOperation(SkillProgramEffectOp.ArmMouPangTongLianhuan)) return !MouPtUpgraded(gate.OwnerSeat) && !MouPtPaidFor(use, gate.OwnerSeat);
        var source = new CardConversionSource(gate.SkillId, gate.Trigger.Id, gate.OwnerSeat, gate.Candidate.SkillInstanceId);
        return (MouPtUpgraded(gate.OwnerSeat) || MouPtPaidBySource(use, source, gate.Candidate.GameplayHash)) &&
            (gate.Context.MouPangTongLateTargetSeat is not null ? MouPtLateNotification(gate.Context, use, source, gate.Candidate.GameplayHash) :
             !ProgramEventHistory<MouPangTongTargetsStartedEvent>().Any(e => e.UseId == use.Id && e.Source.OwnerSeat == gate.OwnerSeat));
    }
    [ProgramCapability] private sealed class MouPangTongCapability(GameEngine g) : IMouPangTongHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => g.ResolveMouPt(f, op); }
    private SkillProgramStepOutcome ResolveMouPt(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (op == SkillProgramEffectOp.UpgradeMouPangTongLianhuan)
        {
            if (f.SkillId != MouPangTongComposition.Niepan || f.TriggerId != "rebirth" || f.InstructionIndex != 6) throw new InvalidOperationException("Lianhuan must upgrade after the complete native rebirth sequence.");
            if (!MouPtUpgraded(f.OwnerSeat)) AdvanceEventRulesAndQueueFact(new MouPangTongUpgradedEvent(f.Id, MouPtSource(f), f.GameplayHash));
            return SkillProgramStepOutcome.Continue;
        }
        if (f.WindowContext is not { } c || MouPtUse(c) is not { Action: { Type: CardActionType.Use, EffectiveKind: CardKind.IronChain } a } use || a.ActorSeat != f.OwnerSeat)
            throw new InvalidOperationException("Lianhuan requires its exact native actor's IronChain Use.");
        if (op == SkillProgramEffectOp.ArmMouPangTongLianhuan)
        {
            if (f.TriggerId != "pay-for-chain" || f.InstructionIndex != 2) throw new InvalidOperationException("Lianhuan's paid Use must follow its original HP cost.");
            if (ProgramEventHistory<ProgramSkillHpLostEvent>().Any(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.TargetSeat == f.OwnerSeat && e.Amount == 1) && !MouPtPaidFor(use, f.OwnerSeat))
                AdvanceEventRulesAndQueueFact(new MouPangTongLianhuanPaidEvent(f.Id, MouPtSource(f), f.GameplayHash, use.Id, a.ActionId));
            return SkillProgramStepOutcome.Continue;
        }
        if (f.MouPangTongTargets is not null || f.InstructionIndex != 1 || c.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || !(MouPtUpgraded(f.OwnerSeat) || MouPtPaidBySource(use, MouPtSource(f), f.GameplayHash)))
            throw new InvalidOperationException("Lianhuan cannot invent an unpaid final-target discard queue.");
        if (c.MouPangTongLateTargetSeat is not null && !MouPtLateNotification(c, use, MouPtSource(f), f.GameplayHash))
            throw new InvalidOperationException("Lianhuan's additional target lost its original native notification and source.");
        var receipt = new MouPangTongTargetsReceipt(MouPtSource(f), f.GameplayHash, c.ParentFrameId, use.Id, a.ActionId)
            { Targets = (c.MouPangTongLateTargetSeat is { } lateTarget ? [lateTarget] : use.TargetSeats.Distinct().Where(t => !MouPtVisited(use.Id, f.OwnerSeat, t)).ToArray()), Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f with { MouPangTongTargets = receipt, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouPangTongTargetsStartedEvent(f.Id, receipt.Source, receipt.GameplayHash, receipt.WindowId, use.Id, a.ActionId, receipt.Targets.Count));
        ContinueMouPtTargets(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private bool ValidMouPtTargets(ProgramSkillFrame f) => f.MouPangTongTargets is { } r && r.Source == MouPtSource(f) && r.GameplayHash == f.GameplayHash && f.InstructionIndex == 1 &&
        f.SkillId == MouPangTongComposition.Lianhuan && f.TriggerId == "discard-unchained-targets" && f.WindowContext is { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized } c && c.ParentFrameId == r.WindowId &&
        LifecycleCardUse(r.UseId) is { Action: { } a } && a.ActionId == r.ActionId && a.ActorSeat == f.OwnerSeat && a.EffectiveKind == CardKind.IronChain &&
        r.Cursor >= 0 && r.Cursor <= r.Targets.Count && r.Targets.Distinct().Count() == r.Targets.Count && r.Targets.All(IsValidPlayerSeat) && r.Before >= 0 && r.After >= r.Before && r.After <= _movementSequence &&
        ProgramEventHistory<MouPangTongTargetsStartedEvent>().Count(e => e == new MouPangTongTargetsStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.UseId, r.ActionId, r.Targets.Count)) == 1;
    private void ContinueMouPtTargets(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouPangTongTargets: { } r } f || f.Id != id) return;
        if (!ValidMouPtTargets(f)) throw new InvalidOperationException("Lianhuan lost its actual target queue.");
        if (DrainMouPt(f)) return;
        f = GetActiveProgramFrame(id); r = f.MouPangTongTargets!;
        if (_winner == Winner.None && _players[f.OwnerSeat].IsAlive)
        {
            for (var cursor = r.Cursor; cursor < r.Targets.Count; cursor++)
            {
                var target = _players[r.Targets[cursor]]; var hand = GetHand(target);
                r = r with { Cursor = cursor + 1 }; ReplaceRuntimeTop(f = f with { MouPangTongTargets = r });
                if (MouPtVisited(r.UseId, f.OwnerSeat, target.Seat)) continue;
                AdvanceEventRulesAndQueueFact(new MouPangTongTargetVisitedEvent(id, f.OwnerSeat, r.UseId, r.ActionId, target.Seat));
                if (!target.IsAlive || target.IsChained || hand.Count == 0) continue;
                var card = hand[_random.Next(hand.Count)]; var from = CardLocation.Hand(target.Seat); var before = _movementSequence;
                ReplaceRuntimeTop(f = f with { MouPangTongTargets = r with { Cursor = cursor + 1, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                var logical = CaptureNativeLogicalDiscardInvocation(f.OwnerSeat, f.OwnerSeat, [card.Id], new("skill-program.mou-mobile:lianhuan.discard"), f.Id);
                MoveCard(card, from, CardLocation.DiscardPile, new("skill-program.mou-mobile:lianhuan.discard"), move =>
                    AdvanceEventRulesAndQueueFact(new MouPangTongTargetDiscardedEvent(id, cursor, target.Seat, card.Id, from, move.To, move.Sequence)));
                CompleteNativeLogicalDiscardInvocation(logical);
                var latest = GetActiveProgramFrame(id); ReplaceRuntimeTop(latest with { MouPangTongTargets = latest.MouPangTongTargets! with { After = _movementSequence } });
                foreach (var move in _cardMovements.Where(m => m.Sequence > before && m.Sequence <= _movementSequence))
                    AdvanceEventRulesAndQueueFact(new MouPangTongMovementSignedEvent(id, move.CardId, move.CardKind, move.From, move.To, move.Reason, move.Sequence));
                ContinueMouPtTargets(id); return;
            }
        }
        AdvanceEventRulesAndQueueFact(new MouPangTongTargetsCompletedEvent(id, f.OwnerSeat, r.UseId, r.Targets.Count));
        ReplaceRuntimeTop(f = f with { MouPangTongTargets = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 11)]
    private void CollectPaidMouPtTargets(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        if (!HasMouPangTongCapability || window != SkillProgramTriggerWindow.CardUseTargetsFinalized || action.Type != CardActionType.Use || action.EffectiveKind != CardKind.IronChain ||
            !_players[action.ActorSeat].IsAlive || _winner != Winner.None || _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Action?.ActionId == action.ActionId) is not { } use) return;
        foreach (var paid in ProgramEventHistory<MouPangTongLianhuanPaidEvent>().Where(e => e.UseId == use.Id && e.ActionId == action.ActionId && e.Source.OwnerSeat == action.ActorSeat))
        {
            if (result.Any(c => c.OwnerSeat == paid.Source.OwnerSeat && c.SkillId == paid.Source.SkillId && c.SkillInstanceId == paid.Source.SkillInstanceId && c.TriggerId == "discard-unchained-targets")) continue;
            var context = CreateCardActionProgramContext(action, window, 0, action.ActorSeat, action.TargetSeats.Count == 1 ? action.TargetSeats[0] : -1, CaptureProgramTriggerFacts(_players[action.ActorSeat], action));
            result.Add(new(action.ActorSeat, action.TargetSeats.Count == 1 ? action.TargetSeats[0] : -1, paid.Source.SkillId, "discard-unchained-targets", paid.GameplayHash, paid.Source.SkillInstanceId, 0, context));
        }
    }
    private bool IsIssuedMouPtTargets(ProgramTriggerCandidate candidate, ProgramSkillWindowContext c)
    {
        if (candidate.SkillId != MouPangTongComposition.Lianhuan || candidate.BindingId != "discard-unchained-targets" || c.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
            c.OwnerSeat != candidate.OwnerSeat || !_players[candidate.OwnerSeat].IsAlive || _winner != Winner.None ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is not { } window || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !DesignatedExtraTargetCandidateMatches(candidate, c, window) || LifecycleCardUse(window.ParentFrameId) is not { Action: { Type: CardActionType.Use, EffectiveKind: CardKind.IronChain } a } use ||
            a.ActorSeat != candidate.OwnerSeat || c.CardUse?.CardActionId != a.ActionId || _contentRegistry.GetSkill(candidate.SkillId).Program is not { } p || p.GameplayHash != candidate.GameplayHash) return false;
        return (c.MouPangTongLateTargetSeat is not null ? MouPtLateNotification(c, use, new(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId), candidate.GameplayHash) :
                !ProgramEventHistory<MouPangTongTargetsStartedEvent>().Any(e => e.UseId == use.Id && e.Source.OwnerSeat == candidate.OwnerSeat)) &&
            ProgramEventHistory<MouPangTongLianhuanPaidEvent>().Any(e => e.UseId == use.Id && e.ActionId == a.ActionId && e.GameplayHash == candidate.GameplayHash && e.Source == new CardConversionSource(candidate.SkillId, "pay-for-chain", candidate.OwnerSeat, candidate.SkillInstanceId));
    }
}
