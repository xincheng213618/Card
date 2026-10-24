using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiLuYuShengChecks
{
    private const string Hero = "mobile:shi-lu-yu-sheng", Runwei = "mobile:shi-runwei", Shuanghuai = "mobile:shi-shuanghuai",
        Quiet = "fixture:shi-lu-yu-sheng-quiet", Peer = "fixture:shi-lu-yu-sheng-peer",
        SlashOwner = "fixture:shi-lu-yu-sheng-slash-owner", HandKeeper = "fixture:shi-lu-yu-sheng-keeper",
        Deck = "fixture:shi-lu-yu-sheng-deck";

    public static void RunweiGrantsColorDebtRefundAndExcludesGainedTargets()
    {
        var s = Scene.Start("runwei");
        s.AutoEndPlay = false;
        s.SkipShuanghuaiOffers = true;
        s.Play();
        s.Offer(Runwei); s.Activate();
        var peerHand = s.V(1).HandCount;
        s.Answer(c => Key(c) == "mobile-shi-runwei" && c.Parameters.GetValueOrDefault("branch") == "count" &&
            c.Parameters.GetValueOrDefault("count") == "1");
        s.Answer(c => Key(c) == "mobile-shi-runwei" && c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets is [1]);
        s.Need(s.E<MobileShiRunweiGrantIssuedEvent>() is [{ TargetSeat: 1, RevealedCount: 1, GainedCount: 1 }] &&
                s.V(1).HandCount == peerHand + 1 &&
                s.G.CardMovements.Count(m => m.Reason.Value == "skill-program.mobile:shi-runwei.grant" && m.To == CardLocation.Hand(1)) == 1,
            "One revealed deck card of its only color reaches the named recipient through the grant reason.");
        // 出牌阶段内失去一张牌（打出一张【杀】）即满足 X=1 的解禁债务并返还阶段使用次数。
        s.Play();
        var slash = s.G.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash && action.TargetSeat == 1);
        s.AcceptPlay(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, s.G.Revision, s.P!.PromptId, slash.PlayedCardKind));
        s.Reach(() => s.E<MobileShiRunweiDebtRefundedEvent>() is [{ Required: 1, Counted: 1 }]);
        s.Play();
        s.Offer(Runwei); s.Activate();
        s.Answer(c => Key(c) == "mobile-shi-runwei" && c.Parameters.GetValueOrDefault("branch") == "count" &&
            c.Parameters.GetValueOrDefault("count") == "1");
        s.Need(s.P is { } target && target.Choices.Where(c => Key(c) == "mobile-shi-runwei" && c.Parameters.GetValueOrDefault("branch") == "target")
                .All(c => c.Targets is not [1]),
            "The refunded second activation this turn must not offer the seat that already gained Runwei cards.");
        var secondHand = s.V(2).HandCount;
        s.Answer(c => Key(c) == "mobile-shi-runwei" && c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets is [2]);
        s.Need(s.E<MobileShiRunweiGrantIssuedEvent>() is [_, { TargetSeat: 2, GainedCount: 1 }] &&
                s.V(2).HandCount == secondHand + 1,
            "The refunded activation grants the second single-color reveal to another recipient.");
        s.Play();
        s.End();
        s.Reach(() => s.E<MobileShiRunweiPhaseEndDiscardedEvent>() is [{ DiscardedCount: 2, LeftHandCount: 0 }]);
        s.Need(s.V(1).HandCount == peerHand && s.V(2).HandCount == secondHand &&
                s.G.CardMovements.Count(m => m.Reason.Value == "skill-program.mobile:shi-runwei.phase-end" && m.To == CardLocation.DiscardPile) == 2,
            "Both phase-marked granted hand cards return to the discard pile at play-phase end.");
        s.Replay();
    }

    public static void ShuanghuaiPreventsDamageAndSettlesSameDifferentMemory()
    {
        var s = Scene.Start("shuanghuai");
        s.End();
        s.Offer(Shuanghuai); s.Activate();
        s.Reach(() => s.P is { } prompt && prompt.Choices.Any(c => Key(c) == "mobile-shi-shuanghuai"));
        s.Answer(c => Key(c) == "mobile-shi-shuanghuai" && c.Parameters.GetValueOrDefault("option") == "prevent");
        s.Reach(() => s.E<MobileShiShuanghuaiGuardIssuedEvent>().Length == 1);
        s.Need(s.E<MobileShiShuanghuaiGuardIssuedEvent>() is [{ VictimSeat: 1, Option: "prevent", SameAsLast: false }] &&
                s.E<ProgramSkillHpLostEvent>().Count(e => e.TargetSeat == 0) == 1 && s.V(1).Hp == 2 && s.V(0).Hp == 2 &&
                !s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 1),
            "The first guard prevents the distance-one Slash damage, and the different-from-last memory costs the owner one HP.");
        s.Offer(Shuanghuai); s.Activate();
        s.Reach(() => s.P is { } prompt && prompt.Choices.Any(c => Key(c) == "mobile-shi-shuanghuai"));
        s.Answer(c => Key(c) == "mobile-shi-shuanghuai" && c.Parameters.GetValueOrDefault("option") == "prevent");
        s.Reach(() => s.E<MobileShiShuanghuaiGuardIssuedEvent>().Length == 2);
        s.Need(s.E<MobileShiShuanghuaiGuardIssuedEvent>() is [_, { VictimSeat: 1, SameAsLast: true }] &&
                s.E<MobileShiShuanghuaiGuardIssuedEvent>().All(g => g.Option == "prevent") &&
                s.V(1).Hp == 2 && s.V(0).Hp == 2 &&
                s.G.CardMovements.Count(m => m.Reason.Value == "skill-program.mobile:shi-shuanghuai.draw" && m.To == CardLocation.Hand(0)) == 1,
            "Repeating the same victim prevents the damage again and grants both players one card instead of an HP loss.");
        s.Replay();
    }

    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public bool AutoEndPlay = true;
        public bool SkipShuanghuaiOffers = false;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Offer(string skill) => Reach(() => skill == Runwei
            ? P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } &&
              (G.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.UseProgramSkill && action.ProgramSkillId == Runwei) ||
               P!.Choices.All(c => c.Parameters.GetValueOrDefault("action") == "end-play"))
            : P is { } p && p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => Key(c) == "activate"));
        public void Activate()
        {
            if (P is { Kind: DecisionKind.PlayCard })
                Accept0(new UseProgramSkillCommand(0, Runwei, "reveal-grant", [], [], G.Revision, P.PromptId));
            else
                Accept0(new AnswerPromptCommand(P!.PlayerSeat, P.PromptId, P.Choices.First(c => Key(c) == "activate").Id, G.Revision));
        }
        public void Answer(Func<PromptChoice, bool> pick)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(pick); Need(choice is not null, "The expected native prompt choice must be published.");
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
                    if (p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Shuanghuai && SkipShuanghuaiOffers &&
                        p.Choices.Any(c => Key(c) == "skip"))
                    { Answer(c => Key(c) == "skip"); continue; }
                    if (p.PlayerSeat == 0 && p.SkillPrompt?.SkillId is not (Runwei or Shuanghuai) &&
                        p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0); continue; }
                    if (p.PlayerSeat == 0 && p.SkillPrompt?.SkillId is not (Runwei or Shuanghuai) && p.Choices.Any(c => Key(c) == "skip"))
                    { Answer(c => Key(c) == "skip"); continue; }
                    if (AutoEndPlay && p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard) { End(); continue; }
                    if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Shi Lu Yusheng boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded real scene must retain all participants.");
                Accept0(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Shi Lu Yusheng did not reach the expected bounded native boundary.");
        }
        public void AcceptPlay(GameCommand command) => Accept0(command);
        public void Replay()
        {
            var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry);
            Need(State() == before, "The short actual command journal reproduces the grants, guards and every state exactly.");
        }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept0(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Shi Lu Yusheng command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }),
            Grants = E<MobileShiRunweiGrantIssuedEvent>(), Refunds = E<MobileShiRunweiDebtRefundedEvent>(),
            PhaseEnds = E<MobileShiRunweiPhaseEndDiscardedEvent>(), Guards = E<MobileShiShuanghuaiGuardIssuedEvent>(),
            Last = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start(string nodeId)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] opening =
            [
                new(CardKind.Slash, Suit.Heart, 4), new(CardKind.Slash, Suit.Club, 5), new(CardKind.Slash, Suit.Spade, 6)
            ];
            ShanheStartingCard[] PeerHand(int seat) =>
                Enumerable.Range(0, nodeId == "shuanghuai" && seat == 1 ? 3 : 6).Select(i => new ShanheStartingCard(CardKind.Peach, Suit.Heart, 3 + i)).ToArray();
            ShanheCombatantDefinition Combatant(int seat) => new(Peer, Hp: nodeId == "shuanghuai" && seat == 1 ? 2 : 8,
                ExtraSkillIds: nodeId == "shuanghuai" && seat == 2 ? [Quiet, SlashOwner] : [Quiet]) { StartingHand = PeerHand(seat) };
            var setup = new ShanheBattleSetup(96, 1, nodeId, Hero, 3, 0, 0, 0, 0, [HandKeeper], opening, [],
                [Combatant(1), Combatant(2), Combatant(3)], [], new string('e', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 9 }, registry);
            var scene = new Scene(game, registry); scene.Accept0(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-lu-yu-sheng", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{HandKeeper}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":10,"priority":0}]},
              {"id":"{{SlashOwner}}","revision":1,"triggers":[{"id":"slash-least","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLivingLeastHandCount"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]}
            ]}
            """;
            var presentations = new Dictionary<string, object>
            {
                [Quiet] = new { name = Quiet, description = "安静真实回合并跳过普通摸牌出牌。" },
                [HandKeeper] = new { name = HandKeeper, description = "放宽弃牌阶段手牌上限。" },
                [SlashOwner] = new { name = SlashOwner, description = "回合开始对最少手牌角色使用虚拟杀。" }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静真实同桌", "", Quiet, "wei", 10));
            b.AddCard(new("fixture:shi-lu-yu-sheng-slash", "杀", "基本牌", "同体杀实体", CardKind.Slash));
            b.AddDeck(new(Deck, "固定真实双色杀牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 100)
                .Select(i => new ContentDeckPhysicalCard("fixture:shi-lu-yu-sheng-slash", i % 2 == 0 ? Suit.Heart : Suit.Club, 5)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势陆郁生润微霜怀实际检查", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
