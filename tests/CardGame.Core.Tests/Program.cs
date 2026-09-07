using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

var tests = new (string Name, Action Body)[]
{
    ("standard setup has the 1/2/4/1 identity distribution", IdentityDistribution),
    ("viewer snapshot hides private roles and hands", SnapshotHidesSecrets),
    ("viewer snapshot hides seed and other players' decisions", SnapshotHidesEngineSecrets),
    ("viewer cannot mutate the engine pending decision", SnapshotDecisionIsDefensive),
    ("commands publish a revision and reject stale input atomically", CommandRevisionBoundary),
    ("play commands use one exact published card and target choice", CommandPlayUsesExactChoice),
    ("prompt answers validate prompt, choice, actor and revision", CommandPromptAnswerBoundary),
    ("command reentry is a typed rejection", CommandReentryIsTyped),
    ("winner rules cover all three camps", WinnerRules),
    ("implemented card content is registered and described", CardCatalogDefinitions),
    ("standard deck content is deterministic and balanced", StandardDeckContent),
    ("standard package builds an immutable isolated registry", StandardContentRegistryBuilds),
    ("content registry rejects duplicate ids and bad references", ContentRegistryValidation),
    ("engine can consume the standard registry through a compatibility projection", EngineConsumesStandardRegistry),
    ("interactive setup exposes private deterministic general choices", InteractiveSetupPipeline),
    ("interactive AI setup consumes the shared pool and terminates", InteractiveAiSetup),
    ("five-player identity mode reuses the shared engine", FivePlayerIdentityMode),
    ("five-player AI matches terminate for fixed seeds", FivePlayerAiSmoke),
    ("standard generals reference registered skills", GeneralContent),
    ("AI uses the card content policy values", AiCardContentPolicy),
    ("passive skill hooks stay small and deterministic", PassiveSkills),
    ("damage trigger candidates use a stable ordering", DamageTriggerOrdering),
    ("damage trigger windows pause with a serializable cursor", DamageTriggerWindowFlow),
    ("AI suspicion changes only from public actions", AiPublicEvidence),
    ("same seed creates the same initial state", FixedSeed),
    ("accepted command journals serialize and replay deterministically", CommandJournalReplay),
    ("initial deal registers every physical card in one zone", InitialDealCardZones),
    ("invalid single and batch moves are atomic", InvalidCardMovesAreAtomic),
    ("Slash and Dodge pass through the Processing zone", SlashAndDodgeProcessing),
    ("card inventory is conserved at every public boundary", CardInventoryConservation),
    ("observers run only after the public operation commits", ObserversRunPostCommit),
    ("typed host events are committed, ordered and deterministic", TypedEventStream),
    ("slash resolution exposes a serializable frame stack", ResolutionFrameStack),
    ("duel alternates Slash responses through a typed window", DuelResponseFlow),
    ("DrawTwo resolves an immediate effect without a target", DrawTwoFlow),
    ("BarbarianAssault resolves each target through private Slash windows", BarbarianAssaultFlow),
    ("ArrowBarrage reuses group resolution through private Dodge windows", ArrowBarrageFlow),
    ("PeachGarden resolves a paused multi-target recovery", PeachGardenFlow),
    ("FiveGrains reveals public cards with private draft prompts", FiveGrainsFlow),
    ("Dismantlement discards a hidden target card deterministically", DismantlementFlow),
    ("Snatch transfers a hidden target card across a distance-one edge", SnatchFlow),
    ("equipment replaces slots and changes public distance rules", EquipmentFlow),
    ("FireAttack reveals privately then resolves typed fire damage", FireAttackFlow),
    ("FireAttack can skip the same-suit discard without damage", FireAttackSkipFlow),
    ("FireSlash and ThunderSlash preserve typed damage nature", AttributeSlashFlow),
    ("Alcohol arms a one-shot Slash damage boost", AlcoholFlow),
    ("Feedback claims a surviving damage card through a typed event", FeedbackFlow),
    ("Feedback exposes a private human trigger choice", FeedbackHumanChoiceFlow),
    ("Feedback can be skipped without claiming the damage card", FeedbackSkipChoiceFlow),
    ("Yiji draws privately and gives one card across seats", YijiGiftFlow),
    ("Yiji accepts a private human gift choice", YijiHumanChoiceFlow),
    ("Jieming draws to a legal target's hand limit", JiemingFlow),
    ("Yuanhu can trigger from another seat and recover the damaged player", YuanhuCrossSeatFlow),
    ("Wusheng converts one red card into a typed Slash", WushengFlow),
    ("Longdan converts Dodge into a typed Slash", LongdanFlow),
    ("Longdan converts Slash into Dodge in a response window", LongdanResponseFlow),
   ("dying response can use Alcohol for self rescue", DyingAlcoholRescueFlow),
    ("dying response can pause and recover with a private Peach", DyingResponseFlow),
    ("throwing observers are isolated after commit", ObserverFailuresAreIsolated),
    ("observer failures do not change deterministic outcomes", ObserverFailuresDoNotChangeOutcome),
    ("uncaught observer reentry cannot interrupt the engine", UncaughtObserverReentryIsIsolated),
    ("human API reaches play and accepts a legal card", HumanPlayApi),
    ("human can answer an incoming Slash with Dodge", HumanDodgeApi),
    ("declining lethal Dodge leaves a completed game completed", LethalHumanResponseKeepsCompletedStatus),
    ("AdvanceOneStep exposes one AI decision at a time", AdvanceOneStepApi),
    ("AI ending play publishes its committed Discard state", AiEndPlayPublishesState),
    ("synchronous observers cannot advance the engine reentrantly", ReentrantAdvanceIsRejected),
    ("an unknown phase fails fast", UnknownPhaseFailsFast),
    ("AI-only match terminates and records explainable thoughts", AiMatchSmoke),
    ("long AI runs finish without leaving an active resolution", StepGuardFinishesResponse),
    ("snapshot is JSON serializable", SnapshotSerialization)
};

var failed = 0;
foreach (var (name, body) in tests)
{
    try
    {
        body();
        Console.WriteLine($"[PASS] {name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.WriteLine($"[FAIL] {name}");
        Console.WriteLine($"       {exception.GetType().Name}: {exception.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"{tests.Length - failed}/{tests.Length} checks passed.");
return failed == 0 ? 0 : 1;

static void IdentityDistribution()
{
    var game = GameEngine.CreateStandard(new GameOptions { Seed = 7 });
    var roles = game.CreateSnapshot(0, revealAll: true).Players
        .GroupBy(player => player.Role!.Value)
        .ToDictionary(group => group.Key, group => group.Count());

    Equal(1, roles[Role.Lord]);
    Equal(2, roles[Role.Loyalist]);
    Equal(4, roles[Role.Rebel]);
    Equal(1, roles[Role.Renegade]);
}

static void SnapshotHidesSecrets()
{
    var game = GameEngine.CreateStandard(new GameOptions { Seed = 13, HumanSeat = 0, HumanRole = Role.Lord });
    var snapshot = game.State;
    var self = snapshot.Players.Single(player => player.Seat == 0);
    var hiddenOpponent = snapshot.Players.First(player => player.Seat != 0 && player.Role is null);

    Equal(Role.Lord, self.Role);
    Equal(4, self.Hand.Count);
    Equal(0, hiddenOpponent.Hand.Count);
    Equal(4, hiddenOpponent.HandCount);
}

static void SnapshotHidesEngineSecrets()
{
    var game = GameEngine.CreateStandard(new GameOptions { Seed = 413, HumanSeat = 0, HumanRole = Role.Lord });
    game.Start();

    Equal(413, game.Seed);
    Equal<int?>(null, game.State.Seed);
    Equal<int?>(413, game.CreateSnapshot(0, revealAll: true).Seed);
    NotNull(game.State.PendingDecision);
    Equal<PendingDecision?>(null, game.CreateSnapshot(1).PendingDecision);
    Equal<PendingDecision?>(null, game.CreateSnapshot(1, revealAll: true).PendingDecision);
    True(game.Log.All(entry => !entry.Message.Contains("413", StringComparison.Ordinal)));
}

static void SnapshotDecisionIsDefensive()
{
    GameEngine? selectedGame = null;
    for (var seed = 1; seed <= 64 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord
        });
        game.Start();
        if (game.PendingDecision is { ValidCardIds.Count: > 0, ValidTargetSeats.Count: > 0 })
        {
            selectedGame = game;
        }
    }

    NotNull(selectedGame);
    var exposed = selectedGame!.State.PendingDecision!;
    var originalCard = exposed.ValidCardIds[0];
    var originalTarget = exposed.ValidTargetSeats[0];
    True(exposed.ValidCardIds is not int[]);
    True(exposed.ValidTargetSeats is not int[]);

    var copiedCards = exposed.ValidCardIds.ToArray();
    var copiedTargets = exposed.ValidTargetSeats.ToArray();
    copiedCards[0] = int.MaxValue;
    copiedTargets[0] = int.MaxValue;

    Equal(originalCard, selectedGame.PendingDecision!.ValidCardIds[0]);
    Equal(originalTarget, selectedGame.PendingDecision!.ValidTargetSeats[0]);
}

static void CommandRevisionBoundary()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        Seed = 901,
        HumanSeat = 0,
        HumanRole = Role.Lord
    });

    Equal(0L, game.Revision);
    Equal(0L, game.State.Revision);

    var started = game.Submit(new StartGameCommand(ExpectedRevision: 0));
    True(started.Accepted);
    Equal<CommandError?>(null, started.Error);
    Equal(1L, started.Revision);
    Equal(started.Revision, started.State.Revision);
    NotNull(started.PendingDecision);
    True(started.PendingDecision!.PromptId.IsValid);
    Equal(started.Revision, started.PendingDecision.Revision);
    True(started.PendingDecision.Choices.Count > 0);
    Equal(
        started.PendingDecision.Choices.Count,
        started.PendingDecision.Choices.Select(choice => choice.Id).Distinct().Count());

    var stateBeforeStale = SnapshotJson.Serialize(game.State);
    var logCountBeforeStale = game.Log.Count;
    var movementCountBeforeStale = game.CardMovements.Count;
    var stale = game.Submit(new EndPlayPhaseCommand(
        ActorSeat: 0,
        ExpectedRevision: 0,
        PromptId: started.PendingDecision.PromptId));

    False(stale.Accepted);
    Equal(CommandErrorCode.StaleRevision, stale.Error!.Code);
    Equal(1L, game.Revision);
    Equal(stateBeforeStale, SnapshotJson.Serialize(game.State));
    Equal(logCountBeforeStale, game.Log.Count);
    Equal(movementCountBeforeStale, game.CardMovements.Count);
}

static void CommandPlayUsesExactChoice()
{
    GameEngine? selectedGame = null;
    PromptChoice? selectedChoice = null;
    for (var seed = 1; seed <= 64 && selectedChoice is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord
        });
        var started = game.Submit(new StartGameCommand());
        var choice = started.PendingDecision?.Choices.FirstOrDefault(candidate =>
            candidate.Cards.Count == 1 && candidate.Targets.Count == 1);
        if (started.Accepted && choice is not null)
        {
            selectedGame = game;
            selectedChoice = choice;
        }
    }

    NotNull(selectedGame);
    NotNull(selectedChoice);
    var pending = selectedGame!.PendingDecision!;
    var before = SnapshotJson.Serialize(selectedGame.State);
    var revisionBefore = selectedGame.Revision;
    var card = selectedChoice!.Cards.Single();
    var target = selectedChoice.Targets.Single();

    var malformed = selectedGame.Submit(new PlayCardCommand(
        ActorSeat: 0,
        CardId: card,
        TargetSeats: [int.MaxValue],
        ExpectedRevision: revisionBefore,
        PromptId: pending.PromptId));
    False(malformed.Accepted);
    Equal(CommandErrorCode.InvalidTarget, malformed.Error!.Code);
    Equal(revisionBefore, selectedGame.Revision);
    Equal(before, SnapshotJson.Serialize(selectedGame.State));

    var accepted = selectedGame.Submit(new PlayCardCommand(
        ActorSeat: 0,
        CardId: card,
        TargetSeats: [target],
        ExpectedRevision: revisionBefore,
        PromptId: pending.PromptId));
    True(accepted.Accepted);
    Equal(revisionBefore + 1, selectedGame.Revision);
    True(selectedGame.Log.Any(entry => entry.Type is "CardUsed" or "Recovered"));
}

static void CommandPromptAnswerBoundary()
{
    GameEngine? selectedGame = null;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 150
        });
        var result = game.Submit(new StartGameCommand());
        var steps = 0;
        while (result.Accepted && result.State.Status != EngineStatus.Completed && steps++ < 500)
        {
            if (result.State.Status == EngineStatus.AwaitingHumanResponse)
            {
                if (result.PendingDecision?.Kind == DecisionKind.RespondDodge)
                {
                    selectedGame = game;
                    break;
                }

                var responsePrompt = result.PendingDecision!;
                var declineResponse = responsePrompt.Choices.Last();
                result = game.Submit(new AnswerPromptCommand(
                    0,
                    responsePrompt.PromptId,
                    declineResponse.Id,
                    game.Revision));
                continue;
            }

            if (result.State.Status == EngineStatus.AwaitingHumanDying)
            {
                var dyingPrompt = result.PendingDecision!;
                var letDieChoice = dyingPrompt.Choices.Single(choice =>
                    choice.Parameters["response"] == "let-die");
                result = game.Submit(new AnswerPromptCommand(
                    0,
                    dyingPrompt.PromptId,
                    letDieChoice.Id,
                    game.Revision));
                continue;
            }

            if (result.State.Status == EngineStatus.AwaitingHumanCardSelection)
            {
                var harvestPrompt = result.PendingDecision!;
                var harvestChoice = harvestPrompt.Choices.First();
                result = game.Submit(new AnswerPromptCommand(
                    0,
                    harvestPrompt.PromptId,
                    harvestChoice.Id,
                    game.Revision));
                continue;
            }

            if (result.State.Status == EngineStatus.AwaitingHumanPlay)
            {
                var playPrompt = result.PendingDecision!;
                result = game.Submit(new EndPlayPhaseCommand(
                    ActorSeat: 0,
                    ExpectedRevision: game.Revision,
                    PromptId: playPrompt.PromptId));
            }
            else
            {
                result = game.Submit(new AdvanceCommand(game.Revision));
            }
        }
    }

    NotNull(selectedGame);
    var gameWithPrompt = selectedGame!;
    var prompt = gameWithPrompt.PendingDecision!;
    True(prompt.PromptId.IsValid);
    Equal(gameWithPrompt.Revision, prompt.Revision);
    True(prompt.Choices.Any(choice => choice.Parameters["response"] == "dodge"));
    var decline = prompt.Choices.Single(choice => choice.Parameters["response"] == "take-damage");
    var before = SnapshotJson.Serialize(gameWithPrompt.State);
    var revisionBefore = gameWithPrompt.Revision;

    var stale = gameWithPrompt.Submit(new AnswerPromptCommand(
        ActorSeat: 0,
        Prompt: prompt.PromptId,
        Choice: decline.Id,
        ExpectedRevision: revisionBefore - 1));
    False(stale.Accepted);
    Equal(CommandErrorCode.StaleRevision, stale.Error!.Code);
    Equal(revisionBefore, gameWithPrompt.Revision);
    Equal(before, SnapshotJson.Serialize(gameWithPrompt.State));

    var wrongPrompt = gameWithPrompt.Submit(new AnswerPromptCommand(
        ActorSeat: 0,
        Prompt: new PromptId(prompt.PromptId.Value + 1),
        Choice: decline.Id,
        ExpectedRevision: revisionBefore));
    False(wrongPrompt.Accepted);
    Equal(CommandErrorCode.InvalidPrompt, wrongPrompt.Error!.Code);
    Equal(before, SnapshotJson.Serialize(gameWithPrompt.State));

    var wrongChoice = gameWithPrompt.Submit(new AnswerPromptCommand(
        ActorSeat: 0,
        Prompt: prompt.PromptId,
        Choice: new ChoiceId("response.not-published"),
        ExpectedRevision: revisionBefore));
    False(wrongChoice.Accepted);
    Equal(CommandErrorCode.InvalidChoice, wrongChoice.Error!.Code);
    Equal(before, SnapshotJson.Serialize(gameWithPrompt.State));

    var accepted = gameWithPrompt.Submit(new AnswerPromptCommand(
        ActorSeat: 0,
        Prompt: prompt.PromptId,
        Choice: decline.Id,
        ExpectedRevision: revisionBefore));
    True(accepted.Accepted);
    Equal(revisionBefore + 1, gameWithPrompt.Revision);
    True(gameWithPrompt.PendingDecision is null ||
         gameWithPrompt.PendingDecision.PromptId != prompt.PromptId);
}

static void CommandReentryIsTyped()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        Seed = 907,
        HumanSeat = 0,
        HumanRole = Role.Lord
    });
    CommandResult? reentry = null;
    game.LogAdded += _ => reentry ??= game.Submit(new AdvanceCommand(game.Revision));

    var result = game.Submit(new StartGameCommand());

    True(result.Accepted);
    NotNull(reentry);
    False(reentry!.Accepted);
    Equal(CommandErrorCode.ReentrantOperation, reentry.Error!.Code);
    Equal(result.Revision, game.Revision);
}

static void WinnerRules()
{
    Equal(Winner.None, GameRules.EvaluateWinner(
    [
        new(Role.Lord, true),
        new(Role.Loyalist, true),
        new(Role.Rebel, true),
        new(Role.Renegade, true)
    ]));

    Equal(Winner.LordAndLoyalists, GameRules.EvaluateWinner(
    [
        new(Role.Lord, true),
        new(Role.Loyalist, true),
        new(Role.Rebel, false),
        new(Role.Renegade, false)
    ]));

    Equal(Winner.Rebels, GameRules.EvaluateWinner(
    [
        new(Role.Lord, false),
        new(Role.Loyalist, false),
        new(Role.Rebel, true),
        new(Role.Renegade, true)
    ]));

    Equal(Winner.Renegade, GameRules.EvaluateWinner(
    [
        new(Role.Lord, false),
        new(Role.Loyalist, false),
        new(Role.Rebel, false),
        new(Role.Renegade, true)
    ]));
}

static void CardCatalogDefinitions()
{
    Equal(20, CardCatalog.ImplementedCards.Count);
    Equal("杀", CardCatalog.Get(CardKind.Slash).DisplayName);
    Equal("闪", CardCatalog.Get(CardKind.Dodge).DisplayName);
    Equal("桃", CardCatalog.Get(CardKind.Peach).DisplayName);
    Equal("决斗", CardCatalog.Get(CardKind.Duel).DisplayName);
    Equal("无中生有", CardCatalog.Get(CardKind.DrawTwo).DisplayName);
    Equal("南蛮入侵", CardCatalog.Get(CardKind.BarbarianAssault).DisplayName);
    Equal("万箭齐发", CardCatalog.Get(CardKind.ArrowBarrage).DisplayName);
    Equal("桃园结义", CardCatalog.Get(CardKind.PeachGarden).DisplayName);
    Equal("五谷丰登", CardCatalog.Get(CardKind.FiveGrains).DisplayName);
    Equal("过河拆桥", CardCatalog.Get(CardKind.Dismantlement).DisplayName);
    Equal("顺手牵羊", CardCatalog.Get(CardKind.Snatch).DisplayName);
    Equal("火攻", CardCatalog.Get(CardKind.FireAttack).DisplayName);
    Equal("火杀", CardCatalog.Get(CardKind.FireSlash).DisplayName);
    Equal("雷杀", CardCatalog.Get(CardKind.ThunderSlash).DisplayName);
    Equal("酒", CardCatalog.Get(CardKind.Alcohol).DisplayName);
    Equal("诸葛连弩", CardCatalog.Get(CardKind.Crossbow).DisplayName);
    Equal("八卦阵", CardCatalog.Get(CardKind.BaguaFormation).DisplayName);
    Equal("赤兔", CardCatalog.Get(CardKind.OffensiveHorse).DisplayName);
    Equal("绝影", CardCatalog.Get(CardKind.DefensiveHorse).DisplayName);
    Equal("玉玺", CardCatalog.Get(CardKind.JadeSeal).DisplayName);
    True(CardCatalog.ImplementedCards.All(definition =>
        !string.IsNullOrWhiteSpace(definition.Description)));
    Equal(38, CardCatalog.Get(CardKind.Peach).AiPlayValue);
    Equal(65, CardCatalog.Get(CardKind.Dodge).AiResponseValue);
    Equal(40, CardCatalog.Get(CardKind.Slash).HandKeepValue);
}

static void StandardDeckContent()
{
    var definition = StandardDeckCatalog.BasicDemo;
    var deck = StandardDeckCatalog.CreateBasicDemoDeck();
    var counts = deck.GroupBy(card => card.Kind)
        .ToDictionary(group => group.Key, group => group.Count());

    Equal(4, definition.InitialHandSize);
    Equal(2, definition.DrawPerTurn);
    Equal(78, definition.TotalCards);
    Equal(definition.TotalCards, deck.Count);
    Equal(18, counts[CardKind.Slash]);
    Equal(18, counts[CardKind.Dodge]);
    Equal(10, counts[CardKind.Peach]);
    Equal(4, counts[CardKind.Duel]);
    Equal(2, counts[CardKind.DrawTwo]);
    Equal(2, counts[CardKind.BarbarianAssault]);
    Equal(2, counts[CardKind.ArrowBarrage]);
    Equal(2, counts[CardKind.PeachGarden]);
    Equal(2, counts[CardKind.FiveGrains]);
    Equal(2, counts[CardKind.Dismantlement]);
    Equal(2, counts[CardKind.Snatch]);
    Equal(2, counts[CardKind.FireAttack]);
    Equal(2, counts[CardKind.FireSlash]);
    Equal(2, counts[CardKind.ThunderSlash]);
    Equal(2, counts[CardKind.Alcohol]);
    Equal(2, counts[CardKind.Crossbow]);
    Equal(1, counts[CardKind.BaguaFormation]);
    Equal(1, counts[CardKind.OffensiveHorse]);
    Equal(1, counts[CardKind.DefensiveHorse]);
    Equal(1, counts[CardKind.JadeSeal]);
    True(deck.Select(card => card.Id).SequenceEqual(Enumerable.Range(1, deck.Count)));
    Equal(CardKind.Slash, deck[0].Kind);
    Equal(CardKind.Slash, deck[17].Kind);
    Equal(CardKind.Dodge, deck[18].Kind);
    Equal(CardKind.Peach, deck[36].Kind);
    Equal(CardKind.Duel, deck[46].Kind);
    Equal(CardKind.DrawTwo, deck[50].Kind);
    Equal(CardKind.BarbarianAssault, deck[52].Kind);
    Equal(CardKind.ArrowBarrage, deck[54].Kind);
    Equal(CardKind.PeachGarden, deck[56].Kind);
    Equal(CardKind.FiveGrains, deck[58].Kind);
    Equal(CardKind.Dismantlement, deck[60].Kind);
    Equal(CardKind.Snatch, deck[62].Kind);
    Equal(CardKind.FireAttack, deck[70].Kind);
    Equal(CardKind.FireSlash, deck[64].Kind);
    Equal(CardKind.ThunderSlash, deck[66].Kind);
    Equal(CardKind.Alcohol, deck[68].Kind);
    Equal(Suit.Spade, deck[0].Suit);
    Equal(1, deck[0].Rank);
    Equal(Suit.Diamond, deck[3].Suit);
    Equal(4, deck[3].Rank);
}

static void StandardContentRegistryBuilds()
{
    var registry = StandardContentRegistry.Create();
    var secondRegistry = StandardContentRegistry.Create();

    Equal(1, registry.Packages.Count);
    Equal("standard", registry.Packages[0].Id);
    Equal(20, registry.Cards.Count);
    Equal(11, registry.Skills.Count);
    Equal(12, registry.Generals.Count);
    Equal(1, registry.Decks.Count);
    Equal(2, registry.Modes.Count);
    Equal(CardKind.Slash, registry.GetCard("standard:slash").LegacyKind);
    Equal("standard:jianxiong", registry.Generals["standard:cao-cao"].SkillId);
    Equal("standard:wusheng", registry.Generals["standard:guan-yu"].SkillId);
    Equal("standard:longdan", registry.Generals["standard:zhao-yun"].SkillId);
    Equal(SkillKind.Yiji, registry.GetSkill("standard:yiji").LegacyKind);
    Equal("standard:yiji", registry.Generals["standard:guo-jia"].SkillId);
    Equal(SkillKind.Jieming, registry.GetSkill("standard:jieming").LegacyKind);
    Equal("standard:jieming", registry.Generals["standard:xun-yu"].SkillId);
    Equal(SkillKind.Yuanhu, registry.GetSkill("standard:yuanhu").LegacyKind);
    Equal("standard:yuanhu", registry.Generals["standard:demo-yuanhu"].SkillId);
    Equal(18, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:slash").Count);
    Equal(4, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:duel").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:draw_two").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:barbarian_assault").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:arrow_barrage").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:fire_attack").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:peach_garden").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:five_grains").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:dismantlement").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:snatch").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:fire_slash").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:thunder_slash").Count);
    Equal(2, registry.GetDeck("standard:basic-demo").Cards.Single(card =>
        card.CardDefinitionId == "standard:alcohol").Count);

    True(!ReferenceEquals(registry.Cards, secondRegistry.Cards));
    Throws<NotSupportedException>(() =>
        ((IDictionary<string, ContentCardDefinition>)registry.Cards).Add(
            "test:extra",
            new ContentCardDefinition("test:extra", "额外", "测试", "测试")));
}

static void ContentRegistryValidation()
{
    var duplicateId = new ContentCardDefinition(
        "test:duplicate", "重复", "测试", "测试");
    Throws<InvalidOperationException>(() => ContentRegistry.Build(
        new SyntheticPackage("one", builder => builder.AddCard(duplicateId)),
        new SyntheticPackage("two", builder => builder.AddCard(duplicateId))));

    Throws<InvalidOperationException>(() => ContentRegistry.Build(
        new SyntheticPackage("deck", builder => builder.AddDeck(new ContentDeckRecipe(
            "test:deck",
            "坏牌堆",
            1,
            1,
            [new ContentDeckCardCount("missing:card", 1)])))));

    Throws<InvalidOperationException>(() => ContentRegistry.Build(
        new SyntheticPackage(
            "consumer",
            _ => { },
            new PackageDependency("missing", new Version(1, 0, 0)))));

    Throws<InvalidOperationException>(() => ContentRegistry.Build(
        new SyntheticPackage(
            "cycle-a",
            _ => { },
            new PackageDependency("cycle-b", new Version(1, 0, 0))),
        new SyntheticPackage(
            "cycle-b",
            _ => { },
            new PackageDependency("cycle-a", new Version(1, 0, 0)))));
}

static void EngineConsumesStandardRegistry()
{
    var registry = StandardContentRegistry.Create();
    var game = GameEngine.CreateStandard(
        new GameOptions
        {
            Seed = 913,
            HumanSeat = 0,
            HumanRole = Role.Lord
        },
        registry);

    True(ReferenceEquals(registry, game.ContentRegistry));
    Equal(78, game.CreateCardZoneDiagnostics().Count);
    Equal(4, game.State.Players.Single(player => player.Seat == 0).HandCount);
    Equal(CardKind.Slash, game.CreateCardZoneDiagnostics()
        .Single(card => card.CardId == 1).CardKind);
}

