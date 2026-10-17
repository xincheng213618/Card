using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BossActivityChecks
{
    private const string FixtureMode = "fixture:boss-campaign";
    private const string PlayerSkill = "fixture:boss-player";
    private const string BossDeathSkill = "fixture:boss-limited-death";

    public static void RealGhostSetupPrivateRosterNonzeroHumanAndColdReplay()
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase(includeActivities: true, includeBossActivities: true);
        var mode = registry.Modes["activity:ghost-2018"];
        var activity = mode.BossActivity!;
        Require(new[] { "classic:cao-pi", "classic:man-chong", "classic:cai-wen-ji", "classic:yuan-shu",
                "ol:sp-diao-chan", "ol:sp-jia-xu", "sp:guan-yu" }.All(id => !activity.PlayerPoolIds.Contains(id)) &&
            activity.PlayerPoolIds.Contains("classic:diao-chan") && activity.PlayerPoolIds.Contains("classic:jia-xu") &&
            activity.PlayerPoolIds.Contains("classic:guan-yu") && registry.Generals["classic:cao-pi"].CharacterId is null,
            "Official ghost bans cover legacy null-identity versions and actual Yuan Shu without banning legal ordinary editions or rewriting old metadata.");
        var game = Create(registry, mode.Id, humanSeat: 3);
        var players = game.CreateSnapshot(3).Players;
        var bossSeat = 2;
        Require(players.Count == 4 && players.Count(p => p.IsHuman) == 1 && players.Single(p => p.IsHuman).Seat == 3 &&
            players.Count(p => p.TeamId == activity.PlayerTeamId) == 3 &&
            players.Single(p => p.TeamId == activity.EnemyTeamId) is { Seat: 2, IsHuman: false },
            "A nonzero human seat must have two allied AIs and the single correctly rotated AI boss.");
        foreach (var player in players)
        {
            var boss = player.Seat == bossSeat;
            Require((boss ? activity.Stages[0].GeneralPoolIds : activity.PlayerPoolIds).Contains(player.GeneralId),
                "Every slot must draw only from its player or first-stage boss pool.");
            var definition = registry.Generals[player.GeneralId];
            Require(player.MaxHp == definition.BaseHp + (boss ? 0 : 2) &&
                player.Hp == (definition.InitialHp ?? definition.BaseHp) + (boss ? 0 : 2) &&
                player.HandCount == (boss ? activity.Stages[0].InitialHandSize : 5),
                "Screenshot tier five adds two health and one initial card only to the three player slots.");
            Require(activity.PlayerSkillIds.All(id => player.Skills!.Any(s => s.ContentId == id) == !boss),
                "Player tier skills must decorate both AI teammates, never the boss.");
            if (!player.IsHuman) Require(player.Hand.Count == 0, "Friendly AI and boss hand faces must remain private.");
        }
        Accept(game, new StartGameCommand());
        Cold(game, registry);

        var draft = Create(registry, mode.Id, humanSeat: 3, interactive: true);
        Accept(draft, new StartGameCommand());
        Reach(draft, () => draft.PendingDecision?.Kind == DecisionKind.SelectGeneral);
        var prompt = draft.PendingDecision!;
        Require(prompt.PlayerSeat == 3 && prompt.ValidContentIds.Count > 0 &&
            prompt.ValidContentIds.All(activity.PlayerPoolIds.Contains) &&
            draft.CreateSnapshot(bossSeat).PendingDecision is null,
            "Only the human may see the private draft and only ordinary player generals may be offered.");
        var before = Views(draft);
        var forged = draft.Submit(new SelectGeneralCommand(3, activity.Stages[0].GeneralPoolIds[0], draft.Revision, prompt.PromptId));
        Require(!forged.Accepted && before.SequenceEqual(Views(draft)), "Selecting a forged boss general must fail atomically.");
        Accept(draft, new RequestGeneralRosterCommand(3, true, draft.Revision, prompt.PromptId));
        Require(draft.PendingDecision!.ValidContentIds.Count > 0 &&
            draft.PendingDecision.ValidContentIds.All(activity.PlayerPoolIds.Contains),
            "Expanding the roster must retain the player-only pool boundary.");
        Cold(draft, registry);
    }

    public static void FourStagesWaitForDeathChildrenPreservePlayersAndColdReplay()
    {
        var registry = Build(new FixturePackage());
        var game = Create(registry, FixtureMode);
        Accept(game, new StartGameCommand());
        ReachPlay(game);
        Accept(game, new UseProgramSkillCommand(0, PlayerSkill, "prepare", [], [], game.Revision, game.PendingDecision!.PromptId));
        ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[0] is { Hp: 5, IsChained: true },
            "The fixture must acquire wounded and chained player state through a real command.");
        var originalTurn = game.State.TurnNumber;
        var preserved = AlliedNonHpState(game);
        var inFlightCold = false;
        for (var stage = 0; stage < 4; stage++)
        {
            var before = game.CreateSnapshot(0, revealAll: true);
            var boss = before.Players.Single(p => p.TeamId == "team:boss");
            Require(boss.GeneralId == BossGeneral(stage) && boss.HandCount == stage + 2 && boss.IsAlive &&
                game.GetAttackRange(boss.Seat) == stage + 2,
                "Each live encounter must expose its own hand recipe and rule binding, including a warmed rule shard.");
            Cold(game, registry);
            var firstEvent = game.Events.Count;
            var oldStageEvents = StageEvents(game).Length;
            Accept(game, new UseProgramSkillCommand(0, PlayerSkill, "defeat", [], [boss.Seat], game.Revision, game.PendingDecision!.PromptId));
            var deadBoundaryCold = false;
            for (var step = 0; step < 100; step++)
            {
                var current = game.CreateSnapshot(0, revealAll: true);
                if (!current.Players[boss.Seat].IsAlive && StageEvents(game).Length == oldStageEvents && !deadBoundaryCold)
                {
                    Require(current.Winner == Winner.None, "A defeated boss must wait for death-skill children before victory or replacement.");
                    Cold(game, registry);
                    deadBoundaryCold = inFlightCold = true;
                }
                if (stage == 3 ? current.Winner != Winner.None :
                    StageEvents(game).Length == oldStageEvents + 1 && game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
                    break;
                Accept(game, new AdvanceOneStepCommand(game.Revision));
            }
            var after = game.CreateSnapshot(0, revealAll: true);
            var recent = game.Events.Skip(firstEvent).Select(e => e.Payload).ToArray();
            var deathSkill = BossDeathSkill;
            var deathReturn = Array.FindIndex(recent, e => e is ProgramBindingResolvedEvent r && r.SkillId == deathSkill &&
                r.Window == SkillProgramTriggerWindow.OwnerDied && r.Activated && r.Completed);
            Require(recent.Count(e => e is PlayerDiedEvent d && d.VictimSeat == boss.Seat) == 1 && deathReturn >= 0 &&
                recent.Count(e => e is ProgramSkillHpLostEvent hp && hp.SkillId == deathSkill && hp.Amount == 1) == 1 &&
                after.Players.Where(p => p.TeamId == "team:allies").Sum(p => p.Hp) ==
                    before.Players.Where(p => p.TeamId == "team:allies").Sum(p => p.Hp) - 1,
                "The old boss's actual death binding and nested HP-loss child must settle exactly once.");
            Require(AlliedNonHpState(game) == preserved && game.State.TurnNumber == originalTurn,
                "Stage replacement must preserve allied generals, hands, skills, health maxima and chain state in the current turn.");
            if (stage < 3)
            {
                var transition = Array.FindIndex(recent, e => e is BossActivityStageStartedEvent s && s.StageIndex == stage + 1);
                Require(after.Winner == Winner.None && transition > deathReturn &&
                    after.Players[boss.Seat] is { IsAlive: true } next && next.GeneralId == BossGeneral(stage + 1) &&
                    next.Skills!.Any(s => s.ContentId == BossSkill(stage + 1)) &&
                    next.Skills!.All(s => s.ContentId != BossSkill(stage)) && game.GetAttackRange(boss.Seat) == stage + 3 &&
                    next.HandCount == stage + 3 && next.Hand.All(card => boss.Hand.All(old => old.Id != card.Id)),
                    "The next boss starts after death children with a fresh hand, its own skills and no cached old rule contributions.");
                Require(!game.GetHumanLegalActions().Any(a => a.ProgramSkillId == PlayerSkill && a.ProgramActivationId == "prepare"),
                    "Resetting the replacement boss's shared limited skill must not reset the allied player's spent limited action.");
            }
            else Require(after.WinnerTeamId == "team:allies" && after.Status == EngineStatus.Completed &&
                recent.Any(e => e is WinnerDeterminedEvent) &&
                Array.FindIndex(recent, e => e is WinnerDeterminedEvent) > deathReturn,
                "Only the final death, after its death binding returns, may declare allied victory.");
            Cold(game, registry);
        }
        Require(inFlightCold && StageEvents(game).Select(e => e.StageIndex).SequenceEqual([1, 2, 3]) &&
            game.Events.Count(e => e.Payload is WinnerDeterminedEvent) == 1,
            "The campaign must replay an in-flight death boundary, publish exactly three transitions, and decide the winner once.");
    }

    public static void OneAdvanceSeesPendingStageFactsAndClosesDeadBossTurn()
    {
        var registry = Build(new FixturePackage(chainedSelfDefeat: true));
        var game = Create(registry, FixtureMode);
        Accept(game, new StartGameCommand());
        ReachPlay(game);
        Require(StageEvents(game).Length == 0, "The self-defeat encounter must remain alive until its own real play phase.");
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        var firstEvent = game.Events.Count;
        var beforeRevision = game.Revision;
        Cold(game, registry);

        // One committed host command must observe its own newly queued stage
        // facts. Later bosses die through the hand-gain children of arrival.
        Accept(game, new AdvanceCommand(game.Revision));
        var recent = game.Events.Skip(firstEvent).Select(e => e.Payload).ToArray();
        var stages = recent.OfType<BossActivityStageStartedEvent>().ToArray();
        Require(game.Revision == beforeRevision + 1 && stages.Select(s => s.StageIndex).SequenceEqual([1, 2, 3]) &&
            stages.Select(s => s.GeneralId).SequenceEqual(Enumerable.Range(1, 3).Select(BossGeneral)) &&
            game.State is { Status: EngineStatus.Completed, WinnerTeamId: "team:allies" },
            "A single Advance command must cross all three distinct stages and win, reading pending transition facts without repeating a stage.");
        var bossSeat = 3;
        var playStarting = Array.FindIndex(recent, e => e is ProgramBindingStartedEvent s &&
            s.OwnerSeat == bossSeat && s.SkillId == BossSkill(0) && s.Window == SkillProgramTriggerWindow.PlayPhaseStarting);
        var death = Array.FindIndex(recent, e => e is PlayerDiedEvent d && d.VictimSeat == bossSeat);
        var ended = Array.FindIndex(recent, e => e is TurnEndedEvent t && t.ActorSeat == bossSeat);
        var nextStage = Array.FindIndex(recent, e => e is BossActivityStageStartedEvent { StageIndex: 1 });
        Require(playStarting >= 0 && death > playStarting && ended > death && nextStage > ended &&
            recent.OfType<TurnStartedEvent>().Count(t => t.ActorSeat == bossSeat) == 1,
            "A boss that dies during its own PlayStarting must finish that dead actual turn before the next boss occupies the seat.");
        Cold(game, registry);
    }

    public static void RegistryFreezesNestedMetadataFingerprintsAndRejectsBadTier()
    {
        var fixture = new FixturePackage();
        var registry = Build(fixture);
        var published = registry.Modes[FixtureMode].BossActivity!;
        var before = JsonSerializer.Serialize(published);
        var fingerprint = registry.ContentHash;
        fixture.PlayerPool.Clear();
        fixture.PlayerSkills.Clear();
        fixture.StagePools[0].Clear();
        fixture.Stages.Clear();
        Require(JsonSerializer.Serialize(published) == before && registry.ContentHash == fingerprint,
            "Registry publication must detach every nested player, skill, stage and boss-pool list from package-owned inputs.");
        RejectMutation(published.PlayerPoolIds);
        RejectMutation(published.PlayerSkillIds);
        RejectMutation(published.Stages);
        RejectMutation(published.Stages[0].GeneralPoolIds);
        Require(Build(new FixturePackage(playerTier: 4)).ContentHash != fingerprint &&
            Build(new FixturePackage(firstBossHand: 3)).ContentHash != fingerprint,
            "Player tier and nested encounter hand recipes must both participate in the content fingerprint.");
        try { Build(new FixturePackage(playerTier: 0)); }
        catch (InvalidOperationException exception) when (exception.Message.Contains("Boss activity", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("A boss activity with an invalid player tier was accepted.");
    }

    private static ContentRegistry Build(FixturePackage fixture) => ContentRegistry.Build(new StandardContentPackage(), fixture);
    private static string BossGeneral(int stage) => $"fixture:boss-general-{stage}";
    private static string BossSkill(int stage) => $"fixture:boss-skill-{stage}";
    private static BossActivityStageStartedEvent[] StageEvents(GameEngine game) =>
        game.Events.Select(e => e.Payload).OfType<BossActivityStageStartedEvent>().ToArray();
    private static GameEngine Create(ContentRegistry registry, string mode, int humanSeat = 0, bool interactive = false) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 4, HumanSeat = humanSeat, HumanRole = null, HumanTeamId = "team:allies",
            ModeId = mode, UseInteractiveSetup = interactive, AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false, MaxTurns = 12
        }, registry);
    private static void ReachPlay(GameEngine game) => Reach(game, () => game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine game, Func<bool> arrived)
    {
        for (var step = 0; step < 100; step++)
        {
            if (arrived()) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Boss fixture did not reach its bounded command boundary.");
    }
    private static string AlliedNonHpState(GameEngine game) => JsonSerializer.Serialize(game.CreateSnapshot(0, revealAll: true).Players
        .Where(p => p.TeamId == "team:allies").Select(p => new
        {
            p.Seat, p.GeneralId, p.MaxHp, p.IsAlive, p.Hand, p.IsChained, p.IsFaceDown,
            Skills = p.Skills!.Select(s => s.ContentId).ToArray()
        }));
    private static string[] Views(GameEngine game) => Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
    private static void Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Views(game).SequenceEqual(Views(restored)) &&
            JsonSerializer.Serialize(game.CardMovements) == JsonSerializer.Serialize(restored.CardMovements) &&
            JsonSerializer.Serialize(StageEvents(game)) == JsonSerializer.Serialize(StageEvents(restored)),
            "Cold JSON replay must preserve all player views, exact movements and accepted encounter transitions.");
    }
    private static void RejectMutation<T>(IReadOnlyList<T> values)
    {
        Require(values is IList<T> { IsReadOnly: true }, "Published boss metadata must expose frozen collection implementations.");
        try { ((IList<T>)values)[0] = values[0]; }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("Published boss metadata accepted an assignment.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.ToString() ?? "Boss command rejected.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class FixturePackage(int playerTier = 5, int firstBossHand = 2, bool chainedSelfDefeat = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("boss-campaign-fixture", new Version(1, 0, 0));
        public List<string> PlayerPool { get; } = Enumerable.Range(0, 3).Select(i => $"fixture:boss-player-{i}").ToList();
        public List<string> PlayerSkills { get; } = ["fixture:boss-tier"];
        public List<List<string>> StagePools { get; } = Enumerable.Range(0, 4).Select(i => new List<string> { BossGeneral(i) }).ToList();
        public List<BossActivityStage> Stages { get; } = [];

        public void Register(IContentRegistryBuilder builder)
        {
            var definitions = new List<object>
            {
                new
                {
                    id = PlayerSkill, revision = 1,
                    modifiers = new[] { new { id = "quiet-player", query = "attackRange", operation = "set", value = 1, priority = 0 } },
                    activations = chainedSelfDefeat ? Array.Empty<object>() : new object[]
                    {
                        new { id = "defeat", minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1,
                            targetKind = "otherLiving", usesPerTurn = (int?)null,
                            effects = new[] { new { op = "loseHp", target = "selectedTarget", amount = 20 } } },
                        new { id = "prepare", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0,
                            targetKind = "anyLiving", usesPerTurn = (int?)null, usesPerGame = 1,
                            effects = new object[] { new { op = "loseHp", target = "owner", amount = 1 },
                                new { op = "setChainedState", target = "owner", chained = true } } }
                    }
                }
            };
            definitions.Add(new
            {
                id = BossDeathSkill, revision = 1,
                triggers = new[] { new { id = "death-price", window = "ownerDied", subject = "owner", optional = false,
                    usageScope = "game", usageLimit = 1,
                    effects = new object[] { new { op = "selectTarget", target = "owner", targetKind = "otherLiving" },
                        new { op = "loseHp", target = "selectedTarget", amount = 1 } } } }
            });
            for (var stage = 0; stage < 4; stage++)
            {
                var triggers = new List<object>();
                if (chainedSelfDefeat)
                {
                    if (stage == 0)
                        triggers.Add(new { id = "own-play-self-defeat", window = "playPhaseStarting", subject = "owner", optional = false,
                            effects = new[] { new { op = "loseHp", target = "owner", amount = 20 } } });
                    else
                        triggers.Add(new { id = "arrival-self-defeat", window = "cardsGained", subject = "owner", optional = false,
                            destinationZones = new[] { "hand" }, movementOccurrence = "perBatch",
                            effects = new[] { new { op = "loseHp", target = "owner", amount = 20 } } });
                }
                definitions.Add(new
                {
                    id = BossSkill(stage), revision = 1,
                    modifiers = new[] { new { id = "range", query = "attackRange", operation = "add", value = stage + 1, priority = 0 } },
                    triggers
                });
            }
            var presentation = Enumerable.Range(0, 4).Select(BossSkill).Prepend(BossDeathSkill).Prepend(PlayerSkill)
                .ToDictionary(id => id, id => new { name = id, description = "Bounded campaign behavior fixture" });
            var catalog = SkillProgramCatalog.Load(
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentation }));
            foreach (var program in catalog.Programs)
                builder.AddSkill(new(program.Key, program.Key, "fixture")
                { Program = program.Value, Tags = program.Key == BossDeathSkill ? SkillTag.Limited : SkillTag.None });
            builder.AddSkill(new("fixture:boss-tier", "tier fixture", "Player-only marker") { Tags = SkillTag.Locked });
            foreach (var id in PlayerPool) builder.AddGeneral(new(id, id, "", PlayerSkill, "shu"));
            for (var stage = 0; stage < 4; stage++)
            {
                builder.AddGeneral(new(BossGeneral(stage), $"Boss {stage}", "", BossSkill(stage), "qun", BaseHp: 8,
                    AdditionalSkillIds: [BossDeathSkill]));
                Stages.Add(new($"Stage {stage + 1}", StagePools[stage], stage == 0 ? firstBossHand : stage + 2));
            }
            builder.AddDeck(new("fixture:boss-dodge", "bounded dodge deck", 4, 2, [new("standard:dodge", 100)]));
            builder.AddMode(new(FixtureMode, "fixture boss campaign", 4, 4, new Dictionary<string, int>(),
                "fixture:boss-dodge", GeneralCandidateCount: 3,
                GeneralPoolIds: PlayerPool.Concat(StagePools.SelectMany(p => p)).ToArray(), ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { ["team:allies"] = 3, ["team:boss"] = 1 })
            { BossActivity = new(PlayerPool, Stages, playerTier, PlayerSkills) });
        }
    }
}
