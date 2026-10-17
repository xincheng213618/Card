using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class HellActivityChecks
{
    private const string FixtureMode = "fixture:hell-campaign", Owner = "fixture:hell-campaign-owner",
        Driver = "fixture:hell-campaign-driver", Quiet = "fixture:hell-campaign-quiet", Death = "fixture:hell-campaign-death";

    public static void RealPackageFiveTiersDormantAuxiliaryRotatedDraftAndCheckpoint()
    {
        var without = ComposedSkillContentRegistry.CreateShowcase();
        var registry = ComposedSkillContentRegistry.CreateShowcase(includeHellActivities: true);
        Need(without.Modes.Keys.All(id => !id.StartsWith("activity:hell-2020-", StringComparison.Ordinal)) &&
            registry.Packages.Count(package => package.Id == "hell-activity") == 1 && registry.ContentHash != without.ContentHash &&
            registry.Modes.Values.Count(mode => mode.HellActivity is not null) == 5,
            "The complete four-stage Hell package must independently opt in to five explicit enemy-tier modes.");
        foreach (var tier in Enumerable.Range(1, 5))
        {
            var mode = registry.Modes[$"activity:hell-2020-tier-{tier}"];
            var boss = mode.BossActivity!;
            Need(mode.MinPlayers == 5 && mode.MaxPlayers == 5 && mode.TeamCounts!.GetValueOrDefault("team:allies") == 3 &&
                mode.TeamCounts.GetValueOrDefault("team:hell") == 2 && mode.HellActivity!.BossTier == tier &&
                boss.PlayerTier == 5 && boss.PlayerSkillIds.SequenceEqual(["hell:player-tier", "hell:chongsheng"]) &&
                new[] { "classic:cai-wen-ji", "boundary:cai-wen-ji", "classic:zuo-ci", "classic:yu-ji", "classic:yuan-shu",
                    "classic:huang-hao", "classic:zhang-chun-hua", "classic:lu-xun", "boundary:lu-xun", "ol:sp-jia-xu",
                    "sp:guan-yu", "ol:ling-ju", "ol:bu-zhi" }.All(id => !boss.PlayerPoolIds.Contains(id)) &&
                boss.PlayerPoolIds.Contains("classic:guan-yu") && boss.PlayerPoolIds.Contains("classic:jia-xu") &&
                boss.PlayerPoolIds.Contains("ol:shen-zhao-yun"),
                "Every real tier must retain three tier-five coalition players, exact official bans and legal ordinary Guan Yu/Jia Xu and gods.");
            foreach (var human in new[] { 0, 2 })
            {
                var game = New(registry, mode.Id, human);
                Accept(game, new StartGameCommand());
                Until(game, () => P(game)?.Kind == DecisionKind.SelectGeneral);
                var prompt = P(game)!;
                Need(prompt.PlayerSeat == human && prompt.IsPrivate && prompt.ValidContentIds.All(boss.PlayerPoolIds.Contains) &&
                    Enumerable.Range(0, 5).Where(seat => seat != human).All(seat => game.CreateSnapshot(seat).PendingDecision is null),
                    "Both zero and rotated human seats receive exclusively private allied player-pool drafts.");
                var unchanged = Views(game);
                var rejected = game.Submit(new SelectGeneralCommand(human, "hell:mengpo", game.Revision, prompt.PromptId));
                Need(!rejected.Accepted && unchanged.SequenceEqual(Views(game)), "The dormant auxiliary cannot be forged into a human general.");
                Accept(game, new RequestGeneralRosterCommand(human, true, game.Revision, prompt.PromptId));
                Accept(game, new SelectGeneralCommand(human, "classic:guan-yu", game.Revision, P(game)!.PromptId));
                Until(game, () => Facts<SetupCompletedEvent>(game).Any());
                var view = game.CreateSnapshot(human);
                var npcSeat = (human + 3) % 5;
                var auxiliarySeat = (human + 4) % 5;
                var auxiliary = view.Players[auxiliarySeat];
                Need(view.Players.Count == 5 && view.Players.Count(player => player.IsHuman) == 1 &&
                    view.Players.Count(player => player.TeamId == "team:allies" && !player.IsHuman) == 2 &&
                    view.Players[npcSeat].GeneralId == "hell:qinguang" &&
                    view.Players[npcSeat].MaxHp == 3 + (tier >= 5 ? 2 : tier >= 3 ? 1 : 0) &&
                    auxiliary is { GeneralId: "hell:mengpo", IsAlive: false, Hp: 0, HandCount: 0 } &&
                    auxiliary.MaxHp == 3 + (tier >= 5 ? 2 : tier >= 3 ? 1 : 0) &&
                    !Facts<PlayerDiedEvent>(game).Any(e => e.VictimSeat == auxiliarySeat) &&
                    !Facts<HellWanghunIssuedEvent>(game).Any(),
                    "The fifth slot must be a genuinely unspawned auxiliary without a death fact, death curse or initial hand, while the rotated first boss has the correct enemy tier health.");
                foreach (var player in view.Players.Where(player => player.Seat != auxiliarySeat))
                {
                    var allied = player.TeamId == "team:allies";
                    Need(player.MaxHp == registry.Generals[player.GeneralId].BaseHp + (allied ? 2 : tier >= 5 ? 2 : tier >= 3 ? 1 : 0) &&
                        game.CardMovements.Count(move => move.To == CardLocation.Hand(player.Seat) && move.Reason == CardMoveReasons.InitialDeal) ==
                            (allied ? 6 : tier >= 5 ? 6 : tier >= 3 ? 5 : 4) &&
                        (player.IsHuman || player.Hand.Count == 0),
                        "Every allied entity must receive the official five-tier six-card initial hand; boss tiers receive four/five/six physical cards and foreign hand faces remain private.");
                }
                Need(game.CreateCardZoneDiagnostics().All(card => card.CardKind != CardKind.WoodenOx),
                    "The actual Hell deck must remove every physical Wooden Ox before any real initial deal.");
                Cold(game, registry);
                if (tier == 5 && human == 2)
                {
                    var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
                    var restoredRegistry = ComposedSkillContentRegistry.CreateShowcaseForCheckpoint(checkpoint);
                    Need(restoredRegistry.ContentHash == registry.ContentHash && checkpoint.ContentPackages.Any(id =>
                            id == $"{new StandardHellActivityPackage().Manifest.Id}@{new StandardHellActivityPackage().Manifest.Version}") &&
                        restoredRegistry.Modes[mode.Id].HellActivity!.BossTier == 5,
                        "Cold checkpoint metadata must reconstruct the exact real Hell package and selected enemy tier.");
                    Cold(game, restoredRegistry);
                }
            }
        }
    }

    public static void FourStagesWaitForDeathChildrenAndRealMengpoUnlocksHiddenBoss()
    {
        var source = ComposedSkillContentRegistry.CreateShowcase(includeHellActivities: true);
        var registry = ContentRegistry.Build(new StandardContentPackage(), new CampaignFixture(source));
        var game = New(registry, FixtureMode, 0);
        Accept(game, new StartGameCommand());
        Until(game, () => P(game)?.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, P(game)!.PromptId));
        Play(game);
        var scheduled = Facts<HellAuxiliaryScheduledEvent>(game).Single();
        Need(scheduled.StageIndex is >= 0 and <= 2 && scheduled.AuxiliarySeat == 4 &&
            Facts<HellNpcAppearedEvent>(game).Count(e => e.Seat == 3) == 1,
            "One real draw must schedule Mengpo in one of the first three encounters and record the first boss appearance once.");
        for (var stage = 0; stage < 4; stage++)
        {
            if (V(game, 4).IsAlive)
            {
                Need(stage == scheduled.StageIndex && Facts<HellNpcAppearedEvent>(game).Count(e => e.Seat == 4) == 1,
                    "Mengpo must arrive once at its actual scheduled encounter, rather than behave as an initially dead character.");
                Use(game, 4);
                AwaitObserver(game);
                Need(!V(game, 4).IsAlive && V(game, 3).IsAlive && game.State.Winner == Winner.None &&
                    Facts<PlayerDiedEvent>(game).Count(e => e.VictimSeat == 4) == 1 &&
                    Facts<HellWanghunCompletedEvent>(game) is [{ InjectedCards: 2 }] &&
                    Facts<HellReturnSoulInjectedEvent>(game).Select(e => e.CardId).Distinct().Count() == 2,
                    "Only a true auxiliary death runs its actual Wanghun subtree and injects two physical Soul cards; it cannot finish the encounter by itself.");
                Cold(game, registry);
                ActivateObserver(game);
                Play(game);
            }
            Need(V(game, 3).IsAlive && (stage < scheduled.StageIndex ? !V(game, 4).IsAlive : true),
                "Every encounter must retain one live primary boss while the not-yet-arrived auxiliary stays dormant.");
            var oldBoss = V(game, 3).GeneralId;
            var alliedHp = game.CreateSnapshot(0).Players.Where(player => player.TeamId == "team:allies").Sum(player => player.Hp);
            Use(game, 3);
            AwaitObserver(game);
            Need(!V(game, 3).IsAlive && V(game, 3).GeneralId == oldBoss && game.State.Winner == Winner.None &&
                Facts<BossActivityStageStartedEvent>(game).Length == stage &&
                game.CreateSnapshot(0).Players.Where(player => player.TeamId == "team:allies").Sum(player => player.Hp) == alliedHp - 1,
                "A defeated boss must complete its real owner-death child while stage replacement and victory still wait for the surviving human death observer.");
            Cold(game, registry);
            ActivateObserver(game);
            if (stage < 3)
            {
                Until(game, () => Facts<BossActivityStageStartedEvent>(game).Length == stage + 1 && V(game, 3).IsAlive &&
                    P(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
                var arrival = Facts<BossActivityStageStartedEvent>(game).Last();
                Need(arrival.StageIndex == stage + 1 && arrival.BossSeat == 3 &&
                    (stage != 2 || arrival.GeneralId == CampaignFixture.Hidden) &&
                    game.CardMovements.Count(move => move.To == CardLocation.Hand(3) && move.Reason == CardMoveReasons.InitialDeal) == 4 * (stage + 2),
                    "Each empty-stack encounter replacement gets one new four-card physical hand; an actual earlier Mengpo kill within fifty turns selects the hidden fourth boss.");
                Cold(game, registry);
            }
            else Until(game, () => game.State.Winner != Winner.None);
        }
        var events = game.Events.Select(entry => entry.Payload).ToArray();
        var lastDeathReturn = Array.FindLastIndex(events, fact => fact is ProgramBindingResolvedEvent
            { SkillId: Death, Completed: true });
        var lastObserverReturn = Array.FindLastIndex(events, fact => fact is ProgramBindingResolvedEvent
            { SkillId: Driver, Window: SkillProgramTriggerWindow.CharacterDied, Activated: true, Completed: true });
        var victory = Array.FindIndex(events, fact => fact is WinnerDeterminedEvent);
        Need(game.State is { Status: EngineStatus.Completed, WinnerTeamId: "team:allies" } &&
            Facts<BossActivityStageStartedEvent>(game).Select(e => e.StageIndex).SequenceEqual([1, 2, 3]) &&
            Facts<ProgramBindingResolvedEvent>(game).Count(e => e.SkillId == Death && e.Completed) == 4 &&
            Facts<WinnerDeterminedEvent>(game).Length == 1 && victory > lastDeathReturn && victory > lastObserverReturn,
            "All four independent once-game boss death programs must settle, with exactly three stage changes and a single final victory after the full death subtree.");
        Cold(game, registry);
    }

    public static void LivingMengpoDoesNotUnlockHiddenBossOrBlockFinalVictory()
    {
        var source = ComposedSkillContentRegistry.CreateShowcase(includeHellActivities: true);
        var registry = ContentRegistry.Build(new StandardContentPackage(), new CampaignFixture(source));
        var game = New(registry, FixtureMode, 0);
        Accept(game, new StartGameCommand());
        Until(game, () => P(game)?.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, P(game)!.PromptId));
        Play(game);
        for (var stage = 0; stage < 4; stage++)
        {
            Use(game, 3);
            AwaitObserver(game);
            ActivateObserver(game);
            if (stage == 3) Until(game, () => game.State.Winner != Winner.None);
            else
            {
                Until(game, () => Facts<BossActivityStageStartedEvent>(game).Length == stage + 1 &&
                    V(game, 3).IsAlive && P(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
                if (stage == 2)
                    Need(V(game, 3).GeneralId == "fixture:hell-campaign-stage-3" && V(game, 4).IsAlive &&
                        Facts<HellNpcAppearedEvent>(game).Count(e => e.Seat == 4) == 1 &&
                        !Facts<PlayerDiedEvent>(game).Any(e => e.VictimSeat == 4),
                        "A genuinely appeared but living Mengpo must not unlock the hidden boss even when all three encounters finish within fifty actual turns.");
            }
        }
        Need(game.State.WinnerTeamId == "team:allies" && V(game, 4).IsAlive &&
            Facts<WinnerDeterminedEvent>(game).Length == 1 && !Facts<HellWanghunIssuedEvent>(game).Any(),
            "Completing the ordinary fourth boss must win after its death children while the surviving auxiliary neither blocks victory nor emits a false death curse.");
        Cold(game, registry);
    }

    private static GameEngine New(ContentRegistry registry, string mode, int human) => GameEngine.CreateStandard(new GameOptions
    {
        Seed = 17, PlayerCount = 5, HumanSeat = human, HumanRole = null, HumanTeamId = "team:allies", ModeId = mode,
        UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
        AiPolicyVersion = AiDecisionPolicy.CurrentVersion, MaxTurns = 16
    }, registry);
    private static PlayerSnapshot V(GameEngine game, int seat) => game.CreateSnapshot(seat).Players[seat];
    private static T[] Facts<T>(GameEngine game) => game.Events.Select(entry => entry.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 5).Select(seat => game.CreateSnapshot(seat).PendingDecision)
        .FirstOrDefault(prompt => prompt is not null);
    private static void Use(GameEngine game, int target) => Accept(game,
        new UseProgramSkillCommand(0, Driver, "defeat", [], [target], game.Revision, P(game)!.PromptId));
    private static void AwaitObserver(GameEngine game) => Until(game, () => P(game) is
        { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, SkillPrompt.SkillId: Driver });
    private static void ActivateObserver(GameEngine game)
    {
        var prompt = P(game)!;
        Accept(game, new AnswerPromptCommand(0, prompt.PromptId,
            prompt.Choices.Single(choice => choice.Id.Value.StartsWith("program-trigger.activate.", StringComparison.Ordinal)).Id, game.Revision));
    }
    private static void Play(GameEngine game) => Until(game, () => P(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Until(GameEngine game, Func<bool> ready)
    {
        for (var step = 0; step < 180; step++)
        {
            if (ready()) return;
            if (P(game) is { PlayerSeat: 0 } prompt && prompt.Choices.FirstOrDefault(choice =>
                choice.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die") is { } pass)
                Accept(game, new AnswerPromptCommand(0, prompt.PromptId, pass.Id, game.Revision));
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Hell campaign did not reach its bounded native boundary: " + SnapshotJson.Serialize(game.CreateSnapshot(0)));
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Need(result.Accepted, result.Error?.ToString() ?? "Native Hell campaign command rejected."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string[] Views(GameEngine game) => Enumerable.Range(0, 5).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
    private static void Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(Views(restored).SequenceEqual(Views(game)) && JsonSerializer.Serialize(restored.CardMovements) == JsonSerializer.Serialize(game.CardMovements) &&
            restored.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType()))
                .SequenceEqual(game.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType()))),
            "Cold command replay must preserve all five private views, dormant/arrived NPC state, exact payments and pending death children.");
    }

    private sealed class CampaignFixture(ContentRegistry source) : IGameContentPackage
    {
        internal const string Hidden = "fixture:hell-campaign-hidden";
        public PackageManifest Manifest { get; } = new("hell-campaign-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var actual = source.Modes["activity:hell-2020-tier-1"];
            var mengpo = source.Generals["hell:mengpo"];
            foreach (var id in mengpo.SkillIds) builder.AddSkill(source.GetSkill(id));
            builder.AddCard(source.GetCard("hell:return-soul"));
            var rules = $$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                  {"id":"{{{Driver}}}","revision":1,"activations":[{"id":"defeat","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]}],"triggers":[{"id":"observe-full-death","window":"characterDied","subject":"owner","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
                  {"id":"{{{Death}}}","revision":1,"triggers":[{"id":"shared-once-game-death-child","window":"ownerDied","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"loseHp","target":"selectedTarget","amount":1}]}]}
                ]}
                """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Death }.ToDictionary(id => id, id => new { name = id, description = "Small native campaign death fixture." })
            }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, id, "Small native campaign death fixture.")
            { Program = program, ProgramPresentation = catalog.Presentations[id], Tags = id == Driver ? SkillTag.Awakening : SkillTag.Locked });
            builder.AddSkill(new(Quiet, "Quiet native campaign", "No additional fixture behavior.") { ExecutionForms = SkillExecutionForm.State });
            string[] players = [Owner, "fixture:hell-campaign-ally-1", "fixture:hell-campaign-ally-2"];
            foreach (var id in players) builder.AddGeneral(new(id, id, "", id == Owner ? Driver : Quiet, "wei", 12));
            var stages = Enumerable.Range(0, 4).Select(index => "fixture:hell-campaign-stage-" + index).ToArray();
            foreach (var id in stages.Append(Hidden)) builder.AddGeneral(new(id, id, "", Quiet, "qun", id == Hidden ? 8 : 4, [Death]));
            builder.AddGeneral(mengpo);
            const string deck = "fixture:hell-campaign-deck";
            builder.AddDeck(new(deck, "Small actual non-recovery campaign deck", 4, 2, [new("standard:dodge", 100)]));
            builder.AddMode(actual with
            {
                Id = FixtureMode, DeckId = deck, GeneralCandidateCount = 3,
                GeneralPoolIds = players.Concat(stages).Append(Hidden).Append(mengpo.Id).ToArray(),
                BossActivity = actual.BossActivity! with
                {
                    PlayerPoolIds = players, PlayerSkillIds = [],
                    Stages = stages.Select((id, index) => new BossActivityStage("Fixture encounter " + index,
                        index == 3 ? [id, Hidden] : [id], 4)).ToArray()
                },
                HellActivity = actual.HellActivity! with { HiddenGeneralId = Hidden }
            });
        }
    }
}
