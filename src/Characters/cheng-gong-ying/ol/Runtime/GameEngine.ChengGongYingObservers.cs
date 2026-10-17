namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 27100)]
    private void AssertChengGongYing(ProgramSkillFrame f)
    {
        if (f.ChengGongYing is not { } r) return;
        if (f.TriggerId is null || f.InstructionIndex != 1 || r.Source != ChengGongYingSource(f) || r.GameplayHash != f.GameplayHash || GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.AdjustKuangxiangHandToFour ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } c || c.ParentFrameId != r.WindowId || r.Turn != _turnNumber || r.TurnOwner != _turnProgression.OwnerSeat || !Enum.IsDefined(r.Stage) ||
            !ChengGongYingParent(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), c) ||
            ProgramEventHistory<ChengGongYingStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.WindowId == r.WindowId && e.Turn == r.Turn && e.TurnOwner == r.TurnOwner) != 1 ||
            r.Count < 0 || r.InitialHandCount < 0 || r.Selected.Count > r.Count || r.Selected.Distinct().Count() != r.Selected.Count || r.Candidates.Distinct().Count() != r.Candidates.Count || r.Selected.Any(id => !r.Candidates.Contains(id)) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.ActualCount < 0)
            throw new InvalidOperationException("Kuangxiang lost its exact current native candidate, owning instruction, frozen participants or physical selection.");
        if (r.Stage == ChengGongYingStage.TargetChoice)
        {
            if (r.Recipient is not null || r.InitialHandCount != 0 || r.Count != 0 || r.Candidates.Count != 0 || r.Selected.Count != 0 || r.Before != 0 || r.After != 0 || r.ActualCount != 0) throw new InvalidOperationException("An unpaid Kuangxiang target selection cannot own an adjustment invoice.");
        }
        else if (r.Recipient is not { } payer || !IsValidPlayerSeat(payer) || ProgramEventHistory<KuangxiangTargetChosenEvent>().Count(e => e.FrameId == f.Id && e.Recipient == payer && e.InitialHandCount == r.InitialHandCount && e.RequestedCount == Math.Abs(r.InitialHandCount - 4) && e.Draw == (r.InitialHandCount < 4)) != 1 ||
            (r.Stage == ChengGongYingStage.DrawChildren ? r.InitialHandCount >= 4 || r.Count != 4 - r.InitialHandCount || r.Candidates.Count != 0 || r.Selected.Count != 0 : r.InitialHandCount < 4 || r.Count > r.InitialHandCount - 4 || r.Count > r.Candidates.Count)) throw new InvalidOperationException("Kuangxiang lost its original hand-count adjustment and exact target.");
        if (f.PendingMovementContinuation is not null)
        {
            var draw = r.Stage == ChengGongYingStage.DrawChildren;
            if (r.Stage is not (ChengGongYingStage.DrawChildren or ChengGongYingStage.DiscardChildren) || r.Recipient is not { } recipient ||
                ProgramEventHistory<KuangxiangAdjustmentIssuedEvent>().Count(e => e.FrameId == f.Id && e.Recipient == recipient && e.Draw == draw && e.Before == r.Before && e.After == r.After && e.ActualCount == r.ActualCount) != 1 ||
                _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && (!draw || m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(recipient) && m.Reason.Value == KuangxiangDrawReason)) != r.ActualCount ||
                r.Reshuffles.Select(s => s.BatchId).Distinct().Count() != r.Reshuffles.Count || r.Reshuffles.Any(s => s.ParentFrameId != f.Id || s.Movements.Count == 0 || s.Before < r.Before || s.After > r.After || s.Before != s.Movements[0].Sequence - 1 || s.After != s.Movements[^1].Sequence || s.Movements.Any(m => m.From != CardLocation.DiscardPile || m.To != CardLocation.DrawPile || m.Reason != CardMoveReasons.Reshuffle || !_cardMovements.Contains(m)) || ProgramEventHistory<KuangxiangReshuffleCapturedEvent>().Count(e => e.FrameId == f.Id && e.BatchId == s.BatchId && e.Before == s.Before && e.After == s.After && e.Count == s.Movements.Count) != 1) ||
                _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After).Any(m => !IsChengGongYingIssuedMovement(f, m))) throw new InvalidOperationException("Kuangxiang lost its once-issued real native movement interval and exact reshuffle proof.");
            if (draw && ProgramEventHistory<KuangxiangCardObtainedEvent>().Where(e => e.FrameId == f.Id).Any(e => e.Source != r.Source || e.Recipient != recipient || e.MovementSequence <= r.Before || e.MovementSequence > r.After || !_cardMovements.Any(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(recipient) && m.Reason.Value == KuangxiangDrawReason))) throw new InvalidOperationException("Kuangxiang cannot mark a card not really obtained by its exact draw.");
        }
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && r.Stage is ChengGongYingStage.TargetChoice or ChengGongYingStage.DiscardChoice)
            if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p || p.PlayerSeat != ChengGongYingChooser(f) || !AssistedChoicesEqual(p.Choices, ChengGongYingChoices(f))) throw new InvalidOperationException("Kuangxiang lost its exact source/recipient-private selection.");
    }
    private bool IsChengGongYingIssuedMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        var r = f.ChengGongYing!;
        if (m.Sequence <= r.Before || m.Sequence > r.After || !_cardMovements.Contains(m) || r.Recipient is not { } payer) return false;
        return r.Stage switch
        {
            ChengGongYingStage.DrawChildren => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(payer) && m.Reason.Value == KuangxiangDrawReason ||
                m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle && r.Reshuffles.Any(s => s.ParentFrameId == f.Id && s.Movements.Contains(m)),
            ChengGongYingStage.DiscardChildren => r.Selected.Contains(m.CardId) && m.From == CardLocation.Hand(payer) && m.To == CardLocation.DiscardPile && m.Reason.Value == KuangxiangDiscardReason,
            _ => false
        };
    }
    private bool ChengGongYingFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.ChengGongYing is not { } r || f.PendingMovementContinuation is null) return false; AssertChengGongYing(f);
        if (child is CardsMovedTriggerWindowFrame moved) return moved.Batch.ParentFrameId == f.Id && (moved.ResumeProgramFrameId == f.Id || moved.Batch.AwaitingProgramFrameId == f.Id) && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => IsChengGongYingIssuedMovement(f, m)) &&
            (moved.Batch.Movements.All(m => m.Reason != CardMoveReasons.Reshuffle) || moved.Batch.ParentBatchId is null && r.Reshuffles.Any(s => s.BatchId == moved.Batch.Id && s.ParentFrameId == moved.Batch.ParentFrameId && s.Movements.SequenceEqual(moved.Batch.Movements)));
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills) return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        return child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.ResumeProgramFrameId == f.Id && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
    }
    private ProgramSkillFrame? ChengGongYingObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.ChengGongYingFirstChild(f, c), static (e, i) => e.HasObserverSuffix(i, static (engine, c) => engine.IsMovementObserverEdge(c) || engine.IsYanPuDyingExitEdge(c) || engine.RecoveryReplacementFrameRidesOn(engine._resolutionStack[c], engine._resolutionStack[c - 1]), static (engine, c, d) => engine.IsSuspendedOrPaidRescueObserverSuffix(c, d)));
    private bool HasChengGongYingDamageObserver(long id) => ChengGongYingObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasChengGongYingBeforeDamageObserver(long id) => HasChengGongYingDamageObserver(id);
    private bool IsChengGongYingDying() => ActiveDying is { } dying && ChengGongYingObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsChengGongYingNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || ChengGongYingObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f); return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature && target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 27100)] private bool AdvanceChengGongYingSubtree() => _pendingDecision is null && ChengGongYingObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
