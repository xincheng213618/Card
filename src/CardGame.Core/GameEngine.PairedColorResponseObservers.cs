namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CardLocation PairedColorPaidDestination(ProgramSkillFrame f, ProgramPairedColorDispositionReceipt r) =>
        r.PaidGeneralWeapon && r.PaidFrom?.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame :
        r.Obtain ? CardLocation.Hand(f.OwnerSeat) : CardLocation.DiscardPile;
    private string PairedColorPaidReason(ProgramPairedColorDispositionReceipt r) => r.Obtain ? PairedColorObtainReason : PairedColorDiscardReason;

    private bool ValidPairedColorDispositionReceipt(ProgramSkillFrame f)
    {
        if (f.PairedColorDisposition is not { } r || !ExactPairedColorDispositionParent(f, out _, out var pair, out var counterpart) ||
            r.InstructionIndex != f.InstructionIndex || r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            r.GameplayHash != f.GameplayHash || r.Pair != pair || r.CounterpartSeat != counterpart || !Enum.IsDefined(r.Stage) ||
            GetProgramTrigger(f).Effects[0].StateId != r.StateId) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId &&
                e.BindingId == f.TriggerId && e.SkillInstanceId == f.SkillInstanceId && e.Window == SkillProgramTriggerWindow.CardResponseCompleted) != 1 ||
            history.OfType<PairedColorDispositionStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var started] ||
            started != new PairedColorDispositionStartedEvent(f.Id, r.Source, r.GameplayHash, r.StateId,
                pair.ResponseActionId, pair.PairedActionId, counterpart, r.Obtain) ||
            history.OfType<PairedColorDispositionCompletedEvent>().Any(e => e.FrameId == f.Id)) return false;
        var payments = history.OfType<PairedColorDispositionPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == PairedColorDispositionStage.ChoosingCard)
            return f.PendingMovementContinuation is null && payments.Length == 0 && r.PaidCardId is null && r.PaidKind is null &&
                r.PaidFrom is null && !r.PaidGeneralWeapon && r.SequenceBefore == 0 && r.SequenceAfter == 0 && r.BatchId is null;
        if (r.PaidCardId is not { } id || r.PaidKind is not { } kind || r.PaidFrom is not { } from ||
            from.OwnerSeat != counterpart || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            r.SequenceBefore < 0 || r.SequenceAfter <= r.SequenceBefore || r.BatchId is not { } batch ||
            f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != counterpart ||
            payments is not [var paid] || paid != new PairedColorDispositionPaidEvent(f.Id, id, kind, from,
                PairedColorPaidDestination(f, r), r.PaidGeneralWeapon, r.SequenceBefore, r.SequenceAfter, batch)) return false;
        var moves = _cardMovements.Where(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter).ToArray();
        return moves is [var moved] && moved.CardId == id && moved.CardKind == kind && moved.From == from &&
            moved.To == paid.To && moved.Reason.Value == PairedColorPaidReason(r);
    }

    private bool IsPairedColorDispositionChoice(ProgramSkillFrame f, PendingDecision decision) =>
        f.PairedColorDisposition is { Stage: PairedColorDispositionStage.ChoosingCard } r && ValidPairedColorDispositionReceipt(f) &&
        decision.Kind == DecisionKind.ProgramTrigger && decision.PlayerSeat == f.OwnerSeat && decision.IsPrivate &&
        decision.SkillPrompt?.SkillId == f.SkillId && decision.Choices.Count > 0 &&
        decision.Choices.Count == PairedColorDispositionChoices(f).Count &&
        decision.Choices.Zip(PairedColorDispositionChoices(f)).All(p => SameNameHandChoicesEqual(p.First, p.Second)) &&
        decision.ValidCardIds.SequenceEqual(decision.Choices.SelectMany(c => c.Cards).Distinct()) &&
        decision.ValidTargetSeats.Count == 0 && decision.TargetSeat == r.CounterpartSeat && decision.SourceSeat == f.OwnerSeat &&
        decision.ValidContentIds.Count == 0 && decision.RequiredCardCount == 0 && decision.IncomingCard is null && decision.RequiredCardKind is null;

    private void AssertPairedColorDisposition(ProgramSkillFrame f)
    {
        if (f.PairedColorDisposition is null)
        {
            if (f.TriggerId is null || _contentRegistry.GetSkill(f.SkillId).Program?.Triggers.SingleOrDefault(t => t.Id == f.TriggerId)?.Effects
                    .Any(e => e.Op == SkillProgramEffectOp.OfferPairedColorCardDisposition) != true) return;
            if (CompleteProgramEventHistory().OfType<PairedColorDispositionStartedEvent>().Any(e => e.FrameId == f.Id) &&
                !CompleteProgramEventHistory().OfType<PairedColorDispositionCompletedEvent>().Any(e => e.FrameId == f.Id))
                throw new InvalidOperationException("An issued paired-color disposition lost its owning receipt.");
            return;
        }
        if (!ValidPairedColorDispositionReceipt(f))
            throw new InvalidOperationException("Paired-color disposition lost its native response pair, exact source or paid entity invoice.");
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        if (index + 1 < _resolutionStack.Count && !PairedColorDispositionFirstChild(f, _resolutionStack[index + 1]))
            throw new InvalidOperationException("Paired-color disposition retained an unrelated native child.");
        if (index == _resolutionStack.Count - 1 && _pendingDecision is { } choice &&
            f.PairedColorDisposition.Stage == PairedColorDispositionStage.ChoosingCard && !IsPairedColorDispositionChoice(f, choice))
            throw new InvalidOperationException("Paired-color disposition changed its owner-only opaque card prompt.");
    }

    private bool PairedColorDispositionFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.PairedColorDisposition is not { Stage: PairedColorDispositionStage.MovementChildren } r || !ValidPairedColorDispositionReceipt(f)) return false;
        bool PaidEquipment(CardKind kind) => r.PaidKind == kind && r.PaidFrom == CardLocation.Equipment(r.CounterpartSeat);
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.ResumeProgramFrameId is null && moved.Batch.Id == moved.Id && moved.Batch.ParentFrameId == f.Id &&
                moved.Batch.AwaitingProgramFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
                moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                    (moved.Batch.Id == r.BatchId && m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter &&
                        m.CardId == r.PaidCardId && m.From == r.PaidFrom && m.To == PairedColorPaidDestination(f, r) && m.Reason.Value == PairedColorPaidReason(r) ||
                     PaidEquipment(CardKind.WoodenOx) && moved.Batch.ParentBatchId == r.BatchId && m.Sequence > r.SequenceAfter &&
                        m.From == CardLocation.WoodenOxGrain(r.CounterpartSeat) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard));
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return r.PaidFrom?.Zone == CardZoneKind.Equipment && skills.ResumeProgramFrameId == f.Id &&
                skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return state.ResumeProgramFrameId == f.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
                state.CandidateIndex >= 0 && state.CandidateIndex <= state.Candidates.Count &&
                CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id &&
                    e.Change.ParentFrameId == f.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        if (!PaidEquipment(CardKind.SilverLion)) return false;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.ParentFrameId == f.Id &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == r.CounterpartSeat && hp.Change.TargetSeat == r.CounterpartSeat && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, f) &&
            recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.SourceSeat == r.CounterpartSeat &&
            recovery.Attempt.TargetSeat == r.CounterpartSeat && recovery.Attempt.Amount == 1 &&
            recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == PairedColorPaidReason(r);
    }

    private bool PairedColorDispositionStructuralEdge(ResolutionFrame parent, ResolutionFrame child) =>
        parent is ProgramSkillFrame root && PairedColorDispositionFirstChild(root, child) ||
        parent is ProgramCardTriggerWindowFrame window && child is ProgramSkillFrame { PairedColorDisposition: not null } program &&
            program.WindowContext?.ParentFrameId == window.Id && ValidPairedColorDispositionReceipt(program);
    private ProgramSkillFrame? PairedColorDispositionObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
            if (_resolutionStack[index] is ProgramSkillFrame root && PairedColorDispositionFirstChild(root, _resolutionStack[index + 1]) &&
                SameNameHandObserverSuffix(index)) return root;
        return null;
    }
    private bool IsPairedColorDispositionDying() => ActiveDying is { } dying && PairedColorDispositionObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasPairedColorDispositionCardObserver(long parentId) => PairedColorDispositionObserverRoot() is { } root &&
        root.PairedColorDisposition!.Pair.NativeParentFrameId == parentId;
    private bool HasPairedColorDispositionDamageObserver(long id) => HasPairedColorDispositionObserver(id, false);
    private bool HasPairedColorDispositionBeforeDamageObserver(long id) => HasPairedColorDispositionObserver(id, true);
    private bool HasPairedColorDispositionObserver(long id, bool before)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == id && (before ? f is BeforeDamageProgramWindowFrame : f is DamageTriggerWindowFrame));
        if (index < 0 || PairedColorDispositionObserverRoot() is not { } root) return false;
        var rootIndex = _resolutionStack.FindIndex(f => f.Id == root.Id);
        if (index > rootIndex) return true;
        for (var child = index + 1; child <= rootIndex; child++)
            if (!PairedColorDispositionStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !ResponseCompletionStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !OrderedPrintedSkillLossStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !OrderedPrintedSkillLossSkillsChangedEdge(_resolutionStack[child - 1], _resolutionStack[child])) return false;
        return index < rootIndex;
    }
    private bool AllowsPairedColorDispositionNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or
                SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            PairedColorDispositionObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }
    private bool TryAdvancePairedColorDispositionSubtree()
    {
        if (_pendingDecision is not null || PairedColorDispositionObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attack)
        {
            if (CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null || attack.AttackReturn is null ||
                _resolutionStack.Any(f => f is DamageFrame d && d.ParentFrameId == attack.Id || f is BeforeDamageProgramWindowFrame b &&
                    (b.ContinuationAttackResolutionId ?? b.ParentFrameId) == attack.Id)) return false;
            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id)); AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or
            ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame or RecoveryReplacementFrame)
        { AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true; }
        if (top is DeathFrame death) { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        return false;
    }
}
