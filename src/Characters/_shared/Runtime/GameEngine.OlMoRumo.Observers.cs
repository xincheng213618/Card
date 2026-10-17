namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool OlMoRumoFirstChild(ProgramSkillFrame frame, ResolutionFrame child)
    {
        if (frame.OlMoRumo is not { } receipt) return false;
        AssertOlMoRumo(frame);
        if (child is IdentityAppearanceFrame appearance)
            return (receipt.Stage == OlMoRumoStage.LossIssuing || receipt.Stage == OlMoRumoStage.LossChildren &&
                    receipt.LossReturned && receipt.AppearancePrevented &&
                    ProgramEventHistory<IdentityHpDeductionPreventedEvent>().Count(item => item.AppearanceFrameId == appearance.Id &&
                        item.OwnerSeat == frame.OwnerSeat && item.Entry == IdentityAppearanceEntry.ProgramHpLoss && item.RequestedDeduction == 1) == 1) && !receipt.HadRoundDamage &&
                appearance.Receipt.Entry == IdentityAppearanceEntry.ProgramHpLoss &&
                appearance.Receipt.ParentFrameId == frame.Id && appearance.Receipt.OwnerSeat == frame.OwnerSeat &&
                appearance.Receipt.RequestedDeduction == 1 && appearance.Receipt.OriginalInstructionIndex == frame.InstructionIndex;
        if (child is HpChangedTriggerWindowFrame hp)
            return receipt.Stage == OlMoRumoStage.LossChildren && receipt.LossReturned && !receipt.HadRoundDamage &&
                hp.ResumeFrameId == frame.Id && hp.Change.ParentFrameId == frame.Id &&
                hp.Continuation == PostEventContinuation.Program && hp.Change.Kind == HpChangeKind.Loss &&
                hp.Change.SourceSeat is null && hp.Change.TargetSeat == frame.OwnerSeat &&
                hp.Change.Amount == receipt.ActualLoss && hp.Change.HpAfter == receipt.HpAfter &&
                hp.Change.HpBefore - hp.Change.HpAfter == hp.Change.Amount;
        if (child is DyingFrame dying)
            return receipt.Stage == OlMoRumoStage.LossChildren && receipt.LossReturned && !receipt.HadRoundDamage &&
                receipt.HpAfter == 0 && dying.ParentFrameId == frame.Id && dying.VictimSeat == frame.OwnerSeat &&
                dying.KillerSeat is null && dying.Continuation == DyingContinuationKind.ProgramSkill;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == frame.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == frame.Id &&
            state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
            ProgramEventHistory<CharacterStateChangedEvent>().Any(item => item.Change.Id == state.Id &&
                item.Change.ParentFrameId == frame.Id && item.Change.Window == state.Window);
    }

    private bool OlMoRumoLifecycleEdge(int index) =>
        index > 0 && _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame window &&
        _resolutionStack[index] is ProgramSkillFrame frame && window.CandidateIndex >= 0 &&
        window.CandidateIndex < window.Candidates.Count &&
        MountObserverCandidateMatches(frame, window.Candidates[window.CandidateIndex]) &&
        frame.WindowContext is { } context && context.ParentFrameId == window.Id && context.Window == window.Window;

    private bool OlMoRumoObserverSuffix(int index) => HasObserverSuffix(index,
        static (engine, next) => engine.IsMovementObserverEdge(next) || engine.IsSameNameHandObserverEdge(next) ||
            engine.OlMoRumoLifecycleEdge(next) || engine.TypedDamagePostEventEdge(engine._resolutionStack[next - 1], engine._resolutionStack[next]) ||
            NativeDrawAttemptRidesOn(engine._resolutionStack[next], engine._resolutionStack[next - 1]) ||
            engine.RecoveryReplacementFrameRidesOn(engine._resolutionStack[next], engine._resolutionStack[next - 1]) ||
            engine.IsYanPuDyingExitEdge(next),
        static (engine, next, dying) => engine.IsSuspendedOrPaidRescueObserverSuffix(next, dying));

    private ProgramSkillFrame? OlMoRumoObserverRoot() => FindProgramObserverRoot(
        static (engine, frame, child) => engine.OlMoRumoFirstChild(frame, child),
        static (engine, index) => engine.OlMoRumoObserverSuffix(index));

    private bool HasOlMoRumoDamageObserver(long frameId) => OlMoRumoObserverRoot() is { } root &&
        _resolutionStack.FindIndex(item => item.Id == frameId && item is DamageTriggerWindowFrame) >
            _resolutionStack.FindIndex(item => item.Id == root.Id);
    private bool HasOlMoRumoBeforeDamageObserver(long frameId) => OlMoRumoObserverRoot() is { } root &&
        _resolutionStack.FindIndex(item => item.Id == frameId && item is BeforeDamageProgramWindowFrame) >
            _resolutionStack.FindIndex(item => item.Id == root.Id);
    private bool IsOlMoRumoDying() => ActiveDying is { } dying && OlMoRumoObserverRoot() is { } root &&
        _resolutionStack.FindIndex(item => item.Id == dying.Id) > _resolutionStack.FindIndex(item => item.Id == root.Id);

    private bool AllowsOlMoRumoNestedDamage(ProgramSkillFrame frame, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (frame.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != frame.Id ||
            OlMoRumoObserverRoot() is not { } root || root.Id == frame.Id) return false;
        var effect = GetPausedPrivateOfferEffect(frame);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount &&
            effect.ActorReference == source && effect.DamageNature == nature && !sourceLess &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(frame, reference) :
                ResolveProgramEffectTarget(frame, effect.Target));
    }

    private bool IsOlMoRumoRecoveryParent(ProgramSkillFrame frame, RecoveryReplacementFrame recovery)
    {
        if (frame.OlMoRumo is null) return false;
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        var child = _resolutionStack.FindIndex(item => item.Id == recovery.Id);
        return index >= 0 && child > index + 1 && OlMoRumoFirstChild(frame, _resolutionStack[index + 1]) &&
            OlMoRumoObserverSuffix(index) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 28940)]
    private bool AdvanceOlMoRumoChildren() => _pendingDecision is null && OlMoRumoObserverRoot() is not null &&
        TryAdvanceObserverSubtree(true);
}
