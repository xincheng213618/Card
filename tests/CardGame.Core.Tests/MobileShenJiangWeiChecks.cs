using System.Globalization;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class MobileShenJiangWeiChecks
{
    private const string Hero = "mobile:shen-jiang-wei", Driver = "fixture:mobile-jw-driver", Watch = "fixture:mobile-jw-watch", Rescue = "fixture:mobile-jw-rescue", Quiet = "fixture:mobile-jw-quiet", Mode = "identity:classic-mobile-jw-fixture";
    public static void XinghunPhysicalExchangeAndOrderedSlashesCold()
    {
        var (g, registry) = Start(); Play(g);
        Need(V(g).Hand.Any(c => c.Kind == CardKind.Slash) && V(g).Hand.Any(c => c.Kind == CardKind.Dodge), "The small fixed physical deck supplies an actual Slash and Dodge without searching seeds.");
        var keep = new[] { V(g).Hand.First(c => c.Kind == CardKind.Slash).Id, V(g).Hand.First(c => c.Kind == CardKind.Dodge).Id };
        while (V(g).HandCount > 2) Give(g, V(g).Hand.First(c => !keep.Contains(c.Id)).Id, 2);
        Accept(g, new UseProgramSkillCommand(0, "mobile:xinghun", "five-stars", [], [], g.Revision, P(g)!.PromptId));
        Menu(g, MobileJiangWeiStage.Exchange); Answer(g, c => Branch(c) == "hand" && c.Cards.SequenceEqual([keep[0]]));
        Menu(g, MobileJiangWeiStage.ExchangeTop);
        var top = P(g)!.Choices.FirstOrDefault(c => Branch(c) == "top" && c.Label.Contains("闪", StringComparison.Ordinal)) ?? P(g)!.Choices.First(c => Branch(c) == "top");
        var topId = top.Cards.Single(); var ownHand = V(g).HandCount;
        Answer(g, c => c.Id == top.Id); Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var root = Root(g)!; var rootId = root.Id;
        Need(root.MobileJiangWei is { Stage: MobileJiangWeiStage.ExchangeChildren, Paid.Count: 2 } && root.PendingMovementContinuation is not null &&
            Zone(g, topId) == CardLocation.Hand(0) && Zone(g, keep[0]) == CardLocation.DrawPile && V(g).HandCount == ownHand,
            "Xinghun truly exchanges equal physical cards before its original paid movement child; it never turns this into drawing or a private public event.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Menu(g, MobileJiangWeiStage.Exchange); Answer(g, c => Branch(c) == "exchange-done");
        while (Root(g)?.MobileJiangWei?.Stage == MobileJiangWeiStage.Ordering || P(g) is null)
        {
            Reach(g, p => p.SkillPrompt?.SkillId == "mobile:xinghun");
            if (Root(g)?.MobileJiangWei?.Stage != MobileJiangWeiStage.Ordering) break;
            var choices = P(g)!.Choices; Answer(g, c => c.Id == (choices.FirstOrDefault(c => c.Cards.SequenceEqual([keep[0]])) ?? choices.First()).Id);
        }
        Menu(g, MobileJiangWeiStage.Target); Answer(g, c => c.Targets.SequenceEqual([1])); Play(g);
        var issued = E<MobileJiangWeiUseIssuedEvent>(g).Where(e => e.Return.ParentId == rootId).Select(e => e.Return).ToArray();
        var shown = E<CardsRevealedEvent>(g).Where(e => e.ResolutionId == rootId).SelectMany(e => e.Cards).ToArray();
        Need(shown.Length == 5 && shown.Select(c => c.Id).Distinct().Count() == 5 && issued.Length > 0 && issued.Any(e => e.From == CardLocation.Processing) &&
            issued.All(e => e.Kind == CardKind.Slash && E<MobileJiangWeiUseReturnedEvent>(g).Count(r => r.FrameId == rootId && r.UseId == e.UseId && r.ActionId == e.ActionId) == 1) &&
            g.CreateCardZoneDiagnostics().All(c => c.Location != CardLocation.Processing) && E<MobileJiangWeiCompletedEvent>(g).Count(e => e.FrameId == rootId) == 1,
            "The other character reveals five distinct real hand/top cards; each payable Slash completes its native Use once and no unspent shown deck card leaks in Processing.");
        Use(g, "draw"); Play(g);
        Need(g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash), "The physical Xinghun sequence does not debit the owner's normal Slash quota.");
        var replay = GameReplay.Replay(Options(), g.AcceptedCommands, registry);
        Need(State(g) == State(replay), "The exact exchange, private order, foreign opaque choices and physical Slash stream replay from commands.");
    }
    public static void TiantaoShenpeiAndHuitianUseRealNativeReturns()
    {
        var (g, registry) = Start(); Play(g);
        var dodge = V(g).Hand.First(c => c.Kind == CardKind.Dodge).Id; Give(g, dodge, 1);
        var slot = V(g, 1).HandCount - 1; var initialHp = V(g).Hp;
        for (var n = 0; n < initialHp; n++) { Use(g, "hurt"); if (n < initialHp - 1) Play(g); }
        Offer(g, "mobile:shenpei"); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Play(g);
        Need(E<PlayerDyingEvent>(g).Count(e => e.VictimSeat == 0) == 1 && V(g).Hp == 1 && E<MobileJiangWeiStartedEvent>(g).All(e => e.Operation != SkillProgramEffectOp.ResolveMobileJiangWeiShenpei),
            "Declining Shenpei leaves its game limit unused; the separate native one-point rescue ends the first genuine Dying.");
        Use(g, "hurt"); Offer(g, "mobile:shenpei"); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Menu(g, MobileJiangWeiStage.ThunderTarget);
        var root = Root(g)!; var rootId = root.Id; var before = V(g, 1).Hp;
        Need(root.MobileJiangWei is { Amount: 2, DyingId: > 0 } r && V(g).Hp == 2 && E<PlayerDyingEvent>(g).Count(e => e.VictimSeat == 0) == 2 &&
            g.ResolutionStack.OfType<DyingFrame>().Any(d => d.Id == r.DyingId && d.VictimSeat == 0),
            "Shenpei recovers two, including its second actual entering-Dying occurrence, while preserving the original native Dying parent until the whole skill returns.");
        g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([1])); Play(g);
        Need(V(g, 1).Hp == before - 2 && V(g).Hp == 2 && E<MobileJiangWeiCompletedEvent>(g).Count(e => e.FrameId == rootId) == 1 &&
            E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 2 && e.Nature == DamageNature.Thunder),
            "The exact restored Shenpei deals native two-point thunder damage, then grants Huitian and returns its original Dying only once.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Menu(g, MobileJiangWeiStage.Region); Answer(g, c => Branch(c) == "region" && Value(c) == (int)CardZoneKind.Hand);
        Menu(g, MobileJiangWeiStage.OtherTargets); Answer(g, c => Branch(c) == "discard-target" && c.Targets.SequenceEqual([2]));
        Menu(g, MobileJiangWeiStage.OtherTargets); Answer(g, c => Branch(c) == "discard-target" && c.Targets.SequenceEqual([1]));
        Menu(g, MobileJiangWeiStage.OtherTargets); Answer(g, c => Branch(c) == "targets-done");
        Menu(g, MobileJiangWeiStage.DiscardCard); Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == slot.ToString(CultureInfo.InvariantCulture));
        Menu(g, MobileJiangWeiStage.DiscardCard); Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == "0");
        Offer(g, "mobile:huitian");
        var tiantao = E<MobileJiangWeiStartedEvent>(g).Last(e => e.Operation == SkillProgramEffectOp.ResolveMobileJiangWeiTiantao);
        Need(V(g, 1).Hp == before - 3 && Zone(g, dodge) == CardLocation.DiscardPile && g.CardMovements.Count(m => m.CardId == dodge && m.Reason.Value == "mobile.tiantao.discard") == 1 &&
            g.CardMovements.Where(m => m.Reason.Value == "mobile.tiantao.discard" && m.From.OwnerSeat != 0).Select(m => m.From.OwnerSeat).SequenceEqual(new int?[] { 1, 2 }),
            "Tiantao discards the selected actual opaque foreign Dodge and causes that character exactly one additional native HP loss.");
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Play(g);
        Need(E<TurnStartedEvent>(g).Count(e => e.ActorSeat == 0) == 2 && E<ProgramBindingStartedEvent>(g).Count(e => e.OwnerSeat == 0 && e.SkillId == "mobile:huitian" && e.BindingId == "higher-hp-extra-turn") == 1,
            "Huitian draws through the ordinary producer and really inserts an extra turn, rather than skipping other characters or just setting a marker.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Menu(g, MobileJiangWeiStage.Region); var hp = V(g).Hp;
        Answer(g, c => Branch(c) == "region" && Value(c) == (int)CardZoneKind.Equipment);
        Menu(g, MobileJiangWeiStage.OtherTargets); Answer(g, c => Branch(c) == "targets-done");
        for (var n = 0; n < 240 && V(g).IsAlive; n++)
        {
            if (P(g) is { } p)
            {
                Need(p.SkillPrompt?.SkillId == "mobile:huitian", "Only remaining real turn-end Huitian offers may interrupt the next round.");
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        Need(hp > 0 && !V(g).IsAlive && E<PlayerDyingEvent>(g).Count(e => e.VictimSeat == 0) == 2 && E<ProgramSkillCauseDeathDeclaredEvent>(g).Count(e => e.SkillId == "mobile:huitian" && e.TargetSeat == 0) == 1,
            "An empty equipment region is legal and costs no HP; the following round causes actual Huitian direct death without inventing another Dying entry.");
        _ = tiantao;
    }
    private static GameOptions Options()
    {
        var setup = new ShanheBattleSetup(7, 1, "mobile-jiang-wei", Hero, 4, 0, 0, 0, 0, [],
            [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Slash, Suit.Club, 4), new(CardKind.Dodge, Suit.Diamond, 6)], [],
            [new("fixture:mobile-jw-peer-2") { StartingHand = [new(CardKind.Slash, Suit.Club, 7), new(CardKind.Dodge, Suit.Heart, 9)] }, new("fixture:mobile-jw-peer-3") { StartingHand = [new(CardKind.Slash, Suit.Spade, 8)] }],
            [new("fixture:mobile-jw-peer-1") { StartingHand = [new(CardKind.Slash, Suit.Club, 7), new(CardKind.Dodge, Suit.Heart, 9)] }], new string('f', 64)) { DefaultGodFactionId = "shu" };
        return new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 };
    }
    private static (GameEngine, ContentRegistry) Start()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture()); var g = GameEngine.CreateStandard(Options(), r);
        Accept(g, new StartGameCommand()); return (g, r);
    }
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static ProgramSkillFrame? Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.MobileJiangWei is not null);
    private static CardLocation Zone(GameEngine g, int id) => g.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
    private static string? Branch(PromptChoice c) => c.Parameters.GetValueOrDefault("branch");
    private static int Value(PromptChoice c) => int.TryParse(c.Parameters.GetValueOrDefault("value"), CultureInfo.InvariantCulture, out var n) ? n : -1;
    private static void Need(bool valid, string why) { if (!valid) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected mobile Jiang Wei real command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick)
    { var p = P(g)!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "Missing Jiang Wei published choice: " + JsonSerializer.Serialize(p)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision)); }
    private static void Use(GameEngine g, string id, int[]? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Menu(GameEngine g, MobileJiangWeiStage stage) => Reach(g, p => Root(g)?.MobileJiangWei?.Stage == stage && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mobile-jiang-wei"));
    private static void Offer(GameEngine g, string skill) => Reach(g, p => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
    private static void Give(GameEngine g, int card, int target)
    { Use(g, "give", [target]); Reach(g, p => p.Choices.Any(c => c.Cards.SequenceEqual([card]))); Answer(g, c => c.Cards.SequenceEqual([card])); Play(g); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var n = 0; n < 260; n++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.PlayerSeat != 0) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
                throw new InvalidOperationException("Unexpected mobile Jiang Wei boundary: " + JsonSerializer.Serialize(new { Prompt = p, Root = Root(g)?.MobileJiangWei }));
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Mobile Jiang Wei missed its bounded native boundary.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(restored), "Cold restore preserves the original private receipt, native parent, paid movements and all four views."); return restored; }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mobile-jw", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b), Hero);
            var json = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"draw","targetKind":"anyLiving","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":12}]},
                {"id":"hurt","targetKind":"anyLiving","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"give","targetKind":"otherLiving","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"selectedTargetHand","awaitMovementTriggers":true}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"exchange","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["mobile.xinghun.exchange"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Rescue}}","revision":1,"triggers":[{"id":"first-rescue","window":"dyingEntering","subject":"owner","optional":false,"priority":-100,"usageScope":"game","usageLimit":1,"effects":[{"op":"recover","target":"owner","amount":1}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
            """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实姜维驱动", description = "原生摸牌、给牌与体力流失。" }, [Watch] = new { name = "真实交换子窗", description = "暂停实际等量交换支付。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }, [Rescue] = new { name = "一次真实救援", description = "第一次神霈放弃后进行实际回复。" }, [Quiet] = new { name = "安静角色", description = "跳过其他角色出牌。" } };
            var catalog = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "Native mobile Jiang Wei fixture") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            for (var n = 1; n < 4; n++) b.AddGeneral(new($"fixture:mobile-jw-peer-{n}", "真实目标", "supporter", Quiet, "wei", 12));
            b.AddDeck(new("fixture:mobile-jw-deck", "小型固定实体牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(i % 2 == 0 ? "standard:slash" : "standard:dodge", (Suit)(i % 4), i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "移动神姜维真实结算", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:mobile-jw-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Hero, "fixture:mobile-jw-peer-1", "fixture:mobile-jw-peer-2", "fixture:mobile-jw-peer-3"]));
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "固定配牌姜维真实结算", 4, 4, new Dictionary<string, int>(), "fixture:mobile-jw-deck", GeneralPoolIds: [Hero, "fixture:mobile-jw-peer-1", "fixture:mobile-jw-peer-2", "fixture:mobile-jw-peer-3"], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b) : IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d) => b.AddGeneral(d.Id == Hero ? d with { AdditionalSkillIds = [.. d.AdditionalSkillIds!, Driver, Watch, Rescue] } : d);
        public void AddSkill(ContentSkillDefinition d) => b.AddSkill(d);
        public void AddCard(ContentCardDefinition d) => b.AddCard(d);
        public void AddDeck(ContentDeckRecipe d) => b.AddDeck(d);
        public void AddMode(ContentModeDefinition d) => b.AddMode(d);
    }
}
