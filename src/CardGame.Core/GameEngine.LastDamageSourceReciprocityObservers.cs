namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IDamageAttempt? LastDamageSourceOriginalAttack(long id) => _resolutionStack.SingleOrDefault(f => f.Id == id) switch
    {
        ProgramSkillFrame { AttackAttempt: not null } => new ProgramAttackHandle(this, id),
        ProgramSkillFrame { CardAttack.Active: true } => new CardAttackHandle(this, id),
        CardUseFrame { CardAttack.Active: true } => new CardAttackHandle(this, id),
        JudgmentFrame { CardAttack.Active: true } => new CardAttackHandle(this, id),
        _ => null
    };

    private bool ValidLastDamageSourceReceipt(ProgramSkillFrame f)
    {
        if (f.LastDamageSourceReciprocity is not { } r || f.WindowContext is not
                { Window: SkillProgramTriggerWindow.AfterDamageApplied, SourceSeat: { } source, TargetSeat: { } target,
                  DamageFrameId: { } damageId, Amount: > 0 } context || f.TriggerId is null ||
            f.InstructionIndex != 1 || r.InstructionIndex != f.InstructionIndex || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            r.GameplayHash != f.GameplayHash || r.DamageWindowId != context.ParentFrameId || r.DamageFrameId != damageId ||
            r.SourceSeat != source || r.TargetSeat != target || source == target || r.Amount != context.Amount || r.Amount <= 0 ||
            !IsValidPlayerSeat(source) || !IsValidPlayerSeat(target) || !IsValidPlayerSeat(r.RecordedSourceSeat) ||
            !Enum.IsDefined(r.Direction) || !Enum.IsDefined(r.Stage) || !Enum.IsDefined(r.Nature) ||
            r.ParticipantSeat != (r.Direction == LastDamageSourceReciprocityDirection.DrawOwner ? f.OwnerSeat : source) ||
            (r.Direction == LastDamageSourceReciprocityDirection.DrawOwner ? source != f.OwnerSeat || r.RecordedSourceSeat != target :
                target != f.OwnerSeat || r.RecordedSourceSeat != source)) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        if (plan.Trigger is not { } trigger || !IsLastDamageSourceTrigger(trigger) || trigger.Effects[0].StateId != r.StateId ||
            LastDamageSourceDirection(trigger) != r.Direction || plan.Instructions.Count != 1 ||
            plan.Instructions[0].Op != SkillProgramEffectOp.LastDamageSourceReciprocity ||
            plan.Instructions[0].StateId != r.StateId) return false;
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        if (index < 2 || _resolutionStack[index - 1] is not DamageTriggerWindowFrame window || window.Id != r.DamageWindowId ||
            window.ParentFrameId != r.DamageFrameId || window.TriggerWindow != context.Window || window.SourceSeat != source || window.TargetSeat != target ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex].ToProgramCandidate()) ||
            _resolutionStack[index - 2] is not DamageFrame damage || damage.Id != r.DamageFrameId || damage.ParentFrameId != r.AttackFrameId ||
            damage.SourceSeat != source || damage.TargetSeat != target || damage.Amount != r.Amount || damage.Nature != r.Nature ||
            LastDamageSourceOriginalAttack(r.AttackFrameId) is not { } attack || attack.IsSourceLess || !attack.DamageWasApplied ||
            attack.SourceSeat != source || attack.TargetSeat != target || attack.DamageAmount != r.Amount || GetDamageNature(attack) != r.Nature) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<DamageRequestedEvent>().Count(e => e.ResolutionId == damage.Id && e.SourceSeat == source &&
                e.TargetSeat == target && e.Amount == r.Amount && e.Nature == r.Nature && !e.SourceLess && e.SourceCard == attack.EffectiveCardKind) != 1)
            return false;
        var requestIndex = Array.FindIndex(history, e => e is DamageRequestedEvent requested && requested.ResolutionId == damage.Id);
        if (history.Skip(requestIndex + 1).TakeWhile(e => e is not DamageRequestedEvent).OfType<DamageAppliedEvent>()
                .FirstOrDefault() is not { SourceLess: false } applied || applied.SourceSeat != source || applied.TargetSeat != target ||
            applied.Amount != r.Amount || applied.Nature != r.Nature) return false;
        var eligibility = history.OfType<LastDamageSourceReciprocityEligibleEvent>().Where(e => e.OwnerSeat == f.OwnerSeat &&
            e.SkillId == f.SkillId && e.StateId == r.StateId && e.Direction == r.Direction && e.DamageFrameId == r.DamageFrameId).ToArray();
        if (eligibility is not [var earned] || earned.SkillInstanceId != f.SkillInstanceId || earned.GameplayHash != f.GameplayHash ||
            earned.BindingId != f.TriggerId || earned.AttackFrameId != r.AttackFrameId || earned.SourceSeat != source || earned.TargetSeat != target ||
            earned.Amount != r.Amount || earned.Nature != r.Nature || earned.RecordedSourceSeat != r.RecordedSourceSeat) return false;
        // Validate the remembered source at the original occurrence. Later nested
        // damage may update memory, but cannot replace this earned conclusion.
        var remembered = history.TakeWhile(e => !Equals(e, earned)).OfType<LastDamageSourceRecordedEvent>()
            .LastOrDefault(e => e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId && e.StateId == r.StateId);
        if (remembered is not { } memory || memory.SourceSeat != r.RecordedSourceSeat || r.Direction == LastDamageSourceReciprocityDirection.DiscardSource &&
            (memory.DamageFrameId != r.DamageFrameId || memory.AttackFrameId != r.AttackFrameId ||
             memory.Amount != r.Amount || memory.Nature != r.Nature)) return false;
        var started = history.OfType<LastDamageSourceReciprocityStartedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (started is not [var start] || start.Source != r.Source || start.GameplayHash != r.GameplayHash || start.StateId != r.StateId ||
            start.Direction != r.Direction || start.DamageWindowId != r.DamageWindowId || start.DamageFrameId != r.DamageFrameId ||
            start.AttackFrameId != r.AttackFrameId || start.SourceSeat != source || start.TargetSeat != target || start.Amount != r.Amount ||
            start.Nature != r.Nature || start.ParticipantSeat != r.ParticipantSeat || start.RecordedSourceSeat != r.RecordedSourceSeat ||
            history.OfType<LastDamageSourceReciprocityStartedEvent>().Count(e => e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillId == f.SkillId &&
                e.StateId == r.StateId && e.Direction == r.Direction && e.DamageFrameId == r.DamageFrameId) != 1) return false;
        if (r.EligibleMaterials.Select(m => m.CardId).Distinct().Count() != r.EligibleMaterials.Count || r.EligibleMaterials.Any(m =>
                m.CardId <= 0 || m.From.OwnerSeat != r.ParticipantSeat || m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            r.ActualCount is < 0 or > 1 || r.SequenceBefore < 0 ||
            r.SequenceAfter < r.SequenceBefore || r.SequenceAfter > LastDamageSourceMovementSequence) return false;
        var issued = history.OfType<LastDamageSourceReciprocityMovementIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (!r.MovementIssued)
            return r.Stage == LastDamageSourceReciprocityStage.ChoosingDiscard && r.Direction == LastDamageSourceReciprocityDirection.DiscardSource &&
                r.EligibleMaterials.Count > 0 && r.PaidMaterial is null && r.ActualCount == 0 && r.BatchId is null &&
                r.SequenceBefore == 0 && r.SequenceAfter == 0 && issued.Length == 0 && f.PendingMovementContinuation is null &&
                r.EligibleMaterials.SequenceEqual(LastDamageSourceLegalMaterials(r.ParticipantSeat));
        if (r.Stage is not (LastDamageSourceReciprocityStage.MovementChildren or LastDamageSourceReciprocityStage.Complete) ||
            issued is not [var invoice] || invoice.ParticipantSeat != r.ParticipantSeat || invoice.Direction != r.Direction ||
            invoice.ActualCount != r.ActualCount || invoice.SequenceBefore != r.SequenceBefore || invoice.SequenceAfter != r.SequenceAfter || invoice.BatchId != r.BatchId)
            return false;
        var records = _cardMovements.Where(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter &&
            m.Reason.Value == LastDamageSourceReason(f, r.Direction == LastDamageSourceReciprocityDirection.DrawOwner)).ToArray();
        if (records.Length != r.ActualCount) return false;
        if (r.Direction == LastDamageSourceReciprocityDirection.DrawOwner)
        {
            if (r.EligibleMaterials.Count != 0 || r.PaidMaterial is not null || r.BatchId is not null || records.Any(m =>
                    m.From != CardLocation.DrawPile || m.To != CardLocation.Hand(f.OwnerSeat))) return false;
        }
        else if (r.ActualCount != 1 || r.BatchId is null || r.PaidMaterial is not { } paid || !r.EligibleMaterials.Contains(paid) ||
            records is not [var movement] || movement.CardId != paid.CardId || movement.CardKind != paid.PrintedKind ||
            movement.From != paid.From || movement.To != (paid.IsGeneralWeapon && paid.From.Zone == CardZoneKind.Equipment
                ? CardLocation.OutsideGame : CardLocation.DiscardPile)) return false;
        return r.Stage == LastDamageSourceReciprocityStage.Complete ? f.PendingMovementContinuation is null :
            f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } pending && pending.SubjectSeat == r.ParticipantSeat;
    }

    private bool LastDamageSourceFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        var r = f.LastDamageSourceReciprocity!;
        if (!r.MovementIssued || r.Stage != LastDamageSourceReciprocityStage.MovementChildren) return false;
        var draw = r.Direction == LastDamageSourceReciprocityDirection.DrawOwner;
        bool PaidEquipment(CardKind kind) => !draw && r.PaidMaterial is { From.Zone: CardZoneKind.Equipment } material &&
            material.PrintedKind == kind && _cardMovements.Any(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter &&
                m.CardId == material.CardId && m.From == material.From && m.To == CardLocation.DiscardPile && m.Reason.Value == LastDamageSourceReason(f, false));
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.ResumeProgramFrameId is null && moved.Batch.Id == moved.Id && moved.Batch.ParentFrameId == f.Id &&
                moved.Batch.AwaitingProgramFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
                moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m =>
                    _cardMovements.Contains(m) && (m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter &&
                        (draw ? m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == LastDamageSourceReason(f, true) ||
                            m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle :
                            moved.Batch.Id == r.BatchId && r.PaidMaterial is { } material && m.CardId == material.CardId &&
                            m.From == material.From && m.To == (material.IsGeneralWeapon && material.From.Zone == CardZoneKind.Equipment
                                ? CardLocation.OutsideGame : CardLocation.DiscardPile) && m.Reason.Value == LastDamageSourceReason(f, false)) ||
                     !draw && PaidEquipment(CardKind.WoodenOx) && m.Reason == CardMoveReasons.WoodenOxGrainDiscard &&
                        m.From == CardLocation.WoodenOxGrain(r.ParticipantSeat) && m.To == CardLocation.DiscardPile));
        if (!PaidEquipment(CardKind.SilverLion)) return false;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.ParentFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == r.ParticipantSeat &&
                hp.Change.TargetSeat == r.ParticipantSeat && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, f) &&
            recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.SourceSeat == r.ParticipantSeat &&
            recovery.Attempt.TargetSeat == r.ParticipantSeat && recovery.Attempt.Amount == 1 &&
            recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            recovery.Attempt.Completion.MoveReason?.Value == LastDamageSourceReason(f, false);
    }

    private void AssertLastDamageSourceReciprocity(ProgramSkillFrame f)
    {
        if (f.LastDamageSourceReciprocity is null) return;
        if (!ValidLastDamageSourceReceipt(f))
            throw new InvalidOperationException("Last-source reciprocity lost its original positive damage, stable memory fact, private draft or once-issued movement.");
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        if (index + 1 < _resolutionStack.Count && !LastDamageSourceFirstChild(f, _resolutionStack[index + 1]))
            throw new InvalidOperationException("Last-source reciprocity retained an unrelated first native child.");
    }

    private ProgramSkillFrame? LastDamageSourceReciprocityObserverRoot(long damageWindowId)
    {
        var index = _resolutionStack.FindIndex(frame => frame.Id == damageWindowId);
        if (index < 0 || index + 2 >= _resolutionStack.Count || _resolutionStack[index] is not DamageTriggerWindowFrame ||
            _resolutionStack[index + 1] is not ProgramSkillFrame root || root.LastDamageSourceReciprocity?.DamageWindowId != damageWindowId ||
            !ValidLastDamageSourceReceipt(root) || !LastDamageSourceFirstChild(root, _resolutionStack[index + 2])) return null;
        for (var child = index + 3; child < _resolutionStack.Count; child++)
        {
            if (_resolutionStack[child] is ProgramLifecycleTriggerWindowFrame skills && _resolutionStack[child - 1] is ProgramSkillFrame changed &&
                skills.Window == SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId == changed.Id &&
                skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && skills.CandidateIndex >= 0 &&
                skills.CandidateIndex <= skills.Candidates.Count) continue;
            if (!DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !HalfHandPaidDamageObserverEdge(child) && !PaidTargetObserverEdge(child)) return null;
            if (_resolutionStack[child] is DyingFrame dying && child + 1 < _resolutionStack.Count &&
                ((IsOriginalDyingSuspendedByDyingSuits(dying) || IsOriginalDyingSuspendedByRecipientCategoryMark(dying)) || IsOriginalDyingSuspendedByOwnedDeathBenefit(dying) ||
                 IsPaidHandRepaymentProgramAlcoholRide(child, dying) || IsPaidHandRepaymentRescueRide(child, dying) ||
                 PolicyCounterspellVirtualAlcoholRide(child, dying) || PaidObserverDamageVirtualAlcoholRide(child, dying))) break;
        }
        return root;
    }
    private bool IsLastDamageSourceReciprocityDying() => ActiveDying is { } dying &&
        _resolutionStack.OfType<DamageTriggerWindowFrame>().Any(window => LastDamageSourceReciprocityObserverRoot(window.Id) is { } root &&
            _resolutionStack.FindIndex(frame => frame.Id == dying.Id) > _resolutionStack.FindIndex(frame => frame.Id == root.Id));

    private ProgramSkillFrame? AnyLastDamageSourceReciprocityObserverRoot() => _resolutionStack.OfType<DamageTriggerWindowFrame>()
        .Select(window => LastDamageSourceReciprocityObserverRoot(window.Id)).FirstOrDefault(root => root is not null);
    private bool HasLastDamageSourceReciprocityDamageObserver(long windowId) =>
        _resolutionStack.Any(frame => frame.Id == windowId && frame is DamageTriggerWindowFrame) &&
        AnyLastDamageSourceReciprocityObserverRoot() is not null;
    private bool AllowsLastDamageSourceReciprocityNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or
                SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            AnyLastDamageSourceReciprocityObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }
    private bool TryAdvanceLastDamageSourceReciprocitySubtree()
    {
        if (_pendingDecision is not null || AnyLastDamageSourceReciprocityObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attack)
        {
            if (CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null || attack.AttackReturn is null ||
                _resolutionStack.Any(frame => frame is DamageFrame damage && damage.ParentFrameId == attack.Id ||
                    frame is BeforeDamageProgramWindowFrame before && (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == attack.Id)) return false;
            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id)); AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or
            ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame or RecoveryReplacementFrame)
        { AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true; }
        if (top is DeathFrame death) { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        return false;
    }

    private bool IsLastDamageSourceReciprocityChoice(ProgramSkillFrame f, PendingDecision decision)
    {
        if (f.LastDamageSourceReciprocity is not { Stage: LastDamageSourceReciprocityStage.ChoosingDiscard, MovementIssued: false } r ||
            !ValidLastDamageSourceReceipt(f) || decision.Kind != DecisionKind.ProgramTrigger || !decision.IsPrivate ||
            decision.PlayerSeat != r.ParticipantSeat || decision.TargetSeat != r.ParticipantSeat || decision.SourceSeat != f.OwnerSeat ||
            decision.ValidTargetSeats.Count != 0 || decision.ValidContentIds.Count != 0 || decision.RequiredCardCount != 0 ||
            decision.SkillPrompt?.SkillId != f.SkillId) return false;
        var expected = LastDamageSourceDiscardChoices(f);
        return decision.ValidCardIds.SequenceEqual(expected.SelectMany(c => c.Cards).Distinct()) && decision.Choices.Count == expected.Count &&
            decision.Choices.Zip(expected).All(pair => pair.First.Id == pair.Second.Id && pair.First.Cards.SequenceEqual(pair.Second.Cards) &&
                pair.First.Targets.SequenceEqual(pair.Second.Targets) && pair.First.Parameters.OrderBy(x => x.Key).SequenceEqual(pair.Second.Parameters.OrderBy(x => x.Key)));
    }
}
