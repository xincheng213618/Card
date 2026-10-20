namespace CardGame.Core;

[ProgramRuntimeModule]
internal sealed partial class NativeHealthRecoveryRuntime(
    INativeHealthRecoveryFacts facts, INativeHealthRecoveryProtocols protocols,
    IProgramRuntimeFrames frames, IProgramRuntimeEvents events)
{
    internal void RecordNativeCardRecovery(long useId, long recoveryId, int source, int target,
        CardKind kind, int requested, int before, int after)
    {
        var receipt = new NativeCardRecoveryReceipt(recoveryId, source, target, kind, requested, before, after);
        var index = facts.FrameIndex(useId);
        if (index < 0 || facts.FrameAt(index) is not CardUseFrame use || use.SourceSeat != source ||
            use.CardKind != kind || !use.TargetSeats.Contains(target) ||
            frames.Top is not RecoveryFrame recovery || recovery.Id != recoveryId ||
            recovery.ParentFrameId != useId || recovery.SourceSeat != source || recovery.TargetSeat != target ||
            recovery.Amount != requested || recovery.HpBefore != before)
            throw new InvalidOperationException("Native card recovery lost its actual use and recovery child.");
        frames.Replace(use.Id, use with { RecoveryApplication = receipt });
        events.AdvanceFact(new NativeCardRecoveryAppliedEvent(use.Id, receipt));
    }

    internal bool TryGetNativeRescueRecovery(int hpIndex, IReadOnlyList<IGameEvent> history, out DyingFrame? dying)
    {
        dying = null;
        if (hpIndex < 2 || facts.FrameAt(hpIndex) is not HpChangedTriggerWindowFrame hp ||
            facts.FrameAt(hpIndex - 1) is not CardUseFrame { DyingResponse: { } response, RecoveryApplication: { } receipt } use ||
            facts.FrameAt(hpIndex - 2) is not DyingFrame original || original.Id != response.ResolutionId ||
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
            !RuntimeSkills.RuntimeRules_Core_NativeRecoveryDamage.NativeRecoveryMaterialGroupMatches(use))
            return false;
        var originalIndex = hpIndex - 2;
        if (original.Continuation == DyingContinuationKind.Damage)
        {
            if (originalIndex < 1 || facts.FrameAt(originalIndex - 1) is not DamageFrame damage ||
                damage.Id != original.ParentFrameId || damage.TargetSeat != original.VictimSeat) return false;
        }
        else if (original.Continuation is not (DyingContinuationKind.ProgramSkill or DyingContinuationKind.AttackHpLoss)) return false;
        dying = original;
        return true;
    }

    internal bool NativeCardRecoveryReturnMatches(CardUseFrame use, HpChangedTriggerWindowFrame hp,
        IReadOnlyList<IGameEvent> history)
    {
        if (use.RecoveryApplication is not { } receipt || use.CardKind is not (CardKind.Peach or CardKind.Alcohol) ||
            use.CardKind != receipt.CardKind || use.SourceSeat != receipt.SourceSeat || !use.TargetSeats.Contains(receipt.TargetSeat) ||
            hp.Id != hp.Change.Id || hp.ResumeFrameId != use.Id || hp.Change.ParentFrameId != use.Id ||
            hp.Continuation != PostEventContinuation.CardUse || hp.CardId != use.CardId || hp.CardKind != use.CardKind ||
            hp.Change.Kind != HpChangeKind.Recovery || hp.Change.SourceSeat != receipt.SourceSeat || hp.Change.TargetSeat != receipt.TargetSeat ||
            hp.Change.HpBefore != receipt.HpBefore || hp.Change.HpAfter != receipt.HpAfter || hp.Change.Amount <= 0 ||
            hp.Change.Amount != receipt.HpAfter - receipt.HpBefore || hp.Change.Amount > receipt.RequestedAmount ||
            !RuntimeSkills.RuntimeRules_Core_NativeRecoveryDamage.NativeRecoveryMaterialGroupMatches(use))
            return false;
        return history.OfType<NativeCardRecoveryAppliedEvent>().Count(e => e.CardUseFrameId == use.Id && e.Receipt == receipt) == 1;
    }

    internal bool ExactPaidNativeRecoveryUseEdge(ResolutionFrame parent, ResolutionFrame child, IReadOnlyList<IGameEvent> history) =>
        parent is ProgramSkillFrame && child is CardUseFrame { HulaoPeachOrigin: { } origin } use &&
        protocols.HulaoActivityFrameRidesOn(use, parent) && history.OfType<HulaoPeachIssuedEvent>().Count(e => e.Origin == origin) == 1;

    internal bool ExactPaidNativeRecoveryReplacementEdge(ResolutionFrame parent, ResolutionFrame child,
        IReadOnlyList<IGameEvent> history)
    {
        if (parent is not CardUseFrame { HulaoPeachOrigin: not null } use || child is not RecoveryReplacementFrame recovery ||
            !protocols.RecoveryReplacementFrameRidesOn(recovery, use) || recovery.Return != new RecoveryReplacementReturn(PostEventContinuation.CardUse, use.Id) ||
            recovery.Attempt.Completion is not { Producer: RecoveryAttemptProducer.CardUse } completion ||
            completion.CardId != use.CardId || completion.CardKind != use.CardKind || recovery.Attempt.SourceSeat != use.SourceSeat ||
            !use.TargetSeats.Contains(recovery.Attempt.TargetSeat) || recovery.Attempt.Amount <= 0 ||
            !protocols.TypedRecoveryApplicationBoundsMatch(recovery, history)) return false;
        protocols.ValidateHulaoPeachUse(use);
        return true;
    }

    // Hide only the original dying token while its exact paid rescue's recovery
    // counter owns a real attack. Inner victims retain normal dying/death flow.
    internal bool IsOriginalDyingSuspendedByRecoveryDamage(DyingFrame dying)
    {
        foreach (var frame in Enumerable.Range(0, facts.FrameCount).Select(facts.FrameAt).OfType<ProgramSkillFrame>())
        {
            if (frame.AttackAttempt is null || frame.AttackReturn?.RecoveryOrigin is not
                { SuspendedDyingFrameId: { } id } origin || id != dying.Id) continue;
            var index = facts.FrameIndex(frame.Id);
            if (index > 0 && TryGetNativeRescueRecovery(index - 1, facts.CompleteProgramEventHistory().ToArray(), out var original) &&
                original?.Id == id && frame.WindowContext?.ParentFrameId == origin.HpWindowId &&
                facts.FrameAt(index - 1) is HpChangedTriggerWindowFrame hp && protocols.TypedDamagePostEventEdge(hp, frame)) return true;
        }
        return false;
    }
}
