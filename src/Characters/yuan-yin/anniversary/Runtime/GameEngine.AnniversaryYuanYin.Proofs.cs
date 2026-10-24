namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool YuanYinOriginalParent(ProgramSkillFrame f)
    {
        if (f.AnniversaryYuanYin is not { } r || f.WindowContext is not { } w || r.ParentId != w.ParentFrameId) return false;
        if (r.Mode == "moshou") return f.SkillId == AnniversaryYuanYinComposition.Moshou && f.TriggerId == "black-target-cycle" &&
            w.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && w.Facts?.CardActionCardIsBlack == true && w.CardUse is { } card &&
            card.ParentCardUseFrameId == r.UseId && card.CardActionId == r.ActionId &&
            _resolutionStack.SingleOrDefault(p => p.Id == w.ParentFrameId) is ProgramCardTriggerWindowFrame parent && parent.Action.ActionId == r.ActionId &&
            parent.ParentFrameId == r.UseId && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
            MountObserverCandidateMatches(f, ToSharedCandidate(parent.Candidates[parent.CandidateIndex]));
        return r.Mode == "yunjiu" && f.SkillId == AnniversaryYuanYinComposition.Yunjiu && f.TriggerId == "dead-owned-card-gift" &&
            w.Window == SkillProgramTriggerWindow.CharacterDied && w.TargetSeat == r.Victim &&
            _resolutionStack.SingleOrDefault(p => p.Id == w.ParentFrameId) is ProgramKillTriggerWindowFrame { BeforeDeathCleanup: true } window &&
            window.DeathFrameId == r.DeathId && window.VictimSeat == r.Victim && window.KillerSeat == w.SourceSeat &&
            window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count && MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex]) &&
            _resolutionStack.SingleOrDefault(p => p.Id == r.DeathId) is DeathFrame death && YuanYinIssuedDeathWindow(window, death);
    }
    private bool ValidYuanYin(ProgramSkillFrame f)
    {
        if (f.AnniversaryYuanYin is not { } r || r.Source != YuanYinSource(f) || r.Hash != f.GameplayHash || r.Turn != _turnNumber ||
            f.InstructionIndex != 1 || f.TriggerId is null || f.ActivationId != f.TriggerId || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !YuanYinOriginalParent(f) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<AnniversaryYuanYinStartedEvent>().Count(e => e == new AnniversaryYuanYinStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Turn, r.Mode)) != 1) return false;
        if (r.Mode == "moshou")
        {
            if (r.Requested < 1 || r.Next != (r.Requested <= 1 ? 0 : r.Requested - 1) || r.Stage is not ("drawing" or "draw-children") ||
                GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveAnniversaryYuanYinMoshou ||
                ProgramEventHistory<AnniversaryYuanYinMoshouIssuedEvent>().Count(e => e == new AnniversaryYuanYinMoshouIssuedEvent(f.Id, r.Source, r.Hash, r.UseId, r.ActionId, r.Requested, r.Next)) != 1) return false;
            if (r.Stage == "drawing") return r.Draw is null && f.PendingMovementContinuation is null;
            return r.Draw is { } draw && draw.RecipientSeat == f.OwnerSeat && draw.RequestedCount == r.Requested && draw.SequenceBefore == r.Before && draw.SequenceAfter == r.After &&
                f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } pending && pending.SubjectSeat == f.OwnerSeat &&
                ProgramEventHistory<AnniversaryYuanYinMoshouAdvancedEvent>().Count(e => e == new AnniversaryYuanYinMoshouAdvancedEvent(f.Id, r.Source, r.Hash, r.Requested, r.Next, draw.ActualDrawIds.Count, r.Before, r.After)) == 1;
        }
        if (GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveAnniversaryYuanYinYunjiu || r.Stage is not ("recipient" or "card" or "gift-children" or "maximum-children" or "recovery-children")) return false;
        if (r.Stage == "recipient") return r.Recipient == -1 && r.Gain is null && f.PendingMovementContinuation is null;
        if (!IsValidPlayerSeat(r.Recipient) || r.Recipient == f.OwnerSeat || r.Recipient == r.Victim) return false;
        if (r.Stage == "card") return r.Gain is null && r.CardId is null && f.PendingMovementContinuation is null;
        if (r.CardId is not { } id || r.From is not { Zone: CardZoneKind.Hand or CardZoneKind.Equipment } from || from.OwnerSeat != r.Victim ||
            r.Gain is not { } gain || gain.Reason != YuanYinGiftReason) return false;
        ValidateHappyWeiYanGainRequestSettlement(gain, f.Id, [id], CardLocation.Hand(r.Recipient), r.Before);
        if (gain.Movements.Any(m => m.CardId != id || m.From != from || !_cardMovements.Contains(m) || m.Sequence <= r.Before || m.Sequence > r.After) ||
            r.Delivered != gain.Movements.Any(m => m.To == CardLocation.Hand(r.Recipient)) ||
            ProgramEventHistory<AnniversaryYuanYinGiftPaidEvent>().Count(e => e == new AnniversaryYuanYinGiftPaidEvent(f.Id, r.DeathId, r.Victim, r.Recipient, r.Delivered, r.Before, r.After)) != 1) return false;
        if (r.Stage == "gift-children") return f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } p && p.SubjectSeat == f.OwnerSeat;
        if (f.PendingMovementContinuation is not null || !r.Delivered ||
            ProgramEventHistory<AnniversaryYuanYinBenefitIssuedEvent>().Count(e => e == new AnniversaryYuanYinBenefitIssuedEvent(f.Id, "maximum-hp", f.OwnerSeat, 1)) != 1) return false;
        return r.Stage != "recovery-children" || ProgramEventHistory<AnniversaryYuanYinBenefitIssuedEvent>().Count(e => e == new AnniversaryYuanYinBenefitIssuedEvent(f.Id, "recovery", f.OwnerSeat, 1)) == 1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 49300)] private void AssertYuanYin(ProgramSkillFrame f)
    {
        if (f.AnniversaryYuanYin is not null && !ValidYuanYin(f)) throw new InvalidOperationException("Yuan Yin lost his original target/death parent, private native gift or one-time health/draw receipt.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 49300)]
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 49300)]
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 49300)]
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 49300)]
    private bool YuanYinMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => f.AnniversaryYuanYin is { Stage: "draw-children" or "gift-children" } &&
        e?.Op is SkillProgramEffectOp.ResolveAnniversaryYuanYinMoshou or SkillProgramEffectOp.ResolveAnniversaryYuanYinYunjiu && p == f.PendingMovementContinuation && ValidYuanYin(f);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 49300)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 49300)]
    private bool RetainYuanYin(ProgramSkillFrame f) => f.AnniversaryYuanYin is { Stage: "drawing" or "draw-children" or "gift-children" or "maximum-children" or "recovery-children" } && ValidYuanYin(f);
    private bool YuanYinFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.AnniversaryYuanYin is not { } r || !ValidYuanYin(f)) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == "drawing" && f.NativeDraw is not null;
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } p && YuanYinMovement(f, GetPausedPrivateOfferEffect(f), p) && ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        if (child is HappyQiaoHealthChoiceFrame qiao) return r.Stage is "maximum-children" or "recovery-children" && HappyQiaoHealthChoiceRidesOn(qiao, f);
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && recovery.Return.ResumeFrameId == f.Id &&
            recovery.Return.Continuation == (f.PendingMovementContinuation is null ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement) && RecoveryReplacementFrameRidesOn(recovery, f);
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id &&
            hp.Continuation == (f.PendingMovementContinuation is null ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CharacterStateContinuation == CharacterStateContinuation.Program);
    }
    private bool YuanYinObserverEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) || TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) ||
        _resolutionStack[i - 1] is ProgramSkillFrame f && YuanYinFirstChild(f, _resolutionStack[i]) || NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private ProgramSkillFrame? YuanYinObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.YuanYinFirstChild(f, c),
        static (e, i) => e.HasObserverSuffix(i, static (g, n) => g.YuanYinObserverEdge(n), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 49300)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 49300)]
    private bool YuanYinDamageObserver(long id) => YuanYinObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 49300)] private bool YuanYinBeforeDamageObserver(long id) => YuanYinObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 49300)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 49300)]
    private bool YuanYinDying() => ActiveDying is { } d && YuanYinObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 49300)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 49300)]
    private bool YuanYinRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.AnniversaryYuanYin is null) return false;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id); var j = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return i >= 0 && j > i && YuanYinFirstChild(f, _resolutionStack[i + 1]) && (j == i + 1 || SameNameHandObserverSuffix(i)) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[j - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 49300)] private bool AdvanceYuanYinChildren() => _pendingDecision is null && YuanYinObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
