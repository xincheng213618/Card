namespace CardGame.Core;

/// <summary>The exact startup program and typed draw request that issued a turn-zero draw.</summary>
public sealed record GameStartingDrawOrigin(long WindowFrameId, long ProgramFrameId, int InstructionIndex,
    string GameplayHash, CardConversionSource Source, NativeDrawAttemptReturnKind ReturnKind, SkillProgramEffectOp? Operation)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public GameStartingOutsideDrawOrigin? OutsideDrawChild { get; init; }
}

public sealed partial class GameEngine
{
    private bool IsExactGameStartingDrawProgram(ProgramSkillFrame program) =>
        TryGetGameStartingDrawProgram(program, out _);

    private bool TryGetGameStartingDrawProgram(ProgramSkillFrame program, out ProgramLifecycleTriggerWindowFrame? window)
    {
        window = null;
        if (!_setupComplete || _turnNumber != 0 || _phase != TurnPhase.NotStarted ||
            program.WindowContext is not { Window: SkillProgramTriggerWindow.GameStarting } context ||
            context.OwnerSeat != program.OwnerSeat || program.TriggerId is null ||
            _contentRegistry.Skills.GetValueOrDefault(program.SkillId)?.Program is not { } definition ||
            definition.GameplayHash != program.GameplayHash || program.InstructionIndex < 1) return false;
        var index = _resolutionStack.FindIndex(frame => frame.Id == program.Id);
        if (index < 1 || _resolutionStack[index - 1] is not ProgramLifecycleTriggerWindowFrame parent ||
            parent.Id != context.ParentFrameId || parent.Window != SkillProgramTriggerWindow.GameStarting ||
            parent.Continuation != ProgramLifecycleContinuation.CompleteGameStarting || parent.OwnerSeat != _currentSeat ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(program, parent.Candidates[parent.CandidateIndex])) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(program, definition);
        if (program.InstructionIndex > plan.Instructions.Count) return false;
        window = parent;
        return true;
    }

    private GameStartingDrawOrigin? CaptureGameStartingDrawOrigin(int recipient, int requested, CardMoveReason reason,
        CardConversionSource? directProducer, out GameStartingMovementDrawOrigin? movementOrigin)
    {
        var derived = CaptureGameStartingGainDrawOrigin(recipient, requested, reason, directProducer, out movementOrigin);
        if (derived is not null || movementOrigin is not null) return derived;
        movementOrigin = CaptureGameStartingNativeMovementDrawOrigin(recipient, requested, reason, directProducer);
        if (movementOrigin is not null) return null;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { NativeDraw: { } paid } program ||
            !TryGetGameStartingDrawProgram(program, out var window) || paid.InstructionIndex != program.InstructionIndex ||
            paid.RequestedCount != requested || paid.Reason != reason || paid.RecipientApplied || paid.Replacement is not null ||
            paid.RecipientIndex < 0 || paid.RecipientIndex >= paid.Recipients.Count || paid.Recipients[paid.RecipientIndex] != recipient)
            return null;
        var origin = new GameStartingDrawOrigin(window!.Id, program.Id, program.InstructionIndex, program.GameplayHash,
            new(program.SkillId, GetProgramBindingId(program), program.OwnerSeat, program.SkillInstanceId), paid.ReturnKind, paid.Operation);
        return GameStartingDrawOriginProgramMatches(origin) ? origin : null;
    }

    private bool GameStartingDrawOriginProgramMatches(GameStartingDrawOrigin origin)
    {
        if (origin.WindowFrameId <= 0 || origin.ProgramFrameId <= origin.WindowFrameId || origin.InstructionIndex < 1 ||
            !IsValidPlayerSeat(origin.Source.OwnerSeat) ||
            _contentRegistry.Skills.GetValueOrDefault(origin.Source.SkillId)?.Program is not { } definition ||
            definition.GameplayHash != origin.GameplayHash) return false;
        var trigger = definition.Triggers.SingleOrDefault(item => item.Id == origin.Source.BindingId);
        if (trigger?.Window != SkillProgramTriggerWindow.GameStarting) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(definition, ProgramInstructionSourceKind.Trigger, trigger.Id);
        if (origin.InstructionIndex > plan.Instructions.Count) return false;
        var instruction = plan.Instructions[origin.InstructionIndex - 1];
        return origin.ReturnKind == NativeDrawAttemptReturnKind.ProgramInstruction
            ? origin.Operation is null && instruction.CompiledInstruction is DrawProgramInstruction
            : origin.ReturnKind == NativeDrawAttemptReturnKind.ProgramOperation && origin.Operation == instruction.Op &&
                origin.Operation is { } operation && ProgramNativeDrawReturns.Value.ContainsKey(operation);
    }

    private bool ValidGameStartingDrawOrigin(NativeDrawInvocationProof proof)
    {
        if (proof.GameStartingOrigin?.OutsideDrawChild is not null)
            return ValidGameStartingGainDrawChain(proof);
        if (proof.GameStartingOrigin is not { } origin || proof.ActualTurnNumber != 0 || proof.Phase is not null ||
            proof.ParentFrameId != origin.ProgramFrameId || proof.InvocationId <= origin.ProgramFrameId ||
            !GameStartingDrawOriginProgramMatches(origin)) return false;
        return CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(started =>
            started.FrameId == origin.ProgramFrameId && started.SkillId == origin.Source.SkillId &&
            started.BindingId == origin.Source.BindingId && started.SkillInstanceId == origin.Source.SkillInstanceId &&
            started.OwnerSeat == origin.Source.OwnerSeat && started.Window == SkillProgramTriggerWindow.GameStarting) == 1;
    }
}
