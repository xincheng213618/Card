namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Admission is a proof of the actual recovery invoice and paused damage
    // instruction. An AfterHpRecovered label alone grants no nested attack.
    private bool TryDescribeRecoveryDamage(ProgramSkillFrame frame, int target, int amount,
        ProgramParticipantReference? sourceReference, DamageNature? nature, bool sourceLess,
        int? explicitSourceSeat, out RecoveryDamageOrigin? origin, out long? damageWindowId)
    {
        origin = null;
        damageWindowId = null;
        if (sourceLess || explicitSourceSeat is not null || frame.WindowContext is not
            { Window: SkillProgramTriggerWindow.AfterHpRecovered, HpChange: { Kind: HpChangeKind.Recovery } change } ||
            change.TargetSeat != frame.OwnerSeat || change.Amount <= 0 ||
            change.HpAfter != change.HpBefore + change.Amount) return false;
        var frameIndex = _resolutionStack.FindLastIndex(f => f.Id == frame.Id);
        if (frameIndex < 2 || _resolutionStack[frameIndex - 1] is not HpChangedTriggerWindowFrame hp ||
            hp.Id != change.Id || hp.Change != change || !TypedDamagePostEventEdge(hp, frame)) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var nativeRescue = TryGetNativeRescueRecovery(frameIndex - 1, history, out var rescuedDying);
        if ((!nativeRescue && !ExactRecoveryProducerEdge(frameIndex - 1, history)) ||
            !PausedRecoveryDamageMatches(frame, target, amount, sourceReference, nature)) return false;
        var windowIndex = frameIndex - 2;
        while (windowIndex >= 0 && _resolutionStack[windowIndex] is not DamageTriggerWindowFrame) windowIndex--;
        if (nativeRescue)
        {
            // The card invoice proves this exceptional edge directly back to
            // the exact still-suspended dying token and its attack owner.
            damageWindowId = windowIndex >= 0 ? _resolutionStack[windowIndex].Id : null;
        }
        else if (windowIndex >= 0)
        {
            var window = (DamageTriggerWindowFrame)_resolutionStack[windowIndex];
            if (windowIndex + 1 >= frameIndex || _resolutionStack[windowIndex + 1] is not ProgramSkillFrame root ||
                !DamageProgramMatchesWindow(root, window)) return false;
            for (var index = windowIndex + 2; index <= frameIndex; index++)
            {
                var parent = _resolutionStack[index - 1];
                var child = _resolutionStack[index];
                if (child is HpChangedTriggerWindowFrame)
                {
                    if (!ExactRecoveryProducerEdge(index, history)) return false;
                }
                else if (ExactPaidNativeRecoveryUseEdge(parent, child, history) ||
                    ExactPaidNativeRecoveryReplacementEdge(parent, child, history)) continue;
                else if (parent is ProgramSkillFrame producer && child is RecoveryReplacementFrame recovery)
                {
                    if (!ProgramRecoveryReplacementReceiptMatches(producer, recovery, history)) return false;
                }
                else if (!TypedDamagePostEventEdge(parent, child)) return false;
            }
            damageWindowId = window.Id;
        }
        else if (_resolutionStack.Take(frameIndex).Any(f => f is ProgramSkillFrame { AttackAttempt: not null } or
            ProgramSkillFrame { CardAttack.Active: true } or CardUseFrame { CardAttack.Active: true } or
            JudgmentFrame { CardAttack.Active: true }))
            return false;
        origin = new(hp.Id, frame.InstructionIndex,
            sourceReference is { } reference ? ResolveProgramParticipant(frame, reference) : frame.OwnerSeat,
            target, amount, nature ?? DamageNature.Normal)
            { SuspendedDyingFrameId = nativeRescue ? rescuedDying!.Id : null };
        return true;
    }

    private bool ExactRecoveryProducerEdge(int hpIndex, IReadOnlyList<IGameEvent> history)
    {
        if (hpIndex < 1 || _resolutionStack[hpIndex] is not HpChangedTriggerWindowFrame hp ||
            hp.Change.Kind != HpChangeKind.Recovery || hp.Change.Amount <= 0) return false;
        var parent = _resolutionStack[hpIndex - 1];
        if (parent is CardUseFrame use) return NativeCardRecoveryReturnMatches(use, hp, history);
        if (parent is RecoveryReplacementFrame recovery)
            return TypedRecoveryApplicationBoundsMatch(recovery, history) && TypedRecoveryHpReturnMatches(recovery, hp, history);
        return parent is ProgramSkillFrame producer && ProgramRecoveryReceiptMatches(producer, hp, history);
    }

    private bool PausedRecoveryDamageMatches(ProgramSkillFrame frame, int target, int amount,
        ProgramParticipantReference? sourceReference, DamageNature? nature)
    {
        var effect = TypedRecoveryPausedEffect(frame);
        if (effect?.CompiledInstruction is DamageProgramInstruction instruction)
            return instruction.Amount == amount && instruction.SourceReference == sourceReference &&
                (instruction.Nature ?? DamageNature.Normal) == (nature ?? DamageNature.Normal) &&
                (instruction.TargetReference is { } reference ? ResolveProgramParticipant(frame, reference) :
                    ResolveProgramEffectTarget(frame, effect.Target)) == target;
        if (effect is not { Op: SkillProgramEffectOp.DamageParticipants, Target: SkillProgramEffectTarget.SelectedTargets,
                TargetKind: null } || sourceReference is not null || !frame.ReexecuteParticipantInstruction ||
            (effect.DamageNature ?? DamageNature.Normal) != (nature ?? DamageNature.Normal)) return false;
        var key = "participant-cursor-" + (frame.InstructionIndex - 1);
        var cursors = frame.NumberBindings.Where(b => b.Name == key).ToArray();
        return cursors is [var cursor] && cursor.Value >= 1 && cursor.Value <= frame.SelectedTargetSeats.Count &&
            frame.SelectedTargetSeats[cursor.Value - 1] == target &&
            amount == (cursor.Value == 1 ? effect.Amount : effect.MinimumValue);
    }

    private void AssertRecoveryDamageOrigin(ProgramSkillFrame frame)
    {
        if (frame.AttackReturn?.RecoveryOrigin is not { } origin) return;
        var effect = TypedRecoveryPausedEffect(frame);
        var sourceReference = effect?.CompiledInstruction is DamageProgramInstruction instruction ? instruction.SourceReference : null;
        if (frame.AttackAttempt is not { } attack || frame.InstructionIndex != origin.InstructionIndex ||
            attack.SourceSeat != origin.SourceSeat || frame.WindowContext?.ParentFrameId != origin.HpWindowId ||
            !TryDescribeRecoveryDamage(frame, origin.TargetSeat, origin.Amount, sourceReference, origin.Nature,
                false, null, out var described, out var damageWindowId) || described != origin ||
            damageWindowId != frame.AttackReturn.ParentDamageWindowFrameId)
            throw new InvalidOperationException("Recovery damage lost its exact HP invoice, issued instruction or suspended damage occurrence.");
    }
}
