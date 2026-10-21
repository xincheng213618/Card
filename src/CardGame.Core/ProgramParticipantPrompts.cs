namespace CardGame.Core;

public sealed partial class GameEngine
{
    // This exact issued window is an external input boundary even when its
    // canonical seat is AI-controlled. It does not admit an automatic AI reply.
    private bool IsIssuedParticipantPrompt(PendingDecision issued) =>
        issued.ParticipantFrameId is > 0 && issued.Kind == DecisionKind.ProgramTrigger &&
        issued.PromptId.Value > 0 && issued.IsPrivate && issued.SkillPrompt is not null &&
        issued.ValidCardIds.Count == 0 && issued.Choices.Count == 0 &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame
            { Step: ResolutionFrameStep.AwaitingResponse } owner && owner.Id == issued.ParticipantFrameId;

    // The owning runtime validates source, participation and the original prompt.
    // No private choice population is stored outside its owning frame.
    private PendingDecision? GetPendingDecisionForActor(int actorSeat)
    {
        if (_pendingDecision is not { } issued) return null;
        if (issued.ParticipantFrameId is null)
            return issued.PlayerSeat == actorSeat ? issued : null;
        if (!IsValidPlayerSeat(actorSeat) || !IsIssuedParticipantPrompt(issued)) return null;

        var decision = _programPipelineHooks.ParticipantPrompt(issued, actorSeat);
        if (decision is null) return null;
        if (decision.PlayerSeat != actorSeat || decision.Kind != issued.Kind ||
            decision.ParticipantFrameId != issued.ParticipantFrameId || decision.PromptId != issued.PromptId ||
            decision.Revision != issued.Revision || decision.SourceSeat != issued.SourceSeat ||
            decision.SkillPrompt != issued.SkillPrompt || !decision.IsPrivate)
            throw new InvalidOperationException("A participant projection must retain its issued prompt identity and actor privacy.");
        return CloneDecision(decision);
    }

    internal bool HasParticipantInputBoundary => _pendingDecision is { } issued && IsIssuedParticipantPrompt(issued);

    internal bool IsParticipantInputBoundary(ParticipantPromptHostKey key) =>
        _pendingDecision is { } issued && IsIssuedParticipantPrompt(issued) &&
        issued.ParticipantFrameId == key.FrameId && issued.PromptId == key.PromptId;

    internal ParticipantPromptHostState? CaptureParticipantHostState()
    {
        var state = _programPipelineHooks.ParticipantHostState();
        if (state is null) return null;
        var key = state.Key;
        var owner = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(frame => frame.Id == key.FrameId);
        if (owner is null || key.PromptId.Value <= 0 || key.Source != new CardConversionSource(
                owner.SkillId, GetProgramBindingId(owner), owner.OwnerSeat, owner.SkillInstanceId) ||
            key.GameplayHash != owner.GameplayHash || state.RecipientSeat != owner.OwnerSeat ||
            state.PendingNpcSeats.Distinct().Count() != state.PendingNpcSeats.Count ||
            state.PendingNpcSeats.Any(seat => !IsValidPlayerSeat(seat) || _players[seat].IsHuman || !_players[seat].IsAlive))
            throw new InvalidOperationException("Participant host state must retain its live owner and distinct pending NPCs.");
        var accepting = state.CanAcceptInput && _pendingDecision is { } issued &&
            IsIssuedParticipantPrompt(issued) && issued.ParticipantFrameId == key.FrameId &&
            issued.PromptId == key.PromptId;
        return state with { PendingNpcSeats = Array.AsReadOnly(state.PendingNpcSeats.ToArray()),
            CanAcceptInput = accepting };
    }

    internal ParticipantGiftWork? CaptureParticipantGiftWork(ParticipantPromptHostState state, int actor)
    {
        if (!state.CanAcceptInput || !state.PendingNpcSeats.Contains(actor)) return null;
        var view = CreateSnapshot(actor);
        if (view.PendingDecision is not { } prompt || prompt.ParticipantFrameId != state.Key.FrameId ||
            prompt.PromptId != state.Key.PromptId || prompt.PlayerSeat != actor) return null;
        if (prompt.Choices.Any(choice => choice.Targets.Count != 0 || choice.Cards.Count > 1))
            throw new InvalidOperationException("The participant gift host accepts only published optional single-entity gifts.");
        var decline = prompt.Choices.Single(choice => choice.Cards.Count == 0).Id;
        var brain = _aiBrains[actor].ForkParticipantDecision();
        var recipient = state.RecipientSeat;
        // Only this detached brain and frozen actor view enter the worker.
        return new(state.Key, actor, view.Revision,
            () => brain.ChooseOptionalOwnedCardGift(view, recipient, prompt.Choices, decline));
    }
}
