namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool KeBiNengCardPaymentStage(KeBiNengPingduanReceipt r) => r.Stage is KeBiNengPingduanStage.RecastChildren or KeBiNengPingduanStage.EquipmentChildren;
    private CardMoveReason KeBiNengPaymentReason(KeBiNengPingduanReceipt r) => r.Stage == KeBiNengPingduanStage.RecastChildren ? CardMoveReasons.RecastDiscard : new(PingduanEquipmentReason);
    private bool IsKeBiNengRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) => f.KeBiNengPingduan is { SelectedCard: { } c } r && KeBiNengCardPaymentStage(r) && ValidKeBiNeng(f) &&
        c.From == CardLocation.Equipment(r.ActorSeat) && GetAttackCard(c.CardId).Kind == CardKind.SilverLion && recovery.ParentFrameId == f.Id &&
        recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.Program, f.Id) && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
        recovery.Attempt.Completion.MoveReason == KeBiNengPaymentReason(r) && recovery.Attempt.SourceSeat == r.ActorSeat && recovery.Attempt.TargetSeat == r.ActorSeat && recovery.Attempt.Amount == 1;
    private bool KeBiNengFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.KeBiNengPingduan is not { } r || !ValidKeBiNeng(f)) return false;
        if (child is CardsMovedTriggerWindowFrame moved)
        {
            if (moved.Batch.ParentFrameId != f.Id || moved.ResumeProgramFrameId != f.Id || moved.Batch.OriginOwnerSeat != f.OwnerSeat || moved.Batch.OriginSkillId != f.SkillId ||
                moved.Batch.OriginSkillInstanceId != f.SkillInstanceId || moved.Batch.Movements.Count == 0) return false;
            return moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After && (KeBiNengCardPaymentStage(r) ?
                r.SelectedCard is { } c && moved.Batch.Id == r.PaidBatchId && m.CardId == c.CardId && m.From == c.From && m.Reason == KeBiNengPaymentReason(r) &&
                    m.To == (r.Stage == KeBiNengPingduanStage.RecastChildren ? GetAttackCard(c.CardId).IsGeneralWeapon ? CardLocation.OutsideGame : CardLocation.DiscardPile : CardLocation.Hand(f.OwnerSeat)) ||
                moved.Batch.ParentBatchId == r.PaidBatchId && m.Reason == CardMoveReasons.WoodenOxGrainDiscard && m.From == CardLocation.WoodenOxGrain(r.ActorSeat) && m.To == CardLocation.DiscardPile && r.WoodenOxGrainIds.Contains(m.CardId) :
                r.Stage is KeBiNengPingduanStage.BasicRewardChildren or KeBiNengPingduanStage.RecastDrawChildren or KeBiNengPingduanStage.RecastRewardChildren or KeBiNengPingduanStage.EquipmentRewardChildren &&
                    (m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(r.ActorSeat) && m.Reason == (r.Stage == KeBiNengPingduanStage.RecastDrawChildren ? CardMoveReasons.RecastDraw : new CardMoveReason(PingduanRewardReason)) || m.Reason == CardMoveReasons.Reshuffle)));
        }
        if (child is RecoveryReplacementFrame recovery) return IsKeBiNengRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp) return KeBiNengCardPaymentStage(r) && r.SelectedCard is { } c && c.From == CardLocation.Equipment(r.ActorSeat) && GetAttackCard(c.CardId).Kind == CardKind.SilverLion &&
            hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.Program && hp.Change.ParentFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery &&
            hp.Change.SourceSeat == r.ActorSeat && hp.Change.TargetSeat == r.ActorSeat && hp.Change.Amount == 1;
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills && skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? KeBiNengObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.KeBiNengFirstChild(f, c),
        static (e, i) => e.HasObserverSuffix(i, static (x, n) => x.IsMovementObserverEdge(n) || RecoveryReplacementFrameRidesOn(x._resolutionStack[n], x._resolutionStack[n - 1]),
            static (x, n, d) => x.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    private bool HasKeBiNengDamageObserver(long id) => KeBiNengObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasKeBiNengBeforeDamageObserver(long id) => HasKeBiNengDamageObserver(id);
    private bool IsKeBiNengDying() => ActiveDying is { } dying && KeBiNengObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsKeBiNengNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || KeBiNengObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f); return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 27040)]
    private bool AdvanceKeBiNengSubtree() => _pendingDecision is null && KeBiNengObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
