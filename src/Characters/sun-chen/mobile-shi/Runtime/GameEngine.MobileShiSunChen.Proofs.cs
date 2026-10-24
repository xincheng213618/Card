namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidShiSc(ProgramSkillFrame f) => f.ShiSunChen is { } r && f.InstructionIndex == 1 && r.Source == ShiScSource(f) && r.Hash == f.GameplayHash && r.Turn == _turnNumber && r.TurnOwner == _turnProgression.OwnerSeat &&
        r.WindowId == f.WindowContext?.ParentFrameId && r.Before >= 0 && r.After >= r.Before && r.After <= _movementSequence && Enum.IsDefined(r.Stage) &&
        ProgramEventHistory<ShiScStartedEvent>().Count(e => e == new ShiScStartedEvent(f.Id, r.Source, r.Hash, r.Op, r.Turn, r.TurnOwner, r.PhaseId, r.WindowId, r.UseId, r.ActionId)) == 1;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 49100)] private void RequireShiSc(ProgramSkillFrame f)
    {
        if (f.ShiSunChen is not { } r) return;
        if (!ValidShiSc(f) || r.Cards.Distinct().Count() != r.Cards.Count || r.Participants.Distinct().Count() != r.Participants.Count || r.Targets.Distinct().Count() != r.Targets.Count ||
            r.Participants.Concat(r.Targets).Any(s => !IsValidPlayerSeat(s)) || r.Votes.Count > r.Participants.Count || !r.Votes.Select(v => v.Seat).SequenceEqual(r.Participants.Take(r.Votes.Count)) ||
            r.Cursor < 0 || r.Ungiven < 0 || r.Ungiven > r.Participants.Count || r.Stage == ShiScStage.Cards && (r.Cards.Any(id => !ShiScOwnedChoices(f, f.OwnerSeat, true).Any(c => c.Cards.SequenceEqual([id]))) ||
                r.Cards.Select(id => EffectiveSuit(_players[f.OwnerSeat], GetAdvancedCard(id))).Distinct().Count() != r.Cards.Count))
            throw new InvalidOperationException("Shi Sun Chen lost the original source, native completed use, unpaid distinct-suit materials or simultaneous chooser order.");
        if (r.Op == SkillProgramEffectOp.ResolveMobileShiSunChenNigu)
        {
            if (f.TriggerId is not null || r.WindowId is not null || r.UseId != 0 || r.ActionId != 0 || r.PhaseId != _cardUseDebitPhaseInstanceId ||
                !ProgramEventHistory<ShiScParticipantEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).Select(e => e.Seat).SequenceEqual(r.Participants) ||
                r.Stage == ShiScStage.Votes && ProgramEventHistory<ShiScVoteRevealedEvent>().Any(e => e.FrameId == f.Id) ||
                r.Stage is ShiScStage.Gifts or ShiScStage.GiveCard or ShiScStage.GiftChildren && (r.Votes.Count != r.Participants.Count || r.Cursor > r.Participants.Count ||
                    ProgramEventHistory<ShiScVoteRevealedEvent>().Count(e => e.FrameId == f.Id) != r.Participants.Count))
                throw new InvalidOperationException("Nigu cannot reveal incomplete private commitments or move another actual-phase obligation.");
        }
        else if (r.Op != SkillProgramEffectOp.ResolveMobileShiSunChenLulian || !ShiScSourceWindow(f) || f.WindowContext is not { } w || ShiScCompleted(w, f.OwnerSeat) is not { } use || use.NativeId != r.UseId || use.Action.ActionId != r.ActionId ||
            ProgramEventHistory<ShiScLulianBoundEvent>().Count(e => e == new ShiScLulianBoundEvent(f.Id, r.UseId, r.ActionId, r.Chain, r.Draw, r.Targets.Count)) != 1 ||
            !ProgramEventHistory<ShiScTargetEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).Select(e => e.Seat).SequenceEqual(r.Targets))
            throw new InvalidOperationException("Lulian lost its frozen completed-use identity, original target set or independent branch qualification.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && r.Stage is ShiScStage.Cards or ShiScStage.Votes or ShiScStage.GiveCard or ShiScStage.Momentum &&
            f.Step == ResolutionFrameStep.AwaitingResponse && (_pendingDecision is not { } p || p.PlayerSeat != ShiScChooser(f) || !AssistedChoicesEqual(p.Choices, ShiScChoices(f))))
            throw new InvalidOperationException("Shi Sun Chen lost its exact published private chooser or mandatory momentum choices.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 49100)]
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 49100)]
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 49100)]
    private bool ShiScMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) => ValidShiSc(f) && f.ShiSunChen is { Stage: ShiScStage.DiscardChildren or ShiScStage.GiftChildren or ShiScStage.DrawChildren } r &&
        effect?.Op == r.Op && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool ShiScFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidShiSc(f) || f.ShiSunChen is not { } r) return false;
        if (child is NativeDrawAttemptFrame draw) return r.Stage == ShiScStage.Drawing && draw.Context.RecipientSeat == f.OwnerSeat && draw.Context.RequestedCount == 1 && NativeDrawAttemptRidesOn(draw, f);
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } p && ShiScMovementProof(f, GetPausedPrivateOfferEffect(f), p) && ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.ParentFrameId == f.Id && moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m));
        if (child is RecoveryReplacementFrame recovery) return r.Stage is ShiScStage.DiscardChildren or ShiScStage.GiftChildren && RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && _cardMovements.Any(m => m.Sequence > r.Before && m.Sequence <= r.After && m.CardKind == CardKind.SilverLion && m.From.Zone == CardZoneKind.Equipment && m.From.OwnerSeat == recovery.Attempt.TargetSeat);
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement && r.Stage is ShiScStage.DiscardChildren or ShiScStage.GiftChildren;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool ShiScChildEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) || TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) ||
        _resolutionStack[i - 1] is ProgramSkillFrame f && ShiScFirstChild(f, _resolutionStack[i]) || NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private bool ShiScChildSuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, d) ||
        HasObserverSuffix(n, static (e, i) => e.ShiScChildEdge(i), static (e, i, dying) => e.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? ShiScObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.ShiScFirstChild(f, child), static (e, i) => e.ShiScChildSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 49100)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 49100)]
    private bool ShiScDamageObserver(long id) => ShiScObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 49100)] private bool ShiScBeforeDamageObserver(long id) => ShiScObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 49100)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 49100)] private bool ShiScDyingObserver() => ActiveDying is { } d && ShiScObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 49100)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 49100)] private bool ShiScRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (ShiScFirstChild(f, recovery)) return true; var at = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return at >= 0 && child > at + 1 && ShiScFirstChild(f, _resolutionStack[at + 1]) && ShiScChildSuffix(at) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 49100)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 49100)]
    private bool ShiScNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess) return false;
        if (f.ShiSunChen is { Stage: ShiScStage.DamageChildren, Chain: true, Draw: true } r && ValidShiSc(f)) return source is null && nature == DamageNature.Fire && target == r.Target && amount == 1 && ProgramEventHistory<ShiScMomentumIssuedEvent>().Count(e => e.FrameId == f.Id && e.Target == target) == 1;
        if (ShiScObserverRoot() is not { } root || root.Id == f.Id) return false; var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature && target == (effect.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 49100)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 49100)] private bool KeepShiScPaid(ProgramSkillFrame f) => f.ShiSunChen is not null && ValidShiSc(f);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 49100)] private bool AdvanceShiScChildren() => _pendingDecision is null && ShiScObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
