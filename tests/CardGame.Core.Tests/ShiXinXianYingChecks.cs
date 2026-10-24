using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
internal static class ShiXinXianYingChecks
{
    private const string Hero = "mobile:shi-xin-xian-ying", Jiejie = "mobile:shi-jiejie", Qingshi = "mobile:shi-qingshi",
        Driver = "fixture:shi-xxy-driver", Watch = "fixture:shi-xxy-watch", Peer = "fixture:shi-xxy-peer", Quiet = "fixture:shi-xxy-quiet", Deck = "fixture:shi-xxy-deck";
    public static void OwnPrivateViewAtomicDiscardSuitQuotaAndSelfQingshiCold()
    {
        var s = Scene.Start(); s.Play();
        var before = s.V(0).HandCount; var hand = s.V(0).Hand.ToArray();
        s.ActivateJiejie();
        s.Need(s.P is { PlayerSeat: 0, IsPrivate: true } && s.G.CreateSnapshot(1).PendingDecision is null &&
            s.G.CreateSnapshot(0).PrivateRevealedCards!.Select(c => c.Id).Order().SequenceEqual(hand.Select(c => c.Id).Order()) &&
            s.G.CreateSnapshot(1).PrivateRevealedCards?.Any(c => hand.Any(h => h.Id == c.Id)) != true,
            "Only the actual skill owner receives the complete private real hand view and suit choices.");
        s.Cold(); s.Answer(c => Branch(c) == "Spade"); s.AtWatch();
        var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Jiejie);
        var paid = s.G.CardMovements.Where(m => m.Reason.Value == "skill-program.mobile-shi-jiejie.discard").ToArray();
        s.Need(root.ShiXinXianYing?.Stage == ShiXinXianYingStage.JiejieChildren && paid.Length == 2 &&
            paid.Select(m => m.CardId).Order().SequenceEqual(hand.Where(c => c.Suit != Suit.Spade).Select(c => c.Id).Order()) &&
            s.G.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single().Batch.Movements.Count == 2 &&
            s.E<ShiXinXianYingQingshiIssuedEvent>().Length == 0,
            "Jiejie discards the whole nonmatching hand group in one native logical batch, before its paid children or automatic Qingshi.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Play();
        s.Need(s.V(0).HandCount == before && s.E<ShiXinXianYingSuitAllowanceEvent>() is [{ Actor: 0, Suit: Suit.Spade }] &&
            s.E<ShiXinXianYingQingshiIssuedEvent>() is [{ Target: 0, FromJiejie: true, SameCamp: true }] &&
            s.E<ShiXinXianYingDrawReturnedEvent>() is [{ Recipient: 0, Actual: 2 }],
            "The uniquely leading first view issues one Qingshi; choosing oneself draws exactly two actual cards after the cost child.");
        var physical = hand.Where(c => c.Kind == CardKind.Slash).Select(c => c.Id).ToArray();
        var spadeAlcohol = hand.Where(c => c.Kind == CardKind.Alcohol && c.Suit == Suit.Spade).Select(c => c.Id).ToArray();
        var heartAlcohol = s.V(0).Hand.Where(c => c.Kind == CardKind.Alcohol && c.Suit == Suit.Heart).Select(c => c.Id).ToArray();
        s.UseAlcohol(spadeAlcohol[0]); s.Play();
        s.Slash(physical[0], 1); s.Play();
        s.Need(heartAlcohol.Length == 2 && s.G.GetHumanLegalActions().Any(a => a.CardId == heartAlcohol[0] && a.Kind == LegalActionKind.Alcohol),
            "The selected-suit Alcohol does not consume the ordinary Alcohol quota.");
        s.UseAlcohol(heartAlcohol[0]); s.Play();
        s.Need(s.G.GetHumanLegalActions().Any(a => a.CardId == physical[1] && a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1])) &&
            !s.G.GetHumanLegalActions().Any(a => a.CardId == physical[1] && a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])),
            "The granted suit permits another real Slash while retaining the native distance restriction.");
        s.Slash(physical[1], 1); s.Play();
        s.Need(!s.G.GetHumanLegalActions().Any(a => a.CardId == heartAlcohol[1] && a.Kind == LegalActionKind.Alcohol) &&
            s.G.GetHumanLegalActions().Any(a => a.CardId == spadeAlcohol[1] && a.Kind == LegalActionKind.Alcohol),
            "After an ordinary other-suit Alcohol, only the granted suit continues past the real quota.");
        s.UseAlcohol(spadeAlcohol[1]); s.Play(); s.Slash(physical[2], 1); s.Play(); s.Cold();
        s.Need(s.G.GetHumanLegalActions().All(a => a.ProgramSkillId != Jiejie), "The actual own Play phase consumes its single Jiejie activation.");
        s.Replay();
    }
    public static void SharedActivatorPrivateViewRealMissingSuitAndActualCampDiscardCold()
    {
        var s = Scene.Start(shared: true); s.Play();
        var actorHand = s.V(0).Hand.ToArray(); var count = s.V(0).HandCount;
        s.ActivateJiejie();
        s.Need(s.P is { PlayerSeat: 1, IsPrivate: true } && s.G.CreateSnapshot(0).PendingDecision is null &&
            s.G.CreateSnapshot(1).PrivateRevealedCards!.Select(c => c.Id).Order().SequenceEqual(actorHand.Select(c => c.Id).Order()),
            "The foreign actor authorizes a real shared activation, while only its exact Xin Xian Ying owner watches that hand.");
        s.Cold(); s.Answer(c => Branch(c) == "Diamond");
        s.Reach(() => s.P?.Choices.Any(c => Branch(c) == "discard") == true);
        var obtained = s.G.CardMovements.Single(m => m.Reason.Value == "skill-program.mobile-shi-jiejie.obtain");
        s.Need(obtained.From == CardLocation.DrawPile && obtained.To == CardLocation.Hand(0) &&
            s.V(0).Hand.Single(c => c.Id == obtained.CardId).Suit == Suit.Diamond && s.V(0).HandCount == count + 1 &&
            s.E<ShiXinXianYingSuitAllowanceEvent>().Length == 0 &&
            s.E<ShiXinXianYingQingshiIssuedEvent>() is [{ Target: 0, SameCamp: false, FromJiejie: true }],
            "A missing suit obtains a real matching deck entity; identical printed Wei faction does not merge the opposing actual teams.");
        s.Cold(); s.Answer(c => Branch(c) == "discard");
        s.Need(s.P is { PlayerSeat: 1 } && s.P.Choices.All(c => c.Cards.Count == 0) && s.G.CreateSnapshot(0).PendingDecision is null,
            "The owner chooses an opaque original foreign Hand slot without exposing that hand to other viewers.");
        s.Cold(); s.Answer(c => Branch(c) == "discard"); s.Play();
        var moves = s.G.CardMovements.Where(m => m.Reason.Value == "skill-program.mobile-shi-qingshi.discard").ToArray();
        s.Need(moves.Length == 2 && moves.Select(m => m.From.OwnerSeat).Order().SequenceEqual(new int?[] { 0, 1 }) &&
            s.E<ShiXinXianYingDrawReturnedEvent>().Length == 0 && s.G.GetHumanLegalActions().All(a => a.ProgramSkillId != Jiejie),
            "Opposing camps discard the two selected original entities once and consume this activator's per-phase shared quota.");
        s.Replay();

        var damage = Scene.Start(); damage.Play(); damage.Incoming();
        damage.Reach(() => damage.P?.SkillPrompt?.SkillId == Qingshi && damage.P.Choices.Any(c => Key(c) == "activate"));
        damage.Answer(c => Key(c) == "activate"); damage.Reach(() => damage.P?.Choices.Any(c => Branch(c) == "target") == true);
        damage.Cold(); damage.Answer(c => c.Targets.SequenceEqual([0])); damage.Play();
        damage.Need(damage.E<ShiXinXianYingQingshiIssuedEvent>() is [{ Target: 0, FromJiejie: false, SameCamp: true }] &&
            damage.E<ShiXinXianYingDrawReturnedEvent>() is [{ Actual: 2 }],
            "The real after-damage Qingshi separately accepts its own native target and self draw, outside Jiejie's round quota.");
    }
    private static string? Branch(PromptChoice c) => c.Parameters.GetValueOrDefault("branch");
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void AtWatch() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
        public void Incoming() => Accept(new UseProgramSkillCommand(0, Driver, "incoming", [], [], G.Revision, P!.PromptId));
        public void ActivateJiejie()
        {
            var a = G.GetHumanLegalActions().Single(a => a.ProgramSkillId == Jiejie);
            Accept(new UseProgramSkillCommand(0, Jiejie, a.ProgramActivationId!, [], [], G.Revision, P!.PromptId) { SkillOwnerSeat = a.ProgramSkillOwnerSeat });
        }
        public void UseAlcohol(int card)
        { var a = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Alcohol && a.CardId == card); Accept(new PlayCardCommand(0, card, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind)); }
        public void Slash(int card, int target)
        { var a = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.CardId == card && a.TargetSeats.SequenceEqual([target])); Accept(new PlayCardCommand(0, card, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind)); }
        public void Answer(Func<PromptChoice, bool> pick)
        { var p = P!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "Expected original current native choice."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 220; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.Choices.Any(c => Key(c) == "skip")) { Answer(c => Key(c) == "skip"); continue; }
                    if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification)
                    { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null); continue; }
                    throw new InvalidOperationException("Unexpected native Xin Xian Ying boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The real fixture keeps all participants alive."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Missing bounded Xin Xian Ying boundary.");
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold restore retains actual actor, private view, suit benefit and unpaid/paid child state."); }
        public void Replay()
        { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(before == State(), "The actual command journal reproduces native shared choices and real entity movements."); }
        private void Accept(GameCommand command)
        { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected actual native command."); }
        public void Need(bool okay, string message) { if (!okay) throw new InvalidOperationException(message + " " + Dump()); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), G.CardMovements,
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.Phase, P, Hand = V(0).Hand,
            SuitGrants = E<ShiXinXianYingSuitAllowanceEvent>(), Draws = E<ShiXinXianYingDrawReturnedEvent>(),
            AlcoholActions = E<CardActionAcceptedEvent>().Where(e => e.Action.EffectiveKind == CardKind.Alcohol).Select(e => new { e.Action.ActionId, e.Action.ActorSeat, e.Action.EffectiveSuit }).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray() });
        public static Scene Start(bool shared = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(shared));
            ShanheStartingCard[] hand = [new(CardKind.Slash, Suit.Spade, 4), new(CardKind.Slash, Suit.Spade, 5), new(CardKind.Slash, Suit.Spade, 6), new(CardKind.Alcohol, Suit.Spade, 8), new(CardKind.Alcohol, Suit.Spade, 9), new(CardKind.Alcohol, Suit.Heart, 6), new(CardKind.Peach, Suit.Club, 7)];
            var enemies = Enumerable.Range(1, 3).Select(i => new ShanheCombatantDefinition(shared && i == 1 ? Hero : Peer, Hp: shared && i == 1 ? 3 : 8)
                { StartingHand = [new(CardKind.Peach, Suit.Heart, 2)] }).ToArray();
            var setup = new ShanheBattleSetup(633, 1, "shi-xxy-native", shared ? Peer : Hero, 3, 0, 0, 0, 0, [Driver, Watch], hand, [], enemies, [], new string('f', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                HumanRole = null, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
    private sealed class Fixture(bool shared) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-xxy", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0}],
              "triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}],
              "activations":[{"id":"incoming","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]}]},
             {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-group","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.mobile-shi-jiejie.discard"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
             {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]}
            ]}
            """;
            var presentation = new Dictionary<string, object> { [Driver] = new { name = "真实命令驱动", description = "实际原生伤害与固定手牌。" },
                [Watch] = new { name = "成本子结算", description = "同组弃牌后暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Quiet] = new { name = "安静同桌", description = "固定摸牌数量。" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentation }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "同势力异阵营", "", Quiet, "wei", 8));
            b.AddDeck(new(Deck, "固定实体牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(shared ? "standard:peach" : "standard:alcohol", shared && i % 2 == 0 ? Suit.Diamond : Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势辛宪英真实阵营与私有观看", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
