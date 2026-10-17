namespace CardGame.Core;

public sealed record NativeCardRecoveryReceipt(long RecoveryFrameId, int SourceSeat, int TargetSeat,
    CardKind CardKind, int RequestedAmount, int HpBefore, int HpAfter);
public sealed record NativeCardRecoveryAppliedEvent(long CardUseFrameId, NativeCardRecoveryReceipt Receipt) : IGameEvent;

public sealed partial class GameEngine
{
    private void RecordNativeCardRecovery(long useId, long recoveryId, int source, int target,
        CardKind kind, int requested, int before, int after)
    {
        var receipt = new NativeCardRecoveryReceipt(recoveryId, source, target, kind, requested, before, after);
        var index = _resolutionStack.FindIndex(f => f.Id == useId);
        if (index < 0 || _resolutionStack[index] is not CardUseFrame use || use.SourceSeat != source ||
            use.CardKind != kind || !use.TargetSeats.Contains(target) ||
            _resolutionStack.LastOrDefault() is not RecoveryFrame recovery || recovery.Id != recoveryId ||
            recovery.ParentFrameId != useId || recovery.SourceSeat != source || recovery.TargetSeat != target ||
            recovery.Amount != requested || recovery.HpBefore != before)
            throw new InvalidOperationException("Native card recovery lost its actual use and recovery child.");
        ReplaceRuntimeFrame(use.Id, use with { RecoveryApplication = receipt });
        AdvanceEventRulesAndQueueFact(new NativeCardRecoveryAppliedEvent(use.Id, receipt));
    }

    private bool TryGetNativeRescueRecovery(int hpIndex, IReadOnlyList<IGameEvent> history, out DyingFrame? dying)
    {
        dying = null;
        if (hpIndex < 2 || _resolutionStack[hpIndex] is not HpChangedTriggerWindowFrame hp ||
            _resolutionStack[hpIndex - 1] is not CardUseFrame { DyingResponse: { } response, RecoveryApplication: { } receipt } use ||
            _resolutionStack[hpIndex - 2] is not DyingFrame original || original.Id != response.ResolutionId ||
            response.ResponderSeat != use.SourceSeat || original.ResponderSeat != use.SourceSeat ||
            original.VictimSeat != receipt.TargetSeat || use.TargetSeats is not [var target] || target != original.VictimSeat ||
            use.CardKind != receipt.CardKind || use.SourceSeat != receipt.SourceSeat ||
            hp.Continuation != PostEventContinuation.CardUse || hp.ResumeFrameId != use.Id || hp.Change.ParentFrameId != use.Id ||
            hp.CardId != use.CardId || hp.CardKind != use.CardKind || hp.Id != hp.Change.Id ||
            hp.Change.Kind != HpChangeKind.Recovery || hp.Change.SourceSeat != receipt.SourceSeat ||
            hp.Change.TargetSeat != receipt.TargetSeat || hp.Change.HpBefore != receipt.HpBefore ||
            hp.Change.HpAfter != receipt.HpAfter || hp.Change.Amount <= 0 ||
            hp.Change.Amount != receipt.HpAfter - receipt.HpBefore || hp.Change.Amount > receipt.RequestedAmount ||
            receipt.HpBefore > 0 ||
            !(receipt.CardKind == CardKind.Peach && response.UsedPeach && !response.UsedAlcohol && response.PeachCardId == use.CardId ||
              receipt.CardKind == CardKind.Alcohol && response.UsedAlcohol && !response.UsedPeach && response.AlcoholCardId == use.CardId && use.SourceSeat == original.VictimSeat) ||
            history.OfType<NativeCardRecoveryAppliedEvent>().Count(e => e.CardUseFrameId == use.Id && e.Receipt == receipt) != 1 ||
            !NativeRecoveryMaterialGroupMatches(use))
            return false;
        var originalIndex = hpIndex - 2;
        if (original.Continuation == DyingContinuationKind.Damage)
        {
            if (originalIndex < 1 || _resolutionStack[originalIndex - 1] is not DamageFrame damage ||
                damage.Id != original.ParentFrameId || damage.TargetSeat != original.VictimSeat) return false;
        }
        else if (original.Continuation is not (DyingContinuationKind.ProgramSkill or DyingContinuationKind.AttackHpLoss)) return false;
        dying = original;
        return true;
    }

