using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class MantianGuohaiChecks
{
    private const string Hero = "mobile:shi-dong-zhao", Driver = "fixture:mantian-driver", Watch = "fixture:mantian-watch",
        Quiet = "fixture:mantian-quiet", Wait = "fixture:mantian-wait", Peer = "fixture:mantian-peer", Holder = "fixture:mantian-holder",
        Deck = "fixture:mantian-deck", Gift = "mobile:mantian-guohai-gift", GiftReason = "skill-program.mobile:mantian-guohai-gift.MoveBoundCards", Fengzi = "ol:fengzi";

    public static void RealTwoTargetHEJAcquisitionsThenOrderedGiftsCold()
    {
        var s = Scene.Start(); s.Play(0);
        var secrets = s.V(0).Hand.Where(c => c.Kind == CardKind.MantianGuohai).ToArray();
        s.Need(secrets.Length == 2 && secrets.Select(c => (c.Suit, c.Rank)).Order().SequenceEqual(new[] { (Suit.Heart, 5), (Suit.Diamond, 5) }.Order()),
            "Miaolue supplies the actual H5 and D5 printed Mantian Guohai entities.");
        var gear = s.V(0).Hand.Single(c => c.Kind == CardKind.Crossbow).Id;
        s.DriverAction("place", [gear], [1]); s.Play(0);
        var judgment = s.V(0).Hand.Single(c => c.Kind == CardKind.Indulgence).Id;
        s.Use(0, judgment, [2]); s.Play(0);
        s.Need(s.V(1).Equipment.Any(c => c.Id == gear) && s.V(2).Judgment.Any(c => c.Id == judgment),
            "The two HEJ sources are established by actual equipment placement and a real delayed-trick use.");
        var moves = s.G.CardMovements.Count;
        s.Use(0, secrets.Single(c => c.Suit == Suit.Heart).Id, [1, 2]);
        s.Target(1); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([gear]));
        s.Target(2); s.Answer(c => c.Cards.SequenceEqual([judgment])); s.AtGift();
        var use = s.UseFrame!;
        s.Need(use.MantianGuohaiGift is { GiftIndex: 0 } cursor && cursor.GiftTargets.SequenceEqual([1, 2]) &&
            s.V(0).Hand.Any(c => c.Id == gear) && s.V(0).Hand.Any(c => c.Id == judgment) &&
            s.G.CardMovements.Skip(moves).Count(m => m.To == CardLocation.Hand(0) && (m.CardId == gear || m.CardId == judgment)) == 2 &&
            !s.G.CardMovements.Skip(moves).Any(m => m.Reason.Value == GiftReason),
            "Both ordered acquisitions finish before the first one-card gift: first Equipment, then Judgment, with no premature return payment.");
        s.Cold(); var gift1 = s.V(0).Hand.First(c => c.Kind == CardKind.Peach).Id;
        s.Answer(c => c.Cards.SequenceEqual([gift1])); s.AtWatch();
        s.Need(s.V(1).Hand.Any(c => c.Id == gift1) && s.G.CardMovements.Count(m => m.CardId == gift1 && m.Reason.Value == GiftReason) == 1,
            "The first gift really moves one original hand entity to the first successful target before its paid movement child.");
        s.Cold(); s.ContinueWatch(); s.AtGift();
        s.Need(s.UseFrame!.MantianGuohaiGift is { GiftIndex: 1 } && s.G.ResolutionStack.OfType<ProgramSkillFrame>()
            .Single(f => f.SkillId == Gift).SelectedTargetSeats.SequenceEqual([2]),
            "The paid first child returns to the second target on the same native card use without repeating acquisition.");
        s.Answer(c => c.Cards.SequenceEqual([gear])); s.AtWatch(); s.Cold(); s.ContinueWatch(); s.Play(0);
        var paid = s.G.CardMovements.Skip(moves).Where(m => m.Reason.Value == GiftReason).ToArray();
        s.Need(paid.Length == 2 && paid[0].To == CardLocation.Hand(1) && paid[0].CardId == gift1 &&
            paid[1].To == CardLocation.Hand(2) && paid[1].CardId == gear &&
            s.G.CardMovements.Skip(moves).Count(m => m.CardId == judgment && m.To == CardLocation.Hand(0)) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == secrets.Single(c => c.Suit == Suit.Heart).Id && m.To == CardLocation.DiscardPile) == 1,
            "Cold gift recovery neither re-obtains nor re-gives a card, preserves target order, and discards the physical trick only after both gifts.");

        var hand = s.V(3).Hand.First().Id;
        s.Use(0, secrets.Single(c => c.Suit == Suit.Diamond).Id, [3]); s.Target(3);
        s.Need(s.P!.Choices.All(c => c.Cards.Count == 0) && s.G.CreateSnapshot(1).PendingDecision is null,
            "A foreign hand acquisition exposes opaque slots only to the real user.");
        s.Answer(c => c.Cards.Count == 0); s.AtGift();
        s.Need(s.UseFrame!.MantianGuohaiGift!.GiftTargets.SequenceEqual([3]) && s.V(0).Hand.Any(c => c.Id == hand),
            "The same printed card supports a genuine one-target use and still obtains its real hidden hand entity first.");
        s.Answer(c => c.Cards.SequenceEqual([hand])); s.AtWatch(); s.ContinueWatch(); s.Play(0); s.Replay();

        var repeated = Scene.Start(fengzi: true); repeated.Play(0);
        var repeatedCard = repeated.V(0).Hand.Single(c => c.Kind == CardKind.MantianGuohai && c.Suit == Suit.Heart).Id;
        var extraCost = repeated.V(0).Hand.Single(c => c.Kind == CardKind.MantianGuohai && c.Suit == Suit.Diamond).Id;
        var repeatedTargetCard = repeated.V(2).Hand.Single().Id;
        repeated.Use(0, repeatedCard, [1, 2]);
        repeated.Reach(() => repeated.P?.SkillPrompt?.SkillId == Fengzi && repeated.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        repeated.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        repeated.Reach(() => repeated.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "extra-effects-rank-guess") == true);
        repeated.Answer(c => c.Parameters.GetValueOrDefault("branch") == "cost" && c.Cards.SequenceEqual([extraCost]));
        repeated.Target(1); repeated.Answer(c => c.Cards.Count == 0);
        repeated.Target(2); repeated.Answer(c => c.Cards.Count == 0); repeated.AtGift();
        repeated.Need(repeated.UseFrame!.MantianGuohaiGift is { EffectRound: 0, GiftIndex: 0 } firstRound && firstRound.GiftTargets.SequenceEqual([1, 2]) &&
            repeated.E<ExtraCardEffectStartedEvent>().Length == 0,
            "Fengzi keeps the first round's two acquisitions together and starts no extra round before its gifts.");
        var firstReturn = repeated.V(0).Hand.First(c => c.Kind == CardKind.Peach).Id;
        repeated.Answer(c => c.Cards.SequenceEqual([firstReturn])); repeated.AtWatch(); repeated.ContinueWatch(); repeated.AtGift();
        repeated.Answer(c => c.Cards.SequenceEqual([repeatedTargetCard])); repeated.AtWatch();
        repeated.Need(repeated.E<ExtraCardEffectStartedEvent>().Length == 0 && repeated.G.CardMovements.Count(m => m.Reason.Value == GiftReason) == 2,
            "The complete first round includes both real gifts and their paid children before the extra effect starts.");
        repeated.Cold(); repeated.ContinueWatch();
        repeated.Reach(() => repeated.P is { Kind: DecisionKind.Nullification, PlayerSeat: 0, TargetSeat: 1 });
        repeated.Need(repeated.UseFrame!.MantianGuohaiGift is { EffectRound: 1, GiftIndex: 0, GiftTargets.Count: 0 } &&
            repeated.E<ExtraCardEffectStartedEvent>().Length == 1,
            "The same physical Use starts one independent extra round with an empty gift debt, even before its first target is nullified.");
        repeated.Cold(); repeated.Answer(c => c.Parameters.GetValueOrDefault("response") == "nullification");
        repeated.Target(2); repeated.Answer(c => c.Cards.Count == 0); repeated.AtGift();
        repeated.Need(repeated.UseFrame!.MantianGuohaiGift is { EffectRound: 1 } secondRound && secondRound.GiftTargets.SequenceEqual([2]) &&
            repeated.G.CardMovements.Count(m => m.Reason.Value == GiftReason) == 2,
            "Extra-round nullification removes only that round's first target; the second target is acquired before its own later gift.");
        var finalReturn = repeated.V(0).Hand.First(c => c.Kind == CardKind.Crossbow).Id;
        repeated.Answer(c => c.Cards.SequenceEqual([finalReturn])); repeated.AtWatch(); repeated.Cold(); repeated.ContinueWatch(); repeated.Play(0);
        repeated.Need(repeated.G.CardMovements.Where(m => m.Reason.Value == GiftReason).Select(m => m.To).SequenceEqual(
                new[] { CardLocation.Hand(1), CardLocation.Hand(2), CardLocation.Hand(2) }) &&
            repeated.E<TargetCardTakenEvent>().Length == 3 && repeated.E<CardUseDeclaredEvent>().Count(e => e.CardId == repeatedCard) == 1 &&
            repeated.G.CardMovements.Count(m => m.CardId == repeatedCard && m.Reason == CardMoveReasons.Use) == 1 &&
            repeated.G.CardMovements.Count(m => m.CardId == repeatedCard && m.To == CardLocation.DiscardPile) == 1 &&
            repeated.G.CardMovements.Count(m => m.CardId == extraCost && m.To == CardLocation.DiscardPile) == 1,
            "Fengzi repeats the full obtain-then-give effect with separate target counterspells, while the native material and extra payment each commit once.");
        repeated.Replay();
    }

    public static void PerTargetNullificationTransferredHolderAndHandLimitCold()
    {
        var s = Scene.Start(); s.Play(0);
        var heart = s.V(0).Hand.Single(c => c.Kind == CardKind.MantianGuohai && c.Suit == Suit.Heart).Id;
        var diamond = s.V(0).Hand.Single(c => c.Kind == CardKind.MantianGuohai && c.Suit == Suit.Diamond).Id;
        var target1 = s.V(1).Hand.Select(c => c.Id).ToArray(); var taken = s.V(2).Hand.First().Id;
        var before = s.G.CardMovements.Count;
        s.Use(0, heart, [1, 2]);
        s.Reach(() => s.P is { Kind: DecisionKind.Nullification, PlayerSeat: 0 } && s.P.TargetSeat == 1);
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "nullification");
        s.Target(2); s.Answer(c => c.Cards.Count == 0); s.AtGift();
        var nulls = s.E<NullificationResolvedEvent>().Where(e => e.EffectCardKind == CardKind.MantianGuohai).ToArray();
        s.Need(nulls.Select(e => e.TargetSeat).SequenceEqual(new int?[] { 1, 2 }) && nulls[0].EffectNullified && !nulls[1].EffectNullified &&
            s.UseFrame!.MantianGuohaiGift!.GiftTargets.SequenceEqual([2]) && target1.All(id => s.V(1).Hand.Any(c => c.Id == id)) &&
            s.V(0).Hand.Any(c => c.Id == taken),
            "Independent nullification cancels only target one; only target two contributes an acquired card and a later gift debt.");
        var returned = s.V(0).Hand.First(c => c.Kind == CardKind.Peach).Id;
        s.Answer(c => c.Cards.SequenceEqual([returned])); s.AtWatch(); s.Cold(); s.ContinueWatch(); s.Play(0);
        s.Need(s.G.CardMovements.Skip(before).Count(m => m.Reason.Value == GiftReason) == 1 &&
            s.G.CardMovements.Skip(before).Single(m => m.Reason.Value == GiftReason).To == CardLocation.Hand(2),
            "A canceled target never receives the post-acquisition hand-card gift.");
        s.DriverAction("give", [diamond], [1]); s.Play(0);
        s.Need(s.V(1).GeneralId == Holder && s.V(1).Hand.Any(c => c.Id == diamond && c.Kind == CardKind.MantianGuohai),
            "The second original secret transfers as a physical card to a non-Dong-Zhao holder.");
        var discardStart = s.G.CardMovements.Count;
        s.Need(s.V(1).Hp == 3 && s.V(1).HandCount == 4, "The recipient has exactly three ordinary hand cards plus the transferred exempt trick.");
        s.End(0); s.Reach(() => s.E<TurnStartedEvent>().Any(e => e.TurnNumber == 3 && e.ActorSeat == 2));
        s.Need(s.G.CardMovements.Skip(discardStart).All(m => m.From != CardLocation.Hand(1) || m.To != CardLocation.DiscardPile) &&
            s.V(1).Hand.Any(c => c.Id == diamond),
            "The card's hand-limit exemption follows its actual recipient without requiring Miaolue or another owner-only rule.");
        s.NextPlay(0); s.End(0);
        s.Reach(() => s.UseFrame is { SourceSeat: 1, MantianGuohaiGift.ProgramFrameId: not null }, allowEnd: true); s.Cold();
        s.Need(s.E<CardUseDeclaredEvent>().Any(e => e.SourceSeat == 1 && e.CardId == diamond && e.CardKind == CardKind.MantianGuohai) &&
            s.UseFrame!.MantianGuohaiGift!.GiftTargets.Count is 1 or 2,
            "The ordinary recipient can really use the transferred printed card during its own actual Play phase.");
        s.Reach(() => s.E<CardUseFinishedEvent>().Any(e => e.CardId == diamond && e.CardKind == CardKind.MantianGuohai), allowEnd: true);
        s.Need(s.G.CardMovements.Count(m => m.CardId == diamond && m.To == CardLocation.DiscardPile) == 1 &&
            s.G.CardMovements.Any(m => m.From == CardLocation.Hand(1) && m.To.Zone == CardZoneKind.Hand && m.To.OwnerSeat != 1 && m.Reason.Value == GiftReason),
            "The new holder pays its own gift once and completes the same native card lifecycle."); s.Replay();
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardUseFrame? UseFrame => G.ResolutionStack.OfType<CardUseFrame>().LastOrDefault(f => f.CardKind == CardKind.MantianGuohai);
        public void Play(int seat) => Reach(() => P is { Kind: DecisionKind.PlayCard } p && p.PlayerSeat == seat);
        public void NextPlay(int seat) => Reach(() => P is { Kind: DecisionKind.PlayCard } p && p.PlayerSeat == seat, allowEnd: true);
        public void End(int seat) => Accept(new EndPlayPhaseCommand(seat, G.Revision, P!.PromptId));
        public void Use(int seat, int id, int[] targets) => Accept(new PlayCardCommand(seat, id, targets, G.Revision, P!.PromptId));
        public void DriverAction(string id, int[] cards, int[] targets) => Accept(new UseProgramSkillCommand(0, Driver, id, cards, targets, G.Revision, P!.PromptId));
        public void Target(int seat) => Reach(() => P is { Kind: DecisionKind.SelectTargetCard } p && p.TargetSeat == seat);
        public void AtGift() => Reach(() => P?.SkillPrompt?.SkillId == Gift);
        public void AtWatch() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
        public void ContinueWatch() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Answer(Func<PromptChoice, bool> pick)
        { var p = P!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "Expected the current exact native choice."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready, bool allowEnd = false)
        {
            for (var n = 0; n < 220; n++)
            {
                if (ready()) return;
                if (P is { } p && V(p.PlayerSeat).IsHuman)
                {
                    if (p.SkillPrompt?.SkillId == Watch) { ContinueWatch(); continue; }
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                    { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                    if (p.Kind == DecisionKind.Nullification) { Answer(c => c.Parameters.GetValueOrDefault("response") == "pass"); continue; }
                    if (allowEnd && p.Kind == DecisionKind.PlayCard) { End(p.PlayerSeat); continue; }
                    throw new InvalidOperationException("Unexpected Mantian Guohai prompt. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The native fixture keeps its real participants alive."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Mantian Guohai missed its bounded native boundary.");
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold restore retains current target, exact gift cursor, physical cards and pending paid children."); }
        public void Replay()
        { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(before == State(), "The accepted journal reproduces target-specific counterspells, HEJ acquisition and ordered real gifts."); }
        public void Need(bool value, string why) { if (!value) throw new InvalidOperationException(why + " " + Dump()); }
        private void Accept(GameCommand c)
        { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Mantian Guohai native command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements,
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, P, Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray() });
        public static Scene Start(bool fengzi = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(fengzi));
            var setup = new ShanheBattleSetup(602, 1, "mantian-native", Hero, 3, 0, 0, 0, 0, fengzi ? [Driver, Watch, Fengzi] : [Driver, Watch],
                [new(CardKind.Crossbow, Suit.Club, 1), new(CardKind.Indulgence, Suit.Heart, 6), new(CardKind.Peach, Suit.Heart, 7), new(CardKind.Nullification, Suit.Spade, 11)], [],
                [new(Holder, Hp: 3) { StartingHand = [new(CardKind.Dodge, Suit.Club, 2), new(CardKind.Dodge, Suit.Club, 3), new(CardKind.Peach, Suit.Heart, 4)] },
                 new(Peer, Hp: 8) { StartingHand = [new(CardKind.Dodge, Suit.Club, 5)] }, new(Peer, Hp: 8) { StartingHand = [new(CardKind.Dodge, Suit.Diamond, 6)] }], [], new string('c', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture(bool fengzi) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mantian-guohai", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            if (fengzi) CharacterFixtureContent.Register(b, Hero, "ol:wang-rong");
            else CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}],"activations":[
              {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]},
              {"id":"give","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gift"},{"op":"moveBoundCards","target":"owner","sourceBind":"gift","destination":"selectedTargetHand"}]}]},
             {"id":"{{Watch}}","revision":1,"triggers":[{"id":"gift-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{GiftReason}}"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
             {"id":"{{Wait}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},{"id":"first-no-play","window":"afterNormalDraw","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},{"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var presentations = new Dictionary<string, object> { [Driver] = new { name = "真实配置与转交", description = "真实置入装备与转交手牌。" },
                [Watch] = new { name = "已付赠牌子结算", description = "实际赠牌后停留。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }, [Wait] = new { name = "普通持牌人", description = "保留实际阶段。" },
                [Quiet] = new { name = "安静同桌", description = "保留防御与区域牌。" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Holder, "普通实体持牌角色", "", Wait, "wei", 3)); b.AddGeneral(new(Peer, "普通目标", "", Quiet, "wei", 8));
            b.AddDeck(new(Deck, "固定实体牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "瞒天过海原生区域交换", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Holder, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
