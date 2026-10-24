using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiTaiShiCiChecks
{
    private const string Hero = "mobile:shi-tai-shi-ci", Hanzhan = "mobile:shi-hanzhan", Zhanlie = "mobile:shi-zhanlie",
        Zhenfeng = "mobile:shi-zhenfeng", Quiet = "fixture:shi-tai-shi-ci-quiet", Peer = "fixture:shi-tai-shi-ci-peer",
        HandKeeper = "fixture:shi-tai-shi-ci-keeper", RangeBoost = "fixture:shi-tai-shi-ci-range", Deck = "fixture:shi-tai-shi-ci-deck";

    public static void HanzhanRalliesBothHandsAndDuels()
    {
        var s = Scene.Start("hanzhan"); s.Play();
        var heroHand = s.V(0).HandCount; var peerHand = s.V(1).HandCount;
        s.ActivateHanzhan(1); s.Play();
        s.Need(s.E<ShiTzHanzhanStartedEvent>() is [{ CounterpartSeat: 1 }],
            "Hanzhan starts exactly one rally against its chosen living counterpart.");
        s.Reach(() => s.E<ShiTzHanzhanRallyDrawnEvent>().Length >= 2);
        _ = heroHand; _ = peerHand;
        s.Reach(() => s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 1 && e.SourceSeat == 0));
        s.Need(s.V(1).Hp == 7 && s.V(0).Hp == 4,
            "The counterpart without a Slash loses the virtual Duel; the owner keeps full HP.");
        s.Need(s.V(0).HandCount == s.V(0).MaxHp && s.V(1).HandCount == s.V(1).MaxHp,
            "Both rally participants end at their own hand cap after the alternated draws.");
        s.Cold(); s.Replay();
    }

    public static void ZhanlieGainsLiesAndIssuesOptionedSlash()
    {
        var s = Scene.Start("zhanlie"); s.Play();
        s.Slash(1); s.Play(); s.Slash(1); s.Play(); s.Slash(1); s.Play();
        var lies = s.E<ShiTzLieGainedEvent>();
        s.Need(lies.Length == 3 && lies.Select(e => e.Ordinal).SequenceEqual([1, 2, 3]) && lies.All(e => e.TurnNumber == 1),
            "The first three Slashes entering the discard pile this turn each grant one lie within the boosted attack range.");
        s.Need(s.V(1).Hp == 5, "Three real Slashes land on the unarmored counterpart.");
        s.End();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Zhanlie && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "option"));
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "extra-damage");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "done");
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") is "pick" or "slash") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick" && c.Targets is [1]);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "slash") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "slash"); s.Play();
        s.Reach(() => s.E<ShiTzSlashIssuedEvent>().Length == 1 && s.E<DamageAppliedEvent>().Count(e => e.TargetSeat == 1) == 4);
        s.Need(s.E<ShiTzSlashIssuedEvent>() is [{ Options: 2, TargetSeats: [1] }] &&
            s.E<DamageAppliedEvent>().Count(e => e.TargetSeat == 1 && e.Amount == 2) == 1 && s.V(1).Hp == 3,
            "The issued virtual Slash carries only the damage option, hits for two, and leaves the counterpart at three.");
        s.Need(s.G.ResolutionStack.OfType<ProgramSkillFrame>().All(f => f.SkillId != Zhanlie),
            "The play-ending option finishes after its last slash child.");
        s.Cold(); s.Replay();
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void ActivateHanzhan(int counterpart)
        {
            Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } &&
                G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.UseProgramSkill && a.ProgramSkillId == Hanzhan));
            Accept0(new UseProgramSkillCommand(0, Hanzhan, "rally", [], [0, counterpart], G.Revision, P!.PromptId));
        }
        public void Answer(Func<PromptChoice, bool> pick)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(pick); Need(choice is not null, "The expected prompt choice must be published.");
            Accept0(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision));
        }
        public void End() => Accept0(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 900; i++)
            {
                if (ready()) return;
                if (P is { } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Zhanlie && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"))
                    { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); continue; }
                    if (p.PlayerSeat == 0 && p.SkillPrompt?.SkillId is not (Zhanlie or Zhenfeng) &&
                        p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0); continue; }
                    if (p.PlayerSeat == 0 && p.SkillPrompt?.SkillId is not (Zhanlie or Zhenfeng) && p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "end-play"))
                    { Answer(c => c.Parameters.GetValueOrDefault("action") == "end-play"); continue; }
                    if (p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard) { End(); continue; }
                    if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Shi Tai Shi Ci boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded real scene must retain all participants.");
                Accept0(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Shi Tai Shi Ci did not reach the expected bounded native boundary.");
        }
        public void Slash(int target)
        {
            var a = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]));
            Accept0(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind));
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold restore preserves exact phase identity, lie ledger and virtual slash children."); }
        public void Replay()
        {
            var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry);
            Need(State() == before, "The short actual command journal reproduces the rally, lie ledger and optioned slash exactly.");
        }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept0(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Shi Tai Shi Ci command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }),
            Rally = E<ShiTzHanzhanStartedEvent>(), Draws = E<ShiTzHanzhanRallyDrawnEvent>(), Lies = E<ShiTzLieGainedEvent>(),
            Issued = E<ShiTzSlashIssuedEvent>(), Damage = E<DamageAppliedEvent>().Select(e => new { e.TargetSeat, e.Amount }),
            Last = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start(string nodeId)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] opening =
            [
                new(CardKind.Slash, Suit.Heart, 4), new(CardKind.Slash, Suit.Club, 5), new(CardKind.Slash, Suit.Spade, 6)
            ];
            ShanheStartingCard[] PeerHand(int seat) => Enumerable.Range(0, 6).Select(i => new ShanheStartingCard(CardKind.Peach, Suit.Heart, 3 + i)).ToArray();
            ShanheCombatantDefinition Combatant(int seat) => new(Peer, Hp: 8, ExtraSkillIds: [Quiet]) { StartingHand = PeerHand(seat) };
            var setup = new ShanheBattleSetup(96, 1, nodeId, Hero, 4, 0, 0, 0, 0, [HandKeeper, RangeBoost], opening, [],
                [Combatant(1), Combatant(2), Combatant(3)], [], new string('e', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 9 }, registry);
            var scene = new Scene(game, registry); scene.Accept0(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-tai-shi-ci", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{HandKeeper}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":10,"priority":0},{"id":"many","query":"slashLimit","operation":"add","value":10,"priority":0}],
                "activations":[{"id":"keep-hand","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
              {"id":"{{RangeBoost}}","revision":1,"modifiers":[{"id":"reach","query":"attackRange","operation":"add","value":2,"priority":0}]}
            ]}
            """;
            var presentations = new Dictionary<string, object>
            {
                [Quiet] = new { name = Quiet, description = "安静真实回合并跳过普通摸牌出牌。" },
                [HandKeeper] = new { name = HandKeeper, description = "保持手牌与无限制出杀。" },
                [RangeBoost] = new { name = RangeBoost, description = "攻击范围+2，供战烈范围记录与距离判定。" },
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静真实同桌", "", Quiet, "wei", 8));
            b.AddCard(new("fixture:shi-tai-shi-ci-slash", "杀", "基本牌", "同体杀实体", CardKind.Slash));
            b.AddDeck(new(Deck, "固定真实杀牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 100)
                .Select(i => new ContentDeckPhysicalCard("fixture:shi-tai-shi-ci-slash", i % 2 == 0 ? Suit.Heart : Suit.Club, 5)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势太史慈酣战战烈实际检查", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
