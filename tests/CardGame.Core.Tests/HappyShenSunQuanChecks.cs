using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class HappyShenSunQuanChecks
{
    private const string Hero = "happy:shen-sun-quan", Watch = "fixture:happy-sq-watch", Quiet = "fixture:happy-sq-quiet", Death = "fixture:happy-sq-death", Ending = "fixture:happy-sq-ending";
    private const string ShehuReason = "skill-program.happy-shehu.discard", DingliDraw = "skill-program.happy-dingli.draw";

    public static void ShehuPaidColdAndLearningDingliUseNativeReturns()
    {
        var (g, registry) = Start(false);
        Grant(g, [1, 2]); Play(g);
        Need(E<RoundStartedEvent>(g) is [{ RoundNumber: 1, ActorSeat: 0 }], "The registered round-scoped Dingli definition enables native round tracking in a minimal content registry.");
        Need(Mark(g, 1) == 1 && Mark(g, 2) == 1 && E<HappyXueGrantEvent>(g).Length == 2, "Quanxue grants one attributed public study to each of two distinct other characters.");
        var slash = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
        var hp = V(g, 1).Hp; var hand = V(g, 1).HandCount;
        Native(g, slash); Menu(g, HappySunQuanStage.Discard);
        Need(P(g)!.Choices.All(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)), "Shehu offers only opaque hand slots of the actual final Slash target.");
        Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == "0");
        Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var paid = Root(g)!; var receipt = paid.HappySunQuan!; var card = receipt.Payments.Single().CardId;
        Need(receipt is { Stage: HappySunQuanStage.MovementChildren, Paid.Count: 1, Payments.Count: 1 } && paid.PendingMovementContinuation is not null &&
            V(g, 1).HandCount == hand - 1 && V(g, 1).Hp == hp && Zone(g, card) == CardLocation.DiscardPile &&
            E<HappyShehuTargetEvent>(g).Single().ActionId == receipt.ActionId,
            "The locked Shehu really discards one foreign entity before Slash damage and awaits that exact paid movement on its original frame.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(V(g, 1).Hp == hp - 1 && g.CardMovements.Count(m => m.CardId == card && m.Reason.Value == ShehuReason) == 1 && E<HappySunQuanCompletedEvent>(g).Count(e => e.FrameId == paid.Id) == 1,
            "Restoring the paid child neither repeats its discard nor skips the original Slash damage.");
        End(g); Learning(g, 1);
        Need(Mark(g, 1) == 0 && V(g, 1).Hp == hp - 1, "The actual holder removes study before choosing its responsibility.");
        Answer(g, c => Option(c) == "lose-hp"); Offer(g, "happy:dingli");
        var ownerHand = V(g).HandCount;
        Need(V(g, 1).Hp == hp - 2 && V(g).Hp == 3, "The chosen loss is an actual one-point HP change before Dingli compares the two current HP values.");
        Answer(g, c => Action(c) == "activate"); Reach(g, p => p.SkillPrompt?.SkillId == Watch && Root(g)?.HappySunQuan?.Operation == SkillProgramEffectOp.ResolveHappySunQuanDingli);
        Need(Root(g)!.HappySunQuan is { Stage: HappySunQuanStage.MovementChildren, Branch: 2, Amount: 2, Draw.ActualDrawIds.Count: 2 } && V(g).HandCount == ownerHand + 2 &&
            g.CardMovements.Count(m => m.Reason.Value == DingliDraw && m.To == CardLocation.Hand(0)) == 2,
            "Dingli draws the actual capped HP difference through the native draw producer and waits for its real gain child.");
        Continue(g); Learning(g, 2); Answer(g, c => Option(c) == "restricted");
        Offer(g, "happy:quanxue");
        Need(E<HappyDingliBenefitEvent>(g).Length == 1 && E<RoundStartedEvent>(g).Last().RoundNumber == 2 &&
            E<SkillUsageConsumedEvent>(g).Count(e => e.SkillOwnerSeat == 0 && e.SkillId == "happy:dingli" && e.Scope == SkillUsageScope.Round) == 1,
            "A second holder cannot spend Dingli in round one; the following own actual turn has genuinely opened native round two.");
        Answer(g, c => Action(c) == "activate"); Menu(g, HappySunQuanStage.Targets); Answer(g, c => c.Targets.SequenceEqual([2]));
        Menu(g, HappySunQuanStage.Targets); Answer(g, c => Option(c) == "finish"); Play(g); End(g);
        Learning(g, 2); Answer(g, c => Option(c) == "restricted"); Offer(g, "happy:dingli");
        Answer(g, c => Action(c) == "activate"); Reach(g, p => p.SkillPrompt?.SkillId == Watch && Root(g)?.HappySunQuan is { Stage: HappySunQuanStage.RecoveryChildren });
        var recovery = Root(g)!;
        Need(V(g).Hp == 4 && recovery.PendingMovementContinuation is not null && recovery.HappySunQuan is { Branch: 1, Amount: 1 }, "The next actual round refreshes Dingli and the greater-HP holder grants native one-point recovery.");
        g = Cold(g, registry); Continue(g); Offer(g, "happy:quanxue"); Answer(g, c => Action(c) == "skip"); Play(g);
        Need(E<HappyDingliBenefitEvent>(g).Select(e => e.Branch).SequenceEqual([2, 1]) && E<HappySunQuanCompletedEvent>(g).Count(e => e.FrameId == recovery.Id) == 1 && V(g).Hp == 4 &&
            E<SkillUsageConsumedEvent>(g).Count(e => e.SkillOwnerSeat == 0 && e.SkillId == "happy:dingli" && e.Scope == SkillUsageScope.Round) == 2,
            "The typed recovery child returns exactly once without repeating the previous round's draw branch.");
        Need(State(g) == State(GameReplay.Replay(Options(false), g.AcceptedCommands, registry)), "The complete learning choices, private hand payment and both native Dingli branches replay from accepted commands.");
    }

    public static void SourceDeathKeepsHolderDutyAndOnlyPlayRestrictsForeignUses()
    {
        var (g, registry) = Start(true); Offer(g, Ending); Answer(g, c => Action(c) == "skip"); Play(g); End(g);
        Grant(g, [0, 2]);
        Learning(g, 2); Answer(g, c => Option(c) == "restricted"); Learning(g, 0);
        var start = g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.HappyLearningStart?.HolderSeat == 0);
        Need(!V(g, 1).IsAlive && Mark(g, 0) == 0 && start.HappyLearningStart!.Grants.Single().Source.OwnerSeat == 1 && E<HappyLearningRemovedEvent>(g).Count(e => e.HolderSeat == 0) == 1,
            "Actual native death of Quanxue's source preserves the original attributed grant and the holder's next actual-turn responsibility.");
        g = Cold(g, registry); Answer(g, c => Option(c) == "restricted");
        var restriction = E<HappyLearningChoiceEvent>(g).Last(e => e.HolderSeat == 0);
        Offer(g, Ending); Answer(g, c => Action(c) == "activate");
        Reach(g, p => p.SkillPrompt?.SkillId == Ending && p.Choices.Any(c => c.Targets.SequenceEqual([3])));
        var damageBefore = E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 3);
        Answer(g, c => c.Targets.SequenceEqual([3]));
        Until(g, () => E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 3) == damageBefore + 1);
        Need(E<TurnStartedEvent>(g).Last() is { ActorSeat: 0 } actual && actual.TurnNumber == restriction.ActualTurn &&
            E<ProgramBindingStartedEvent>(g).Count(e => e.OwnerSeat == 0 && e.SkillId == Ending) == 1 &&
            E<CardUsedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 3 && e.CardKind == CardKind.Slash),
            "In the very same restricted actual turn, a native preparation-window Slash may still use a card against another character; the rule only applies during Play.");
        Play(g);
        Need(g.GetHumanLegalActions().All(a => a.Kind != LegalActionKind.Slash) && g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Equip),
            "The restricted actual Play disallows foreign Slash targets but still permits a genuine self equipment use.");
        var physical = V(g).Hand.First(c => c.Kind == CardKind.Slash).Id; var before = State(g);
        Need(!g.Submit(new PlayCardCommand(0, physical, [3], g.Revision, P(g)!.PromptId)).Accepted && State(g) == before,
            "A forged foreign physical Slash cannot cross the current Play restriction or consume a card.");
        Native(g, g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip)); Play(g);
        Need(V(g).Equipment.Any(c => c.Kind == CardKind.Crossbow) && g.GetHumanLegalActions().All(a => a.Kind != LegalActionKind.Slash),
            "An actually equipped unlimited-Slash Crossbow still cannot target another character during restricted Play; ordinary Slash quota cannot explain the restriction.");
        before = State(g);
        Need(!g.Submit(new PlayCardCommand(0, physical, [3], g.Revision, P(g)!.PromptId)).Accepted && State(g) == before,
            "Even after actual Crossbow equipment, a forged foreign Slash is rejected without consuming its physical card.");
    }

    private static GameOptions Options(bool sourceDies)
    {
        ShanheStartingCard[] slashes = [new(CardKind.Slash, Suit.Club, 7), new(CardKind.Slash, Suit.Spade, 8), new(CardKind.Slash, Suit.Diamond, 9), new(CardKind.Slash, Suit.Heart, 6)];
        var setup = new ShanheBattleSetup(7, 1, "happy-sun-quan", sourceDies ? Peer(0) : Hero, sourceDies ? 8 : 3, 0, 0, 0, 0, [],
            [new(CardKind.Crossbow, Suit.Club, 1), new(CardKind.Slash, Suit.Spade, 7)], [],
            [new(Peer(2)) { StartingHand = slashes }, new(Peer(3)) { StartingHand = slashes }],
            [new(sourceDies ? Hero : Peer(1)) { StartingHand = slashes }], new string('f', 64)) { DefaultGodFactionId = "wu" };
        return new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 };
    }
    private static (GameEngine, ContentRegistry) Start(bool sourceDies)
    { var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(sourceDies)); var g = GameEngine.CreateStandard(Options(sourceDies), r); Accept(g, new StartGameCommand()); return (g, r); }
    private static string Peer(int n) => "fixture:happy-sq-peer-" + n;
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static int Mark(GameEngine g, int seat) => V(g, seat).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.HappyXue)?.Count ?? 0;
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static ProgramSkillFrame? Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.HappySunQuan is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardLocation Zone(GameEngine g, int id) => g.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
    private static string? Action(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static string? Option(PromptChoice c) => c.Parameters.GetValueOrDefault("option");
    private static void Need(bool valid, string why) { if (!valid) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Happy Sun Quan command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick)
    { var p = P(g)!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "Missing Happy Sun Quan published choice: " + JsonSerializer.Serialize(p)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision)); }
    private static void Native(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Offer(GameEngine g, string skill) => Reach(g, p => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => Action(c) == "activate"));
    private static void Menu(GameEngine g, HappySunQuanStage stage) => Reach(g, p => Root(g)?.HappySunQuan?.Stage == stage && p.Choices.Any(c => Action(c) == "happy-sun-quan"));
    private static void Learning(GameEngine g, int holder) => Reach(g, p => p.PlayerSeat == holder && Root(g)?.HappySunQuan?.Stage == HappySunQuanStage.LearningChoice && p.Choices.Any(c => Option(c) == "restricted"));
    private static void Grant(GameEngine g, int[] targets)
    {
        Offer(g, "happy:quanxue"); Answer(g, c => Action(c) == "activate");
        foreach (var target in targets) { Menu(g, HappySunQuanStage.Targets); Answer(g, c => c.Targets.SequenceEqual([target])); }
        if (targets.Length < 2) { Menu(g, HappySunQuanStage.Targets); Answer(g, c => Option(c) == "finish"); }
    }
    private static void Step(GameEngine g)
    {
        if (P(g) is { } p)
        {
            if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); return; }
            if (p.SkillPrompt?.SkillId == Watch) { Continue(g); return; }
            if (p.Kind == DecisionKind.RescueDying && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) { Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass"); return; }
            if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Happy Sun Quan boundary: " + JsonSerializer.Serialize(new { Prompt = p, Root = Root(g)?.HappySunQuan, Hp = Enumerable.Range(0, 4).Select(s => V(g, s).Hp), Rounds = E<RoundStartedEvent>(g), Turns = E<TurnStartedEvent>(g), Usage = E<SkillUsageConsumedEvent>(g).Where(e => e.SkillId == "happy:dingli"), Grants = E<HappyXueGrantEvent>(g), Benefits = E<HappyDingliBenefitEvent>(g) }));
        }
        Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    { for (var n = 0; n < 260; n++) { if (P(g) is { } p && ready(p)) return; Step(g); } throw new InvalidOperationException("Happy Sun Quan missed its bounded native boundary."); }
    private static void Until(GameEngine g, Func<bool> ready)
    { for (var n = 0; n < 180; n++) { if (ready()) return; Step(g); } throw new InvalidOperationException("Happy Sun Quan missed its bounded native return."); }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(restored), "JSON cold restore preserves the holder's frozen issued duty, original paid frame, all real entities and four viewer snapshots."); return restored; }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(g.AcceptedCommands) });

    private sealed class Fixture(bool sourceDies) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-happy-sun-quan", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b, sourceDies), Hero);
            var json = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"hand-payment","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{ShehuReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]},
                {"id":"native-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{DingliDraw}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"drawn","options":[{"id":"continue"}]}]},
                {"id":"native-recover","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"recovered","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Death}}","revision":1,"triggers":[{"id":"real-source-death","window":"turnEnding","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]},
              {"id":"{{Ending}}","revision":1,"triggers":[{"id":"outside-play","window":"turnStartBeforeNormalFlow","subject":"owner","usageScope":"game","usageLimit":1,"optional":true,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]}]}]}
            """;
            var labels = new Dictionary<string, object> { [Watch] = new { name = "真实支付子窗", description = "暂停实际弃牌、摸牌及回复。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }, [Quiet] = new { name = "安静角色", description = "跳过出牌。" }, [Death] = new { name = "真实来源死亡", description = "授予后在原生体力流失及濒死结算中死亡。" }, [Ending] = new { name = "实际准备阶段使用", description = "在出牌阶段外真正使用一张杀。" } };
            var catalog = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "Native Happy Sun Quan fixture") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer(0), "真实持有人", "supporter", Ending, "wei", 8) { AdditionalSkillIds = [Watch] });
            for (var n = 1; n < 4; n++) b.AddGeneral(new(Peer(n), "真实其他角色", "supporter", Quiet, "wei", n == 1 ? 3 : 8) { AdditionalSkillIds = [Watch] });
            b.AddDeck(new("fixture:happy-sq-deck", "小型固定实体牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard("standard:slash", (Suit)(i % 4), i % 13 + 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "固定配牌欢乐孙权", 4, 4, new Dictionary<string, int>(), "fixture:happy-sq-deck", GeneralPoolIds: [Hero, Peer(0), Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b, bool sourceDies) : IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d) => b.AddGeneral(d.Id == Hero ? d with { AdditionalSkillIds = [.. d.AdditionalSkillIds!, Watch, .. (sourceDies ? new[] { Death } : [])] } : d);
        public void AddSkill(ContentSkillDefinition d) => b.AddSkill(d);
        public void AddCard(ContentCardDefinition d) => b.AddCard(d);
        public void AddDeck(ContentDeckRecipe d) => b.AddDeck(d);
        public void AddMode(ContentModeDefinition d) => b.AddMode(d);
    }
}
