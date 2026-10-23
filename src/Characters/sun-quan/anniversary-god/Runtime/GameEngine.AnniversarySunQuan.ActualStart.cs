using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AnniversaryCangmingActualStart { get; init; }
}
public sealed record AnniversaryMingActualStartIssuedEvent(long WindowId, int ActualTurn, int OwnerSeat, string SkillInstanceId, string GameplayHash) : IGameEvent;
public sealed record AnniversaryMingActualStartReturnedEvent(long WindowId, int ActualTurn, int OwnerSeat) : IGameEvent;
public sealed partial class GameEngine
{
    // This is a turn boundary, including a face-down skipped turn. The ordinary
    // preparation host intentionally starts later and cannot own this obligation.
    private bool TryBeginAnniversaryCangmingActualTurnStart(CharacterState current)
    {
        if (!AnniversaryMingHolderHasCards(current.Seat)) return false;
        if (_resolutionStack.Count != 0 || _pendingDecision is not null || current.Seat != _currentSeat || _turnNumber <= 0 ||
            ProgramEventHistory<TurnStartedEvent>().LastOrDefault() is not { } started || started.TurnNumber != _turnNumber || started.ActorSeat != current.Seat)
            throw new InvalidOperationException("Cangming requires the exact clean actual-turn-start boundary.");
        var candidate = AnniversaryMingReturnCandidate(current.Seat, "return-start");
        var frame = new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, current.Seat, SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
            [candidate], ProgramLifecycleContinuation.NormalTurnStart, CaptureProgramTriggerFacts(current)) { AnniversaryCangmingActualStart = true };
        AdvanceEventRulesAndQueueFact(new AnniversaryMingActualStartIssuedEvent(frame.Id, _turnNumber, current.Seat, candidate.SkillInstanceId, candidate.GameplayHash));
        PushRuntimeFrame(frame); AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>(); return true;
    }
    [ProgramPipelineHook(ProgramPipelinePhase.LifecycleWindowCompleted, 40500)]
    private bool CompleteAnniversaryCangmingActualTurnStart(ProgramLifecycleTriggerWindowFrame frame)
    {
        if (frame.AnniversaryCangmingActualStart is null) return false;
        if (frame.AnniversaryCangmingActualStart != true || frame.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || frame.Continuation != ProgramLifecycleContinuation.NormalTurnStart ||
            frame.OwnerSeat != _currentSeat || frame.CandidateIndex != 1 || frame.Candidates is not [var candidate] || candidate != AnniversaryMingReturnCandidate(frame.OwnerSeat, "return-start") ||
            ProgramEventHistory<AnniversaryMingActualStartIssuedEvent>().Count(e => e == new AnniversaryMingActualStartIssuedEvent(frame.Id, _turnNumber, frame.OwnerSeat, candidate.SkillInstanceId, candidate.GameplayHash)) != 1 ||
            ProgramEventHistory<AnniversaryMingActualStartReturnedEvent>().Any(e => e.WindowId == frame.Id) || _resolutionStack.Any(p => p.Id == frame.Id))
            throw new InvalidOperationException("Cangming actual turn start lost its one issued holder, native lifecycle return or completed cursor.");
        AdvanceEventRulesAndQueueFact(new AnniversaryMingActualStartReturnedEvent(frame.Id, _turnNumber, frame.OwnerSeat));
        ContinueTurnAfterActualStart(_players[frame.OwnerSeat]); return true;
    }
}
