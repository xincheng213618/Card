namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool SameOverflowAction(CardActionContext a, CardActionContext b) =>
        a.ActionId == b.ActionId && a.ParentActionId == b.ParentActionId && a.Type == b.Type &&
        a.ActorSeat == b.ActorSeat && a.ProviderSeat == b.ProviderSeat && a.RequesterSeat == b.RequesterSeat &&
        a.ResponderSeat == b.ResponderSeat && a.OpponentSeat == b.OpponentSeat && a.EffectiveKind == b.EffectiveKind &&
        a.EffectiveSuit == b.EffectiveSuit && a.EffectiveRank == b.EffectiveRank && a.EffectiveIsRed == b.EffectiveIsRed &&
        a.FactionOrigin == b.FactionOrigin && a.TargetSeats.SequenceEqual(b.TargetSeats) &&
        a.EffectiveDesignatedTargetSeats.SequenceEqual(b.EffectiveDesignatedTargetSeats) &&
        a.PhysicalCards.SequenceEqual(b.PhysicalCards) && a.ConversionChain.SequenceEqual(b.ConversionChain);

    private static bool SameOverflowIssuance(OverflowTargetCancellationReceipt a, OverflowTargetCancellationReceipt b) =>
        a.InstructionIndex == b.InstructionIndex && a.Source == b.Source && a.GameplayHash == b.GameplayHash &&
        a.WindowFrameId == b.WindowFrameId && a.CardUseFrameId == b.CardUseFrameId && a.ActionId == b.ActionId &&
        a.ProviderSeat == b.ProviderSeat && a.EffectiveKind == b.EffectiveKind && SameOverflowAction(a.ActionBeforeCancellation, b.ActionBeforeCancellation) &&
        a.HandCount == b.HandCount && a.HandLimit == b.HandLimit && a.RecipientSeat == b.RecipientSeat && a.SequenceBefore == b.SequenceBefore &&
        a.OriginalTargetSeats.SequenceEqual(b.OriginalTargetSeats) && a.BeforeTargetSeats.SequenceEqual(b.BeforeTargetSeats) &&
        a.CanceledPrimaryTargetSeats.SequenceEqual(b.CanceledPrimaryTargetSeats) && a.ResultTargetSeats.SequenceEqual(b.ResultTargetSeats) &&
        a.MaterialCardIds.SequenceEqual(b.MaterialCardIds);

    private bool HasOverflowAcceptedPrefix(CardUseFrame use, OverflowUseTargetsCanceledEvent fact, IReadOnlyList<IGameEvent> history, int boundary)
    {
        var r = fact.Receipt; var before = r.ActionBeforeCancellation;
        var prior = history.Take(boundary).ToArray();
        var previousUse = use with { SourceSeat = before.ActorSeat, CardKind = before.EffectiveKind, TargetSeats = r.BeforeTargetSeats, Action = before };
        if (prior.OfType<CardUseDeclaredEvent>().Where(e => e.ResolutionId == use.Id && e.CardId == use.CardId).ToArray() is not [var declared] ||
            !ShownEntityUseActorMatches(previousUse, declared.SourceSeat, before.ProviderSeat)) return false;
        for (var i = prior.Length - 1; i >= 0; i--)
        {
            if (prior[i] is not CardActionAcceptedEvent { Action: var accepted } || accepted.ActionId != before.ActionId ||
                accepted.Type != CardActionType.Use || accepted.ProviderSeat != before.ProviderSeat || accepted.ParentActionId != before.ParentActionId ||
                accepted.RequesterSeat != before.RequesterSeat || accepted.ResponderSeat != before.ResponderSeat || accepted.OpponentSeat != before.OpponentSeat ||
                accepted.EffectiveSuit != before.EffectiveSuit || accepted.EffectiveRank != before.EffectiveRank || !accepted.PhysicalCards.SequenceEqual(before.PhysicalCards)) continue;
            if (RebuildAcceptedActualHandGainUse(previousUse, accepted, before, prior, i)) return true;
        }
        return false;
    }

    private bool IsOverflowTargetCancellationFact(CardUseFrame use, OverflowUseTargetsCanceledEvent fact, IReadOnlyList<int> beforeTargets)
    {
        var r = fact.Receipt; var before = r.ActionBeforeCancellation;
        if (use.Action is not { Type: CardActionType.Use } current || r.CardUseFrameId != use.Id || r.ActionId != current.ActionId ||
            r.InstructionIndex != 1 || r.WindowFrameId <= use.Id || fact.ProgramFrameId <= r.WindowFrameId ||
            before.Type != CardActionType.Use || before.ActionId != r.ActionId || before.ActorSeat != r.Source.OwnerSeat ||
            before.ProviderSeat != r.ProviderSeat || before.EffectiveKind != r.EffectiveKind || before.ResponderSeat is not null || before.OpponentSeat is not null ||
            current.ProviderSeat != r.ProviderSeat || !current.PhysicalCards.SequenceEqual(before.PhysicalCards) ||
            !r.BeforeTargetSeats.SequenceEqual(beforeTargets) || !before.TargetSeats.SequenceEqual(r.BeforeTargetSeats) ||
            r.HandCount <= r.HandLimit || r.HandLimit < 0 || r.MaterialCardIds.Distinct().Count() != r.MaterialCardIds.Count ||
            r.MaterialCardIds.Any(id => !before.PhysicalCards.Any(c => c.CardId == id)) ||
            r.BeforeTargetSeats.Any(s => !IsValidPlayerSeat(s)) || r.OriginalTargetSeats.Any(s => !IsValidPlayerSeat(s)) ||
            r.EffectiveKind == CardKind.BorrowedSword && (r.BeforeTargetSeats.Count % 2 != 0 || r.OriginalTargetSeats.Count % 2 != 0) ||
            !r.CanceledPrimaryTargetSeats.SequenceEqual(OverflowPrimaryTargets(r.EffectiveKind, r.BeforeTargetSeats).Where(s => s != r.Source.OwnerSeat)) ||
            r.CanceledPrimaryTargetSeats.Count == 0 ||
            !r.ResultTargetSeats.SequenceEqual(OverflowRetainedTargets(r.EffectiveKind, r.Source.OwnerSeat, r.BeforeTargetSeats)) ||
            r.SequenceBefore < 0 || r.SequenceAfter != r.SequenceBefore || r.MovementBatchId is not null || r.MovementIssued ||
            _contentRegistry.GetSkill(r.Source.SkillId).Program is not { } program || program.GameplayHash != r.GameplayHash ||
            program.Triggers.SingleOrDefault(t => t.Id == r.Source.BindingId) is not { } trigger) return false;
        OverflowTargetCancellationContract.ValidateTrigger(r.Source.SkillId, trigger);
        var originalPrimary = OverflowPrimaryTargets(r.EffectiveKind, r.OriginalTargetSeats);
        int? expectedRecipient = originalPrimary is [var unique] && unique != r.Source.OwnerSeat ? unique : null;
        if (r.RecipientSeat != expectedRecipient || r.RecipientSeat is null && r.MaterialCardIds.Count != 0) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var facts = history.OfType<OverflowUseTargetsCanceledEvent>().Where(e => e.ProgramFrameId == fact.ProgramFrameId).ToArray();
        var boundary = Array.FindIndex(history, e => e is OverflowUseTargetsCanceledEvent canceled && canceled.ProgramFrameId == fact.ProgramFrameId);
        return facts is [var issued] && SameOverflowIssuance(issued.Receipt, r) && boundary >= 0 &&
            history.Take(boundary).OfType<TargetsConfirmedEvent>().Where(e => e.ResolutionId == use.Id).ToArray() is [var declaredTargets] &&
            declaredTargets.TargetSeats.SequenceEqual(r.OriginalTargetSeats) &&
            history.Take(boundary).OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == fact.ProgramFrameId && e.SkillId == r.Source.SkillId &&
                e.OwnerSeat == r.Source.OwnerSeat && e.BindingId == r.Source.BindingId && e.SkillInstanceId == r.Source.SkillInstanceId &&
                e.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized) == 1 && HasOverflowAcceptedPrefix(use, fact, history, boundary);
    }

    private bool ValidOverflowTargetCancellationReceipt(ProgramSkillFrame frame)
    {
        if (frame.OverflowTargetCancellation is not { } r || !ExactOverflowTargetParent(frame, out var use, out _) ||
            r.Source != new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId) ||
            r.GameplayHash != frame.GameplayHash || r.WindowFrameId != frame.WindowContext!.ParentFrameId ||
            r.CardUseFrameId != use.Id || r.ActionId != use.Action!.ActionId || r.ProviderSeat != use.Action.ProviderSeat ||
            r.EffectiveKind != use.CardKind || r.InstructionIndex != frame.InstructionIndex || r.SequenceAfter < r.SequenceBefore ||
            !use.TargetSeats.SequenceEqual(r.ResultTargetSeats) ||
            !SameOverflowAction(use.Action, CloneDesignatedExtraTargetAction(r.ActionBeforeCancellation, r.ResultTargetSeats)) ||
            CompleteProgramEventHistory().OfType<OverflowUseTargetsCanceledEvent>().Where(e => e.ProgramFrameId == frame.Id).ToArray() is not [var fact] ||
            !SameOverflowIssuance(fact.Receipt, r) || !IsOverflowTargetCancellationFact(use, fact, r.BeforeTargetSeats) ||
            CompleteProgramEventHistory().OfType<OverflowTargetCancellationCompletedEvent>().Any(e => e.ProgramFrameId == frame.Id)) return false;
        var movements = CompleteProgramEventHistory().OfType<OverflowUseMaterialTransferIssuedEvent>().Where(e => e.ProgramFrameId == frame.Id).ToArray();
        if (r.MaterialCardIds.Count == 0)
            return !r.MovementIssued && r.MovementBatchId is null && r.SequenceBefore == r.SequenceAfter && movements.Length == 0 && frame.PendingMovementContinuation is null;
        if (!r.MovementIssued || r.MovementBatchId is null || r.RecipientSeat is not { } recipient || movements is not [var movement] ||
            movement.CardUseFrameId != r.CardUseFrameId || movement.ActionId != r.ActionId || movement.RecipientSeat != recipient ||
            movement.ActualCount != r.MaterialCardIds.Count || movement.SequenceBefore != r.SequenceBefore || movement.SequenceAfter != r.SequenceAfter ||
            movement.MovementBatchId != r.MovementBatchId || frame.PendingMovementContinuation is not { } pending || pending.SubjectSeat != frame.OwnerSeat ||
            pending.BeforeCount != 0 || pending.CoverageResultBind is not null) return false;
        var paid = _cardMovements.Where(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter).ToArray();
        return paid.Select(m => m.CardId).SequenceEqual(r.MaterialCardIds) && paid.All(m => m.From == CardLocation.Processing &&
            m.To == CardLocation.Hand(recipient) && m.Reason.Value == OverflowMaterialReason &&
            r.ActionBeforeCancellation.PhysicalCards.Any(c => c.CardId == m.CardId && c.CardKind == m.CardKind));
    }

    private void AssertOverflowTargetCancellation(ProgramSkillFrame frame)
    {
        if (frame.OverflowTargetCancellation is null)
        {
            if (frame.TriggerId is null || !ProgramInstructionResolver.Default.Features(GetProgramTrigger(frame))
                .HasOperation(SkillProgramEffectOp.CancelOtherCurrentUseTargetsIfOverHandLimit)) return;
            if (CompleteProgramEventHistory().OfType<OverflowUseTargetsCanceledEvent>().Any(e => e.ProgramFrameId == frame.Id) &&
                !CompleteProgramEventHistory().OfType<OverflowTargetCancellationCompletedEvent>().Any(e => e.ProgramFrameId == frame.Id))
                throw new InvalidOperationException("An issued overflow cancellation lost its owning paid receipt.");
            return;
        }
        if (!ValidOverflowTargetCancellationReceipt(frame))
            throw new InvalidOperationException("Overflow cancellation lost its exact actual use, designation, source or native material invoice.");
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        if (index + 1 < _resolutionStack.Count && !OverflowTargetCancellationFirstChild(frame, _resolutionStack[index + 1]))
            throw new InvalidOperationException("Overflow cancellation retained an unrelated native child.");
    }

    private bool OverflowTargetCancellationFirstChild(ProgramSkillFrame frame, ResolutionFrame child)
    {
        if (frame.OverflowTargetCancellation is not { } r || !ValidOverflowTargetCancellationReceipt(frame)) return false;
        if (child is CardsMovedTriggerWindowFrame moved)
            return r.MovementIssued && moved.ResumeProgramFrameId is null && moved.Batch.Id == r.MovementBatchId &&
                moved.Batch.ParentFrameId == frame.Id && moved.Batch.AwaitingProgramFrameId == frame.Id &&
                moved.Batch.OriginOwnerSeat == frame.OwnerSeat && moved.Batch.OriginSkillId == frame.SkillId &&
                moved.Batch.OriginSkillInstanceId == frame.SkillInstanceId && moved.Batch.Movements.Count == r.MaterialCardIds.Count &&
                moved.Batch.Movements.Select(m => m.CardId).SequenceEqual(r.MaterialCardIds) && moved.Batch.Movements.All(m =>
                    _cardMovements.Contains(m) && m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter &&
                    m.From == CardLocation.Processing && m.To == CardLocation.Hand(r.RecipientSeat!.Value) && m.Reason.Value == OverflowMaterialReason);
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return skills.ResumeProgramFrameId == frame.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
        return child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
            state.ResumeProgramFrameId == frame.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
            state.CandidateIndex >= 0 && state.CandidateIndex <= state.Candidates.Count &&
            CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id &&
                e.Change.ParentFrameId == frame.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
    }

    private bool OverflowTargetCancellationStructuralEdge(ResolutionFrame parent, ResolutionFrame child) =>
        parent is ProgramSkillFrame root && OverflowTargetCancellationFirstChild(root, child) ||
        parent is ProgramCardTriggerWindowFrame window && child is ProgramSkillFrame { OverflowTargetCancellation: { } r } program &&
            r.WindowFrameId == window.Id && ValidOverflowTargetCancellationReceipt(program);

    private ProgramSkillFrame? OverflowTargetCancellationObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root || !OverflowTargetCancellationFirstChild(root, _resolutionStack[index + 1])) continue;
            var aligned = true;
            for (var child = index + 2; child < _resolutionStack.Count; child++)
            {
                if (_resolutionStack[child] is ProgramLifecycleTriggerWindowFrame changed && _resolutionStack[child - 1] is ProgramSkillFrame owner &&
                    changed.Window == SkillProgramTriggerWindow.SkillsChanged && changed.ResumeProgramFrameId == owner.Id &&
                    changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && changed.CandidateIndex >= 0 &&
                    changed.CandidateIndex <= changed.Candidates.Count) continue;
                if (!DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !OriginalHandEntityStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !OutsidePhaseDrawDiscardStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !CompletedUndamagedTargetRevealStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !RecipientCategoryMarkStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !OffTurnUsedCardGiftStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !SameNameHandStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !ResponseCompletionStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !CardSupplyCompletionStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !MatchingRecastStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !OrderedPrintedSkillLossStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !OverflowTargetCancellationStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !HalfHandPaidDamageObserverEdge(child) && !PaidTargetObserverEdge(child)) { aligned = false; break; }
                if (_resolutionStack[child] is DyingFrame dying && child + 1 < _resolutionStack.Count &&
                    (IsOriginalDyingSuspendedByDyingSuits(dying) || IsOriginalDyingSuspendedByRecipientCategoryMark(dying) ||
                     IsOriginalDyingSuspendedByOwnedDeathBenefit(dying) || IsPaidHandRepaymentProgramAlcoholRide(child, dying) ||
                     IsPaidHandRepaymentRescueRide(child, dying) || PolicyCounterspellVirtualAlcoholRide(child, dying) ||
                     PaidObserverDamageVirtualAlcoholRide(child, dying) || TieredRoundZeroDyingRescueRide(child, dying) ||
                     DrawFundedDistinctBasicDyingRescueRide(child, dying))) break;
            }
            if (aligned) return root;
        }
        return null;
    }

    private bool IsOverflowTargetCancellationDying() => ActiveDying is { } dying && OverflowTargetCancellationObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasOverflowTargetCancellationDamageObserver(long id) => HasOverflowTargetCancellationObserver(id, false);
    private bool HasOverflowTargetCancellationBeforeDamageObserver(long id) => HasOverflowTargetCancellationObserver(id, true);
    private bool HasOverflowTargetCancellationObserver(long id, bool before)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == id && (before ? f is BeforeDamageProgramWindowFrame : f is DamageTriggerWindowFrame));
        if (index < 0 || OverflowTargetCancellationObserverRoot() is not { } root) return false;
        var rootIndex = _resolutionStack.FindIndex(f => f.Id == root.Id);
        if (index > rootIndex) return true;
        for (var child = index + 1; child <= rootIndex; child++)
            if (!OverflowTargetCancellationStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !OrderedPrintedSkillLossStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !OrderedPrintedSkillLossSkillsChangedEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !MatchingRecastStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child])) return false;
        return index < rootIndex;
    }
    private bool AllowsOverflowTargetCancellationNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or
                SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            OverflowTargetCancellationObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }

    private bool TryAdvanceOverflowTargetCancellationSubtree()
    {
        if (_pendingDecision is not null || OverflowTargetCancellationObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attack)
        {
            if (CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null || attack.AttackReturn is null ||
                _resolutionStack.Any(f => f is DamageFrame damage && damage.ParentFrameId == attack.Id ||
                    f is BeforeDamageProgramWindowFrame before && (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == attack.Id)) return false;
            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id)); AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or
            ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame or RecoveryReplacementFrame)
        { AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true; }
        if (top is DeathFrame death) { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        return false;
    }
}
