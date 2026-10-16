namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,26520)]
    private void AssertDuFuRen(ProgramSkillFrame f)
    {
        if (f.DuFuRen is not { } r) return;
        var effect = GetPausedPrivateOfferEffect(f);
        if (f.TriggerId is null || f.InstructionIndex != 1 || r.InstructionIndex != f.InstructionIndex || r.Source != DuFuRenSource(f) || r.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.GameplayHash || effect is null || !DuFuRenComposition.IsOperation(effect.Op) ||
            f.WindowContext is not { } context || context.ParentFrameId != r.WindowId || r.Turn != _turnNumber || r.TurnOwner != _turnProgression.OwnerSeat ||
            !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<DuFuRenStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.WindowId == r.WindowId &&
                e.Turn == r.Turn && e.TurnOwner == r.TurnOwner && e.DamageFrameId == r.DamageFrameId && e.BatchId == r.BatchId) != 1)
            throw new InvalidOperationException("Du Fu Ren lost its owning instruction, actual turn, immutable start or payment interval.");
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,context.OccurrenceIndex);
        if (effect.Op == SkillProgramEffectOp.ResolveYiseOwnedGain)
        {
            if (r.Stage is not (DuFuRenStage.RedChoice or DuFuRenStage.RecoveryChildren) || r.CardId is not null || r.From is not null || r.GiftSequence != 0 ||
                r.DamageSourceSeat is not null || r.DamageFrameId is not null || r.RecipientSeat != context.TargetSeat || r.BatchId != context.MovementBatch?.Id ||
                CurrentDuFuRenGains(candidate,context) is not { } gains ||
                ProgramEventHistory<DuFuRenBlackIssuedEvent>().Count(e => e.FrameId == f.Id) != (gains.Any(g => g.Suit is Suit.Spade or Suit.Club) ? 1 : 0))
                throw new InvalidOperationException("Yise lost its exact original owned-gain batch and recipient.");
        }
        else
        {
            if (r.Stage is not (DuFuRenStage.GiftChoice or DuFuRenStage.GiftChildren) || r.BatchId is not null || r.DamageFrameId != context.DamageFrameId ||
                r.Stage == DuFuRenStage.GiftChoice && (r.CardId is not null || r.From is not null || r.GiftSequence != 0 || r.RecipientSeat is not null) ||
                context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
                    (_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == r.WindowId) is not { } prep ||
                     prep.CandidateIndex < 0 || prep.CandidateIndex >= prep.Candidates.Count || prep.Candidates[prep.CandidateIndex] != candidate) ||
                context.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                    (_resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == r.WindowId) is not { } damage || damage.ParentFrameId != r.DamageFrameId ||
                     damage.CandidateIndex < 0 || damage.CandidateIndex >= damage.Candidates.Count || damage.Candidates[damage.CandidateIndex].ToProgramCandidate() != candidate))
                throw new InvalidOperationException("Shunshi lost its exact preparation or actual after-damage parent candidate.");
            if (r.Stage == DuFuRenStage.GiftChildren && (r.RecipientSeat is not { } target || !IsValidPlayerSeat(target) || target == f.OwnerSeat || target == r.DamageSourceSeat ||
                r.CardId is not { } card || r.From is not { } from || from.OwnerSeat != f.OwnerSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                r.GiftSequence <= r.Before || r.GiftSequence > r.After ||
                _cardMovements.Count(m => m.Sequence == r.GiftSequence && m.CardId == card && m.From == from && m.To == CardLocation.Hand(target) && m.Reason.Value == DuFuRenGiftReason) != 1 ||
                ProgramEventHistory<DuFuRenGiftIssuedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.Turn == r.Turn &&
                    e.TurnOwner == r.TurnOwner && e.RecipientSeat == target && e.CardId == card && e.From == from && e.MovementSequence == r.GiftSequence) != 1))
                throw new InvalidOperationException("Shunshi lost its one genuine paid HE gift and issued independent phase benefits.");
        }
    }
    private bool IsDuFuRenRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
    {
        if (f.DuFuRen is not { } r || !RecoveryReplacementFrameRidesOn(recovery,f) || recovery.Return.Continuation != PostEventContinuation.AwaitedProgramMovement ||
            recovery.Attempt.SourceSeat != f.OwnerSeat || recovery.Attempt.Amount != 1) return false;
        return r.Stage == DuFuRenStage.RecoveryChildren && recovery.Attempt.TargetSeat == r.RecipientSeat && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program &&
            recovery.Attempt.Completion.InstructionIndex == 1 || r.Stage == DuFuRenStage.GiftChildren && r.From?.Zone == CardZoneKind.Equipment &&
            recovery.Attempt.TargetSeat == f.OwnerSeat && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == DuFuRenGiftReason &&
            _cardMovements.Any(m => m.Sequence == r.GiftSequence && m.CardId == r.CardId && m.CardKind == CardKind.SilverLion);
    }
    private bool DuFuRenFirstChild(ProgramSkillFrame root,ResolutionFrame child)
    {
        if (root.DuFuRen is not { } r || root.PendingMovementContinuation is null) return false;
        AssertDuFuRen(root);
        if (child is CardsMovedTriggerWindowFrame moved) return r.Stage == DuFuRenStage.GiftChildren && moved.Batch.ParentFrameId == root.Id &&
            (moved.ResumeProgramFrameId == root.Id || moved.Batch.AwaitingProgramFrameId == root.Id) && moved.Batch.OriginOwnerSeat == root.OwnerSeat &&
            moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId && moved.Batch.Movements is [var m] &&
            m.Sequence == r.GiftSequence && m.CardId == r.CardId && m.From == r.From && m.To == CardLocation.Hand(r.RecipientSeat!.Value) && m.Reason.Value == DuFuRenGiftReason;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == root.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return state.ResumeProgramFrameId == root.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == root.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        if (child is RecoveryReplacementFrame recovery) return IsDuFuRenRecoveryParent(root,recovery);
        if (child is not HpChangedTriggerWindowFrame hp || hp.Change.ParentFrameId != root.Id || hp.ResumeFrameId != root.Id || hp.Continuation != PostEventContinuation.AwaitedProgramMovement ||
            hp.Change.Kind != HpChangeKind.Recovery || hp.Change.Amount != 1 || hp.Change.SourceSeat != root.OwnerSeat) return false;
        return r.Stage == DuFuRenStage.RecoveryChildren && hp.Change.TargetSeat == r.RecipientSeat ||
            r.Stage == DuFuRenStage.GiftChildren && r.From?.Zone == CardZoneKind.Equipment && hp.Change.TargetSeat == root.OwnerSeat &&
            _cardMovements.Any(m => m.Sequence == r.GiftSequence && m.CardId == r.CardId && m.CardKind == CardKind.SilverLion);
    }
    private ProgramSkillFrame? DuFuRenObserverRoot() => FindProgramObserverRoot(static (e,root,child) => e.DuFuRenFirstChild(root,child),
        static (e,index) => e.HasObserverSuffix(index,static (engine,child) => engine.IsMovementObserverEdge(child) ||
            engine.IsYanPuDyingExitEdge(child) || RecoveryReplacementFrameRidesOn(engine._resolutionStack[child],engine._resolutionStack[child-1]),
            static (engine,child,dying) => engine.IsSuspendedOrPaidRescueObserverSuffix(child,dying)));
    private bool HasDuFuRenDamageObserver(long id) => DuFuRenObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsDuFuRenDying() => ActiveDying is { } dying && DuFuRenObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsDuFuRenNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || DuFuRenObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f,reference) : ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,26520)]
    private bool AdvanceDuFuRenSubtree() => _pendingDecision is null && DuFuRenObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
