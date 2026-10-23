namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool YangBiaoOriginal(ProgramSkillFrame f)
    {
        if (f.ActivationId != f.TriggerId || f.WindowContext is not { } c || c.OwnerSeat != f.OwnerSeat ||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.BindingId == f.TriggerId && e.OwnerSeat == f.OwnerSeat && e.SkillInstanceId == f.SkillInstanceId && e.Window == c.Window) != 1) return false;
        if (f.SkillId == AnniversaryYangBiaoComposition.Juee)
            return f.TriggerId == "ending-random-uses" && c.Window == SkillProgramTriggerWindow.TurnEnding &&
                _resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) is TurnEndingBoundaryFrame w && w.TurnNumber == _turnNumber && w.OwnerSeat == _currentSeat && c.SourceSeat == w.OwnerSeat && c.TargetSeat == w.OwnerSeat &&
                w.ItemIndex >= 0 && w.ItemIndex < w.Items.Count && w.Items[w.ItemIndex] is { Kind: TurnEndingBoundaryItemKind.Program, Candidate: { } candidate } && MountObserverCandidateMatches(f, candidate) && candidate.OccurrenceIndex == c.OccurrenceIndex;
        if (_resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) is not ProgramLifecycleTriggerWindowFrame parent || parent.Window != c.Window ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]) || parent.Candidates[parent.CandidateIndex].OccurrenceIndex != c.OccurrenceIndex) return false;
        if (f.SkillId == AnniversaryYangBiaoComposition.Zhaohan)
            return (f.TriggerId, c.Window) is ("extra-normal-draw", SkillProgramTriggerWindow.DrawPhaseStarting) or ("settle-extra-draw", SkillProgramTriggerWindow.AfterNormalDraw) or ("settle-phase-draw", SkillProgramTriggerWindow.DrawPhaseEnded) && parent.OwnerSeat == f.OwnerSeat && c.SourceSeat == f.OwnerSeat && c.TargetSeat == f.OwnerSeat && _currentSeat == f.OwnerSeat;
        return f.SkillId == AnniversaryYangBiaoComposition.Jinjie && f.TriggerId == "dying-support" && c.Window == SkillProgramTriggerWindow.DyingEntering &&
            c.TargetSeat == parent.OwnerSeat && parent.Continuation == ProgramLifecycleContinuation.ResumeDyingEntry &&
            _resolutionStack.SingleOrDefault(x => x.Id == parent.ResumeDyingFrameId) is DyingFrame dying && dying.VictimSeat == parent.OwnerSeat &&
            ProgramEventHistory<PlayerDyingEvent>().Count(e => e.ResolutionId == dying.Id && e.VictimSeat == dying.VictimSeat && e.KillerSeat == dying.KillerSeat) == 1;
    }
    private bool YangBiaoPaymentProof(ProgramSkillFrame f)
    {
        var r = f.AnniversaryYangBiao!;
        if (!r.Paid) return r.From.Count == 0 && !ProgramEventHistory<AnniversaryYangBiaoPaymentIssuedEvent>().Any(e => e.FrameId == f.Id);
        if (r.Branch == "gift" && !IsValidPlayerSeat(r.Target)) return false;
        if (r.Cards.Count != r.Amount || r.From.Count != r.Amount || r.From.Any(from => from.OwnerSeat != f.OwnerSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            r.Op == SkillProgramEffectOp.ResolveAnniversaryYangBiaoZhaohan && r.From.Any(from => from != CardLocation.Hand(f.OwnerSeat)) ||
            ProgramEventHistory<AnniversaryYangBiaoPaymentIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var paid] ||
            paid != new AnniversaryYangBiaoPaymentIssuedEvent(f.Id, r.Branch, r.Target, r.Amount, r.Before)) return false;
        for (var i = 0; i < r.Cards.Count; i++)
        {
            var id = r.Cards[i]; var from = r.From[i]; var requestedTo = r.Branch == "gift" ? CardLocation.Hand(r.Target) : CardLocation.DiscardPile;
            var rows = _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == id && m.From == from &&
                (r.Branch == "gift" ? m.Reason.Value == YangBiaoGiftReason && (m.To == requestedTo || IsHappyWeiYanRedirectedGain(m, requestedTo)) :
                    m.Reason.Value == YangBiaoDiscardReason && (m.To == CardLocation.DiscardPile || IsRedirectedPaidDiscardMovement(m, from, new(YangBiaoDiscardReason))))).ToArray();
            if (rows.Length == 1) continue;
            if (rows.Length != 0) return false;
            if (r.Branch == "gift" && f.HappyWeiYanRetainedGains?.Any(retained => IsHappyWeiYanRetainedGainSettlement(retained, f.Id, id, requestedTo, r.Before)) == true) continue;
            // The cost is issued immediately before its atomic physical movement.
            if (r.Stage == AnniversaryYangBiaoStage.CostChildren && r.Before == r.After && _cardZones.GetLocation(id) == from) continue;
            return false;
        }
        return true;
    }
    private bool ValidYangBiao(ProgramSkillFrame f)
    {
        if (f.AnniversaryYangBiao is not { } r || !YangBiaoOriginal(f) || r.Source != YangBiaoSource(f) || r.Hash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            r.ParentId != f.WindowContext!.ParentFrameId || r.Turn != _turnNumber || r.Round != _roundNumber || !Enum.IsDefined(r.Stage) || f.InstructionIndex < 1 ||
            f.InstructionIndex > (r.Op == SkillProgramEffectOp.ResolveAnniversaryYangBiaoJinjie && r.Stage == AnniversaryYangBiaoStage.Completed ? 3 : 1) ||
            f.SelectedCardIds.Count != 0 || r.Cards.Distinct().Count() != r.Cards.Count || r.Cards.Count > r.Amount || r.Amount is < 0 or > 3 ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<AnniversaryYangBiaoStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] ||
            start != new AnniversaryYangBiaoStartedEvent(f.Id, r.Source, r.Hash, r.Op, r.ParentId, r.Turn, r.Round, r.DyingId, r.ArmId)) return false;
        if (r.Op == SkillProgramEffectOp.ResolveAnniversaryYangBiaoJuee) return ValidYangBiaoUseCursor(f);
        if (r.Plan is not null || r.RandomKind is not null || r.RandomKinds.Count != 0 || r.UseId != 0 || r.ActionId != 0 || r.Budget != 0 || r.Cursor != 0 || !YangBiaoPaymentProof(f)) return false;
        if (r.Op == SkillProgramEffectOp.ResolveAnniversaryYangBiaoZhaohan)
        {
            if (f.SkillId != AnniversaryYangBiaoComposition.Zhaohan || r.DyingId != 0 || r.Draw is not null || f.SelectedTargetSeats.Count != 0 || r.Amount > 2 ||
                ProgramEventHistory<AnniversaryYangBiaoDrawArmedEvent>().SingleOrDefault(e => e.FrameId == r.ArmId) is not { } arm || arm.Source.SkillId != f.SkillId || arm.Source.OwnerSeat != f.OwnerSeat || arm.Source.SkillInstanceId != f.SkillInstanceId || arm.Hash != r.Hash || arm.Turn != r.Turn || arm.DrawPhaseId != YangBiaoDrawPhaseId(f.WindowContext) || arm.Source.BindingId != "extra-normal-draw" ||
                ProgramEventHistory<AnniversaryYangBiaoStartedEvent>().Count(e => e.ArmId == r.ArmId) != 1) return false;
            if (r.Stage == AnniversaryYangBiaoStage.ZhaohanBranch) return !r.Paid && r.Branch == "" && r.Cards.Count == 0 && r.Target == -1;
            if (r.Branch is not ("gift" or "discard")) return false;
            if (r.Stage == AnniversaryYangBiaoStage.Recipient) return r.Branch == "gift" && r.Target == -1 && !r.Paid;
            return r.Stage is AnniversaryYangBiaoStage.Materials or AnniversaryYangBiaoStage.CostChildren or AnniversaryYangBiaoStage.Completed &&
                (r.Branch != "gift" || IsValidPlayerSeat(r.Target)) && (r.Stage == AnniversaryYangBiaoStage.Materials ? !r.Paid : r.Paid);
        }
        if (r.Op != SkillProgramEffectOp.ResolveAnniversaryYangBiaoJinjie || f.SkillId != AnniversaryYangBiaoComposition.Jinjie || r.ArmId != 0 || !IsValidPlayerSeat(r.Target) ||
            f.WindowContext.TargetSeat != r.Target || _resolutionStack.SingleOrDefault(x => x.Id == r.ParentId) is not ProgramLifecycleTriggerWindowFrame entry || entry.ResumeDyingFrameId != r.DyingId ||
            _resolutionStack.SingleOrDefault(x => x.Id == r.DyingId) is not DyingFrame dying || dying.VictimSeat != r.Target) return false;
        if (r.Draw is { } draw && (draw.RecipientSeat != r.Target || draw.RequestedCount != r.Amount || draw.SequenceBefore < 0 || draw.SequenceAfter < draw.SequenceBefore || draw.SequenceAfter > _movementSequence ||
            ProgramEventHistory<AnniversaryYangBiaoDrawReturnedEvent>().Count(e => e == new AnniversaryYangBiaoDrawReturnedEvent(f.Id, r.Target, r.Amount, draw.ActualDrawIds.Count, draw.SequenceBefore, draw.SequenceAfter)) != 1)) return false;
        if (r.Stage == AnniversaryYangBiaoStage.Completed)
        {
            if (!f.SelectedTargetSeats.SequenceEqual([r.Target]) || f.ChoiceBindings.Where(b => b.Name == "jinjie-pay").ToArray() is not [var choice] || choice.ChooserSeat != f.OwnerSeat || choice.OptionId is not ("paid" or "decline")) return false;
            return ProgramEventHistory<AnniversaryYangBiaoRescueSelectedEvent>().Count(e => e == new AnniversaryYangBiaoRescueSelectedEvent(f.Id, r.DyingId, r.Target, r.Amount, choice.OptionId == "paid")) == 1 && (choice.OptionId != "paid" || r.Paid);
        }
        if (f.SelectedTargetSeats.Count != 0 || r.Stage is not (AnniversaryYangBiaoStage.Amount or AnniversaryYangBiaoStage.Drawing or AnniversaryYangBiaoStage.DrawChildren or AnniversaryYangBiaoStage.RescuePay or AnniversaryYangBiaoStage.Materials or AnniversaryYangBiaoStage.CostChildren)) return false;
        if (r.Stage == AnniversaryYangBiaoStage.Amount) return r.Amount == 0 && r.Draw is null && !r.Paid;
        if (r.Stage == AnniversaryYangBiaoStage.Drawing) return r.Amount > 0 && r.Draw is null && !r.Paid;
        return (r.Amount == 0 ? r.Draw is null : r.Draw is not null) && (r.Stage == AnniversaryYangBiaoStage.CostChildren ? r.Paid : !r.Paid);
    }
    private bool ValidYangBiaoUseCursor(ProgramSkillFrame f)
    {
        var r = f.AnniversaryYangBiao!;
        if (f.SkillId != AnniversaryYangBiaoComposition.Juee || r.DyingId != 0 || r.ArmId != 0 || r.Cards.Count != 0 || r.From.Count != 0 || r.Draw is not null || r.Paid || f.SelectedTargetSeats.Count != 0 || r.Cursor < 0 || r.Cursor > r.Budget) return false;
        if (r.Stage == AnniversaryYangBiaoStage.Recipient) return r.Target == -1 && f.WindowContext!.SourceSeat == f.OwnerSeat && r.Budget == 0;
        if (!IsValidPlayerSeat(r.Target) || r.Target == f.OwnerSeat || f.WindowContext!.SourceSeat != f.OwnerSeat && r.Target != f.WindowContext.SourceSeat) return false;
        if (r.Stage == AnniversaryYangBiaoStage.FreezeBudget) return r.Budget == 0 && r.UseId == 0 && r.RandomKind is null;
        if (ProgramEventHistory<AnniversaryYangBiaoBudgetEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var budget] || budget != new AnniversaryYangBiaoBudgetEvent(f.Id, r.Target, r.DiscardCount, r.Maximum, r.Budget) || r.DiscardCount < 0 || r.Maximum < 0 || r.Budget != Math.Min(r.DiscardCount, r.Maximum)) return false;
        var issued = ProgramEventHistory<AnniversaryYangBiaoNativeIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray(); var returned = ProgramEventHistory<AnniversaryYangBiaoNativeReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        for (var i = 0; i < r.Cursor; i++) if (issued.Where(e => e.Ordinal == i).ToArray() is not [var prior] || prior.Target != r.Target ||
            returned.Count(e => e.Ordinal == i && e.UseId == prior.UseId) != 1 || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == prior.UseId && e.CardId == 0 && e.CardKind == prior.Kind) != 1) return false;
        if (r.Stage is AnniversaryYangBiaoStage.NextUse or AnniversaryYangBiaoStage.Completed) return r.Plan is null && r.UseId == 0 && r.ActionId == 0 && r.RandomKind is null && issued.Length == r.Cursor && returned.Length == r.Cursor;
        if (r.RandomKind is not (CardKind.Slash or CardKind.Dismantlement or CardKind.FiveGrains) || r.RandomKinds.Count == 0 || r.RandomKinds.Distinct().Count() != r.RandomKinds.Count || r.RandomKinds.Any(k => k is not (CardKind.Slash or CardKind.Dismantlement or CardKind.FiveGrains)) ||
            ProgramEventHistory<AnniversaryYangBiaoRandomCardEvent>().Count(e => e.FrameId == f.Id && e.Ordinal == r.Cursor && e.Kind == r.RandomKind) != 1) return false;
        var random = new DeterministicRandom(unchecked((int)r.RandomBefore)); if (r.RandomKinds[random.Next(r.RandomKinds.Count)] != r.RandomKind || random.State != r.RandomAfter) return false;
        if (r.Stage == AnniversaryYangBiaoStage.UsePlan) return r.Plan is null && r.UseId == 0 && issued.Length == r.Cursor && returned.Length == r.Cursor;
        if (r.Plan is not { } p || p.Kind != r.RandomKind || !p.Targets.SequenceEqual([r.Target])) return false;
        if (r.Stage == AnniversaryYangBiaoStage.IssuingUse) return r.UseId == 0 && r.ActionId == 0 && issued.Length == r.Cursor;
        if (r.Stage is not (AnniversaryYangBiaoStage.NativeUse or AnniversaryYangBiaoStage.ReturnedUse) || r.UseId <= f.Id || r.ActionId <= 0 ||
            issued.Count(e => e == new AnniversaryYangBiaoNativeIssuedEvent(f.Id, r.Cursor, r.UseId, r.ActionId, p.Kind, r.Target)) != 1 || issued.Length != r.Cursor + 1) return false;
        return r.Stage == AnniversaryYangBiaoStage.NativeUse ? returned.Length == r.Cursor : r.HadAttack == (p.Kind == CardKind.Slash) && returned.Length == r.Cursor + 1 &&
            returned.Count(e => e == new AnniversaryYangBiaoNativeReturnedEvent(f.Id, r.Cursor, r.UseId, r.HadAttack)) == 1 && ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == r.UseId && e.CardId == 0 && e.CardKind == p.Kind) == 1;
    }
    private bool HasExactAnniversaryYangBiaoParticipantSelection(ProgramSkillFrame f) => f.AnniversaryYangBiao is { Op: SkillProgramEffectOp.ResolveAnniversaryYangBiaoJinjie, Stage: AnniversaryYangBiaoStage.Completed } && ValidYangBiao(f);
    private bool HasAnniversaryYangBiaoPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger } p && _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryYangBiao: not null } f &&
        f.Step == ResolutionFrameStep.AwaitingResponse && p.PlayerSeat == f.OwnerSeat && ValidYangBiao(f) && AssistedChoicesEqual(p.Choices, YangBiaoChoices(f));
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 43700)] private void AssertYangBiao(ProgramSkillFrame f)
    { if (f.AnniversaryYangBiao is not null && !ValidYangBiao(f)) throw new InvalidOperationException($"Yang Biao lost original candidate, actual native cost/draw or whole Use ordinal ({f.Id}/{f.SkillId}/{f.AnniversaryYangBiao.Stage}/{f.InstructionIndex})."); }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 43700)] private bool YangBiaoMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.AnniversaryYangBiao is { Stage: AnniversaryYangBiaoStage.CostChildren or AnniversaryYangBiaoStage.DrawChildren } r && ValidYangBiao(f) && e?.Op == r.Op && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 43700)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 43700)]
    private bool YangBiaoRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame child) => f.AnniversaryYangBiao is { Stage: AnniversaryYangBiaoStage.CostChildren, Paid: true } r && ValidYangBiao(f) &&
        child.ParentFrameId == f.Id && child.Return == new RecoveryReplacementReturn(PostEventContinuation.Program, f.Id) && child.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && child.Attempt.Completion.MoveReason == new CardMoveReason(YangBiaoDiscardReason) &&
        child.Attempt.SourceSeat == f.OwnerSeat && child.Attempt.TargetSeat == f.OwnerSeat && child.Attempt.Amount == 1 &&
        _cardMovements.Any(m => m.Sequence > r.Before && m.Sequence <= r.After && m.From == CardLocation.Equipment(f.OwnerSeat) && m.Reason.Value == YangBiaoDiscardReason && r.Cards.Contains(m.CardId) && GetAdvancedCard(m.CardId).Kind == CardKind.SilverLion);
    private bool YangBiaoFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.AnniversaryYangBiao is not { } r || !ValidYangBiao(f)) return false;
        if (child is NativeDrawAttemptFrame draw) return r.Stage == AnniversaryYangBiaoStage.Drawing && draw.Context.ParentFrameId == f.Id && draw.Context.RecipientSeat == r.Target && draw.Context.RequestedCount == r.Amount;
        if (child is CardsMovedTriggerWindowFrame moved) return r.Stage is AnniversaryYangBiaoStage.CostChildren or AnniversaryYangBiaoStage.DrawChildren && f.PendingMovementContinuation is not null && moved.ResumeProgramFrameId == f.Id && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
            moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After && (m.Reason == CardMoveReasons.Reshuffle || m.Reason.Value == (r.Stage == AnniversaryYangBiaoStage.DrawChildren ? YangBiaoDrawReason : r.Branch == "gift" ? YangBiaoGiftReason : YangBiaoDiscardReason)));
        if (child is RecoveryReplacementFrame recovery) return YangBiaoRecoveryParent(f, recovery);
        return child is HpChangedTriggerWindowFrame hp && r.Stage == AnniversaryYangBiaoStage.CostChildren && hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.Program && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == f.OwnerSeat && hp.Change.Amount == 1;
    }
    private ProgramSkillFrame? YangBiaoObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.YangBiaoFirstChild(f, c), static (e, i) => e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 43700)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 43700)] private bool YangBiaoDamageObserver(long id) => YangBiaoObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 43700)] private bool YangBiaoBeforeDamageObserver(long id) => YangBiaoObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 43700)] private bool YangBiaoDyingObserver() => ActiveDying is { } dying && YangBiaoObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 43700)] private bool AdvanceYangBiaoSubtree() => _pendingDecision is null && YangBiaoObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 43700)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 43700)] private bool KeepPaidYangBiao(ProgramSkillFrame f) => f.AnniversaryYangBiao is { } r && (r.Paid || r.Stage is AnniversaryYangBiaoStage.Drawing or AnniversaryYangBiaoStage.DrawChildren or AnniversaryYangBiaoStage.NativeUse or AnniversaryYangBiaoStage.ReturnedUse) && ValidYangBiao(f);
    // Hide only the original pending Dying token during a proved native draw/cost
    // subtree. A genuine descendant Dying remains visible and resolves normally.
    private bool IsOriginalDyingSuspendedByAnniversaryYangBiao(DyingFrame dying)
    {
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>())
        {
            if (f.AnniversaryYangBiao is not { Op: SkillProgramEffectOp.ResolveAnniversaryYangBiaoJinjie, Stage: AnniversaryYangBiaoStage.Drawing or AnniversaryYangBiaoStage.DrawChildren or AnniversaryYangBiaoStage.CostChildren } r || r.DyingId != dying.Id || !ValidYangBiao(f)) continue;
            var index = _resolutionStack.FindIndex(x => x.Id == f.Id); if (index < 2 || _resolutionStack[index - 1].Id != r.ParentId || _resolutionStack[index - 2].Id != dying.Id) return false;
            if (index + 1 == _resolutionStack.Count) return true;
            if (!YangBiaoFirstChild(f, _resolutionStack[index + 1])) return false;
            for (var i = index + 2; i < _resolutionStack.Count; i++)
                if (!NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) &&
                    !DyingSuitsStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i])) return false;
            return true;
        }
        return false;
    }
}
