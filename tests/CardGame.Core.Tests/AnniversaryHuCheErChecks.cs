using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class AnniversaryHuCheErChecks
{
    private const string Hero = "anniversary:hu-che-er", Daoji = "anniversary:daoji", Fuzhong = "anniversary:fuzhong", Driver = "fixture:hu-che-er-driver", Quiet = "fixture:hu-che-er-quiet", NoDraw = "fixture:hu-che-er-no-draw", Watch = "fixture:hu-che-er-watch", Feed = "fixture:hu-che-er-feed", Deck = "fixture:hu-che-er-deck", Mode = "fixture:hu-che-er-human-mode", Hand0 = "fixture:hu-che-er-hand-0", Hand2 = "fixture:hu-che-er-hand-2";
    private static string Peer(int seat) => "fixture:hu-che-er-peer-" + seat;

    public static void FirstActualWeaponClaimAndTurnUsePlayedBanCold()
    {
        var s = Scene.Start(); s.Play();
        var spear = s.V(0).Hand!.Single(c => c.Kind == CardKind.ZhangbaSerpentSpear).Id;
        var ownWeapon = s.V(0).Hand!.First(c => c.Kind == CardKind.Crossbow).Id;
        var forbiddenSlash = s.V(2).Hand!.Single(c => c.Kind == CardKind.Slash).Id;
        s.Driver("place", [1]); s.Answer(c => c.Cards.SequenceEqual([spear])); s.Play();
        s.Need(s.Zone(spear) == CardLocation.Equipment(1) && !s.E<CardUseDeclaredEvent>().Any(e => e.CardId == spear) && s.E<AnniversaryHuCheErStartedEvent>().Length == 0,
            "A real direct equipment gift neither declares a Use nor consumes that actor's first Weapon Use opportunity.");
        s.Equip(ownWeapon); s.Play();
        s.Need(s.Zone(ownWeapon) == CardLocation.Equipment(0) && s.E<AnniversaryHuCheErStartedEvent>().Length == 0,
            "Hu Che Er's own real Weapon Use never triggers his Daoji.");
        s.Driver("equip", [1]); s.Offer(); s.Activate(); s.Menu();
        var root = s.Root!.Id; var useId = s.Root.AnniversaryHuCheEr!.UseId;
        var weapon = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == useId).Action!.PhysicalCards.Single().CardId;
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "obtain"); s.Watch();
        s.Need(s.Zone(weapon) == CardLocation.Hand(0) && s.Zone(spear) == CardLocation.Equipment(1) &&
            s.Root!.AnniversaryHuCheEr is { Stage: AnniversaryHuCheErStage.MovementChildren, Materials.Count: 1, Gain.Movements.Count: 1 } &&
            s.E<AnniversaryHuCheErWeaponClaimedEvent>().Single(e => e.FrameId == root) is { Count: 1 },
            "Daoji pays the exact original Weapon entity once before its native movement child pauses, preserving the old installed weapon.");
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.Zone(weapon) == CardLocation.Hand(0) && s.Zone(spear) == CardLocation.Equipment(1) && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == useId) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == weapon && m.Reason.Value == "skill-program.anniversary:daoji.obtain") == 1 && s.Zhong == 0,
            "The restored native Use finishes without installing the claimed original, replacing the old weapon, repeating payment or counting an own-turn gain as Fuzhong.");
        s.Driver("equip", [1]); s.Play();
        s.Need(s.E<AnniversaryHuCheErStartedEvent>().Length == 1 && s.Zone(spear) == CardLocation.DiscardPile && s.V(1).Equipment.Any(c => c.Kind == CardKind.Crossbow),
            "That same actor's second actual Weapon Use equips normally and cannot reopen the consumed first opportunity.");
        s.Need(s.G.GetAttackRange(2) >= s.G.GetCombatDistance(2, 0), "The future requested-Slash user already covers the real opposite target before Daoji issues any ban.");
        s.Driver("equip", [2]); s.Offer(); s.Activate(); s.Menu(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "forbid"); s.Play();
        s.Need(s.E<AnniversaryHuCheErStartedEvent>().Length == 2 && s.E<AnniversaryHuCheErSlashForbiddenEvent>() is [{ Actor: 2, Turn: 1 }] && s.V(2).Equipment.Any(c => c.Kind == CardKind.Crossbow),
            "A different actor has an independent first opportunity; choosing the real turn ban lets its original weapon equip.");
        var hp = s.V(0).Hp;
        var victimHp = s.V(2).Hp;
        s.Driver("request", [2]); s.Play();
        s.Need(s.G.GetAttackRange(2) >= s.G.GetCombatDistance(2, 0) && s.V(0).Hp == hp && s.Zone(forbiddenSlash) == CardLocation.Hand(2) && !s.E<CardUseDeclaredEvent>().Any(e => e.SourceSeat == 2 && e.CardKind == CardKind.Slash),
            "The existing native requested-Slash Use honors Daoji before issuing or paying the forbidden entity.");
        s.Need(s.V(2).IsHuman && !s.NativeSlashResponseIds(2).Contains(forbiddenSlash), "The actually human banned Duel responder has no legal native response entity for its exact printed Slash.");
        s.Driver("duel", [0, 2]); s.Play();
        s.Need(s.V(2).Hp == victimHp - 1 && s.Zone(forbiddenSlash) == CardLocation.Hand(2) && !s.E<CardRespondedEvent>().Any(e => e.CardId == forbiddenSlash),
            "The same ban also blocks a genuine pure Played Duel response, so the still-living native opponent takes the actual damage.");
        s.End(); s.Play();
        s.Need(s.G.State.TurnNumber == 5, "The fixed quiet table reaches the next real own turn.");
        s.Driver("duel", [0, 2]); s.Reach(() => s.P is { PlayerSeat: 2, Kind: DecisionKind.RespondSlash });
        s.Need(s.V(2).IsHuman && s.P!.Choices.Any(c => c.Cards.SequenceEqual([forbiddenSlash])), "The exact native Slash returns as a legal pure Played choice for the real human after the issuing turn expires.");
        s.Answer(c => c.Cards.SequenceEqual([forbiddenSlash])); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash }); s.Answer(c => c.Cards.Count == 0); s.Play();
        s.Need(s.E<CardRespondedEvent>().Count(e => e.CardId == forbiddenSlash && e.ResponderSeat == 2) == 1 && s.Zone(forbiddenSlash) == CardLocation.DiscardPile && s.V(0).Hp == hp - 1 && s.V(2).Hp == victimHp - 1,
            "After the issuing actual turn ends, that exact Slash may really be Played once; Hu Che Er declines the native counter-response and takes the subsequent Duel damage.");
        s.Replay();
    }

    public static void ActualForeignGainBatchesAllTiersAndEndingDamageKeepsRemainder()
    {
        var s = Scene.Start(feed: true); s.Play();
        s.Need(s.Zhong == 0 && s.HandLimit() == 24 && s.G.GetCombatDistance(0, 2) == 2,
            "The actual own normal draw grants no heavy mark and starts without any tier bonus.");
        s.End(); s.Feed();
        s.Need(s.G.State.CurrentSeat == 1 && s.Zhong == 1 && s.E<AnniversaryHuCheErGainRecordedEvent>() is [{ Before: 0, After: 1 }] && s.E<CardsDrawnEvent>().Last(e => e.PlayerSeat == 0).Count == 2,
            "A real two-card draw during the other actor's actual turn produces one mark for its complete gain batch.");
        s.Continue(); s.Feed();
        s.Need(s.Zhong == 2 && s.G.GetCombatDistance(0, 2) == 1 && s.G.GetCombatDistance(2, 0) == 2 && s.G.GetCombatDistance(0, 0) == 0 && s.HandLimit() == 24,
            "At two real batches only outgoing distance falls by two with the native floor; incoming/self distance and the three-mark hand tier remain correct.");
        s.Continue(); s.Feed();
        s.Need(s.Zhong == 3 && s.HandLimit() == 27, "The third separate original gain batch enables exactly three additional hand limit.");
        s.Continue(); s.Feed(); s.Continue(); s.Feed();
        s.Need(s.Zhong == 5 && s.E<AnniversaryHuCheErGainRecordedEvent>().Select(e => (e.Before, e.After)).SequenceEqual([(0, 1), (1, 2), (2, 3), (3, 4), (4, 5)]),
            "Five real native gain batches accumulate five marks, preserving the excess above the end-phase threshold.");
        var draws = s.E<CardsDrawnEvent>().Count(e => e.PlayerSeat == 0);
        s.Continue(); s.Play();
        s.Need(s.G.State.TurnNumber == 5 && s.Zhong == 5 && s.E<AnniversaryHuCheErGainRecordedEvent>().Length == 5 && s.E<CardsDrawnEvent>().Where(e => e.PlayerSeat == 0).Skip(draws).Select(e => e.Count).SequenceEqual([3]),
            "The next actual own draw phase really draws normal two plus one, and those own-turn cards add no Fuzhong batch.");
        s.End(); s.Reach(() => s.P?.SkillPrompt?.SkillId == Fuzhong && s.P.Choices.Any(c => c.Targets.SequenceEqual([2])));
        s.Answer(c => c.Targets.SequenceEqual([2])); s.Reach(() => s.E<AnniversaryHuCheErFourRemovedEvent>().Length == 1);
        var removed = s.E<AnniversaryHuCheErFourRemovedEvent>().Single();
        var history = s.G.Events.Select(e => e.Payload).ToArray();
        s.Need(removed is { Before: 5, After: 1, Turn: 5 } && s.Zhong == 1 && s.V(2).Hp == 7 &&
            Array.FindLastIndex(history, e => e is AfterDamageEvent { SourceSeat: 0, TargetSeat: 2, Amount: 1 }) < Array.FindIndex(history, e => e is AnniversaryHuCheErFourRemovedEvent) &&
            s.HandLimit() == 24 && s.G.GetCombatDistance(0, 2) == 2,
            "The real ending native damage and its after-damage return precede removal of exactly four; the fifth remains while the distance and hand tiers turn off.");
        s.Play();
        s.Need(s.E<AnniversaryHuCheErFourRemovedEvent>().Length == 1 && s.Zhong == 1 && s.E<CardsDrawnEvent>().Last(e => e.PlayerSeat == 0).Count == 3,
            "The remaining single mark retains its actual draw bonus across the next round without another four-mark payment.");
        s.Replay();
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryHuCheEr is not null);
        public int Zhong => V(0).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.AnniversaryHuCheErZhong)?.Count ?? 0;
        private CharacterState Player(int seat) => ((IEnumerable<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(G)!).Single(p => p.Seat == seat);
        public int HandLimit() => (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(G, [Player(0)])!;
        public int[] NativeSlashResponseIds(int seat) => ((IEnumerable<Card>)typeof(GameEngine).GetMethod("GetResponseCards", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(G, [Player(seat), CardKind.Slash])!).Select(c => c.Id).ToArray();
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Offer() => Reach(() => P?.SkillPrompt?.SkillId == Daoji && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Menu() => Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-hu-che-er") == true);
        public void Watch() => Reach(() => P?.SkillPrompt?.SkillId == AnniversaryHuCheErChecks.Watch);
        public void Feed() => Reach(() => P?.SkillPrompt?.SkillId == AnniversaryHuCheErChecks.Feed);
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Driver(string binding, int[] targets) => Accept(new UseProgramSkillCommand(0, AnniversaryHuCheErChecks.Driver, binding, [], targets, G.Revision, P!.PromptId));
        public void Equip(int card)
        {
            var a = G.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.Equip && a.CardId == card);
            Accept(new PlayCardCommand(0, card, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
        }
        public void Answer(Func<PromptChoice, bool> match)
        { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(c is not null, "The fixed native original published choice exists."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Accept(GameCommand c)
        { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected real Hu Che Er command."); }
        public void Reach(Func<bool> ready, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 200; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification) { Answer(c => c.Cards.Count == 0); continue; }
                    throw new InvalidOperationException("Unexpected native Hu Che Er prompt at " + line + Dump());
                }
                Need(G.State.Winner == Winner.None, "The fixed short table retains its native participants."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The original native Hu Che Er flow did not reach " + line + Dump());
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold preserves the original first Weapon Use, exact already-paid entity and owning movement child."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(before == State(), "First Weapon opportunities, native bans, actual gain batches, dynamic tiers and four-marker payment replay exactly."); }
        public void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, P, Root = Root?.AnniversaryHuCheEr, Zhong, Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount, V(i).HandLimit }), Last = G.Events.TakeLast(10).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start(bool feed = false)
        {
            if (!feed) return StartHumanWeapon();
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var skills = new List<string> { AnniversaryHuCheErChecks.Driver, AnniversaryHuCheErChecks.Watch }; if (feed) skills.Add(AnniversaryHuCheErChecks.Feed);
            var setup = new ShanheBattleSetup(17, 1, "hu-che-er-actual-native", Hero, 4, 0, 0, 0, 0, skills,
                [new(CardKind.ZhangbaSerpentSpear, Suit.Spade, 12), new(CardKind.Slash, Suit.Heart, 7)], [],
                [new(Peer(2), Hp: 8) { StartingHand = [new(CardKind.Slash, Suit.Club, 9)] }, new(Peer(3), Hp: 8) { StartingHand = [] }],
                [new(Peer(1), Hp: 8) { StartingHand = [] }], new string('a', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 14 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
        private static Scene StartHumanWeapon()
        {
            var stock = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, 1)).ToArray();
            ShanheStartingCard[][] hands = [[new(CardKind.ZhangbaSerpentSpear, Suit.Spade, 12), new(CardKind.Slash, Suit.Heart, 7), new(CardKind.Crossbow, Suit.Club, 1), new(CardKind.Crossbow, Suit.Club, 1)], [], [new(CardKind.Slash, Suit.Club, 9)], []];
            var probe = Boot(ContentRegistry.Build(new StandardContentPackage(), new Fixture(true, stock)));
            for (var seat = 0; seat < 4; seat++)
            {
                var ids = probe.V(seat).Hand!.Select(c => c.Id).ToArray(); probe.Need(ids.Length == hands[seat].Length, "The fixed native identity deal and first normal draw retain their intended sparse hands.");
                for (var i = 0; i < ids.Length; i++) stock[ids[i] - 1] = new(probe.registry.Cards.Values.First(c => c.LegacyKind == hands[seat][i].Kind).Id, hands[seat][i].Suit, hands[seat][i].Rank);
            }
            var actual = Boot(ContentRegistry.Build(new StandardContentPackage(), new Fixture(true, stock)));
            actual.Need(actual.V(0).IsHuman && actual.V(2).IsHuman, "Both original Daoji owner and native Duel responder are genuinely human-controlled participants.");
            return actual;
        }
        private readonly ContentRegistry registry = registry;
        private static Scene Boot(ContentRegistry registry)
        {
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 47, PlayerCount = 4, HumanSeat = 0, HumanSeats = [0, 1, 2, 3], HumanRole = Role.Lord, AiPolicyVersion = AiDecisionPolicy.CurrentVersion,
                ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 14 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand());
            for (var n = 0; n < 40; n++)
            {
                if (s.P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return s;
                if (s.P is { Kind: DecisionKind.SelectGeneral } p) { s.Accept(new SelectGeneralCommand(p.PlayerSeat, p.PlayerSeat == 0 ? Hero : Peer(p.PlayerSeat), g.Revision, p.PromptId)); continue; }
                if (s.P is { Kind: DecisionKind.SelectFaction }) { s.Answer(_ => true); continue; }
                s.Need(s.P is null, "The fixed native identity opening retains only actual setup and deal boundaries."); s.Accept(new AdvanceOneStepCommand(g.Revision));
            }
            throw new InvalidOperationException("The fixed identity opening missed its first actual Play phase.");
        }
    }
    private sealed class Fixture(bool identity = false, IReadOnlyList<ContentDeckPhysicalCard>? stock = null) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-hu-che-er", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(identity ? new OwnerBuilder(b) : b, Hero);
            b.AddCard(StandardContentRegistry.CreateWithClassicGenerals().GetCard("classic:zhangba-serpent-spear"));
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"modifiers":[{"id":"real-request-range","query":"attackRange","operation":"add","value":1,"priority":0}],"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"none","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"preserve-originals","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[
                {"id":"place","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"cardCategories":["equipment"],"cardKinds":["zhangbaSerpentSpear"],"count":1,"destination":"selectedTargetEquipment","targetRef":{"kind":"selectedTarget"},"awaitMovementTriggers":true}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"weapon"}]},
                {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"request"}]},
                {"id":"duel","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startVirtualDuel","target":"owner"}]}
              ]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-original","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:daoji.obtain"],"priority":1000,"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Feed}}","revision":1,"triggers":[{"id":"five-real-batches","window":"turnStartBeforeNormalFlow","subject":"owner","turnOwnerScope":"otherLiving","usageScope":"game","usageLimit":1,"optional":false,"isSkillInvocation":false,"effects":[
                {"op":"draw","target":"owner","amount":2},{"op":"chooseOption","target":"owner","resultBind":"one","options":[{"id":"continue"}]},
                {"op":"draw","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"two","options":[{"id":"continue"}]},
                {"op":"draw","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"three","options":[{"id":"continue"}]},
                {"op":"draw","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"four","options":[{"id":"continue"}]},
                {"op":"draw","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"five","options":[{"id":"continue"}]}
              ]}]}
            ]}
            """;
            var names = new Dictionary<string, object>();
            foreach (var id in new[] { Quiet, NoDraw, Driver }) names[id] = new { name = "胡车儿固定原生输入", description = "保留真实使用、直接置入、请求和纯打出。" };
            foreach (var id in new[] { Watch, Feed }) names[id] = new { name = "胡车儿原生暂停", description = "逐批真实移动及已付获取子窗。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var parsed = JsonNode.Parse(rules)!;
            if (identity)
            {
                foreach (var (id, count) in new[] { (Hand0, 2), (Hand2, 1) })
                {
                    parsed["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{id}}","revision":1,"modifiers":[{"id":"sparse-opening","query":"initialHandSize","operation":"add","value":{{count}},"priority":0}]}"""));
                    names[id] = new { name = "固定真实发牌", description = "保留真实洗牌发牌及人类响应。" };
                }
            }
            var catalog = SkillProgramCatalog.Load(parsed.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            for (var i = 1; i < 4; i++) b.AddGeneral(new(Peer(i), "固定安静同桌", "", Quiet, "wei", 8, AdditionalSkillIds: identity && i == 2 ? [NoDraw, Hand2] : [NoDraw]));
            b.AddDeck(new(Deck, "固定真实武器库存", 0, 2, []) { PhysicalCards = stock ?? Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, 1)).ToArray() });
            if (identity) b.AddMode(new(Mode, "胡车儿真实人类武器与响应", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, Deck, GeneralCandidateCount: 4, GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)]));
            else b.AddMode(new(ShanheBattleSetup.BattleModeId, "胡车儿真实逐批重", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b) : IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d) => b.AddGeneral(d.Id == Hero ? d with { AdditionalSkillIds = [.. d.AdditionalSkillIds ?? [], Driver, Watch, Hand0] } : d);
        public void AddSkill(ContentSkillDefinition d) => b.AddSkill(d); public void AddCard(ContentCardDefinition d) => b.AddCard(d); public void AddDeck(ContentDeckRecipe d) => b.AddDeck(d); public void AddMode(ContentModeDefinition d) => b.AddMode(d);
    }
}
