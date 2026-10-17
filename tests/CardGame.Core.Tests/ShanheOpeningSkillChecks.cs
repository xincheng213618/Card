using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheOpeningSkillChecks
{
    private const string Player = "fixture:opening-player";
    private const string Enemy = "fixture:opening-enemy";
    private const string Force = "fixture:opening-force";
    private const string Inheritance = "inheritance";
    private static readonly Lazy<ContentRegistry> Content = new(() => ContentRegistry.Build(new StandardContentPackage(), new FixturePackage()));

    public static void InitialThreeChoiceSkillIsFrozenUnlockedOnceAndColdReplayed()
    {
        var definition = Definition();
        var options = new ShanheCampaignOptions(71, Player) { UseCodexRewardPool = true, UnlockedRewardIds = [Inheritance], EquippedTacticIds = [Inheritance] };
        var session = Create(definition, options);
        var initial = session.Snapshot;
        Require(initial.Phase == ShanheCampaignPhase.OpeningSkillDraft && initial.Revision == 0 && initial.AvailableNodes.Count == 0 &&
            initial.OpeningSkillOffers.Count == 3 && initial.ExtraSkillIds.Count == 0 && initial.ExtraSkillSlots == 1 && initial.TacticIds.SequenceEqual([Inheritance]),
            "An equipped inheritance tactic must publish one initial three-choice draft before entering the map.");
        Require(initial.OpeningSkillOffers.All(reward => reward.SkillId is not null && reward.SkillId != Force && reward.Id != "outside") &&
            initial.OpeningSkillOffers.Select(reward => reward.SkillId).Distinct().Count() == 3,
            "Initial offers must exclude printed skills, deduplicate aliases and stay inside the explicit pool.");
        Frozen(initial.OpeningSkillOffers); Frozen(session.Definition.Rewards.Single(reward => reward.Id == Inheritance).OpeningSkillRewardIds!);
        Require(Serialize(Create(definition, options)) == Serialize(session), "The initial offer must be deterministic without consuming a journal revision.");
        for (var query = 0; query < 3; query++) _ = session.Snapshot.OpeningSkillOffers;
        Require(ReferenceEquals(initial, session.Snapshot) && session.AcceptedCommands.Count == 0, "Initial-offer queries must not redraw or mutate the run.");
        Cold(session, definition);
        RejectUnchanged(session, new MoveShanheNodeCommand("entry1", session.Revision));
        RejectUnchanged(session, new SkipShanheRewardCommand(session.Revision));
        RejectUnchanged(session, new ChooseShanheRewardCommand(initial.OpeningSkillOffers[0].Id, null, session.Revision));
        RejectUnchanged(session, new ChooseShanheOpeningSkillCommand("outside", session.Revision));
        RejectUnchanged(session, new ChooseShanheOpeningSkillCommand(initial.OpeningSkillOffers[0].Id, session.Revision + 1));
        var chosen = initial.OpeningSkillOffers[0];
        var originalEntry = Create(definition, options with { EquippedTacticIds = [] }).Snapshot.AvailableNodes.Select(node => node.Id).ToArray();
        Accept(session, new ChooseShanheOpeningSkillCommand(chosen.Id, session.Revision));
        Require(session.Snapshot.Phase == ShanheCampaignPhase.Map && session.Snapshot.OpeningSkillOffers.Count == 0 &&
            session.Snapshot.ExtraSkillIds.SequenceEqual([chosen.SkillId!]) && session.Snapshot.ExtraSkillSlots == 1 &&
            session.Snapshot.AvailableNodes.Select(node => node.Id).SequenceEqual(originalEntry) && session.Snapshot.Gold == initial.Gold,
            "The initial tactic grants its locked rare skill into one normal slot, then returns to the original seeded map entry without paying gold.");
        Require(options.UnlockedRewardIds!.SequenceEqual([Inheritance]), "An inheritance grant must not unlock its skill in the persistent codex pool.");
        RejectUnchanged(session, new ChooseShanheOpeningSkillCommand(chosen.Id, session.Revision));
        Cold(session, definition);
        Accept(session, new MoveShanheNodeCommand(originalEntry.Single(), session.Revision));
        Accept(session, new ChooseShanheNodeOptionCommand("continue", null, session.Revision));
        Accept(session, new MoveShanheNodeCommand("battle1", session.Revision));
        Require(session.PendingBattleSetup!.ExtraSkillIds.SequenceEqual([chosen.SkillId!]) && session.PendingBattleSetup.TacticSkillIds.Count == 0,
            "The selected skill must enter the real battle recipe; inheritance must not create an empty battle skill program.");
        var battle = CreateBattle(session.PendingBattleSetup!);
        Accept(battle, new StartGameCommand());
        for (var step = 0; step < 50 && battle.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
            Accept(battle, new AdvanceOneStepCommand(battle.Revision));
        Require(battle.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, "The battle fixture must reach its real play prompt.");
        Accept(battle, new UseProgramSkillCommand(0, Force, "finish", [], [1], battle.Revision, battle.PendingDecision!.PromptId));
        for (var step = 0; step < 50 && battle.State.Status != EngineStatus.Completed; step++) Accept(battle, new AdvanceOneStepCommand(battle.Revision));
        Require(session.CompleteBattle(battle, session.Revision).Accepted, "The first real encounter must complete through the normal journal.");
        Accept(session, new MoveShanheNodeCommand("shop", session.Revision));
        Require(session.Snapshot.ShopItems.Count == 0, "Initial inheritance must not bypass ordinary locked skill shop gates later in the run.");
        RejectUnchanged(session, new ChooseShanheNodeOptionCommand("buy-rare0", null, session.Revision));
        Accept(session, new ChooseShanheNodeOptionCommand("$leave", null, session.Revision));
        Accept(session, new MoveShanheNodeCommand("battle2", session.Revision));
        Require(session.Snapshot.OpeningSkillOffers.Count == 0 && session.Snapshot.ExtraSkillIds.SequenceEqual([chosen.SkillId!]) &&
            session.PendingBattleSetup!.ExtraSkillIds.SequenceEqual([chosen.SkillId!]) && session.PendingBattleSetup.TacticSkillIds.Count == 0,
            "Later encounters must retain the chosen skill without triggering another initial draft.");
        RejectUnchanged(session, new ChooseShanheOpeningSkillCommand(chosen.Id, session.Revision));
        Cold(session, definition);
        MustThrow(() => Create(definition with { ExtraSkillSlots = 0 }, options));
        MustThrow(() => Create(definition, options with { EquippedTacticIds = [Inheritance, Inheritance] }));
        MustThrow(() => Create(definition, options with { UnlockedRewardIds = [] }));
    }

    public static void VerifiedRareCatalogAndOldDefinitionFingerprintsRemainValid()
    {
        var mutable = new List<string> { "rare0", "rare1", "rare2" };
        var draft = new ShanheRewardDefinition("detached", "传承") { OpeningSkillRewardIds = mutable };
        mutable.Clear();
        Require(draft.OpeningSkillRewardIds!.Count == 3, "An initial tactic must detach its candidate pool before publication.");
        var definition = Definition();
        foreach (var bad in new[]
        {
            new ShanheRewardDefinition(Inheritance, "空池") { OpeningSkillRewardIds = [] },
            new ShanheRewardDefinition(Inheritance, "未知技能") { OpeningSkillRewardIds = ["missing"] },
            new ShanheRewardDefinition(Inheritance, "伪装程序") { TacticSkillId = Force, OpeningSkillRewardIds = ["rare0"] }
        }) MustThrow(() => ShanheCampaignContent.FreezeAndValidate(definition with
            { Rewards = definition.Rewards.Select(reward => reward.Id == Inheritance ? bad : reward).ToArray() }));
        foreach (var edition in StandardShanheCampaignCatalog.Editions)
        {
            var current = StandardShanheCampaignCatalog.CreateCodexDefinition(1, edition.Id);
            var inheritance = current.Rewards.Single(reward => reward.Id == StandardShanheCampaignCatalog.OpeningInheritanceRewardId);
            Require(StandardShanheCampaignCatalog.CodexRewardIdsByNodeId[2] == inheritance.Id && inheritance.TacticSkillId is null && inheritance.SkillId is null &&
                inheritance.OpeningSkillRewardIds is { Count: >= 3 }, "Node 2 must map to a real one-time opening tactic with no placeholder program.");
            foreach (var id in inheritance.OpeningSkillRewardIds!)
            {
                var skill = current.Rewards.Single(reward => reward.Id == id).SkillId!;
                Require(StandardShanheCampaignCatalog.ShopMetadataByRewardId[id] is { Rarity: 2, Kind: "技能" } &&
                    StandardShanheCampaignCatalog.PlayerRosterRegistry.Skills[skill].ImplementationStatus == SkillImplementationStatus.Complete,
                    "Every inheritance candidate must be an implemented skill explicitly classified rare in the verified source metadata.");
            }
            var options = new ShanheCampaignOptions(73, current.PlayerGeneralIds[0])
            { UseCodexRewardPool = true, EquippedTacticIds = [inheritance.Id], UnlockedRewardIds = [inheritance.Id] };
            var session = ShanheCampaignSession.Create(current, options, StandardShanheCampaignCatalog.CreateBattleRegistry);
            Require(session.Snapshot.OpeningSkillOffers.Count == 3, "Real codex content must offer three rare skills with only inheritance unlocked.");
            var old = current with { Rewards = current.Rewards.Where(reward => reward.Id != inheritance.Id).ToArray() };
            Require(!JsonSerializer.Serialize(old).Contains("OpeningSkillRewardIds", StringComparison.Ordinal), "Absent opening metadata must not alter historical content JSON.");
            var historical = ShanheCampaignSession.Create(old, options with { EquippedTacticIds = [], UnlockedRewardIds = [] }, StandardShanheCampaignCatalog.CreateBattleRegistry);
            var checkpoint = ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(historical.CreateCheckpoint()));
            var reconstructed = StandardShanheCampaignCatalog.CreateDefinitionForCheckpoint(checkpoint, 1);
            Require(ShanheCampaignContent.GetContentHash(reconstructed) == checkpoint.CampaignHash &&
                reconstructed.Rewards.All(reward => reward.Id != inheritance.Id), "A pre-inheritance codex journal must select its complete old content fingerprint.");
            var restored = ShanheCampaignSession.Restore(checkpoint, reconstructed, StandardShanheCampaignCatalog.CreateBattleRegistry);
            Require(Serialize(restored) == Serialize(historical), "Old codex runs must restore without an opening draft being inserted.");
        }
    }

    private static ShanheCampaignDefinition Definition()
    {
        var skills = Enumerable.Range(0, 5).Select(i => new ShanheRewardDefinition("rare" + i, "稀有技能" + i, SkillId: "fixture:opening-rare-" + i)).ToArray();
        return new("fixture:opening", "传承征程", [Player], ["entry1", "entry2"],
        [
            new("entry1", "北路", ShanheNodeKind.Event, ["battle1"], Choices: [new("continue", "启程")]),
            new("entry2", "南路", ShanheNodeKind.Event, ["battle1"], Choices: [new("continue", "启程")]),
            new("battle1", "首战", ShanheNodeKind.Battle, ["shop"], [new("first", "守军", [new(Enemy)])]),
            new("shop", "行商", ShanheNodeKind.Shop, ["battle2"], Choices: skills.Select(reward =>
                new ShanheNodeChoiceDefinition("buy-" + reward.Id, reward.Name, 100, RewardId: reward.Id, LeavesNode: false)).ToArray()),
            new("battle2", "次战", ShanheNodeKind.Battle, [], [new("second", "守军", [new(Enemy)])])
        ],
        [.. skills, new("alias", "同技能别名", SkillId: skills[0].SkillId), new("printed", "已印制技能", SkillId: Force),
            new("outside", "池外技能", SkillId: "fixture:opening-outside"),
            new(Inheritance, "传承") { OpeningSkillRewardIds = [.. skills.Select(reward => reward.Id), "alias", "printed"] }],
            StartingGold: 400, ExtraSkillSlots: 1) { RandomStartingNode = true };
    }
    private static ShanheCampaignSession Create(ShanheCampaignDefinition definition, ShanheCampaignOptions options) => ShanheCampaignSession.Create(definition, options, _ => Content.Value);
    private static string Serialize(ShanheCampaignSession session) => JsonSerializer.Serialize(session.Snapshot);
    private static void Cold(ShanheCampaignSession session, ShanheCampaignDefinition definition)
    { var restored = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(session.CreateCheckpoint())), definition, _ => Content.Value);
      Require(Serialize(restored) == Serialize(session), "Cold replay must preserve the frozen initial choice, grant and ordinary run progression."); }
    private static void Accept(ShanheCampaignSession session, ShanheCampaignCommand command)
    { var result = session.Submit(command); Require(result.Accepted, result.Error ?? "Campaign command rejected."); }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Require(result.Accepted, result.Error?.ToString() ?? "Battle command rejected."); }
    private static void RejectUnchanged(ShanheCampaignSession session, ShanheCampaignCommand command)
    { var before = Serialize(session); Require(!session.Submit(command).Accepted && Serialize(session) == before, "A rejected initial-draft command must preserve the entire run."); }
    private static void Frozen<T>(IReadOnlyList<T> values)
    { Require(values is IList<T> { IsReadOnly: true }, "Initial offers and pools must be frozen.");
      if (values.Count == 0) return; try { ((IList<T>)values)[0] = values[0]; } catch (NotSupportedException) { return; }
      throw new InvalidOperationException("Published initial offers accepted mutation."); }
    private static void MustThrow(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("An invalid initial skill tactic was accepted."); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static GameEngine CreateBattle(ShanheBattleSetup setup) => GameEngine.CreateStandard(new()
    {
        Seed = setup.BattleSeed, PlayerCount = 2, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
        ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false,
        AiPolicyVersion = AiDecisionPolicy.CurrentVersion
    }, Content.Value);
    private sealed class FixturePackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-opening", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var passive = Enumerable.Range(0, 5).Select(i => "fixture:opening-rare-" + i).Append("fixture:opening-outside");
            var programs = passive.Select(id => (object)new { id, revision = 1,
                modifiers = new[] { new { id = "range", query = "attackRange", operation = "add", value = 1, priority = 0 } } }).Append(new
            {
                id = Force, revision = 1, activations = new[] { new { id = "finish", minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1,
                    targetKind = "otherLiving", usesPerTurn = (int?)null, effects = new[] { new { op = "loseHp", target = "selectedTarget", amount = 20 } } } }
            }).ToArray();
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = programs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = passive.Append(Force).ToDictionary(id => id, id => new { name = id, description = "Opening skill fixture" }) }));
            foreach (var entry in catalog.Programs) builder.AddSkill(new(entry.Key, entry.Key, "fixture") { Program = entry.Value });
            builder.AddGeneral(new(Player, "行者", "", Force, "shu"));
            builder.AddGeneral(new(Enemy, "守军", "", "fixture:opening-outside", "qun"));
            builder.AddDeck(new("fixture:opening-dodge", "闪牌堆", 4, 2, [new("standard:dodge", 40)]));
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "山河战斗", 2, 2, new Dictionary<string, int>(), "fixture:opening-dodge",
                GeneralPoolIds: [Player, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 1 }));
        }
    }
}
