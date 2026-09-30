namespace CardGame.Core;

public enum CharacterStateContinuation { Boundary, Program, CardUse, SkippedTurn }
public sealed record CharacterStateChangeContext(long Id, long? ParentFrameId, int TargetSeat, SkillProgramTriggerWindow Window);
public sealed record CharacterStateChangedEvent(CharacterStateChangeContext Change) : IGameEvent;

public sealed partial class GameEngine
{
    private readonly List<CharacterStateChangeContext> _pendingCharacterStateChanges = [];

    private void RecordCharacterStateChange(int targetSeat, SkillProgramTriggerWindow window, long? parentFrameId = null)
    {
        // Opt-in event boundaries leave command journals for older content unchanged.
        if (!_setupComplete || !_contentRegistry.Skills.Values.Any(skill =>
            skill.Program?.Triggers.Any(trigger => trigger.Window == window) == true)) return;
        var change = new CharacterStateChangeContext(++_resolutionSequence,
            parentFrameId ?? _resolutionStack.LastOrDefault()?.Id, targetSeat, window);
        _pendingCharacterStateChanges.Add(change);
        QueueGameEvent(new CharacterStateChangedEvent(change));
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
            var candidates = _players.Where(player => player.IsAlive).SelectMany(player =>
                CollectEligibleProgramTriggerCandidates(player, change.Window, facts[player.Seat])
                    .Where(candidate => GetProgramTrigger(candidate).Subject == SkillProgramTriggerSubject.Any || player.Seat == change.TargetSeat))
                .OrderBy(candidate => (candidate.OwnerSeat - _currentSeat + _playerCount) % _playerCount)
                .ThenByDescending(candidate => candidate.Priority).ThenBy(candidate => candidate.SkillId, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.BindingId, StringComparer.Ordinal).ToArray();
            if (candidates.Length == 0) continue;
            _resolutionStack.Add(new ProgramLifecycleTriggerWindowFrame(change.Id, change.TargetSeat, change.Window,
                candidates, ProgramLifecycleContinuation.ResumeCharacterStateChange, facts[change.TargetSeat])
            {
                ParticipantFacts = facts, ResumeProgramFrameId = resumeFrameId,
                CharacterStateContinuation = continuation, ResumeCardId = cardId, ResumeCardKind = cardKind
            });
            ContinueProgramLifecycleWindow();
            return true;
        }
        return false;
    }

    private void ResumeCharacterStateChange(ProgramLifecycleTriggerWindowFrame frame)
    {
        var continuation = frame.CharacterStateContinuation ?? throw new InvalidOperationException("A character-state window lost its continuation.");
        if (TryBeginCharacterStateProgramWindow(frame.ResumeProgramFrameId, continuation, frame.ResumeCardId, frame.ResumeCardKind)) return;
        switch (continuation)
        {
            case CharacterStateContinuation.Program: ContinueProgramSkill(frame.ResumeProgramFrameId!.Value); break;
            case CharacterStateContinuation.CardUse:
                var card = _cardZones.CardsAt(_cardZones.GetLocation(frame.ResumeCardId!.Value)).Single(item => item.Id == frame.ResumeCardId);
                FinishCardUse(frame.ResumeProgramFrameId!.Value, card, frame.ResumeCardKind); break;
            case CharacterStateContinuation.SkippedTurn: CompleteFaceUpSkippedTurn(_players[frame.OwnerSeat]); break;
            case CharacterStateContinuation.Boundary: PublishState(); break;
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
        _resolutionStack.Add(new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, turnOwner.Seat,
            SkillProgramTriggerWindow.JudgmentPhaseStarting, candidates, ProgramLifecycleContinuation.CompleteJudgmentPhaseStarting,
            facts[turnOwner.Seat]) { ParticipantFacts = facts });
        ContinueProgramLifecycleWindow();
        return true;
    }

    private void CompleteFaceUpSkippedTurn(CharacterState current)
    {
        _phase = TurnPhase.Finished;
        AddLog("TurnEnded", $"{current.Name} 的翻面回合结束。", current.Seat);
        QueueGameEvent(new TurnEndedEvent(_turnNumber, current.Seat));
        _currentSeat = FindNextAliveSeat(_currentSeat);
        _phase = TurnPhase.NotStarted;
        PublishState();
    }
}