static void InteractiveSetupPipeline()
{
    var options = new GameOptions
    {
        Seed = 1_931,
        HumanSeat = 0,
        HumanRole = Role.Lord,
        UseInteractiveSetup = true
    };
    var registry = StandardContentRegistry.Create();
    var left = GameEngine.CreateStandard(options, registry);
    var right = GameEngine.CreateStandard(options, StandardContentRegistry.Create());

    Equal(EngineStatus.NotStarted, left.State.Status);
    True(left.State.Players.All(player => player.HandCount == 0));
    True(left.State.Players.All(player => player.GeneralId == string.Empty));

    var started = left.Submit(new StartGameCommand());
    var rightStarted = right.Submit(new StartGameCommand());
    True(started.Accepted);
    True(rightStarted.Accepted);
    Equal(EngineStatus.AwaitingHumanGeneralSelection, started.State.Status);
    Equal(DecisionKind.SelectGeneral, started.PendingDecision!.Kind);
    Equal(3, started.PendingDecision.ValidContentIds.Count);
    True(started.PendingDecision.ValidContentIds.SequenceEqual(
        rightStarted.PendingDecision!.ValidContentIds));
    True(started.PendingDecision.Choices.All(choice => choice.ContentIds.Count == 1));
    True(started.PendingDecision.PromptId.IsValid);

    var otherView = left.CreateSnapshot(1);
    Equal<PendingDecision?>(null, otherView.PendingDecision);
    True(otherView.Players.All(player => player.GeneralId == string.Empty));
    var otherJson = SnapshotJson.Serialize(otherView);
    True(started.PendingDecision.ValidContentIds.All(
        candidate => !otherJson.Contains(candidate, StringComparison.Ordinal)));

    var beforeInvalid = left.Revision;
    var invalid = left.Submit(new SelectGeneralCommand(
        0,
        "standard:not-a-candidate",
        beforeInvalid,
        started.PendingDecision.PromptId));
    False(invalid.Accepted);
    Equal(CommandErrorCode.InvalidGeneral, invalid.Error!.Code);
    Equal(beforeInvalid, left.Revision);

    var choice = started.PendingDecision.Choices[0];
    var selected = left.Submit(new SelectGeneralCommand(
        0,
        choice.ContentIds.Single(),
        left.Revision,
        started.PendingDecision.PromptId));
    var rightSelected = right.Submit(new SelectGeneralCommand(
        0,
        rightStarted.PendingDecision!.Choices[0].ContentIds.Single(),
        right.Revision,
        rightStarted.PendingDecision.PromptId));

    True(selected.Accepted);
    True(rightSelected.Accepted);
    Equal(EngineStatus.AwaitingHumanPlay, selected.State.Status);
    True(selected.State.Players.Single(player => player.Seat == 0).HandCount >= 4);
    True(selected.State.Players.All(player => player.IsGeneralPublic));
    Equal(
        selected.State.Players.Count,
        selected.State.Players.Select(player => player.GeneralId)
            .Distinct(StringComparer.Ordinal)
            .Count());
    Equal(
        SnapshotJson.Serialize(left.CreateSnapshot(0, revealAll: true)),
        SnapshotJson.Serialize(right.CreateSnapshot(0, revealAll: true)));

    True(left.Events.Any(eventItem => eventItem.Payload is GeneralSelectionRequestedEvent));
    True(left.Events.Any(eventItem => eventItem.Payload is GeneralSelectedEvent));
    True(left.Events.Any(eventItem => eventItem.Payload is SetupCompletedEvent));
    var leftEvents = left.Events.Select(eventItem =>
        $"{eventItem.Sequence}|{eventItem.Payload.GetType().Name}|{eventItem.Payload}");
    var rightEvents = right.Events.Select(eventItem =>
        $"{eventItem.Sequence}|{eventItem.Payload.GetType().Name}|{eventItem.Payload}");
    True(leftEvents.SequenceEqual(rightEvents));
}

static void InteractiveAiSetup()
{
    for (var seed = 1; seed <= 8; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = -1,
                HumanRole = null,
                UseInteractiveSetup = true,
                MaxTurns = 250
            },
            StandardContentRegistry.Create());

        var result = game.Submit(new StartGameCommand());
        True(result.Accepted);
        Equal(EngineStatus.Completed, result.Status);
        Equal(5, game.AiGeneralThoughts.Count);
        Equal(
            5,
            result.State.Players
                .Select(player => player.GeneralId)
                .Distinct(StringComparer.Ordinal)
                .Count());
        True(result.State.Players.All(player => player.IsGeneralPublic));
        True(game.Events.Any(eventItem => eventItem.Payload is SetupCompletedEvent));
        AssertCardInventory(game);
    }
}

static void FivePlayerIdentityMode()
{
    var game = GameEngine.CreateStandard(
        new GameOptions
        {
            Seed = 917,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord
        },
        StandardContentRegistry.Create());
    var view = game.CreateSnapshot(0, revealAll: true);
    var roles = view.Players
        .GroupBy(player => player.Role!.Value)
        .ToDictionary(group => group.Key, group => group.Count());

    Equal(5, game.PlayerCount);
    Equal(5, view.Players.Count);
    Equal(1, roles[Role.Lord]);
    Equal(1, roles[Role.Loyalist]);
    Equal(2, roles[Role.Rebel]);
    Equal(1, roles[Role.Renegade]);
    True(view.Players.All(player => player.HandCount == 4));

    var started = game.Submit(new StartGameCommand());
    True(started.Accepted);
    Equal(EngineStatus.AwaitingHumanPlay, started.State.Status);
    Equal(22, game.CreateCardZoneDiagnostics().Count(card =>
        card.Location.Zone == CardZoneKind.Hand));
}

static void FivePlayerAiSmoke()
{
    for (var seed = 1; seed <= 16; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = -1,
                HumanRole = null,
                MaxTurns = 250
            },
            StandardContentRegistry.Create());
        var result = game.Submit(new StartGameCommand());

        True(result.Accepted);
        Equal(EngineStatus.Completed, result.State.Status);
        True(result.Winner != Winner.None);
        AssertCardInventory(game);
    }
}

static void GeneralContent()
{
    var generals = GeneralCatalog.DemoGenerals;
    Equal(11, generals.Count);
    Equal(generals.Count, generals.Select(general => general.Id).Distinct().Count());
    True(generals.All(general =>
        general.SkillName == SkillRegistry.Get(general.Skill).Name));
    Equal(SkillKind.Wusheng, generals.Single(general => general.Id == "guan-yu").Skill);
    Equal(SkillKind.Longdan, generals.Single(general => general.Id == "zhao-yun").Skill);
    Equal(SkillKind.Jieming, generals.Single(general => general.Id == "xun-yu").Skill);
    Equal(SkillKind.Yuanhu, generals.Single(general => general.Id == "demo-yuanhu").Skill);
}

static void AiCardContentPolicy()
{
    var peach = new CardSnapshot(12, CardKind.Peach, Suit.Heart, 12, "桃", "Q");
    var drawTwo = new CardSnapshot(13, CardKind.DrawTwo, Suit.Spade, 13, "无中生有", "K");
    var barbarianAssault = new CardSnapshot(14, CardKind.BarbarianAssault, Suit.Club, 1, "南蛮入侵", "A");
    var arrowBarrage = new CardSnapshot(15, CardKind.ArrowBarrage, Suit.Diamond, 2, "万箭齐发", "2");
    var peachGarden = new CardSnapshot(16, CardKind.PeachGarden, Suit.Heart, 3, "桃园结义", "3");
    var fiveGrains = new CardSnapshot(17, CardKind.FiveGrains, Suit.Spade, 4, "五谷丰登", "4");
    var dismantlement = new CardSnapshot(18, CardKind.Dismantlement, Suit.Club, 5, "过河拆桥", "5");
    var snatch = new CardSnapshot(19, CardKind.Snatch, Suit.Diamond, 6, "顺手牵羊", "6");
    var fireSlash = new CardSnapshot(20, CardKind.FireSlash, Suit.Heart, 7, "火杀", "7");
    var thunderSlash = new CardSnapshot(21, CardKind.ThunderSlash, Suit.Spade, 8, "雷杀", "8");
    var alcohol = new CardSnapshot(22, CardKind.Alcohol, Suit.Club, 9, "酒", "9");
    var fireAttack = new CardSnapshot(23, CardKind.FireAttack, Suit.Diamond, 10, "火攻", "10");
    var self = new PlayerSnapshot(
        Seat: 0,
        Name: "AI",
        IsHuman: false,
        Role: Role.Lord,
        IsRoleRevealed: true,
        GeneralId: "liu-bei",
        GeneralName: "刘备",
        PortraitKey: "liu_bei",
        Skill: SkillKind.None,
        SkillName: "无",
        SkillDescription: "",
        Hp: 2,
        MaxHp: 4,
        IsAlive: true,
        HandCount: 12,
        Hand: [peach, drawTwo, barbarianAssault, arrowBarrage, peachGarden, fiveGrains, dismantlement, snatch, fireSlash, thunderSlash, alcohol, fireAttack]);
    var target = self with
    {
        Seat = 1,
        Name = "Target",
        IsHuman = false,
        Role = null,
        IsRoleRevealed = false,
        GeneralId = "",
        GeneralName = "目标",
        PortraitKey = "",
        HandCount = 4,
        Hand = []
    };
    var view = new GameSnapshot(
        Seed: null,
        HumanSeat: -1,
        Status: EngineStatus.Running,
        Winner: Winner.None,
        TurnNumber: 1,
        CurrentSeat: 0,
        Phase: TurnPhase.Play,
        DrawPileCount: 20,
        DiscardPileCount: 0,
        Players: [self, target],
        PendingDecision: null);
    var actions = new LegalAction[]
    {
        new(LegalActionKind.Peach, peach.Id, self.Seat, "对自己使用【桃】"),
        new(LegalActionKind.DrawTwo, drawTwo.Id, null, "使用【无中生有】摸两张牌"),
        new(LegalActionKind.BarbarianAssault, barbarianAssault.Id, null, "使用【南蛮入侵】"),
        new(LegalActionKind.ArrowBarrage, arrowBarrage.Id, null, "使用【万箭齐发】"),
        new(LegalActionKind.PeachGarden, peachGarden.Id, null, "使用【桃园结义】"),
        new(LegalActionKind.FiveGrains, fiveGrains.Id, null, "使用【五谷丰登】"),
        new(LegalActionKind.Dismantlement, dismantlement.Id, 1, "对 目标使用【过河拆桥】"),
        new(LegalActionKind.Snatch, snatch.Id, 1, "对 目标使用【顺手牵羊】"),
        new(LegalActionKind.FireAttack, fireAttack.Id, 1, "对 目标使用【火攻】"),
        new(LegalActionKind.Slash, fireSlash.Id, 1, "对 目标使用【火杀】"),
        new(LegalActionKind.Slash, thunderSlash.Id, 1, "对 目标使用【雷杀】"),
        new(LegalActionKind.Alcohol, alcohol.Id, null, "使用【酒】，本回合下一张杀伤害+1"),
        new(LegalActionKind.EndPlay, null, null, "结束出牌")
    };

    var (action, thought) = new SimpleAiBrain(seat: 0, seed: 5)
        .ChoosePlay(view, actions, thoughtSequence: 1);

    Equal(LegalActionKind.Peach, action.Kind);
    True(thought.Candidates.Single(candidate => candidate.Action.Kind == LegalActionKind.Peach).Score > 0d);
    True(thought.Candidates.Single(candidate => candidate.Action.Kind == LegalActionKind.DrawTwo).Score > 0d);
    True(thought.Candidates.Single(candidate => candidate.Action.Kind == LegalActionKind.BarbarianAssault).Score > 0d);
    True(thought.Candidates.Single(candidate => candidate.Action.Kind == LegalActionKind.ArrowBarrage).Score > 0d);
    True(thought.Candidates.Single(candidate => candidate.Action.Kind == LegalActionKind.PeachGarden).Score > 0d);
    True(thought.Candidates.Single(candidate => candidate.Action.Kind == LegalActionKind.FiveGrains).Score > 0d);
    var dismantlementCandidate = thought.Candidates.Single(candidate =>
        candidate.Action.Kind == LegalActionKind.Dismantlement);
    True(dismantlementCandidate.Score > 0d);
    True(dismantlementCandidate.Reason.Contains("不读取目标暗牌", StringComparison.Ordinal));
    var snatchCandidate = thought.Candidates.Single(candidate =>
        candidate.Action.Kind == LegalActionKind.Snatch);
    True(snatchCandidate.Score > 0d);
    True(snatchCandidate.Reason.Contains("距离 1", StringComparison.Ordinal));
    True(snatchCandidate.Reason.Contains("不读取目标暗牌", StringComparison.Ordinal));
    var fireAttackCandidate = thought.Candidates.Single(candidate =>
        candidate.Action.Kind == LegalActionKind.FireAttack);
    True(fireAttackCandidate.Score > 0d);
    True(fireAttackCandidate.Reason.Contains("不读取双方暗牌", StringComparison.Ordinal));
    True(thought.Candidates.Single(candidate => candidate.Action.CardId == fireSlash.Id).Score > 0d);
    True(thought.Candidates.Single(candidate => candidate.Action.CardId == thunderSlash.Id).Score > 0d);
    var alcoholCandidate = thought.Candidates.Single(candidate => candidate.Action.CardId == alcohol.Id);
    True(alcoholCandidate.Score > 0d);
    True(alcoholCandidate.Reason.Contains("+1 伤害", StringComparison.Ordinal));

    var alcoholCard = new Card(alcohol.Id, alcohol.Kind, alcohol.Suit, alcohol.Rank);
    var dyingAlcohol = new SimpleAiBrain(seat: 0, seed: 5).ChooseDyingResponseWithAlcohol(
        view,
        victimSeat: 0,
        peaches: [],
        alcohols: [alcoholCard],
        thoughtSequence: 2);
    True(dyingAlcohol.UseAlcohol);
    Equal(alcohol.Id, dyingAlcohol.AlcoholCardId);
    True(dyingAlcohol.Thought.Candidates.Any(candidate =>
        candidate.Action.Kind == LegalActionKind.Alcohol &&
        candidate.Reason.Contains("自己濒死", StringComparison.Ordinal)));

    var nonVictimAlcohol = new SimpleAiBrain(seat: 0, seed: 5).ChooseDyingResponseWithAlcohol(
        view,
        victimSeat: 1,
        peaches: [],
        alcohols: [alcoholCard],
        thoughtSequence: 3);
    False(nonVictimAlcohol.UseAlcohol);
    True(nonVictimAlcohol.Thought.Candidates.All(candidate =>
        candidate.Action.Kind != LegalActionKind.Alcohol));

    var harvest = new SimpleAiBrain(seat: 0, seed: 5).ChooseHarvestCard(
        view,
        [
            new CardSnapshot(21, CardKind.Slash, Suit.Spade, 1, "杀", "A"),
            new CardSnapshot(22, CardKind.Peach, Suit.Heart, 2, "桃", "2")
        ],
        thoughtSequence: 2);
    Equal(22, harvest.CardId);
    True(harvest.Thought.Candidates.Count == 2);

    var feedbackWithFullHand = new SimpleAiBrain(seat: 0, seed: 5).ChooseFeedback(
        view,
        sourceSeat: 1,
        incomingCard: CardKind.Slash,
        thoughtSequence: 4);
    False(feedbackWithFullHand.UseFeedback);
    True(feedbackWithFullHand.Thought.Candidates.Any(candidate =>
        candidate.Action.Kind == LegalActionKind.SkipFeedback));

    var sparseSelf = self with
    {
        Hp = 1,
        HandCount = 0,
        Hand = []
    };
    var sparseView = view with { Players = [sparseSelf, target] };
    var feedbackWithSparseHand = new SimpleAiBrain(seat: 0, seed: 5).ChooseFeedback(
        sparseView,
        sourceSeat: 1,
        incomingCard: CardKind.Slash,
        thoughtSequence: 5);
    True(feedbackWithSparseHand.UseFeedback);
    True(feedbackWithSparseHand.Thought.Candidates.Any(candidate =>
        candidate.Action.Kind == LegalActionKind.Feedback));

    var redVirtualSlash = new CardSnapshot(23, CardKind.Peach, Suit.Heart, 10, "桃", "10");
    var wushengSelf = self with
    {
        Skill = SkillKind.Wusheng,
        SkillName = "武圣",
        SkillDescription = "红色牌可当作杀使用。",
        HandCount = 1,
        Hand = [redVirtualSlash]
    };
    var wushengView = view with { Players = [wushengSelf, target] };
    var (wushengAction, wushengThought) = new SimpleAiBrain(seat: 0, seed: 5).ChoosePlay(
        wushengView,
        [
            new LegalAction(
                LegalActionKind.Slash,
                redVirtualSlash.Id,
                target.Seat,
                "将【桃】当作【杀】使用",
                PlayedCardKind: CardKind.Slash),
            new LegalAction(LegalActionKind.EndPlay, null, null, "结束出牌")
        ],
        thoughtSequence: 6);
    Equal(LegalActionKind.Slash, wushengAction.Kind);
    Equal(redVirtualSlash.Id, wushengAction.CardId);
    True(wushengThought.Candidates.Single(candidate =>
        candidate.Action.CardId == redVirtualSlash.Id).Reason.Contains("当作杀", StringComparison.Ordinal));

    var jiemingSelf = self with
    {
        Skill = SkillKind.Jieming,
        SkillName = "节命",
        SkillDescription = "受到伤害后，可令一名手牌数少于体力上限的角色摸牌至上限。",
        HandCount = 2,
        Hand = []
    };
    var sparseTarget = target with { MaxHp = 4, HandCount = 1, Hand = [] };
    var jiemingView = view with { Players = [jiemingSelf, sparseTarget] };
    var (jiemingTargetSeat, jiemingThought) = new SimpleAiBrain(seat: 0, seed: 5)
        .ChooseJiemingTarget(jiemingView, [1], thoughtSequence: 7);
    Equal<int?>(1, jiemingTargetSeat);
    True(jiemingThought.Candidates.Any(candidate =>
        candidate.Action.Kind == LegalActionKind.Jieming &&
        candidate.Action.TargetSeat == 1));
    True(jiemingThought.Candidates.All(candidate =>
        candidate.Action.Kind != LegalActionKind.Jieming ||
        candidate.Reason.Contains("不读取目标隐藏牌面", StringComparison.Ordinal)));

    var yuanhuSelf = self with
    {
        Skill = SkillKind.Yuanhu,
        SkillName = "援护",
        SkillDescription = "其他角色受到伤害后，可弃置一张牌令其回复 1 点体力。",
        HandCount = 1,
        Hand = [redVirtualSlash]
    };
    var yuanhuTarget = target with
    {
        Role = Role.Loyalist,
        IsRoleRevealed = true,
        Hp = 3,
        MaxHp = 4,
        HandCount = 3,
        Hand = []
    };
    var yuanhuView = view with { Players = [yuanhuSelf, yuanhuTarget] };
    var (yuanhuCardId, yuanhuThought) = new SimpleAiBrain(seat: 0, seed: 5)
        .ChooseYuanhuCard(yuanhuView, [redVirtualSlash.Id], targetSeat: 1, thoughtSequence: 8);
    Equal<int?>(redVirtualSlash.Id, yuanhuCardId);
    True(yuanhuThought.Candidates.Any(candidate =>
        candidate.Action.Kind == LegalActionKind.Yuanhu &&
        candidate.Action.TargetSeat == 1));
    True(yuanhuThought.Candidates.All(candidate =>
        candidate.Action.Kind != LegalActionKind.Yuanhu ||
        candidate.Reason.Contains("不读取目标手牌", StringComparison.Ordinal)));
}

static void PassiveSkills()
{
    var fullHand = new PlayerSkillContext(0, 4, 4, 3, TurnPhase.Draw);
    var emptyHand = fullHand with { HandCount = 0 };

    Equal(3, SkillRegistry.Get(SkillKind.Yingzi).ModifyDrawCount(fullHand, 2));
    Equal(int.MaxValue, SkillRegistry.Get(SkillKind.Paoxiao).ModifySlashLimit(fullHand, 1));
    True(SkillRegistry.Get(SkillKind.Kongcheng).ProhibitsSlashTarget(emptyHand));
    True(SkillRegistry.Get(SkillKind.Jianxiong).ClaimsDamageCard(
        new DamageSkillContext(fullHand, 1, CardKind.Slash, true)));
    True(SkillRegistry.Get(SkillKind.Jianxiong).ClaimsDamageCard(
        new DamageSkillContext(fullHand, 1, CardKind.FireSlash, true, DamageNature.Fire)));
    True(SkillRegistry.Get(SkillKind.Jianxiong).ClaimsDamageCard(
        new DamageSkillContext(fullHand, 1, CardKind.ThunderSlash, true, DamageNature.Thunder)));
    True(SkillRegistry.Get(SkillKind.Feedback).ClaimsDamageCard(
        new DamageSkillContext(fullHand, 1, CardKind.Duel, true)));
    True(SkillRegistry.Get(SkillKind.Feedback).OffersDamageCardChoice(
        new DamageSkillContext(fullHand, 1, CardKind.Duel, true)));
    True(SkillRegistry.Get(SkillKind.Feedback).CanTriggerAfterDamage(
        new DamageSkillContext(fullHand, 1, CardKind.Duel, true, TargetSeat: 0)));
    False(SkillRegistry.Get(SkillKind.Feedback).CanTriggerAfterDamage(
        new DamageSkillContext(fullHand, 1, CardKind.Duel, true, TargetSeat: 1)));
    False(SkillRegistry.Get(SkillKind.Feedback).OffersDamageCardChoice(
        new DamageSkillContext(fullHand, 1, CardKind.Duel, false)));
    False(SkillRegistry.Get(SkillKind.Feedback).ClaimsDamageCard(
        new DamageSkillContext(fullHand, 1, CardKind.Duel, false)));

    var yijiContext = new DamageSkillContext(
        fullHand,
        1,
        CardKind.Slash,
        true,
        Amount: 1,
        SourceCardId: 21,
        TargetSeat: 0);
    True(SkillRegistry.Get(SkillKind.Yiji).OffersDamageCardChoice(yijiContext));
    Equal(
        DamageSkillEffectKind.GiftDrawnCard,
        SkillRegistry.Get(SkillKind.Yiji).GetDamageSkillEffect(yijiContext));
    True(SkillRegistry.Get(SkillKind.Yiji).CanTriggerAfterDamage(yijiContext));
    False(SkillRegistry.Get(SkillKind.Yiji).CanTriggerAfterDamage(
        yijiContext with { TargetSeat = 1 }));

    var jiemingContext = yijiContext;
    True(SkillRegistry.Get(SkillKind.Jieming).OffersDamageCardChoice(jiemingContext));
    Equal(
        DamageSkillEffectKind.DrawToMaxHand,
        SkillRegistry.Get(SkillKind.Jieming).GetDamageSkillEffect(jiemingContext));
    True(SkillRegistry.Get(SkillKind.Jieming).CanTriggerAfterDamage(jiemingContext));
    False(SkillRegistry.Get(SkillKind.Jieming).CanTriggerAfterDamage(
        jiemingContext with { TargetSeat = 1 }));
    False(SkillRegistry.Get(SkillKind.Jieming).OffersDamageCardChoice(
        jiemingContext with { Amount = 0 }));

    var yuanhuContext = yijiContext with
    {
        Owner = fullHand,
        TargetSeat = 1,
        TargetHp = 3,
        TargetMaxHp = 4
    };
    True(SkillRegistry.Get(SkillKind.Yuanhu).CanTriggerAfterDamage(yuanhuContext));
    True(SkillRegistry.Get(SkillKind.Yuanhu).OffersDamageCardChoice(yuanhuContext));
    Equal(
        DamageSkillEffectKind.RecoverDamageTarget,
        SkillRegistry.Get(SkillKind.Yuanhu).GetDamageSkillEffect(yuanhuContext));
    False(SkillRegistry.Get(SkillKind.Yuanhu).CanTriggerAfterDamage(
        yuanhuContext with { TargetSeat = fullHand.Seat }));
    False(SkillRegistry.Get(SkillKind.Yuanhu).OffersDamageCardChoice(
        yuanhuContext with { Owner = emptyHand }));
    False(SkillRegistry.Get(SkillKind.Yuanhu).OffersDamageCardChoice(
        yuanhuContext with { TargetHp = 4 }));

    var redPeach = new Card(31, CardKind.Peach, Suit.Heart, 5);
    var blackPeach = new Card(32, CardKind.Peach, Suit.Spade, 6);
    var wusheng = SkillRegistry.Get(SkillKind.Wusheng);
    True(wusheng.CanUseAsSlash(fullHand, redPeach));
    False(wusheng.CanUseAsSlash(fullHand, blackPeach));
    False(wusheng.CanUseAsSlash(fullHand, redPeach with { Kind = CardKind.Slash }));

    var longdan = SkillRegistry.Get(SkillKind.Longdan);
    var physicalDodge = new Card(33, CardKind.Dodge, Suit.Spade, 7);
    var physicalSlash = new Card(34, CardKind.Slash, Suit.Heart, 8);
    var physicalPeach = new Card(35, CardKind.Peach, Suit.Heart, 9);
    True(longdan.CanUseAsSlash(fullHand, physicalDodge));
    False(longdan.CanUseAsSlash(fullHand, physicalSlash));
    True(longdan.CanUseAsResponse(fullHand, physicalSlash, CardKind.Dodge));
    True(longdan.CanUseAsResponse(fullHand, physicalDodge, CardKind.Slash));
    False(longdan.CanUseAsResponse(fullHand, physicalPeach, CardKind.Dodge));
}

static void DamageTriggerOrdering()
{
    var ordered = CardGame.Core.DamageTriggerOrdering.Order(
    [
        new DamageTriggerCandidate(3, SkillKind.Feedback, "seat3:feedback", Priority: 10),
        new DamageTriggerCandidate(1, SkillKind.Jianxiong, "seat1:jianxiong", Priority: 10),
        new DamageTriggerCandidate(2, SkillKind.Feedback, "feedback-b", Priority: 10),
        new DamageTriggerCandidate(2, SkillKind.Feedback, "feedback-a", Priority: 10),
        new DamageTriggerCandidate(2, SkillKind.Jianxiong, "seat2:jianxiong", Priority: 10),
        new DamageTriggerCandidate(0, SkillKind.Wusheng, "wusheng", Priority: 20)
    ],
    currentActorSeat: 7,
    playerCount: 8);

    True(ordered.Select(candidate => candidate.CandidateId).SequenceEqual(
    [
        "wusheng",
        "seat1:jianxiong",
        "seat2:jianxiong",
        "feedback-a",
        "feedback-b",
        "seat3:feedback",
    ]));
    Equal(1, CardGame.Core.DamageTriggerOrdering.GetRelativeSeatOrder(7, 0, 8));
    Equal(7, CardGame.Core.DamageTriggerOrdering.GetRelativeSeatOrder(7, 6, 8));
}

