using System.IO;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class NativeCollectedDiscardProjectionChecks
{
    private const string Mode = "identity:classic-native-collected-discard";
    private const string Owner = "fixture:native-collected-owner";
    private const string Driver = "fixture:native-collected-driver";

    public static void CaoXianAdjustmentAndTargetDiscardPreserveNativeCollectionCold()
    {
        var (game, registry) = CreateCaoXianDiscardScene();
        var prompt = Prompt(game)!;
        Require(prompt.SkillPrompt?.SkillId == "ol:lingxi" && prompt.RequiredCardCount == 5 && prompt.MinimumCardCount == 5,
            "The actual wing removal exposes Lingxi's five-card unpaid hand adjustment despite the native producer having no SkillPrompt.");
        var before = game.CardMovements.Count;
        var first = prompt.Choices.First(c => HasRole(c, "pick"));
        AssertProjectionOnly(game, first); Pick(game, c => c.Id == first.Id);
        Require(Prompt(game) is { RequiredCardCount: 4, MinimumCardCount: 4 } remaining &&
            remaining.Choices.All(c => c.Parameters.GetValueOrDefault("owned-selection-selected") == first.Cards.Single().ToString()) &&
            game.CardMovements.Count == before, "A native partial adjustment retains its original selected identity and moves no unpaid cards.");
        game = Cold(game, registry);
        for (var i = 0; i < 4; i++) Pick(game, c => HasRole(c, "pick"));
        Reach(game, _ => game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.CaoXian?.Stage == CaoXianStage.OptionTarget);
        Require(game.CardMovements.Skip(before).Count(m => m.Reason.Value == "skill-program.ol:lingxi-or-zhifou.discard") == 5,
            "All five Lingxi adjustment entities pay once as their original collection completes.");
        BeginCaoXianSelfDiscard(game);
        prompt = Prompt(game)!; before = game.CardMovements.Count;
        Require(prompt.SkillPrompt?.SkillId == "ol:zhifou" && prompt.RequiredCardCount == 2 && prompt.MinimumCardCount == 2 &&
            prompt.Choices.All(c => HasRole(c, "pick")), "Zhifou's self-target second option is a separate genuine two-card unpaid batch.");
        first = prompt.Choices[0]; Pick(game, c => c.Id == first.Id); game = Cold(game, registry);
        Require(Prompt(game) is { RequiredCardCount: 1 } && game.CardMovements.Count == before,
            "Cold recovery does not pay Zhifou's first singleton or combine it with the earlier Lingxi set.");
        Pick(game, c => HasRole(c, "pick")); Reach(game, IsOwnPlay);
        Require(game.CardMovements.Skip(before).Count(m => m.Reason.Value == "skill-program.ol:lingxi-or-zhifou.discard") == 2 &&
            game.CreateSnapshot(0).Players[0].HandCount == 0, "The exact final native selection pays the second two-card batch once.");
    }

    internal static (GameEngine Game, ContentRegistry Registry) CreateCaoXianDiscardScene()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new CaoXianFixture());
        ShanheStartingCard[] hand = [new(CardKind.Dodge, Suit.Heart, 1), new(CardKind.Dodge, Suit.Heart, 2),
            new(CardKind.DrawTwo, Suit.Club, 3), new(CardKind.Dodge, Suit.Spade, 4), new(CardKind.Dodge, Suit.Club, 5),
            new(CardKind.Dodge, Suit.Diamond, 6), new(CardKind.Dodge, Suit.Spade, 7), new(CardKind.Dodge, Suit.Club, 8)];
        var setup = new ShanheBattleSetup(373, 1, "cao-xian-native-discard-draft", "ol:cao-xian", 3, 0, 0, 0, 0,
            [CaoXianFixture.Driver], hand, [], [new(CaoXianFixture.Peer, Hp: 8) { StartingHand = [] },
                new(CaoXianFixture.Peer, Hp: 8) { StartingHand = [] }, new(CaoXianFixture.Peer, Hp: 8) { StartingHand = [] }], [], new string('c', 64));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0,
            HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
            ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
        AnswerCommand(game, new StartGameCommand());
        Reach(game, p => p?.SkillPrompt?.SkillId == "ol:lingxi" && Has(p, "program-action", "activate"));
        Pick(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, _ => game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.CaoXian?.Stage == CaoXianStage.StoreCards);
        Require(!Prompt(game)!.Choices.Any(c => c.Parameters.ContainsKey("owned-selection-role")),
            "Placing real cards in wings remains storage rather than being relabeled as a discard batch.");
        var wings = game.CreateSnapshot(0).Players[0].Hand.Where(c => c.Kind == CardKind.Dodge && c.Suit == Suit.Heart).Select(c => c.Id).ToArray();
        foreach (var id in wings) Pick(game, c => c.Cards.SequenceEqual([id]));
        Pick(game, c => c.Parameters.GetValueOrDefault("branch") == "finish"); Reach(game, IsOwnPlay);
        var draw = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.DrawTwo && a.ConversionSource is null);
        AnswerCommand(game, new PlayCardCommand(0, draw.CardId!.Value, draw.TargetSeats, game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => p?.SkillPrompt?.SkillId == "ol:zhifou" && Has(p, "program-action", "activate"));
        Pick(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, _ => game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.CaoXian?.Stage == CaoXianStage.RemoveCards);
        Require(!Prompt(game)!.Choices.Any(c => c.Parameters.ContainsKey("owned-selection-role")),
            "Removing a wing from its special area remains its original special-pile choice.");
        Pick(game, c => c.Cards.SequenceEqual([wings[0]])); Pick(game, c => c.Parameters.GetValueOrDefault("branch") == "finish");
        Reach(game, _ => game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.CaoXian?.Stage == CaoXianStage.AdjustCards);
        return (game, registry);
    }

    internal static void BeginCaoXianSelfDiscard(GameEngine game)
    {
        Reach(game, _ => game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.CaoXian?.Stage == CaoXianStage.OptionTarget);
        Pick(game, c => c.Parameters.GetValueOrDefault("option") == "2" && c.Targets.SequenceEqual([0]));
        Reach(game, _ => game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.CaoXian is { Stage: CaoXianStage.EffectCards, Option: 2 });
    }
    private static bool HasRole(PromptChoice c, string role) => c.Parameters.GetValueOrDefault("owned-selection-role") == role;

    public static void FixedAndVariableNativeCollectionsPublishRemainingBoundsAndFinish()
    {
        var (fixedGame, fixedRegistry) = Start();
        AnswerCommand(fixedGame, new EndPlayPhaseCommand(0, fixedGame.Revision, Prompt(fixedGame)!.PromptId));
        Reach(fixedGame, p => p?.SkillPrompt?.SkillId == "classic:jinqu" && Has(p, "program-action", "activate"));
        Pick(fixedGame, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(fixedGame, p => Has(p, "owned-selection-role", "pick"));
        var initial = Prompt(fixedGame)!;
        var required = fixedGame.CreateSnapshot(0).Players[0].Hand.Count;
        Require(required > 1 && initial.RequiredCardCount == required && initial.MinimumCardCount == required,
            "Actual Jinqu publishes the whole fixed unpaid discard requirement.");
        var origin = initial.Choices.First().Parameters;
        Require(origin["owned-selection-stage"] == "named-turn-hand" && origin.ContainsKey("owned-selection-frame"),
            "The projected native collection identifies its owning frame and exact stage.");
        var before = fixedGame.CardMovements.Count;
        var first = initial.Choices.First(c => c.Parameters.GetValueOrDefault("owned-selection-role") == "pick");
        AssertProjectionOnly(fixedGame, first);
        Pick(fixedGame, c => c.Id == first.Id);
        Require(Prompt(fixedGame) is { } remaining && remaining.RequiredCardCount == required - 1 &&
            remaining.MinimumCardCount == required - 1 && fixedGame.CardMovements.Count == before &&
            remaining.Choices.All(c => c.Parameters["owned-selection-frame"] == origin["owned-selection-frame"] &&
                c.Parameters["owned-selection-stage"] == origin["owned-selection-stage"] &&
                c.Parameters["owned-selection-selected"] == first.Cards.Single().ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "One accepted native pick reduces the remaining bound without moving any of the set.");
        fixedGame = Cold(fixedGame, fixedRegistry);
        for (var index = 1; index < required; index++) Pick(fixedGame, c => c.Parameters.GetValueOrDefault("owned-selection-role") == "pick");
        Require(fixedGame.CardMovements.Skip(before).Count(m => m.Reason.Value == "skill-program.named-turn-count.discard") == required,
            "The fixed collection commits all actual cards once after its final native pick.");

        var (variableGame, variableRegistry) = Start();
        Use(variableGame, "draw", []);
        Reach(variableGame, IsOwnPlay);
        Use(variableGame, "incoming", [1]);
        Reach(variableGame, p => p?.SkillPrompt?.SkillId == "ol:benyu" && Has(p, "program-action", "activate"));
        Pick(variableGame, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(variableGame, p => Has(p, "branch", "retaliate"));
        Pick(variableGame, c => c.Parameters.GetValueOrDefault("branch") == "retaliate");
        var cost = Prompt(variableGame)!;
        var minimum = variableGame.CreateSnapshot(1).Players[1].Hand.Count + 1;
        Require(cost.MinimumCardCount == minimum && cost.RequiredCardCount > minimum &&
            cost.Choices.Where(c => c.Cards.Count == 1).All(c => c.Parameters.GetValueOrDefault("owned-selection-role") == "pick") &&
            cost.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "decline" &&
                !c.Parameters.ContainsKey("owned-selection-role")),
            "Actual Benyu publishes a variable lower bound, whole legal maximum and separate decline alternative.");
        before = variableGame.CardMovements.Count;
        for (var index = 0; index < minimum + 1; index++)
        {
            Pick(variableGame, c => c.Parameters.GetValueOrDefault("owned-selection-role") == "pick");
            Require(Prompt(variableGame)!.RequiredCardCount == cost.RequiredCardCount - index - 1 &&
                Prompt(variableGame)!.MinimumCardCount == Math.Max(0, minimum - index - 1) &&
                variableGame.CardMovements.Count == before,
                "Variable native picks reduce both live bounds while their payment remains entirely unpaid.");
            if (index == 0) variableGame = Cold(variableGame, variableRegistry);
        }
        var finish = Prompt(variableGame)!.Choices.Single(c => c.Parameters.GetValueOrDefault("owned-selection-role") == "finish");
        Require(finish.Parameters["branch"] == "pay" && finish.Cards.Count == 0,
            "The original native pay choice receives only presentation metadata.");
        AssertProjectionOnly(variableGame, finish);
        Pick(variableGame, c => c.Id == finish.Id);
        Require(variableGame.CardMovements.Skip(before).Count(m => m.Reason.Value == "skill-program.damage-source-hand.discard") == minimum + 1,
            "The chosen variable set moves exactly once through the original native pay command.");
        Cold(variableGame, variableRegistry);
    }

    private static (GameEngine, ContentRegistry) Start()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        AnswerCommand(game, new StartGameCommand());
        Reach(game, p => p?.Kind == DecisionKind.SelectGeneral);
        AnswerCommand(game, new SelectGeneralCommand(0, Owner, game.Revision, Prompt(game)!.PromptId));
        Reach(game, IsOwnPlay);
        return (game, registry);
    }

    private static bool IsOwnPlay(PendingDecision? p) => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static PendingDecision? Prompt(GameEngine game) => game.CreateSnapshot(0).PendingDecision;
    private static bool Has(PendingDecision? p, string key, string value) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault(key) == value) == true;
    private static void Use(GameEngine game, string activation, IReadOnlyList<int> targets) =>
        AnswerCommand(game, new UseProgramSkillCommand(0, Driver, activation, [], targets, game.Revision, Prompt(game)!.PromptId));
    private static void Pick(GameEngine game, Func<PromptChoice, bool> select)
    {
        var prompt = Prompt(game)!;
        AnswerCommand(game, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(select).Id, game.Revision));
    }

    private static void Reach(GameEngine game, Func<PendingDecision?, bool> stop)
    {
        for (var step = 0; step < 160; step++)
        {
            var prompt = Prompt(game);
            if (stop(prompt)) return;
            if (prompt is { Kind: DecisionKind.DiscardCards })
                AnswerCommand(game, new DiscardCardsCommand(0, prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, game.Revision));
            else if (prompt is { Choices.Count: > 0 })
            {
                var choice = prompt.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip") ?? prompt.Choices[0];
                AnswerCommand(game, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
            }
            else AnswerCommand(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The real fixed discard fixture did not reach its native boundary.");
    }

    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(copy.CreateSnapshot(seat))),
            "Cold replay preserves every player-safe projection of partially selected native costs.");
        return copy;
    }
    private static void AssertProjectionOnly(GameEngine game, PromptChoice projected)
    {
        var native = game.PendingDecision!.Choices.Single(choice => choice.Id == projected.Id);
        Require(projected.Cards.SequenceEqual(native.Cards) && projected.Targets.SequenceEqual(native.Targets) &&
            projected.ContentIds.SequenceEqual(native.ContentIds) && native.Parameters.All(pair => projected.Parameters.GetValueOrDefault(pair.Key) == pair.Value) &&
            !native.Parameters.ContainsKey("owned-selection-role"),
            "The public selection metadata does not rewrite native command inputs or the owning runtime prompt.");
        var immutable = false;
        try { ((IDictionary<string, string>)projected.Parameters)["owned-selection-role"] = "altered"; }
        catch (NotSupportedException) { immutable = true; }
        Require(immutable, "Projected selection metadata is read-only to observers.");
    }
    private static void AnswerCommand(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "Native collection command was rejected.");
    }
    private static void Require(bool yes, string why) { if (!yes) throw new InvalidOperationException(why); }

    private sealed class CaoXianFixture : IGameContentPackage
    {
        internal const string Driver = "fixture:native-cao-xian-driver", Peer = "fixture:native-cao-xian-peer", Deck = "fixture:native-cao-xian-deck";
        public PackageManifest Manifest { get; } = new("fixture-native-cao-xian-discard", new(1, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:cao-xian");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,
              "triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]}]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "固定真实手牌", description = "以真实翼与卡牌效果驱动弃牌阶段。" } } }));
            b.AddSkill(new(Driver, "固定真实手牌", "以真实翼与卡牌效果驱动弃牌阶段。")
                { Program = catalog.Programs[Driver], ProgramPresentation = catalog.Presentations[Driver] });
            b.AddGeneral(new(Peer, "实际同桌", "", "standard:none", "qun", 8));
            b.AddDeck(new(Deck, "固定真实闪牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80)
                .Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 10)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "曹宪原生多选弃牌", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: ["ol:cao-xian", Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-native-collected-discard", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var name in new[] { "classic-wang-ji", "ol-cheng-yu" })
            {
                using var rules = typeof(StandardContentPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{name}.rules.json")!;
                using var labels = typeof(StandardContentPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{name}.presentation.json")!;
                using var ruleReader = new StreamReader(rules);
                using var labelReader = new StreamReader(labels);
                Register(SkillProgramCatalog.Load(ruleReader.ReadToEnd(), labelReader.ReadToEnd()));
            }
            var driverPresentation = JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                { [Driver] = new { name = "实际结算驱动", description = "以真实摸牌和伤害进入原生选牌窗口。" } }
            });
            Register(SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"activations":[
              {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":8}]},
              {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]}]}]}
            """, driverPresentation));
            void Register(SkillProgramCatalog catalog)
            {
                foreach (var (id, program) in catalog.Programs)
                    builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    { Program = program, ProgramPresentation = catalog.Presentations[id] });
            }
            builder.AddGeneral(new(Owner, "真实多张弃牌", "supporter", Driver, "wei", 12, ["classic:jinqu", "ol:benyu"]));
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new($"fixture:native-collected-{seat}", "目标", "supporter", "standard:none", "wei", 12));
            builder.AddDeck(new("fixture:native-collected-deck", "固定实体牌堆", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard("standard:dodge", (Suit)(i % 4), i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "原生集合投影", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:native-collected-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, "fixture:native-collected-1", "fixture:native-collected-2", "fixture:native-collected-3"]));
        }
    }
}
