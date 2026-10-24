using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiZhongHuiChecks
{
    private const string Hero = "mobile:shi-zhong-hui", Sizi = "mobile:shi-sizi", Kechang = "mobile:shi-kechang",
        Quiet = "fixture:shi-zhong-hui-quiet", Peer = "fixture:shi-zhong-hui-peer",
        HandKeeper = "fixture:shi-zhong-hui-keeper", RangeBoost = "fixture:shi-zhong-hui-range",
        SeederTurn = "fixture:shi-zhong-hui-seed-turn", SeederBigTurn = "fixture:shi-zhong-hui-seed-big-turn",
        SeederDraw = "fixture:shi-zhong-hui-seed-draw",
        Deck = "fixture:shi-zhong-hui-deck";

    public static void SiziBurnsChargeBoostsSlashesAndSettlesEndings()
    {
        var s = Scene.Start("sizi", 4, [SeederTurn, HandKeeper, RangeBoost],
            [new(8), new(8), new(8), new(8)]); s.Play();
        s.Reach(() => s.E<PlayerMarkerChangedEvent>().Any(e => e.Marker == PlayerMarkerKind.ShiZhHuiCharge && e.Delta == 2));
        s.Need(s.V(0).Hp == 2 && s.E<PlayerMarkerChangedEvent>().Last(e => e.Marker == PlayerMarkerKind.ShiZhHuiCharge).Count == 2,
            "The seeded HP loss grants two charge points through the locked health-change skill.");
        s.ActivateSizi(2);
        s.Need(s.E<ShiZhHuiSiziActivatedEvent>() is [{ Ordinal: 1, Consumed: 2, OwnerHp: 2 }] &&
            s.E<PlayerMarkerChangedEvent>().Last(e => e.Marker == PlayerMarkerKind.ShiZhHuiCharge).Count == 0,
            "Consuming two charge points records the first activation and clears the marker.");
        s.Play(); s.Slash(1);
        s.Need(s.E<DamageAppliedEvent>().Count(e => e.TargetSeat == 1 && e.Amount == 2) == 1 && s.V(1).Hp == 6,
            "While the effect runs, the owner's Slash deals one extra damage to the unarmored peer.");
        s.Play(); s.End();
        s.Reach(() => s.E<ShiZhHuiSiziElapsedEvent>().Length >= 1);
        s.Need(s.V(0).Hp == 1 && s.E<PlayerMarkerChangedEvent>().Last(e => e.Marker == PlayerMarkerKind.ShiZhHuiCharge).Count == 1 &&
            s.E<ShiZhHuiSiziElapsedEvent>() is [{ ActivationOrdinal: 1, Elapsed: 1 }],
            "The owner's turn ending draws two, costs the turn's Slash user one HP and grants that loss as charge.");
        s.Reach(() => s.E<ShiZhHuiSiziExpiredEvent>().Length >= 1);
        s.Need(s.E<ShiZhHuiSiziExpiredEvent>() is [{ ActivationOrdinal: 1, Cause: "turn-count" }] &&
            s.E<ShiZhHuiSiziElapsedEvent>() is [_, { ActivationOrdinal: 1, Elapsed: 2 }],
            "The second turn ending settles the last elapsed turn and expires the X=2 effect by turn count.");
        s.Cold(); s.Replay();
    }

    public static void XiezhiOverflowRaisesPermanentSlashLimit()
    {
        var s = Scene.Start("xiezhi", 8, [SeederBigTurn, SeederDraw, RangeBoost],
            [new(8), new(8), new(8), new(8)]); s.Play();
        s.Reach(() => s.E<ShiZhHuiXiezhiOverflowEvent>().Length >= 1);
        s.Need(s.E<ShiZhHuiXiezhiOverflowEvent>() is [{ Excess: 1, Total: 1 }] &&
            s.E<PlayerMarkerChangedEvent>().Last(e => e.Marker == PlayerMarkerKind.ShiZhHuiCharge).Count == 4,
            "Gaining five charge points overflows one point past the capacity and clamps the marker at four.");
        s.Play(); s.Slash(1);
        s.Need(s.V(1).Hp == 7 && s.E<DamageAppliedEvent>().Last(e => e.TargetSeat == 1).Amount == 1,
            "Without a running sizi effect the Slash keeps its base damage.");
        s.Play();
        s.Need(s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "The overflow bonus keeps a second Slash usable this phase.");
        s.Slash(1);
        s.Need(s.V(1).Hp == 6, "The second Slash lands through the overflow-raised limit.");
        s.Play();
        s.Need(!s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "A third Slash is back outside the permanent two-slash limit.");
        s.Cold(); s.Replay();
    }

    public static void YunanAwakensOnCausedDyingAndGrantsKechang()
    {
        var s = Scene.Start("yunan", 8, [HandKeeper],
            [new(1), new(1), new(1, Dodge: true), new(1)],
            openingSlashes: 5); s.Play();
        s.Slash(1);
        s.Reach(() => s.E<PlayerDiedEvent>().Any(e => e.VictimSeat == 1));
        s.Need(!s.E<ShiZhHuiYunanAwakenedEvent>().Any(),
            "The first caused dying of the round does not awaken without an earlier death.");
        s.Play(); s.Slash(4);
        s.Reach(() => s.E<ShiZhHuiYunanAwakenedEvent>().Length >= 1);
        s.Need(s.E<ShiZhHuiYunanAwakenedEvent>() is [{ VictimSeat: 4, Level: 1, FactionId: "qun" }] &&
            s.E<NativeEffectiveFactionChangedEvent>().Any(e => e.TargetSeat == 0 && e.PreviousFactionId == "wei" && e.CurrentFactionId == "qun"),
            "The second caused dying awakens once and changes the faction to qun with Kechang granted.");
        s.Play();
        s.Need(s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([3])),
            "Kechang lets the qun-faction owner target out-of-range seats with Slash.");
        s.Slash(3);
        s.Reach(() => s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 3) || s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash));
        s.Need(!s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 3) && s.E<ShiZhHuiYunanAwakenedEvent>().Length == 1,
            "Level-1 Kechang still allows the dodge response, so the distant seat survives unhurt.");
        s.Slash(2);
        s.Reach(() => s.E<ShiZhHuiYunanAwakenedEvent>().Length >= 2);
        s.Need(s.E<ShiZhHuiYunanAwakenedEvent>() is [{ VictimSeat: 4, Level: 1 }, { VictimSeat: 2, Level: 2 }],
            "The third caused dying upgrades Kechang to its second level.");
        s.Play(); s.Slash(3);
        s.Reach(() => s.E<PlayerDiedEvent>().Any(e => e.VictimSeat == 3));
        s.Need(s.E<PlayerDiedEvent>().Select(e => e.VictimSeat).ToArray() is [1, 4, 2, 3],
            "The level-2 owner's Slash kills the dodge-holding seat without any response chance.");
        s.Cold(); s.Replay();
    }

    private sealed record Enemy(int Hp, bool Dodge = false);

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 5).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void ActivateSizi(int consumed)
        {
            Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } &&
                G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.UseProgramSkill && a.ProgramSkillId == Sizi));
            Accept0(new UseProgramSkillCommand(0, Sizi, "burn-charge", [], [], G.Revision, P!.PromptId));
            Reach(() => P is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "consume" &&
                c.Parameters.GetValueOrDefault("count") == consumed.ToString()));
            Answer(c => c.Parameters.GetValueOrDefault("branch") == "consume" &&
                c.Parameters.GetValueOrDefault("count") == consumed.ToString());
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
            for (var i = 0; i < 1600; i++)
            {
                if (ready()) return;
                if (P is { } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard) { End(); continue; }
                    if (p.PlayerSeat == 0 && p.SkillPrompt?.SkillId != Sizi &&
                        p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0); continue; }
                    if (p.PlayerSeat == 0 && p.Kind != DecisionKind.PlayCard && p.SkillPrompt?.SkillId != Sizi &&
                        p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "end-play"))
                    { Answer(c => c.Parameters.GetValueOrDefault("action") == "end-play"); continue; }
                    if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Shi Zhong Hui boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded real scene must retain its participants.");
                Accept0(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Shi Zhong Hui did not reach the expected bounded native boundary.");
        }
        public void Slash(int target)
        {
            var a = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]));
            Accept0(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind));
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold restore preserves exact phase identity, charge marker and ledger facts."); }
        public void Replay()
        {
            var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry);
            Need(State() == before, "The short actual command journal reproduces charge, overflow and awakening exactly.");
        }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept0(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Shi Zhong Hui command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 5).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P,
            Players = Enumerable.Range(0, 5).Select(i => new { i, V(i).Hp, V(i).HandCount }),
            Markers = E<PlayerMarkerChangedEvent>().Select(e => new { e.Marker, e.Delta, e.Count }),
            Sizi = E<ShiZhHuiSiziActivatedEvent>(), Elapsed = E<ShiZhHuiSiziElapsedEvent>(), Expired = E<ShiZhHuiSiziExpiredEvent>(),
            Overflow = E<ShiZhHuiXiezhiOverflowEvent>(), Awakened = E<ShiZhHuiYunanAwakenedEvent>(),
            Died = E<PlayerDiedEvent>().Select(e => e.VictimSeat), SlashUses = E<ShiZhHuiSlashUsedEvent>().Select(e => new { e.TurnNumber, e.ActorSeat }),
            Faction = E<NativeEffectiveFactionChangedEvent>().Select(e => new { e.TargetSeat, e.PreviousFactionId, e.CurrentFactionId }),
            Damage = E<DamageAppliedEvent>().Select(e => new { e.TargetSeat, e.Amount }),
            Moves = G.CardMovements.TakeLast(40).Select(m => new { m.CardId, From = m.From.Zone, FromSeat = m.From.OwnerSeat, To = m.To.Zone, ToSeat = m.To.OwnerSeat, Reason = m.Reason.Value }),
            Last = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start(string nodeId, int heroHp, string[] heroExtras, Enemy[] enemies, int openingSlashes = 3)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] opening = Enumerable.Range(0, openingSlashes)
                .Select(i => new ShanheStartingCard(CardKind.Slash, i % 2 == 0 ? Suit.Heart : Suit.Club, 4 + i % 9)).ToArray();
            ShanheCombatantDefinition Combatant(int seat, Enemy enemy) => new(Peer, Hp: enemy.Hp, ExtraSkillIds: [Quiet])
            { StartingHand = enemy.Dodge ? [new ShanheStartingCard(CardKind.Dodge, Suit.Heart, 7)] : [] };
            var setup = new ShanheBattleSetup(96, 1, nodeId, Hero, heroHp, heroHp - 4, 0, 0, 0, heroExtras, opening, [],
                enemies.Select((enemy, index) => Combatant(index + 1, enemy)).ToArray(), [], new string('e', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 5, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 9 }, registry);
            var scene = new Scene(game, registry); scene.Accept0(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-zhong-hui", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{HandKeeper}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":10,"priority":0},{"id":"many","query":"slashLimit","operation":"add","value":10,"priority":0}]},
              {"id":"{{RangeBoost}}","revision":1,"modifiers":[{"id":"reach","query":"attackRange","operation":"add","value":2,"priority":0}]},
              {"id":"{{SeederTurn}}","revision":1,"triggers":[{"id":"seed-turn","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"loseHp","target":"owner","amount":2}]}]},
              {"id":"{{SeederBigTurn}}","revision":1,"triggers":[{"id":"seed-big-turn","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"loseHp","target":"owner","amount":3}]}]},
              {"id":"{{SeederDraw}}","revision":1,"triggers":[{"id":"seed-draw","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"loseHp","target":"owner","amount":2}]}]}
            ]}
            """;
            var presentations = new Dictionary<string, object>
            {
                [Quiet] = new { name = Quiet, description = "安静真实回合并跳过普通摸牌出牌。" },
                [HandKeeper] = new { name = HandKeeper, description = "保持手牌并无限制出杀。" },
                [RangeBoost] = new { name = RangeBoost, description = "攻击范围+2。" },
                [SeederTurn] = new { name = SeederTurn, description = "回合开始失去2点体力以驱动挟志。" },
                [SeederBigTurn] = new { name = SeederBigTurn, description = "回合开始失去3点体力以触发超额蓄力。" },
                [SeederDraw] = new { name = SeederDraw, description = "摸牌后失去2点体力以触发超额蓄力。" },
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静真实同桌", "", Quiet, "wei", 8));
            b.AddCard(new("fixture:shi-zhong-hui-slash", "杀", "基本牌", "同体杀实体", CardKind.Slash));
            b.AddCard(new("fixture:shi-zhong-hui-dodge", "闪", "基本牌", "同体闪实体", CardKind.Dodge));
            b.AddDeck(new(Deck, "固定真实杀牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 120)
                .Select(i => i % 7 == 3
                    ? new ContentDeckPhysicalCard("fixture:shi-zhong-hui-dodge", Suit.Heart, 7)
                    : new ContentDeckPhysicalCard("fixture:shi-zhong-hui-slash", i % 2 == 0 ? Suit.Heart : Suit.Club, 5)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势钟会四技实际检查", 5, 5, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 4 }));
        }
    }
}
