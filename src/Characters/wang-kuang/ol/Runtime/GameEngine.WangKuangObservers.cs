namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsWangKuangRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) => f.WangKuangRenxia is { Stage: WangKuangRenxiaStage.MovementChildren, Branch: WangKuangRenxiaBranch.Discard } r &&
        ValidWangKuang(f) && r.Selected.Any(c => c.From == CardLocation.Equipment(f.OwnerSeat) && GetAttackCard(c.CardId).Kind == CardKind.SilverLion) &&
        recovery.ParentFrameId == f.Id && recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.Program, f.Id) &&
        recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == RenxiaDiscardReason &&
        recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == f.OwnerSeat && recovery.Attempt.Amount == 1;
    private bool WangKuangFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.WangKuangRenxia is not { Stage: WangKuangRenxiaStage.MovementChildren } r || !ValidWangKuang(f)) return false;
        if (child is CardsMovedTriggerWindowFrame moved) return moved.Batch.ParentFrameId == f.Id && moved.ResumeProgramFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
            moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                m.Sequence > r.Before && m.Sequence <= r.After && (r.Branch == WangKuangRenxiaBranch.Draw ?
                    m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == RenxiaDrawReason || m.Reason == CardMoveReasons.Reshuffle :
                    m.Reason.Value == RenxiaDiscardReason && r.Selected.Any(c => c.CardId == m.CardId && c.From == m.From &&
                        m.To == (c.From.Zone == CardZoneKind.Equipment && GetAttackCard(c.CardId).IsGeneralWeapon ? CardLocation.OutsideGame : CardLocation.DiscardPile)) ||
                    m.Reason == CardMoveReasons.WoodenOxGrainDiscard && m.From == CardLocation.WoodenOxGrain(f.OwnerSeat) && m.To == CardLocation.DiscardPile && r.WoodenOxGrainIds.Contains(m.CardId)));
        if (child is RecoveryReplacementFrame recovery) return IsWangKuangRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp) return r.Branch == WangKuangRenxiaBranch.Discard && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.Program &&
            hp.Change.ParentFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == f.OwnerSeat && hp.Change.TargetSeat == f.OwnerSeat && hp.Change.Amount == 1 &&
            r.Selected.Any(c => c.From == CardLocation.Equipment(f.OwnerSeat) && GetAttackCard(c.CardId).Kind == CardKind.SilverLion);
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills && skills.ResumeProgramFrameId == f.Id &&
            skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? WangKuangObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.WangKuangFirstChild(f, c),
        static (e, i) => e.HasObserverSuffix(i, static (x, n) => x.IsMovementObserverEdge(n) || RecoveryReplacementFrameRidesOn(x._resolutionStack[n], x._resolutionStack[n - 1]),
            static (x, n, d) => x.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    private bool HasWangKuangDamageObserver(long id) => WangKuangObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsWangKuangDying() => ActiveDying is { } dying && WangKuangObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsWangKuangNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || WangKuangObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26910)]
    private bool AdvanceWangKuangSubtree() => _pendingDecision is null && WangKuangObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
