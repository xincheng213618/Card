using CardGame.Content.Standard;
using CardGame.Core;

internal static class SpZhaoYunChecks
{
    internal static void ConvertedSlashUseTriggersChongzhenAndReplays()
    {
        var registry = CreateRegistry();
        var game = SelectSpZhaoYun(registry, "identity:classic-sp-zhao-use-5", Role.Lord, seed: 7);
        AdvanceUntil(game, current => current.PendingDecision?.Kind == DecisionKind.PlayCard,
            "SP Zhao Yun did not reach the play phase.");
        var action = game.GetHumanLegalActions().First(candidate =>
            candidate.Kind == LegalActionKind.Slash &&
            candidate.ConversionSource is { SkillId: "sp:longdan", BindingId: "dodge-to-slash" });
        var physical = game.CreateSnapshot(0, revealAll: true).Players[0].Hand
            .Single(card => card.Id == action.CardId);
        Require(physical.Kind == CardKind.Dodge,
            "Formal SP Longdan must pay one hand Dodge for the converted Slash.");
        var accepted = game.Submit(new PlayCardCommand(
            0,
            action.CardId!.Value,
            action.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            action.PlayedCardKind)
        {
            ConversionSource = action.ConversionSource
        });
        Require(accepted.Accepted, accepted.Error?.Message ?? "Formal SP Longdan Slash was rejected.");
        var invoke = game.PendingDecision;
        Require(invoke is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, IsPrivate: true } &&
                invoke.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"),
            "Formal Chongzhen must pause against the converted Slash's final target.");
        var cardAction = game.Events.Select(item => item.Payload).OfType<CardActionAcceptedEvent>()
            .Single(item => item.Action.Type == CardActionType.Use &&
                item.Action.ConversionChain.Any(source => source.SkillId == "sp:longdan")).Action;
        Require(cardAction.ConversionChain.Single() is
                { SkillId: "sp:longdan", BindingId: "dodge-to-slash", OwnerSeat: 0 } &&
                cardAction.PhysicalCards.Single() is { CardKind: CardKind.Dodge },
            "The formal use action must retain the exact SP Longdan binding and physical Dodge.");

        var paused = game.CreateCheckpoint();
        var skipped = GameReplay.Restore(paused, registry);
        Answer(skipped, "skip");
        Require(skipped.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>()
                .Single() is { SkillId: "sp:chongzhen", Activated: false },
            "Skipping formal Chongzhen must resolve without taking a card.");

        var activated = GameReplay.Restore(paused, registry);
        var before = activated.CreateSnapshot(0, revealAll: true);
        var opponent = action.TargetSeats.Single();
        Answer(activated, "activate");
        Require(activated.PendingDecision is { Kind: DecisionKind.ProgramTrigger } take &&
                take.Choices.Count == before.Players[opponent].HandCount &&
                take.Choices.All(choice => choice.Cards.Count == 0 &&
                    choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"),
            "Formal Chongzhen must expose only opaque opponent-hand slots.");
        Answer(activated, "select-and-move-owned-card");
        var after = activated.CreateSnapshot(0, revealAll: true);
        Require(after.Players[0].HandCount == before.Players[0].HandCount + 1 &&
                after.Players[opponent].HandCount == before.Players[opponent].HandCount - 1 &&
                activated.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>()
                    .Single() is { SkillId: "sp:chongzhen", TriggerId: "after-longdan-slash-use", Activated: true },
            "Formal Chongzhen must take exactly one target hand card before Slash continuation.");
        AssertReplay(activated, registry, "Formal SP Longdan Slash and Chongzhen did not replay exactly.");
    }

    internal static void ConvertedDodgeResponseTargetsTheAttackerAndReplays()
    {
        var registry = CreateRegistry();
        GameEngine? game = null;
        for (var seed = 1; seed <= 256 && game is null; seed++)
        {
            var candidate = SelectSpZhaoYun(registry, "identity:classic-sp-zhao-response-5", Role.Rebel, seed);
            for (var step = 0; step < 240 && candidate.State.Status != EngineStatus.Completed; step++)
            {
                if (candidate.PendingDecision is { Kind: DecisionKind.RespondDodge } prompt &&
                    prompt.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("conversion-skill-id") == "sp:longdan" &&
                        choice.Parameters.GetValueOrDefault("conversion-binding-id") == "slash-to-dodge"))
                {
                    game = candidate;
                    break;
                }

                if (candidate.PendingDecision?.Kind == DecisionKind.PlayCard)
                {
                    candidate.DriveHumanEndPlay(advanceToHumanBoundary: false);
                }
                else
                {
                    var advanced = candidate.Submit(new AdvanceOneStepCommand(candidate.Revision));
                    Require(advanced.Accepted,
                        advanced.Error?.Message ?? "SP response fixture could not advance.");
                }
            }
        }

