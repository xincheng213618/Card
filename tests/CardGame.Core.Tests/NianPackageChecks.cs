using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class NianPackageChecks
{
    private const string Mode = "activity:nian-2016";
    private const string FixtureMode = "fixture:nian-package-victory";
    private const string Driver = "fixture:nian-package-driver";
    private const string Death = "fixture:nian-package-death";
    private const string Owner = "fixture:nian-package-owner";

    public static void RealPackageRotatedDraftTierFiveDeathChildrenAndColdReplay()
    {
        var without = ComposedSkillContentRegistry.CreateShowcase(includeActivities: true,
            includeBossActivities: true, includeHulaoActivities: true, includeShenwuActivities: true);
        var registry = ComposedSkillContentRegistry.CreateShowcase(includeActivities: true,
            includeBossActivities: true, includeHulaoActivities: true, includeShenwuActivities: true,
            includeNianActivities: true);
        var mode = registry.Modes[Mode];
        var boss = mode.BossActivity!;
        var nian = mode.NianActivity!;
        Need(!without.Modes.ContainsKey(Mode) && without.Packages.All(p => p.Id != "nian-activity") &&
            registry.Packages.Count(p => p.Id == "nian-activity") == 1 && registry.ContentHash != without.ContentHash,
            "The complete Nian package is independently opt-in and changes the content fingerprint exactly when included.");
        Need(new[] { "classic:cao-pi", "classic:man-chong", "classic:cai-wen-ji", "boundary:cai-wen-ji",
                "ol:sp-cai-wen-ji", "classic:zuo-ci", "classic:yu-ji" }.All(id => !boss.PlayerPoolIds.Contains(id)) &&
            boss.PlayerPoolIds.All(id => registry.Generals[id].CharacterId is not
                ("character:cao-pi" or "character:man-chong" or "character:cai-wen-ji" or "character:zuo-ci" or "character:yu-ji")) &&
            new[] { "classic:huang-gai", "classic:guan-yu", "ol:wang-yun", "classic:shen-lu-bu", "ol:shen-zhao-yun" }
                .All(boss.PlayerPoolIds.Contains) && !boss.PlayerPoolIds.Contains("classic:shen-zhao-yun") &&
            registry.Generals["classic:cao-pi"].CharacterId is null,
            "The official 2016 bans cover null-identity legacy and current family versions, preserving legal gods and Wang Yun without importing later bans or rewriting metadata.");
        Need(boss.PlayerTier == 5 && boss.PlayerSkillIds.SequenceEqual(["nian:niepan", "nian:revive"]) &&
            nian.SharedRevives == 3 && nian.FormGeneralIds.SequenceEqual(
                ["nian:initial", "nian:wise", "nian:alert", "nian:capricious", "nian:furious"]) &&
            nian.FormGeneralIds is IList<string> { IsReadOnly: true } &&
            nian.FormGeneralIds.All(id => registry.Generals[id] is { BaseHp: 12, FactionId: "god" }) &&
            !boss.PlayerPoolIds.Intersect(nian.FormGeneralIds).Any(),
            "Frozen Nian metadata must retain five twelve-health god forms, shared revival quota and player-only tier skills.");
        try { ((IList<string>)nian.FormGeneralIds)[0] = "nian:furious"; throw new InvalidOperationException("Nian form metadata accepted mutation."); }
        catch (NotSupportedException) { }

        foreach (var human in new[] { 0, 2 })
        {
            // This representative draft observes the tier baseline without an allied general's own opening-hand or HP modification.
            var game = Create(registry, Mode, human, seed: 31);
            Accept(game, new StartGameCommand());
            Until(game, () => game.PendingDecision is { Kind: DecisionKind.SelectGeneral } p && p.PlayerSeat == human);
            var prompt = game.PendingDecision!;
            Need(prompt.IsPrivate && prompt.ValidContentIds.All(boss.PlayerPoolIds.Contains) &&
                Enumerable.Range(0, 4).Where(seat => seat != human).All(seat => game.CreateSnapshot(seat).PendingDecision is null),
                "Both zero and nonzero human seats must receive an exclusively private player-pool draft.");
            var before = Views(game);
            var forged = game.Submit(new SelectGeneralCommand(human, "nian:furious", game.Revision, prompt.PromptId));
            Need(!forged.Accepted && before.SequenceEqual(Views(game)), "A forged NPC selection must reject without changing any viewer state.");
            Accept(game, new RequestGeneralRosterCommand(human, true, game.Revision, prompt.PromptId));
            Need(game.PendingDecision!.ValidContentIds.Contains("ol:shen-zhao-yun") &&
                game.PendingDecision.ValidContentIds.All(boss.PlayerPoolIds.Contains),
                "The real expanded roster preserves legal gods without exposing any Nian state as a player candidate.");
            Accept(game, new SelectGeneralCommand(human, "ol:shen-zhao-yun", game.Revision, game.PendingDecision.PromptId));
            Until(game, () => Facts<SetupCompletedEvent>(game).Any());
            var view = game.CreateSnapshot(human);
            var enemy = view.Players.Single(player => player.TeamId == boss.EnemyTeamId);
            Need(view.Players.Count == 4 && view.Players.Count(player => player.IsHuman) == 1 &&
                view.Players.Single(player => player.IsHuman) is { GeneralId: "ol:shen-zhao-yun", FactionId: "god", MaxHp: 4 } &&
                view.Players.Count(player => !player.IsHuman && player.TeamId == boss.PlayerTeamId) == 2 &&
                enemy.Seat == (human + 3) % 4 && enemy is { GeneralId: "nian:initial", Hp: 12, MaxHp: 12, IsHuman: false },
                "A rotated real encounter must start only the initial twelve-health beast, with a still-god human and exactly two allied AIs.");
            foreach (var player in view.Players)
            {
                var allied = player.TeamId == boss.PlayerTeamId;
                Need(player.MaxHp == registry.Generals[player.GeneralId].BaseHp + (allied ? 2 : 0) &&
                    game.CardMovements.Count(move => move.To == CardLocation.Hand(player.Seat) && move.Reason == CardMoveReasons.InitialDeal) == (allied ? 5 : 4) &&
                    boss.PlayerSkillIds.All(id => player.Skills!.Any(skill => skill.ContentId == id) == allied) &&
                    (player.IsHuman || player.Hand.Count == 0),
                    "Each actual allied slot gets tier-five health, five initial entities and player skills while all foreign hand faces remain private. " +
                    JsonSerializer.Serialize(new
                    {
                        HumanSeat = human, player.Seat, player.GeneralId, Allied = allied, player.Hp, player.MaxHp,
                        BaseHp = registry.Generals[player.GeneralId].BaseHp,
                        ExpectedMaxHp = registry.Generals[player.GeneralId].BaseHp + (allied ? 2 : 0),
                        InitialDeal = game.CardMovements.Count(move => move.To == CardLocation.Hand(player.Seat) && move.Reason == CardMoveReasons.InitialDeal),
                        ExpectedInitialDeal = allied ? 5 : 4,
                        Skills = player.Skills?.Select(skill => skill.ContentId).ToArray(),
                        ExpectedPlayerSkills = boss.PlayerSkillIds, player.IsHuman,
                        VisibleHandFaces = player.Hand.Count, player.HandCount,
                        SetupCompleted = Facts<SetupCompletedEvent>(game).Any(), game.State.Phase,
                        Frames = game.ResolutionStack.Select(frame => frame.GetType().Name).ToArray()
                    }));
            }
            Need(game.IsNianClockRunning && game.NianRemainingMilliseconds == 30 * 60 * 1000 &&
                !Facts<NianFormChangedEvent>(game).Any(), "Setup starts a fresh recorded clock and cannot randomly begin in a later Nian state.");
            Cold(game, registry);
            if (human == 2)
            {
                var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
                var rebuilt = ComposedSkillContentRegistry.CreateShowcaseForCheckpoint(checkpoint);
                var manifest = new StandardNianActivityPackage().Manifest;
                Need(checkpoint.ModeId == Mode && checkpoint.ContentPackages.Contains($"{manifest.Id}@{manifest.Version}") &&
                    rebuilt.ContentHash == checkpoint.ContentHash && rebuilt.Modes[Mode].NianActivity!.SharedRevives == 3 &&
                    JsonSerializer.Serialize(rebuilt.Modes[Mode].NianActivity) == JsonSerializer.Serialize(nian),
                    "Checkpoint package metadata must reconstruct the exact opt-in Nian registry rather than silently selecting a default package set.");
                Cold(game, rebuilt);
            }
        }

        var fixture = ContentRegistry.Build(new StandardContentPackage(), new VictoryFixture(registry));
        var battle = Create(fixture, FixtureMode, 0);
        Accept(battle, new StartGameCommand());
        Until(battle, () => battle.PendingDecision?.Kind == DecisionKind.SelectGeneral);
        Accept(battle, new SelectGeneralCommand(0, Owner, battle.Revision, battle.PendingDecision!.PromptId));
        Until(battle, () => battle.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        var oldHp = battle.CreateSnapshot(0).Players.Where(p => p.TeamId == boss.PlayerTeamId).Sum(p => p.Hp);
        var oldHand = battle.CreateSnapshot(0).Players[0].HandCount;
        var firstEvent = battle.Events.Count;
        Accept(battle, new UseProgramSkillCommand(0, Driver, "defeat", [], [3], battle.Revision, battle.PendingDecision!.PromptId));
        Until(battle, () => battle.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Driver);
        Need(!battle.CreateSnapshot(0).Players[3].IsAlive && battle.State.Winner == Winner.None &&
            battle.CreateSnapshot(0).Players.Where(p => p.TeamId == boss.PlayerTeamId).Sum(p => p.Hp) == oldHp - 1,
            "The real Nian death must finish its owner-death HP-loss child yet remain undecided while a surviving character's death observer is pending.");
        Cold(battle, fixture);
        var observer = battle.PendingDecision!;
        Accept(battle, new AnswerPromptCommand(0, observer.PromptId,
            observer.Choices.Single(choice => choice.Id.Value.StartsWith("program-trigger.activate.", StringComparison.Ordinal)).Id, battle.Revision));
        Until(battle, () => battle.State.Winner != Winner.None);
        var recent = battle.Events.Skip(firstEvent).Select(entry => entry.Payload).ToArray();
        var bossReturn = Array.FindIndex(recent, fact => fact is ProgramBindingResolvedEvent e && e.SkillId == Death && e.Completed);
        var observerReturn = Array.FindIndex(recent, fact => fact is ProgramBindingResolvedEvent e && e.SkillId == Driver &&
            e.Window == SkillProgramTriggerWindow.CharacterDied && e.Activated && e.Completed);
        var victory = Array.FindIndex(recent, fact => fact is WinnerDeterminedEvent);
        Need(battle.State is { Status: EngineStatus.Completed, WinnerTeamId: "team:allies" } &&
            bossReturn >= 0 && observerReturn >= 0 && victory > bossReturn && victory > observerReturn &&
            battle.CreateSnapshot(0).Players[0].HandCount == oldHand + 1 &&
            Facts<WinnerDeterminedEvent>(battle).Count() == 1 && !Facts<BossActivityStageStartedEvent>(battle).Any() &&
            !Facts<NianFormChangedEvent>(battle).Any(),
            "Only the fully returned death subtree may declare one allied victory; Nian state variants never become post-death campaign stages.");
        Cold(battle, fixture);
    }

    private static GameEngine Create(ContentRegistry registry, string mode, int human, int seed = 17) => GameEngine.CreateStandard(new GameOptions
    {
        Seed = seed, PlayerCount = 4, HumanSeat = human, HumanRole = null, HumanTeamId = "team:allies", ModeId = mode,
        UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
        AiPolicyVersion = AiDecisionPolicy.CurrentVersion, MaxTurns = 12
    }, registry);
    private static IEnumerable<T> Facts<T>(GameEngine game) => game.Events.Select(entry => entry.Payload).OfType<T>();
    private static string[] Views(GameEngine game) => Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
    private static void Cold(GameEngine game, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(Views(game).SequenceEqual(Views(copy)) && JsonSerializer.Serialize(game.CardMovements) == JsonSerializer.Serialize(copy.CardMovements) &&
            game.NianRemainingMilliseconds == copy.NianRemainingMilliseconds,
            "Cold JSON replay must preserve every viewer's privacy, physical movement and Nian clock state.");
    }
    private static void Until(GameEngine game, Func<bool> ready)
    {
        for (var step = 0; step < 120; step++) { if (ready()) return; Accept(game, new AdvanceOneStepCommand(game.Revision)); }
        throw new InvalidOperationException("Nian package fixture did not reach its bounded actual boundary: " + SnapshotJson.Serialize(game.CreateSnapshot(0)));
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Need(result.Accepted, result.Error?.ToString() ?? "Nian command rejected."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class VictoryFixture(ContentRegistry source) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("nian-package-victory-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var actual = source.Modes[Mode];
            var forms = actual.NianActivity!.FormGeneralIds;
            foreach (var id in forms.SelectMany(id => source.Generals[id].SkillIds).Concat(actual.BossActivity!.PlayerSkillIds).Distinct())
                builder.AddSkill(source.GetSkill(id));
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"activations":[{"id":"defeat","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]}],"triggers":[{"id":"survivor-death-draw","window":"characterDied","subject":"owner","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
              {"id":"{{{Death}}}","revision":1,"triggers":[{"id":"real-death-child","window":"ownerDied","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"loseHp","target":"selectedTarget","amount":1}]}]}
            ]}
            """;
            var programs = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new[] { Driver, Death }
                .ToDictionary(id => id, id => new { name = id, description = "Bounded native Nian death fixture." }) }));
            foreach (var (id, program) in programs.Programs) builder.AddSkill(new(id, id, "Native Nian death fixture.")
            { Program = program, ProgramPresentation = programs.Presentations[id] });
            const string quiet = "fixture:nian-package-quiet";
            builder.AddSkill(new(quiet, "Quiet fixture", "No active gameplay effect.") { ExecutionForms = SkillExecutionForm.State });
            string[] players = [Owner, "fixture:nian-package-ally-1", "fixture:nian-package-ally-2"];
            foreach (var id in players) builder.AddGeneral(new(id, id, "", id == Owner ? Driver : quiet, "wei", 4));
            foreach (var id in forms)
            {
                var general = source.Generals[id];
                builder.AddGeneral(id == "nian:initial" ? general with
                    { AdditionalSkillIds = (general.AdditionalSkillIds ?? []).Append(Death).ToArray() } : general);
            }
            builder.AddDeck(new("fixture:nian-package-dodge", "Small physical non-recovery deck", 4, 2, [new("standard:dodge", 80)]));
            builder.AddMode(actual with
            {
                Id = FixtureMode, DeckId = "fixture:nian-package-dodge", GeneralCandidateCount = 3,
                GeneralPoolIds = players.Concat(forms).ToArray(),
                BossActivity = actual.BossActivity! with { PlayerPoolIds = players }
            });
        }
    }
}
