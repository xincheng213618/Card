namespace CardGame.Core;

public sealed partial class GameEngine
{
    private readonly record struct TypedRecoveryDamageCursorProjection(
        ProgramSkillFrame? Root, long? OwnedDyingId = null, bool IsMalformed = false);

    private readonly record struct TypedResponseCompletionHealthCursorProjection(
        CardUseFrame? Owner, long? OwnedDyingId = null, bool IsMalformed = false);

    private readonly record struct TypedCommittedSlashPaymentCursorProjection(
        CardUseFrame? Owner, long? OwnedDyingId = null, bool IsMalformed = false);

    // This family starts at an actually issued Slash payment batch. Native HP
    // and removal-recovery children have separate invoices and remain outside it.
    private TypedCommittedSlashPaymentCursorProjection ProjectTypedCommittedSlashPaymentCursor()
    {
        for (var rootIndex = 0; rootIndex + 1 < _resolutionStack.Count; rootIndex++)
        {
            if (_resolutionStack[rootIndex] is not CardUseFrame use ||
                !IsSlashCard(use.CardKind) && !(use.Action is { } supplied && IsSlashCard(supplied.EffectiveKind)) ||
                _resolutionStack[rootIndex + 1] is not CardsMovedTriggerWindowFrame payment ||
                use.RecoveryPaidContinuation?.Kind != RecoveryPaidCardUseKind.CommittedSlash &&
                !(use.PaymentMovementReceipt is { Batch: { } issued } &&
                  (payment.Id == issued.Id || payment.Batch.Id == issued.Id))) continue;
            if (use.Action is not { Type: CardActionType.Use } action || !IsSlashCard(action.EffectiveKind) ||
                use.CardKind != action.EffectiveKind || use.SourceSeat != action.ActorSeat || use.ProgramUseCommitted ||
                use.CardAttack is not { } attack || attack.SourceSeat != use.SourceSeat || attack.DamageWasApplied ||
                attack.EffectiveCardKind is { } kind && kind != use.CardKind ||
                !use.TargetSeats.SequenceEqual(action.TargetSeats) ||
                !action.PhysicalCards.Select(cost => cost.CardId).SequenceEqual(use.PhysicalCardIds ?? [use.CardId]) ||
                !IsPaidCardUseMovementReturn(use, payment) || !TypedCommittedSlashPaymentActionMatches(use, action, rootIndex))
                return new(null, IsMalformed: true);
            ProgramSkillFrame? origin = null;
            for (var before = rootIndex - 1; before >= 0; before--)
                if (_resolutionStack[before] is ProgramSkillFrame program) { origin = program; break; }
            if (payment.Batch.OriginOwnerSeat != origin?.OwnerSeat || payment.Batch.OriginSkillId != origin?.SkillId ||
                payment.Batch.OriginSkillInstanceId != origin?.SkillInstanceId || origin is not null && TypedRecoveryPausedEffect(origin) is null)
                return new(null, IsMalformed: true);
            IReadOnlyList<IGameEvent>? history = null;
            IReadOnlyList<IGameEvent> History() => history ??= CompleteProgramEventHistory().ToArray();
            var suffix = ProjectTypedPaidObserverSuffix(rootIndex + 1, History, exactOrdinaryHpProducer: true);
            return suffix.IsMalformed ? new(null, IsMalformed: true) :
                suffix.IsKnown ? new(use, suffix.OwnedDyingId) : default;
        }
        return default;
    }

    private bool TypedCommittedSlashPaymentActionMatches(CardUseFrame use, CardActionContext action, int rootIndex)
    {
        if (!IsValidPlayerSeat(action.ProviderSeat) || action.PhysicalCards.Any(cost =>
            cost.From.OwnerSeat is { } owner && owner != action.ProviderSeat)) return false;
        if (use.ProgramUseAccepted || ProgramEventHistory<CardActionAcceptedEvent>().Any(e => e.Action.ActionId == action.ActionId))
            return HasExactAcceptedActualHandGainUse(use, action);
        long? parentActionId = null;
        for (var before = rootIndex - 1; before >= 0; before--)
            if (_resolutionStack[before] is CardUseFrame parent) { parentActionId = parent.Action?.ActionId; break; }
        if (action.ProviderSeat != (action.PhysicalCards.FirstOrDefault()?.From.OwnerSeat ?? action.ActorSeat) ||
            action.RequesterSeat != (action.ProviderSeat == action.ActorSeat ? null : (int?)action.ActorSeat) ||
            action.ResponderSeat is not null || action.OpponentSeat is not null || action.ParentActionId != parentActionId) return false;
        // Initial payment runs before finalized-target acceptance. Its actual
        // declaration and frozen original targets already belong to this Use;
        // a later CardActionAccepted fact cannot be required at this boundary.
        return ProgramEventHistory<CardUseDeclaredEvent>().Where(e => e.ResolutionId == use.Id).ToArray() is [var declared] &&
            declared.CardId == use.CardId && declared.CardKind == action.EffectiveKind && declared.SourceSeat == action.ActorSeat &&
            ProgramEventHistory<TargetsConfirmedEvent>().Where(e => e.ResolutionId == use.Id).ToArray() is [var targets] &&
            targets.TargetSeats.SequenceEqual(action.TargetSeats);
    }