    private bool NativeCardRecoveryReturnMatches(CardUseFrame use, HpChangedTriggerWindowFrame hp,
        IReadOnlyList<IGameEvent> history)
    {
        if (use.RecoveryApplication is not { } receipt || use.CardKind is not (CardKind.Peach or CardKind.Alcohol) ||
            use.CardKind != receipt.CardKind || use.SourceSeat != receipt.SourceSeat || !use.TargetSeats.Contains(receipt.TargetSeat) ||
            hp.Id != hp.Change.Id || hp.ResumeFrameId != use.Id || hp.Change.ParentFrameId != use.Id ||
            hp.Continuation != PostEventContinuation.CardUse || hp.CardId != use.CardId || hp.CardKind != use.CardKind ||
            hp.Change.Kind != HpChangeKind.Recovery || hp.Change.SourceSeat != receipt.SourceSeat || hp.Change.TargetSeat != receipt.TargetSeat ||
            hp.Change.HpBefore != receipt.HpBefore || hp.Change.HpAfter != receipt.HpAfter || hp.Change.Amount <= 0 ||
            hp.Change.Amount != receipt.HpAfter - receipt.HpBefore || hp.Change.Amount > receipt.RequestedAmount ||
            !NativeRecoveryMaterialGroupMatches(use))
            return false;
        return history.OfType<NativeCardRecoveryAppliedEvent>().Count(e => e.CardUseFrameId == use.Id && e.Receipt == receipt) == 1;
    }

    private static bool NativeRecoveryMaterialGroupMatches(CardUseFrame use)
    {
        if (use.Action is not { Type: CardActionType.Use } action || action.ActorSeat != use.SourceSeat ||
            action.EffectiveKind != use.CardKind || !action.TargetSeats.SequenceEqual(use.TargetSeats)) return false;
        var materials = action.PhysicalCards.Select(c => c.CardId).ToArray();
        // Empty virtual uses retain their own issuance protocol. A physical
        // conversion pays the complete ordered group, not a representative only.
        return materials.Length > 0 && materials.Distinct().Count() == materials.Length && materials[0] == use.CardId &&
            materials.SequenceEqual(use.PhysicalCardIds ?? [use.CardId]) &&
            action.PhysicalCards.All(c => c.From.OwnerSeat is null || c.From.OwnerSeat == action.ProviderSeat);
    }

    private bool ExactPaidNativeRecoveryUseEdge(ResolutionFrame parent, ResolutionFrame child, IReadOnlyList<IGameEvent> history) =>
        parent is ProgramSkillFrame && child is CardUseFrame { HulaoPeachOrigin: { } origin } use &&
        HulaoActivityFrameRidesOn(use, parent) && history.OfType<HulaoPeachIssuedEvent>().Count(e => e.Origin == origin) == 1;

    private bool ExactPaidNativeRecoveryReplacementEdge(ResolutionFrame parent, ResolutionFrame child,
        IReadOnlyList<IGameEvent> history)
    {
        if (parent is not CardUseFrame { HulaoPeachOrigin: not null } use || child is not RecoveryReplacementFrame recovery ||
            !RecoveryReplacementFrameRidesOn(recovery, use) || recovery.Return != new RecoveryReplacementReturn(PostEventContinuation.CardUse, use.Id) ||
            recovery.Attempt.Completion is not { Producer: RecoveryAttemptProducer.CardUse } completion ||
            completion.CardId != use.CardId || completion.CardKind != use.CardKind || recovery.Attempt.SourceSeat != use.SourceSeat ||
            !use.TargetSeats.Contains(recovery.Attempt.TargetSeat) || recovery.Attempt.Amount <= 0 ||
            !TypedRecoveryApplicationBoundsMatch(recovery, history)) return false;
        ValidateHulaoPeachUse(use);
        return true;
    }

    // Hide only the original dying token while its exact paid rescue's recovery
    // counter owns a real attack. Inner victims retain normal dying/death flow.
    private bool IsOriginalDyingSuspendedByRecoveryDamage(DyingFrame dying)
    {
        foreach (var frame in _resolutionStack.OfType<ProgramSkillFrame>())
        {
            if (frame.AttackAttempt is null || frame.AttackReturn?.RecoveryOrigin is not
                { SuspendedDyingFrameId: { } id } origin || id != dying.Id) continue;
            var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
            if (index > 0 && TryGetNativeRescueRecovery(index - 1, CompleteProgramEventHistory().ToArray(), out var original) &&
                original?.Id == id && frame.WindowContext?.ParentFrameId == origin.HpWindowId &&
                _resolutionStack[index - 1] is HpChangedTriggerWindowFrame hp && TypedDamagePostEventEdge(hp, frame)) return true;
        }
        return false;
    }
}
