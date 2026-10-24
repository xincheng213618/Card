namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void RequireShiWangChang(ProgramSkillFrame f)
    {
        if (f.MobileShiWangChang is not { } r || r.Source != ShiWangChangSource(f) || r.GameplayHash != f.GameplayHash ||
            f.WindowContext is not { } c || c.ParentFrameId != r.WindowId || c.OwnerSeat != f.OwnerSeat ||
            r.ActualTurn != _turnNumber || r.TurnOwner != _currentSeat || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<MobileShiWangChangStartedEvent>().Count(e => e == new MobileShiWangChangStartedEvent(f.Id, r.Source, r.GameplayHash,
                r.WindowId, r.ActualTurn, r.TurnOwner, r.Maximum, r.UseId, r.ActionId, r.Actor, r.Kind)) != 1)
            throw new InvalidOperationException("Shi Wang Chang lost its exact source, native boundary or issued invoice.");
        if (f.SkillId == ShiKaiji)
        {
            if (f.TriggerId != "kaiji-prepare" || c.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || c.SourceSeat != f.OwnerSeat ||
                r.Maximum < 1 || r.Maximum > _players.Count || r.Targets.Count > r.Maximum || r.Targets.Distinct().Count() != r.Targets.Count ||
                r.Targets.Any(s => !IsValidPlayerSeat(s)) || r.Index < 0 || r.Index > r.Targets.Count || r.UseId != 0 || r.ActionId != 0 || r.Actor != -1 ||
                r.Stage is MobileShiWangChangStage.Mode or MobileShiWangChangStage.Resolving || f.InstructionIndex is < 1 or > 3)
                throw new InvalidOperationException("Kaiji changed its preparation selection or paid recipient cursor.");
            if (r.Stage != MobileShiWangChangStage.Targets && !ProgramEventHistory<MobileShiKaijiTargetCommittedEvent>().Where(e => e.FrameId == f.Id)
                    .OrderBy(e => e.Index).Select(e => e.Target).SequenceEqual(r.Targets))
                throw new InvalidOperationException("Kaiji changed its frozen original recipients after payment began.");
            if (r.Draw is { } draw && (r.Stage != MobileShiWangChangStage.DrawChildren || r.Index >= r.Targets.Count || draw.RecipientSeat != r.Targets[r.Index] || draw.RequestedCount != 1 ||
                draw.SequenceBefore != r.Before || draw.SequenceAfter != r.After ||
                ProgramEventHistory<MobileShiKaijiDrawReturnedEvent>().Count(e => e.FrameId == f.Id && e.Index == r.Index && e.Target == draw.RecipientSeat &&
                    e.Actual == draw.ActualDrawIds.Count && e.Replacement == draw.ReplacementGainedIds.Count && e.Before == r.Before && e.After == r.After) != 1))
                throw new InvalidOperationException("Kaiji cannot repeat or redirect a completed native draw.");
        }
        else if (f.SkillId != ShiShepan || f.TriggerId != "shepan-adjust" || c.Window != SkillProgramTriggerWindow.CardUseBeforeTargetEffects ||
            ShiShepanUse(c, f.OwnerSeat) is not { } use || use.Id != r.UseId || use.Action!.ActionId != r.ActionId || use.SourceSeat != r.Actor || use.CardKind != r.Kind ||
            r.Maximum != 0 || r.Targets.Count != 0 || r.Index != 0 || r.Draw is not null || r.Stage is not (MobileShiWangChangStage.Mode or MobileShiWangChangStage.Resolving or MobileShiWangChangStage.Complete) ||
            r.Stage == MobileShiWangChangStage.Mode && r.Mode is not null || r.Stage != MobileShiWangChangStage.Mode && r.Mode is not ("draw" or "top"))
            throw new InvalidOperationException("Shepan lost its original current damage-card target, branch or adjustment.");
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 47400)]
    private void AssertShiWangChang(ProgramSkillFrame f)
    {
        if (f.MobileShiWangChang is null) return;
        RequireShiWangChang(f);
        if (f.MobileShiWangChang.Stage is MobileShiWangChangStage.Targets or MobileShiWangChangStage.Mode && _resolutionStack.LastOrDefault()?.Id == f.Id &&
            (_pendingDecision is not { } p || p.PlayerSeat != f.OwnerSeat || p.SkillPrompt?.SkillId != f.SkillId || !AssistedChoicesEqual(p.Choices, ShiWangChangChoices(f))))
            throw new InvalidOperationException("Shi Wang Chang lost its exact current target or adjustment choices.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 47400)]
    private bool IsShiKaijiMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation movement)
    {
        if (e?.Op != SkillProgramEffectOp.SelectMobileShiKaijiTargets || f.MobileShiWangChang is not { Stage: MobileShiWangChangStage.DrawChildren, Draw: not null } ||
            movement != f.PendingMovementContinuation || movement.SubjectSeat != f.OwnerSeat || movement.BeforeCount != 0 || movement.CoverageResultBind is not null) return false;
        RequireShiWangChang(f); return true;
    }
    private bool ShiKaijiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.SkillId != ShiKaiji || f.InstructionIndex != 1 || f.MobileShiWangChang is not { } r) return false;
        RequireShiWangChang(f);
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MobileShiWangChangStage.Drawing && f.NativeDraw is not null;
        if (r.Stage != MobileShiWangChangStage.DrawChildren || r.Draw is null) return false;
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m));
        if (child is RecoveryReplacementFrame recovery) return ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray());
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
             ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private ProgramSkillFrame? ShiKaijiObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.ShiKaijiFirstChild(f, child),
        static (g, i) => g.HasObserverSuffix(i, static (engine, n) => engine.IsMovementObserverEdge(n) || engine.IsSameNameHandObserverEdge(n) || engine.IsYanPuDyingExitEdge(n) ||
            engine.TypedDamagePostEventEdge(engine._resolutionStack[n - 1], engine._resolutionStack[n]) ||
            NativeDrawAttemptRidesOn(engine._resolutionStack[n], engine._resolutionStack[n - 1]) || engine.RecoveryReplacementFrameRidesOn(engine._resolutionStack[n], engine._resolutionStack[n - 1]),
            static (engine, n, d) => engine.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 47400)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 47400)]
    private bool ShiKaijiNestedDamage(long id) => ShiKaijiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 47400)]
    private bool ShiKaijiBeforeDamage(long id) => ShiKaijiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 47400)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 47400)]
    private bool ShiKaijiDying() => ActiveDying is { } dying && ShiKaijiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 47400)]
    private bool AdvanceShiKaijiChildren() => _pendingDecision is null && ShiKaijiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 47400)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 47400)]
    private bool ShiKaijiRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.SkillId != ShiKaiji || f.InstructionIndex != 1 || f.MobileShiWangChang is null) return false;
        RequireShiWangChang(f);
        if (ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray())) return true;
        var root = _resolutionStack.FindIndex(frame => frame.Id == f.Id); var child = _resolutionStack.FindIndex(frame => frame.Id == recovery.Id);
        return root >= 0 && child > root + 1 && ShiKaijiFirstChild(f, _resolutionStack[root + 1]) && ShiKaijiObserverRoot()?.Id == f.Id &&
            RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 47400)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 47400)]
    private bool KeepPaidShiWangChang(ProgramSkillFrame f)
    {
        if (f.MobileShiWangChang is not { } r ||
            !(f.SkillId == ShiKaiji && r.Stage != MobileShiWangChangStage.Targets ||
              f.SkillId == ShiShepan && r.Stage is MobileShiWangChangStage.Resolving or MobileShiWangChangStage.Complete)) return false;
        RequireShiWangChang(f); return true;
    }
}
