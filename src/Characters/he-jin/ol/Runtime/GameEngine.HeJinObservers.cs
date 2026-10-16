namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsHeJinRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) =>
        f.HeJinYanhuo is { Stage: HeJinYanhuoStage.DiscardChildren } r && ValidHeJinYanhuo(f) &&
        r.SelectedCards.Any(s => s.From == CardLocation.Equipment(r.KillerSeat) && GetAttackCard(s.CardId).Kind == CardKind.SilverLion) &&
        recovery.ParentFrameId == f.Id && recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.Program, f.Id) &&
        recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == HeJinDiscardReason &&
        recovery.Attempt.SourceSeat == r.KillerSeat && recovery.Attempt.TargetSeat == r.KillerSeat && recovery.Attempt.Amount == 1;
    private bool HeJinFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.HeJinMouzhu is { Stage: HeJinMouzhuStage.GiftChildren } gift && ValidHeJinMouzhu(f) && child is CardsMovedTriggerWindowFrame gained)
            return gained.Batch.ParentFrameId == f.Id && gained.ResumeProgramFrameId == f.Id && gained.Batch.OriginOwnerSeat == f.OwnerSeat &&
                gained.Batch.OriginSkillId == f.SkillId && gained.Batch.OriginSkillInstanceId == f.SkillInstanceId && gained.Batch.Movements is [var m] &&
                m.CardId == gift.GiftId && m.From == CardLocation.Hand(gift.ActorSeat) && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == HeJinGiftReason &&
                m.Sequence > gift.Before && m.Sequence <= gift.After && _cardMovements.Contains(m);
        if (f.HeJinYanhuo is { Stage: HeJinYanhuoStage.DiscardChildren } r && ValidHeJinYanhuo(f))
        {
            if (child is CardsMovedTriggerWindowFrame moved)
                return moved.Batch.ParentFrameId == f.Id && moved.ResumeProgramFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
                    moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                    moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After &&
                        (m.Reason.Value == HeJinDiscardReason && r.SelectedCards.Any(s => s.CardId == m.CardId && s.From == m.From &&
                            m.To == (s.From.Zone == CardZoneKind.Equipment && GetAttackCard(s.CardId).IsGeneralWeapon ? CardLocation.OutsideGame : CardLocation.DiscardPile)) ||
                         m.Reason == CardMoveReasons.WoodenOxGrainDiscard && m.From == CardLocation.WoodenOxGrain(r.KillerSeat) && m.To == CardLocation.DiscardPile &&
                            r.SelectedCards.Any(s => s.From == CardLocation.Equipment(r.KillerSeat) && GetAttackCard(s.CardId).Kind == CardKind.WoodenOx)));
            if (child is RecoveryReplacementFrame recovery) return IsHeJinRecoveryParent(f, recovery);
            if (child is HpChangedTriggerWindowFrame hp)
                return hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.Program && hp.Change.ParentFrameId == f.Id &&
                    hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == r.KillerSeat && hp.Change.TargetSeat == r.KillerSeat && hp.Change.Amount == 1 &&
                    r.SelectedCards.Any(s => s.From == CardLocation.Equipment(r.KillerSeat) && GetAttackCard(s.CardId).Kind == CardKind.SilverLion);
        }
        return (f.HeJinMouzhu is { Stage: not HeJinMouzhuStage.ChoosingGift } && ValidHeJinMouzhu(f) || f.HeJinYanhuo is not null && ValidHeJinYanhuo(f)) &&
            child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills &&
            skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? HeJinObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.HeJinFirstChild(f, c),
        static (e, i) => e.HasObserverSuffix(i, static (x, n) => x.IsMovementObserverEdge(n) || RecoveryReplacementFrameRidesOn(x._resolutionStack[n], x._resolutionStack[n - 1]),
            static (x, n, d) => x.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    private bool HasHeJinDamageObserver(long id) => HeJinObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsHeJinDying() => ActiveDying is { } dying && HeJinObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsHeJinNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || HeJinObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26710)]
    private bool AdvanceHeJinSubtree() => _pendingDecision is null && HeJinObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
