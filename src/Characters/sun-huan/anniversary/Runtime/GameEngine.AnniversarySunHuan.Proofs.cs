namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidSunHuanDraw(AnniversarySunHuanDraw d) => d.Source.SkillId == AnniversarySunHuanComposition.Skill && d.Source.BindingId == "target-draw" && IsValidPlayerSeat(d.Source.OwnerSeat) &&
        d.Outcome.RecipientSeat == d.Source.OwnerSeat && d.Outcome.RequestedCount == 1 && d.Outcome.ActualDrawIds.Count <= 1 && d.Outcome.SequenceBefore >= 0 && d.Outcome.SequenceAfter >= d.Outcome.SequenceBefore && d.Outcome.SequenceAfter <= _movementSequence &&
        ProgramEventHistory<AnniversarySunHuanDrawReturnedEvent>().Count(e => e == new AnniversarySunHuanDrawReturnedEvent(d.FrameId, d.Source.OwnerSeat, d.Turn, d.Outcome.ActualDrawIds.Count, d.Outcome.SequenceBefore, d.Outcome.SequenceAfter, d.Outcome.AttemptFrameId)) == 1 &&
        d.Outcome.ActualDrawIds.All(id => id > 0 && _cardMovements.Count(m => m.CardId == id && m.Sequence > d.Outcome.SequenceBefore && m.Sequence <= d.Outcome.SequenceAfter && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(d.Source.OwnerSeat) && m.Reason == SunHuanDrawReason) == 1);
    private bool ValidSunHuan(ProgramSkillFrame f) => f.AnniversarySunHuan is { } r && f.SkillId == AnniversarySunHuanComposition.Skill && f.InstructionIndex == 1 && r.Source == SunHuanSource(f) && r.Hash == f.GameplayHash &&
        r.Turn == _turnNumber && r.TurnOwner == _currentSeat && r.ParentId == f.WindowContext?.ParentFrameId && r.Operation == GetPausedPrivateOfferEffect(f)?.Op && Enum.IsDefined(r.Stage) &&
        r.Before >= 0 && r.After >= r.Before && r.After <= _movementSequence &&
        ProgramEventHistory<AnniversarySunHuanStartedEvent>().Count(e => e == new AnniversarySunHuanStartedEvent(f.Id, r.Source, r.Hash, r.Turn, r.TurnOwner, r.ParentId, r.Operation)) == 1 &&
        (r.Operation == SkillProgramEffectOp.ResolveAnniversarySunHuanNijiDraw ? f.TriggerId == "target-draw" && f.WindowContext is { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } c } &&
            LifecycleCardUse(c.ParentCardUseFrameId) is { Action: { Type: CardActionType.Use } a } use && a.ActionId == c.CardActionId && use.TargetSeats.Contains(f.OwnerSeat) :
            f.TriggerId == "ending-use-discard" && f.WindowContext?.Window == SkillProgramTriggerWindow.TurnEnding && SunHuanDraws(f.OwnerSeat, f.SkillInstanceId).Any(d => d.Hash == r.Hash && ValidSunHuanDraw(d)));
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 45400)] private void RequireSunHuan(ProgramSkillFrame f)
    {
        if (f.AnniversarySunHuan is not { } r) return;
        if (!ValidSunHuan(f)) throw new InvalidOperationException("Niji lost its original actual turn, target/ending, skill instance or accepted instruction.");
        if (r.Draw is { } d && !_players[f.OwnerSeat].AnniversaryNijiDraws.Any(x => x.FrameId == f.Id && x.Source == r.Source && x.Hash == r.Hash && x.Outcome == d && ValidSunHuanDraw(x)))
            throw new InvalidOperationException("Niji cannot substitute obtained cards for its actual native draw result.");
        if (r.Native is { } n && (n.FrameId != f.Id || n.Instruction != f.InstructionIndex || n.Source != r.Source || n.Hash != r.Hash || n.Turn != r.Turn || n.Actor != f.OwnerSeat || n.CardId <= 0 || n.Action.CardId != n.CardId ||
            !SunHuanDraws(f.OwnerSeat, f.SkillInstanceId).Any(x => x.Outcome.ActualDrawIds.Contains(n.CardId) && ValidSunHuanDraw(x))))
            throw new InvalidOperationException("Niji's actual use must retain a truly drawn physical entity and its original source.");
        if (r.Stage is AnniversarySunHuanStage.NativeUse or AnniversarySunHuanStage.NativeTail && (r.Native is not { UseId: > 0, ActionId: > 0 } native ||
            ProgramEventHistory<AnniversarySunHuanUseIssuedEvent>().Count(e => e == new AnniversarySunHuanUseIssuedEvent(f.Id, native.UseId, native.ActionId, native.CardId)) != 1 ||
            r.Returned && ProgramEventHistory<AnniversarySunHuanUseReturnedEvent>().Count(e => e == new AnniversarySunHuanUseReturnedEvent(f.Id, native.UseId, native.ActionId)) != 1))
            throw new InvalidOperationException("Niji lost its exact issued or completed native card use.");
        if (r.Stage == AnniversarySunHuanStage.DiscardChildren && (r.Cards.Distinct().Count() != r.Cards.Count ||
            ProgramEventHistory<AnniversarySunHuanDiscardedEvent>().Count(e => e == new AnniversarySunHuanDiscardedEvent(f.Id, r.Cards.Count, r.Before, r.After)) != 1 ||
            r.Cards.Any(id => !SunHuanDraws(f.OwnerSeat, f.SkillInstanceId).Any(x => x.Outcome.ActualDrawIds.Contains(id)) || _cardMovements.Count(m => m.CardId == id && m.Sequence > r.Before && m.Sequence <= r.After &&
                (m.From == CardLocation.Hand(f.OwnerSeat) || GetProgramDiscardSource(m) == CardLocation.Hand(f.OwnerSeat)) && (m.To == CardLocation.DiscardPile || m.To == CardLocation.OutsideGame && IsEphemeralCardDestroyedMaterial(m)) && m.Reason == SunHuanDiscardReason) != 1)))
            throw new InvalidOperationException("Niji must really discard only the remaining Hand entities drawn by this skill this actual turn.");
    }
    private bool HasAnniversarySunHuanPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p && _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversarySunHuan.Stage: AnniversarySunHuanStage.Choice, Step: ResolutionFrameStep.AwaitingResponse } f &&
        p.PlayerSeat == f.OwnerSeat && p.TargetSeat == f.OwnerSeat && ValidSunHuan(f) && AssistedChoicesEqual(p.Choices, SunHuanChoices(f));
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 45400)] [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 45400)] [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 45400)]
    private bool SunHuanMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p)
    {
        if (f.AnniversarySunHuan is not { } r || effect?.Op != r.Operation || p != f.PendingMovementContinuation || p.SubjectSeat != f.OwnerSeat || p.BeforeCount != 0 || p.CoverageResultBind is not null || !ValidSunHuan(f)) return false;
        if (r.Stage == AnniversarySunHuanStage.DrawChildren) return r.Draw is { } d && _players[f.OwnerSeat].AnniversaryNijiDraws.Any(x => x.FrameId == f.Id && x.Outcome == d && ValidSunHuanDraw(x));
        if (r.Stage == AnniversarySunHuanStage.DiscardChildren) { RequireSunHuan(f); return true; }
        return r.Stage == AnniversarySunHuanStage.NativeTail && r.Returned && r.Native is { } n && _pendingCardsMovedBatches.Where(b => r.NativeBatches.Contains(b.Id)).All(b =>
            b.ParentFrameId == n.UseId && b.AwaitingProgramFrameId is null && b.OriginOwnerSeat == f.OwnerSeat && b.OriginSkillId == f.SkillId && b.OriginSkillInstanceId == f.SkillInstanceId &&
            b.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m)) && ProgramEventHistory<AnniversarySunHuanNativeBatchEvent>().Count(e => e == new AnniversarySunHuanNativeBatchEvent(f.Id, n.UseId, b.Id)) == 1);
    }
    private bool SunHuanFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.AnniversarySunHuan is not { } r || !ValidSunHuan(f)) return false;
        if (child is NativeDrawAttemptFrame draw) return r.Stage == AnniversarySunHuanStage.Drawing && draw.Context.ParentFrameId == f.Id && draw.Context.RecipientSeat == f.OwnerSeat && draw.Context.RequestedCount == 1 && NativeDrawAttemptRidesOn(draw, f);
        if (child is CardUseFrame use) return r.Stage == AnniversarySunHuanStage.NativeUse && IsSunHuanNative(use);
        if (child is CardDeclarationFrame declaration) return MatchesSunHuanDeclaration(f, declaration);
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } p && SunHuanMovement(f, GetPausedPrivateOfferEffect(f), p) && ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m));
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && recovery.Return.ResumeFrameId == f.Id && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CharacterStateContinuation == CharacterStateContinuation.Program);
    }
    private ProgramSkillFrame? SunHuanObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.SunHuanFirstChild(f, child), static (e, i) => e.SunHuanObserverSuffix(i));
    private bool SunHuanObserverSuffix(int i) => HasObserverSuffix(i, static (e, index) => e.IsSameNameHandObserverEdge(index) || e.SunHuanDeclarationEdge(index),
        static (e, index, dying) => e.IsSuspendedOrPaidRescueObserverSuffix(index, dying) || e.TieredRoundZeroDyingRescueRide(index, dying) || e.DrawFundedDistinctBasicDyingRescueRide(index, dying) || e.HuanpeiDyingRescueRide(index, dying));
    private bool SunHuanDeclarationEdge(int i)
    {
        if (i < 1 || _resolutionStack[i - 1] is not CardDeclarationFrame d || _resolutionStack.SingleOrDefault(f => f.Id == d.Return.ParentFrameId) is not ProgramSkillFrame root || !MatchesSunHuanDeclaration(root, d)) return false;
        if (_resolutionStack[i] is CardDeclarationChallengeFrame challenge) return d.Stage == CardDeclarationStage.Challenging && challenge.ParentFrameId == d.Id && challenge.SeatIndex >= 0 && challenge.SeatIndex < challenge.EligibleSeats.Count;
        if (_resolutionStack[i] is CardsMovedTriggerWindowFrame moved) return d.Stage is CardDeclarationStage.Paying or CardDeclarationStage.Cleaning && d.ActiveChildFrameId == moved.Id && moved.ResumeDeclarationFrameId == d.Id && moved.Batch.ParentFrameId == d.Id && moved.Batch.Movements is [var m] &&
            _cardMovements.Contains(m) && m.CardId == d.Payment.Cost.CardId && (d.Stage == CardDeclarationStage.Paying ? m.From == d.Payment.Cost.From && m.To == CardLocation.Processing && m.Reason.Value == "conversion.declaration.pay" : m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason.Value is "conversion.declaration.failed" or "conversion.declaration.cancelled");
        return _resolutionStack[i] is ProgramLifecycleTriggerWindowFrame skills && d.Stage == CardDeclarationStage.Granting && skills.Window == SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId == d.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeCardDeclaration;
    }
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 45400)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 45400)]
    private bool SunHuanDamageObserver(long id) => SunHuanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 45400)] private bool SunHuanBeforeDamage(long id) => SunHuanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 45400)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 45400)] private bool SunHuanDying() => ActiveDying is { } dying && SunHuanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 45400)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 45400)] private bool RetainSunHuan(ProgramSkillFrame f) => f.AnniversarySunHuan is not null && ValidSunHuan(f);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 45400)] private bool AdvanceSunHuanChildren() => _pendingDecision is null && SunHuanObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 45400)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 45400)] private bool SunHuanRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.AnniversarySunHuan is null) return false;
        var p = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return p >= 0 && c > p && SunHuanFirstChild(f, _resolutionStack[p + 1]) && (c == p + 1 || SunHuanObserverSuffix(p)) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
}
