namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidMouGyz(ProgramSkillFrame f)
    {
        if (f.MouGuoYouZhi is not { } r || r.Source != MouGyzSource(f) || r.GameplayHash != f.GameplayHash || f.InstructionIndex != 1 || !Enum.IsDefined(r.Stage) || !IsValidPlayerSeat(r.ActorSeat) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Selected.Distinct().Count() != r.Selected.Count || r.Gifts.Select(g => g.CardId).Distinct().Count() != r.Gifts.Count || r.DistinctNames < 0 || r.DistinctNames > r.Gifts.Count ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || ProgramEventHistory<MouGuoYouZhiStartedEvent>().Count(e => e == new MouGuoYouZhiStartedEvent(f.Id, r.Source, r.GameplayHash, r.Op, r.ActorSeat, r.ActualTurn, r.ActualPhase)) != 1) return false;
        if (r.Op == SkillProgramEffectOp.ResolveMouGuoYouZhiBique)
        { if (f.SkillId != MouGuoYouZhiComposition.Bique || f.ActivationId != "inspect-and-give" || f.TriggerId is not null || f.SelectedTargetSeats is not [var target] || target != r.ActorSeat || ProgramEventHistory<MouGuoYouZhiViewedEvent>().Count(e => e.FrameId == f.Id && e.TargetSeat == r.ActorSeat && e.OwnerSeat == f.OwnerSeat) != 1) return false; }
        else if (r.Op != SkillProgramEffectOp.ResolveMouGuoYouZhiZhongchun || f.SkillId != MouGuoYouZhiComposition.Zhongchun || f.TriggerId is not ("own-ending" or "other-ending") || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } c || c.SourceSeat != r.ActorSeat || !ExactMouGyzEndingParent(f)) return false;
        if (r.Paid && (r.Op == SkillProgramEffectOp.ResolveMouGuoYouZhiBique ? ProgramEventHistory<MouGuoYouZhiGiftAcceptedEvent>().Count(e => e.FrameId == f.Id && e.TargetSeat == r.ActorSeat && e.Count == r.Gifts.Count) != 1 : ProgramEventHistory<MouGuoYouZhiObtainIssuedEvent>().Count(e => e == new MouGuoYouZhiObtainIssuedEvent(f.Id, r.ActorSeat, r.CardId)) != 1)) return false;
        return ProgramEventHistory<MouGuoYouZhiMovementEvent>().Where(e => e.FrameId == f.Id).All(e => _cardMovements.Count(m => m.CardId == e.CardId && m.CardKind == e.PrintedKind && m.From == e.From && m.To == e.To && m.Reason == e.Reason && m.Sequence == e.Sequence) == 1);
    }
    private void SealMouGyz(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.MouGuoYouZhi!; ReplaceRuntimeTop(f with { MouGuoYouZhi = r with { After = _movementSequence } });
        foreach (var m in _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= _movementSequence).ToArray())
        { var fact = new MouGuoYouZhiMovementEvent(id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence); if (!ProgramEventHistory<MouGuoYouZhiMovementEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact); }
    }
    private bool DrainMouGyz(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 32400)]
    private bool MouGyzMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => e?.Op is SkillProgramEffectOp.ResolveMouGuoYouZhiBique or SkillProgramEffectOp.ResolveMouGuoYouZhiZhongchun && ValidMouGyz(f) && p == f.PendingMovementContinuation && p.BeforeCount == 0 && p.CoverageResultBind is null && f.MouGuoYouZhi!.Stage is MouGuoYouZhiStage.GiftChildren or MouGuoYouZhiStage.ObtainChildren or MouGuoYouZhiStage.DiscardChildren or MouGuoYouZhiStage.DrawChildren;
    private bool MouGyzFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouGyz(f)) return false; var r = f.MouGuoYouZhi!;
        if (child is CardUseFrame use) return MouGyzNativeRidesOn(use, f);
        if (child is CardDeclarationFrame declaration) return MatchesMouGyzDeclaration(f, declaration);
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouGuoYouZhiStage.Drawing && f.NativeDraw is not null;
        if (child is RecoveryReplacementFrame recovery) return ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray());
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp);
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && ProgramEventHistory<MouGuoYouZhiMovementEvent>().Contains(new(f.Id, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)));
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouGyzSuffix(int i) => HasObserverSuffix(i, static (g, n) => g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) || g.TypedDamagePostEventEdge(g._resolutionStack[n - 1], g._resolutionStack[n]) || g._resolutionStack[n - 1] is ProgramSkillFrame f && g.MouGyzFirstChild(f, g._resolutionStack[n]) || g.MouGyzDeclarationEdge(n) || NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? MouGyzObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouGyzFirstChild(f, c), static (g, i) => g.MouGyzSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 32400)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 32400)]
    private bool MouGyzDamage(long id) => MouGyzObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 32400)]
    private bool MouGyzBeforeDamage(long id) => MouGyzObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 32400)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 32400)]
    private bool MouGyzDying() => ActiveDying is { } d && MouGyzObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 32400)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 32400)]
    private bool MouGyzRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame child)
    { if (!ValidMouGyz(f)) return false; if (ProgramRecoveryReplacementReceiptMatches(f, child, CompleteProgramEventHistory().ToArray())) return true; var a = _resolutionStack.FindIndex(x => x.Id == f.Id); var b = _resolutionStack.FindIndex(x => x.Id == child.Id); return a >= 0 && b > a + 1 && MouGyzFirstChild(f, _resolutionStack[a + 1]) && MouGyzSuffix(a) && RecoveryReplacementFrameRidesOn(child, _resolutionStack[b - 1]); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 32400)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 32400)]
    private bool KeepMouGyzPaid(ProgramSkillFrame f) => f.MouGuoYouZhi is { Paid: true } && ValidMouGyz(f);
    [ProgramContinuation(ProgramContinuationKind.Resume, 32400)]
    private bool ResumeMouGyz(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouGuoYouZhi: not null } f || f.Id != id) return false; ContinueMouGyz(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 32400)] private bool ReturnMouGyzMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouGyz(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 32400)] private bool AdvanceMouGyzChildren() => _pendingDecision is null && MouGyzObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 32400)]
    private void AssertMouGyz(ProgramSkillFrame f) { if (f.MouGuoYouZhi is not null && (!ValidMouGyz(f) || f.MouGuoYouZhi.Native is { } r && !ValidMouGyzNative(r, f, r.UseId > 0))) throw new InvalidOperationException("Guo You Zhi lost its real gift, original logical discard or exact native return."); }
}
