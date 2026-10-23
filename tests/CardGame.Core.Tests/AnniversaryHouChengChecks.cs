using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryHouChengChecks
{
    private const string Hero = "anniversary:hou-cheng", Skill = "anniversary:xianniang", Driver = "fixture:hou-cheng-driver", Quiet = "fixture:hou-cheng-quiet", Watch = "fixture:hou-cheng-watch", Deck = "fixture:hou-cheng-deck";
    private static string Peer(int seat) => "fixture:hou-cheng-peer-" + seat;
    private static string? Verb(PromptChoice c) => c.Parameters.GetValueOrDefault("verb");

    public static void ActualTwoItemsTransferredWinePlayedAndOncePaidHpCold()
    {
        var s = Scene.Start(); s.Play();
        var originalSlash = s.V(0).Hand.Single(c => c.Kind == CardKind.Slash).Id;
        var chain = s.V(0).Hand.Single(c => c.Kind == CardKind.IronChain).Id;
        s.Driver("hurt"); s.Offer(); s.Activate(); s.Menu(AnniversaryHouChengStage.Source);
        var first = s.Root!.Id;
        s.Need(s.Root.TriggerId == "after-damage" && s.V(0).Hp == 4, "A real one-point damage completes before the independently limited damage item offers Xianniang.");
        s.Answer(c => Verb(c) == "source" && c.Targets.SequenceEqual([1])); s.Menu(AnniversaryHouChengStage.Obtain);
        s.Need(s.P!.Choices.Where(c => Verb(c) == "card").All(c => c.Cards.Count == 0) && s.P.ValidCardIds.Count == 0,
            "Obtaining foreign Hand cards exposes only opaque current slots.");
        s.PickForeign(CardKind.Slash, 9); s.PickForeign(CardKind.Dodge, 8); s.Watch();
        var gained = s.Root!.AnniversaryHouCheng!.Moves.Select(m => m.CardId).ToArray();
        var slash = gained.Single(id => s.Kind(id) == CardKind.Slash); var dodge = gained.Single(id => s.Kind(id) == CardKind.Dodge);
        s.Need(gained.All(id => s.Zone(id) == CardLocation.Hand(0)) && s.E<AnniversaryHouChengMovementEvent>().Single(e => e.FrameId == first) is { Giving: false, ActualCount: 2 } && s.Root.AnniversaryHouCheng!.Obtained == 2,
            "The exact two printed entities really enter Hand before the original gain child pauses, with the upper bound based on its actual obtain receipt.");
        s.Cold(); s.Continue(); s.Menu(AnniversaryHouChengStage.Recipient);
        s.Need(s.P!.Choices.Where(c => Verb(c) == "recipient").SelectMany(c => c.Targets).Order().SequenceEqual([2, 3]), "The give recipient excludes both Hou Cheng and the original obtain source.");
        s.Answer(c => Verb(c) == "recipient" && c.Targets.SequenceEqual([2])); s.Menu(AnniversaryHouChengStage.Give);
        s.Answer(c => Verb(c) == "card" && c.Cards.SequenceEqual([dodge])); s.Answer(c => Verb(c) == "finish"); s.Play();
        s.Need(s.Zone(dodge) == CardLocation.Hand(2) && s.E<AnniversaryHouChengMovementEvent>().Where(e => e.FrameId == first).Select(e => (e.Giving, e.ActualCount)).SequenceEqual([(false, 2), (true, 1)]) &&
            !s.E<AnniversaryHouChengHpPaidEvent>().Any(), "One selected card is genuinely given through native movement, while exactly two round obtains do not cost HP.");
        s.Driver("take", [2]); s.Answer(c => c.Parameters.GetValueOrDefault("slot-index") == "0"); s.Play();
        s.Need(s.Zone(dodge) == CardLocation.Hand(0) && s.G.GetHumanLegalActions().Any(a => a.CardId == dodge && a.Kind == LegalActionKind.Alcohol && a.ConversionSource?.SkillId == Skill) &&
            !s.G.GetHumanLegalActions().Any(a => a.CardId == slash && a.Kind is LegalActionKind.Slash or LegalActionKind.IronChain) &&
            s.G.GetHumanLegalActions().Any(a => a.CardId == slash && a.Kind == LegalActionKind.Recast && a.ConversionSource?.SkillId == "classic:lianhuan"), "The marked printed Dodge returns through an unrelated later obtain and keeps a real Wine entrance. The fixed marked club Slash retains native Lianhuan Recast, while its true Slash and converted IronChain Uses remain unavailable.");
        s.Driver("request", [1]); s.Play();
        s.Need(s.V(0).Hp == 3 && s.Zone(dodge) == CardLocation.Hand(0) && !s.E<CardActionAcceptedEvent>().Any(e => e.Action is { ActorSeat: 0, EffectiveKind: CardKind.Dodge }) &&
            s.E<AnniversaryHouChengStartedEvent>().Length == 1, "A genuine incoming native Slash cannot consume the marked Dodge as a true Dodge Use; the already-spent damage item does not offer twice.");
        s.Card(dodge, [], LegalActionKind.Alcohol); s.Play();
        var wine = s.E<CardUseDeclaredEvent>().Single(e => e.CardId == dodge);
        var winePayment = s.G.CardMovements.Where(m => m.CardId == dodge && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use).ToArray();
        s.Need(s.Zone(dodge) == CardLocation.DiscardPile && s.Kind(dodge) == CardKind.Dodge && s.V(0).HasAlcoholEffect && wine.CardKind == CardKind.Alcohol && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == wine.ResolutionId) == 1 && winePayment is [{ CardKind: CardKind.Dodge }],
            "The printed Dodge pays one real physical Wine Use without mutating its printed kind.");
        s.Card(originalSlash, [3], LegalActionKind.Slash); s.Play();
        s.Need(!s.V(0).HasAlcoholEffect && s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 3 && e.Amount == 2) &&
            !s.G.GetHumanLegalActions().Any(a => a.CardId == slash && a.Kind == LegalActionKind.Alcohol), "A real Slash consumes the Wine damage bonus, and the same actual turn's ordinary Wine quota remains spent afterward.");
        s.Driver("foreign-discard", [1]); s.Offer(); s.Activate(); s.Menu(AnniversaryHouChengStage.Source);
        var second = s.Root!.Id;
        s.Need(s.Root.TriggerId == "foreign-discard" && s.Zone(chain) == CardLocation.DiscardPile && s.E<NativeLogicalDiscardInvocationRecordedEvent>().Any(i => i.ExecutorSeat == 1 && s.E<NativeLogicalDiscardMaterialCompletedEvent>().Any(m => m.InvocationId == i.InvocationId && m.CardId == chain && m.From == CardLocation.Hand(0))),
            "The other independent item is issued only from the selected other player's real logical discard of Hou Cheng's Hand entity.");
        s.Answer(c => Verb(c) == "source" && c.Targets.SequenceEqual([1])); s.Menu(AnniversaryHouChengStage.Obtain);
        s.PickForeign(CardKind.Dodge, 4); s.PickForeign(CardKind.Peach, 3); s.Watch();
        var secondCards = s.Root!.AnniversaryHouCheng!.Moves.Select(m => m.CardId).ToArray(); var peach = secondCards.Single(id => s.Kind(id) == CardKind.Peach); var secondDodge = secondCards.Single(id => s.Kind(id) == CardKind.Dodge);
        s.Cold(); s.Continue(); s.Menu(AnniversaryHouChengStage.Recipient); s.Answer(c => Verb(c) == "stop"); s.Play();
        s.Need(s.V(0).Hp == 2 && s.E<AnniversaryHouChengMovementEvent>().Where(e => !e.Giving).Sum(e => e.ActualCount) == 4 &&
            s.E<AnniversaryHouChengHpPaidEvent>().ToArray() is [{ FrameId: var hpFrame, ActualLoss: 1, Before: 3, After: 2 }] && hpFrame == second &&
            s.E<ProgramSkillHpLostEvent>().Count(e => e.FrameId == second && e.TargetSeat == 0 && e.Amount == 1) == 1 && s.E<AnniversaryHouChengCompletedEvent>().Count(e => e.FrameId == second) == 1,
            "Four actual round obtains cost exactly one native HP after the optional give, and paid gain recovery neither repeats cards nor the HP deduction.");
        s.Driver("pure-duel", [1, 0]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        s.Need(s.P!.Choices.Any(c => c.Cards.SequenceEqual([slash])), "The same marked Slash still has its genuine pure Played Duel response.");
        s.Answer(c => c.Cards.SequenceEqual([slash])); s.Play();
        s.Need(s.Zone(slash) == CardLocation.DiscardPile && s.E<CardActionAcceptedEvent>().Count(e => e.Action is { ActorSeat: 0, Type: CardActionType.Response, EffectiveKind: CardKind.Slash } a && a.PhysicalCards.Any(c => c.CardId == slash)) == 1 && s.V(0).Hp == 2 && s.V(1).Hp == 11,
            "A genuine Duel pays that marked Slash exactly once as Response rather than Use; its real opposing participant cannot reply and takes the Duel damage.");
        s.Driver("hurt"); s.Play();
        s.Need(s.V(0).Hp == 1 && s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1) == 2 && s.E<AnniversaryHouChengStartedEvent>().Length == 2,
            "The later actual native one-point damage is explicitly paid and cannot refresh either of the two already-used round items.");
        s.Driver("lose-one"); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        s.Need(s.V(0).Hp == 0 && s.P!.Choices.Any(c => c.Cards.SequenceEqual([peach]) && c.Parameters.GetValueOrDefault("response") == "alcohol") &&
            !s.P.Choices.Any(c => c.Cards.SequenceEqual([peach]) && c.Parameters.GetValueOrDefault("response") == "peach"), "Marked Peach has only the true Wine self-rescue entrance, even after ordinary Wine quota was spent.");
        s.Cold(); s.Answer(c => c.Cards.SequenceEqual([peach]) && c.Parameters.GetValueOrDefault("response") == "alcohol"); s.Play();
        s.Need(s.V(0).Hp == 1 && s.Zone(peach) == CardLocation.DiscardPile && s.E<AnniversaryHouChengHpPaidEvent>().Length == 1, "Native Wine self-rescue returns through the original dying parent once without repeating Xianniang's paid HP loss.");
        s.Driver("recover"); s.Play(); s.End(); s.Play();
        s.Need(s.E<RoundStartedEvent>().Last().RoundNumber == 2 && s.G.GetHumanLegalActions().Any(a => a.CardId == secondDodge && a.Kind == LegalActionKind.Alcohol), "The quiet fixed table reaches the next actual round without simulation or seed search; the untouched marked Dodge keeps its real Wine entrance across that round boundary.");
        var before = s.V(0).Hp; s.Driver("hurt"); s.Offer(); s.Activate(); s.Menu(AnniversaryHouChengStage.Source); s.Answer(c => Verb(c) == "stop"); s.Play();
        s.Need(s.E<AnniversaryHouChengStartedEvent>().Last().Round == 2 && s.V(0).Hp == before - 1 && s.E<AnniversaryHouChengHpPaidEvent>().Length == 1, "Both the trigger allowance and actual gain sum use the new round; taking zero does not fabricate a gain or old-round HP charge.");
        s.Replay();
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().FirstOrDefault(f => f.AnniversaryHouCheng is not null);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public CardKind Kind(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).CardKind;
        public void Offer() => Reach(() => P?.SkillPrompt?.SkillId == Skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Menu(AnniversaryHouChengStage stage) => Reach(() => Root?.AnniversaryHouCheng?.Stage == stage && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-hou-cheng") == true);
        public void Watch() => Reach(() => P?.SkillPrompt?.SkillId == AnniversaryHouChengChecks.Watch);
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void PickForeign(CardKind kind, int rank)
        {
            var hand = V(Root!.AnniversaryHouCheng!.Victim).Hand;
            var slot = hand.ToList().FindIndex(c => c.Kind == kind && c.Rank == rank); Need(slot >= 0, "The fixed foreign Hand contains its exact intended physical material.");
            Answer(c => Verb(c) == "card" && c.Parameters.GetValueOrDefault("source-zone") == CardZoneKind.Hand.ToString() && c.Parameters.GetValueOrDefault("slot-index") == slot.ToString());
        }
        public void Driver(string binding, int[]? targets = null)
        {
            Accept(new UseProgramSkillCommand(0, AnniversaryHouChengChecks.Driver, binding, [], targets ?? [], G.Revision, P!.PromptId));
        }
        public void Card(int id, int[] targets, LegalActionKind kind)
        {
            var a = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId == id && a.TargetSeats.SequenceEqual(targets) && a.Kind == kind); Need(a is not null, "The intended physical entity has its canonical native action.");
            Accept(new PlayCardCommand(0, id, a!.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        }
        public void Answer(Func<PromptChoice, bool> match)
        { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(c is not null, "The original published Hou Cheng/native child choice exists."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Accept(GameCommand c)
        { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected real Xianniang command."); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 200; i++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification) { Answer(c => c.Cards.Count == 0); continue; }
                    throw new InvalidOperationException("Unexpected native Xianniang human boundary." + Dump());
                }
                Need(G.State.Winner == Winner.None, "The fixed short table retains native participants."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The bounded original Xianniang/native child did not advance.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold restores the original paid gain or genuine dying Wine choice exactly."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(before == State(), "Real independent items, permanent entities, native Wine/Played and once-paid HP replay exactly."); }
        public void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Root = Root?.AnniversaryHouCheng, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).HandCount }), Last = G.Events.TakeLast(12).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard C(CardKind k, int r) => new(k, Suit.Heart, r);
            var setup = new ShanheBattleSetup(17, 1, "hou-cheng-actual-native", Hero, 5, 0, 0, 0, 0, [AnniversaryHouChengChecks.Driver, AnniversaryHouChengChecks.Watch, "classic:lianhuan"], [C(CardKind.IronChain, 12), C(CardKind.Slash, 1)], [],
                [new(Peer(2), Hp: 12) { StartingHand = [] }, new(Peer(3), Hp: 12) { StartingHand = [] }],
                [new(Peer(1), Hp: 12) { StartingHand = [new(CardKind.Slash, Suit.Club, 9), C(CardKind.Dodge, 8), C(CardKind.Slash, 7), C(CardKind.Peach, 6), C(CardKind.Slash, 5), C(CardKind.Dodge, 4), C(CardKind.Peach, 3), C(CardKind.Slash, 2)] }], new string('e', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-hou-cheng", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero, "classic:pang-tong");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                {"id":"lose-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"recover","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":4}]},
                {"id":"take","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"ownerHand","awaitMovementTriggers":true}]},
                {"id":"foreign-discard","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"selectedTarget"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"cardKinds":["ironChain"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"request"}]},
                {"id":"pure-duel","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startVirtualDuel","target":"owner"}]}
              ]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"paid","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:xianniang.obtain"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}
              ]}
            ]}
            """;
            var names = new Dictionary<string, object>(); foreach (var id in new[] { Quiet, AnniversaryHouChengChecks.Driver }) names[id] = new { name = "侯成固定原生输入", description = "保留真实伤害、逻辑弃置、获取与响应。" };
            names[AnniversaryHouChengChecks.Watch] = new { name = "献酿真实已付获取子窗", description = "真实获取完成后暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            for (var i = 1; i < 4; i++) b.AddGeneral(new(Peer(i), "固定安静同桌", "", Quiet, "wei", 12));
            b.AddDeck(new(Deck, "固定无普通摸牌的真实库存", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 10)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "侯成真实两项次数和永久酒实体", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
