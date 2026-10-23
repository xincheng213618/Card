using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiChengPuChecks
{
    private const string Hero = "mobile:shi-cheng-pu", Duzuo = "mobile:shi-duzuo", Bihan = "mobile:shi-bihan",
        Quiet = "fixture:shi-cheng-pu-quiet", Keeper = "fixture:shi-cheng-pu-keeper",
        Supplier = "fixture:shi-cheng-pu-supplier", SlashOwner = "fixture:shi-cheng-pu-slash-owner",
        Peer = "fixture:shi-cheng-pu-peer", Deck = "fixture:shi-cheng-pu-deck";

    public static void DuzuoGrantsFireSlashFromPilesAndExcludesOwnGrants()
    {
        var s = Scene.Start("duzuo");
        s.Offer(Duzuo); s.Activate();
        var peerHand = s.V(1).HandCount;
        s.Answer(c => Key(c) == "mobile-shi-duzuo" && c.Targets is [1]);
        s.Need(s.E<MobileShiDuzuoGrantIssuedEvent>() is [{ RecipientSeat: 1, FromDiscardPile: false, CardId: var granted }] &&
                s.Zone(granted) == CardLocation.Hand(1) && s.V(1).HandCount == peerHand + 1 &&
                s.V(1).Hand.Count(card => card.Id == granted && card.Kind == CardKind.FireSlash) == 1,
            "Accepting Duzuo after a normal draw grants one real fire Slash from the draw pile to the chosen player.");
        s.Play();
        s.End();
        s.Offer(Duzuo); s.Activate();
        var ownerHand = s.V(0).HandCount;
        s.Answer(c => Key(c) == "mobile-shi-duzuo" && c.Targets is [0]);
        s.Play();
        s.Need(s.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } &&
                s.E<MobileShiDuzuoGrantIssuedEvent>() is [{ RecipientSeat: 1 }, { RecipientSeat: 0, FromDiscardPile: false }] &&
                s.V(0).HandCount == ownerHand + 1,
            "Naming himself grants the owner the pile fire Slash, and reaching the play phase proves the own-reason gain never re-offers Duzuo.");
        s.Replay();
    }

    public static void BihanDiscardsToHpAndReducesSlashDamage()
    {
        var s = Scene.Start("bihan");
        s.Offer(Bihan); s.Activate();
        s.Answer(c => Key(c) == "mobile-shi-bihan" && c.Parameters.GetValueOrDefault("branch") == "discarder" && c.Targets is [2]);
        s.Reach(() => s.E<MobileShiBihanGuardCompletedEvent>().Length == 1);
        s.Need(s.E<MobileShiBihanGuardIssuedEvent>() is [{ DamageTargetSeat: 2, DiscarderSeat: 2, PlannedDiscardCount: 2 }] &&
                s.E<MobileShiBihanGuardCompletedEvent>() is [{ DiscarderSeat: 2, DiscardedCount: 2, DamageReduced: true }] &&
                s.V(2).Hp == 2 && s.V(2).HandCount == 2 && s.V(0).HandCount == 5,
            "Invoked on the slashed victim, Bihan makes the chosen discarder drop to hp cards and removes the single Slash damage.");
        s.Need(s.E<ProgramDamageReducedEvent>() is [{ AmountBefore: 1, AmountAfter: 0 }] &&
                s.G.CardMovements.Count(m => m.Reason.Value == "skill-program.mobile:shi-bihan.discard" && m.To == CardLocation.DiscardPile) == 2,
            "Both paid discards land in the discard pile and the finalized damage window records the exact one-point reduction.");
        s.Replay();
    }

    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Offer(string skill) => Reach(() => P is { } p && p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => Key(c) == "activate"));
        public void Activate() => Answer(c => Key(c) == "activate");
        public void Answer(Func<PromptChoice, bool> pick)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(pick); Need(choice is not null, "The expected native prompt choice must be published.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision));
        }
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 400; i++)
            {
                if (ready()) return;
                if (P is { } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.PlayerSeat == 0 && p.SkillPrompt?.SkillId is not (Duzuo or Bihan) &&
                        p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0); continue; }
                    if (p.PlayerSeat == 0 && p.SkillPrompt?.SkillId is not (Duzuo or Bihan) && p.Choices.Any(c => Key(c) == "skip"))
                    { Answer(c => Key(c) == "skip"); continue; }
                    if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Shi Cheng Pu boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded real scene must retain all participants.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Shi Cheng Pu did not reach the expected bounded native boundary.");
        }
        public void Replay()
        {
            var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry);
            Need(State() == before, "The short actual command journal reproduces the grants, guards and every state exactly.");
        }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Shi Cheng Pu command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }),
            Grants = E<MobileShiDuzuoGrantIssuedEvent>(), Guards = E<MobileShiBihanGuardIssuedEvent>(),
            Reduced = E<ProgramDamageReducedEvent>(),
            Last = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start(string nodeId)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] opening = nodeId switch
            {
                "duzuo" => [new(CardKind.Dodge, Suit.Spade, 4), new(CardKind.Dodge, Suit.Club, 5), new(CardKind.Dodge, Suit.Heart, 6)],
                _ => [new(CardKind.Dodge, Suit.Spade, 4), new(CardKind.Dodge, Suit.Club, 5), new(CardKind.Dodge, Suit.Heart, 6),
                    new(CardKind.Dodge, Suit.Diamond, 7), new(CardKind.Dodge, Suit.Spade, 8)]
            };
            ShanheStartingCard[] PeerHand(int seat) =>
                Enumerable.Range(0, nodeId == "bihan" && seat == 2 ? 4 : 6).Select(i => new ShanheStartingCard(CardKind.Peach, Suit.Heart, 3 + i)).ToArray();
            ShanheCombatantDefinition Combatant(int seat) => new(Peer, Hp: nodeId == "bihan" && seat == 2 ? 2 : 8,
                ExtraSkillIds: nodeId == "bihan"
                    ? seat == 1 ? [Quiet, SlashOwner] : [Quiet]
                    : [Quiet]) { StartingHand = PeerHand(seat) };
            var setup = new ShanheBattleSetup(92, 1, nodeId, Hero, 3, 0, 0, 0, 0, nodeId == "bihan" ? [Keeper] : [Supplier], opening, [],
                [Combatant(1), Combatant(2), Combatant(3)], [], new string('e', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 9 }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-cheng-pu", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Keeper}}","revision":1,
                "modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":10,"priority":0}],
                "triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{SlashOwner}}","revision":1,"triggers":[{"id":"slash-least","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLivingLeastHandCount"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]},
              {"id":"{{Supplier}}","revision":1,"triggers":[{"id":"opening-draw","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"draw","target":"owner","amount":1}]}]}
            ]}
            """;
            var presentations = new Dictionary<string, object>
            {
                [Quiet] = new { name = Quiet, description = "保留真实回合并跳过普通摸牌出牌。" },
                [Keeper] = new { name = Keeper, description = "安静真实回合且放宽弃牌阶段手牌上限。" },
                [Supplier] = new { name = Supplier, description = "回合开始摸一张牌，制造非技能获得。" },
                [SlashOwner] = new { name = SlashOwner, description = "回合开始对最少手牌角色使用虚拟杀。" }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静真实同桌", "", Quiet, "wei", 10));
            b.AddCard(new("fixture:shi-cheng-pu-fire-slash", "火杀", "基本牌", "同点火杀实体", CardKind.FireSlash));
            b.AddDeck(new(Deck, "固定真实火杀牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("fixture:shi-cheng-pu-fire-slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势程普督佐蔽扞实际检查", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
