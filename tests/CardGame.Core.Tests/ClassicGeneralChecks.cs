using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ClassicGeneralChecks
{

    public static void SetupHealthAndReplay()
    {
        var registry = CreatePreProgramClassicRegistry();
        var currentSun = SelectGeneral(registry, "classic:sun-quan", GameCheckpoint.CurrentRulesVersion);
        var currentSunPlayer = currentSun.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(currentSunPlayer.MaxHp == 5 && currentSunPlayer.Hp == 5,
            "A classic 4-HP Lord must receive the identity-mode +1 maximum HP.");
        Require(currentSunPlayer.Skills is { Count: 2 } &&
                currentSunPlayer.Skills.Select(skill => skill.ContentId).SequenceEqual(["classic:zhiheng", "classic:jiuyuan"]),
            "The current snapshot must publish the selected general's ordered skill list.");

        var simaYi = SelectGeneral(registry, "classic:sima-yi", GameCheckpoint.CurrentRulesVersion);
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
        Require(GameCheckpoint.CurrentRulesVersion >= 40,
            "Formal Wusheng equipment conversion must have an explicit rules-version boundary.");

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







    private static void SetRuntimeHp(GameEngine game, int seat, int hp)
    {
        var playersField = typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The runtime player list was not found.");
        var players = (System.Collections.IList)(playersField.GetValue(game) ??
            throw new InvalidOperationException("The runtime player list is unavailable."));
        var player = players[seat] ?? throw new InvalidOperationException("The runtime player is unavailable.");
        var hpProperty = player.GetType().GetProperty("Hp") ??
            throw new InvalidOperationException("The runtime HP property was not found.");
        hpProperty.SetValue(player, hp);
    }




    private static GameEngine? StartClassicGeneralAtPlay(
        ContentRegistry registry,
        int seed,
        string generalId,
        int rulesVersion)
    {
        var game = CreateInteractive(registry, seed);
        if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
        {
            game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
        }

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


    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));


    private static bool IsLuoyiActivationPrompt(PendingDecision? prompt) =>
        prompt is { Kind: DecisionKind.ProgramTrigger } ||
        prompt is { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: "classic:luoyi" };

    private static PromptChoice GetLuoyiActivateChoice(PendingDecision prompt) =>
        prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault(
                prompt.Kind == DecisionKind.ProgramTrigger ? "program-action" : "action") ==
            (prompt.Kind == DecisionKind.ProgramTrigger ? "activate" : "luoyi-use"));

    private static (GameEngine Game, LegalAction Action, int TargetHp) FindXuChuDirectAttackFixture(
        ContentRegistry registry,
        LegalActionKind actionKind,
        Func<PlayerSnapshot, bool> targetPredicate,
        bool requireNoNullification = false)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Xu Chu attack fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:xu-chu"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:xu-chu",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Xu Chu.");
            var reachedLuoyi = game.Submit(new AdvanceCommand(game.Revision));
            Require(reachedLuoyi.Accepted, reachedLuoyi.Error?.Message ?? "Xu Chu did not reach Luoyi.");
            var luoyi = game.PendingDecision;
            if (!IsLuoyiActivationPrompt(luoyi))
            {
                continue;
            }

            var used = game.Submit(new AnswerPromptCommand(
                0,
                luoyi.PromptId,
                GetLuoyiActivateChoice(luoyi).Id,
                game.Revision));
            Require(used.Accepted, used.Error?.Message ?? "Could not enable Luoyi for the attack fixture.");
            var reachedPlay = game.Submit(new AdvanceCommand(game.Revision));
            Require(reachedPlay.Accepted, reachedPlay.Error?.Message ?? "Xu Chu did not reach the play phase.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            if (requireNoNullification && full.Players.Any(player =>
                    player.Hand.Any(card => card.Kind == CardKind.Nullification)))
            {
                continue;
            }

            var action = game.GetHumanLegalActions()
                .Where(candidate => candidate.Kind == actionKind && candidate.TargetSeat is not null)
                .FirstOrDefault(candidate => targetPredicate(
                    full.Players.Single(player => player.Seat == candidate.TargetSeat)));
            if (action is null)
            {
                continue;
            }

            var target = full.Players.Single(player => player.Seat == action.TargetSeat);
            return (game, action, target.Hp);
        }

        throw new InvalidOperationException($"Could not find a deterministic Xu Chu {actionKind} fixture.");
    }

    private static (GameEngine Game, int PhysicalCardId, int TargetSeat, int DistanceTwoSeat)
        FindXuHuangDuanliangFixture(ContentRegistry registry)
    {
        var offered = 0;
        var equippedCandidates = 0;
        var convertedCandidates = 0;
        var placedCandidates = 0;
        var resolvedCandidates = 0;
        string? lastError = null;
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Xu Huang fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:xu-huang"])) != true)
            {
                continue;
            }
            offered++;

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:xu-huang",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Xu Huang.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Xu Huang did not reach the play phase.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            if (full.Players.SelectMany(player => player.Hand).Any(card => card.Kind == CardKind.Nullification))
            {
                continue;
            }

            var equipment = full.Players.Single(player => player.Seat == 0).Hand
                .Where(card =>
                    EquipmentCatalog.IsEquipment(card.Kind) &&
                    card.Suit is Suit.Spade or Suit.Club &&
                    EquipmentCatalog.Get(card.Kind).Slot != EquipmentSlot.OffensiveHorse)
                .OrderBy(card => card.Id)
                .FirstOrDefault();
            if (equipment is null)
            {
                continue;
            }
            equippedCandidates++;

            Equip(game, equipment.Id);
            var converted = game.GetHumanLegalActions()
                .Where(action =>
                    action.Kind == LegalActionKind.SupplyShortage &&
                    action.CardId == equipment.Id &&
                    action.PlayedCardKind == CardKind.SupplyShortage)
                .ToArray();
            var targetAction = converted.FirstOrDefault(action => action.TargetSeat == 1);
            var distanceTwoAction = converted.FirstOrDefault(action =>
                action.TargetSeat is { } seat && game.GetCombatDistance(0, seat) == 2);
            if (targetAction is null || distanceTwoAction?.TargetSeat is not { } distanceTwoSeat)
            {
                continue;
            }
            convertedCandidates++;

            var simulated = GameReplay.Restore(game.CreateCheckpoint(), registry);
            var simulatedAction = simulated.GetHumanLegalActions().Single(action =>
                action.Kind == LegalActionKind.SupplyShortage &&
                action.CardId == equipment.Id &&
                action.TargetSeat == 1 &&
                action.PlayedCardKind == CardKind.SupplyShortage);
            var used = simulated.Submit(new PlayCardCommand(
                0,
                equipment.Id,
                simulatedAction.TargetSeats,
                simulated.Revision,
                simulated.PendingDecision!.PromptId,
                CardKind.SupplyShortage)
            { ConversionSource = simulatedAction.ConversionSource });
            if (!used.Accepted ||
                simulated.Events.Select(item => item.Payload).OfType<DelayedCardPlacedEvent>()
                    .All(placed => placed.CardId != equipment.Id))
            {
                lastError = used.Error?.Message ??
                    $"placement accepted={used.Accepted}, status={used.Status}, " +
                    $"pending={simulated.PendingDecision?.Kind}, " +
                    $"events={string.Join(',', simulated.Events.TakeLast(4).Select(item => item.Payload.GetType().Name))}";
                continue;
            }
            var returnedToPlay = simulated.Submit(new AdvanceCommand(simulated.Revision));
            if (!returnedToPlay.Accepted || simulated.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                lastError = returnedToPlay.Error?.Message ??
                    $"return-to-play accepted={returnedToPlay.Accepted}, status={returnedToPlay.Status}, " +
                    $"pending={simulated.PendingDecision?.Kind}";
                continue;
            }
            placedCandidates++;

            var ended = simulated.Submit(new EndPlayPhaseCommand(
                0,
                simulated.Revision,
                simulated.PendingDecision.PromptId));
            var resolved = ended.Accepted
                ? AdvanceUntilDelayedCardResolves(simulated, equipment.Id)
                : null;
            if (!ended.Accepted || resolved?.SkippedDrawPhase != true)
            {
                lastError = ended.Error?.Message ?? $"resolution pending={simulated.PendingDecision?.Kind}";
                continue;
            }
            resolvedCandidates++;

            return (game, equipment.Id, 1, distanceTwoSeat);
        }

        throw new InvalidOperationException(
            $"Could not find a deterministic Xu Huang Duanliang equipment fixture " +
            $"(offered={offered}, equipment={equippedCandidates}, converted={convertedCandidates}, " +
            $"placed={placedCandidates}, resolved={resolvedCandidates}, last={lastError ?? "none"}).");
    }

    private static DelayedCardResolvedEvent? AdvanceUntilDelayedCardResolves(
        GameEngine game,
        int cardId)
    {
        for (var step = 0; step < 2_000 && game.State.Status != EngineStatus.Completed; step++)
        {
            var resolved = game.Events.Select(item => item.Payload)
                .OfType<DelayedCardResolvedEvent>()
                .LastOrDefault(item => item.CardId == cardId);
            if (resolved is not null)
            {
                return resolved;
            }

            if (game.PendingDecision?.PlayerSeat == 0)
            {
                return null;
            }

            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            if (!advanced.Accepted)
            {
                return null;
            }
        }

        return game.Events.Select(item => item.Payload)
            .OfType<DelayedCardResolvedEvent>()
            .LastOrDefault(item => item.CardId == cardId);
    }

    private static (GameEngine Game, LegalAction Action, int TargetSeat, int WeaponCardId)
        FindDianWeiQiangxiFixture(
        ContentRegistry registry,
        bool requireWeapon,
        string? targetSkill = null,
        bool requirePeach = false,
        bool requireTargetHeart = false,
        Func<GameEngine, LegalAction, int, int, bool>? probe = null)
    {
        var damageTriggerSkills = new HashSet<string>
        {
            "standard:feedback",
            "classic:yiji",
            "classic:jieming",
            "standard:yuanhu",
            "classic:ganglie"
        };
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Dian Wei fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:dian-wei"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:dian-wei",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Dian Wei.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Dian Wei did not reach the play phase.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
                candidate.Kind == LegalActionKind.UseProgramSkill &&
                candidate.ProgramSkillId == "classic:qiangxi" &&
                candidate.ProgramActivationId == "discard-weapon-and-damage");
            if (action is null)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            var source = full.Players.Single(player => player.Seat == 0);
            var weaponCardId = source.Hand
                .Where(card => action.SelectableCardIds.Contains(card.Id))
                .Select(card => card.Id)
                .FirstOrDefault();
            if (requireWeapon && weaponCardId == 0)
            {
                continue;
            }
            if (requirePeach && source.Hand.All(card => card.Kind != CardKind.Peach))
            {
                continue;
            }

            var target = full.Players
                .Where(player => action.SelectableTargetSeats.Contains(player.Seat))
                .FirstOrDefault(player =>
                    (targetSkill is { } required
                        ? player.Skills?.Any(skill => skill.ContentId == required) == true
                        : player.Skills?.All(skill => (skill.ContentId is null || !damageTriggerSkills.Contains(skill.ContentId))) != false) &&
                    (!requireTargetHeart || player.Hand.Any(card => card.Suit is Suit.Heart or Suit.Spade)));
            if (target is null)
            {
                continue;
            }

            if (probe is not null && !probe(game, action, target.Seat, weaponCardId))
            {
                continue;
            }

            return (game, action, target.Seat, weaponCardId);
        }

        throw new InvalidOperationException(
            $"Could not find a deterministic Dian Wei Qiangxi fixture " +
            $"(weapon={requireWeapon}, peach={requirePeach}, " +
            $"target-skill={targetSkill?.ToString() ?? "none"}, target-heart={requireTargetHeart}).");
    }

    private static void SetPlayerHp(GameEngine game, int seat, int hp)
    {
        var playersField = typeof(GameEngine).GetField(
            "_players",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine player store was not found.");
        var players = (System.Collections.IList)playersField.GetValue(game)!;
        players[seat]!.GetType().GetProperty("Hp")!.SetValue(players[seat], hp);
    }

    private static (GameEngine Game, int TargetSeat, long ResolutionId, int XuChuHp) FindXuChuReverseDuelFixture(
        ContentRegistry registry)
    {
        var offered = 0;
        var noNullification = 0;
        var duelFound = 0;
        var responsePromptFound = 0;
        var slashChoiceFound = 0;
        var lastPending = "none";
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Reverse Duel fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:xu-chu"])) != true)
            {
                continue;
            }
            offered++;

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:xu-chu",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Xu Chu.");
            var reachedLuoyi = game.Submit(new AdvanceCommand(game.Revision));
            Require(reachedLuoyi.Accepted && IsLuoyiActivationPrompt(game.PendingDecision),
                reachedLuoyi.Error?.Message ?? "Reverse Duel fixture did not reach Luoyi.");
            var luoyi = game.PendingDecision!;
            var used = game.Submit(new AnswerPromptCommand(
                0,
                luoyi.PromptId,
                GetLuoyiActivateChoice(luoyi).Id,
                game.Revision));
            Require(used.Accepted, used.Error?.Message ?? "Could not enable Luoyi for reverse Duel.");
            var reachedPlay = game.Submit(new AdvanceCommand(game.Revision));
            Require(reachedPlay.Accepted, reachedPlay.Error?.Message ?? "Reverse Duel fixture did not reach play.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            if (full.Players.Any(player => player.Hand.Any(card => card.Kind == CardKind.Nullification)))
            {
                continue;
            }
            noNullification++;

            var duelAction = game.GetHumanLegalActions()
                .Where(action => action.Kind == LegalActionKind.Duel && action.TargetSeat is not null)
                .FirstOrDefault(action => full.Players.Single(player => player.Seat == action.TargetSeat)
                    .Hand.Any(card => card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash));
            if (duelAction is null)
            {
                continue;
            }
            duelFound++;

            var targetSeat = duelAction.TargetSeat!.Value;
            var played = game.Submit(new PlayCardCommand(
                0,
                duelAction.CardId!.Value,
                duelAction.TargetSeats,
                game.Revision,
                game.PendingDecision.PromptId,
                duelAction.PlayedCardKind));
            if (!played.Accepted)
            {
                continue;
            }

            var hostPending = GetHostPendingDecision(game);
            lastPending = $"{hostPending?.Kind.ToString() ?? "none"}/" +
                $"{hostPending?.PlayerSeat.ToString() ?? "none"}/target-{targetSeat}/" +
                $"status-{game.State.Status}/stack-{string.Join(',', game.ResolutionStack.Select(frame => frame.Kind))}";

            var resolutionId = game.Events.Select(item => item.Payload)
                .OfType<CardUseDeclaredEvent>()
                .Last(item => item.CardId == duelAction.CardId).ResolutionId;
            if (hostPending is not { Kind: DecisionKind.RespondSlash } targetPrompt ||
                targetPrompt.PlayerSeat != targetSeat)
            {
                continue;
            }
            responsePromptFound++;

            var slashChoice = targetPrompt.Choices.FirstOrDefault(choice =>
                choice.Parameters.GetValueOrDefault("response") == "slash");
            if (slashChoice?.Cards.Count != 1)
            {
                continue;
            }
            slashChoiceFound++;

            ResolveSyntheticDuelSlash(game, targetSeat, slashChoice);
            if (game.PendingDecision is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 } prompt &&
                prompt.IncomingCard == CardKind.Duel)
            {
                var xuChuHp = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hp;
                return (game, targetSeat, resolutionId, xuChuHp);
            }
        }

        throw new InvalidOperationException(
            $"Could not find a deterministic Luoyi reverse-Duel fixture " +
            $"(offered={offered}, no-null={noNullification}, duel={duelFound}, prompt={responsePromptFound}, slash={slashChoiceFound}, last={lastPending}).");
    }

    private static void ResolveSyntheticDuelSlash(GameEngine game, int responderSeat, PromptChoice choice)
    {
        var slashCardId = choice.Cards.Single();
        var duelField = typeof(GameEngine).GetField(
            "_pendingDuel",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine Duel continuation was not found.");
        var duel = duelField.GetValue(game) ??
            throw new InvalidOperationException("The reverse-Duel fixture lost its continuation.");
        var playersField = typeof(GameEngine).GetField(
            "_players",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine player store was not found.");
        var players = (System.Collections.IList)playersField.GetValue(game)!;
        var responder = players[responderSeat]!;
        var getHand = typeof(GameEngine).GetMethod(
            "GetHand",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine hand accessor was not found.");
        var slash = ((System.Collections.IEnumerable)getHand.Invoke(game, [responder])!)
            .Cast<Card>()
            .Single(card => card.Id == slashCardId);
        var resolutionId = game.ResolutionStack.OfType<CardUseFrame>()
            .Single(frame => frame.CardKind == CardKind.Duel).Id;
        var popResponse = typeof(GameEngine).GetMethod(
            "PopResponseWindow",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine response-window popper was not found.");
        popResponse.Invoke(game, [resolutionId]);
        var setCardUseStep = typeof(GameEngine).GetMethod(
            "SetCardUseStep",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine card-use cursor updater was not found.");
        setCardUseStep.Invoke(game, [resolutionId, ResolutionFrameStep.ResolvingEffect]);
        var clearPending = typeof(GameEngine).GetMethod(
            "ClearPendingDecision",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine pending-decision clearer was not found.");
        typeof(GameEngine).GetMethod("CaptureSelectedResponseConversion", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [choice]);
        clearPending.Invoke(game, null);
        var resolve = typeof(GameEngine).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == "ResolveDuelResponse" && method.GetParameters().Length == 3);
        resolve.Invoke(game, [duel, responder, slash]);
    }

    private static PendingDecision? GetHostPendingDecision(GameEngine game)
    {
        var decisionField = typeof(GameEngine).GetField(
            "_pendingDecision",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine pending-decision store was not found.");
        return (PendingDecision?)decisionField.GetValue(game);
    }


    private static (
        GameEngine Game,
        GameCheckpoint BeforeDamage,
        LegalAction Action,
        int EventCount,
        int TargetSeat,
        int SourceHpBefore,
        int Distance) FindWeiYanKuangguFixture(
            ContentRegistry registry,
            int rulesVersion = GameCheckpoint.CurrentRulesVersion)
    {
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = CreateInteractive(registry, seed, Role.Rebel);
            if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
            {
                game = GameReplay.Restore(
                    game.CreateCheckpoint() with { RulesVersion = rulesVersion },
                    registry);
            }
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Wei Yan fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:wei-yan"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:wei-yan",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Wei Yan.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Wei Yan setup did not advance.");
            var result = advanced.Result;
            for (var step = 0; result.Status != EngineStatus.Completed && step < 1_200; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } prompt)
                {
                    if (game.Events.Any(item => item.Payload is StoneAxeResolvedEvent))
                    {
                        break;
                    }

                    var full = game.CreateSnapshot(0, revealAll: true);
                    var source = full.Players.Single(player => player.Seat == 0);
                    if (source.Hp > 0 && source.Hp < source.MaxHp)
                    {
                        var candidate = game.GetHumanLegalActions()
                            .Where(action => action.Kind == LegalActionKind.Slash &&
                                             action.CardId is not null &&
                                             action.TargetSeat is not null)
                            .Select(action => new
                            {
                                Action = action,
                                Target = full.Players.Single(player => player.Seat == action.TargetSeat),
                                Distance = game.GetCombatDistance(0, action.TargetSeat!.Value)
                            })
                            .Where(item => item.Distance == 1 &&
                                           item.Target.Hand.All(card => card.Kind != CardKind.Dodge) &&
                                           item.Target.Equipment.All(card =>
                                               card.Kind is not (CardKind.BaguaFormation or CardKind.RenwangShield)) &&
                                           item.Target.Skills?.All(skill =>
                                               skill.ContentId is not ("classic:qingguo" or "classic:longdan" or "classic:hujia")) != false)
                            .OrderBy(item => item.Action.CardId)
                            .ThenBy(item => item.Action.TargetSeat)
                            .FirstOrDefault();
                        if (candidate is not null)
                        {
                            var beforeDamage = GameCheckpointJson.Deserialize(
                                GameCheckpointJson.Serialize(game.CreateCheckpoint()));
                            var eventCount = game.Events.Count;
                            var played = SubmitPlayAction(game, candidate.Action);
                            if (!played.Accepted)
                            {
                                continue;
                            }

                            if (rulesVersion >= 38 &&
                                !game.Events.Skip(eventCount).Any(item =>
                                    item.Payload is RecoveryAppliedEvent recovery &&
                                    recovery.SourceSeat == 0 && recovery.TargetSeat == 0))
                            {
                                var resolved = game.Submit(new AdvanceOneStepCommand(game.Revision));
                                if (!resolved.Accepted)
                                {
                                    continue;
                                }
                            }

                            var newEvents = game.Events.Skip(eventCount)
                                .Select(item => item.Payload)
                                .ToArray();
                            if ((rulesVersion >= 38 && newEvents.Any(item =>
                                    item is RecoveryAppliedEvent recovery &&
                                    recovery.SourceSeat == 0 && recovery.TargetSeat == 0)) ||
                                (rulesVersion < 38 && newEvents.Any(item =>
                                    item is DamageAppliedEvent damage &&
                                    damage.SourceSeat == 0 &&
                                    damage.TargetSeat == candidate.Target.Seat)))
                            {
                                return (
                                    game,
                                    beforeDamage,
                                    candidate.Action,
                                    eventCount,
                                    candidate.Target.Seat,
                                    source.Hp,
                                    candidate.Distance);
                            }
                        }
                    }

                    result = DeclineOrAdvance(game, result);
                    continue;
                }

                result = DeclineOrAdvance(game, result);
            }
        }

        throw new InvalidOperationException("Could not find a deterministic classic Wei Yan Kuanggu fixture.");
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
                "classic:zhang-fei",
                GameCheckpoint.CurrentRulesVersion);
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
                "classic:zhao-yun",
                GameCheckpoint.CurrentRulesVersion);
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
                "classic:guan-yu",
                GameCheckpoint.CurrentRulesVersion);
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

    private static (
        GameEngine Game,
        GameCheckpoint BeforeAction,
        LegalAction Action,
        int TargetSeat) FindLuBuWushuangSlashFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:lu-bu",
                GameCheckpoint.CurrentRulesVersion);
            if (game is null)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            // A plain two-Dodge respondent is identified by observable properties, not by a
            // general whitelist: the pool keeps growing and the whitelist drifted with it.
            var candidates = game.GetHumanLegalActions()
                .Where(action => action.Kind == LegalActionKind.Slash && action.TargetSeat is not null)
                .Select(action => (Action: action, Target: full.Players.Single(player => player.Seat == action.TargetSeat)))
                .Where(item => item.Target.Hand.Count(card => card.Kind == CardKind.Dodge) >= 2 &&
                    item.Target.Equipment.All(card =>
                        card.Kind is not (CardKind.BaguaFormation or CardKind.RenwangShield)))
                .OrderBy(item => item.Action.CardId)
                .ThenBy(item => item.Action.TargetSeat)
                .ToArray();
            foreach (var candidate in candidates)
            {
                // Deep probe: replay the exact assertion body on a checkpoint copy so that
                // Dodge conversions, Slash nullification or redirection disqualify the seat.
                if (!ProbeWushuangSlash(game, candidate.Action, candidate.Target.Seat, registry))
                {
                    continue;
                }

                return (
                    game,
                    GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
                    candidate.Action,
                    candidate.Target.Seat);
            }
        }

        throw new InvalidOperationException("Could not find a deterministic classic Lu Bu two-Dodge fixture.");
    }

    private static bool ProbeWushuangSlash(
        GameEngine game,
        LegalAction action,
        int targetSeat,
        ContentRegistry registry)
    {
        var probe = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        var before = probe.Events.Count;
        if (!SubmitPlayAction(probe, action).Accepted) return false;
        if (!DriveProbeUntilDodge(probe, before, 1)) return false;
        if (probe.CreateSnapshot(targetSeat).PendingDecision is not
            {
                Kind: DecisionKind.RespondDodge,
                PlayerSeat: var responderSeat,
                RequiredCardKind: CardKind.Dodge
            } prompt ||
            responderSeat != targetSeat ||
            !prompt.Prompt.Contains("第 2 张", StringComparison.Ordinal))
        {
            return false;
        }

        if (!DriveProbeUntilDodge(probe, before, 2)) return false;
        var events = probe.Events.Skip(before).Select(item => item.Payload).ToArray();
        var progress = events.OfType<RequiredResponseProgressEvent>()
            .Where(item => item.RequiredCardKind == CardKind.Dodge)
            .ToArray();
        return progress.Select(item => item.ResponseCount).SequenceEqual([1, 2]) &&
               progress.All(item => item.SkillOwnerSeat == 0 &&
                   item.ResponderSeat == targetSeat &&
                   item.RequiredResponseCount == 2) &&
               events.OfType<CardRespondedEvent>().Count(item => item.ResponderSeat == targetSeat) == 2 &&
               events.OfType<DamageAppliedEvent>().All(item => item.TargetSeat != targetSeat);
    }

    private static bool DriveProbeUntilDodge(GameEngine probe, int eventCount, int dodgeCount)
    {
        for (var step = 0; step < 64; step++)
        {
            if (probe.Events.Skip(eventCount).Select(item => item.Payload)
                    .OfType<RequiredResponseProgressEvent>()
                    .Count(item => item.RequiredCardKind == CardKind.Dodge) >= dodgeCount)
            {
                return true;
            }

            if (probe.PendingDecision is { PlayerSeat: 0 } ||
                probe.State.Status == EngineStatus.Completed ||
                !probe.Submit(new AdvanceOneStepCommand(probe.Revision)).Accepted)
            {
                return false;
            }
        }

        return false;
    }

    private static (
        GameEngine Game,
        GameCheckpoint BeforeAction,
        LegalAction Action,
        int TargetSeat) FindLuBuWushuangDuelFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:lu-bu",
                GameCheckpoint.CurrentRulesVersion);
            if (game is null)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            if (full.Players.SelectMany(player => player.Hand)
                .Any(card => card.Kind == CardKind.Nullification))
            {
                continue;
            }

            var candidate = game.GetHumanLegalActions()
                .Where(action => action.Kind == LegalActionKind.Duel && action.TargetSeat is not null)
                .Select(action => new
                {
                    Action = action,
                    Target = full.Players.Single(player => player.Seat == action.TargetSeat)
                })
                .Where(item => item.Target.Hand.Count(card =>
                                   card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) == 1 &&
                               item.Target.Skills?.All(skill =>
                                   skill.ContentId is not ("classic:wusheng" or "classic:longdan" or
                                       "classic:jijiang" or "classic:longhun")) != false)
                .OrderBy(item => item.Action.CardId)
                .ThenBy(item => item.Action.TargetSeat)
                .FirstOrDefault();
            if (candidate is null)
            {
                continue;
            }

            // The hand filters cannot see skills that convert another card into
            // a Slash response mid-duel, so probe the actual Wushuang sequence
            // and only accept seeds whose target pays its one Slash and fails
            // the second required response.
            var probeEventCount = game.Events.Count;
            SetPlayerHp(game, candidate.Target.Seat, hp: 1);
            var probeUse = SubmitPlayAction(game, candidate.Action);
            if (!probeUse.Accepted || !DriveWushuangDuelProbe(game, probeEventCount, candidate.Target.Seat))
            {
                continue;
            }

            var pristine = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:lu-bu",
                GameCheckpoint.CurrentRulesVersion) ??
                throw new InvalidOperationException("The probed Lu Bu fixture lost determinism.");
            var pristineAction = pristine.GetHumanLegalActions()
                .Where(action => action.Kind == LegalActionKind.Duel &&
                                action.TargetSeat == candidate.Target.Seat)
                .OrderBy(action => action.CardId)
                .ThenBy(action => action.TargetSeat)
                .FirstOrDefault();
            if (pristineAction is null)
            {
                continue;
            }

            return (
                pristine,
                GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(pristine.CreateCheckpoint())),
                pristineAction,
                candidate.Target.Seat);
        }

        throw new InvalidOperationException("Could not find a deterministic classic Lu Bu one-Slash Duel fixture.");
    }

    private static bool DriveWushuangDuelProbe(GameEngine game, int eventCount, int targetSeat)
    {
        for (var step = 0; step < 32; step++)
        {
            var events = game.Events.Skip(eventCount).Select(item => item.Payload).ToArray();
            var paid = events.OfType<DuelResponseEvent>().Count(response =>
                response.ResponderSeat == targetSeat && response.UsedSlash) == 1;
            var failed = events.OfType<DuelResponseEvent>().Any(response =>
                response.ResponderSeat == targetSeat && !response.UsedSlash);
            var progressed = events.OfType<RequiredResponseProgressEvent>().Any(progress =>
                progress.SkillOwnerSeat == 0 &&
                progress.ResponderSeat == targetSeat &&
                progress.IncomingCard == CardKind.Duel &&
                progress.RequiredCardKind == CardKind.Slash &&
                progress.ResponseCount == 1 &&
                progress.RequiredResponseCount == 2);
            var damaged = events.OfType<DamageAppliedEvent>().Any(damage =>
                damage.TargetSeat == targetSeat);
            if (paid && failed && progressed && damaged)
            {
                return true;
            }

            if (game.PendingDecision is { PlayerSeat: 0 })
            {
                return false;
            }

            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            if (!advanced.Accepted)
            {
                return false;
            }
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

    private static (
        GameEngine Game,
        PendingDecision? Prompt,
        int TargetSeat,
        int SourceHp,
        int TargetHandCount,
        int AttackRange) FindHuangZhongLiegongFixture(
        ContentRegistry registry,
        int rulesVersion,
        LiegongFixtureKind kind)
    {
        var humanRole = kind == LiegongFixtureKind.Ineligible ? Role.Lord : Role.Rebel;
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = CreateInteractive(registry, seed, humanRole);
            if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
            {
                game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
            }

            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Huang Zhong fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:huang-zhong"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:huang-zhong",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Huang Zhong.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Huang Zhong setup did not advance.");
            var result = advanced.Result;
            for (var step = 0; result.Status != EngineStatus.Completed && step < 1_200; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
                {
                    var full = game.CreateSnapshot(0, revealAll: true);
                    var source = full.Players.Single(player => player.Seat == 0);
                    var attackRange = game.GetAttackRange(0);
                    var candidate = game.GetHumanLegalActions()
                        .Where(action => action.Kind == LegalActionKind.Slash &&
                                         action.CardId is not null &&
                                         action.TargetSeat is not null)
                        .Select(action =>
                        {
                            var target = full.Players.Single(player => player.Seat == action.TargetSeat);
                            var targetHandCount = target.Hand.Count;
                            var eligibleByHp = targetHandCount >= source.Hp;
                            var eligibleByRange = targetHandCount <= attackRange;
                            return new
                            {
                                Action = action,
                                Target = target,
                                TargetHandCount = targetHandCount,
                                EligibleByHp = eligibleByHp,
                                EligibleByRange = eligibleByRange
                            };
                        })
                        .Where(item => item.Target.Hand.Any(card => card.Kind == CardKind.Dodge))
                        .Where(item => item.Target.GeneralId != "classic:da-qiao")
                        .Where(item => item.Target.Equipment.All(card =>
                            !EquipmentCatalog.IsEquipment(card.Kind) ||
                            EquipmentCatalog.Get(card.Kind).Slot != EquipmentSlot.Armor))
                        .Where(item => kind switch
                        {
                            LiegongFixtureKind.EligibleByHp =>
                                item.EligibleByHp && !item.EligibleByRange,
                            LiegongFixtureKind.EligibleByRange =>
                                item.EligibleByRange && !item.EligibleByHp,
                            LiegongFixtureKind.Ineligible =>
                                !item.EligibleByHp && !item.EligibleByRange,
                            _ => false
                        })
                        .OrderBy(item => item.Action.CardId)
                        .ThenBy(item => item.Action.TargetSeat)
                        .FirstOrDefault();
                    if (candidate is not null)
                    {
                        var slashEventStart = game.Events.Count;
                        var played = game.Submit(new PlayCardCommand(
                            0,
                            candidate.Action.CardId!.Value,
                            candidate.Action.TargetSeats,
                            game.Revision,
                            game.PendingDecision.PromptId,
                            candidate.Action.PlayedCardKind,
                            candidate.Action.TargetCardId)
                        { ConversionSource = candidate.Action.ConversionSource });
                        Require(played.Accepted, played.Error?.Message ?? "Huang Zhong could not use Slash.");
                        if (kind == LiegongFixtureKind.Ineligible)
                        {
                            // The shared card-trigger window may pause after acceptance, before Dodge is requested.
                            for (var resume = 0; resume < 64 &&
                                    !game.Events.Select(item => item.Payload).OfType<ResponseRequestedEvent>()
                                        .Any(item => item.TargetSeat == candidate.Target.Seat &&
                                                     item.RequiredCardKind == CardKind.Dodge) &&
                                    game.ResolutionStack.Count != 0 && game.PendingDecision is null; resume++)
                            {
                                var next = game.Submit(new AdvanceOneStepCommand(game.Revision));
                                Require(next.Accepted, next.Error?.Message ?? "The ineligible Slash stalled before Dodge.");
                            }
                            // Other target skills can nullify this Slash before its ordinary Dodge window.
                            // Such a card never tests Liegong's ineligible branch.
                            if (game.Events.Skip(slashEventStart).Any(item =>
                                    item.Payload is ProgramCardEffectNullifiedEvent))
                                break;
                        }
                        var prompt = game.PendingDecision;
                        if (prompt is null &&
                            rulesVersion >= 37 &&
                            kind != LiegongFixtureKind.Ineligible)
                        {
                            break;
                        }
                        return (
                            game,
                            prompt,
                            candidate.Target.Seat,
                            source.Hp,
                            candidate.TargetHandCount,
                            attackRange);
                    }
                }

                result = DeclineOrAdvance(game, result);
            }
        }

        throw new InvalidOperationException($"Could not find a deterministic Huang Zhong {kind} fixture.");
    }

    private static (
        GameEngine Game,
        PendingDecision Prompt,
        int TargetSeat,
        int Seed) FindMaChaoTieqiFixture(
        ContentRegistry registry,
        bool requireRedJudgment)
    {
        var attempted = 0;
        var tieqiPrompts = 0;
        var resolvedJudgments = 0;
        var offeredWithoutFixture = 0;
        string? lastFailure = null;
        string? lastOfferedFailure = null;
        for (var seed = 1; seed <= 8_192; seed++)
        {
            GameEngine game;
            int targetSeat;
            try
            {
                (game, targetSeat) = FindMaChaoSlashFixture(
                    registry,
                    seed,
                    GameCheckpoint.CurrentRulesVersion);
            }
            catch (InvalidOperationException error)
            {
                lastFailure = error.Message;
                if (error.Message != "Ma Chao was not offered.")
                {
                    offeredWithoutFixture++;
                    lastOfferedFailure = error.Message;
                }
                continue;
            }
            attempted++;

            if (game.PendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt)
            {
                continue;
            }
            tieqiPrompts++;

            var probe = GameReplay.Restore(game.CreateCheckpoint(), registry);
            var probePrompt = probe.PendingDecision!;
            var used = probe.Submit(new AnswerPromptCommand(
                0,
                probePrompt.PromptId,
                probePrompt.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "activate").Id,
                probe.Revision));
            if (!used.Accepted || probe.PendingDecision?.Kind == DecisionKind.ProgramJudgmentReplacement)
            {
                continue;
            }

            var judgment = probe.Events.Select(item => item.Payload)
                .OfType<JudgmentResolvedEvent>()
                .LastOrDefault(item => item.Reason == "skill.slash-response-judgment");
            if (judgment is not null) resolvedJudgments++;
            if (judgment is not null &&
                (judgment.Suit is Suit.Heart or Suit.Diamond) == requireRedJudgment)
            {
                return (game, prompt, targetSeat, seed);
            }
        }

        throw new InvalidOperationException(
            $"Could not find a deterministic Ma Chao Tieqi fixture for a " +
            $"{(requireRedJudgment ? "red" : "black")} judgment " +
            $"(attempted={attempted}, offered-failures={offeredWithoutFixture}, prompts={tieqiPrompts}, " +
            $"judgments={resolvedJudgments}, last={lastFailure}, offered-last={lastOfferedFailure}).");
    }

    private static (GameEngine Game, int TargetSeat) FindMaChaoSlashFixture(
        ContentRegistry registry,
        int seed,
        int rulesVersion)
    {
        var game = CreateInteractive(registry, seed);
        if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
        {
            game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
        }

        var started = game.Submit(new StartGameCommand());
        Require(started.Accepted, started.Error?.Message ?? "Ma Chao fixture failed to start.");
        if (game.PendingDecision?.Choices.Any(choice =>
                choice.ContentIds.SequenceEqual(["classic:ma-chao"])) != true)
        {
            throw new InvalidOperationException("Ma Chao was not offered.");
        }

        var selected = game.Submit(new SelectGeneralCommand(
            0,
            "classic:ma-chao",
            game.Revision,
            game.PendingDecision.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Ma Chao.");
        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted, advanced.Error?.Message ?? "Ma Chao did not reach the play phase.");
        if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
        {
            throw new InvalidOperationException("Ma Chao did not stop at a PlayCard decision.");
        }

        var full = game.CreateSnapshot(0, revealAll: true);
        Require(game.GetCombatDistance(0, 2) == 1,
            "Classic Ma Chao must reduce the public distance-two seat to distance one through Mashu.");
        var action = game.GetHumanLegalActions()
            .Where(candidate => candidate.Kind == LegalActionKind.Slash &&
                                candidate.CardId is not null &&
                                candidate.TargetSeat is { } targetSeat &&
                                full.Players.Single(player => player.Seat == targetSeat).Hand.Any(card =>
                                    card.Kind == CardKind.Dodge))
            .OrderBy(candidate => candidate.CardId)
            .ThenBy(candidate => candidate.TargetSeat)
            .FirstOrDefault() ??
            throw new InvalidOperationException("Ma Chao has no Slash target holding Dodge.");
        var target = action.TargetSeat!.Value;
        var played = game.Submit(new PlayCardCommand(
            0,
            action.CardId!.Value,
            action.TargetSeats,
            game.Revision,
            game.PendingDecision.PromptId,
            action.PlayedCardKind,
            action.TargetCardId));
        Require(played.Accepted, played.Error?.Message ?? "Ma Chao could not use Slash.");
        return (game, target);
    }

    private static (GameEngine Game, LegalAction Action) FindHuangYueyingOrdinaryTrickFixture(
        ContentRegistry registry,
        int rulesVersion,
        bool requireNullification)
    {
        for (var seed = 1; seed <= 8_192; seed++)
        {
            var game = CreateInteractive(registry, seed);
            if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
            {
                game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
            }

            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Huang Yueying fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:huang-yueying"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:huang-yueying",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Huang Yueying.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Huang Yueying did not reach the play phase.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var hand = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hand;
            if (requireNullification && hand.All(card => card.Kind != CardKind.Nullification))
            {
                continue;
            }

            var action = game.GetHumanLegalActions()
                .Where(candidate => candidate.CardId is not null && candidate.Kind is
                    LegalActionKind.DrawTwo or
                    LegalActionKind.BarbarianAssault or
                    LegalActionKind.ArrowBarrage or
                    LegalActionKind.PeachGarden or
                    LegalActionKind.FiveGrains or
                    LegalActionKind.IronChain or
                    LegalActionKind.Dismantlement or
                    LegalActionKind.Snatch or
                    LegalActionKind.FireAttack or
                    LegalActionKind.Duel)
                .OrderBy(candidate => candidate.Kind == LegalActionKind.DrawTwo ? 0 : 1)
                .ThenBy(candidate => candidate.CardId)
                .FirstOrDefault();
            if (action is not null)
            {
                return (game, action);
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Huang Yueying ordinary-trick fixture.");
    }


    private static (GameEngine Game, LegalAction Action) FindHuangYueyingOrdinaryTrickFixtureForSeed(
        ContentRegistry registry,
        int seed)
    {
        var game = CreateInteractive(registry, seed);
        var started = game.Submit(new StartGameCommand());
        Require(started.Accepted, started.Error?.Message ?? "Huang Yueying Nullification fixture failed to start.");
        if (game.PendingDecision?.Choices.Any(choice =>
                choice.ContentIds.SequenceEqual(["classic:huang-yueying"])) != true)
        {
            throw new InvalidOperationException("Huang Yueying was not offered.");
        }

        var selected = game.Submit(new SelectGeneralCommand(
            0,
            "classic:huang-yueying",
            game.Revision,
            game.PendingDecision.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Huang Yueying.");
        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted && game.PendingDecision?.Kind == DecisionKind.PlayCard,
            advanced.Error?.Message ?? "Huang Yueying did not reach play for Nullification.");
        var hand = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hand;
        if (hand.All(card => card.Kind != CardKind.Nullification))
        {
            throw new InvalidOperationException("Huang Yueying has no Nullification.");
        }

        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            candidate.CardId is not null && candidate.Kind is
                LegalActionKind.DrawTwo or
                LegalActionKind.BarbarianAssault or
                LegalActionKind.ArrowBarrage or
                LegalActionKind.PeachGarden or
                LegalActionKind.FiveGrains or
                LegalActionKind.IronChain or
                LegalActionKind.Dismantlement or
                LegalActionKind.Snatch or
                LegalActionKind.FireAttack or
                LegalActionKind.Duel) ??
            throw new InvalidOperationException("Huang Yueying has no ordinary trick action.");
        return (game, action);
    }


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

    private static GameEngine SelectGeneral(ContentRegistry registry, string generalId, int rulesVersion,
        Func<GameEngine, bool>? fixtureFilter = null)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateInteractive(registry, seed);
            if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
                game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
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

    private static (
        GameEngine Game,
        int ProviderSeat,
        int PeachCardId,
        int SelfPeachCardId,
        int NonWuProviderSeat,
        int NonWuPeachCardId) FindJiuyuanFixture(
        ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Jiuyuan fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:sun-quan"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:sun-quan",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Sun Quan.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ??
                "The Jiuyuan fixture could not finish AI general selection.");
            var players = game.CreateSnapshot(0, revealAll: true).Players;
            var selfPeach = players.Single(player => player.Seat == 0).Hand
                .FirstOrDefault(card => card.Kind == CardKind.Peach);
            if (selfPeach is null)
            {
                continue;
            }

            // Providers are classified by their effective faction (the private projection does
            // not publish it): a god general that chose Wu counts as Wu for Jiuyuan even though
            // its printed registry faction is "god".
            // Every candidate Peach must survive the synthetic injection probe first, because a
            // conversion or card identity claiming that physical Peach cannot be used plainly.
            var candidates = players
                .Where(player => player.Seat != 0)
                .Select(player => new
                {
                    player.Seat,
                    Faction = GetEffectiveFaction(game, player.Seat),
                    Peach = player.Hand.FirstOrDefault(card => card.Kind == CardKind.Peach)
                })
                .Where(candidate => candidate.Peach is not null &&
                                    CanInjectSyntheticPeach(game, candidate.Seat, candidate.Peach!.Id, registry))
                .ToArray();
            var provider = candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Faction, "wu", StringComparison.Ordinal));
            var nonWuProvider = candidates.FirstOrDefault(candidate =>
                !string.Equals(candidate.Faction, "wu", StringComparison.Ordinal));
            if (provider is not null && nonWuProvider is not null &&
                CanInjectSyntheticPeach(game, 0, selfPeach.Id, registry))
            {
                return (
                    game,
                    provider.Seat,
                    provider.Peach!.Id,
                    selfPeach.Id,
                    nonWuProvider.Seat,
                    nonWuProvider.Peach!.Id);
            }
        }

        throw new InvalidOperationException("No deterministic Jiuyuan fixture exposed a Wu provider with Peach.");
    }

    private static string? GetEffectiveFaction(GameEngine game, int seat)
    {
        var players = (IReadOnlyList<CharacterState>)(typeof(GameEngine)
            .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(game) ?? throw new InvalidOperationException("The engine player store was not found."));
        return (string?)typeof(GameEngine)
            .GetMethod("GetEffectiveFactionId", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [players[seat]]);
    }

    private static bool CanInjectSyntheticPeach(
        GameEngine game,
        int providerSeat,
        int peachCardId,
        ContentRegistry registry)
    {
        try
        {
            var probe = GameReplay.Restore(
                GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
                registry);
            ApplySyntheticDyingPeach(probe, providerSeat, peachCardId);
            // A usable synthetic Peach must recover the dying Lord in the same synchronous step;
            // skills that claim or interrupt the physical Peach leave the target at zero HP and
            // are rejected here instead of by a hardcoded general whitelist.
            return probe.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hp is 1 or 2;
        }
        catch (Exception exception) when (exception is InvalidOperationException or TargetInvocationException)
        {
            return false;
        }
    }

    private static void ApplySyntheticDyingPeach(
        GameEngine game,
        int providerSeat,
        int peachCardId)
    {
        var playersField = typeof(GameEngine).GetField(
            "_players",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine player store was not found.");
        var players = (System.Collections.IList)playersField.GetValue(game)!;
        var target = players[0]!;
        var provider = players[providerSeat]!;
        target.GetType().GetProperty("Hp")!.SetValue(target, 0);

        var getHand = typeof(GameEngine).GetMethod(
            "GetHand",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine hand accessor was not found.");
        var providerHand = ((System.Collections.IEnumerable)getHand.Invoke(game, [provider])!)
            .Cast<Card>();
        var peach = providerHand.Single(card => card.Id == peachCardId);
        var resolvePeach = typeof(GameEngine).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == "ResolvePeach" && method.GetParameters().Length == 6);
        resolvePeach.Invoke(game, [provider, target, peach, true, null, null]);

        var commitEvents = typeof(GameEngine).GetMethod(
            "CommitPendingEvents",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine event commit method was not found.");
        commitEvents.Invoke(game, null);
        // The synthetic call bypasses the command pipeline, so deferred movement
        // continuations need one pipeline step to publish and resume.
        for (var step = 0; step < 32 && game.ResolutionStack.Count > 0 &&
             game.PendingDecision is null; step++)
        {
            var advance = game.Submit(new AdvanceOneStepCommand(game.Revision));
            if (!advance.Accepted) break;
        }
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
