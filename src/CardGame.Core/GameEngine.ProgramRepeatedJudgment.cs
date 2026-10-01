namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome BeginProgramRepeatedJudgment(ProgramSkillFrame frame,
        string reason, string resultBind, IReadOnlyList<Suit> successSuits)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.RepeatedJudgment is not null || successSuits.Count == 0 ||
            !_players[frame.OwnerSeat].IsAlive)
            throw new InvalidOperationException("Repeated judgment requires one active owner and success suit set.");
        active = active with
        {
            RepeatedJudgment = new ProgramRepeatedJudgment(reason, resultBind,
                Array.AsReadOnly(successSuits.ToArray()), 0)
        };
        ReplaceRuntimeTop(active);
        return StartProgramJudgment(active, frame.OwnerSeat, reason, resultBind,
            SkillProgramCardSetVisibility.Public, frame.OwnerSeat);
    }

    private void ResumeProgramRepeatedJudgment(JudgmentFrame pending)
    {
        var frame = GetActiveProgramFrame(pending.ParentFrameId);
        var state = frame.RepeatedJudgment ??
            throw new InvalidOperationException("Repeated judgment lost its program state.");
        var card = GetJudgmentCard(pending);
        if (card is null || !_players[frame.OwnerSeat].IsAlive)
        {
            ReplaceRuntimeTop(frame with { RepeatedJudgment = null });
            AdvanceRuntimeProgram(frame.Id);
            return;
        }
        var matched = state.SuccessSuits.Contains(EffectiveSuit(_players[pending.TargetSeat], card));
        var location = _cardZones.GetLocation(card.Id);
        if (location != CardLocation.Judgment(pending.TargetSeat))
            throw new InvalidOperationException("Repeated judgment card left its resolution zone.");
        ReplaceRuntimeTop(frame with
        {
            RepeatedJudgment = state with { CompletedCount = checked(state.CompletedCount + 1),
                LastMatched = matched },
            PendingMovementContinuation = new ProgramMovementContinuation(frame.OwnerSeat, 0, null)
        });
        MoveCard(card, location, matched ? CardLocation.Hand(frame.OwnerSeat) : CardLocation.DiscardPile,
            new CardMoveReason($"skill-program.{frame.SkillId}.repeatJudgment"));
        if (matched)
            AdvanceEventRulesAndQueueFact(new ProgramJudgmentCardClaimedEvent(
                pending.Id, frame.SkillId, frame.OwnerSeat, card.Id, card.Kind));
        if (!TryBeginCardsMovedProgramWindow())
            ReturnRuntimeProgramMovement(frame.Id);
    }

    private void ContinueProgramRepeatedJudgmentAfterMovement(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId);
        var state = frame.RepeatedJudgment ??
            throw new InvalidOperationException("Repeated judgment lost its movement result.");
        if (state.LastMatched != true)
        {
            ReplaceRuntimeTop(frame with { RepeatedJudgment = null });
            AdvanceRuntimeProgram(frameId);
            return;
        }
        _pendingDecision = new PendingDecision(DecisionKind.ProgramRepeatJudgment, frame.OwnerSeat,
            "判定符合继续条件，是否再次判定？", [], [])
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = frame.OwnerSeat,
            Choices =
            [
                new PromptChoice(new ChoiceId($"program-repeat-judgment.{frameId}.continue"),
                    "继续判定。", [], [], new Dictionary<string, string> { ["action"] = "continue" }),
                new PromptChoice(new ChoiceId($"program-repeat-judgment.{frameId}.stop"),
                    "停止判定。", [], [], new Dictionary<string, string> { ["action"] = "stop" })
            ]
        };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private CommandResult SubmitProgramRepeatJudgmentAnswer(PromptChoice choice) => Accept(() =>
    {
        ResolveProgramRepeatJudgmentChoice(choice);
        AdvanceRulesAndPublishState();
        if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary();
    });

    private void ResolveProgramRepeatJudgmentChoice(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("Repeated judgment has no active program frame.");
        var state = frame.RepeatedJudgment;
        if (state?.LastMatched != true ||
            _pendingDecision is not { Kind: DecisionKind.ProgramRepeatJudgment, PlayerSeat: var seat } decision ||
            seat != frame.OwnerSeat || !decision.Choices.Any(item => item.Id == choice.Id))
            throw new InvalidOperationException("Repeated judgment choice is no longer current.");
        var action = choice.Parameters.GetValueOrDefault("action");
        ClearPendingDecision();
        if (action == "stop")
        {
            ReplaceRuntimeTop(frame with { RepeatedJudgment = null });
            AdvanceRuntimeProgram(frame.Id);
            return;
        }
        if (action != "continue")
            throw new InvalidOperationException("Repeated judgment choice is unsupported.");
        frame = frame with { RepeatedJudgment = state with { LastMatched = null } };
        ReplaceRuntimeTop(frame);
        _ = StartProgramJudgment(frame, frame.OwnerSeat, state.Reason, state.ResultBind,
            SkillProgramCardSetVisibility.Public, frame.OwnerSeat);
    }

    private bool IsAiProgramRepeatJudgmentPending() => _pendingDecision is
        { Kind: DecisionKind.ProgramRepeatJudgment, PlayerSeat: var seat } && !_players[seat].IsHuman;

    private void ResolvePendingAiProgramRepeatJudgment()
    {
        var decision = _pendingDecision ?? throw new InvalidOperationException("AI repeated judgment has no prompt.");
        ResolveProgramRepeatJudgmentChoice(decision.Choices[0]);
        AdvanceRulesAndPublishState();
    }
}
