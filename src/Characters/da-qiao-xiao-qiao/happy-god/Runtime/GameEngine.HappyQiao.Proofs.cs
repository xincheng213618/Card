namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 42400)] private void RequireHappyQiaoSkill(ProgramSkillFrame frame)
    {
        if (frame.HappyQiao is not { } receipt) return;
        if (frame.InstructionIndex != 1 || !HappyQiaoOriginalHealthCandidate(frame) || receipt.Source != HappyQiaoSource(frame) || receipt.Hash != frame.GameplayHash ||
            receipt.Parent != frame.WindowContext!.ParentFrameId || receipt.Change != frame.WindowContext.HpChange || receipt.Turn != _turnNumber || !HasHappyQiaoHealth(frame.OwnerSeat) ||
            !Enum.IsDefined(receipt.Stage) || receipt.Before < 0 || receipt.After < receipt.Before || receipt.After > _movementSequence ||
            ProgramEventHistory<HappyQiaoStartedEvent>().Count(e => e == new HappyQiaoStartedEvent(frame.Id, receipt.Source, receipt.Hash, receipt.Operation, receipt.Parent, receipt.Turn, receipt.Change.Id)) != 1 ||
            receipt.Operation == SkillProgramEffectOp.ResolveHappyQiaoShutu && (frame.SkillId != HappyQiaoComposition.Shutu || !HappyQiaoShutuChange(frame.OwnerSeat, receipt.Change)) ||
            receipt.Operation == SkillProgramEffectOp.ResolveHappyQiaoTongxin && (frame.SkillId != HappyQiaoComposition.Tongxin || !HappyQiaoRedQualification(frame.OwnerSeat, frame.SkillInstanceId, receipt.Change)))
            throw new InvalidOperationException("Happy Qiao lost her original accepted health window, selected sister, real source instance or native payment boundary.");
        if (receipt.Gained.Distinct().Count() != receipt.Gained.Count || receipt.Gained.Count > 2 || receipt.Suits.Any(s => s is not (Suit.Heart or Suit.Diamond)) ||
            receipt.Gained.Count > 0 && receipt.Suits.Count != 2 ||
            receipt.Stage == HappyQiaoStage.GainChildren && receipt.Gained.Any(id => !_cardMovements.Any(m => m.Sequence > receipt.Before && m.Sequence <= receipt.After && m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(frame.OwnerSeat) && m.Reason.Value == "skill.happy.shutu.chosen-red")) ||
            receipt.CostBatch is not null && (receipt.CostFrom is null ||
                ProgramEventHistory<HappyQiaoCostPaidEvent>().Count(e => e.FrameId == frame.Id && e.BatchId == receipt.CostBatch && e.CardId == receipt.CardId && e.From == receipt.CostFrom && e.Sequence == receipt.After &&
                    _cardMovements.Any(m => m.Sequence == e.Sequence && m.CardId == e.CardId && m.To == e.To && (m.From == e.From || GetProgramDiscardSource(m) == e.From))) != 1))
            throw new InvalidOperationException("Happy Qiao lost the original real selected red deck gain or once-paid logical discard.");
        if (receipt.Native is { } native && (native.FrameId != frame.Id || native.Instruction != frame.InstructionIndex || native.Source != receipt.Source || native.Hash != frame.GameplayHash || native.Turn != receipt.Turn ||
            native.Plan.Cards.Distinct().Count() != native.Plan.Cards.Count || native.Costs.Count != native.Plan.Cards.Count || native.Costs.Any(c => !native.Plan.Cards.Contains(c.CardId))))
            throw new InvalidOperationException("Shutu lost its original owned material set and typed native Use intent.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 42400)]
    private bool HappyQiaoBoundMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation continuation)
    {
        if (frame.HappyQiao is not { Stage: HappyQiaoStage.GainChildren or HappyQiaoStage.DiscardChildren } ||
            effect?.Op is not (SkillProgramEffectOp.ResolveHappyQiaoShutu or SkillProgramEffectOp.ResolveHappyQiaoTongxin) || continuation != frame.PendingMovementContinuation ||
            continuation.SubjectSeat != frame.OwnerSeat || continuation.BeforeCount != 0 || continuation.CoverageResultBind is not null) return false;
        RequireHappyQiaoSkill(frame); return true;
    }
    private bool HappyQiaoFirstSkillChild(ProgramSkillFrame frame, ResolutionFrame child)
    {
        if (frame.HappyQiao is not { } receipt) return false;
        RequireHappyQiaoSkill(frame);
        if (child is SkillInvocationFrame invocation) return receipt.Operation == SkillProgramEffectOp.ResolveHappyQiaoTongxin &&
            receipt.Stage is HappyQiaoStage.HealChildren or HappyQiaoStage.DiscardChildren && invocation.ActorSeat == frame.OwnerSeat && invocation.SkillKey == frame.SkillId &&
            invocation.SkillInstanceId == frame.SkillInstanceId && invocation.OccurrenceKey == "tongxin:" + frame.Id && SkillInvocationStructuralEdge(frame, invocation);
        if (child is CardUseFrame use) return receipt.Stage is HappyQiaoStage.NativeUse or HappyQiaoStage.ReturnedUse && HappyQiaoNativeUseRidesOn(use, frame);
        if (child is CardDeclarationFrame declaration) return receipt.Stage == HappyQiaoStage.Declaration && MatchesHappyQiaoShutuDeclaration(frame, declaration);
        if (child is HappyQiaoHealthChoiceFrame health) return HappyQiaoHealthChoiceRidesOn(health, frame);
        if (child is RecoveryReplacementFrame recovery) return receipt.Stage == HappyQiaoStage.HealChildren &&
            recovery.Attempt.SourceSeat == frame.OwnerSeat && recovery.Attempt.TargetSeat == frame.OwnerSeat && recovery.Attempt.Amount == 1 &&
            recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program && recovery.Return.Continuation == PostEventContinuation.Program && RecoveryReplacementFrameRidesOn(recovery, frame);
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == frame.Id && hp.Change.ParentFrameId == frame.Id &&
            (receipt.Stage == HappyQiaoStage.HealChildren && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == frame.OwnerSeat ||
             receipt.Stage == HappyQiaoStage.DiscardChildren && hp.Change.TargetSeat == frame.OwnerSeat) &&
            hp.Continuation is PostEventContinuation.Program or PostEventContinuation.AwaitedProgramMovement;
        if (child is CardsMovedTriggerWindowFrame moved) return receipt.Stage is HappyQiaoStage.GainChildren or HappyQiaoStage.DiscardChildren &&
            frame.PendingMovementContinuation is { } continuation && HappyQiaoBoundMovement(frame, GetPausedPrivateOfferEffect(frame), continuation) &&
            ProgramMovementReturnMatches(frame, moved) && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => m.Sequence > receipt.Before && m.Sequence <= receipt.After && _cardMovements.Contains(m));
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills && skills.ResumeProgramFrameId == frame.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? HappyQiaoSkillObserverRoot() => FindProgramObserverRoot(static (engine, frame, child) => engine.HappyQiaoFirstSkillChild(frame, child), static (engine, index) => engine.SameNameHandObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 42400)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 42400)]
    private bool HappyQiaoSkillDamageObserver(long id) => HappyQiaoSkillObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 42400)]
    private bool HappyQiaoSkillBeforeDamageObserver(long id) => HappyQiaoSkillObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 42400)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 42400)]
    private bool HappyQiaoSkillDying() => ActiveDying is { } dying && HappyQiaoSkillObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 42400)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 42400)]
    private bool HappyQiaoSkillRecoveryParent(ProgramSkillFrame frame, RecoveryReplacementFrame recovery)
    {
        if (frame.HappyQiao is null) return false;
        var root = _resolutionStack.FindIndex(f => f.Id == frame.Id); var child = _resolutionStack.FindIndex(f => f.Id == recovery.Id);
        return root >= 0 && child > root && HappyQiaoFirstSkillChild(frame, _resolutionStack[root + 1]) &&
            (child == root + 1 || Enumerable.Range(root + 2, child - root - 1).All(SameNameHandObserverSuffix)) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 42400)]
    private bool AdvanceHappyQiaoSkillObservers() => _pendingDecision is null && HappyQiaoSkillObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 42400)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 42400)]
    private bool HappyQiaoNestedProgramDamage(ProgramSkillFrame frame, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || frame.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != frame.Id) return false;
        if (frame.HappyQiao is { Operation: SkillProgramEffectOp.ResolveHappyQiaoTongxin, Stage: HappyQiaoStage.DamageChildren, CostBatch: not null } receipt)
        {
            RequireHappyQiaoSkill(frame);
            return source is null && nature is null && target == receipt.Target && amount == 1 &&
                ProgramEventHistory<HappyQiaoCostPaidEvent>().Count(e => e.FrameId == frame.Id && e.BatchId == receipt.CostBatch && e.CardId == receipt.CardId && e.Sequence == receipt.After) == 1;
        }
        if (HappyQiaoSkillObserverRoot() is not { } root || root.Id == frame.Id) return false;
        var effect = GetPausedPrivateOfferEffect(frame);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(frame, reference) : ResolveProgramEffectTarget(frame, effect.Target));
    }
}
