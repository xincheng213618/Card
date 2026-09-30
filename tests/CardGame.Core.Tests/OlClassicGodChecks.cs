using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OlClassicGodChecks
{
    private const string Driver = "fixture:classic-god-driver";
    private const string Mode = "identity:classic-god-fixture";
    public static void StarsPrivateExchangeAndFogReplay()
    {
        var (game, registry) = Create("zhuge", stopAtStartup: true);
        Require(game.CreateSnapshot(0).Players[0].PrivateReserveCount == 7, "Qixing must initialize seven physical stars before the first turn.");
        Require(game.CreateSnapshot(1).Players[0].PrivateReserveCards is null, "Another player must not see private star identities.");
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        var selectedHand = game.PendingDecision!.Choices.First(choice => choice.Cards.Count == 1).Cards[0];
        Answer(game, game.PendingDecision.Choices.First(choice => choice.Cards.Contains(selectedHand)));
        EqualReplay(game, registry);
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Cards.Count == 0));
        Require(game.PendingDecision is { IsPrivate: true }, "Equal star exchange must remain private while selecting stars.");
        var selectedStar = game.PendingDecision!.Choices.First(choice => choice.Cards.Count == 1).Cards[0];
        Answer(game, game.PendingDecision.Choices.First(choice => choice.Cards.Contains(selectedStar)));
        Drain(game);
        var self = game.CreateSnapshot(0).Players[0];
        Require(self.PrivateReserveCards!.Any(card => card.Id == selectedHand) && self.Hand.Any(card => card.Id == selectedStar) && self.PrivateReserveCount == 7,
            "Qixing must exchange equal physical cards without changing the star count.");
        EqualReplay(game, registry);
        EndPlay(game);
        Until(game, () => game.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("skill-id") == "ol:dawu" && choice.Parameters.GetValueOrDefault("program-action") == "activate") == true);
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Require(game.PendingDecision is { IsPrivate: true }, "Dawu star selection must be private.");
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Cards.Count == 1));
        EqualReplay(game, registry);
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards"));
        Require(game.PendingDecision!.Choices.All(choice => choice.Targets.Count == 1), "The number of Dawu targets must equal the committed star cost.");
        Answer(game, game.PendingDecision.Choices.First(choice => choice.Targets.SequenceEqual([1])));
        Until(game, () => game.CreateSnapshot(0).Players[1].Markers?.Any(marker => marker.Kind == PlayerMarkerKind.Mist) == true);
        Require(game.CreateSnapshot(0).Players[0].PrivateReserveCount == 6, "Dawu must physically discard one chosen star.");
        EqualReplay(game, registry);
        Drain(game);
        Require(game.CreateSnapshot(0).Players.All(player => player.Markers?.Any(marker => marker.Kind == PlayerMarkerKind.Mist) != true), "Dawu must expire when its source's next turn begins.");
    }
    public static void RageCostsAndShenfenOrdering()
    {
        var (game, registry) = Create("lu");
        Require(Marker(game, PlayerMarkerKind.Rage) == 2, "Kuangbao must initialize two rage marks.");
        Use(game, "damage", [1]);
        Require(Marker(game, PlayerMarkerKind.Rage) == 3, "Causing one damage must grant one rage.");
        Activate(game, "ol:wuqian", "rage-attack", [], [1]);
        Drain(game);
        Require(Marker(game, PlayerMarkerKind.Rage) == 1, "Wuqian must pay two rage marks before granting effects.");
        Require(game.CreateSnapshot(0).Players[0].Skills!.Any(skill => skill.ContentId == "classic:wushuang"), "Wuqian must grant Wushuang for the current turn.");
        Use(game, "six-rage");
        var rageBefore = Marker(game, PlayerMarkerKind.Rage);
        var before = game.CreateSnapshot(0, true).Players.Skip(1).ToDictionary(player => player.Seat, player => player.Hp);
        Activate(game, "ol:shenfen", "rage-destruction", [], []);
        EqualReplay(game, registry);
        Drain(game);
        var snapshot = game.CreateSnapshot(0, true);
        Require(snapshot.Players.Skip(1).All(player => player.Hp == before[player.Seat] - 1 && player.HandCount == 0), "Shenfen must damage every other player then discard four of each player's hand cards.");
        Require(snapshot.Players[0].IsFaceDown && Marker(game, PlayerMarkerKind.Rage) == rageBefore - 6 + 3, "Shenfen must turn its owner over after the table losses, retaining rage earned by three damage points.");
        Require(!game.GetHumanLegalActions().Any(action => action.ProgramSkillId == "ol:shenfen"), "Shenfen must remain limited to once per play phase.");
        EqualReplay(game, registry);
        var (armor, armorRegistry) = Create("lu-armor");
        Use(armor, "draw");
        Use(armor, "give-lion", [1]);
        var hp = armor.CreateSnapshot(0).Players[1].Hp;
        Use(armor, "damage-three", [1]);
        Require(armor.CreateSnapshot(0).Players[1].Hp == hp - 1, "A valid Silver Lion must cap skill damage before Wuqian.");
        Activate(armor, "ol:wuqian", "rage-attack", [], [1]);
        Drain(armor);
        hp = armor.CreateSnapshot(0).Players[1].Hp;
        Use(armor, "damage-three", [1]);
        Require(armor.CreateSnapshot(0).Players[1].Hp == hp - 3, "Wuqian must invalidate Silver Lion for non-Slash skill damage during the current turn.");
        EqualReplay(armor, armorRegistry);
    }
    public static void LonghunTwoCardRecoveryFireAndDyingDraw()
    {
        var (game, registry) = Create("zhao");
        Use(game, "draw");
        Use(game, "hurt-two");
        var cards = game.CreateSnapshot(0).Players[0].Hand;
        var hearts = cards.Where(card => card.Suit == Suit.Heart).Take(2).Select(card => card.Id).ToArray();
        var diamonds = cards.Where(card => card.Suit == Suit.Diamond).Take(2).Select(card => card.Id).ToArray();
        var before = game.CreateSnapshot(0).Players[0].Hp;
        Activate(game, "ol:longhun", "two-heart", hearts, []);
        Drain(game);
        Require(game.CreateSnapshot(0).Players[0].Hp == before + 2 && hearts.All(id => game.CardMovements.Any(move => move.CardId == id && move.To == CardLocation.DiscardPile)), "Two hearts must recover two HP and consume both physical inputs.");
        EqualReplay(game, registry);
        var enemyHp = game.CreateSnapshot(0).Players[1].Hp;
        Activate(game, "ol:longhun", "two-diamond", diamonds, [1]);
        Drain(game);
        Require(game.CreateSnapshot(0).Players[1].Hp == enemyHp - 2, $"Two diamonds must deal two fire damage through a single Slash. Actual {enemyHp}->{game.CreateSnapshot(0).Players[1].Hp}.");
        Require(diamonds.All(id => game.CardMovements.Any(move => move.CardId == id && move.To == CardLocation.DiscardPile)), "Both diamond physical costs must finish in the discard pile.");
        EqualReplay(game, registry);
        var handCount = game.CreateSnapshot(0).Players[0].HandCount;
        Use(game, "hurt-three", drain: false);
        Until(game, () => game.PendingDecision?.Kind == DecisionKind.RescueDying);
        Require(game.CreateSnapshot(0).Players[0].HandCount == handCount + 1, "Juejing must draw immediately on entering dying, before rescue choices.");
        EqualReplay(game, registry);
        var pair = game.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("response") == "extended-view-as" && choice.Cards.Count == 2);
        Answer(game, pair);
        Drain(game);
        Require(game.CreateSnapshot(0).Players[0].Hp > 0 && game.CreateSnapshot(0).Players[0].HandCount == handCount,
            "Two-heart dying rescue must consume two cards and Juejing must draw once again on leaving dying.");
        EqualReplay(game, registry);
    }
    public static void YeyanDistinctSuitCostAndLimitedDamage()
    {
        var (game, registry) = Create("zhou");
        Use(game, "draw");
        var hand = game.CreateSnapshot(0).Players[0].Hand;
        var suits = hand.GroupBy(card => card.Suit).Select(group => group.First().Id).ToArray();
        var hp = game.CreateSnapshot(0).Players[0].Hp;
        var enemyHp = game.CreateSnapshot(0).Players[1].Hp;
        Activate(game, "ol:yeyan", "three", suits, [1]);
        Drain(game);
        Require(game.CreateSnapshot(0).Players[0].Hp == hp - 3 && game.CreateSnapshot(0).Players[1].Hp == enemyHp - 3, "Heavy Yeyan must lose three HP before dealing three fire damage.");
        Require(suits.All(id => game.CardMovements.Any(move => move.CardId == id && move.To == CardLocation.DiscardPile)), "Heavy Yeyan must consume four distinct-suit hand cards.");
        Require(!game.GetHumanLegalActions().Any(action => action.ProgramSkillId == "ol:yeyan"), "All Yeyan allocations must share one limited-use debit.");
        EqualReplay(game, registry);
        var (dying, dyingRegistry) = Create("zhou-death");
        Use(dying, "draw");
        var costs = dying.CreateSnapshot(0).Players[0].Hand.GroupBy(card => card.Suit).Select(group => group.First().Id).ToArray();
        var previousHp = dying.CreateSnapshot(0).Players[1].Hp;
        Activate(dying, "ol:yeyan", "three", costs, [1]);
        Require(dying.CreateSnapshot(0).Players[0].Hp == -1, "Losing three HP from two HP must preserve the negative dying HP and exact rescue requirement.");
        Until(dying, () => dying.CreateSnapshot(0).Players[1].Hp == previousHp - 3);
        Require(!dying.CreateSnapshot(0).Players[0].IsAlive, "Heavy Yeyan must continue its committed fire damage after its owner dies from the HP cost.");
        EqualReplay(dying, dyingRegistry);
    }
    public static void LonghunBlackResponsesAndQinyin()
    {
        var (game, registry) = Create("zhao-response");
        Use(game, "draw");
        EndPlay(game);
        Until(game, () => game.PendingDecision is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 });
        var currentSeat = game.CreateSnapshot(0).CurrentSeat;
        var before = game.CreateSnapshot(0, true).Players[currentSeat].HandCount;
        var discardBefore = game.CardMovements.Count(move => move.From == CardLocation.Hand(currentSeat) && move.To == CardLocation.DiscardPile);
        var pair = game.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("response") == "extended-view-as" && choice.Cards.Count == 2);
        var costs = pair.Cards.ToArray();
        Answer(game, pair);
        Until(game, () => game.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("binding-id") == "two-club-discard") == true);
        EqualReplay(game, registry);
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Until(game, () => game.PendingDecision?.Choices.Any(choice => choice.Targets.SequenceEqual([currentSeat])) == true);
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Targets.SequenceEqual([currentSeat])));
        Until(game, () => game.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"));
        Require(game.CardMovements.Count(move => move.From == CardLocation.Hand(currentSeat) && move.To == CardLocation.DiscardPile) == discardBefore + 1, "Two-club Longhun must discard one physical card belonging to the current turn character.");
        Until(game, () => costs.All(id => game.CardMovements.Any(move => move.CardId == id && move.To == CardLocation.DiscardPile)));
        Require(costs.All(id => game.CardMovements.Any(move => move.CardId == id && move.To == CardLocation.DiscardPile)),
            $"A two-club Dodge must consume both cards and allow discarding the current turn character's card. Seat={currentSeat}, hand={before}->{game.CreateSnapshot(0,true).Players[currentSeat].HandCount}, physical={string.Join(',', costs.Select(id => game.CardMovements.Any(move => move.CardId == id && move.To == CardLocation.DiscardPile)))}.");
        EqualReplay(game, registry);

        var (counter, counterRegistry) = Create("zhao-null");
        Use(counter, "draw");
        var draw = counter.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.DrawTwo);
        Accept(counter.Submit(new PlayCardCommand(0, draw.CardId!.Value, draw.TargetSeats, counter.Revision, counter.PendingDecision!.PromptId)));
        Until(counter, () => counter.PendingDecision is { Kind: DecisionKind.Nullification, PlayerSeat: 0 });
        var nullPair = counter.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("response") == "extended-view-as" && choice.Cards.Count == 2);
        Answer(counter, nullPair);
        Until(counter, () => counter.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("binding-id") == "two-spade-discard") == true);
        Answer(counter, counter.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Until(counter, () => counter.PendingDecision?.Choices.Any(choice => choice.Targets.SequenceEqual([0])) == true);
        Answer(counter, counter.PendingDecision!.Choices.First(choice => choice.Targets.SequenceEqual([0])));
        Until(counter, () => counter.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
        var count = counter.CreateSnapshot(0).Players[0].HandCount;
        Answer(counter, counter.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"));
        Require(counter.CreateSnapshot(0).Players[0].HandCount == count - 1, "Two-spade Longhun during its owner's turn must allow discarding the owner's own card.");
        EqualReplay(counter, counterRegistry);
        Drain(counter);
        EqualReplay(counter, counterRegistry);

        var (music, musicRegistry) = Create("zhou");
        Use(music, "draw");
        var hp = music.CreateSnapshot(0).Players.Select(player => player.Hp).ToArray();
        EndPlay(music);
        Until(music, () => music.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("skill-id") == "ol:qinyin") == true);
        Answer(music, music.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Answer(music, music.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("option-id") == "lose"));
        Until(music, () => music.CreateSnapshot(0).Players.Select((player, seat) => player.Hp == hp[seat] - 1).All(value => value));
        Require(Marker(music, PlayerMarkerKind.Rage) == 0,
            "A legacy Own discardPhaseEnded trigger without the opt-in must remain inactive.");
        EqualReplay(music, musicRegistry);
    }
    private static int Marker(GameEngine game, PlayerMarkerKind kind) => game.CreateSnapshot(0).Players[0].Markers?.SingleOrDefault(marker => marker.Kind == kind)?.Count ?? 0;
    private static (GameEngine, ContentRegistry) Create(string flavor, bool stopAtStartup = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(flavor));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = flavor == "zhou-death" ? 8 : 7, PlayerCount = 4, HumanSeat = 0, HumanRole = flavor == "zhou-death" ? Role.Loyalist : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:classic-god-owner", game.Revision, game.PendingDecision!.PromptId)));
        if (stopAtStartup) Until(game, () => game.PendingDecision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("binding-id") == "initial-exchange") == true);
        else Drain(game);
        return (game, registry);
    }
    private static void Activate(GameEngine game, string skill, string activation, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => Accept(game.Submit(new UseProgramSkillCommand(0, skill, activation, cards, targets, game.Revision, game.PendingDecision!.PromptId)));
    private static void Use(GameEngine game, string id, IReadOnlyList<int>? targets = null, bool drain = true) { Activate(game, Driver, id, [], targets ?? []); if (drain) Drain(game); }
    private static void EndPlay(GameEngine game) => Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
    private static void Drain(GameEngine game) => Until(game, () => game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && game.ResolutionStack.Count == 0);
    private static void Until(GameEngine game, Func<bool> predicate) { for (var i = 0; i < 1600; i++) { if (predicate()) return; Step(game); } throw new InvalidOperationException("Classic god fixture did not reach its expected boundary."); }
    private static void Step(GameEngine game)
    {
        if (game.PendingDecision is not { } prompt) { Accept(game.Submit(new AdvanceOneStepCommand(game.Revision))); return; }
        if (prompt.Kind == DecisionKind.PlayCard) { Accept(game.Submit(new EndPlayPhaseCommand(prompt.PlayerSeat, game.Revision, prompt.PromptId))); return; }
        var choice = prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip") ??
            prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("response") is "take-damage" or "pass") ??
            prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("choice") == "finish") ?? prompt.Choices.First();
        Answer(game, choice);
    }
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(new AnswerPromptCommand(game.PendingDecision!.PlayerSeat, game.PendingDecision.PromptId, choice.Id, game.Revision)));
    private static void EqualReplay(GameEngine game, ContentRegistry registry)
    {
        var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(replay.CreateSnapshot(0, true)), "Classic god state must survive checkpoint replay.");
        Require(game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).SequenceEqual(replay.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))), "Classic god typed events must replay deterministically.");
    }
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Classic god command rejected.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture(string flavor) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("classic-god-fixture", new Version(1, 0, 0), [new PackageDependency("standard", new Version(1, 0, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddCard(new ContentCardDefinition("fixture:silver-lion", "白银狮子", "装备牌", "Fixture", CardKind.SilverLion));
            var assembly = typeof(StandardContentPackage).Assembly;
            foreach (var bundle in new[] { "ol-shen-zhou-yu", "ol-shen-zhuge-liang", "ol-shen-lu-bu", "ol-shen-zhao-yun", "passive-card-rules" })
            {
                string Read(string suffix) { using var stream = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var catalog = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
                foreach (var (id, program) in catalog.Programs.Where(pair => bundle != "passive-card-rules" || pair.Key == "classic:wushuang"))
                {
                    var presentation = catalog.Presentations[id];
                    builder.AddSkill(new ContentSkillDefinition(id, presentation.Name, presentation.Description) { Program = program, ProgramPresentation = presentation });
                }
            }
            var driver = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:classic-god-driver","revision":1,"minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"activations":[
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20},{"op":"draw","target":"owner","amount":20}]},
                {"id":"hurt-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHpUnclamped","target":"owner","amount":2}]},
                {"id":"hurt-three","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHpUnclamped","target":"owner","amount":3}]},
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"damage-three","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":3}]},
                {"id":"give-lion","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"cardCategories":["equipment"],"cardKinds":["silverLion"],"count":1,"destination":"selectedTargetEquipment","targetRef":{"kind":"selectedTarget"} }]},
                {"id":"six-rage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setMarkerAmount","target":"owner","marker":"rage","amount":6}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:classic-god-driver":{"name":"Fixture","description":"Fixture"}}}""");
            builder.AddSkill(new ContentSkillDefinition(Driver, "Fixture", "Fixture") { Program = driver.Programs[Driver], SelectionWeights = new Dictionary<Role, double> { [Role.Lord] = -10000 } });
            var legacyOwn = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:legacy-own-discard","revision":1,
                "triggers":[{"id":"end","window":"discardPhaseEnded","turnOwnerScope":"own","subject":"owner","optional":false,
                "effects":[{"op":"setMarkerAmount","target":"owner","marker":"rage","amount":1}]},
                {"id":"default-end","window":"discardPhaseEnded","subject":"owner","optional":false,
                "effects":[{"op":"setMarkerAmount","target":"owner","marker":"rage","amount":2}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:legacy-own-discard":{"name":"Legacy discard","description":"Legacy discard"}}}""");
            builder.AddSkill(new ContentSkillDefinition("fixture:legacy-own-discard", "Legacy discard", "Legacy discard")
                { Program = legacyOwn.Programs["fixture:legacy-own-discard"] });
            string[] skills = flavor switch { "zhuge" => ["ol:qixing", "ol:kuangfeng", "ol:dawu"], "lu" or "lu-armor" => ["ol:kuangbao", "ol:wumou", "ol:wuqian", "ol:shenfen"], "zhao" or "zhao-response" or "zhao-null" => ["ol:juejing", "ol:longhun"], _ => ["ol:qinyin", "ol:yeyan"] };
            builder.AddGeneral(new ContentGeneralDefinition("fixture:classic-god-owner", "Fixture", "supporter", skills[0], "god", BaseHp: flavor is "zhao" or "zhou-death" ? 2 : flavor == "zhao-response" ? 20 : 5, AdditionalSkillIds: [.. skills.Skip(1), Driver, "fixture:legacy-own-discard"]));
            var opponents = Enumerable.Range(1, 3).Select(index => $"fixture:classic-god-opponent-{index}").ToArray();
            builder.AddSkill(new ContentSkillDefinition("fixture:classic-god-observer", "Observer", "Observer"));
            if (flavor == "zhao-response")
            {
                var incoming = SkillProgramCatalog.Load($$"""
                    {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:incoming-slash","revision":1,"minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"triggers":[{"id":"turn-slash","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"priority":0,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget"}]}]}]}
                    """, """{"schemaVersion":3,"skills":{"fixture:incoming-slash":{"name":"Incoming","description":"Incoming"}}}""");
                builder.AddSkill(new ContentSkillDefinition("fixture:incoming-slash", "Incoming", "Incoming") { Program = incoming.Programs["fixture:incoming-slash"] });
            }
            foreach (var opponent in opponents) builder.AddGeneral(new ContentGeneralDefinition(opponent, "Opponent", "supporter", flavor == "zhao-response" ? "fixture:incoming-slash" : "fixture:classic-god-observer", "qun", BaseHp: 20));
            builder.AddDeck(new ContentDeckRecipe("fixture:classic-god-deck", "Fixture", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 400).Select(index => new ContentDeckPhysicalCard(flavor == "zhao-null" && index % 5 == 0 ? "standard:draw_two" : flavor == "lu-armor" && index % 10 == 0 ? "fixture:silver-lion" : "standard:nullification", (Suit)(index % 4), index % 13 + 1)).ToArray() });
            builder.AddMode(new ContentModeDefinition(Mode, "Fixture", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 }, "fixture:classic-god-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:classic-god-owner", .. opponents]));
        }
    }
}





