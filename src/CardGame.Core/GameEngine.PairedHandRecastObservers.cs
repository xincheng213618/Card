namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidPairedHandRecastReceipt(ProgramSkillFrame f)
    {
        if (f.PairedHandRecast is not { } r || f.TriggerId is not null || f.WindowContext is not null ||
            f.InstructionIndex != 1 || r.InstructionIndex != f.InstructionIndex ||
            r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            r.GameplayHash != f.GameplayHash || r.Owner.Seat != f.OwnerSeat || !IsValidPlayerSeat(r.Partner.Seat) ||
            r.Partner.Seat == f.OwnerSeat || f.SelectedCardIds is not [var original] ||
            r.Owner.Material is not { } ownerMaterial || original != ownerMaterial.CardId ||
            f.SelectedTargetSeats is not [var partner] || partner != r.Partner.Seat ||
            r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _currentSeat || _currentSeat != f.OwnerSeat ||
            _phase != TurnPhase.Play || r.PhaseInstanceId <= 0 || r.PhaseInstanceId != _cardUseDebitPhaseInstanceId ||
            r.Cursor is < 0 or > 2 || !Enum.IsDefined(r.Stage) ||
            _resolutionStack.FindIndex(frame => frame.Id == f.Id) != 0) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        if (plan.Activation is not { MinCards: 1, MaxCards: 1, MinTargets: 1, MaxTargets: 1, UsesPerPhase: 1,
                UsesPerTurn: null, UsesPerGame: null, TargetKind: SkillProgramTargetKind.OtherLivingWithHand } activation ||
            activation.UsageGroup != r.UsageGroup || !activation.SourceZones.SequenceEqual([CardZoneKind.Hand]) ||
            activation.Effects is not [{ Op: SkillProgramEffectOp.PairedHandRecast, Target: SkillProgramEffectTarget.SelectedTarget,
                Amount: 1, Condition.Kind: SkillProgramConditionKind.Always }] ||
            plan.Instructions is not [{ Op: SkillProgramEffectOp.PairedHandRecast }] ||
            _programPhaseUses.GetValueOrDefault((f.OwnerSeat, f.SkillId, r.UsageGroup)) != 1 ||
            r.EligiblePartnerMaterials.Count == 0 || r.EligiblePartnerMaterials.Select(m => m.CardId).Distinct().Count() != r.EligiblePartnerMaterials.Count ||
            r.EligiblePartnerMaterials.Any(m => m.CardId <= 0 || !Enum.IsDefined(m.PrintedKind) || m.From != CardLocation.Hand(partner)) ||
            r.Partner.Material is { } selected && !r.EligiblePartnerMaterials.Contains(selected)) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<ProgramSkillStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
                e.SkillId == f.SkillId && e.ActivationId == f.ActivationId) != 1 ||
            history.OfType<PairedHandRecastStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] ||
            start.Source != r.Source || start.GameplayHash != r.GameplayHash || start.UsageGroup != r.UsageGroup ||
            start.PartnerSeat != partner || start.ActualTurnNumber != r.ActualTurnNumber ||
            start.ActualTurnOwnerSeat != r.ActualTurnOwnerSeat || start.PhaseInstanceId != r.PhaseInstanceId) return false;

        bool ParticipantValid(PairedHandRecastParticipantReceipt p, int cursor)
        {
            if (p.Material is { } material && (material.CardId <= 0 || !Enum.IsDefined(material.PrintedKind) || material.From != CardLocation.Hand(p.Seat))) return false;
            var payments = history.OfType<PairedHandRecastPaidEvent>().Where(e => e.FrameId == f.Id && e.Cursor == cursor).ToArray();
            var draws = history.OfType<PairedHandRecastDrawIssuedEvent>().Where(e => e.FrameId == f.Id && e.Cursor == cursor).ToArray();
            var skips = history.OfType<PairedHandRecastSkippedEvent>().Where(e => e.FrameId == f.Id && e.Cursor == cursor).ToArray();
            if (!p.CostIssued)
                return !p.DrawIssued && p.CostBefore == 0 && p.CostAfter == 0 && p.CostBatchId is null &&
                    p.ActualDrawCount == 0 && p.DrawBefore == 0 && p.DrawAfter == 0 && payments.Length == 0 && draws.Length == 0 &&
                    (p.Skipped ? p.Material is not null && p.SkipReason is { } reason && Enum.IsDefined(reason) &&
                        skips is [var skipped] && skipped.ParticipantSeat == p.Seat && skipped.Reason == reason :
                        p.SkipReason is null && skips.Length == 0);
            if (p.Material is not { } paid || p.Skipped || p.SkipReason is not null || skips.Length != 0 ||
                p.CostBefore < 0 || p.CostAfter <= p.CostBefore || p.CostAfter > PairedHandRecastSequence ||
                p.CostBatchId is not { } batch || payments is not [var invoice] || invoice.ParticipantSeat != p.Seat ||
                invoice.CardId != paid.CardId || invoice.PrintedKind != paid.PrintedKind || invoice.From != paid.From ||
                invoice.BatchId != batch || invoice.SequenceBefore != p.CostBefore || invoice.SequenceAfter != p.CostAfter) return false;
            var costs = _cardMovements.Where(m => m.Sequence > p.CostBefore && m.Sequence <= p.CostAfter).ToArray();
            if (costs is not [var cost] || cost.CardId != paid.CardId || cost.CardKind != paid.PrintedKind ||
                cost.From != paid.From || cost.To != CardLocation.DiscardPile || cost.Reason != CardMoveReasons.RecastDiscard) return false;
            if (!p.DrawIssued) return p.ActualDrawCount == 0 && p.DrawBefore == 0 && p.DrawAfter == 0 && draws.Length == 0;
            if (p.ActualDrawCount is < 0 or > 1 || p.DrawBefore < p.CostAfter || p.DrawAfter < p.DrawBefore ||
                p.DrawAfter > PairedHandRecastSequence || draws is not [var draw] || draw.ParticipantSeat != p.Seat ||
                draw.ActualCount != p.ActualDrawCount || draw.SequenceBefore != p.DrawBefore || draw.SequenceAfter != p.DrawAfter) return false;
            var rewards = _cardMovements.Where(m => m.Sequence > p.DrawBefore && m.Sequence <= p.DrawAfter).ToArray();
            return rewards.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(p.Seat) &&
                    m.Reason == CardMoveReasons.RecastDraw) == p.ActualDrawCount &&
                rewards.All(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(p.Seat) && m.Reason == CardMoveReasons.RecastDraw ||
                    m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle);
        }
        if (!ParticipantValid(r.Owner, 0) || !ParticipantValid(r.Partner, 1) ||
            history.OfType<PairedHandRecastPaidEvent>().Any(e => e.FrameId == f.Id && e.Cursor is not (0 or 1)) ||
            history.OfType<PairedHandRecastDrawIssuedEvent>().Any(e => e.FrameId == f.Id && e.Cursor is not (0 or 1)) ||
            history.OfType<PairedHandRecastSkippedEvent>().Any(e => e.FrameId == f.Id && e.Cursor is not (0 or 1))) return false;
        var completions = history.OfType<PairedHandRecastCompletedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == PairedHandRecastStage.ChoosingPartner)
            return r.Cursor == 0 && r.Partner.Material is null && !r.Owner.CostIssued && !r.Partner.CostIssued &&
                !r.Owner.Skipped && !r.Partner.Skipped && f.PendingMovementContinuation is null && completions.Length == 0 &&
                PairedRecastMaterialStillOwned(r.Owner) && r.EligiblePartnerMaterials.SequenceEqual(PairedHandMaterials(partner));
        if (r.Partner.Material is null) return false;
        for (var cursor = 0; cursor < 2; cursor++)
        {
            var p = cursor == 0 ? r.Owner : r.Partner;
            if (cursor < r.Cursor && !p.Skipped && (!p.CostIssued || !p.DrawIssued)) return false;
            if (cursor > r.Cursor && (p.CostIssued || p.Skipped)) return false;
        }
        if (r.Owner.DrawIssued && r.Partner.CostIssued && r.Partner.CostBefore < r.Owner.DrawAfter) return false;
        if (r.Stage == PairedHandRecastStage.Complete)
            return r.Cursor == 2 && f.PendingMovementContinuation is null && completions is [var complete] &&
                complete.OwnerPaid == r.Owner.CostIssued && complete.PartnerPaid == r.Partner.CostIssued &&
                complete.OwnerDrawCount == r.Owner.ActualDrawCount && complete.PartnerDrawCount == r.Partner.ActualDrawCount;
        if (completions.Length != 0) return false;
        if (r.Stage == PairedHandRecastStage.Ready)
            return f.PendingMovementContinuation is null && (r.Cursor == 2 ||
                CurrentPairedRecast(r) is { CostIssued: false, Skipped: false });
        if (r.Cursor >= 2 || f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending ||
            pending.SubjectSeat != CurrentPairedRecast(r).Seat) return false;
        var current = CurrentPairedRecast(r);
        return r.Stage == PairedHandRecastStage.CostChildren ? current.CostIssued && !current.DrawIssued :
            r.Stage == PairedHandRecastStage.DrawChildren && current.CostIssued && current.DrawIssued;
    }

    private bool PairedHandRecastFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.PairedHandRecast is not { Stage: PairedHandRecastStage.CostChildren or PairedHandRecastStage.DrawChildren } r ||
            !ValidPairedHandRecastReceipt(f) || child is not CardsMovedTriggerWindowFrame moved) return false;
        var p = CurrentPairedRecast(r); var drawing = r.Stage == PairedHandRecastStage.DrawChildren;
        var before = drawing ? p.DrawBefore : p.CostBefore; var after = drawing ? p.DrawAfter : p.CostAfter;
        return moved.ResumeProgramFrameId is null && moved.Batch.Id == moved.Id && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.AwaitingProgramFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
            moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && (drawing || moved.Batch.Id == p.CostBatchId) && moved.Batch.Movements.All(m =>
                _cardMovements.Contains(m) && m.Sequence > before && m.Sequence <= after &&
                (drawing ? m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(p.Seat) && m.Reason == CardMoveReasons.RecastDraw ||
                    m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle :
                    p.Material is { } paid && m.CardId == paid.CardId && m.CardKind == paid.PrintedKind &&
                    m.From == paid.From && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.RecastDiscard));
    }

    private void AssertPairedHandRecast(ProgramSkillFrame f)
    {
        if (f.PairedHandRecast is null) return;
        if (!ValidPairedHandRecastReceipt(f))
            throw new InvalidOperationException("Paired hand recast lost its frozen real-hand choices, actual phase, participant cursor or once-paid cost/draw invoice.");
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        if (index + 1 < _resolutionStack.Count && !PairedHandRecastFirstChild(f, _resolutionStack[index + 1]))
            throw new InvalidOperationException("Paired hand recast retained an unrelated first native child.");
        if (ReferenceEquals(f, _resolutionStack.LastOrDefault()) && f.PairedHandRecast.Stage == PairedHandRecastStage.ChoosingPartner &&
            (_pendingDecision is not { } prompt || !IsPairedHandRecastChoice(f, prompt)))
            throw new InvalidOperationException("Paired hand recast lost its exact partner-only private hand prompt.");
    }
    private bool IsPairedHandRecastChoice(ProgramSkillFrame f, PendingDecision decision)
    {
        if (f.PairedHandRecast is not { Stage: PairedHandRecastStage.ChoosingPartner, Cursor: 0, Partner.Material: null } r ||
            !ValidPairedHandRecastReceipt(f) || decision.Kind != DecisionKind.ProgramTrigger || !decision.IsPrivate ||
            decision.PlayerSeat != r.Partner.Seat || decision.TargetSeat != r.Partner.Seat || decision.SourceSeat != f.OwnerSeat ||
            decision.ValidTargetSeats.Count != 0 || decision.ValidContentIds.Count != 0 || decision.RequiredCardCount != 0 ||
            decision.SkillPrompt?.SkillId != f.SkillId) return false;
        var expected = PairedHandRecastChoices(f);
        return decision.ValidCardIds.SequenceEqual(expected.SelectMany(c => c.Cards).Distinct()) && decision.Choices.Count == expected.Count &&
            decision.Choices.Zip(expected).All(pair => pair.First.Id == pair.Second.Id && pair.First.Cards.SequenceEqual(pair.Second.Cards) &&
                pair.First.Targets.SequenceEqual(pair.Second.Targets) && pair.First.Parameters.OrderBy(x => x.Key).SequenceEqual(pair.Second.Parameters.OrderBy(x => x.Key)));
    }

    // Native observer suffixes are admitted only after the issued pair, actual
    // cost/draw ledger and exact first incoming movement batch have been proved.
    private ProgramSkillFrame? PairedHandRecastObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root || !PairedHandRecastFirstChild(root, _resolutionStack[index + 1])) continue;
            var aligned = true;
            for (var child = index + 2; child < _resolutionStack.Count; child++)
            {
                if (_resolutionStack[child] is ProgramLifecycleTriggerWindowFrame changed && _resolutionStack[child - 1] is ProgramSkillFrame owner &&
                    changed.Window == SkillProgramTriggerWindow.SkillsChanged && changed.ResumeProgramFrameId == owner.Id &&
                    changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                    changed.CandidateIndex >= 0 && changed.CandidateIndex <= changed.Candidates.Count) continue;
                if (!DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !SameNameHandStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !CompletedUndamagedTargetRevealStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) && !RecipientCategoryMarkStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) && !OffTurnUsedCardGiftStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !CardSupplyCompletionStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !ResponseCompletionStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !OriginalHandEntityStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !OutsidePhaseDrawDiscardStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !HalfHandPaidDamageObserverEdge(child) && !PaidTargetObserverEdge(child)) { aligned = false; break; }
                if (_resolutionStack[child] is DyingFrame dying && child + 1 < _resolutionStack.Count &&
                    ((IsOriginalDyingSuspendedByDyingSuits(dying) || IsOriginalDyingSuspendedByRecipientCategoryMark(dying)) || IsOriginalDyingSuspendedByOwnedDeathBenefit(dying) ||
                     IsPaidHandRepaymentProgramAlcoholRide(child, dying) || IsPaidHandRepaymentRescueRide(child, dying) ||
                     PolicyCounterspellVirtualAlcoholRide(child, dying) || PaidObserverDamageVirtualAlcoholRide(child, dying))) break;
            }
            if (aligned) return root;
        }
        return null;
    }
    private bool IsPairedHandRecastDying() => ActiveDying is { } dying && PairedHandRecastObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasPairedHandRecastDamageObserver(long windowId) => _resolutionStack.Any(f =>
        f.Id == windowId && f is DamageTriggerWindowFrame) && PairedHandRecastObserverRoot() is not null;
    private bool AllowsPairedHandRecastNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or
                SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            PairedHandRecastObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }
    private bool TryAdvancePairedHandRecastSubtree()
    {
        if (_pendingDecision is not null || PairedHandRecastObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attack)
        {
            if (CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null || attack.AttackReturn is null ||
                _resolutionStack.Any(frame => frame is DamageFrame damage && damage.ParentFrameId == attack.Id ||
                    frame is BeforeDamageProgramWindowFrame before && (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == attack.Id)) return false;
            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id)); AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or
            ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame or RecoveryReplacementFrame)
        { AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true; }
        if (top is DeathFrame death) { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        return false;
    }
}
