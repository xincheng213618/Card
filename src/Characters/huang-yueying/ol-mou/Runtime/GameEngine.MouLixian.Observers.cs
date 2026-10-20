namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MouLixianCardWindowEdge(int index)
    {
        if (_resolutionStack[index - 1] is CardUseFrame { CardKind: CardKind.IronChain } chained && IsIssuedMouLixianUse(chained) &&
            _resolutionStack[index] is ProgramLifecycleTriggerWindowFrame
            { Window: SkillProgramTriggerWindow.CharacterEnteredChain, Continuation: ProgramLifecycleContinuation.ResumeCharacterStateChange,
                CharacterStateContinuation: CharacterStateContinuation.CardUse } state)
            // IronChain resolves its whole target set before draining these windows;
            // its target cursor does not advance once for each entered-chain child.
            return state.ResumeProgramFrameId == chained.Id && state.ResumeCardId == chained.CardId && state.ResumeCardKind == CardKind.IronChain &&
                chained.TargetIndex >= 0 && chained.TargetIndex < chained.TargetSeats.Count && chained.TargetSeats.Skip(chained.TargetIndex).Contains(state.OwnerSeat) &&
                ProgramEventHistory<CharacterStateChangedEvent>().Count(e => e.Change.Id == state.Id && e.Change.ParentFrameId == chained.Id &&
                    e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == SkillProgramTriggerWindow.CharacterEnteredChain && e.Change.TurnedOver is null) == 1 &&
                ProgramEventHistory<IronChainStateChangedEvent>().Any(e => e.ResolutionId == chained.Id && e.SourceSeat == chained.SourceSeat &&
                    e.TargetSeat == state.OwnerSeat && e.IsChained) &&
                ProgramEventHistory<IronChainResolvedEvent>().Any(e => e.ResolutionId == chained.Id && e.SourceSeat == chained.SourceSeat && e.TargetSeats.SequenceEqual(chained.TargetSeats));
        if (_resolutionStack[index - 1] is CardUseFrame original && IsIssuedMouLixianUse(original) &&
            _resolutionStack[index] is TargetCardSelectionFrame selection)
            return selection.ParentFrameId == original.Id && selection.SourceSeat == original.SourceSeat && selection.EffectCardId == original.CardId &&
                selection.EffectCardKind == original.CardKind && original.TargetIndex >= 0 && original.TargetIndex < original.TargetSeats.Count &&
                selection.TargetSeat == original.TargetSeats[original.TargetIndex] &&
                (original.CardKind == CardKind.Snatch && selection.ActionKind == LegalActionKind.Snatch ||
                 original.CardKind == CardKind.Dismantlement && selection.ActionKind == LegalActionKind.Dismantlement) &&
                selection.Step == ResolutionFrameStep.AwaitingResponse && selection.CandidateSlots.Count > 0 &&
                selection.CandidateSlots.SequenceEqual(Enumerable.Range(0, GetHand(_players[selection.TargetSeat]).Count)) &&
                ProgramEventHistory<TargetCardSelectionRequestedEvent>().Any(e => e.ResolutionId == original.Id && e.SourceSeat == original.SourceSeat &&
                    e.TargetSeat == selection.TargetSeat && e.ActionKind == selection.ActionKind && e.CandidateCount == selection.CandidateSlots.Count);
        if (_resolutionStack[index - 1] is CardUseFrame use && IsIssuedMouLixianUse(use) &&
            _resolutionStack[index] is ProgramCardTriggerWindowFrame window)
            return window.ParentFrameId == use.Id && window.Action.ActionId == use.Action!.ActionId &&
                window.Action.ActorSeat == use.SourceSeat && window.Action.EffectiveKind == use.CardKind;
        if (_resolutionStack[index - 1] is not ProgramCardTriggerWindowFrame parent ||
            LifecycleCardUse(parent.ParentFrameId) is not { } native || !IsIssuedMouLixianUse(native) ||
            parent.Action.ActionId != native.Action!.ActionId || _resolutionStack[index] is not ProgramSkillFrame child ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count) return false;
        var candidate = parent.Candidates[parent.CandidateIndex];
        return MountObserverCandidateMatches(child, ToSharedCandidate(candidate)) &&
            child.WindowContext == CreateCardActionProgramContext(parent, candidate);
    }
    private IReadOnlyList<Card> SeparateMouLixianDamageChildProcessing(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (processing.Count == 0 || MouHuangYueyingObserverRoot() is not { MouLixian: { Paid: true, Stage: MouLixianStage.NativeUse } r } parent ||
            !ValidMouLixianStarted(parent) || parent.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } context ||
            _resolutionStack.SingleOrDefault(f => f.Id == r.WindowId) is not DamageTriggerWindowFrame window ||
            window.TriggerWindow != SkillProgramTriggerWindow.AfterDamageApplied || context.ParentFrameId != window.Id ||
            !DamageProgramMatchesWindow(parent, window) || !SameAttackOwner(GetDamageTriggerAttack(window), attack) ||
            GetDamageTriggerAttack(window).ResolutionId != attack.ResolutionId || LifecycleCardUse(r.UseId) is not { Action.PhysicalCards: [{ } cost] } use ||
            use.MouLixianOrigin?.ProgramFrameId != parent.Id || !IsIssuedMouLixianUse(use) || cost.CardId != r.MaterialId || cost.CardKind != r.PrintedKind || cost.From != r.From ||
            _cardZones.GetLocation(cost.CardId) != CardLocation.Processing || attack.PhysicalCards.Any(c => c.Id == cost.CardId) ||
            ProgramEventHistory<MouLixianMaterialPaidEvent>().Count(p => p.FrameId == parent.Id && p.UseId == use.Id && p.CardId == cost.CardId &&
                p.PrintedKind == cost.CardKind && p.From == cost.From && p.To == CardLocation.Processing && p.MovementSequence == r.MovementSequence) != 1)
            return processing;
        var wi = _resolutionStack.FindIndex(f => f.Id == window.Id);
        var pi = _resolutionStack.FindIndex(f => f.Id == parent.Id);
        var ui = _resolutionStack.FindIndex(f => f.Id == use.Id);
        var ai = _resolutionStack.FindIndex(f => f.Id == attack.ResolutionId);
        if (ai < 0 || wi < 0 || pi != wi + 1 || ui != pi + 1 || ai >= wi) return processing;
        return processing.Where(c => c.Id != cost.CardId).ToArray();
    }
    private ProgramSkillFrame? MouLixianDamageCursorRoot(DamageTriggerWindowFrame window, out long? ownedDyingId)
    {
        ownedDyingId = null;
        if (MouHuangYueyingObserverRoot() is not { MouLixian: { Paid: true } r } parent ||
            !ValidMouLixianStarted(parent) || r.WindowId != window.Id || window.TriggerWindow != SkillProgramTriggerWindow.AfterDamageApplied ||
            parent.WindowContext?.ParentFrameId != window.Id || !DamageProgramMatchesWindow(parent, window) || r.MovementSequence <= r.Before ||
            ProgramEventHistory<MouLixianMaterialPaidEvent>().Count(p => p.FrameId == parent.Id && p.UseId == r.UseId && p.CardId == r.MaterialId &&
                p.PrintedKind == r.PrintedKind && p.From == r.From && p.MovementSequence == r.MovementSequence) != 1) return null;
        var wi = _resolutionStack.FindIndex(f => f.Id == window.Id);
        var pi = _resolutionStack.FindIndex(f => f.Id == parent.Id);
        if (wi < 0 || pi != wi + 1) return null;
        if (r.Stage == MouLixianStage.NativeUse)
        {
            if (_resolutionStack[pi + 1] is not CardUseFrame use || use.Id != r.UseId ||
                use.MouLixianOrigin?.ProgramFrameId != parent.Id || !IsIssuedMouLixianUse(use)) return null;
        }
        else if (!IsReturnedMouLixianUse(parent) || _resolutionStack[pi + 1] is not CardsMovedTriggerWindowFrame movement ||
            movement.ResumeProgramFrameId != parent.Id || !IsMouLixianNativeSettlementBatch(parent, movement.Batch)) return null;
        if (ActiveDying is { } dying)
        {
            if (!IsMouHuangYueyingDying() || _resolutionStack.FindIndex(f => f.Id == dying.Id) <= pi) return null;
            ownedDyingId = dying.Id;
        }
        return parent;
    }
    private IReadOnlyList<int>? MouLixianNativeAttackAncestorProcessing(long useId)
    {
        if (ActiveDamageTrigger is not { TriggerWindow: SkillProgramTriggerWindow.AfterDamageApplied } window ||
            MouLixianDamageCursorRoot(window, out _) is not { MouLixian: { Stage: MouLixianStage.NativeUse } r } parent || r.UseId != useId ||
            LifecycleCardUse(useId) is not { Action: { } childAction } use || !IsIssuedMouLixianUse(use)) return null;
        var attack = GetDamageTriggerAttack(window);
        var ai = _resolutionStack.FindIndex(f => f.Id == attack.ResolutionId);
        var wi = _resolutionStack.FindIndex(f => f.Id == window.Id);
        if (ai < 0 || ai >= wi || _resolutionStack.SingleOrDefault(f => f.Id == window.ParentFrameId) is not DamageFrame damage ||
            damage.ParentFrameId != attack.ResolutionId || damage.Id != parent.WindowContext?.DamageFrameId ||
            ActiveCardAttack is { } cardAttack && !SameAttackOwner(cardAttack, attack) ||
            ActiveDuel is { } duel && !SameAttackOwner(duel.Attack, attack) ||
            ActiveGroupCard is { } group && group.ResolutionId != attack.ResolutionId ||
            ActiveFireAttack is { } fire && fire.Id != attack.ResolutionId ||
            attack.PhysicalCards.Select(c => c.Id).Distinct().Count() != attack.PhysicalCards.Count) return null;
        var originalUse = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(f => f.Id == attack.ResolutionId);
        var ids = new List<int>();
        foreach (var card in attack.PhysicalCards)
        {
            if (_cardZones.GetLocation(card.Id) != CardLocation.Processing) continue;
            var entry = _cardMovements.LastOrDefault(m => m.CardId == card.Id);
            if (childAction.PhysicalCards.Any(c => c.CardId == card.Id) || entry is null || entry.To != CardLocation.Processing ||
                entry.From == CardLocation.Processing || entry.CardKind != card.Kind || entry.Sequence > r.Before ||
                originalUse is { Action: { } action } && !action.PhysicalCards.Any(c => c.CardId == card.Id && c.CardKind == card.Kind && c.From == entry.From))
                return null;
            ids.Add(card.Id);
        }
        return Array.AsReadOnly(ids.ToArray());
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouHuangYueyingLixian)]
    private PromptChoice ChooseMouLixian(PendingDecision decision, ProgramSkillFrame frame) => decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") != "cancel") ?? decision.Choices[0];
}
