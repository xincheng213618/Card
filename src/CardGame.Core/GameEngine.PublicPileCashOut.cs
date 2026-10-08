namespace CardGame.Core;

public sealed partial class GameEngine
{
    private long PublicPileCashOutSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private static string PublicPileCashOutReason(ProgramSkillFrame frame, string step) =>
        $"skill-program.{frame.SkillId}.{frame.PublicPileCashOut!.Operation}.{step}";

    private bool PublicPileStorageCard(int owner, string skill, string instance, Card card, CardLocation location) =>
        location.OwnerSeat == owner && location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
        !card.IsGeneralWeapon && !IsActiveProgramSourceEquipmentCard(owner, skill, instance, card) &&
        !IsForeignEquipmentDiscardPrevented(owner, card, location, OwnedCardMoveIntent.Transfer);

    private bool MatchesPublicPileStorageSelection(ProgramSkillFrame frame, string bind, Card card, CardLocation location)
    {
        if (frame.TriggerId is null || GetProgramTrigger(frame).Effects is not
            [{ Op: SkillProgramEffectOp.SelectOwnedCards } selection, { Op: SkillProgramEffectOp.StoreBoundCardsInPublicPile } store] ||
            selection.ResultBind != bind || store.SourceBind != bind) return true;
        return frame.InstructionIndex == 1 && PublicPileStorageCard(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, card, location);
    }

