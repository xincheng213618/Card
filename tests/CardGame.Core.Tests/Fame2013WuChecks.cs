using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2013WuChecks
{
    public static void DanshouEscalationAndReplay()
    {
        RejectMalformedDynamicCosts();
        var (game, registry) = Create("classic:danshou", extraPlay: true);
        var turn = game.CreateSnapshot(0).TurnNumber;
        for (var ordinal = 1; ordinal <= 5; ordinal++)
        {
            var action = game.GetHumanLegalActions().Single(item => item.ProgramSkillId == "classic:danshou");
            Require(action.MinCardCount == ordinal && action.MaxCardCount == ordinal, "The exact cost must grow without a four-use cap.");
            var cards = action.SelectableCardIds.Take(ordinal).ToArray();
            var state = State(game); var journal = game.AcceptedCommands.Count; var movements = game.CardMovements.Count;
            var bad = game.Submit(new UseProgramSkillCommand(0, "classic:danshou", "escalating-discard",
                ordinal == 1 ? [] : cards.Take(ordinal - 1).ToArray(), [1], game.Revision, game.PendingDecision!.PromptId));
            Require(!bad.Accepted && State(game) == state && journal == game.AcceptedCommands.Count && movements == game.CardMovements.Count,
                "A wrong exact cost must reject without consuming an ordinal or mutating state.");
            if (ordinal == 1)
            {
                var distant = Enumerable.Range(1, 3).First(seat => !action.SelectableTargetSeats.Contains(seat));
                bad = game.Submit(new UseProgramSkillCommand(0, "classic:danshou", "escalating-discard", cards, [distant], game.Revision, game.PendingDecision!.PromptId));
                Require(!bad.Accepted && State(game) == state && journal == game.AcceptedCommands.Count, "Actual attack range must reject distant targets atomically.");
            }
            var before = game.CreateSnapshot(0, revealAll: true);
            Accept(game.Submit(new UseProgramSkillCommand(0, "classic:danshou", "escalating-discard", cards, [1], game.Revision, game.PendingDecision!.PromptId)));
            var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            ReachPlayPair(game, replay);
            var after = game.CreateSnapshot(0, revealAll: true);
            var cost = game.CardMovements.Skip(movements).Where(move => cards.Contains(move.CardId) && move.To == CardLocation.DiscardPile).ToArray();
            Require(cost.Length == ordinal && cost.Select(move => move.CardId).Distinct().Count() == ordinal, "Each activation discards its exact cost once.");
            if (ordinal == 1) Require(after.Players[1].HandCount == before.Players[1].HandCount - 1, "First invocation discards the chosen target card.");
            if (ordinal == 2) Require(after.Players[1].HandCount == before.Players[1].HandCount - 1 && after.Players[0].HandCount == before.Players[0].HandCount - ordinal + 1, "Second invocation lets the target give one card.");
            if (ordinal == 3) Require(after.Players[1].Hp == before.Players[1].Hp - 1, "Third invocation deals one damage.");
            if (ordinal >= 4) Require(after.Players[0].HandCount == before.Players[0].HandCount - ordinal + 2 && after.Players[1].HandCount == before.Players[1].HandCount + 2, "Fourth and fifth invocations both draw two for each participant.");
            Equivalent(game, replay);
        }
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 1024 && !(game.PendingDecision?.Kind == DecisionKind.PlayCard && game.PendingDecision.PlayerSeat == 0); step++) Step(game);
        var reset = game.GetHumanLegalActions().Single(item => item.ProgramSkillId == "classic:danshou");
        Require(reset.MinCardCount == 1 && reset.MaxCardCount == 1 && game.CreateSnapshot(0).TurnNumber == turn,
            "Normal Play after an extra Play phase resets the cost ordinal within the same turn.");
    }

    private static void RejectMalformedDynamicCosts()
    {
        var assembly = typeof(StandardContentPackage).Assembly;
        string Read(string suffix)
        {
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("classic-zhu-ran" + suffix, StringComparison.Ordinal)))!;
            using var reader = new StreamReader(stream); return reader.ReadToEnd();
        }
        var rules = Read(".rules.json"); var presentation = Read(".presentation.json");
        foreach (var edit in new Action<JsonObject>[]
        {
            node => node["minCards"] = -1,
            node => node["maxCards"] = 64,
            node => node["cardCountExpression"] = "unknownOrdinal",
            node => node["selectedCardsDistinctSuits"] = true,
            node => node["cardSuits"] = new JsonArray("unknownSuit"),
            node => node["cardKinds"] = new JsonArray("unknownKind"),
            node => node["sourceZones"] = new JsonArray("judgment")
        })
        {
            var root = JsonNode.Parse(rules)!;
            edit(root["skills"]![0]!["activations"]![0]!.AsObject());
            var rejected = false;
            try { _ = SkillProgramCatalog.Load(root.ToJsonString(), presentation); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Malformed dynamic costs and card-input filters must reject at definition load.");
        }
    }

    public static void ZongxuanSubsetOrderAndReplay()
    {
        var (game, registry) = Create("classic:zongxuan");
        for (var index = 0; index < 2; index++)
        {
            var equipment = game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Equip);
            Accept(game.Submit(new PlayCardCommand(0, equipment.CardId!.Value, [], game.Revision, game.PendingDecision!.PromptId)));
            ReachPlay(game);
        }
        Require(!game.Events.Any(item => item.Payload is ProgramSkillStartedEvent { SkillId: "classic:zongxuan" }), "Equipment replacement and use completion are not discard triggers.");
        var action = game.GetHumanLegalActions().Single(item => item.ProgramSkillId == "fixture:wu-discard");
        var equipped = game.CreateCardZoneDiagnostics().Single(item => item.Location == CardLocation.Equipment(0)).CardId;
        var ids = action.SelectableCardIds.Where(id => id != equipped).Take(11).Append(equipped).ToArray();
        Accept(game.Submit(new UseProgramSkillCommand(0, "fixture:wu-discard", "discard", ids, [], game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 64 && game.PendingDecision?.SkillPrompt?.SkillId != "classic:zongxuan"; step++) Step(game);
        Choose(game, game.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Require(game.PendingDecision!.Choices.Count(choice => choice.Cards.Count == 1) == 12, "The full mixed-zone discard batch must be available without an eight-card cap.");
        Choose(game, game.PendingDecision!.Choices.Single(choice => choice.Cards.SequenceEqual([ids[2]])));
        Require(game.CardMovements.Count(move => ids.Contains(move.CardId) && move.To == CardLocation.DrawPile) == 0, "Drafting must not move discarded cards.");
        var hidden = game.CreateSnapshot(1);
        Require(hidden.PendingDecision is null || hidden.PendingDecision.Choices.Count == 0, "Another viewer must not receive the private ordering choices.");
        var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        ChoosePair(game, replay, game.PendingDecision!.Choices.Single(choice => choice.Cards.SequenceEqual([ids[0]])));
        ChoosePair(game, replay, game.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "discard-top-finish"));
        ReachPlayPair(game, replay);
        Require(game.CardMovements.Count(move => move.CardId == ids[1] && move.To == CardLocation.DrawPile) == 0, "Unselected discarded cards stay in the discard pile.");
        Require(game.CreateCardZoneDiagnostics().Where(card => card.Location == CardLocation.DrawPile).OrderByDescending(card => card.ZoneIndex).Take(2).Select(card => card.CardId).SequenceEqual([ids[2], ids[0]]), "The selected order is the next-draw-first order.");
        Equivalent(game, replay);
    }

    public static void ZhiyanRealEquipmentUseAndBasicBranch()
    {
        foreach (var equipment in new[] { true, false })
        {
            var (game, registry) = Create("classic:zhiyan", equipment);
            Accept(game.Submit(new UseProgramSkillCommand(0, "fixture:wu-wound", "wound", [], [], game.Revision, game.PendingDecision!.PromptId)));
            ReachPlay(game);
            var hp = game.CreateSnapshot(0).Players[0].Hp;
            Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
            for (var step = 0; step < 256 && game.PendingDecision?.SkillPrompt?.SkillId != "classic:zhiyan"; step++) Step(game);
            Choose(game, game.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
            Choose(game, game.PendingDecision!.Choices.Single(choice => choice.Targets.SequenceEqual([0])));
            var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            for (var step = 0; step < 128 && game.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == "classic:zhiyan"); step++) StepPair(game, replay);
            var after = game.CreateSnapshot(0);
            Require(after.Players[0].Hp == hp + (equipment ? 1 : 0), "Only drawing an equipment card recovers HP.");
            if (equipment) Require(game.CardMovements.Any(move => move.To == CardLocation.Equipment(0)) && game.Events.Any(item => item.Payload is CardUseDeclaredEvent), "Equipment must be used through the real card-use pipeline.");
            Equivalent(game, replay);
        }
    }

    private static void ReachPlay(GameEngine game)
    { for (var step = 0; step < 256 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++) Step(game); Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Failed to reach Play."); }
    private static void ReachPlayPair(GameEngine game, GameEngine replay)
    { for (var step = 0; step < 256 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++) StepPair(game, replay); Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Failed to resume Play."); }
    private static void Step(GameEngine game)
    { if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } prompt) Choose(game, prompt.Choices[0]); else Accept(game.Submit(new AdvanceOneStepCommand(game.Revision))); }
    private static void StepPair(GameEngine game, GameEngine replay)
    { if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } prompt) ChoosePair(game, replay, prompt.Choices[0]); else { Accept(game.Submit(new AdvanceOneStepCommand(game.Revision))); Accept(replay.Submit(new AdvanceOneStepCommand(replay.Revision))); } }
    private static void Choose(GameEngine game, PromptChoice choice) => Accept(game.Submit(new AnswerPromptCommand(0, game.PendingDecision!.PromptId, choice.Id, game.Revision)));
    private static void ChoosePair(GameEngine game, GameEngine replay, PromptChoice choice) { Choose(game, choice); Choose(replay, replay.PendingDecision!.Choices.Single(item => item.Id == choice.Id)); }
    private static void Equivalent(GameEngine game, GameEngine replay) => Require(State(game) == State(replay) && JsonSerializer.Serialize(game.AcceptedCommands) == JsonSerializer.Serialize(replay.AcceptedCommands) && JsonSerializer.Serialize(game.Events) == JsonSerializer.Serialize(replay.Events) && game.CardMovements.SequenceEqual(replay.CardMovements), "Checkpoint replay must preserve state, commands, events and movements.");
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Command rejected.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(string skill, bool equipment = true, bool extraPlay = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new Fixture(skill, equipment, extraPlay));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4, ModeId = "fixture:wu-mode", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:wu-owner", game.Revision, game.PendingDecision!.PromptId)));
        ReachPlay(game); return (game, registry);
    }

    private sealed class Fixture(string skill, bool equipment, bool extraPlay) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wu", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            foreach (var bundle in new[] { "classic-yu-fan", "classic-zhu-ran" })
            {
                string Read(string suffix)
                {
                    using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith(bundle + suffix, StringComparison.Ordinal)))!;
                    using var reader = new StreamReader(stream); return reader.ReadToEnd();
                }
                var native = SkillProgramCatalog.Load(Read(".rules.json"), Read(".presentation.json"));
                foreach (var id in native.Programs.Keys) builder.AddSkill(new ContentSkillDefinition(id, native.Presentations[id].Name, native.Presentations[id].Description) { Program = native.Programs[id], ProgramPresentation = native.Presentations[id] });
            }
            var rules = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:wu-discard","revision":1,"activations":[{"id":"discard","minCards":1,"maxCards":null,"sourceZones":["hand","equipment"],"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"cost"},{"op":"moveBoundCards","target":"owner","sourceBind":"cost","destination":"discardPile","awaitMovementTriggers":true}]}]},{"id":"fixture:wu-wound","revision":1,"activations":[{"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]}]}""";
            var presentation = """{"schemaVersion":3,"skills":{"fixture:wu-discard":{"name":"弃牌","description":"弃置三张牌"},"fixture:wu-wound":{"name":"失血","description":"失去一点体力"}}}""";
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            foreach (var id in catalog.Programs.Keys) builder.AddSkill(new ContentSkillDefinition(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            if (extraPlay)
            {
                var extra = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:wu-extra-play","revision":1,"triggers":[{"id":"extra","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}]}""",
                    """{"schemaVersion":3,"skills":{"fixture:wu-extra-play":{"name":"额外出牌","description":"回合开始时执行额外出牌阶段"}}}""");
                builder.AddSkill(new ContentSkillDefinition("fixture:wu-extra-play", "额外出牌", "回合开始时执行额外出牌阶段") { Program = extra.Programs["fixture:wu-extra-play"], ProgramPresentation = extra.Presentations["fixture:wu-extra-play"] });
            }
            builder.AddGeneral(new ContentGeneralDefinition("fixture:wu-owner", "吴将", "supporter", skill, "wu", BaseHp: 5,
                AdditionalSkillIds: extraPlay ? ["fixture:wu-discard", "fixture:wu-wound", "fixture:wu-extra-play"] : ["fixture:wu-discard", "fixture:wu-wound"]));
            for (var index = 1; index < 4; index++) builder.AddGeneral(new ContentGeneralDefinition($"fixture:wu-{index}", $"目标{index}", "supporter", "standard:none", "wei", BaseHp: 5));
            builder.AddDeck(new ContentDeckRecipe("fixture:wu-deck", "吴将测试", 22, 0, [new ContentDeckCardCount(equipment ? "standard:crossbow" : "standard:slash", 160)]));
            builder.AddMode(new ContentModeDefinition("fixture:wu-mode", "吴将测试", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:wu-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:wu-owner", "fixture:wu-1", "fixture:wu-2", "fixture:wu-3"]));
        }
    }
}
