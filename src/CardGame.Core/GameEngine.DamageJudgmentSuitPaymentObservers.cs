namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsValidDamageJudgmentSuitPayment(ProgramSkillFrame frame)
    {
        if (frame.DamageJudgmentSuitPayment is not { } draft || frame.TriggerId is null || draft.InstructionIndex != frame.InstructionIndex ||
            draft.InstructionIndex != 1 || draft.OwnerSeat != frame.OwnerSeat || !IsValidPlayerSeat(draft.SourceSeat) || !IsValidPlayerSeat(draft.TargetSeat) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0 } context ||
            context.ParentFrameId != draft.DamageWindowId || context.DamageFrameId != draft.DamageFrameId || context.SourceSeat != draft.SourceSeat ||
            context.TargetSeat != draft.TargetSeat || context.OccurrenceIndex != draft.OccurrenceIndex ||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == draft.DamageWindowId) is not { } window ||
            window.ParentFrameId != draft.DamageFrameId || window.SourceSeat != draft.SourceSeat || window.TargetSeat != draft.TargetSeat || window.TriggerWindow != context.Window ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(frame, window.Candidates[window.CandidateIndex].ToProgramCandidate()) ||
            _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == draft.DamageFrameId) is not { Amount: > 0 } damage ||
            damage.ParentFrameId != draft.CardUseFrameId || damage.SourceSeat != draft.SourceSeat || damage.TargetSeat != draft.TargetSeat ||
            !DamageJudgmentUseMatches(draft, window.SourceCard) ||
            ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).Instructions is not
                [{ Op: SkillProgramEffectOp.JudgeDamageTargetThenOfferSuitDiscard } instruction] ||
            instruction.JudgmentReason != draft.Reason || instruction.ResultBind != draft.ResultBind ||
            !Enum.IsDefined(draft.Stage) || draft.SourceDiscardsRemaining is < 0 or > 2) return false;
        if (draft.JudgmentCardId is { } judged)
        {
            if (draft.JudgmentCardKind is null || draft.JudgmentSuit is not { } suit || !Enum.IsDefined(suit) ||
                draft.JudgmentRank is null or < 1 or > 13 ||
                !_cardZones.CardsAt(_cardZones.GetLocation(judged)).Any(c => c.Id == judged && c.Kind == draft.JudgmentCardKind && c.Rank == draft.JudgmentRank) ||
                !_events.Select(e => e.Payload).Concat(_pendingEvents).OfType<JudgmentResolvedEvent>().Any(e => e.ResolutionId == draft.JudgmentFrameId &&
                    e.ParentResolutionId == frame.Id && e.TargetSeat == draft.TargetSeat && e.Reason == draft.Reason && e.CardId == judged &&
                    e.CardKind == draft.JudgmentCardKind && e.Suit == draft.JudgmentSuit && e.Rank == draft.JudgmentRank)) return false;
        }
        else if (draft.Stage is not (ProgramDamageJudgmentPaymentStage.Judging or ProgramDamageJudgmentPaymentStage.CleanupChildren or ProgramDamageJudgmentPaymentStage.Complete) || draft.PaidCardId is not null) return false;
        if (draft.PaidCardId is not { } paid)
            return draft.PaidMovementSequence is null && !draft.JudgmentClaimIssued && !draft.PaymentClaimIssued &&
                draft.JudgmentClaimMovementSequence is null && draft.PaymentClaimMovementSequence is null &&
                draft.SourceDiscardMovementSequence1 is null && draft.SourceDiscardMovementSequence2 is null &&
                draft.Stage is ProgramDamageJudgmentPaymentStage.Judging or ProgramDamageJudgmentPaymentStage.ChoosingPayment or
                    ProgramDamageJudgmentPaymentStage.CleanupChildren or ProgramDamageJudgmentPaymentStage.Complete;
        if (draft.PaidMovementSequence is not { } sequence || draft.PaidCardKind is null || draft.PaidFrom is not { } from ||
            draft.PaidTo is not { } to || from.OwnerSeat != frame.OwnerSeat || !DamageJudgmentPaymentZones.Contains(from.Zone) ||
            to != CardLocation.DiscardPile && to != CardLocation.OutsideGame || draft.PaidSuit is not { } paidSuit || !Enum.IsDefined(paidSuit) || draft.PaidRank is null or < 1 or > 13 ||
            !_cardZones.CardsAt(_cardZones.GetLocation(paid)).Any(c => c.Id == paid && c.Kind == draft.PaidCardKind && c.Rank == draft.PaidRank) ||
            !_cardMovements.Any(m => m.Sequence == sequence && m.CardId == paid && m.CardKind == draft.PaidCardKind && m.From == from && m.To == to && m.Reason.Value == DamageJudgmentPaymentReason)) return false;
        if (!HasDamageJudgmentMatchingClaim(frame, draft, true) || !HasDamageJudgmentMatchingClaim(frame, draft, false)) return false;
        var sourceSequences = new[] { draft.SourceDiscardMovementSequence1, draft.SourceDiscardMovementSequence2 }.OfType<long>().ToArray();
        if (sourceSequences.Distinct().Count() != sourceSequences.Length || draft.SourceDiscardMovementSequence2 is not null && draft.SourceDiscardMovementSequence1 is null ||
            sourceSequences.Any(seq => !_cardMovements.Any(m => m.Sequence == seq && m.From.OwnerSeat == draft.SourceSeat && DamageJudgmentPaymentZones.Contains(m.From.Zone) &&
                (m.To == CardLocation.DiscardPile || m.To == CardLocation.OutsideGame) && m.Reason.Value == DamageJudgmentSourceDiscardReason)) ||
            draft.JudgmentSuit != Suit.Club && (sourceSequences.Length != 0 || draft.SourceDiscardsRemaining != 0) ||
            draft.JudgmentSuit == Suit.Club && draft.Stage != ProgramDamageJudgmentPaymentStage.PaymentChildren && sourceSequences.Length != 2 - draft.SourceDiscardsRemaining) return false;
        return _events.Select(e => e.Payload).Concat(_pendingEvents).OfType<ProgramJudgmentSuitPaymentCommittedEvent>().Any(e =>
            e.FrameId == frame.Id && e.SkillId == frame.SkillId && e.BindingId == GetProgramBindingId(frame) && e.SkillInstanceId == frame.SkillInstanceId &&
            e.OwnerSeat == frame.OwnerSeat && e.JudgmentFrameId == draft.JudgmentFrameId && e.JudgmentCardId == draft.JudgmentCardId &&
            e.PaidCardId == paid && e.PaidMovementSequence == sequence && e.SuitMatched == (draft.PaidSuit == draft.JudgmentSuit) && e.RankMatched == (draft.PaidRank == draft.JudgmentRank) &&
            e.JudgmentSuit == draft.JudgmentSuit && e.JudgmentRank == draft.JudgmentRank && e.PaidSuit == draft.PaidSuit && e.PaidRank == draft.PaidRank);
    }

    private bool HasDamageJudgmentMatchingClaim(ProgramSkillFrame frame, ProgramDamageJudgmentSuitPayment draft, bool judged)
    {
        var issued = judged ? draft.JudgmentClaimIssued : draft.PaymentClaimIssued;
        var sequence = judged ? draft.JudgmentClaimMovementSequence : draft.PaymentClaimMovementSequence;
        if (!issued) return sequence is null;
        var id = judged ? draft.JudgmentCardId : draft.PaidCardId;
        return sequence is { } seq && id is { } card && (judged ? draft.JudgmentSuit == draft.PaidSuit : draft.JudgmentRank == draft.PaidRank) &&
            _cardMovements.Any(m => m.Sequence == seq && m.CardId == card && m.To == CardLocation.Hand(frame.OwnerSeat) &&
                m.From == (judged ? CardLocation.Processing : CardLocation.DiscardPile) &&
                m.Reason.Value == (judged ? "skill-program.damage-judgment-suit.claim-judgment" : "skill-program.damage-judgment-suit.reclaim-payment")) &&
            _events.Select(e => e.Payload).Concat(_pendingEvents).OfType<ProgramJudgmentSuitPaymentCardClaimedEvent>().Any(e => e.FrameId == frame.Id &&
                e.SkillId == frame.SkillId && e.SkillInstanceId == frame.SkillInstanceId && e.OwnerSeat == frame.OwnerSeat && e.JudgmentFrameId == draft.JudgmentFrameId &&
                e.CardId == card && e.MovementSequence == seq && e.IsJudgmentCard == judged);
    }

    private ProgramSkillFrame? DamageJudgmentSuitPaymentRoot(long damageWindow)
    {
        var parentIndex = _resolutionStack.FindIndex(f => f.Id == damageWindow);
        if (parentIndex < 0 || parentIndex + 1 >= _resolutionStack.Count || _resolutionStack[parentIndex + 1] is not
            { } owned || owned is not ProgramSkillFrame frame || !IsValidDamageJudgmentSuitPayment(frame)) return null;
        var draft = frame.DamageJudgmentSuitPayment!;
        for (var index = parentIndex + 2; index < _resolutionStack.Count; index++)
        {
            if (!DamageJudgmentSuitPaymentEdge(index, frame, draft)) return null;
            if (_resolutionStack[index] is DyingFrame dying && IsPaidHandRepaymentProgramAlcoholRide(index, dying)) return frame;
        }
        return frame;
    }

    private bool DamageJudgmentSuitPaymentEdge(int index, ProgramSkillFrame root, ProgramDamageJudgmentSuitPayment draft)
    {
        var child = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
        if (child is JudgmentFrame judgment && parent.Id == root.Id)
            return draft.Stage == ProgramDamageJudgmentPaymentStage.Judging && judgment.Id == draft.JudgmentFrameId &&
                judgment.ParentFrameId == root.Id && judgment.TargetSeat == draft.TargetSeat && judgment.SourceSeat == draft.SourceSeat &&
                judgment.Reason == draft.Reason && judgment.ProgramResultBind == draft.ResultBind && IsValidProgramJudgmentContinuation(judgment, root);
        if (child is ProgramJudgmentTriggerWindowFrame window && parent is JudgmentFrame judged)
            return judged.Id == draft.JudgmentFrameId && window.ParentFrameId == judged.Id && window.Judgment.JudgmentFrameId == judged.Id &&
                window.Judgment.SubjectSeat == draft.TargetSeat && window.Judgment.SourceSeat == draft.SourceSeat && window.Judgment.Reason == draft.Reason;
        if (child is ProgramSkillFrame final && parent is ProgramJudgmentTriggerWindowFrame finalized)
        {
            if (finalized.Judgment.JudgmentFrameId != draft.JudgmentFrameId || finalized.CandidateIndex < 0 || finalized.CandidateIndex >= finalized.Candidates.Count ||
                final.WindowContext is not { Window: SkillProgramTriggerWindow.JudgmentFinalized } context || context.ParentFrameId != finalized.Id ||
                context.Judgment != finalized.Judgment) return false;
            var candidate = finalized.Candidates[finalized.CandidateIndex];
            return candidate.OwnerSeat == final.OwnerSeat && candidate.SkillId == final.SkillId && candidate.TriggerId == final.TriggerId &&
                candidate.SkillInstanceId == final.SkillInstanceId && candidate.GameplayHash == final.GameplayHash;
        }
        if (child is ProgramSkillFrame replacementProgram && parent is JudgmentFrame replace)
            return replace.Id == draft.JudgmentFrameId && replacementProgram.WindowContext is { Window: SkillProgramTriggerWindow.JudgmentReplacing, JudgmentReplacement: { } replacement } c &&
                c.ParentFrameId == replace.Id && replacement.JudgmentFrameId == replace.Id && CurrentJudgmentCandidate(replace) is { } candidate &&
                candidate.OwnerSeat == replacementProgram.OwnerSeat && candidate.ProgramId == replacementProgram.SkillId && candidate.ProgramTriggerId == replacementProgram.TriggerId &&
                candidate.SkillInstanceId == replacementProgram.SkillInstanceId && candidate.GameplayHash == replacementProgram.GameplayHash;
        if (child is DyingFrame hpLossDying && parent is ProgramSkillFrame hpLossProgram)
            return IsDamageJudgmentHpLossDyingRide(hpLossDying, hpLossProgram);
        if (child is ProgramSkillFrame entryProgram && parent is ProgramLifecycleTriggerWindowFrame
            { Continuation: ProgramLifecycleContinuation.ResumeDyingEntry } entry &&
            ActiveDying is { ResumesProgramSkill: true } entered &&
            _resolutionStack.OfType<DyingFrame>().SingleOrDefault(f => f.Id == entered.Id) is { } entryDying)
            return ParticipantHandDyingEntryRide(entryProgram, entry, entryDying) &&
                entry.ResumeDyingFrameId == entered.Id && entryDying.ParentFrameId == entered.ParentFrameId &&
                entryDying.VictimSeat == entered.VictimSeat && entry.OwnerSeat == entered.VictimSeat &&
                entry.CandidateIndex >= 0 && entry.CandidateIndex < entry.Candidates.Count &&
                MountObserverCandidateMatches(entryProgram, entry.Candidates[entry.CandidateIndex]) &&
                entryProgram.WindowContext is { Window: SkillProgramTriggerWindow.DyingEntering } entryContext &&
                entryContext.ParentFrameId == entry.Id && entryContext.OwnerSeat == entryProgram.OwnerSeat &&
                entryContext.TargetSeat == entered.VictimSeat && entryContext.SourceSeat == entered.KillerSeat;
        if (child is ProgramLifecycleTriggerWindowFrame stateWindow && parent is ProgramSkillFrame stateParent)
            return stateWindow.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && stateWindow.ResumeProgramFrameId == stateParent.Id &&
                stateWindow.CharacterStateContinuation == CharacterStateContinuation.Program &&
                stateWindow.Window is SkillProgramTriggerWindow.CharacterTurnedOver or SkillProgramTriggerWindow.CharacterTurnedFaceUp or SkillProgramTriggerWindow.CharacterEnteredChain &&
                _events.Select(e => e.Payload).Concat(_pendingEvents).OfType<CharacterStateChangedEvent>().Any(e =>
                    e.Change.Id == stateWindow.Id && e.Change.ParentFrameId == stateParent.Id &&
                    e.Change.TargetSeat == stateWindow.OwnerSeat && e.Change.Window == stateWindow.Window);
        if (child is ProgramSkillFrame stateProgram && parent is ProgramLifecycleTriggerWindowFrame state)
            return state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CandidateIndex >= 0 && state.CandidateIndex < state.Candidates.Count &&
                MountObserverCandidateMatches(stateProgram, state.Candidates[state.CandidateIndex]) && stateProgram.WindowContext?.ParentFrameId == state.Id && stateProgram.WindowContext.Window == state.Window;
        // This helper proves only generic typed descendants. Our root above
        // independently locks the 4200 actual Slash/damage/judgment/payment.
        return PreventionDrawObserverEdge(index);
    }

    // This edge is reached only after the actual 4200 root and every earlier
    // observer edge have been proved. Bind HP loss and the resulting Dying to
    // the exact child producer, without requiring HP to remain zero after rescue.
    private bool IsDamageJudgmentHpLossDyingRide(DyingFrame dying, ProgramSkillFrame program) =>
        dying.ResumesProgramSkill && dying.ParentFrameId == program.Id && dying.KillerSeat is null &&
        ActiveDying is { ResumesProgramSkill: true } active && active.Id == dying.Id &&
        active.ParentFrameId == program.Id && active.VictimSeat == dying.VictimSeat &&
        _events.Select(e => e.Payload).Concat(_pendingEvents).OfType<ProgramSkillHpLostEvent>()
            .LastOrDefault(e => e.FrameId == program.Id && e.SkillId == program.SkillId && e.TargetSeat == dying.VictimSeat)
            is { Amount: > 0, RemainingHp: <= 0 } &&
        _events.Select(e => e.Payload).Concat(_pendingEvents).OfType<PlayerDyingEvent>().Any(e =>
            e.ResolutionId == dying.Id && e.VictimSeat == dying.VictimSeat && e.KillerSeat is null);

    private bool HasDamageJudgmentSuitPaymentObserver(long id) => DamageJudgmentSuitPaymentRoot(id) is not null;
    private bool IsDamageJudgmentSuitPaymentMovement(ProgramSkillFrame frame, SkillProgramEffect? effect,
        ProgramMovementContinuation movement) => effect?.Op == SkillProgramEffectOp.JudgeDamageTargetThenOfferSuitDiscard &&
        movement.SubjectSeat == frame.OwnerSeat && movement.BeforeCount == 0 && movement.CoverageResultBind is null &&
        frame.DamageJudgmentSuitPayment is { Stage: ProgramDamageJudgmentPaymentStage.PaymentChildren or
            ProgramDamageJudgmentPaymentStage.SourceDiscardChildren or ProgramDamageJudgmentPaymentStage.ClaimChildren or
            ProgramDamageJudgmentPaymentStage.CleanupChildren } && IsValidDamageJudgmentSuitPayment(frame);
    private bool HasDamageJudgmentSuitPaymentRide(long judgment) => ActiveDamageTrigger is { } damage &&
        DamageJudgmentSuitPaymentRoot(damage.Id)?.DamageJudgmentSuitPayment?.JudgmentFrameId == judgment;
    private bool IsDamageJudgmentSuitPaymentDying() => ActiveDamageTrigger is { } damage && ActiveDying is { ResumesProgramSkill: true } &&
        DamageJudgmentSuitPaymentRoot(damage.Id) is not null;

    private bool IsExactDamageJudgmentSuitPaymentFinalizedSubtree(ProgramJudgmentTriggerWindowFrame window, JudgmentFrame? judgment, int index) =>
        judgment is not null && window.Activated && index >= 1 && index + 1 < _resolutionStack.Count && _resolutionStack[index - 1] == judgment &&
        judgment.Id == window.ParentFrameId && _resolutionStack[index + 1] is ProgramSkillFrame child && child.WindowContext is
            { Window: SkillProgramTriggerWindow.JudgmentFinalized } context && context.ParentFrameId == window.Id && context.Judgment == window.Judgment &&
        HasDamageJudgmentSuitPaymentRide(judgment.Id);

    private void AssertDamageJudgmentSuitPayment(ProgramSkillFrame frame)
    {
        if (frame.DamageJudgmentSuitPayment is not { } draft) return;
        if (!IsValidDamageJudgmentSuitPayment(frame)) throw new InvalidOperationException("Invalid damage judgment payment owning receipt.");
        if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) && draft.Stage is
            ProgramDamageJudgmentPaymentStage.ChoosingPayment or ProgramDamageJudgmentPaymentStage.ChoosingSourceDiscard)
        {
            var source = draft.Stage == ProgramDamageJudgmentPaymentStage.ChoosingSourceDiscard;
            if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p || p.PlayerSeat != (source ? draft.SourceSeat : frame.OwnerSeat) ||
                !AssistedChoicesEqual(p.Choices, DamageJudgmentPaymentChoices(frame, source))) throw new InvalidOperationException("Damage judgment payment changed its published exact choices.");
        }
    }

    private PromptChoice SelectAiDamageJudgmentSuitPayment(PendingDecision decision)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("AI damage judgment owner missing.");
        var draft = frame.DamageJudgmentSuitPayment!;
        if (draft.Stage == ProgramDamageJudgmentPaymentStage.ChoosingSourceDiscard) return decision.Choices.OrderBy(c => c.Id.Value, StringComparer.Ordinal).First();
        var view = CreateSnapshot(frame.OwnerSeat);
        var benefit = draft.JudgmentSuit switch
        {
            Suit.Heart => _aiBrains[frame.OwnerSeat].ScoreProgramTarget(view, draft.TargetSeat, new(0, 1, 0, 0, 0, 0, false, false)),
            Suit.Diamond => _aiBrains[frame.OwnerSeat].ScoreProgramTarget(view, draft.TargetSeat, new(2, 0, 0, 0, 0, 0, false, false)),
            _ => _aiBrains[frame.OwnerSeat].ScoreProgramTarget(view, draft.SourceSeat, new(0, 0, 0, 0, 0, 1, false, false))
        };
        return decision.Choices.Select(c => (Choice: c, Value: c.Parameters["payment-kind"] == "decline" ? 0d :
            benefit - 8d + (_cardZones.CardsAt(_cardZones.GetLocation(c.Cards.Single())).Single(candidateCard => candidateCard.Id == c.Cards.Single()) is { } card
                ? (EffectiveSuit(_players[frame.OwnerSeat], card) == draft.JudgmentSuit ? 10d : 0d) + (card.Rank == draft.JudgmentRank ? 10d : 0d) : 0d)))
            .OrderByDescending(x => x.Value).ThenBy(x => x.Choice.Id.Value, StringComparer.Ordinal).First().Choice;
    }
}
