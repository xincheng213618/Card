namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome SkipProgramTurnPhases(ProgramSkillFrame frame,
        IReadOnlyList<SkillProgramTurnPhase> phases)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.TriggerId is null || active.WindowContext is not { } context ||
            context.OwnerSeat != _currentSeat || active.OwnerSeat != _currentSeat ||
            _resolutionStack.Count < 2 ||
            _resolutionStack[^2] is not ProgramLifecycleTriggerWindowFrame parent ||
            parent.Id != context.ParentFrameId || parent.Window != context.Window ||
            phases.Count == 0 || phases.Distinct().Count() != phases.Count)
            throw new InvalidOperationException("Phase substitution requires the current lifecycle program.");

        var first = context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
            phases.All(phase => phase is SkillProgramTurnPhase.Judgment or SkillProgramTurnPhase.Draw);
        var second = context.Window == SkillProgramTriggerWindow.AfterNormalDraw &&
            phases.Count == 1 && phases[0] == SkillProgramTurnPhase.Play;
        if (!first && !second)
            throw new InvalidOperationException("The configured phase substitution does not match its boundary.");

        foreach (var phase in phases)
            _pendingTurnDelayedEffects |= phase switch
            {
                SkillProgramTurnPhase.Judgment => DelayedTurnEffects.SkipJudgmentPhase,
                SkillProgramTurnPhase.Draw => DelayedTurnEffects.SkipDrawPhase,
                SkillProgramTurnPhase.Play => DelayedTurnEffects.SkipPlayPhase,
                _ => throw new InvalidOperationException("Unknown turn phase.")
            };
        return SkillProgramStepOutcome.Continue;
    }

    private SkillProgramStepOutcome BeginProgramVirtualCardUse(ProgramSkillFrame frame,
        int targetSeat, CardKind cardKind, bool ignoreDistance)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.TriggerId is null || active.SelectedTargetSeats.Count != 1 ||
            active.SelectedTargetSeats[0] != targetSeat || cardKind != CardKind.Slash ||
            !ignoreDistance || _pendingAttack is not null || _pendingDuel is not null ||
            !IsValidPlayerSeat(targetSeat))
            throw new InvalidOperationException("Virtual card use requires one current selected Slash target.");
        var source = _players[active.OwnerSeat];
        var target = _players[targetSeat];
        if (!source.IsAlive || !target.IsAlive || source.Seat == target.Seat ||
            IsDirectedCardTargetProhibited(source.Seat, targetSeat, cardKind) ||
            IsSlashProhibited(target))
            return SkillProgramStepOutcome.Continue;

        var resolutionId = ++_resolutionSequence;
        _resolutionStack.Add(new CardUseFrame(resolutionId, source.Seat, 0, cardKind,
            Array.AsReadOnly(new[] { targetSeat }),
            PhysicalCardIds: Array.AsReadOnly(Array.Empty<int>())));
        QueueGameEvent(new CardUseDeclaredEvent(resolutionId, 0, cardKind, source.Seat));
        QueueGameEvent(new TargetsConfirmedEvent(resolutionId, Array.AsReadOnly(new[] { targetSeat })));
        var attack = new AttackResolution(resolutionId, source.Seat, targetSeat, card: null,
            playedCardKind: cardKind, programSkillCardUseFrameId: frame.Id);
        _pendingAttack = attack;
        QueueGameEvent(new CardUsedEvent(0, cardKind, source.Seat, targetSeat));
        ContinueSlashAfterResponsePrograms(attack);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
