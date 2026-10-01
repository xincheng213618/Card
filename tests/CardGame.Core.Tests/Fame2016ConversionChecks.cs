using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2016ConversionChecks
{
    public static void ConfiguredConversionResourceContracts()
    {
        var assembly = typeof(StandardClassicGeneralPackage).Assembly;
        string Read(string kind)
        {
            using var stream = assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.classic-guo-huanghou.{kind}.json")!;
            using var reader = new StreamReader(stream); return reader.ReadToEnd();
        }
        var rules = Read("rules"); var presentation = Read("presentation");
        void Reject(Action<System.Text.Json.Nodes.JsonObject> mutate)
        {
            var document = System.Text.Json.Nodes.JsonNode.Parse(rules)!.AsObject(); mutate(document);
            try { SkillProgramCatalog.Load(document.ToJsonString(), presentation); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException("A malformed conversion resource contract was accepted.");
        }
        System.Text.Json.Nodes.JsonNode Skill(System.Text.Json.Nodes.JsonObject node) => node["skills"]![0]!;
        System.Text.Json.Nodes.JsonNode Direct(System.Text.Json.Nodes.JsonObject node) => Skill(node)["viewAs"]!.AsArray().First(rule => rule!["id"]!.GetValue<string>() == "direct-duel")!;
        Reject(node => Skill(node)["activations"]![0]!["sourceZones"] = new System.Text.Json.Nodes.JsonArray("equipment"));
        Reject(node => Skill(node)["activations"]![0]!["effects"]![1]!["sourceBind"] = "unknown-card-binding");
        Reject(node => Direct(node)["activationUsageGroup"] = "unknown-activation");
        Reject(node => Direct(node)["maximumTier"] = 3);
        Reject(node => Direct(node)["outputKind"] = "indulgence");
        Reject(node => Direct(node)["inputCount"] = 2);
        Reject(node => Skill(node)["viewAs"]![0]!["sourceZones"] = new System.Text.Json.Nodes.JsonArray("equipment"));
        Reject(node => node["skills"]![1]!["triggers"]![0]!["effects"]![2]!["stateId"] = "unknown-conversion-state");
    }

    public static void DeclarationEntityPrivacyAndReplay()
    {
        var (game, registry) = Create();
        var card = game.CreateSnapshot(0).Players[0].Hand[0];
        Declare(game, registry, card.Id, CardKind.Slash);
        var action = game.GetHumanLegalActions().First(action => action.ConversionSource?.SkillId == "classic:jiaozhao" && action.TargetSeat == 1);
        Require(action.CardId == card.Id && game.GetHumanLegalActions().Where(action => action.ConversionSource?.SkillId == "classic:jiaozhao")
            .All(action => action.CardId == card.Id), "Only the exact declared hand entity can use its declared name.");
        Play(game, action);
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Require(game.CardMovements.Count(move => move.CardId == card.Id && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1 &&
            game.CardMovements.Any(move => move.CardId == card.Id && move.To == CardLocation.DiscardPile), "The declared card pays one real card cost and finishes in discard.");
        Require(!game.GetHumanLegalActions().Any(action => action.ProgramSkillId == "classic:jiaozhao" || action.ConversionSource?.SkillId == "classic:jiaozhao"), "The declaration cannot be activated again in its Play phase.");
        Replay(game, registry);
    }

    public static void TierModificationNativeTricksAndReplay()
    {
        var (game, registry) = Create();
        Upgrade(game, registry);
        var card = game.CreateSnapshot(0).Players[0].Hand[0];
        Declare(game, registry, card.Id, CardKind.Duel);
        var action = game.GetHumanLegalActions().First(action => action.ConversionSource?.SkillId == "classic:jiaozhao" && action.PlayedCardKind == CardKind.Duel && action.TargetSeat == 1);
        Play(game, action);
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Require(game.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>().Any(item => item.CardId == card.Id && item.CardKind == CardKind.Duel),
            "The single physical card enters the real Duel pipeline rather than Slash.");
        Replay(game, registry);

        var (final, finalRegistry) = Create();
        Upgrade(final, finalRegistry); Upgrade(final, finalRegistry);
        var options = final.GetHumanLegalActions().Where(action => action.ConversionSource?.SkillId == "classic:jiaozhao").ToArray();
        Require(!final.GetHumanLegalActions().Any(action => action.ProgramSkillId == "classic:jiaozhao") && options.Length > 0 &&
            options.All(action => action.PlayedCardKind is not (CardKind.Peach or CardKind.Alcohol or CardKind.DrawTwo)), "The direct tier skips showing and declaring and cannot use self-only cards.");
        foreach (var kind in new[] { CardKind.PeachGarden, CardKind.FiveGrains })
            Require(options.Any(action => action.PlayedCardKind == kind) && options.Where(action => action.PlayedCardKind == kind).All(action => !action.TargetSeats.Contains(0)),
                "Global benefits exclude the skill owner from their real target array.");
        var use = options.First(action => action.PlayedCardKind == CardKind.FiveGrains);
        var before = State(final);
        var bad = final.Submit(new PlayCardCommand(0, use.CardId!.Value, [0], final.Revision, Prompt(final)!.PromptId, use.PlayedCardKind) { ConversionSource = use.ConversionSource });
        Require(!bad.Accepted && State(final) == before, "An illegal self target cannot consume the physical card or direct allowance.");
        var handBeforeGlobal = final.CreateSnapshot(0).Players[0].Hand.Count;
        Play(final, use);
        Replay(final, finalRegistry);
        Reach(final, prompt => prompt.Kind == DecisionKind.PlayCard);
        Require(final.CreateSnapshot(0).Players[0].Hand.Count == handBeforeGlobal - 1, "The excluded owner receives no Five Grains card while every other target resolves normally.");
        Require(!final.GetHumanLegalActions().Any(action => action.ConversionSource?.SkillId == "classic:jiaozhao"), "All direct output names share one phase allowance.");
        Require(!final.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>().Any(), "The final tier never displays a hand card before converting it.");
        Replay(final, finalRegistry);
        Damage(final); ActivateDanxin(final);
        var decision = Prompt(final)!;
        Require(decision.Choices.All(choice => choice.Parameters.GetValueOrDefault("option-id") != "upgrade"), "A third upgrade is unavailable.");
        Answer(final, _ => true); Reach(final, prompt => prompt.Kind == DecisionKind.PlayCard);
        Require(final.Events.Select(item => item.Payload).OfType<ConfiguredConversionTierChangedEvent>().Count() == 2, "The tier remains capped at two.");
        Replay(final, finalRegistry);
    }

    public static void DeclarationAndDirectSharePhaseAllowance()
    {
        var (game, registry) = Create();
        var id = game.CreateSnapshot(0).Players[0].Hand[0].Id;
        Declare(game, registry, id, CardKind.Slash);
        Upgrade(game, registry); Upgrade(game, registry);
        var options = game.GetHumanLegalActions().Where(action => action.ConversionSource?.SkillId == "classic:jiaozhao").ToArray();
        Require(options.Length > 0 && options.All(action => action.CardId == id && action.ConversionSource!.BindingId.StartsWith("declared-")),
            "Modification preserves the earlier turn declaration but cannot grant a second phase activation.");
        Replay(game, registry);
    }

    public static void DirectDodgeUseConsumesSharedAllowance()
    {
        var (game, registry) = Create(slashCards: true);
        Upgrade(game, registry); Upgrade(game, registry);
        var target = Enumerable.Range(1, 3).First(seat => game.GetCombatDistance(0, seat) == 1 &&
            game.CreateSnapshot(seat, true).Players[seat].Hand.Any(card => card.Kind == CardKind.Slash));
        Accept(game, new UseProgramSkillCommand(0, "classic:tiaoxin", "taunt", [], [target], game.Revision, Prompt(game)!.PromptId));
        Require(Prompt(game) is { PlayerSeat: var provider } && provider == target, "The real other player is asked to use their Slash.");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "request-slash");
        Reach(game, prompt => prompt.Kind == DecisionKind.RespondDodge && prompt.PlayerSeat == 0);
        var dodge = Prompt(game)!.Choices.First(choice => choice.Parameters.GetValueOrDefault("conversion-skill-id") == "classic:jiaozhao");
        var physical = dodge.Cards.Single();
        RejectUnknown(game); Replay(game, registry);
        Answer(game, choice => choice.Id == dodge.Id);
        Replay(game, registry); Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Require(game.CardMovements.Count(move => move.CardId == physical && move.To == CardLocation.Processing) == 1 &&
            game.CardMovements.Any(move => move.CardId == physical && move.To == CardLocation.DiscardPile), "The actual declared Dodge cost resolves once.");
        Require(!game.GetHumanLegalActions().Any(action => action.ConversionSource?.SkillId == "classic:jiaozhao"), "Using Dodge defensively spends the same direct tier allowance as proactive ordinary tricks.");
        Replay(game, registry);
    }

    private static void Declare(GameEngine game, ContentRegistry registry, int card, CardKind kind)
    {
        Accept(game, new UseProgramSkillCommand(0, "classic:jiaozhao", "declare-card", [card], [], game.Revision, Prompt(game)!.PromptId));
        var prompt = Prompt(game)!;
        Require(prompt.Choices.All(choice => choice.Targets.Count == 1 && choice.Targets[0] != 0 && game.GetCombatDistance(0, choice.Targets[0]) == 1), "The owner chooses only nearest living other characters.");
        foreach (var seat in Enumerable.Range(0, 4))
            Require(game.CreateSnapshot(seat).PublicRevealedCards.Single().Id == card, "The displayed real hand card is public during declaration.");
        RejectUnknown(game); Replay(game, registry);
        Answer(game, _ => true);
        var declarer = Prompt(game)!.PlayerSeat;
        Require(declarer != 0 && Prompt(game)!.Choices.Any(choice => choice.Parameters.GetValueOrDefault("choice") == kind.ToString()), "The selected other character declares a permitted real name.");
        foreach (var seat in Enumerable.Range(0, 4).Where(seat => seat != declarer))
            Require(game.CreateSnapshot(seat).PendingDecision is null || game.CreateSnapshot(seat).PendingDecision!.Choices.Count == 0, "Only the declarer sees their pending choices.");
        RejectUnknown(game); Replay(game, registry);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("choice") == kind.ToString());
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Replay(game, registry);
    }

    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void Damage(GameEngine game) => Accept(game, new UseProgramSkillCommand(0, "fixture:conversion-damage", "damage", [], [0], game.Revision, Prompt(game)!.PromptId));
    private static void ActivateDanxin(GameEngine game)
    {
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:danxin" && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
    }
    private static void Upgrade(GameEngine game, ContentRegistry registry)
    {
        Damage(game); ActivateDanxin(game); RejectUnknown(game); Replay(game, registry);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "upgrade");
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard); Replay(game, registry);
    }
    private static void Play(GameEngine game, LegalAction action) => Accept(game,
        new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, Prompt(game)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var prompt = Prompt(game)!; Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(predicate).Id, game.Revision)); }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 400; step++)
        {
            if (Prompt(game) is { } prompt && predicate(prompt)) return;
            if (Prompt(game) is { Kind: DecisionKind.SelectHarvestCard, PlayerSeat: 0 } draft) Answer(game, _ => true);
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The conversion fixture did not reach its prompt.");
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(), Frames = JsonSerializer.Serialize(game.ResolutionStack),
        Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(), Movements = game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands) });
    private static void RejectUnknown(GameEngine game)
    { var before = State(game); var p = Prompt(game)!; var result = game.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("fixture:illegal"), game.Revision)); Require(!result.Accepted && before == State(game), "An invalid answer preserves every view, event, real movement, cursor and accepted command."); }
    private static void Replay(GameEngine game, ContentRegistry registry)
    { var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry); Require(State(game) == State(replay), "Checkpoint and command JSON replay all public/private states and physical moves."); }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Command rejected."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine Game, ContentRegistry Registry) Create(bool slashCards = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Scenario(slashCards));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = "identity:conversion", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(game, new StartGameCommand()); Accept(game, new SelectGeneralCommand(0, "fixture:conversion-owner", game.Revision, Prompt(game)!.PromptId));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard); return (game, registry);
    }
    private sealed class Scenario(bool slashCards) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-conversion", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:conversion-damage","revision":1,"activations":[{"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:conversion-damage":{"name":"伤害","description":"测试真实伤害"}}}""");
            builder.AddSkill(new("fixture:conversion-damage", "伤害", "测试真实伤害") { Program = catalog.Programs["fixture:conversion-damage"] });
            builder.AddGeneral(new("fixture:conversion-owner", "郭皇后测试", "supporter", "classic:jiaozhao", "wei", BaseHp: 9, AdditionalSkillIds: ["classic:danxin", "classic:tiaoxin", "fixture:conversion-damage"]));
            foreach (var seat in Enumerable.Range(1, 3)) builder.AddGeneral(new($"fixture:conversion-{seat}", "目标", "supporter", "standard:none", "wei", BaseHp: 9));
            builder.AddDeck(new("fixture:conversion-deck", "真实装备牌", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 160).Select(index => new ContentDeckPhysicalCard(slashCards && index % 2 == 0 ? "standard:slash" : "standard:crossbow", index % 2 == 0 ? Suit.Spade : Suit.Heart, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:conversion", "转换测试", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:conversion-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:conversion-owner", "fixture:conversion-1", "fixture:conversion-2", "fixture:conversion-3"]));
        }
    }
}
