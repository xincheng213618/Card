using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiCaoShuangChecks
{
    private const string Hero = "mobile:shi-cao-shuang", SheQuan = "mobile:shi-shequan", DianYi = "mobile:shi-dianyi",
        JianZhuan = "mobile:shi-jianzhuan", NiZun = "mobile:shi-nizun",
        Quiet = "fixture:shi-cao-shuang-quiet", SlashLimit = "fixture:shi-cao-shuang-slash-limit",
        SeederDamage = "fixture:shi-cao-shuang-seed-damage",
        SeederDamageLate = "fixture:shi-cao-shuang-seed-damage-late", Peer = "fixture:shi-cao-shuang-peer",
        Deck = "fixture:shi-cao-shuang-deck";

    public static void SheQuanMarksAndDianYiTakesWithOverflow()
    {
        var s = Scene.Start("shequan-take", 4, [SeederDamage, SeederDamageLate],
            [new(1), new(8), new(8), new(8)]); s.Play();
        // The turn-1 seeder self-damage fires caused and taken windows: marking precedes taking.
        s.Reach(() => s.E<ShiCaoShuangSheQuanMarkedEvent>().Length >= 8);
        s.Need(s.E<ShiCaoShuangSheQuanMarkedEvent>().All(e => e.HolderSeat != 0),
            "The marking marks only other living holders' hand cards and never the owner's.");
        // The owner's lethal Slash sends seat 1 into dying; lethal damage skips the
        // after-damage windows, so the dying window carries the round's last take.
        s.Slash(1);
        s.Reach(() => s.E<ShiCaoShuangJianZhuanAwakenedEvent>().Length >= 1, autoEndPlay: false);
        s.Need(s.E<ShiCaoShuangDianYiGainEvent>().Select(e => (e.Round, e.Count, e.Total)).ToArray() is
                [(1, 1, 1), (1, 2, 3), (1, 3, 6)],
            "DianYi gains one more card per same-round trigger (1, 2 then 3) as the marking pool grows.");
        s.Need(s.E<ShiCaoShuangJianZhuanTimingEvent>().Select(e => e.WindowKind).ToArray() is ["caused", "taken", "dying"] &&
            s.E<ShiCaoShuangJianZhuanTimingEvent>().Select(e => e.SkillId).All(id => id == DianYi),
            "JianZhuan records the three first-in-game timings in window order, all placed on DianYi.");
        s.Need(s.E<ShiCaoShuangJianZhuanAwakenedEvent>() is [{ LostSkillId: DianYi, TimingCount: 3 }] &&
            s.E<CharacterSkillsLostEvent>().Any(e => e.Seat == 0 && e.SkillIds.Contains(DianYi)) &&
            s.E<MaximumHpChangedEvent>().Last(e => e.SkillId == JianZhuan).Delta == 3,
            "The completed timings cost DianYi, grant NiZun and raise the maximum HP by the lost skill's timing count.");
        s.Need(s.V(0).Hp == 5,
            "The round-1 overflow (six gained cards above a maximum of four) costs one HP beyond the seeder damage.");
        s.Need(s.MarkerCount(0) == 6,
            "The 奢权 marker tracks the six marked cards the owner took into hand.");
        s.Cold(); s.Replay();
    }

    public static void JianZhuanAwakensAndNiZunCyclesCards()
    {
        var s = Scene.Start("nizun-cycle", 8, [SlashLimit, SeederDamage, SeederDamageLate],
            [new(1), new(8), new(8), new(8)]); s.Play();
        // The lethal Slash drives seat 1 into dying, where the third timing completes the awakening.
        s.Slash(1);
        s.Reach(() => s.E<ShiCaoShuangJianZhuanAwakenedEvent>().Length >= 1, autoEndPlay: false);
        s.Need(s.E<ShiCaoShuangJianZhuanAwakenedEvent>() is [{ LostSkillId: DianYi, TimingCount: 3, NewMaxHp: 11 }] &&
            s.E<RecoveryAppliedEvent>().Any(e => e.TargetSeat == 0 && e.Amount == 3) &&
            s.E<SkillsAcquiredEvent>().Any(e => e.PlayerSeat == 0 && e.SkillIds.Contains(NiZun)),
            "The awakening loses DianYi with its three timings, grants NiZun and recovers the owner by the same count.");
        s.Need(s.V(0).MaxHp == 11,
            "The awakening raised the maximum HP to eight plus the three lost timings.");
        // Still inside turn 1: the owner plays a taken marked Slash; it ends the turn in the discard pile.
        s.Play();
        s.SlashMarked();
        s.Reach(() => s.E<ShiCaoShuangNiZunStashedEvent>().Length >= 1);
        s.Need(s.E<ShiCaoShuangNiZunStashedEvent>().All(e => e.TurnNumber == 1),
            "Marked cards discarded in the used-card turn return onto the owner's hero card at its ending.");
        // Next hero turn: retrieve a stashed card, then the late seeder damage pays the pending penalty.
        s.Play();
        s.Reach(() => s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.UseProgramSkill &&
            a.ProgramSkillId == NiZun));
        s.Accept0(new UseProgramSkillCommand(0, NiZun, "nizun-use", [], [], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.E<ShiCaoShuangNiZunUsedEvent>().Length >= 1);
        s.Need(s.E<ShiCaoShuangNiZunUsedEvent>() is [{ Count: 1 }] && s.E<ShiCaoShuangNiZunUsedEvent>()[0].Round == 2 &&
            s.E<ShiCaoShuangNiZunStashedEvent>().Any(stash => stash.CardId == s.E<ShiCaoShuangNiZunUsedEvent>()[0].CardId),
            "The retrieval records the first same-round NiZun use and takes a card that was stashed.");
        s.Play();
        s.Need(s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "The retrieved card sits in the owner's hand and can be used normally this turn.");
        s.PlayToEnd();
        s.Reach(() => s.E<ShiCaoShuangNiZunPenaltyConsumedEvent>().Length >= 1);
        s.Need(s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 0 && e.Amount == 2) &&
            s.E<ShiCaoShuangNiZunPenaltyConsumedEvent>() is [{ Count: 1 }] &&
            s.E<ShiCaoShuangNiZunPenaltyConsumedEvent>()[0].Round == 2,
            "The next damage after the retrieval lands with +1 for the one same-round use and consumes the penalty once.");
        s.Cold(); s.Replay();
    }

    public static void LostSkillStopsRoundTriggersAndGatesStay()
    {
        var s = Scene.Start("shequan-gates", 8, [SeederDamage],
            [new(1), new(8), new(8), new(8)]); s.Play();
        s.Reach(() => s.E<ShiCaoShuangSheQuanMarkedEvent>().Length >= 8);
        s.Slash(1);
        s.Reach(() => s.E<ShiCaoShuangJianZhuanAwakenedEvent>().Length >= 1,
            pick: choice => choice.Parameters.GetValueOrDefault("branch") == "lose"
                ? choice.Parameters.GetValueOrDefault("skill-id") == SheQuan
                : choice.Parameters.GetValueOrDefault("skill-id") == DianYi);
        s.Need(s.E<ShiCaoShuangJianZhuanAwakenedEvent>() is [{ LostSkillId: SheQuan, TimingCount: 0, NewMaxHp: 8 }] &&
            !s.E<RecoveryAppliedEvent>().Any(e => e.TargetSeat == 0),
            "Losing the skill without placed timings keeps the maximum HP unchanged and recovers nothing.");
        var sheQuanTriggers = s.E<ShiCaoShuangSheQuanRoundTriggerEvent>().Length;
        var dianYiTriggers = s.E<ShiCaoShuangDianYiRoundTriggerEvent>().Length;
        s.Need(sheQuanTriggers == 3 && dianYiTriggers == 3,
            "Round one keeps SheQuan and DianYi to their caused/taken/dying windows once each.");
        s.PlayToEnd();
        for (var round = 0; round < 2; round++)
        {
            s.Play();
            s.Reach(() => s.E<ShiCaoShuangDianYiRoundTriggerEvent>().Length >= dianYiTriggers + 2 * (round + 1));
            s.Need(s.E<ShiCaoShuangSheQuanRoundTriggerEvent>().Length == sheQuanTriggers &&
                s.E<ShiCaoShuangSheQuanRoundTriggerEvent>().All(e => e.Round == 1),
                "The lost SheQuan never triggers again after the awakening.");
            s.Need(s.E<ShiCaoShuangJianZhuanTimingEvent>().Length == 3,
                "JianZhuan stays quiet once all first-in-game timings are placed.");
            s.End();
        }
        s.Need(s.E<ShiCaoShuangDianYiRoundTriggerEvent>().Count(e => e.Round == 2) == 2 &&
            s.E<ShiCaoShuangDianYiRoundTriggerEvent>().Count(e => e.Round == 3) == 2,
            "Later rounds keep one caused and one taken window per round for the surviving DianYi.");
        s.Cold(); s.Replay();
    }

    private sealed record Enemy(int Hp);

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 5).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public int MarkerCount(int seat) => V(seat).Markers?
            .Where(marker => marker.Kind == PlayerMarkerKind.ShiCaoShuangSheQuan).Sum(marker => marker.Count) ?? 0;
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Accept0(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Shi Cao Shuang command."); }
        public void Answer(Func<PromptChoice, bool> pick)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(pick); Need(choice is not null, "The expected prompt choice must be published.");
            Accept0(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision));
        }
        public void End() => Accept0(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void PlayToEnd() { Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }); End(); }
        public void Slash(int target)
        {
            var action = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash &&
                a.TargetSeats.SequenceEqual([target]));
            Accept0(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind));
        }
        public void SlashMarked()
        {
            var marked = E<ShiCaoShuangSheQuanMarkedEvent>().Select(e => e.CardId).ToHashSet();
            var action = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash &&
                a.CardId is { } cardId && marked.Contains(cardId));
            Accept0(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind));
        }
        public void Reach(Func<bool> ready, Func<PromptChoice, bool>? pick = null, bool autoEndPlay = true)
        {
            for (var i = 0; i < 2400; i++)
            {
                if (ready()) return;
                if (P is { } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard)
                    {
                        if (!autoEndPlay) throw new InvalidOperationException("The play phase ended while waiting. " + Dump());
                        End(); continue;
                    }
                    if (p.PlayerSeat == 0 && p.SkillPrompt?.SkillId is SheQuan or DianYi or JianZhuan or NiZun &&
                        p.Kind != DecisionKind.PlayCard)
                    { Answer(pick ?? (_ => true)); continue; }
                    if (p.PlayerSeat == 0 &&
                        p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0); continue; }
                    if (p.PlayerSeat == 0 && p.Kind != DecisionKind.PlayCard &&
                        p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "end-play"))
                    { Answer(c => c.Parameters.GetValueOrDefault("action") == "end-play"); continue; }
                    if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Shi Cao Shuang boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded real scene must retain its participants.");
                Accept0(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Shi Cao Shuang did not reach the expected bounded native boundary.");
        }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold restore preserves exact phase identity, markers and ledger facts."); }
        public void Replay()
        {
            var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry);
            Need(State() == before, "The short actual command journal reproduces marking, taking, awakening and the NiZun cycle exactly.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 5).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P,
            Players = Enumerable.Range(0, 5).Select(i => new { i, V(i).Hp, V(i).MaxHp, V(i).HandCount,
                Markers = V(i).Markers }),
            Marked = E<ShiCaoShuangSheQuanMarkedEvent>().Select(e => new { e.CardId, e.HolderSeat, e.Round }),
            RoundTriggers = new { SheQuan = E<ShiCaoShuangSheQuanRoundTriggerEvent>().Select(e => new { e.Round, e.WindowKind }),
                DianYi = E<ShiCaoShuangDianYiRoundTriggerEvent>().Select(e => new { e.Round, e.WindowKind }) },
            Gains = E<ShiCaoShuangDianYiGainEvent>().Select(e => new { e.Round, e.Count, e.Total }),
            Timings = E<ShiCaoShuangJianZhuanTimingEvent>().Select(e => new { e.SkillId, e.WindowKind, e.Ordinal }),
            Awakened = E<ShiCaoShuangJianZhuanAwakenedEvent>().Select(e => new { e.LostSkillId, e.TimingCount, e.NewMaxHp }),
            Stashed = E<ShiCaoShuangNiZunStashedEvent>().Select(e => new { e.CardId, e.TurnNumber }),
            Used = E<ShiCaoShuangNiZunUsedEvent>().Select(e => new { e.CardId, e.Round, e.Count }),
            Consumed = E<ShiCaoShuangNiZunPenaltyConsumedEvent>().Select(e => new { e.Round, e.Count }),
            Damage = E<DamageAppliedEvent>().Select(e => new { e.SourceSeat, e.TargetSeat, e.Amount }),
            Lost = E<CharacterSkillsLostEvent>().Select(e => new { e.Seat, e.SkillIds }),
            Recovered = E<RecoveryAppliedEvent>().Select(e => new { e.SourceSeat, e.TargetSeat, e.Amount }),
            Acquired = E<SkillsAcquiredEvent>().Select(e => new { e.PlayerSeat, e.SkillIds }),
            Moves = G.CardMovements.TakeLast(40).Select(m => new { m.CardId, From = m.From.Zone, FromSeat = m.From.OwnerSeat, To = m.To.Zone, ToSeat = m.To.OwnerSeat, Reason = m.Reason.Value }),
            Last = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start(string nodeId, int heroHp, string[] heroExtras, Enemy[] enemies, int openingSlashes = 3)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] opening = Enumerable.Range(0, openingSlashes)
                .Select(i => new ShanheStartingCard(CardKind.Slash, i % 2 == 0 ? Suit.Heart : Suit.Club, 4 + i % 9)).ToArray();
            ShanheCombatantDefinition Combatant(int seat, Enemy enemy) => new(Peer, Hp: enemy.Hp, ExtraSkillIds: [Quiet])
            { StartingHand = [new ShanheStartingCard(CardKind.Slash, Suit.Spade, 3 + seat), new ShanheStartingCard(CardKind.Slash, Suit.Club, 6 + seat % 2)] };
            var setup = new ShanheBattleSetup(96, 1, nodeId, Hero, heroHp, heroHp - 4, 0, 0, 0, heroExtras, opening, [],
                enemies.Select((enemy, index) => Combatant(index + 1, enemy)).ToArray(), [], new string('e', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 5, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 24 }, registry);
            var scene = new Scene(game, registry); scene.Accept0(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-cao-shuang", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{SlashLimit}}","revision":1,"modifiers":[{"id":"limit","query":"slashLimit","operation":"add","value":10,"priority":0}]},
              {"id":"{{SeederDamage}}","revision":1,"triggers":[{"id":"seed-damage","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"damage","target":"owner","amount":1}]}]},
              {"id":"{{SeederDamageLate}}","revision":1,"triggers":[{"id":"seed-damage-late","window":"playEnding","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"damage","target":"owner","amount":1}]}]}
            ]}
            """;
            var presentations = new Dictionary<string, object>
            {
                [Quiet] = new { name = Quiet, description = "安静真实回合并跳过普通摸牌出牌。" },
                [SlashLimit] = new { name = SlashLimit, description = "出杀次数上限+10。" },
                [SeederDamage] = new { name = SeederDamage, description = "回合开始受到1点伤害以驱动受到伤害时机。" },
                [SeederDamageLate] = new { name = SeederDamageLate, description = "出牌阶段结束受到1点伤害以结算溺尊惩罚。" },
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静真实同桌", "", Quiet, "wei", 8));
            b.AddCard(new("fixture:shi-cao-shuang-slash", "杀", "基本牌", "同体杀实体", CardKind.Slash));
            b.AddCard(new("fixture:shi-cao-shuang-dodge", "闪", "基本牌", "同体闪实体", CardKind.Dodge));
            b.AddDeck(new(Deck, "固定真实杀牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 120)
                .Select(i => i % 7 == 3
                    ? new ContentDeckPhysicalCard("fixture:shi-cao-shuang-dodge", Suit.Heart, 7)
                    : new ContentDeckPhysicalCard("fixture:shi-cao-shuang-slash", i % 2 == 0 ? Suit.Heart : Suit.Club, 5)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势曹爽四技实际检查", 5, 5, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 4 }));
        }
    }
}
