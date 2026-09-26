using CardGame.Core;

// Test scenarios express their choices through the same command boundary as a host.
// This driver only selects a published command; it has no access to engine continuations.
internal static class TestCommandDriver
{
    public static EngineRunResult DriveStart(this GameEngine game) =>
        Send(game, new StartGameCommand(game.Revision), advance: false);

    public static EngineRunResult DriveAdvance(this GameEngine game) =>
        Send(game, new AdvanceCommand(game.Revision), advance: false, allowCompleted: true);

    public static EngineRunResult DriveAdvanceOneStep(this GameEngine game) =>
        Send(game, new AdvanceOneStepCommand(game.Revision), advance: false, allowCompleted: true);

    public static EngineRunResult DriveHumanSelectGeneral(this GameEngine game, string generalId,
        bool advanceToHumanBoundary = true)
    {
        var prompt = Prompt(game);
        return Send(game, new SelectGeneralCommand(prompt.PlayerSeat, generalId, game.Revision, prompt.PromptId),
            advanceToHumanBoundary);
    }

    public static EngineRunResult DriveHumanPlay(this GameEngine game, int cardId, int? targetSeat = null,
        bool advanceToHumanBoundary = true, CardKind? playedCardKind = null, int? targetCardId = null)
    {
        var prompt = Prompt(game);
        return Send(game, new PlayCardCommand(prompt.PlayerSeat, cardId,
            targetSeat is { } seat ? [seat] : [], game.Revision, prompt.PromptId,
            playedCardKind, targetCardId), advanceToHumanBoundary);
    }

    public static EngineRunResult DriveHumanEndPlay(this GameEngine game, bool advanceToHumanBoundary = true)
    {
        var prompt = Prompt(game);
        return Send(game, new EndPlayPhaseCommand(prompt.PlayerSeat, game.Revision, prompt.PromptId),
            advanceToHumanBoundary);
    }

    public static EngineRunResult DriveHumanRespond(this GameEngine game, bool useDodge,
        bool advanceToHumanBoundary = true)
    {
        var prompt = Prompt(game);
        if (useDodge && prompt.Kind == DecisionKind.Nullification)
            return Answer(game, choice => choice.Parameters.GetValueOrDefault("response") == "nullification",
                advanceToHumanBoundary);
        if (useDodge && prompt.Kind == DecisionKind.RespondDodge)
            return Answer(game, choice => choice.Parameters.GetValueOrDefault("response") == "dodge" ||
                choice.Parameters.GetValueOrDefault("response") == "bagua", advanceToHumanBoundary);

        return Answer(game, choice => prompt.Kind switch
        {
            DecisionKind.Nullification => choice.Parameters.GetValueOrDefault("response") == "pass",
            DecisionKind.ProgramJudgmentReplacement => choice.Cards.Count == 0,
            DecisionKind.ProgramTrigger => choice.Parameters.GetValueOrDefault("action") == "decline" ||
                choice.Parameters.GetValueOrDefault("activation") == "skip",
            _ => choice.Parameters.GetValueOrDefault("response") == "take-damage"
        }, advanceToHumanBoundary);
    }

    public static EngineRunResult DriveHumanRespondSlash(this GameEngine game, bool useSlash,
        int? requestedSlashCardId = null, bool advanceToHumanBoundary = true) =>
        Answer(game, choice => choice.Parameters.GetValueOrDefault("response") ==
                (useSlash ? "slash" : "take-damage") &&
            (requestedSlashCardId is null || choice.Cards.Contains(requestedSlashCardId.Value)),
            advanceToHumanBoundary);

    public static EngineRunResult DriveHumanRespondNullification(this GameEngine game,
        bool useNullification, int? requestedNullificationCardId = null,
        bool advanceToHumanBoundary = true) =>
        Answer(game, choice => choice.Parameters.GetValueOrDefault("response") ==
                (useNullification ? "nullification" : "pass") &&
            (requestedNullificationCardId is null || choice.Cards.Contains(requestedNullificationCardId.Value)),
            advanceToHumanBoundary);