static void DamageTriggerWindowFlow()
{
    GameEngine? selectedGame = null;
    var selectedAttackCardId = -1;
    var targetSeat = -1;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var result = game.Start();
        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var revealed = game.CreateSnapshot(0, revealAll: true);
        if (revealed.Players.Any(player => player.Skill == SkillKind.Yuanhu))
        {
            continue;
        }

        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            candidate.Kind == LegalActionKind.Slash &&
            candidate.TargetSeat is { } candidateTarget &&
            revealed.Players.Any(player =>
                player.Seat == candidateTarget &&
                player.Skill == SkillKind.Jianxiong &&
                player.IsAlive &&
                player.Hand.All(card => card.Kind != CardKind.Dodge)));
        if (action is not null)
        {
            selectedGame = game;
            selectedAttackCardId = revealed.Players
                .Single(player => player.Seat == 0)
                .Hand
                .Single(card => card.Id == action.CardId)
                .Id;
            targetSeat = action.TargetSeat!.Value;
        }
    }

    if (selectedGame is null || selectedAttackCardId < 0 || targetSeat < 0)
    {
        throw new InvalidOperationException("No deterministic Jianxiong trigger window was found.");
    }

    var gameWithWindow = selectedGame!;
    var attackCardId = selectedAttackCardId;
    var used = gameWithWindow.HumanPlay(
        attackCardId,
        targetSeat,
        advanceToHumanBoundary: false);
    Equal(EngineStatus.Running, used.Status);
    Equal(1, used.State.ProcessingCardCount);
    Equal<PendingDecision?>(null, used.PendingDecision);

    var triggerWindow = gameWithWindow.ResolutionStack[^1] as DamageTriggerWindowFrame;
    NotNull(triggerWindow);
    True(gameWithWindow.ResolutionStack[^2] is DamageFrame damageFrame &&
         triggerWindow!.ParentFrameId == damageFrame.Id &&
         triggerWindow.CandidateIndex == 0);
    Equal(1, triggerWindow!.Candidates.Count);
    Equal(SkillKind.Jianxiong, triggerWindow.Candidates[0].Skill);
    Equal(targetSeat, triggerWindow.Candidates[0].OwnerSeat);
    var opened = gameWithWindow.Events
        .Single(eventItem =>
            eventItem.Payload is DamageTriggerWindowOpenedEvent openedEvent &&
            openedEvent.ResolutionId == triggerWindow.Id);
    var openedEvent = (DamageTriggerWindowOpenedEvent)opened.Payload;
    Equal(triggerWindow.ParentFrameId, openedEvent.DamageFrameId);
    Equal(1, openedEvent.Candidates.Count);
    Equal(targetSeat, openedEvent.Candidates[0].OwnerSeat);
    var serialized = JsonSerializer.Serialize(gameWithWindow.ResolutionStack);
    TrueWithMessage(serialized.Contains("damage-trigger-window", StringComparison.Ordinal), "serialized trigger window");
    TrueWithMessage(serialized.Contains("CandidateIndex", StringComparison.Ordinal), "serialized trigger cursor");
    TrueWithMessage(
        !gameWithWindow.SerializeState().Contains("DamageTriggerWindow", StringComparison.Ordinal),
        "player state hides trusted trigger stack");

    var resumed = gameWithWindow.AdvanceOneStep();
    Equal(EngineStatus.Running, resumed.Status);
    Equal(0, resumed.State.ProcessingCardCount);
    Equal(0, gameWithWindow.ResolutionStack.Count);
    var advanced = gameWithWindow.Events
        .Select(eventItem => eventItem)
        .OfType<EventEnvelope>()
        .Single(eventItem =>
            eventItem.Payload is DamageTriggerWindowAdvancedEvent advancedEvent &&
            advancedEvent.ResolutionId == triggerWindow.Id);
    var advancedEvent = (DamageTriggerWindowAdvancedEvent)advanced.Payload;
    Equal(1, advancedEvent.CandidateIndex);
    TrueWithMessage(advancedEvent.Completed, "damage trigger cursor event");
    TrueWithMessage(opened.Sequence < advanced.Sequence, "damage trigger event order");
    TrueWithMessage(gameWithWindow.Events.Any(eventItem =>
        eventItem.Payload is DamageCardClaimedEvent claimed &&
        claimed.CardId == attackCardId &&
        claimed.OwnerSeat == targetSeat &&
        claimed.Skill == SkillKind.Jianxiong), "Jianxiong claim after cursor resume");
    TrueWithMessage(gameWithWindow.CardMovements.Any(movement =>
        movement.CardId == attackCardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.Hand(targetSeat) &&
        movement.Reason == CardMoveReasons.JianxiongClaim), "Jianxiong claim movement");
    AssertCardInventory(gameWithWindow);
}

static void AiPublicEvidence()
{
    var brain = new SimpleAiBrain(seat: 7, seed: 42);

    brain.ObserveSlash(sourceSeat: 1, targetSeat: 0, lordSeat: 0, revealedTargetRole: Role.Lord);
    Equal(3d, brain.RebelSuspicion[1]);

    brain.ObserveSlash(sourceSeat: 2, targetSeat: 1, lordSeat: 0, revealedTargetRole: null);
    True(brain.RebelSuspicion[2] < 0d);

    brain.ObserveDeath(killerSeat: 3, revealedVictimRole: Role.Rebel);
    Equal(-3d, brain.RebelSuspicion[3]);
}

static void FixedSeed()
{
    var left = GameEngine.CreateStandard(new GameOptions { Seed = 99 }).SerializeState(revealAll: true);
    var right = GameEngine.CreateStandard(new GameOptions { Seed = 99 }).SerializeState(revealAll: true);
    Equal(left, right);
}

static string EventSignature(EventEnvelope eventItem) =>
    JsonSerializer.Serialize(new
    {
        eventItem.Id,
        eventItem.ParentId,
        eventItem.Sequence,
        eventItem.Revision,
        eventItem.CorrelationId,
        Payload = JsonSerializer.Serialize(eventItem.Payload, eventItem.Payload.GetType())
    });

static void CommandJournalReplay()
{
    var options = new GameOptions
    {
        Seed = 808,
        HumanSeat = 0,
        HumanRole = Role.Lord,
        UseInteractiveSetup = false,
        MaxTurns = 120
    };
    var original = GameEngine.CreateStandard(options, StandardContentRegistry.Create());
    var started = original.Submit(new StartGameCommand(original.Revision));
    TrueWithMessage(started.Accepted, "replay start accepted");
    var endedPlay = original.Submit(new EndPlayPhaseCommand(0, original.Revision));
    TrueWithMessage(endedPlay.Accepted, "replay end-play accepted");

    var journal = original.AcceptedCommands;
    Equal(2, journal.Count);
    var json = CommandJson.Serialize(journal);
    TrueWithMessage(json.Contains("\"$type\": \"start\"", StringComparison.Ordinal), "journal has command discriminator");
    var decoded = CommandJson.Deserialize(json);
    Equal(journal.Count, decoded.Count);

    var replayed = GameReplay.Replay(
        options,
        decoded,
        StandardContentRegistry.Create());
    Equal(SnapshotJson.Serialize(original.CreateSnapshot(0, revealAll: true)),
        SnapshotJson.Serialize(replayed.CreateSnapshot(0, revealAll: true)));
    TrueWithMessage(original.CardMovements.SequenceEqual(replayed.CardMovements), "replay preserves card movements");
    TrueWithMessage(
        original.Events.Select(EventSignature).SequenceEqual(replayed.Events.Select(EventSignature)),
        "replay preserves typed events");
    TrueWithMessage(
        original.AcceptedCommands.SequenceEqual(replayed.AcceptedCommands),
        "replay preserves accepted command journal");
}

static void InitialDealCardZones()
{
    var game = GameEngine.CreateStandard(new GameOptions { Seed = 211 });
    var cards = game.CreateCardZoneDiagnostics();

    Equal(78, cards.Count);
    Equal(78, cards.Select(card => card.CardId).Distinct().Count());
    Equal(46, cards.Count(card => card.Location == CardLocation.DrawPile));
    Equal(0, cards.Count(card => card.Location == CardLocation.Processing));
    Equal(0, cards.Count(card => card.Location == CardLocation.DiscardPile));

    for (var seat = 0; seat < 8; seat++)
    {
        Equal(4, cards.Count(card => card.Location == CardLocation.Hand(seat)));
    }

    Equal(32, game.CardMovements.Count);
    for (var index = 0; index < game.CardMovements.Count; index++)
    {
        var movement = game.CardMovements[index];
        Equal(index + 1, movement.Sequence);
        Equal(CardLocation.DrawPile, movement.From);
        Equal(CardLocation.Hand(index % 8), movement.To);
        Equal(CardMoveReasons.InitialDeal, movement.Reason);
    }
}

static void InvalidCardMovesAreAtomic()
{
    var store = new CardZoneStore(playerCount: 2);
    store.LoadInitialDeck(
    [
        new Card(1, CardKind.Slash, Suit.Spade, 7),
        new Card(2, CardKind.Dodge, Suit.Heart, 2),
        new Card(3, CardKind.Peach, Suit.Diamond, 3)
    ]);

    var beforeInvalidTarget = store.CreateDiagnostics();
    Throws<ArgumentOutOfRangeException>(() =>
        store.Move(1, CardLocation.DrawPile, CardLocation.Hand(99)));
    True(beforeInvalidTarget.SequenceEqual(store.CreateDiagnostics()));

    store.Move(1, CardLocation.DrawPile, CardLocation.Hand(0));
    var beforeInvalidBatch = store.CreateDiagnostics();
    Throws<InvalidOperationException>(() =>
        store.MoveMany([2, 1], CardLocation.DrawPile, CardLocation.Hand(1)));
    True(beforeInvalidBatch.SequenceEqual(store.CreateDiagnostics()));
    True(store.CardsAt(CardLocation.DrawPile) is not List<Card>);
    store.AssertInvariants(expectedCardCount: 3);
}

static void SlashAndDodgeProcessing()
{
    GameEngine? selectedGame = null;
    for (var seed = 1; seed <= 128 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 150
        });
        var result = game.Start();
        if (HasSkill(game, SkillKind.Yuanhu))
        {
            continue;
        }

        var decisions = 0;
        while (result.Status != EngineStatus.Completed && decisions++ < 400)
        {
            if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                var processingCards = game.CreateCardZoneDiagnostics()
                    .Where(card => card.Location == CardLocation.Processing)
                    .ToArray();
                if (result.PendingDecision is
                    { Kind: DecisionKind.RespondDodge, IncomingCard: CardKind.Slash } &&
                    processingCards.Length == 1 &&
                    processingCards[0].CardKind == CardKind.Slash)
                {
                    selectedGame = game;
                    break;
                }

                result = result.PendingDecision?.Kind == DecisionKind.RespondDodge
                    ? game.HumanRespond(useDodge: false)
                    : game.HumanRespondSlash(useSlash: false);
                continue;
            }

            if (result.Status == EngineStatus.AwaitingHumanDying)
            {
                result = game.HumanRespondDying(usePeach: false);
                continue;
            }

            if (result.Status == EngineStatus.AwaitingHumanCardSelection)
            {
                result = ResolveFirstHarvestChoice(game);
                continue;
            }

            result = result.Status == EngineStatus.AwaitingHumanPlay
                ? game.HumanEndPlay()
                : game.AdvanceOneStep();
        }
    }

    NotNull(selectedGame);
    var pendingGame = selectedGame!;
    var before = pendingGame.State;
    Equal(1, before.ProcessingCardCount);
    var slash = pendingGame.CreateCardZoneDiagnostics()
        .Single(card => card.Location == CardLocation.Processing);
    Equal(CardKind.Slash, slash.CardKind);

    var dodgeId = before.Players.Single(player => player.Seat == 0).Hand
        .First(card => card.Kind == CardKind.Dodge).Id;
    var after = pendingGame.HumanRespond(useDodge: true, advanceToHumanBoundary: false);
    Equal(0, after.State.ProcessingCardCount);
    AssertCardInventory(pendingGame);

    True(pendingGame.CardMovements.Any(movement =>
        movement.CardId == slash.CardId &&
        movement.From.Zone == CardZoneKind.Hand &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use));
    True(pendingGame.CardMovements.Any(movement =>
        movement.CardId == slash.CardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished));
    True(pendingGame.CardMovements.Any(movement =>
        movement.CardId == dodgeId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Respond));
    True(pendingGame.CardMovements.Any(movement =>
        movement.CardId == dodgeId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.ResponseFinished));
}

static void CardInventoryConservation()
{
    for (var seed = 1; seed <= 16; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 100
        });
        game.StateChanged += AssertPublishedCardTotal;

        var result = game.Start();
        AssertCardInventory(game);
        var steps = 0;
        while (result.Status != EngineStatus.Completed && steps++ < 800)
        {
            if (result.Status == EngineStatus.AwaitingHumanPlay)
            {
                var action = game.GetHumanLegalActions()
                    .FirstOrDefault(candidate => candidate.Kind != LegalActionKind.EndPlay);
                result = action is null
                    ? game.HumanEndPlay(advanceToHumanBoundary: false)
                    : game.HumanPlay(action.CardId!.Value, action.TargetSeat, advanceToHumanBoundary: false);
            }
            else if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                result = game.PendingDecision?.Kind == DecisionKind.RespondSlash
                    ? game.HumanRespondSlash(useSlash: true, advanceToHumanBoundary: false)
                    : game.HumanRespond(useDodge: true, advanceToHumanBoundary: false);
            }
            else if (result.Status == EngineStatus.AwaitingHumanDying)
            {
                result = game.HumanRespondDying(usePeach: false, advanceToHumanBoundary: false);
            }
            else if (result.Status == EngineStatus.AwaitingHumanCardSelection)
            {
                result = ResolveFirstHarvestChoice(game);
            }
            else
            {
                result = game.AdvanceOneStep();
            }

            AssertCardInventory(game);
        }

        TrueWithMessage(steps < 1_200, $"seed {seed} exceeded the inventory loop guard at {steps} steps");
    }
}

static void ObserversRunPostCommit()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        Seed = 331,
        HumanSeat = 0,
        HumanRole = Role.Lord
    });
    var observedStatuses = new List<EngineStatus>();
    var publishedSnapshots = new List<GameSnapshot>();
    game.LogAdded += _ => observedStatuses.Add(game.State.Status);
    game.CardMoved += _ => observedStatuses.Add(game.State.Status);
    game.StateChanged += snapshot => publishedSnapshots.Add(snapshot);

    var result = game.Start();

    Equal(EngineStatus.AwaitingHumanPlay, result.Status);
    True(observedStatuses.Count > 0);
    True(observedStatuses.All(status => status == EngineStatus.AwaitingHumanPlay));
    Equal(1, publishedSnapshots.Count);
    Equal(SnapshotJson.Serialize(result.State), SnapshotJson.Serialize(publishedSnapshots[0]));
}

static void TypedEventStream()
{
    var options = new GameOptions
    {
        Seed = 929,
        HumanSeat = 0,
        HumanRole = Role.Lord,
        MaxTurns = 80
    };
    var left = GameEngine.CreateStandard(options);
    var right = GameEngine.CreateStandard(options);
    var observedStatuses = new List<EngineStatus>();
    left.EventCommitted += _ => observedStatuses.Add(left.State.Status);

    var leftResult = left.Start();
    var rightResult = right.Start();

    True(left.Events.Count > 0);
    True(left.Events[0].Payload is GameStartedEvent started && started.PlayerCount == 8);
    Equal(left.Events.Count, left.Events.Select(eventItem => eventItem.Id).Distinct().Count());
    True(left.Events.Select(eventItem => eventItem.Sequence)
        .SequenceEqual(Enumerable.Range(1, left.Events.Count).Select(value => (long)value)));
    True(left.Events.All(eventItem => eventItem.Revision == left.Revision));
    True(left.Events.Any(eventItem => eventItem.Payload is CardMovedEvent));
    True(left.Events.Any(eventItem => eventItem.Payload is TurnStartedEvent));
    True(observedStatuses.Count > 0);
    True(observedStatuses.All(status => status == leftResult.Status));
    True(!left.SerializeState().Contains("Events", StringComparison.Ordinal));

    var leftSignature = left.Events.Select(eventItem =>
        $"{eventItem.Sequence}|{eventItem.Payload.GetType().Name}|{eventItem.Payload}");
    var rightSignature = right.Events.Select(eventItem =>
        $"{eventItem.Sequence}|{eventItem.Payload.GetType().Name}|{eventItem.Payload}");
    True(leftSignature.SequenceEqual(rightSignature));
    Equal(leftResult.Revision, rightResult.Revision);
}

static void ResolutionFrameStack()
{
    GameEngine? selectedGame = null;
    EngineRunResult result = null!;
    for (var seed = 1; seed <= 128 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 150
        });
        result = game.Start();
        if (HasSkill(game, SkillKind.Yuanhu))
        {
            continue;
        }

        var steps = 0;
        while (result.Status != EngineStatus.Completed && steps++ < 500)
        {
            if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                var processingCards = game.CreateCardZoneDiagnostics()
                    .Where(card => card.Location == CardLocation.Processing)
                    .ToArray();
                if (result.PendingDecision is
                    { Kind: DecisionKind.RespondDodge, IncomingCard: CardKind.Slash } &&
                    processingCards.Length == 1 &&
                    processingCards[0].CardKind == CardKind.Slash)
                {
                    selectedGame = game;
                    break;
                }

                result = result.PendingDecision?.Kind == DecisionKind.RespondDodge
                    ? game.HumanRespond(useDodge: false)
                    : game.HumanRespondSlash(useSlash: false);
                continue;
            }

            result = result.Status switch
            {
                EngineStatus.AwaitingHumanPlay => game.HumanEndPlay(),
                EngineStatus.AwaitingHumanDying => game.HumanRespondDying(usePeach: false),
                EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
                _ => game.AdvanceOneStep()
            };
        }
    }

    NotNull(selectedGame);
    var gameWithResolution = selectedGame!;
    Equal(2, gameWithResolution.ResolutionStack.Count);
    var originalResolutionId = gameWithResolution.ResolutionStack[0].Id;
    True(gameWithResolution.ResolutionStack[0] is CardUseFrame cardUse &&
         cardUse.Step == ResolutionFrameStep.AwaitingResponse);
    True(gameWithResolution.ResolutionStack[1] is ResponseWindowFrame responseWindow &&
         responseWindow.ParentFrameId == gameWithResolution.ResolutionStack[0].Id);
    var serializedStack = JsonSerializer.Serialize(gameWithResolution.ResolutionStack);
    True(serializedStack.Contains("response-window", StringComparison.Ordinal));
    True(!gameWithResolution.SerializeState().Contains("ResolutionStack", StringComparison.Ordinal));

    var prompt = gameWithResolution.PendingDecision!;
    var decline = prompt.Choices.Single(choice => choice.Parameters["response"] == "take-damage");
    var accepted = gameWithResolution.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        decline.Id,
        gameWithResolution.Revision));
    True(accepted.Accepted);
    True(gameWithResolution.ResolutionStack.All(frame => frame.Id != originalResolutionId));
    True(gameWithResolution.Events.Any(eventItem => eventItem.Payload is CardUseDeclaredEvent));
    True(gameWithResolution.Events.Any(eventItem => eventItem.Payload is DamageRequestedEvent));
    True(gameWithResolution.Events.Any(eventItem => eventItem.Payload is AfterDamageEvent));
    True(gameWithResolution.Events.Any(eventItem => eventItem.Payload is CardUseFinishedEvent));
}

static void DuelResponseFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? duelPrompt = null;
    for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 220
        });
        var result = game.Start();
        var steps = 0;
        while (result.Status != EngineStatus.Completed && steps++ < 2_500)
        {
            if (result.Status == EngineStatus.AwaitingHumanPlay)
            {
                var duel = game.GetHumanLegalActions()
                    .FirstOrDefault(action => action.Kind == LegalActionKind.Duel);
                result = duel is null
                    ? game.HumanEndPlay(advanceToHumanBoundary: false)
                    : game.HumanPlay(duel.CardId!.Value, duel.TargetSeat, advanceToHumanBoundary: false);
                continue;
            }

            if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                if (game.PendingDecision is
                    { Kind: DecisionKind.RespondSlash, IncomingCard: CardKind.Duel })
                {
                    selectedGame = game;
                    duelPrompt = game.PendingDecision;
                    break;
                }

                result = game.HumanRespond(useDodge: false, advanceToHumanBoundary: false);
                continue;
            }

            result = result.Status == EngineStatus.AwaitingHumanDying
                ? game.HumanRespondDying(usePeach: false, advanceToHumanBoundary: false)
                : result.Status == EngineStatus.AwaitingHumanCardSelection
                    ? ResolveFirstHarvestChoice(game)
                : game.AdvanceOneStep();
        }
    }

    if (selectedGame is null || duelPrompt is null)
    {
        throw new InvalidOperationException("No deterministic Duel Slash-response prompt was found.");
    }

    var gameWithDuel = selectedGame!;
    var prompt = duelPrompt!;
    Equal(DecisionKind.RespondSlash, prompt.Kind);
    Equal(CardKind.Duel, prompt.IncomingCard);
    Equal(CardKind.Slash, prompt.RequiredCardKind);
    TrueWithMessage(prompt.Choices.Any(choice => choice.Parameters["response"] == "slash"), "duel Slash choice");
    TrueWithMessage(gameWithDuel.ResolutionStack[^1] is ResponseWindowFrame response &&
         response.IncomingCard == CardKind.Duel &&
         response.RequiredCardKind == CardKind.Slash, "duel response frame");
    var duelFrame = gameWithDuel.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.CardKind == CardKind.Duel);
    var otherViewer = gameWithDuel.CreateSnapshot(1);
    Equal<PendingDecision?>(null, otherViewer.PendingDecision);

    var beforeInvalid = gameWithDuel.SerializeState();
    var invalid = gameWithDuel.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        new ChoiceId("respond.slash.fake"),
        gameWithDuel.Revision));
    TrueWithMessage(!invalid.Accepted, "invalid Duel response rejected");
    Equal(CommandErrorCode.InvalidChoice, invalid.Error!.Code);
    Equal(beforeInvalid, gameWithDuel.SerializeState());

    var slashChoice = prompt.Choices.First(choice => choice.Parameters["response"] == "slash");
    var accepted = gameWithDuel.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        slashChoice.Id,
        gameWithDuel.Revision));
    TrueWithMessage(accepted.Accepted, "Duel Slash response accepted");
    TrueWithMessage(gameWithDuel.Events.Any(eventItem =>
        eventItem.Payload is DuelResponseEvent response &&
        response.ResolutionId == duelFrame.Id &&
        response.ResponderSeat == 0 &&
        response.UsedSlash), "Duel Slash response event");

    var resultAfterResponse = accepted.Result;
    var stepsAfterResponse = 0;
    while (!gameWithDuel.Events.Any(eventItem =>
               eventItem.Payload is CardUseFinishedEvent finished &&
               finished.ResolutionId == duelFrame.Id) &&
           resultAfterResponse.Status != EngineStatus.Completed &&
           stepsAfterResponse++ < 2_500)
    {
        if (resultAfterResponse.Status == EngineStatus.AwaitingHumanPlay)
        {
            resultAfterResponse = gameWithDuel.HumanEndPlay(advanceToHumanBoundary: false);
        }
        else if (resultAfterResponse.Status == EngineStatus.AwaitingHumanResponse)
        {
            resultAfterResponse = gameWithDuel.PendingDecision?.Kind == DecisionKind.RespondSlash
                ? gameWithDuel.HumanRespondSlash(useSlash: false, advanceToHumanBoundary: false)
                : gameWithDuel.HumanRespond(useDodge: false, advanceToHumanBoundary: false);
        }
        else if (resultAfterResponse.Status == EngineStatus.AwaitingHumanDying)
        {
            resultAfterResponse = gameWithDuel.HumanRespondDying(usePeach: false, advanceToHumanBoundary: false);
        }
        else if (resultAfterResponse.Status == EngineStatus.AwaitingHumanCardSelection)
        {
            resultAfterResponse = ResolveFirstHarvestChoice(gameWithDuel);
        }
        else
        {
            resultAfterResponse = gameWithDuel.AdvanceOneStep();
        }
    }

    TrueWithMessage(gameWithDuel.Events.Any(eventItem =>
        eventItem.Payload is DuelResponseEvent response &&
        response.ResolutionId == duelFrame.Id &&
        !response.UsedSlash), "Duel pass event");
    TrueWithMessage(gameWithDuel.Events.Any(eventItem =>
        eventItem.Payload is DamageRequestedEvent damage &&
        damage.ResolutionId != 0 &&
        damage.SourceCard == CardKind.Duel), "Duel damage event");
    TrueWithMessage(gameWithDuel.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.ResolutionId == duelFrame.Id &&
        finished.CardKind == CardKind.Duel), "Duel finished event");
    TrueWithMessage(
        gameWithDuel.ResolutionStack.All(frame => frame.Id != duelFrame.Id),
        $"Duel frame completed (count={gameWithDuel.ResolutionStack.Count}, status={gameWithDuel.State.Status}, pending={gameWithDuel.PendingDecision?.Kind})");
    Equal(
        CardLocation.DiscardPile,
        gameWithDuel.CreateCardZoneDiagnostics()
            .Single(card => card.CardId == duelFrame.CardId)
            .Location);
}

static void DrawTwoFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedAction = null;
    for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord
        });
        var result = game.Start();
        var human = game.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0);
        var action = game.GetHumanLegalActions()
            .FirstOrDefault(candidate => candidate.Kind == LegalActionKind.DrawTwo);
        if (result.Status == EngineStatus.AwaitingHumanPlay &&
            human.Skill != SkillKind.Wusheng &&
            action is not null)
        {
            selectedGame = game;
            selectedAction = action;
        }
    }

    if (selectedGame is null || selectedAction is null)
    {
        throw new InvalidOperationException("No deterministic DrawTwo opening hand was found.");
    }

    var gameWithDrawTwo = selectedGame!;
    var drawTwoAction = selectedAction!;
    var prompt = gameWithDrawTwo.PendingDecision!;
    var drawTwoChoice = prompt.Choices.Single(choice =>
        choice.Cards.Count == 1 &&
        choice.Cards[0] == drawTwoAction.CardId &&
        choice.Parameters.GetValueOrDefault("action") == "draw-two");
    Equal("draw-two", drawTwoChoice.Parameters["action"]);
    Equal(0, drawTwoChoice.Targets.Count);

    var drawTwoCardId = drawTwoAction.CardId!.Value;
    var before = gameWithDrawTwo.State;
    var drawMovementCountBefore = gameWithDrawTwo.CardMovements.Count(movement =>
        movement.From == CardLocation.DrawPile &&
        movement.To == CardLocation.Hand(0) &&
        movement.Reason == CardMoveReasons.Draw);
    var beforeSerialized = gameWithDrawTwo.SerializeState();
    var invalidTarget = gameWithDrawTwo.Submit(new PlayCardCommand(
        ActorSeat: 0,
        CardId: drawTwoCardId,
        TargetSeats: [1],
        ExpectedRevision: gameWithDrawTwo.Revision,
        PromptId: prompt.PromptId));
    False(invalidTarget.Accepted);
    Equal(CommandErrorCode.InvalidTarget, invalidTarget.Error!.Code);
    Equal(beforeSerialized, gameWithDrawTwo.SerializeState());

    var accepted = gameWithDrawTwo.Submit(new PlayCardCommand(
        ActorSeat: 0,
        CardId: drawTwoCardId,
        TargetSeats: [],
        ExpectedRevision: gameWithDrawTwo.Revision,
        PromptId: prompt.PromptId));
    True(accepted.Accepted);
    Equal(EngineStatus.AwaitingHumanPlay, accepted.Status);
    Equal(before.Players.Single(player => player.Seat == 0).HandCount + 1,
        accepted.State.Players.Single(player => player.Seat == 0).HandCount);
    Equal(before.DrawPileCount - 2, accepted.State.DrawPileCount);
    Equal(before.DiscardPileCount + 1, accepted.State.DiscardPileCount);
    Equal(0, accepted.State.ProcessingCardCount);
    Equal(0, gameWithDrawTwo.ResolutionStack.Count);

    var declared = gameWithDrawTwo.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUseDeclaredEvent>()
        .Single(eventItem => eventItem.CardId == drawTwoCardId);
    Equal(CardKind.DrawTwo, declared.CardKind);
    var targets = gameWithDrawTwo.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<TargetsConfirmedEvent>()
        .Single(eventItem => eventItem.ResolutionId == declared.ResolutionId);
    Equal(0, targets.TargetSeats.Count);
    TrueWithMessage(gameWithDrawTwo.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.ResolutionId == declared.ResolutionId &&
        finished.CardId == drawTwoCardId &&
        finished.CardKind == CardKind.DrawTwo), "DrawTwo finished event");

    var drawMovements = gameWithDrawTwo.CardMovements
        .Where(movement =>
            movement.From == CardLocation.DrawPile &&
            movement.To == CardLocation.Hand(0) &&
            movement.Reason == CardMoveReasons.Draw)
        .ToArray();
    Equal(2, drawMovements.Length - drawMovementCountBefore);
    TrueWithMessage(gameWithDrawTwo.CardMovements.Any(movement =>
        movement.CardId == drawTwoCardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use), "DrawTwo enters Processing");
    TrueWithMessage(gameWithDrawTwo.CardMovements.Any(movement =>
        movement.CardId == drawTwoCardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished), "DrawTwo leaves Processing");
    TrueWithMessage(gameWithDrawTwo.Log.Any(entry =>
        entry.Type == "CardEffect" && entry.Message.Contains("无中生有", StringComparison.Ordinal)),
        "DrawTwo effect log");

    var otherViewer = gameWithDrawTwo.CreateSnapshot(1);
    Equal(0, otherViewer.Players.Single(player => player.Seat == 0).Hand.Count);
    AssertCardInventory(gameWithDrawTwo);
}

