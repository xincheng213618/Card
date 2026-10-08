namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool IsResponseCompletionContinuation(ProgramCardContinuation continuation) => continuation is
        ProgramCardContinuation.Dodge or ProgramCardContinuation.DuelSlash or ProgramCardContinuation.GroupResponse or
        ProgramCardContinuation.FactionDefenseDodge or ProgramCardContinuation.FactionSlashDuelSlash or
        ProgramCardContinuation.FactionSlashGroupResponse or ProgramCardContinuation.NullificationResponse;

    // A native cost child can grant the first observer. Collect live bindings
    // after those children return, rather than using a premature live-owner gate.
    private bool HasCardResponseCompletedObserver(CardActionContext action, ProgramCardContinuation continuation) =>
        action.Type == CardActionType.Response && IsResponseCompletionContinuation(continuation) &&
        _contentRegistry.ProgramDependencies.HasTriggerWindow(SkillProgramTriggerWindow.CardResponseCompleted);

    private bool TryBeginCardResponseCompleted(CardAttackHandle? attack, CardActionContext action,
        ProgramCardContinuation originalContinuation)
    {
        if (_winner != Winner.None || !HasCardResponseCompletedObserver(action, originalContinuation)) return false;
        var parent = _resolutionStack.LastOrDefault() ?? throw new InvalidOperationException("A finished response has no native parent.");
        if (originalContinuation == ProgramCardContinuation.NullificationResponse
            ? parent is not NullificationWindowFrame || attack is not null
            : attack is null || parent.Id != attack.ResolutionId)
            throw new InvalidOperationException("A finished response lost its exact native parent.");
        var costRecords = CaptureResponseCompletionCosts(action, originalContinuation, parent.Id);
        var costSequences = costRecords.Select(m => m.Sequence).ToHashSet();
        var batches = _pendingCardsMovedBatches.Where(b => b.ParentFrameId == parent.Id && b.AwaitingProgramFrameId is null &&
                b.Movements.Any(m => costSequences.Contains(m.Sequence))).OrderBy(b => b.Id).ToArray();
        var completionActor = action.ActorSeat == action.ProviderSeat ? action.ActorSeat : action.ProviderSeat;
        if (action.ActorSeat != action.ProviderSeat && (action.RequesterSeat != action.ActorSeat ||
                originalContinuation is not (ProgramCardContinuation.FactionDefenseDodge or ProgramCardContinuation.FactionSlashDuelSlash or ProgramCardContinuation.FactionSlashGroupResponse)))
            throw new InvalidOperationException("A foreign supplied response requires its exact native requester direction.");
        var returned = new ProgramResponseCompletionReturn(action.ActionId, parent.Id, attack?.ResolutionId,
            originalContinuation, _turnNumber, _currentSeat) { CompletionActorSeat = completionActor, NativeCosts = costRecords, CostBatches = batches,
            CostRecoveries = (parent.PendingRecoveryAttempts ?? []).Where(a => IsResponseCompletionSilverLionAttempt(costRecords, a)).ToArray(),
            CostHealthChanges = _pendingHpChanges.Where(h => h.ParentFrameId == parent.Id && IsResponseCompletionSilverLionHealth(costRecords, h)).ToArray() };
        var id = ++_resolutionSequence;
        var window = new ProgramCardTriggerWindowFrame(id, parent.Id, action, originalContinuation, [],
            AttackOwnerFrameId: attack?.ResolutionId) { ResponseCompletion = returned };
        if (!ValidResponseCompletionParent(window) || CompleteProgramEventHistory().OfType<CardResponseCompletionStartedEvent>().Any(e => e.ActionId == action.ActionId))
            throw new InvalidOperationException("A response cannot finish twice or change its native cursor.");
        AdvanceEventRulesAndQueueFact(new CardResponseCompletionStartedEvent(id, action.ActionId, parent.Id,
            completionActor, action.ProviderSeat, action.EffectiveKind, originalContinuation, _turnNumber, _currentSeat, action.ActorSeat));
        PushRuntimeFrame(window); CapturePairedColorResponseLink(window); AdvanceRuntimeTop<ProgramCardTriggerWindowFrame>(); return true;
    }

    private CardMovementRecord[] CaptureResponseCompletionCosts(CardActionContext a, ProgramCardContinuation continuation, long parentFrameId)
    {
        var paid = new List<CardMovementRecord>();
        foreach (var cost in a.PhysicalCards)
        {
            if (IsProgramAlternativeCost(a, cost.CardId))
            {
                var alternative = _cardMovements.LastOrDefault(m => m.CardId == cost.CardId && m.From == cost.From &&
                    m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Respond)
                    ?? throw new InvalidOperationException("A completed alternative response lost its real draw-pile-top payment.");
                paid.Add(alternative); continue;
            }
            var entered = _cardMovements.LastOrDefault(m => m.CardId == cost.CardId &&
                IsResponseCompletionEntry(a, continuation, parentFrameId, cost, m))
                ?? throw new InvalidOperationException("A completed response lost its actual entered entity.");
            paid.Add(entered);
            if (IsExchangedCardClaim(a.ActionId, cost.CardId)) continue;
            var finished = _cardMovements.LastOrDefault(m => m.CardId == cost.CardId && m.Sequence > entered.Sequence &&
                m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == ResponseCompletionFinishReason(entered, continuation))
                ?? throw new InvalidOperationException("A completed response still owes its exact native finish movement.");
            paid.Add(finished);
        }
        return paid.OrderBy(m => m.Sequence).ToArray();
    }

    // Extended Nullification uses the ordinary multi-card Respond/ResponseFinished
    // route; the single native counterspell uses Nullification/NullificationFinished.
    private static bool IsResponseCompletionEntryReason(CardActionContext action, ProgramCardContinuation continuation, CardMoveReason reason) =>
        continuation == ProgramCardContinuation.NullificationResponse
            ? reason == CardMoveReasons.Nullification || action.ConversionChain.Count > 0 && reason == CardMoveReasons.Respond
            : reason == CardMoveReasons.Respond;
    private static CardMoveReason ResponseCompletionFinishReason(CardMovementRecord entered, ProgramCardContinuation continuation) =>
        entered.Reason == CardMoveReasons.Nullification || entered.Reason.Value == "conversion.declaration.pay" &&
        continuation == ProgramCardContinuation.NullificationResponse ? CardMoveReasons.NullificationFinished : CardMoveReasons.ResponseFinished;

    private bool IsResponseCompletionEntry(CardActionContext action, ProgramCardContinuation continuation,
        long parentFrameId, CardActionCost cost, CardMovementRecord moved)
    {
        if (!_cardMovements.Contains(moved) || moved.CardId != cost.CardId || moved.CardKind != cost.CardKind ||
            moved.From != cost.From || moved.To != CardLocation.Processing) return false;
        if (IsResponseCompletionEntryReason(action, continuation, moved.Reason)) return true;
        // Declaration payment children returned before the native response claimed
        // this entity. Reuse only that exact claimed invoice, without inventing a
        // second Respond/Nullification entry or replaying its payment children.
        if (moved.Reason.Value != "conversion.declaration.pay" || moved.TurnNumber != _turnNumber ||
            action.PhysicalCards is not [var only] || only != cost || cost.From != CardLocation.Hand(action.ProviderSeat) ||
            _resolutionStack.SingleOrDefault(f => f.Id == parentFrameId)?.AcceptedDeclarationPayment is not { Claimed: true } paid ||
            paid.OwnerFrameId != parentFrameId || paid.DeclarationId <= 0 || paid.ProviderSeat != action.ProviderSeat ||
            paid.ActorSeat != action.ActorSeat || paid.Cost != cost || paid.Source.OwnerSeat != action.ProviderSeat ||
            !(paid.DeclaredKind == action.EffectiveKind || action.EffectiveKind == CardKind.Slash && MatchesRequiredCard(paid.DeclaredKind, action.EffectiveKind) &&
                continuation is (ProgramCardContinuation.DuelSlash or ProgramCardContinuation.FactionSlashDuelSlash or
                    ProgramCardContinuation.GroupResponse or ProgramCardContinuation.FactionSlashGroupResponse)) ||
            !action.ConversionChain.Contains(paid.Source) ||
            ViewAsRule(paid.Source) is not { DeclarationValidation: not null } rule || rule.OutputKind != paid.DeclaredKind) return false;
        IReadOnlyList<int>? nativeTargets = continuation switch
        {
            ProgramCardContinuation.NullificationResponse =>
                (_resolutionStack.SingleOrDefault(f => f.Id == parentFrameId) as NullificationWindowFrame)?.TargetSeats,
            ProgramCardContinuation.DuelSlash => ActiveDuel is { } duel && duel.ResolutionId == parentFrameId ? [duel.OpponentSeat] : null,
            ProgramCardContinuation.GroupResponse => ActiveGroupCard is { } group && group.ResolutionId == parentFrameId ? [group.SourceSeat] : null,
            ProgramCardContinuation.Dodge or ProgramCardContinuation.FactionDefenseDodge or
                ProgramCardContinuation.FactionSlashDuelSlash or ProgramCardContinuation.FactionSlashGroupResponse =>
                _resolutionStack.SingleOrDefault(f => f.Id == parentFrameId) switch
                {
                    CardUseFrame { CardAttack: { } attack } => [attack.SourceSeat],
                    ProgramSkillFrame program when IsResponseCompletionProgramDuelParent(program, continuation) => [program.CardAttack!.SourceSeat],
                    _ => null
                },
            _ => null
        };
        if (nativeTargets is null) return false;
        var history = CompleteProgramEventHistory().ToArray();
        return history.OfType<CardDeclarationCommittedEvent>().Where(e => e.DeclarationId == paid.DeclarationId).ToArray() is [var declared] &&
            declared.OwnerSeat == action.ProviderSeat && declared.ActorSeat == action.ActorSeat && declared.DeclaredKind == paid.DeclaredKind &&
            declared.TargetSeats.SequenceEqual(nativeTargets) &&
            !history.OfType<CardDeclarationRevealedEvent>().Any(e => e.DeclarationId == paid.DeclarationId && !e.Succeeded);
    }

    private static CardResponseCompletedEvent ResponseCompletionFact(ProgramCardTriggerWindowFrame window)
    {
        var r = window.ResponseCompletion!; var a = window.Action;
        return new(a.ActionId, r.ParentFrameId, r.AttackOwnerFrameId, r.CompletionActorSeat, a.ProviderSeat,
            a.EffectiveKind, r.OriginalContinuation, r.ActualTurnNumber, r.ActualTurnOwnerSeat, a.ActorSeat);
    }

    // This is an observer projection, never a second accepted Use/Response. A
    // faction provider actually plays the supplied card; the original principal
    // still owns the native response and every old completed-use continuation.
    private static CardActionContext ResponseCompletionActorAction(ProgramCardTriggerWindowFrame window)
    {
        var a = window.Action; var r = window.ResponseCompletion!;
        if (r.CompletionActorSeat == a.ActorSeat) return a;
        return new(a.ActionId, a.ParentActionId, a.Type, r.CompletionActorSeat, a.ProviderSeat, a.RequesterSeat,
            r.CompletionActorSeat, a.OpponentSeat, a.EffectiveKind, a.TargetSeats, a.PhysicalCards, a.ConversionChain,
            a.DesignatedTargetSeats, a.EffectiveSuit, a.EffectiveRank, a.EffectiveIsRed, a.FactionOrigin);
    }

    private bool IsResponseCompletionCostFinished(ProgramCardTriggerWindowFrame window, CardActionCost cost)
    {
        if (window.ResponseCompletion is not { } r || !window.Action.PhysicalCards.Contains(cost)) return false;
        var records = r.NativeCosts.Where(m => m.CardId == cost.CardId).ToArray();
        if (records.Length == 0 || records.Any(m => !_cardMovements.Contains(m) || m.CardKind != cost.CardKind)) return false;
        if (IsProgramAlternativeCost(window.Action, cost.CardId))
            return records is [var alternative] && alternative.From == cost.From && alternative.To == CardLocation.DrawPile && alternative.Reason == CardMoveReasons.Respond;
        var enter = records[0];
        if (!IsResponseCompletionEntry(window.Action, r.OriginalContinuation, r.ParentFrameId, cost, enter)) return false;
        if (IsExchangedCardClaim(window.Action.ActionId, cost.CardId)) return records.Length == 1;
        return records is [_, var finish] && finish.Sequence > enter.Sequence && finish.From == CardLocation.Processing &&
            finish.To == CardLocation.DiscardPile && finish.Reason == ResponseCompletionFinishReason(enter, r.OriginalContinuation);
    }

    private bool IsResponseCompletionProgramDuelParent(ProgramSkillFrame frame, ProgramCardContinuation continuation)
    {
        if (continuation is not (ProgramCardContinuation.DuelSlash or ProgramCardContinuation.FactionSlashDuelSlash) ||
            frame.CardAttack is not { Active: true, CardId: null, EffectiveCardKind: CardKind.Duel, ProgramSkillCardUseFrameId: null } attack ||
            attack.ProgramSkillFrameId != frame.Id || attack.PhysicalCardIds.Count != 0 || attack.SourceSeat == attack.TargetSeat ||
            frame.Continuations.Duel is not { Active: true } || ActiveDuel is not { } duel || duel.ResolutionId != frame.Id ||
            _contentRegistry.Skills.GetValueOrDefault(frame.SkillId)?.Program is not { } program || program.GameplayHash != frame.GameplayHash)
            return false;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, program);
        if (frame.InstructionIndex < 1 || frame.InstructionIndex > plan.Instructions.Count) return false;
        var paused = plan.GetPausedInstruction(frame.InstructionIndex).Effect;
        if (paused.Op == SkillProgramEffectOp.JuanxiaDeclareTricks)
            return frame.JuanxiaLaunch is { Stage: JuanxiaStage.Child, UsedCount: > 0 } launch &&
                frame.OwnerSeat == attack.SourceSeat && launch.TargetSeat == attack.TargetSeat &&
                launch.UsedKinds.Count == launch.UsedCount && launch.UsedKinds[^1] == CardKind.Duel.ToString() &&
                CompleteProgramEventHistory().OfType<ProgramJuanxiaTrickUsedEvent>()
                    .Where(e => e.FrameId == frame.Id && e.TrickKind == CardKind.Duel.ToString()).ToArray() is [var issued] &&
                issued.SkillId == frame.SkillId && issued.BindingId == GetProgramBindingId(frame) &&
                issued.OwnerSeat == frame.OwnerSeat && issued.TargetSeat == launch.TargetSeat && issued.CardId == 0 && !issued.Paid;
        if (paused.Op != SkillProgramEffectOp.StartVirtualDuel) return false;
        // These are the ordered participants captured by BeginProgramVirtualDuel;
        // its response cursor remains on the same owning program until return.
        return frame.TriggerId is null
            ? frame.SelectedTargetSeats is [var source, var target] && source == attack.SourceSeat && target == attack.TargetSeat
            : frame.WindowContext?.Window == SkillProgramTriggerWindow.PlayPhaseStarting && frame.OwnerSeat == attack.SourceSeat &&
                frame.SelectedTargetSeats is [var counterpart] && counterpart == attack.TargetSeat;
    }

    private bool ValidResponseCompletionParent(ProgramCardTriggerWindowFrame window)
    {
        if (window.ResponseCompletion is not { } r || window.CompletedResponseReturn is not null ||
            window.Continuation != r.OriginalContinuation || !IsResponseCompletionContinuation(r.OriginalContinuation) ||
            r.ActionId != window.Action.ActionId || r.ParentFrameId != window.ParentFrameId ||
            r.AttackOwnerFrameId != window.AttackOwnerFrameId || r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _currentSeat ||
            window.Action is not { Type: CardActionType.Response } a || !IsValidPlayerSeat(a.ActorSeat) ||
            !IsValidPlayerSeat(a.ProviderSeat) || a.TargetSeats.Count != 0 ||
            CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Where(e => e.Action.ActionId == a.ActionId).ToArray() is not [var accepted] ||
            accepted.Action.Type != a.Type || accepted.Action.ActorSeat != a.ActorSeat || accepted.Action.ProviderSeat != a.ProviderSeat ||
            accepted.Action.RequesterSeat != a.RequesterSeat || accepted.Action.ResponderSeat != a.ResponderSeat || accepted.Action.OpponentSeat != a.OpponentSeat ||
            accepted.Action.ParentActionId != a.ParentActionId || accepted.Action.EffectiveKind != a.EffectiveKind ||
            accepted.Action.EffectiveSuit != a.EffectiveSuit || accepted.Action.EffectiveRank != a.EffectiveRank || accepted.Action.EffectiveIsRed != a.EffectiveIsRed ||
            !accepted.Action.TargetSeats.SequenceEqual(a.TargetSeats) || !accepted.Action.EffectiveDesignatedTargetSeats.SequenceEqual(a.EffectiveDesignatedTargetSeats) ||
            !accepted.Action.PhysicalCards.SequenceEqual(a.PhysicalCards) || !accepted.Action.ConversionChain.SequenceEqual(a.ConversionChain)) return false;
        if (r.CompletionActorSeat != a.ProviderSeat || a.ActorSeat != a.ProviderSeat &&
            (a.RequesterSeat != a.ActorSeat || r.OriginalContinuation is not (ProgramCardContinuation.FactionDefenseDodge or
                ProgramCardContinuation.FactionSlashDuelSlash or ProgramCardContinuation.FactionSlashGroupResponse))) return false;
        if (a.ResponderSeat != a.ActorSeat && !(a.ActorSeat != a.ProviderSeat && a.ResponderSeat == a.ProviderSeat && a.RequesterSeat == a.ActorSeat)) return false;
        if (r.OriginalContinuation == ProgramCardContinuation.NullificationResponse)
        {
            if (r.AttackOwnerFrameId is not null || a.EffectiveKind != CardKind.Nullification || a.ProviderSeat != a.ActorSeat || a.RequesterSeat is not null ||
                _resolutionStack.SingleOrDefault(f => f.Id == r.ParentFrameId) is not NullificationWindowFrame pending ||
                ActiveNullificationWindow?.Id != pending.Id || a.OpponentSeat != pending.SourceSeat ||
                LifecycleCardUse(pending.ParentFrameId) is not { } use || a.ParentActionId != use.Action?.ActionId) return false;
        }
        else
        {
            var parent = _resolutionStack.SingleOrDefault(f => f.Id == r.ParentFrameId);
            var attack = parent switch
            {
                CardUseFrame use => use.CardAttack,
                ProgramSkillFrame program when IsResponseCompletionProgramDuelParent(program, r.OriginalContinuation) => program.CardAttack,
                _ => null
            };
            if (r.AttackOwnerFrameId != r.ParentFrameId || attack is null ||
                a.ParentActionId != (parent as CardUseFrame)?.Action?.ActionId) return false;
            if (r.OriginalContinuation is ProgramCardContinuation.Dodge or ProgramCardContinuation.FactionDefenseDodge &&
                (a.EffectiveKind != CardKind.Dodge || a.ActorSeat != attack.TargetSeat || a.OpponentSeat != attack.SourceSeat)) return false;
            if (r.OriginalContinuation is ProgramCardContinuation.DuelSlash or ProgramCardContinuation.FactionSlashDuelSlash &&
                (!IsSlashCard(a.EffectiveKind) || ActiveDuel is not { } duel || duel.ResolutionId != r.ParentFrameId ||
                    a.ActorSeat != duel.ResponderSeat || a.OpponentSeat != duel.OpponentSeat)) return false;
            if (r.OriginalContinuation is ProgramCardContinuation.GroupResponse or ProgramCardContinuation.FactionSlashGroupResponse &&
                (ActiveGroupCard is not { } group || group.ResolutionId != r.ParentFrameId || group.CurrentAttack?.ResolutionId != r.ParentFrameId ||
                    group.RequiredCardKind != ProgramBasicCardName(a.EffectiveKind) || a.ActorSeat != attack.TargetSeat ||
                    a.OpponentSeat != attack.SourceSeat && a.OpponentSeat != group.SourceSeat)) return false;
        }
        return r.NativeCosts.Select(m => m.Sequence).Distinct().Count() == r.NativeCosts.Count &&
            r.NativeCosts.All(m => a.PhysicalCards.Any(c => c.CardId == m.CardId)) && a.PhysicalCards.All(c => IsResponseCompletionCostFinished(window, c));
    }

    private bool ValidResponseCompletionWindow(ProgramCardTriggerWindowFrame window)
    {
        if (!ValidResponseCompletionParent(window) || window.ResponseCompletion is not { } r ||
            r.CostBatchCursor < 0 || r.CostBatchCursor > r.CostBatches.Count || r.CostBatches.Select(b => b.Id).Distinct().Count() != r.CostBatches.Count ||
            r.CostBatches.Any(b => b.ParentFrameId != window.ParentFrameId || b.AwaitingProgramFrameId is not null || b.Movements.Count == 0 ||
                !b.Movements.Any(m => r.NativeCosts.Contains(m)) || b.Movements.Any(m => !_cardMovements.Contains(m))) ||
            CompleteProgramEventHistory().OfType<CardResponseCompletionStartedEvent>().Where(e => e.ActionId == r.ActionId).ToArray() is not [var started] ||
            started != new CardResponseCompletionStartedEvent(window.Id, r.ActionId, r.ParentFrameId, r.CompletionActorSeat,
                window.Action.ProviderSeat, window.Action.EffectiveKind, r.OriginalContinuation, r.ActualTurnNumber, r.ActualTurnOwnerSeat, window.Action.ActorSeat) ||
            r.CostRecoveryCursor < 0 || r.CostRecoveryCursor > r.CostRecoveries.Count || r.CostHealthCursor < 0 || r.CostHealthCursor > r.CostHealthChanges.Count ||
            r.CostRecoveries.Any(a => !IsResponseCompletionSilverLionAttempt(r.NativeCosts, a)) ||
            r.CostHealthChanges.Any(h => h.ParentFrameId != r.ParentFrameId || !IsResponseCompletionSilverLionHealth(r.NativeCosts, h))) return false;
        var facts = CompleteProgramEventHistory().OfType<CardResponseCompletedEvent>().Where(e => e.ActionId == r.ActionId).ToArray();
        if (r.CostsDrained) return r.CostBatchCursor == r.CostBatches.Count && r.CostRecoveryCursor == r.CostRecoveries.Count &&
            r.CostHealthCursor == r.CostHealthChanges.Count && r.ActiveHealthChildFrameId is null && r.ActiveCostChildFrameId is null &&
            facts is [var completed] && completed == ResponseCompletionFact(window);
        if (window.Candidates.Count != 0 || window.CandidateIndex != 0 || facts.Length != 0) return false;
        if (r.ActiveHealthChildFrameId is { } healthId)
            return r.ActiveCostChildFrameId is null && _resolutionStack.SingleOrDefault(f => f.Id == healthId) is { } health && IsResponseCompletionHealthChild(window, health);
        if (r.ActiveCostChildFrameId is not { } child) return true;
        return r.CostBatchCursor < r.CostBatches.Count && child == r.CostBatches[r.CostBatchCursor].Id &&
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(f => f.Id == child) is { } moved &&
            ResponseCompletionFirstChild(window, moved);
    }

    private bool TryDrainCardResponseCompletionCosts(ProgramCardTriggerWindowFrame supplied)
    {
        if (supplied.ResponseCompletion is not { CostsDrained: false }) return false;
        var window = supplied;
        if (_resolutionStack.LastOrDefault()?.Id != window.Id || !ValidResponseCompletionWindow(window))
            throw new InvalidOperationException("A response cost prelude lost its owning cursor.");
        var r = window.ResponseCompletion!;
        if (r.ActiveCostChildFrameId is not null || r.ActiveHealthChildFrameId is not null) throw new InvalidOperationException("An unfinished response cost child cannot be skipped.");
        if (TryDrainResponseCompletionHealth(window)) return true;
        window = (ProgramCardTriggerWindowFrame)_resolutionStack[^1]; r = window.ResponseCompletion!;
        while (r.CostBatchCursor < r.CostBatches.Count)
        {
            var batch = r.CostBatches[r.CostBatchCursor];
            var queued = _pendingCardsMovedBatches.SingleOrDefault(b => b.Id == batch.Id);
            if (queued is null || !queued.Movements.SequenceEqual(batch.Movements)) throw new InvalidOperationException("A response cost lost its queued native batch.");
            _pendingCardsMovedBatches.Remove(queued);
            var child = CreateCardsMovedProgramWindow(queued, null);
            if (child is null)
            { r = r with { CostBatchCursor = r.CostBatchCursor + 1 }; ReplaceRuntimeTop(window = window with { ResponseCompletion = r }); continue; }
            child = child with { ResumeResponseCompletionFrameId = window.Id };
            ReplaceRuntimeTop(window = window with { ResponseCompletion = r with { ActiveCostChildFrameId = child.Id } });
            PushRuntimeFrame(child); AdvanceRuntimeTop<CardsMovedTriggerWindowFrame>(); return true;
        }
        var candidates = CollectSharedCardActionCandidates(ResponseCompletionActorAction(window), SkillProgramTriggerWindow.CardResponseCompleted,
                window.Action.OpponentSeat is { } opponent ? [opponent] : [], null)
            .OrderByDescending(c => c.Priority).ThenBy(c => c.OwnerSeat).ThenBy(c => c.SkillId, StringComparer.Ordinal)
            .ThenBy(c => c.SkillInstanceId, StringComparer.Ordinal).ThenBy(c => c.TriggerId, StringComparer.Ordinal)
            .Select(c => c.FrozenContext is { CardUse: { } use } context ? c with { FrozenContext = context with {
                ParentFrameId = window.Id, CardUse = use with { ParentCardUseFrameId = window.ParentFrameId } } } :
                throw new InvalidOperationException("A response completion candidate lost its frozen action.")).ToArray();
        ReplaceRuntimeTop(window = window with { ResponseCompletion = r with { CostsDrained = true }, Candidates = Array.AsReadOnly(candidates) });
        AdvanceEventRulesAndQueueFact(ResponseCompletionFact(window));
        return false;
    }

    private bool ResponseCompletionFirstChild(ProgramCardTriggerWindowFrame window, ResolutionFrame child) =>
        IsResponseCompletionHealthChild(window, child) || window.ResponseCompletion is { CostsDrained: false, ActiveCostChildFrameId: { } id } r &&
        r.CostBatchCursor >= 0 && r.CostBatchCursor < r.CostBatches.Count && child is CardsMovedTriggerWindowFrame moved && moved.Id == id &&
        moved.ResumeResponseCompletionFrameId == window.Id && moved.ResumeProgramFrameId is null &&
        moved.Batch.Id == r.CostBatches[r.CostBatchCursor].Id && moved.Batch.ParentFrameId == window.ParentFrameId &&
        moved.Batch.AwaitingProgramFrameId is null && moved.Batch.Movements.SequenceEqual(r.CostBatches[r.CostBatchCursor].Movements);

    private bool ReturnCardResponseCompletionMovement(CardsMovedTriggerWindowFrame child)
    {
        if (child.ResumeResponseCompletionFrameId is not { } id) return false;
        if (_resolutionStack.LastOrDefault() is not ProgramCardTriggerWindowFrame window || window.Id != id ||
            !ResponseCompletionFirstChild(window, child)) throw new InvalidOperationException("A response cost child lost its exact typed parent.");
        var r = window.ResponseCompletion!;
        ReplaceRuntimeTop(window with { ResponseCompletion = r with { CostBatchCursor = r.CostBatchCursor + 1, ActiveCostChildFrameId = null } });
        AdvanceRuntimeTop<ProgramCardTriggerWindowFrame>(); return true;
    }

    private void ContinueCardResponseAfterCompletion(CardAttackHandle? attack, ProgramCardTriggerWindowFrame window)
    {
        if (window.ResponseCompletion is not { CostsDrained: true } || !ValidResponseCompletionWindow(window) || _resolutionStack.LastOrDefault()?.Id != window.ParentFrameId)
            throw new InvalidOperationException("A completed response lost its once-paid typed return.");
        var continuation = window.ResponseCompletion.OriginalContinuation;
        if (TryBeginCompletedResponseUsePrograms(attack, window.Action, continuation)) return;
        if (continuation == ProgramCardContinuation.NullificationResponse)
            ContinueNullificationWindow(ClearTieredRoundCounterspellResponse(ActiveNullificationWindow ??
                throw new InvalidOperationException("The completed response lost its counterspell chain.")));
        else ContinueFinishedCardResponse(attack ?? throw new InvalidOperationException("The completed response lost its attack."), window.Action, continuation);
    }

    private bool ResponseCompletionStructuralEdge(ResolutionFrame parent, ResolutionFrame child) =>
        parent is ProgramCardTriggerWindowFrame { ResponseCompletion: not null } w && ResponseCompletionFirstChild(w, child) ||
        child is ProgramCardTriggerWindowFrame { ResponseCompletion: not null } window && window.ParentFrameId == parent.Id && ValidResponseCompletionWindow(window);
}
