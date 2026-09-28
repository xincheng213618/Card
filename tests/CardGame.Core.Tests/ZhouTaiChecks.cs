using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhouTaiChecks
{
    public static void BuquWoundsHandLimitAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var fixture = FindFirstWound(registry);
        var game = fixture.Game;
        var duplicate = fixture.Duplicate;
        var owner = fixture.FirstWoundOwner;
        var resolved = game.Events.Select(item => item.Payload).OfType<ProgramUniqueRankDyingResolvedEvent>()
            .First(item => item.RankWasUnique);
        Require(owner is { GeneralId: "classic:zhou-tai", Hp: 1, IsAlive: true } &&
                owner.BuquWounds?.Count == 1 && owner.BuquWounds[0].Id == resolved.CardId &&
                fixture.FirstWoundHandLimit == 1 &&
                resolved is { OwnerSeat: 0, SkillId: "classic:buqu", RankWasUnique: true } &&
                game.CreateCardZoneDiagnostics().Single(card => card.CardId == resolved.CardId).Location ==
                    CardLocation.BuquWound(0),
            "A first unique Buqu rank must become one public wound and restore Zhou Tai to one HP. " +
            $"state: hp={owner.Hp} alive={owner.IsAlive} wounds=[{string.Join(',', owner.BuquWounds ?? [])}] " +
            $"resolvedCard={resolved.CardId} unique={resolved.RankWasUnique} handLimit={fixture.FirstWoundHandLimit}; " +
            $"uniqueEvents={game.Events.Select(item => item.Payload).OfType<ProgramUniqueRankDyingResolvedEvent>().Count(item => item.RankWasUnique)}.");

        Require(!duplicate.RankWasUnique && game.CardMovements.Any(move =>
                    move.CardId == duplicate.CardId && move.From == CardLocation.DrawPile &&
                    move.To == CardLocation.DiscardPile &&
                    move.Reason.Value == "skill-program.dying-rank.duplicate"),
            "A repeated Buqu rank must enter the discard pile and leave the normal rescue/death path active.");

        var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                replay.CreateCardZoneDiagnostics().SequenceEqual(game.CreateCardZoneDiagnostics()),
            "A completed Buqu wound and its dedicated card zone must replay exactly.");

    }

    private static Fixture FindFirstWound(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 8_192; seed++)
        {
          // A seed whose unrelated cast hits an engine limitation or ends before
          // Zhou Tai reaches a repeated Buqu rank is not the scenario under test:
          // skip it and keep scanning.
          try
          {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
                ModeId = "identity:classic-5", UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 100
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } setup ||
                !setup.ValidContentIds.Contains("classic:zhou-tai") ||
                !game.Submit(new SelectGeneralCommand(0, "classic:zhou-tai", game.Revision, setup.PromptId)).Accepted)
                continue;
            for (var step = 0; step < 64 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard) continue;
            for (var step = 0; step < 1200 && game.State.Status != EngineStatus.Completed; step++)
            {
                var command = NextCommand(game);
                if (command is null) break;
                var eventCount = game.Events.Count;
                var result = game.Submit(command);
                if (!result.Accepted) break;
                if (game.Events.Skip(eventCount).Any(item => item.Payload is ProgramUniqueRankDyingResolvedEvent))
                {
                    var firstWoundOwner = game.CreateSnapshot(0, revealAll: true).Players[0];
                    var firstWoundHandLimit = ReadHandLimit(game, 0);
                    var duplicate = DriveUntilDuplicate(game);
                    if (game.Events.Select(item => item.Payload).OfType<ProgramUniqueRankDyingResolvedEvent>()
                            .Count(item => item.RankWasUnique) == 1)
                        return new Fixture(game, duplicate, firstWoundOwner, firstWoundHandLimit);
                    break;
                }
            }
          }
          catch (InvalidOperationException)
          {
          }
        }
        throw new InvalidOperationException("No bounded Zhou Tai fixture reached a unique Buqu wound.");
    }

    private static GameCommand? NextCommand(GameEngine game)
    {
        if (game.PendingDecision is not { PlayerSeat: 0 } prompt)
            return new AdvanceOneStepCommand(game.Revision);
        if (prompt.Kind == DecisionKind.PlayCard)
            return new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId);
        var choice = prompt.Choices.FirstOrDefault(candidate => candidate.Cards.Count == 0) ??
                     prompt.Choices.FirstOrDefault();
        return choice is null ? null : new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision);
    }

    private static ProgramUniqueRankDyingResolvedEvent DriveUntilDuplicate(GameEngine game)
    {
        for (var step = 0; step < 5_000 && game.State.Status != EngineStatus.Completed; step++)
        {
            var duplicate = game.Events.Select(item => item.Payload).OfType<ProgramUniqueRankDyingResolvedEvent>()
                .LastOrDefault(item => !item.RankWasUnique);
            if (duplicate is not null) return duplicate;
            var command = NextCommand(game) ?? throw new InvalidOperationException("Buqu duplicate fixture lost its command path.");
            var result = game.Submit(command);
            Require(result.Accepted, result.Error?.Message ?? "Could not advance to a repeated Buqu rank.");
        }
        var finalDuplicate = game.Events.Select(item => item.Payload).OfType<ProgramUniqueRankDyingResolvedEvent>()
            .LastOrDefault(item => !item.RankWasUnique);
        return finalDuplicate ?? throw new InvalidOperationException("No repeated Buqu rank occurred before the bounded game ended.");
    }

    private static int ReadHandLimit(GameEngine game, int seat)
    {
        var players = (System.Collections.IList)typeof(GameEngine)
            .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
        return (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [players[seat]])!;
    }

    private sealed record Fixture(
        GameEngine Game,
        ProgramUniqueRankDyingResolvedEvent Duplicate,
        PlayerSnapshot FirstWoundOwner,
        int FirstWoundHandLimit);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
