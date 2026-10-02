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
        var third = context.Window == SkillProgramTriggerWindow.DiscardPhaseStarting &&
            phases.Count == 1 && phases[0] == SkillProgramTurnPhase.Discard;
        if (!first && !second && !third)
            throw new InvalidOperationException("The configured phase substitution does not match its boundary.");

        foreach (var phase in phases)
            _pendingTurnDelayedEffects |= phase switch
            {
                SkillProgramTurnPhase.Judgment => DelayedTurnEffects.SkipJudgmentPhase,
                SkillProgramTurnPhase.Draw => DelayedTurnEffects.SkipDrawPhase,
                SkillProgramTurnPhase.Play => DelayedTurnEffects.SkipPlayPhase,
                SkillProgramTurnPhase.Discard => DelayedTurnEffects.SkipDiscardPhase,
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
            !ignoreDistance || ActiveCardAttack is not null || ActiveDuel is not null ||
            !IsValidPlayerSeat(targetSeat))
            throw new InvalidOperationException("Virtual card use requires one current selected Slash target.");
        var source = _players[active.OwnerSeat];
        var target = _players[targetSeat];
        if (!source.IsAlive || !target.IsAlive || source.Seat == target.Seat ||
            IsDirectedCardTargetProhibited(source.Seat, targetSeat, cardKind) ||
            IsSlashProhibited(target))
            return SkillProgramStepOutcome.Continue;

        var resolutionId = ++_resolutionSequence;
        var instruction = ProgramInstructionResolver.Default.Resolve(active, _contentRegistry.GetSkill(active.SkillId).Program!)
            .GetPausedInstruction(active.InstructionIndex).Effect;
        var action = instruction.UseCardActionWindows
            ? CaptureFactionAction(new CardActionContext(++_cardActionSequence, _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId,
                CardActionType.Use, source.Seat, source.Seat, null, null, null, cardKind, [targetSeat], [], [],
                effectiveSuit: Suit.None, effectiveRank: 0)) : null;
        PushRuntimeFrame(new CardUseFrame(resolutionId, source.Seat, 0, cardKind,
            Array.AsReadOnly(new[] { targetSeat }),
            PhysicalCardIds: Array.AsReadOnly(Array.Empty<int>())) { Action = action });
        if (action is not null && TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(resolutionId, 0, cardKind, source.Seat));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(resolutionId, Array.AsReadOnly(new[] { targetSeat })));
        var attack = new CardAttackHandle(this, resolutionId, source.Seat, targetSeat, card: null,
            damageAmount: action is not null && source.HasAlcoholEffect ? 2 : 1,
            playedCardKind: cardKind, ignoresArmor: action is not null && HasCardArmorBypass(source, target, cardKind),
            programSkillCardUseFrameId: frame.Id);
        if (action is not null) source.HasAlcoholEffect = false;
        ActiveCardAttack = attack;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, cardKind, source.Seat, targetSeat));
        if (action is not null)
        {
            TryMarkProgramUseCommitted(resolutionId);
            if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted,
                action.TargetSeats, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
        }
        else ContinueSlashAfterResponsePrograms(attack);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
