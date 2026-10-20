namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidBao2025(ProgramSkillFrame f)
    {
        if (f.Bao2025 is not { } r || f.InstructionIndex != 1 || f.TriggerId is null || f.WindowContext is not { } c ||
            GetProgramTrigger(f).Effects is not [var e] || e.Op != r.Operation || !IsBaoOperation(e.Op) ||
            r.Source != BaoSource(f) || r.GameplayHash != f.GameplayHash || r.WindowId != c.ParentFrameId ||
            r.ActualTurn != _turnNumber || r.ActualTurnOwner != _currentSeat || !Enum.IsDefined(r.Stage) ||
            r.Cards.Count != r.From.Count || r.Cards.Distinct().Count() != r.Cards.Count ||
            r.SelectedTargets.Distinct().Count() != r.SelectedTargets.Count || r.SelectedTargets.Any(t => !IsValidPlayerSeat(t)) ||
            r.MovementBefore < 0 || r.MovementAfter < r.MovementBefore || r.MovementAfter > _movementSequence ||
            r.OutcomeCursor < 0 || r.OutcomeCursor > r.SelectedTargets.Count) return false;
        var started = CompleteProgramEventHistory().OfType<ProgramBao2025StartedEvent>().Where(x => x.FrameId == f.Id).ToArray();
        if (started is not [var start] || start.Receipt.Operation != r.Operation || start.Receipt.Source != r.Source ||
            start.Receipt.GameplayHash != r.GameplayHash || start.Receipt.WindowId != r.WindowId ||
            start.Receipt.OriginalUseId != r.OriginalUseId || start.Receipt.OriginalActionId != r.OriginalActionId ||
            !start.Receipt.Targets.SequenceEqual(r.Targets) || CompleteProgramEventHistory().OfType<ProgramBao2025CompletedEvent>().Any(x => x.FrameId == f.Id)) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index < 1) return false;
        if (r.Operation == SkillProgramEffectOp.TakeConvertedSlashTargetAndResetLimitedSkill)
        {
            if (_resolutionStack[index - 1] is not ProgramCardTriggerWindowFrame window || window.Id != c.ParentFrameId ||
                c.Window != SkillProgramTriggerWindow.CardUseCompleted || window.ParentFrameId != r.OriginalUseId ||
                window.Action.ActionId != r.OriginalActionId || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
                !MountObserverCandidateMatches(f, ToSharedCandidate(window.Candidates[window.CandidateIndex])) ||
                LifecycleCardUse(r.OriginalUseId!.Value) is not { Step: ResolutionFrameStep.Completed } use || !HasIssuedNonBasicSlashDistance(use.Id) ||
                !use.TargetSeats.SequenceEqual(r.Targets)) return false;
            if (r.GainedCardId is { } gained)
            {
                if (r.GainedFrom is not { } from || from.OwnerSeat != r.Recipient || r.GainedCardKind is null ||
                    _cardMovements.Count(m => m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter && m.CardId == gained &&
                        m.CardKind == r.GainedCardKind && m.From == from && m.Reason == BaoGainReason &&
                        (m.To == CardLocation.Hand(f.OwnerSeat) || m.To == CardLocation.OutsideGame)) != 1 ||
                    !CompleteProgramEventHistory().OfType<ProgramBao2025MovementIssuedEvent>().Any(x => x.FrameId == f.Id &&
                        x.Receipt.GainedCardId == gained && x.Receipt.MovementBefore == r.MovementBefore && x.Receipt.MovementAfter == r.MovementAfter)) return false;
            }
            return r.Stage is ProgramBao2025Stage.TakeTarget or ProgramBao2025Stage.TakeCard or ProgramBao2025Stage.GainChildren or
                ProgramBao2025Stage.ResetChoice or ProgramBao2025Stage.ResetHpChildren;
        }
        if (_resolutionStack[index - 1] is not ProgramLifecycleTriggerWindowFrame lifecycle || lifecycle.Id != c.ParentFrameId ||
            lifecycle.Window != c.Window || lifecycle.CandidateIndex < 0 || lifecycle.CandidateIndex >= lifecycle.Candidates.Count ||
            !MountObserverCandidateMatches(f, lifecycle.Candidates[lifecycle.CandidateIndex])) return false;
        if (r.Operation == SkillProgramEffectOp.PayTwoForChosenAssaultAndApplyTargetOutcomes)
        {
            if (c.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || c.OwnerSeat != f.OwnerSeat || r.Cards.Count > 2) return false;
            var paid = CompleteProgramEventHistory().OfType<ProgramBao2025MovementIssuedEvent>().Where(x => x.FrameId == f.Id).ToArray();
            if (r.Stage is ProgramBao2025Stage.AssaultCost or ProgramBao2025Stage.AssaultTargets) return paid.Length == 0;
            if (paid is not [var payment] || payment.Receipt.Cards.Count != 2 || !payment.Receipt.Cards.SequenceEqual(r.Cards) ||
                !payment.Receipt.From.SequenceEqual(r.From) || payment.Receipt.MovementAfter < payment.Receipt.MovementBefore ||
                r.Cards.Select((id, i) => _cardMovements.Count(m => m.CardId == id && m.From == r.From[i] && m.To == CardLocation.DiscardPile &&
                    m.Reason == BaoCostReason && m.Sequence > payment.Receipt.MovementBefore && m.Sequence <= payment.Receipt.MovementAfter) != 1).Any(v => v)) return false;
            if (r.AssaultUseId is { } useId)
            {
                if (r.AssaultActionId is not { } action || CompleteProgramEventHistory().OfType<ChosenAssaultUseIssuedEvent>().Count(x => x.FrameId == f.Id &&
                    x.Source == r.Source && x.UseId == useId && x.ActionId == action && x.SelectedTargets.SequenceEqual(r.SelectedTargets)) != 1) return false;
                var damage = CompleteProgramEventHistory().OfType<ChosenAssaultActualDamageObservedEvent>().Where(x => x.ProgramFrameId == f.Id).ToArray();
                if (damage.Any(x => x.UseId != useId || x.SourceSeat != f.OwnerSeat || x.Amount <= 0) ||
                    !damage.Select(x => x.TargetSeat).Distinct().Order().SequenceEqual(r.DamagedTargets.Order())) return false;
            }
            return r.Stage is ProgramBao2025Stage.AssaultCostChildren or ProgramBao2025Stage.AssaultUse or
                ProgramBao2025Stage.AssaultOutcomes or ProgramBao2025Stage.AssaultOutcomeChildren;
        }
        if (c.Window != SkillProgramTriggerWindow.DyingEntering || c.TargetSeat != f.OwnerSeat ||
            !_resolutionStack.OfType<DyingFrame>().Any(d => d.VictimSeat == f.OwnerSeat)) return false;
        var facts = CompleteProgramEventHistory().OfType<DyingRecipientSkillChoiceEvent>().Where(x => x.FrameId == f.Id).ToArray();
        return r.GrantedSkillId is null ? facts.Length == 0 : facts is [var chosen] && chosen.Source == r.Source && chosen.RecipientSeat == r.Recipient &&
            chosen.Replace == r.ReplacedSkills && chosen.GrantedSkillId == r.GrantedSkillId && chosen.LostGrantIds.SequenceEqual(r.LostGrantIds);
    }
    private void RequireBao2025(ProgramSkillFrame f)
    { if (!ValidBao2025(f)) throw new InvalidOperationException("The native converted Slash, paid assault or Dying recipient lost its exact owner, candidate, once-paid invoice or typed return."); }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 100)]
    private void AssertRegisteredBao2025(ProgramSkillFrame frame)
    {
        if (frame.Bao2025 is not null) RequireBao2025(frame);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 40)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 40)]
    private bool CanContinueBao2025(ProgramSkillFrame f) => f.Bao2025 is { } r && ValidBao2025(f) &&
        (r.GainedCardId is not null || r.Stage is ProgramBao2025Stage.AssaultCostChildren or ProgramBao2025Stage.AssaultUse or ProgramBao2025Stage.AssaultOutcomes or
            ProgramBao2025Stage.AssaultOutcomeChildren or ProgramBao2025Stage.DyingRecipientChoice or ProgramBao2025Stage.DyingSkillChildren or ProgramBao2025Stage.DyingRecoveryChildren);
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 50)]
    private bool IsBaoMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.Bao2025 is not null && e is not null && IsBaoOperation(e.Op) && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && ValidBao2025(f);
    private bool IsChosenAssaultUse(long id) => LifecycleCardUse(id) is { ChosenAssault: { } ret, CardId: 0, CardKind: CardKind.BarbarianAssault,
        PhysicalCardIds.Count: 0, Action: { Type: CardActionType.Use, EffectiveKind: CardKind.BarbarianAssault, PhysicalCards.Count: 0, ConversionChain.Count: 0 } action } use &&
        _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == ret.ProgramFrameId) is { Bao2025: { Stage: ProgramBao2025Stage.AssaultUse } r } parent &&
        parent.InstructionIndex == ret.InstructionIndex && r.AssaultUseId == id && r.AssaultActionId == action.ActionId &&
        action.ProviderSeat == parent.OwnerSeat && use.SourceSeat == action.ActorSeat && action.TargetSeats.SequenceEqual(use.TargetSeats) && ValidBao2025(parent);
    private bool MatchesChosenAssaultUseAction(long id, CardActionContext action, int cardId) => cardId == 0 && IsChosenAssaultUse(id) &&
        LifecycleCardUse(id)!.Action!.ActionId == action.ActionId;
    private bool IsChosenAssaultAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing) =>
        IsChosenAssaultUse(attack.ResolutionId) && processing.Count == 0 && attack.Card is null && attack.PhysicalCards.Count == 0 &&
        attack.EffectiveCardKind == CardKind.BarbarianAssault && ActiveGroupCard is { } group && group.ResolutionId == attack.ResolutionId &&
        group.CurrentAttack?.ResolutionId == attack.ResolutionId && group.DamageSourceSeat == attack.SourceSeat;
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 400)]
    private void ObserveChosenAssaultDamage(IGameEvent payload)
    {
        if (payload is not DamageAppliedEvent { Amount: > 0, SourceLess: false } applied || CurrentDamageAttempt is not { } attack ||
            LifecycleCardUse(attack.ResolutionId) is not { ChosenAssault: { } ret } use || !IsChosenAssaultUse(use.Id) ||
            _resolutionStack.OfType<DamageFrame>().LastOrDefault(d => d.ParentFrameId == use.Id) is not { } damage ||
            damage.SourceSeat != applied.SourceSeat || damage.TargetSeat != applied.TargetSeat || damage.Amount != applied.Amount ||
            attack.SourceSeat != applied.SourceSeat || attack.TargetSeat != applied.TargetSeat) return;
        var f = _resolutionStack.OfType<ProgramSkillFrame>().Single(p => p.Id == ret.ProgramFrameId && p.InstructionIndex == ret.InstructionIndex);
        if (applied.SourceSeat != f.OwnerSeat) return;
        if (CompleteProgramEventHistory().OfType<ChosenAssaultActualDamageObservedEvent>().Any(x => x.DamageFrameId == damage.Id))
            throw new InvalidOperationException("One native chosen-assault damage cannot be observed twice.");
        ReplaceRuntimeFrame(f.Id, f with { Bao2025 = f.Bao2025! with { DamagedTargets = f.Bao2025.DamagedTargets.Append(applied.TargetSeat).Distinct().ToArray() } });
        AdvanceEventRulesAndQueueFact(new ChosenAssaultActualDamageObservedEvent(f.Id, use.Id, damage.Id, applied.SourceSeat, applied.TargetSeat, applied.Amount));
    }
    private bool BaoFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.Bao2025 is not { } r || !CanContinueBao2025(root)) return false;
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return skills.ResumeProgramFrameId == root.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == root.Id && moved.Batch.AwaitingProgramFrameId == root.Id && moved.ResumeProgramFrameId is null &&
                moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter &&
                    (m.Reason == BaoGainReason || m.Reason == BaoCostReason || m.Reason == BaoOutcomeReason));
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == root.Id && hp.ResumeFrameId == root.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                (r.Stage == ProgramBao2025Stage.ResetHpChildren && hp.Change.Kind == HpChangeKind.Loss && hp.Change.TargetSeat == root.OwnerSeat && hp.Change.Amount == 1 ||
                 r.Stage == ProgramBao2025Stage.DyingRecoveryChildren && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == root.OwnerSeat ||
                 hp.Change.Kind == HpChangeKind.Recovery && hp.Change.Amount == 1 && HasBaoSilverLionPayment(root, hp.Change.TargetSeat));
        if (child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, root))
            return recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                (r.Stage == ProgramBao2025Stage.DyingRecoveryChildren && recovery.Attempt.SourceSeat == root.OwnerSeat && recovery.Attempt.TargetSeat == root.OwnerSeat &&
                    recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program || recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
                    HasBaoSilverLionPayment(root, recovery.Attempt.TargetSeat));
        if (child is DyingFrame dying)
            return r.Stage == ProgramBao2025Stage.ResetHpChildren && dying.VictimSeat == root.OwnerSeat && dying.ParentFrameId == root.Id;
        return child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
            state.ResumeProgramFrameId == root.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
            CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == root.Id);
    }
    private bool HasBaoSilverLionPayment(ProgramSkillFrame f, int seat) => f.Bao2025 is { } r && _cardMovements.Any(m => m.Sequence > r.MovementBefore &&
        m.Sequence <= r.MovementAfter && m.From == CardLocation.Equipment(seat) && m.CardKind == CardKind.SilverLion &&
        (m.Reason == BaoGainReason || m.Reason == BaoCostReason || m.Reason == BaoOutcomeReason));
    private ProgramSkillFrame? BaoObserverRoot() => FindProgramObserverRoot(static (engine, root, child) => engine.BaoFirstChild(root, child),
        static (engine, index) => engine.HasMovementObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 20)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1680)]
    private bool HasBaoDamageObserver(long id) => BaoObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 10, group: 170)]
    private bool IsBaoDying() => ActiveDying is { } dying && BaoObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 720)]
    private bool AllowsBaoNestedDamage(ProgramSkillFrame observer, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            BaoObserverRoot() is not { } root || root.Id == observer.Id || observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsMoved or
            SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or
            SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged)) return false;
        var e = GetPausedPrivateOfferEffect(observer);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 450)]
    private bool TryAdvanceBaoSubtree() => _pendingDecision is null && BaoObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramAiChoice(
        SkillProgramEffectOp.TakeConvertedSlashTargetAndResetLimitedSkill,
        SkillProgramEffectOp.PayTwoForChosenAssaultAndApplyTargetOutcomes,
        SkillProgramEffectOp.DyingRecipientChooseReplaceOrGrantThenRecover)]
    private PromptChoice SelectAiBaoChoice(PendingDecision decision, ProgramSkillFrame f)
    {
        var r = f.Bao2025!;
        if (r.Stage == ProgramBao2025Stage.ResetChoice)
            return decision.Choices.Single(c => c.Parameters["branch"] == (_players[f.OwnerSeat].Hp > 1 && BaoHasSpentFriendlyResetSkill(f) ? "reset" : "decline"));
        if (r.Stage == ProgramBao2025Stage.DyingRecipientChoice) return decision.Choices.Single(c => c.Parameters["branch"] == "grant");
        if (r.Stage == ProgramBao2025Stage.AssaultTargets)
        {
            var enemy = decision.Choices.FirstOrDefault(c => c.Parameters["branch"].StartsWith("add-", StringComparison.Ordinal) &&
                !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets.Single()]));
            return enemy ?? decision.Choices.Single(c => c.Parameters["branch"] == "confirm");
        }
        if (r.Stage == ProgramBao2025Stage.AssaultCost)
            return decision.Choices.Where(c => c.Cards.Count == 2).OrderBy(c => c.Cards.Sum(id => GetKeepValue(GetAttackCard(id), _players[f.OwnerSeat]))).First();
        if (r.Stage is ProgramBao2025Stage.TakeTarget or ProgramBao2025Stage.DyingTarget)
            return decision.Choices.FirstOrDefault(c => c.Targets.Count == 1 && AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets.Single()]) == (r.Stage == ProgramBao2025Stage.DyingTarget)) ?? decision.Choices.First(c => c.Targets.Count == 1);
        return decision.Choices.First();
    }
    private bool BaoHasSpentFriendlyResetSkill(ProgramSkillFrame f)
    {
        var resetId = GetProgramTrigger(f).Effects.Single().SourceBind!;
        return _players.Where(p => p.IsAlive && (p.Seat == f.OwnerSeat || AreProgramDistributionAllies(_players[f.OwnerSeat], p)))
            .Any(p => GetSkillBindingShard(p).ProgramInstances.Where(i => i.SkillId == resetId).Any(i => i.Program.Triggers.Any(t =>
                t.UsageScope == SkillUsageScope.Game && t.UsageLimit == 1 &&
                t.Effects is [{ Op: SkillProgramEffectOp.PayTwoForChosenAssaultAndApplyTargetOutcomes }] &&
                _skillRuntimeState.GetUsage(p.Seat, resetId, ProgramTriggerUsageId(new(p.Seat, resetId, t.Id, i.SkillInstanceId,
                    i.Program.GameplayHash, t.Priority)), SkillUsageScope.Game) > 0)));
    }
    private void AssertChosenAssaults()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.ChosenAssault is not null))
            if (!IsChosenAssaultUse(use.Id)) throw new InvalidOperationException("Chosen assault lost its exact issued native action and paid typed program parent.");
    }
}
