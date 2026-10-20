namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool DiouRootMatches(ProgramSkillFrame f)
    {
        if (f.Diou is not { } r || f.SkillId != DiouSkill || f.TriggerId != DiouBinding || f.ActivationId != DiouBinding ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            r.Claim.Source != DiouSource(f) || r.Claim.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveDiouTanbanUse ||
            f.WindowContext is not { } c || c.ParentFrameId != r.WindowId || c.Window != SkillProgramTriggerWindow.CardUseCompleted ||
            _resolutionStack.SingleOrDefault(x => x.Id == r.WindowId) is not ProgramCardTriggerWindowFrame w ||
            w.ParentFrameId != r.Claim.NativeParentId || !ValidDiouClaim(r.Claim, w.Action) ||
            _resolutionStack.SingleOrDefault(x => x.Id == w.ParentFrameId)?.DiouClaims?.Contains(r.Claim) != true ||
            !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId,
                f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), c, w)) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1].Id == w.Id &&
            ProgramEventHistory<DiouStartedEvent>().Count(e => e == new DiouStartedEvent(f.Id, r.Claim, w.Id)) == 1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 29042)]
    private void AssertDiou(ProgramSkillFrame f)
    {
        if (f.Diou is not { } r) return;
        if (!DiouRootMatches(f) || !Enum.IsDefined(r.Stage))
            throw new InvalidOperationException("Diou lost its frozen original Tanban materials or completed native Use mother.");
        var shown = ProgramEventHistory<DiouShownEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (!r.Paid)
        {
            if (r.Stage != DiouStage.ShowChoice || r.ShownCardId != 0 || shown.Length != 0 || r.UseId != 0 ||
                r.Returned || r.DrawOutcome is not null || f.PendingMovementContinuation is not null)
                throw new InvalidOperationException("Unpaid Diou cannot contain a reveal, native Use or draw receipt.");
            return;
        }
        if (shown is not [var fact] || fact != new DiouShownEvent(f.Id, f.OwnerSeat, r.ShownCardId,
            r.ShownPrintedKind, r.OutputKind, r.ActualTurn, r.TurnOwner, r.ShowSequence, r.FirstShow, r.SameName) ||
            r.ShownCardId <= 0 || r.ShowSequence > _movementSequence ||
            r.SameName != (ProgramBasicCardName(r.OutputKind) == ProgramBasicCardName(r.Claim.EffectiveKind)))
            throw new InvalidOperationException("Paid Diou lost its exact original retained-Hand reveal and conditional reward.");
        if (r.Plan is { } plan && (plan.Kind != r.OutputKind || plan.Targets is not System.Collections.IList { IsReadOnly: true }))
            throw new InvalidOperationException("Diou cannot change its frozen same-name native plan or its ordered targets.");
        if (r.Plan is { } accepted &&
            (ProgramEventHistory<DiouPlanAcceptedEvent>().Count(e => e == new DiouPlanAcceptedEvent(f.Id,
                accepted.Kind, accepted.ActionKind, accepted.TargetCardId, accepted.RequiredKind, accepted.Adjusted, accepted.Targets.Count)) != 1 ||
             !ProgramEventHistory<DiouTargetAcceptedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index)
                .Select((e, i) => (e, i)).All(pair => pair.e.Index == pair.i && pair.i < accepted.Targets.Count &&
                    accepted.Targets[pair.i] == pair.e.TargetSeat) ||
             ProgramEventHistory<DiouTargetAcceptedEvent>().Count(e => e.FrameId == f.Id) != accepted.Targets.Count))
            throw new InvalidOperationException("Diou changed its exact issued native plan or a selected ordered target.");
        if (r.Stage == DiouStage.NativeUse && (LifecycleCardUse(r.UseId) is not { } use || !IsIssuedDiouUse(use)))
            throw new InvalidOperationException("Diou's native child lost its exact separate zero-material issuance.");
        if (r.Returned && r.UseId != 0 && (LifecycleCardUse(r.UseId) is not null ||
            ProgramEventHistory<DiouNativeReturnedEvent>().Count(e => e == new DiouNativeReturnedEvent(f.Id, r.UseId, r.ActionId)) != 1))
            throw new InvalidOperationException("Diou cannot claim an unreturned or repeated native Use.");
        if (r.Stage == DiouStage.DrawChildren && (r.DrawOutcome is not { } o || o.RequestedCount != 2 ||
            o.RecipientSeat != f.OwnerSeat || f.NativeDraw is not null ||
            ProgramEventHistory<DiouDrawReturnedEvent>().Count(e => e == new DiouDrawReturnedEvent(f.Id, 2,
                o.ActualDrawIds.Count, o.ReplacementGainedIds.Count, o.SequenceBefore, o.SequenceAfter, o.AttemptFrameId)) != 1))
            throw new InvalidOperationException("Diou lost its once-returned actual/replacement Draw2 invoice.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29042)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29042)]
    private bool PermitPaidDiou(ProgramSkillFrame f)
    {
        if (f.Diou is { Paid: true }) { AssertDiou(f); return true; }
        return f.Diou is null && f.InstructionIndex is 0 or 1 && f.TriggerId == DiouBinding && f.WindowContext is { } c &&
            IsIssuedDiouCandidate(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0, c.OccurrenceIndex), c);
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 330)]
    private bool IsDiouMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.Diou is { Paid: true, Stage: DiouStage.NativeChildren or DiouStage.DrawChildren } &&
        e?.Op == SkillProgramEffectOp.ResolveDiouTanbanUse && p == f.PendingMovementContinuation &&
        p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool DiouDrawMovement(ProgramSkillFrame f, CardMovementRecord m) =>
        f.Diou is { Stage: DiouStage.DrawChildren, DrawOutcome: { } o } && _cardMovements.Contains(m) &&
        m.Sequence > o.SequenceBefore && m.Sequence <= o.SequenceAfter &&
        (o.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == DiouDrawReason ||
         o.ReplacementGainedIds.Contains(m.CardId) && m.To == CardLocation.Hand(f.OwnerSeat) ||
         m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle);
    private bool IsDiouSuspendedCompletedAttack(CardUseFrame original, long? requiredWindowId = null)
    {
        // Do not query an active-token getter here. In particular an equipment
        // or recovery clone need not create any attack that could deactivate
        // this already completed mother, but its paid children still own the
        // entire interval before the exact native return.
        if (original.Action is not { } action || original.CardAttack is null) return false;
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>())
        {
            if (f.Diou is not { Paid: true } r || requiredWindowId is { } exactWindow && r.WindowId != exactWindow ||
                r.Stage is not (DiouStage.UseChoice or DiouStage.NativeIssuing or
                    DiouStage.NativeUse or DiouStage.NativeChildren or DiouStage.Drawing or DiouStage.DrawChildren) ||
                f.SkillId != DiouSkill || f.TriggerId != DiouBinding || f.ActivationId != DiouBinding ||
                r.Claim.Source != DiouSource(f) || r.Claim.GameplayHash != f.GameplayHash ||
                _resolutionStack.SingleOrDefault(x => x.Id == r.WindowId) is not ProgramCardTriggerWindowFrame mother ||
                mother.ParentFrameId != original.Id || mother.AttackOwnerFrameId != original.Id ||
                r.Claim.NativeParentId != original.Id || r.Claim.ActionId != mother.Action.ActionId ||
                original.DiouClaims?.Contains(r.Claim) != true || !ValidDiouClaim(r.Claim, mother.Action) ||
                f.WindowContext?.ParentFrameId != mother.Id ||
                ProgramEventHistory<DiouStartedEvent>().Count(e => e == new DiouStartedEvent(f.Id, r.Claim, mother.Id)) != 1 ||
                ProgramEventHistory<DiouShownEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
                    e.CardId == r.ShownCardId && e.OutputKind == r.OutputKind && e.MovementSequence == r.ShowSequence) != 1) continue;
            var response = mother.CompletedResponseReturn is { Kind: ProgramCompletedResponseKind.Dodge, IsCommitted: false };
            if (response ? mother.Action.ParentActionId != action.ActionId :
                mother.Continuation != ProgramCardContinuation.CompletedSlash || mother.Action.ActionId != action.ActionId ||
                original.Step != ResolutionFrameStep.Completed) continue;
            var fi = _resolutionStack.FindIndex(x => x.Id == f.Id);
            if (fi < 1 || _resolutionStack[fi - 1].Id != mother.Id ||
                _resolutionStack.FindIndex(x => x.Id == original.Id) >= fi - 1) continue;
            if (r.Stage == DiouStage.NativeUse &&
                (LifecycleCardUse(r.UseId) is not { DiouOrigin: { } origin, Action: { } childAction } child ||
                 _resolutionStack.ElementAtOrDefault(fi + 1)?.Id != child.Id || origin.ProgramFrameId != f.Id ||
                 origin.UseId != child.Id || origin.ActionId != childAction.ActionId || origin.Source != r.Claim.Source ||
                 origin.GameplayHash != f.GameplayHash || childAction.ParentActionId != action.ActionId ||
                 childAction.PhysicalCards.Count != 0 ||
                 ProgramEventHistory<DiouNativeIssuedEvent>().Count(e => e.FrameId == f.Id && e.UseId == child.Id &&
                     e.ActionId == childAction.ActionId && e.ActorSeat == f.OwnerSeat && e.Kind == child.CardKind && e.CarrierId == child.CardId) != 1)) continue;
            if (r.Stage is DiouStage.NativeChildren or DiouStage.Drawing or DiouStage.DrawChildren &&
                (!r.Returned || r.UseId > 0 && (LifecycleCardUse(r.UseId) is not null ||
                    ProgramEventHistory<DiouNativeReturnedEvent>().Count(e => e.FrameId == f.Id && e.UseId == r.UseId && e.ActionId == r.ActionId) != 1))) continue;
            return true;
        }
        return false;
    }
    private void RestoreDiouParentAttack(ProgramSkillFrame f)
    {
        RestoreDiouRescueAttack(f);
        if (f.Diou is not { Paid: true, Returned: true, UseId: > 0 } r ||
            _resolutionStack.SingleOrDefault(x => x.Id == r.WindowId) is not ProgramCardTriggerWindowFrame w ||
            w.AttackOwnerFrameId is not { } originalId) return;
        var response = w.CompletedResponseReturn is { Kind: ProgramCompletedResponseKind.Dodge, IsCommitted: false };
        if (!response && w.Continuation != ProgramCardContinuation.CompletedSlash) return;
        if (!DiouRootMatches(f) || LifecycleCardUse(r.UseId) is not null ||
            ProgramEventHistory<DiouNativeReturnedEvent>().Count(e => e == new DiouNativeReturnedEvent(f.Id, r.UseId, r.ActionId)) != 1 ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == r.UseId) != 1 ||
            w.ParentFrameId != originalId || LifecycleCardUse(originalId) is not { Action: { } a, CardAttack: not null } original ||
            (response ? w.Action.ParentActionId != a.ActionId : w.Action.ActionId != a.ActionId || original.Step != ResolutionFrameStep.Completed) ||
            ActiveCardAttack is { } active && active.ResolutionId != originalId)
            throw new InvalidOperationException("Diou cannot restore another attack or return before its exact native child has retired.");
        if (!SameAttackOwner(ActiveCardAttack, new CardAttackHandle(this, originalId)))
        {
            ActiveCardAttack = new(this, originalId);
            AdvanceEventRulesAndQueueFact(new DiouParentAttackRestoredEvent(f.Id, r.UseId, originalId, a.ActionId));
        }
    }
    private IReadOnlyList<int>? DiouNativeAncestorProcessing(long useId)
    {
        if (LifecycleCardUse(useId) is not { DiouOrigin: { } o } use || !IsIssuedDiouUse(use) ||
            _resolutionStack.SingleOrDefault(x => x.Id == o.ProgramFrameId) is not ProgramSkillFrame { Diou: { Stage: DiouStage.NativeUse } r } f ||
            r.UseId != useId || !DiouRootMatches(f)) return null;
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
                entry.Sequence > r.ShowSequence || entry.From != cost.From &&
                    !(entry.Reason.Value == "conversion.declaration.pay" && entry.From == CardLocation.Hand(o.ActorSeat))) return null;
            if (!result.Contains(cost.CardId)) result.Add(cost.CardId);
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private bool IsDiouSuspendedCounterspell(NullificationWindowFrame window)
    {
        // A true counterspell completion can itself issue Diou's native trick.
        // Keep the old response cursor on its exact frame while the separate
        // child gets the active Nullification window; do not hide any sibling.
        foreach (var f in _resolutionStack.OfType<ProgramSkillFrame>())
        {
            if (f.Diou is not { Paid: true, Stage: DiouStage.NativeUse } r ||
                _resolutionStack.SingleOrDefault(x => x.Id == r.WindowId) is not ProgramCardTriggerWindowFrame mother ||
                mother.CompletedResponseReturn is not { Kind: ProgramCompletedResponseKind.Nullification, IsCommitted: false } ||
                mother.ParentFrameId != window.Id || r.Claim.NativeParentId != window.Id ||
                r.Claim.ActionId != mother.Action.ActionId || window.DiouClaims?.Contains(r.Claim) != true ||
                r.Claim.Source != DiouSource(f) || r.Claim.GameplayHash != f.GameplayHash ||
                LifecycleCardUse(r.UseId) is not { DiouOrigin: { } origin, Action: { } a } child ||
                origin.ProgramFrameId != f.Id || origin.UseId != child.Id || origin.ActionId != a.ActionId ||
                origin.Source != r.Claim.Source || origin.GameplayHash != f.GameplayHash ||
                a.PhysicalCards.Count != 0 || a.ParentActionId != LifecycleCardUse(window.ParentFrameId)?.Action?.ActionId ||
                ProgramEventHistory<DiouStartedEvent>().Count(e => e.FrameId == f.Id && e.WindowId == mother.Id && e.Claim == r.Claim) != 1 ||
                ProgramEventHistory<DiouNativeIssuedEvent>().Count(e => e.FrameId == f.Id && e.UseId == child.Id &&
                    e.ActionId == a.ActionId && e.ActorSeat == f.OwnerSeat && e.Kind == child.CardKind && e.CarrierId == child.CardId) != 1) continue;
            return true;
        }
        return false;
    }
    private bool IsOriginalDyingSuspendedByDiou(DyingFrame dying)
        => _resolutionStack.OfType<ProgramSkillFrame>().Any(f => DiouSuspendsRescue(f, dying));
    private bool DiouSuspendsRescue(ProgramSkillFrame f, DyingFrame dying)
    {
        // This filter deliberately never queries ActiveDying or mature
        // observer helpers: they may themselves query this active-token list.
        // Only the dying token of the already completed genuine rescue is
        // suspended. A new dying victim inside Diou keeps the normal flow.
        if (f.Diou is not { Paid: true } r || r.Stage is not (DiouStage.UseChoice or DiouStage.NativeIssuing or
                DiouStage.NativeUse or DiouStage.NativeChildren or DiouStage.Drawing or DiouStage.DrawChildren) ||
            f.SkillId != DiouSkill || r.Claim.Source != DiouSource(f) || r.Claim.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            _resolutionStack.SingleOrDefault(x => x.Id == r.WindowId) is not ProgramCardTriggerWindowFrame mother ||
            mother.Continuation != ProgramCardContinuation.CompletedCard || mother.CompletedResponseReturn is not null ||
            mother.ParentFrameId != r.Claim.NativeParentId || mother.Action.ActionId != r.Claim.ActionId ||
            LifecycleCardUse(mother.ParentFrameId) is not { Step: ResolutionFrameStep.Completed, Action: { } action,
                DyingResponse: { } rescue } original || rescue.ResolutionId != dying.Id ||
            original.CardKind is not (CardKind.Peach or CardKind.Alcohol) ||
            action.EffectiveKind != original.CardKind || original.SourceSeat != action.ActorSeat ||
            action.ActorSeat != r.Claim.ActorSeat || !original.TargetSeats.SequenceEqual([dying.VictimSeat]) ||
            rescue.ResponderSeat != original.SourceSeat ||
            (original.CardKind == CardKind.Peach ? !rescue.UsedPeach : !rescue.UsedAlcohol || original.SourceSeat != dying.VictimSeat) ||
            original.DiouClaims?.Contains(r.Claim) != true ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == original.Id && e.CardKind == original.CardKind) != 1 ||
            ProgramEventHistory<DiouStartedEvent>().Count(e => e.FrameId == f.Id && e.WindowId == mother.Id && e.Claim == r.Claim) != 1 ||
            ProgramEventHistory<DiouShownEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
                e.CardId == r.ShownCardId && e.OutputKind == r.OutputKind && e.MovementSequence == r.ShowSequence) != 1) return false;
        var di = _resolutionStack.FindIndex(x => x.Id == dying.Id);
        var ui = _resolutionStack.FindIndex(x => x.Id == original.Id);
        var wi = _resolutionStack.FindIndex(x => x.Id == mother.Id);
        var fi = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (di < 0 || ui != di + 1 || wi != ui + 1 || fi != wi + 1 || f.WindowContext?.ParentFrameId != mother.Id) return false;
        if (r.Stage == DiouStage.NativeUse &&
            (LifecycleCardUse(r.UseId) is not { DiouOrigin: { } origin, Action: { } childAction } child ||
             _resolutionStack.ElementAtOrDefault(fi + 1)?.Id != child.Id || origin.ProgramFrameId != f.Id ||
             origin.UseId != child.Id || origin.ActionId != childAction.ActionId || origin.Source != r.Claim.Source ||
             origin.GameplayHash != f.GameplayHash || childAction.ParentActionId != action.ActionId ||
             childAction.PhysicalCards.Count != 0 ||
             ProgramEventHistory<DiouNativeIssuedEvent>().Count(e => e.FrameId == f.Id && e.UseId == child.Id &&
                 e.ActionId == childAction.ActionId && e.ActorSeat == f.OwnerSeat && e.Kind == child.CardKind && e.CarrierId == child.CardId) != 1)) return false;
        if (r.Stage is DiouStage.NativeChildren or DiouStage.Drawing or DiouStage.DrawChildren &&
            (!r.Returned || r.UseId > 0 && (LifecycleCardUse(r.UseId) is not null ||
                ProgramEventHistory<DiouNativeReturnedEvent>().Count(e => e.FrameId == f.Id && e.UseId == r.UseId && e.ActionId == r.ActionId) != 1))) return false;
        return true;
    }
    private ResolutionFrame? DiouRescueAttackOwner(ProgramSkillFrame f, out DyingFrame? dying)
    {
        dying = null;
        if (f.Diou is not { } r || LifecycleCardUse(r.Claim.NativeParentId)?.DyingResponse is not { } rescue ||
            _resolutionStack.SingleOrDefault(x => x.Id == rescue.ResolutionId) is not DyingFrame original ||
            !DiouSuspendsRescue(f, original)) return null;
        var di = _resolutionStack.FindIndex(x => x.Id == original.Id);
        ResolutionFrame? owner;
        DamageFrame? damage = null;
        if (original.Continuation == DyingContinuationKind.Damage)
        {
            if (di < 1 || _resolutionStack[di - 1] is not DamageFrame d || d.Id != original.ParentFrameId ||
                d.TargetSeat != original.VictimSeat || d.Amount <= 0) return null;
            damage = d;
            owner = _resolutionStack.Take(di - 1).SingleOrDefault(x => x.Id == d.ParentFrameId);
        }
        else if (original.Continuation == DyingContinuationKind.AttackHpLoss)
            owner = di > 0 && _resolutionStack[di - 1].Id == original.ParentFrameId ? _resolutionStack[di - 1] : null;
        else return null;
        var attack = owner switch
        {
            CardUseFrame use => use.CardAttack,
            JudgmentFrame judgment => judgment.CardAttack,
            ProgramSkillFrame program => program.CardAttack,
            _ => null
        };
        if (attack is null || attack.TargetSeat != original.VictimSeat ||
            !attack.IsSourceLess && original.KillerSeat != attack.SourceSeat ||
            damage is not null && (damage.SourceSeat != attack.SourceSeat || damage.Amount != attack.DamageAmount || !attack.DamageWasApplied) ||
            ProgramEventHistory<PlayerDyingEvent>().Count(e => e.ResolutionId == original.Id &&
                e.VictimSeat == original.VictimSeat && e.KillerSeat == original.KillerSeat) != 1) return null;
        dying = original;
        return owner;
    }
    private bool IsDiouSuspendedRescueAttack(ResolutionFrame owner) =>
        _resolutionStack.OfType<ProgramSkillFrame>().Any(f => DiouRescueAttackOwner(f, out _)?.Id == owner.Id);
    private void RestoreDiouRescueAttack(ProgramSkillFrame f)
    {
        if (f.Diou is not { Paid: true, Returned: true } r || DiouRescueAttackOwner(f, out var dying) is not { } owner) return;
        if (!DiouRootMatches(f) || r.UseId > 0 && (LifecycleCardUse(r.UseId) is not null ||
                ProgramEventHistory<DiouNativeReturnedEvent>().Count(e => e == new DiouNativeReturnedEvent(f.Id, r.UseId, r.ActionId)) != 1 ||
                ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == r.UseId) != 1) ||
            ActiveCardAttack is { } active && active.ResolutionId != owner.Id)
            throw new InvalidOperationException("Diou cannot restore another rescue attack or expose its dying cursor before the exact child returned.");
        // The active getter hides this exact ancestor throughout the paid
        // rescue child. Keep its native damage/duel/target cursor unchanged;
        // clearing Diou below makes the same owning state visible again.
        UpdateCardAttackState(owner.Id, state => state! with { Active = true });
        AdvanceEventRulesAndQueueFact(new DiouRescueAttackReturnedEvent(f.Id, r.UseId, dying!.Id, owner.Id,
            (owner as CardUseFrame)?.Action?.ActionId));
    }
    private bool DiouFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.Diou is not { Paid: true } r || !DiouRootMatches(f)) return false;
        if (child is CardUseFrame use) return r.Stage == DiouStage.NativeUse && use.Id == r.UseId &&
            use.DiouOrigin?.ProgramFrameId == f.Id && IsIssuedDiouUse(use);
        if (r.Stage == DiouStage.Drawing && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
        if (child is CardsMovedTriggerWindowFrame moved)
            return f.PendingMovementContinuation is not null &&
                (r.Stage == DiouStage.NativeChildren && moved.ResumeProgramFrameId == f.Id && IsDiouNativeSettlementBatch(f, moved.Batch) ||
                 r.Stage == DiouStage.DrawChildren && ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
                 moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
                 moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => DiouDrawMovement(f, m)));
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state)
            return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id &&
                    e.Change.ParentFrameId == f.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id &&
            hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
    }
    private bool DiouNativeWindowEdge(int index)
    {
        if (_resolutionStack[index - 1] is CardUseFrame use && IsIssuedDiouUse(use) &&
            _resolutionStack[index] is ProgramCardTriggerWindowFrame w)
            return w.ParentFrameId == use.Id && w.Action.ActionId == use.Action!.ActionId && w.Action.ActorSeat == use.SourceSeat && w.Action.EffectiveKind == use.CardKind;
        if (_resolutionStack[index - 1] is ProgramCardTriggerWindowFrame parent &&
            LifecycleCardUse(parent.ParentFrameId) is { } native && IsIssuedDiouUse(native) &&
            parent.Action.ActionId == native.Action!.ActionId && _resolutionStack[index] is ProgramSkillFrame child &&
            parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count)
        {
            var c = parent.Candidates[parent.CandidateIndex];
            return MountObserverCandidateMatches(child, ToSharedCandidate(c)) && child.WindowContext == CreateCardActionProgramContext(parent, c);
        }
        return _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame lifecycle &&
            _resolutionStack[index] is ProgramSkillFrame f && lifecycle.CandidateIndex >= 0 && lifecycle.CandidateIndex < lifecycle.Candidates.Count &&
            MountObserverCandidateMatches(f, lifecycle.Candidates[lifecycle.CandidateIndex]) &&
            f.WindowContext is { } context && context.ParentFrameId == lifecycle.Id && context.Window == lifecycle.Window;
    }
    private bool DiouObserverSuffix(int index) => HasObserverSuffix(index, static (g, n) =>
        g.IsMovementObserverEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) ||
        g.DiouNativeWindowEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) ||
        g._resolutionStack[n - 1] is ProgramSkillFrame p && (g.DiouFirstChild(p, g._resolutionStack[n]) ||
            p.Zenrun is not null && g.ZenrunFirstChild(p, g._resolutionStack[n])) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) ||
        g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]),
        static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? DiouObserverRoot() => FindProgramObserverRoot(
        static (g, f, child) => g.DiouFirstChild(f, child), static (g, index) => g.DiouObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1310)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1420)]
    private bool HasDiouDamageObserver(long id) => DiouObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1450)]
    private bool HasDiouBeforeDamageObserver(long id) => DiouObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1280)]
    private bool IsDiouDying() => ActiveDying is { } dying && DiouObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1870)]
    private bool AllowsDiouNestedDamage(ProgramSkillFrame f, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || DiouObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 480)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 470)]
    private bool IsDiouRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.Diou is null || recovery.ParentFrameId == f.Id) return false;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && DiouFirstChild(f, _resolutionStack[parent + 1]) &&
            DiouObserverSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29042)]
    private bool AdvanceDiouChildren()
    {
        if (_pendingDecision is not null) return false;
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame { Diou: { Stage: DiouStage.NativeChildren, Returned: true } } f)
        { ContinueDiou(f.Id); AdvanceRulesAndPublishState(); return true; }
        return DiouObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    }
}
