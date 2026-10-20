namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26920)]
    private void AssertYuanHuan(ProgramSkillFrame f)
    {
        if (f.YuanHuan is not { } r) return;
        if (r.Source != YuanHuanSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex || f.InstructionIndex != 1 ||
            !YuanHuanComposition.Handles(r.Op) || GetPausedPrivateOfferEffect(f)?.Op != r.Op || r.ActualTurn != _turnNumber || !Enum.IsDefined(r.Stage) ||
            !IsValidPlayerSeat(r.TargetSeat) || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.GameplayHash || r.Movements is null ||
            ProgramEventHistory<YuanHuanStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] || start.Source != r.Source || start.GameplayHash != r.GameplayHash ||
            start.Op != r.Op || start.ActualTurn != r.ActualTurn || start.PhaseInstance != r.PhaseInstance || start.TargetSeat != r.TargetSeat || start.ParentId != r.ParentId || start.DamageFrameId != r.DamageFrameId || start.DamageAmount != r.DamageAmount)
            throw new InvalidOperationException("YuanHuan lost its exact original source, instruction, actual turn or single start invoice.");
        var deru = r.Op == SkillProgramEffectOp.ResolveDeruBasicNames;
        var expected = r.Stage switch { YuanHuanStage.Guess or YuanHuanStage.SourceDiscard => 0, YuanHuanStage.RecoveryChildren or YuanHuanStage.DiscardChildren => 1, YuanHuanStage.TakeChildren or YuanHuanStage.LinjieDrawChildren => 2, _ => 3 };
        if (deru)
        {
            if (f.TriggerId is not null || f.WindowContext is not null || r.ParentId != 0 || r.DamageFrameId is not null || r.DamageAmount != 0 ||
                f.SelectedTargetSeats is not [var target] || target != r.TargetSeat || target == f.OwnerSeat || r.PhaseInstance != _cardUseDebitPhaseInstanceId ||
                r.Stage is YuanHuanStage.SourceDiscard or YuanHuanStage.DiscardChildren or YuanHuanStage.LinjieDrawChildren || r.DiscardedKind is not null || r.DiscardedPrintedKind is not null ||
                r.Stage == YuanHuanStage.Guess && (r.GuessMask is not null || r.ActualMask is not null || ProgramEventHistory<DeruGuessCommittedEvent>().Any(e => e.FrameId == f.Id)) ||
                r.Stage != YuanHuanStage.Guess && (r.GuessMask is not (>= 0 and < 16) || r.ActualMask is not (>= 0 and < 16) || ProgramEventHistory<DeruGuessCommittedEvent>().Count(e => e.FrameId == f.Id) != 1))
                throw new InvalidOperationException("Deru lost its actual Play target or exact private four-name presence comparison.");
            if (r.Stage is YuanHuanStage.TakeChildren or YuanHuanStage.FillChildren)
            {
                if (r.BasicCandidates is null || r.BasicCandidates.Distinct().Count() != r.BasicCandidates.Count ||
                    (r.BasicCandidates.Count == 0 ? r.RandomIndex != -1 || r.Movements[1].CardId is not null || r.TakenKind is not null || r.TakenPrintedKind is not null :
                     r.RandomIndex < 0 || r.RandomIndex >= r.BasicCandidates.Count || r.Movements[1].CardId != r.BasicCandidates[r.RandomIndex] || r.TakenKind is null || DeruNameBit(r.TakenKind.Value) == 0 || r.TakenPrintedKind is null) ||
                    r.GuessMask == r.ActualMask && r.BasicCandidates.Count != 0)
                    throw new InvalidOperationException("Deru lost its frozen private basic-hand candidates or native random index.");
            }
            else if (r.BasicCandidates is not null || r.RandomIndex != -1 || r.TakenKind is not null || r.TakenPrintedKind is not null) throw new InvalidOperationException("Deru cannot select its random hand material before the recovery children return.");
        }
        else
        {
            if (r.GuessMask is not null || r.ActualMask is not null || r.BasicCandidates is not null || r.RandomIndex != -1 || r.TakenKind is not null || r.TakenPrintedKind is not null || f.SelectedTargetSeats.Count != 0 ||
                f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } context || context.ParentFrameId != r.ParentId || context.DamageFrameId != r.DamageFrameId ||
                context.SourceSeat != r.TargetSeat || context.TargetSeat != f.OwnerSeat || context.Amount != r.DamageAmount || r.DamageAmount < 1 ||
                r.Stage is not (YuanHuanStage.SourceDiscard or YuanHuanStage.DiscardChildren or YuanHuanStage.LinjieDrawChildren) ||
                _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == r.ParentId) is not { } window || window.ParentFrameId != r.DamageFrameId || window.SourceSeat != r.TargetSeat || window.TargetSeat != f.OwnerSeat ||
                window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex].ToProgramCandidate()) ||
                !_resolutionStack.OfType<DamageFrame>().Any(d => d.Id == r.DamageFrameId && d.SourceSeat == r.TargetSeat && d.TargetSeat == f.OwnerSeat && d.Amount == r.DamageAmount) ||
                r.Stage == YuanHuanStage.SourceDiscard && (r.DiscardedKind is not null || r.DiscardedPrintedKind is not null) || r.Stage != YuanHuanStage.SourceDiscard && (r.DiscardedKind is null || r.DiscardedPrintedKind is null))
                throw new InvalidOperationException("Linjie lost its exact actual whole-damage parent, original source or single hand-discard payment.");
        }
        var issued = ProgramEventHistory<YuanHuanStepIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Movements.Count != expected || issued.Length != expected || (expected == 0 ? f.PendingMovementContinuation is not null : f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } p || p.SubjectSeat != f.OwnerSeat))
            throw new InvalidOperationException("YuanHuan lost its paid step cursor or exact owning movement continuation.");
        for (var i = 0; i < expected; i++)
        {
            var m = r.Movements[i]; var e = issued[i]; var reason = deru ? i switch { 0 => DeruRecoverReason, 1 => DeruTakeReason, _ => DeruFillReason } : i == 0 ? LinjieDiscardReason : LinjieDrawReason;
            if (m.Index != i || e.Index != i || m.Reason != reason || e.Reason != reason || e.SubjectSeat != m.SubjectSeat || e.Before != m.Before || e.After != m.After || e.ActualCount != m.ActualCount ||
                e.CardId != m.CardId || e.From != m.From || e.To != m.To || m.Before < 0 || m.After < m.Before || m.After > _movementSequence || m.ActualCount < 0 ||
                i > 0 && m.Before < r.Movements[i - 1].After)
                throw new InvalidOperationException("YuanHuan paid steps differ from their immutable ordered scalar invoices.");
            var actual = _cardMovements.Where(move => move.Sequence > m.Before && move.Sequence <= m.After && move.Reason.Value == reason).ToArray();
            if (reason == DeruRecoverReason)
            { if (m.ActualCount != 0 || m.CardId is not null || actual.Length != 0) throw new InvalidOperationException("Deru recovery cannot fabricate a card payment."); }
            else if (reason is DeruFillReason or LinjieDrawReason)
            { if (actual.Length != m.ActualCount || m.CardId is not null || actual.Any(move => move.From != CardLocation.DrawPile || move.To != CardLocation.Hand(f.OwnerSeat))) throw new InvalidOperationException("YuanHuan draw invoice differs from its actual native drawn entities."); }
            else if (actual.Length != m.ActualCount || m.ActualCount is < 0 or > 1 || m.ActualCount == 0 && (m.CardId is not null || m.From is not null || m.To is not null) ||
                m.ActualCount == 1 && (actual is not [var card] || card.CardId != m.CardId || card.From != m.From || card.To != m.To ||
                    card.From != CardLocation.Hand(r.TargetSeat) || card.To != (reason == DeruTakeReason ? CardLocation.Hand(f.OwnerSeat) : CardLocation.DiscardPile) ||
                    reason == DeruTakeReason && card.CardKind != r.TakenPrintedKind || reason == LinjieDiscardReason && (m.ActualCount != 1 || card.CardKind != r.DiscardedPrintedKind)))
                throw new InvalidOperationException("YuanHuan lost its exact original hand entity, basic-only opaque take or true source hand discard.");
        }
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26920)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26920)]
    private bool ContinueIssuedYuanHuan(ProgramSkillFrame f)
    { if (f.YuanHuan is not { Stage: not (YuanHuanStage.Guess or YuanHuanStage.SourceDiscard) }) return false; AssertYuanHuan(f); return true; }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 210)]
    private bool IsYuanHuanMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p)
    { if (f.YuanHuan is not { Movements.Count: > 0 } r || e?.Op != r.Op || p != f.PendingMovementContinuation) return false; AssertYuanHuan(f); return true; }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 750)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 690)]
    private bool IsYuanHuanRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.YuanHuan is not { Stage: YuanHuanStage.RecoveryChildren } r) return false;
        AssertYuanHuan(f);
        return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && r.GuessMask != (r.ActualMask ^ 15) &&
            recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == f.OwnerSeat && recovery.Attempt.Amount == 1 &&
            recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program && recovery.Attempt.Completion.InstructionIndex == r.InstructionIndex;
    }
    private bool YuanHuanFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.YuanHuan is not { Movements.Count: > 0 } r || f.PendingMovementContinuation is null) return false;
        AssertYuanHuan(f); var m = r.Movements[^1];
        if (child is CardsMovedTriggerWindowFrame moved)
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(move => _cardMovements.Contains(move) && move.Sequence > m.Before && move.Sequence <= m.After && (move.Reason.Value == m.Reason || move.Reason == CardMoveReasons.Reshuffle));
        if (child is RecoveryReplacementFrame replacement) return IsYuanHuanRecoveryParent(f, replacement);
        if (child is HpChangedTriggerWindowFrame hp) return r.Stage == YuanHuanStage.RecoveryChildren && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id &&
            hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == f.OwnerSeat && hp.Change.TargetSeat == f.OwnerSeat && hp.Change.Amount == 1;
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills && skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? YuanHuanObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.YuanHuanFirstChild(f, c), static (e, i) => e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 560)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 670)]
    private bool HasYuanHuanDamageObserver(long id) => YuanHuanObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 700)]
    private bool HasYuanHuanBeforeDamageObserver(long id) => YuanHuanObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is BeforeDamageProgramWindowFrame) && _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 520)]
    private bool IsYuanHuanDying() => ActiveDying is { } dying && YuanHuanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1160)]
    private bool AllowsYuanHuanNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || YuanHuanObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26920)]
    private bool AdvanceYuanHuanSubtree() => _pendingDecision is null && YuanHuanObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