    // A native Dodge's paid health invoices, including a faction supplier's,
    // belong to its response-completion window and retain the Slash owner ID.
    // Complex rescue/macro suffixes continue to use their own return protocol.
    private TypedResponseCompletionHealthCursorProjection ProjectTypedResponseCompletionHealthCursor()
    {
        for (var rootIndex = 1; rootIndex < _resolutionStack.Count; rootIndex++)
        {
            if (_resolutionStack[rootIndex] is not ProgramCardTriggerWindowFrame window ||
                _resolutionStack[rootIndex - 1] is not CardUseFrame native || !IsSlashCard(native.CardKind) ||
                !(window.Continuation is ProgramCardContinuation.Dodge or ProgramCardContinuation.FactionDefenseDodge ||
                  window.ResponseCompletion?.OriginalContinuation is ProgramCardContinuation.Dodge or ProgramCardContinuation.FactionDefenseDodge ||
                  window.Action is { Type: CardActionType.Response, EffectiveKind: CardKind.Dodge })) continue;
            var first = rootIndex + 1 < _resolutionStack.Count ? _resolutionStack[rootIndex + 1] : null;
            if (window.ResponseCompletion?.ActiveHealthChildFrameId is null &&
                first is not (HpChangedTriggerWindowFrame or RecoveryReplacementFrame)) continue;
            var validWindow = ValidResponseCompletionWindow(window);
            // Historical virtual Slash producers can lack an accepted Use
            // action. Their existing native-return proof remains responsible.
            if (native.Action is null && window.Action.ParentActionId is null && validWindow) return default;
            if (native.Action is not { Type: CardActionType.Use } original || native.CardKind != original.EffectiveKind ||
                native.SourceSeat != original.ActorSeat || native.CardAttack is not { } attack || attack.SourceSeat != native.SourceSeat ||
                window.ParentFrameId != native.Id ||
                window.Continuation is not (ProgramCardContinuation.Dodge or ProgramCardContinuation.FactionDefenseDodge) ||
                window.Action is not { Type: CardActionType.Response, EffectiveKind: CardKind.Dodge } ||
                !validWindow || first is null || !IsResponseCompletionHealthChild(window, first) ||
                first is HpChangedTriggerWindowFrame hpInvoice && hpInvoice.Id != hpInvoice.Change.Id)
                return new(null, IsMalformed: true);
            IReadOnlyList<IGameEvent>? history = null;
            IReadOnlyList<IGameEvent> History() => history ??= CompleteProgramEventHistory().ToArray();
            if (first is RecoveryReplacementFrame initialRecovery && !TypedResponseCompletionRecoveryMatches(initialRecovery, History()))
                return new(null, IsMalformed: true);
            var suffix = ProjectTypedPaidObserverSuffix(rootIndex + 1, History);
            return suffix.IsMalformed ? new(null, IsMalformed: true) :
                suffix.IsKnown ? new(native, suffix.OwnedDyingId) : default;
        }
        return default;
    }

    private readonly record struct TypedPaidObserverSuffixProjection(
        bool IsKnown = false, long? OwnedDyingId = null, bool IsMalformed = false);

