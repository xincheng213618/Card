namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidMatchingRecastReceipt(ProgramSkillFrame f)
    {
        if (f.MatchingRecast is not { } r || f.TriggerId is not null || f.WindowContext is not null ||
            f.InstructionIndex != 1 || r.InstructionIndex != f.InstructionIndex ||
            r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            r.GameplayHash != f.GameplayHash || r.Owner.Seat != f.OwnerSeat || !IsValidPlayerSeat(r.Peer.Seat) ||
            !f.SelectedCardIds.SequenceEqual(r.Owner.Materials.Select(m => m.CardId)) || !MatchingRecastGroup(r.Owner.Materials) ||
            f.SelectedTargetSeats is not [var peer] || peer != r.Peer.Seat || r.Category != GetProgramCardCategory(r.Owner.Materials[0].PrintedKind) ||
            r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _turnProgression.OwnerSeat || _currentSeat != f.OwnerSeat ||
            _phase != TurnPhase.Play || r.PhaseInstanceId <= 0 || r.PhaseInstanceId != _cardUseDebitPhaseInstanceId ||
            !Enum.IsDefined(r.Stage) || _resolutionStack.FindIndex(frame => frame.Id == f.Id) != 0) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        if (plan.Activation is not { } a || a.UsageGroup != r.UsageGroup || a.TargetPhaseLedgerId != r.TargetLedgerId ||
            a.Effects is not [{ Op: SkillProgramEffectOp.RecastMatchingCardsThenPeerChoice, StateId: { } ledger }] || ledger != r.TargetLedgerId ||
            plan.Instructions is not [{ Op: SkillProgramEffectOp.RecastMatchingCardsThenPeerChoice }] ||
            _skillRuntimeState.GetUsage(f.OwnerSeat, f.SkillId, $"{ledger}:target:{peer}", SkillUsageScope.Phase) != 1 ||
            r.EligiblePeerMaterials.Select(m => m.CardId).Distinct().Count() != r.EligiblePeerMaterials.Count ||
            r.EligiblePeerMaterials.Any(m => m.From.OwnerSeat != peer || m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                m.CardId <= 0 || !Enum.IsDefined(m.PrintedKind) || GetProgramCardCategory(m.PrintedKind) != r.Category) ||
            r.Peer.Materials.Count > r.Owner.Materials.Count || r.Peer.Materials.Any(m => !r.EligiblePeerMaterials.Contains(m))) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<ProgramSkillStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
                e.SkillId == f.SkillId && e.ActivationId == f.ActivationId) != 1 ||
            history.OfType<MatchingRecastStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] ||
            start != new MatchingRecastStartedEvent(f.Id, r.Source, r.GameplayHash, r.UsageGroup, ledger, peer,
                r.ActualTurnNumber, r.ActualTurnOwnerSeat, r.PhaseInstanceId)) return false;
        bool ParticipantValid(MatchingRecastParticipantReceipt p, int cursor)
        {
            if (p.Materials.Select(m => m.CardId).Distinct().Count() != p.Materials.Count || p.Materials.Any(m => m.CardId <= 0 ||
                !Enum.IsDefined(m.PrintedKind) || m.From.OwnerSeat != p.Seat || m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))) return false;
            var payments = history.OfType<MatchingRecastPaidEvent>().Where(e => e.FrameId == f.Id && e.Cursor == cursor).ToArray();
            var draws = history.OfType<MatchingRecastDrawIssuedEvent>().Where(e => e.FrameId == f.Id && e.Cursor == cursor).ToArray();
            if (!p.CostIssued) return !p.DrawIssued && p.CostBefore == 0 && p.CostAfter == 0 && p.CostBatchId is null &&
                p.ActualDrawCount == 0 && p.DrawBefore == 0 && p.DrawAfter == 0 && payments.Length == 0 && draws.Length == 0;
            if (p.Materials.Count != r.Owner.Materials.Count || p.CostBefore < 0 || p.CostAfter <= p.CostBefore ||
                p.CostAfter > MatchingRecastSequence || p.CostBatchId is not { } batch || payments.Length != p.Materials.Count ||
                !payments.Zip(p.Materials).All(pair => pair.First == new MatchingRecastPaidEvent(f.Id, cursor, p.Seat, pair.Second.CardId,
                    pair.Second.PrintedKind, pair.Second.From, batch, p.CostBefore, p.CostAfter))) return false;
            var costs = _cardMovements.Where(m => m.Sequence > p.CostBefore && m.Sequence <= p.CostAfter).ToArray();
            if (costs.Length != p.Materials.Count || !costs.Zip(p.Materials).All(pair => pair.First.CardId == pair.Second.CardId &&
                pair.First.CardKind == pair.Second.PrintedKind && pair.First.From == pair.Second.From &&
                pair.First.To == pair.Second.Destination && pair.First.Reason == CardMoveReasons.RecastDiscard)) return false;
            if (!p.DrawIssued) return p.ActualDrawCount == 0 && p.DrawBefore == 0 && p.DrawAfter == 0 && draws.Length == 0;
            if (p.ActualDrawCount < 0 || p.ActualDrawCount > p.Materials.Count || p.DrawBefore < p.CostAfter ||
                p.DrawAfter < p.DrawBefore || p.DrawAfter > MatchingRecastSequence || draws is not [var draw] ||
                draw != new MatchingRecastDrawIssuedEvent(f.Id, cursor, p.Seat, p.ActualDrawCount, p.DrawBefore, p.DrawAfter)) return false;
            var rewards = _cardMovements.Where(m => m.Sequence > p.DrawBefore && m.Sequence <= p.DrawAfter).ToArray();
            return rewards.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(p.Seat) && m.Reason == CardMoveReasons.RecastDraw) == p.ActualDrawCount &&
                rewards.All(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(p.Seat) && m.Reason == CardMoveReasons.RecastDraw ||
                    m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle);
        }
        if (!ParticipantValid(r.Owner, 0) || !ParticipantValid(r.Peer, 1) ||
            history.OfType<MatchingRecastPaidEvent>().Any(e => e.FrameId == f.Id && e.Cursor is not (0 or 1)) ||
            history.OfType<MatchingRecastDrawIssuedEvent>().Any(e => e.FrameId == f.Id && e.Cursor is not (0 or 1))) return false;
        var damages = history.OfType<MatchingRecastDamageIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.DamageIssued ? r.Peer.CostIssued || r.Peer.Materials.Count != 0 || damages is not [var damage] ||
                damage != new MatchingRecastDamageIssuedEvent(f.Id, f.OwnerSeat, peer, 1, DamageNature.Thunder) : damages.Length != 0) return false;
        var done = history.OfType<MatchingRecastCompletedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == MatchingRecastStage.Complete) return r.Owner.CostIssued && r.Owner.DrawIssued &&
            (!r.Peer.CostIssued || r.Peer.DrawIssued) && f.PendingMovementContinuation is null && done is [var complete] &&
            complete == new MatchingRecastCompletedEvent(f.Id, r.Owner.CostIssued, r.Peer.CostIssued, r.DamageIssued, r.Owner.ActualDrawCount, r.Peer.ActualDrawCount);
        if (done.Length != 0 || r.Peer.CostIssued && r.Peer.CostBefore < r.Owner.DrawAfter) return false;
        if (r.Stage == MatchingRecastStage.OwnerReady) return !r.Owner.CostIssued && !r.Peer.CostIssued && !r.DamageIssued &&
            r.Peer.Materials.Count == 0 && r.EligiblePeerMaterials.Count == 0 && f.PendingMovementContinuation is null && MatchingRecastMaterialsStillOwned(r.Owner);
        if (!r.Owner.CostIssued) return false;
        if (r.Stage is MatchingRecastStage.ChoosingPeer or MatchingRecastStage.ChoosingPeerCards)
            return r.Owner.DrawIssued && !r.Peer.CostIssued && !r.DamageIssued && f.PendingMovementContinuation is null &&
                (r.Stage == MatchingRecastStage.ChoosingPeer ? r.Peer.Materials.Count == 0 : r.Peer.Materials.Count < r.Owner.Materials.Count &&
                    r.EligiblePeerMaterials.Count >= r.Owner.Materials.Count) &&
                r.EligiblePeerMaterials.SequenceEqual(MatchingRecastMaterials(peer).Where(m => GetProgramCardCategory(m.PrintedKind) == r.Category)) && MatchingRecastMaterialsStillOwned(r.Peer);
        if (r.Stage == MatchingRecastStage.DamageIssued) return r.Owner.DrawIssued && r.DamageIssued && f.PendingMovementContinuation is null &&
            (f.AttackAttempt is not { } attack || attack.SourceSeat == f.OwnerSeat && attack.TargetSeat == peer && attack.Nature == DamageNature.Thunder && !attack.SourceLess);
        var p = CurrentMatchingRecast(r);
        return !r.DamageIssued && p.CostIssued && f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } pending &&
            pending.SubjectSeat == p.Seat && (r.Stage is MatchingRecastStage.OwnerCostChildren or MatchingRecastStage.PeerCostChildren ? !p.DrawIssued :
                r.Stage is MatchingRecastStage.OwnerDrawChildren or MatchingRecastStage.PeerDrawChildren && p.DrawIssued);
    }
    private bool MatchingRecastFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MatchingRecast is not { } r || !ValidMatchingRecastReceipt(f)) return false;
        if (r.Stage == MatchingRecastStage.DamageIssued) return r.DamageIssued && f.AttackAttempt is not null && DyingSuitsStructuralEdge(f, child);
        if (r.Stage is not (MatchingRecastStage.OwnerCostChildren or MatchingRecastStage.OwnerDrawChildren or MatchingRecastStage.PeerCostChildren or MatchingRecastStage.PeerDrawChildren)) return false;
        var p = CurrentMatchingRecast(r); var drawing = r.Stage is MatchingRecastStage.OwnerDrawChildren or MatchingRecastStage.PeerDrawChildren;
        var before = drawing ? p.DrawBefore : p.CostBefore; var after = drawing ? p.DrawAfter : p.CostAfter;
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.ResumeProgramFrameId is null && moved.Batch.Id == moved.Id && moved.Batch.ParentFrameId == f.Id &&
                moved.Batch.AwaitingProgramFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
                moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                (drawing || moved.Batch.Id == p.CostBatchId || moved.Batch.ParentBatchId == p.CostBatchId && p.Materials.Any(m => m.PrintedKind == CardKind.WoodenOx && m.From.Zone == CardZoneKind.Equipment)) &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                    (drawing ? m.Sequence > before && m.Sequence <= after && (m.Reason == CardMoveReasons.RecastDraw || m.Reason == CardMoveReasons.Reshuffle) :
                        p.Materials.Any(paid => m.Sequence > before && m.Sequence <= after && m.CardId == paid.CardId && m.From == paid.From &&
                            m.To == paid.Destination && m.Reason == CardMoveReasons.RecastDiscard) ||
                        m.Sequence > p.CostAfter && m.From == CardLocation.WoodenOxGrain(p.Seat) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard &&
                        p.Materials.Any(paid => paid.PrintedKind == CardKind.WoodenOx && paid.From == CardLocation.Equipment(p.Seat))));
        if (OrderedPrintedSkillLossSkillsChangedEdge(f, child)) return !drawing && p.Materials.Any(m => m.From.Zone == CardZoneKind.Equipment);
        if (drawing || !p.Materials.Any(m => m.PrintedKind == CardKind.SilverLion && m.From == CardLocation.Equipment(p.Seat))) return false;
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            hp.Change.ParentFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == p.Seat && hp.Change.TargetSeat == p.Seat && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, f) &&
            recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.SourceSeat == p.Seat && recovery.Attempt.TargetSeat == p.Seat &&
            recovery.Attempt.Amount == 1 && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason == CardMoveReasons.RecastDiscard;
    }
    private bool MatchingRecastStructuralEdge(ResolutionFrame parent, ResolutionFrame child) => parent is ProgramSkillFrame f && MatchingRecastFirstChild(f, child);
    private bool IsMatchingRecastChoice(ProgramSkillFrame f, PendingDecision decision)
    {
        if (f.MatchingRecast is not { Stage: MatchingRecastStage.ChoosingPeer or MatchingRecastStage.ChoosingPeerCards } r ||
            !ValidMatchingRecastReceipt(f) || decision.Kind != DecisionKind.ProgramTrigger || !decision.IsPrivate ||
            decision.PlayerSeat != r.Peer.Seat || decision.TargetSeat != r.Peer.Seat || decision.SourceSeat != f.OwnerSeat ||
            decision.ValidTargetSeats.Count != 0 || decision.ValidContentIds.Count != 0 || decision.RequiredCardCount != 0 || decision.SkillPrompt?.SkillId != f.SkillId) return false;
        var choices = MatchingRecastChoices(f);
        return decision.ValidCardIds.SequenceEqual(choices.SelectMany(c => c.Cards).Distinct()) && decision.Choices.Count == choices.Count &&
            decision.Choices.Zip(choices).All(pair => SameNameHandChoicesEqual(pair.First, pair.Second));
    }
    private void AssertMatchingRecast(ProgramSkillFrame f)
    {
        if (f.MatchingRecast is null) return;
        if (!ValidMatchingRecastReceipt(f)) throw new InvalidOperationException("Matching recast lost its group, target phase debit, private choice or once-paid cost/draw invoice.");
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        if (index + 1 < _resolutionStack.Count && !MatchingRecastFirstChild(f, _resolutionStack[index + 1]))
            throw new InvalidOperationException("Matching recast retained an unrelated first native child.");
        if (index == _resolutionStack.Count - 1 && _pendingDecision is { } prompt &&
            f.MatchingRecast.Stage is MatchingRecastStage.ChoosingPeer or MatchingRecastStage.ChoosingPeerCards && !IsMatchingRecastChoice(f, prompt))
            throw new InvalidOperationException("Matching recast changed its participant-only private prompt.");
    }
    private ProgramSkillFrame? MatchingRecastObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
            if (_resolutionStack[index] is ProgramSkillFrame root && MatchingRecastFirstChild(root, _resolutionStack[index + 1]) && SameNameHandObserverSuffix(index)) return root;
        return null;
    }
    private bool IsMatchingRecastDying() => ActiveDying is { } dying && MatchingRecastObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasMatchingRecastDamageObserver(long windowId) => _resolutionStack.Any(f => f.Id == windowId && f is DamageTriggerWindowFrame) && MatchingRecastObserverRoot() is not null;
    private bool HasMatchingRecastBeforeDamageObserver(long windowId) => _resolutionStack.Any(f => f.Id == windowId && f is BeforeDamageProgramWindowFrame) && MatchingRecastObserverRoot() is not null;
    private bool AllowsMatchingRecastNestedDamage(ProgramSkillFrame observer, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id) return false;
        if (observer.MatchingRecast is { Stage: MatchingRecastStage.DamageIssued, DamageIssued: true } r && ValidMatchingRecastReceipt(observer))
            return target == r.Peer.Seat && amount == 1 && source is null && nature == DamageNature.Thunder;
        if (observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or
            SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or
            SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) || MatchingRecastObserverRoot() is null) return false;
        var e = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        return e.Op == SkillProgramEffectOp.Damage && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, e.Target));
    }
    private bool TryAdvanceMatchingRecastSubtree()
    {
        if (_pendingDecision is not null || MatchingRecastObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attack)
        {
            if (CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null || attack.AttackReturn is null ||
                _resolutionStack.Any(f => f is DamageFrame d && d.ParentFrameId == attack.Id || f is BeforeDamageProgramWindowFrame b && (b.ContinuationAttackResolutionId ?? b.ParentFrameId) == attack.Id)) return false;
            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id)); AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or
            ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame or RecoveryReplacementFrame)
        { AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true; }
        if (top is DeathFrame death) { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        return false;
    }
}
