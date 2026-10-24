using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiDengAiChecks
{
    private const string Hero = "mobile:shi-deng-ai", Tuntian = "mobile:shi-tuntian", Jixi = "mobile:shi-jixi", Driver = "fixture:shi-deng-ai-driver", Quiet = "fixture:shi-deng-ai-quiet", WatchSkill = "fixture:shi-deng-ai-watch", Peer = "fixture:shi-deng-ai-peer", Deck = "fixture:shi-deng-ai-deck";
    private static string? Branch(PromptChoice choice) => choice.Parameters.GetValueOrDefault("branch");
    public static void FullCapacityPerEntityLossAndThresholdSpendPaidChildCold()
    {
        var s = Scene.Start(grow: true);
        s.Watch("full-paid");
        s.Need(s.E<ShiDaDrawPaidEvent>().Length == 1 && s.Charge() == (0, 0), "Initial zero-of-zero is full: one actual native draw precedes the capacity increase.");
        var full = s.Root!.Id; s.Cold(); s.Replay(); s.Continue("full-paid"); s.Play();
        s.Need(s.Charge() == (1, 1) && s.E<ShiDaDrawPaidEvent>().Single().FrameId == full, "The resumed first full-charge draw grows capacity once; the later real loss fills the new point.");
        while (s.G.State.TurnNumber < 9) { s.End(); s.Play(); }
        s.Need(s.Charge() == (9, 9) && s.E<ShiDaDrawPaidEvent>().Length == 9, "Each real own or foreign turn sees the prior full charge once, with no fixed capacity ceiling.");
        var rewards = new[] { CardKind.DrawTwo, CardKind.Nullification, CardKind.FiveGrains }.Select(kind => s.G.CreateCardZoneDiagnostics().Single(c => c.Location == CardLocation.DiscardPile && c.CardKind == kind).CardId).ToArray();
        s.Activate(Tuntian, "spend-charge"); s.Stage(ShiDaStage.Amount); s.Answer(c => c.Parameters.GetValueOrDefault("number") == "7");
        s.Answer(c => Branch(c) == "target" && c.Targets.SequenceEqual([0])); s.Answer(c => Branch(c) == "target" && c.Targets.SequenceEqual([1]));
        s.Need(s.Charge() == (9, 9) && s.E<ShiDaSpentEvent>().Length == 0, "Choosing points and recipients remains an unpaid editable draft until confirmation.");
        s.Cold(); s.Answer(c => Branch(c) == "spend"); s.Stage(ShiDaStage.Rewards); var frame = s.Root!.Id;
        s.Need(s.Charge() == (2, 9) && s.E<ShiDaSpentEvent>() is [{ Amount: 7, Recipients: 2, Zaoxian: not null }], "One confirmed payment debits seven points while allowing fewer than seven recipients and freezing the locked reward source.");
        s.Answer(c => Branch(c) == "reward" && c.Cards.Contains(rewards[0])); s.Watch("reward-paid");
        s.Need(s.Zone(rewards[0]) == CardLocation.Hand(0) && s.E<ShiDaThresholdObtainedEvent>().Length == 1, "The first threshold obtains its real discard entity before the native gain child.");
        s.Cold(); s.Replay(); s.Continue("reward-paid");
        for (var i = 1; i < rewards.Length; i++) { var index = i; s.Stage(ShiDaStage.Rewards); s.Answer(c => Branch(c) == "reward" && c.Cards.Contains(rewards[index])); }
        s.Play();
        s.Need(s.E<ShiDaThresholdObtainedEvent>().Where(e => e.FrameId == frame).Select(e => e.Threshold).SequenceEqual([3, 5, 7]) && rewards.All(id => s.Zone(id) == CardLocation.Hand(0)) &&
            s.E<ShiDaRandomIssuedEvent>().Where(e => e.FrameId == frame).Select(e => e.Recipient).SequenceEqual([0, 1]) &&
            s.E<ShiDaRandomIssuedEvent>().Where(e => e.FrameId == frame).All(e => e.CardId > 0 && s.V(e.Recipient).Hand.Single(c => c.Id == e.CardId).Suit == Suit.Heart) && s.E<ShiDaSpentEvent>().Length == 1,
            "Seven points independently grant all three current thresholds, then each chosen recipient obtains one actual random Heart without repaying the debit.");
        var before = s.Charge(); var movement = s.G.CardMovements.Count;
        s.Activate(Driver, "draw-and-drop"); s.Play();
        var lost = s.G.CardMovements.Skip(movement).Where(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile).ToArray();
        s.Need(lost.Length >= 2 && s.Charge() == (Math.Min(before.Capacity, before.Current + lost.Length), before.Capacity), "One real batch containing several non-damage entities gains one point for each entity, capped by the current capacity.");
        s.Cold(); s.Replay();
    }
    public static void ActualTurnEndDiscardAndOneDistanceFreeMultiTargetSnatchCold()
    {
        var s = Scene.Start(grow: false); s.Play(); s.Need(s.Charge() == (0, 1), "The initial real full-charge start grows zero-of-zero before the first Play.");
        s.Native(CardKind.BaguaFormation); s.Watch("equipment-paid");
        var equipmentUse = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.ShiDengAiEquipmentLoss is not null);
        s.Need(equipmentUse.ShiDengAiEquipmentLoss is { Settled: false, PlacementSequence: null } receipt && s.Zone(receipt.CardId) == CardLocation.Processing && s.Charge() == (0, 1),
            "The actual equipment payment retains its exact original native frame while placement is pending, without prematurely gaining charge.");
        s.Cold(); s.Replay(); s.Continue("equipment-paid"); s.Play();
        s.Need(s.E<ShiDaEquipmentLossSettledEvent>() is [{ UseId: var settledUse, Lost: false, PlacementSequence: not null }] && settledUse == equipmentUse.Id,
            "The same native equipment payment returns through its own real placement once after cold recovery.");
        s.Need(s.Charge() == (0, 1), "Moving a hand equipment card into one's own equipment area is not an actual loss.");
        s.Native(CardKind.IronChain, [1, 2]); s.Play();
        s.Need(s.Charge() == (1, 1) && s.E<ShiDaTargetRecordedEvent>().Where(e => e.Actor == 0).Select(e => e.Target).Distinct().Order().SequenceEqual([1, 2]), "The real non-damage use gains charge and records the actual actor's two native designated targets.");
        s.Activate(Tuntian, "spend-charge"); s.Stage(ShiDaStage.Amount); s.Answer(c => c.Parameters.GetValueOrDefault("number") == "1"); s.Answer(c => Branch(c) == "spend"); s.Play();
        s.Need(s.Charge() == (0, 1) && s.E<ShiDaSpentEvent>() is [{ Amount: 1, Recipients: 0 }], "Tuntian may spend a point with fewer recipients, including none.");
        s.Native(CardKind.Slash, [1]); s.Play(); s.Need(s.Charge() == (0, 1), "A physical damage-card loss does not fund Tuntian.");
        var armor = s.V(0).Equipment.Single().Id; s.End();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Jixi && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        s.Need(s.G.Events.Any(e => e.Payload is TurnEndedEvent { TurnNumber: 1, ActorSeat: 0 }) && s.G.State.Phase == TurnPhase.Finished, "Jixi starts after the actual turn has ended, rather than at the ending phase.");
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); s.Stage(ShiDaStage.Cost);
        s.Answer(c => Branch(c) == "cost" && c.Cards.Contains(armor)); s.Watch("cost-paid"); var frame = s.Root!.Id;
        s.Need(s.Zone(armor) == CardLocation.DiscardPile && s.E<ShiDaCostPaidEvent>() is [{ Owner: 0, CardId: var paid }] && paid == armor &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == frame && e.ActualCount == 1 && e.Reason.Value == "skill.mobile-shi-deng-ai.jixi.discard") == 1,
            "One real current-turn owner's equipment discard is paid once before its observer child.");
        s.Cold(); s.Replay(); s.Continue("cost-paid"); s.Stage(ShiDaStage.Targets);
        s.Answer(c => Branch(c) == "target" && c.Targets.SequenceEqual([1])); s.Answer(c => Branch(c) == "target" && c.Targets.SequenceEqual([2]));
        var hands = new[] { s.V(1).HandCount, s.V(2).HandCount }; s.Answer(c => Branch(c) == "snatch");
        s.Reach(() => s.P?.Kind == DecisionKind.SelectTargetCard && s.Root?.ShiDengAi?.Stage == ShiDaStage.NativeUse);
        var issued = s.E<ShiDaSnatchIssuedEvent>().Single(); var native = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == issued.UseId);
        s.Need(native.CardKind == CardKind.Snatch && native.Action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0, PhysicalCards.Count: 0, ConversionChain.Count: 0 } a && a.ActionId == issued.ActionId && a.TargetSeats.SequenceEqual([1, 2]) &&
            s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == issued.UseId && e.CardKind == CardKind.Snatch) == 1,
            "Jixi issues one complete zero-entity native Snatch on both historical targets, including the seat at ordinary distance two.");
        s.Cold(); s.Replay(); s.Reach(() => s.E<ShiDaSnatchReturnedEvent>().Length == 1);
        s.Need(s.V(1).HandCount == hands[0] - 1 && s.V(2).HandCount == hands[1] - 1 && s.E<ShiDaCostPaidEvent>().Length == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId && e.CardKind == CardKind.Snatch) == 1 && s.G.ResolutionStack.All(f => f.Id != issued.UseId),
            "The restored original Snatch resolves both real target selections and returns once without paying another cost or creating a second card use.");
        s.Cold(); s.Replay();
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.ShiDengAi is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public (int Current, int Capacity) Charge() { var e = E<ShiDaChargeChangedEvent>().LastOrDefault(e => e.Source.OwnerSeat == 0); return e is null ? (0, 0) : (e.After, e.CapacityAfter); }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Stage(ShiDaStage stage) => Reach(() => Root?.ShiDengAi?.Stage == stage && P is { PlayerSeat: 0 });
        public void Watch(string option) => Reach(() => P?.SkillPrompt?.SkillId == WatchSkill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option));
        public void Continue(string option) => Answer(c => c.Parameters.GetValueOrDefault("option-id") == option);
        public void Activate(string skill, string activation) => Accept(new UseProgramSkillCommand(0, skill, activation, [], [], G.Revision, P!.PromptId));
        public void Native(CardKind kind, IReadOnlyList<int>? targets = null)
        {
            var a = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is { } id && V(0).Hand.Any(c => c.Id == id && c.Kind == kind) && a.Kind != LegalActionKind.Recast && a.ConversionSource is null && (targets is null || a.TargetSeats.SequenceEqual(targets)));
            Need(a is not null, "The original hand material retains its actual native action."); Accept(new PlayCardCommand(0, a!.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void Answer(Func<PromptChoice, bool> select) { var p = P!; var c = p.Choices.FirstOrDefault(select); Need(c is not null, "The expected original choice exists."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready, [CallerLineNumber] int line = 0)
        {
            for (var n = 0; n < 1400; n++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction || p.Kind == DecisionKind.SelectTargetCard) { Answer(_ => true); continue; }
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                    if (p.Choices.Any(c => c.Parameters.ContainsKey("option-id"))) { Answer(c => c.Parameters.ContainsKey("option-id")); continue; }
                    if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is not "bagua"); continue; }
                    throw new InvalidOperationException("Unexpected Shi Deng Ai boundary at " + line + " " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The small table remains alive."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The bounded native Shi Deng Ai boundary was not reached at " + line);
        }
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold recovery retains the same original paid child, private sample and native Use."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "The actual command journal reproduces original capacity, debit, target history and native card use."); }
        public void Need(bool good, string why) { if (!good) throw new InvalidOperationException(why + " " + Dump()); }
        private void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected native Shi Deng Ai command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements, Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, P, Receipt = Root?.ShiDengAi, Charge = Charge(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray() });
        public static Scene Start(bool grow)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(grow));
            ShanheStartingCard[] own = grow ? [new(CardKind.DrawTwo, Suit.Club, 3), new(CardKind.Nullification, Suit.Spade, 4), new(CardKind.FiveGrains, Suit.Diamond, 5)] :
                [new(CardKind.IronChain, Suit.Club, 3), new(CardKind.Slash, Suit.Spade, 4), new(CardKind.BaguaFormation, Suit.Heart, 5)];
            var setup = new ShanheBattleSetup(643, 1, "shi-deng-ai-native", Hero, 4, 0, 0, 0, 0, [Driver, WatchSkill], own, [],
                [new(Peer, Hp: 8) { StartingHand = [new(CardKind.Peach, Suit.Club, 6), new(CardKind.Peach, Suit.Diamond, 7)] }, new(Peer, Hp: 8) { StartingHand = [new(CardKind.Peach, Suit.Club, 8), new(CardKind.Peach, Suit.Diamond, 9)] }],
                [new(Peer, Hp: 8) { StartingHand = [new(CardKind.Peach, Suit.Club, 10)] }], new string('a', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
                ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture(bool grow) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-deng-ai", new(1, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            var growTrigger = grow ? """,{"id":"fill-after-full","window":"turnStartBeforeNormalFlow","subject":"owner","turnOwnerScope":"anyLiving","priority":-10,"optional":false,"isSkillInvocation":false,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]}""" : "";
            var fullWatch = grow ? """{"id":"full-paid","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.mobile-shi-deng-ai.tuntian.full"],"optional":false,"isSkillInvocation":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"full-paid","options":[{"id":"full-paid"}]}]},""" : "";
            var equipmentWatch = grow ? "" : """{"id":"equipment-paid","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardCategories":["equipment"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"equipment-paid","options":[{"id":"equipment-paid"}]}]},""";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}],"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}{{growTrigger}}],
                "activations":[{"id":"draw-and-drop","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":2},{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},{"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{WatchSkill}}","revision":1,"triggers":[{{fullWatch}}{{equipmentWatch}}
                {"id":"reward-paid","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.mobile-shi-deng-ai.zaoxian.obtain"],"optional":false,"isSkillInvocation":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"reward-paid","options":[{"id":"reward-paid"}]}]},
                {"id":"cost-paid","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perBatch","movementReasons":["skill.mobile-shi-deng-ai.jixi.discard"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost-paid","options":[{"id":"cost-paid"}]}]}]}
            ]}
            """;
            var optionLabels = new Dictionary<string, string> { ["reward-paid"] = "继续原凿险", ["cost-paid"] = "继续原急袭" };
            if (grow) optionLabels["full-paid"] = "继续原摸牌";
            else optionLabels["equipment-paid"] = "继续原装备";
            var presentations = new Dictionary<string, object> { [Driver] = new { name = "真实失牌驱动", description = "保留普通回合与真实失牌。" }, [Quiet] = new { name = "安静同桌", description = "不干扰原生主流程。" },
                [WatchSkill] = new { name = "已付原生子窗", description = "保留原付款与恢复边界。", optionLabels } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, p) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Peer, "真实同桌", "", Quiet, "wei", 8)); builder.AddDeck(new(Deck, "固定真实红桃牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "势邓艾实际原生流程", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
