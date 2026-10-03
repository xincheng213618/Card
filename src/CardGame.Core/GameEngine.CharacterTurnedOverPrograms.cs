namespace CardGame.Core;

/// <summary>The public, immutable face-state transition that opened a turned-over window.</summary>
public sealed record CharacterTurnedOverState(bool WasFaceDown, bool IsFaceDown);

public sealed partial class GameEngine
{
    private void RecordCharacterTurnedOver(int targetSeat, bool wasFaceDown)
    {
        var isFaceDown = _players[targetSeat].IsFaceDown;
        if (wasFaceDown == isFaceDown) return;
        RecordProvenanceFaceUpReset(targetSeat, wasFaceDown);
        RecordCharacterStateChange(targetSeat, SkillProgramTriggerWindow.CharacterTurnedOver,
            turnedOver: new(wasFaceDown, isFaceDown));
    }

    private static bool CharacterTurnedOverFrameRidesOn(ResolutionFrame ride, ResolutionFrame beneath)
    {
        if (ride is ProgramLifecycleTriggerWindowFrame
            { Window: SkillProgramTriggerWindow.CharacterTurnedOver,
              Continuation: ProgramLifecycleContinuation.ResumeCharacterStateChange,
              CharacterStateContinuation: CharacterStateContinuation.Program } window)
            return beneath is ProgramSkillFrame && window.ResumeProgramFrameId == beneath.Id;
        if (ride is not ProgramSkillFrame { WindowContext: { Window: SkillProgramTriggerWindow.CharacterTurnedOver } context } program ||
            beneath is not ProgramLifecycleTriggerWindowFrame
                { Window: SkillProgramTriggerWindow.CharacterTurnedOver,
                  Continuation: ProgramLifecycleContinuation.ResumeCharacterStateChange } parent ||
            context.ParentFrameId != parent.Id || context.TargetSeat != parent.OwnerSeat ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count) return false;
        var candidate = parent.Candidates[parent.CandidateIndex];
        return program.OwnerSeat == candidate.OwnerSeat && program.SkillId == candidate.SkillId &&
            program.SkillInstanceId == candidate.SkillInstanceId && program.TriggerId == candidate.BindingId;
    }

    private bool CharacterTurnedOverDamageObserverRidesOn(int index)
    {
        if (!DamageObserverRidesOn(_resolutionStack[index], _resolutionStack[index - 1])) return false;
        for (var parentIndex = index - 1; parentIndex >= 1; parentIndex--)
        {
            var frame = _resolutionStack[parentIndex]; var parent = _resolutionStack[parentIndex - 1];
            if (CharacterTurnedOverFrameRidesOn(frame, parent)) return true;
            if (!DamageFrameRidesOn(frame, parent) && !DamageObserverRidesOn(frame, parent) &&
                !RecoveryReplacementFrameRidesOn(frame, parent) &&
                !PileEquipmentFrameRidesOn(frame, parent) && !RandomEquipmentFrameRidesOn(frame, parent)) return false;
        }
        return false;
    }

    private bool IsCharacterTurnedOverProgramDying()
    {
        if (ActiveDying is not { ResumesProgramSkill: true } dying ||
            _resolutionStack.OfType<DyingFrame>().SingleOrDefault(f => f.Id == dying.FrameId) is not { } child ||
            child.ParentFrameId != dying.ParentFrameId) return false;
        var top = DamageCursorEffectiveTop(includeNestedObservers: true);
        if (!(top is DyingFrame exact && exact.Id == child.Id ||
              top is ProgramSkillFrame { WindowContext: { } response } && response.ParentFrameId == child.Id &&
              response.Window is SkillProgramTriggerWindow.DyingResponse or SkillProgramTriggerWindow.SelfDyingResponse)) return false;
        var parentIndex = _resolutionStack.FindLastIndex(f => f.Id == child.ParentFrameId);
        if (parentIndex < 1 || _resolutionStack[parentIndex] is not ProgramSkillFrame) return false;
        var hasTurnedOver = false;
        for (var index = parentIndex; index > 0; index--)
        {
            var frame = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
            if (hasTurnedOver && ActiveDamageTrigger is { } damage &&
                frame is ProgramSkillFrame { WindowContext: { } context } && context.ParentFrameId == damage.Id &&
                context.Window is SkillProgramTriggerWindow.AfterDamageApplied or SkillProgramTriggerWindow.DamageAppliedBeforeDying)
                return true;
            if (CharacterTurnedOverFrameRidesOn(frame, parent)) { hasTurnedOver = true; continue; }
            if (!DamageFrameRidesOn(frame, parent) && !DamageObserverRidesOn(frame, parent) &&
                !RecoveryReplacementFrameRidesOn(frame, parent) &&
                !PileEquipmentFrameRidesOn(frame, parent) && !RandomEquipmentFrameRidesOn(frame, parent)) return false;
        }
        return false;
    }
}
