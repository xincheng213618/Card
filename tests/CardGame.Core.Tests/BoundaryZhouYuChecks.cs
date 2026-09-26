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

    public static void EmptyHandCannotActivate()
    {
        var registry = Registry(0, 0);
        var game = Start(registry, 3, General);
        ReachPlay(game);
        var playable = game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Slash &&
            item.CardId is not null && item.TargetSeats.Count > 0);
        Accept(game.Submit(new PlayCardCommand(0, playable.CardId!.Value, playable.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, playable.PlayedCardKind, playable.TargetCardId)));
        ReachPlay(game);
        Require(game.CreateSnapshot(0, true).Players[0].HandCount == 0 &&
                game.GetHumanLegalActions().All(item => item.ProgramSkillId != Fanjian),
            $"A handless Zhou Yu must not be offered Fanjian before target selection: " +
            $"hand={game.CreateSnapshot(0, true).Players[0].HandCount}, " +
            $"actions={string.Join(',', game.GetHumanLegalActions().Where(item => item.ProgramSkillId == Fanjian).Select(item => item.Kind))}.");
        var revision = game.Revision;
        var forged = game.Submit(new UseProgramSkillCommand(0, Fanjian, "show-and-give", [], [],
            game.Revision, game.PendingDecision!.PromptId));
        Require(!forged.Accepted && game.Revision == revision &&
                game.Events.All(item => item.Payload is not ProgramBindingResolvedEvent { SkillId: Fanjian }),
            "A handless forged use must be rejected without consuming the phase limit.");
    }

    public static void RecipientCanDiscardOrLoseHp()
    {
        var registry = Registry();
        var found = new Dictionary<string, (int Seed, int GiftIndex, int Matching)>(StringComparer.Ordinal);
        for (var seed = 1; seed <= 1; seed++)
        {
            var setup = Start(registry, seed, General);
            ReachPlay(setup);
            Accept(setup.Submit(new UseProgramSkillCommand(0, Fanjian, "show-and-give", [], [],
                setup.Revision, setup.PendingDecision!.PromptId)));
            Answer(setup, setup.PendingDecision!.Choices.Single(item => item.Targets.SequenceEqual([1])));
            var giftCheckpoint = RoundTrip(setup.CreateCheckpoint());
            foreach (var index in new[] { 0, 3 })
            {
                var game = GameReplay.Restore(giftCheckpoint, registry);
                var chosen = game.PendingDecision!.Choices[index];
                Answer(game, chosen);
                var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Single();
                var matching = frame.CardSetBindings.Single(item => item.Name == "matching");
                var frozen = frame.CardSetBindings.Single(item => item.Name == "gift");
                Require(frozen.FrozenRevealedSuit is not null && matching.SourceLocations.All(item =>
                        item.OwnerSeat == 1 && item.Zone is CardZoneKind.Hand or CardZoneKind.Equipment),
                    "A public gift suit must survive movement as scalar metadata; matched cards belong to chooser.");
                var checkpoint = RoundTrip(game.CreateCheckpoint());
                var restored = GameReplay.Restore(checkpoint, registry);
                var beforeHp = game.CreateSnapshot(0, true).Players[1].Hp;
                for (var step = 0; step < 20 && game.Events.All(item => item.Payload is not ProgramOptionChosenEvent
                         { SkillId: Fanjian }); step++)
                {
                    Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
                    Accept(restored.Submit(new AdvanceOneStepCommand(restored.Revision)));
                }
                var option = game.Events.Select(item => item.Payload).OfType<ProgramOptionChosenEvent>()
                    .Single(item => item.SkillId == Fanjian);
                Require(State(game) == State(restored) && Events(game).SequenceEqual(Events(restored)),
                    "The recipient choice must replay from a checkpoint containing frozen gift suit.");
                if (option.OptionId == "discard")
                    Require(matching.CardIds.Count > 0 && matching.CardIds.All(id => game.CardMovements.Any(move =>
                            move.CardId == id && move.To == CardLocation.DiscardPile)) &&
                            game.CreateSnapshot(0, true).Players[1].Hp == beforeHp,
                        "Discard must include every currently matched hand or equipment card and no HP loss.");
                else
                    Require(option.OptionId == "lose-hp" &&
                            game.CreateSnapshot(0, true).Players[1].Hp == beforeHp - 1 &&
                            matching.CardIds.All(id => game.CreateCardZoneDiagnostics().Any(item =>
                                item.CardId == id && item.Location != CardLocation.DiscardPile)) &&
                            game.Events.All(item => item.Payload is not DamageAppliedEvent { TargetSeat: 1 }),
                        "Lose-HP branch must preserve matched cards and never apply damage.");
                found.TryAdd(option.OptionId, (seed, index, matching.CardIds.Count));
            }
        }
        Require(found.Count == 2,
            $"Real choices did not reach both branches: {string.Join(';', found.Select(item =>
                $"{item.Key}={item.Value.Seed}/{item.Value.GiftIndex}/{item.Value.Matching}"))}.");
    }

    public static void HongyanChangesRecipientSuitOnly()
    {
        var registry = Registry(hongyanTargets: true);
        var game = Start(registry, 1, General);
        ReachPlay(game);
        Accept(game.Submit(new UseProgramSkillCommand(0, Fanjian, "show-and-give", [], [],
            game.Revision, game.PendingDecision!.PromptId)));
        Answer(game, game.PendingDecision!.Choices.Single(item => item.Targets.SequenceEqual([1])));
        var hand = game.CreateSnapshot(0, true).Players[0].Hand;
        var spade = game.PendingDecision!.Choices.First(choice => choice.Cards.Count == 1 &&
            hand.Single(card => card.Id == choice.Cards[0]).Suit == Suit.Spade);
        Answer(game, spade);
        var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Single();
        var gift = frame.CardSetBindings.Single(item => item.Name == "gift");
        var matching = frame.CardSetBindings.Single(item => item.Name == "matching");
        var saved = RoundTrip(game.CreateCheckpoint());
        var replay = GameReplay.Restore(saved, registry);
        for (var step = 0; step < 20 && game.PendingDecision?.PlayerSeat != 1 &&
             game.Events.All(item => item.Payload is not ProgramOptionChosenEvent { SkillId: Fanjian }); step++)
        {
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
            Accept(replay.Submit(new AdvanceOneStepCommand(replay.Revision)));
        }
        Require(gift.FrozenRevealedSuit == Suit.Spade && matching.CardIds.Count == 0 &&
                (game.PendingDecision?.PlayerSeat == 1 &&
                 game.PendingDecision.Choices.Single().Parameters.GetValueOrDefault("option-id") == "lose-hp" ||
                 game.Events.Select(item => item.Payload).OfType<ProgramOptionChosenEvent>()
                     .Any(item => item.SkillId == Fanjian && item.OptionId == "lose-hp")),
            $"Hongyan must turn recipient spades into hearts while the publicly shown gift remains spade: " +
            $"gift={gift.FrozenRevealedSuit}, matched={matching.CardIds.Count}, " +
            $"target={game.CreateSnapshot(0, true).Players[1].GeneralId}/" +
            $"{string.Join(',', registry.Generals[game.CreateSnapshot(0, true).Players[1].GeneralId].SkillIds)}, " +
            $"prompt={game.PendingDecision?.PlayerSeat}/{string.Join(',', game.PendingDecision?.Choices.Select(item => item.Parameters.GetValueOrDefault("option-id")) ?? [])}.");
        if (game.Events.All(item => item.Payload is not ProgramOptionChosenEvent { SkillId: Fanjian }))
        {
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
            Accept(replay.Submit(new AdvanceOneStepCommand(replay.Revision)));
        }
        Require(game.Events.Select(item => item.Payload).OfType<ProgramOptionChosenEvent>()
                    .Single(item => item.SkillId == Fanjian).OptionId == "lose-hp" &&
                State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
            "Only lose HP may be selected with no effective spade, and the suspended branch must replay.");
    }

    public static void YingziRespectsDrawReplacement()
    {
        var registry = Registry(combinedDrawReplacement: true);
        var game = Start(registry, 1, YingziLuoyiFixture);
        Accept(game.Submit(new AdvanceCommand(game.Revision)));
        Require(game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0,
            SkillPrompt.SkillId: "boundary:luoyi" },
            "A normal draw modifier and a draw replacement must reach the replacement decision.");
        var before = game.CreateSnapshot(0, true).Players[0].HandCount;
        var prompt = game.PendingDecision!;
        var choice = prompt.Choices.Single(item => item.Parameters.GetValueOrDefault("program-action") == "activate");
        Accept(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision)));
        var revealed = game.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
            .Single(item => item.SkillId == "boundary:luoyi");
        var gained = revealed.Cards.Count(card => CardCatalog.Get(card.Kind).CategoryName == "基本牌" ||
            EquipmentCatalog.IsEquipment(card.Kind) &&
            EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon || card.Kind == CardKind.Duel);
        Require(game.CreateSnapshot(0, true).Players[0].HandCount == before + gained,
            "Yingzi must not add an independent card after Luoyi replaces the normal draw.");
    }

    public static void YingziTracksActualLostHp()
    {
        var registry = Registry(withKujin: true);
        var game = Start(registry, 1, YingziKujinFixture);
        ReachPlay(game);
        var before = game.CreateSnapshot(0, true).Players[0];
        Require(ReadHandLimit(game, 0) == before.MaxHp,
            "An unwounded Yingzi owner must have the ordinary maximum-HP hand limit.");
        Accept(game.Submit(new UseProgramSkillCommand(0, "standard:kujin", "lose-hp-and-draw",
            [], [], game.Revision, game.PendingDecision!.PromptId)));
        var after = game.CreateSnapshot(0, true).Players[0];
        Require(after.Hp == before.Hp - 1 && ReadHandLimit(game, 0) == before.MaxHp,
            "After a real skill loses one HP, Yingzi must add exactly the missing HP to the hand limit.");
    }

    public static void FanjianDiscardsMatchingEquipment()
    {
        // The fixture installs a real dealt equipment card on the recipient. Its
        // placement is test setup; the following Fanjian commands are ordinary.
        var registry = Registry();
        var game = Start(registry, 1, General);
        ReachPlay(game);
        var targetHand = game.CreateSnapshot(0, true).Players[1].Hand;
        var equipment = targetHand.First(card => EquipmentCatalog.IsEquipment(card.Kind));
        typeof(GameEngine).GetMethod("MoveCard", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [new Card(equipment.Id, equipment.Kind, equipment.Suit, equipment.Rank),
                CardLocation.Hand(1), CardLocation.Equipment(1), CardMoveReasons.Use]);
        Require(game.CreateCardZoneDiagnostics().Any(item => item.CardId == equipment.Id &&
                item.Location == CardLocation.Equipment(1)), "Fixture equipment must be in the recipient's equipment zone.");
        Accept(game.Submit(new UseProgramSkillCommand(0, Fanjian, "show-and-give", [], [],
            game.Revision, game.PendingDecision!.PromptId)));
        Answer(game, game.PendingDecision!.Choices.Single(item => item.Targets.SequenceEqual([1])));
        var ownerHand = game.CreateSnapshot(0, true).Players[0].Hand;
        var matchingGift = game.PendingDecision!.Choices.First(item =>
            ownerHand.Single(card => card.Id == item.Cards.Single()).Suit == equipment.Suit);
        Answer(game, matchingGift);
        var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Single();
        var matching = frame.CardSetBindings.Single(item => item.Name == "matching");
        Require(matching.CardIds.Contains(equipment.Id),
            "Matching equipment must be captured together with the recipient's hand cards.");
        for (var step = 0; step < 20 && game.Events.All(item => item.Payload is not ProgramOptionChosenEvent
                 { SkillId: Fanjian }); step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        var option = game.Events.Select(item => item.Payload).OfType<ProgramOptionChosenEvent>()
            .Single(item => item.SkillId == Fanjian);
        Require(option.OptionId == "discard" && game.CardMovements.Any(item =>
                item.CardId == equipment.Id && item.From == CardLocation.Equipment(1) &&
                item.To == CardLocation.DiscardPile),
            "Choosing the discard branch must move every matching equipment card to the discard pile.");
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