        Require(game is not null, "No bounded formal SP Longdan Dodge response fixture was found.");
        var response = game!.PendingDecision!;
        var choice = response.Choices.First(item =>
            item.Parameters.GetValueOrDefault("conversion-skill-id") == "sp:longdan" &&
            item.Parameters.GetValueOrDefault("conversion-binding-id") == "slash-to-dodge");
        var physicalId = choice.Cards.Single();
        Require(game.CreateSnapshot(0, revealAll: true).Players[0].Hand
                .Single(card => card.Id == physicalId).Kind == CardKind.FireSlash,
            "Formal SP Longdan must also accept one hand Fire Slash for the Dodge response.");
        var accepted = game.Submit(new AnswerPromptCommand(
            0,
            response.PromptId,
            choice.Id,
            game.Revision));
        Require(accepted.Accepted, accepted.Error?.Message ?? "Formal SP Longdan Dodge was rejected.");
        var invoke = game.PendingDecision;
        Require(invoke is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, IsPrivate: true } &&
                invoke.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate") &&
                game.State.ProcessingCardCount >= 1,
            "Formal Chongzhen response must target the Slash user while the paid card remains in Processing.");
        var cardAction = game.Events.Select(item => item.Payload).OfType<CardActionAcceptedEvent>()
            .Single(item => item.Action.Type == CardActionType.Response &&
                item.Action.ConversionChain.Any(source => source.SkillId == "sp:longdan")).Action;
        Require(cardAction.OpponentSeat == response.SourceSeat &&
                cardAction.EffectiveKind == CardKind.Dodge &&
                cardAction.PhysicalCards.Single() is { CardId: var paid, CardKind: CardKind.FireSlash } &&
                paid == physicalId &&
                cardAction.ConversionChain.Single() is
                    { SkillId: "sp:longdan", BindingId: "slash-to-dodge", OwnerSeat: 0 },
            "The formal response action must retain its attacker, physical Slash and exact SP Longdan source.");
        Answer(game, "activate");
        Answer(game, "select-and-move-owned-card");
        Require(game.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>()
                .Single() is { SkillId: "sp:chongzhen", TriggerId: "after-longdan-dodge-response", Activated: true },
            "Formal response Chongzhen must resolve once for the attacker.");
        AssertReplay(game, registry, "Formal SP Longdan response and Chongzhen did not replay exactly.");
    }

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new FixturePackage());

    private static GameEngine SelectSpZhaoYun(ContentRegistry registry, string modeId, Role role, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            HumanSeat = 0,
            HumanRole = role,
            PlayerCount = 5,
            ModeId = modeId,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 40
        }, registry);
        var started = game.Submit(new StartGameCommand());
        Require(started.Accepted, started.Error?.Message ?? "SP Zhao Yun fixture failed to start.");
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("The formal SP Zhao Yun fixture did not publish a selection prompt.");
        Require(prompt is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } &&
                prompt.ValidContentIds.Contains("sp:zhao-yun"),
            "The formal SP Zhao Yun fixture did not publish the configured general.");
        var selected = game.Submit(new SelectGeneralCommand(
            0,
            "sp:zhao-yun",
            game.Revision,
            prompt.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "SP Zhao Yun could not be selected.");
        return game;
    }

    private static void AdvanceUntil(GameEngine game, Func<GameEngine, bool> predicate, string failure)
    {
        for (var step = 0; step < 80 && !predicate(game); step++)
        {
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? failure);
        }
        Require(predicate(game), failure);
    }

    private static void Answer(GameEngine game, string action)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("SP Zhao Yun prompt was lost.");
        var choice = prompt.Choices.First(item => item.Parameters.GetValueOrDefault("program-action") == action);
        var answered = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? $"SP Zhao Yun prompt action '{action}' was rejected.");
    }

    private static void AssertReplay(GameEngine game, ContentRegistry registry, string message)
    {
        var restored = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                restored.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>().Count() ==
                game.Events.Select(item => item.Payload).OfType<ProgramCardTriggerResolvedEvent>().Count(),
            message);
    }

    private sealed class FixturePackage : IGameContentPackage
    {
        private static readonly string[] GeneralIds =
        [
            "sp:zhao-yun", "classic:liu-bei", "classic:sun-quan", "classic:sima-yi", "classic:xiahou-dun"
        ];

        public PackageManifest Manifest { get; } = new(
            "sp-zhao-yun-fixture",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 64, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe(
                "sp-zhao-yun:dodge-deck", "SP赵云用牌夹具", 4, 2,
                [new ContentDeckCardCount("standard:dodge", 60)]));
            builder.AddDeck(new ContentDeckRecipe(
                "sp-zhao-yun:slash-deck", "SP赵云响应夹具", 4, 2,
                [new ContentDeckCardCount("standard:fire_slash", 60)]));
            AddMode(builder, "identity:classic-sp-zhao-use-5", "sp-zhao-yun:dodge-deck");
            AddMode(builder, "identity:classic-sp-zhao-response-5", "sp-zhao-yun:slash-deck");
        }

        private static void AddMode(IContentRegistryBuilder builder, string id, string deckId) =>
            builder.AddMode(new ContentModeDefinition(
                id,
                "SP赵云规则夹具",
                5,
                5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: deckId,
                GeneralCandidateCount: 5,
                GeneralPoolIds: GeneralIds));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
