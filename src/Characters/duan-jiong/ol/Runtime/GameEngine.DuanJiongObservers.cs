namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsDuanJiongRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) => f.DuanJiongSaogu is { } r &&
        r.Stage is DuanJiongSaoguStage.DelegateChildren or DuanJiongSaoguStage.DiscardChildren && ValidSaogu(f) &&
        r.Selected.Any(c => c.From == CardLocation.Equipment(r.Stage == DuanJiongSaoguStage.DelegateChildren ? f.OwnerSeat : r.ActorSeat) && c.Kind == CardKind.SilverLion) &&
        recovery.ParentFrameId == f.Id && recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.Program, f.Id) &&
        recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == SaoguDiscardReason &&
        recovery.Attempt.SourceSeat == (r.Stage == DuanJiongSaoguStage.DelegateChildren ? f.OwnerSeat : r.ActorSeat) && recovery.Attempt.TargetSeat == recovery.Attempt.SourceSeat && recovery.Attempt.Amount == 1;
    private bool SaoguFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.DuanJiongSaogu is not { Paid: true } r || !ValidSaogu(f)) return false;
        if (child is CardUseFrame use) return r.Stage == DuanJiongSaoguStage.NativeUse && r.NativeUse is { } native && use.DuanJiongSaoguUse == native && use.Id == native.CardUseFrameId &&
            use.Action?.ActionId == native.ActionId && use.CardId == native.CardId && ShownEntityUseActorMatches(use, native.ActorSeat, native.ActorSeat);
        if (child is CardsMovedTriggerWindowFrame moved)
        {
            if (moved.Batch.ParentFrameId != f.Id || moved.ResumeProgramFrameId != f.Id || moved.Batch.OriginOwnerSeat != f.OwnerSeat || moved.Batch.OriginSkillId != f.SkillId ||
                moved.Batch.OriginSkillInstanceId != f.SkillInstanceId || moved.Batch.Movements.Count == 0) return false;
            if (r.Stage is DuanJiongSaoguStage.DelegateChildren or DuanJiongSaoguStage.DiscardChildren)
            {
                var actor = r.Stage == DuanJiongSaoguStage.DelegateChildren ? f.OwnerSeat : r.ActorSeat;
                return moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After &&
                    (moved.Batch.Id == r.PaidBatchId && r.Selected.Any(c => c.CardId == m.CardId && c.From == m.From) && m.To == CardLocation.DiscardPile && m.Reason.Value == SaoguDiscardReason ||
                    moved.Batch.ParentBatchId == r.PaidBatchId && m.From == CardLocation.WoodenOxGrain(actor) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard && r.WoodenOxGrainIds.Contains(m.CardId)));
            }
            if (r.Stage == DuanJiongSaoguStage.DrawChildren) return moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After &&
                (m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(r.ActorSeat) && m.Reason.Value == SaoguDrawReason || m.Reason == CardMoveReasons.Reshuffle));
            return r.Stage == DuanJiongSaoguStage.NativeChildren && r.NativeUse is { } used && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.CardId == used.CardId && m.From == CardLocation.Processing && m.Reason == CardMoveReasons.UseFinished);
        }
        if (child is RecoveryReplacementFrame recovery) return IsDuanJiongRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp) return r.Stage is DuanJiongSaoguStage.DelegateChildren or DuanJiongSaoguStage.DiscardChildren && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.Program &&
            hp.Change.ParentFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == hp.Change.TargetSeat && hp.Change.Amount == 1 &&
            r.Selected.Any(c => c.From == CardLocation.Equipment(hp.Change.TargetSeat) && c.Kind == CardKind.SilverLion);
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills && skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? SaoguObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.SaoguFirstChild(f, c),
        static (e, i) => e.HasObserverSuffix(i, static (x, n) => x.IsMovementObserverEdge(n) || x.RecoveryReplacementFrameRidesOn(x._resolutionStack[n], x._resolutionStack[n - 1]),
            static (x, n, d) => x.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    private bool HasDuanJiongDamageObserver(long id) => SaoguObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasDuanJiongBeforeDamageObserver(long id) => HasDuanJiongDamageObserver(id);
    private bool IsDuanJiongDying() => ActiveDying is { } dying && SaoguObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsDuanJiongNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || SaoguObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 27060)]
    private bool AdvanceSaoguSubtree() => _pendingDecision is null && SaoguObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
