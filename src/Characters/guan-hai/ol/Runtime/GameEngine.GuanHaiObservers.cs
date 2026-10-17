namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanContinueIssuedGuanHai(ProgramSkillFrame f)
    {
        if (f.GuanHai is { Paid: true }) { AssertGuanHai(f); return true; }
        if (f.InstructionIndex is not (0 or 1) || f.TriggerId is null || f.WindowContext is not { } c) return false;
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex);
        return IsIssuedXieweiCandidate(candidate, c) || IsIssuedGuanHaiYouqueCandidate(candidate, c);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 27578)]
    private void AssertGuanHai(ProgramSkillFrame f)
    {
        if (f.GuanHai is not { } r) return;
        if (f.InstructionIndex != 1 || r.Source != GuanHaiSource(f) || r.GameplayHash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.GameplayHash || GetPausedPrivateOfferEffect(f)?.Op != r.Op || r.ActualTurn != _turnNumber || r.TurnOwner != _turnProgression.OwnerSeat || !Enum.IsDefined(r.Stage) || r.Materials is not System.Collections.IList { IsReadOnly: true } || r.Materials.Select(m => m.CardId).Distinct().Count() != r.Materials.Count || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || ProgramEventHistory<GuanHaiStartedEvent>().Count(e => e == new GuanHaiStartedEvent(f.Id, r.Source, r.GameplayHash, r.Op, r.ActualTurn, r.TurnOwner, r.ParentId)) != 1) throw new InvalidOperationException("Guan Hai lost his original owning instruction, immutable costs, native turn or issued source.");
        if (r.Op == SkillProgramEffectOp.ResolveXieweiTwoHandBaitBasicUse)
        {
            if (r.Stage != GuanHaiStage.XieweiChildren || !r.Paid || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.TriggerId is null || f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCommitted } c || c.ParentFrameId != r.ParentId || _resolutionStack.SingleOrDefault(w => w.Id == r.ParentId) is not ProgramCardTriggerWindowFrame w || w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || ToSharedCandidate(w.Candidates[w.CandidateIndex]) is not { } candidate || candidate.OwnerSeat != f.OwnerSeat || candidate.SkillId != f.SkillId || candidate.BindingId != f.TriggerId || candidate.SkillInstanceId != f.SkillInstanceId || IssuedXieweiPayment(w.Action) is not { } p || p.Id != r.PaymentId || p.UseId != r.NativeUseId || p.ActionId != r.ActionId || !p.Materials.SequenceEqual(r.Materials) || ProgramEventHistory<XieweiCommittedEvent>().Count(e => e == new XieweiCommittedEvent(f.Id, p.Id, p.UseId, p.ActionId)) != 1) throw new InvalidOperationException("Xiewei lost its exact paid native commitment parent.");
        }
        else if (r.Op == SkillProgramEffectOp.ResolveYouqueMinimumBaitPindian)
        {
            if (f.TriggerId is not null || f.WindowContext is not null || f.ActivationId != "minimum-bait-pindian" || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var target] || target != r.OpponentSeat || r.Phase is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != f.OwnerSeat || phase.TurnNumber != r.ActualTurn || phase.ActualTurnOwnerSeat != r.TurnOwner || r.Paid != (r.PindianId > 0) || !r.Paid && r.Stage != GuanHaiStage.ChooseBait) throw new InvalidOperationException("Youque lost its actual Play and original other hand-bearing participant.");
            if (r.Paid && (r.Pile is null || ProgramEventHistory<YouquePindianIssuedEvent>().Count(e => e == new YouquePindianIssuedEvent(f.Id, r.PindianId, r.Source, r.GameplayHash, r.Pile, r.BaitCardId, r.BaitRank, r.OpponentSeat, phase, ProgramEventHistory<YouquePindianIssuedEvent>().Single(e => e.FrameId == f.Id).Before)) != 1)) throw new InvalidOperationException("Youque replaced the original selected minimum bait.");
            if (r.Result is { } result)
            {
                if (ProgramEventHistory<PindianResultDeterminedEvent>().Count(e => e.FrameId == r.PindianId && e.SkillId == f.SkillId && e.Result == result) != 1 || result.SourceSeat != f.OwnerSeat || (result.SourceCardId != r.BaitCardId && !IsDouyuHistoricalPairAssignment(r.PindianId, result, r.BaitCardId)) || result.OpponentSeat != r.OpponentSeat || result.SourceRank == result.OpponentRank || r.WinnerSeat != (result.SourceWon ? result.SourceSeat : result.OpponentSeat) || r.LoserSeat != (result.SourceWon ? result.OpponentSeat : result.SourceSeat)) throw new InvalidOperationException("Youque changed its real native Pindian winner, loser or rank.");
                var frozen = ProgramEventHistory<YouqueDiscardMaterialEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
                if (!frozen.Select(e => new GuanHaiMaterial(e.CardId, e.Kind, e.From)).SequenceEqual(r.Materials) || frozen.Where((e, i) => e.Index != i || e.LoserSeat != r.LoserSeat || e.From != CardLocation.Hand(r.LoserSeat)).Any()) throw new InvalidOperationException("Youque cannot split or replace the real loser's whole higher-rank Hand invoice.");
                foreach (var material in r.Materials) if (ProgramEventHistory<YouqueDiscardPaidEvent>().Where(e => e.FrameId == f.Id && e.CardId == material.CardId).ToArray() is not [var paid] || !_cardMovements.Any(m => m.Sequence == paid.Sequence && m.CardId == material.CardId && m.CardKind == material.Kind && m.From == material.From && m.To == CardLocation.DiscardPile && m.Reason.Value == YouqueDiscardReason)) throw new InvalidOperationException("Youque's whole Hand discard has an unpaid entity.");
            }
            if (r.NativeUseId > 0 && ProgramEventHistory<YouqueSlashIssuedEvent>().Count(e => e.Origin.ProgramFrameId == f.Id && e.Origin.UseId == r.NativeUseId && e.Origin.ActionId == r.ActionId && e.Origin.Source == r.Source && e.Origin.Phase == phase && e.Origin.ActorSeat == r.LoserSeat && e.Origin.TargetSeat == r.WinnerSeat) != 1) throw new InvalidOperationException("Youque lost the exact once-issued native loser-to-winner Slash.");
            if (r.NativeReturned && (r.Stage is not (GuanHaiStage.NativeSlash or GuanHaiStage.NativeChildren) || LifecycleCardUse(r.NativeUseId) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == r.NativeUseId) != 1)) throw new InvalidOperationException("Youque returned before its whole native Slash finished.");
        }
        else if (r.Op != SkillProgramEffectOp.ResolveYouqueIssuedSlashDamageBenefit || !r.Paid || r.Stage is not (GuanHaiStage.RewardDraw or GuanHaiStage.RewardChildren) || f.TriggerId is null || f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } dc || dc.DamageFrameId != r.DamageFrameId || dc.Amount <= 0 || ProgramEventHistory<YouqueBenefitIssuedEvent>().Count(e => e == new YouqueBenefitIssuedEvent(f.Id, r.ParentId, r.NativeUseId, r.DamageFrameId, f.OwnerSeat)) != 1 || ProgramEventHistory<YouqueSlashIssuedEvent>().Count(e => e.Origin.ProgramFrameId == r.ParentId && e.Origin.UseId == r.NativeUseId && e.Origin.ActionId == r.ActionId && e.Origin.Phase == r.Phase && e.Origin.Source.OwnerSeat == f.OwnerSeat) != 1) throw new InvalidOperationException("Youque reward requires its original actual positive-damage native use, never a synthetic damage event.");
        if (f.PendingMovementContinuation is { } pending && !IsGuanHaiMovement(f, GetPausedPrivateOfferEffect(f), pending)) throw new InvalidOperationException("Guan Hai has an unrelated native movement return.");
    }
    private bool IsGuanHaiMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => f.GuanHai is { Paid: true } r && e?.Op == r.Op && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && r.Stage is GuanHaiStage.XieweiChildren or GuanHaiStage.Pindian or GuanHaiStage.PindianChildren or GuanHaiStage.DiscardChildren or GuanHaiStage.NativeChildren or GuanHaiStage.RewardChildren;
    private bool GuanHaiMovementMatches(ProgramSkillFrame f, CardMovementRecord m)
    {
        var r = f.GuanHai!; if (!_cardMovements.Contains(m)) return false;
        if (r.Stage == GuanHaiStage.XieweiChildren) return LifecycleCardUse(r.NativeUseId)?.XieweiPayments?.Any(p => p.Id == r.PaymentId && ValidXieweiPayment(p) && p.Movements.Contains(m)) == true;
        if (r.Stage == GuanHaiStage.Pindian && ProgramEventHistory<PindianResultDeterminedEvent>().SingleOrDefault(e => e.FrameId == r.PindianId && e.SkillId == f.SkillId) is { Result: { } result })
            return new[] { result.SourceCardId, result.OpponentCardId }.Contains(m.CardId) && (m.Reason == CardMoveReasons.PindianReveal && m.To == CardLocation.Processing && (m.From == (m.CardId == result.SourceCardId ? r.Pile!.Location : CardLocation.Hand(result.OpponentSeat)) || m.From == CardLocation.DrawPile) || m.Reason == CardMoveReasons.PindianFinish && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile || m.Reason.Value == "program.pindian.claim" && m.From == CardLocation.Processing && m.To.Zone == CardZoneKind.Hand || m.Reason.Value == "pindian.reserve-top" && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing);
        if (m.Sequence <= r.Before || m.Sequence > r.After) return false;
        if (r.Stage == GuanHaiStage.DiscardChildren) return r.Materials.Any(c => c.CardId == m.CardId && c.Kind == m.CardKind && c.From == m.From) && m.To == CardLocation.DiscardPile && m.Reason.Value == YouqueDiscardReason;
        if (r.Stage == GuanHaiStage.RewardChildren) return m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == YouqueDrawReason || m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle;
        return false; // The zero-entity native Slash has no material settlement.
    }
    private bool GuanHaiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.GuanHai is not { Paid: true } r) return false; AssertGuanHai(f);
        if (child is PindianFrame p) return r.Stage == GuanHaiStage.Pindian && p.ParentFrameId == f.Id && p.Id == r.PindianId && IsGuanHaiPindianSource(p, HasPaidOwnedPilePindianSource(p));
        if (child is CardUseFrame use) return r.Stage == GuanHaiStage.NativeSlash && use.YouqueSlash?.ProgramFrameId == f.Id && IsIssuedYouqueUse(use);
        if (r.Stage == GuanHaiStage.RewardDraw && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills) return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state) return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange;
        if (child is HpChangedTriggerWindowFrame hp) return r.Stage is GuanHaiStage.XieweiChildren or GuanHaiStage.DiscardChildren or GuanHaiStage.RewardChildren && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is not CardsMovedTriggerWindowFrame moved || moved.Batch.Movements.Count == 0 || !moved.Batch.Movements.All(m => GuanHaiMovementMatches(f, m))) return false;
        return r.Stage == GuanHaiStage.XieweiChildren ? moved.Batch.ParentFrameId == r.NativeUseId && (moved.Batch.AwaitingProgramFrameId == f.Id || moved.ResumeProgramFrameId == f.Id) : ProgramMovementReturnMatches(f, moved) && (r.Stage == GuanHaiStage.Pindian ? moved.Batch.ParentFrameId == r.PindianId : moved.Batch.ParentFrameId == f.Id);
    }
    private ProgramSkillFrame? GuanHaiObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.GuanHaiFirstChild(f, child), static (e, i) => e.HasObserverSuffix(i, static (g, n) => g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    private bool HasGuanHaiDamageObserver(long id) => GuanHaiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasGuanHaiBeforeDamageObserver(long id) => GuanHaiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsGuanHaiDying() => ActiveDying is { } d && GuanHaiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsGuanHaiNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || GuanHaiObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    private bool IsGuanHaiRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.GuanHai is not { Paid: true } || !RecoveryReplacementFrameRidesOn(recovery, f)) return false;
        return _resolutionStack.FindIndex(p => p.Id == recovery.Id) > _resolutionStack.FindIndex(p => p.Id == f.Id) && GuanHaiObserverRoot()?.Id == f.Id;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 27578)] private bool AdvanceGuanHaiChildren() => _pendingDecision is null && GuanHaiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
