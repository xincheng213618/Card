using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheGodFactionChecks
{
    private const string God = "classic:shen-guan-yu", Dong = "boundary:dong-zhuo", Baonue = "boundary:baonue-current";
    private const string Reason = "skill.boundary.baonue-current", Qingliu = "ol:qingliu";
    private const string Driver = "fixture:god-fallback-driver", Quiet = "fixture:god-fallback-quiet";
    private const string Ally = "fixture:god-fallback-ally", Other = "fixture:god-fallback-other", Deck = "fixture:god-fallback-deck";
    private const string OrdinaryMode = "identity:classic-god-fallback-isolation";
    private static readonly Lazy<ContentRegistry> Content = new(() => ContentRegistry.Build(new StandardContentPackage(), new Fixture()));

    public static void RealGodGeneralUsesScenarioFallbackForPublicFactionAndNativeBaonueCold()
    {
        var s = Scene.Start(Recipe("qun", godAlly: true)); s.Play();
        Need(Content.Value.Generals[God] is { FactionId: "god" } printed && printed.SkillIds.SequenceEqual(["classic:wushen", "classic:wuhun"]) &&
            new[] { 0, 1 }.All(seat => s.EffectiveFaction(seat) == "qun" && s.RuntimePlayer(seat).ChosenFactionId is null) &&
            Enumerable.Range(0, 4).All(viewer => new[] { 0, 1 }.All(seat => s.G.CreateSnapshot(viewer).Players[seat] is
                { GeneralId: God, IsGeneralPublic: true, FactionId: "qun", IsFactionRevealed: true })) &&
            s.Events<GodFactionSelectedEvent>().Length == 0 && s.Events<GodFactionSelectionRequestedEvent>().Length == 0,
            "The actual printed god general and original skills stay immutable; only the undeclared scenario fallback is Qun, publicly visible to every viewer without writing ChosenFactionId or fabricating a setup choice.");
        var heartId = s.G.CardMovements.First(m => m.From == CardLocation.OutsideGame && m.To == CardLocation.Hand(0)).CardId;
        Need(s.V().Hand.Single(c => c.Id == heartId) is { Suit: Suit.Heart, Rank: 7 } &&
            s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId == heartId),
            "The real Shen Guan Yu still applies his original compulsory Wushen identity to the actual Heart opening entity while using the scenario's effective faction.");
        s.Cold(); s.Begin("hit-two", [3]); s.AtPaidReturn();
        Need(s.Events<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 3, Amount: 2 }] &&
            s.Events<JudgmentRequestedEvent>().Count(e => e.Reason == Reason && e.TargetSeat == 2) == 2 &&
            s.Events<ProgramJudgmentCardClaimedEvent>().Count(e => e.SkillId == Baonue && e.OwnerSeat == 2) == 2 &&
            s.V(2) is { Hp: 6, HandCount: 2 },
            "An actual D2 from the god general's effective Qun source reaches the other qualified Dong Zhuo's original per-point Baonue twice, with real Spade recovery and physical claims.");
        s.Cold(); s.Continue(); s.Play();
        Need(s.Events<JudgmentRequestedEvent>().Count(e => e.Reason == Reason) == 2 && s.V(2).HandCount == 2,
            "Cold recovery of the actual paid damage parent cannot repeat either Baonue point or original-card payment."); s.Cold();

        var declared = Scene.Start(Recipe("qun", godAlly: true, nativeDeclaration: true));
        declared.Reach(() => declared.P?.SkillPrompt?.SkillId == Qingliu && declared.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "wei"));
        declared.Cold(); declared.Answer(c => c.Parameters.GetValueOrDefault("branch") == "wei"); declared.Play();
        Need(declared.EffectiveFaction(0) == "wei" && declared.EffectiveFaction(1) == "qun" &&
            declared.Events<CaoTengFactionCompletedEvent>() is [{ Setup: true, Faction: "wei" }] &&
            declared.RuntimePlayer(0).ChosenFactionId is null && Content.Value.Generals[God].FactionId == "god" &&
            Enumerable.Range(0, 4).All(viewer => declared.G.CreateSnapshot(viewer).Players[0].FactionId == "wei") &&
            declared.Events<GodFactionSelectedEvent>().Length == 0,
            "The genuine native Qingliu opening declaration overrides Qun fallback for its own god owner, while the other undeclared god remains Qun; no ordinary god-selection state is forged.");
        declared.Cold(); declared.Begin("hit-two", [3]); declared.AtPaidReturn();
        Need(declared.Events<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 3, Amount: 2 }] &&
            !declared.Events<ProgramBindingStartedEvent>().Any(e => e.SkillId == Baonue) && declared.V(2).Hp == 4,
            "After a real Wei declaration, the same real D2 is no longer a Qun-source event and cannot grant Dong Zhuo a Baonue opportunity.");
        declared.Cold(); declared.Continue(); declared.Play(); declared.Cold();
    }

    public static void DefaultGodFactionPolicyFreezesHashesPassesSessionAndRejectsTamperedBattle()
    {
        Need(StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.Returning2026EditionId).DefaultGodFactionId == "qun" &&
            StandardShanheCampaignCatalog.CreateDefinition(1).DefaultGodFactionId is null,
            "Only the actual 2026 edition authors Qun fallback; the 2024 local entry preserves its former absent policy.");
        var legacyDefinition = Definition(null); var currentDefinition = Definition("qun");
        Need(!JsonSerializer.Serialize(legacyDefinition).Contains(nameof(ShanheCampaignDefinition.DefaultGodFactionId), StringComparison.Ordinal) &&
            !JsonSerializer.Serialize(Recipe(null)).Contains(nameof(ShanheBattleSetup.DefaultGodFactionId), StringComparison.Ordinal) &&
            !ShanheBattleSetup.ValueEquals(Recipe(null), Recipe("qun")) &&
            ShanheCampaignContent.GetContentHash(legacyDefinition) != ShanheCampaignContent.GetContentHash(currentDefinition),
            "An absent fallback preserves the legacy JSON shape, while explicit scenario faction policy participates in both definition hashing and exact encounter equality.");
        foreach (var invalid in new[] { "", "god", "jin", "QUN", " qun " })
        {
            Reject(() => ShanheCampaignContent.FreezeAndValidate(Definition(invalid)));
            Reject(() => Scene.Start(Recipe(invalid)));
        }
        foreach (var legal in new[] { "wei", "shu", "wu", "qun" })
        {
            _ = ShanheCampaignContent.FreezeAndValidate(Definition(legal));
            var legalScene = Scene.Start(Recipe(legal)); legalScene.Play();
            Need(legalScene.EffectiveFaction(0) == legal, "Every existing legal god-faction id is a genuine accepted fallback value.");
        }
        var legacy = Scene.Start(Recipe(null)); legacy.Play();
        Need(legacy.EffectiveFaction(0) == "god" && legacy.RuntimePlayer(0).ChosenFactionId is null &&
            legacy.Events<GodFactionSelectedEvent>().Length == 0, "An unconfigured Shanhe encounter keeps the actual printed god faction and does not silently acquire a new choice or fallback.");
        legacy.Begin("hit-two", [3]); legacy.AtPaidReturn();
        Need(!legacy.Events<ProgramBindingStartedEvent>().Any(e => e.SkillId == Baonue), "The former god source does not qualify for the Qun-only damage observer.");
        legacy.Cold(); legacy.Continue(); legacy.Play();
        VerifyOrdinaryChoiceIsStillIndependent();

        var run = ShanheCampaignSession.Create(currentDefinition, new(83, God), _ => Content.Value);
        Accept(run, new MoveShanheNodeCommand("prepare", run.Revision));
        Accept(run, new ChooseShanheNodeOptionCommand("driver", null, run.Revision));
        Accept(run, new MoveShanheNodeCommand("battle", run.Revision));
        var pending = run.PendingBattleSetup!;
        Need(pending.DefaultGodFactionId == "qun" && pending.ExtraSkillIds.SequenceEqual([Driver]),
            "The actual campaign transition copies its frozen definition policy into the exact pending battle recipe, alongside the genuinely acquired test driver.");
        var runBefore = ShanheCampaignJson.Serialize(run.CreateCheckpoint());
        var restoredRun = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(runBefore), currentDefinition, _ => Content.Value);
        Need(ShanheBattleSetup.ValueEquals(restoredRun.PendingBattleSetup, pending) &&
            SnapshotJsonText(restoredRun) == SnapshotJsonText(run), "Cold campaign replay reconstructs the same policy-bearing battle identity.");
        Reject(() => ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(runBefore), legacyDefinition, _ => Content.Value));
        var battle = Scene.Start(pending); battle.Play(); battle.Cold();
        battle.Use("finish", [2]); battle.Begin("finish", [3]);
        battle.Reach(() => battle.G.State.Status == EngineStatus.Completed); battle.Cold();
        var checkpoint = battle.G.CreateCheckpoint();
        foreach (var changed in new string?[] { null, "wei" })
        {
            var forged = checkpoint with { Options = checkpoint.Options with { ShanheBattle = pending with { DefaultGodFactionId = changed } } };
            var before = SnapshotJsonText(run);
            var result = run.Submit(new CompleteShanheBattleCommand(GameCheckpointJson.Serialize(forged), run.Revision));
            Need(!result.Accepted && SnapshotJsonText(run) == before && ShanheCampaignJson.Serialize(run.CreateCheckpoint()) == runBefore,
                "A completed battle carrying a different valid or absent faction policy cannot settle the pending expedition, alter its state or rewrite its journal.");
        }
        Need(run.CompleteBattle(battle.G, run.Revision).Accepted && run.Snapshot.Phase == ShanheCampaignPhase.Won,
            "The original unmodified command-complete battle still settles through the real campaign boundary.");
        restoredRun = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(run.CreateCheckpoint())), currentDefinition, _ => Content.Value);
        Need(SnapshotJsonText(restoredRun) == SnapshotJsonText(run), "The completed expedition, exact faction policy and final journal remain cold-replayable.");
    }

    private static void VerifyOrdinaryChoiceIsStillIndependent()
    {
        var g = GameEngine.CreateStandard(new() { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = OrdinaryMode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3 }, Content.Value);
        var s = new Scene(g); s.Accept(new StartGameCommand());
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        s.Accept(new SelectGeneralCommand(0, God, s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectFaction });
        Need(s.G.CreateCheckpoint().Options.ShanheBattle is null && s.P!.Choices.Select(c => c.Parameters.GetValueOrDefault("faction-id"))
                .SequenceEqual(["wei", "shu", "wu", "qun"]),
            "After scenario battles, an ordinary real god general still receives its original private four-way choice rather than inheriting a process-wide Qun default.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("faction-id") == "wei"); s.Play();
        Need(s.V().FactionId == "wei" && s.Events<GodFactionSelectedEvent>().Single(e => e.ActorSeat == 0).FactionId == "wei" &&
            Content.Value.Generals[God].FactionId == "god", "The ordinary chosen Wei faction remains audited and independent of the 2026 scenario policy."); s.Cold();
    }

    private static ShanheCombatantDefinition Boss() => new(Dong, Hp: 4) { StartingHand = [], QualifiedPrintedLordSkillIds = [Baonue] };
    private static ShanheBattleSetup Recipe(string? policy, bool godAlly = false, bool nativeDeclaration = false) =>
        new(83, 1, "fixture-god-fallback", God, 5, 0, 0, 0, 0, nativeDeclaration ? [Driver, Qingliu] : [Driver],
            [new(CardKind.Dodge, Suit.Heart, 7), new(CardKind.Dodge, Suit.Spade, 8)], [],
            [Boss(), new(Other) { StartingHand = [] }], [new(godAlly ? God : Ally) { StartingHand = [] }], new string('e', 64))
        { DefaultGodFactionId = policy };
    private static ShanheCampaignDefinition Definition(string? policy) => new("fixture-god-fallback", "真实神势力征程", [God], ["prepare"],
        [new("prepare", "实际整备", ShanheNodeKind.Event, ["battle"], Choices: [new("driver", "取得操作", RewardId: "driver")]),
         new("battle", "真实关卡", ShanheNodeKind.BossBattle, [], [new("one", "明确阵容", [Boss(), new(Other) { StartingHand = [] }], [new(Ally) { StartingHand = [] }])])],
        [new("driver", "实际技能", SkillId: Driver)]) { DefaultGodFactionId = policy };
    private static string SnapshotJsonText(ShanheCampaignSession run) => JsonSerializer.Serialize(run.Snapshot);
    private static void Accept(ShanheCampaignSession run, ShanheCampaignCommand command) { var r = run.Submit(command); Need(r.Accepted, r.Error ?? "The real campaign command was rejected."); }
    private static void Reject(Action action) { var rejected = false; try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { rejected = true; } Need(rejected, "Invalid or fingerprint-mismatched scenario faction metadata must be rejected."); }
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CharacterState RuntimePlayer(int seat) => ((IReadOnlyList<CharacterState>)typeof(GameEngine)
            .GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(G)!)[seat];
        public string? EffectiveFaction(int seat) => (string?)typeof(GameEngine).GetMethod("GetEffectiveFactionId", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(G, [RuntimePlayer(seat)]);
        public void Accept(GameCommand command) { var r = G.Submit(command); Need(r.Accepted, r.Error?.Message ?? "A real god-faction command was rejected."); }
        public void Answer(Func<PromptChoice, bool> choose) => Accept(new AnswerPromptCommand(0, P!.PromptId, P.Choices.First(choose).Id, G.Revision));
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Begin(string id, int[] targets) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets, G.Revision, P!.PromptId));
        public void Use(string id, int[] targets) { Begin(id, targets); Play(); }
        public void AtPaidReturn() => Reach(() => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == Driver && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 180; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == Driver) { Continue(); continue; }
                Need(G.State.Winner == Winner.None && P?.PlayerSeat != 0,
                    "Unexpected god-faction boundary: " + JsonSerializer.Serialize(new { Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, P.Prompt, Skill = P.SkillPrompt?.SkillId },
                        Factions = Enumerable.Range(0, 4).Select(seat => new { Seat = seat, Effective = EffectiveFaction(seat), V(seat).FactionId }),
                        Damage = Events<DamageAppliedEvent>(), Judgments = Events<JudgmentRequestedEvent>() }));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded real god-faction continuation was not reached.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint()) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), Content.Value);
            Need(State() == before, "Cold command replay preserves scenario fallback, explicit native declaration, ordinary private choices and actual paid damage children."); }
        public static Scene Start(ShanheBattleSetup setup)
        {
            var s = new Scene(GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, Content.Value));
            s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-god-fallback", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, God, Dong, "ol:cao-teng");
            object Activation(string id, object[] effects) => new { id, minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1,
                targetKind = "anyLiving", usesPerTurn = (int?)null, effects };
            var definitions = new object[] {
                new { id = Driver, revision = 1, activations = new[] {
                    Activation("hit-two", [new { op = "damage", target = "selectedTarget", amount = 2 },
                        new { op = "chooseOption", target = "owner", resultBind = "paid-return", options = new[] { new { id = "continue" } } }]),
                    Activation("finish", [new { op = "loseHp", target = "selectedTarget", amount = 20 }]) } },
                new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false,
                    effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } }
            };
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> {
                    [Driver] = new { name = "实际神势力联动操作", description = "通过真实伤害、失去体力及原生返回检测势力规则", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                    [Quiet] = new { name = "安静实际参战者", description = "只跳出牌阶段" } } }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Ally, "实际同行", "", Quiet, "shu", 8)); b.AddGeneral(new(Other, "实际敌军", "", Quiet, "wei", 8));
            b.AddDeck(new(Deck, "固定黑桃实际判定牌", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64)
                .Select(i => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "神将默认势力真实对局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [God, Dong, Ally, Other], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int>
                { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
            b.AddMode(new(OrdinaryMode, "普通神将选择隔离", 4, 4, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 }, Deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [God, Dong, Ally, Other]));
        }
    }
}
