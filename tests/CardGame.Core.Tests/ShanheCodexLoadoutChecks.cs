using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheCodexLoadoutChecks
{
    private const string Player = "fixture:codex-player";
    private const string Enemy = "fixture:codex-enemy";
    private const string Force = "fixture:codex-force";
    private const string OpenSkill = "fixture:codex-open";
    private const string LockedSkill = "fixture:codex-locked";
    private static readonly Lazy<ContentRegistry> Content = new(() => ContentRegistry.Build(new StandardContentPackage(), new FixturePackage()));

    public static void InitialTacticsValidateFreezeGrantAndReplayWithoutChangingContent()
    {
        var definition = Definition();
        var unlocked = new List<string> { "t0", "t1", "skill-open", "gear-open" };
        var equipped = new List<string> { "t0" };
        var options = new ShanheCampaignOptions(71, Player)
        {
            UnlockedRewardIds = unlocked, EquippedTacticIds = equipped, ExpeditionId = "fixture-expedition"
        };
        unlocked.Clear(); equipped.Clear();
        Require(options.UnlockedRewardIds!.Count == 4 && options.EquippedTacticIds.SequenceEqual(["t0"]),
            "Options must detach caller-owned lists when initialized.");
        Frozen(options.UnlockedRewardIds); Frozen(options.EquippedTacticIds);
        var session = Create(definition, options);
        Require(session.Revision == 0 && session.Snapshot.TacticIds.SequenceEqual(["t0"]) &&
            session.Snapshot.ExtraSkillIds.Count == 0 && session.Snapshot.ExtraSkillSlots == 2 &&
            session.Snapshot.Bonuses == new ShanheTacticBonuses(1, 1, 1, 1) && session.Snapshot.Gold == 12 &&
            session.Snapshot.StartingCards.Single().Kind == CardKind.Slash,
            "Starting tactics must use the reward grant path without consuming command revisions or general-skill slots.");
        Require(session.ContentHash == Create(definition, new(71, Player)).ContentHash,
            "Codex progress, loadout and host expedition identity must not change the content fingerprint.");
        Accept(session, new MoveShanheNodeCommand("shop", session.Revision));
        Accept(session, new ChooseShanheNodeOptionCommand("$leave", null, session.Revision));
        Accept(session, new MoveShanheNodeCommand("event", session.Revision));
        Accept(session, new ChooseShanheNodeOptionCommand("t1", null, session.Revision));
        Accept(session, new MoveShanheNodeCommand("battle", session.Revision));
        Require(session.PendingBattleSetup!.TacticSkillIds.SequenceEqual([Tactic(0), Tactic(1)]) &&
            session.PendingBattleSetup is { MaxHpBonus: 1, InitialHandBonus: 1, DrawBonus: 1, SlashBonus: 1 },
            "Initial tactic programs and later rewards must reach the real battle recipe in grant order.");
        var checkpoint = session.CreateCheckpoint();
        Frozen(checkpoint.Options.UnlockedRewardIds!); Frozen(checkpoint.Options.EquippedTacticIds);
        Require(checkpoint.Options.ExpeditionId == "fixture-expedition", "Checkpoint options must retain the host settlement identity.");
        Cold(session, definition);

        var six = Enumerable.Range(0, ShanheCampaignOptions.MaximumEquippedTactics).Select(i => $"t{i}").ToArray();
        Require(Create(definition, new(71, Player) { EquippedTacticIds = six }).Snapshot.TacticIds.Count == 6,
            "The six-tactic boundary must remain usable in legacy unrestricted mode.");
        foreach (var invalid in new IReadOnlyList<string>[]
        {
            Enumerable.Range(0, 7).Select(i => $"t{i}").ToArray(), ["t0", "t0"], ["t0", "alias"],
            ["missing"], ["gold"], ["skill-open"], ["gear-open"]
        }) MustThrow(() => Create(definition, new(71, Player) { EquippedTacticIds = invalid }));
        MustThrow(() => Create(definition, new(71, Player) { UnlockedRewardIds = [], EquippedTacticIds = ["t0"] }));
    }

    public static void LockedSkillsAndEquipmentStayOutOfAllRewardPathsAndForgedCommands()
    {
        var definition = Definition();
        var options = new ShanheCampaignOptions(71, Player) { UnlockedRewardIds = ["skill-open", "gear-open"] };
        var shop = Create(definition, options);
        Accept(shop, new MoveShanheNodeCommand("shop", 0));
        Require(shop.Snapshot.ShopItems.Select(item => item.ChoiceId).SequenceEqual(["skill-open", "gear-open", "t2"]) &&
            shop.Snapshot.NodeChoices.All(choice => choice.Id is not ("skill-locked" or "gear-locked")),
            "Codex gating must filter existing shop stock while retaining unlocked skills, equipment and ordinary tactic offers.");
        RejectUnchanged(shop, new ChooseShanheNodeOptionCommand("skill-locked", null, shop.Revision));
        RejectUnchanged(shop, new ChooseShanheNodeOptionCommand("gear-locked", null, shop.Revision));
        Accept(shop, new ChooseShanheNodeOptionCommand("skill-open", null, shop.Revision));
        Accept(shop, new ChooseShanheNodeOptionCommand("gear-open", null, shop.Revision));
        Require(shop.Snapshot.ExtraSkillIds.SequenceEqual([OpenSkill]) && shop.Snapshot.StartingEquipment.Single().Kind == CardKind.Crossbow &&
            shop.Snapshot.Gold == 6, "Unlocked stock must remain purchasable at its original price.");
        Cold(shop, definition);

        var session = Create(definition, options);
        Accept(session, new MoveShanheNodeCommand("shop", 0));
        Accept(session, new ChooseShanheNodeOptionCommand("$leave", null, session.Revision));
        Accept(session, new MoveShanheNodeCommand("event", session.Revision));
        Require(session.Snapshot.NodeChoices.Select(choice => choice.Id).SequenceEqual(["t1"]),
            "Events must filter locked skill and equipment grants, but a tactic need not be unlocked to be acquired during a run.");
        RejectUnchanged(session, new ChooseShanheNodeOptionCommand("skill-locked", null, session.Revision));
        RejectUnchanged(session, new ChooseShanheNodeOptionCommand("gear-locked", null, session.Revision));
        Accept(session, new ChooseShanheNodeOptionCommand("t1", null, session.Revision));
        Accept(session, new MoveShanheNodeCommand("battle", session.Revision));
        var battle = GameEngine.CreateStandard(new()
        {
            Seed = session.PendingBattleSetup!.BattleSeed, PlayerCount = 2, HumanSeat = 0,
            HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
            ShanheBattle = session.PendingBattleSetup, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false,
            AiPolicyVersion = AiDecisionPolicy.CurrentVersion
        }, Content.Value);
        Accept(battle, new StartGameCommand());
        for (var step = 0; step < 50 && battle.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
            Accept(battle, new AdvanceOneStepCommand(battle.Revision));
        Require(battle.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, "The fixture must reach its real play prompt.");
        Accept(battle, new UseProgramSkillCommand(0, Force, "finish", [], [1], battle.Revision, battle.PendingDecision!.PromptId));
        for (var step = 0; step < 50 && battle.State.Status != EngineStatus.Completed; step++)
            Accept(battle, new AdvanceOneStepCommand(battle.Revision));
        Require(session.CompleteBattle(battle, session.Revision).Accepted &&
            session.Snapshot.RewardOffers.Select(reward => reward.Id).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { "gear-open", "gold", "skill-open", "t2" }),
            "A real victory must filter locked skills and equipment from its existing pool without locking ordinary tactic rewards.");
        RejectUnchanged(session, new ChooseShanheRewardCommand("skill-locked", null, session.Revision));
        RejectUnchanged(session, new ChooseShanheRewardCommand("gear-locked", null, session.Revision));
        Cold(session, definition);
        Accept(session, new ChooseShanheRewardCommand("t2", null, session.Revision));
        Require(session.Snapshot.TacticIds.SequenceEqual(["t1", "t2"]), "A run may acquire tactics outside its initial codex loadout.");
        Cold(session, definition);
    }

    public static void OldOptionsRemainUnrestrictedThroughColdCheckpointReplay()
    {
        var definition = Definition();
        var legacy = Create(definition, new(71, Player));
        Accept(legacy, new MoveShanheNodeCommand("shop", 0));
        Accept(legacy, new ChooseShanheNodeOptionCommand("skill-locked", null, legacy.Revision));
        var json = JsonNode.Parse(ShanheCampaignJson.Serialize(legacy.CreateCheckpoint()))!;
        var options = json["Options"]!.AsObject();
        options.Remove(nameof(ShanheCampaignOptions.UnlockedRewardIds));
        options.Remove(nameof(ShanheCampaignOptions.EquippedTacticIds));
        options.Remove(nameof(ShanheCampaignOptions.ExpeditionId));
        var checkpoint = ShanheCampaignJson.Deserialize(json.ToJsonString());
        Require(checkpoint.Options.UnlockedRewardIds is null && checkpoint.Options.EquippedTacticIds.Count == 0 && checkpoint.Options.ExpeditionId is null,
            "Missing codex fields in old JSON must preserve unrestricted rewards with an empty initial loadout.");
        var restored = ShanheCampaignSession.Restore(checkpoint, definition, _ => Content.Value);
        Require(Snapshot(restored) == Snapshot(legacy) && restored.Snapshot.ShopItems.Count == 5 &&
            restored.Snapshot.ExtraSkillIds.SequenceEqual([LockedSkill]),
            "An old journal must replay its formerly unrestricted purchases without changing its fingerprint or results.");
        Cold(restored, definition);
    }

    private static string Tactic(int index) => $"fixture:codex-tactic-{index}";
    private static ShanheCampaignDefinition Definition() => new("fixture:codex", "册中征程", [Player], ["shop"],
    [
        new("shop", "行商", ShanheNodeKind.Shop, ["event"], Choices:
            new[] { "skill-open", "skill-locked", "gear-open", "gear-locked", "t2" }
                .Select(id => new ShanheNodeChoiceDefinition(id, id, GoldCost: 2, RewardId: id, LeavesNode: false)).ToArray()),
        new("event", "奇遇", ShanheNodeKind.Event, ["battle"], Choices:
            new[] { "skill-locked", "gear-locked", "t1" }.Select(id => new ShanheNodeChoiceDefinition(id, id, RewardId: id)).ToArray()),
        new("battle", "关隘", ShanheNodeKind.Battle, [], [new("gate", "守军", [new(Enemy)])],
            ["skill-open", "skill-locked", "gear-open", "gear-locked", "t2", "gold"], RewardChoiceCount: 12)
    ],
    [
        .. Enumerable.Range(0, 7).Select(i => new ShanheRewardDefinition($"t{i}", $"战法{i}", Gold: i == 0 ? 2 : 0,
            Bonuses: i == 0 ? new(1, 1, 1, 1) : null, StartingCards: i == 0 ? [new(CardKind.Slash)] : null) { TacticSkillId = Tactic(i) }),
        new("alias", "战法别名") { TacticSkillId = Tactic(0) },
        new("skill-open", "已解锁技能", SkillId: OpenSkill), new("skill-locked", "未解锁技能", SkillId: LockedSkill),
        new("gear-open", "已解锁装备", StartingEquipment: [new(CardKind.Crossbow)]),
        new("gear-locked", "未解锁装备", StartingEquipment: [new(CardKind.QinglongCrescentBlade)]), new("gold", "粮草", Gold: 1)
    ], StartingGold: 10);

    private static ShanheCampaignSession Create(ShanheCampaignDefinition definition, ShanheCampaignOptions options) =>
        ShanheCampaignSession.Create(definition, options, _ => Content.Value);
    private static string Snapshot(ShanheCampaignSession session) => JsonSerializer.Serialize(session.Snapshot);
    private static void Cold(ShanheCampaignSession session, ShanheCampaignDefinition definition)
    {
        var restored = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(session.CreateCheckpoint())), definition, _ => Content.Value);
        Require(Snapshot(session) == Snapshot(restored), "Cold campaign replay must retain the exact codex options and grants.");
    }
    private static void RejectUnchanged(ShanheCampaignSession session, ShanheCampaignCommand command)
    {
        var before = Snapshot(session);
        Require(!session.Submit(command).Accepted && Snapshot(session) == before, "A forged locked reward command must preserve the complete published state and revision.");
    }
    private static void Accept(ShanheCampaignSession session, ShanheCampaignCommand command)
    {
        var result = session.Submit(command); Require(result.Accepted, result.Error ?? "Campaign command rejected.");
    }
    private static void Accept(GameEngine battle, GameCommand command)
    {
        var result = battle.Submit(command); Require(result.Accepted, result.Error?.ToString() ?? "Battle command rejected.");
    }
    private static void Frozen(IReadOnlyList<string> values)
    {
        Require(values is IList<string> { IsReadOnly: true }, "Options must expose read-only detached lists.");
        if (values.Count == 0) return;
        try { ((IList<string>)values)[0] = "tampered"; }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("An options list accepted mutation.");
    }
    private static void MustThrow(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("An invalid initial tactic loadout was accepted.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class FixturePackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-codex", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var passiveIds = Enumerable.Range(0, 7).Select(Tactic).Concat([OpenSkill, LockedSkill]).ToArray();
            var programs = passiveIds.Select(id => (object)new { id, revision = 1,
                modifiers = new[] { new { id = "range", query = "attackRange", operation = "add", value = 1, priority = 0 } } }).Append(new
            {
                id = Force, revision = 1, activations = new[] { new { id = "finish", minCards = 0, maxCards = 0,
                    minTargets = 1, maxTargets = 1, targetKind = "otherLiving", usesPerTurn = (int?)null,
                    effects = new[] { new { op = "loseHp", target = "selectedTarget", amount = 20 } } } }
            }).ToArray();
            var catalog = SkillProgramCatalog.Load(
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = programs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = passiveIds.Append(Force).ToDictionary(id => id, id => new { name = id, description = "Codex behavior fixture" }) }));
            foreach (var entry in catalog.Programs) builder.AddSkill(new(entry.Key, entry.Key, "fixture") { Program = entry.Value });
            builder.AddGeneral(new(Player, "行者", "", Force, "shu"));
            builder.AddGeneral(new(Enemy, "守军", "", OpenSkill, "qun"));
            builder.AddDeck(new("fixture:codex-dodge", "闪牌堆", 4, 2, [new("standard:dodge", 40)]));
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "山河战斗", 2, 2, new Dictionary<string, int>(),
                "fixture:codex-dodge", GeneralPoolIds: [Player, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 1 }));
        }
    }
}
