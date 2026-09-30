namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TryBeginDyingEntryProgramWindow(DyingResolution dying)
    {
        const string once = "window:dying-entering";
        if (dying.AttemptedSelfDyingBindings.Contains(once) || _players[dying.VictimSeat].Hp > 0) return false;
        var participants = _players.Where(player => player.IsAlive).ToArray();
        var facts = participants.ToDictionary(player => player.Seat, player => CaptureProgramTriggerFacts(player) with
        { EventTargetHp = _players[dying.VictimSeat].Hp, EventTargetMaxHp = _players[dying.VictimSeat].MaxHp,
            EventTargetHandCount = GetHand(_players[dying.VictimSeat]).Count });
        var candidates = participants.SelectMany(player => CollectEligibleProgramTriggerCandidates(player,
            SkillProgramTriggerWindow.DyingEntering, facts[player.Seat])).OrderBy(candidate =>
            (candidate.OwnerSeat - _currentSeat + _players.Count) % _players.Count).ThenByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.SkillId, StringComparer.Ordinal).ThenBy(candidate => candidate.BindingId, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) return false;
        dying.AttemptedSelfDyingBindings.Add(once);
        _resolutionStack.Add(new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, dying.VictimSeat,
            SkillProgramTriggerWindow.DyingEntering, candidates, ProgramLifecycleContinuation.ResumeDyingEntry,
            facts[dying.VictimSeat]) { ParticipantFacts = facts, ResumeDyingFrameId = dying.FrameId });
        ContinueProgramLifecycleWindow();
        return true;
    }

    private void CompleteDyingEntryProgramWindow(ProgramLifecycleTriggerWindowFrame frame)
    {
        var dying = _pendingDying ?? throw new InvalidOperationException("Dying entry lost its resolution.");
        if (dying.FrameId != frame.ResumeDyingFrameId || _resolutionStack.LastOrDefault()?.Id != dying.FrameId)
            throw new InvalidOperationException("Dying entry lost its parent continuation.");
        if (_players[dying.VictimSeat].Hp > 0) CompleteDying(dying, survived: true);
        else { SetDyingFrameStep(dying.FrameId, ResolutionFrameStep.AwaitingResponse); _status = EngineStatus.Running; ExposeHumanDyingPrompt(); }
    }
}
