using CardGame.Content.Standard;
using CardGame.Core;

internal static class XiahouYuanChecks
{
    public static void ShensuPhaseSkipsAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = FindFixture(registry);
        var prompt = game.PendingDecision!;
        var paused = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(paused.PendingDecision is { Kind: DecisionKind.ProgramTrigger } restoredPrompt &&
                restoredPrompt.Choices.Select(choice => choice.Id).SequenceEqual(prompt.Choices.Select(choice => choice.Id)),
            "A paused private Shensu phase choice must restore with the same published choices.");
        var handBefore = game.CreateSnapshot(0, revealAll: true).Players[0].Hand.Select(card => card.Id).Order().ToArray();
        var use = prompt.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        var accepted = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, use.Id, game.Revision));
        Require(accepted.Accepted, accepted.Error?.Message ?? "Shensu's first option was rejected.");
        DriveToHumanBoundary(game);

        Require(game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Any(item => item.SkillId == "classic:shensu" &&
                                 item.BindingId == "skip-judgment-and-draw" && item.Completed) &&
                game.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>()
                    .Any(item => item.CardId == 0 && item.CardKind == CardKind.Slash) &&
                game.CreateSnapshot(0, revealAll: true).Players[0].Hand.Select(card => card.Id).Order().SequenceEqual(handBefore) &&
                game.CreateSnapshot(0, revealAll: true).Phase == TurnPhase.Play && game.ResolutionStack.Count == 0,
            "Shensu option one must skip judgment/draw, consume no card and resume at Play after its virtual Slash.");

        var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "Completed Shensu option one must replay exactly.");

        var stageTwoGame = FindFixture(registry);
        EquipFirstHandEquipment(stageTwoGame);
        var nextStageOne = stageTwoGame.PendingDecision!;
        var skipOne = nextStageOne.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
        Require(stageTwoGame.Submit(new AnswerPromptCommand(0, nextStageOne.PromptId, skipOne.Id, stageTwoGame.Revision)).Accepted,
            "Could not skip Shensu option one before the option-two fixture.");
        DriveToStageTwo(stageTwoGame);
        Require(stageTwoGame.PendingDecision is { Kind: DecisionKind.ProgramTrigger } &&
                stageTwoGame.PendingDecision.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "activate" &&
                    choice.Parameters.GetValueOrDefault("binding-id") == "skip-play-after-draw"),
            "An equipped card must open Shensu option two before the play phase.");
        var stageTwo = stageTwoGame.PendingDecision!;
        var useTwo = stageTwo.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        var stageTwoEquipmentId = stageTwoGame.CreateSnapshot(0).Players[0].Equipment.Single().Id;
        Require(stageTwoGame.Submit(new AnswerPromptCommand(0, stageTwo.PromptId, useTwo.Id, stageTwoGame.Revision)).Accepted,
            "Shensu option two was rejected.");
        DriveUntilResolutionFinishes(stageTwoGame);
        Require(stageTwoGame.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Any(item => item.SkillId == "classic:shensu" &&
                                 item.BindingId == "skip-play-after-draw" && item.Completed) &&
                stageTwoGame.CardMovements.Any(move => move.CardId == stageTwoEquipmentId &&
                    move.From == CardLocation.Equipment(0) && move.To == CardLocation.DiscardPile &&
                    move.Reason.Value.Contains("classic:shensu", StringComparison.Ordinal)),
            "Shensu option two must discard the exact equipped card and resolve another virtual Slash.");

        var (chained, chainedRegistry) = FindEquippedFixture();
        var firstPrompt = chained.PendingDecision!;
        var firstUse = firstPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Require(chained.Submit(new AnswerPromptCommand(0, firstPrompt.PromptId, firstUse.Id, chained.Revision)).Accepted,
            "The consecutive Shensu fixture could not use its first virtual Slash.");
        DriveToStageTwo(chained);
        var secondPrompt = chained.PendingDecision!;
        Require(secondPrompt.Kind == DecisionKind.ProgramTrigger && chained.ResolutionStack.Count == 0,
            "Finishing the first Shensu Slash must preserve the newly created second-stage prompt.");
        var resumed = GameReplay.Restore(chained.CreateCheckpoint(), chainedRegistry);
        Require(SnapshotJson.Serialize(resumed.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(chained.CreateSnapshot(0, revealAll: true)),
            "The consecutive Shensu continuation must replay with its second prompt intact.");
        var secondUse = secondPrompt.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        foreach (var branch in new[] { chained, resumed })
        {
            Require(branch.Submit(new AnswerPromptCommand(0, secondPrompt.PromptId, secondUse.Id, branch.Revision)).Accepted,
                "The second consecutive Shensu Slash was rejected.");
            DriveUntilResolutionFinishes(branch);
            Require(branch.CreateSnapshot(0, revealAll: true).Phase == TurnPhase.Discard &&
                    branch.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                        .Where(item => item.SkillId == "classic:shensu" && item.Completed)
                        .Select(item => item.BindingId).SequenceEqual(
                            new[] { "skip-judgment-and-draw", "skip-play-after-draw" }),
                "Both consecutive Shensu Slashes must finish before the discard phase.");
        }
        Require(SnapshotJson.Serialize(resumed.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(chained.CreateSnapshot(0, revealAll: true)),
            "Both branches must finish the consecutive Shensu actions identically.");

    }

    private static (GameEngine Game, ContentRegistry Registry) FindEquippedFixture()
    {
        const string modeId = "identity:classic-shensu-equipment-5";
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new SyntheticPackage("shensu-equipment-fixture", builder =>
            {
                builder.AddDeck(new ContentDeckRecipe(
                    "fixture:shensu-equipment-deck", "神速连续发动牌堆", 4, 2,
                    [new ContentDeckCardCount("standard:crossbow", 80),
                     new ContentDeckCardCount("standard:peach", 80)]));
                builder.AddMode(new ContentModeDefinition(
                    modeId, "神速连续发动身份局", 5, 5,
                    new Dictionary<string, int>
                    {
                        [nameof(Role.Lord)] = 1,
                        [nameof(Role.Loyalist)] = 1,
                        [nameof(Role.Rebel)] = 2,
                        [nameof(Role.Renegade)] = 1
                    },
                    "fixture:shensu-equipment-deck", GeneralCandidateCount: 5,
                    GeneralPoolIds:
                    [
                        "classic:xiahou-yuan", "classic:liu-bei", "classic:sun-quan",
                        "classic:hua-tuo", "classic:cao-cao"
                    ]));
            }));
        var game = FindFixture(registry, modeId);
        var prompt = game.PendingDecision!;
        var skip = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "skip");
        Require(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, skip.Id, game.Revision)).Accepted,
            "Could not reach the equipment setup turn.");
        DriveToHumanBoundary(game);
        var equipment = game.CreateSnapshot(0).Players[0].Hand.First(card => EquipmentCatalog.IsEquipment(card.Kind));
        Require(game.Submit(new PlayCardCommand(0, equipment.Id, [], game.Revision, game.PendingDecision!.PromptId)).Accepted,
            "Could not equip a card through the command boundary.");
        for (var step = 0; step < 512 && game.State.Winner == Winner.None; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } next &&
                next.Choices.Any(choice => choice.Parameters.GetValueOrDefault("binding-id") == "skip-judgment-and-draw") &&
                game.CreateSnapshot(0).Players[0].Equipment.Count > 0)
                return (game, registry);
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } play &&
                game.CreateSnapshot(0).Players[0].Equipment.Count == 0 &&
                game.CreateSnapshot(0, revealAll: true).Players[0].Hand.FirstOrDefault(card =>
                    EquipmentCatalog.IsEquipment(card.Kind)) is { } replacement)
            {
                Require(game.Submit(new PlayCardCommand(0, replacement.Id, [], game.Revision, play.PromptId)).Accepted,
                    "The consecutive Shensu fixture could not replace equipment through a legal command.");
                continue;
            }
            BorrowedSwordScenario.Step(game);
        }
        throw new InvalidOperationException(
            $"The equipped Xiahou Yuan fixture did not return to its first Shensu prompt: " +
            $"status={game.State.Status}, winner={game.State.Winner}, turn={game.State.TurnNumber}, " +
            $"phase={game.State.Phase}, prompt={game.PendingDecision?.Kind}/{game.PendingDecision?.PlayerSeat}, " +
            $"hp={game.CreateSnapshot(0).Players[0].Hp}, equipment={game.CreateSnapshot(0).Players[0].Equipment.Count}.");
    }

    private static GameEngine FindFixture(ContentRegistry registry, string modeId = "identity:classic-5")
    {
        for (var seed = 1; seed <= 4096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
                ModeId = modeId, UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 100
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } setup ||
                !setup.ValidContentIds.Contains("classic:xiahou-yuan") ||
                !game.Submit(new SelectGeneralCommand(0, "classic:xiahou-yuan", game.Revision, setup.PromptId)).Accepted)
                continue;
            for (var step = 0; step < 64 && game.PendingDecision?.Kind != DecisionKind.ProgramTrigger; step++)
                if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
            if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } prompt &&
                prompt.SkillPrompt?.SkillId == "classic:shensu" &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate") &&
                game.CreateSnapshot(0, revealAll: true).Players[0].Hand.Any(card => EquipmentCatalog.IsEquipment(card.Kind)))
                return game;
        }
        throw new InvalidOperationException("No bounded Xiahou Yuan fixture reached the first Shensu choice.");
    }

    private static void DriveToHumanBoundary(GameEngine game)
    {
        for (var step = 0; step < 256 && (game.ResolutionStack.Count > 0 || game.PendingDecision?.Kind != DecisionKind.PlayCard); step++)
        {
            GameCommand command;
            if (game.PendingDecision is { PlayerSeat: 0 } prompt)
            {
                var choice = prompt.Choices.FirstOrDefault(item =>
                    item.Parameters.GetValueOrDefault("response")?.Contains("decline", StringComparison.Ordinal) == true ||
                    item.Parameters.Values.Any(value => value.Contains("skip", StringComparison.Ordinal) || value.Contains("decline", StringComparison.Ordinal)))
                    ?? prompt.Choices.Last();
                command = new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision);
            }
            else command = new AdvanceOneStepCommand(game.Revision);
            var result = game.Submit(command);
            Require(result.Accepted, result.Error?.Message ?? "Could not finish the Shensu virtual Slash.");
        }
    }

    private static void DriveUntilResolutionFinishes(GameEngine game)
    {
        for (var step = 0; step < 256 && game.ResolutionStack.Count > 0; step++)
        {
            GameCommand command = game.PendingDecision is { PlayerSeat: 0 } prompt
                ? new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.Last().Id, game.Revision)
                : new AdvanceOneStepCommand(game.Revision);
            var result = game.Submit(command);
            Require(result.Accepted, result.Error?.Message ?? "Could not finish Shensu option two.");
        }
        Require(game.ResolutionStack.Count == 0, "Shensu option two left an unfinished resolution frame.");
    }

    private static void DriveToStageTwo(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger } prompt &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("binding-id") == "skip-play-after-draw")) return;
            GameCommand command = game.PendingDecision is { PlayerSeat: 0 } decision
                ? new AnswerPromptCommand(0, decision.PromptId, decision.Choices.Last().Id, game.Revision)
                : new AdvanceOneStepCommand(game.Revision);
            var result = game.Submit(command);
            Require(result.Accepted, result.Error?.Message ?? "Could not reach Shensu option two.");
        }
    }

    private static void EquipFirstHandEquipment(GameEngine game)
    {
        var zones = typeof(GameEngine).GetField("_cardZones", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(game)!;
        var cards = (IEnumerable<Card>)zones.GetType().GetMethod("CardsAt")!.Invoke(zones, [CardLocation.Hand(0)])!;
        var equipment = cards.First(card => EquipmentCatalog.IsEquipment(card.Kind));
        typeof(GameEngine).GetMethod("MoveCard", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(game, [equipment, CardLocation.Hand(0), CardLocation.Equipment(0), CardMoveReasons.EquipmentEnter]);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
