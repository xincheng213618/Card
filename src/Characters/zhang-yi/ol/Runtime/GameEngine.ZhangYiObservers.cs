namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26400)]
    private void AssertZhangYi(ProgramSkillFrame f)
    {
        if (f.ZhangYi is not { } r) return;
        var dianjun = f.SkillId == "ol:dianjun";
        if (f.SkillId is not ("ol:dianjun" or "ol:kangrui") || r.Source != ZhangYiSource(f) || r.GameplayHash != f.GameplayHash ||
            f.WindowContext is not { } c || c.ParentFrameId != r.ParentWindowId || c.TargetSeat != r.RecipientSeat ||
            r.ActualTurnNumber != _turnNumber || r.InstructionIndex != f.InstructionIndex || f.InstructionIndex != 1 || !Enum.IsDefined(r.Stage) ||
            r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore || r.SequenceAfter > _movementSequence ||
            !ContinuePaidZhangYi(f) || dianjun && (c.Window != SkillProgramTriggerWindow.AfterTurnEnded || r.RecipientSeat != f.OwnerSeat ||
                r.DamageFrameId is not null || r.Stage is not (ZhangYiStage.DianjunDamage or ZhangYiStage.DianjunExtraPlay or ZhangYiStage.DianjunReturned or ZhangYiStage.DianjunCleanupChildren)) ||
            !dianjun && (c.Window != SkillProgramTriggerWindow.AfterDamageApplied || c.DamageFrameId != r.DamageFrameId || r.DamageFrameId is null ||
                r.Stage is not (ZhangYiStage.KangruiDraw or ZhangYiStage.KangruiChoice or ZhangYiStage.KangruiRecovery) ||
                !ProgramEventHistory<ZhangYiActualDamageFactEvent>().Any(e => e.DamageFrameId == r.DamageFrameId && e.ActualTurnNumber == r.ActualTurnNumber &&
                    e.TargetSeat == r.RecipientSeat && e.TargetSeat == e.ActualTurnOwnerSeat)))
            throw new InvalidOperationException("Zhang Yi lost its exact issued end/first-damage receipt, recipient, source or turn.");
        if (!dianjun && ProgramEventHistory<ZhangYiKangruiDrawIssuedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
            e.RecipientSeat == r.RecipientSeat && e.ActualTurnNumber == r.ActualTurnNumber && e.SequenceBefore == r.SequenceBefore) != 1)
            throw new InvalidOperationException("Kangrui cannot repeat or fabricate its actual draw payment.");
        if (!dianjun && r.Stage is ZhangYiStage.KangruiChoice or ZhangYiStage.KangruiRecovery)
        {
            var drawn = _cardMovements.Where(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter && m.Reason.Value == ZhangYiDrawReason).ToArray();
            if (drawn.Length > 1 || drawn.Any(m => m.From != CardLocation.DrawPile || m.To != CardLocation.Hand(f.OwnerSeat)))
                throw new InvalidOperationException("Kangrui draw lost its original one-card native movement.");
        }
        if (r.Stage == ZhangYiStage.KangruiRecovery && (r.Choice is not ("recover" or "damage") || r.RecoveryRequested is < 0 or > 1 ||
            r.Choice == "damage" && r.RecoveryRequested != 0 || ProgramEventHistory<ZhangYiKangruiChoiceEvent>().Count(e => e.FrameId == f.Id &&
                e.Source == r.Source && e.RecipientSeat == r.RecipientSeat && e.ActualTurnNumber == r.ActualTurnNumber && e.Choice == r.Choice && e.RecoveryRequested == r.RecoveryRequested) != 1))
            throw new InvalidOperationException("Kangrui lost its one committed option and frozen recovery request.");
        if (r.Stage == ZhangYiStage.DianjunCleanupChildren && (r.CleanupBefore < 0 || r.CleanupAfter < r.CleanupBefore || r.CleanupAfter > _movementSequence ||
            ProgramEventHistory<ZhangYiDianjunCleanupStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.ActualTurnNumber == r.ActualTurnNumber && e.SequenceBefore == r.CleanupBefore) != 1 ||
            ProgramEventHistory<ZhangYiDianjunCleanupIssuedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.ActualTurnNumber == r.ActualTurnNumber &&
                e.SequenceBefore == r.CleanupBefore && e.SequenceAfter == r.CleanupAfter) != 1))
            throw new InvalidOperationException("Dianjun lost its once-issued native actual-end cleanup receipt.");
    }

    private bool IsZhangYiRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) =>
        f.ZhangYi is { Stage: ZhangYiStage.KangruiRecovery, Choice: "recover", RecoveryRequested: > 0 } r && ContinuePaidZhangYi(f) &&
        recovery.ParentFrameId == f.Id && recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.Program, f.Id) &&
        recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == r.RecipientSeat && recovery.Attempt.Amount == r.RecoveryRequested &&
        recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program && recovery.Attempt.Completion.InstructionIndex == r.InstructionIndex;

    private bool ZhangYiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.ZhangYi is not { } r) return false;
        AssertZhangYi(f);
        if (child is RecoveryReplacementFrame recovery) return IsZhangYiRecoveryParent(f, recovery);
        if (child is CardsMovedTriggerWindowFrame moved)
            return r.Stage == ZhangYiStage.DianjunCleanupChildren && moved.ResumeProgramFrameId == f.Id && moved.Batch.ParentFrameId == f.Id &&
                moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => m.Sequence > r.CleanupBefore && m.Sequence <= r.CleanupAfter && _cardMovements.Contains(m)) ||
                r.Stage == ZhangYiStage.KangruiDraw && moved.ResumeProgramFrameId == f.Id && moved.Batch.ParentFrameId == f.Id &&
                moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.Count == 1 && moved.Batch.Movements.All(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) &&
                    m.Reason.Value == ZhangYiDrawReason && m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter && _cardMovements.Contains(m));
        if (child is HpChangedTriggerWindowFrame hp)
            return r.Stage == ZhangYiStage.DianjunCleanupChildren && hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.Program ||
                r.Stage == ZhangYiStage.KangruiRecovery && hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id &&
                hp.Continuation == PostEventContinuation.Program && hp.Change.TargetSeat == r.RecipientSeat && hp.Change.SourceSeat == f.OwnerSeat &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.Amount > 0;
        if (child is BeforeDamageProgramWindowFrame before)
            return r.Stage == ZhangYiStage.DianjunDamage && f.AttackAttempt is { SourceLess: true } &&
                (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == f.Id && before.TargetSeat == f.OwnerSeat;
        if (child is DamageFrame damage)
            return r.Stage == ZhangYiStage.DianjunDamage && f.AttackAttempt is { SourceLess: true } && damage.ParentFrameId == f.Id && damage.TargetSeat == f.OwnerSeat;
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills &&
            skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && ContinuePaidZhangYi(f);
    }
    private bool ZhangYiObserverSuffix(int root) => HasObserverSuffix(root,
        static (e, i) => e.IsMovementObserverEdge(i) || e.RecoveryReplacementFrameRidesOn(e._resolutionStack[i], e._resolutionStack[i - 1]) ||
            e.PaidTargetObserverEdge(i), static (e, i, dying) => e.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? ZhangYiObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.ZhangYiFirstChild(f, child), static (e, i) => e.ZhangYiObserverSuffix(i));
    private bool HasZhangYiDamageObserver(long id) => ZhangYiObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsZhangYiDying() => ActiveDying is { } dying && ZhangYiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsZhangYiNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || ZhangYiObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26400)]
    private bool AdvanceZhangYiSubtree() => _pendingDecision is null && ZhangYiObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
