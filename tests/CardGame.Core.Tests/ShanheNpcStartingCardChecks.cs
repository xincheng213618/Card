using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheNpcStartingCardChecks
{
    private const string Owner = "fixture:npc-supplement-owner", Enemy = "fixture:npc-supplement-enemy";
    private const string Driver = "fixture:npc-supplement-driver", Quiet = "fixture:npc-supplement-quiet", Deck = "fixture:npc-supplement-deck";
    private static readonly ShanheStartingCard Sword = new(CardKind.ScarletBloodSword, Suit.Heart, 5);

    public static void RandomFourPlusFixedEquipmentOrdinaryLifecycleAndRecipeCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var supplied = new List<ShanheStartingCard> { Sword };
        var definition = Definition(new(Enemy) { StartingCards = supplied });
        var frozen = ShanheCampaignContent.FreezeAndValidate(definition);
        supplied.Clear();
        Need(frozen.Nodes[0].Encounters![0].Enemies[0].StartingCards is [var extra] && extra == Sword,
            "Caller mutation cannot alter the copied NPC supplement or the campaign's frozen encounter.");

        // The optional member field must remain absent in old serialized
        // rosters, rather than becoming an empty list in every old fingerprint.
        var legacy = new ShanheCombatantDefinition(Enemy);
        var legacyJson = JsonSerializer.Serialize(legacy);
        var legacyNode = JsonNode.Parse(legacyJson)!.AsObject();
        Need(!legacyNode.ContainsKey(nameof(ShanheCombatantDefinition.StartingCards)) &&
            JsonSerializer.Deserialize<ShanheCombatantDefinition>(legacyJson)!.StartingCards is null &&
            ShanheCampaignContent.GetContentHash(Definition(legacy)) ==
                ShanheCampaignContent.GetContentHash(Definition(legacy with { StartingCards = null })) &&
            ShanheCampaignContent.GetContentHash(frozen) != ShanheCampaignContent.GetContentHash(Definition(legacy)),
            "Null supplements preserve the old absent field and complete hash; real extra cards change the complete definition hash.");

        var session = ShanheCampaignSession.Create(frozen, new(73, Owner), _ => registry);
        Need(session.Submit(new MoveShanheNodeCommand("battle", session.Revision)).Accepted,
            "A real campaign command prepares the supplemented NPC encounter.");
        var campaignJson = ShanheCampaignJson.Serialize(session.CreateCheckpoint());
        var restored = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(campaignJson), frozen, _ => registry);
        var setup = restored.PendingBattleSetup!;
        Need(ShanheCampaignJson.Serialize(restored.CreateCheckpoint()) == campaignJson &&
            ShanheBattleSetup.ValueEquals(session.PendingBattleSetup, setup) &&
            setup.EnemyRoster[0].StartingCards is [var saved] && saved == Sword &&
            setup.EnemyRoster[0].StartingHand is null,
            "The campaign checkpoint reconstructs the exact supplemental recipe while preserving the random-hand contract.");

        var g = Create(setup, registry);
        var npc = g.CreateSnapshot(1).Players[1];
        var swordId = npc.Hand.Single(card => card.Kind == Sword.Kind).Id;
        Need(npc.Hand.Count == 5 && npc.Equipment.Count == 0 &&
            g.CardMovements.Count(move => move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(1) &&
                move.Reason == CardMoveReasons.InitialDeal) == 4 &&
            g.CardMovements.Count(move => move.CardId == swordId && move.From == CardLocation.OutsideGame &&
                move.To == CardLocation.Hand(1) && move.Reason == CardMoveReasons.InitialDeal) == 1,
            "The actual constructor deals four random physical cards, then adds the fixed Scarlet Blood Sword to NPC hand without equipping it.");
        g = Cold(g, registry);
        Accept(g, new StartGameCommand());
        Reach(g, () => Prompt(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, Prompt(g)!.PromptId));
        Reach(g, () => g.Events.Any(item => item.Payload is CardUseFinishedEvent { CardId: var id } && id == swordId));
        // Accepted-action facts belong to optional finalized target windows.
        // This minimal catalog proves the complete native use through its
        // mandatory declaration, movement, equipment and completion receipts.
        var declarations = g.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>()
            .Where(item => item.CardId == swordId).ToArray();
        Need(declarations is [var use] && use.SourceSeat == 1 && use.CardKind == Sword.Kind &&
            Zone(g, swordId) == CardLocation.Equipment(1) &&
            g.CardMovements.Count(move => move.CardId == swordId && move.From == CardLocation.Hand(1) &&
                move.To == CardLocation.Processing && move.Reason == CardMoveReasons.EquipmentUse) == 1 &&
            g.CardMovements.Count(move => move.CardId == swordId && move.From == CardLocation.Processing &&
                move.To == CardLocation.Equipment(1) && move.Reason == CardMoveReasons.EquipmentEnter) == 1 &&
            g.Events.Select(item => item.Payload).OfType<EquipmentChangedEvent>().Count(item =>
                item.ResolutionId == use.ResolutionId && item.PlayerSeat == 1 && item.CardId == swordId && item.CardKind == Sword.Kind) == 1 &&
            g.Events.Select(item => item.Payload).OfType<CardUseFinishedEvent>().Count(item =>
                item.ResolutionId == use.ResolutionId && item.CardId == swordId && item.CardKind == Sword.Kind) == 1 &&
            !g.ResolutionStack.Any(frame => frame.Id == use.ResolutionId) &&
            !g.Events.Any(item => item.Payload is ShanheStartingResourceCreatedEvent),
            "The real NPC AI installs the extra card through native equipment Use, without assigning player expedition-resource provenance. " +
            NpcUseEvidence(g, swordId));
        g = Cold(g, registry);
        Reach(g, () => Prompt(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Accept(g, new UseProgramSkillCommand(0, Driver, "discard-npc-gear", [], [1], g.Revision, Prompt(g)!.PromptId));
        Reach(g, () => Prompt(g)?.Choices.Any(choice => choice.Cards.SequenceEqual([swordId])) == true);
        Answer(g, choice => choice.Cards.SequenceEqual([swordId]));
        Reach(g, () => Prompt(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Need(Zone(g, swordId) == CardLocation.DiscardPile &&
            !g.Events.Any(item => item.Payload is ShanheStartingResourceDestroyedEvent { CardId: var id } && id == swordId),
            "Discarding the genuinely installed NPC weapon uses the ordinary discard pile even when player starting resources expire on use.");
        _ = Cold(g, registry);

        var explicitHand = Create(setup with { EnemyRoster =
            [setup.EnemyRoster[0] with { StartingHand = [new(CardKind.Dodge, Suit.Diamond, 7)] }] }, registry);
        Need(explicitHand.CreateSnapshot(1).Players[1].Hand.Count == 2 &&
            explicitHand.CreateSnapshot(1).Players[1].Hand.Any(card => card.Kind == Sword.Kind),
            "An explicit StartingHand remains its own replacement contract; the same supplement is added after it rather than replacing it.");
        var invalid = setup.EnemyRoster[0] with { StartingCards = [new(CardKind.Slash, Suit.None, 1)] };
        Reject(() => ShanheCampaignContent.FreezeAndValidate(Definition(invalid)));
        Reject(() => Create(setup with { EnemyRoster = [invalid] }, registry));
    }

    private static ShanheCampaignDefinition Definition(ShanheCombatantDefinition enemy) => new(
        "fixture:npc-supplement-campaign", "NPC实际补充起手", [Owner], ["battle"],
        [new("battle", "真实遭遇", ShanheNodeKind.Battle, [], Encounters:
            [new("battle:fixed-gear", "随机四张加额外装备", [enemy])])], [])
    { StartingResourcesExpireOnUse = true };

    private static GameEngine Create(ShanheBattleSetup setup, ContentRegistry registry) => GameEngine.CreateStandard(new()
    {
        Seed = setup.BattleSeed, PlayerCount = 2, HumanSeat = 0, HumanRole = null,
        HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
        UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
        MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
    }, registry);
    private static CardLocation Zone(GameEngine g, int id) => g.CreateCardZoneDiagnostics().Single(card => card.CardId == id).Location;
    private static string NpcUseEvidence(GameEngine g, int id) => JsonSerializer.Serialize(new
    {
        CardId = id,
        Zone = Zone(g, id),
        Equipment = g.CreateSnapshot(1).Players[1].Equipment,
        Moves = g.CardMovements.Where(move => move.CardId == id).TakeLast(8),
        CardUsed = g.Events.Select(item => item.Payload).OfType<CardUsedEvent>()
            .Where(item => item.CardId == id).TakeLast(4),
        Declared = g.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>()
            .Where(item => item.CardId == id).TakeLast(4),
        EquipmentChanged = g.Events.Select(item => item.Payload).OfType<EquipmentChangedEvent>()
            .Where(item => item.CardId == id).TakeLast(4),
        Accepted = g.Events.Select(item => item.Payload).OfType<CardActionAcceptedEvent>()
            .Where(item => item.Action.ActorSeat == 1).TakeLast(4),
        Finished = g.Events.Select(item => item.Payload).OfType<CardUseFinishedEvent>()
            .Where(item => item.CardId == id).TakeLast(4),
        StartingOrigin = g.Events.Select(item => item.Payload).OfType<ShanheStartingResourceCreatedEvent>().Take(8),
        Frames = g.ResolutionStack.TakeLast(4).Select(frame => new { Kind = frame.GetType().Name, frame.Id }),
        Prompt = Prompt(g)
    });
    private static PendingDecision? Prompt(GameEngine g) => Enumerable.Range(0, g.PlayerCount)
        .Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("An invalid NPC supplemental physical card was accepted.");
    }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Need(result.Accepted, result.Error?.Message ?? "Rejected NPC supplemental-card command.");
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        var prompt = Prompt(g)!;
        Accept(g, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(predicate).Id, g.Revision));
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 160; step++)
        {
            if (done()) return;
            Need(g.State.Winner == Winner.None, "Small NPC supplement fixture ended before the expected native boundary.");
            if (Prompt(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } play)
                Accept(g, new EndPlayPhaseCommand(0, g.Revision, play.PromptId));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("NPC supplement fixture missed its native boundary: " + JsonSerializer.Serialize(Prompt(g)));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, g.PlayerCount).Select(seat => SnapshotJson.Serialize(g.CreateSnapshot(seat))),
        Frames = g.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
        Events = g.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())),
        g.CardMovements, Checkpoint = GameCheckpointJson.Serialize(g.CreateCheckpoint())
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "Cold JSON replay preserves the additive NPC recipe, physical card, native frame and ordinary discard lifecycle.");
        return restored;
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("npc-supplement-native-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var equipment = EquipmentCatalog.Get(Sword.Kind);
            builder.AddCard(new("fixture:scarlet-blood-sword", equipment.DisplayName, "装备牌", equipment.Description, Sword.Kind));
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"activations":[
                    {"id":"discard-npc-gear","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
                     "effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},
                                 "zones":["equipment"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]}]}
                ]}
                """, JsonSerializer.Serialize(new
                {
                    schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [Driver] = new { name = "真实弃置装备", description = "通过实际移动弃置NPC装备" } }
                }));
            builder.AddSkill(new(Driver, "真实弃置装备", "通过实际移动弃置NPC装备")
            { Program = catalog.Programs[Driver], ProgramPresentation = catalog.Presentations[Driver] });
            builder.AddSkill(new(Quiet, "普通NPC", "无额外能力"));
            builder.AddGeneral(new(Owner, "原生玩家", "supporter", Driver, "shu", 8));
            builder.AddGeneral(new(Enemy, "原生NPC", "supporter", Quiet, "wei", 4));
            builder.AddDeck(new(Deck, "四张真实随机牌", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray()
            });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "NPC补充手牌真实战斗", 2, 2,
                new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 1 }));
        }
    }
}