static void BarbarianAssaultFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedAction = null;
    CardUseFrame? groupFrame = null;
    for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var result = game.Start();
        var action = game.GetHumanLegalActions()
            .FirstOrDefault(candidate => candidate.Kind == LegalActionKind.BarbarianAssault);
        if (result.Status != EngineStatus.AwaitingHumanPlay || action is null)
        {
            continue;
        }

        result = game.HumanPlay(action.CardId!.Value, null, advanceToHumanBoundary: false);
        var currentFrame = game.ResolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(frame => frame.CardKind == CardKind.BarbarianAssault);
        var response = game.ResolutionStack.OfType<ResponseWindowFrame>()
            .SingleOrDefault(frame => frame.IncomingCard == CardKind.BarbarianAssault);
        if (currentFrame is not null &&
            response is not null &&
            currentFrame.TargetIndex == 0 &&
            response.ResponderSeat == currentFrame.TargetSeats[0])
        {
            selectedGame = game;
            selectedAction = action;
            groupFrame = currentFrame;
        }
    }

    if (selectedGame is null || selectedAction is null || groupFrame is null)
    {
        throw new InvalidOperationException("No deterministic BarbarianAssault response window was found.");
    }

    var gameWithAssault = selectedGame!;
    var frame = groupFrame!;
    Equal(7, frame.TargetSeats.Count);
    Equal(0, frame.TargetIndex);
    Equal(1, gameWithAssault.State.ProcessingCardCount);
    Equal(
        CardLocation.Processing,
        gameWithAssault.CreateCardZoneDiagnostics()
            .Single(card => card.CardId == frame.CardId)
            .Location);

    var firstTarget = frame.TargetSeats[0];
    var privateTargetView = gameWithAssault.CreateSnapshot(firstTarget);
    Equal(DecisionKind.RespondSlash, privateTargetView.PendingDecision!.Kind);
    Equal(CardKind.BarbarianAssault, privateTargetView.PendingDecision.IncomingCard);
    Equal(CardKind.Slash, privateTargetView.PendingDecision.RequiredCardKind);
    True(privateTargetView.PendingDecision.ValidCardIds.Count > 0);
    Equal<PendingDecision?>(null, gameWithAssault.State.PendingDecision);
    Equal<PendingDecision?>(null, gameWithAssault.CreateSnapshot(1 == firstTarget ? 2 : 1).PendingDecision);

    True(gameWithAssault.Events.Any(eventItem =>
        eventItem.Payload is GroupCardUsedEvent used &&
        used.ResolutionId == frame.Id &&
        used.CardKind == CardKind.BarbarianAssault &&
        used.TargetSeats.SequenceEqual(frame.TargetSeats)));

    var steps = 0;
    while (!gameWithAssault.Events.Any(eventItem =>
               eventItem.Payload is CardUseFinishedEvent finished &&
               finished.ResolutionId == frame.Id) &&
           steps++ < 500)
    {
        gameWithAssault.AdvanceOneStep();
    }

    TrueWithMessage(
        gameWithAssault.Events.Any(eventItem =>
            eventItem.Payload is CardUseFinishedEvent finished &&
            finished.ResolutionId == frame.Id &&
            finished.CardKind == CardKind.BarbarianAssault),
        "BarbarianAssault finished event");
    var responses = gameWithAssault.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<GroupResponseEvent>()
        .Where(response => response.ResolutionId == frame.Id)
        .ToArray();
    Equal(frame.TargetSeats.Count, responses.Length);
    True(responses.Select(response => response.ResponderSeat)
        .SequenceEqual(frame.TargetSeats));
    True(responses.All(response => response.IncomingCard == CardKind.BarbarianAssault));
    True(gameWithAssault.Events.Any(eventItem =>
        eventItem.Payload is DamageRequestedEvent damage &&
        damage.SourceCard == CardKind.BarbarianAssault));
    Equal(0, gameWithAssault.ResolutionStack.Count);
    var damageCardClaims = gameWithAssault.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageCardClaimedEvent>()
        .Where(claim => claim.CardId == frame.CardId)
        .ToArray();
    True(damageCardClaims.Length <= 1);
    Equal(
        damageCardClaims.SingleOrDefault() is { } claim
            ? CardLocation.Hand(claim.OwnerSeat)
            : CardLocation.DiscardPile,
        gameWithAssault.CreateCardZoneDiagnostics()
            .Single(card => card.CardId == frame.CardId)
            .Location);
    AssertCardInventory(gameWithAssault);
}

static void ArrowBarrageFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedAction = null;
    CardUseFrame? groupFrame = null;
    for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var result = game.Start();
        var action = game.GetHumanLegalActions()
            .FirstOrDefault(candidate => candidate.Kind == LegalActionKind.ArrowBarrage);
        if (result.Status != EngineStatus.AwaitingHumanPlay || action is null)
        {
            continue;
        }

        result = game.HumanPlay(action.CardId!.Value, null, advanceToHumanBoundary: false);
        var currentFrame = game.ResolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(frame => frame.CardKind == CardKind.ArrowBarrage);
        var responseFrame = game.ResolutionStack.OfType<ResponseWindowFrame>()
            .SingleOrDefault(frame => frame.IncomingCard == CardKind.ArrowBarrage);
        var hasGuaranteedDamageTarget = currentFrame is not null && currentFrame.TargetSeats.Any(targetSeat =>
            !game.CreateSnapshot(targetSeat).Players.Single(player => player.Seat == targetSeat).Hand
                .Any(card => card.Kind == CardKind.Dodge));
        var hasNoFeedbackTarget = currentFrame is not null && currentFrame.TargetSeats.All(targetSeat =>
            game.CreateSnapshot(targetSeat, revealAll: true).Players
                .Single(player => player.Seat == targetSeat).Skill != SkillKind.Feedback);
        if (currentFrame is not null && responseFrame is not null &&
            hasGuaranteedDamageTarget && hasNoFeedbackTarget)
        {
            selectedGame = game;
            selectedAction = action;
            groupFrame = currentFrame;
        }
    }

    if (selectedGame is null || selectedAction is null || groupFrame is null)
    {
        throw new InvalidOperationException("No deterministic ArrowBarrage response window was found.");
    }

    var gameWithBarrage = selectedGame!;
    var frame = groupFrame!;
    Equal(7, frame.TargetSeats.Count);
    True(frame.TargetIndex >= 0 && frame.TargetIndex < frame.TargetSeats.Count);
    Equal(1, gameWithBarrage.State.ProcessingCardCount);
    Equal(
        CardLocation.Processing,
        gameWithBarrage.CreateCardZoneDiagnostics()
            .Single(card => card.CardId == frame.CardId)
            .Location);

    var firstTarget = frame.TargetSeats[frame.TargetIndex];
    var privateTargetView = gameWithBarrage.CreateSnapshot(firstTarget);
    Equal(DecisionKind.RespondDodge, privateTargetView.PendingDecision!.Kind);
    Equal(CardKind.ArrowBarrage, privateTargetView.PendingDecision.IncomingCard);
    Equal(CardKind.Dodge, privateTargetView.PendingDecision.RequiredCardKind);
    True(privateTargetView.PendingDecision.ValidCardIds.Count > 0);
    Equal<PendingDecision?>(null, gameWithBarrage.State.PendingDecision);
    var otherViewerSeat = Enumerable.Range(0, gameWithBarrage.PlayerCount)
        .First(seat => seat != firstTarget);
    Equal<PendingDecision?>(null, gameWithBarrage.CreateSnapshot(otherViewerSeat).PendingDecision);

    True(gameWithBarrage.Events.Any(eventItem =>
        eventItem.Payload is GroupCardUsedEvent used &&
        used.ResolutionId == frame.Id &&
        used.CardKind == CardKind.ArrowBarrage &&
        used.TargetSeats.SequenceEqual(frame.TargetSeats)));
    TrueWithMessage(
        gameWithBarrage.ResolutionStack[^1] is ResponseWindowFrame response &&
        response.IncomingCard == CardKind.ArrowBarrage &&
        response.RequiredCardKind == CardKind.Dodge,
        "ArrowBarrage Dodge response frame");

    var steps = 0;
    while (!gameWithBarrage.Events.Any(eventItem =>
               eventItem.Payload is CardUseFinishedEvent finished &&
               finished.ResolutionId == frame.Id) &&
           steps++ < 500)
    {
        gameWithBarrage.AdvanceOneStep();
    }

    TrueWithMessage(
        gameWithBarrage.Events.Any(eventItem =>
            eventItem.Payload is CardUseFinishedEvent finished &&
            finished.ResolutionId == frame.Id &&
            finished.CardKind == CardKind.ArrowBarrage),
        "ArrowBarrage finished event");
    var responses = gameWithBarrage.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<GroupResponseEvent>()
        .Where(response => response.ResolutionId == frame.Id)
        .ToArray();
    Equal(frame.TargetSeats.Count, responses.Length);
    True(responses.Select(response => response.ResponderSeat)
        .SequenceEqual(frame.TargetSeats));
    True(responses.All(response =>
        response.IncomingCard == CardKind.ArrowBarrage &&
        response.RequiredCardKind == CardKind.Dodge));
    True(gameWithBarrage.Events.Any(eventItem =>
        eventItem.Payload is DamageRequestedEvent damage &&
        damage.SourceCard == CardKind.ArrowBarrage));
    TrueWithMessage(
        gameWithBarrage.ResolutionStack.Count == 0,
        $"ArrowBarrage resolution stack should be empty, got {gameWithBarrage.ResolutionStack.Count}: {string.Join(", ", gameWithBarrage.ResolutionStack.Select(frame => frame.Kind))}");
    Equal(
        CardLocation.DiscardPile,
        gameWithBarrage.CreateCardZoneDiagnostics()
            .Single(card => card.CardId == frame.CardId)
            .Location);
    AssertCardInventory(gameWithBarrage);
}

static void PeachGardenFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedGarden = null;
    CardUseFrame? groupFrame = null;
    var injuredSeat = -1;

    for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var result = game.Start();
        var garden = game.GetHumanLegalActions()
            .FirstOrDefault(candidate => candidate.Kind == LegalActionKind.PeachGarden);
        var slash = game.GetHumanLegalActions()
            .FirstOrDefault(candidate =>
                candidate.Kind == LegalActionKind.Slash &&
                candidate.TargetSeat is { } targetSeat &&
                !game.CreateSnapshot(targetSeat).Players.Single(player => player.Seat == targetSeat).Hand
                    .Any(card => card.Kind == CardKind.Dodge));
        if (result.Status != EngineStatus.AwaitingHumanPlay || garden is null || slash is null)
        {
            continue;
        }

        var targetSeatForTest = slash.TargetSeat!.Value;
        game.HumanPlay(slash.CardId!.Value, targetSeatForTest, advanceToHumanBoundary: false);
        var targetAfterSlash = game.State.Players.Single(player => player.Seat == targetSeatForTest);
        if (targetAfterSlash.Hp != targetAfterSlash.MaxHp - 1)
        {
            continue;
        }

        result = game.AdvanceOneStep();
        garden = game.GetHumanLegalActions()
            .FirstOrDefault(candidate => candidate.Kind == LegalActionKind.PeachGarden);
        if (result.Status != EngineStatus.AwaitingHumanPlay || garden is null)
        {
            continue;
        }

        game.HumanPlay(garden.CardId!.Value, null, advanceToHumanBoundary: false);
        var currentFrame = game.ResolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(frame => frame.CardKind == CardKind.PeachGarden);
        if (currentFrame is not null &&
            currentFrame.TargetIndex == 0 &&
            currentFrame.TargetSeats.Count == 8 &&
            game.State.ProcessingCardCount == 1)
        {
            selectedGame = game;
            selectedGarden = garden;
            groupFrame = currentFrame;
            injuredSeat = targetSeatForTest;
        }
    }

    if (selectedGame is null || selectedGarden is null || groupFrame is null || injuredSeat < 0)
    {
        throw new InvalidOperationException("No deterministic PeachGarden recovery boundary was found.");
    }

    var gameWithGarden = selectedGame!;
    var frame = groupFrame!;
    Equal(8, frame.TargetSeats.Count);
    Equal(0, frame.TargetIndex);
    Equal(1, gameWithGarden.State.ProcessingCardCount);
    Equal<PendingDecision?>(null, gameWithGarden.State.PendingDecision);
    Equal<PendingDecision?>(null, gameWithGarden.CreateSnapshot(injuredSeat).PendingDecision);
    True(!gameWithGarden.SerializeState().Contains("ResolutionStack", StringComparison.Ordinal));
    TrueWithMessage(
        gameWithGarden.ResolutionStack[^1] is CardUseFrame cardUse &&
        cardUse.CardKind == CardKind.PeachGarden &&
        cardUse.TargetIndex == 0,
        "PeachGarden card-use frame is paused before the first target");

    var firstStep = gameWithGarden.AdvanceOneStep();
    Equal(EngineStatus.Running, firstStep.Status);
    TrueWithMessage(
        gameWithGarden.ResolutionStack.OfType<CardUseFrame>().Single().TargetIndex == 1,
        "PeachGarden advances one target per engine step");
    Equal(1, gameWithGarden.State.ProcessingCardCount);

    True(gameWithGarden.Events.Any(eventItem =>
        eventItem.Payload is GroupCardUsedEvent used &&
        used.ResolutionId == frame.Id &&
        used.CardKind == CardKind.PeachGarden &&
        used.TargetSeats.SequenceEqual(frame.TargetSeats)));

    var steps = 0;
    while (!gameWithGarden.Events.Any(eventItem =>
               eventItem.Payload is CardUseFinishedEvent finished &&
               finished.ResolutionId == frame.Id) &&
           steps++ < 100)
    {
        gameWithGarden.AdvanceOneStep();
    }

    TrueWithMessage(
        gameWithGarden.Events.Any(eventItem =>
            eventItem.Payload is CardUseFinishedEvent finished &&
            finished.ResolutionId == frame.Id &&
            finished.CardKind == CardKind.PeachGarden),
        "PeachGarden finished event");
    var recovered = gameWithGarden.State.Players.Single(player => player.Seat == injuredSeat);
    Equal(recovered.MaxHp, recovered.Hp);
    TrueWithMessage(
        gameWithGarden.Events.Any(eventItem =>
            eventItem.Payload is RecoveryAppliedEvent recovery &&
            recovery.SourceSeat == 0 &&
            recovery.TargetSeat == injuredSeat &&
            recovery.Amount == 1 &&
            recovery.RemainingHp == recovered.MaxHp),
        "PeachGarden recovery event");
    True(!gameWithGarden.Events.Any(eventItem =>
        eventItem.Payload is DamageRequestedEvent damage &&
        damage.SourceCard == CardKind.PeachGarden));
    True(gameWithGarden.CardMovements.Any(movement =>
        movement.CardId == frame.CardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use));
    True(gameWithGarden.CardMovements.Any(movement =>
        movement.CardId == frame.CardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished));
    Equal(0, gameWithGarden.ResolutionStack.Count);
    Equal(
        CardLocation.DiscardPile,
        gameWithGarden.CreateCardZoneDiagnostics()
            .Single(card => card.CardId == frame.CardId)
            .Location);
    AssertCardInventory(gameWithGarden);
}

static void FiveGrainsFlow()
{
    GameEngine? selectedGame = null;
    CardUseFrame? draftFrame = null;
    EngineRunResult? afterUse = null;
    for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var result = game.Start();
        var action = game.GetHumanLegalActions()
            .FirstOrDefault(candidate => candidate.Kind == LegalActionKind.FiveGrains);
        if (result.Status != EngineStatus.AwaitingHumanPlay || action is null)
        {
            continue;
        }

        result = game.HumanPlay(action.CardId!.Value, null, advanceToHumanBoundary: false);
        var candidateFrame = game.ResolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(candidate => candidate.CardKind == CardKind.FiveGrains);
        if (result.Status == EngineStatus.AwaitingHumanCardSelection &&
            candidateFrame is not null &&
            candidateFrame.TargetSeats.Count == 8 &&
            candidateFrame.TargetIndex == 0 &&
            result.State.PublicRevealedCards.Count == 8)
        {
            selectedGame = game;
            draftFrame = candidateFrame;
            afterUse = result;
        }
    }

    if (selectedGame is null || draftFrame is null || afterUse is null)
    {
        throw new InvalidOperationException("No deterministic FiveGrains public draft boundary was found.");
    }

    var gameWithDraft = selectedGame!;
    var frame = draftFrame!;
    var startedState = afterUse!.State;
    var prompt = gameWithDraft.PendingDecision!;
    Equal(DecisionKind.SelectHarvestCard, prompt.Kind);
    Equal(EngineStatus.AwaitingHumanCardSelection, startedState.Status);
    Equal(0, frame.TargetIndex);
    Equal(9, startedState.ProcessingCardCount);
    Equal(8, startedState.PublicRevealedCards.Count);
    Equal(8, prompt.ValidCardIds.Count);
    True(prompt.ValidCardIds.OrderBy(id => id)
        .SequenceEqual(startedState.PublicRevealedCards.Select(card => card.Id).OrderBy(id => id)));
    Equal(prompt.ValidCardIds.Count, prompt.Choices.Count);
    True(prompt.Choices.All(choice =>
        choice.Parameters.GetValueOrDefault("action") == "harvest-pick" &&
        choice.Cards.Count == 1 &&
        prompt.ValidCardIds.Contains(choice.Cards[0])));

    var otherViewer = gameWithDraft.CreateSnapshot(1);
    Equal<PendingDecision?>(null, otherViewer.PendingDecision);
    Equal(8, otherViewer.PublicRevealedCards.Count);
    True(otherViewer.Players.Single(player => player.Seat == 0).Hand.Count == 0);
    True(startedState.Players.Single(player => player.Seat == 0).Hand
        .Where(card => !prompt.ValidCardIds.Contains(card.Id))
        .All(card => otherViewer.Players.Single(player => player.Seat == 0).Hand.All(hidden => hidden.Id != card.Id)));
    True(gameWithDraft.SerializeState().Contains("PublicRevealedCards", StringComparison.Ordinal));
    True(!gameWithDraft.SerializeState().Contains("ResolutionStack", StringComparison.Ordinal));
    True(gameWithDraft.ResolutionStack[^1] is CardUseFrame cardUse &&
         cardUse.CardKind == CardKind.FiveGrains &&
         cardUse.TargetIndex == 0);
    True(gameWithDraft.Events.Any(eventItem =>
        eventItem.Payload is CardsRevealedEvent revealed &&
        revealed.ResolutionId == frame.Id &&
        revealed.Cards.Count == 8 &&
        revealed.Cards.Select(card => card.Id).SequenceEqual(startedState.PublicRevealedCards.Select(card => card.Id))));
    True(gameWithDraft.CardMovements.Any(movement =>
        movement.From == CardLocation.DrawPile &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Reveal));

    var beforeInvalid = gameWithDraft.SerializeState();
    var invalid = gameWithDraft.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        new ChoiceId("harvest.card-fake"),
        gameWithDraft.Revision));
    False(invalid.Accepted);
    Equal(CommandErrorCode.InvalidChoice, invalid.Error!.Code);
    Equal(beforeInvalid, gameWithDraft.SerializeState());

    var selectedCardId = prompt.Choices[0].Cards.Single();
    var afterHumanPick = gameWithDraft.HumanSelectHarvestCard(
        selectedCardId,
        advanceToHumanBoundary: false);
    Equal(EngineStatus.Running, afterHumanPick.Status);
    Equal<PendingDecision?>(null, afterHumanPick.State.PendingDecision);
    Equal(7, afterHumanPick.State.PublicRevealedCards.Count);
    Equal(8, afterHumanPick.State.ProcessingCardCount);
    Equal(1, afterHumanPick.State.Players.Single(player => player.Seat == 0).Hand.Count(card => card.Id == selectedCardId));
    Equal(DecisionKind.SelectHarvestCard, gameWithDraft.CreateSnapshot(1).PendingDecision!.Kind);
    Equal<PendingDecision?>(null, gameWithDraft.CreateSnapshot(2).PendingDecision);

    var guard = 0;
    while (gameWithDraft.ResolutionStack.OfType<CardUseFrame>()
               .Any(cardUse => cardUse.Id == frame.Id) &&
           guard++ < 32)
    {
        True(gameWithDraft.State.Status == EngineStatus.Running);
        gameWithDraft.AdvanceOneStep();
    }

    TrueWithMessage(guard < 32, "FiveGrains draft completes within one step per picker");
    TrueWithMessage(gameWithDraft.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.ResolutionId == frame.Id &&
        finished.CardKind == CardKind.FiveGrains), "FiveGrains finished event");
    var selections = gameWithDraft.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<HarvestCardSelectedEvent>()
        .Where(selection => selection.ResolutionId == frame.Id)
        .ToArray();
    Equal(frame.TargetSeats.Count, selections.Length);
    True(selections.Select(selection => selection.PlayerSeat).SequenceEqual(frame.TargetSeats));
    True(selections.All(selection =>
        gameWithDraft.CreateCardZoneDiagnostics()
            .Single(card => card.CardId == selection.CardId)
            .Location == CardLocation.Hand(selection.PlayerSeat)));
    True(gameWithDraft.CardMovements.Any(movement =>
        movement.CardId == selectedCardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.Hand(0) &&
        movement.Reason == CardMoveReasons.HarvestPick));
    True(gameWithDraft.CardMovements.Any(movement =>
        movement.CardId == frame.CardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished));
    Equal(0, gameWithDraft.State.PublicRevealedCards.Count);
    Equal(0, gameWithDraft.State.ProcessingCardCount);
    Equal(0, gameWithDraft.ResolutionStack.Count);
    AssertCardInventory(gameWithDraft);
}

static void DismantlementFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedAction = null;
    for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var result = game.Start();
        var action = game.GetHumanLegalActions()
            .FirstOrDefault(candidate => candidate.Kind == LegalActionKind.Dismantlement);
        if (result.Status == EngineStatus.AwaitingHumanPlay &&
            action is { TargetSeat: not null })
        {
            selectedGame = game;
            selectedAction = action;
        }
    }

    if (selectedGame is null || selectedAction is null)
    {
        throw new InvalidOperationException("No deterministic Dismantlement target boundary was found.");
    }

    var gameWithDismantlement = selectedGame!;
    var actionToUse = selectedAction!;
    var targetSeat = actionToUse.TargetSeat!.Value;
    var sourceBefore = gameWithDismantlement.State.Players.Single(player => player.Seat == 0);
    var publicTargetBefore = gameWithDismantlement.State.Players.Single(player => player.Seat == targetSeat);
    var privateTargetBefore = gameWithDismantlement.CreateSnapshot(targetSeat)
        .Players.Single(player => player.Seat == targetSeat);
    True(publicTargetBefore.Hand.Count == 0);
    True(publicTargetBefore.HandCount > 0);
    True(privateTargetBefore.Hand.Count == privateTargetBefore.HandCount);

    var beforeTargetCards = privateTargetBefore.Hand.ToArray();
    var resultAfterUse = gameWithDismantlement.HumanPlay(
        actionToUse.CardId!.Value,
        targetSeat,
        advanceToHumanBoundary: false);

    Equal(EngineStatus.Running, resultAfterUse.Status);
    Equal<PendingDecision?>(null, resultAfterUse.PendingDecision);
    Equal(0, resultAfterUse.State.ProcessingCardCount);
    Equal(0, gameWithDismantlement.ResolutionStack.Count);

    var privateTargetAfter = gameWithDismantlement.CreateSnapshot(targetSeat)
        .Players.Single(player => player.Seat == targetSeat);
    var removedCard = beforeTargetCards
        .Single(card => privateTargetAfter.Hand.All(remaining => remaining.Id != card.Id));
    Equal(beforeTargetCards.Length - 1, privateTargetAfter.Hand.Count);
    Equal(sourceBefore.HandCount - 1, resultAfterUse.State.Players.Single(player => player.Seat == 0).HandCount);

    var resolutionId = gameWithDismantlement.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUseDeclaredEvent>()
        .Single(cardUse => cardUse.CardId == actionToUse.CardId)
        .ResolutionId;
    var discardedEvent = gameWithDismantlement.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<TargetCardDiscardedEvent>()
        .Single(eventItem => eventItem.ResolutionId == resolutionId);
    Equal(0, discardedEvent.SourceSeat);
    Equal(targetSeat, discardedEvent.TargetSeat);
    Equal(CardZoneKind.Hand, discardedEvent.FromZone);

    True(gameWithDismantlement.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.ResolutionId == resolutionId &&
        finished.CardKind == CardKind.Dismantlement));
    True(gameWithDismantlement.CardMovements.Any(movement =>
        movement.CardId == actionToUse.CardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use));
    True(gameWithDismantlement.CardMovements.Any(movement =>
        movement.CardId == actionToUse.CardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished));
    True(gameWithDismantlement.CardMovements.Any(movement =>
        movement.CardId == removedCard.Id &&
        movement.From == CardLocation.Hand(targetSeat) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Dismantlement));
    True(gameWithDismantlement.CardMovements.Any(movement =>
        movement.CardId == removedCard.Id &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.DismantlementFinished));
    True(resultAfterUse.State.Players
        .SelectMany(player => player.Hand)
        .All(card => card.Id != removedCard.Id));
    True(!gameWithDismantlement.Log.Last(entry => entry.Type == "CardEffect")
        .Message.Contains(removedCard.DisplayName, StringComparison.Ordinal));
    AssertCardInventory(gameWithDismantlement);
}

