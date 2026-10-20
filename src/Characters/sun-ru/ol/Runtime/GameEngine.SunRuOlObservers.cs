namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26110)]
    private void AssertSunRuOl(ProgramSkillFrame f)
    {
        if (f.SunRuOlChishi is { } c)
        {
            if (f.SunRuOlWeimian is not null || c.Source != SunRuOlSource(f) || c.GameplayHash != f.GameplayHash || c.InstructionIndex != 1 ||
                c.InstructionIndex != f.InstructionIndex || f.TriggerId is null || !SunRuOlActualTurn(c.Turn) ||
                c.SequenceBefore < 0 || c.SequenceAfter < c.SequenceBefore || c.SequenceAfter > _movementSequence ||
                f.WindowContext is not { MovementBatch: { } batch } context || context.ParentFrameId != c.MovementWindowId ||
                batch.Id != c.MovementWindowId || batch.SunRuOlLastZoneLoss != c.Turn || context.TargetSeat != c.Turn.OwnerSeat ||
                !SunRuOlLostLastZone(batch, c.Turn.OwnerSeat) ||
                _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.MovementWindowId) is not { } window ||
                window.CandidateIndex >= window.Candidates.Count || !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex]) ||
                ProgramEventHistory<SunRuOlChishiAppliedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var applied] ||
                applied.Source != c.Source || applied.Turn != c.Turn || applied.MovementWindowId != c.MovementWindowId ||
                ProgramEventHistory<TurnRuleModifierGrantedEvent>().Count(e => e.Modifier.ParentFrameId == f.Id &&
                    e.Modifier.AffectedSeat == c.Turn.OwnerSeat && e.Modifier.Query == SkillRuleQuery.HandLimit && e.Modifier.Amount == 2) != 1)
                throw new InvalidOperationException("Chishi lost its exact immutable native last-zone batch, actual-turn token, paid draw or recipient hand-limit grant.");
        }
        if (f.SunRuOlWeimian is not { } r) return;
        if (r.Source != SunRuOlSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != 1 || r.InstructionIndex != f.InstructionIndex ||
            f.TriggerId is not null || !SunRuOlActualTurn(r.Turn) || !IsValidPlayerSeat(r.RecipientSeat) || f.SelectedTargetSeats is not [var target] || target != r.RecipientSeat ||
            !Enum.IsDefined(r.Stage) || r.SlotsMask is < 0 or > 31 || r.BenefitsMask is < 0 or > 7 || r.NextBenefit is < 1 or > 4 ||
            r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore || r.SequenceAfter > _movementSequence ||
            (r.Paid ? SunRuOlBitCount(r.SlotsMask) is < 1 or > 3 : r.SlotsMask != 0 || r.Stage != SunRuOlWeimianStage.Slots) ||
            r.BenefitsMask != 0 && SunRuOlBitCount(r.BenefitsMask) != SunRuOlBitCount(r.SlotsMask))
            throw new InvalidOperationException("Weimian lost its accepted living-recipient binding, actual-turn token or distinct equipment/benefit cursor.");
        var payments = ProgramEventHistory<SunRuOlWeimianPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Paid ? payments is not [var paid] || paid.Source != r.Source || paid.RecipientSeat != r.RecipientSeat || paid.SlotsMask != r.SlotsMask || paid.Turn != r.Turn ||
                paid.SequenceBefore < 0 || paid.SequenceAfter < paid.SequenceBefore || paid.SequenceAfter > _movementSequence
            : payments.Length != 0)
            throw new InvalidOperationException("Weimian must pay exactly its frozen available-slot combination once before recipient benefits.");
        var chosen = ProgramEventHistory<SunRuOlWeimianBenefitsChosenEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.BenefitsMask == 0 ? chosen.Length != 0 : chosen is not [var b] || b.RecipientSeat != r.RecipientSeat || b.BenefitsMask != r.BenefitsMask)
            throw new InvalidOperationException("Weimian must retain the recipient's exact distinct project selection.");
        var issued = ProgramEventHistory<SunRuOlWeimianBenefitIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (issued.GroupBy(e => e.Benefit).Any(g => g.Count() != 1) || issued.Any(e => e.RecipientSeat != r.RecipientSeat || e.Benefit is < 1 or > 3 ||
            (r.BenefitsMask & (1 << (e.Benefit - 1))) == 0 || e.SequenceBefore < 0 || e.SequenceAfter < e.SequenceBefore || e.SequenceAfter > _movementSequence))
            throw new InvalidOperationException("A Weimian benefit cannot issue twice or be paid by another recipient.");
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 50)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 40)]
    private bool IsSunRuOlRecoveryParent(ProgramSkillFrame producer, RecoveryReplacementFrame recovery)
    {
        if (producer.SunRuOlWeimian is not { Paid: true } r || r.Source != SunRuOlSource(producer) ||
            recovery.ParentFrameId != producer.Id || recovery.Return != new RecoveryReplacementReturn(PostEventContinuation.Program, producer.Id)) return false;
        if (r.Stage == SunRuOlWeimianStage.RecoveryChildren)
            return recovery.Attempt.Completion == new RecoveryAttemptCompletion(RecoveryAttemptProducer.Program, producer.InstructionIndex) &&
                recovery.Attempt.SourceSeat == producer.OwnerSeat && recovery.Attempt.TargetSeat == r.RecipientSeat && recovery.Attempt.Amount == 1 &&
                ProgramEventHistory<SunRuOlWeimianBenefitIssuedEvent>().Any(e => e.FrameId == producer.Id && e.Benefit == 2 && e.RecipientSeat == r.RecipientSeat);
        return r.Stage == SunRuOlWeimianStage.CostChildren && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            recovery.Attempt.Completion.MoveReason == new CardMoveReason("equipment.slot-abolished") && recovery.Attempt.Amount == 1 &&
            recovery.Attempt.SourceSeat == producer.OwnerSeat && recovery.Attempt.TargetSeat == producer.OwnerSeat &&
            _cardMovements.Any(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter && m.From == CardLocation.Equipment(producer.OwnerSeat) &&
                m.Reason.Value == "equipment.slot-abolished" && GetAdvancedCard(m.CardId).Kind == CardKind.SilverLion);
    }
    private bool SunRuOlFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.SunRuOlChishi is null && root.SunRuOlWeimian is null) return false;
        AssertSunRuOl(root);
        if (child is CardsMovedTriggerWindowFrame moved)
        {
            var before = root.SunRuOlChishi?.SequenceBefore ?? root.SunRuOlWeimian!.SequenceBefore;
            var after = root.SunRuOlChishi?.SequenceAfter ?? root.SunRuOlWeimian!.SequenceAfter;
            var reason = root.SunRuOlChishi is not null ? SunRuOlChishiDrawReason : root.SunRuOlWeimian!.Stage switch
            { SunRuOlWeimianStage.CostChildren => "equipment.slot-abolished", SunRuOlWeimianStage.DiscardChildren => SunRuOlRefreshDiscardReason,
                SunRuOlWeimianStage.DrawChildren => SunRuOlRefreshDrawReason, _ => "" };
            return moved.ResumeProgramFrameId == root.Id && moved.Batch.ParentFrameId == root.Id && moved.Batch.OriginOwnerSeat == root.OwnerSeat &&
                moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > before && m.Sequence <= after && m.Reason.Value == reason);
        }
        if (child is RecoveryReplacementFrame recovery) return IsSunRuOlRecoveryParent(root, recovery);
        if (child is HpChangedTriggerWindowFrame hp && root.SunRuOlWeimian is { Paid: true } w)
            return hp.ResumeFrameId == root.Id && hp.Change.ParentFrameId == root.Id && hp.Continuation == PostEventContinuation.Program && hp.Change.Kind == HpChangeKind.Recovery &&
                hp.Change.Amount == 1 && (w.Stage == SunRuOlWeimianStage.CostChildren && hp.Change.TargetSeat == root.OwnerSeat ||
                    w.Stage == SunRuOlWeimianStage.RecoveryChildren && hp.Change.TargetSeat == w.RecipientSeat && hp.Change.SourceSeat == root.OwnerSeat);
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills &&
            skills.ResumeProgramFrameId == root.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && CanContinuePaidSunRuOl(root);
    }
    private ProgramSkillFrame? SunRuOlObserverRoot() => FindProgramObserverRoot(
        static (e, root, child) => e.SunRuOlFirstChild(root, child), static (e, index) => e.HasMovementObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 340)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 450)]
    private bool HasSunRuOlDamageObserver(long id) => SunRuOlObserverRoot() is { } root &&
        _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 310)]
    private bool IsSunRuOlDying() => ActiveDying is { } dying && SunRuOlObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 950)]
    private bool AllowsSunRuOlNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || SunRuOlObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26110)]
    private bool TryAdvanceSunRuOlSubtree() => _pendingDecision is null && SunRuOlObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
}