    private TypedPaidObserverSuffixProjection ProjectTypedPaidObserverSuffix(int firstIndex,
        Func<IReadOnlyList<IGameEvent>> history, bool exactOrdinaryHpProducer = false)
    {
        long? ownedDyingId = null;
        for (var index = firstIndex + 1; index < _resolutionStack.Count; index++)
        {
            var parent = _resolutionStack[index - 1]; var child = _resolutionStack[index];
            if (exactOrdinaryHpProducer && parent is ProgramSkillFrame hpProducer && child is HpChangedTriggerWindowFrame producedHp)
            {
                var paused = TypedRecoveryPausedEffect(hpProducer);
                if (paused?.CompiledInstruction is not (RecoverProgramInstruction or LoseHpProgramInstruction))
                    return paused is null || paused.Op == SkillProgramEffectOp.ChooseOption || paused.CompiledInstruction is DrawProgramInstruction
                        ? new(IsMalformed: true) : default;
                if (paused.CompiledInstruction is RecoverProgramInstruction unsupported && !IsTypedRecoveryTargetSupported(unsupported))
                    return !TypedDamagePostEventEdge(parent, child) || UnsupportedTypedRecoveryTargetIsMalformed(hpProducer, unsupported, producedHp.Change.TargetSeat)
                        ? new(IsMalformed: true) : default;
                if (!TypedDamagePostEventEdge(parent, child) || !TypedOrdinaryPaidHpProducerMatches(hpProducer, paused, producedHp))
                    return new(IsMalformed: true);
                continue;
            }
            if (TypedDamagePostEventEdge(parent, child)) continue;
            if (parent is RecoveryReplacementFrame recovery)
            {
                if (child is HpChangedTriggerWindowFrame hp)
                {
                    if (!TypedRecoveryHpReturnMatches(recovery, hp, history())) return new(IsMalformed: true);
                    continue;
                }
                if (child is CardsMovedTriggerWindowFrame movement)
                {
                    ProgramSkillFrame? origin = null;
                    for (var before = index - 2; before >= 0; before--)
                        if (_resolutionStack[before] is ProgramSkillFrame program) { origin = program; break; }
                    if (!TypedRecoveryRewardReturnMatches(recovery, movement, history(), origin)) return new(IsMalformed: true);
                    continue;
                }
            }
            if (parent is ProgramSkillFrame producer && child is RecoveryReplacementFrame nestedRecovery)
            {
                var paused = TypedRecoveryPausedEffect(producer);
                if (paused?.CompiledInstruction is not RecoverProgramInstruction instruction)
                    return paused is null || IsRecoveryCursorPrimitive(paused.CompiledInstruction) ? new(IsMalformed: true) : default;
                if (!IsTypedRecoveryTargetSupported(instruction))
                    return UnsupportedTypedRecoveryTargetIsMalformed(producer, instruction, nestedRecovery.Attempt.TargetSeat)
                        ? new(IsMalformed: true) : default;
                if (!DirectProgramRecoveryReturnMatches(producer, paused, instruction, nestedRecovery) ||
                    nestedRecovery.CandidateIndex is not null && !TypedRecoveryChosenCandidateMatches(nestedRecovery, history()))
                    return new(IsMalformed: true);
                continue;
            }
            if (parent is ProgramSkillFrame losing && child is DyingFrame dying)
            {
                var paused = TypedRecoveryPausedEffect(losing);
                if (paused?.CompiledInstruction is not LoseHpProgramInstruction)
                    return paused is null || paused.Op == SkillProgramEffectOp.ChooseOption || IsRecoveryCursorPrimitive(paused.CompiledInstruction)
                        ? new(IsMalformed: true) : default;
                if (ownedDyingId is not null || !TypedRecoveryProgramDyingMatches(losing, dying, history()))
                    return new(IsMalformed: true);
                ownedDyingId = dying.Id;
                continue;
            }
            if (parent is DyingFrame owned && child is ProgramSkillFrame response)
            {
                if (ownedDyingId != owned.Id || !TypedRecoveryDyingResponseMatches(owned, response, history()))
                    return new(IsMalformed: true);
                continue;
            }
            if (parent is DyingFrame entering && child is ProgramLifecycleTriggerWindowFrame entry &&
                (entry.Window == SkillProgramTriggerWindow.DyingEntering || entry.Continuation == ProgramLifecycleContinuation.ResumeDyingEntry ||
                 entry.ResumeDyingFrameId is not null))
            {
                if (ownedDyingId != entering.Id || !TypedPaidDyingEntryReturnMatches(entering, entry)) return new(IsMalformed: true);
                continue;
            }
            if (parent is ProgramLifecycleTriggerWindowFrame lifecycle && lifecycle.Window == SkillProgramTriggerWindow.DyingEntering &&
                child is ProgramSkillFrame entryProgram)
            {
                if (lifecycle.ResumeDyingFrameId != ownedDyingId || !TypedPaidDyingEntryObserverMatches(lifecycle, entryProgram, history()))
                    return new(IsMalformed: true);
                continue;
            }
            if (parent is (CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame) && child is ProgramSkillFrame)
                return new(IsMalformed: true);
            if (parent is ProgramSkillFrame ordinary && child is (CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame))
            {
                var paused = TypedRecoveryPausedEffect(ordinary);
                return paused is null || IsRecoveryCursorPrimitive(paused.CompiledInstruction) ? new(IsMalformed: true) : default;
            }
            return default;
        }
        if (ownedDyingId is { } id && ActiveDying?.Id != id) return new(IsMalformed: true);
        return new(IsKnown: true, OwnedDyingId: ownedDyingId);
    }

