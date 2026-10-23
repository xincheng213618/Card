using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class AnniversaryXiahouHuiChecks
{
    private const string Hero = "anniversary:xiahou-hui", Dujun = "anniversary:dujun", Jikun = "anniversary:jikun",
        Driver = "fixture:xiahou-hui-driver", Counter = "fixture:xiahou-hui-counter", Watch = "fixture:xiahou-hui-watch",
        Quiet = "fixture:xiahou-hui-quiet", Peer = "fixture:xiahou-hui-peer", Partner = "fixture:xiahou-hui-partner", Deck = "fixture:xiahou-hui-deck";
    public static void FirstDamageRolesWholeDrawnGiftAndLossThresholdsCold()
    {
        var s = Scene.Start();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Dujun && s.P.Choices.Any(c => Key(c) == "select-target" && c.Targets.SequenceEqual([1])));
        s.Answer(c => c.Targets.SequenceEqual([1])); s.Play();
        var relation = s.E<XiahouHuiBoundEvent>().Single(); var turn = s.G.State.TurnNumber;
        s.Need(relation.Source.OwnerSeat == 0 && relation.Partner == 1 && s.V(0).HandCount == 4 && s.V(0).Hp == 3,
            "The actual GameStarting owner binds one real other character without inventing an opening draw.");
        var crossbow = s.V(0).Hand.Single(c => c.Kind == CardKind.Crossbow).Id;
        var slash = s.V(0).Hand.Single(c => c.Kind == CardKind.Slash).Id;
        var dodges = s.V(0).Hand.Where(c => c.Kind == CardKind.Dodge).Select(c => c.Id).Order().ToArray();
        s.Card(crossbow); s.Play(); s.Card(slash, 1); var firstSkippedEarly = s.PreparePair("self-first-caused", "partner-first-received");
        var giftRoot = s.Root!; var actualDrawn = giftRoot.XiahouHui!.Drawn.ToArray(); var donorHand = s.V(0).HandCount;
        s.Need(actualDrawn.Length == 2 && actualDrawn.All(id => s.Zone(id) == CardLocation.Hand(0)) && s.V(1).Hp == 9,
            "The original physical Slash really deals damage and the first own-source benefit holds exactly its two actual drawn entities.");
        s.Answer(c => c.Targets.SequenceEqual([3])); s.GiftChild();
        var paidGift = s.Root!.XiahouHui!;
        s.Need(paidGift.Stage == XiahouHuiStage.MovementChildren && paidGift.Moves.Count == 2 && paidGift.Moves.All(m => actualDrawn.Contains(m.CardId) && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(3)) &&
            s.V(0).HandCount == donorHand - 2 && actualDrawn.All(id => s.Zone(id) == CardLocation.Hand(3)) &&
            s.E<XiahouHuiLossRecordedEvent>().Last().Count == 4,
            "The whole native Give spends only those two drawn entities, in one actual paid batch, and counts real hand/equipment losses by card.");
        s.Cold(); s.Continue(); s.CompletePair("partner-first-received", firstSkippedEarly); s.Play();
        s.Need(s.E<XiahouHuiMovedEvent>().Count(e => e.FrameId == giftRoot.Id) == 1 && s.E<XiahouHuiStartedEvent>().Count(e => e.FrameId == giftRoot.Id) == 1,
            "Cold return does not redraw, repay or reissue the completed native whole-gift instruction.");

        s.Use("discard-one", [dodges[0]]); s.Offer(Jikun, "five-hand-loss"); s.Activate(); s.Menu();
        var threshold = s.Root!.XiahouHui!;
        s.Need(threshold.ThresholdSequence == s.E<XiahouHuiLossRecordedEvent>().Single(e => e.Count == 5).Sequence &&
            s.P!.Choices.Any(c => c.Targets.SequenceEqual([2, 3])) && s.P.Choices.All(c => c.Targets[0] != 0),
            "The fifth real owned-card departure opens one original threshold and offers a genuine other recipient and a current maximum-hand donor.");
        var thirdHand = s.V(3).Hand.Select(c => c.Id).ToHashSet(); var recipientHand = s.V(2).HandCount;
        s.Answer(c => c.Targets.SequenceEqual([2, 3])); s.Play();
        var randomMove = s.E<XiahouHuiMovedEvent>().Single(e => e.FrameId != giftRoot.Id);
        s.Need(randomMove.Recipient == 2 && randomMove.Donor == 3 && randomMove.Count == 1 && s.V(2).HandCount == recipientHand + 1 &&
            s.G.CardMovements.Count(m => m.Sequence > randomMove.Before && m.Sequence <= randomMove.After && thirdHand.Contains(m.CardId) && m.From == CardLocation.Hand(3) && m.To == CardLocation.Hand(2)) == 1,
            "Jikun obtains one real random HE entity from the frozen maximum-hand donor rather than fabricating a draw or exposing a hidden-card menu.");

        var retainedDodge = dodges[1]; var hp = s.V(0).Hp;
        s.End(); s.DriverDone(); s.Continue(); var secondSkippedEarly = s.PreparePair("self-first-received", "partner-first-caused");
        s.Answer(c => c.Targets.SequenceEqual([2])); s.GiftChild(); s.Continue(); s.CompletePair("partner-first-caused", secondSkippedEarly);
        s.Reach(() => s.E<CardUseDeclaredEvent>().Where(e => e.SourceSeat == 1 && e.CardKind == CardKind.Slash).ToArray() is [var first, var second] &&
            s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == first.ResolutionId) && s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == second.ResolutionId));
        var nativeSlashes = s.E<CardUseDeclaredEvent>().Where(e => e.SourceSeat == 1 && e.CardKind == CardKind.Slash).ToArray();
        s.Need(nativeSlashes.Length == 2 && nativeSlashes.All(e => s.E<CardUseFinishedEvent>().Any(done => done.ResolutionId == e.ResolutionId)) &&
            s.V(0).Hp == hp - 2 && s.Zone(retainedDodge) == CardLocation.Hand(0) && s.G.State.TurnNumber == turn &&
            s.E<XiahouHuiDamageRecordedEvent>().Count(e => e.Turn == turn && e.Source == 1 && e.Target == 0) == 2 &&
            s.Answers(Dujun, "self-first-caused", "activate") == 1 && s.Answers(Dujun, "partner-first-received", "skip") == 1 &&
            s.Answers(Dujun, "self-first-received", "activate") == 1 && s.Answers(Dujun, "partner-first-caused", "skip") == 1 &&
            s.E<XiahouHuiLossRecordedEvent>().Last().Count == 7,
            "The selected partner's two genuine native Slashes cannot be answered by the retained Dodge; all four independent actual-turn first roles are offered once, and declined roles do not reoffer on the second damage.");
        s.Play();
        var beforeLoss = s.E<XiahouHuiLossRecordedEvent>().Last().Count; var beforeBatches = s.E<XiahouHuiStartedEvent>().Length;
        var actualHand = s.V(0).HandCount;
        s.Use("stock-and-discard"); s.Offer(Jikun, "five-hand-loss");
        s.Need(beforeLoss == 7 && actualHand == 1 && s.V(0).HandCount == 0 && s.E<XiahouHuiLossRecordedEvent>().Last().Count == 18,
            "A single native Draw10 and whole-Hand logical discard really crosses two distinct cumulative thresholds in one original batch.");
        s.Skip(); s.Offer(Jikun, "five-hand-loss"); s.Activate(); s.Menu(); s.Answer(c => c.Targets.SequenceEqual([3, 2])); s.Play();
        var starts = s.E<XiahouHuiStartedEvent>().Skip(beforeBatches).Where(e => e.Operation == SkillProgramEffectOp.ResolveAnniversaryXiahouHuiJikun).ToArray();
        s.Need(starts is [var fifteenth] && s.E<XiahouHuiLossRecordedEvent>().Single(e => e.Sequence == fifteenth.ThresholdSequence).Count == 15 &&
            s.Answers(Jikun, "five-hand-loss", "activate") == 2 && s.Answers(Jikun, "five-hand-loss", "skip") == 1 &&
            s.E<XiahouHuiLossRecordedEvent>().Count(e => e.Count % 5 == 0) == 3,
            "Declining the tenth-card opportunity leaves the same batch's fifteenth-card opportunity intact, with no duplicate payment or reset of the cumulative loss count.");
        s.Replay();
    }
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.XiahouHui is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Offer(string skill, string binding) => Reach(() => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => Key(c) == "activate" && c.Parameters.GetValueOrDefault("binding-id") == binding));
        public bool PreparePair(string invoke, string skip)
        {
            Reach(() => P?.SkillPrompt?.SkillId == Dujun && P.Choices.Any(c => Key(c) == "activate" && c.Parameters.GetValueOrDefault("binding-id") is { } id && (id == invoke || id == skip)));
            var skippedEarly = P!.Choices.Any(c => Key(c) == "activate" && c.Parameters.GetValueOrDefault("binding-id") == skip);
            if (skippedEarly) Skip();
            Offer(Dujun, invoke); Activate(); Menu(); return skippedEarly;
        }
        public void CompletePair(string skipped, bool skippedEarly) { if (!skippedEarly) { Offer(Dujun, skipped); Skip(); } }
        public void Menu() => Reach(() => Root?.XiahouHui?.Stage == XiahouHuiStage.Recipient && P?.Choices.Any(c => Key(c) == "anniversary-xiahou-hui") == true);
        public void GiftChild() => Reach(() => P?.SkillPrompt?.SkillId == Watch && Root?.XiahouHui?.Stage == XiahouHuiStage.MovementChildren);
        public void DriverDone() => Reach(() => P?.SkillPrompt?.SkillId == Driver && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "done"));
        public void Activate() => Answer(c => Key(c) == "activate");
        public void Skip() => Answer(c => Key(c) == "skip");
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") is "continue" or "done");
        public void Use(string id, int[]? cards = null) => Accept(new UseProgramSkillCommand(0, Driver, id, cards ?? [], [], G.Revision, P!.PromptId));
        public int Answers(string skill, string binding, string action) => G.AcceptedCommands.OfType<AnswerPromptCommand>().Count(c => c.Choice.Value.StartsWith($"program-trigger.{action}.{skill}.{binding}.", StringComparison.Ordinal));
        public void Card(int id, int? target = null)
        {
            var a = G.GetHumanLegalActions().First(a => a.CardId == id && (target is null || a.TargetSeats.SequenceEqual([target.Value])));
            Accept(new PlayCardCommand(0, id, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(choice is not null, "The actual original Xiahou Hui choice must be published.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision));
        }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 320; i++)
            {
                if (ready()) return;
                if (P is { Kind: DecisionKind.SelectFaction }) { Answer(_ => true); continue; }
                if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Xiahou Hui native boundary. " + Dump());
                Need(G.State.Winner == Winner.None, "The short original native scenario retains all participants."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Xiahou Hui did not reach her bounded native continuation.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "The exact already-paid native whole-gift, hidden drawn entities and movement child cold-restore identically."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "The original partner, four first-damage roles, drawn-card gifts and cumulative random obtain thresholds replay identically."); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Root = Root?.XiahouHui,
            Views = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).HandCount }), Loss = E<XiahouHuiLossRecordedEvent>(),
            Last = G.Events.TakeLast(10).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected genuine Xiahou Hui command."); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(47, 1, "anniversary-xiahou-hui", Hero, 3, 0, 0, 0, 0, [Driver, Watch],
                [new(CardKind.Crossbow, Suit.Spade, 5), new(CardKind.Slash, Suit.Heart, 6), new(CardKind.Dodge, Suit.Club, 7), new(CardKind.Dodge, Suit.Diamond, 8)], [],
                [new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }], [new(Partner) { StartingHand = [] }], new string('e', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, AiPolicyVersion = AiDecisionPolicy.CurrentVersion, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-xiahou-hui", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var catalog = SkillProgramCatalog.Load($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"activations":[
                {"id":"discard-one","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
                {"id":"stock-and-discard","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":10},{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]}],
                "triggers":[{"id":"owner-ending-watch","window":"playEnding","subject":"owner","optional":false,"isSkillInvocation":false,"priority":100,"effects":[{"op":"chooseOption","target":"owner","resultBind":"done","options":[{"id":"done"}]}]}]},
              {"id":"{{{Counter}}}","revision":1,"triggers":[{"id":"two-real-partner-slashes","window":"playEnding","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"isSkillInvocation":false,"usageScope":"game","usageLimit":1,"priority":0,"effects":[
                {"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"useVirtualSlash","target":"selectedTarget"},{"op":"useVirtualSlash","target":"selectedTarget"}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"real-gift-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:dujun.give-drawn"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            {
                [Driver] = new { name = "真实失牌及原回合停点", description = "原生弃置、摸牌和实际出牌阶段结束。", optionLabels = new Dictionary<string, string> { ["done"] = "继续原回合" } },
                [Counter] = new { name = "所选角色的两次原生杀", description = "实际使用两张零实体杀，正常进入响应与伤害。" },
                [Quiet] = new { name = "安静同桌", description = "保留真实回合，跳过主动出牌。" },
                [Watch] = new { name = "实际整包交牌子窗", description = "已付实际交牌后的原生移动观察。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            } }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实安静同桌", "", Quiet, "wei", 10));
            b.AddGeneral(new(Partner, "真实笃君同伴", "", Quiet, "wei", 10) { AdditionalSkillIds = [Counter] });
            b.AddDeck(new(Deck, "固定原生摸牌库存", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "夏侯徽真实首伤与失牌五阈值", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer, Partner], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