static void SnatchFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedAction = null;
    for (var seed = 1; seed <= 1_024 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var result = game.Start();
        var action = game.GetHumanLegalActions()
            .FirstOrDefault(candidate =>
                candidate.Kind == LegalActionKind.Snatch &&
                candidate.TargetSeat is not null);
        if (result.Status == EngineStatus.AwaitingHumanPlay && action is not null)
        {
            selectedGame = game;
            selectedAction = action;
        }
    }

    if (selectedGame is null || selectedAction is null)
    {
        throw new InvalidOperationException("No deterministic Snatch target boundary was found.");
    }

    var gameWithSnatch = selectedGame!;
    var actionToUse = selectedAction!;
    var targetSeat = actionToUse.TargetSeat!.Value;
    Equal(0, gameWithSnatch.GetSeatDistance(0, 0));
    Equal(1, gameWithSnatch.GetSeatDistance(0, 1));
    Equal(1, gameWithSnatch.GetSeatDistance(0, 7));
    Equal(2, gameWithSnatch.GetSeatDistance(0, 2));
    Equal(4, gameWithSnatch.GetSeatDistance(0, 4));
    Equal(1, gameWithSnatch.GetSeatDistance(0, targetSeat));
    True(gameWithSnatch.GetHumanLegalActions()
        .Where(action => action.Kind == LegalActionKind.Snatch)
        .All(action => action.TargetSeat is { } seat && gameWithSnatch.GetSeatDistance(0, seat) == 1));
    Throws<ArgumentOutOfRangeException>(() => gameWithSnatch.GetSeatDistance(-1, 0));
    Throws<ArgumentOutOfRangeException>(() => gameWithSnatch.GetSeatDistance(0, 8));

    var sourceBefore = gameWithSnatch.State.Players.Single(player => player.Seat == 0);
    var publicTargetBefore = gameWithSnatch.State.Players.Single(player => player.Seat == targetSeat);
    var privateTargetBefore = gameWithSnatch.CreateSnapshot(targetSeat)
        .Players.Single(player => player.Seat == targetSeat);
    True(publicTargetBefore.Hand.Count == 0);
    True(publicTargetBefore.HandCount > 0);
    True(privateTargetBefore.Hand.Count == privateTargetBefore.HandCount);

    var beforeTargetCards = privateTargetBefore.Hand.ToArray();
    var resultAfterUse = gameWithSnatch.HumanPlay(
        actionToUse.CardId!.Value,
        targetSeat,
        advanceToHumanBoundary: false);

    Equal(EngineStatus.Running, resultAfterUse.Status);
    Equal<PendingDecision?>(null, resultAfterUse.PendingDecision);
    Equal(0, resultAfterUse.State.ProcessingCardCount);
    Equal(0, gameWithSnatch.ResolutionStack.Count);

    var privateTargetAfter = gameWithSnatch.CreateSnapshot(targetSeat)
        .Players.Single(player => player.Seat == targetSeat);
    var sourceAfter = gameWithSnatch.State.Players.Single(player => player.Seat == 0);
    var takenCard = beforeTargetCards
        .Single(card => privateTargetAfter.Hand.All(remaining => remaining.Id != card.Id));
    Equal(beforeTargetCards.Length - 1, privateTargetAfter.Hand.Count);
    Equal(sourceBefore.HandCount, sourceAfter.HandCount);
    True(sourceAfter.Hand.Any(card => card.Id == takenCard.Id));

    var resolutionId = gameWithSnatch.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUseDeclaredEvent>()
        .Single(cardUse => cardUse.CardId == actionToUse.CardId)
        .ResolutionId;
    var takenEvent = gameWithSnatch.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<TargetCardTakenEvent>()
        .Single(eventItem => eventItem.ResolutionId == resolutionId);
    Equal(0, takenEvent.SourceSeat);
    Equal(targetSeat, takenEvent.TargetSeat);
    Equal(CardZoneKind.Hand, takenEvent.FromZone);

    True(gameWithSnatch.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.ResolutionId == resolutionId &&
        finished.CardKind == CardKind.Snatch));
    True(gameWithSnatch.CardMovements.Any(movement =>
        movement.CardId == actionToUse.CardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use));
    True(gameWithSnatch.CardMovements.Any(movement =>
        movement.CardId == actionToUse.CardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished));
    True(gameWithSnatch.CardMovements.Any(movement =>
        movement.CardId == takenCard.Id &&
        movement.From == CardLocation.Hand(targetSeat) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Snatch));
    True(gameWithSnatch.CardMovements.Any(movement =>
        movement.CardId == takenCard.Id &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.Hand(0) &&
        movement.Reason == CardMoveReasons.SnatchFinished));

    var ordinaryObserverAfter = gameWithSnatch.CreateSnapshot(2);
    True(ordinaryObserverAfter.Players.Single(player => player.Seat == targetSeat).Hand.Count == 0);
    True(ordinaryObserverAfter.Players.Single(player => player.Seat == 0).Hand.Count == 0);
    True(ordinaryObserverAfter.Players
        .SelectMany(player => player.Hand)
        .All(card => card.Id != takenCard.Id));
    True(gameWithSnatch.State.Players
        .Single(player => player.Seat == 0)
        .Hand.Any(card => card.Id == takenCard.Id));
    True(!gameWithSnatch.Log.Last(entry => entry.Type == "CardEffect")
        .Message.Contains(takenCard.DisplayName, StringComparison.Ordinal));
    AssertCardInventory(gameWithSnatch);
}

static void EquipmentFlow()
{
    Equal(5, EquipmentCatalog.Implemented.Count);
    Equal(EquipmentSlot.Weapon, EquipmentCatalog.Get(CardKind.Crossbow).Slot);
    Equal(EquipmentSlot.Armor, EquipmentCatalog.Get(CardKind.BaguaFormation).Slot);
    Equal(EquipmentSlot.OffensiveHorse, EquipmentCatalog.Get(CardKind.OffensiveHorse).Slot);
    Equal(EquipmentSlot.DefensiveHorse, EquipmentCatalog.Get(CardKind.DefensiveHorse).Slot);
    Equal(EquipmentSlot.Treasure, EquipmentCatalog.Get(CardKind.JadeSeal).Slot);

    GameEngine? selectedGame = null;
    CardSnapshot? firstCrossbow = null;
    CardSnapshot? secondCrossbow = null;
    CardSnapshot? slash = null;
    for (var seed = 1; seed <= 8_192 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var result = game.Start();
        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var hand = game.State.Players.Single(player => player.Seat == 0).Hand;
        var crossbows = hand.Where(card => card.Kind == CardKind.Crossbow).ToArray();
        var slashCard = hand.FirstOrDefault(card => card.Kind == CardKind.Slash);
        if (crossbows.Length >= 2 && slashCard is not null)
        {
            selectedGame = game;
            firstCrossbow = crossbows[0];
            secondCrossbow = crossbows[1];
            slash = slashCard;
        }
    }

    if (selectedGame is null || firstCrossbow is null || secondCrossbow is null || slash is null)
    {
        throw new InvalidOperationException("No deterministic equipment replacement boundary was found.");
    }

    var gameWithEquipment = selectedGame;
    var firstEquipAction = gameWithEquipment.GetHumanLegalActions().Single(action =>
        action.Kind == LegalActionKind.Equip && action.CardId == firstCrossbow.Id);
    True(firstEquipAction.TargetSeat is null);
    var firstResult = gameWithEquipment.HumanPlay(
        firstCrossbow.Id,
        targetSeat: null,
        advanceToHumanBoundary: true);
    Equal(EngineStatus.AwaitingHumanPlay, firstResult.Status);

    var afterFirst = gameWithEquipment.CreateSnapshot(0, revealAll: true);
    var firstEquipment = afterFirst.Players.Single(player => player.Seat == 0).Equipment;
    Equal(1, firstEquipment.Count);
    Equal(firstCrossbow.Id, firstEquipment.Single().Id);
    Equal(2, gameWithEquipment.GetAttackRange(0));
    Equal(2, gameWithEquipment.GetCombatDistance(0, 2));
    True(gameWithEquipment.GetHumanLegalActions().Any(action =>
        action.Kind == LegalActionKind.Slash &&
        action.CardId == slash.Id &&
        action.TargetSeat == 2));
    True(gameWithEquipment.CreateSnapshot(2).Players.Single(player => player.Seat == 0)
        .Equipment.Any(card => card.Id == firstCrossbow.Id));

    var changedEvents = gameWithEquipment.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<EquipmentChangedEvent>()
        .ToArray();
    var firstChanged = changedEvents.Single();
    Equal(0, firstChanged.PlayerSeat);
    Equal(EquipmentSlot.Weapon, firstChanged.Slot);
    Equal(firstCrossbow.Id, firstChanged.CardId);
    Equal<int?>(null, firstChanged.ReplacedCardId);
    True(gameWithEquipment.CardMovements.Any(movement =>
        movement.CardId == firstCrossbow.Id &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.EquipmentUse));
    True(gameWithEquipment.CardMovements.Any(movement =>
        movement.CardId == firstCrossbow.Id &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.Equipment(0) &&
        movement.Reason == CardMoveReasons.EquipmentEnter));

    var secondResult = gameWithEquipment.HumanPlay(
        secondCrossbow.Id,
        targetSeat: null,
        advanceToHumanBoundary: true);
    Equal(EngineStatus.AwaitingHumanPlay, secondResult.Status);
    var afterReplacement = gameWithEquipment.CreateSnapshot(0, revealAll: true);
    var finalEquipment = afterReplacement.Players.Single(player => player.Seat == 0).Equipment;
    Equal(1, finalEquipment.Count);
    Equal(secondCrossbow.Id, finalEquipment.Single().Id);
    var allChangedEvents = gameWithEquipment.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<EquipmentChangedEvent>()
        .ToArray();
    Equal(2, allChangedEvents.Length);
    var replacementEvent = gameWithEquipment.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<EquipmentChangedEvent>()
        .Single(eventItem => eventItem.CardId == secondCrossbow.Id);
    Equal(firstCrossbow.Id, replacementEvent.ReplacedCardId);
    True(gameWithEquipment.CreateCardZoneDiagnostics().Any(card =>
        card.CardId == firstCrossbow.Id && card.Location == CardLocation.DiscardPile));
    True(gameWithEquipment.CardMovements.Any(movement =>
        movement.CardId == firstCrossbow.Id &&
        movement.From == CardLocation.Equipment(0) &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.EquipmentReplace));

    GameEngine? horseGame = null;
    CardSnapshot? horse = null;
    for (var seed = 1; seed <= 2_048 && horseGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var result = game.Start();
        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        horse = game.State.Players.Single(player => player.Seat == 0).Hand
            .FirstOrDefault(card => card.Kind == CardKind.OffensiveHorse);
        if (horse is not null)
        {
            horseGame = game;
        }
    }

    if (horseGame is null || horse is null)
    {
        throw new InvalidOperationException("No deterministic offensive-horse boundary was found.");
    }

    Equal(2, horseGame.GetCombatDistance(0, 2));
    horseGame.HumanPlay(horse.Id, targetSeat: null, advanceToHumanBoundary: true);
    Equal(1, horseGame.GetCombatDistance(0, 2));
    Equal(2, horseGame.GetSeatDistance(0, 2));
    True(horseGame.GetCombatDistance(0, 2) <= horseGame.GetSeatDistance(0, 2));

    AssertCardInventory(gameWithEquipment);
    AssertCardInventory(horseGame);
}

static void FireAttackFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedAction = null;
    PendingDecision? revealPrompt = null;
    PendingDecision? discardPrompt = null;
    CardUseFrame? selectedFrame = null;
    for (var seed = 1; seed <= 8_192 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var result = game.Start();
        if (HasSkill(game, SkillKind.Yuanhu))
        {
            continue;
        }

        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var action = game.GetHumanLegalActions()
            .FirstOrDefault(candidate =>
                candidate.Kind == LegalActionKind.FireAttack &&
                candidate.TargetSeat is not null);
        if (action is null)
        {
            continue;
        }

        var targetSeat = action.TargetSeat!.Value;
        result = game.HumanPlay(action.CardId!.Value, targetSeat, advanceToHumanBoundary: false);
        if (result.Status != EngineStatus.Running || game.PendingDecision is not null)
        {
            continue;
        }

        var targetView = game.CreateSnapshot(targetSeat);
        var candidateRevealPrompt = targetView.PendingDecision;
        if (candidateRevealPrompt is not { Kind: DecisionKind.FireAttackReveal } ||
            candidateRevealPrompt.ValidCardIds.Count == 0)
        {
            continue;
        }

        var sourceViewBeforeReveal = game.CreateSnapshot(0);
        Equal<PendingDecision?>(null, sourceViewBeforeReveal.PendingDecision);
        foreach (var cardId in candidateRevealPrompt.ValidCardIds)
        {
            False(SnapshotJson.Serialize(sourceViewBeforeReveal).Contains(
                $"\"Id\": {cardId},",
                StringComparison.Ordinal));
        }

        var candidateFrame = game.ResolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(frame => frame.CardKind == CardKind.FireAttack);
        if (candidateFrame is null)
        {
            continue;
        }

        result = game.AdvanceOneStep();
        var candidateDiscardPrompt = game.PendingDecision;
        if (result.Status != EngineStatus.AwaitingHumanCardSelection ||
            candidateDiscardPrompt is not { Kind: DecisionKind.FireAttackDiscard, PlayerSeat: 0 } ||
            !candidateDiscardPrompt.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("response") == "fire-attack-discard"))
        {
            continue;
        }

        if (game.State.PublicRevealedCards.Count != 1 ||
            game.State.ProcessingCardCount != 2)
        {
            continue;
        }

        selectedGame = game;
        selectedAction = action;
        revealPrompt = candidateRevealPrompt;
        discardPrompt = candidateDiscardPrompt;
        selectedFrame = candidateFrame;
    }

    if (selectedGame is null || selectedAction is null || revealPrompt is null ||
        discardPrompt is null || selectedFrame is null)
    {
        throw new InvalidOperationException("No deterministic FireAttack damage boundary was found.");
    }

    var gameWithFireAttack = selectedGame!;
    var actionToUse = selectedAction!;
    var frame = selectedFrame!;
    var targetSeatAtBoundary = actionToUse.TargetSeat!.Value;
    var revealedEvent = gameWithFireAttack.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<FireAttackCardRevealedEvent>()
        .Single(eventItem => eventItem.ResolutionId == frame.Id);
    var discardChoice = discardPrompt.Choices.First(choice =>
        choice.Parameters.GetValueOrDefault("response") == "fire-attack-discard");
    var matchingDiscardCardId = discardChoice.Cards.Single();
    var sourceBefore = gameWithFireAttack.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == 0);
    var targetBefore = gameWithFireAttack.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == targetSeatAtBoundary);
    var ordinaryViewer = gameWithFireAttack.CreateSnapshot(1);
    TrueWithMessage(ordinaryViewer.PublicRevealedCards.Count == 1, "FireAttack publishes the revealed card");
    TrueWithMessage(ordinaryViewer.Players.Single(player => player.Seat == targetSeatAtBoundary).Hand
        .All(card => card.Id != revealedEvent.CardId), "revealed card leaves the target hand");
    TrueWithMessage(
        ordinaryViewer.Players.SelectMany(player => player.Hand).All(card => card.Id != matchingDiscardCardId),
        "source discard remains private in an ordinary viewer");

    var invalid = gameWithFireAttack.Submit(new AnswerPromptCommand(
        0,
        discardPrompt.PromptId,
        new ChoiceId("fire-attack.fake"),
        gameWithFireAttack.Revision));
    False(invalid.Accepted);
    Equal(CommandErrorCode.InvalidChoice, invalid.Error!.Code);

    var accepted = gameWithFireAttack.Submit(new AnswerPromptCommand(
        0,
        discardPrompt.PromptId,
        discardChoice.Id,
        gameWithFireAttack.Revision));
    TrueWithMessage(accepted.Accepted, "FireAttack discard choice accepted");
    Equal(EngineStatus.AwaitingHumanPlay, accepted.State.Status);
    Equal(0, accepted.State.ProcessingCardCount);
    Equal(0, gameWithFireAttack.ResolutionStack.Count);
    Equal(0, accepted.State.PublicRevealedCards.Count);

    var resolved = gameWithFireAttack.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<FireAttackResolvedEvent>()
        .Single(eventItem => eventItem.ResolutionId == frame.Id);
    Equal(revealedEvent.CardId, resolved.RevealedCardId);
    Equal(revealedEvent.Suit, resolved.RevealedSuit);
    Equal(matchingDiscardCardId, resolved.MatchingDiscardCardId);
    True(resolved.CausedDamage);
    TrueWithMessage(gameWithFireAttack.Events.Any(eventItem =>
        eventItem.Payload is DamageRequestedEvent damage &&
        damage.SourceSeat == 0 &&
        damage.TargetSeat == targetSeatAtBoundary &&
        damage.Amount == 1 &&
        damage.SourceCard == CardKind.FireAttack &&
        damage.Nature == DamageNature.Fire), "FireAttack emits typed fire damage");
    var targetAfter = gameWithFireAttack.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == targetSeatAtBoundary);
    Equal(targetBefore.Hp - 1, targetAfter.Hp);
    Equal(sourceBefore.HandCount - 1,
        gameWithFireAttack.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount);

    TrueWithMessage(gameWithFireAttack.CardMovements.Any(movement =>
        movement.CardId == actionToUse.CardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use), "FireAttack effect enters Processing");
    TrueWithMessage(gameWithFireAttack.CardMovements.Any(movement =>
        movement.CardId == revealedEvent.CardId &&
        movement.From == CardLocation.Hand(targetSeatAtBoundary) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.FireAttackReveal), "FireAttack reveal movement");
    TrueWithMessage(gameWithFireAttack.CardMovements.Any(movement =>
        movement.CardId == matchingDiscardCardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.FireAttackDiscard), "FireAttack same-suit discard movement");
    TrueWithMessage(gameWithFireAttack.CardMovements.Any(movement =>
        movement.CardId == revealedEvent.CardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.FireAttackFinished), "FireAttack revealed card finishes");
    TrueWithMessage(gameWithFireAttack.CardMovements.Any(movement =>
        movement.CardId == matchingDiscardCardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.FireAttackDiscardFinished), "FireAttack source discard finishes");
    TrueWithMessage(gameWithFireAttack.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.ResolutionId == frame.Id &&
        finished.CardKind == CardKind.FireAttack), "FireAttack finished event");
    TrueWithMessage(gameWithFireAttack.AiThoughts.Any(thought =>
        thought.Candidates.Any(candidate =>
            candidate.Action.Kind == LegalActionKind.FireAttackReveal &&
            candidate.Reason.Contains("自己的私有手牌", StringComparison.Ordinal))),
        "FireAttack reveal AI uses its private snapshot");
    AssertCardInventory(gameWithFireAttack);
}

static void FireAttackSkipFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? selectedPrompt = null;
    CardUseFrame? selectedFrame = null;
    for (var seed = 1; seed <= 8_192 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var result = game.Start();
        if (HasSkill(game, SkillKind.Yuanhu))
        {
            continue;
        }

        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var action = game.GetHumanLegalActions()
            .FirstOrDefault(candidate =>
                candidate.Kind == LegalActionKind.FireAttack &&
                candidate.TargetSeat is not null);
        if (action is null)
        {
            continue;
        }

        var targetSeat = action.TargetSeat!.Value;
        result = game.HumanPlay(action.CardId!.Value, targetSeat, advanceToHumanBoundary: false);
        if (result.Status != EngineStatus.Running || game.PendingDecision is not null)
        {
            continue;
        }

        var targetPrompt = game.CreateSnapshot(targetSeat).PendingDecision;
        if (targetPrompt is not { Kind: DecisionKind.FireAttackReveal })
        {
            continue;
        }

        var frame = game.ResolutionStack.OfType<CardUseFrame>()
            .SingleOrDefault(candidate => candidate.CardKind == CardKind.FireAttack);
        if (frame is null)
        {
            continue;
        }

        result = game.AdvanceOneStep();
        var prompt = game.PendingDecision;
        if (result.Status == EngineStatus.AwaitingHumanCardSelection &&
            prompt is { Kind: DecisionKind.FireAttackDiscard, PlayerSeat: 0 } &&
            prompt.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("response") == "fire-attack-skip"))
        {
            selectedGame = game;
            selectedPrompt = prompt;
            selectedFrame = frame;
        }
    }

    if (selectedGame is null || selectedPrompt is null || selectedFrame is null)
    {
        throw new InvalidOperationException("No deterministic FireAttack skip boundary was found.");
    }

    var gameWithSkip = selectedGame!;
    var promptAtBoundary = selectedPrompt!;
    var frameAtBoundary = selectedFrame!;
    var skipChoice = promptAtBoundary.Choices.Single(choice =>
        choice.Parameters.GetValueOrDefault("response") == "fire-attack-skip");
    var revealed = gameWithSkip.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<FireAttackCardRevealedEvent>()
        .Single(eventItem => eventItem.ResolutionId == frameAtBoundary.Id);
    var targetBefore = gameWithSkip.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == frameAtBoundary.TargetSeats.Single());
    var sourceBefore = gameWithSkip.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == 0);

    var accepted = gameWithSkip.Submit(new AnswerPromptCommand(
        0,
        promptAtBoundary.PromptId,
        skipChoice.Id,
        gameWithSkip.Revision));
    TrueWithMessage(accepted.Accepted, "FireAttack skip choice accepted");
    Equal(EngineStatus.AwaitingHumanPlay, accepted.State.Status);
    Equal(0, accepted.State.ProcessingCardCount);
    Equal(0, accepted.State.PublicRevealedCards.Count);
    Equal(0, gameWithSkip.ResolutionStack.Count);

    var resolved = gameWithSkip.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<FireAttackResolvedEvent>()
        .Single(eventItem => eventItem.ResolutionId == frameAtBoundary.Id);
    False(resolved.CausedDamage);
    Equal<int?>(null, resolved.MatchingDiscardCardId);
    False(gameWithSkip.Events.Any(eventItem =>
        eventItem.Payload is DamageRequestedEvent damage &&
        damage.ResolutionId == frameAtBoundary.Id));
    var targetAfter = gameWithSkip.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == frameAtBoundary.TargetSeats.Single());
    Equal(targetBefore.Hp, targetAfter.Hp);
    Equal(sourceBefore.HandCount,
        gameWithSkip.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount);
    TrueWithMessage(gameWithSkip.CardMovements.Any(movement =>
        movement.CardId == revealed.CardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.FireAttackFinished), "FireAttack skip discards revealed card");
    TrueWithMessage(gameWithSkip.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.ResolutionId == frameAtBoundary.Id &&
        finished.CardKind == CardKind.FireAttack), "FireAttack skip finishes card use");
    AssertCardInventory(gameWithSkip);
}

static void AttributeSlashFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedAction = null;
    CardSnapshot? selectedCard = null;
    for (var seed = 1; seed <= 2_048 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var result = game.Start();
        if (HasSkill(game, SkillKind.Yuanhu))
        {
            continue;
        }

        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var self = game.CreateSnapshot(0).Players.Single(player => player.Seat == 0);
        foreach (var action in game.GetHumanLegalActions()
                     .Where(candidate =>
                         candidate.Kind == LegalActionKind.Slash &&
                         candidate.TargetSeat is not null))
        {
            var card = self.Hand.Single(candidate => candidate.Id == action.CardId);
            if (card.Kind is not (CardKind.FireSlash or CardKind.ThunderSlash))
            {
                continue;
            }

            var target = game.CreateSnapshot(action.TargetSeat!.Value)
                .Players.Single(player => player.Seat == action.TargetSeat.Value);
            if (target.Hand.Any(candidate => candidate.Kind == CardKind.Dodge) ||
                IsDamageTriggerSkill(target.Skill) ||
                target.Skill == SkillKind.Longdan)
            {
                continue;
            }

            selectedGame = game;
            selectedAction = action;
            selectedCard = card;
            break;
        }
    }

    if (selectedGame is null || selectedAction is null || selectedCard is null)
    {
        throw new InvalidOperationException("No deterministic elemental Slash boundary was found.");
    }

    var gameWithAttributeSlash = selectedGame!;
    var actionToUse = selectedAction!;
    var attackCard = selectedCard!;
    var targetSeat = actionToUse.TargetSeat!.Value;
    var expectedNature = attackCard.Kind == CardKind.FireSlash
        ? DamageNature.Fire
        : DamageNature.Thunder;
    var expectedLabel = expectedNature == DamageNature.Fire ? "火焰" : "雷电";

    var resultAfterUse = gameWithAttributeSlash.HumanPlay(
        actionToUse.CardId!.Value,
        targetSeat,
        advanceToHumanBoundary: false);

    Equal(EngineStatus.Running, resultAfterUse.Status);
    Equal(0, resultAfterUse.State.ProcessingCardCount);
    Equal(0, gameWithAttributeSlash.ResolutionStack.Count);

    var events = gameWithAttributeSlash.Events.Select(eventItem => eventItem.Payload).ToArray();
    var declared = events
        .OfType<CardUseDeclaredEvent>()
        .Single(eventItem => eventItem.CardId == attackCard.Id);
    Equal(attackCard.Kind, declared.CardKind);

    var requested = events
        .OfType<DamageRequestedEvent>()
        .Single(eventItem => eventItem.SourceCard == attackCard.Kind);
    Equal(expectedNature, requested.Nature);
    Equal(expectedNature, events
        .OfType<DamageAppliedEvent>()
        .Single(eventItem => eventItem.SourceSeat == 0 && eventItem.TargetSeat == targetSeat)
        .Nature);
    Equal(expectedNature, events
        .OfType<AfterDamageEvent>()
        .Single(eventItem => eventItem.ResolutionId == requested.ResolutionId)
        .Nature);
    True(events.Any(eventItem =>
        eventItem is CardUseFinishedEvent finished &&
        finished.CardId == attackCard.Id &&
        finished.CardKind == attackCard.Kind));
    True(gameWithAttributeSlash.Log.Any(entry =>
        entry.Type == "Damage" &&
        entry.Message.Contains($"1 点{expectedLabel}伤害", StringComparison.Ordinal)));

    AssertCardInventory(gameWithAttributeSlash);
}

