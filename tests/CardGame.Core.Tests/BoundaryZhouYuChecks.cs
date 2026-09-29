using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryZhouYuChecks
{
    private const string General = "boundary:zhou-yu";
    private const string Yingzi = "boundary:yingzi";
    private const string Fanjian = "boundary:fanjian";
    private const string YingziLuoyiFixture = "fixture:zhou-yingzi-luoyi";
    private const string YingziKujinFixture = "fixture:zhou-yingzi-kujin";
    private const string Presentation = """{"schemaVersion":3,"skills":{"fixture:gift":{"name":"赠牌","description":"通用公开赠牌"}}}""";

    public static void DefinitionAndResourceContracts()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var general = current.Generals[General];
        Require(general is { Name: "界周瑜", FactionId: "wu", BaseHp: 3, PortraitKey: "boundary_zhou_yu" } &&
                general.SkillIds.SequenceEqual([Yingzi, Fanjian]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2014 Zhou Yu must be an independent Wu three-HP identity general.");
        var yingzi = current.Skills[Yingzi].Program!;
        Require(yingzi.Triggers.Count == 0 && yingzi.Modifiers.Any(item => item.Query == SkillRuleQuery.DrawCount &&
                item.Operation == SkillRuleOperation.Add && item.Value == 1) &&
                yingzi.Modifiers.Any(item => item.Query == SkillRuleQuery.HandLimit &&
                item.ValueExpression == SkillRuleValueExpression.OwnerLostHp),
            "Yingzi must amend the normal draw plan and hand limit, not start an independent draw.");
        var owner = new PlayerSkillContext(0, 1, 3, 2, TurnPhase.Play);
        Require(yingzi.Modifiers.Single(item => item.Query == SkillRuleQuery.HandLimit)
                    .EvaluateValue(new SkillProgramRuleContext(owner, 2)) == 2,
            "One-HP Zhou Yu must add exactly two to the ordinary hand-limit base.");

        var rules = Resource("rules"); var presentation = Resource("presentation");
        Reject(Edit(rules, root => root["skills"]![1]!["activations"]![0]!["effects"]![3]!["suits"] =
            new JsonArray("spade")), presentation, "cannot mix");
        Reject(Edit(rules, root => root["skills"]![1]!["activations"]![0]!["effects"]![3]!["categories"] =
            new JsonArray("basic")), presentation, "cannot mix");
        Reject(Edit(rules, root => root["skills"]![1]!["activations"]![0]!["effects"]![1]!["revealBeforeMove"] = false),
            presentation, "public frozen suit");
        Reject(Edit(rules, root => root["skills"]![1]!["activations"]![0]!["effects"]![1]!["count"] = 2),
            presentation, "must be 1");
        Reject(Edit(rules, root => root["skills"]![1]!["activations"]![0]!["effects"]![1]!["zones"] =
            new JsonArray("equipment")), presentation, "public transfer");
        Reject(Edit(rules, root => root["skills"]![1]!["activations"]![0]!["effects"]![1]!["targetRef"]!["kind"] =
            "owner"), presentation, "public transfer");
        var copied = rules.Replace("boundary:yingzi", "fixture:unused", StringComparison.Ordinal)
            .Replace("boundary:fanjian", "fixture:gift", StringComparison.Ordinal);
        var copiedPresentation = presentation.Replace("boundary:yingzi", "fixture:unused", StringComparison.Ordinal)
            .Replace("boundary:fanjian", "fixture:gift", StringComparison.Ordinal);
        Require(SkillProgramCatalog.Load(copied, copiedPresentation).Programs.ContainsKey("fixture:gift"),
            "Public gift/suit matching must compose for an unrelated skill id.");
    }

    public static void TransferChoiceAndReplay()
    {
        var registry = Registry();
        var game = Start(registry, 17, General);
        ReachPlay(game);
        var owner = game.CreateSnapshot(0, true).Players[0];
        Require(ReadHandLimit(game, 0) == owner.MaxHp && owner.HandCount == 7,
            "Yingzi must draw three normally and use maximum HP as the unmodified hand limit.");
        var beforeMoves = game.CardMovements.Count;
        Accept(game.Submit(new UseProgramSkillCommand(0, Fanjian, "show-and-give", [], [],
            game.Revision, game.PendingDecision!.PromptId)));
        var targetPrompt = game.PendingDecision!;
        Require(targetPrompt is { PlayerSeat: 0, IsPrivate: true } &&
                targetPrompt.Choices.Any(item => item.Targets.SequenceEqual([1])),
            "Fanjian must select another living recipient first.");
        Answer(game, targetPrompt.Choices.Single(item => item.Targets.SequenceEqual([1])));
        var giftPrompt = game.PendingDecision!;
        Require(giftPrompt is { PlayerSeat: 0, IsPrivate: true } && giftPrompt.Choices.Count > 0 &&
                game.CreateSnapshot(1).PendingDecision is null,
            "Only Zhou Yu may choose his exact private hand card.");
        var offered = giftPrompt.Choices.First();
        var giftId = offered.Cards.Single();
        var paused = RoundTrip(game.CreateCheckpoint());
        Answer(game, offered);
        var replayed = GameReplay.Restore(paused, registry);
        Answer(replayed, replayed.PendingDecision!.Choices.Single(item => item.Cards.SequenceEqual([giftId])));
        var revealed = game.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
            .Single(item => item.SkillId == Fanjian && item.Bind == "gift");
        var transfer = game.CardMovements.Skip(beforeMoves).Where(item => item.CardId == giftId).ToArray();
        Require(revealed.Cards.Single().Id == giftId && transfer.Length >= 2 &&
                transfer[0].From == CardLocation.Hand(0) && transfer[0].To == CardLocation.Processing &&
                transfer[1].To == CardLocation.Hand(1) &&
                game.Events.Single(item => ReferenceEquals(item.Payload, revealed)).Sequence <
                game.Events.First(item => item.Payload is CardMovedEvent moved && moved.CardId == giftId &&
                    moved.From == CardLocation.Hand(0) && moved.To == CardLocation.Processing).Sequence,
            "Public card identity must be revealed before the first transfer movement.");
        for (var step = 0; step < 30 && game.Events.All(item => item.Payload is not ProgramOptionChosenEvent
                 { SkillId: Fanjian }); step++)
        {
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
            Accept(replayed.Submit(new AdvanceOneStepCommand(replayed.Revision)));
        }
        var option = game.Events.Select(item => item.Payload).OfType<ProgramOptionChosenEvent>()
            .Single(item => item.SkillId == Fanjian);
        Require(option.ChooserSeat == 1 && option.OptionId is "discard" or "lose-hp" &&
                State(game) == State(replayed) && Events(game).SequenceEqual(Events(replayed)),
            "Recipient choice and every subsequent event must replay from the gift prompt checkpoint.");
        if (option.OptionId == "lose-hp")
            Require(game.Events.All(item => item.Payload is not DamageAppliedEvent { TargetSeat: 1 }),
                "Losing HP through Fanjian must not enter the damage pipeline.");
        else
            Require(game.CardMovements.Any(item => item.CardId == giftId && item.To == CardLocation.DiscardPile),
                "Discard branch must include the given matching-suit card.");
        Require(game.GetHumanLegalActions().All(item => item.ProgramSkillId != Fanjian),
            "Fanjian must be unavailable after its play-phase use.");
    }

    private static ContentRegistry Registry(int initialHand = 4, int draw = 2, bool hongyanTargets = false,
        bool combinedDrawReplacement = false, bool withKujin = false) => ContentRegistry.Build(new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario(initialHand, draw, hongyanTargets, combinedDrawReplacement, withKujin));
    private static GameEngine Start(ContentRegistry registry, int seed, string general)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
            ModeId = Scenario.ModeId, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, general, game.Revision, game.PendingDecision!.PromptId)));
        return game;
    }
    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 128 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Zhou Yu fixture did not reach Play.");
    }
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(
        new AnswerPromptCommand(0, game.PendingDecision!.PromptId, choice.Id, game.Revision)));
    private static int ReadHandLimit(GameEngine game, int seat)
    {
        var players = (System.Collections.IList)typeof(GameEngine)
            .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
        return (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [players[seat]])!;
    }
    private static string Resource(string suffix)
    {
        using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream(
            $"CardGame.Content.Standard.SkillPrograms.boundary-zhou-yu.{suffix}.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static string Edit(string json, Action<JsonNode> edit)
    {
        var root = JsonNode.Parse(json)!;
        edit(root);
        return root.ToJsonString();
    }
    private static void Reject(string rules, string presentation, string expected)
    {
        try { _ = SkillProgramCatalog.Load(rules, presentation); }
        catch (InvalidOperationException error) when (error.Message.Contains(expected, StringComparison.OrdinalIgnoreCase))
        { return; }
        throw new InvalidOperationException($"Expected a definition failure containing '{expected}'.");
    }
    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Command rejected.");
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(int initialHand, int draw, bool hongyanTargets,
        bool combinedDrawReplacement, bool withKujin) : IGameContentPackage
    {
        internal const string ModeId = "identity:classic-boundary-zhou-yu-check-5";
        public PackageManifest Manifest { get; } = new("boundary-zhou-yu-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 141, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            var others = Enumerable.Range(1, 4).Select(i => $"fixture:zhou-target-{i}").ToArray();
            foreach (var id in others)
                builder.AddGeneral(new ContentGeneralDefinition(id, "测试目标", "supporter",
                    hongyanTargets ? "classic:hongyan" : "standard:none", "qun", BaseHp: 8));
            if (combinedDrawReplacement)
                builder.AddGeneral(new ContentGeneralDefinition(YingziLuoyiFixture, "盈余裸衣组合", "supporter",
                    Yingzi, "wu", BaseHp: 4, AdditionalSkillIds: ["boundary:luoyi"]));
            if (withKujin)
                builder.AddGeneral(new ContentGeneralDefinition(YingziKujinFixture, "盈余苦尽组合", "supporter",
                    Yingzi, "wu", BaseHp: 4, AdditionalSkillIds: ["standard:kujin"]));
            string[] cards = initialHand == 0 && draw == 0
                ? ["standard:slash"]
                : new[] { "standard:slash", "standard:peach", "standard:crossbow",
                    "standard:duel", "standard:dodge", "standard:bagua" };
            builder.AddDeck(new ContentDeckRecipe("fixture:zhou-deck", "反间牌堆", initialHand, draw, [])
            {
                PhysicalCards = Enumerable.Range(0, 120).Select(i => new ContentDeckPhysicalCard(
                    cards[i % cards.Length], (Suit)(i % 4), i % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition(ModeId, "界周瑜测试", 5, 5,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 }, "fixture:zhou-deck",
                GeneralCandidateCount: combinedDrawReplacement || withKujin ? 6 : 5,
                GeneralPoolIds: combinedDrawReplacement ? [General, YingziLuoyiFixture, .. others] :
                    withKujin ? [General, YingziKujinFixture, .. others] : [General, .. others]));
        }
    }
}
