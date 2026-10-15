using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

internal static class MouZhugeLiangChecks
{
    public static void PrivateTopYangPaymentAndColdReplay()
    {
        var (game, registry) = Start("standard:slash");
        var initial = game.State.Players[0].HandCount;
        var targetHp = game.State.Players[1].Hp;
        AssertPrivate(game, 7); ReplayFourViews(game, registry);
        Use(game, "mou:zhitian", "view-top");
        AssertPrivate(game, 7); ReplayFourViews(game, registry);
        var before = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        var prompt = P(game)!;
        Require(!game.Submit(new AnswerPromptCommand(0, prompt.PromptId, new("foreign-choice"), game.Revision)).Accepted &&
            before == GameCheckpointJson.Serialize(game.CreateCheckpoint()), "A foreign private-top answer leaves the command prefix unchanged.");
        Answer(game, _ => true); Settle(game);
        Use(game, "mou:zhijue", "alternate");
        ReplayFourViews(game, registry);
        var top = P(game)!.Choices[0].Cards.Single();
        Answer(game, choice => choice.Targets.SequenceEqual([1]));
        Reach(game, prompt => prompt.Kind == DecisionKind.FireAttackDiscard);
        ReplayFourViews(game, registry); AssertPrivate(game, 7);
        var deckCost = game.State.PrivateRevealedCards!.First(card => card.Suit == Suit.Heart).Id;
        Answer(game, choice => choice.Cards.SequenceEqual([deckCost])); Settle(game);
        Require(game.State.Players[1].Hp == targetHp - 1 && game.State.Players[0].HandCount == initial + 1,
            $"Yang top-card FireAttack damages once and its first nonhand discard draws one without losing an owner hand card. hp={game.State.Players[1].Hp}, hand={game.State.Players[0].HandCount}, initial={initial}");
        Require(game.CardMovements.Count(move => move.CardId == top && move.From == CardLocation.DrawPile && move.Reason == CardMoveReasons.Use) == 1 &&
            game.CardMovements.Count(move => move.CardId == deckCost && move.From == CardLocation.DrawPile && move.To == CardLocation.DiscardPile) == 1,
            "The FireAttack material and Zhitian replacement each pay once from the real deck.");
        Require(!game.Events.Select(e => e.Payload).OfType<MouZhijueUndamagedEvent>().Any(), "A damaging Zhijue use does not reduce its view or draw two.");
        AssertPrivate(game, 7); ReplayFourViews(game, registry);
        Use(game, "mou:zhijue", "alternate");
        var paid = P(game)!.Choices.Single().Cards.ToArray();
        Answer(game, _ => true); Reach(game, prompt => prompt.Choices.Any(choice => choice.Id.Value.StartsWith("mou-copy.")));
        Require(game.CreateSnapshot(1).PendingDecision is null && game.CreateSnapshot(1).PrivateRevealedCards is null,
            "Other viewers cannot see the private copy menu or current deck faces.");
        ReplayFourViews(game, registry);
        Answer(game, choice => choice.Targets.SequenceEqual([1])); Settle(game);
        Require(game.State.Players[1].Hp == targetHp - 2 && paid.All(id => game.CardMovements.Count(move => move.CardId == id &&
            move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile && move.Reason.Value == "mou.zhijue.place") == 1),
            "Yin copies the paid Slash as a logical use without charging its entire color hand twice.");
        ReplayFourViews(game, registry);
    }