static void AlcoholFlow()
{
    GameEngine? selectedGame = null;
    LegalAction? alcoholAction = null;
    LegalAction? slashAction = null;
    CardSnapshot? slashCard = null;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var result = game.Start();
        if (HasSkill(game, SkillKind.Yuanhu))
        {
            continue;
        }

        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var self = game.CreateSnapshot(0).Players.Single(player => player.Seat == 0);
        var candidateAlcohol = game.GetHumanLegalActions()
            .FirstOrDefault(action => action.Kind == LegalActionKind.Alcohol);
        if (candidateAlcohol is null)
        {
            continue;
        }

        foreach (var candidateSlash in game.GetHumanLegalActions()
                     .Where(action =>
                         action.Kind == LegalActionKind.Slash &&
                         action.TargetSeat is not null))
        {
            var candidateCard = self.Hand.Single(card => card.Id == candidateSlash.CardId);
            var targetSeat = candidateSlash.TargetSeat!.Value;
            var target = game.CreateSnapshot(targetSeat).Players
                .Single(player => player.Seat == targetSeat);
            if (!IsSlashCard(candidateCard.Kind) ||
                target.Hand.Any(card => card.Kind == CardKind.Dodge) ||
                IsDamageTriggerSkill(target.Skill) ||
                target.Skill == SkillKind.Longdan)
            {
                continue;
            }

            selectedGame = game;
            alcoholAction = candidateAlcohol;
            slashAction = candidateSlash;
            slashCard = candidateCard;
            break;
        }
    }

    if (selectedGame is null || alcoholAction is null || slashAction is null || slashCard is null)
    {
        throw new InvalidOperationException("No deterministic Alcohol and direct Slash boundary was found.");
    }

    var gameWithAlcohol = selectedGame!;
    var alcoholCardId = alcoholAction!.CardId!.Value;
    var slashCardSnapshot = slashCard!;
    var targetSeatForSlash = slashAction!.TargetSeat!.Value;
    var afterAlcohol = gameWithAlcohol.HumanPlay(
        alcoholCardId,
        targetSeat: null,
        advanceToHumanBoundary: false);
    Equal(EngineStatus.Running, afterAlcohol.Status);
    True(afterAlcohol.State.Players.Single(player => player.Seat == 0).HasAlcoholEffect);
    Equal(0, afterAlcohol.State.ProcessingCardCount);
    Equal(1, gameWithAlcohol.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<AlcoholAppliedEvent>()
        .Count(eventItem => eventItem.SourceSeat == 0 && eventItem.DamageBonus == 1));
    True(gameWithAlcohol.CardMovements.Any(movement =>
        movement.CardId == alcoholCardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use));
    True(gameWithAlcohol.CardMovements.Any(movement =>
        movement.CardId == alcoholCardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished));
    True(gameWithAlcohol.GetHumanLegalActions().All(action =>
        action.Kind != LegalActionKind.Alcohol));

    var alcoholPlayBoundary = gameWithAlcohol.Advance();
    Equal(EngineStatus.AwaitingHumanPlay, alcoholPlayBoundary.Status);
    var afterSlash = gameWithAlcohol.HumanPlay(
        slashAction.CardId!.Value,
        targetSeatForSlash,
        advanceToHumanBoundary: false);
    False(afterSlash.State.Players.Single(player => player.Seat == 0).HasAlcoholEffect);
    Equal(0, afterSlash.State.ProcessingCardCount);

    var slashEvents = gameWithAlcohol.Events.Select(eventItem => eventItem.Payload).ToArray();
    var requested = slashEvents
        .OfType<DamageRequestedEvent>()
        .Single(eventItem => eventItem.SourceSeat == 0 && eventItem.TargetSeat == targetSeatForSlash);
    var applied = slashEvents
        .OfType<DamageAppliedEvent>()
        .Single(eventItem => eventItem.SourceSeat == 0 && eventItem.TargetSeat == targetSeatForSlash);
    var afterDamage = slashEvents
        .OfType<AfterDamageEvent>()
        .Single(eventItem => eventItem.ResolutionId == requested.ResolutionId);
    Equal(slashCardSnapshot.Kind, requested.SourceCard);
    Equal(2, requested.Amount);
    Equal(2, applied.Amount);
    Equal(2, afterDamage.Amount);
    True(gameWithAlcohol.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.CardId == slashAction.CardId &&
        finished.CardKind == slashCardSnapshot.Kind));
    True(gameWithAlcohol.Log.Any(entry =>
        entry.Type == "Damage" && entry.Message.Contains("2 点", StringComparison.Ordinal)));
    AssertCardInventory(gameWithAlcohol);

    GameEngine? expiringGame = null;
    for (var seed = 1; seed <= 4_096 && expiringGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var result = game.Start();
        if (HasSkill(game, SkillKind.Yuanhu))
        {
            continue;
        }

        var action = result.Status == EngineStatus.AwaitingHumanPlay
            ? game.GetHumanLegalActions().FirstOrDefault(candidate =>
                candidate.Kind == LegalActionKind.Alcohol)
            : null;
        if (action is null)
        {
            continue;
        }

        var openingTurn = result.State.TurnNumber;
        game.HumanPlay(action.CardId!.Value, advanceToHumanBoundary: false);
        game.Advance();
        game.HumanEndPlay(advanceToHumanBoundary: false);
        var nextBoundary = game.Advance();
        var boundarySteps = 0;
        while (nextBoundary.Status != EngineStatus.Completed &&
               !(nextBoundary.Status == EngineStatus.AwaitingHumanPlay &&
                 nextBoundary.State.CurrentSeat == 0 &&
                 nextBoundary.State.TurnNumber > openingTurn) &&
               boundarySteps++ < 2_000)
        {
            nextBoundary = nextBoundary.Status switch
            {
                EngineStatus.AwaitingHumanResponse => game.PendingDecision?.Kind == DecisionKind.RespondSlash
                    ? game.HumanRespondSlash(useSlash: false, advanceToHumanBoundary: false)
                    : game.HumanRespond(useDodge: false, advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanDying => game.HumanRespondDying(
                    usePeach: false,
                    advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
                _ => game.Advance()
            };
        }
        if (nextBoundary.Status == EngineStatus.AwaitingHumanPlay &&
            nextBoundary.State.CurrentSeat == 0 &&
            nextBoundary.State.TurnNumber > openingTurn)
        {
            expiringGame = game;
        }
    }

    NotNull(expiringGame);
    var expiredState = expiringGame!.State;
    False(expiredState.Players.Single(player => player.Seat == 0).HasAlcoholEffect);
    True(expiringGame.Events.Any(eventItem =>
        eventItem.Payload is AlcoholExpiredEvent expired && expired.PlayerSeat == 0));
    True(expiringGame.Log.Any(entry =>
        entry.Type == "EffectExpired" && entry.Message.Contains("酒效", StringComparison.Ordinal)));
    AssertCardInventory(expiringGame);
}

static void FeedbackFlow()
{
    GameEngine? selectedGame = null;
    CardSnapshot? selectedAttackCard = null;
    int feedbackSeat = -1;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                MaxTurns = 180
            },
            StandardContentRegistry.Create());
        var result = game.Start();
        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }
        var revealed = game.CreateSnapshot(0, revealAll: true);
        if (revealed.Players.Any(player => player.Skill == SkillKind.Yuanhu))
        {
            continue;
        }

        var self = revealed.Players.Single(player => player.Seat == 0);
        var target = revealed.Players.FirstOrDefault(player =>
            player.Seat != 0 &&
            player.Skill == SkillKind.Feedback &&
            player.IsAlive &&
            player.Hand.All(card => card.Kind != CardKind.Dodge));
        if (target is null)
        {
            continue;
        }
        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            candidate.Kind == LegalActionKind.Slash &&
            candidate.TargetSeat == target.Seat);
        if (action is not null)
        {
            selectedGame = game;
            selectedAttackCard = self.Hand.Single(card => card.Id == action.CardId);
            feedbackSeat = target.Seat;
        }
    }
    if (selectedGame is null || selectedAttackCard is null || feedbackSeat < 0)
    {
        throw new InvalidOperationException("No deterministic Feedback damage-card boundary was found.");
    }
    var gameWithFeedback = selectedGame!;
    var attackCard = selectedAttackCard!;
    var targetBefore = gameWithFeedback.CreateSnapshot(feedbackSeat)
        .Players.Single(player => player.Seat == feedbackSeat);
    var sourceBefore = gameWithFeedback.State.Players.Single(player => player.Seat == 0);
    var resultAfterUse = gameWithFeedback.HumanPlay(
        attackCard.Id,
        feedbackSeat,
        advanceToHumanBoundary: false);
    Equal(EngineStatus.Running, resultAfterUse.Status);
    Equal(1, resultAfterUse.State.ProcessingCardCount);
    Equal<PendingDecision?>(null, gameWithFeedback.State.PendingDecision);
    var privatePrompt = gameWithFeedback.CreateSnapshot(feedbackSeat).PendingDecision;
    NotNull(privatePrompt);
    Equal(DecisionKind.Feedback, privatePrompt!.Kind);
    True(privatePrompt.ValidCardIds.Contains(attackCard.Id));
    var feedbackFrame = gameWithFeedback.ResolutionStack[^1] as DamageSkillFrame;
    True(feedbackFrame is not null &&
         gameWithFeedback.ResolutionStack[^2] is DamageTriggerWindowFrame triggerWindow &&
         gameWithFeedback.ResolutionStack[^3] is DamageFrame damageFrame &&
         feedbackFrame.ParentFrameId == triggerWindow.Id &&
         triggerWindow.ParentFrameId == damageFrame.Id &&
         triggerWindow.CandidateIndex == 0);
    Equal("Feedback", feedbackFrame!.CandidateId);
    var aiThoughtCountBefore = gameWithFeedback.AiThoughts.Count;
    var afterAiTrigger = gameWithFeedback.AdvanceOneStep();
    Equal(EngineStatus.Running, afterAiTrigger.Status);
    Equal(0, afterAiTrigger.State.ProcessingCardCount);
    Equal(0, gameWithFeedback.ResolutionStack.Count);
    var targetAfter = gameWithFeedback.CreateSnapshot(feedbackSeat)
        .Players.Single(player => player.Seat == feedbackSeat);
    Equal(targetBefore.HandCount + 1, targetAfter.HandCount);
    TrueWithMessage(targetAfter.Hand.Any(card => card.Id == attackCard.Id), "Feedback claimed damage card");
    TrueWithMessage(gameWithFeedback.AiThoughts.Count > aiThoughtCountBefore, "Feedback AI thought");
    TrueWithMessage(gameWithFeedback.AiThoughts.Last().Candidates.Any(candidate =>
        candidate.Action.Kind == LegalActionKind.Feedback), "Feedback AI candidate");
    Equal(sourceBefore.HandCount - 1, gameWithFeedback.State.Players.Single(player => player.Seat == 0).HandCount);
    var claim = gameWithFeedback.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageCardClaimedEvent>()
        .Single(eventItem => eventItem.CardId == attackCard.Id);
    Equal(feedbackSeat, claim.OwnerSeat);
    Equal(0, claim.SourceSeat);
    Equal(attackCard.Kind, claim.CardKind);
    Equal(SkillKind.Feedback, claim.Skill);
    var feedbackRequested = gameWithFeedback.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageSkillRequestedEvent>()
        .Single(eventItem => eventItem.CardId == attackCard.Id);
    Equal("Feedback", feedbackRequested.CandidateId);
    Equal(0, feedbackRequested.Priority);
    var feedbackResolved = gameWithFeedback.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageSkillResolvedEvent>()
        .Single(eventItem => eventItem.CardId == attackCard.Id);
    Equal("Feedback", feedbackResolved.CandidateId);
    TrueWithMessage(gameWithFeedback.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.CardId == attackCard.Id &&
        finished.CardKind == attackCard.Kind), "Feedback attack finished event");
    TrueWithMessage(gameWithFeedback.CardMovements.Any(movement =>
        movement.CardId == attackCard.Id &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use), "Feedback attack entered Processing");
    TrueWithMessage(gameWithFeedback.CardMovements.Any(movement =>
        movement.CardId == attackCard.Id &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.Hand(feedbackSeat) &&
        movement.Reason == CardMoveReasons.FeedbackClaim), "Feedback claim movement");
    TrueWithMessage(gameWithFeedback.Log.Any(entry =>
        entry.Type == "SkillTriggered" &&
        entry.Message.Contains("反馈", StringComparison.Ordinal)), "Feedback log");
    var ordinary = gameWithFeedback.State;
    TrueWithMessage(
        ordinary.Players.Single(player => player.Seat == feedbackSeat).Hand.Count == 0,
        "ordinary view hides Feedback target hand");
    TrueWithMessage(
        !SnapshotJson.Serialize(ordinary).Contains($"\"Id\": {attackCard.Id},", StringComparison.Ordinal),
        "ordinary view hides claimed card id");
    TrueWithMessage(
        gameWithFeedback.CreateSnapshot(feedbackSeat).Players
            .Single(player => player.Seat == feedbackSeat).Hand
            .Any(card => card.Id == attackCard.Id),
        "Feedback owner sees claimed card");
    AssertCardInventory(gameWithFeedback);
}

static void FeedbackHumanChoiceFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? selectedPrompt = null;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var revealed = game.CreateSnapshot(0, revealAll: true);
        if (revealed.Players.Any(player => player.Skill == SkillKind.Yuanhu))
        {
            continue;
        }

        if (revealed.Players.Single(player => player.Seat == 0).Skill != SkillKind.Feedback)
        {
            continue;
        }

        var result = game.Start();
        var steps = 0;
        while (result.Status != EngineStatus.Completed && steps++ < 2_000)
        {
            if (result.Status == EngineStatus.AwaitingHumanResponse &&
                result.PendingDecision?.Kind == DecisionKind.Feedback)
            {
                selectedGame = game;
                selectedPrompt = result.PendingDecision;
                break;
            }

            result = result.Status switch
            {
                EngineStatus.AwaitingHumanPlay => game.HumanEndPlay(advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanResponse when result.PendingDecision?.Kind == DecisionKind.RespondSlash =>
                    game.HumanRespondSlash(useSlash: false, advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanResponse =>
                    game.HumanRespond(useDodge: false, advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanDying => game.HumanRespondDying(
                    usePeach: false,
                    advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
                _ => game.AdvanceOneStep()
            };
        }
    }

    if (selectedGame is null || selectedPrompt is null)
    {
        throw new InvalidOperationException("No deterministic human Feedback trigger was found.");
    }

    var gameWithFeedback = selectedGame!;
    var prompt = selectedPrompt!;
    Equal(DecisionKind.Feedback, prompt.Kind);
    Equal(EngineStatus.AwaitingHumanResponse, gameWithFeedback.State.Status);
    Equal(0, prompt.PlayerSeat);
    Equal(0, prompt.TargetSeat);
    True(prompt.Choices.Any(choice => choice.Parameters["response"] == "feedback"));
    True(prompt.Choices.Any(choice => choice.Parameters["action"] == "skip-damage-skill"));
    Equal<PendingDecision?>(null, gameWithFeedback.CreateSnapshot(1).PendingDecision);

    var beforeInvalid = gameWithFeedback.SerializeState();
    var invalid = gameWithFeedback.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        new ChoiceId("feedback.fake"),
        gameWithFeedback.Revision));
    False(invalid.Accepted);
    Equal(CommandErrorCode.InvalidChoice, invalid.Error!.Code);
    Equal(beforeInvalid, gameWithFeedback.SerializeState());

    var useChoice = prompt.Choices.Single(choice =>
        choice.Parameters.GetValueOrDefault("response") == "feedback");
    var damageCardId = useChoice.Cards.Single();
    var accepted = gameWithFeedback.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        useChoice.Id,
        gameWithFeedback.Revision));
    True(accepted.Accepted);
    TrueWithMessage(gameWithFeedback.Events.Any(eventItem =>
        eventItem.Payload is DamageSkillResolvedEvent resolved &&
        resolved.OwnerSeat == 0 &&
        resolved.CardId == damageCardId &&
        resolved.Skill == SkillKind.Feedback &&
        resolved.Used), "human Feedback resolution");
    TrueWithMessage(gameWithFeedback.Events.Any(eventItem =>
        eventItem.Payload is DamageCardClaimedEvent claimed &&
        claimed.OwnerSeat == 0 &&
        claimed.CardId == damageCardId), "human Feedback claim");
    TrueWithMessage(
        gameWithFeedback.CreateSnapshot(0).Players.Single(player => player.Seat == 0).Hand
            .Any(card => card.Id == damageCardId),
        "human Feedback owner sees claimed card");
    TrueWithMessage(
        !SnapshotJson.Serialize(gameWithFeedback.CreateSnapshot(1))
            .Contains($"\"Id\": {damageCardId}", StringComparison.Ordinal),
        "other viewer hides human Feedback card");
    AssertCardInventory(gameWithFeedback);
}

static void FeedbackSkipChoiceFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? selectedPrompt = null;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var revealed = game.CreateSnapshot(0, revealAll: true);
        if (revealed.Players.Any(player => player.Skill == SkillKind.Yuanhu))
        {
            continue;
        }

        if (revealed.Players.Single(player => player.Seat == 0).Skill != SkillKind.Feedback)
        {
            continue;
        }

        var result = game.Start();
        var steps = 0;
        while (result.Status != EngineStatus.Completed && steps++ < 2_000)
        {
            if (result.Status == EngineStatus.AwaitingHumanResponse &&
                result.PendingDecision?.Kind == DecisionKind.Feedback)
            {
                var skillFrame = game.ResolutionStack
                    .OfType<DamageSkillFrame>()
                    .SingleOrDefault();
                var isDirectSlash = skillFrame is { } currentSkillFrame &&
                    currentSkillFrame.CardKind is (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) &&
                    game.ResolutionStack.OfType<CardUseFrame>().Any(cardUse =>
                        cardUse.CardId == currentSkillFrame.CardId &&
                        cardUse.CardKind is (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) &&
                        cardUse.TargetSeats.Count == 1);
                if (isDirectSlash)
                {
                    selectedGame = game;
                    selectedPrompt = result.PendingDecision;
                    break;
                }
            }

            result = result.Status switch
            {
                EngineStatus.AwaitingHumanPlay => game.HumanEndPlay(advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanResponse when result.PendingDecision?.Kind == DecisionKind.RespondSlash =>
                    game.HumanRespondSlash(useSlash: false, advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanResponse =>
                    game.HumanRespond(useDodge: false, advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanDying => game.HumanRespondDying(
                    usePeach: false,
                    advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
                _ => game.AdvanceOneStep()
            };
        }
    }

    if (selectedGame is null || selectedPrompt is null)
    {
        throw new InvalidOperationException("No deterministic direct Slash Feedback skip boundary was found.");
    }

    var gameWithFeedback = selectedGame!;
    var prompt = selectedPrompt!;
    var skipChoice = prompt.Choices.Single(choice =>
        choice.Parameters.GetValueOrDefault("action") == "skip-damage-skill");
    var useChoice = prompt.Choices.Single(choice =>
        choice.Parameters.GetValueOrDefault("response") == "feedback");
    var damageCardId = useChoice.Cards.Single();
    var targetBefore = gameWithFeedback.CreateSnapshot(0)
        .Players.Single(player => player.Seat == 0);

    var accepted = gameWithFeedback.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        skipChoice.Id,
        gameWithFeedback.Revision));
    TrueWithMessage(accepted.Accepted, "Feedback skip choice accepted");

    var resolved = gameWithFeedback.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageSkillResolvedEvent>()
        .Single(eventItem => eventItem.CardId == damageCardId);
    False(resolved.Used);
    False(gameWithFeedback.ResolutionStack.OfType<DamageSkillFrame>().Any(frame =>
        frame.ParentFrameId == resolved.ResolutionId));
    False(gameWithFeedback.Events.Any(eventItem =>
        eventItem.Payload is DamageCardClaimedEvent claimed && claimed.CardId == damageCardId));
    TrueWithMessage(gameWithFeedback.CardMovements.Any(movement =>
        movement.CardId == damageCardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished), "Feedback skip discard movement");
    TrueWithMessage(gameWithFeedback.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.CardId == damageCardId), "Feedback skip attack finished event");
    var targetAfter = gameWithFeedback.CreateSnapshot(0)
        .Players.Single(player => player.Seat == 0);
    Equal(targetBefore.HandCount, targetAfter.HandCount);
    False(targetAfter.Hand.Any(card => card.Id == damageCardId));
    AssertCardInventory(gameWithFeedback);
}

static void YijiGiftFlow()
{
    GameEngine? selectedGame = null;
    CardSnapshot? selectedAttackCard = null;
    int yijiSeat = -1;
    for (var seed = 1; seed <= 8_192 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                MaxTurns = 180
            },
            StandardContentRegistry.Create());
        var result = game.Start();
        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var revealed = game.CreateSnapshot(0, revealAll: true);
        if (revealed.Players.Any(player => player.Skill == SkillKind.Yuanhu))
        {
            continue;
        }

        var source = revealed.Players.Single(player => player.Seat == 0);
        var target = revealed.Players.FirstOrDefault(player =>
            player.Seat != 0 &&
            player.Skill == SkillKind.Yiji &&
            player.IsAlive &&
            player.Hand.All(card => card.Kind != CardKind.Dodge));
        if (target is null)
        {
            continue;
        }

        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            candidate.Kind == LegalActionKind.Slash &&
            candidate.TargetSeat == target.Seat);
        if (action is not null)
        {
            selectedGame = game;
            selectedAttackCard = source.Hand.Single(card => card.Id == action.CardId);
            yijiSeat = target.Seat;
        }
    }

    if (selectedGame is null || selectedAttackCard is null || yijiSeat < 0)
    {
        throw new InvalidOperationException("No deterministic Yiji damage boundary was found.");
    }

    var gameWithYiji = selectedGame!;
    var attackCard = selectedAttackCard!;
    var resultAfterDamage = gameWithYiji.HumanPlay(
        attackCard.Id,
        yijiSeat,
        advanceToHumanBoundary: false);
    Equal(EngineStatus.Running, resultAfterDamage.Status);
    Equal<PendingDecision?>(null, gameWithYiji.State.PendingDecision);

    var privatePrompt = gameWithYiji.CreateSnapshot(yijiSeat).PendingDecision;
    NotNull(privatePrompt);
    Equal(DecisionKind.Yiji, privatePrompt!.Kind);
    Equal(2, privatePrompt.ValidCardIds.Count);
    TrueWithMessage(privatePrompt.ValidTargetSeats.All(seat => seat != yijiSeat), "Yiji excludes its owner");
    TrueWithMessage(privatePrompt.Choices.Any(choice =>
        choice.Parameters.GetValueOrDefault("response") == "yiji-gift"), "Yiji publishes gift choices");

    var skillFrame = gameWithYiji.ResolutionStack.OfType<DamageSkillFrame>().Single();
    Equal(DamageSkillEffectKind.GiftDrawnCard, skillFrame.Effect);
    TrueWithMessage(skillFrame.EffectCardIds!.SequenceEqual(privatePrompt.ValidCardIds), "Yiji frame retains drawn cards");
    TrueWithMessage(gameWithYiji.ResolutionStack[^2] is DamageTriggerWindowFrame, "Yiji parent trigger frame");
    TrueWithMessage(gameWithYiji.ResolutionStack[^3] is DamageFrame, "Yiji grandparent damage frame");

    var ordinaryBefore = gameWithYiji.CreateSnapshot(
        Enumerable.Range(0, gameWithYiji.PlayerCount).First(seat => seat != yijiSeat));
    TrueWithMessage(ordinaryBefore.PendingDecision is null, "Yiji prompt remains private");
    foreach (var cardId in privatePrompt.ValidCardIds)
    {
        False(SnapshotJson.Serialize(ordinaryBefore).Contains(
            $"\"Id\": {cardId},",
            StringComparison.Ordinal));
    }

    var completedStep = gameWithYiji.AdvanceOneStep();
    TrueWithMessage(completedStep.Status is EngineStatus.Running or EngineStatus.AwaitingHumanPlay or EngineStatus.Completed, "Yiji AI step returns after the gift");
    Equal(0, gameWithYiji.ResolutionStack.Count);
    Equal(0, completedStep.State.ProcessingCardCount);
    var drawn = gameWithYiji.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageSkillCardsDrawnEvent>()
        .Single(eventItem => eventItem.OwnerSeat == yijiSeat);
    Equal(2, drawn.CardIds.Count);
    var given = gameWithYiji.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageSkillCardGivenEvent>()
        .Single(eventItem => eventItem.OwnerSeat == yijiSeat);
    TrueWithMessage(drawn.CardIds.Contains(given.CardId), "Yiji gives one of its drawn cards");
    TrueWithMessage(given.TargetSeat != yijiSeat, "Yiji gives to another seat");
    TrueWithMessage(gameWithYiji.Events.Any(eventItem =>
        eventItem.Payload is DamageSkillResolvedEvent resolved &&
        resolved.Skill == SkillKind.Yiji &&
        resolved.Used), "Yiji resolution event");
    TrueWithMessage(gameWithYiji.CardMovements.Any(movement =>
        movement.CardId == given.CardId &&
        movement.From == CardLocation.Hand(yijiSeat) &&
        movement.To == CardLocation.Hand(given.TargetSeat) &&
        movement.Reason == CardMoveReasons.YijiGive), "Yiji gift movement");
    TrueWithMessage(gameWithYiji.CreateSnapshot(given.TargetSeat, revealAll: true).Players
        .Single(player => player.Seat == given.TargetSeat)
        .Hand.Any(card => card.Id == given.CardId), "Yiji target receives the card");
    var privateViewer = Enumerable.Range(0, gameWithYiji.PlayerCount)
        .First(seat => seat != yijiSeat && seat != given.TargetSeat);
    False(SnapshotJson.Serialize(gameWithYiji.CreateSnapshot(privateViewer))
        .Contains($"\"Id\": {given.CardId},", StringComparison.Ordinal));
    TrueWithMessage(gameWithYiji.AiThoughts.Any(thought =>
        thought.Candidates.Any(candidate => candidate.Action.Kind == LegalActionKind.YijiGift)), "Yiji AI exposes gift candidates");
    AssertCardInventory(gameWithYiji);
}

static void YijiHumanChoiceFlow()
{
    GameEngine? selectedGame = null;
    for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = true,
                MaxTurns = 180
            },
            StandardContentRegistry.Create());
        var started = game.Start();
        if (started.Status != EngineStatus.AwaitingHumanGeneralSelection ||
            !started.PendingDecision!.ValidContentIds.Contains("standard:guo-jia", StringComparer.Ordinal))
        {
            continue;
        }

        _ = game.HumanSelectGeneral("standard:guo-jia", advanceToHumanBoundary: false);
        var result = game.Advance();
        for (var step = 0; step < 1_200 && result.Status != EngineStatus.Completed; step++)
        {
            if (result.Status == EngineStatus.AwaitingHumanResponse &&
                result.PendingDecision?.Kind == DecisionKind.Yiji)
            {
                selectedGame = game;
                break;
            }

            result = result.Status switch
            {
                EngineStatus.AwaitingHumanPlay => game.HumanEndPlay(advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanResponse when result.PendingDecision?.Kind == DecisionKind.RespondSlash =>
                    game.HumanRespondSlash(useSlash: false, advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanResponse =>
                    game.HumanRespond(useDodge: false, advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanDying => game.HumanRespondDying(
                    usePeach: false,
                    advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
                _ => game.Advance()
            };
        }
    }

    NotNull(selectedGame);
    var gameWithYiji = selectedGame!;
    var prompt = gameWithYiji.State.PendingDecision;
    NotNull(prompt);
    Equal(DecisionKind.Yiji, prompt!.Kind);
    var giftChoice = prompt.Choices.First(choice =>
        choice.Parameters.GetValueOrDefault("response") == "yiji-gift");
    var targetSeat = giftChoice.Targets.Single();
    var cardId = giftChoice.Cards.Single();
    var accepted = gameWithYiji.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        giftChoice.Id,
        gameWithYiji.Revision));
    TrueWithMessage(accepted.Accepted, "human Yiji gift choice accepted");
    TrueWithMessage(gameWithYiji.Events.Any(eventItem =>
        eventItem.Payload is DamageSkillCardGivenEvent given &&
        given.OwnerSeat == 0 &&
        given.TargetSeat == targetSeat &&
        given.CardId == cardId), "human Yiji gift event");
    TrueWithMessage(gameWithYiji.CardMovements.Any(movement =>
        movement.CardId == cardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Hand(targetSeat) &&
        movement.Reason == CardMoveReasons.YijiGive), "human Yiji gift movement");
    var hiddenViewer = Enumerable.Range(0, gameWithYiji.PlayerCount)
        .First(seat => seat != 0 && seat != targetSeat);
    False(SnapshotJson.Serialize(gameWithYiji.CreateSnapshot(hiddenViewer))
        .Contains($"\"Id\": {cardId},", StringComparison.Ordinal));
    AssertCardInventory(gameWithYiji);
}

