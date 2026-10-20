namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MouHuangYueyingFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouBingcai is { } r && IsPaidMouBingcai(f))
        {
            if (r.Stage == MouBingcaiStage.Drawing && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
            if (child is RecoveryReplacementFrame recovery) return IsMouHuangYueyingRecoveryParent(f, recovery);
            if (child is HpChangedTriggerWindowFrame hp) return r.Stage == MouBingcaiStage.CostChildren &&
                hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == f.OwnerSeat && hp.Change.TargetSeat == f.OwnerSeat && hp.Change.Amount == 1 &&
                r.PrintedKind == CardKind.SilverLion && r.From == CardLocation.Equipment(f.OwnerSeat);
            if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is not null && ProgramMovementReturnMatches(f, moved) &&
                moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
                moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                (moved.Batch.Movements.All(m => MouBingcaiPrimaryMovement(f, m)) || MouBingcaiOxSpill(f, moved.Batch));
        }
        if (f.MouLixian is { Paid: true } lx && ValidMouLixianStarted(f))
        {
            if (child is CardUseFrame use) return lx.Stage == MouLixianStage.NativeUse && use.Id == lx.UseId &&
                use.MouLixianOrigin?.ProgramFrameId == f.Id && IsIssuedMouLixianUse(use);
            if (child is CardsMovedTriggerWindowFrame settlement) return settlement.ResumeProgramFrameId == f.Id && f.PendingMovementContinuation is not null &&
                IsMouLixianNativeSettlementBatch(f, settlement.Batch);
        }
        if (f.MouBingcai is null && f.MouLixian is null) return false;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state) return state.ResumeProgramFrameId == f.Id &&
            state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e =>
                e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id && e.Change.Window == state.Window);
        return false;
    }
    private bool MouHuangYueyingLifecycleEdge(int index) => _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame window &&
        _resolutionStack[index] is ProgramSkillFrame f && window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count &&
        MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex]) && f.WindowContext is { } context &&
        context.ParentFrameId == window.Id && context.Window == window.Window;
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
    private ProgramSkillFrame? MouHuangYueyingObserverRoot() => FindProgramObserverRoot(
        static (g, f, child) => g.MouHuangYueyingFirstChild(f, child), static (g, index) => g.HasObserverSuffix(index,
            static (engine, next) => engine.IsMovementObserverEdge(next) || engine.IsSameNameHandObserverEdge(next) ||
                engine.TypedDamagePostEventEdge(engine._resolutionStack[next - 1], engine._resolutionStack[next]) ||
                engine.MouHuangYueyingLifecycleEdge(next) || engine.MouLixianCardWindowEdge(next) || engine._resolutionStack[next - 1] is ProgramSkillFrame { Zenrun: not null } zenrun &&
                    engine.ZenrunFirstChild(zenrun, engine._resolutionStack[next]) ||
                NativeDrawAttemptRidesOn(engine._resolutionStack[next], engine._resolutionStack[next - 1]) ||
                engine.RecoveryReplacementFrameRidesOn(engine._resolutionStack[next], engine._resolutionStack[next - 1]) || engine.IsYanPuDyingExitEdge(next),
            static (engine, next, dying) => engine.IsSuspendedOrPaidRescueObserverSuffix(next, dying)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1400)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1510)]
    private bool HasMouHuangYueyingDamageObserver(long id)
    {
        if (MouHuangYueyingObserverRoot() is not { } root || _resolutionStack.SingleOrDefault(f => f.Id == id) is not DamageTriggerWindowFrame window) return false;
        if (_resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id)) return true;
        return root.MouLixian is { Paid: true } r && ValidMouLixianStarted(root) && r.WindowId == window.Id &&
            root.WindowContext?.ParentFrameId == window.Id && window.TriggerWindow == SkillProgramTriggerWindow.AfterDamageApplied && DamageProgramMatchesWindow(root, window);
    }
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1530)]
    private bool HasMouHuangYueyingBeforeDamageObserver(long id) => MouHuangYueyingObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1360)]
    private bool IsMouHuangYueyingDying()
    {
        if (ActiveDying is not { } dying) return false;
        if (MouHuangYueyingObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id)) return true;
        return _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MouBingcai is not null && ValidMouBingcaiStarted(f) && IsMouBingcaiLegacyDyingParent(f)) ||
            IsMouBingcaiLegacyDyingWindow();
    }
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1950)]
    private bool AllowsMouHuangYueyingNestedDamage(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            MouHuangYueyingObserverRoot() is not { } root || root.Id == f.Id || sourceLess) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 28960)]
    private bool AdvanceMouHuangYueyingChildren()
    {
        if (_pendingDecision is not null) return false;
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame f && IsReturnedMouLixianUse(f))
        {
            ContinueMouLixian(f.Id);
            AdvanceRulesAndPublishState();
            return true;
        }
        return MouHuangYueyingObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28960)]
    private bool ResumeMouHuangYueying(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id) return false;
        if (f.MouBingcai is not null) { if (f.NativeDraw is null) ContinueMouBingcai(id); return true; }
        if (f.MouLixian is not null) { ContinueMouLixian(id); return true; }
        return false;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28960)]
    private bool ReturnMouHuangYueyingMovement(ProgramSkillFrame f)
    {
        if (f.MouBingcai is not null) { ContinueMouBingcai(f.Id); return true; }
        if (f.MouLixian is not null) { ContinueMouLixian(f.Id); return true; }
        return false;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28960)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28960)]
    private bool ContinuePaidMouHuangYueying(ProgramSkillFrame f) => f.MouBingcai is { Paid: true } && IsPaidMouBingcai(f) ||
        f.MouLixian is { Paid: true } r && ValidMouLixianStarted(f) &&
        (r.Stage == MouLixianStage.NativeUse && LifecycleCardUse(r.UseId) is { } use && IsIssuedMouLixianUse(use) ||
         r.Stage == MouLixianStage.NativeChildren && r.NativeReturned && ProgramEventHistory<MouLixianUseReturnedEvent>().Count(e => e.FrameId == f.Id && e.UseId == r.UseId && e.ActionId == r.ActionId) == 1);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouHuangYueyingLixian)]
    private PromptChoice ChooseMouLixian(PendingDecision decision, ProgramSkillFrame frame) => decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") != "cancel") ?? decision.Choices[0];
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouHuangYueyingBingcai)]
    private PromptChoice ChooseMouBingcai(PendingDecision decision, ProgramSkillFrame frame)
    {
        // Only the chooser's own private cards and the already public Basic kind are inspected.
        var cards = CreateSnapshot(frame.OwnerSeat).Players[frame.OwnerSeat].Hand.Concat(CreateSnapshot(frame.OwnerSeat).Players[frame.OwnerSeat].Equipment).ToDictionary(c => c.Id);
        return decision.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") != "cancel").OrderByDescending(c => c.Cards.Count == 1 && cards.TryGetValue(c.Cards[0], out var card)
            ? (IsMouHuangYueyingDamageCardKind(card.Kind) == IsMouHuangYueyingDamageCardKind(frame.MouBingcai!.BasicKind) ? 2 : 0) +
                (CardUseCategoryCatalog.Get(card.Kind) is CardUseCategories.InstantTrick or CardUseCategories.DelayedTrick ? 1 : 0) : 0).FirstOrDefault() ?? decision.Choices[0];
    }
}
