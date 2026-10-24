using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiSunJunChecks
{
    private const string Hero = "mobile:shi-sun-jun", Xiongtu = "mobile:shi-xiongtu", Quiet = "fixture:shi-sun-jun-quiet",
        Driver = "fixture:shi-sun-jun-driver", Watch = "fixture:shi-sun-jun-watch", Peer = "fixture:shi-sun-jun-peer", Deck = "fixture:shi-sun-jun-deck";
    public static void XiongtuFrozenSuitCostOtherDamageAndPhaseCapCold()
    {
        var s = Scene.Start(); s.Play();
        s.Skill(1);
        s.Need(s.P is { PlayerSeat: 0, IsPrivate: true } && s.P.Choices.All(c => c.Cards.Count == 0) && s.G.CreateSnapshot(2).PendingDecision is null,
            "Before the reveal the chooser receives opaque real Hand slots, and another observer receives no private selection.");
        s.Answer(c => Branch(c) == "hand");
        var revealed = s.E<ShiSunJunRevealedEvent>().Single().CardId;
        s.Need(s.E<ProgramCardsRevealedEvent>().Any(e => e.Cards.Select(c => c.Id).SequenceEqual([revealed])) &&
            s.G.CreateSnapshot(2).PublicRevealedCards?.Any(c => c.Id == revealed) == true,
            "Selecting one opaque slot publicly reveals precisely the original foreign Hand entity.");
        s.Cold(); s.Answer(c => Branch(c) == "damage");
        foreach (var suit in new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond })
        {
            var id = s.V(0).Hand.First(c => c.Suit == suit).Id;
            s.Answer(c => Branch(c) == "cost" && c.Cards.SequenceEqual([id]));
        }
        s.AtWatch();
        s.Need(s.E<ShiSunJunCostPaidEvent>() is [{ Count: 4 }] && s.E<TurnDiscardSuitMaskChangedEvent>().Last().SuitMask == 15 &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Xiongtu).ShiSunJun is { Stage: ShiSunJunStage.CostChildren, Required: 4 },
            "The selected branch freezes X=4 before one actual four-suit logical discard, and retains the paid parent while its child is pending.");
        s.Cold(); s.ContinueWatch(); s.Play();
        s.Need(s.V(1).Hp == 11 && s.E<ShiSunJunBenefitIssuedEvent>().Length == 0 && s.E<ShiSunJunCostPaidEvent>().Length == 1 &&
            s.G.GetHumanLegalActions().All(a => a.ProgramSkillId != Xiongtu),
            "Xiongtu's own real damage neither rewards itself nor raises the initial one-use phase cap, and cold recovery pays the cost once.");
        s.Use(CardKind.Slash, Suit.Heart); s.AtWatch();
        s.Need(s.E<ShiSunJunDrawReturnedEvent>() is [{ ActualCount: 1 }] && s.V(1).Hp == 10,
            "A later genuine different-source damage draws exactly one real card before its paid draw child finishes.");
        s.Cold(); s.ContinueWatch(); s.Play(); s.Skill(1); s.Answer(c => Branch(c) == "hand");
        s.Need(s.P!.Choices.Any(c => Branch(c) == "damage" && c.Description.Contains("0 张", StringComparison.Ordinal)),
            "After all four suits genuinely entered the discard pile, the next activation offers a zero-card damage branch.");
        var second = s.E<ShiSunJunRevealedEvent>().Last().CardId;
        s.Answer(c => Branch(c) == "discard"); s.Play();
        s.Need(s.G.CardMovements.Any(m => m.CardId == second && m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill.mobile-shi-sun-jun.discard"),
            "The other branch discards the same publicly revealed original foreign Hand entity.");
        s.Use(CardKind.Slash, Suit.Club); s.AtWatch(); s.ContinueWatch(); s.Play();
        s.Need(s.E<ShiSunJunBenefitIssuedEvent>().Length == 2 && s.E<ShiSunJunDrawReturnedEvent>().Length == 2 &&
            s.G.GetHumanLegalActions().All(a => a.ProgramSkillId != Xiongtu),
            "The listener is not duplicated by the second activation: each other damage draws once, and repeated damage keeps the actual phase cap at two.");
        s.Cold(); s.Replay();
    }
    public static void XianshuaiFirstSuitSlashAndAlcoholKeepOrdinaryQuota()
    {
        var s = Scene.Start(wine: true); s.Play();
        s.Use(CardKind.Alcohol, Suit.Heart); s.Play();
        s.Need(s.V(0).HasAlcoholEffect && s.G.GetHumanLegalActions().All(a => a.Kind != LegalActionKind.Alcohol),
            "The first Heart hand use is exempt, while an existing Alcohol effect still cannot stack.");
        s.Use(CardKind.Slash, Suit.Spade); s.Play();
        s.Use(CardKind.Alcohol, Suit.Heart); s.Play(); s.Use(CardKind.Slash, Suit.Heart); s.Play();
        s.Need(s.E<CardUseDebitRecordedEvent>().Count(e => e.Debit.Query == SkillRuleQuery.SlashLimit) == 1 &&
            s.E<ShiSunJunHandUseEvent>().Where(e => e.Suit == Suit.Heart).Select(e => e.Exempt).SequenceEqual([true, false, false]),
            "Using a Heart Alcohol consumes that suit's first opportunity; another Heart Alcohol and the later Heart Slash use their ordinary quotas.");
        s.Need(s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Alcohol && s.V(0).Hand.Any(c => c.Id == a.CardId && c.Suit == Suit.Club)),
            "A genuinely unused Club hand Alcohol remains legal after the ordinary Alcohol quota was spent.");
        s.Cold(); s.Use(CardKind.Alcohol, Suit.Club); s.Play(); s.Use(CardKind.Slash, Suit.Diamond); s.Play();
        s.Need(s.E<DamageAppliedEvent>().Where(e => e.TargetSeat == 1).Select(e => e.Amount).SequenceEqual([2, 2, 2]) &&
            s.E<CardUseDebitRecordedEvent>().Count(e => e.Debit.Query == SkillRuleQuery.SlashLimit) == 1 &&
            s.G.GetHumanLegalActions().All(a => a.Kind != LegalActionKind.Slash && a.Kind != LegalActionKind.Alcohol),
            "Each Alcohol is consumed by a genuine Slash; only each suit's first hand use is exempt, so a remaining same-Club Slash or Alcohol cannot exceed ordinary quotas.");
        s.Cold(); s.Replay();
    }
    private static string? Branch(PromptChoice c) => c.Parameters.GetValueOrDefault("branch");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message + " " + Dump()); }
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(choice is not null, "The expected native prompt choice must exist."); Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision)); }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Skill(int target) => Submit(new UseProgramSkillCommand(0, Xiongtu, "show-hand", [], [target], G.Revision, P!.PromptId));
        public void AtWatch() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
        public void ContinueWatch() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Use(CardKind kind, Suit suit)
        {
            var hand = V(0).Hand.Where(c => c.Kind == kind && c.Suit == suit).Select(c => c.Id).ToHashSet();
            var action = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is { } id && hand.Contains(id) && (kind == CardKind.Slash ? a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]) : a.Kind == LegalActionKind.Alcohol));
            Need(action is not null, $"The exact real {suit} {kind} must be legally usable.");
            Submit(new PlayCardCommand(0, action!.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind));
        }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 600; i++)
            {
                if (ready()) return;
                if (P is { } p && p.PlayerSeat == 0)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                    throw new InvalidOperationException("Unexpected Shi Sun Jun native boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded scene must retain all participants."); Submit(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The actual scene did not reach its native boundary.");
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold restore preserves exact native state, frozen costs and original paid children."); }
        public void Replay()
        { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "The actual command journal reproduces every skill fact, physical movement and player view."); }
        private void Submit(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Shi Sun Jun command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }),
            Starts = E<ShiSunJunStartedEvent>(), Costs = E<ShiSunJunCostPaidEvent>(), Benefits = E<ShiSunJunBenefitIssuedEvent>(), Last = G.Events.TakeLast(5).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start(bool wine = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] hand = wine ?
                [new(CardKind.Alcohol, Suit.Heart, 1), new(CardKind.Slash, Suit.Spade, 2), new(CardKind.Alcohol, Suit.Heart, 3), new(CardKind.Slash, Suit.Heart, 4),
                 new(CardKind.Alcohol, Suit.Club, 5), new(CardKind.Slash, Suit.Diamond, 6), new(CardKind.Slash, Suit.Club, 7), new(CardKind.Alcohol, Suit.Club, 8)] :
                [new(CardKind.Slash, Suit.Spade, 1), new(CardKind.Slash, Suit.Heart, 2), new(CardKind.Slash, Suit.Club, 3), new(CardKind.Slash, Suit.Diamond, 4),
                 new(CardKind.Slash, Suit.Heart, 5), new(CardKind.Slash, Suit.Club, 6), new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Slash, Suit.Diamond, 8)];
            ShanheCombatantDefinition PeerAt(int seat) => new(Peer, Hp: 12, ExtraSkillIds: [Quiet])
                { StartingHand = Enumerable.Range(0, 8).Select(i => new ShanheStartingCard(CardKind.Peach, Suit.Heart, i + 1)).ToArray() };
            var setup = new ShanheBattleSetup(96, 1, wine ? "wine" : "xiongtu", Hero, 3, 0, 0, 0, 0, [Driver, Watch], hand, [], [PeerAt(1), PeerAt(2), PeerAt(3)], [], new string('f', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 5 }, registry);
            var s = new Scene(g, registry); s.Submit(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-sun-jun", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":12,"priority":0}],
                "triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"priority":0,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[
                {"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"priority":0,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},
                {"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"priority":0,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"paid-discard","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.mobile-shi-sun-jun.discard"],"optional":false,"priority":0,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]},
                {"id":"paid-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.mobile-shi-sun-jun.draw"],"optional":false,"priority":0,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var presentation = new Dictionary<string, object> {
                [Driver] = new { name = Driver, description = "固定真实手牌与正常次数，不摸普通牌。" }, [Quiet] = new { name = Quiet, description = "跳过普通摸牌和出牌。" },
                [Watch] = new { name = "原生已支付子结算", description = "停在真实批次支付后。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentation }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静真实同桌", "", Quiet, "wei", 12));
            b.AddCard(new("fixture:shi-sun-jun-slash", "杀", "基本牌", "真实固定杀牌", CardKind.Slash));
            b.AddDeck(new(Deck, "固定实体杀牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard("fixture:shi-sun-jun-slash", Suit.Spade, 5)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势孙峻真实结算检查", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