static void JiemingFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? selectedPrompt = null;
    for (var seed = 1; seed <= 1_024 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = true,
                MaxTurns = 180
            },
            StandardContentRegistry.Create());
        var started = game.Start();
        if (started.Status != EngineStatus.AwaitingHumanGeneralSelection ||
            !started.PendingDecision!.ValidContentIds.Contains("standard:xun-yu", StringComparer.Ordinal))
        {
            continue;
        }

        _ = game.HumanSelectGeneral("standard:xun-yu", advanceToHumanBoundary: false);
        var result = game.Advance();
        for (var step = 0; step < 2_400 && result.Status != EngineStatus.Completed; step++)
        {
            if (result.Status == EngineStatus.AwaitingHumanResponse &&
                result.PendingDecision is { Kind: DecisionKind.Jieming, ValidTargetSeats.Count: > 0 })
            {
                selectedGame = game;
                selectedPrompt = result.PendingDecision;
                break;
            }

            result = result.Status switch
            {
                EngineStatus.AwaitingHumanPlay => game.HumanEndPlay(advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanResponse when result.PendingDecision?.Kind == DecisionKind.RespondSlash =>
                    game.HumanRespondSlash(useSlash: false, advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanResponse when result.PendingDecision?.Kind == DecisionKind.Feedback =>
                    game.HumanRespondFeedback(useFeedback: false, advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanResponse =>
                    game.HumanRespond(useDodge: false, advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanDying => game.HumanRespondDying(
                    usePeach: false,
                    advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
                _ => game.AdvanceOneStep()
            };
        }
    }

    if (selectedGame is null || selectedPrompt is null)
    {
        throw new InvalidOperationException("No deterministic human Jieming trigger was found.");
    }

    var gameWithJieming = selectedGame!;
    var prompt = selectedPrompt!;
    Equal(DecisionKind.Jieming, prompt.Kind);
    Equal(EngineStatus.AwaitingHumanResponse, gameWithJieming.State.Status);
    Equal(0, prompt.PlayerSeat);
    Equal(0, prompt.TargetSeat);
    True(prompt.IsPrivate);
    TrueWithMessage(prompt.ValidTargetSeats.Count > 0, "Jieming publishes at least one legal target");
    TrueWithMessage(prompt.Choices.Any(choice =>
        choice.Parameters.GetValueOrDefault("response") == "jieming-draw"),
        "Jieming publishes a draw choice");
    TrueWithMessage(prompt.Choices.Any(choice =>
        choice.Parameters.GetValueOrDefault("response") == "jieming-skip"),
        "Jieming publishes a skip choice");
    Equal<PendingDecision?>(null, gameWithJieming.CreateSnapshot(1).PendingDecision);

    var drawChoice = prompt.Choices.FirstOrDefault(choice =>
        choice.Parameters.GetValueOrDefault("response") == "jieming-draw" &&
        choice.Targets.Count == 1) ??
        throw new InvalidOperationException(
            $"Jieming target choices were [{string.Join(',', prompt.ValidTargetSeats)}], " +
            $"but no draw choice was published: {string.Join(" | ", prompt.Choices.Select(choice => choice.Id))}.");
    var targetSeat = drawChoice.Targets.Single();
    var beforeTarget = gameWithJieming.CreateSnapshot(0, revealAll: true).Players
        .Single(player => player.Seat == targetSeat);
    TrueWithMessage(beforeTarget.HandCount < beforeTarget.MaxHp,
        $"Jieming target {targetSeat} hand={beforeTarget.HandCount} max={beforeTarget.MaxHp}");

    var beforeInvalid = gameWithJieming.SerializeState();
    var invalid = gameWithJieming.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        new ChoiceId("jieming.fake"),
        gameWithJieming.Revision));
    False(invalid.Accepted);
    Equal(CommandErrorCode.InvalidChoice, invalid.Error!.Code);
    Equal(beforeInvalid, gameWithJieming.SerializeState());

    var completedSkill = gameWithJieming.HumanRespondJieming(
        targetSeat,
        advanceToHumanBoundary: false);
    True(completedSkill.Status is EngineStatus.Running or EngineStatus.AwaitingHumanPlay);
    var afterTarget = gameWithJieming.CreateSnapshot(0, revealAll: true).Players
        .Single(player => player.Seat == targetSeat);
    Equal(afterTarget.MaxHp, afterTarget.HandCount);

    var drawnEvents = gameWithJieming.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageSkillCardsDrawnEvent>()
        .Where(eventItem => eventItem.OwnerSeat == 0 && eventItem.Skill == SkillKind.Jieming)
        .ToArray();
    Equal(1, drawnEvents.Length);
    var drawn = drawnEvents.Single();
    Equal<int?>(targetSeat, drawn.TargetSeat);
    Equal(afterTarget.HandCount - beforeTarget.HandCount, drawn.CardIds.Count);
    TrueWithMessage(drawn.CardIds.Count > 0,
        $"Jieming draw count was {drawn.CardIds.Count}, draw pile before={gameWithJieming.CreateCardZoneDiagnostics().Count(card => card.Location == CardLocation.DrawPile)}");
    var resolved = gameWithJieming.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageSkillResolvedEvent>()
        .Single(eventItem =>
            eventItem.ResolutionId == drawn.ResolutionId &&
            eventItem.OwnerSeat == 0 &&
            eventItem.Skill == SkillKind.Jieming);
    TrueWithMessage(resolved.Used, "Jieming resolution is marked used");
    Equal<int?>(targetSeat, resolved.EffectTargetSeat);
    foreach (var cardId in drawn.CardIds)
    {
        TrueWithMessage(gameWithJieming.CardMovements.Any(movement =>
            movement.CardId == cardId &&
            movement.From == CardLocation.DrawPile &&
            movement.To == CardLocation.Hand(targetSeat) &&
            movement.Reason == CardMoveReasons.JiemingDraw), "Jieming draw movement");
    }

    var ordinaryViewer = Enumerable.Range(0, gameWithJieming.PlayerCount)
        .First(seat => seat != 0 && seat != targetSeat);
    var ordinaryAfter = gameWithJieming.CreateSnapshot(ordinaryViewer);
    Equal<PendingDecision?>(null, ordinaryAfter.PendingDecision);
    foreach (var cardId in drawn.CardIds)
    {
        False(SnapshotJson.Serialize(ordinaryAfter).Contains(
            $"\"Id\": {cardId},",
            StringComparison.Ordinal));
    }
    TrueWithMessage(gameWithJieming.AiThoughts.Count > 0, "Jieming match keeps prior AI evidence");
    gameWithJieming.Advance();
    AssertCardInventory(gameWithJieming);
}

static void YuanhuCrossSeatFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? selectedPrompt = null;
    CardSnapshot? selectedAttackCard = null;
    var targetSeat = -1;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = true,
                MaxTurns = 180
            },
            StandardContentRegistry.Create());
        var started = game.Start();
        if (started.Status != EngineStatus.AwaitingHumanGeneralSelection ||
            !started.PendingDecision!.ValidContentIds.Contains("standard:demo-yuanhu", StringComparer.Ordinal))
        {
            continue;
        }

        _ = game.HumanSelectGeneral("standard:demo-yuanhu", advanceToHumanBoundary: false);
        var ready = game.Advance();
        if (ready.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var revealed = game.CreateSnapshot(0, revealAll: true);
        var human = revealed.Players.Single(player => player.Seat == 0);
        if (human.Skill != SkillKind.Yuanhu)
        {
            continue;
        }

        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
        {
            if (candidate.Kind != LegalActionKind.Slash ||
                candidate.CardId is not { } cardId ||
                candidate.TargetSeat is not { } candidateTarget ||
                candidateTarget == 0)
            {
                return false;
            }

            var target = revealed.Players.Single(player => player.Seat == candidateTarget);
            return target.IsAlive &&
                   target.Hp > 1 &&
                   target.Hand.All(card => card.Kind != CardKind.Dodge);
        });
        if (action is null)
        {
            continue;
        }

        var result = game.HumanPlay(
            action.CardId!.Value,
            action.TargetSeat,
            advanceToHumanBoundary: true,
            playedCardKind: action.PlayedCardKind);
        if (result.Status == EngineStatus.AwaitingHumanResponse &&
            result.PendingDecision?.Kind == DecisionKind.Yuanhu)
        {
            selectedGame = game;
            selectedPrompt = result.PendingDecision;
            selectedAttackCard = human.Hand.Single(card => card.Id == action.CardId);
            targetSeat = action.TargetSeat!.Value;
        }
    }

    if (selectedGame is null || selectedPrompt is null || selectedAttackCard is null || targetSeat < 0)
    {
        throw new InvalidOperationException("No deterministic cross-seat Yuanhu trigger was found.");
    }

    var gameWithYuanhu = selectedGame!;
    var prompt = selectedPrompt!;
    var targetBefore = gameWithYuanhu.CreateSnapshot(0, revealAll: true).Players
        .Single(player => player.Seat == targetSeat);
    var ownerBefore = gameWithYuanhu.CreateSnapshot(0).Players.Single(player => player.Seat == 0);
    Equal(DecisionKind.Yuanhu, prompt.Kind);
    Equal(0, prompt.PlayerSeat);
    Equal(targetSeat, prompt.TargetSeat);
    Equal(targetSeat, prompt.ValidTargetSeats.Single());
    True(prompt.IsPrivate);
    True(prompt.ValidCardIds.Count > 0);
    True(prompt.Choices.Any(choice =>
        choice.Parameters.GetValueOrDefault("response") == "yuanhu" &&
        choice.Targets.SequenceEqual([targetSeat])));
    True(prompt.Choices.Any(choice =>
        choice.Parameters.GetValueOrDefault("response") == "yuanhu-skip"));
    True(prompt.Choices.Where(choice =>
            choice.Parameters.GetValueOrDefault("response") == "yuanhu")
        .All(choice => choice.Cards.Count == 1 && choice.Targets.SequenceEqual([targetSeat])));
    Equal<PendingDecision?>(null, gameWithYuanhu.CreateSnapshot(targetSeat).PendingDecision);

    var opened = gameWithYuanhu.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageTriggerWindowOpenedEvent>()
        .Last();
    True(opened.Candidates.Any(candidate =>
        candidate.OwnerSeat == 0 &&
        candidate.OwnerSeat != opened.TargetSeat &&
        candidate.Skill == SkillKind.Yuanhu));

    var beforeInvalid = gameWithYuanhu.SerializeState();
    var invalid = gameWithYuanhu.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        new ChoiceId("yuanhu.fake"),
        gameWithYuanhu.Revision));
    False(invalid.Accepted);
    Equal(CommandErrorCode.InvalidChoice, invalid.Error!.Code);
    Equal(beforeInvalid, gameWithYuanhu.SerializeState());

    var useChoice = prompt.Choices.First(choice =>
        choice.Parameters.GetValueOrDefault("response") == "yuanhu");
    var discardCardId = useChoice.Cards.Single();
    var accepted = gameWithYuanhu.Submit(new AnswerPromptCommand(
        0,
        prompt.PromptId,
        useChoice.Id,
        gameWithYuanhu.Revision));
    True(accepted.Accepted);

    var targetAfter = gameWithYuanhu.CreateSnapshot(0, revealAll: true).Players
        .Single(player => player.Seat == targetSeat);
    var ownerAfter = gameWithYuanhu.CreateSnapshot(0).Players.Single(player => player.Seat == 0);
    Equal(targetBefore.Hp + 1, targetAfter.Hp);
    Equal(ownerBefore.HandCount - 1, ownerAfter.HandCount);
    True(gameWithYuanhu.Events.Any(eventItem =>
        eventItem.Payload is DamageSkillCardDiscardedEvent discarded &&
        discarded.OwnerSeat == 0 &&
        discarded.CardId == discardCardId &&
        discarded.Skill == SkillKind.Yuanhu));
    True(gameWithYuanhu.Events.Any(eventItem =>
        eventItem.Payload is RecoveryAppliedEvent recovery &&
        recovery.SourceSeat == 0 &&
        recovery.TargetSeat == targetSeat &&
        recovery.Amount == 1));
    var resolved = gameWithYuanhu.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageSkillResolvedEvent>()
        .Single(eventItem => eventItem.OwnerSeat == 0 && eventItem.Skill == SkillKind.Yuanhu);
    True(resolved.Used);
    Equal<int?>(targetSeat, resolved.EffectTargetSeat);
    True(gameWithYuanhu.CardMovements.Any(movement =>
        movement.CardId == discardCardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.YuanhuDiscard));

    var ordinaryViewer = Enumerable.Range(0, gameWithYuanhu.PlayerCount)
        .First(seat => seat != 0 && seat != targetSeat);
    Equal<PendingDecision?>(null, gameWithYuanhu.CreateSnapshot(ordinaryViewer).PendingDecision);
    False(SnapshotJson.Serialize(gameWithYuanhu.CreateSnapshot(ordinaryViewer))
        .Contains($"\"Id\": {discardCardId},", StringComparison.Ordinal));
    AssertCardInventory(gameWithYuanhu);
}

static void WushengFlow()
{
    GameEngine? selectedGame = null;
    CardSnapshot? selectedCard = null;
    int selectedTargetSeat = -1;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                MaxTurns = 180
            },
            StandardContentRegistry.Create());
        var result = game.Start();
        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var revealed = game.CreateSnapshot(0, revealAll: true);
        var human = revealed.Players.Single(player => player.Seat == 0);
        if (human.Skill != SkillKind.Wusheng)
        {
            continue;
        }

        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
        {
            if (candidate.Kind != LegalActionKind.Slash ||
                candidate.PlayedCardKind != CardKind.Slash ||
                candidate.CardId is not { } cardId ||
                candidate.TargetSeat is not { } targetSeat)
            {
                return false;
            }

            var card = human.Hand.Single(handCard => handCard.Id == cardId);
            var target = revealed.Players.Single(player => player.Seat == targetSeat);
            return !IsSlashCard(card.Kind) &&
                   card.Suit is (Suit.Heart or Suit.Diamond) &&
                   target.IsAlive &&
                   target.Hp > 1 &&
                   target.Skill == SkillKind.None &&
                   target.Hand.All(handCard => handCard.Kind != CardKind.Dodge);
        });
        if (action is not null)
        {
            selectedGame = game;
            selectedCard = human.Hand.Single(card => card.Id == action.CardId);
            selectedTargetSeat = action.TargetSeat!.Value;
        }
    }

    if (selectedGame is null || selectedCard is null || selectedTargetSeat < 0)
    {
        throw new InvalidOperationException("No deterministic Wusheng red-card Slash boundary was found.");
    }

    var gameWithWusheng = selectedGame!;
    var convertedCard = selectedCard!;
    var targetBefore = gameWithWusheng.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == selectedTargetSeat);
    var sourceBefore = gameWithWusheng.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == 0);
    var actionAfterStart = gameWithWusheng.GetHumanLegalActions().Single(action =>
        action.CardId == convertedCard.Id &&
        action.TargetSeat == selectedTargetSeat &&
        action.PlayedCardKind == CardKind.Slash);

    var prompt = gameWithWusheng.PendingDecision!;
    var accepted = gameWithWusheng.Submit(new PlayCardCommand(
        ActorSeat: 0,
        CardId: convertedCard.Id,
        TargetSeats: [selectedTargetSeat],
        ExpectedRevision: gameWithWusheng.Revision,
        PromptId: prompt.PromptId,
        PlayedCardKind: CardKind.Slash));
    True(accepted.Accepted);
    var resultAfterUse = accepted.State;
    Equal(EngineStatus.AwaitingHumanPlay, resultAfterUse.Status);
    Equal(0, resultAfterUse.ProcessingCardCount);
    Equal(sourceBefore.HandCount - 1, gameWithWusheng.State.Players.Single(player => player.Seat == 0).HandCount);
    var targetAfter = gameWithWusheng.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == selectedTargetSeat);
    Equal(targetBefore.Hp - 1, targetAfter.Hp);

    var declared = gameWithWusheng.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUseDeclaredEvent>()
        .Single(eventItem => eventItem.CardId == convertedCard.Id);
    Equal(CardKind.Slash, declared.CardKind);
    var used = gameWithWusheng.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUsedEvent>()
        .Single(eventItem => eventItem.CardId == convertedCard.Id);
    Equal(CardKind.Slash, used.CardKind);
    var damage = gameWithWusheng.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageRequestedEvent>()
        .Single(eventItem => eventItem.SourceSeat == 0 && eventItem.TargetSeat == selectedTargetSeat);
    Equal(CardKind.Slash, damage.SourceCard);
    var finished = gameWithWusheng.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUseFinishedEvent>()
        .Single(eventItem => eventItem.CardId == convertedCard.Id);
    Equal(CardKind.Slash, finished.CardKind);
    TrueWithMessage(gameWithWusheng.CardMovements.Any(movement =>
        movement.CardId == convertedCard.Id &&
        movement.CardKind == convertedCard.Kind &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use), "Wusheng keeps physical card movement");
    TrueWithMessage(gameWithWusheng.CardMovements.Any(movement =>
        movement.CardId == convertedCard.Id &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished), "Wusheng finishes physical card");
    TrueWithMessage(actionAfterStart.Description.Contains("当作【杀】", StringComparison.Ordinal),
        "Wusheng action exposes card conversion");
    AssertCardInventory(gameWithWusheng);
}

static void LongdanFlow()
{
    GameEngine? selectedGame = null;
    CardSnapshot? selectedCard = null;
    var selectedTargetSeat = -1;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                MaxTurns = 180
            },
            StandardContentRegistry.Create());
        var result = game.Start();
        if (result.Status != EngineStatus.AwaitingHumanPlay)
        {
            continue;
        }

        var revealed = game.CreateSnapshot(0, revealAll: true);
        var human = revealed.Players.Single(player => player.Seat == 0);
        if (human.Skill != SkillKind.Longdan || HasSkill(game, SkillKind.Yuanhu))
        {
            continue;
        }

        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
        {
            if (candidate.Kind != LegalActionKind.Slash ||
                candidate.PlayedCardKind != CardKind.Slash ||
                candidate.CardId is not { } cardId ||
                candidate.TargetSeat is not { } targetSeat)
            {
                return false;
            }

            var card = human.Hand.Single(handCard => handCard.Id == cardId);
            var target = revealed.Players.Single(player => player.Seat == targetSeat);
            return card.Kind == CardKind.Dodge &&
                   target.IsAlive &&
                   target.Hp > 1 &&
                   target.Skill == SkillKind.None &&
                   target.Hand.All(handCard => handCard.Kind != CardKind.Dodge);
        });
        if (action is not null)
        {
            selectedGame = game;
            selectedCard = human.Hand.Single(card => card.Id == action.CardId);
            selectedTargetSeat = action.TargetSeat!.Value;
        }
    }

    if (selectedGame is null || selectedCard is null || selectedTargetSeat < 0)
    {
        throw new InvalidOperationException("No deterministic Longdan Dodge-to-Slash boundary was found.");
    }

    var gameWithLongdan = selectedGame!;
    var convertedCard = selectedCard!;
    var targetBefore = gameWithLongdan.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == selectedTargetSeat);
    var sourceBefore = gameWithLongdan.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == 0);
    var actionAfterStart = gameWithLongdan.GetHumanLegalActions().Single(action =>
        action.CardId == convertedCard.Id &&
        action.TargetSeat == selectedTargetSeat &&
        action.PlayedCardKind == CardKind.Slash);

    var prompt = gameWithLongdan.PendingDecision!;
    var accepted = gameWithLongdan.Submit(new PlayCardCommand(
        ActorSeat: 0,
        CardId: convertedCard.Id,
        TargetSeats: [selectedTargetSeat],
        ExpectedRevision: gameWithLongdan.Revision,
        PromptId: prompt.PromptId,
        PlayedCardKind: CardKind.Slash));
    TrueWithMessage(accepted.Accepted, "Longdan Dodge-to-Slash command accepted");
    Equal(EngineStatus.AwaitingHumanPlay, accepted.State.Status);
    Equal(0, accepted.State.ProcessingCardCount);
    Equal(sourceBefore.HandCount - 1,
        gameWithLongdan.State.Players.Single(player => player.Seat == 0).HandCount);
    var targetAfter = gameWithLongdan.CreateSnapshot(0, revealAll: true)
        .Players.Single(player => player.Seat == selectedTargetSeat);
    Equal(targetBefore.Hp - 1, targetAfter.Hp);

    var declared = gameWithLongdan.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUseDeclaredEvent>()
        .Single(eventItem => eventItem.CardId == convertedCard.Id);
    Equal(CardKind.Slash, declared.CardKind);
    var used = gameWithLongdan.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUsedEvent>()
        .Single(eventItem => eventItem.CardId == convertedCard.Id);
    Equal(CardKind.Slash, used.CardKind);
    var damage = gameWithLongdan.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DamageRequestedEvent>()
        .Single(eventItem => eventItem.SourceSeat == 0 && eventItem.TargetSeat == selectedTargetSeat);
    Equal(CardKind.Slash, damage.SourceCard);
    var finished = gameWithLongdan.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<CardUseFinishedEvent>()
        .Single(eventItem => eventItem.CardId == convertedCard.Id);
    Equal(CardKind.Slash, finished.CardKind);
    TrueWithMessage(gameWithLongdan.CardMovements.Any(movement =>
        movement.CardId == convertedCard.Id &&
        movement.CardKind == convertedCard.Kind &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use), "Longdan keeps physical card movement");
    TrueWithMessage(gameWithLongdan.CardMovements.Any(movement =>
        movement.CardId == convertedCard.Id &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished), "Longdan finishes physical card");
    TrueWithMessage(actionAfterStart.Description.Contains("当作【杀】", StringComparison.Ordinal),
        "Longdan action exposes card conversion");
    AssertCardInventory(gameWithLongdan);
}

static void LongdanResponseFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? selectedPrompt = null;
    PromptChoice? selectedChoice = null;
    CardSnapshot? selectedResponseCard = null;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(
            new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                MaxTurns = 180
            },
            StandardContentRegistry.Create());
        var result = game.Start();
        var humanAtStart = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        if (result.Status != EngineStatus.AwaitingHumanPlay || humanAtStart.Skill != SkillKind.Longdan)
        {
            continue;
        }

        var steps = 0;
        while (result.Status != EngineStatus.Completed && steps++ < 2_000)
        {
            if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                var prompt = game.PendingDecision ??
                    throw new InvalidOperationException("The response status has no prompt.");
                if (prompt is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge })
                {
                    var human = game.CreateSnapshot(0, revealAll: true).Players
                        .Single(player => player.Seat == 0);
                    var choice = prompt.Choices.FirstOrDefault(candidate =>
                        candidate.Parameters.GetValueOrDefault("response") == "dodge" &&
                        candidate.Parameters.GetValueOrDefault("response-card-kind") == nameof(CardKind.Dodge) &&
                        candidate.Cards.Count == 1 &&
                        human.Hand.Single(card => card.Id == candidate.Cards[0]).Kind is
                            CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash);
                    if (choice is not null)
                    {
                        selectedGame = game;
                        selectedPrompt = prompt;
                        selectedChoice = choice;
                        selectedResponseCard = human.Hand.Single(card => card.Id == choice.Cards[0]);
                        break;
                    }
                }

                result = prompt.Kind switch
                {
                    DecisionKind.RespondSlash => game.HumanRespondSlash(
                        useSlash: false,
                        advanceToHumanBoundary: false),
                    DecisionKind.RespondDodge => game.HumanRespond(
                        useDodge: false,
                        advanceToHumanBoundary: false),
                    DecisionKind.Feedback => game.HumanRespondFeedback(
                        useFeedback: false,
                        advanceToHumanBoundary: false),
                    _ => throw new InvalidOperationException(
                        $"Unexpected human response prompt {prompt.Kind} while searching for Longdan.")
                };
                continue;
            }

            result = result.Status switch
            {
                EngineStatus.AwaitingHumanPlay => game.HumanEndPlay(advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanDying => game.HumanRespondDying(
                    usePeach: false,
                    advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
                _ => game.AdvanceOneStep()
            };
        }
    }

    if (selectedGame is null || selectedPrompt is null ||
        selectedChoice is null || selectedResponseCard is null)
    {
        throw new InvalidOperationException("No deterministic Longdan Slash-to-Dodge response boundary was found.");
    }

    var gameWithLongdan = selectedGame!;
    var promptAtBoundary = selectedPrompt!;
    var responseChoice = selectedChoice!;
    var responseCard = selectedResponseCard!;
    Equal(DecisionKind.RespondDodge, promptAtBoundary.Kind);
    Equal(CardKind.Dodge, promptAtBoundary.RequiredCardKind);
    Equal("dodge", responseChoice.Parameters["response"]);
    Equal(nameof(CardKind.Dodge), responseChoice.Parameters["response-card-kind"]);
    TrueWithMessage(responseChoice.Description.Contains("当作【闪】", StringComparison.Ordinal),
        "Longdan response choice exposes card conversion");

    var accepted = gameWithLongdan.Submit(new AnswerPromptCommand(
        ActorSeat: 0,
        Prompt: promptAtBoundary.PromptId,
        Choice: responseChoice.Id,
        ExpectedRevision: gameWithLongdan.Revision));
    TrueWithMessage(accepted.Accepted, "Longdan Slash-to-Dodge response accepted");
    TrueWithMessage(gameWithLongdan.Events.Any(eventItem =>
        eventItem.Payload is CardRespondedEvent responded &&
        responded.CardId == responseCard.Id &&
        responded.ResponderSeat == 0 &&
        responded.EffectiveCardKind == CardKind.Dodge),
        "Longdan response exposes the effective Dodge kind");
    TrueWithMessage(gameWithLongdan.CardMovements.Any(movement =>
        movement.CardId == responseCard.Id &&
        movement.CardKind == responseCard.Kind &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Respond),
        "Longdan response enters Processing with the physical card");
    TrueWithMessage(gameWithLongdan.CardMovements.Any(movement =>
        movement.CardId == responseCard.Id &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.ResponseFinished),
        "Longdan response finishes the physical card");
    Equal(CardLocation.DiscardPile, gameWithLongdan.CreateCardZoneDiagnostics()
        .Single(card => card.CardId == responseCard.Id).Location);
    AssertCardInventory(gameWithLongdan);
}

