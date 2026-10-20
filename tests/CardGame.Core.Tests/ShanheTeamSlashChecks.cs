using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheTeamSlashChecks
{
    private const string Skill = "shanhe:duisha-2026", Wusheng = "classic:wusheng";
    private const string Owner = "fixture:team-slash-owner", AllyA = "fixture:team-slash-a", AllyB = "fixture:team-slash-b";
    private const string Enemy = "fixture:team-slash-enemy", QuietGeneral = "fixture:team-slash-quiet-general";
    private const string Driver = "fixture:team-slash-driver", Insert = "fixture:team-slash-insert";
    private const string Watch = "fixture:team-slash-before-response", Loss = "fixture:team-slash-dying-loss";
    private const string Deck = "fixture:team-slash-deck", Identity = "identity:classic-team-slash-unknown";

    public static void OtherPublicAlliesAddNativeSharedSlashQuotaAndInsertedPlayResetsCold()
    {
        VerifyLoaderBoundaries();
        var s = Scene.Start("insert"); var turn = s.G.State.TurnNumber;
        Need(s.Events<ProgramPhaseScheduledEvent>().Any(e => e.OwnerSeat == 0 && e.Phase == TurnPhase.Play && e.Started),
            "The first real Play is inserted by the existing preparation-phase instruction, without starting a new turn.");
        s.Slash(CardKind.Slash); s.Slash(CardKind.FireSlash); s.Slash(CardKind.Dodge, converted: true);
        var firstPhase = s.Debits().Select(e => e.PhaseInstanceId).Distinct().Single();
        Need(s.Debits().Length == 3 && !s.CanSlash() && s.V().Hand.Any(card => card.Kind == CardKind.Slash) &&
            s.Debits().All(e => e.ActorSeat == 0 && e.Query == SkillRuleQuery.SlashLimit && e.Phase == TurnPhase.Play && e.TurnNumber == turn),
            "Two distinct allied holders add two uses: ordinary, elemental and real Wusheng-converted Slash share exactly three native paid uses; the owner's own copy and the enemy's copy add none.");
        s.Cold(); s.EndPlay(); s.Play();
        Need(s.G.State.TurnNumber == turn && s.CanSlash(), "Returning from the inserted Play into the same actual turn's normal Play gives a fresh phase-local Slash budget.");
        s.Cold(); s.Slash(CardKind.Slash); s.Slash(CardKind.ThunderSlash); s.Slash(CardKind.Dodge, converted: true);
        var debits = s.Debits(); var phases = debits.GroupBy(e => e.PhaseInstanceId).ToArray();
        Need(debits.Length == 6 && phases.Length == 2 && phases.All(group => group.Count() == 3) &&
            phases.Any(group => group.Key == firstPhase) && debits.All(e => e.TurnNumber == turn) &&
            debits.Select(e => e.CardActionId).Distinct().Count() == 6 && !s.CanSlash() &&
            s.Events<CardUseDebitRefundedEvent>().Length == 0 && s.Events<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 3) == 6,
            "Both genuine Play identities spend their own three-use allowance through the same native debit path, without refunding, sharing or double-paying actions across phases."); s.Cold();

        var ownAndEnemy = Scene.Start("own-and-enemy"); ownAndEnemy.Slash(CardKind.Slash);
        Need(!ownAndEnemy.CanSlash() && ownAndEnemy.Debits().Length == 1 && ownAndEnemy.V().Hand.Any(card => card.Kind == CardKind.Slash),
            "With only the owner's and opposing team's live copies, the public aura still leaves the ordinary single native Slash use."); ownAndEnemy.Cold();
        var unknown = Scene.Start("unknown"); unknown.Slash(CardKind.Slash);
        Need(Enumerable.Range(0, 4).All(seat => unknown.V(seat).TeamId is null) && !unknown.CanSlash() && unknown.Debits().Length == 1,
            "Four same-printed-faction holders in an identity game have no public allied relationship and cannot invent extra Slash uses."); unknown.Cold();
    }

    public static void PaidSlashSurvivesHolderDeathOrLossWhileFutureQuotaRechecksCold()
    {
        foreach (var scene in new[] { "death", "loss" })
        {
            var s = Scene.Start(scene);
            s.BeginSlash(CardKind.Slash); s.AtWatch(); s.Answer(c => Action(c) == "skip"); s.Play();
            Need(s.Debits().Length == 1 && s.CanSlash(), "The first actual Slash is fully paid while both independent allied holders are still enabled.");
            var second = s.BeginSlash(CardKind.ThunderSlash); s.AtWatch();
            Need(s.Debits().Length == 2 && s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == second).Location == CardLocation.Processing,
                "The second real Slash has already paid its own native quota debit and physical hand material before the response-window child can remove an aura source.");
            s.Cold(); s.Answer(c => Action(c) == "activate");
            s.Reach(() => s.P?.Choices.Any(c => c.Targets.SequenceEqual([1])) == true);
            s.Answer(c => c.Targets.SequenceEqual([1]));
            s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
            var native = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == second);
            Need(s.Debits().Length == 2 && native.Action is { ActorSeat: 0 } action &&
                s.Debits().Count(debit => debit.CardActionId == action.ActionId) == 1 &&
                s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == second).Location == CardLocation.Processing &&
                (scene == "death" ? !s.V(1).IsAlive && s.Events<PlayerDiedEvent>().Count(e => e.VictimSeat == 1) == 1 :
                    s.V(1) is { IsAlive: true, Hp: 1 } && s.Events<ProgramOwnerSkillsReplacedEvent>().Single() is { OwnerSeat: 1 } lost && lost.LostSkillIds.SequenceEqual([Skill])) &&
                s.V(2).IsAlive && s.Events<CardUseDebitRefundedEvent>().Length == 0,
                "A real selected holder dies or legally recovers from dying and loses its printed skill while the second Slash remains issued, counted and in Processing; the other holder stays live.");
            s.Cold(); s.Continue(); s.Play();
            Need(s.Events<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 3) == 2 &&
                s.Debits().Length == 2 && s.Events<CardUseDebitRefundedEvent>().Length == 0 &&
                s.G.CardMovements.Count(m => m.CardId == second && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == second && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 && !s.CanSlash(),
                "The already-issued second Slash still resolves and cleans up exactly once; current two-use quota is now fully consumed, so later attacks are unavailable without refunds or repeated costs.");
            var remaining = s.V().Hand.First(card => card.Kind == CardKind.Slash).Id; var before = s.State();
            var rejected = s.G.Submit(new PlayCardCommand(0, remaining, [3], s.G.Revision, s.P!.PromptId, CardKind.Slash));
            Need(!rejected.Accepted && s.State() == before && s.Debits().Length == 2,
                "Submitting a real remaining Slash after source loss is rejected atomically against the current live quota rather than reviving the earlier three-use budget."); s.Cold();
        }
    }

    private static string? Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action");
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string Resource(string suffix)
    {
        var assembly = typeof(StandardContentPackage).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith('.' + suffix, StringComparison.Ordinal)))!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    private static void VerifyLoaderBoundaries()
    {
        var rules = Resource("shanhe-tactics-2026.rules.json"); var presentation = Resource("shanhe-tactics-2026.presentation.json");
        void Reject(Action<JsonObject> change)
        {
            var copy = JsonNode.Parse(rules)!;
            var skill = copy["skills"]!.AsArray().Single(node => node!["id"]!.GetValue<string>() == Skill)!;
            change(skill["modifiers"]![0]!.AsObject()); var rejected = false;
            try { _ = SkillProgramCatalog.Load(copy.ToJsonString(), presentation); } catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "The new numeric Slash aura keeps the shared positive-constant/Add/Always contract instead of accepting set, zero, conditional or card-specific policies.");
        }
        Reject(m => m["operation"] = "set"); Reject(m => m["value"] = 0);
        Reject(m => m["condition"] = new JsonObject { ["kind"] = "wounded" });
        Reject(m => m["cardKinds"] = new JsonArray("slash"));
    }

    private static readonly ShanheStartingCard[] Opening = [new(CardKind.Slash, Suit.Spade, 2), new(CardKind.FireSlash, Suit.Club, 3),
        new(CardKind.Dodge, Suit.Heart, 4), new(CardKind.Slash, Suit.Club, 5), new(CardKind.ThunderSlash, Suit.Spade, 6),
        new(CardKind.Dodge, Suit.Diamond, 7), new(CardKind.Slash, Suit.Spade, 8)];
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardUseDebitIdentity[] Debits() => Events<CardUseDebitRecordedEvent>().Select(e => e.Debit).Where(e => e.ActorSeat == 0 && e.Query == SkillRuleQuery.SlashLimit).ToArray();
        public bool CanSlash() => G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([3]));
        public void Accept(GameCommand command) { var r = G.Submit(command); Need(r.Accepted, r.Error?.Message ?? "A true team-Slash command was rejected."); }
        public void Answer(Func<PromptChoice, bool> choose) => Accept(new AnswerPromptCommand(0, P!.PromptId, P.Choices.First(choose).Id, G.Revision));
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void AtWatch() => Reach(() => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => Action(c) == "activate"));
        public int BeginSlash(CardKind material, bool converted = false)
        {
            var action = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([3]) &&
                a.CardId is { } id && V().Hand.Any(card => card.Id == id && card.Kind == material) &&
                (converted ? a.ConversionSource?.SkillId == Wusheng : a.ConversionSource is null));
            var card = action.CardId!.Value;
            Accept(new PlayCardCommand(0, card, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)
                { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources }); return card;
        }
        public void Slash(CardKind material, bool converted = false) { BeginSlash(material, converted); Play(); }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Reach(Func<bool> done)
        {
            for (var step = 0; step < 180; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die"))
                { Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die"); continue; }
                Need(G.State.Winner == Winner.None && P?.PlayerSeat != 0, "Unexpected team-Slash boundary: " + JsonSerializer.Serialize(new
                { Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId, P.Prompt },
                    Seats = Enumerable.Range(0, 4).Select(seat => new { Seat = seat, V(seat).Hp, V(seat).IsAlive }), Debits = Debits(),
                    Frames = G.ResolutionStack.Select(frame => new { frame.Id, frame.Kind }) }));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded native team-Slash continuation was not reached.");
        }
        public string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint()) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves independent living holders, native phase-local debits, actual cards and paid-before-source-loss children exactly."); }
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene)); GameEngine game;
            if (scene == "unknown")
            {
                game = GameEngine.CreateStandard(new() { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                    ModeId = Identity, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3 }, registry);
                var s = new Scene(game, registry); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
                s.Accept(new SelectGeneralCommand(0, Owner, s.G.Revision, s.P!.PromptId)); s.Play(); return s;
            }
            var setup = new ShanheBattleSetup(83, 1, "fixture-team-slash-" + scene, Owner, 20, 0, 0, 0, 0,
                scene == "insert" ? [Insert] : scene is "death" or "loss" ? [Watch] : [], Opening, [],
                [new(Enemy) { StartingHand = [] }],
                [new(scene == "own-and-enemy" ? QuietGeneral : AllyA, Hp: scene is "death" or "loss" ? 1 : null,
                    ExtraSkillIds: scene == "loss" ? [Loss] : []) { StartingHand = [] },
                 new(scene == "own-and-enemy" ? QuietGeneral : AllyB) { StartingHand = [] }], new string('a', 64));
            game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var result = new Scene(game, registry); result.Accept(new StartGameCommand()); result.Play(); return result;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-team-slash", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "classic:guan-yu");
            var actual = SkillProgramCatalog.Load(Resource("shanhe-tactics-2026.rules.json"), Resource("shanhe-tactics-2026.presentation.json"));
            b.AddSkill(new(Skill, actual.Presentations[Skill].Name, actual.Presentations[Skill].Description)
                { Program = actual.Programs[Skill], ProgramPresentation = actual.Presentations[Skill] });
            var definitions = new object[] {
                new { id = Driver, revision = 1, activations = new[] { new { id = "no-quantity-shortcut", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0,
                    targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { new { op = "draw", target = "owner", amount = 1 } } } } },
                new { id = Insert, revision = 1, triggers = new[] { new { id = "real-preparation-play", window = "turnStartBeforeNormalFlow", subject = "owner", optional = false,
                    usageScope = "game", usageLimit = 1, effects = new[] { new { op = "insertPhase", target = "owner", phase = "play", phaseContinuation = "beforeNormalPreparation" } } } } },
                new { id = Watch, revision = 1, triggers = new[] { new { id = "real-paid-slash-child", window = "slashBeforeResponse", ownerRelation = "actor",
                    cardKinds = new[] { "slash", "fireSlash", "thunderSlash" }, optional = true, usageScope = "game", usageLimit = 1,
                    effects = new object[] { new { op = "selectTarget", target = "owner", targetKind = "otherLiving" },
                        new { op = "loseHp", target = "selectedTarget", amount = 1 },
                        new { op = "chooseOption", target = "owner", resultBind = "after-real-holder-loss", options = new[] { new { id = "continue" } } } } } } },
                new { id = Loss, revision = 1, triggers = new[] { new { id = "legal-dying-entry-loss", window = "dyingEntering", subject = "owner", optional = false,
                    usageScope = "game", usageLimit = 1, effects = new object[] { new { op = "recover", target = "owner", amount = 1 },
                        new { op = "loseOwnerSkillsAndGrant", target = "owner", skillIds = new[] { Skill }, sourceBind = "standard:none" } } } } }
            };
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实队友杀操作", description = "不直接修改出杀额度" },
                [Insert] = new { name = "真实插入出牌", description = "本回合准备阶段插入出牌阶段" },
                [Watch] = new { name = "已付杀的真实子窗口", description = "在同一张杀发行后通过原生失去体力移除队友来源", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Loss] = new { name = "真实濒死失去来源", description = "回复一点并合法终止式失去该战法" } };
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "真实杀使用者", "", Driver, "wu", 20, [Wusheng, Skill]));
            foreach (var id in new[] { AllyA, AllyB, Enemy }) b.AddGeneral(new(id, "独立战法持有者", "", Skill, "wu", 20));
            b.AddGeneral(new(QuietGeneral, "没有该战法的同行", "", "standard:none", "wu", 20));
            b.AddDeck(new(Deck, "固定真实实体", scene == "unknown" ? 7 : 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64)
                .Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "队友杀真实额度", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, AllyA, AllyB, Enemy, QuietGeneral], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int>
                { [ShanheBattleSetup.PlayerTeamId] = 3, [ShanheBattleSetup.EnemyTeamId] = 1 }));
            b.AddMode(new(Identity, "未知公开队伍边界", 4, 4, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 }, Deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, AllyA, AllyB, Enemy]));
        }
    }
}
