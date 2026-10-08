namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string OverflowMaterialReason = "skill-program.overflow-target-cancellation.give";
    private long OverflowMovementSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private static IReadOnlyList<int> OverflowPrimaryTargets(CardKind kind, IReadOnlyList<int> targets) =>
        kind == CardKind.BorrowedSword ? Array.AsReadOnly(targets.Where((_, index) => index % 2 == 0).ToArray()) : targets;

    private static IReadOnlyList<int> OverflowRetainedTargets(CardKind kind, int owner, IReadOnlyList<int> targets) =>
        Array.AsReadOnly(kind == CardKind.BorrowedSword
            ? targets.Chunk(2).Where(pair => pair.Length == 2 && pair[0] == owner).SelectMany(pair => pair).ToArray()
            : targets.Where(target => target == owner).ToArray());

    private bool TryGetOverflowTargetUse(int owner, ProgramSkillWindowContext context,
        out CardUseFrame use, out ProgramCardTriggerWindowFrame window)
    {
        use = null!; window = null!;
        if (context is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } identity } ||
            context.OwnerSeat != owner || identity.ActorSeat != owner ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } found ||
            found.ParentFrameId != identity.ParentCardUseFrameId || found.CompletedResponseReturn is not null ||
            found.Continuation is not (ProgramCardContinuation.Slash or ProgramCardContinuation.FinalizedTrick or
                ProgramCardContinuation.FinalizedSimpleCard or ProgramCardContinuation.DelayedCard) ||
            GetCardActionWindow(found) != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
            LifecycleCardUse(found.ParentFrameId) is not { Action: { Type: CardActionType.Use } action } actual ||
            action.ActionId != identity.CardActionId || action.ActorSeat != owner || actual.SourceSeat != owner || identity.EffectiveKind != actual.CardKind ||
            action.ResponderSeat is not null || action.OpponentSeat is not null || action.EffectiveKind != actual.CardKind ||
            found.Action.ActionId != action.ActionId || found.Action.ActorSeat != owner ||
            !found.Action.TargetSeats.SequenceEqual(actual.TargetSeats) || !action.TargetSeats.SequenceEqual(actual.TargetSeats) ||
            actual.TargetSeats.Any(s => !IsValidPlayerSeat(s)) ||
            action.PhysicalCards.Any(c => c.CardId <= 0) || action.PhysicalCards.DistinctBy(c => c.CardId).Count() != action.PhysicalCards.Count ||
            actual.CardKind == CardKind.BorrowedSword && actual.TargetSeats.Count % 2 != 0) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == found.Id);
        if (index < 1 || _resolutionStack[index - 1].Id != actual.Id) return false;
        use = actual; window = found; return true;
    }

    private bool ExactOverflowTargetParent(ProgramSkillFrame frame, out CardUseFrame use, out ProgramCardTriggerWindowFrame window)
    {
        use = null!; window = null!;
        if (frame.TriggerId is null || frame.InstructionIndex != 1 || frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            frame.WindowContext is not { } context || !TryGetOverflowTargetUse(frame.OwnerSeat, context, out use, out window) ||
            !DesignatedExtraTargetCandidateMatches(new(frame.OwnerSeat, frame.SkillId, frame.TriggerId, frame.SkillInstanceId, frame.GameplayHash, 0), context, window)) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        if (index < 1 || _resolutionStack[index - 1].Id != window.Id || !window.Activated ||
            _contentRegistry.GetSkill(frame.SkillId).Program is not { } program || program.GameplayHash != frame.GameplayHash ||
            program.Triggers.SingleOrDefault(t => t.Id == frame.TriggerId) is not { } trigger) return false;
        OverflowTargetCancellationContract.ValidateTrigger(frame.SkillId, trigger);
        return CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == frame.Id &&
            e.OwnerSeat == frame.OwnerSeat && e.SkillId == frame.SkillId && e.BindingId == frame.TriggerId &&
            e.SkillInstanceId == frame.SkillInstanceId && e.Window == context.Window) == 1;
    }

    private bool CanOfferOverflowTargetCancellation(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.CancelOtherCurrentUseTargetsIfOverHandLimit)) return true;
        return TryGetOverflowTargetUse(candidate.OwnerSeat, context, out var use, out var window) &&
            DesignatedExtraTargetCandidateMatches(candidate, context, window) &&
            OverflowPrimaryTargets(use.CardKind, use.TargetSeats).Any(s => s != candidate.OwnerSeat) &&
            GetHand(_players[candidate.OwnerSeat]).Count > GetHandLimit(_players[candidate.OwnerSeat]) &&
            !CompleteProgramEventHistory().OfType<OverflowUseTargetsCanceledEvent>().Any(e =>
                e.Receipt.CardUseFrameId == use.Id && e.Receipt.ActionId == use.Action!.ActionId &&
                e.Receipt.Source == new CardConversionSource(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId));
    }

    private SkillProgramStepOutcome BeginOverflowTargetCancellation(ProgramSkillFrame input)
    {
        var frame = GetActiveProgramFrame(input.Id);
        if (!ExactOverflowTargetParent(frame, out var use, out var window) || frame.OverflowTargetCancellation is not null ||
            frame.PendingMovementContinuation is not null ||
            CompleteProgramEventHistory().OfType<OverflowUseTargetsCanceledEvent>().Any(e => e.ProgramFrameId == frame.Id))
            throw new InvalidOperationException("Overflow cancellation lost its exact unpaid designation instruction.");
        var owner = _players[frame.OwnerSeat];
        var hand = GetHand(owner).Count; var limit = GetHandLimit(owner);
        if (_winner != Winner.None || !owner.IsAlive || !HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId) ||
            hand <= limit || !OverflowPrimaryTargets(use.CardKind, use.TargetSeats).Any(s => s != frame.OwnerSeat)) return SkillProgramStepOutcome.Continue;
        if (!HasExactAcceptedActualHandGainUse(use, use.Action!))
            throw new InvalidOperationException("Overflow cancellation cannot replace an unproven actual use.");
        var originals = CompleteProgramEventHistory().OfType<TargetsConfirmedEvent>().Where(e => e.ResolutionId == use.Id).ToArray();
        if (originals is not [var original] || original.TargetSeats.Any(s => !IsValidPlayerSeat(s)) ||
            use.CardKind == CardKind.BorrowedSword && original.TargetSeats.Count % 2 != 0)
            throw new InvalidOperationException("Overflow cancellation lost the original designation.");
        var originalPrimary = OverflowPrimaryTargets(use.CardKind, original.TargetSeats);
        int? recipient = originalPrimary is [var unique] && unique != frame.OwnerSeat ? unique : null;
        var beforeAction = use.Action!;
        var retained = OverflowRetainedTargets(use.CardKind, frame.OwnerSeat, use.TargetSeats);
        var materials = recipient is { } recipientSeat && _players[recipientSeat].IsAlive ? beforeAction.PhysicalCards.Select(c => c.CardId)
            .Where(id => _cardZones.GetLocation(id) == CardLocation.Processing && !IsClaimedUseCardEntity(use.Id, id)).ToArray() : [];
        var receipt = new OverflowTargetCancellationReceipt(1, new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId),
            frame.GameplayHash, window.Id, use.Id, beforeAction.ActionId, beforeAction.ProviderSeat, use.CardKind, beforeAction,
            hand, limit, recipient, OverflowMovementSequence, OverflowMovementSequence, null)
        {
            OriginalTargetSeats = original.TargetSeats, BeforeTargetSeats = use.TargetSeats,
            CanceledPrimaryTargetSeats = OverflowPrimaryTargets(use.CardKind, use.TargetSeats).Where(s => s != frame.OwnerSeat).ToArray(),
            ResultTargetSeats = retained, MaterialCardIds = materials
        };
        ReplaceRuntimeTop(frame = frame with { OverflowTargetCancellation = receipt,
            PendingMovementContinuation = materials.Length > 0 ? new(frame.OwnerSeat, 0, null) : null });
        var updated = CloneDesignatedExtraTargetAction(beforeAction, retained);
        UpdateProgramRoleCardUse(use with { TargetSeats = retained, TargetIndex = 0, Action = updated, TargetsAdjusted = true,
            SlashTargetsCancelled = IsSlashCard(use.CardKind) && retained.Count == 0,
            SequentialTrick = retained.Count == 0 ? null : use.SequentialTrick,
            PreparedTargetAttacks = use.PreparedTargetAttacks is { } prepared
                ? Array.AsReadOnly(prepared.Where(attack => retained.Contains(attack.TargetSeat)).ToArray()) : null,
            Continuations = use.Continuations with { FangtianHalberd = use.Continuations.FangtianHalberd is { Active: true } multi
                ? multi with { TargetSeats = retained } : use.Continuations.FangtianHalberd } }, updated);
        AdvanceEventRulesAndQueueFact(new OverflowUseTargetsCanceledEvent(frame.Id, receipt));
        if (materials.Length > 0)
        {
            var destination = CardLocation.Hand(recipient!.Value);
            var batch = BeginCardMovementBatch([CardLocation.Processing], [destination]);
            var records = new List<CardMovementRecord>(); var committed = false;
            try
            {
                foreach (var id in materials)
                {
                    var card = EntityAtCurrentLocation(id);
                    _cardZones.Move(id, CardLocation.Processing, destination);
                    records.Add(RecordMovement(card, CardLocation.Processing, destination, new(OverflowMaterialReason)));
                }
                committed = true;
            }
            finally { CompleteCardMovementBatch(batch, records, committed); }
            receipt = receipt with { SequenceAfter = OverflowMovementSequence, MovementBatchId = batch.Id, MovementIssued = true };
            ReplaceRuntimeTop(frame = GetActiveProgramFrame(frame.Id) with { OverflowTargetCancellation = receipt });
            AdvanceEventRulesAndQueueFact(new OverflowUseMaterialTransferIssuedEvent(frame.Id, use.Id, beforeAction.ActionId, recipient.Value,
                materials.Length, receipt.SequenceBefore, receipt.SequenceAfter, receipt.MovementBatchId));
        }
        AdvanceRuntimeProgram(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool DrainOverflowTargetChildren(ProgramSkillFrame frame) =>
        TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(frame.Id) || TryBeginAdvancedSkillsChanged(frame.Id);

    private bool ResumeOverflowTargetCancellation(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != id || frame.OverflowTargetCancellation is not { } receipt) return false;
        AssertOverflowTargetCancellation(frame);
        if (DrainOverflowTargetChildren(frame)) return true;
        AdvanceEventRulesAndQueueFact(new OverflowTargetCancellationCompletedEvent(frame.Id, receipt.CardUseFrameId, receipt.ActionId));
        ReplaceRuntimeTop(frame = frame with { OverflowTargetCancellation = null, PendingMovementContinuation = null });
        FinishProgramSkill(frame, true);
        return true;
    }

    private bool ReturnOverflowTargetCancellation(ProgramSkillFrame frame)
    {
        if (frame.OverflowTargetCancellation is null || frame.PendingMovementContinuation is null) return false;
        AssertOverflowTargetCancellation(frame); AdvanceRuntimeProgram(frame.Id); return true;
    }

    private bool CanContinueOverflowTargetCancellation(ProgramSkillFrame frame) =>
        frame.OverflowTargetCancellation is not null && ValidOverflowTargetCancellationReceipt(frame);

    private bool IsOverflowTargetCancellationAwaitedMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.CancelOtherCurrentUseTargetsIfOverHandLimit && frame.PendingMovementContinuation == pending &&
        pending.SubjectSeat == frame.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        frame.OverflowTargetCancellation is { MaterialCardIds.Count: > 0 } receipt && receipt.InstructionIndex == frame.InstructionIndex &&
        ExactOverflowTargetParent(frame, out var use, out _) && receipt.CardUseFrameId == use.Id && receipt.ActionId == use.Action!.ActionId;

    private bool HasOverflowTargetCancellation(CardUseFrame use) => use.Action is { } action &&
        CompleteProgramEventHistory().OfType<OverflowUseTargetsCanceledEvent>().Any(e => e.Receipt.CardUseFrameId == use.Id &&
            e.Receipt.ActionId == action.ActionId && IsOverflowTargetCancellationFact(use, e, e.Receipt.BeforeTargetSeats));

    private bool IsOverflowTargetCancellationPhysicalClaim(long useId, int cardId) =>
        LifecycleCardUse(useId) is { Action: { } action } use &&
        CompleteProgramEventHistory().OfType<OverflowUseTargetsCanceledEvent>().Any(e => e.Receipt.CardUseFrameId == useId &&
            e.Receipt.ActionId == action.ActionId && e.Receipt.MaterialCardIds.Contains(cardId) &&
            IsOverflowTargetCancellationFact(use, e, e.Receipt.BeforeTargetSeats) &&
            CompleteProgramEventHistory().OfType<OverflowUseMaterialTransferIssuedEvent>().Any(m => m.ProgramFrameId == e.ProgramFrameId &&
                _cardMovements.Count(move => move.CardId == cardId && move.Sequence > m.SequenceBefore && move.Sequence <= m.SequenceAfter &&
                    move.From == CardLocation.Processing && move.To == CardLocation.Hand(m.RecipientSeat) && move.Reason.Value == OverflowMaterialReason) == 1));

    private bool TryCompleteOverflowCanceledUse(ProgramCardTriggerWindowFrame window)
    {
        if (window.Continuation is not (ProgramCardContinuation.Slash or ProgramCardContinuation.FinalizedTrick or
                ProgramCardContinuation.FinalizedSimpleCard or ProgramCardContinuation.DelayedCard) ||
            LifecycleCardUse(window.ParentFrameId) is not { TargetSeats.Count: 0 } use || !(HasOverflowTargetCancellation(use) ||
                    window.Continuation == ProgramCardContinuation.FinalizedTrick && HasRoundGainedTrickTargetCancellation(use))) return false;
        if (_resolutionStack.LastOrDefault()?.Id != use.Id || window.Action.ActionId != use.Action!.ActionId)
            throw new InvalidOperationException("Overflow cancellation lost its whole-use native return.");
        if (window.Continuation == ProgramCardContinuation.Slash)
        {
            if (window.AttackOwnerFrameId != use.Id || ActiveCardAttack is not { } attack || attack.ResolutionId != use.Id)
                throw new InvalidOperationException("Overflow cancellation lost its exact Slash attack.");
            CompleteAttack(attack);
        }
        else
        {
            var representation = use.CardId == 0 ? new Card(0, use.CardKind, Suit.None, 0) : EntityAtCurrentLocation(use.CardId);
            FinishCardUse(use.Id, representation, use.CardKind);
        }
        return true;
    }

    private sealed partial class ProgramSkillHost : IOverflowTargetCancellationHost
    {
        public SkillProgramStepOutcome BeginOverflowTargetCancellation(ProgramSkillFrame frame) => engine.BeginOverflowTargetCancellation(frame);
        public bool CanContinueOverflowTargetCancellation(ProgramSkillFrame frame) => engine.CanContinueOverflowTargetCancellation(frame);
    }
}
