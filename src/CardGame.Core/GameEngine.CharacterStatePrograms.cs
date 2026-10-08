namespace CardGame.Core;

public enum CharacterStateContinuation { Boundary, Program, CardUse, SkippedTurn, VirtualBasicCardUse, ChainedStateBasic }
public sealed record CharacterStateChangeContext(long Id, long? ParentFrameId, int TargetSeat, SkillProgramTriggerWindow Window)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterTurnedOverState? TurnedOver { get; init; }
}
public sealed record CharacterStateChangedEvent(CharacterStateChangeContext Change) : IGameEvent;

public sealed partial class GameEngine
{
    private readonly List<CharacterStateChangeContext> _pendingCharacterStateChanges = [];

    private void RecordCharacterStateChange(int targetSeat, SkillProgramTriggerWindow window, long? parentFrameId = null,
        CharacterTurnedOverState? turnedOver = null)
    {
        // Opt-in event boundaries leave command journals for older content unchanged.
        if (!_setupComplete || !_contentRegistry.ProgramDependencies.HasTriggerWindow(window)) return;
        var change = new CharacterStateChangeContext(++_resolutionSequence,
            parentFrameId ?? _resolutionStack.LastOrDefault()?.Id, targetSeat, window) { TurnedOver = turnedOver };
        _pendingCharacterStateChanges.Add(change);
        AdvanceEventRulesAndQueueFact(new CharacterStateChangedEvent(change));
    }

    private bool TryBeginCharacterStateProgramWindow(long? resumeFrameId = null,
        CharacterStateContinuation continuation = CharacterStateContinuation.Boundary,
        int? cardId = null, CardKind? cardKind = null)
    {
        if (_pendingDecision is not null || _winner != Winner.None || _status == EngineStatus.Completed ||
            (resumeFrameId is null ? _resolutionStack.Count != 0 : _resolutionStack.LastOrDefault()?.Id != resumeFrameId)) return false;
        while (_pendingCharacterStateChanges.FirstOrDefault(change => resumeFrameId is null || change.ParentFrameId == resumeFrameId) is { } change)
        {
            _pendingCharacterStateChanges.Remove(change);
            if (!_players[change.TargetSeat].IsAlive) continue;
            var facts = _players.Where(player => player.IsAlive).ToDictionary(player => player.Seat, CaptureProgramTriggerFacts);
            if (change.TurnedOver is { } turnedOver)
                facts[change.TargetSeat] = facts[change.TargetSeat] with { OwnerIsFaceDown = turnedOver.IsFaceDown };
            var candidates = _players.Where(player => player.IsAlive).SelectMany(player =>
                CollectEligibleProgramTriggerCandidates(player, change.Window, facts[player.Seat])
                    .Where(candidate => GetProgramTrigger(candidate).Subject == SkillProgramTriggerSubject.Any || player.Seat == change.TargetSeat))
                .OrderBy(candidate => (candidate.OwnerSeat - _currentSeat + _playerCount) % _playerCount)
                .ThenByDescending(candidate => candidate.Priority).ThenBy(candidate => candidate.SkillId, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.BindingId, StringComparer.Ordinal).ToArray();
            if (candidates.Length == 0) continue;
            if (continuation == CharacterStateContinuation.ChainedStateBasic)
                BeginChainedStateBasicChild(resumeFrameId!.Value, change.Id);
            PushRuntimeFrame(new ProgramLifecycleTriggerWindowFrame(change.Id, change.TargetSeat, change.Window,
                candidates, ProgramLifecycleContinuation.ResumeCharacterStateChange, facts[change.TargetSeat])
            {
                ParticipantFacts = facts, ResumeProgramFrameId = resumeFrameId,
                CharacterStateContinuation = continuation, ResumeCardId = cardId, ResumeCardKind = cardKind
            });
            AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
            return true;
        }
        return false;
    }

    private void ResumeCharacterStateChange(ProgramLifecycleTriggerWindowFrame frame)
    {
        var continuation = frame.CharacterStateContinuation ?? throw new InvalidOperationException("A character-state window lost its continuation.");
        if (continuation == CharacterStateContinuation.ChainedStateBasic)
        { ResumeChainedStateBasicState(frame); return; }
        if (TryBeginCharacterStateProgramWindow(frame.ResumeProgramFrameId, continuation, frame.ResumeCardId, frame.ResumeCardKind)) return;
        switch (continuation)
        {
            case CharacterStateContinuation.Program: AdvanceRuntimeProgram(frame.ResumeProgramFrameId!.Value); break;
            case CharacterStateContinuation.CardUse:
                var card = GetTrickRepresentation(frame.ResumeProgramFrameId!.Value, frame.ResumeCardId!.Value);
                FinishCardUse(frame.ResumeProgramFrameId!.Value, card, frame.ResumeCardKind); break;
            case CharacterStateContinuation.VirtualBasicCardUse: FinishVirtualBasicUse(frame.ResumeProgramFrameId!.Value); break;
            case CharacterStateContinuation.SkippedTurn: CompleteFaceUpSkippedTurn(_players[frame.OwnerSeat]); break;
            case CharacterStateContinuation.Boundary: AdvanceRulesAndPublishState(); break;
        }
    }

    private bool TryBeginJudgmentPhaseStartingPrograms(CharacterState turnOwner)
    {
        var facts = _players.Where(player => player.IsAlive).ToDictionary(player => player.Seat, player =>
            CaptureProgramTriggerFacts(player) with { OwnerEventTargetDistance = GetCombatDistance(player.Seat, turnOwner.Seat) });
        var candidates = _players.Where(player => player.IsAlive).SelectMany(player =>
            CollectEligibleProgramTriggerCandidates(player, SkillProgramTriggerWindow.JudgmentPhaseStarting, facts[player.Seat])
                .Where(candidate => GetProgramTrigger(candidate).TurnOwnerScope ==
                    (player.Seat == turnOwner.Seat ? SkillProgramTurnOwnerScope.Own : SkillProgramTurnOwnerScope.OtherLiving)))
            .OrderBy(candidate => (candidate.OwnerSeat - turnOwner.Seat + _playerCount) % _playerCount)
            .ThenByDescending(candidate => candidate.Priority).ThenBy(candidate => candidate.SkillId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.BindingId, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) return false;
        PushRuntimeFrame(new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, turnOwner.Seat,
            SkillProgramTriggerWindow.JudgmentPhaseStarting, candidates, ProgramLifecycleContinuation.CompleteJudgmentPhaseStarting,
            facts[turnOwner.Seat]) { ParticipantFacts = facts });
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
        return true;
    }

    private void CompleteFaceUpSkippedTurn(CharacterState current)
    {
        ExpireGiftRetentionObligations();
        _phase = TurnPhase.Finished;
        AddLog("TurnEnded", $"{current.Name} 的翻面回合结束。", current.Seat);
        AdvanceEventRulesAndQueueFact(new TurnEndedEvent(_turnNumber, current.Seat));
        if (TryBeginDeferredTurnEnd(current, skipped: true)) return;
        AdvanceAfterDeferredTurnEnd(current, skipped: true);
    }
}
