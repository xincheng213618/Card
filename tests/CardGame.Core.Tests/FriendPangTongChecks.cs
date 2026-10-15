using System.Reflection;
using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

internal static class FriendPangTongChecks
{
    public static void ManjuanOrdersOnlyCurrentGainAndReplays()
    {
        var (game, registry) = Start(waitGain: true);
        Activate(game); var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Last();
        var gained = frame.FriendPangTong!.Cards.ToArray(); var old = game.State.Players[0].Hand!.Select(c => c.Id).Except(gained).ToArray();
        Require(gained.Length == 2 && P(game)!.Choices.Where(c => c.Cards.Count > 0).All(c => gained.Contains(c.Cards.Single())) &&
            !P(game)!.ValidCardIds.Any(old.Contains), "Manjuan exposes only this one native gain batch and never an old hand card.");
        ReplayFourViews(game, registry);
        Answer(game, c => c.Cards.SequenceEqual([gained[1]])); ReplayFourViews(game, registry);
        Answer(game, c => c.Cards.SequenceEqual([gained[0]])); ReplayFourViews(game, registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("value") == "finish"); Settle(game);
        Require(Pile(game).Take(2).SequenceEqual([gained[1], gained[0]]) && game.State.Players[0].HandCount == 4,
            "The chosen top-first order survives the native draw return; an empty discard pool produces no replacement cards.");
        Require(gained.All(id => game.CardMovements.Count(move => move.CardId == id && move.From == CardLocation.Hand(0) &&
            move.To == CardLocation.DrawPile && move.Reason.Value == "friend.manjuan.top") == 1), "The entire ordered payment moves once.");
        ReplayFourViews(game, registry);
    }
    public static void YangmingUsesRealHeldCardsAndUniqueSuits()
    {
        var (game, registry) = Start(waitGain: false);
        var cards = game.State.Players[0].Hand!.Take(3).Select(c => c.Id).ToArray();
        Accept(game, new UseProgramSkillCommand(0, "fixture:pang-driver", "lose-three", cards, [], game.Revision, P(game)!.PromptId)); Settle(game);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "friend:yangming"));
        Activate(game); Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "friend-pang-tong"));
        var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Last(); var stored = frame.FriendPangTong!.Cards.ToArray();
        var expected = game.CardMovements.Where(move => move.TurnNumber == game.State.TurnNumber && move.To == CardLocation.DiscardPile)
            .Select(move => ReadCard(game, move.CardId).Suit).Where(s => s != Suit.None).Distinct().Count();
        Require(stored.Length == expected && stored.All(id => game.State.PublicRevealedCards.Any(c => c.Id == id)),
            "Yangming places the actual turn's distinct discard-suit count on its public owning pile.");
        Require(Enumerable.Range(0, 4).All(view => game.CreateSnapshot(view).PublicRevealedCards.Select(c => c.Id).Order().SequenceEqual(stored.Order())),
            "All viewers see the same held card faces without exposing any remaining deck faces.");
        ReplayFourViews(game, registry);
        var selected = P(game)!.Choices.First(c => c.Cards.Count == 1 && c.Targets.SequenceEqual([1])); var used = selected.Cards.Single(); var suit = ReadCard(game, used).Suit;
        Answer(game, c => c.Id == selected.Id);
        var issued = game.Events.Select(e => e.Payload).OfType<FriendYangmingUseIssuedEvent>().Single(e => e.Return.CardId == used);
        var action = game.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Single(e => e.Action.ActionId == issued.Return.CardActionId).Action;
        Require(action.PhysicalCards is [var material] && material.CardId == used &&
            material.From.Zone == CardZoneKind.PublicPersistentPile && material.From.OwnerSeat == 0,
            "The actual Use owns its one physical held card and an issued unlimited-count policy.");
        ReplayFourViews(game, registry);
        for (var index = 0; index < 100; index++)
        {
            var p = P(game);
            if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "friend-pang-tong") == true) break;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        if (P(game)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "friend-pang-tong") == true)
        {
            Require(P(game)!.Choices.Where(c => c.Cards.Count == 1).All(c => ReadCard(game, c.Cards[0]).Suit != suit),
                "A returned physical use removes that suit from the remaining menu.");
            Answer(game, c => c.Parameters.GetValueOrDefault("value") == "finish");
        }
        for (var index = 0; index < 100 && game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.FriendPangTong is not null); index++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(!game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.FriendPangTong is not null) &&
            stored.All(id => game.CardMovements.Any(move => move.CardId == id && move.To == CardLocation.DiscardPile)) &&
            game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(d => d.SourceSeat == 0 && d.TargetSeat == 1),
            "The real Slash resolves damage, then all unused held cards drain once and the owning program completes.");
        ReplayFourViews(game, registry);
    }
    private static Card ReadCard(GameEngine game, int id) => ((CardZoneStore)typeof(GameEngine)
        .GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!).CardsAt(
            ((CardZoneStore)typeof(GameEngine).GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!).GetLocation(id)).Single(c => c.Id == id);
    private static (GameEngine, ContentRegistry) Start(bool waitGain)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = "identity:pang-fixture", UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 5 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:pang", game.Revision, P(game)!.PromptId));
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "friend:manjuan"));
        if (!waitGain) { Skip(game); Settle(game); } return (game, registry);
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-friend-pang", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "friend:zhuge-liang", "friend:pang-tong", "friend:xu-shu");
            var driver = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:pang-driver","revision":1,
                "activations":[{"id":"lose-three","usesPerTurn":null,"minCards":3,"maxCards":3,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving",
                "effects":[{"op":"discardSelected","target":"owner","amount":3}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:pang-driver":{"name":"夹具","description":"夹具"}}}""").Programs["fixture:pang-driver"];
            builder.AddSkill(new("fixture:pang-driver", "夹具", "fixture") { Program = driver });
            builder.AddGeneral(new("fixture:pang", "友庞统", "friend-pang-tong", "friend:manjuan", "qun", 3, ["friend:yangming", "friend:gongli-pang-tong", "fixture:pang-driver"]));
            for (var index = 1; index <= 3; index++) builder.AddGeneral(new($"fixture:pang-peer-{index}", "夹具对手", "supporter", "standard:none", "wei", 7));
            builder.AddDeck(new("fixture:pang-deck", "固定牌堆", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 64).Select(index => new ContentDeckPhysicalCard("standard:slash",
                new[] { Suit.Heart, Suit.Spade, Suit.Club, Suit.Diamond }[index % 4], 5)).ToArray() });
            builder.AddMode(new("identity:pang-fixture", "友庞统夹具", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:pang-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:pang", "fixture:pang-peer-1", "fixture:pang-peer-2", "fixture:pang-peer-3"]));
        }
    }
}
