namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ShiDaWindowMatches(ProgramSkillFrame f, SkillProgramTriggerWindow expected)
    {
        if (f.WindowContext is not { } context || context.Window != expected || context.OwnerSeat != f.OwnerSeat || f.TriggerId is null) return false;
        var parent = _resolutionStack.SingleOrDefault(x => x.Id == context.ParentFrameId);
        if (parent is DeferredTurnEndFrame ended)
            return expected == SkillProgramTriggerWindow.AfterTurnEnded && IsActualAfterTurnEndedParent(ended) && MatchesAfterTurnEndedChild(ended, f, context);
        if (parent is ProgramLifecycleTriggerWindowFrame lifecycle)
            return lifecycle.Window == expected && lifecycle.CandidateIndex >= 0 && lifecycle.CandidateIndex < lifecycle.Candidates.Count && MountObserverCandidateMatches(f, lifecycle.Candidates[lifecycle.CandidateIndex]);
        if (parent is CardsMovedTriggerWindowFrame moved)
            return expected == SkillProgramTriggerWindow.CardsMoved && context.MovementBatch is { } batch && batch.Id == moved.Batch.Id && batch.Movements.SequenceEqual(moved.Batch.Movements) &&
                moved.CandidateIndex >= 0 && moved.CandidateIndex < moved.Candidates.Count && MountObserverCandidateMatches(f, moved.Candidates[moved.CandidateIndex]);
        return parent is ProgramCardTriggerWindowFrame card && GetCardActionWindow(card) == expected && card.CandidateIndex >= 0 && card.CandidateIndex < card.Candidates.Count &&
            MountObserverCandidateMatches(f, ToSharedCandidate(card.Candidates[card.CandidateIndex])) && context.CardUse?.CardActionId == card.Action.ActionId;
    }
    private bool ValidShiDa(ProgramSkillFrame f)
    {
        if (f.ShiDengAi is not { } r || r.Source != ShiDaSource(f) || r.Hash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.Hash || f.InstructionIndex != 1 ||
            r.Turn != _turnNumber || r.TurnOwner != _turnProgression.OwnerSeat || r.WindowId != (f.WindowContext?.ParentFrameId ?? 0) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            !Enum.IsDefined(r.Stage) || GetPausedPrivateOfferEffect(f)?.Op != r.Op ||
            ProgramEventHistory<ShiDaStartedEvent>().Count(e => e == new ShiDaStartedEvent(f.Id, r.Source, r.Hash, r.Op, r.Turn, r.TurnOwner, r.WindowId)) != 1) return false;
        return r.Op switch
        {
            SkillProgramEffectOp.ResolveMobileShiDengAiFull => f.SkillId == ShiDaTuntian && f.TriggerId == "full-charge" && ShiDaWindowMatches(f, SkillProgramTriggerWindow.TurnStartBeforeNormalFlow),
            SkillProgramEffectOp.ResolveMobileShiDengAiTuntian => f.SkillId == ShiDaTuntian && f.TriggerId is null && f.ActivationId == "spend-charge" && f.WindowContext is null && _currentSeat == f.OwnerSeat && _phase == TurnPhase.Play,
            SkillProgramEffectOp.ResolveMobileShiDengAiJixi => f.SkillId == ShiDaJixi && f.TriggerId == "ending-jixi" && ShiDaWindowMatches(f, SkillProgramTriggerWindow.AfterTurnEnded),
            _ => false
        };
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 50300)]
    private void RequireShiDa(ProgramSkillFrame f)
    {
        if (f.ShiDengAi is not { } r) return;
        if (!ValidShiDa(f) || r.Targets.Count != r.Targets.Distinct().Count() || r.Targets.Any(seat => !IsValidPlayerSeat(seat)) || r.History.Count != r.History.Distinct().Count() || r.History.Any(seat => !IsValidPlayerSeat(seat) || seat == f.OwnerSeat) ||
            r.Cursor < 0 || r.Cursor > r.Targets.Count || r.RewardCursor is < 0 or > 3 || r.Amount < -1 ||
            r.Op == SkillProgramEffectOp.ResolveMobileShiDengAiTuntian && (r.Stage != ShiDaStage.Amount && (r.Amount < 0 || r.Targets.Count > r.Amount) ||
                r.Paid && (ProgramEventHistory<ShiDaSpentEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var paid] || paid.Source != r.Source || paid.Amount != r.Amount || paid.Recipients != r.Targets.Count || paid.Zaoxian != r.Zaoxian ||
                    !ProgramEventHistory<ShiDaRecipientEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).Select(e => e.Seat).SequenceEqual(r.Targets))) ||
            r.Op == SkillProgramEffectOp.ResolveMobileShiDengAiJixi && r.Targets.Any(seat => !r.History.Contains(seat)))
            throw new InvalidOperationException("Shi Deng Ai lost its exact original boundary, charge payment or selected historical targets.");
        if (r.Draw is { } draw && (r.Op != SkillProgramEffectOp.ResolveMobileShiDengAiFull || draw.RecipientSeat != f.OwnerSeat || draw.RequestedCount != 1 ||
            ProgramEventHistory<ShiDaDrawPaidEvent>().Count(e => e == new ShiDaDrawPaidEvent(f.Id, draw.AttemptFrameId, draw.ActualDrawIds.Count, draw.SequenceBefore, draw.SequenceAfter)) != 1))
            throw new InvalidOperationException("Tuntian's full-charge draw must retain its exact one native outcome.");
        if (r.Op == SkillProgramEffectOp.ResolveMobileShiDengAiJixi && r.Paid &&
            (r.CardFrom is not { } from || !ShiDaOwns(from, r.TurnOwner) || ProgramEventHistory<ShiDaCostPaidEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var cost] ||
             cost.Owner != r.TurnOwner || cost.CardId != r.CardId || cost.From != from || cost.Sequence != r.PaymentSequence || !_cardMovements.Any(m => m.Sequence == cost.Sequence && m.CardId == cost.CardId && m.From == cost.From && m.To == cost.To && m.Reason.Value == ShiDaCostReason)))
            throw new InvalidOperationException("Jixi cannot replace or repay its original real discard.");
        if (r.Stage == ShiDaStage.HeartChildren)
        {
            var candidates = ProgramEventHistory<ShiDaRandomCandidateEvent>().Where(e => e.FrameId == f.Id && e.Ordinal == r.Cursor).OrderBy(e => e.Index).Select(e => e.CardId).ToArray();
            var generator = new DeterministicRandom(unchecked((int)r.RandomBefore)); var index = candidates.Length == 0 ? -1 : generator.Next(candidates.Length);
            if (!candidates.SequenceEqual(r.Eligible) || r.RandomIndex != index || generator.State != r.RandomAfter || r.CardId != (index < 0 ? 0 : candidates[index]) ||
                ProgramEventHistory<ShiDaRandomIssuedEvent>().Count(e => e == new ShiDaRandomIssuedEvent(f.Id, r.Cursor, r.Targets[r.Cursor], r.CardId, index, candidates.Length, r.RandomBefore, r.RandomAfter)) != 1)
                throw new InvalidOperationException("Tuntian's random Heart must retain the original issued sample without rerolling.");
        }
        if (r.NativeActionId > 0 && ProgramEventHistory<ShiDaSnatchIssuedEvent>().Count(e => e.FrameId == f.Id && e.UseId == r.NativeUseId && e.ActionId == r.NativeActionId && e.Origin == r.Origin) != 1)
            throw new InvalidOperationException("Jixi lost its exact one whole native Snatch.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && f.Step == ResolutionFrameStep.AwaitingResponse && r.Stage is ShiDaStage.Amount or ShiDaStage.Recipients or ShiDaStage.Rewards or ShiDaStage.Cost or ShiDaStage.Targets &&
            (_pendingDecision is not { } prompt || prompt.PlayerSeat != f.OwnerSeat || !prompt.IsPrivate || !AssistedChoicesEqual(prompt.Choices, ShiDaChoices(f))))
            throw new InvalidOperationException("Shi Deng Ai lost the current original unpaid prompt.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 50300)]
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 50300)]
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 50300)]
    private bool ShiDaMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) => f.ShiDengAi is { Paid: true } r && ValidShiDa(f) && effect?.Op == r.Op &&
        pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        r.Stage is ShiDaStage.DrawChildren or ShiDaStage.RewardChildren or ShiDaStage.HeartChildren or ShiDaStage.CostChildren or ShiDaStage.NativeChildren;
    private bool ShiDaFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.ShiDengAi is not { Paid: true } r || !ValidShiDa(f)) return false;
        if (child is NativeDrawAttemptFrame draw) return r.Stage == ShiDaStage.Drawing && draw.Context.RecipientSeat == f.OwnerSeat && draw.Context.RequestedCount == 1 && NativeDrawAttemptRidesOn(draw, f);
        if (child is CardUseFrame use) return r.Stage == ShiDaStage.NativeUse && use.Id == r.NativeUseId && ValidShiDaVirtualSnatchUse(use);
        if (child is CardsMovedTriggerWindowFrame moved)
        {
            if (f.PendingMovementContinuation is not { } pending || !ShiDaMovementProof(f, GetPausedPrivateOfferEffect(f), pending) || !ProgramMovementReturnMatches(f, moved)) return false;
            if (r.Stage == ShiDaStage.NativeChildren) return r.NativeBatches.Contains(moved.Batch.Id) && moved.Batch.ParentFrameId == r.NativeUseId && moved.Batch.AwaitingProgramFrameId is null &&
                moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                ProgramEventHistory<ShiDaNativeBatchEvent>().Count(e => e == new ShiDaNativeBatchEvent(f.Id, r.NativeUseId, moved.Batch.Id, moved.Batch.ParentBatchId, moved.Batch.Movements.Count)) == 1;
            return moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m));
        }
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && recovery.Return.ResumeFrameId == f.Id && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
            state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CharacterStateContinuation == CharacterStateContinuation.Program);
    }
    private bool ShiDaNativeEdge(int i)
    {
        if (_resolutionStack[i - 1] is CardUseFrame use && ValidShiDaVirtualSnatchUse(use) && _resolutionStack[i] is ProgramCardTriggerWindowFrame window) return window.ParentFrameId == use.Id && window.Action.ActionId == use.Action!.ActionId;
        if (_resolutionStack[i - 1] is ProgramCardTriggerWindowFrame parent && LifecycleCardUse(parent.ParentFrameId) is { } native && ValidShiDaVirtualSnatchUse(native) &&
            _resolutionStack[i] is ProgramSkillFrame f && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count)
            return MountObserverCandidateMatches(f, ToSharedCandidate(parent.Candidates[parent.CandidateIndex])) && f.WindowContext == CreateCardActionProgramContext(parent, parent.Candidates[parent.CandidateIndex]);
        return false;
    }
    private bool ShiDaChildEdge(int i) => SkillInvocationStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]) || IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) || ShiDaNativeEdge(i) ||
        TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) || _resolutionStack[i - 1] is ProgramSkillFrame f && ShiDaFirstChild(f, _resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private bool ShiDaChildSuffix(int i) => HasObserverSuffix(i, static (g, n) => g.ShiDaChildEdge(n), static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? ShiDaObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.ShiDaFirstChild(f, child), static (g, i) => g.ShiDaChildSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 50300)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 50300)]
    private bool ShiDaDamageObserver(long id) => ShiDaObserverRoot() is { } root && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 50300)] private bool ShiDaBeforeDamageObserver(long id) => ShiDaObserverRoot() is { } root && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 50300)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 50300)]
    private bool ShiDaDyingObserver() => ActiveDying is { } dying && ShiDaObserverRoot() is { } root && _resolutionStack.FindIndex(x => x.Id == dying.Id) > _resolutionStack.FindIndex(x => x.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 50300)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 50300)]
    private bool ShiDaNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || ShiDaObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 50300)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 50300)]
    private bool ShiDaRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (ShiDaFirstChild(f, recovery)) return true;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && ShiDaFirstChild(f, _resolutionStack[parent + 1]) && ShiDaChildSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 50300)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 50300)]
    private bool KeepPaidShiDa(ProgramSkillFrame f) => f.ShiDengAi is { Paid: true } && ValidShiDa(f);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 50300)] private bool AdvanceShiDaChildren() => _pendingDecision is null && ShiDaObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
