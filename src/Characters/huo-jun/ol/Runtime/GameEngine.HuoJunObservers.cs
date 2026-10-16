namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsHuoJunFenruiRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) =>
        f.HuoJunFenrui is { Stage: HuoJunFenruiStage.CostChildren, CostCardId: { } id, CostFrom: { Zone: CardZoneKind.Equipment } } r &&
        GetAdvancedCard(id).Kind == CardKind.SilverLion && recovery.ParentFrameId == f.Id && recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.Program, f.Id) &&
        recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason == new CardMoveReason(HuoJunDiscardReason) &&
        recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == f.OwnerSeat && recovery.Attempt.Amount == 1 &&
        ProgramEventHistory<HuoJunFenruiCostPaidEvent>().Any(e => e.FrameId == f.Id && e.Source == r.Source && e.CardId == id);
    private bool HuoJunFenruiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.HuoJunFenrui is not { CostCardId: not null } r) return false;
        AssertHuoJunFenrui(f);
        if (child is CardUseFrame) return IsHuoJunFenruiNativeEquipment(child, f);
        if (child is CardsMovedTriggerWindowFrame moved)
            return r.Stage == HuoJunFenruiStage.CostChildren && moved.ResumeProgramFrameId == f.Id && moved.Batch.ParentFrameId == f.Id &&
                moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements is [var m] && m.CardId == r.CostCardId && m.From == r.CostFrom && m.To == CardLocation.DiscardPile &&
                m.Reason.Value == HuoJunDiscardReason && m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter && _cardMovements.Contains(m);
        if (child is RecoveryReplacementFrame recovery) return IsHuoJunFenruiRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp)
            return r.Stage == HuoJunFenruiStage.CostChildren && r.CostCardId is { } id && GetAdvancedCard(id).Kind == CardKind.SilverLion &&
                hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.Program &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.Amount == 1 && hp.Change.TargetSeat == f.OwnerSeat;
        if (child is BeforeDamageProgramWindowFrame before)
            return r.Stage == HuoJunFenruiStage.DamageIssued && f.AttackAttempt is not null && (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == f.Id;
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills &&
            skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && CanContinuePaidHuoJunFenrui(f);
    }
    private bool HuoJunFenruiObserverSuffix(int root) => HasObserverSuffix(root,
        static (e, i) => e.IsMovementObserverEdge(i) || e.RandomEquipmentFrameRidesOn(e._resolutionStack[i], e._resolutionStack[i - 1]) ||
            RecoveryReplacementFrameRidesOn(e._resolutionStack[i], e._resolutionStack[i - 1]),
        static (e, i, dying) => e.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? HuoJunFenruiObserverRoot() => FindProgramObserverRoot(
        static (e, f, child) => e.HuoJunFenruiFirstChild(f, child), static (e, i) => e.HuoJunFenruiObserverSuffix(i));
    private bool HasHuoJunFenruiDamageObserver(long id) => HuoJunFenruiObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsHuoJunFenruiDying() => ActiveDying is { } dying && HuoJunFenruiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsHuoJunFenruiNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || HuoJunFenruiObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26210)]
    private bool AdvanceHuoJunFenruiSubtree() => _pendingDecision is null && HuoJunFenruiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
