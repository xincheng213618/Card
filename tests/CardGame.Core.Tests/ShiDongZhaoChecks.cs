using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiDongZhaoChecks
{
    private const string Hero = "mobile:shi-dong-zhao", Miaolue = "mobile:shi-miaolue", Yingjia = "mobile:shi-yingjia",
        Driver = "fixture:shi-dong-zhao-driver", Watch = "fixture:shi-dong-zhao-watch", Quiet = "fixture:shi-dong-zhao-quiet",
        Peer = "fixture:shi-dong-zhao-peer", Deck = "fixture:shi-dong-zhao-deck";

    public static void OpeningBrainGainPaidChildAndForeignExtraStartCold()
    {
        var s = Scene.Start(); s.Play();
        var secrets = s.V(0).Hand.Where(c => c.Kind == CardKind.MantianGuohai).ToArray();
        s.Need(secrets.Length == 2 && secrets.Select(c => (c.Suit, c.Rank)).Order().SequenceEqual(new[] { (Suit.Heart, 5), (Suit.Diamond, 5) }.Order()),
            "Miaolue creates the two real H5/D5 Mantian Guohai cards at game start, rather than virtual substitutions.");
        s.Incoming(2); s.Offer(Miaolue); s.Activate(); s.Branch("Dismantlement"); s.AtWatch();
        var gain = s.E<MobileShiDongZhaoBenefitEvent>().Single();
        s.Need(gain.Branch == "Dismantlement" && gain.CardId > 0 && s.E<MobileShiDongZhaoStartedEvent>().Count(e => e.TriggerId == "damage-benefit") == 1 &&
            s.G.CardMovements.Any(m => m.CardId == gain.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Sequence > gain.Before && m.Sequence <= gain.After),
            "Two damage in one damage occurrence offers one Miaolue, and the chosen brain name obtains a real matching deck entity before its gain child.");
        s.Cold(); s.ContinueWatch(); s.Play();
        s.Native(CardKind.DrawTwo); s.Play(); s.Native(CardKind.DrawTwo); s.Play();
        s.Need(s.E<ActualTurnTrickUseRecordedEvent>().Count(e => e.ActorSeat == 0 && e.EffectiveKind == CardKind.DrawTwo && e.TurnNumber == 1) == 2,
            "Two genuine same-name Trick uses qualify through the shared actual-use ledger.");
        s.End(); s.Offer(Yingjia); s.Activate(); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "payment") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "payment"); s.AtWatch();
        var payment = s.E<MobileShiDongZhaoCostPaidEvent>().Single();
        s.Need(s.Root is { MobileShiDongZhao.Stage: MobileShiDongZhaoStage.CostChildren, PendingMovementContinuation: not null } &&
            s.E<MobileShiDongZhaoExtraGrantedEvent>().Length == 0 && s.G.CardMovements.Count(m => m.CardId == payment.CardId &&
                m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill.mobile-shi-dong-zhao.discard") == 1,
            "Yingjia pays one actual hand discard on its owning frame, waits for its child and has not yet issued an extra turn.");
        s.Cold(); s.ContinueWatch(); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "target") == true);
        s.Answer(c => c.Targets.SequenceEqual([3]));
        s.Reach(() => s.E<TurnStartedEvent>().Any(e => e.TurnNumber == 2 && e.ActorSeat == 3));
        var grant = s.E<MobileShiDongZhaoExtraGrantedEvent>().Single(); var entered = s.E<MobileShiDongZhaoExtraEnteredEvent>().Single();
        s.Need(grant.TargetSeat == 3 && entered.GrantFrameId == grant.FrameId && entered.TargetSeat == 3 && entered.UpcomingTurn == 2 &&
            s.E<RoundStartedEvent>().Length == 1 && s.G.CardMovements.Count(m => m.To == CardLocation.Hand(3) &&
                m.Reason.Value == "skill-program.mobile-shi-dong-zhao.draw" && m.TurnNumber == 1) == 2 &&
            s.E<MobileShiDongZhaoCostPaidEvent>().Length == 1,
            "The issued foreign extra turn really starts before the saved normal successor, receives exactly two start cards before TurnStarted, and never re-pays the saved discard.");
        s.Cold(); s.Reach(() => s.E<TurnStartedEvent>().Any(e => e.TurnNumber == 3 && e.ActorSeat == 1));
        s.Need(s.E<MobileShiDongZhaoExtraEnteredEvent>().Length == 1 && s.V(0).SkillRuntimeStates!.Single(x => x.SkillId == Yingjia)
            .PublicRuleStates!.Single(x => x.StateId == "same-tricks-ended").Used == 1,
            "The extra-turn start lease is consumed once and the normal table resumes with the visible limited skill spent.");
        s.Replay();
    }

    public static void DistinctTricksDoNotQualifyAndDamageDrawsOnce()
    {
        var s = Scene.Start(distinct: true); s.Play(); s.Incoming(1); s.Offer(Miaolue); s.Activate();
        var before = s.V(0).HandCount; s.Branch("draw"); s.AtWatch(); s.Cold(); s.ContinueWatch(); s.Play();
        s.Need(s.V(0).HandCount == before + 2 && s.E<MobileShiDongZhaoBenefitEvent>() is [{ Branch: "draw" }] &&
            s.G.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.mobile-shi-dong-zhao.draw") == 2,
            "The draw branch requests exactly two real draws, and its saved gain child does not draw again.");
        var realDrawTwo = s.V(0).Hand.Single(c => c.Kind == CardKind.DrawTwo).Id;
        s.Native(CardKind.DrawTwo); s.Play(); s.Incoming(1); s.Offer(Miaolue); s.Activate(); s.Branch("DrawTwo"); s.AtWatch();
        s.Need(s.E<MobileShiDongZhaoBenefitEvent>().Last().CardId == realDrawTwo && s.G.CardMovements.Any(m =>
            m.CardId == realDrawTwo && m.From == CardLocation.DiscardPile && m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.mobile-shi-dong-zhao.gain"),
            "When the deck has no selected brain name, Miaolue obtains the real matching entity from the discard pile, without calling it a draw or use.");
        s.Cold(); s.ContinueWatch(); s.Play(); s.Native(CardKind.IronChain, [1]); s.Play(); s.End();
        s.Reach(() => s.E<TurnStartedEvent>().Any(e => e.TurnNumber == 2 && e.ActorSeat == 1));
        s.Need(s.E<ActualTurnTrickUseRecordedEvent>().Count(e => e.ActorSeat == 0 && e.TurnNumber == 1) == 2 &&
            s.E<MobileShiDongZhaoStartedEvent>().All(e => e.TriggerId != "same-tricks-ended") && s.YingjiaOffersSkipped == 0,
            "Two distinct Trick names cannot unlock Yingjia even though the total Trick-use counter reached two; unused opening secrets do not count as uses.");
        s.Replay();

        var absent = Scene.Start(); absent.Play(); absent.Incoming(1); absent.Offer(Miaolue); absent.Activate();
        var unchanged = absent.V(0).HandCount; absent.Branch("Nullification"); absent.Play();
        absent.Need(absent.V(0).HandCount == unchanged && absent.E<MobileShiDongZhaoBenefitEvent>() is [{ Branch: "Nullification", CardId: 0 }],
            "An absent declared brain name does not reveal deck identities, create a card or substitute two draws.");
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.MobileShiDongZhao is not null);
        public int YingjiaOffersSkipped { get; private set; }
        public void Native(CardKind kind, int[]? targets = null)
        {
            var action = G.GetHumanLegalActions().FirstOrDefault(a => a.Kind != LegalActionKind.UseProgramSkill && a.CardId is > 0 &&
                a.ConversionSource is null && V(0).Hand.Any(c => c.Id == a.CardId && c.Kind == kind) && (targets is null || a.TargetSeats.SequenceEqual(targets)));
            Need(action is not null, "The prepared real " + kind + " must be playable.");
            Accept(new PlayCardCommand(0, action!.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId));
        }
        public void Incoming(int amount) => Accept(new UseProgramSkillCommand(0, Driver, "incoming-" + amount, [], [1], G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Offer(string id) => Reach(() => P?.SkillPrompt?.SkillId == id && P.Choices.Any(c => Key(c) == "activate"));
        public void AtWatch() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
        public void ContinueWatch() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Activate() => Answer(c => Key(c) == "activate");
        public void Branch(string branch)
        { Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch) == true); Answer(c => c.Parameters.GetValueOrDefault("branch") == branch); }
        public void Answer(Func<PromptChoice, bool> pick)
        { var p = P!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "The exact expected native choice must exist."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
        public void Reach(Func<bool> ready)
        {
            for (var n = 0; n < 200; n++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.SkillPrompt?.SkillId == Watch) { ContinueWatch(); continue; }
                    if (p.Choices.Any(c => Key(c) == "skip")) { if (p.SkillPrompt?.SkillId == Yingjia) YingjiaOffersSkipped++; Answer(c => Key(c) == "skip"); continue; }
                    if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null); continue; }
                    throw new InvalidOperationException("Unexpected Shi Dong Zhao boundary: " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The real native scene retains living participants."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Shi Dong Zhao missed the bounded native boundary.");
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Shi Dong Zhao's original paid state cold-restores exactly."); }
        public void Replay()
        { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "Original commands replay the real secret cards, named benefit, actual Trick uses and foreign extra turn exactly."); }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Shi Dong Zhao command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements,
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, P, Root?.MobileShiDongZhao,
            Grants = E<MobileShiDongZhaoExtraGrantedEvent>(), Entries = E<MobileShiDongZhaoExtraEnteredEvent>(), Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }) });
        public static Scene Start(bool distinct = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(79, 1, "shi-dong-zhao", Hero, 3, 0, 0, 0, 0, [Driver, Watch],
                [new(CardKind.DrawTwo, Suit.Heart, 7), new(distinct ? CardKind.IronChain : CardKind.DrawTwo, Suit.Heart, 8),
                 new(CardKind.Dismantlement, Suit.Spade, 3), new(CardKind.Dodge, Suit.Diamond, 4)], [],
                [new(Peer, Hp: 8) { StartingHand = [new(CardKind.Dodge, Suit.Club, 2)] }, new(Peer, Hp: 8) { StartingHand = [] },
                 new(Peer, Hp: 8) { StartingHand = [] }], [], new string('d', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0,
                HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-dong-zhao", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"retain-test-hand","query":"handLimit","operation":"add","value":30,"priority":0}],"activations":[
                {"id":"incoming-1","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
                {"id":"incoming-2","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"gain-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.mobile-shi-dong-zhao.gain","skill-program.mobile-shi-dong-zhao.draw"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]},
                {"id":"cost-child","window":"discardPileReceived","subject":"owner","movementDiscardOnly":true,"discardOwnerScope":"own","movementReasons":["skill.mobile-shi-dong-zhao.discard"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"skip-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "真实受伤驱动", description = "真实伤害并保留实际手牌。" },
                    [Watch] = new { name = "已支付原生子结算", description = "真实得牌和弃牌后保留原始帧。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                    [Quiet] = new { name = "安静同桌", description = "保留真实回合，跳过出牌。" } } }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实同桌角色", "", Quiet, "wei", 10));
            b.AddDeck(new(Deck, "固定过河拆桥牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 120)
                .Select(_ => new ContentDeckPhysicalCard("standard:dismantlement", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势董昭实体牌与迎驾", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
