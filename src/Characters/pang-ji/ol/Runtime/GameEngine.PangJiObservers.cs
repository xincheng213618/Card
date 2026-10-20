namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool PangJiWindowMatches(ProgramSkillFrame f)
    {
        if (f.PangJi is not { } r || f.WindowContext is not { } c || c.ParentFrameId != r.WindowId || c.OwnerSeat != f.OwnerSeat) return false;
        if (r.Op == SkillProgramEffectOp.ResolveBiguoDrawAndArmTargetPindian)
            return f.TriggerId == "draw-end-category-gain" && c.Window == SkillProgramTriggerWindow.DrawPhaseEnded && _resolutionStack.SingleOrDefault(x => x.Id == r.WindowId) is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.DrawPhaseEnded, Continuation: ProgramLifecycleContinuation.CompleteDrawPhaseEnded } w && w.OwnerSeat == f.OwnerSeat && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex]);
        return r.Op == SkillProgramEffectOp.ResolveBiguoTargetedPindianAndCategoryBan && c.BiguoTarget == r.Target && BiguoTargetRoot(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), c, false);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28130)]
    private void AssertPangJi(ProgramSkillFrame f)
    {
        if (f.PangJi is not { } r) return;
        if (r.Source != PangJiSource(f) || r.GameplayHash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || f.InstructionIndex is < 0 or > 1 || f.InstructionIndex == 1 && GetPausedPrivateOfferEffect(f)?.Op != r.Op || !Enum.IsDefined(r.Stage) || !PangJiWindowMatches(f) || r.Obtained is not System.Collections.IList { IsReadOnly: true } || r.Obtained.Select(e => e.CardId).Distinct().Count() != r.Obtained.Count || r.Obtained.Any(e => e.From != CardLocation.DrawPile) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<PangJiStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.Op == r.Op && e.WindowId == r.WindowId && e.ActualTurn == r.ActualTurn && e.TurnOwner == r.TurnOwner) != 1) throw new InvalidOperationException("Pang Ji lost his exact source, native window, immutable entities or owning instruction.");
        if (r.Op == SkillProgramEffectOp.ResolveBiguoDrawAndArmTargetPindian)
        {
            if (r.Stage is not (PangJiStage.CostChoice or PangJiStage.CostChildren or PangJiStage.ObtainChildren) || r.Target is not null || r.PindianId != 0 || r.Result is not null) throw new InvalidOperationException("Biguo's draw-end receipt acquired an unrelated target or Pindian.");
            if (r.Paid)
            {
                if (r.Cost is not { } cost || cost.From.OwnerSeat != f.OwnerSeat || cost.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || ProgramEventHistory<BiguoCostCommittedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var committed] || committed.CardId != cost.CardId || committed.PrintedKind != cost.CardKind || committed.From != cost.From || ProgramEventHistory<BiguoCostPaidEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var paid] || paid.CardId != cost.CardId || paid.PrintedKind != cost.CardKind || paid.From != cost.From || paid.Sequence <= committed.Before || !_cardMovements.Any(m => m.CardId == paid.CardId && m.CardKind == paid.PrintedKind && m.From == paid.From && m.To == paid.To && m.Reason == paid.Reason && m.Sequence == paid.Sequence)) throw new InvalidOperationException("Biguo cannot repeat or replace his one genuine owned HE logical discard.");
            }
            else if (r.Stage != PangJiStage.CostChoice || r.Cost is not null || ProgramEventHistory<BiguoCostCommittedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("An unpaid Biguo draft cannot contain paid physical materials.");
            if (r.Stage == PangJiStage.ObtainChildren)
            {
                var issued = ProgramEventHistory<BiguoObtainEntityIssuedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
                if (!issued.Select(e => new PangJiEntity(e.CardId, e.PrintedKind, CardLocation.DrawPile)).SequenceEqual(r.Obtained) || issued.Select(e => GetProgramCardCategory(e.PrintedKind)).Distinct().Count() != issued.Length || issued.Any(e => ProgramEventHistory<BiguoObtainEntityPaidEvent>().Count(p => p.FrameId == f.Id && p.Index == e.Index && p.CardId == e.CardId && p.PrintedKind == e.PrintedKind && p.From == CardLocation.DrawPile && p.To == CardLocation.Hand(f.OwnerSeat) && p.Sequence > r.Before && p.Sequence <= r.After) != 1)) throw new InvalidOperationException("Biguo lost the actual one-per-category search, public reveal or whole obtain invoice.");
            }
        }
        else
        {
            if (!r.Paid || r.Target is not { } origin || r.Stage is not (PangJiStage.Pindian or PangJiStage.PindianChildren) || ProgramEventHistory<BiguoTargetAcceptedEvent>().Count(e => e.FrameId == f.Id && e.Origin == origin) != 1 || ProgramEventHistory<BiguoTargetAcceptedEvent>().Count(e => e.Origin.UseId == origin.UseId && e.Origin.Source.OwnerSeat == f.OwnerSeat) != 1 || ProgramEventHistory<BiguoPindianIssuedEvent>().Count(e => e.FrameId == f.Id && e.PindianId == r.PindianId && e.ArmFrameId == origin.ArmFrameId && e.UseId == origin.UseId && e.ActionId == origin.ActionId && e.OwnerSeat == f.OwnerSeat && e.Before == r.Before) != 1) throw new InvalidOperationException("Biguo's target-trigger contest lost its original paid lease and unique native child.");
            if (r.Stage == PangJiStage.PindianChildren && (r.Result is not { } result || _resolutionStack.Any(x => x.Id == r.PindianId) || ProgramEventHistory<PindianResultDeterminedEvent>().Count(e => e.FrameId == r.PindianId && e.SkillId == f.SkillId && e.Result == result) != 1 || ProgramEventHistory<BiguoPindianReturnedEvent>().Count(e => e.FrameId == f.Id && e.PindianId == r.PindianId && e.SourceSeat == result.SourceSeat && e.OpponentSeat == result.OpponentSeat && e.SourceCardId == result.SourceCardId && e.OpponentCardId == result.OpponentCardId && e.SourceRank == result.SourceRank && e.OpponentRank == result.OpponentRank) != 1 || ProgramEventHistory<BiguoPindianCardCategoryEvent>().Count(e => e.FrameId == f.Id && e.PindianId == r.PindianId) != 2 || ProgramEventHistory<BiguoCategoryBanEvent>().Where(e => e.FrameId == f.Id).Any(e => !ValidBiguoBan(e)))) throw new InvalidOperationException("Biguo cannot return before the native final pair, result, claims and category restrictions agree.");
        }
        if (f.PendingMovementContinuation is { } p && !IsPangJiMovement(f, GetPausedPrivateOfferEffect(f), p)) throw new InvalidOperationException("Pang Ji lost his typed paid movement return.");
    }
    private bool CanContinueIssuedPangJi(ProgramSkillFrame f)
    {
        if (CanContinueIssuedDouyu(f)) return true;
        if (f.PangJi is { Paid: true }) { AssertPangJi(f); return true; }
        return f.PangJi is null && f.TriggerId == "armed-target-pindian" && f.WindowContext is { } c && IsIssuedBiguoCandidate(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), c);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28130)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28130)]
    private bool PermitIssuedPangJi(ProgramSkillFrame f) => CanContinueIssuedPangJi(f);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 190)]
    private bool IsPangJiMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => IsDouyuMovement(f, e, p) || f.PangJi is { Paid: true } r && e?.Op == r.Op && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && r.Stage is PangJiStage.CostChildren or PangJiStage.ObtainChildren or PangJiStage.PindianChildren;
    private bool PangJiPaidMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        var r = f.PangJi!; if (!_cardMovements.Contains(m) || m.Sequence <= r.Before || m.Sequence > r.After) return false;
        if (r.Stage == PangJiStage.CostChildren)
            return ProgramEventHistory<BiguoCostPaidEvent>().Any(e => e.FrameId == f.Id && e.CardId == m.CardId && e.PrintedKind == m.CardKind && e.From == m.From && e.To == m.To && e.Reason == m.Reason && e.Sequence == m.Sequence) || r.Cost is { CardKind: CardKind.WoodenOx, From.Zone: CardZoneKind.Equipment } && m.From == CardLocation.WoodenOxGrain(f.OwnerSeat) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard;
        if (r.Stage == PangJiStage.ObtainChildren) return ProgramEventHistory<BiguoObtainEntityPaidEvent>().Any(e => e.FrameId == f.Id && e.CardId == m.CardId && e.PrintedKind == m.CardKind && e.From == m.From && e.To == m.To && e.Sequence == m.Sequence) && m.Reason.Value == BiguoObtainReason;
        return r.Stage == PangJiStage.PindianChildren && r.Result is { } result && (m.CardId == result.SourceCardId || m.CardId == result.OpponentCardId);
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 350)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 340)]
    private bool IsPangJiRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame child) => IsDouyuRecoveryParent(f, child) || f.PangJi is { Paid: true, Stage: PangJiStage.CostChildren, Cost: { CardKind: CardKind.SilverLion, From.Zone: CardZoneKind.Equipment } } && RecoveryReplacementFrameRidesOn(child, f) && child.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && child.Attempt.SourceSeat == f.OwnerSeat && child.Attempt.TargetSeat == f.OwnerSeat && child.Attempt.Amount == 1 && child.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && child.Attempt.Completion.MoveReason?.Value == BiguoCostReason;
    private bool IsBiguoPindianSettlementBatch(ProgramSkillFrame f, CardMovementBatchContext batch)
    {
        if (f.PangJi is not { Paid: true, Stage: PangJiStage.PindianChildren, Result: { } result } r ||
            r.Op != SkillProgramEffectOp.ResolveBiguoTargetedPindianAndCategoryBan ||
            batch.ParentFrameId != r.PindianId || batch.AwaitingProgramFrameId is not null ||
            batch.TurnNumber != r.ActualTurn || batch.OriginOwnerSeat != f.OwnerSeat ||
            batch.OriginSkillId != f.SkillId || batch.OriginSkillInstanceId != f.SkillInstanceId ||
            batch.Movements.Count is < 1 or > 2 ||
            batch.Movements.Select(m => m.Sequence).Distinct().Count() != batch.Movements.Count ||
            batch.Movements.Select(m => m.CardId).Distinct().Count() != batch.Movements.Count ||
            f.PendingMovementContinuation is not { } pending || !IsPangJiMovement(f, GetPausedPrivateOfferEffect(f), pending)) return false;
        // The native child has returned; its original reveal/claim/finish batches
        // retain that retired child's ID rather than acquiring the program's ID.
        AssertPangJi(f);
        return batch.Movements.All(m => m.TurnNumber == r.ActualTurn && m.Sequence > r.Before && m.Sequence <= r.After &&
            (m.CardId == result.SourceCardId || m.CardId == result.OpponentCardId) &&
            GetAdvancedCard(m.CardId).Kind == m.CardKind &&
            _cardMovements.Count(actual => actual.Sequence == m.Sequence && actual == m) == 1 &&
            (m.To == CardLocation.Processing &&
                (m.Reason == CardMoveReasons.PindianReveal &&
                    (m.From == CardLocation.Hand(result.SourceSeat) || m.From == CardLocation.Hand(result.OpponentSeat) ||
                     m.From == CardLocation.DrawPile && ProgramEventHistory<PindianTopSourceChosenEvent>().Any(e => e.CardId == m.CardId &&
                         (e.OwnerSeat == result.SourceSeat || e.OwnerSeat == result.OpponentSeat))) ||
                 m.From == CardLocation.DrawPile && m.Reason.Value == "pindian.reserve-top" &&
                    ProgramEventHistory<PindianTopSourceChosenEvent>().Any(e => e.CardId == m.CardId &&
                        (e.OwnerSeat == result.SourceSeat || e.OwnerSeat == result.OpponentSeat))) ||
             m.From == CardLocation.Processing &&
                (m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.PindianFinish ||
                 m.Reason.Value == "program.pindian.claim" &&
                    (m.To == CardLocation.Hand(result.SourceSeat) || m.To == CardLocation.Hand(result.OpponentSeat)))));
    }
    private bool IsBiguoPindianSettlementReturn(ProgramSkillFrame f, CardsMovedTriggerWindowFrame moved) =>
        moved.Id == moved.Batch.Id && moved.ResumeProgramFrameId == f.Id &&
        !HasAlternateProgramMovementReturn(moved) && IsBiguoPindianSettlementBatch(f, moved.Batch);
    private bool PangJiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.PangJi is not { Paid: true } r) return false; AssertPangJi(f);
        if (child is PindianFrame p) return r.Stage == PangJiStage.Pindian && p.ParentFrameId == f.Id && IsBiguoPindianFrame(p);
        if (child is RecoveryReplacementFrame recovery) return IsPangJiRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills) return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state) return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id && e.Change.Window == state.Window);
        return child is CardsMovedTriggerWindowFrame moved && (IsBiguoPindianSettlementReturn(f, moved) ||
            ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => PangJiPaidMovement(f, m)));
    }
    private bool PangJiLifecycleEdge(int index) => _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame w && _resolutionStack[index] is ProgramSkillFrame f && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex]) && f.WindowContext is { } c && c.ParentFrameId == w.Id && c.Window == w.Window;
    private bool PangJiObserverSuffix(int index) => HasObserverSuffix(index, static (g, n) => g.IsMovementObserverEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) || g.PangJiLifecycleEdge(n) || g.DouyuPindianChildRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || g._resolutionStack[n - 1] is ProgramSkillFrame { Zenrun: not null } z && g.ZenrunFirstChild(z, g._resolutionStack[n]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || g.IsYanPuDyingExitEdge(n), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? PangJiObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.PangJiFirstChild(f, child), static (g, n) => g.PangJiObserverSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1120)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1230)]
    private bool HasPangJiDamageObserver(long id) => HasDouyuDamageObserver(id) || PangJiObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1260)]
    private bool HasPangJiBeforeDamageObserver(long id) => HasDouyuBeforeDamageObserver(id) || PangJiObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1090)]
    private bool IsPangJiDying() => IsDouyuDying() || ActiveDying is { } d && PangJiObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1660)]
    private bool AllowsPangJiNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (AllowsDouyuNestedDamage(f, target, amount, source, nature, sourceLess)) return true;
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || PangJiObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && !sourceLess && target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 28130)] private bool AdvancePangJiChildren() => _pendingDecision is null && PangJiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
