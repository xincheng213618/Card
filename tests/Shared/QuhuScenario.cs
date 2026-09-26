using CardGame.Content.Standard;
using CardGame.Core;

internal sealed record QuhuBoundary(
    GameEngine Game,
    GameCheckpoint BeforeUse,
    GameCheckpoint AtPindianPrompt,
    int SourceSeat,
    int OpponentSeat,
    int SourceCardId,
    int SourceHpBefore,
    bool SourceWon);

internal static class QuhuScenario
{
    public static QuhuBoundary Find(bool sourceWins)
        => Find(
            (sourceRank, opponentRank) => (sourceRank > opponentRank) == sourceWins,
            sourceWins,
            sourceWins ? "win" : "loss");

    public static QuhuBoundary FindTie() =>
        Find((sourceRank, opponentRank) => sourceRank == opponentRank, sourceWins: false, "tie");

    private static QuhuBoundary Find(
        Func<int, int, bool> rankMatch,
        bool sourceWins,
        string outcome)
    {
        const int sourceSeat = 0;
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = sourceSeat,
                HumanRole = Role.Rebel,
                PlayerCount = 5,
                ModeId = "identity:classic-5",
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 220,
                AiPolicyVersion = 2
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted, "Quhu fixture failed to start.");
            var selection = game.PendingDecision;
            if (selection is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: sourceSeat } ||
                !selection.Choices.Any(choice => choice.ContentIds.SequenceEqual(["classic:xun-yu"])))
            {
                continue;
            }
            Require(game.Submit(new SelectGeneralCommand(
                sourceSeat, "classic:xun-yu", game.Revision, selection.PromptId)).Accepted,
                "Quhu fixture could not select Xun Yu.");

            for (var step = 0; step < 1_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var pending = game.PendingDecision;
                if (pending is { Kind: DecisionKind.PlayCard, PlayerSeat: sourceSeat })
                {
                    var action = game.GetHumanLegalActions().SingleOrDefault(candidate =>
                        candidate.Kind == LegalActionKind.UseProgramSkill &&
                        candidate.ProgramSkillId == "classic:quhu" && candidate.ProgramActivationId == "contest");
                    if (action is null)
                    {
                        break;
                    }

                    var full = game.CreateSnapshot(sourceSeat, revealAll: true);
                    var source = full.Players[sourceSeat];
                    var candidates = action.SelectableTargetSeats.Select(seat => new
                    {
                        Seat = seat,
                        OpponentMax = full.Players[seat].Hand.Max(card => card.Rank)
                    });
                    var pair = (from sourceCard in source.Hand
                                from opponent in candidates
                                where rankMatch(sourceCard.Rank, opponent.OpponentMax)
                                orderby sourceWins ? -sourceCard.Rank : sourceCard.Rank,
                                    opponent.Seat
                                select new { Card = sourceCard, opponent.Seat }).FirstOrDefault();
                    if (pair is null)
                    {
                        break;
                    }

                    var before = game.CreateCheckpoint();
                    var used = game.Submit(new UseProgramSkillCommand(
                        sourceSeat, "classic:quhu", "contest",
                        [pair.Card.Id],
                        [pair.Seat],
                        game.Revision,
                        pending.PromptId));
                    Require(used.Accepted, used.Error?.Message ?? "Quhu fixture could not start Pindian.");
                    var atPindian = game.CreateCheckpoint();
                    Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                        "Quhu AI opponent could not commit its Pindian card.");
                    return new QuhuBoundary(
                        game, before, atPindian, sourceSeat, pair.Seat, pair.Card.Id, source.Hp, sourceWins);
                }

                var command = pending is null || pending.PlayerSeat != sourceSeat
                    ? (GameCommand)new AdvanceOneStepCommand(game.Revision)
                    : new AnswerPromptCommand(
                        sourceSeat, pending.PromptId, pending.Choices.Last().Id, game.Revision);
                if (!game.Submit(command).Accepted)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException($"No bounded Quhu {outcome} fixture was found.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