    private static bool TypedPaidDyingEntryReturnMatches(DyingFrame dying, ProgramLifecycleTriggerWindowFrame entry) =>
        entry.Window == SkillProgramTriggerWindow.DyingEntering && entry.Continuation == ProgramLifecycleContinuation.ResumeDyingEntry &&
        entry.ResumeDyingFrameId == dying.Id && entry.OwnerSeat == dying.VictimSeat &&
        entry.ResumeProgramFrameId is null && entry.ResumeDrawPhaseObligationFrameId is null && entry.ProgramTarget is null &&
        entry.CharacterStateContinuation is null && entry.ResumeCardId is null && entry.ResumeCardKind is null &&
        entry.ParticipantFacts is { } facts && facts.GetValueOrDefault(dying.VictimSeat) == entry.Facts &&
        entry.CandidateIndex >= 0 && entry.CandidateIndex <= entry.Candidates.Count &&
        dying.AttemptedSelfDyingBindings.Contains("window:dying-entering");

    private bool TypedPaidDyingEntryObserverMatches(ProgramLifecycleTriggerWindowFrame entry, ProgramSkillFrame observer,
        IReadOnlyList<IGameEvent> history)
    {
        if (entry.CandidateIndex < 0 || entry.CandidateIndex >= entry.Candidates.Count ||
            !TypedDamageObserverCandidateMatches(observer, entry.Candidates[entry.CandidateIndex])) return false;
        var candidate = entry.Candidates[entry.CandidateIndex];
        return observer.WindowContext == new ProgramSkillWindowContext(entry.Window, entry.Id, candidate.OwnerSeat,
                SourceSeat: ActiveDying?.KillerSeat, TargetSeat: entry.OwnerSeat, OccurrenceIndex: candidate.OccurrenceIndex,
                Facts: entry.ParticipantFacts?.GetValueOrDefault(candidate.OwnerSeat) ?? entry.Facts) &&
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == observer.Id && e.SkillId == observer.SkillId &&
                e.BindingId == observer.TriggerId && e.SkillInstanceId == observer.SkillInstanceId && e.OwnerSeat == observer.OwnerSeat &&
                e.Window == entry.Window) == 1;
    }

    private bool TypedOrdinaryPaidHpProducerMatches(ProgramSkillFrame producer, SkillProgramEffect effect,
        HpChangedTriggerWindowFrame hp)
    {
        var change = hp.Change;
        if (hp.Continuation != PostEventContinuation.Program || hp.CardId is not null || hp.CardKind is not null ||
            change.Amount <= 0 || !IsValidPlayerSeat(change.TargetSeat)) return false;
        if (effect.CompiledInstruction is LoseHpProgramInstruction loss)
            return ResolveProgramEffectTarget(producer, effect.Target) == change.TargetSeat && change.Kind == HpChangeKind.Loss &&
                change.SourceSeat is null && change.HpBefore > 0 && change.Amount == Math.Min(change.HpBefore, loss.Amount) &&
                change.HpAfter == Math.Max(0, change.HpBefore - loss.Amount);
        if (effect.CompiledInstruction is not RecoverProgramInstruction recovery || change.Kind != HpChangeKind.Recovery ||
            change.SourceSeat != producer.OwnerSeat || change.HpAfter != change.HpBefore + change.Amount) return false;
        var requested = recovery.Amount switch
        {
            FixedProgramAmount fixedAmount => fixedAmount.Value,
            BoundCardCountProgramAmount bound => producer.CardSetBindings.SingleOrDefault(b => b.Name == bound.SourceBind)?.CardIds.Count ?? -1,
            _ => -1
        };
        if (change.Amount > requested) return false;
        if (recovery.Target == SkillProgramEffectTarget.SelectedTargets)
            return recovery.TargetReference is null && producer.SelectedTargetSeats.Contains(change.TargetSeat);
        return (recovery.TargetReference is { } reference ? ResolveProgramParticipant(producer, reference) :
            ResolveProgramEffectTarget(producer, effect.Target)) == change.TargetSeat;
    }

    private static bool IsTypedRecoveryTargetSupported(RecoverProgramInstruction instruction) => instruction.Target is
        SkillProgramEffectTarget.Owner or SkillProgramEffectTarget.Actor or SkillProgramEffectTarget.SelectedTarget or SkillProgramEffectTarget.SelectedTargets;

    private bool UnsupportedTypedRecoveryTargetIsMalformed(ProgramSkillFrame producer, RecoverProgramInstruction instruction, int actualTarget)
    {
        // A valid frozen HP pair remains on its existing receipt protocol. Never
        // infer the old comparison from HP after a child has already changed it.
        if (producer.HpPairSnapshot is not { } pair ||
            (instruction.Target == SkillProgramEffectTarget.HpPairHigher ? pair.HigherSeat : pair.LowerSeat) != actualTarget)
            return true;
        try
        {
            AssertEquipmentPlacementAndHpPair(producer,
                ProgramInstructionResolver.Default.Resolve(producer, _contentRegistry.GetSkill(producer.SkillId).Program!));
            return false;
        }
        catch (InvalidOperationException) { return true; }
    }

    private bool TypedResponseCompletionRecoveryMatches(RecoveryReplacementFrame recovery, IReadOnlyList<IGameEvent> history) =>
        recovery.Id == recovery.Attempt.Id && Enum.IsDefined(recovery.Stage) &&
        recovery.Attempt.Completion == new RecoveryAttemptCompletion(RecoveryAttemptProducer.SilverLion,
            MoveReason: recovery.Attempt.Completion.MoveReason) &&
        recovery.OriginalRecovered >= 0 && recovery.OriginalRecovered <= recovery.Attempt.Amount &&
        (recovery.CandidateIndex is not { } index
            ? recovery.Stage != RecoveryReplacementStage.Choosing || recovery.OriginalRecovered == 0
            : recovery.Stage != RecoveryReplacementStage.Choosing && index >= 0 && index < recovery.Attempt.Candidates.Count &&
              recovery.OriginalRecovered == 0 && TypedRecoveryChosenCandidateMatches(recovery, history));

    // This pilot follows frame-owned returns, never skill/capability names.
    // Special payment producers retain their existing receipt validators.
    private TypedRecoveryDamageCursorProjection ProjectTypedRecoveryDamageCursor(long windowId)
    {
        var windowIndex = _resolutionStack.FindIndex(f => f.Id == windowId);
        if (windowIndex < 1 || windowIndex + 1 >= _resolutionStack.Count ||
            _resolutionStack[windowIndex] is not DamageTriggerWindowFrame window ||
            _resolutionStack[windowIndex + 1] is not ProgramSkillFrame root || !DamageProgramMatchesWindow(root, window)) return default;
        var hasRecovery = false;
        long? ownedDyingId = null;
        IReadOnlyList<IGameEvent>? history = null;
        IReadOnlyList<IGameEvent> History() => history ??= CompleteProgramEventHistory().ToArray();
        for (var index = windowIndex + 2; index < _resolutionStack.Count; index++)
        {
            var parent = _resolutionStack[index - 1]; var child = _resolutionStack[index];
            if (TypedDamagePostEventEdge(parent, child)) continue;
            if (parent is ProgramSkillFrame producer && child is RecoveryReplacementFrame recovery)
            {
                var paused = TypedRecoveryPausedEffect(producer);
                if (paused is null || recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program &&
                    recovery.Attempt.Completion.InstructionIndex != producer.InstructionIndex) return new(null, IsMalformed: true);
                if (paused.CompiledInstruction is not RecoverProgramInstruction instruction)
                    return IsRecoveryCursorPrimitive(paused.CompiledInstruction) ? new(null, IsMalformed: true) : default;
                if (!IsTypedRecoveryTargetSupported(instruction))
                    return UnsupportedTypedRecoveryTargetIsMalformed(producer, instruction, recovery.Attempt.TargetSeat)
                        ? new(null, IsMalformed: true) : default;
                if (!DirectProgramRecoveryReturnMatches(producer, paused, instruction, recovery))
                    return new(null, IsMalformed: true);
                if (recovery.CandidateIndex is not null && !TypedRecoveryChosenCandidateMatches(recovery, History()))
                    return new(null, IsMalformed: true);
                hasRecovery = true;
                continue;
            }
            if (parent is RecoveryReplacementFrame recovering)
            {
                if (child is HpChangedTriggerWindowFrame hp)
                {
                    if (!TypedRecoveryHpReturnMatches(recovering, hp, History())) return new(null, IsMalformed: true);
                    continue;
                }
                if (child is CardsMovedTriggerWindowFrame movement)
                {
                    if (!TypedRecoveryRewardReturnMatches(recovering, movement, History())) return new(null, IsMalformed: true);
                    continue;
                }
            }
            if (hasRecovery && parent is ProgramSkillFrame losing && child is DyingFrame dying)
            {
                var paused = TypedRecoveryPausedEffect(losing);
                if (paused?.CompiledInstruction is not LoseHpProgramInstruction)
                    return paused is null || paused.Op == SkillProgramEffectOp.ChooseOption || IsRecoveryCursorPrimitive(paused.CompiledInstruction)
                        ? new(null, IsMalformed: true) : default;
                if (!TypedRecoveryProgramDyingMatches(losing, dying, History()) || ownedDyingId is not null)
                    return new(null, IsMalformed: true);
                ownedDyingId = dying.Id;
                continue;
            }
            if (hasRecovery && parent is DyingFrame ownedDying && child is ProgramSkillFrame response)
            {
                if (ownedDyingId != ownedDying.Id || !TypedRecoveryDyingResponseMatches(ownedDying, response, History()))
                    return new(null, IsMalformed: true);
                continue;
            }
            // Known ordinary edges must not fall through to an older permissive
            // observer ride. Unmigrated CardUse/payment families remain unknown.
            var ordinaryEdge = parent is ProgramSkillFrame && child is (CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame) ||
                parent is (CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame) && child is ProgramSkillFrame;
            return ordinaryEdge && hasRecovery ? new(null, IsMalformed: true) : default;
        }
        if (!hasRecovery) return default;
        if (ownedDyingId is { } owned && ActiveDying?.Id != owned) return new(null, IsMalformed: true);
        return new(root, ownedDyingId);
    }

    private static bool IsRecoveryCursorPrimitive(ProgramSkillInstruction? instruction) => instruction is
        DrawProgramInstruction or RecoverProgramInstruction or LoseHpProgramInstruction or DamageProgramInstruction or
        ChangeMaximumHpProgramInstruction or GrowMaximumHpAndHpProgramInstruction;

    private SkillProgramEffect? TypedRecoveryPausedEffect(ProgramSkillFrame producer)
    {
        if (!_contentRegistry.Skills.TryGetValue(producer.SkillId, out var skill) || skill.Program is not { } definition ||
            definition.GameplayHash != producer.GameplayHash || producer.InstructionIndex < 1) return null;
        var plan = ProgramInstructionResolver.Default.Resolve(producer, definition);
        return producer.InstructionIndex <= plan.Instructions.Count ? plan.GetPausedInstruction(producer.InstructionIndex).Effect : null;
    }

    private bool DirectProgramRecoveryReturnMatches(ProgramSkillFrame producer, SkillProgramEffect effect,
        RecoverProgramInstruction instruction, RecoveryReplacementFrame recovery)
    {
        var attempt = recovery.Attempt; var completion = attempt.Completion;
        if (recovery.Id != attempt.Id || recovery.ParentFrameId != producer.Id ||
            recovery.Return != new RecoveryReplacementReturn(PostEventContinuation.Program, producer.Id) ||
            completion != new RecoveryAttemptCompletion(RecoveryAttemptProducer.Program, producer.InstructionIndex) ||
            attempt.SourceSeat != producer.OwnerSeat || attempt.Amount <= 0 || !IsValidPlayerSeat(attempt.TargetSeat) ||
            !Enum.IsDefined(recovery.Stage) || recovery.CandidateIndex is { } selected &&
                (selected < 0 || selected >= attempt.Candidates.Count) ||
            recovery.Stage == RecoveryReplacementStage.Choosing && (recovery.CandidateIndex is not null || recovery.OriginalRecovered != 0)) return false;
        if (recovery.OriginalRecovered < 0 || recovery.OriginalRecovered > attempt.Amount ||
            recovery.CandidateIndex is not null && recovery.OriginalRecovered != 0) return false;
        var requested = instruction.Amount switch
        {
            FixedProgramAmount fixedAmount => fixedAmount.Value,
            BoundCardCountProgramAmount bound => producer.CardSetBindings.SingleOrDefault(b => b.Name == bound.SourceBind)?.CardIds.Count ?? -1,
            _ => -1
        };
        if (attempt.Amount > requested) return false;
        if (instruction.Target == SkillProgramEffectTarget.SelectedTargets)
            return instruction.TargetReference is null && producer.SelectedTargetSeats.Contains(attempt.TargetSeat);
        var target = instruction.TargetReference is { } reference
            ? ResolveProgramParticipant(producer, reference) : ResolveProgramEffectTarget(producer, effect.Target);
        return target == attempt.TargetSeat;
    }

    private bool TypedRecoveryHpReturnMatches(RecoveryReplacementFrame recovery, HpChangedTriggerWindowFrame hp,
        IReadOnlyList<IGameEvent> history)
    {
        var attempt = recovery.Attempt; var change = hp.Change;
        if (recovery.Stage != RecoveryReplacementStage.RecoveryApplied || hp.Id != change.Id ||
            change.ParentFrameId != recovery.Id || hp.ResumeFrameId != recovery.Id ||
            hp.Continuation != PostEventContinuation.RecoveryReplacement || hp.CardId is not null || hp.CardKind is not null ||
            change.Kind != HpChangeKind.Recovery || change.Amount <= 0 || change.HpAfter != change.HpBefore + change.Amount) return false;
        if (recovery.CandidateIndex is not { } index)
            return change.SourceSeat == attempt.SourceSeat && change.TargetSeat == attempt.TargetSeat &&
                change.HpBefore == attempt.HpBefore && change.Amount == recovery.OriginalRecovered && change.Amount <= attempt.Amount;
        if (!TypedRecoveryChosenCandidateMatches(recovery, history)) return false;
        var candidate = attempt.Candidates[index];
        return recovery.OriginalRecovered == 0 && change.SourceSeat == attempt.TargetSeat &&
            change.TargetSeat == candidate.OwnerSeat && change.Amount <= candidate.RecoveryAmount;
    }

    private bool TypedRecoveryChosenCandidateMatches(RecoveryReplacementFrame recovery, IReadOnlyList<IGameEvent> history)
    {
        if (recovery.CandidateIndex is not { } index || index < 0 || index >= recovery.Attempt.Candidates.Count) return false;
        var c = recovery.Attempt.Candidates[index];
        if (!IsValidPlayerSeat(c.OwnerSeat) || c.RecoveryAmount <= 0 || c.ProviderDrawCount < 0 ||
            string.IsNullOrWhiteSpace(c.SkillInstanceId) || !_contentRegistry.Skills.TryGetValue(c.SkillId, out var skill) ||
            skill.Program?.CardPolicies.SingleOrDefault(p => p.Id == c.PolicyId) is not
                { Kind: SkillProgramCardPolicyKind.RedirectOwnTurnFactionRecovery } policy ||
            policy.Value != c.RecoveryAmount || policy.ProviderDrawCount != c.ProviderDrawCount) return false;
        // Paid candidates retain their issued identity after source loss. Do not
        // enumerate current skills or recompute the original faction/eligibility.
        return history.OfType<RecoveryReplacementChosenEvent>().Count(e => e.RecoveryFrameId == recovery.Id &&
            e.RecoveringSeat == recovery.Attempt.TargetSeat && e.BeneficiarySeat == c.OwnerSeat && e.SkillId == c.SkillId &&
            e.SkillInstanceId == c.SkillInstanceId && e.PolicyId == c.PolicyId && e.ReplacedAmount == recovery.Attempt.Amount &&
            e.RecoveryAmount == c.RecoveryAmount) == 1;
    }

    private bool TypedRecoveryRewardReturnMatches(RecoveryReplacementFrame recovery, CardsMovedTriggerWindowFrame movement,
        IReadOnlyList<IGameEvent> history)
    {
        var parentIndex = _resolutionStack.FindIndex(f => f.Id == recovery.Id);
        return parentIndex >= 1 && _resolutionStack[parentIndex - 1] is ProgramSkillFrame producer && producer.Id == recovery.ParentFrameId &&
            TypedRecoveryRewardReturnMatches(recovery, movement, history, producer);
    }

    private bool TypedRecoveryRewardReturnMatches(RecoveryReplacementFrame recovery, CardsMovedTriggerWindowFrame movement,
        IReadOnlyList<IGameEvent> history, ProgramSkillFrame? origin)
    {
        var batch = movement.Batch;
        if (recovery.Stage != RecoveryReplacementStage.RewardApplied || !TypedRecoveryChosenCandidateMatches(recovery, history) ||
            !TypedRecoveryRewardBatchMatchesReceipt(recovery, batch) ||
            movement.Id != batch.Id || batch.ParentFrameId != recovery.Id || batch.AwaitingProgramFrameId is not null ||
            movement.ResumeRecoveryReplacementFrameId != recovery.Id || movement.ResumeProgramFrameId is not null ||
            movement.ResumeDeclarationFrameId is not null || movement.ResumeDrawFundedDistinctBasicFrameId is not null ||
            movement.ResumePaidCardUseFrameId is not null || movement.ResumeEquipmentRecastFrameId is not null ||
            movement.ResumeColorFireAttackFrameId is not null || movement.ResumeCounterspellPaymentFrameId is not null ||
            movement.ResumeHistoricalEndingUseFrameId is not null || movement.ResumeRoundPileAlcoholUseFrameId is not null ||
            movement.ResumeDrawPhaseObligationFrameId is not null || movement.ResumeFactionRequestCostFrameId is not null ||
            movement.ResumeCardSupplyCompletionFrameId is not null || movement.ResumeResponseCompletionFrameId is not null ||
            movement.DeferredTurnEndReturn is not null || batch.Movements.Count == 0) return false;
        if (batch.OriginSkillId != origin?.SkillId || batch.OriginSkillInstanceId != origin?.SkillInstanceId ||
            batch.OriginOwnerSeat != origin?.OwnerSeat) return false;
        var reward = recovery.Attempt.Candidates[recovery.CandidateIndex!.Value].ProviderDrawCount;
        var draws = 0;
        foreach (var move in batch.Movements)
        {
            if (move.Sequence <= 0 || move.Sequence > _cardMovements.Count || _cardMovements[move.Sequence - 1] != move) return false;
            if (move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(recovery.Attempt.TargetSeat) &&
                move.Reason.Value == "skill-program.recovery-replacement.reward") draws++;
            else if (move.From != CardLocation.DiscardPile || move.To != CardLocation.DrawPile || move.Reason != CardMoveReasons.Reshuffle) return false;
        }
        return reward > 0 && draws <= reward;
    }

    private bool TypedRecoveryRewardBatchMatchesReceipt(RecoveryReplacementFrame recovery, CardMovementBatchContext batch)
    {
        if (recovery.RewardMovementReceipt is not { } receipt || receipt.SequenceBefore < 0 ||
            receipt.SequenceAfter <= receipt.SequenceBefore || receipt.SequenceAfter > _cardMovements.Count ||
            receipt.Batches.Count(issued => issued.Id == batch.Id) != 1) return false;
        var issued = receipt.Batches.Single(issued => issued.Id == batch.Id);
        if (batch.Movements.Any(move => move.Sequence <= receipt.SequenceBefore || move.Sequence > receipt.SequenceAfter) ||
            !batch.Movements.SequenceEqual(issued.Movements) || !batch.SourceCounts.SequenceEqual(issued.SourceCounts) ||
            (batch.DestinationCounts is null) != (issued.DestinationCounts is null) ||
            batch.DestinationCounts is { } destinations && !destinations.SequenceEqual(issued.DestinationCounts!)) return false;
        // Compare every remaining batch fact as a record, while comparing its
        // copied collection payloads by value above. Historical reward records
        // cannot borrow this producer's issued batch identity or sequence range.
        return (batch with { Movements = issued.Movements, SourceCounts = issued.SourceCounts,
            DestinationCounts = issued.DestinationCounts }) == issued;
    }

    private bool TypedRecoveryProgramDyingMatches(ProgramSkillFrame producer, DyingFrame dying, IReadOnlyList<IGameEvent> history)
    {
        var effect = TypedRecoveryPausedEffect(producer);
        if (effect?.CompiledInstruction is not LoseHpProgramInstruction instruction || dying.ParentFrameId != producer.Id ||
            dying.Continuation != DyingContinuationKind.ProgramSkill || dying.KillerSeat is not null ||
            !IsValidPlayerSeat(dying.VictimSeat) || ResolveProgramEffectTarget(producer, effect.Target) != dying.VictimSeat ||
            dying.ResponderIndex < 0 || dying.ResponderIndex >= dying.ResponderSeats.Count ||
            dying.ResponderSeats.Count > _players.Count || dying.ResponderSeats.Distinct().Count() != dying.ResponderSeats.Count ||
            dying.ResponderSeats.Any(seat => !IsValidPlayerSeat(seat))) return false;
        var invoice = _pendingHpChanges.LastOrDefault(h => h.ParentFrameId == producer.Id && h.TargetSeat == dying.VictimSeat &&
            h.Kind == HpChangeKind.Loss && h.SourceSeat is null && h.HpBefore > 0 && h.HpAfter == 0 &&
            h.Amount == Math.Min(h.HpBefore, instruction.Amount) && h.HpAfter == Math.Max(0, h.HpBefore - instruction.Amount));
        return invoice is not null && history.OfType<ProgramSkillHpLostEvent>().LastOrDefault(e => e.FrameId == producer.Id) is { } lost &&
            lost.SkillId == producer.SkillId && lost.TargetSeat == dying.VictimSeat && lost.Amount == invoice.Amount && lost.RemainingHp == 0 &&
            history.OfType<PlayerDyingEvent>().Count(e => e.ResolutionId == dying.Id && e.VictimSeat == dying.VictimSeat && e.KillerSeat is null) == 1;
    }

    private bool TypedRecoveryDyingResponseMatches(DyingFrame dying, ProgramSkillFrame response, IReadOnlyList<IGameEvent> history) =>
        response.WindowContext is { } context && context.ParentFrameId == dying.Id && context.TargetSeat == dying.VictimSeat &&
        context.SourceSeat is null && context.DamageFrameId is null && context.OwnerSeat == response.OwnerSeat && context.OccurrenceIndex == 0 &&
        response.OwnerSeat == dying.ResponderSeat &&
        (context.Window == SkillProgramTriggerWindow.DyingResponse ||
         context.Window == SkillProgramTriggerWindow.SelfDyingResponse && response.OwnerSeat == dying.VictimSeat) &&
        response.TriggerId is { } trigger && response.ActivationId == trigger && !string.IsNullOrWhiteSpace(response.SkillInstanceId) &&
        _contentRegistry.Skills.TryGetValue(response.SkillId, out var skill) && skill.Program is { } definition &&
        definition.GameplayHash == response.GameplayHash && definition.Triggers.SingleOrDefault(t => t.Id == trigger)?.Window == context.Window &&
        history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == response.Id && e.SkillId == response.SkillId &&
            e.BindingId == trigger && e.SkillInstanceId == response.SkillInstanceId && e.OwnerSeat == response.OwnerSeat && e.Window == context.Window) == 1;

    private bool HasTypedRecoveryProgramDying() => ActiveDamageTrigger is { } window && ActiveDying is { } dying &&
        ProjectTypedRecoveryDamageCursor(window.Id) is { Root: not null, IsMalformed: false, OwnedDyingId: { } owned } && owned == dying.Id;
}