    private bool CanRunPublicPileCashOut(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(effect => PublicPileCashOutContract.IsOperation(effect.Op))) return true;
        if (context.OwnerSeat != candidate.OwnerSeat || context.TargetSeat != candidate.OwnerSeat ||
            !_players[candidate.OwnerSeat].IsAlive) return false;
        if (trigger.Effects.Last().Op == SkillProgramEffectOp.StoreBoundCardsInPublicPile)
            return context.Window == SkillProgramTriggerWindow.AfterDamageApplied && context.Amount > 0 &&
                GetHand(_players[candidate.OwnerSeat]).Concat(GetEquipment(_players[candidate.OwnerSeat])).Any(card =>
                    PublicPileStorageCard(candidate.OwnerSeat, candidate.SkillId, candidate.SkillInstanceId, card, _cardZones.GetLocation(card.Id)));
        return context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && candidate.OwnerSeat == _currentSeat &&
            context.SourceSeat == candidate.OwnerSeat &&
            _publicPersistentPiles.GetValueOrDefault((candidate.OwnerSeat, candidate.SkillId, candidate.SkillInstanceId)) is { } pile &&
            PublicPileCards(pile).Count > 0;
    }

    private bool ExactPublicPileCashOutParent(ProgramSkillFrame frame, SkillProgramEffectOp op)
    {
        if (frame.TriggerId is null || frame.WindowContext is not { } context || context.OwnerSeat != frame.OwnerSeat ||
            context.TargetSeat != frame.OwnerSeat || frame.GameplayHash != _contentRegistry.GetSkill(frame.SkillId).Program?.GameplayHash ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0) return false;
        var trigger = GetProgramTrigger(frame);
        if (op == SkillProgramEffectOp.StoreBoundCardsInPublicPile ? frame.InstructionIndex != 2 ||
                trigger.Effects is not [{ Op: SkillProgramEffectOp.SelectOwnedCards }, { Op: SkillProgramEffectOp.StoreBoundCardsInPublicPile }] :
            op != SkillProgramEffectOp.CashOutPublicPile || frame.InstructionIndex != 1 ||
                trigger.Effects is not [{ Op: SkillProgramEffectOp.CashOutPublicPile }]) return false;
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        if (index <= 0 || _resolutionStack[index - 1].Id != context.ParentFrameId) return false;
        if (CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(fact => fact.FrameId == frame.Id &&
                fact.OwnerSeat == frame.OwnerSeat && fact.SkillId == frame.SkillId && fact.BindingId == frame.TriggerId &&
                fact.SkillInstanceId == frame.SkillInstanceId && fact.Window == context.Window) != 1) return false;
        if (op == SkillProgramEffectOp.StoreBoundCardsInPublicPile)
        {
            if (context.Window != SkillProgramTriggerWindow.AfterDamageApplied || context.Amount <= 0 ||
                context.OccurrenceIndex < 0 || context.OccurrenceIndex >= context.Amount ||
                _resolutionStack[index - 1] is not DamageTriggerWindowFrame window || window.TriggerWindow != context.Window ||
                window.TargetSeat != frame.OwnerSeat ||
                window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
                !MountObserverCandidateMatches(frame, window.Candidates[window.CandidateIndex].ToProgramCandidate()) ||
                _resolutionStack.OfType<DamageFrame>().SingleOrDefault(damage => damage.Id == window.ParentFrameId) is not { } applied ||
                context.DamageFrameId != applied.Id || applied.SourceSeat != window.SourceSeat || applied.TargetSeat != frame.OwnerSeat ||
                applied.Amount != context.Amount) return false;
            var attack = GetDamageTriggerAttack(window);
            if (context.SourceSeat != (attack.IsSourceLess ? null : applied.SourceSeat)) return false;
            return CompleteProgramEventHistory().OfType<DamageRequestedEvent>().Count(fact => fact.ResolutionId == applied.Id &&
                fact.SourceSeat == applied.SourceSeat && fact.TargetSeat == applied.TargetSeat && fact.Amount == applied.Amount &&
                fact.Nature == applied.Nature && fact.SourceLess == attack.IsSourceLess) == 1;
        }
        return context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && frame.OwnerSeat == _currentSeat &&
            context.SourceSeat == frame.OwnerSeat && _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame start &&
            start.Window == context.Window && start.OwnerSeat == frame.OwnerSeat &&
            start.Continuation == ProgramLifecycleContinuation.NormalTurnStart && start.CandidateIndex >= 0 &&
            start.CandidateIndex < start.Candidates.Count && MountObserverCandidateMatches(frame, start.Candidates[start.CandidateIndex]);
    }

    private SkillProgramStepOutcome BeginPublicPileCashOut(SkillProgramEffect effect, ProgramSkillFrame input)
    {
        var frame = GetActiveProgramFrame(input.Id);
        AssertPublicPileCashOut(frame);
        if (frame.PublicPileCashOut is not null || frame.PendingMovementContinuation is not null ||
            !ExactPublicPileCashOutParent(frame, effect.Op) ||
            CompleteProgramEventHistory().OfType<PublicPileCashOutStartedEvent>().Any(fact => fact.FrameId == frame.Id))
            throw new InvalidOperationException("Public pile cash-out requires its exact real owner window and unpaid instruction.");
        if (!_players[frame.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        PublicPersistentPileSource pile;
        int[] ids;
        CardLocation[] from;
        if (effect.Op == SkillProgramEffectOp.StoreBoundCardsInPublicPile)
        {
            var bound = GetProgramCardSet(frame, effect.SourceBind!);
            if (bound.CardIds is not [var id] || bound.SourceLocations is not [var location] ||
                bound.SelectionActorSeat is { } actor && actor != frame.OwnerSeat ||
                _cardZones.GetLocation(id) != location ||
                _cardZones.CardsAt(location).SingleOrDefault(entity => entity.Id == id) is not { } card ||
                !PublicPileStorageCard(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, card, location))
                throw new InvalidOperationException("Public pile storage requires exactly one still-payable original owner HE card.");
            ids = [id]; from = [location];
            pile = EnsurePublicPileSource(frame, int.MaxValue);
        }
        else
        {
            var existing = _publicPersistentPiles.GetValueOrDefault((frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId));
            if (existing is null || PublicPileCards(existing).Count == 0) return SkillProgramStepOutcome.Continue;
            if (existing.Capacity != int.MaxValue || effect.Amount is < 1 or > 4)
                throw new InvalidOperationException("Public pile cash-out requires its own unbounded storage source and bounded draw multiplier.");
            pile = existing;
            ids = PublicPileCards(pile).Select(card => card.Id).ToArray();
            from = ids.Select(_cardZones.GetLocation).ToArray();
            _ = checked(ids.Length * effect.Amount);
        }
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        var before = PublicPileCashOutSequence;
        var receipt = new PublicPileCashOutReceipt
        {
            InstructionIndex = frame.InstructionIndex, Operation = effect.Op,
            Issuer = new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId),
            GameplayHash = frame.GameplayHash, ActualTurn = _turnNumber, ParentId = frame.WindowContext!.ParentFrameId,
            Stage = PublicPileCashOutStage.PaymentChildren, Pile = pile, FrozenCount = ids.Length,
            PaidCardIds = ids, PaidFrom = from, Before = before, After = before
        };
        ReplaceRuntimeTop(frame = frame with { PublicPileCashOut = receipt, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new PublicPileCashOutStartedEvent(frame.Id, receipt.Operation, receipt.Issuer,
            receipt.GameplayHash, receipt.ActualTurn, receipt.ParentId, pile, receipt.FrozenCount));
        MoveProgramCardsFromMultipleSources(ids,
            effect.Op == SkillProgramEffectOp.StoreBoundCardsInPublicPile ? pile.Location : CardLocation.DiscardPile,
            new(PublicPileCashOutReason(frame, "payment")), (_, _) =>
            {
                var paid = GetActiveProgramFrame(input.Id);
                var invoice = paid.PublicPileCashOut! with { After = PublicPileCashOutSequence };
                ReplaceRuntimeTop(paid with { PublicPileCashOut = invoice });
                AdvanceEventRulesAndQueueFact(new PublicPileCashOutPaidEvent(paid.Id, invoice.Operation,
                    invoice.FrozenCount, invoice.Before, invoice.After));
            });
        AdvanceRuntimeProgram(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool DrainPublicPileCashOut(ProgramSkillFrame frame) =>
        TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(frame.Id) || TryBeginAdvancedSkillsChanged(frame.Id);

    private bool ResumePublicPileCashOut(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != id || frame.PublicPileCashOut is not { } receipt) return false;
        AssertPublicPileCashOut(frame);
        if (PreparationGameEnded()) return true;
        if (DrainPublicPileCashOut(frame)) return true;
        frame = GetActiveProgramFrame(id); receipt = frame.PublicPileCashOut!;
        ReplaceRuntimeTop(frame = frame with { PendingMovementContinuation = null });
        if (receipt.Operation == SkillProgramEffectOp.StoreBoundCardsInPublicPile || !_players[frame.OwnerSeat].IsAlive ||
            receipt.Stage == PublicPileCashOutStage.DrawChildren)
        {
            var grant = receipt.Operation == SkillProgramEffectOp.CashOutPublicPile && _players[frame.OwnerSeat].IsAlive &&
                receipt.Stage == PublicPileCashOutStage.DrawChildren ? receipt.FrozenCount : 0;
            if (grant > 0) GrantProgramTurnRuleModifier(frame, SkillRuleQuery.SlashLimit, SkillRuleOperation.Add, grant, []);
            AdvanceEventRulesAndQueueFact(new PublicPileCashOutResolvedEvent(frame.Id, receipt.Operation, receipt.FrozenCount, receipt.DrawActual, grant));
            ReplaceRuntimeTop(frame = frame with { PublicPileCashOut = null });
            FinishProgramSkill(frame, true);
            return true;
        }
        var requested = checked(receipt.FrozenCount * GetProgramTrigger(frame).Effects.Single().Amount);
        var before = PublicPileCashOutSequence;
        ReplaceRuntimeTop(frame = frame with { PublicPileCashOut = receipt with
        {
            Stage = PublicPileCashOutStage.DrawChildren, DrawRequested = requested, DrawBefore = before, DrawAfter = before
        }, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        var actual = DrawCards(_players[frame.OwnerSeat], requested, true, new(PublicPileCashOutReason(frame, "draw"))).Count;
        frame = GetActiveProgramFrame(id);
        receipt = frame.PublicPileCashOut! with { DrawActual = actual, DrawAfter = PublicPileCashOutSequence };
        ReplaceRuntimeTop(frame = frame with { PublicPileCashOut = receipt });
        AdvanceEventRulesAndQueueFact(new PublicPileCashOutDrawIssuedEvent(id, requested, actual, before, receipt.DrawAfter));
        AdvanceRuntimeProgram(id);
        return true;
    }

    private bool ReturnPublicPileCashOutMovement(ProgramSkillFrame frame)
    {
        if (frame.PublicPileCashOut is null || frame.PendingMovementContinuation is null) return false;
        AssertPublicPileCashOut(frame);
        AdvanceRuntimeProgram(frame.Id);
        return true;
    }

    private bool ValidPublicPileCashOut(ProgramSkillFrame frame)
    {
        if (frame.PublicPileCashOut is not { } receipt || receipt.InstructionIndex != frame.InstructionIndex ||
            receipt.GameplayHash != frame.GameplayHash || receipt.ActualTurn != _turnNumber || !Enum.IsDefined(receipt.Stage) ||
            receipt.Issuer != new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId) ||
            frame.WindowContext?.ParentFrameId != receipt.ParentId || !ExactPublicPileCashOutParent(frame, receipt.Operation) ||
            receipt.Pile is not { } pile || pile.OwnerSeat != frame.OwnerSeat || pile.SkillId != frame.SkillId ||
            pile.SkillInstanceId != frame.SkillInstanceId || pile.Capacity != int.MaxValue ||
            receipt.FrozenCount <= 0 || receipt.FrozenCount != receipt.PaidCardIds.Count ||
            receipt.PaidCardIds.Distinct().Count() != receipt.FrozenCount || receipt.PaidFrom.Count != receipt.FrozenCount ||
            frame.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != frame.OwnerSeat ||
            receipt.Before < 0 || receipt.After <= receipt.Before || receipt.After > PublicPileCashOutSequence) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<PublicPileCashOutStartedEvent>().Where(fact => fact.FrameId == frame.Id).ToArray() is not [var started] ||
            started != new PublicPileCashOutStartedEvent(frame.Id, receipt.Operation, receipt.Issuer, receipt.GameplayHash,
                receipt.ActualTurn, receipt.ParentId, pile, receipt.FrozenCount) ||
            history.OfType<PublicPileCashOutPaidEvent>().Where(fact => fact.FrameId == frame.Id).ToArray() is not [var paid] ||
            paid != new PublicPileCashOutPaidEvent(frame.Id, receipt.Operation, receipt.FrozenCount, receipt.Before, receipt.After) ||
            history.OfType<PublicPileCashOutResolvedEvent>().Any(fact => fact.FrameId == frame.Id)) return false;
        var destination = receipt.Operation == SkillProgramEffectOp.StoreBoundCardsInPublicPile ? pile.Location : CardLocation.DiscardPile;
        var invoice = _cardMovements.Where(move => move.Sequence > receipt.Before && move.Sequence <= receipt.After &&
            move.Reason.Value == PublicPileCashOutReason(frame, "payment")).ToArray();
        if (invoice.Length != receipt.FrozenCount || receipt.PaidCardIds.Select((id, index) => (id, from: receipt.PaidFrom[index])).Any(item =>
            (receipt.Operation == SkillProgramEffectOp.StoreBoundCardsInPublicPile ? item.from.OwnerSeat != frame.OwnerSeat ||
                item.from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) : item.from != pile.Location) ||
            invoice.Count(move => move.CardId == item.id && move.From == item.from && move.To == destination) != 1)) return false;
        if (receipt.Operation == SkillProgramEffectOp.StoreBoundCardsInPublicPile)
        {
            var binding = GetProgramCardSet(frame, GetProgramTrigger(frame).Effects[1].SourceBind!);
            if (receipt.FrozenCount != 1 || !binding.CardIds.SequenceEqual(receipt.PaidCardIds) ||
                !binding.SourceLocations.SequenceEqual(receipt.PaidFrom) || binding.SelectionActorSeat is { } actor && actor != frame.OwnerSeat ||
                receipt.Stage != PublicPileCashOutStage.PaymentChildren) return false;
        }
        var draws = history.OfType<PublicPileCashOutDrawIssuedEvent>().Where(fact => fact.FrameId == frame.Id).ToArray();
        if (receipt.Stage == PublicPileCashOutStage.PaymentChildren)
            return draws.Length == 0 && receipt.DrawRequested == 0 && receipt.DrawActual == 0 && receipt.DrawBefore == 0 && receipt.DrawAfter == 0;
        if (receipt.Operation != SkillProgramEffectOp.CashOutPublicPile ||
            receipt.DrawRequested != checked(receipt.FrozenCount * GetProgramTrigger(frame).Effects.Single().Amount) ||
            receipt.DrawActual < 0 || receipt.DrawActual > receipt.DrawRequested || receipt.DrawBefore < receipt.After ||
            receipt.DrawAfter < receipt.DrawBefore || receipt.DrawAfter > PublicPileCashOutSequence ||
            draws is not [var draw] || draw != new PublicPileCashOutDrawIssuedEvent(frame.Id, receipt.DrawRequested,
                receipt.DrawActual, receipt.DrawBefore, receipt.DrawAfter)) return false;
        var drawInvoice = _cardMovements.Where(move => move.Sequence > receipt.DrawBefore && move.Sequence <= receipt.DrawAfter &&
            move.Reason.Value == PublicPileCashOutReason(frame, "draw")).ToArray();
        return drawInvoice.Length == receipt.DrawActual && drawInvoice.Select(move => move.CardId).Distinct().Count() == receipt.DrawActual &&
            drawInvoice.All(move => move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(frame.OwnerSeat)) &&
            (receipt.DrawActual > 0 || receipt.DrawBefore == receipt.DrawAfter);
    }

    private void AssertPublicPileCashOut(ProgramSkillFrame frame)
    {
        if (frame.PublicPileCashOut is null)
        {
            // No old program reads the new facts. A new producer may never turn
            // a missing paid receipt into a second payment from today's stock.
            if (frame.TriggerId is not null && GetProgramTrigger(frame).Effects.Any(effect => PublicPileCashOutContract.IsOperation(effect.Op)))
            {
                var history = CompleteProgramEventHistory().ToArray();
                if ((history.OfType<PublicPileCashOutStartedEvent>().Any(fact => fact.FrameId == frame.Id) ||
                     history.OfType<PublicPileCashOutPaidEvent>().Any(fact => fact.FrameId == frame.Id) ||
                     history.OfType<PublicPileCashOutDrawIssuedEvent>().Any(fact => fact.FrameId == frame.Id)) &&
                    !history.OfType<PublicPileCashOutResolvedEvent>().Any(fact => fact.FrameId == frame.Id))
                    throw new InvalidOperationException("A paid public pile instruction cannot lose its owning receipt.");
            }
            return;
        }
        if (!ValidPublicPileCashOut(frame))
            throw new InvalidOperationException("Public pile cash-out lost its exact original instance, owner window, paid physical invoice or once-issued successor.");
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        if (index + 1 < _resolutionStack.Count && !PublicPileCashOutFirstChild(frame, _resolutionStack[index + 1]))
            throw new InvalidOperationException("Public pile cash-out retained an unrelated native child.");
    }

    private bool IsPublicPileCashOutMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        frame.PublicPileCashOut is { } receipt && effect?.Op == receipt.Operation &&
        pending == frame.PendingMovementContinuation && ValidPublicPileCashOut(frame);

    private bool PublicPileCashOutFirstChild(ProgramSkillFrame frame, ResolutionFrame child)
    {
        if (frame.PublicPileCashOut is not { } receipt || !ValidPublicPileCashOut(frame)) return false;
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return skills.ResumeProgramFrameId == frame.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return state.ResumeProgramFrameId == frame.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
                state.CandidateIndex >= 0 && state.CandidateIndex <= state.Candidates.Count &&
                CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(fact => fact.Change.Id == state.Id &&
                    fact.Change.ParentFrameId == frame.Id && fact.Change.TargetSeat == state.OwnerSeat && fact.Change.Window == state.Window);
        var drawing = receipt.Stage == PublicPileCashOutStage.DrawChildren;
        var before = drawing ? receipt.DrawBefore : receipt.Before;
        var after = drawing ? receipt.DrawAfter : receipt.After;
        var reason = PublicPileCashOutReason(frame, drawing ? "draw" : "payment");
        var storage = receipt.Operation == SkillProgramEffectOp.StoreBoundCardsInPublicPile;
        bool EquipmentPayment(CardKind kind) => storage && !drawing && _cardMovements.Any(move =>
            move.Sequence > receipt.Before && move.Sequence <= receipt.After && receipt.PaidCardIds.Contains(move.CardId) &&
            move.CardKind == kind && move.From == CardLocation.Equipment(frame.OwnerSeat) && move.To == receipt.Pile.Location &&
            move.Reason.Value == PublicPileCashOutReason(frame, "payment"));
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.ResumeProgramFrameId is null && moved.Batch.ParentFrameId == frame.Id &&
                moved.Batch.AwaitingProgramFrameId == frame.Id && moved.Batch.OriginOwnerSeat == frame.OwnerSeat &&
                moved.Batch.OriginSkillId == frame.SkillId && moved.Batch.OriginSkillInstanceId == frame.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(move => _cardMovements.Contains(move) &&
                    (move.Sequence > before && move.Sequence <= after && move.Reason.Value == reason ||
                     move.Reason == CardMoveReasons.WoodenOxGrainDiscard && move.From == CardLocation.WoodenOxGrain(frame.OwnerSeat) &&
                     move.To == CardLocation.DiscardPile && EquipmentPayment(CardKind.WoodenOx)));
        if (!EquipmentPayment(CardKind.SilverLion)) return false;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.ResumeFrameId == frame.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.ParentFrameId == frame.Id && hp.Change.Kind == HpChangeKind.Recovery &&
                hp.Change.SourceSeat == frame.OwnerSeat && hp.Change.TargetSeat == frame.OwnerSeat && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, frame) &&
            recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            recovery.Attempt.SourceSeat == frame.OwnerSeat && recovery.Attempt.TargetSeat == frame.OwnerSeat && recovery.Attempt.Amount == 1 &&
            recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == reason;
    }

    private ProgramSkillFrame? PublicPileCashOutObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root || !PublicPileCashOutFirstChild(root, _resolutionStack[index + 1])) continue;
            var exact = true;
            for (var child = index + 2; child < _resolutionStack.Count; child++)
            {
                if (_resolutionStack[child] is ProgramLifecycleTriggerWindowFrame changed && _resolutionStack[child - 1] is ProgramSkillFrame owner &&
                    changed.Window == SkillProgramTriggerWindow.SkillsChanged && changed.ResumeProgramFrameId == owner.Id &&
                    changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && changed.CandidateIndex >= 0 && changed.CandidateIndex <= changed.Candidates.Count) continue;
                if (!DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !HalfHandPaidDamageObserverEdge(child) && !PaidTargetObserverEdge(child)) { exact = false; break; }
                if (_resolutionStack[child] is DyingFrame dying && child + 1 < _resolutionStack.Count &&
                    ((IsOriginalDyingSuspendedByDyingSuits(dying) || IsOriginalDyingSuspendedByRecipientCategoryMark(dying)) || IsOriginalDyingSuspendedByOwnedDeathBenefit(dying) ||
                     IsPaidHandRepaymentProgramAlcoholRide(child, dying) || IsPaidHandRepaymentRescueRide(child, dying) ||
                     PolicyCounterspellVirtualAlcoholRide(child, dying) || PaidObserverDamageVirtualAlcoholRide(child, dying))) break;
            }
            if (exact) return root;
        }
        return null;
    }

    private bool IsPublicPileCashOutProgramDying() => ActiveDying is { } dying && PublicPileCashOutObserverRoot() is { } root &&
        _resolutionStack.FindIndex(frame => frame.Id == dying.Id) > _resolutionStack.FindIndex(frame => frame.Id == root.Id);

    private bool HasPublicPileCashOutDamageObserver(long id) =>
        _resolutionStack.Any(frame => frame.Id == id && frame is DamageTriggerWindowFrame) && PublicPileCashOutObserverRoot() is not null;

    private bool AllowsPublicPileCashOutNestedDamage(ProgramSkillFrame frame, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || frame.AttackAttempt is not null || frame.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != frame.Id ||
            frame.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or
                SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or
                SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            PublicPileCashOutObserverRoot() is not { } root || root.Id == frame.Id) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(frame, reference) : ResolveProgramEffectTarget(frame, effect.Target));
    }

    private sealed partial class ProgramSkillHost : IPublicPileCashOutHost
    {
        public SkillProgramStepOutcome ExecutePublicPileCashOut(SkillProgramEffect effect, ProgramSkillFrame frame) =>
            engine.BeginPublicPileCashOut(effect, frame);
    }
}
