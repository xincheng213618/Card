using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhuLingEquipmentEntryChecks
{
    private const string Driver = "fixture:zhanyi-entry-driver", Mode = "identity:zhanyi-entry-fixture";
    private const string Skill = "ol:zhanyi";

    public static void PlacedEquipmentTriggersOnceAndColdReplays()
    {
        var (game, registry) = Create();
        var card = EquipmentInHand(game);
        var declaredUses = Facts<CardUseDeclaredEvent>(game).Count();
        Accept(game, new UseProgramSkillCommand(0, Driver, "place", [card.Id], [0], game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => IsEquipmentOffer(p) || IsPlay(p));
        Require(game.CreateSnapshot(0).Players[0].Equipment.Any(c => c.Id == card.Id),
            "The real placement fixture must enter the owner's equipment zone before checking the trigger.");
        Require(Facts<CardUseDeclaredEvent>(game).Count() == declaredUses,
            "PlaceSelectedEquipment must be an actual zone placement without a fabricated card Use.");
        Require(IsEquipmentOffer(Prompt(game)!),
            "Zhanyi must offer its equipment benefit when a card is placed into the owner's equipment zone without being used.");
        game = Cold(game, registry);
        game = ResolvePunish(game, registry);
        Require(game.CardMovements.Count(m => m.CardId == card.Id && m.To == CardLocation.Equipment(0)) == 1 &&
            Facts<ProgramZhanyiEquipmentPunishEvent>(game).Count() == 1,
            "The placed entity and its equipment-entry punishment each resolve once after cold recovery.");
        Cold(game, registry);

        // The same placement mechanism directed at another seat must not trigger the owner's benefit.
        var before = Facts<ProgramZhanyiEquipmentPunishEvent>(game).Count();
        var foreignCard = EquipmentInHand(game);
        Accept(game, new UseProgramSkillCommand(0, Driver, "place", [foreignCard.Id], [1], game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => IsEquipmentOffer(p) || IsPlay(p));
        Require(IsPlay(Prompt(game)!) && Facts<ProgramZhanyiEquipmentPunishEvent>(game).Count() == before &&
            game.CreateSnapshot(0).Players[1].Equipment.Any(c => c.Id == foreignCard.Id),
            "Giving equipment into another player's equipment zone must not offer the source owner's Zhanyi.");
        Cold(game, registry);
    }

    public static void UsedEquipmentStillTriggersExactlyOnce()
    {
        var (game, registry) = Create();
        var card = EquipmentInHand(game);
        Accept(game, new PlayCardCommand(0, card.Id, [], game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => IsEquipmentOffer(p) || IsPlay(p));
        Require(IsEquipmentOffer(Prompt(game)!) && game.CreateSnapshot(0).Players[0].Equipment.Any(c => c.Id == card.Id),
            "Ordinary equipment Use must still offer Zhanyi after the actual equipment entry.");
        game = Cold(game, registry);
        game = ResolvePunish(game, registry);
        Require(Facts<ProgramZhanyiEquipmentPunishEvent>(game).Count() == 1 &&
            Facts<CardUseDeclaredEvent>(game).Count(e => e.SourceSeat == 0 && e.CardId == card.Id) == 1,
            "Moving the equipment benefit to the entry boundary must retain one real Use and one punishment: " +
            JsonSerializer.Serialize(new { CardId = card.Id, Punishments = Facts<ProgramZhanyiEquipmentPunishEvent>(game).ToArray(),
                Uses = Facts<CardUseDeclaredEvent>(game).ToArray() }));
        Cold(game, registry);
    }

    public static void EquipmentEntryContractsPreserveHandGainBoundaries()
    {
        const string rules = """
        {"skills":[{"id":"fixture:equipment-entry-contract","revision":1,"triggers":[
          {"id":"entry","window":"cardsGained","subject":"owner","destinationZones":["equipment"],"movementOccurrence":"perCard","optional":false,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
        """;
        var node = JsonNode.Parse(rules)!;
        node["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
        var presentations = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = new Dictionary<string, object> { ["fixture:equipment-entry-contract"] = new { name = "entry", description = "shared destination contract" } } });
        SkillProgramCatalog.Load(node.ToJsonString(), presentations);
        void Reject(Action<JsonNode> mutate, string reason)
        {
            var copy = node.DeepClone(); mutate(copy["skills"]![0]!["triggers"]![0]!);
            try { SkillProgramCatalog.Load(copy.ToJsonString(), presentations); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException(reason);
        }
        foreach (var occurrence in new[] { "perSourceOwner", "perThirdPartyHandGain" })
            Reject(trigger => trigger["movementOccurrence"] = occurrence, "Equipment entry must not be accepted as a hand-gain occurrence.");
        Reject(trigger => trigger["gainPhaseQualification"] = "outsideOwnerDraw", "Equipment entry must not accept a hand-gain phase qualifier.");
        foreach (var operation in new[] { "giveAfterBatchGain", "exchangeFriendGainForDiscard" })
            Reject(trigger => { trigger["movementOccurrence"] = "perBatch"; trigger["effects"] = JsonNode.Parse($$"""[{"op":"{{operation}}","target":"owner"}]"""); },
                "A hand-gain-only operation must not be admitted through the equipment destination extension.");
        foreach (var zone in new[] { "judgment", "hand" })
            Reject(trigger => { trigger["optional"] = true; trigger["destinationZones"] = new JsonArray(zone);
                trigger["effects"] = JsonNode.Parse("""[{"op":"zhuLingZhanyiEquipmentPunish","target":"owner"}]"""); },
                "Zhanyi equipment punishment must require an actual equipment-zone destination.");
    }

    private static GameEngine ResolvePunish(GameEngine game, ContentRegistry registry)
    {
        Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "zhanyi-punish-target"));
        game = Cold(game, registry);
        Answer(game, c => c.Targets.SequenceEqual([1]));
        Reach(game, p => p.Choices.Any(c => c.Parameters.ContainsKey("hand-slot")));
        game = Cold(game, registry);
        Answer(game, c => c.Parameters.ContainsKey("hand-slot"));
        Reach(game, p => IsEquipmentOffer(p) || IsPlay(p));
        Require(IsPlay(Prompt(game)!), "A single equipment entry must not offer a duplicate punishment.");
        return game;
    }

    private static bool IsPlay(PendingDecision p) => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard;
    private static bool IsEquipmentOffer(PendingDecision p) => p.SkillPrompt?.SkillId == Skill &&
        p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static IEnumerable<T> Facts<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>();
    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static CardSnapshot EquipmentInHand(GameEngine game) => game.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.Crossbow);
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var p = Prompt(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, game.Revision)); }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 120; step++)
        {
            if (Prompt(game) is { } prompt && predicate(prompt)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Zhanyi equipment boundary missing: " + JsonSerializer.Serialize(Prompt(game)));
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Require(result.Accepted, result.Error?.Message ?? "Zhanyi fixture command rejected."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(copy.ResolutionStack) &&
            Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(copy.CreateSnapshot(seat))) &&
            JsonSerializer.Serialize(game.CardMovements) == JsonSerializer.Serialize(copy.CardMovements),
            "Cold replay must preserve the actual equipment movement, paused punishment and all four private player views.");
        return copy;
    }
    private static (GameEngine, ContentRegistry) Create()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:zhanyi-entry-owner", game.Revision, Prompt(game)!.PromptId));
        Reach(game, IsEquipmentOffer);
        Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "zhanyi-launch"));
        Require(Prompt(game)!.Choices.Any(c => c.Parameters.GetValueOrDefault("category") == "Trick") &&
            game.CreateSnapshot(0).Players[0].Hand.Count(c => c.Kind == CardKind.Crossbow) >= 2,
            "The fixed seed must supply one trick-category payment and two equipment entities: " + JsonSerializer.Serialize(game.CreateSnapshot(0).Players[0].Hand));
        Answer(game, c => c.Parameters.GetValueOrDefault("category") == "Trick");
        Reach(game, IsPlay);
        return (game, registry);
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-zhanyi-entry", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            string Read(string suffix)
            { using var reader = new StreamReader(assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("ol-zhu-ling." + suffix + ".json", StringComparison.Ordinal)))!); return reader.ReadToEnd(); }
            var zhanyi = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            builder.AddSkill(new(Skill, zhanyi.Presentations[Skill].Name, zhanyi.Presentations[Skill].Description)
            { Program = zhanyi.Programs[Skill], ProgramPresentation = zhanyi.Presentations[Skill] });
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"activations":[
              {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]}]}]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "真实装备置入", description = "直接使用已有装备置入能力验证正式战意触发。" } } }));
            builder.AddSkill(new(Driver, Driver, "Actual equipment placement") { Program = catalog.Programs[Driver] });
            builder.AddSkill(new("fixture:zhanyi-entry-pick-owner", "owner", "fixture")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? 100000d : -100000d) });
            builder.AddSkill(new("fixture:zhanyi-entry-pick-other", "other", "fixture")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? -100000d : 100000d) });
            builder.AddGeneral(new("fixture:zhanyi-entry-owner", "朱灵置入回归", "supporter", "fixture:zhanyi-entry-pick-owner", "wei", 12, [Skill, Driver]));
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new($"fixture:zhanyi-entry-other-{seat}", "固定对手", "supporter", "fixture:zhanyi-entry-pick-other", "wei", 12));
            builder.AddDeck(new("fixture:zhanyi-entry-deck", "fixed seed equipment entry", 8, 2, [])
            { PhysicalCards = Enumerable.Range(0, 100).Select(i => new ContentDeckPhysicalCard(i % 2 == 0 ? "standard:crossbow" : "standard:draw_two", Suit.Heart, 7)).ToArray() });
            builder.AddMode(new(Mode, "战意装备置入", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:zhanyi-entry-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:zhanyi-entry-owner", "fixture:zhanyi-entry-other-1", "fixture:zhanyi-entry-other-2", "fixture:zhanyi-entry-other-3"]));
        }
    }
}
