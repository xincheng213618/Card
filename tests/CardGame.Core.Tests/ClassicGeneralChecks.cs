using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ClassicGeneralChecks
{

    public static void SetupHealthAndReplay()
    {
        var registry = CreatePreProgramClassicRegistry();
        var currentSun = SelectGeneral(registry, "classic:sun-quan");
        var currentSunPlayer = currentSun.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(currentSunPlayer.MaxHp == 5 && currentSunPlayer.Hp == 5,
            "A classic 4-HP Lord must receive the identity-mode +1 maximum HP.");
        Require(currentSunPlayer.Skills is { Count: 2 } &&
                currentSunPlayer.Skills.Select(skill => skill.ContentId).SequenceEqual(["classic:zhiheng", "classic:jiuyuan"]),
            "The current snapshot must publish the selected general's ordered skill list.");

        var simaYi = SelectGeneral(registry, "classic:sima-yi");
        var simaYiPlayer = simaYi.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(simaYiPlayer.MaxHp == 4 &&
                simaYiPlayer.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:feedback", "classic:guicai"]),
            "Sima Yi must combine base 3 HP, the Lord bonus, Feedback and Guicai.");
        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(simaYi.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(simaYi.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(simaYi)),
            "A selected multi-skill classic general must replay exactly.");
    }






    private sealed class TuxiTransferScenario : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("tuxi-transfer-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder)
        {
            var pool = new List<string> { "classic:zhang-liao" };
            for (var index = 0; index < 4; index++)
            {
                var id = "fixture:tuxi-bank-" + index;
                pool.Add(id);
                builder.AddGeneral(new(id, "测试对手", "supporter", "standard:none", "qun"));
            }
            builder.AddMode(new("identity:classic-tuxi-transfer", "突袭转移测试", 5, 5,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 },
                "classic:standard-deck", GeneralCandidateCount: 5, GeneralPoolIds: pool));
        }
    }









    public static void FormalPaoxiaoFlow()
    {
        var registry = CreatePreProgramClassicRegistry();
        var fixture = FindZhangFeiPaoxiaoFixture(registry);
        var game = fixture.Game;
        var before = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(before.GeneralId == "classic:zhang-fei" &&
                before.MaxHp == 5 &&
                before.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:paoxiao"]) &&
                before.Equipment.All(card => card.Kind != CardKind.Crossbow),
            "Classic Zhang Fei must enter the Lord fixture with formal Paoxiao and no Crossbow fallback.");

        var eventCount = game.Events.Count;
        var first = SubmitPlayAction(game, fixture.FirstAction);
        Require(first.Accepted, first.Error?.Message ?? "Classic Zhang Fei could not use his first Slash.");
        Require(TryReturnToHumanPlay(game),
            "Classic Zhang Fei did not return to the same play phase after his first Slash.");

        var secondAction = game.GetHumanLegalActions()
            .Where(action => action.Kind == LegalActionKind.Slash && action.CardId is not null)
            .OrderBy(action => action.CardId)
            .ThenBy(action => action.TargetSeat)
            .FirstOrDefault();
        Require(secondAction is not null,
            "Paoxiao must leave a second physical Slash legal in the same play phase.");
        var second = SubmitPlayAction(game, secondAction!);
        Require(second.Accepted, second.Error?.Message ?? "Paoxiao rejected Zhang Fei's second Slash.");

        var slashUses = game.Events.Skip(eventCount)
            .Select(item => item.Payload)
            .OfType<CardUsedEvent>()
            .Where(item => item.SourceSeat == 0 &&
                           item.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)
            .ToArray();
        Require(slashUses.Length == 2 &&
                slashUses.Select(item => item.CardId).Distinct().Count() == 2,
            "Formal Paoxiao must publish two distinct Slash uses in one play phase.");

        var replayed = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(replayed.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replayed).SequenceEqual(EventSignatures(game)),
            "A second in-flight Paoxiao Slash must replay exactly.");
    }

    public static void FormalLongdanFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var fixture = FindZhaoYunLongdanFixture(registry);
        var game = fixture.Game;
        var before = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        var physicalDodge = before.Hand.Single(card => card.Id == fixture.Action.CardId);
        Require(before.GeneralId == "classic:zhao-yun" &&
                before.MaxHp == 5 &&
                before.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:longdan"]) &&
                physicalDodge.Kind == CardKind.Dodge &&
                fixture.Action.PlayedCardKind == CardKind.Slash,
            "Classic Zhao Yun must publish a physical Dodge as a typed Slash through formal Longdan.");

        var eventCount = game.Events.Count;
        var used = SubmitPlayAction(game, fixture.Action);
        Require(used.Accepted, used.Error?.Message ?? "Classic Zhao Yun could not use Dodge as Slash.");
        Require(TryReturnToHumanPlay(game),
            "Classic Zhao Yun did not finish the converted Slash and return to play.");
        var playEvents = game.Events.Skip(eventCount).Select(item => item.Payload).ToArray();
        Require(playEvents.OfType<CardUseDeclaredEvent>().Any(item =>
                    item.CardId == physicalDodge.Id && item.CardKind == CardKind.Slash) &&
                playEvents.OfType<CardUsedEvent>().Any(item =>
                    item.CardId == physicalDodge.Id && item.CardKind == CardKind.Slash) &&
                game.CardMovements.Any(move =>
                    move.CardId == physicalDodge.Id &&
                    move.CardKind == CardKind.Dodge &&
                    move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Processing &&
                    move.Reason == CardMoveReasons.Use),
            "Formal Longdan must preserve the physical Dodge while publishing an effective Slash use.");

        var activeReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(activeReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(activeReplay).SequenceEqual(EventSignatures(game)),
            "A completed formal Longdan Dodge-to-Slash use must replay exactly.");

        var responseGame = WushengResponseScenario.FindLongdanDodge(
            registry,
            "identity:classic-8");
        var responseOwner = responseGame.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == 0);
        var prompt = responseGame.PendingDecision ??
            throw new InvalidOperationException("The formal Longdan response fixture lost its prompt.");
        var choice = prompt.Choices.First(candidate =>
            candidate.Parameters.GetValueOrDefault("response") == "dodge" &&
            candidate.Parameters.GetValueOrDefault("response-card-kind") == nameof(CardKind.Dodge) &&
            candidate.Cards.Count == 1 &&
            responseOwner.Hand.Single(card => card.Id == candidate.Cards[0]).Kind is
                CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash);
        var physicalSlash = responseOwner.Hand.Single(card => card.Id == choice.Cards[0]);
        Require(responseOwner.GeneralId == "classic:zhao-yun" &&
                prompt.Kind == DecisionKind.RespondDodge &&
                choice.Description.Contains("当作【闪】", StringComparison.Ordinal),
            "Classic Zhao Yun must publish a Slash-to-Dodge response without leaking the physical identity.");

        var response = responseGame.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            choice.Id,
            responseGame.Revision));
        Require(response.Accepted &&
                responseGame.Events.Any(item =>
                    item.Payload is CardRespondedEvent responded &&
                    responded.CardId == physicalSlash.Id &&
                    responded.ResponderSeat == 0 &&
                    responded.EffectiveCardKind == CardKind.Dodge) &&
                responseGame.CardMovements.Any(move =>
                    move.CardId == physicalSlash.Id &&
                    move.CardKind == physicalSlash.Kind &&
                    move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Processing &&
                    move.Reason == CardMoveReasons.Respond),
            response.Error?.Message ??
            "Formal Longdan must preserve the physical Slash while publishing an effective Dodge response.");

        var responseReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(responseGame.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(responseReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(responseGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(responseReplay).SequenceEqual(EventSignatures(responseGame)),
            "An in-flight formal Longdan Slash-to-Dodge response must replay exactly.");
    }







    public static void FormalWushengEquipmentFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(registry.Skills["classic:wusheng"].Program?.ViewAs.Single().SourceZones.Contains(
                CardZoneKind.Equipment) == true,
            "Configured Wusheng must accept eligible equipment sources.");

        var fixture = FindGuanYuWushengEquipmentFixture(registry);
        var activeGame = fixture.ActiveGame;
        var owner = activeGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        var equipment = owner.Equipment.Single(card => card.Id == fixture.EquipmentCardId);
        Require(owner.GeneralId == "classic:guan-yu" &&
                owner.MaxHp == 5 &&
                owner.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:wusheng"]) &&
                equipment.Suit is Suit.Heart or Suit.Diamond &&
                fixture.ActiveAction.PlayedCardKind == CardKind.Slash,
            "Classic Guan Yu must publish a red equipment card as a typed Slash through formal Wusheng.");
        var eventCount = activeGame.Events.Count;
        var used = SubmitPlayAction(activeGame, fixture.ActiveAction);
        Require(used.Accepted, used.Error?.Message ??
            "Classic Guan Yu could not use red equipment as Slash.");
        Require(activeGame.CardMovements.Any(move =>
                    move.CardId == equipment.Id &&
                    move.CardKind == equipment.Kind &&
                    move.From == CardLocation.Equipment(0) &&
                    move.To == CardLocation.Processing &&
                    move.Reason == CardMoveReasons.Use) &&
                activeGame.Events.Skip(eventCount).Select(item => item.Payload)
                    .OfType<CardUsedEvent>().Any(item =>
                        item.CardId == equipment.Id && item.CardKind == CardKind.Slash),
            "Formal Wusheng must retain the physical equipment and publish an effective Slash use.");
        Require(TryReturnToHumanPlay(activeGame) &&
                activeGame.CardMovements.Any(move =>
                    move.CardId == equipment.Id &&
                    move.From == CardLocation.Processing &&
                    move.To == CardLocation.DiscardPile &&
                    move.Reason == CardMoveReasons.UseFinished),
            "The equipped Wusheng Slash did not finish through the ordinary Slash movement chain.");

        var activeReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(activeGame.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(activeReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(activeGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(activeReplay).SequenceEqual(EventSignatures(activeGame)),
            "A completed equipped Wusheng Slash must replay exactly.");

        var responseGame = fixture.ResponseGame;
        var responsePrompt = responseGame.PendingDecision ??
            throw new InvalidOperationException("The equipped Wusheng response fixture lost its prompt.");
        var responseOwner = responseGame.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == 0);
        var responseChoice = responsePrompt.Choices.Single(choice =>
            choice.Cards.SequenceEqual([equipment.Id]) &&
            choice.Parameters.GetValueOrDefault("response-card-kind") == nameof(CardKind.Slash));
        Require(responsePrompt.Kind == DecisionKind.RespondSlash &&
                responseOwner.Equipment.Any(card => card.Id == equipment.Id) &&
                responseChoice.Description.Contains("当作【杀】", StringComparison.Ordinal) &&
                responseGame.CreateSnapshot(1).PendingDecision is null,
            "Formal Wusheng must publish the equipped red card only in Guan Yu's private Slash response.");

        var responseCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(responseGame.CreateCheckpoint()));
        var pausedReplay = GameReplay.Restore(responseCheckpoint, registry);
        Require(SnapshotJson.Serialize(pausedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(responseGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(pausedReplay).SequenceEqual(EventSignatures(responseGame)),
            "An in-flight equipped Wusheng response must replay exactly.");

        var answered = responseGame.Submit(new AnswerPromptCommand(
            0,
            responsePrompt.PromptId,
            responseChoice.Id,
            responseGame.Revision));
        Require(answered.Accepted &&
                responseGame.Events.Any(item =>
                    item.Payload is CardRespondedEvent responded &&
                    responded.CardId == equipment.Id &&
                    responded.ResponderSeat == 0 &&
                    responded.EffectiveCardKind == CardKind.Slash) &&
                responseGame.CardMovements.Any(move =>
                    move.CardId == equipment.Id &&
                    move.CardKind == equipment.Kind &&
                    move.From == CardLocation.Equipment(0) &&
                    move.To == CardLocation.Processing &&
                    move.Reason == CardMoveReasons.Respond) &&
                responseGame.CardMovements.Any(move =>
                    move.CardId == equipment.Id &&
                    move.From == CardLocation.Processing &&
                    move.To == CardLocation.DiscardPile &&
                    move.Reason == CardMoveReasons.ResponseFinished),
            answered.Error?.Message ??
            "Formal Wusheng must pay the equipped physical card through the Slash response chain.");
    }






    public static void FormalFactionSlashResponseFlow()
    {
        var (registry, modeId) = CreateFactionSlashFixtureRegistry(
            "response",
            [
                new ContentDeckCardCount("standard:slash", 60),
                new ContentDeckCardCount("standard:barbarian_assault", 40)
            ]);
        GameEngine? selectedGame = null;
        PendingDecision? selectedPrompt = null;
        FactionSlashRequestedEvent? selectedRequest = null;
        for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = 1,
                HumanRole = Role.Loyalist,
                ModeId = modeId,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 80
            }, registry);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "FactionSlash response fixture failed to start.");
            var full = game.CreateSnapshot(1, revealAll: true);
            if (full.Players.Single(player => player.Role == Role.Lord).GeneralId != "classic:liu-bei")
            {
                continue;
            }

            for (var step = 0; step < 2_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var decision = game.PendingDecision;
                if (decision?.PlayerSeat == 1 &&
                    decision.Kind == DecisionKind.RespondSlash &&
                    decision.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "faction-slash-slash"))
                {
                    var request = game.Events.Select(envelope => envelope.Payload)
                        .OfType<FactionSlashRequestedEvent>()
                        .LastOrDefault();
                    if (request is { IsActiveUse: false })
                    {
                        selectedGame = game;
                        selectedPrompt = decision;
                        selectedRequest = request;
                        break;
                    }
                }

                GameCommand command;
                if (decision is null || decision.PlayerSeat != 1)
                {
                    command = new AdvanceOneStepCommand(game.Revision);
                }
                else if (decision.Kind == DecisionKind.PlayCard)
                {
                    command = new EndPlayPhaseCommand(1, game.Revision, decision.PromptId);
                }
                else if (decision.Kind == DecisionKind.DiscardCards)
                {
                    command = new DiscardCardsCommand(
                        1,
                        decision.ValidCardIds.Take(decision.RequiredCardCount).ToArray(),
                        decision.PromptId,
                        game.Revision);
                }
                else
                {
                    command = new AnswerPromptCommand(
                        1,
                        decision.PromptId,
                        DeclineChoice(decision).Id,
                        game.Revision);
                }

                var advanced = game.Submit(command);
                if (!advanced.Accepted)
                {
                    break;
                }
            }
        }

        if (selectedGame is null || selectedPrompt is null || selectedRequest is null)
        {
            throw new InvalidOperationException("No deterministic response FactionSlash provider boundary was found.");
        }

        var gameWithResponse = selectedGame;
        var prompt = selectedPrompt;
        var requestEvent = selectedRequest;
        var ownerSeat = requestEvent.OwnerSeat;
        Require(prompt.IsPrivate && prompt.TargetSeat == ownerSeat && prompt.SourceSeat == ownerSeat &&
                gameWithResponse.CreateSnapshot(ownerSeat).PendingDecision is null &&
                Enumerable.Range(0, gameWithResponse.PlayerCount)
                    .Where(seat => seat != 1)
                    .All(seat => gameWithResponse.CreateSnapshot(seat).PendingDecision is null),
            "A response FactionSlash prompt must be private to exactly one Shu provider.");

        var beforeForgery = SnapshotJson.Serialize(gameWithResponse.CreateSnapshot(1, revealAll: true));
        var beforeForgeryRevision = gameWithResponse.Revision;
        var forged = gameWithResponse.Submit(new AnswerPromptCommand(
            1,
            prompt.PromptId,
            new ChoiceId("faction-slash.forged"),
            gameWithResponse.Revision));
        Require(!forged.Accepted && gameWithResponse.Revision == beforeForgeryRevision &&
                SnapshotJson.Serialize(gameWithResponse.CreateSnapshot(1, revealAll: true)) == beforeForgery,
            "A forged FactionSlash provider choice must be rejected atomically.");

        var paused = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(gameWithResponse.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(paused.CreateSnapshot(1, revealAll: true)) ==
                SnapshotJson.Serialize(gameWithResponse.CreateSnapshot(1, revealAll: true)) &&
                EventSignatures(paused).SequenceEqual(EventSignatures(gameWithResponse)),
            "A paused response FactionSlash provider prompt must replay exactly.");

        var slashChoice = prompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("response") == "faction-slash-slash");
        var slashCardId = slashChoice.Cards.Single();
        var answered = gameWithResponse.Submit(new AnswerPromptCommand(
            1,
            prompt.PromptId,
            slashChoice.Id,
            gameWithResponse.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? "The FactionSlash Slash response was rejected.");
        var resolved = gameWithResponse.Events.Select(envelope => envelope.Payload)
            .OfType<FactionSlashResolvedEvent>()
            .Last(item => item.ResolutionId == requestEvent.ResolutionId);
        Require(resolved is { Succeeded: true, IsActiveUse: false, ProviderSeat: 1 } &&
                resolved.OwnerSeat == ownerSeat && resolved.SlashCardId == slashCardId &&
                resolved.EffectiveSlashKind == CardKind.Slash &&
                gameWithResponse.CardMovements.Any(movement =>
                    movement.CardId == slashCardId &&
                    movement.From == CardLocation.Hand(1) &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.Respond) &&
                gameWithResponse.CardMovements.Any(movement =>
                    movement.CardId == slashCardId &&
                    movement.From == CardLocation.Processing &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.ResponseFinished) &&
                gameWithResponse.Events.Select(envelope => envelope.Payload).OfType<CardRespondedEvent>().Any(response =>
                    response.CardId == slashCardId && response.ResponderSeat == ownerSeat &&
                    response.EffectiveCardKind == CardKind.Slash),
            "Response FactionSlash must spend the provider's exact Slash while publishing Liu Bei as the responder.");

        var replayed = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(gameWithResponse.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(replayed.CreateSnapshot(1, revealAll: true)) ==
                SnapshotJson.Serialize(gameWithResponse.CreateSnapshot(1, revealAll: true)) &&
                EventSignatures(replayed).SequenceEqual(EventSignatures(gameWithResponse)),
            "A completed response FactionSlash branch must replay exactly.");
    }

    private static (ContentRegistry Registry, string ModeId) CreateFactionSlashFixtureRegistry(
        string suffix,
        IReadOnlyList<ContentDeckCardCount> cards)
    {
        var modeId = $"identity:classic-faction-slash-{suffix}-test";
        var deckId = $"test:faction-slash-{suffix}-deck";
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new SyntheticPackage(
                $"faction-slash-{suffix}-test",
                builder =>
                {
                    builder.AddDeck(new ContentDeckRecipe(
                        deckId,
                        $"激将{suffix}测试牌堆",
                        InitialHandSize: 4,
                        DrawPerTurn: 2,
                        Cards: cards));
                    builder.AddMode(new ContentModeDefinition(
                        modeId,
                        $"激将{suffix}测试身份局",
                        MinPlayers: 5,
                        MaxPlayers: 5,
                        RoleCounts: new Dictionary<string, int>
                        {
                            [nameof(Role.Lord)] = 1,
                            [nameof(Role.Loyalist)] = 1,
                            [nameof(Role.Rebel)] = 2,
                            [nameof(Role.Renegade)] = 1
                        },
                        DeckId: deckId,
                        GeneralCandidateCount: 5,
                        GeneralPoolIds:
                        [
                            "classic:liu-bei",
                            "standard:zhang-fei",
                            "standard:liu-bei",
                            "standard:zhuge-liang",
                            "classic:zhuge-liang"
                        ]));
                },
                new PackageDependency("standard-classic-generals", new Version(1, 5, 0))));
        return (registry, modeId);
    }




    private static GameEngine? StartClassicGeneralAtPlay(
        ContentRegistry registry,
        int seed,
        string generalId)
    {
        var game = CreateInteractive(registry, seed);
        var started = game.Submit(new StartGameCommand());
        Require(started.Accepted, started.Error?.Message ?? "Classic active-skill fixture failed to start.");
        if (started.Result.PendingDecision?.Choices.Any(choice =>
                choice.ContentIds.SequenceEqual([generalId])) != true)
        {
            return null;
        }

        var selected = game.Submit(new SelectGeneralCommand(
            0,
            generalId,
            game.Revision,
            game.PendingDecision!.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? $"Could not select {generalId}.");
        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted, advanced.Error?.Message ?? "Classic active-skill setup did not advance.");
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Classic active-skill fixture did not stop at the human play phase.");
        return game;
    }

    private static (
        GameEngine Game,
        LegalAction FirstAction) FindZhangFeiPaoxiaoFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:zhang-fei");
            if (game is null)
            {
                continue;
            }

            var self = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
            if (self.Hand.Count(card =>
                    card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) < 2)
            {
                continue;
            }

            var beforeFirstSlash = GameCheckpointJson.Deserialize(
                GameCheckpointJson.Serialize(game.CreateCheckpoint()));
            foreach (var firstAction in game.GetHumanLegalActions()
                         .Where(action => action.Kind == LegalActionKind.Slash && action.CardId is not null)
                         .OrderBy(action => action.CardId)
                         .ThenBy(action => action.TargetSeat))
            {
                var probe = GameReplay.Restore(beforeFirstSlash, registry);
                var matchingAction = probe.GetHumanLegalActions().Single(action =>
                    action.Kind == firstAction.Kind &&
                    action.CardId == firstAction.CardId &&
                    action.TargetSeats.SequenceEqual(firstAction.TargetSeats) &&
                    action.PlayedCardKind == firstAction.PlayedCardKind);
                var used = SubmitPlayAction(probe, matchingAction);
                if (!used.Accepted ||
                    !TryReturnToHumanPlay(probe) ||
                    !probe.GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.Slash && action.CardId is not null))
                {
                    continue;
                }

                return (game, firstAction);
            }
        }

        throw new InvalidOperationException(
            "Could not find a deterministic classic Zhang Fei two-Slash Paoxiao fixture.");
    }

    private static (
        GameEngine Game,
        LegalAction Action) FindZhaoYunLongdanFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:zhao-yun");
            if (game is null)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            var self = full.Players.Single(player => player.Seat == 0);
            var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            {
                if (candidate.Kind != LegalActionKind.Slash ||
                    candidate.PlayedCardKind != CardKind.Slash ||
                    candidate.CardId is not { } cardId ||
                    candidate.TargetSeat is not { } targetSeat ||
                    self.Hand.Single(card => card.Id == cardId).Kind != CardKind.Dodge)
                {
                    return false;
                }

                var target = full.Players.Single(player => player.Seat == targetSeat);
                return target.Hp > 1 &&
                       target.Hand.All(card => card.Kind != CardKind.Dodge) &&
                       target.Equipment.All(card => card.Kind != CardKind.BaguaFormation) &&
                       target.Skills?.All(skill =>
                           skill.ContentId is not ("classic:qingguo" or "classic:longdan" or "classic:hujia")) != false;
            });
            if (action is not null)
            {
                return (game, action);
            }
        }

        throw new InvalidOperationException(
            "Could not find a deterministic classic Zhao Yun Longdan conversion fixture.");
    }

    private static (
        GameEngine ActiveGame,
        GameEngine ResponseGame,
        int EquipmentCardId,
        LegalAction ActiveAction) FindGuanYuWushengEquipmentFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var current = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:guan-yu");
            if (current is null)
            {
                continue;
            }

            var self = current.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
            var redEquipment = self.Hand.FirstOrDefault(card =>
                EquipmentCatalog.IsEquipment(card.Kind) &&
                card.Suit is Suit.Heart or Suit.Diamond);
            if (redEquipment is null)
            {
                continue;
            }

            Equip(current, redEquipment.Id);
            var activeAction = current.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash &&
                action.CardId == redEquipment.Id &&
                action.PlayedCardKind == CardKind.Slash);
            if (activeAction is null)
            {
                continue;
            }

            var response = GameReplay.Restore(
                GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(current.CreateCheckpoint())),
                registry);
            var playPrompt = response.PendingDecision ??
                throw new InvalidOperationException("The equipped Wusheng fixture lost its play prompt.");
            var ended = response.Submit(new EndPlayPhaseCommand(
                0,
                response.Revision,
                playPrompt.PromptId));
            Require(ended.Accepted, ended.Error?.Message ??
                "The equipped Wusheng response fixture could not end the play phase.");

            for (var step = 0; step < 4_000 && response.State.Status != EngineStatus.Completed; step++)
            {
                if (response.PendingDecision is
                    {
                        Kind: DecisionKind.RespondSlash,
                        PlayerSeat: 0
                    } responsePrompt &&
                    responsePrompt.IncomingCard is CardKind.Duel or CardKind.BarbarianAssault &&
                    responsePrompt.Choices.Any(choice =>
                        choice.Cards.SequenceEqual([redEquipment.Id]) &&
                        choice.Parameters.GetValueOrDefault("response-card-kind") == nameof(CardKind.Slash)))
                {
                    return (current, response, redEquipment.Id, activeAction);
                }

                var responseOwner = response.CreateSnapshot(0, revealAll: true).Players
                    .Single(player => player.Seat == 0);
                if (!responseOwner.IsAlive ||
                    responseOwner.Equipment.All(card => card.Id != redEquipment.Id))
                {
                    break;
                }

                DeclineOrAdvance(response);
            }
        }

        throw new InvalidOperationException(
            "Could not find a deterministic classic Guan Yu equipped Wusheng use-and-response fixture.");
    }

    private static bool TryReturnToHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 64 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard })
            {
                return true;
            }

            DeclineOrAdvance(game);
        }

        return false;
    }


    private static CommandResult SubmitPlayAction(GameEngine game, LegalAction action) =>
        game.Submit(new PlayCardCommand(
            0,
            action.CardId ?? throw new InvalidOperationException("The play action has no physical card."),
            action.TargetSeats,
            game.Revision,
            game.PendingDecision?.PromptId ??
            throw new InvalidOperationException("The play action has no current prompt."),
            action.PlayedCardKind,
            action.TargetCardId)
        {
            ConversionSource = action.ConversionSource,
            AdditionalConversionSources = action.AdditionalConversionSources,
        });


    private static void Equip(GameEngine game, int cardId)
    {
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Equipment fixture lost its play prompt.");
        var equipped = game.Submit(new PlayCardCommand(
            0,
            cardId,
            [],
            game.Revision,
            prompt.PromptId));
        Require(equipped.Accepted, equipped.Error?.Message ?? "Could not equip the Zhiheng fixture card.");
        if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
        {
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ??
                "Could not return the Zhiheng fixture to the human play boundary.");
        }
        Require(game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0)
                .Equipment.Any(card => card.Id == cardId),
            "The Zhiheng fixture card did not enter the equipment zone.");
    }

    private static ContentRegistry CreatePreProgramClassicRegistry() =>
        StandardContentRegistry.CreateWithClassicGenerals();

    private static GameEngine SelectGeneral(ContentRegistry registry, string generalId,
        Func<GameEngine, bool>? fixtureFilter = null)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Classic selection fixture failed to start.");
            if (started.Result.PendingDecision?.Choices.Any(choice => choice.ContentIds.SequenceEqual([generalId])) != true)
                continue;
            var selected = game.Submit(new SelectGeneralCommand(
                0,
                generalId,
                game.Revision,
                game.PendingDecision!.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? $"Could not select {generalId}.");
            if (fixtureFilter is not null)
            {
                for (var step = 0; step < 80 &&
                        game.CreateSnapshot(0, true).Players[0].Hand.Count == 0 &&
                        game.State.Status != EngineStatus.Completed; step++)
                {
                    var dealt = game.Submit(new AdvanceOneStepCommand(game.Revision));
                    Require(dealt.Accepted, dealt.Error?.Message ?? "Classic selection fixture did not deal.");
                }
                if (!fixtureFilter(game))
                    continue;
            }
            return game;
        }

        throw new InvalidOperationException($"No deterministic selection fixture exposed {generalId}.");
    }

    private static GameEngine CreateInteractive(
        ContentRegistry registry,
        int seed,
        Role humanRole = Role.Lord) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = humanRole,
            ModeId = "identity:classic-5",
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 220
        }, registry);

    private enum LiegongFixtureKind
    {
        EligibleByHp,
        EligibleByRange,
        Ineligible
    }

    private static EngineRunResult DeclineOrAdvance(GameEngine game, EngineRunResult? result = null)
    {
        var prompt = game.PendingDecision;
        GameCommand command = prompt?.Kind switch
        {
            null => new AdvanceOneStepCommand(game.Revision),
            DecisionKind.PlayCard => new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId),
            DecisionKind.DiscardCards => new DiscardCardsCommand(
                0,
                prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                prompt.PromptId,
                game.Revision),
            DecisionKind.SelectGeneral => new SelectGeneralCommand(
                0,
                prompt.ValidContentIds[0],
                game.Revision,
                prompt.PromptId),
            _ => new AnswerPromptCommand(
                0,
                prompt.PromptId,
                DeclineChoice(prompt).Id,
                game.Revision)
        };
        var accepted = game.Submit(command);
        if (!accepted.Accepted)
            throw new InvalidOperationException(accepted.Error?.Message ?? $"Could not advance from {result?.Status} / {prompt?.Kind}.");
        return accepted.Result;
    }

    private static PromptChoice DeclineChoice(PendingDecision prompt) =>
        prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.Values.Any(value =>
                value.StartsWith("skip", StringComparison.Ordinal) ||
                value is "take-damage" or "no-nullification" or "ganglie-lose-hp"))
        ?? prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0)
        ?? prompt.Choices.First();

    private static IReadOnlyList<string> EventSignatures(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
