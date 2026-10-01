namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IEnumerable<PromptChoice> ProgramDodgeResponseChoices(CharacterState owner)
    {
        if (HasIssuedPlayPhaseUseBan(owner.Seat) && IsProgramResponseCardUse(owner, CardKind.Dodge)) yield break;
        foreach (var program in EnabledActivationPrograms(owner))
            foreach (var activation in program.Activations.Where(item => item.Effects is
                [{ Op: SkillProgramEffectOp.DrawTurnOwnerThenDiscardMaximumHandForDodge }]))
                yield return new PromptChoice(new ChoiceId($"respond.program-dodge.{program.Id}.{activation.Id}"),
                    $"发动【{_contentRegistry.GetSkill(program.Id).Name}】尝试提供闪。", [], [],
                    new Dictionary<string, string> { ["response"] = "program-dodge", ["skill"] = program.Id,
                        ["activation"] = activation.Id });
    }

    private EngineRunResult BeginProgramDodgeResponse(PromptChoice choice, bool advanceToHumanBoundary)
    {
        var decision = _pendingDecision is { Kind: DecisionKind.RespondDodge } pending ? pending :
            throw new InvalidOperationException("A configured Dodge requires its current response prompt.");
        var attack = ActiveCardAttack ?? throw new InvalidOperationException("Configured Dodge lost its attack.");
        var owner = _players[decision.PlayerSeat];
        if ((owner.Seat != attack.TargetSeat && ActiveFactionDefense?.CurrentCandidateSeat != owner.Seat) || !owner.IsAlive ||
            !ProgramDodgeResponseChoices(owner).Any(item => item.Id == choice.Id && AssistedChoicesEqual([item], [choice])))
            throw new InvalidOperationException("Configured Dodge changed its living responder or published activation.");
        var program = GetEnabledSkillProgram(owner, choice.Parameters["skill"]);
        var activation = program.Activations.Single(item => item.Id == choice.Parameters["activation"]);
        ClearPendingDecision();
        var frame = new ProgramSkillFrame(++_resolutionSequence, owner.Seat, program.Id, activation.Id,
            program.GameplayHash, 0, [], [])
        { SkillInstanceId = GetRuntimeSkillInstanceId(owner, program.Id), ResponseDecision = decision };
        PushRuntimeFrame(frame);
        AdvanceEventRulesAndQueueFact(new ProgramSkillStartedEvent(frame.Id, owner.Seat, program.Id, activation.Id));
        AdvanceRuntimeProgram(frame.Id);
        AdvanceRulesAndPublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private void CompleteProgramMaximumHandDodge(ProgramSkillFrame frame, bool succeeded)
    {
        var decision = frame.ResponseDecision ?? throw new InvalidOperationException("Configured Dodge lost its saved response.");
        var attack = ActiveCardAttack ?? throw new InvalidOperationException("Configured Dodge lost its parent attack.");
        if (frame != GetActiveProgramFrame(frame.Id) || decision.PlayerSeat != frame.OwnerSeat ||
            (attack.TargetSeat != frame.OwnerSeat && ActiveFactionDefense?.CurrentCandidateSeat != frame.OwnerSeat) || frame.HandControlDraft is not null)
            throw new InvalidOperationException("Configured Dodge changed its parent or uncommitted physical cost.");
        ClearPendingDecision();
        AdvanceEventRulesAndQueueFact(new ProgramSkillResolvedEvent(frame.Id, frame.OwnerSeat, frame.SkillId, frame.ActivationId, true));
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramSkill);
        var owner = _players[frame.OwnerSeat];
        if (!succeeded)
        {
            var cards = GetResponseCards(owner, CardKind.Dodge);
            var freshChoices = ActiveFactionDefense is { } defense
                ? CreateFactionDefenseResponseChoices(owner, defense, cards, HasBagua(owner))
                : CreateResponseChoices(cards, CardKind.Dodge, decision.IncomingCard,
                    card => GetEffectiveResponseKind(owner, card, CardKind.Dodge),
                    includeBagua: decision.Choices.Any(item => item.Parameters.GetValueOrDefault("response") == "bagua"),
                    includeFactionDefense: decision.Choices.Any(item => item.Parameters.GetValueOrDefault("response") == "faction-defense-request"),
                    responder: owner);
            _pendingDecision = decision with { PromptId = CreatePromptId(), ValidCardIds = cards.Select(card => card.Id).ToArray(),
                Choices = freshChoices.Where(item => item.Parameters.GetValueOrDefault("skill") != frame.SkillId ||
                        item.Parameters.GetValueOrDefault("response") != "program-dodge").ToArray() };
            _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
            return;
        }
        var source = new CardConversionSource(frame.SkillId, frame.ActivationId, frame.OwnerSeat, frame.SkillInstanceId);
        if (ActiveFactionDefense is { } factionDefense)
        {
            CompleteFactionDefenseResponse(factionDefense, owner, null, false, source);
            return;
        }
        PopResponseWindow(attack.ResolutionId);
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        AdvanceEventRulesAndQueueFact(new CardRespondedEvent(-1, owner.Seat, attack.SourceSeat, CardKind.Dodge));
        var continuation = ActiveGroupCard is null ? ProgramCardContinuation.Dodge : ProgramCardContinuation.GroupResponse;
        if (!TryBeginCardResponsePrograms(attack, owner, owner, null, attack.SourceSeat, CardKind.Dodge, [], continuation, source))
        {
            if (ActiveGroupCard is null) CompleteSuccessfulDodgeResponse(attack);
            else CompleteAttack(attack);
        }
    }

    private bool HasProgramDodgeResponseContinuation(CardAttackHandle attack)
    {
        if (DamageCursorEffectiveTop(includeNestedObservers: true) is not ProgramSkillFrame
            { ResponseDecision: { Kind: DecisionKind.RespondDodge } decision } frame ||
            frame.OwnerSeat != decision.PlayerSeat || !IsValidPlayerSeat(frame.OwnerSeat) ||
            (frame.OwnerSeat != attack.TargetSeat && ActiveFactionDefense?.CurrentCandidateSeat != frame.OwnerSeat))
            return false;
        var index = _resolutionStack.IndexOf(frame);
        return index > 0 && _resolutionStack[index - 1] is ResponseWindowFrame response &&
            response.ParentFrameId == attack.ResolutionId && response.ResponderSeat == attack.TargetSeat &&
            response.SourceSeat == attack.SourceSeat && decision.IncomingCard == response.IncomingCard &&
            GetEnabledSkillProgram(_players[frame.OwnerSeat], frame.SkillId).Activations.Any(activation =>
                activation.Id == frame.ActivationId && activation.Effects is
                    [{ Op: SkillProgramEffectOp.DrawTurnOwnerThenDiscardMaximumHandForDodge }]) &&
            decision.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") == "program-dodge" &&
                choice.Parameters.GetValueOrDefault("skill") == frame.SkillId &&
                choice.Parameters.GetValueOrDefault("activation") == frame.ActivationId);
    }

    private bool IsWithinAttackRange(int actorSeat, int targetSeat) =>
        IsGameFactionAttackRangeTarget(actorSeat, targetSeat) ||
        GetCombatDistance(actorSeat, targetSeat) <= GetAttackRange(actorSeat);
}
