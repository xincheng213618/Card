namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidKuangfu(ProgramSkillFrame f)
    {
        if (f.Kuangfu is not { } r || f.TriggerId is not null || f.WindowContext is not null || f.InstructionIndex != 1 ||
            r.InstructionIndex != f.InstructionIndex || r.Source != KuangfuSource(f) || r.GameplayHash != f.GameplayHash ||
            r.ActualTurn != _turnNumber || r.ActualTurnOwnerSeat != _currentSeat || r.ActualTurnOwnerSeat != f.OwnerSeat ||
            _phase != TurnPhase.Play && !KuangfuGameEnded() || !Enum.IsDefined(r.Stage) || f.SelectedCardIds.Count != 0 ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } definition || definition.GameplayHash != f.GameplayHash) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(f, definition);
        if (plan.Activation is not { UsesPerPhase: 1, UsesPerTurn: null, MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0 } ||
            plan.Instructions is not [{ Op: SkillProgramEffectOp.DiscardEquipmentThenSlashAndOwnershipOutcome }]) return false;
        var facts = CompleteProgramEventHistory().ToArray();
        if (facts.OfType<KuangfuStartedEvent>().Count(e => e == new KuangfuStartedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.ActualTurnOwnerSeat)) != 1 ||
            r.SelectedHandIds.Distinct().Count() != r.SelectedHandIds.Count || r.DiscardedHandIds.Distinct().Count() != r.DiscardedHandIds.Count ||
            r.SelectedHandIds.Any(id => id <= 0) || r.DiscardedHandIds.Any(id => id <= 0)) return false;
        if (r.Stage == KuangfuStage.EquipmentChoice) return r.EquipmentOwnerSeat is null && r.EquipmentCardId is null &&
            r.EquipmentKind is null && r.PaymentBefore == 0 && r.PaymentAfter == 0 && r.SlashReturn is null && !r.CausedDamage &&
            f.SelectedTargetSeats.Count == 0 && f.PendingMovementContinuation is null && r.SelectedHandIds.Count == 0 && r.DiscardedHandIds.Count == 0 &&
            r.DiscardRequired == 0 && r.ActualDraw == 0 && r.OutcomeBefore == 0 && r.OutcomeAfter == 0;
        if (r.EquipmentOwnerSeat is not { } equipmentOwner || !IsValidPlayerSeat(equipmentOwner) || r.EquipmentCardId is not { } card || card <= 0 ||
            r.EquipmentKind is not { } kind || GetAttackCard(card).IsGeneralWeapon || r.PaymentBefore < 0 || r.PaymentAfter <= r.PaymentBefore ||
            _cardMovements.Count(m => m.Sequence > r.PaymentBefore && m.Sequence <= r.PaymentAfter && m.CardId == card && m.CardKind == kind &&
                m.From == CardLocation.Equipment(equipmentOwner) && m.To == CardLocation.DiscardPile && m.Reason.Value == KuangfuEquipmentReason) != 1 ||
            facts.OfType<KuangfuEquipmentPaidEvent>().Count(e => e == new KuangfuEquipmentPaidEvent(f.Id, equipmentOwner, card, kind, r.PaymentBefore, r.PaymentAfter)) != 1) return false;
        if (_cardMovements.Any(m => m.Sequence > r.PaymentBefore && m.Sequence <= r.PaymentAfter && m.CardId != card &&
            !(kind == CardKind.WoodenOx && m.From == CardLocation.Grain(equipmentOwner) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard))) return false;
        if (r.Stage is KuangfuStage.EquipmentChildren or KuangfuStage.SlashTargetChoice)
            return r.SlashReturn is null && !r.CausedDamage && f.SelectedTargetSeats.Count == 0 && r.SelectedHandIds.Count == 0 &&
                r.DiscardedHandIds.Count == 0 && r.DiscardRequired == 0 && r.ActualDraw == 0 && r.OutcomeBefore == 0 && r.OutcomeAfter == 0 &&
                (r.Stage == KuangfuStage.EquipmentChildren ? IsKuangfuPending(f) : f.PendingMovementContinuation is null);
        if (r.SlashReturn is not { } returned || returned.ProgramFrameId != f.Id || returned.InstructionIndex != r.InstructionIndex ||
            returned.Source != r.Source || returned.GameplayHash != r.GameplayHash || returned.ActualTurn != r.ActualTurn ||
            returned.ActualTurnOwnerSeat != r.ActualTurnOwnerSeat || returned.EquipmentOwnerSeat != equipmentOwner || returned.EquipmentCardId != card ||
            returned.CardUseFrameId <= f.Id || returned.CardActionId <= 0 || !IsValidPlayerSeat(returned.OriginalTargetSeat) || returned.OriginalTargetSeat == f.OwnerSeat ||
            !f.SelectedTargetSeats.SequenceEqual([returned.OriginalTargetSeat]) ||
            facts.OfType<KuangfuSlashIssuedEvent>().Count(e => e.Return == returned) != 1) return false;
        var damageFacts = facts.OfType<KuangfuActualDamageObservedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if (r.CausedDamage != (damageFacts.Length > 0) || damageFacts.Select(e => e.DamageFrameId).Distinct().Count() != damageFacts.Length ||
            damageFacts.Any(e => e.CardUseFrameId != returned.CardUseFrameId || e.Amount <= 0 || !IsValidPlayerSeat(e.SourceSeat) || !IsValidPlayerSeat(e.TargetSeat) ||
                facts.OfType<DamageRequestedEvent>().Count(q => q.ResolutionId == e.DamageFrameId && q.SourceSeat == e.SourceSeat && q.TargetSeat == e.TargetSeat &&
                    q.Amount == e.Amount && q.SourceLess == e.SourceLess && (q.SourceCard is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)) != 1)) return false;
        if (r.Stage == KuangfuStage.SlashIssued) return f.PendingMovementContinuation is null && r.SelectedHandIds.Count == 0 && r.DiscardedHandIds.Count == 0 &&
            r.OutcomeBefore == 0 && r.OutcomeAfter == 0 && r.DiscardRequired == 0 && r.ActualDraw == 0;
        if (facts.OfType<KuangfuSlashResolvedEvent>().Count(e => e == new KuangfuSlashResolvedEvent(f.Id, returned.CardUseFrameId, returned.CardActionId, r.CausedDamage)) != 1 ||
            facts.OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == returned.CardUseFrameId) != 1) return false;
        if (r.Stage == KuangfuStage.HandDiscardChoice)
            return equipmentOwner != f.OwnerSeat && !r.CausedDamage && r.DiscardRequired is > 0 and <= 2 &&
                r.SelectedHandIds.Count < r.DiscardRequired && r.SelectedHandIds.All(id => GetHand(_players[f.OwnerSeat]).Any(c => c.Id == id)) &&
                r.DiscardedHandIds.Count == 0 && f.PendingMovementContinuation is null;
        if (r.Stage != KuangfuStage.OutcomeChildren || !IsKuangfuPending(f) || r.SelectedHandIds.Count != 0 || r.OutcomeBefore < r.PaymentAfter || r.OutcomeAfter < r.OutcomeBefore) return false;
        if (equipmentOwner == f.OwnerSeat)
            return r.CausedDamage && r.DiscardedHandIds.Count == 0 && r.ActualDraw is >= 0 and <= 2 &&
                facts.OfType<KuangfuDrawIssuedEvent>().Count(e => e == new KuangfuDrawIssuedEvent(f.Id, 2, r.ActualDraw, r.OutcomeBefore, r.OutcomeAfter)) == 1 &&
                _cardMovements.Count(m => m.Sequence > r.OutcomeBefore && m.Sequence <= r.OutcomeAfter && m.From == CardLocation.DrawPile &&
                    m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == KuangfuDrawReason) == r.ActualDraw;
        return !r.CausedDamage && r.DiscardRequired is > 0 and <= 2 && r.DiscardedHandIds.Count == r.DiscardRequired && r.ActualDraw == 0 &&
            facts.OfType<KuangfuHandDiscardPaidEvent>().Count(e => e.ProgramFrameId == f.Id && e.Before == r.OutcomeBefore && e.After == r.OutcomeAfter &&
                e.CardIds.SequenceEqual(r.DiscardedHandIds)) == 1 &&
            r.DiscardedHandIds.All(id => _cardMovements.Count(m => m.Sequence > r.OutcomeBefore && m.Sequence <= r.OutcomeAfter && m.CardId == id &&
                m.From == CardLocation.Hand(f.OwnerSeat) && m.To == CardLocation.DiscardPile && m.Reason.Value == KuangfuHandReason) == 1);
    }
    private static bool IsKuangfuPending(ProgramSkillFrame f) => f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } p && p.SubjectSeat == f.OwnerSeat;
    private bool IsKuangfuMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.DiscardEquipmentThenSlashAndOwnershipOutcome && pending == f.PendingMovementContinuation &&
        f.Kuangfu is { Stage: KuangfuStage.EquipmentChildren or KuangfuStage.OutcomeChildren } && IsKuangfuPending(f) && ValidKuangfu(f);
    private bool HasExactKuangfuProgramSelection(ProgramSkillFrame f, ProgramExecutionPlan plan) =>
        plan.Activation is { MinTargets: 0, MaxTargets: 0 } && f.Kuangfu?.SlashReturn is { } returned &&
        plan.Instructions is [{ Op: SkillProgramEffectOp.DiscardEquipmentThenSlashAndOwnershipOutcome }] &&
        f.SelectedTargetSeats.SequenceEqual([returned.OriginalTargetSeat]) && ValidKuangfu(f);
    private bool IsKuangfuSlashUse(CardUseFrame use) => use.KuangfuSlashReturn is { } returned &&
        use.Id == returned.CardUseFrameId && use.CardId == 0 && use.PhysicalCardIds is { Count: 0 } &&
        _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == returned.ProgramFrameId) is { } root &&
        root.Kuangfu is { Stage: KuangfuStage.SlashIssued } receipt && receipt.SlashReturn == returned && ValidKuangfu(root) &&
        (use.CurrentSlashFirePolicy?.OriginalAction ?? use.Action) is { Type: CardActionType.Use, EffectiveKind: CardKind.Slash, EffectiveSuit: Suit.None,
            EffectiveRank: 0, PhysicalCards.Count: 0 } action && action.ActionId == returned.CardActionId &&
        ShownEntityUseActorMatches(use, root.OwnerSeat, action.ProviderSeat) && action.ConversionChain.Count == 0;
    private bool HasIssuedKuangfuDistance(long? id, int actor) => id is { } useId && LifecycleCardUse(useId) is { } use &&
        use.KuangfuSlashReturn?.Source.OwnerSeat == actor && IsKuangfuSlashUse(use);
    private void ObserveKuangfuAppliedDamage(IGameEvent payload)
    {
        if (payload is not DamageAppliedEvent { Amount: > 0 } applied || CurrentDamageAttempt is not { } attack ||
            LifecycleCardUse(attack.ResolutionId) is not { KuangfuSlashReturn: { } returned } use || !IsKuangfuSlashUse(use) ||
            !IsSlashCard(attack.EffectiveCardKind ?? CardKind.Slash) ||
            _resolutionStack.OfType<DamageFrame>().LastOrDefault(d => d.ParentFrameId == use.Id) is not { } damage ||
            damage.SourceSeat != applied.SourceSeat || damage.TargetSeat != applied.TargetSeat || damage.Amount != applied.Amount ||
            attack.SourceSeat != applied.SourceSeat || attack.TargetSeat != applied.TargetSeat || attack.IsSourceLess != applied.SourceLess ||
            CompleteProgramEventHistory().OfType<DamageRequestedEvent>().Count(e => e.ResolutionId == damage.Id && e.SourceSeat == applied.SourceSeat &&
                e.TargetSeat == applied.TargetSeat && e.Amount == applied.Amount && e.SourceLess == applied.SourceLess) != 1) return;
        var root = _resolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == returned.ProgramFrameId);
        if (CompleteProgramEventHistory().OfType<KuangfuActualDamageObservedEvent>().Any(e => e.DamageFrameId == damage.Id))
            throw new InvalidOperationException("One actual equipment Slash damage cannot be recorded twice.");
        ReplaceRuntimeFrame(root.Id, root with { Kuangfu = root.Kuangfu! with { CausedDamage = true } });
        AdvanceEventRulesAndQueueFact(new KuangfuActualDamageObservedEvent(root.Id, use.Id, damage.Id, applied.SourceSeat,
            applied.TargetSeat, applied.Amount, applied.SourceLess));
    }
    private bool KuangfuFirstPaidChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (!ValidKuangfu(root) || !IsKuangfuPending(root) || root.Kuangfu is not { } r ||
            r.Stage is not (KuangfuStage.EquipmentChildren or KuangfuStage.OutcomeChildren)) return false;
        if (child is ProgramLifecycleTriggerWindowFrame changed && changed.Window == SkillProgramTriggerWindow.SkillsChanged)
            return changed.ResumeProgramFrameId == root.Id && changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                changed.CandidateIndex >= 0 && changed.CandidateIndex <= changed.Candidates.Count;
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return state.ResumeProgramFrameId == root.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
                state.Window is SkillProgramTriggerWindow.CharacterTurnedOver or SkillProgramTriggerWindow.CharacterTurnedFaceUp or SkillProgramTriggerWindow.CharacterEnteredChain &&
                CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Count(e => e.Change.Id == state.Id && e.Change.ParentFrameId == root.Id &&
                    e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window) == 1;
        var before = r.Stage == KuangfuStage.EquipmentChildren ? r.PaymentBefore : r.OutcomeBefore;
        var after = r.Stage == KuangfuStage.EquipmentChildren ? r.PaymentAfter : r.OutcomeAfter;
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == root.Id && moved.ResumeProgramFrameId is null && moved.Batch.AwaitingProgramFrameId == root.Id &&
                moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > before && m.Sequence <= after);
        if (r.Stage != KuangfuStage.EquipmentChildren || r.EquipmentKind != CardKind.SilverLion || r.EquipmentOwnerSeat is not { } payer) return false;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.ResumeFrameId == root.Id && hp.Change.ParentFrameId == root.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == payer && hp.Change.TargetSeat == payer && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, root) &&
            recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            recovery.Attempt.Completion.MoveReason?.Value == KuangfuEquipmentReason && recovery.Attempt.SourceSeat == payer && recovery.Attempt.TargetSeat == payer && recovery.Attempt.Amount == 1;
    }
    private ProgramSkillFrame? KuangfuPaidObserverRoot(long? damageWindow = null)
    {
        for (var i = 0; i + 1 < _resolutionStack.Count; i++)
        {
            if (_resolutionStack[i] is not ProgramSkillFrame root || !KuangfuFirstPaidChild(root, _resolutionStack[i + 1])) continue;
            if (damageWindow is { } id && !_resolutionStack.Skip(i + 1).Any(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)) continue;
            var valid = true;
            for (var j = i + 2; j < _resolutionStack.Count; j++)
            {
                if (_resolutionStack[j] is ProgramLifecycleTriggerWindowFrame changed && _resolutionStack[j - 1] is ProgramSkillFrame owner &&
                    changed.Window == SkillProgramTriggerWindow.SkillsChanged && changed.ResumeProgramFrameId == owner.Id &&
                    changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && changed.CandidateIndex >= 0 && changed.CandidateIndex <= changed.Candidates.Count) continue;
                if (!PaidColorDamageClaimObserverEdge(j) && !DyingSuitsStructuralEdge(_resolutionStack[j - 1], _resolutionStack[j])) { valid = false; break; }
                if (_resolutionStack[j] is DyingFrame dying && j + 1 < _resolutionStack.Count &&
                    (IsPaidHandRepaymentRescueRide(j, dying) || IsPaidHandRepaymentProgramAlcoholRide(j, dying) ||
                     PolicyCounterspellVirtualAlcoholRide(j, dying) || PaidObserverDamageVirtualAlcoholRide(j, dying))) break;
            }
            if (valid) return root;
        }
        return null;
    }
    private bool HasKuangfuDamageObserver(long window) => KuangfuPaidObserverRoot(window) is not null;
    private bool IsKuangfuProgramDying() => ActiveDying is { } dying && KuangfuPaidObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool TryAdvanceKuangfuSubtree()
    {
        if (_pendingDecision is not null || KuangfuPaidObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attack)
        {
            if (CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null || attack.AttackReturn is null ||
                _resolutionStack.Any(f => f is DamageFrame damage && damage.ParentFrameId == attack.Id ||
                    f is BeforeDamageProgramWindowFrame before && (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == attack.Id)) return false;
            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id)); AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or
            ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame)
        { AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true; }
        if (top is DeathFrame death) { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        return false;
    }
    private bool AllowsKuangfuNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || f.InstructionIndex < 1 || ActiveDying is not null || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            f.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            KuangfuPaidObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        return e.Op == SkillProgramEffectOp.Damage && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    private void AssertKuangfuPrograms()
    {
        foreach (var root in _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.Kuangfu is not null))
        {
            if (!ValidKuangfu(root)) throw new InvalidOperationException("An equipment Slash changed its paid entity, original owner or issued successor.");
            if (root.Kuangfu!.Stage is KuangfuStage.EquipmentChoice or KuangfuStage.SlashTargetChoice or KuangfuStage.HandDiscardChoice &&
                _resolutionStack.LastOrDefault()?.Id == root.Id && !KuangfuGameEnded() &&
                (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } p || p.PlayerSeat != root.OwnerSeat ||
                 p.IsPrivate != (root.Kuangfu.Stage == KuangfuStage.HandDiscardChoice) || !AssistedChoicesEqual(p.Choices, KuangfuChoices(root))))
                throw new InvalidOperationException("Equipment Slash lost its exact public equipment/target or private own-hand choices.");
        }
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.KuangfuSlashReturn is not null))
        {
            var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
            if (index <= 0 || _resolutionStack[index - 1].Id != use.KuangfuSlashReturn!.ProgramFrameId || !IsKuangfuSlashUse(use) ||
                !IsSlashCard(use.CardKind) || use.CardKind != CardKind.Slash && !IsCurrentSlashFireChangedUse(use) ||
                use.TargetSeats.Count == 0 && !CompleteProgramEventHistory().OfType<SlashTargetsReplacedEvent>().Any(e =>
                    e.CardUseFrameId == use.Id && e.Targets.Count == 0) || use.TargetSeats.Count > 1 && !HasShortRangeSlashTail(use) &&
                    !HasSameTypeAidTargetTail(use) && !HasIssuedOriginalTargetAdditionTail(use) && !IsCurrentSlashFireChangedUse(use) ||
                CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Count(e => e.ResolutionId == use.Id && e.CardId == 0 &&
                    e.SourceSeat == use.KuangfuSlashReturn.Source.OwnerSeat && e.CardKind == CardKind.Slash) != 1 ||
                CompleteProgramEventHistory().OfType<TargetsConfirmedEvent>().Count(e => e.ResolutionId == use.Id &&
                    e.TargetSeats.SequenceEqual([use.KuangfuSlashReturn.OriginalTargetSeat])) != 1)
                throw new InvalidOperationException("Equipment Slash lost its exact zero-entity use and typed paid parent.");
        }
    }
}
