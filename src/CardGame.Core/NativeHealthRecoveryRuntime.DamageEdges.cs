namespace CardGame.Core;

internal sealed partial class NativeHealthRecoveryRuntime
{
    // Admission is a proof of the actual recovery invoice and paused damage
    // instruction. An AfterHpRecovered label alone grants no nested attack.
    internal bool TryDescribeRecoveryDamage(ProgramSkillFrame frame, int target, int amount,
        ProgramParticipantReference? sourceReference, DamageNature? nature, bool sourceLess,
        int? explicitSourceSeat, out RecoveryDamageOrigin? origin, out long? damageWindowId)
    {
        origin = null;
        damageWindowId = null;
        if (sourceLess || explicitSourceSeat is not null || frame.WindowContext is not
            { Window: SkillProgramTriggerWindow.AfterHpRecovered, HpChange: { Kind: HpChangeKind.Recovery } change } ||
            change.TargetSeat != frame.OwnerSeat || change.Amount <= 0 ||
            change.HpAfter != change.HpBefore + change.Amount) return false;
        var frameIndex = facts.FrameLastIndex(frame.Id);
        if (frameIndex < 2 || facts.FrameAt(frameIndex - 1) is not HpChangedTriggerWindowFrame hp ||
            hp.Id != change.Id || hp.Change != change || !protocols.TypedDamagePostEventEdge(hp, frame)) return false;
        var history = facts.CompleteProgramEventHistory().ToArray();
        var nativeRescue = TryGetNativeRescueRecovery(frameIndex - 1, history, out var rescuedDying);
        if ((!nativeRescue && !ExactRecoveryProducerEdge(frameIndex - 1, history)) ||
            !PausedRecoveryDamageMatches(frame, target, amount, sourceReference, nature)) return false;
        var windowIndex = frameIndex - 2;
        while (windowIndex >= 0 && facts.FrameAt(windowIndex) is not DamageTriggerWindowFrame) windowIndex--;
        if (nativeRescue)
        {
            // The card invoice proves this exceptional edge directly back to
            // the exact still-suspended dying token and its attack owner.
            damageWindowId = windowIndex >= 0 ? facts.FrameAt(windowIndex).Id : null;
        }
        else if (windowIndex >= 0)
        {
            var window = (DamageTriggerWindowFrame)facts.FrameAt(windowIndex);
            if (windowIndex + 1 >= frameIndex || facts.FrameAt(windowIndex + 1) is not ProgramSkillFrame root ||
                !protocols.DamageProgramMatchesWindow(root, window)) return false;
            for (var index = windowIndex + 2; index <= frameIndex; index++)
            {
                var parent = facts.FrameAt(index - 1);
                var child = facts.FrameAt(index);
                if (child is HpChangedTriggerWindowFrame)
                {
                    if (!ExactRecoveryProducerEdge(index, history)) return false;
                }
                else if (ExactPaidNativeRecoveryUseEdge(parent, child, history) ||
                    ExactPaidNativeRecoveryReplacementEdge(parent, child, history)) continue;
                else if (parent is ProgramSkillFrame producer && child is RecoveryReplacementFrame recovery)
                {
                    if (!protocols.ProgramRecoveryReplacementReceiptMatches(producer, recovery, history)) return false;
                }
                else if (!protocols.TypedDamagePostEventEdge(parent, child)) return false;
            }
            damageWindowId = window.Id;
        }
        else if (Enumerable.Range(0, frameIndex).Any(index => facts.FrameAt(index) is ProgramSkillFrame { AttackAttempt: not null } or
            ProgramSkillFrame { CardAttack.Active: true } or CardUseFrame { CardAttack.Active: true } or
            JudgmentFrame { CardAttack.Active: true }))
            return false;
        origin = new(hp.Id, frame.InstructionIndex,
            sourceReference is { } reference ? facts.ResolveProgramParticipant(frame, reference) : frame.OwnerSeat,
            target, amount, nature ?? DamageNature.Normal)
            { SuspendedDyingFrameId = nativeRescue ? rescuedDying!.Id : null };
        return true;
    }

    internal bool ExactRecoveryProducerEdge(int hpIndex, IReadOnlyList<IGameEvent> history)
    {
        if (hpIndex < 1 || facts.FrameAt(hpIndex) is not HpChangedTriggerWindowFrame hp ||
            hp.Change.Kind != HpChangeKind.Recovery || hp.Change.Amount <= 0) return false;
        var parent = facts.FrameAt(hpIndex - 1);
        if (parent is CardUseFrame use) return NativeCardRecoveryReturnMatches(use, hp, history);
        if (parent is RecoveryReplacementFrame recovery)
            return protocols.TypedRecoveryApplicationBoundsMatch(recovery, history) && protocols.TypedRecoveryHpReturnMatches(recovery, hp, history);
        return parent is ProgramSkillFrame producer && protocols.ProgramRecoveryReceiptMatches(producer, hp, history);
    }

    internal bool PausedRecoveryDamageMatches(ProgramSkillFrame frame, int target, int amount,
        ProgramParticipantReference? sourceReference, DamageNature? nature)
    {
        var effect = protocols.TypedRecoveryPausedEffect(frame);
        if (effect?.CompiledInstruction is DamageProgramInstruction instruction)
            return instruction.Amount == amount && instruction.SourceReference == sourceReference &&
                (instruction.Nature ?? DamageNature.Normal) == (nature ?? DamageNature.Normal) &&
                (instruction.TargetReference is { } reference ? facts.ResolveProgramParticipant(frame, reference) :
                    ProgramPaidCardRuntime.ResolveProgramEffectTarget(frame, effect.Target)) == target;
        if (effect is not { Op: SkillProgramEffectOp.DamageParticipants, Target: SkillProgramEffectTarget.SelectedTargets,
                TargetKind: null } || sourceReference is not null || !frame.ReexecuteParticipantInstruction ||
            (effect.DamageNature ?? DamageNature.Normal) != (nature ?? DamageNature.Normal)) return false;
        var key = "participant-cursor-" + (frame.InstructionIndex - 1);
        var cursors = frame.NumberBindings.Where(b => b.Name == key).ToArray();
        return cursors is [var cursor] && cursor.Value >= 1 && cursor.Value <= frame.SelectedTargetSeats.Count &&
            frame.SelectedTargetSeats[cursor.Value - 1] == target &&
            amount == (cursor.Value == 1 ? effect.Amount : effect.MinimumValue);
    }

    internal void AssertRecoveryDamageOrigin(ProgramSkillFrame frame)
    {
        if (frame.AttackReturn?.RecoveryOrigin is not { } origin) return;
        var effect = protocols.TypedRecoveryPausedEffect(frame);
        var sourceReference = effect?.CompiledInstruction is DamageProgramInstruction instruction ? instruction.SourceReference : null;
        if (frame.AttackAttempt is not { } attack || frame.InstructionIndex != origin.InstructionIndex ||
            attack.SourceSeat != origin.SourceSeat || frame.WindowContext?.ParentFrameId != origin.HpWindowId ||
            !TryDescribeRecoveryDamage(frame, origin.TargetSeat, origin.Amount, sourceReference, origin.Nature,
                false, null, out var described, out var damageWindowId) || described != origin ||
            damageWindowId != frame.AttackReturn.ParentDamageWindowFrameId)
            throw new InvalidOperationException("Recovery damage lost its exact HP invoice, issued instruction or suspended damage occurrence.");
    }
}
