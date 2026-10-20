namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 29401)]
    private void BindMouLiuChengIssuedCard(IGameEvent fact)
    {
        if (fact is CardUseDeclaredEvent e && LifecycleCardUse(e.ResolutionId) is { } use)
            BindMouLiuChengNativeUse(use);
    }
    private bool MouLiuChengRootMatches(ProgramSkillFrame f)
    {
        if (f.MouLiuChengCompletion is not { } r || !MouLiuChengComposition.IsSkill(f.SkillId) || f.TriggerId != "after-use" || f.ActivationId != "after-use" ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            r.Claim.Source != MouLiuChengSource(f) || r.Claim.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveMouLiuChengCompletion ||
            f.WindowContext is not { } c || c.ParentFrameId != r.WindowId || c.Window != SkillProgramTriggerWindow.CardUseCompleted ||
            _resolutionStack.SingleOrDefault(x => x.Id == r.WindowId) is not ProgramCardTriggerWindowFrame w ||
            w.ParentFrameId != r.Claim.NativeParentId || !ValidMouLiuChengClaim(r.Claim, w.Action) ||
            _resolutionStack.SingleOrDefault(x => x.Id == w.ParentFrameId)?.MouLiuChengClaims?.Contains(r.Claim) != true ||
            !MouLiuChengCandidateMatches(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId,
                f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), c, w)) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1].Id == w.Id &&
            ProgramEventHistory<MouLiuChengCompletionStartedEvent>().Count(e => e == new MouLiuChengCompletionStartedEvent(f.Id, r.Claim, w.Id, r.MarkBefore, r.MarkAfter)) == 1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 29401)]
    private void AssertMouLiuChengCompletion(ProgramSkillFrame f)
    {
        if (f.MouLiuChengCompletion is not { } r) return;
        if (!MouLiuChengRootMatches(f) || !r.Paid || !Enum.IsDefined(r.Stage) || r.MarkBefore < 2 ||
            r.MarkAfter != r.MarkBefore - 2 || r.PaidSequence > _movementSequence ||
            r.OutputKind != (f.SkillId == MouLiuChengComposition.Lueying ? CardKind.Dismantlement : CardKind.Slash))
            throw new InvalidOperationException("Liu Cheng lost its exact paid shared marks and completed native Use parent.");
        if (r.Plan is { } plan && (plan.Kind != r.OutputKind || plan.Targets is not System.Collections.IList { IsReadOnly: true } ||
            ProgramEventHistory<MouLiuChengPlanAcceptedEvent>().Count(e => e == new MouLiuChengPlanAcceptedEvent(f.Id,
                plan.Kind, plan.ActionKind, plan.TargetCardId, plan.RequiredKind, plan.Adjusted, plan.Targets.Count)) != 1 ||
            !ProgramEventHistory<MouLiuChengTargetAcceptedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index)
                .Select((e, i) => e.Index == i && i < plan.Targets.Count && plan.Targets[i] == e.TargetSeat).All(v => v) ||
            ProgramEventHistory<MouLiuChengTargetAcceptedEvent>().Count(e => e.FrameId == f.Id) != plan.Targets.Count))
            throw new InvalidOperationException("Liu Cheng changed its exact native plan or ordered target receipt.");
        if (r.Stage == MouLiuChengStage.NativeUse && (LifecycleCardUse(r.UseId) is not { } use || !IsIssuedMouLiuChengUse(use)))
            throw new InvalidOperationException("Liu Cheng's native child lost its exact separate zero-material issuance.");
        if (r.Returned && (r.UseId <= 0 || LifecycleCardUse(r.UseId) is not null ||
            ProgramEventHistory<MouLiuChengNativeReturnedEvent>().Count(e => e == new MouLiuChengNativeReturnedEvent(f.Id, r.UseId, r.ActionId)) != 1))
            throw new InvalidOperationException("Liu Cheng cannot claim an unreturned or repeated native Use.");
        if (r.DrawOutcome is { } o && (o.RequestedCount != 1 || o.RecipientSeat != f.OwnerSeat || f.NativeDraw is not null ||
            ProgramEventHistory<MouLiuChengDrawReturnedEvent>().Count(e => e == new MouLiuChengDrawReturnedEvent(f.Id, 1,
                o.ActualDrawIds.Count, o.ReplacementGainedIds.Count, o.SequenceBefore, o.SequenceAfter, o.AttemptFrameId)) != 1) ||
            r.Stage != MouLiuChengStage.Drawing && r.DrawOutcome is null)
            throw new InvalidOperationException("Liu Cheng lost its once-returned actual/replacement requested Draw1 invoice.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29401)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29401)]
    private bool PermitPaidMouLiuCheng(ProgramSkillFrame f)
    { if (f.MouLiuChengCompletion is not { Paid: true }) return false; AssertMouLiuChengCompletion(f); return true; }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 340)]
    private bool IsMouLiuChengMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.MouLiuChengCompletion is { Paid: true, Stage: MouLiuChengStage.NativeChildren or MouLiuChengStage.DrawChildren } &&
        e?.Op == SkillProgramEffectOp.ResolveMouLiuChengCompletion && p == f.PendingMovementContinuation &&
        p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool MouLiuChengDrawMovement(ProgramSkillFrame f, CardMovementRecord m) =>
        f.MouLiuChengCompletion is { Stage: MouLiuChengStage.DrawChildren, DrawOutcome: { } o } && _cardMovements.Contains(m) &&
        m.Sequence > o.SequenceBefore && m.Sequence <= o.SequenceAfter &&
        (o.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == MouLiuChengDrawReason ||
         o.ReplacementGainedIds.Contains(m.CardId) && m.To == CardLocation.Hand(f.OwnerSeat) ||
         m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle);
    private bool IsMouLiuChengSuspendedCompletedAttack(CardUseFrame original, long? requiredWindowId = null)
    {
        // The completed mother owns its exact suspended interval through requested
        // Draw1, gain children and the separate native followup. This predicate
        // never queries an active-token getter, preventing recursive cursors.
        if (original.Action is not { } action || original.CardAttack is null) return false;
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>())
        {
            if (f.MouLiuChengCompletion is not { Paid: true } r || requiredWindowId is { } exactWindow && r.WindowId != exactWindow ||
                r.Stage is not (MouLiuChengStage.UseChoice or MouLiuChengStage.NativeIssuing or
                    MouLiuChengStage.NativeUse or MouLiuChengStage.NativeChildren or MouLiuChengStage.Drawing or MouLiuChengStage.DrawChildren) ||
                !MouLiuChengComposition.IsSkill(f.SkillId) || f.TriggerId != "after-use" || f.ActivationId != "after-use" ||
                r.Claim.Source != MouLiuChengSource(f) || r.Claim.GameplayHash != f.GameplayHash ||
                _resolutionStack.SingleOrDefault(x => x.Id == r.WindowId) is not ProgramCardTriggerWindowFrame mother ||
                mother.ParentFrameId != original.Id || mother.AttackOwnerFrameId != original.Id ||
                r.Claim.NativeParentId != original.Id || r.Claim.ActionId != mother.Action.ActionId ||
                original.MouLiuChengClaims?.Contains(r.Claim) != true || !ValidMouLiuChengClaim(r.Claim, mother.Action) ||
                f.WindowContext?.ParentFrameId != mother.Id ||
                ProgramEventHistory<MouLiuChengCompletionStartedEvent>().Count(e => e == new MouLiuChengCompletionStartedEvent(f.Id, r.Claim, mother.Id, r.MarkBefore, r.MarkAfter)) != 1) continue;
            var response = mother.CompletedResponseReturn is { Kind: ProgramCompletedResponseKind.Dodge, IsCommitted: false };
            if (response ? mother.Action.ParentActionId != action.ActionId :
                mother.Continuation != ProgramCardContinuation.CompletedSlash || mother.Action.ActionId != action.ActionId ||
                original.Step != ResolutionFrameStep.Completed) continue;
            var fi = _resolutionStack.FindIndex(x => x.Id == f.Id);
            if (fi < 1 || _resolutionStack[fi - 1].Id != mother.Id ||
                _resolutionStack.FindIndex(x => x.Id == original.Id) >= fi - 1) continue;
            if (r.Stage == MouLiuChengStage.NativeUse &&
                (LifecycleCardUse(r.UseId) is not { MouLiuChengOrigin: { } origin, Action: { } childAction } child ||
                 _resolutionStack.ElementAtOrDefault(fi + 1)?.Id != child.Id || origin.ProgramFrameId != f.Id ||
                 origin.UseId != child.Id || origin.ActionId != childAction.ActionId || origin.Source != r.Claim.Source ||
                 origin.GameplayHash != f.GameplayHash || childAction.ParentActionId != action.ActionId ||
                 childAction.PhysicalCards.Count != 0 ||
                 ProgramEventHistory<MouLiuChengNativeIssuedEvent>().Count(e => e.FrameId == f.Id && e.UseId == child.Id &&
                     e.ActionId == childAction.ActionId && e.ActorSeat == f.OwnerSeat && e.Kind == child.CardKind) != 1)) continue;
            if (r.Stage == MouLiuChengStage.NativeChildren &&
                (!r.Returned || r.UseId > 0 && (LifecycleCardUse(r.UseId) is not null ||
                    ProgramEventHistory<MouLiuChengNativeReturnedEvent>().Count(e => e.FrameId == f.Id && e.UseId == r.UseId && e.ActionId == r.ActionId) != 1))) continue;
            return true;
        }
        return false;
    }
    private void RestoreMouLiuChengParentAttack(ProgramSkillFrame f)
    {

        if (f.MouLiuChengCompletion is not { Paid: true, Returned: true, UseId: > 0 } r ||
            _resolutionStack.SingleOrDefault(x => x.Id == r.WindowId) is not ProgramCardTriggerWindowFrame w ||
            w.AttackOwnerFrameId is not { } originalId) return;
        var response = w.CompletedResponseReturn is { Kind: ProgramCompletedResponseKind.Dodge, IsCommitted: false };
        if (!response && w.Continuation != ProgramCardContinuation.CompletedSlash) return;
        if (!MouLiuChengRootMatches(f) || LifecycleCardUse(r.UseId) is not null ||
            ProgramEventHistory<MouLiuChengNativeReturnedEvent>().Count(e => e == new MouLiuChengNativeReturnedEvent(f.Id, r.UseId, r.ActionId)) != 1 ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == r.UseId) != 1 ||
            w.ParentFrameId != originalId || LifecycleCardUse(originalId) is not { Action: { } a, CardAttack: not null } original ||
            (response ? w.Action.ParentActionId != a.ActionId : w.Action.ActionId != a.ActionId || original.Step != ResolutionFrameStep.Completed) ||
            ActiveCardAttack is { } active && active.ResolutionId != originalId)
            throw new InvalidOperationException("MouLiuCheng cannot restore another attack or return before its exact native child has retired.");
        if (!SameAttackOwner(ActiveCardAttack, new CardAttackHandle(this, originalId)))
        {
            ActiveCardAttack = new(this, originalId);
            AdvanceEventRulesAndQueueFact(new MouLiuChengParentAttackRestoredEvent(f.Id, r.UseId, originalId, a.ActionId));
        }
    }
    private IReadOnlyList<int>? MouLiuChengNativeAncestorProcessing(long useId)
    {
        if (LifecycleCardUse(useId) is not { MouLiuChengOrigin: { } o } use || !IsIssuedMouLiuChengUse(use) ||
            _resolutionStack.SingleOrDefault(x => x.Id == o.ProgramFrameId) is not ProgramSkillFrame { MouLiuChengCompletion: { Stage: MouLiuChengStage.NativeUse } r } f ||
            r.UseId != useId || !MouLiuChengRootMatches(f)) return null;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index < 1 || _resolutionStack.ElementAtOrDefault(index + 1)?.Id != useId) return null;
        // Only actual materials of the already accepted ancestor actions may
        // remain in Processing. Empty-material child representations add no
        // entities, and unrelated Processing cards cannot borrow this return.
        var costs = _resolutionStack.Take(index).SelectMany(frame => frame switch
        {
            CardUseFrame { Action: { } a } => a.PhysicalCards,
            ProgramCardTriggerWindowFrame window => window.Action.PhysicalCards,
            _ => Array.Empty<CardActionCost>()
        }).Distinct().ToArray();
        var result = new List<int>();
        foreach (var cost in costs)
        {
            if (_cardZones.GetLocation(cost.CardId) != CardLocation.Processing) continue;
            var entry = _cardMovements.LastOrDefault(m => m.CardId == cost.CardId);
            if (entry is null || entry.To != CardLocation.Processing || entry.CardKind != cost.CardKind ||
                entry.Sequence > r.PaidSequence || entry.From != cost.From &&
                    !(entry.Reason.Value == "conversion.declaration.pay" && entry.From == CardLocation.Hand(o.ActorSeat))) return null;
            if (!result.Contains(cost.CardId)) result.Add(cost.CardId);
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private bool IsMouLiuChengSuspendedCounterspell(NullificationWindowFrame window)
    {
        // A true counterspell completion can itself issue MouLiuCheng's native trick.
        // Keep the old response cursor on its exact frame while the separate
        // child gets the active Nullification window; do not hide any sibling.
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>())
        {
            if (f.MouLiuChengCompletion is not { Paid: true, Stage: MouLiuChengStage.NativeUse } r ||
                _resolutionStack.SingleOrDefault(x => x.Id == r.WindowId) is not ProgramCardTriggerWindowFrame mother ||
                mother.CompletedResponseReturn is not { Kind: ProgramCompletedResponseKind.Nullification, IsCommitted: false } ||
                mother.ParentFrameId != window.Id || r.Claim.NativeParentId != window.Id ||
                r.Claim.ActionId != mother.Action.ActionId || window.MouLiuChengClaims?.Contains(r.Claim) != true ||
                r.Claim.Source != MouLiuChengSource(f) || r.Claim.GameplayHash != f.GameplayHash ||
                LifecycleCardUse(r.UseId) is not { MouLiuChengOrigin: { } origin, Action: { } a } child ||
                origin.ProgramFrameId != f.Id || origin.UseId != child.Id || origin.ActionId != a.ActionId ||
                origin.Source != r.Claim.Source || origin.GameplayHash != f.GameplayHash ||
                a.PhysicalCards.Count != 0 || a.ParentActionId != LifecycleCardUse(window.ParentFrameId)?.Action?.ActionId ||
                ProgramEventHistory<MouLiuChengCompletionStartedEvent>().Count(e => e.FrameId == f.Id && e.WindowId == mother.Id && e.Claim == r.Claim) != 1 ||
                ProgramEventHistory<MouLiuChengNativeIssuedEvent>().Count(e => e.FrameId == f.Id && e.UseId == child.Id &&
                    e.ActionId == a.ActionId && e.ActorSeat == f.OwnerSeat && e.Kind == child.CardKind) != 1) continue;
            return true;
        }
        return false;
    }
    private bool MouLiuChengFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouLiuChengCompletion is not { Paid: true } r || !MouLiuChengRootMatches(f)) return false;
        if (child is CardUseFrame use) return r.Stage == MouLiuChengStage.NativeUse && use.Id == r.UseId &&
            use.MouLiuChengOrigin?.ProgramFrameId == f.Id && IsIssuedMouLiuChengUse(use);
        if (r.Stage == MouLiuChengStage.Drawing && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
        if (child is CardsMovedTriggerWindowFrame moved)
            return f.PendingMovementContinuation is not null &&
                (r.Stage == MouLiuChengStage.NativeChildren && moved.ResumeProgramFrameId == f.Id && IsMouLiuChengNativeSettlementBatch(f, moved.Batch) ||
                 r.Stage == MouLiuChengStage.DrawChildren && ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
                 moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
                 moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => MouLiuChengDrawMovement(f, m)));
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state)
            return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id &&
                    e.Change.ParentFrameId == f.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id &&
            hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
    }
    private bool MouLiuChengNativeWindowEdge(int index)
    {
        if (_resolutionStack[index - 1] is CardUseFrame use && IsIssuedMouLiuChengUse(use) &&
            _resolutionStack[index] is ProgramCardTriggerWindowFrame w)
            return w.ParentFrameId == use.Id && w.Action.ActionId == use.Action!.ActionId && w.Action.ActorSeat == use.SourceSeat && w.Action.EffectiveKind == use.CardKind;
        if (_resolutionStack[index - 1] is ProgramCardTriggerWindowFrame parent &&
            LifecycleCardUse(parent.ParentFrameId) is { } native && IsIssuedMouLiuChengUse(native) &&
            parent.Action.ActionId == native.Action!.ActionId && _resolutionStack[index] is ProgramSkillFrame child &&
            parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count)
        {
            var c = parent.Candidates[parent.CandidateIndex];
            return MountObserverCandidateMatches(child, ToSharedCandidate(c)) && child.WindowContext is { } nativeContext &&
                (MouLiuChengComposition.IsSkill(child.SkillId) ? MouLiuChengCandidateMatches(ToSharedCandidate(c), nativeContext, parent) : nativeContext == CreateCardActionProgramContext(parent, c));
        }
        return _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame lifecycle &&
            _resolutionStack[index] is ProgramSkillFrame f && lifecycle.CandidateIndex >= 0 && lifecycle.CandidateIndex < lifecycle.Candidates.Count &&
            MountObserverCandidateMatches(f, lifecycle.Candidates[lifecycle.CandidateIndex]) &&
            f.WindowContext is { } context && context.ParentFrameId == lifecycle.Id && context.Window == lifecycle.Window;
    }
    private bool MouLiuChengObserverSuffix(int index) => HasObserverSuffix(index, static (g, n) =>
        g.IsMovementObserverEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) ||
        g.MouLiuChengNativeWindowEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) ||
        g._resolutionStack[n - 1] is ProgramSkillFrame p && (g.MouLiuChengFirstChild(p, g._resolutionStack[n]) ||
            p.Zenrun is not null && g.ZenrunFirstChild(p, g._resolutionStack[n])) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) ||
        g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]),
        static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? MouLiuChengObserverRoot() => FindProgramObserverRoot(
        static (g, f, child) => g.MouLiuChengFirstChild(f, child), static (g, index) => g.MouLiuChengObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1320)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1430)]
    private bool HasMouLiuChengDamageObserver(long id) => MouLiuChengObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1460)]
    private bool HasMouLiuChengBeforeDamageObserver(long id) => MouLiuChengObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1290)]
    private bool IsMouLiuChengDying() => ActiveDying is { } dying && MouLiuChengObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1880)]
    private bool AllowsMouLiuChengNestedDamage(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MouLiuChengObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 490)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 480)]
    private bool IsMouLiuChengRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.MouLiuChengCompletion is null || recovery.ParentFrameId == f.Id) return false;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && MouLiuChengFirstChild(f, _resolutionStack[parent + 1]) &&
            MouLiuChengObserverSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29401)]
    private bool AdvanceMouLiuChengChildren()
    {
        if (_pendingDecision is not null) return false;
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame { MouLiuChengCompletion: { Stage: MouLiuChengStage.NativeChildren, Returned: true } } f)
        { ContinueMouLiuCheng(f.Id); AdvanceRulesAndPublishState(); return true; }
        return MouLiuChengObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    }
}
