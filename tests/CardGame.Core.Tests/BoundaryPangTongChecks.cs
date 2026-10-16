using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryPangTongChecks
{
    public static void LianhuanAddsThirdTargetAndRecastsOwnedEquipment()
    {
        var (raw, rawRegistry) = Start("chain");
        var action = raw.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.IronChain &&
            a.TargetSeats.SequenceEqual(new[] { 0, 1, 2 }));
        Require(raw.GetHumanLegalActions().Where(a => a.Kind == LegalActionKind.IronChain)
            .All(a => a.TargetSeats.Count <= 3), "Lianhuan adds exactly one target to a raw Iron Chain.");
        var before = SnapshotJson.Serialize(raw.CreateSnapshot(0));
        var invalid = raw.Submit(new PlayCardCommand(0, action.CardId!.Value, [0, 1, 2, 3],
            raw.Revision, P(raw)!.PromptId));
        Require(!invalid.Accepted && before == SnapshotJson.Serialize(raw.CreateSnapshot(0)),
            "A fourth target is rejected before payment.");
        Play(raw, action);
        Replay(raw, rawRegistry);
        Reach(raw, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Require(raw.CreateSnapshot(0).Players.Take(3).All(p => p.IsChained) &&
            !raw.CreateSnapshot(0).Players[3].IsChained,
            "One real raw Iron Chain switches exactly the three declared targets.");

        var (converted, convertedRegistry) = Start("equipment");
        var equip = converted.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        Play(converted, equip);
        Reach(converted, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        var equippedId = converted.CreateSnapshot(0).Players[0].Equipment.Single().Id;
        var equipmentUse = converted.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.IronChain &&
            a.CardId == equippedId && a.ConversionSource?.SkillId == "boundary:lianhuan" &&
            a.TargetSeats.SequenceEqual(new[] { 0, 1, 2 }));
        Play(converted, equipmentUse);
        Replay(converted, convertedRegistry);
        Reach(converted, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Require(converted.CreateSnapshot(0).Players.Take(3).All(p => p.IsChained) &&
            converted.CardMovements.Count(m => m.CardId == equippedId &&
                m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing) == 1,
            "A club equipment conversion pays its real equipment source once and uses three targets.");

        var (recast, recastRegistry) = Start("equipment");
        Play(recast, recast.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip));
        Reach(recast, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        var armor = recast.CreateSnapshot(0).Players[0].Equipment.Single();
        var recastAction = recast.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.Recast &&
            a.CardId == armor.Id && a.ConversionSource?.SkillId == "boundary:lianhuan");
        var handCount = recast.CreateSnapshot(0).Players[0].HandCount;
        var useCount = recast.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Count();
        Accept(recast, new RecastCardCommand(0, armor.Id, recast.Revision, P(recast)!.PromptId)
        { ConversionSource = recastAction.ConversionSource });
        Require(recast.CreateSnapshot(0).Players[0].Equipment.Count == 0 &&
            recast.CreateSnapshot(0).Players[0].HandCount == handCount + 1 &&
            recast.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Count() == useCount &&
            recast.CardMovements.Count(m => m.CardId == armor.Id &&
                m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile) == 1,
            "Equipment recast draws once, pays once and does not declare a trick use.");
        Replay(recast, recastRegistry);
        Conserve(raw); Conserve(converted); Conserve(recast);
    }

    private static (GameEngine, ContentRegistry) Start(string mode)
    {
        var definitions = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage());
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(definitions, mode));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = mode is "recast-policy" or "recast-program" ? Role.Loyalist : Role.Lord,
            ModeId = "identity:classic-boundary-pang-tong-check", UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:pang-tong", game.Revision, P(game)!.PromptId));
        Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        return (game, registry);
    }

    private static PendingDecision? P(GameEngine game) => game.PendingDecision ??
        Enumerable.Range(0, 4).Select(v => game.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Require(result.Accepted, result.Error?.Message ?? "Command rejected."); }
    private static void Answer(GameEngine game, ChoiceId choice) =>
        Accept(game, new AnswerPromptCommand(P(game)!.PlayerSeat, P(game)!.PromptId, choice, game.Revision));
    private static void Play(GameEngine game, LegalAction action) => Accept(game,
        new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, P(game)!.PromptId,
            action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 80; i++)
        {
            if (P(game) is { } prompt && predicate(prompt)) return;
            if (P(game) is { PlayerSeat: 0, Kind: DecisionKind.Nullification } nullification)
                Answer(game, nullification.Choices.First(c => c.Cards.Count == 0).Id);
            else if (P(game) is { SkillPrompt.SkillId: "fixture:pt-recovery-child" or "fixture:pt-recast-gain" } child)
                Answer(game, child.Choices.Single().Id);
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Pang Tong fixture did not reach its requested prompt: " + P(game)?.Prompt);
    }
    private static void Replay(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(-1, 5).All(v => SnapshotJson.Serialize(game.CreateSnapshot(v)) ==
                SnapshotJson.Serialize(restored.CreateSnapshot(v))) &&
            game.CardMovements.SequenceEqual(restored.CardMovements) &&
            JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(restored.ResolutionStack),
            "Cold replay preserves each player view, spectator privacy, exact movement history and typed owning frames.");
        Require(Enumerable.Range(0, 4).All(v => game.CreateSnapshot(v).Players.Where(p => p.Seat != v)
            .All(p => p.Hand.Count == 0)), "Other players' hand identities remain private.");
    }
    private static void Conserve(GameEngine game) => Require(game.CreateCardZoneDiagnostics()
        .Select(c => c.CardId).Distinct().Count() == 80, "All fixture physical entities are conserved.");
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(ContentRegistry definitions, string mode) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-pang-tong", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in new[] { "boundary:lianhuan", "boundary:niepan", "classic:bazhen", "classic:huoji", "classic:kanpo" })
                builder.AddSkill(definitions.GetSkill(id));
            var recoveryPolicy = mode is "recast-policy" or "recast-program";
            if (recoveryPolicy) builder.AddSkill(definitions.GetSkill("boundary:jiuyuan"));
            if (mode == "recast-program") builder.AddSkill(definitions.GetSkill("classic:huairou"));
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:pt-dying","revision":1,
                "activations":[{"id":"enter","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
                "targetKind":"anyLiving","usesPerTurn":2,"effects":[
                {"op":"setChainedState","target":"owner","chained":true},
                {"op":"setFaceState","target":"owner","faceDown":true},
                {"op":"loseHp","target":"owner","amount":8}]},
                {"id":"lower-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,
                "targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]}]},
                {"id":"fixture:pt-recovery-child","revision":1,"triggers":[{"id":"real-recovery-child",
                "window":"afterHpRecovered","subject":"owner","optional":false,"effects":[
                {"op":"chooseOption","target":"owner","resultBind":"recovery-observed","options":[{"id":"continue"}]}]}]},
                {"id":"fixture:pt-recast-gain","revision":1,"triggers":[{"id":"draw-child","window":"cardsGained",
                "subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","effects":[
                {"op":"chooseOption","target":"owner","resultBind":"gain-observed","options":[{"id":"continue"}]}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:pt-dying":{"name":"进入濒死","description":"设置真实角色状态并失去体力。"},"fixture:pt-recovery-child":{"name":"真实回复子窗口","description":"暂停真实回复观察。","optionLabels":{"continue":"继续结算"}},"fixture:pt-recast-gain":{"name":"真实重铸摸牌子窗口","description":"暂停真实摸牌观察。","optionLabels":{"continue":"继续结算"}}}}""");
            builder.AddSkill(new("fixture:pt-dying", "进入濒死", "真实濒死边界")
            { Program = catalog.Programs["fixture:pt-dying"] });
            builder.AddSkill(new("fixture:pt-recovery-child", "真实回复子窗口", "暂停真实回复观察")
            { Program = catalog.Programs["fixture:pt-recovery-child"] });
            builder.AddSkill(new("fixture:pt-recast-gain", "真实摸牌子窗口", "暂停真实摸牌观察")
            { Program = catalog.Programs["fixture:pt-recast-gain"] });
            builder.AddSkill(new("fixture:pt-selection", "固定其他选将", "保留人工机制角色")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            var recast = mode.StartsWith("recast-", StringComparison.Ordinal);
            builder.AddGeneral(new("fixture:pang-tong", "界庞统机制", "pang_tong", "boundary:lianhuan", recoveryPolicy ? "wu" : "shu", recast ? 4 : 3,
                mode == "recast-program" ? ["fixture:pt-dying", "fixture:pt-recovery-child", "fixture:pt-recast-gain", "classic:huairou"] :
                recast ? ["fixture:pt-dying", "fixture:pt-recovery-child", "fixture:pt-recast-gain"] : mode == "dying" ? ["boundary:niepan", "fixture:pt-dying", "fixture:pt-recovery-child"]
                    : ["boundary:niepan", "fixture:pt-dying"]) { InitialHp = recast ? recoveryPolicy ? 3 : 1 : null });
            for (var i = 1; i < 4; i++)
                builder.AddGeneral(new($"fixture:pt-other-{i}", "其他" + i, "supporter", "fixture:pt-selection", "qun", 4,
                    recoveryPolicy ? ["boundary:jiuyuan", "fixture:pt-recovery-child"] : [])
                { InitialHp = recoveryPolicy ? 1 : null });
            builder.AddCard(new("fixture:pt-chain", "铁索连环", "锦囊牌", "固定铁索", CardKind.IronChain));
            builder.AddCard(new("fixture:pt-equipment", "诸葛连弩", "装备牌", "固定梅花装备", CardKind.Crossbow));
            builder.AddCard(new("fixture:pt-lion", "白银狮子", "装备牌", "真实移牌恢复", CardKind.SilverLion));
            builder.AddCard(new("fixture:pt-lightning", "闪电", "锦囊牌", "真实判定区牌", CardKind.Lightning));
            builder.AddDeck(new("fixture:pt-deck", "固定实体", mode == "dying" ? 8 : 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(
                    recast ? "fixture:pt-lion" : mode == "chain" ? "fixture:pt-chain" : mode == "dying"
                        ? i % 2 == 0 ? "fixture:pt-lion" : "fixture:pt-lightning" : "fixture:pt-equipment",
                    Suit.Club, i % 13 + 1)).ToArray()
            });
            builder.AddMode(new("identity:classic-boundary-pang-tong-check", "界庞统机制", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:pt-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:pang-tong", "fixture:pt-other-1", "fixture:pt-other-2", "fixture:pt-other-3"]));
        }
    }
}
