namespace CardGame.Core;

public sealed partial class GameEngine
{
    private FuRongPayment? FuRongCurrentCost(ProgramSkillFrame f) => f.FuRongPair is { } r ?
        r.Stage == FuRongPairStage.OwnerChildren ? r.OwnerPayment : r.Stage == FuRongPairStage.TargetChildren ? r.TargetPayment : null : null;
    private bool FuRongFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.FuRongPair is not { } r || r.OwnerPayment is null) return false;
        if (r.Stage == FuRongPairStage.NativeUse && r.NativeReturn is null) return false;
        AssertFuRongPair(f);
        if (child is CardUseFrame use) return r.Stage == FuRongPairStage.NativeUse && r.NativeReturn is { } ret &&
            ret.ProgramFrameId == f.Id && ret.CardUseFrameId == use.Id && use.FuRongReturn == ret && use.SourceSeat == f.OwnerSeat &&
            use.CardId == ret.CardId && use.Action?.ActionId == ret.ActionId && use.PhysicalCardIds is [var physical] && physical == ret.CardId;
        if (child is CardsMovedTriggerWindowFrame moved)
        {
            if (moved.Batch.ParentFrameId != f.Id || moved.Batch.OriginOwnerSeat != f.OwnerSeat || moved.Batch.OriginSkillId != f.SkillId ||
                moved.Batch.OriginSkillInstanceId != f.SkillInstanceId || moved.Batch.Movements.Count == 0 ||
                moved.ResumeProgramFrameId != f.Id && moved.Batch.AwaitingProgramFrameId != f.Id) return false;
            if (FuRongCurrentCost(f) is { } paid)
                return moved.Batch.Movements is [var m] && _cardMovements.Contains(m) && m.Sequence == paid.Sequence &&
                    m.CardId == paid.CardId && m.From == paid.From && m.To == CardLocation.DiscardPile && m.Reason.Value == FuRongCostReason;
            if (r.Stage == FuRongPairStage.DrawChildren)
                return moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.DrawBefore && m.Sequence <= r.DrawAfter &&
                    (m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == "skill-program.fu-rong.xiaosi.compensation" ||
                     m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle));
            return r.Stage == FuRongPairStage.NativeChildren && r.NativeReturn is { } native && moved.Batch.Movements.All(m =>
                _cardMovements.Contains(m) && m.CardId == native.CardId && m.From == CardLocation.Processing);
        }
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills &&
            skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? FuRongObserverRoot() => FindProgramObserverRoot(
        static (e, root, child) => e.FuRongFirstChild(root, child),
        static (e, root) => e.HasObserverSuffix(root,
            static (engine, i) => engine.IsMovementObserverEdge(i) ||
                engine.RecoveryReplacementFrameRidesOn(engine._resolutionStack[i], engine._resolutionStack[i - 1]),
            static (engine, i, dying) => engine.IsSuspendedOrPaidRescueObserverSuffix(i, dying)));
    private bool HasFuRongDamageObserver(long id) => FuRongObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsFuRongDying() => ActiveDying is { } dying && FuRongObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsFuRongNestedDamage(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            FuRongObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26310)]
    private bool AdvanceFuRongSubtree() => _pendingDecision is null && FuRongObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
