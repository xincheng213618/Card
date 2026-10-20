namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TaoQianWindowMatches(ProgramSkillFrame f)
    {
        if (f.TaoQian is not { } r) return false;
        if (r.Op == SkillProgramEffectOp.ResolveWenrenSequentialDraw) return f.TriggerId is null && f.WindowContext is null && f.ActivationId == "distribute-draw" && f.SelectedTargetSeats.Count == 0 && r.Phase is { Kind: ActualDiscardRecoveryPhaseKind.Play } p && p.ActorSeat == f.OwnerSeat && IsRecordedActualDiscardRecoveryPhase(p);
        return r.Op == SkillProgramEffectOp.ResolveZongluanPreparationSlashAndDiscard && f.TriggerId == "preparation-slash" && f.WindowContext is { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } c && c.ParentFrameId == r.WindowId && c.OwnerSeat == f.OwnerSeat && r.TurnOwner == f.OwnerSeat && _resolutionStack.SingleOrDefault(x => x.Id == r.WindowId) is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } w && w.OwnerSeat == f.OwnerSeat && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex]);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28120)]
    private void AssertTaoQian(ProgramSkillFrame f)
    {
        if (f.TaoQian is not { } r) return;
        if (r.Source != TaoQianSource(f) || r.GameplayHash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || f.InstructionIndex is < 0 or > 1 || f.InstructionIndex == 1 && GetPausedPrivateOfferEffect(f)?.Op != r.Op || !Enum.IsDefined(r.Stage) || !TaoQianWindowMatches(f) || r.Targets is not System.Collections.IList { IsReadOnly: true } || r.Selected is not System.Collections.IList { IsReadOnly: true } || r.Materials is not System.Collections.IList { IsReadOnly: true } || r.Targets.Distinct().Count() != r.Targets.Count || r.Targets.Any(s => !IsValidPlayerSeat(s)) || r.Selected.Distinct().Count() != r.Selected.Count || r.Cursor < 0 || r.Cursor > r.Targets.Count || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || ProgramEventHistory<TaoQianStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.Op == r.Op && e.WindowId == r.WindowId && e.ActualTurn == r.ActualTurn && e.TurnOwner == r.TurnOwner) != 1) throw new InvalidOperationException("Tao Qian lost his exact source, native window, immutable queue or paid instruction.");
        if (r.Op == SkillProgramEffectOp.ResolveZongluanPreparationSlashAndDiscard)
        {
            var committed = ProgramEventHistory<ZongluanCommittedEvent>().Where(e => e.FrameId == f.Id).ToArray();
            if (r.Paid ? committed is not [var paid] || paid.Source != r.Source || paid.GameplayHash != r.GameplayHash || paid.ActorSeat != r.ActorSeat || paid.ActualTurn != r.ActualTurn : committed.Length != 0) throw new InvalidOperationException("Zongluan repeated or replaced its original accepted actor.");
            if (r.UseId > 0 && (ProgramEventHistory<ZongluanSlashIssuedEvent>().Where(e => e.Origin.ProgramFrameId == f.Id).ToArray() is not [var issued] || issued.Origin.UseId != r.UseId || issued.Origin.ActionId != r.ActionId || issued.Origin.Source != r.Source || issued.Origin.GameplayHash != r.GameplayHash || issued.Origin.ActorSeat != r.ActorSeat || issued.TargetCount != r.Targets.Count || !ProgramEventHistory<ZongluanInitialTargetEvent>().Where(e => e.FrameId == f.Id && e.UseId == r.UseId).OrderBy(e => e.Index).Select(e => e.TargetSeat).SequenceEqual(r.Targets))) throw new InvalidOperationException("Zongluan's accepted zero-entity native Use changed.");
            if (r.Stage is TaoQianStage.NativeChildren or TaoQianStage.DiscardChoice or TaoQianStage.DiscardChildren)
                if (!r.NativeReturned || LifecycleCardUse(r.UseId) is not null || ProgramEventHistory<ZongluanSlashReturnedEvent>().Count(e => e.FrameId == f.Id && e.UseId == r.UseId && e.ActionId == r.ActionId && e.DamageRecipientCount == r.DamageRecipientCount) != 1 || ProgramEventHistory<ZongluanDirectDamageEvent>().Where(e => e.FrameId == f.Id && e.UseId == r.UseId).Select(e => e.TargetSeat).Distinct().Count() != r.DamageRecipientCount) throw new InvalidOperationException("Zongluan cannot discard before its whole native Slash returns or recalculate its damage recipients.");
            if (r.Stage == TaoQianStage.DiscardChildren && (r.Materials.Count != r.DiscardRequired || r.Selected.Count != r.DiscardRequired || !r.Materials.Select(m => m.CardId).SequenceEqual(r.Selected) || r.Materials.Any(c => ProgramEventHistory<ZongluanDiscardEntityEvent>().Count(e => e.FrameId == f.Id && e.CardId == c.CardId && e.Kind == c.CardKind && e.From == c.From && e.Sequence > r.Before && e.Sequence <= r.After) != 1))) throw new InvalidOperationException("Zongluan's logical HE discard lost its exact physical invoices.");
        }
        else
        {
            var committed = ProgramEventHistory<WenrenCommittedEvent>().Where(e => e.FrameId == f.Id).ToArray();
            if (r.Paid ? committed is not [var paid] || paid.Source != r.Source || paid.GameplayHash != r.GameplayHash || paid.Phase != r.Phase || paid.RecipientCount != r.Targets.Count || ProgramEventHistory<WenrenCommittedEvent>().Count(e => e.Source.OwnerSeat == f.OwnerSeat && e.Phase == r.Phase) != 1 || !ProgramEventHistory<WenrenRecipientFrozenEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).Select(e => e.RecipientSeat).SequenceEqual(r.Targets) : committed.Length != 0) throw new InvalidOperationException("Wenren repeated its actual Play quota or replaced its already-issued ordered living recipients.");
            if (r.Stage == TaoQianStage.DrawChildren && (r.Cursor >= r.Targets.Count || r.DrawOutcome is not { } outcome || outcome.RecipientSeat != r.Targets[r.Cursor] || outcome.RequestedCount != r.DrawAmount || ProgramEventHistory<WenrenRecipientEvaluatedEvent>().Count(e => e.FrameId == f.Id && e.Index == r.Cursor && e.RecipientSeat == outcome.RecipientSeat && e.Amount == r.DrawAmount && e.Amount == (e.RecipientHand == 0 ? 1 : 0) + (e.RecipientHand <= e.OwnerHand ? 1 : 0)) != 1 || ProgramEventHistory<WenrenDrawReturnedEvent>().Count(e => e.FrameId == f.Id && e.Index == r.Cursor && e.RecipientSeat == outcome.RecipientSeat && e.Before == outcome.SequenceBefore && e.After == outcome.SequenceAfter && e.RequestedCount == outcome.RequestedCount && e.ActualCount == outcome.ActualDrawIds.Count && e.ReplacementCount == outcome.ReplacementGainedIds.Count) != 1)) throw new InvalidOperationException("Wenren lost its freshly sampled recipient and unique native draw result.");
        }
        if (f.PendingMovementContinuation is { } p && !IsTaoQianMovement(f, GetPausedPrivateOfferEffect(f), p)) throw new InvalidOperationException("Tao Qian lost his exact typed native child return.");
    }
    private bool CanContinueIssuedTaoQian(ProgramSkillFrame f) { if (f.TaoQian is not { Paid: true }) return ContinueIssuedZhaohuo(f); AssertTaoQian(f); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28120)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28120)]
    private bool PermitIssuedTaoQian(ProgramSkillFrame f) => f.TaoQian is { Paid: true } && CanContinueIssuedTaoQian(f);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 180)]
    private bool IsTaoQianMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => IsZhaohuoMovement(f, e, p) || f.TaoQian is { Paid: true } r && e?.Op == r.Op && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && r.Stage is TaoQianStage.NativeChildren or TaoQianStage.DiscardChildren or TaoQianStage.DrawChildren;
    private bool TaoQianPaidMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        var r = f.TaoQian!; if (!_cardMovements.Contains(m) || m.Sequence <= r.Before || m.Sequence > r.After) return false;
        if (r.Stage == TaoQianStage.DiscardChildren)
        {
            if (ProgramEventHistory<ZongluanDiscardEntityEvent>().Any(e => e.FrameId == f.Id && e.CardId == m.CardId && e.Kind == m.CardKind && e.From == m.From && e.To == m.To && e.Sequence == m.Sequence)) return true;
            return m.From == CardLocation.WoodenOxGrain(f.OwnerSeat) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard && r.Materials.Any(c => c.CardKind == CardKind.WoodenOx && c.From == CardLocation.Equipment(f.OwnerSeat));
        }
        return r.Stage == TaoQianStage.DrawChildren && r.DrawOutcome is { } o && (o.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(o.RecipientSeat) && m.Reason.Value == WenrenDrawReason || o.ReplacementGainedIds.Contains(m.CardId) && m.To == CardLocation.Hand(o.RecipientSeat) || m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle);
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 340)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 330)]
    private bool IsTaoQianRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame child) => IsZhaohuoRecoveryParent(f, child) || f.TaoQian is { Paid: true, Stage: TaoQianStage.DiscardChildren } r && RecoveryReplacementFrameRidesOn(child, f) && child.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && child.Attempt.SourceSeat == f.OwnerSeat && child.Attempt.TargetSeat == f.OwnerSeat && child.Attempt.Amount == 1 && child.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && r.Materials.Any(c => c.CardKind == CardKind.SilverLion && c.From == CardLocation.Equipment(f.OwnerSeat)) && child.Attempt.Completion.MoveReason?.Value == ZongluanDiscardReason;
    private bool TaoQianFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.TaoQian is not { Paid: true } r) return false; AssertTaoQian(f);
        if (child is CardUseFrame use) return r.Stage == TaoQianStage.NativeSlash && use.ZongluanSlash?.ProgramFrameId == f.Id && IsIssuedZongluanUse(use);
        if (r.Stage == TaoQianStage.Drawing && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
        if (child is RecoveryReplacementFrame recovery) return IsTaoQianRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills) return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state) return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id && e.Change.Window == state.Window);
        return child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => TaoQianPaidMovement(f, m));
    }
    private bool TaoQianLifecycleEdge(int index) => _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame w && _resolutionStack[index] is ProgramSkillFrame f && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex]) && f.WindowContext is { } c && c.ParentFrameId == w.Id && c.Window == w.Window;
    private bool TaoQianObserverSuffix(int index) => HasObserverSuffix(index, static (g, n) => g.IsMovementObserverEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) || g.TaoQianLifecycleEdge(n) || g._resolutionStack[n - 1] is ProgramSkillFrame { Zenrun: not null } z && g.ZenrunFirstChild(z, g._resolutionStack[n]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || g.IsYanPuDyingExitEdge(n), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? TaoQianObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.TaoQianFirstChild(f, child), static (g, i) => g.TaoQianObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1110)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1220)]
    private bool HasTaoQianDamageObserver(long id) => HasZhaohuoDamageObserver(id) || TaoQianObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1250)]
    private bool HasTaoQianBeforeDamageObserver(long id) => HasZhaohuoBeforeDamageObserver(id) || TaoQianObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1080)]
    private bool IsTaoQianDying() => IsZhaohuoDying() || ActiveDying is { } d && TaoQianObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1650)]
    private bool AllowsTaoQianNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (AllowsZhaohuoNestedDamage(f, target, amount, source, nature, sourceLess)) return true;
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || TaoQianObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && !sourceLess && target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 28120)] private bool AdvanceTaoQianChildren() => _pendingDecision is null && TaoQianObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
