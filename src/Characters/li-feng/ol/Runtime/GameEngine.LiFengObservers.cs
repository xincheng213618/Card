namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanContinueIssuedLiFeng(ProgramSkillFrame f)
    {
        if (f.LiFeng is { Paid: true }) { AssertLiFeng(f); return true; }
        return f.InstructionIndex is 0 or 1 && f.TriggerId == "completed-drawn-entity" && f.WindowContext is { } c && IsIssuedJiyunCompletedUseCandidate(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), c);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27591)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27591)]
    private bool PermitIssuedLiFeng(ProgramSkillFrame f) => CanContinueIssuedLiFeng(f);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 27591)]
    private void AssertLiFeng(ProgramSkillFrame f)
    {
        if (f.LiFeng is not { } r) return;
        if (f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } c || c.ParentFrameId != r.WindowId || c.OwnerSeat != f.OwnerSeat || r.Source != LiFengSource(f) || r.GameplayHash != f.GameplayHash || GetPausedPrivateOfferEffect(f)?.Op != r.Op || !LiFengComposition.Handles(r.Op) || !Enum.IsDefined(r.Stage) || r.ActualTurn != _turnNumber || r.TurnOwner != _turnProgression.OwnerSeat || r.Pile.OwnerSeat != f.OwnerSeat || r.Pile.SkillId != "ol:jiyun" || r.Pile.Capacity != 3 || r.Cursor < 0 || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Recipients is not System.Collections.IList { IsReadOnly: true } || r.Entities is not System.Collections.IList { IsReadOnly: true } || r.ActualDrawIds is not System.Collections.IList { IsReadOnly: true } || r.DistributedIds is not System.Collections.IList { IsReadOnly: true } || r.CompletedEntities is not System.Collections.IList { IsReadOnly: true } || r.Recipients.Distinct().Count() != r.Recipients.Count || r.Entities.Select(e => e.CardId).Distinct().Count() != r.Entities.Count || r.ActualDrawIds.Distinct().Count() != r.ActualDrawIds.Count || r.DistributedIds.Distinct().Count() != r.DistributedIds.Count || r.CompletedEntities.Select(e => e.LeaseId).Distinct().Count() != r.CompletedEntities.Count || ProgramEventHistory<LiFengStartedEvent>().Count(e => e == new LiFengStartedEvent(f.Id, r.Source, r.GameplayHash, r.Op, r.WindowId, r.ActualTurn, r.TurnOwner, r.Pile)) != 1 || ProgramEventHistory<LiFengPaidEvent>().Count(e => e.FrameId == f.Id && e.Op == r.Op) != (r.Paid ? 1 : 0)) throw new InvalidOperationException("Li Feng lost his immutable owning source, native window or paid cursor.");
        if (r.Op == SkillProgramEffectOp.InitializeJiyunCategoryGrain)
        {
            if (c.Window != SkillProgramTriggerWindow.GameStarting || f.TriggerId != "initial-categories" || r.Stage != LiFengStage.InitialChildren || !r.Paid || r.Entities.Count > 3 || r.Entities.Select(e => GetProgramCardCategory(e.Kind)).Distinct().Count() != r.Entities.Count || r.Entities.Any(e => e.From != CardLocation.DrawPile) || !ProgramEventHistory<JiyunInitialEntityStoredEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).Select(e => new LiFengEntity(e.CardId, e.PrintedKind, CardLocation.DrawPile)).SequenceEqual(r.Entities)) throw new InvalidOperationException("Jiyun initialization lost its real distinct-category Deck entities.");
        }
        else if (r.Op == SkillProgramEffectOp.ResolveJiyunPreparationDrawLeases)
        {
            if (c.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || f.TriggerId != "preparation-recipients" || c.SourceSeat != f.OwnerSeat || r.Stage is not (LiFengStage.PreparationTargets or LiFengStage.PreparationDraw or LiFengStage.PreparationChildren) || r.MaximumRecipients < 1 || r.Recipients.Count > r.MaximumRecipients || r.Cursor > r.Recipients.Count || r.Paid == (r.Stage == LiFengStage.PreparationTargets) || ProgramEventHistory<JiyunRecipientLimitFrozenEvent>().Count(e => e == new JiyunRecipientLimitFrozenEvent(f.Id, r.MaximumRecipients)) != 1 || r.Paid && !ProgramEventHistory<JiyunRecipientFrozenEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).Select(e => e.RecipientSeat).SequenceEqual(r.Recipients)) throw new InvalidOperationException("Jiyun preparation changed its frozen target bound or actual recipient order.");
            if (r.Stage == LiFengStage.PreparationChildren)
            {
                if (r.Cursor >= r.Recipients.Count || r.DrawOutcome is not { } o || o.RecipientSeat != r.Recipients[r.Cursor] || o.RequestedCount != 1 || !r.ActualDrawIds.SequenceEqual(o.ActualDrawIds) || ProgramEventHistory<JiyunPreparationDrawReturnedEvent>().Where(e => e.FrameId == f.Id && e.Index == r.Cursor).ToArray() is not [var returned] || returned != new JiyunPreparationDrawReturnedEvent(f.Id, r.Cursor, o.RecipientSeat, o.ActualDrawIds.Count, o.ReplacementGainedIds.Count, o.SequenceBefore, o.SequenceAfter, o.AttemptFrameId) || !ProgramEventHistory<JiyunLeaseIssuedEvent>().Where(e => e.FrameId == f.Id && e.RecipientIndex == r.Cursor).OrderBy(e => e.MovementSequence).Select(e => e.CardId).SequenceEqual(o.ActualDrawIds)) throw new InvalidOperationException("Jiyun must mark actual DrawPile gains, never replacement gained entities.");
            }
        }
        else if (r.Op == SkillProgramEffectOp.CollectJiyunCompletedUseEntities)
        {
            if (c.Window != SkillProgramTriggerWindow.CardUseCompleted || f.TriggerId != "completed-drawn-entity" || !r.Paid || r.Stage is not (LiFengStage.CollectNext or LiFengStage.CollectChildren) || c.Jiyun is not { } issued || issued != r.CompletedUse || issued.Pile != r.Pile || r.Cursor > r.CompletedEntities.Count || r.CompletedEntities.Count != issued.EntityCount || !r.CompletedEntities.SequenceEqual(JiyunCompletedEntities(issued))) throw new InvalidOperationException("Jiyun lost its original whole-use lease invoice.");
            for (var i = 0; i < r.Cursor; i++) if (!ProgramEventHistory<JiyunLeaseResolvedEvent>().Any(e => e.FrameId == f.Id && e.LeaseId == r.CompletedEntities[i].LeaseId && e.ActionId == issued.ActionId)) throw new InvalidOperationException("A completed Jiyun lease cannot re-enter its collection cursor.");
            if (r.Stage == LiFengStage.CollectChildren && (r.Cursor >= r.CompletedEntities.Count || r.ActiveCardId != r.CompletedEntities[r.Cursor].CardId || r.ActiveFrom is not { Zone: CardZoneKind.Equipment or CardZoneKind.DiscardPile } from || ProgramEventHistory<JiyunClaimedEntityEvent>().Where(e => e.FrameId == f.Id && e.LeaseId == r.CompletedEntities[r.Cursor].LeaseId).ToArray() is not [var claim] || claim.CardId != r.ActiveCardId || claim.From != from || claim.To != r.Pile.Location || claim.ActionId != issued.ActionId || !_cardMovements.Any(m => m.Sequence == claim.MovementSequence && m.CardId == claim.CardId && m.CardKind == claim.PrintedKind && m.From == from && m.To == claim.To && m.Reason.Value == JiyunCollectReason))) throw new InvalidOperationException("Jiyun collection cannot substitute or pay an entity twice.");
        }
        else
        {
            if (c.Window != SkillProgramTriggerWindow.TurnEnding || f.TriggerId != "ending-distribution" || c.SourceSeat != f.OwnerSeat || r.Stage is not (LiFengStage.DistributionChoice or LiFengStage.DistributionChildren) || !ProgramEventHistory<ShuliangDistributionEntityFrozenEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).Select(e => new LiFengEntity(e.CardId, e.PrintedKind, e.From)).SequenceEqual(r.Entities) || r.Entities.Any(e => e.From.Zone != CardZoneKind.PublicPersistentPile || e.From.OwnerSeat != f.OwnerSeat) || r.DistributedIds.Any(id => !r.Entities.Any(e => e.CardId == id))) throw new InvalidOperationException("Shuliang changed its exact ending grain set or repeated a distributed entity.");
            if (r.Stage == LiFengStage.DistributionChildren && (!r.Paid || !r.DistributedIds.Contains(r.ActiveCardId) || ProgramEventHistory<ShuliangGrainDistributedEvent>().Where(e => e.FrameId == f.Id && e.CardId == r.ActiveCardId).ToArray() is not [var gift] || gift.RecipientSeat != r.ActiveRecipient || gift.From != r.ActiveFrom || !r.Entities.Any(e => e.CardId == gift.CardId && e.From == gift.From) || !_cardMovements.Any(m => m.Sequence == gift.MovementSequence && m.CardId == gift.CardId && m.From == gift.From && m.To == CardLocation.Hand(gift.RecipientSeat) && m.Reason.Value == ShuliangGiftReason))) throw new InvalidOperationException("Shuliang lost its one actual gift and typed child return.");
        }
        if (f.PendingMovementContinuation is { } movement && !IsLiFengMovement(f, GetPausedPrivateOfferEffect(f), movement)) throw new InvalidOperationException("Li Feng retained an unrelated movement continuation.");
    }
    private bool IsLiFengMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) => f.LiFeng is { Paid: true } r && effect?.Op == r.Op && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && r.Stage is LiFengStage.InitialChildren or LiFengStage.PreparationChildren or LiFengStage.CollectChildren or LiFengStage.DistributionChildren;
    private bool IsLiFengPaidMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        if (f.LiFeng is not { } r || !_cardMovements.Contains(m)) return false;
        return r.Stage switch
        {
            LiFengStage.InitialChildren => m.Sequence > r.Before && m.Sequence <= r.After && m.Reason.Value == JiyunInitialReason && m.From == CardLocation.DrawPile && m.To == r.Pile.Location && r.Entities.Any(e => e.CardId == m.CardId && e.Kind == m.CardKind),
            LiFengStage.PreparationChildren => r.DrawOutcome is { } o && m.Sequence > o.SequenceBefore && m.Sequence <= o.SequenceAfter && (r.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(o.RecipientSeat) && m.Reason.Value == JiyunDrawReason || m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle),
            LiFengStage.CollectChildren => m.Sequence > r.Before && m.Sequence <= r.After && (m.CardId == r.ActiveCardId && m.From == r.ActiveFrom && m.To == r.Pile.Location && m.Reason.Value == JiyunCollectReason || IsLiFengCollectedEquipment(f, CardKind.WoodenOx) && m.From == CardLocation.WoodenOxGrain(r.ActiveFrom!.Value.OwnerSeat!.Value) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard),
            LiFengStage.DistributionChildren => m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == r.ActiveCardId && m.From == r.ActiveFrom && m.To == CardLocation.Hand(r.ActiveRecipient) && m.Reason.Value == ShuliangGiftReason,
            _ => false
        };
    }
    private bool IsLiFengCollectedEquipment(ProgramSkillFrame f, CardKind kind) => f.LiFeng is { Paid: true, Stage: LiFengStage.CollectChildren, ActiveFrom: { Zone: CardZoneKind.Equipment } } r && r.CompletedEntities.Any(e => e.CardId == r.ActiveCardId && e.Kind == kind);
    private bool LiFengFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.LiFeng is not { Paid: true } r) return false;
        AssertLiFeng(f);
        if (r.Stage == LiFengStage.PreparationDraw && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills) return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state) return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id);
        if (child is RecoveryReplacementFrame recovery) return IsLiFengRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp) return IsLiFengCollectedEquipment(f, CardKind.SilverLion) && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.ParentFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == r.ActiveFrom!.Value.OwnerSeat && hp.Change.TargetSeat == r.ActiveFrom.Value.OwnerSeat && hp.Change.Amount == 1;
        return child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => IsLiFengPaidMovement(f, m));
    }
    private bool LiFengObserverSuffix(int index) => HasObserverSuffix(index, static (g, n) => g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]), static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? LiFengObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.LiFengFirstChild(f, child), static (e, index) => e.LiFengObserverSuffix(index));
    private bool HasLiFengDamageObserver(long id) => LiFengObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasLiFengBeforeDamageObserver(long id) => LiFengObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsLiFengDying() => ActiveDying is { } dying && LiFengObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsLiFengNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || LiFengObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    private bool IsLiFengRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.LiFeng is not { Paid: true } r) return false;
        if (recovery.ParentFrameId == f.Id) return IsLiFengCollectedEquipment(f, CardKind.SilverLion) && RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.SourceSeat == r.ActiveFrom!.Value.OwnerSeat && recovery.Attempt.TargetSeat == r.ActiveFrom.Value.OwnerSeat && recovery.Attempt.Amount == 1 && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.CardId == r.ActiveCardId && recovery.Attempt.Completion.MoveReason?.Value == JiyunCollectReason;
        var i = _resolutionStack.FindIndex(p => p.Id == f.Id); var j = _resolutionStack.FindIndex(p => p.Id == recovery.Id);
        return i >= 0 && j > i + 1 && LiFengFirstChild(f, _resolutionStack[i + 1]) && LiFengObserverSuffix(i) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[j - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 27591)] private bool AdvanceLiFengChildren() => _pendingDecision is null && LiFengObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