    public static EngineRunResult DriveHumanRespondDying(this GameEngine game, bool usePeach,
        int? requestedPeachCardId = null, bool advanceToHumanBoundary = true,
        bool useAlcohol = false, int? requestedAlcoholCardId = null) =>
        Answer(game, choice => choice.Parameters.GetValueOrDefault("response") ==
                (usePeach ? "peach" : useAlcohol ? "alcohol" : "let-die") &&
            (requestedPeachCardId is null || choice.Cards.Contains(requestedPeachCardId.Value)) &&
            (requestedAlcoholCardId is null || choice.Cards.Contains(requestedAlcoholCardId.Value)),
            advanceToHumanBoundary);

    public static EngineRunResult DriveHumanRespondFeedback(this GameEngine game, bool useFeedback,
        bool advanceToHumanBoundary = true) =>
        Answer(game, choice => useFeedback
            ? choice.Parameters.GetValueOrDefault("response") == "feedback" ||
              choice.Parameters.GetValueOrDefault("program-action") == "activate"
            : choice.Parameters.GetValueOrDefault("action") == "skip-damage-skill" ||
              choice.Parameters.GetValueOrDefault("program-action") == "skip",
            advanceToHumanBoundary);

    public static EngineRunResult DriveHumanRespondJieming(this GameEngine game, int? targetSeat,
        bool advanceToHumanBoundary = true) =>
        Answer(game, choice => targetSeat is { } seat
            ? choice.Targets.Contains(seat) &&
              choice.Parameters.GetValueOrDefault("response") == "jieming-draw"
            : choice.Parameters.GetValueOrDefault("response") == "jieming-skip",
            advanceToHumanBoundary);

    public static EngineRunResult DriveHumanRespondYuanhu(this GameEngine game, int? discardCardId,
        bool advanceToHumanBoundary = true) =>
        Answer(game, choice => discardCardId is { } cardId
            ? choice.Cards.Contains(cardId) && choice.Parameters.GetValueOrDefault("response") == "yuanhu"
            : choice.Parameters.GetValueOrDefault("response") == "yuanhu-skip",
            advanceToHumanBoundary);

    public static EngineRunResult DriveHumanSelectHarvestCard(this GameEngine game, int cardId,
        bool advanceToHumanBoundary = true) =>
        Answer(game, choice => choice.Cards.Contains(cardId) &&
            choice.Parameters.GetValueOrDefault("action") == "harvest-pick", advanceToHumanBoundary);

    public static EngineRunResult DriveHumanSelectTargetCardSlot(this GameEngine game, int slot,
        bool advanceToHumanBoundary = true) =>
        Answer(game, choice => choice.Parameters.GetValueOrDefault("slot-index") == slot.ToString(
            System.Globalization.CultureInfo.InvariantCulture), advanceToHumanBoundary);

    public static EngineRunResult DriveHumanSelectFireAttackCard(this GameEngine game, int cardId,
        bool advanceToHumanBoundary = true) =>
        Answer(game, choice => choice.Cards.Contains(cardId) &&
            choice.Parameters.GetValueOrDefault("response") is "fire-attack-reveal" or "fire-attack-discard",
            advanceToHumanBoundary);

    private static EngineRunResult Answer(GameEngine game, Func<PromptChoice, bool> predicate,
        bool advanceToHumanBoundary)
    {
        var prompt = Prompt(game);
        var choice = prompt.Choices.FirstOrDefault(predicate) ??
            throw new InvalidOperationException($"No published {prompt.Kind} choice matches this test action.");
        return Send(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision),
            advanceToHumanBoundary);
    }

    private static PendingDecision Prompt(GameEngine game) => game.PendingDecision ??
        throw new InvalidOperationException("The test action requires a published prompt.");

    private static EngineRunResult Send(GameEngine game, GameCommand command, bool advance,
        bool allowCompleted = false)
    {
        var result = game.Submit(command);
        if (!result.Accepted && !(allowCompleted && result.Error?.Code == CommandErrorCode.Completed))
            throw new InvalidOperationException(result.Error?.Message ?? "The test command was rejected.");
        if (!advance || result.Status == EngineStatus.Completed) return result.Result;
        var next = game.Submit(new AdvanceCommand(game.Revision));
        if (!next.Accepted && next.Error?.Code != CommandErrorCode.Completed)
            throw new InvalidOperationException(next.Error?.Message ?? "The test advance was rejected.");
        return next.Result;
    }
}
