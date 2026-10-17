using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenwuActivityChecks
{
    private const string RealMode = "activity:shenwu-2017";
    private const string FixtureMode = "fixture:shenwu-encounter";
    private const string Driver = "fixture:shenwu-driver";
    private const string Quiet = "fixture:shenwu-quiet";
    private const string Half = "fixture:shenwu-half";
    private const string DeathChild = "fixture:shenwu-death-child";

    public static void RealSharedDraftPrivacyRotatedSeatsEquipmentAndColdReplay()
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase(includeActivities: true,
            includeBossActivities: true, includeHulaoActivities: true, includeShenwuActivities: true);
        var activity = registry.Modes[RealMode].ShenwuActivity!;
        string[] bannedVersions =
        [
            "classic:cai-wen-ji", "classic:da-qiao", "boundary:da-qiao", "classic:diao-chan", "boundary:diao-chan",
            "ol:sp-diao-chan", "classic:man-chong", "classic:xun-yu", "classic:xiao-qiao", "classic:xu-huang",
            "classic:guo-huanghou", "boundary:guo-huang-hou", "ol:da-qiao-xiao-qiao", "classic:yuan-shu",
            "ol:sp-jia-xu", "sp:guan-yu", "ol:kan-ze", "ol:wang-yun", "ol:ma-yun-lu"
        ];
        Require(bannedVersions.All(id => !activity.PlayerPoolIds.Contains(id)) &&
            activity.PlayerPoolIds.Contains("classic:jia-xu") && activity.PlayerPoolIds.Contains("classic:guan-yu") &&
            activity.GodPoolIds.Count == 8 && activity.GodPoolIds.Contains("ol:shen-zhao-yun") &&
            !activity.GodPoolIds.Contains("classic:shen-zhao-yun"),
            "Real bans must cover legacy null-identity and alternate-family versions without banning ordinary Jia Xu/Guan Yu or selecting special Changban Zhao Yun.");
        foreach (var human in new[] { 0, 4 })
        {
            var game = Create(registry, RealMode, human, interactive: true);
            var initialCards = game.CreateCardZoneDiagnostics().ToDictionary(card => card.CardId, card => card.CardKind);
            var players = game.CreateSnapshot(human).Players;
            var leaderSeat = (human + activity.LeaderSeatOffset) % 6;
            Require(players.Count == 6 && players.Count(player => player.IsHuman) == 1 &&
                players.Single(player => player.IsHuman).Seat == human &&
                activity.PlayerSeatOffsets.All(offset => players[(human + offset) % 6].TeamId == activity.PlayerTeamId) &&
                activity.MinionSeatOffsets.Append(activity.LeaderSeatOffset).All(offset =>
                    players[(human + offset) % 6] is { IsHuman: false } player && player.TeamId == activity.EnemyTeamId),
                "Both zero and nonzero human origins must rotate all three allied and all three beast seats exactly once.");
            Accept(game, new StartGameCommand());
            Reach(game, () => game.PendingDecision is { Kind: DecisionKind.SelectGeneral } decision && decision.PlayerSeat == human);
            var prompt = game.PendingDecision!;
            var shared = game.Events.Select(entry => entry.Payload).OfType<ShenwuSharedGodCandidatesEvent>().Single().GeneralIds;
            Require(shared.Count == 3 && shared.Distinct().Count() == 3 && shared.All(activity.GodPoolIds.Contains) &&
                prompt.Choices.Where(choice => choice.Parameters.GetValueOrDefault("shenwu-shared-god") == "true")
                    .SelectMany(choice => choice.ContentIds).Order().SequenceEqual(shared.Order()) &&
                prompt.Choices.Where(choice => choice.Parameters.GetValueOrDefault("shenwu-shared-god") == "false")
                    .Select(choice => choice.Parameters["candidate-general-id"]).Distinct().Count() == 5 &&
                Enumerable.Range(0, 6).Where(seat => seat != human).All(seat => game.CreateSnapshot(seat).PendingDecision is null),
                "Five ordinary candidate families are private while the same three distinct god candidates are shared.");
            Cold(game, registry);
            var unoffered = activity.GodPoolIds.First(id => !shared.Contains(id));
            foreach (var forbidden in new[] { unoffered, activity.LeaderPoolIds[0], activity.MinionGeneralIds[0] })
            {
                var before = Views(game);
                Require(!game.Submit(new SelectGeneralCommand(human, forbidden, game.Revision, prompt.PromptId)).Accepted &&
                    before.SequenceEqual(Views(game)), "Unshared gods and NPCs must be rejected atomically by the actual draft command.");
            }
            Accept(game, new RequestGeneralRosterCommand(human, true, game.Revision, prompt.PromptId));
            prompt = game.PendingDecision!;
            Require(prompt.ValidContentIds.All(id => activity.PlayerPoolIds.Contains(id) || shared.Contains(id)) &&
                prompt.ValidContentIds.Where(activity.GodPoolIds.Contains).Order().SequenceEqual(shared.Order()),
                "The expanded ordinary roster must not reveal or allow the five unshared gods or any beast.");
            var chosen = shared.First(id => id != "ol:shen-zhuge-liang");
            Accept(game, new SelectGeneralCommand(human, chosen, game.Revision, prompt.PromptId));
            Reach(game, () => game.CreateSnapshot(human, revealAll: true).Players.All(player => player.HandCount > 0));
            var after = game.CreateSnapshot(human, revealAll: true);
            Require(after.Players[human].GeneralId == chosen && after.Players[human].FactionId == "god" &&
                after.Players.Count(player => player.GeneralId == chosen) == 1 &&
                activity.LeaderPoolIds.Contains(after.Players[leaderSeat].GeneralId) &&
                activity.MinionSeatOffsets.Select((offset, index) =>
                    after.Players[(human + offset) % 6].GeneralId == activity.MinionGeneralIds[index]).All(value => value),
                $"The selected god must retain god faction, remain unavailable to AI teammates and preserve exact leader/minion slots. Chosen={chosen}; human={human}; leader={leaderSeat}; actual=" +
                    string.Join("; ", after.Players.Select(player => $"{player.Seat}:{player.GeneralId}/{player.FactionId}")));
            var allCards = game.CreateCardZoneDiagnostics();
            var gods = after.Players.Where(player => activity.GodPoolIds.Contains(player.GeneralId)).ToArray();
            Require(allCards.Count == allCards.Select(card => card.CardId).Distinct().Count() &&
                allCards.Count == initialCards.Count + gods.Count(player =>
                    player.GeneralId is "classic:shen-lu-meng" or "classic:shen-sima-yi"),
                "God equipment must transform original entities, adding only the selected gods' two genuinely additional equipment kinds.");
            foreach (var god in gods)
            {
                var (equipment, originalKind) = GodEquipment(god.GeneralId);
                var actual = god.Equipment.Single(card => card.Kind == equipment);
                Require(allCards.Single(card => card.CardId == actual.Id).Location == CardLocation.Equipment(god.Seat) &&
                    (originalKind is { } original ? initialCards.GetValueOrDefault(actual.Id) == original : !initialCards.ContainsKey(actual.Id)),
                    "Each god's equipped card must be the transformed physical original or its single added entity, never a second copy.");
            }
            foreach (var player in game.CreateSnapshot(human).Players.Where(player => !player.IsHuman))
                Require(player.Hand.Count == 0, "Allied AI and beast hand faces must remain private after the complete draft.");
            Cold(game, registry);
        }
    }

    public static void ExactInitialRecipePermanentHalfHealthAndFrozenMetadata()
    {
        var fixture = new Fixture();
        var registry = ContentRegistry.Build(new StandardContentPackage(), fixture);
        var published = registry.Modes[FixtureMode].ShenwuActivity!;
        var frozen = JsonSerializer.Serialize(published);
        var hash = registry.ContentHash;
        fixture.PlayerPool.Clear();
        fixture.PlayerOffsets[0] = 5;
        fixture.HalfSkills.Clear();
        Require(JsonSerializer.Serialize(published) == frozen && registry.ContentHash == hash &&
            published.PlayerPoolIds is IList<string> { IsReadOnly: true } &&
            published.PlayerSeatOffsets is IList<int> { IsReadOnly: true } &&
            published.HalfHpSkillIds is IDictionary<string, string> { IsReadOnly: true },
            "Published Shenwu metadata must detach and freeze nested pools, seat offsets and permanent-skill maps.");
        Require(ContentRegistry.Build(new StandardContentPackage(), new Fixture(playerTier: 4)).ContentHash != hash,
            "The local player tier must participate in the content fingerprint.");
        foreach (var human in new[] { 0, 4 })
        {
            var initial = Create(registry, FixtureMode, human);
            var snapshot = initial.CreateSnapshot(human, revealAll: true);
            Require(snapshot.Players.All(player => player.MaxHp == (player.TeamId == "team:allies" ? 6 :
                    player.Seat == (human + 4) % 6 ? 8 : 4) && player.Hp == player.MaxHp &&
                player.HandCount == (player.TeamId == "team:allies" ? 5 : player.Seat == (human + 4) % 6 ? 8 : 4)),
                "The six-seat initial recipe is leader eight cards, minions four, and three tier-five players at health six with five cards.");
            Cold(initial, registry);
        }
        var game = Create(registry, FixtureMode);
        Accept(game, new StartGameCommand());
        ReachPlay(game);
        var range = game.GetAttackRange(4);
        Activate(game, "hit-four", [4]);
        ReachPlay(game);
        Require(View(game).Players[4].Hp == 4 && View(game).Players[4].Skills!.Any(skill => skill.ContentId == Half) &&
            game.GetAttackRange(4) == range + 4 && HalfEvents(game).Length == 1,
            "The first real damage to half health must grant the formerly absent skill and invalidate warmed rule queries.");
        Cold(game, registry);
        Activate(game, "heal-three", [4]);
        ReachPlay(game);
        Require(View(game).Players[4].Hp == 7 && View(game).Players[4].Skills!.Any(skill => skill.ContentId == Half),
            "Healing above half health must retain the permanent skill.");
        Activate(game, "hit-four", [4]);
        ReachPlay(game);
        Require(View(game).Players[4].Hp == 3 && HalfEvents(game).Length == 1 &&
            View(game).Players[4].Skills!.Count(skill => skill.ContentId == Half) == 1,
            "A later threshold crossing must not grant or publish the same hidden skill twice.");
        Cold(game, registry);
    }

    public static void LeaderVictoryWaitsForDeathChildrenAndAlliedExtinctionLoses()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = Create(registry, FixtureMode);
        Accept(game, new StartGameCommand());
        ReachPlay(game);
        Activate(game, "defeat", [3]);
        ReachPlay(game);
        Require(!View(game).Players[3].IsAlive && View(game).Players[4].IsAlive && game.State.Winner == Winner.None,
            "Defeating a minion alone must never complete the single leader encounter.");
        var originalAlliedHp = View(game).Players.Where(player => player.TeamId == "team:allies").Sum(player => player.Hp);
        var firstEvent = game.Events.Count;
        Activate(game, "defeat", [4]);
        var replayedDeath = false;
        Reach(game, () =>
        {
            if (!View(game).Players[4].IsAlive && game.State.Winner == Winner.None && !replayedDeath)
            {
                Cold(game, registry);
                replayedDeath = true;
            }
            return game.State.Winner != Winner.None;
        });
        var recent = game.Events.Skip(firstEvent).Select(entry => entry.Payload).ToArray();
        var childReturn = Array.FindIndex(recent, fact => fact is ProgramBindingResolvedEvent resolved &&
            resolved.SkillId == DeathChild && resolved.Window == SkillProgramTriggerWindow.OwnerDied && resolved.Completed);
        var victory = Array.FindIndex(recent, fact => fact is WinnerDeterminedEvent);
        Require(replayedDeath && childReturn >= 0 && victory > childReturn &&
            recent.Count(fact => fact is ProgramSkillHpLostEvent hp && hp.SkillId == DeathChild && hp.Amount == 1) == 1 &&
            View(game).Players.Where(player => player.TeamId == "team:allies").Sum(player => player.Hp) == originalAlliedHp - 1 &&
            game.State.WinnerTeamId == "team:allies" && View(game).Players[5].IsAlive,
            "Leader death must settle the genuine death binding and its HP-loss child before victory, preserving a living minion.");
        Cold(game, registry);

        var defeat = Create(registry, FixtureMode);
        Accept(defeat, new StartGameCommand());
        ReachPlay(defeat);
        foreach (var ally in new[] { 1, 2 })
        {
            Activate(defeat, "defeat", [ally]);
            ReachPlay(defeat);
        }
        Activate(defeat, "self-defeat", []);
        Reach(defeat, () => defeat.State.Winner != Winner.None);
        Require(defeat.State.WinnerTeamId == "team:beasts" && View(defeat).Players.Take(3).All(player => !player.IsAlive) &&
            View(defeat).Players[4].IsAlive, "Allied extinction must lose even while the leader is still alive.");
        Cold(defeat, registry);
    }

    private static (CardKind Kind, CardKind? Original) GodEquipment(string id) => id switch
    {
        "classic:shen-guan-yu" => (CardKind.GhostDragonCrescentBlade, CardKind.QinglongCrescentBlade),
        "classic:shen-lu-meng" => (CardKind.NationalStyleRobe, null),
        "classic:shen-zhou-yu" => (CardKind.CrimsonSoulZither, CardKind.ZhuqueFan),
        "ol:shen-zhuge-liang" => (CardKind.QimenEightTrigrams, CardKind.BaguaFormation),
        "classic:shen-cao-cao" => (CardKind.DustlessGoldenSpear, CardKind.DefensiveHorse),
        "classic:shen-lu-bu" => (CardKind.ShuraInfernoHalberd, CardKind.FangtianHalberd),
        "classic:shen-sima-yi" => (CardKind.DelusionCrown, null),
        "ol:shen-zhao-yun" => (CardKind.ScarletBloodSword, CardKind.QinggangSword),
        _ => throw new InvalidOperationException("Unexpected god in the exact eight-character activity pool.")
    };
    private static GameEngine Create(ContentRegistry registry, string mode, int human = 0, bool interactive = false) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 6, HumanSeat = human, HumanRole = null, HumanTeamId = "team:allies", ModeId = mode,
            UseInteractiveSetup = interactive, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
    private static GameSnapshot View(GameEngine game) => game.CreateSnapshot(game.State.HumanSeat, revealAll: true);
    private static ShenwuLeaderEnragedEvent[] HalfEvents(GameEngine game) =>
        game.Events.Select(entry => entry.Payload).OfType<ShenwuLeaderEnragedEvent>().ToArray();
    private static void Activate(GameEngine game, string activation, int[] targets) =>
        Accept(game, new UseProgramSkillCommand(0, Driver, activation, [], targets, game.Revision, game.PendingDecision!.PromptId));
    private static void ReachPlay(GameEngine game) => Reach(game,
        () => game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine game, Func<bool> arrived)
    {
        for (var step = 0; step < 300; step++)
        {
            if (arrived()) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The bounded Shenwu fixture did not reach its requested command boundary.");
    }
    private static string[] Views(GameEngine game) => Enumerable.Range(0, 6)
        .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
    private static void Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Views(game).SequenceEqual(Views(restored)) &&
            JsonSerializer.Serialize(game.CardMovements) == JsonSerializer.Serialize(restored.CardMovements) &&
            JsonSerializer.Serialize(game.Events) == JsonSerializer.Serialize(restored.Events),
            "Cold JSON replay must preserve all six private views, exact physical movements and mode facts.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "Shenwu command rejected.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(int playerTier = 5) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("shenwu-mode-fixture", new Version(1, 0, 0));
        public List<string> PlayerPool { get; } = Enumerable.Range(0, 15).Select(index => $"fixture:shenwu-player-{index}").ToList();
        public List<int> PlayerOffsets { get; } = [0, 1, 2];
        public Dictionary<string, string> HalfSkills { get; } = new(StringComparer.Ordinal) { ["fixture:shenwu-leader"] = Half };

        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"hit-four","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":4}]},
                {"id":"heal-three","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyWounded","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":3}]},
                {"id":"defeat","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                {"id":"self-defeat","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":20}]}
              ]},
              {"id":"{{Half}}","revision":1,"modifiers":[{"id":"granted-range","query":"attackRange","operation":"add","value":4,"priority":0}]},
              {"id":"{{DeathChild}}","revision":1,"triggers":[{"id":"leader-real-death-child","window":"ownerDied","subject":"owner","optional":false,"effects":[
                {"op":"selectTarget","target":"owner","targetKind":"publicTeamEnemies"},{"op":"loseHp","target":"selectedTarget","amount":1}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Half, DeathChild }.ToDictionary(id => id, id =>
                    new { name = id, description = "Small six-seat actual-command Shenwu fixture." })
            }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, id, "fixture") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddSkill(new(Quiet, "Quiet fixture", "No additional behavior."));
            foreach (var id in PlayerPool) builder.AddGeneral(new(id, id, "", Driver, "shu", BaseHp: 4));
            var gods = Enumerable.Range(0, 3).Select(index => $"fixture:shenwu-god-{index}").ToArray();
            foreach (var id in gods) builder.AddGeneral(new(id, id, "", Quiet, "god", BaseHp: 4));
            builder.AddGeneral(new("fixture:shenwu-leader", "Fixture leader", "", Quiet, "qun", BaseHp: 8,
                AdditionalSkillIds: [DeathChild]));
            string[] minions = ["fixture:shenwu-minion-left", "fixture:shenwu-minion-right"];
            foreach (var id in minions) builder.AddGeneral(new(id, id, "", Quiet, "qun", BaseHp: 4));
            builder.AddDeck(new("fixture:shenwu-dodge", "Uniform bounded dodge deck", 4, 2, [new("standard:dodge", 100)]));
            builder.AddMode(new(FixtureMode, "Small six-seat Shenwu", 6, 6, new Dictionary<string, int>(), "fixture:shenwu-dodge", 5,
                PlayerPool.Concat(gods).Concat(["fixture:shenwu-leader"]).Concat(minions).ToArray(), ContentModeKind.Team,
                new Dictionary<string, int> { ["team:allies"] = 3, ["team:beasts"] = 3 })
            { ShenwuActivity = new(PlayerPool, gods, ["fixture:shenwu-leader"], minions, PlayerOffsets, 4, [3, 5], playerTier, [], HalfSkills) });
        }
    }
}