    public static void UndamagedCopyDrawAndColorLimit()
    {
        var (game, registry) = Start("standard:draw_two");
        Use(game, "mou:zhijue", "alternate"); Answer(game, choice => choice.Targets.SequenceEqual([1]));
        Reach(game, prompt => prompt.Kind == DecisionKind.FireAttackDiscard);
        Answer(game, choice => choice.Cards.Count == 0); Settle(game);
        AssertPrivate(game, 6);
        Require(game.Events.Select(e => e.Payload).OfType<MouZhijueUndamagedEvent>().Single().VisibleCount == 6,
            "An undamaged Yang use reduces the live private view by one and draws two.");
        Use(game, "mou:zhijue", "alternate"); Answer(game, _ => true);
        Reach(game, prompt => prompt.Choices.Any(choice => choice.Id.Value.StartsWith("mou-copy.")));
        ReplayFourViews(game, registry); Answer(game, choice => choice.Label.Contains("无中生有")); Settle(game);
        Require(game.State.Players[0].HandCount == 4 && game.Events.Select(e => e.Payload).OfType<MouZhijueUndamagedEvent>().Last().VisibleCount == 5,
            "A zero-material Yin DrawTwo draws its two cards plus the undamaged reward after the whole Use completes.");
        Use(game, "mou:zhijue", "alternate"); Answer(game, choice => choice.Targets.SequenceEqual([1]));
        Reach(game, prompt => prompt.Kind == DecisionKind.FireAttackDiscard); Answer(game, choice => choice.Cards.Count == 0); Settle(game);
        Require(!game.GetLegalActions().Any(action => action.ProgramSkillId == "mou:zhijue"),
            "The second Yin cannot place red again during this actual turn, including newly drawn red hand cards.");
        AssertPrivate(game, 4); ReplayFourViews(game, registry);
    }

    private static void AssertPrivate(GameEngine game, int count)
    {
        Require(game.CreateSnapshot(0).PrivateRevealedCards is { } cards && cards.Count == count &&
            cards.Select(card => card.Id).SequenceEqual(Pile(game).Take(count)) &&
            Enumerable.Range(1, 3).All(viewer => game.CreateSnapshot(viewer).PrivateRevealedCards is null),
            "Only the owner sees current live deck top faces in their true top-first order.");
    }
    private static void Use(GameEngine game, string skill, string activation, int[]? targets = null) =>
        Accept(game, new UseProgramSkillCommand(0, skill, activation, [], targets ?? [], game.Revision, P(game)!.PromptId));
    private static (GameEngine, ContentRegistry) Start(string kind, bool equipment = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(kind, equipment));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "identity:mou-fixture", UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 5 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, prompt => prompt.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:mou-zhuge", game.Revision, P(game)!.PromptId)); Settle(game); return (game, registry);
    }
    private sealed class Fixture(string kind, bool equipment) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-zhuge", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "mou:zhuge-liang");
            var driver = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:mou-driver","revision":1,
                "activations":[{"id":"discard-other-equipment","usesPerTurn":null,"minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,
                "targetKind":"otherLiving","effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipped"},
                {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["equipment"],"count":1,"destination":"discardPile","skipIfNoCards":true}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:mou-driver":{"name":"夹具","description":"夹具"}}}""").Programs["fixture:mou-driver"];
            builder.AddSkill(new("fixture:mou-driver", "夹具", "fixture") { Program = driver });
            builder.AddGeneral(new("fixture:mou-zhuge", "谋诸葛亮", "mou-zhuge-liang", "mou:zhitian", "shu", 3,
                ["mou:wujing", "mou:zhijue", "fixture:mou-driver"]));
            for (var index = 1; index <= 3; index++) builder.AddGeneral(new($"fixture:mou-peer-{index}", "夹具对手", "supporter", "standard:none", "wei", 7));
            builder.AddDeck(new("fixture:mou-deck", "固定牌堆", 4, 1, [])
            { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard(kind, Suit.Heart, 5))
                .Concat(Enumerable.Range(0, equipment ? 8 : 0).Select(_ => new ContentDeckPhysicalCard("standard:qinggang_sword", Suit.Spade, 6))).ToArray() });
            builder.AddMode(new("identity:mou-fixture", "谋诸葛夹具", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:mou-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:mou-zhuge", "fixture:mou-peer-1", "fixture:mou-peer-2", "fixture:mou-peer-3"]));
        }
    }
}
