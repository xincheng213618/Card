namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidShiLs(ProgramSkillFrame f) => f.ShiLuSu is { } r && f.InstructionIndex == 1 && r.Source == ShiLsSource(f) && r.Hash == f.GameplayHash &&
        r.Turn == _turnNumber && r.WindowId == f.WindowContext?.ParentFrameId && Enum.IsDefined(r.Stage) && r.Before >= 0 && r.After >= r.Before && r.After <= _movementSequence &&
        ProgramEventHistory<ShiLsStartedEvent>().Count(e => e == new ShiLsStartedEvent(f.Id, r.Source, r.Hash, r.Turn, r.WindowId)) == 1;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 48900)] private void RequireShiLs(ProgramSkillFrame f)
    {
        if (f.ShiLuSu is not { } r) return;
        if (!ValidShiLs(f) || r.Seats.Count > 2 || r.Seats.Distinct().Count() != r.Seats.Count || r.Seats.Any(s => !IsValidPlayerSeat(s)) ||
            r.Cards.Count != r.Cards.Distinct().Count() || r.Cards.Count > r.Required || r.Required < 0 || r.X < 0 ||
            r.GrantId > 0 && (f.TriggerId != "shared-last-hand" || f.WindowContext?.MovementBatch?.Id != r.BatchId ||
                ProgramEventHistory<ShiLsRefillIssuedEvent>().Count(e => e == new ShiLsRefillIssuedEvent(f.Id, r.GrantId, r.BatchId, r.X)) != 1) ||
            r.Stage == ShiLsStage.Discard && r.Cards.Any(id => !ShiLsDiscardChoices(f).Any(c => c.Cards.SequenceEqual([id]))))
            throw new InvalidOperationException("Shi Lu Su lost the original source, actual activation, authorized movement batch or unpaid exact cards.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && r.Stage is ShiLsStage.Target or ShiLsStage.Pair or ShiLsStage.Benefit or ShiLsStage.Discard &&
            (_pendingDecision is not { } p || p.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(p.Choices, ShiLsChoices(f))))
            throw new InvalidOperationException("Shi Lu Su lost its exact originally published choice set.");
    }
    private bool HasShiLsPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { ShiLuSu: not null, Step: ResolutionFrameStep.AwaitingResponse } f &&
        p.PlayerSeat == f.OwnerSeat && ValidShiLs(f) && AssistedChoicesEqual(p.Choices, ShiLsChoices(f));
    [ProgramCardLegality(ProgramCardLegalityStage.AfterLegalActionKind, 48900)] private bool ShiLsRecastBan(ProgramLegalActionQuery q) =>
        q.Action.Kind == LegalActionKind.Recast && q.Action.CardId is { } id && IsShiLsHandAccess(q.Actor, id);
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 48900)]
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 48900)]
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 48900)]
    private bool ShiLsMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => f.ShiLuSu is { } r && ValidShiLs(f) &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null &&
        r.Stage is ShiLsStage.ExchangeChildren or ShiLsStage.DiscardChildren or ShiLsStage.DrawChildren &&
        e?.Op is SkillProgramEffectOp.ResolveMobileShiLuSuHaoshi or SkillProgramEffectOp.ResolveMobileShiLuSuDimeng or SkillProgramEffectOp.ResolveMobileShiLuSuHaoshiRefill;
    private bool ShiLsFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidShiLs(f) || f.ShiLuSu is not { } r) return false;
        if (child is NativeDrawAttemptFrame draw) return r.Stage == ShiLsStage.Drawing && draw.Context.RecipientSeat == r.Target && draw.Context.RequestedCount == r.X && NativeDrawAttemptRidesOn(draw, f);
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } p && ShiLsMovementProof(f, GetPausedPrivateOfferEffect(f), p) &&
            ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= _movementSequence && _cardMovements.Contains(m));
        if (child is RecoveryReplacementFrame rec) return rec.ParentFrameId == f.Id && rec.Return.ResumeFrameId == f.Id && rec.Return.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CharacterStateContinuation == CharacterStateContinuation.Program);
    }
    private bool ShiLsChildSuffix(int i) => HasObserverSuffix(i, static (e, index) => e.IsSameNameHandObserverEdge(index),
        static (e, index, dying) => e.IsSuspendedOrPaidRescueObserverSuffix(index, dying) || e.TieredRoundZeroDyingRescueRide(index, dying) || e.DrawFundedDistinctBasicDyingRescueRide(index, dying) || e.HuanpeiDyingRescueRide(index, dying));
    private ProgramSkillFrame? ShiLsObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.ShiLsFirstChild(f, child), static (e, i) => e.ShiLsChildSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 48900)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 48900)]
    private bool ShiLsDamageChild(long id) => ShiLsObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 48900)] private bool ShiLsBeforeDamageChild(long id) => ShiLsObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 48900)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 48900)] private bool ShiLsDyingChild() => ActiveDying is { } d && ShiLsObserverRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 48900)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 48900)] private bool KeepShiLsPaid(ProgramSkillFrame f) => f.ShiLuSu is not null && ValidShiLs(f) ||
        f.ShiLuSu is null && f.TriggerId == "shared-last-hand" && f.InstructionIndex is 0 or 1 && f.WindowContext is { } w && IssuedShiLsRefill(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), w);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 48900)] private bool AdvanceShiLsChildren() => _pendingDecision is null && ShiLsObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    private IReadOnlyList<ProgramPublicRuleStateSnapshot> MobileShiLuSuPublicStates(CharacterState owner, string id)
    {
        if (id != ShiLsHaoshi) return [];
        return LiveShiLsGrants().Where(g => g.Source.OwnerSeat == owner.Seat).SelectMany(g => new[] {
            new ProgramPublicRuleStateSnapshot(g.Source.SkillInstanceId, "haoshi-beneficiary", ProgramPublicRuleStateKind.GameActivationProgress, g.Beneficiary),
            new ProgramPublicRuleStateSnapshot(g.Source.SkillInstanceId, "haoshi-refill", ProgramPublicRuleStateKind.ActivationLimit, 2, ProgramEventHistory<ShiLsRefillIssuedEvent>().Count(e => e.GrantId == g.GrantId), SkillUsageScope.Game)
        }).ToArray();
    }
}