static void DyingAlcoholRescueFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? dyingPrompt = null;
    EngineRunResult result = null!;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        result = game.Start();
        var steps = 0;
        while (result.Status != EngineStatus.Completed && steps++ < 3_000)
        {
            if (result.Status == EngineStatus.AwaitingHumanDying)
            {
                var prompt = game.PendingDecision;
                if (prompt is { TargetSeat: 0 } &&
                    prompt.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "alcohol"))
                {
                    selectedGame = game;
                    dyingPrompt = prompt;
                    break;
                }
            }

            result = result.Status switch
            {
                EngineStatus.AwaitingHumanPlay => game.HumanEndPlay(advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanResponse => game.PendingDecision?.Kind == DecisionKind.RespondSlash
                    ? game.HumanRespondSlash(useSlash: false, advanceToHumanBoundary: false)
                    : game.HumanRespond(useDodge: false, advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanDying => game.HumanRespondDying(
                    usePeach: false,
                    advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
                _ => game.AdvanceOneStep()
            };
        }
    }

    if (selectedGame is null || dyingPrompt is null)
    {
        throw new InvalidOperationException("No deterministic human dying prompt with a self-rescue Alcohol was found.");
    }

    var gameWithDying = selectedGame!;
    var promptAtBoundary = dyingPrompt!;
    var dyingFrame = gameWithDying.ResolutionStack.OfType<DyingFrame>().Single();
    var alcoholChoice = promptAtBoundary.Choices
        .Single(choice => choice.Parameters.GetValueOrDefault("response") == "alcohol");
    var alcoholCardId = alcoholChoice.Cards.Single();
    Equal(EngineStatus.AwaitingHumanDying, gameWithDying.State.Status);
    Equal(0, promptAtBoundary.PlayerSeat);
    Equal(0, promptAtBoundary.TargetSeat);
    TrueWithMessage(alcoholChoice.Description.Contains("自救", StringComparison.Ordinal), "Alcohol self-rescue choice");
    TrueWithMessage(
        promptAtBoundary.Choices.All(choice =>
            choice.Parameters.GetValueOrDefault("response") != "alcohol" ||
            choice.Targets.Count == 0),
        "Alcohol rescue has no other target");

    var otherViewer = gameWithDying.CreateSnapshot(1);
    Equal<PendingDecision?>(null, otherViewer.PendingDecision);
    TrueWithMessage(
        otherViewer.Players.All(player => player.Hand.All(card => card.Id != alcoholCardId)),
        "other viewer hides dying Alcohol");

    var beforeInvalid = gameWithDying.SerializeState();
    var invalid = gameWithDying.Submit(new AnswerPromptCommand(
        0,
        promptAtBoundary.PromptId,
        new ChoiceId("dying.fake-alcohol"),
        gameWithDying.Revision));
    TrueWithMessage(!invalid.Accepted, "invalid Alcohol dying choice rejected");
    Equal(CommandErrorCode.InvalidChoice, invalid.Error!.Code);
    Equal(beforeInvalid, gameWithDying.SerializeState());

    var accepted = gameWithDying.Submit(new AnswerPromptCommand(
        0,
        promptAtBoundary.PromptId,
        alcoholChoice.Id,
        gameWithDying.Revision));
    TrueWithMessage(accepted.Accepted, "Alcohol dying choice accepted");
    var response = gameWithDying.Events
        .Select(eventItem => eventItem.Payload)
        .OfType<DyingResponseEvent>()
        .Single(eventItem => eventItem.ResolutionId == dyingFrame.Id);
    TrueWithMessage(
        response.UsedAlcohol &&
        response.AlcoholCardId == alcoholCardId &&
        !response.UsedPeach &&
        response.PeachCardId is null,
        "typed Alcohol dying response");
    TrueWithMessage(gameWithDying.Events.Any(eventItem =>
        eventItem.Payload is RecoveryAppliedEvent recovery &&
        recovery.SourceSeat == 0 &&
        recovery.TargetSeat == 0 &&
        recovery.Amount == 1 &&
        recovery.RemainingHp == 1), "Alcohol recovery event");
    TrueWithMessage(gameWithDying.Events.Any(eventItem =>
        eventItem.Payload is CardUseFinishedEvent finished &&
        finished.CardId == alcoholCardId &&
        finished.CardKind == CardKind.Alcohol), "Alcohol recovery card finished");
    TrueWithMessage(gameWithDying.Events.Any(eventItem =>
        eventItem.Payload is DyingResolvedEvent resolved &&
        resolved.ResolutionId == dyingFrame.Id &&
        resolved.Survived), "Alcohol dying resolution survived");
    TrueWithMessage(gameWithDying.CardMovements.Any(movement =>
        movement.CardId == alcoholCardId &&
        movement.From == CardLocation.Hand(0) &&
        movement.To == CardLocation.Processing &&
        movement.Reason == CardMoveReasons.Use), "Alcohol rescue entered Processing");
    TrueWithMessage(gameWithDying.CardMovements.Any(movement =>
        movement.CardId == alcoholCardId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished), "Alcohol rescue discarded");
    Equal(1, accepted.State.Players.Single(player => player.Seat == 0).Hp);
    Equal(
        CardLocation.DiscardPile,
        gameWithDying.CreateCardZoneDiagnostics().Single(card => card.CardId == alcoholCardId).Location);
    TrueWithMessage(
        gameWithDying.ResolutionStack.All(frame => frame.Id != dyingFrame.Id),
        "Alcohol dying frame completed");
    AssertCardInventory(gameWithDying);
}

static void DyingResponseFlow()
{
    GameEngine? selectedGame = null;
    PendingDecision? dyingPrompt = null;
    EngineRunResult result = null!;
    for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        result = game.Start();
        var steps = 0;
        while (result.Status != EngineStatus.Completed && steps++ < 2_000)
        {
            if (result.Status == EngineStatus.AwaitingHumanDying)
            {
                var prompt = game.PendingDecision;
                if (prompt is { Choices.Count: > 1 } &&
                    prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") == "peach"))
                {
                    selectedGame = game;
                    dyingPrompt = prompt;
                    break;
                }
            }

            result = result.Status switch
            {
                EngineStatus.AwaitingHumanPlay => game.HumanEndPlay(advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanResponse => game.PendingDecision?.Kind == DecisionKind.RespondSlash
                    ? game.HumanRespondSlash(useSlash: false, advanceToHumanBoundary: false)
                    : game.HumanRespond(useDodge: false, advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanDying => game.HumanRespondDying(usePeach: false, advanceToHumanBoundary: false),
                EngineStatus.AwaitingHumanCardSelection => ResolveFirstHarvestChoice(game),
                _ => game.AdvanceOneStep()
            };
        }
    }

    if (selectedGame is null || dyingPrompt is null)
    {
        throw new InvalidOperationException("No human dying prompt with a Peach was found in the deterministic seed search.");
    }
    var gameWithDying = selectedGame!;
    var promptAtBoundary = dyingPrompt!;
    Equal(EngineStatus.AwaitingHumanDying, gameWithDying.State.Status);
    Equal(DecisionKind.RescueDying, promptAtBoundary.Kind);
    Equal(promptAtBoundary.TargetSeat, promptAtBoundary.SourceSeat);
    TrueWithMessage(promptAtBoundary.Choices.Any(choice => choice.Parameters["response"] == "peach"), "peach choice");
    TrueWithMessage(gameWithDying.ResolutionStack[^1] is DyingFrame dyingFrame &&
         gameWithDying.ResolutionStack[^2] is DamageFrame damageFrame &&
         dyingFrame.ParentFrameId == damageFrame.Id, "dying stack");
    var serializedDyingStack = JsonSerializer.Serialize(gameWithDying.ResolutionStack);
    TrueWithMessage(serializedDyingStack.Contains("dying", StringComparison.Ordinal), "dying stack serialization");

    var otherViewer = gameWithDying.CreateSnapshot(1);
    Equal<PendingDecision?>(null, otherViewer.PendingDecision);
    var hiddenPeachId = promptAtBoundary.Choices
        .First(choice => choice.Parameters["response"] == "peach")
        .Cards.Single();
    TrueWithMessage(otherViewer.Players.All(player => player.Hand.All(card => card.Id != hiddenPeachId)), "other viewer hides peach");

    var beforeInvalidChoice = gameWithDying.SerializeState();
    var invalidChoice = gameWithDying.Submit(new AnswerPromptCommand(
        gameWithDying.State.HumanSeat,
        promptAtBoundary.PromptId,
        new ChoiceId("dying.fake"),
        gameWithDying.Revision));
    TrueWithMessage(!invalidChoice.Accepted, "invalid dying choice rejected");
    Equal(CommandErrorCode.InvalidChoice, invalidChoice.Error!.Code);
    Equal(beforeInvalidChoice, gameWithDying.SerializeState());

    var originalRevision = gameWithDying.Revision;
    var peachChoice = promptAtBoundary.Choices
        .First(choice => choice.Parameters["response"] == "peach");
    var rescued = gameWithDying.Submit(new AnswerPromptCommand(
        gameWithDying.State.HumanSeat,
        promptAtBoundary.PromptId,
        peachChoice.Id,
        originalRevision));
    TrueWithMessage(rescued.Accepted, "peach command accepted");
    TrueWithMessage(gameWithDying.Events.Any(eventItem =>
        eventItem.Payload is DyingResponseEvent response &&
        response.UsedPeach && response.PeachCardId == hiddenPeachId), "dying peach event");
    TrueWithMessage(gameWithDying.Events.Any(eventItem =>
        eventItem.Payload is DyingResolvedEvent resolved && resolved.Survived), "dying survived event");
    TrueWithMessage(gameWithDying.ResolutionStack.All(frame =>
        frame is not DyingFrame dying || dying.VictimSeat != promptAtBoundary.TargetSeat),
        "completed dying frame removed");
    TrueWithMessage(gameWithDying.CardMovements.Any(movement =>
        movement.CardId == hiddenPeachId &&
        movement.From == CardLocation.Processing &&
        movement.To == CardLocation.DiscardPile &&
        movement.Reason == CardMoveReasons.UseFinished),
        "rescue Peach discarded through the recovery resolution");
    TrueWithMessage(rescued.State.Players.Single(player => player.Seat == promptAtBoundary.TargetSeat).Hp > 0,
        "dying victim survives");
}

static void ObserverFailuresAreIsolated()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        Seed = 337,
        HumanSeat = 0,
        HumanRole = Role.Lord,
        MaxTurns = 100
    });
    var deliveredLogs = 0;
    var deliveredThoughts = 0;
    var deliveredStates = 0;
    var deliveredMovements = 0;

    game.LogAdded += _ => throw new InvalidOperationException("log observer failed");
    game.LogAdded += _ => deliveredLogs++;
    game.AiThoughtAdded += _ => throw new InvalidOperationException("thought observer failed");
    game.AiThoughtAdded += _ => deliveredThoughts++;
    game.StateChanged += _ => throw new InvalidOperationException("state observer failed");
    game.StateChanged += _ => deliveredStates++;
    game.CardMoved += _ => throw new InvalidOperationException("movement observer failed");
    game.CardMoved += _ => deliveredMovements++;

    var result = game.Start();
    Equal(EngineStatus.AwaitingHumanPlay, result.Status);
    True(deliveredLogs > 0);
    True(deliveredStates > 0);
    True(deliveredMovements > 0);

    result = game.HumanEndPlay(advanceToHumanBoundary: false);
    for (var step = 0; step < 8 && deliveredThoughts == 0; step++)
    {
        result = game.AdvanceOneStep();
    }

    True(deliveredThoughts > 0);
    True(game.ObserverFailures.Any(failure => failure.NotificationType == nameof(game.LogAdded)));
    True(game.ObserverFailures.Any(failure => failure.NotificationType == nameof(game.AiThoughtAdded)));
    True(game.ObserverFailures.Any(failure => failure.NotificationType == nameof(game.StateChanged)));
    True(game.ObserverFailures.Any(failure => failure.NotificationType == nameof(game.CardMoved)));
    AssertCardInventory(game);
    True(result.Status != EngineStatus.NotStarted);
}

static void ObserverFailuresDoNotChangeOutcome()
{
    var options = new GameOptions
    {
        Seed = 359,
        HumanSeat = -1,
        HumanRole = null,
        MaxTurns = 200
    };
    var baseline = GameEngine.CreateStandard(options);
    var faulted = GameEngine.CreateStandard(options);
    faulted.LogAdded += _ => throw new InvalidOperationException("log observer failed");
    faulted.AiThoughtAdded += _ => throw new InvalidOperationException("thought observer failed");
    faulted.StateChanged += _ => throw new InvalidOperationException("state observer failed");
    faulted.CardMoved += _ => throw new InvalidOperationException("movement observer failed");

    var baselineResult = baseline.Start();
    var faultedResult = faulted.Start();

    Equal(baselineResult.Winner, faultedResult.Winner);
    Equal(baseline.SerializeState(revealAll: true), faulted.SerializeState(revealAll: true));
    Equal(JsonSerializer.Serialize(baseline.Log), JsonSerializer.Serialize(faulted.Log));
    Equal(JsonSerializer.Serialize(baseline.AiThoughts), JsonSerializer.Serialize(faulted.AiThoughts));
    Equal(JsonSerializer.Serialize(baseline.CardMovements), JsonSerializer.Serialize(faulted.CardMovements));
    True(faulted.ObserverFailures.Count > 0);
    AssertCardInventory(faulted);
}

static void UncaughtObserverReentryIsIsolated()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        Seed = 347,
        HumanSeat = 0,
        HumanRole = Role.Lord
    });
    var laterObserverCalls = 0;
    game.LogAdded += _ => game.Advance();
    game.LogAdded += _ => laterObserverCalls++;

    var result = game.Start();

    Equal(EngineStatus.AwaitingHumanPlay, result.Status);
    True(laterObserverCalls > 0);
    True(game.ObserverFailures.Any(failure =>
        failure.ExceptionType.Contains(nameof(InvalidOperationException), StringComparison.Ordinal) &&
        failure.Message.Contains("reentrantly", StringComparison.Ordinal)));
    AssertCardInventory(game);
}

static void HumanPlayApi()
{
    GameEngine? selectedGame = null;
    LegalAction? selectedAction = null;

    // Search a small deterministic seed range instead of coupling the test to one deck order.
    for (var seed = 1; seed <= 64 && selectedAction is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, HumanSeat = 0, HumanRole = Role.Lord });
        var result = game.Start();
        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            candidate.Kind is LegalActionKind.Slash or LegalActionKind.Peach);
        if (result.Status == EngineStatus.AwaitingHumanPlay && action is not null)
        {
            selectedGame = game;
            selectedAction = action;
        }
    }

    NotNull(selectedGame);
    NotNull(selectedAction);
    var next = selectedGame!.HumanPlay(selectedAction!.CardId!.Value, selectedAction.TargetSeat);
    True(next.Status is EngineStatus.AwaitingHumanPlay or
        EngineStatus.AwaitingHumanResponse or
        EngineStatus.AwaitingHumanDying or
        EngineStatus.AwaitingHumanCardSelection or
        EngineStatus.Completed);
    True(selectedGame.Log.Any(entry => entry.Type is "CardUsed" or "Recovered"));
}

static void HumanDodgeApi()
{
    GameEngine? selectedGame = null;
    for (var seed = 1; seed <= 4_096 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 150
        });
        var result = game.Start();
        var decisions = 0;
        while (result.Status != EngineStatus.Completed && decisions++ < 400)
        {
            if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                if (result.PendingDecision?.Kind == DecisionKind.RespondDodge)
                {
                    selectedGame = game;
                    break;
                }

                result = result.PendingDecision?.Kind == DecisionKind.RespondSlash
                    ? game.HumanRespondSlash(useSlash: false)
                    : game.HumanRespond(useDodge: false);
                continue;
            }

            result = result.Status == EngineStatus.AwaitingHumanPlay
                ? game.HumanEndPlay()
                : game.AdvanceOneStep();
        }
    }

    NotNull(selectedGame);
    var before = selectedGame!.State.Players.Single(player => player.Seat == 0);
    True(before.Hand.Any(card => card.Kind == CardKind.Dodge));
    var resultAfterDodge = selectedGame.HumanRespond(
        useDodge: true,
        advanceToHumanBoundary: false);
    var after = resultAfterDodge.State.Players.Single(player => player.Seat == 0);
    Equal(before.Hp, after.Hp);
    True(selectedGame.Log.Any(entry => entry.Type == "CardResponded" && entry.ActorSeat == 0));
}

static void LethalHumanResponseKeepsCompletedStatus()
{
    var exercised = false;
    for (var seed = 1; seed <= 128 && !exercised; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        var completedCardTotals = new List<int>();
        game.StateChanged += snapshot =>
        {
            if (snapshot.Status == EngineStatus.Completed)
            {
                completedCardTotals.Add(
                    snapshot.DrawPileCount +
                    snapshot.DiscardPileCount +
                    snapshot.ProcessingCardCount +
                    snapshot.Players.Sum(player => player.HandCount + player.Equipment.Count));
            }
        };
        var result = game.Start();
        var decisions = 0;
        while (result.Status != EngineStatus.Completed && decisions++ < 500)
        {
            if (result.Status == EngineStatus.AwaitingHumanPlay)
            {
                result = game.HumanEndPlay();
                continue;
            }

            if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                if (game.PendingDecision?.Kind == DecisionKind.RespondSlash)
                {
                    result = game.HumanRespondSlash(useSlash: false);
                    continue;
                }

                var hpBefore = game.State.Players.Single(player => player.Seat == 0).Hp;
                result = game.HumanRespond(useDodge: false);
                if (hpBefore == 1)
                {
                    if (result.Status == EngineStatus.AwaitingHumanDying)
                    {
                        result = game.HumanRespondDying(usePeach: false);
                    }

                    if (result.Status == EngineStatus.Completed)
                    {
                        True(result.Winner is Winner.Rebels or Winner.Renegade);
                        Equal(EngineStatus.Completed, game.State.Status);
                        Equal(1, completedCardTotals.Count);
                        Equal(78, completedCardTotals[0]);
                        exercised = true;
                    }
                }

                continue;
            }

            if (result.Status == EngineStatus.AwaitingHumanDying)
            {
                result = game.HumanRespondDying(usePeach: false);
                continue;
            }

            if (result.Status == EngineStatus.AwaitingHumanCardSelection)
            {
                result = ResolveFirstHarvestChoice(game);
            }
        }
    }

    True(exercised);
}

static void AdvanceOneStepApi()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        Seed = 31,
        HumanSeat = 0,
        HumanRole = Role.Lord
    });

    Equal(EngineStatus.AwaitingHumanPlay, game.Start().Status);
    var afterHuman = game.HumanEndPlay(advanceToHumanBoundary: false);
    Equal(EngineStatus.Running, afterHuman.Status);
    Equal(TurnPhase.Discard, afterHuman.State.Phase);

    var thoughtsBefore = game.AiThoughts.Count;
    var afterDiscard = game.AdvanceOneStep();
    Equal(TurnPhase.NotStarted, afterDiscard.State.Phase);
    Equal(thoughtsBefore, game.AiThoughts.Count);

    var afterAiTurnStart = game.AdvanceOneStep();
    Equal(TurnPhase.Play, afterAiTurnStart.State.Phase);
    Equal(thoughtsBefore, game.AiThoughts.Count);

    game.AdvanceOneStep();
    Equal(thoughtsBefore + 1, game.AiThoughts.Count);

    var observedAiDodge = false;
    var result = game.State.Status == EngineStatus.Completed
        ? new EngineRunResult(game.State.Status, game.State.Winner, game.State, game.PendingDecision)
        : new EngineRunResult(game.State.Status, game.State.Winner, game.State, game.PendingDecision);
    for (var step = 0; step < 1_000 && result.Status != EngineStatus.Completed; step++)
    {
        if (result.Status == EngineStatus.AwaitingHumanPlay)
        {
            result = game.HumanEndPlay(advanceToHumanBoundary: false);
            continue;
        }

        if (result.Status == EngineStatus.AwaitingHumanResponse)
        {
            result = game.PendingDecision?.Kind == DecisionKind.RespondSlash
                ? game.HumanRespondSlash(useSlash: false, advanceToHumanBoundary: false)
                : game.HumanRespond(useDodge: false, advanceToHumanBoundary: false);
            continue;
        }

        if (result.Status == EngineStatus.AwaitingHumanDying)
        {
            result = game.HumanRespondDying(usePeach: false, advanceToHumanBoundary: false);
            continue;
        }

        if (result.Status == EngineStatus.AwaitingHumanCardSelection)
        {
            result = ResolveFirstHarvestChoice(game);
            continue;
        }

        var before = game.AiThoughts.Count;
        result = game.AdvanceOneStep();
        var emitted = game.AiThoughts.Count - before;
        True(emitted <= 1);
        if (emitted == 1 && game.AiThoughts[^1].Decision == "打出闪")
        {
            observedAiDodge = true;
            break;
        }
    }

    True(observedAiDodge);
}

static void AiEndPlayPublishesState()
{
    GameEngine? selectedGame = null;
    EngineRunResult? selectedResult = null;
    List<GameSnapshot>? published = null;
    for (var seed = 1; seed <= 128 && selectedGame is null; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            MaxTurns = 180
        });
        game.Start();
        game.HumanEndPlay(advanceToHumanBoundary: false);
        var snapshots = new List<GameSnapshot>();
        game.StateChanged += snapshot => snapshots.Add(snapshot);
        var result = game.AdvanceOneStep();
        for (var step = 0; step < 400 &&
                          result.Status != EngineStatus.Completed &&
                          result.State.Phase != TurnPhase.Discard; step++)
        {
            if (result.Status == EngineStatus.AwaitingHumanPlay)
            {
                result = game.HumanEndPlay(advanceToHumanBoundary: false);
            }
            else if (result.Status == EngineStatus.AwaitingHumanResponse)
            {
                result = game.PendingDecision?.Kind == DecisionKind.RespondSlash
                    ? game.HumanRespondSlash(useSlash: false, advanceToHumanBoundary: false)
                    : game.HumanRespond(useDodge: false, advanceToHumanBoundary: false);
            }
            else if (result.Status == EngineStatus.AwaitingHumanDying)
            {
                result = game.HumanRespondDying(usePeach: false, advanceToHumanBoundary: false);
            }
            else if (result.Status == EngineStatus.AwaitingHumanCardSelection)
            {
                result = ResolveFirstHarvestChoice(game);
            }
            else
            {
                result = game.AdvanceOneStep();
            }
        }

        if (result.Status != EngineStatus.Completed && result.State.Phase == TurnPhase.Discard)
        {
            selectedGame = game;
            selectedResult = result;
            published = snapshots;
        }
    }

    NotNull(selectedGame);
    NotNull(selectedResult);
    NotNull(published);
    Equal(TurnPhase.Discard, selectedResult!.State.Phase);
    True(published!.Count > 0);
    Equal(
        SnapshotJson.Serialize(selectedResult.State),
        SnapshotJson.Serialize(published[^1]));
}

static void ReentrantAdvanceIsRejected()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        Seed = 71,
        HumanSeat = 0,
        HumanRole = Role.Lord
    });
    var rejected = false;
    game.LogAdded += entry =>
    {
        if (entry.Type != "CardsDrawn")
        {
            return;
        }

        try
        {
            game.Advance();
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("reentrantly", StringComparison.Ordinal))
        {
            rejected = true;
        }
    };

    var result = game.Start();
    True(rejected);
    Equal(EngineStatus.AwaitingHumanPlay, result.Status);
    Equal(Winner.None, result.Winner);
    Equal(TurnPhase.Play, result.State.Phase);
}

static void UnknownPhaseFailsFast()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        Seed = 79,
        HumanSeat = 0,
        HumanRole = Role.Lord
    });
    game.Start();
    game.HumanEndPlay(advanceToHumanBoundary: false);

    var phaseField = typeof(GameEngine).GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic);
    NotNull(phaseField);
    phaseField!.SetValue(game, (TurnPhase)999);

    Throws<InvalidOperationException>(() => game.AdvanceOneStep());
}

static void AiMatchSmoke()
{
    for (var seed = 1; seed <= 32; seed++)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = -1,
            HumanRole = null,
            MaxTurns = 250
        });

        var result = game.Start();
        Equal(EngineStatus.Completed, result.Status);
        True(result.Winner != Winner.None);
        True(game.AiThoughts.Count > 0);
        True(game.AiThoughts.All(thought => thought.Candidates.Count > 0));
    }
}

static void StepGuardFinishesResponse()
{
    var game = GameEngine.CreateStandard(new GameOptions
    {
        Seed = 617,
        HumanSeat = -1,
        HumanRole = null,
        MaxTurns = 5_559
    });

    var result = game.Start();

    Equal(EngineStatus.Completed, result.Status);
    True(result.Winner != Winner.None);
    Equal(0, result.State.ProcessingCardCount);
    Equal(0, game.ResolutionStack.Count);
    Equal(0, game.CreateCardZoneDiagnostics().Count(card => card.Location == CardLocation.Processing));
    AssertCardInventory(game);
}

static void SnapshotSerialization()
{
    var game = GameEngine.CreateStandard(new GameOptions { Seed = 123 });
    using var json = JsonDocument.Parse(game.SerializeState());
    Equal("Lord", json.RootElement.GetProperty("Players")[0].GetProperty("Role").GetString());
    Equal(8, json.RootElement.GetProperty("Players").GetArrayLength());
}

static bool IsSlashCard(CardKind kind) =>
    kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;

static bool HasSkill(GameEngine game, SkillKind skill) =>
    game.CreateSnapshot(0, revealAll: true).Players.Any(player => player.Skill == skill);

static bool IsDamageTriggerSkill(SkillKind skill) =>
    skill is SkillKind.Jianxiong or
        SkillKind.Feedback or
        SkillKind.Yiji or
        SkillKind.Jieming or
        SkillKind.Yuanhu;

static void AssertCardInventory(GameEngine game)
{
    var cards = game.CreateCardZoneDiagnostics();
    Equal(78, cards.Count);
    Equal(78, cards.Select(card => card.CardId).Distinct().Count());

    var snapshot = game.CreateSnapshot(0, revealAll: true);
    Equal(snapshot.DrawPileCount, cards.Count(card => card.Location == CardLocation.DrawPile));
    Equal(snapshot.DiscardPileCount, cards.Count(card => card.Location == CardLocation.DiscardPile));
    Equal(snapshot.ProcessingCardCount, cards.Count(card => card.Location == CardLocation.Processing));
    foreach (var player in snapshot.Players)
    {
        var handCards = cards
            .Where(card => card.Location == CardLocation.Hand(player.Seat))
            .Select(card => card.CardId)
            .OrderBy(id => id)
            .ToArray();
        var snapshotCards = player.Hand.Select(card => card.Id).OrderBy(id => id).ToArray();
        Equal(player.HandCount, handCards.Length);
        TrueWithMessage(
            handCards.SequenceEqual(snapshotCards),
            $"hand snapshot mismatch for seat {player.Seat}: zone=[{string.Join(',', handCards)}], snapshot=[{string.Join(',', snapshotCards)}]");
    }

    if (snapshot.Status == EngineStatus.Completed)
    {
        Equal(0, snapshot.ProcessingCardCount);
    }
}

static EngineRunResult ResolveFirstHarvestChoice(GameEngine game)
{
    var prompt = game.PendingDecision ??
        throw new InvalidOperationException("The test expected a human FiveGrains prompt.");
    var choice = prompt.Choices.FirstOrDefault() ??
        throw new InvalidOperationException("The test expected at least one FiveGrains choice.");
    return prompt.Kind is DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard
        ? game.HumanSelectFireAttackCard(choice.Cards.Single(), advanceToHumanBoundary: false)
        : game.HumanSelectHarvestCard(choice.Cards.Single(), advanceToHumanBoundary: false);
}

static void AssertPublishedCardTotal(GameSnapshot snapshot)
{
    Equal(
        78,
        snapshot.DrawPileCount +
        snapshot.DiscardPileCount +
        snapshot.ProcessingCardCount +
        snapshot.Players.Sum(player => player.HandCount + player.Equipment.Count));
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void True(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Expected condition to be true.");
    }
}

static void TrueWithMessage(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Expected condition to be true: {message}.");
    }
}

static void False(bool condition)
{
    if (condition)
    {
        throw new InvalidOperationException("Expected condition to be false.");
    }
}

static void NotNull(object? value)
{
    if (value is null)
    {
        throw new InvalidOperationException("Expected a non-null value.");
    }
}

static void Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name} to be thrown.");
}

sealed class SyntheticPackage : IGameContentPackage
{
    private readonly Action<IContentRegistryBuilder> _register;

    public SyntheticPackage(
        string id,
        Action<IContentRegistryBuilder> register,
        params PackageDependency[] dependencies)
    {
        _register = register;
        Manifest = new PackageManifest(id, new Version(1, 0, 0), dependencies);
    }

    public PackageManifest Manifest { get; }

    public void Register(IContentRegistryBuilder builder) => _register(builder);
}
