using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhangLingChecks
{
    private const string Driver = "fixture:zhangling-driver", Give = "fixture:zhangling-give", Pause = "fixture:zhangling-pause";
    private const string Discard = "fixture:zhangling-discard", Attack = "fixture:zhangling-attack", Judge = "fixture:zhangling-judge";
    private const string Quiet = "fixture:zhangling-quiet", Mode = "identity:classic-zhangling-fixture";
    private const string Replacement = "fixture:zhangling-replacement";
    private const string Conditional = "fixture:zhangling-conditional";

    public static void ShoufuActualHandPlacementAndIssuerLossKeepPublicTalismanUntilDamage()
    {
        var (game, registry) = Start("place"); ReachPlay(game);
        var before = game.CreateSnapshot(0).Players[0].HandCount;
        Accept(game, new UseProgramSkillCommand(0, "ol:shoufu", "draw-and-place-talisman", [], [], game.Revision, P(game)!.PromptId));
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
        Require(game.CreateSnapshot(0).Players[0].HandCount == before + 1 && P(game)!.IsPrivate,
            "Shoufu performs its actual draw before choosing exactly one current private hand entity.");
        var card = P(game)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        Answer(game, c => c.Cards.SequenceEqual([card])); Reach(game, p => IsTarget(p, "OtherLivingWithoutCategoryTalisman"));
        game = Cold(game, registry); Answer(game, c => c.Targets.SequenceEqual([1]));
        Reach(game, p => p.SkillPrompt?.SkillId == Pause);
        Require(E<CategoryTalismanPlacedEvent>(game) is [{ HolderSeat: 1, CardId: var placed }] && placed == card &&
            game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "ol:shoufu").CategoryTalismanMovement is not null &&
            Enumerable.Range(0, 4).All(seat => Pile(game, seat, 1).SequenceEqual([card])),
            "Placement is a real public source-aware pile, while its paid movement continuation remains on the issuing frame.");
        game = Cold(game, registry); Answer(game, c => true); ReachPlay(game);
        Use(game, "lose-shoufu"); ReachPlay(game);
        Require(!game.CreateSnapshot(0).Players[0].Skills!.Any(s => s.ContentId == "ol:shoufu") && Pile(game, 0, 1).SequenceEqual([card]),
            "Losing the issuer's Shoufu leaves the holder's persistent talisman and prohibition intact.");
        game = Cold(game, registry); Use(game, "hurt", [1]); ReachPlay(game);
        Require(Pile(game, 0, 1).Count == 0 && E<CategoryTalismanReleasedEvent>(game) is [{ HolderSeat: 1, CardId: var released }] && released == card &&
            game.CardMovements.Count(m => m.CardId == card && m.Reason.Value == "program.category-talisman.place") == 1 &&
            game.CardMovements.Count(m => m.CardId == card && m.Reason.Value == "program.category-talisman.release") == 1,
            "A later actual received damage releases the exact talisman once after its issuer lost the skill; replay never repays placement.");
        Cold(game, registry);
    }

    private static T[] E<T>(GameEngine game) where T : IGameEvent => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine game) => game.CreateSnapshot(0).PendingDecision;
    private static IReadOnlyList<int> Pile(GameEngine game, int viewer, int holder)
    {
        var player = game.CreateSnapshot(viewer).Players[holder];
        return player.PublicPersistentPiles?.Where(p => p.SourceSkillId == "ol:shoufu-talisman").SelectMany(p => p.Cards).Select(c => c.Id).ToArray() ??
            (player.PublicPersistentPileSkillId == "ol:shoufu-talisman" ? player.PublicPersistentPileCards?.Select(c => c.Id).ToArray() : null) ?? [];
    }
    private static bool IsTarget(PendingDecision p, string target) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("target-kind") == target);
    private static void ReachPlay(GameEngine game) => Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Use(GameEngine game, string binding, IReadOnlyList<int>? targets = null) => Accept(game,
        new UseProgramSkillCommand(0, Driver, binding, [], targets ?? [], game.Revision, P(game)!.PromptId));
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var p = P(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, game.Revision)); }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 200; step++)
        {
            var p = P(game); if (p is not null && predicate(p)) return;
            if (p?.Kind == DecisionKind.DiscardCards) Accept(game, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, game.Revision));
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass") == true) Answer(game, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "take-damage") == true) Answer(game, c => c.Parameters.GetValueOrDefault("response") == "take-damage");
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        var prompt = JsonSerializer.Serialize(P(game));
        throw new InvalidOperationException("Zhang Ling boundary missing: " + prompt[..Math.Min(500, prompt.Length)] + "; frames=" +
            JsonSerializer.Serialize(game.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step })) + "; facts=" + JsonSerializer.Serialize(game.Events.TakeLast(6).Select(e => e.Payload.ToString())));
    }
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
            Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements });
        Require(State(game) == State(restored), "Cold replay preserves every viewer, frozen judgment, exact talisman source, paid movement and actual phase receipt."); return restored;
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected Zhang Ling command."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:zhangling-owner", game.Revision, P(game)!.PromptId)); return (game, registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-zhang-ling", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "ol:zhang-ling", "classic:guo-jia");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"range","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0}],"viewAs":[
                {"id":"duel","inputKinds":["slash"],"inputSuits":[],"outputKind":"duel","singleCardTrickUse":true,"forPlay":true,"forResponse":false},
                {"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":true},
                {"id":"dodge","inputKinds":[],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true}],
                "triggers":[{"id":"stock","window":"turnStartBeforeNormalFlow","subject":"owner","priority":1000,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"draw","target":"owner","amount":12}]}],"activations":[
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
                {"id":"discard-play","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"lose-shoufu","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:shoufu"],"sourceBind":"{{Replacement}}"}]}]},
              {"id":"{{Give}}","revision":1,"triggers":[{"id":"give","window":"gameStarting","subject":"owner","optional":false,"effects":[
                {"op":"draw","target":"owner","amount":1},{"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["hand"],"resultBind":"gift"},
                {"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"placeBoundHandAsCategoryTalisman","target":"owner","sourceBind":"gift"}]}]},
              {"id":"{{Pause}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perOwnerBatch","movementReasons":["program.category-talisman.place"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"{{Discard}}","revision":1,"triggers":[{"id":"two-batches","window":"discardPhaseStarting","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[
                {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true},
                {"op":"chooseOption","target":"owner","resultBind":"first","options":[{"id":"continue"}]},
                {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]}]},
              {"id":"{{Attack}}","revision":1,"triggers":[{"id":"native-attack","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingHighestHand"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]},
              {"id":"{{Judge}}","revision":1,"triggers":[{"id":"judged-loss","window":"judgmentFinalized","subject":"owner","suits":["spade","club","heart","diamond"],"minimumRank":1,"maximumRank":13,"excludedReasons":[],"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-huqi","skillId":"ol:huqi","minimumHp":12}]},
              {"id":"{{Replacement}}","revision":1,"modifiers":[{"id":"benign-replacement","query":"handLimit","operation":"add","value":1,"priority":0}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var ids = new[] { Driver, Give, Pause, Discard, Attack, Judge, Quiet, Replacement, Conditional };
            var presentation = JsonSerializer.Serialize(new { schemaVersion = 3, skills = ids.ToDictionary(id => id,
                id => new { name = id, description = "张陵原生结算夹具", optionLabels = id is Pause or Discard or Judge ? new Dictionary<string, string> { ["continue"] = "继续" } : [] }) });
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, id, "张陵实际机制夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var huqi = scenario.StartsWith("huqi", StringComparison.Ordinal);
            builder.AddGeneral(new("fixture:zhangling-owner", "张陵机制", "ol-zhang-ling", huqi ? Conditional : "ol:shoufu", "qun", 12,
                huqi ? scenario == "huqi-claimed" ? [Driver, Judge, "classic:tiandu"] : [Driver, Judge] : scenario == "discard" ? [Driver, Discard] : scenario == "place" ? [Driver, Pause] : [Driver]));
            for (var peer = 1; peer < 4; peer++) builder.AddGeneral(new($"fixture:zhangling-peer-{peer}", "其他", "supporter", Quiet, "qun", 12,
                peer == 1 ? huqi ? [Attack] : scenario is "receive" or "discard" ? [Give] : [] : []));
            builder.AddDeck(new("fixture:zhangling-deck", "固定类型与判定颜色", 4, 1, []) { PhysicalCards = Enumerable.Range(0, 128)
                .Select(i => new ContentDeckPhysicalCard("standard:slash", scenario == "huqi-black" ? Suit.Spade : Suit.Heart, 7)).ToArray() });
            builder.AddMode(new(Mode, "张陵实际机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:zhangling-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:zhangling-owner", "fixture:zhangling-peer-1", "fixture:zhangling-peer-2", "fixture:zhangling-peer-3"]));
        }
    }
}
