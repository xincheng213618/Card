namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TryBeginDyingEntryProgramWindow(DyingFrame dying)
    {
        const string once = "window:dying-entering";
        if (dying.AttemptedSelfDyingBindings.Contains(once) || _players[dying.VictimSeat].Hp > 0) return false;
        var participants = _players.Where(player => player.IsAlive).ToArray();
        var facts = participants.ToDictionary(player => player.Seat, player => CaptureProgramTriggerFacts(player) with
        { EventTargetHp = _players[dying.VictimSeat].Hp, EventTargetMaxHp = _players[dying.VictimSeat].MaxHp,
            EventTargetHandCount = GetHand(_players[dying.VictimSeat]).Count });
        var candidates = participants.SelectMany(player => CollectEligibleProgramTriggerCandidates(player,
            SkillProgramTriggerWindow.DyingEntering, facts[player.Seat])
            .Where(candidate => GetProgramTrigger(candidate).Subject != SkillProgramTriggerSubject.Owner ||
                player.Seat == dying.VictimSeat)).OrderBy(candidate =>
            (candidate.OwnerSeat - _currentSeat + _players.Count) % _players.Count).ThenByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.SkillId, StringComparer.Ordinal).ThenBy(candidate => candidate.BindingId, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) return false;
        UpdateDyingFrame(dying.Id, current => current with
        {
            AttemptedSelfDyingBindings = [.. current.AttemptedSelfDyingBindings, once]
        });
        PushRuntimeFrame(new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, dying.VictimSeat,
            SkillProgramTriggerWindow.DyingEntering, candidates, ProgramLifecycleContinuation.ResumeDyingEntry,
            facts[dying.VictimSeat]) { ParticipantFacts = facts, ResumeDyingFrameId = dying.FrameId });
        AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>();
        return true;
    }

    private void CompleteDyingEntryProgramWindow(ProgramLifecycleTriggerWindowFrame frame)
    {
        var dying = ActiveDying ?? throw new InvalidOperationException("Dying entry lost its resolution.");
        if (dying.FrameId != frame.ResumeDyingFrameId || _resolutionStack.LastOrDefault()?.Id != dying.FrameId)
            throw new InvalidOperationException("Dying entry lost its parent continuation.");
        if (_players[dying.VictimSeat].Hp > 0) CompleteDying(dying, survived: true);
        else { SetDyingFrameStep(dying.FrameId, ResolutionFrameStep.AwaitingResponse); _status = EngineStatus.Running; ExposeHumanDyingPrompt(); }
    }
}
