namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,36100)] private bool MouLuSuMovement(ProgramSkillFrame f,SkillProgramEffect? op,ProgramMovementContinuation p) => f.MouLuSuAnniversary is { } r && op?.Op == r.Operation && MouLuSuAnniversary.Valid(f) && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool MouLuSuFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if (!MouLuSuAnniversary.Valid(f) || f.MouLuSuAnniversary is not { } r) return false;
        if(child is CardDeclarationFrame declaration)return MatchesMouLuSuPlayedDeclaration(f,declaration);
        if (NativeDrawAttemptRidesOn(child,f)) return r.Stage == MouLuSuAnniversaryStage.Drawing && f.NativeDraw is not null;
        if (child is CardUseFrame use) return r.Stage == MouLuSuAnniversaryStage.NativeUse && use.Id == r.UseId && MouLuSuNativeMatches(use);
        if (child is DrawFundedDistinctBasicFrame funded) return IsMouLuSuPaymentFrame(funded);
        if (child is ChainedStateBasicFrame chained) return IsMouLuSuPaymentFrame(chained);
        if (child is ProgramSkillFrame { EquipmentSlotNamedUse: { } slot }) return MouLuSuSlotParent(slot)?.Id == f.Id;
        if (child is ProgramCardTriggerWindowFrame card) return card.MouLuSuAnniversaryPlayed is { } back && back.FrameId == f.Id && back.ActionId == r.ActionId && card.ParentFrameId == f.Id && r.Response?.ActionId == card.Action.ActionId && back.Window == (r.Stage == MouLuSuAnniversaryStage.PlayedAccepted ? SkillProgramTriggerWindow.CardResponseAccepted : r.Stage == MouLuSuAnniversaryStage.PlayedCompleted ? SkillProgramTriggerWindow.CardResponseCompleted : (SkillProgramTriggerWindow)(-1));
        if (child is PublicHandRevealWindowFrame reveal) return r.Stage == MouLuSuAnniversaryStage.Recipient && reveal.ParentFrameId == f.Id && reveal.ReturnKind == PublicHandRevealReturnKind.Registered && reveal.ReturnKey == "mou-lu-su-mingshi-three" && IsPublicHandRevealProducerSuspended(f.Id,reveal.ReturnKey);
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && MouLuSuMovement(f,GetPausedPrivateOfferEffect(f),p)) return ProgramMovementReturnMatches(f,moved) && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        if (r.Stage == MouLuSuAnniversaryStage.HpChildren && ProgramEventHistory<MouLuSuAnniversaryHpIssuedEvent>().Count(e => e.FrameId == f.Id && e.Cursor == r.Cursor - 1 && e.Target == r.Other && !e.Recovery) == 1) return DyingSuitsStructuralEdge(f,child);
        if (r.Stage == MouLuSuAnniversaryStage.RecoveryChildren)
        {
            var target = (r.Cursor - 1) % 2 == 0 ? f.OwnerSeat : r.Other;
            if (ProgramEventHistory<MouLuSuAnniversaryHpIssuedEvent>().Count(e => e.FrameId == f.Id && e.Cursor == r.Cursor - 1 && e.Target == target && e.Recovery) != 1) return false;
            if (child is RecoveryReplacementFrame rr) return RecoveryReplacementFrameRidesOn(rr,f) && rr.Attempt.Completion.Producer == RecoveryAttemptProducer.Program && rr.Attempt.SourceSeat == f.OwnerSeat && rr.Attempt.TargetSeat == target && rr.Attempt.Amount == 1;
            if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Change.TargetSeat == target && hp.Change.Kind == HpChangeKind.Recovery;
        }
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouLuSuEdge(int i) => MouLuSuPlayedDeclarationEdge(i) || IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) || TypedDamagePostEventEdge(_resolutionStack[i - 1],_resolutionStack[i]) || _resolutionStack[i - 1] is ProgramSkillFrame f && MouLuSuFirstChild(f,_resolutionStack[i]) || NativeDrawAttemptRidesOn(_resolutionStack[i],_resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i],_resolutionStack[i - 1]) || DyingSuitsStructuralEdge(_resolutionStack[i - 1],_resolutionStack[i]);
    private bool MouLuSuSuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1,d) || HasObserverSuffix(n,static(g,i) => g.MouLuSuEdge(i),static(g,i,d) => g.IsSuspendedOrPaidRescueObserverSuffix(i,d));
    private ProgramSkillFrame? MouLuSuRoot() => FindProgramObserverRoot(static(g,f,c) => g.MouLuSuFirstChild(f,c),static(g,n) => g.MouLuSuSuffix(n));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,36100)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime,36100)] private bool MouLuSuDying() => ActiveDying is { } d && MouLuSuRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,36100)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,36100)] private bool MouLuSuDamageWindow(long id) => MouLuSuRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,36100)] private bool MouLuSuBeforeDamage(long id) => MouLuSuRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts,36100)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts,36100)] private bool MouLuSuNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || MouLuSuRoot() is not { } root || root.Id == f.Id) return false; var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature && target == (e.TargetReference is { } p ? ResolveProgramParticipant(f,p) : ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor,36100)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor,36100)] private bool MouLuSuRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame rr)
    {
        if (MouLuSuFirstChild(f,rr)) return true; var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == rr.Id); return n >= 0 && c > n + 1 && MouLuSuFirstChild(f,_resolutionStack[n + 1]) && MouLuSuSuffix(n) && RecoveryReplacementFrameRidesOn(rr,_resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,36100)] private bool MouLuSuAdvanceChildren() => _pendingDecision is null && MouLuSuRoot() is not null && TryAdvanceObserverSubtree(true);
}
