namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 27030)]
    private void AssertZhuJunCuipo(ProgramSkillFrame f)
    {
        if (f.ZhuJunCuipoDraw is not { } r) return;
        var q = r.Qualification;
        if (f.InstructionIndex != 1 || r.InstructionIndex != f.InstructionIndex || f.SkillId != q.Source.SkillId || f.SkillInstanceId != q.Source.SkillInstanceId || f.OwnerSeat != q.Source.OwnerSeat || f.TriggerId != q.Source.BindingId ||
            f.GameplayHash != q.GameplayHash || q.Damage || q.Ordinal != q.NameLength || q.NameLength != ZhuJunCardNameLength(q.EffectiveKind) || q.Turn != _turnNumber || q.TurnOwner != _currentSeat ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCommitted, CardUse: { } card } c || c.ParentFrameId != r.WindowId || c.OwnerSeat != f.OwnerSeat || card.ActorSeat != f.OwnerSeat || card.EffectiveKind != q.EffectiveKind ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == r.WindowId) is not { } window || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(f, ToSharedCandidate(window.Candidates[window.CandidateIndex])) || window.Action.ActionId != card.CardActionId || window.Action.ActorSeat != f.OwnerSeat ||
            q.ActionId is { } actionId && card.CardActionId != actionId || q.ActionId is null && (LifecycleCardUse(q.OriginFrameId) is not { } legacy || legacy.Action?.ActionId != card.CardActionId || !IsExactLegacyActualUseCompletion(legacy)) ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveCuipoActualTurnUse || f.PendingMovementContinuation is null || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.ActualCount is < 0 or > 1 ||
            ProgramEventHistory<ZhuJunCuipoQualifiedEvent>().Count(e => e == q) != 1 || ProgramEventHistory<ZhuJunCuipoDrawIssuedEvent>().Count(e => e.FrameId == f.Id && e.WindowId == r.WindowId && e.OriginFrameId == q.OriginFrameId && e.ActionId == q.ActionId && e.Source == q.Source && e.Before == r.Before && e.After == r.After && e.ActualCount == r.ActualCount) != 1 ||
            _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == ZhuJunDrawReason) != r.ActualCount ||
            _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After).Any(m => !IsZhuJunDrawMovement(f, m)))
            throw new InvalidOperationException("Cuipo lost its exact committed actor, frozen ordinal or once-issued native draw interval.");
    }
    private bool IsZhuJunDrawMovement(ProgramSkillFrame f, CardMovementRecord m) => f.ZhuJunCuipoDraw is { } r && _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After &&
        (m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == ZhuJunDrawReason || m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle);
    private bool IsZhuJunMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.ZhuJunCuipoDraw is not null && effect?.Op == SkillProgramEffectOp.ResolveCuipoActualTurnUse && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool ZhuJunFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.ZhuJunCuipoDraw is not { } r || f.PendingMovementContinuation is null) return false;
        AssertZhuJunCuipo(f);
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => IsZhuJunDrawMovement(f, m));
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills) return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        return child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.ResumeProgramFrameId == f.Id &&
            ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
    }
    private ProgramSkillFrame? ZhuJunObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.ZhuJunFirstChild(f, c), static (e, i) => e.HasObserverSuffix(i,
        static (engine, child) => engine.IsMovementObserverEdge(child) || engine.IsYanPuDyingExitEdge(child) || RecoveryReplacementFrameRidesOn(engine._resolutionStack[child], engine._resolutionStack[child - 1]),
        static (engine, child, dying) => engine.IsSuspendedOrPaidRescueObserverSuffix(child, dying)));
    private bool HasZhuJunDamageObserver(long id) => ZhuJunObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsZhuJunDying() => IsZhuJunLegacyAlcoholPreRecoveryRide() || ActiveDying is { } dying && ZhuJunObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsZhuJunNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || ZhuJunObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 27030)] private bool AdvanceZhuJunSubtree() => _pendingDecision is null && ZhuJunObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
