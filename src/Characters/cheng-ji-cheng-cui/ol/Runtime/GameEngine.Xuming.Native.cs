namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramAnswer("xuming-issued-use")]
    private void AnswerXumingUse(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { XumingEnding: { Stage: XumingStage.Choosing } r } f ||
            r.PromiseIndex >= r.Promises.Count || _pendingDecision?.PlayerSeat != r.Promises[r.PromiseIndex].ActorSeat ||
            !XumingChoices(f).Any(c => AssistedChoicesEqual([c], [selected])))
            throw new InvalidOperationException("Xuming requires the actual counterparty's current complete-material native Use choice.");
        var p = r.Promises[r.PromiseIndex]; var plan = XumingPlans(f)[int.Parse(selected.Parameters["plan"], System.Globalization.CultureInfo.InvariantCulture)];
        var cards = XumingCurrentMaterials(p); var expectedUse = _resolutionSequence + 1;
        ClearPendingDecision();
        ReplaceRuntimeTop(f = f with { XumingEnding = r with { Stage = XumingStage.Paying, Plan = plan,
            UseId = expectedUse, Before = _movementSequence, After = _movementSequence } });
        var useId = BeginCardUse(cards[0], p.ActorSeat, plan.Targets, plan.Kind, physicalCardIds: cards.Select(c => c.Id).ToArray(),
            conversionSource: r.Source, designatedTargetSeats: plan.Kind == CardKind.BorrowedSword ? [p.TargetSeat] : null);
        if (useId != expectedUse || LifecycleCardUse(useId) is not { Action: { } action } use ||
            action.ActorSeat != p.ActorSeat || action.ProviderSeat != p.ActorSeat || action.EffectiveKind != plan.Kind ||
            !action.PhysicalCards.Select(c => c.CardId).SequenceEqual(cards.Select(c => c.Id)) ||
            !action.ConversionChain.Contains(r.Source))
            throw new InvalidOperationException("Xuming's complete original materials must issue exactly one real native Use.");
        var origin = new XumingUseOrigin(f.Id, p.PromiseId, p.Source, f.GameplayHash, useId, action.ActionId,
            p.ActorSeat, p.TargetSeat, plan.Kind);
        UpdateLifecycleCardUse(useId, u => u with { XumingOrigin = origin });
        ReplaceRuntimeFrame(f.Id, f with { XumingEnding = f.XumingEnding! with { Stage = XumingStage.NativeUse, ActionId = action.ActionId } });
        AdvanceEventRulesAndQueueFact(new XumingNativeUseIssuedEvent(f.Id, p.PromiseId, p.Source, useId,
            action.ActionId, p.ActorSeat, p.TargetSeat, plan.Kind, cards.Count));
        // Discard entities enter the real Use Processing zone. An entity still
        // retained by its original native Use is claimed there without a fake
        // Processing->Processing movement or an intervening fictitious Hand gain.
        foreach (var card in cards)
        {
            var from = _cardZones.GetLocation(card.Id);
            if (from == CardLocation.Processing)
                AdvanceEventRulesAndQueueFact(new XumingProcessingMaterialClaimedEvent(f.Id, p.DamageOwnerId,
                    useId, p.PromiseId, p.Source, card.Id));
            else MoveCard(card, from, CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
        }
        var parent = _resolutionStack.OfType<ProgramSkillFrame>().Single(x => x.Id == f.Id);
        ReplaceRuntimeFrame(parent.Id, parent with { XumingEnding = parent.XumingEnding! with { After = _movementSequence } });
        BeginJizhiOrNullificationWindow(useId, cards[0], p.ActorSeat, plan.Targets, plan.ActionKind,
            plan.TargetCardId, plan.RequiredKind, plan.Kind);
    }
    private bool IsIssuedXumingUse(CardUseFrame use)
    {
        if (use.XumingOrigin is not { } o || use.Action is not { Type: CardActionType.Use } action ||
            o.UseId != use.Id || o.ActionId != action.ActionId || action.ActorSeat != o.ActorSeat || action.ProviderSeat != o.ActorSeat ||
            use.SourceSeat != o.ActorSeat || action.EffectiveKind != o.Kind || use.CardKind != o.Kind ||
            o.Kind is not (CardKind.BorrowedSword or CardKind.Dismantlement) || use.TargetSeats.Count == 0 || use.TargetSeats[0] != o.TargetSeat ||
            _resolutionStack.SingleOrDefault(x => x.Id == o.ProgramFrameId) is not ProgramSkillFrame
                { XumingEnding: { Stage: XumingStage.NativeUse } r } f ||
            r.UseId != use.Id || r.ActionId != action.ActionId || r.Plan is not { } plan || r.PromiseIndex >= r.Promises.Count ||
            r.Promises[r.PromiseIndex] is not { } p || p.PromiseId != o.PromiseId || p.Source != o.Source ||
            p.ActorSeat != o.ActorSeat || p.TargetSeat != o.TargetSeat || r.GameplayHash != o.GameplayHash ||
            !action.ConversionChain.Contains(r.Source) || !use.TargetSeats.SequenceEqual(plan.Targets)) return false;
        var materials = ProgramEventHistory<XumingMaterialFrozenEvent>().Where(e => e.PromiseId == p.PromiseId && e.Source == p.Source).OrderBy(e => e.Index).ToArray();
        return materials.Length == p.MaterialCount && action.PhysicalCards.Select(c => (c.CardId, c.CardKind))
            .SequenceEqual(materials.Select(m => (m.CardId, m.PrintedKind))) &&
            ProgramEventHistory<XumingNativeUseIssuedEvent>().Count(e => e == new XumingNativeUseIssuedEvent(f.Id,
                p.PromiseId, p.Source, use.Id, action.ActionId, p.ActorSeat, p.TargetSeat, plan.Kind, p.MaterialCount)) == 1;
    }
    private bool IsXumingClaimedProcessingMaterial(long originalUseId, int cardId) => HasChengJiChengCui && cardId > 0 &&
        ProgramEventHistory<XumingProcessingMaterialClaimedEvent>().Any(e => e.OriginalUseId == originalUseId && e.CardId == cardId &&
            ProgramEventHistory<XumingNativeUseIssuedEvent>().Any(issued => issued.FrameId == e.ProgramFrameId && issued.UseId == e.UseId &&
                issued.PromiseId == e.PromiseId && issued.Source == e.Source) &&
            ProgramEventHistory<XumingMaterialFrozenEvent>().Any(m => m.PromiseId == e.PromiseId && m.Source == e.Source && m.CardId == cardId));
    [NativeUseValidation(32202)]
    private void ValidateXumingNativeUse(CardUseFrame use)
    {
        if (use.XumingOrigin is not null && (!IsIssuedXumingUse(use) || use.Step != ResolutionFrameStep.Completed ||
            use.PendingRecoveryAttempts is { Count: > 0 } || use.RecoveryPaidContinuation is not null))
            throw new InvalidOperationException("Xuming cannot return before the exact complete native trick and all children.");
    }
    [NativeUseReturn(32202)]
    private void ReturnXumingNativeUse(long useId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { XumingEnding: { Stage: XumingStage.NativeUse } r } f || r.UseId != useId) return;
        if (!ProgramEventHistory<CardUseFinishedEvent>().Any(e => e.ResolutionId == useId && e.CardKind == r.Plan!.Kind) ||
            ProgramEventHistory<XumingNativeUseReturnedEvent>().Any(e => e.UseId == useId))
            throw new InvalidOperationException("Xuming's native Use must finish and return once, without re-paying its original materials.");
        AdvanceEventRulesAndQueueFact(new XumingNativeUseReturnedEvent(f.Id, useId));
        SettleXumingPromise(f, true, useId); ContinueXumingEnding(f.Id);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 32202)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 32202)]
    private bool ContinueIssuedXuming(ProgramSkillFrame f)
    {
        if (f.SkillId != ChengJiChengCuiComposition.Xuming) return false;
        if (f.XumingEnding is { } r)
            return r.Source == ChengJiSource(f) && r.GameplayHash == f.GameplayHash && r.Promises.Count > 0 &&
                r.Promises.All(p => ProgramEventHistory<XumingPromiseIssuedEvent>().Contains(p));
        // Source loss may precede the actual turn end. The native candidate is
        // already issued before its first instruction creates the local receipt.
        // Admit that exact owning child, not an arbitrary newly acquired source.
        return f.TriggerId == "issued-whole-turn-uses" && f.ActivationId == f.TriggerId &&
            f.WindowContext is { Window: SkillProgramTriggerWindow.AfterTurnEnded } c &&
            _resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) is DeferredTurnEndFrame parent &&
            parent.AfterTurnEnded?.CurrentChild is { } child && child.FrameId == f.Id &&
            child.Candidate.OwnerSeat == f.OwnerSeat && child.Candidate.SkillId == f.SkillId &&
            child.Candidate.SkillInstanceId == f.SkillInstanceId && child.Candidate.GameplayHash == f.GameplayHash &&
            child.Candidate.BindingId == f.TriggerId && IsIssuedXumingCandidate(child.Candidate, c);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 32202)]
    private void AssertXumingFrame(ProgramSkillFrame f)
    {
        if (f.XumingEnding is not { } r) return;
        if (f.InstructionIndex != 1 || f.TriggerId != "issued-whole-turn-uses" || f.ActivationId != f.TriggerId ||
            f.SkillId != ChengJiChengCuiComposition.Xuming || r.Source != ChengJiSource(f) || r.GameplayHash != f.GameplayHash ||
            !Enum.IsDefined(r.Stage) || r.PromiseIndex < 0 || r.PromiseIndex > r.Promises.Count || r.Before > r.After ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterTurnEnded } c || c.ParentFrameId != r.WindowId ||
            _resolutionStack.SingleOrDefault(x => x.Id == r.WindowId) is not DeferredTurnEndFrame parent ||
            parent.TurnNumber != r.ActualTurn || parent.OwnerSeat != r.TurnOwnerSeat ||
            parent.AfterTurnEnded?.CurrentChild?.FrameId != f.Id || r.Promises.Any(p => p.Source.OwnerSeat != f.OwnerSeat ||
                p.Source.SkillInstanceId != f.SkillInstanceId || p.GameplayHash != f.GameplayHash || p.ActualTurn != r.ActualTurn ||
                !ProgramEventHistory<XumingPromiseIssuedEvent>().Contains(p)))
            throw new InvalidOperationException("Xuming lost the original source-qualified whole-turn promise cursor.");
        if (r.Stage == XumingStage.NativeUse && (r.UseId is not { } id || LifecycleCardUse(id) is not { } use || !IsIssuedXumingUse(use)))
            throw new InvalidOperationException("Xuming's issued native child lost its original complete material group.");
    }
}
