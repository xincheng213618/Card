using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiSunChenChecks
{
    private const string Hero = "mobile:shi-sun-chen", Nigu = "mobile:shi-nigu", Lulian = "mobile:shi-lulian", Driver = "fixture:shi-sun-chen-driver", WatchSkill = "fixture:shi-sun-chen-watch",
        Quiet = "fixture:shi-sun-chen-quiet", Shield = "fixture:shi-sun-chen-shield", Spent = "fixture:shi-sun-chen-spent", Peer = "fixture:shi-sun-chen-peer", Deck = "fixture:shi-sun-chen-deck";

    public static void DistinctCostPrivateCommitmentsNativeGiftsAndDamageQuotaCold()
    {
        var s = Scene.Start(); s.Play(); var peach = s.V(0).Hand.Single(c => c.Kind == CardKind.Peach).Id; var dodge = s.V(0).Hand.Single(c => c.Kind == CardKind.Dodge).Id; var armor = s.V(0).Equipment.Single().Id;
        var donated = s.V(1).Hand.Single().Id; s.ActivateNigu(); s.Reach(() => s.Root?.ShiSunChen?.Stage == ShiScStage.Cards);
        s.Need(s.P!.MinimumCardCount == 0 && s.P.Choices.Any(c => Branch(c) == "pay"), "Nigu publishes the genuine zero-card option before any independent commitment.");
        s.Answer(c => Branch(c) == "add" && c.Cards.SequenceEqual([peach]));
        s.Need(s.P!.Choices.All(c => !c.Cards.Contains(dodge)) && s.Zone(peach) == CardLocation.Hand(0), "Selecting one Heart suppresses the other Heart without paying an individual click.");
        s.Answer(c => Branch(c) == "add" && c.Cards.SequenceEqual([armor])); s.Cold(); s.Answer(c => Branch(c) == "pay"); s.Watch("cost");
        var original = s.Root!.Id;
        s.Need(s.E<ShiScCostPaidEvent>().Single() is { Count: 2 } && s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == original && e.ActualCount == 2 && e.Reason.Value == "skill.mobile-shi-sun-chen.nigu.discard") == 1 &&
            s.Zone(peach) == CardLocation.DiscardPile && s.Zone(armor) == CardLocation.DiscardPile && s.E<ShiScVoteRevealedEvent>().Length == 0,
            "One real logical HE discard pays both different suits before its native observer child and before any vote is revealed.");
        s.Cold(); s.Continue("cost"); s.Reach(() => s.Root?.ShiSunChen?.Stage == ShiScStage.Votes);
        var participants = s.Root!.ShiSunChen!.Participants.ToArray(); s.Need(participants.SequenceEqual([1, 2, 3]), "The post-payment attack range freezes all living other participants.");
        for (var i = 0; i < participants.Length; i++)
        {
            var index = i; s.Reach(() => s.Root?.ShiSunChen is { Stage: ShiScStage.Votes } r && r.Votes.Count == index && s.P?.PlayerSeat == participants[index]);
            var chooser = participants[i]; s.Need(s.P is { IsPrivate: true } && Enumerable.Range(0, 4).Where(seat => seat != chooser).All(seat => s.G.CreateSnapshot(seat).PendingDecision is null) &&
                s.E<ShiScVoteRevealedEvent>().Length == 0 && s.E<ShiScGiftPaidEvent>().Length == 0, "Every private commitment stays hidden and cannot pay a card before all participants have committed.");
            s.Cold(); s.Step();
        }
        s.Reach(() => s.Root?.ShiSunChen?.Stage == ShiScStage.GiveCard);
        s.Need(s.E<ShiScVoteRevealedEvent>().Length == 3 && s.E<ShiScGiftPaidEvent>().Length == 0 && s.Zone(donated) == CardLocation.Hand(1), "The complete simultaneous decision reveals before the willing giver selects a genuine material.");
        s.Cold(); s.Play(); var arm = s.E<ShiScArmedEvent>().Single();
        s.Need(arm.Count == 2 && s.E<ShiScGiftPaidEvent>().Single() is { Seat: 1, CardId: var paid, From: var from, To: var to } && paid == donated && from == CardLocation.Hand(1) && to == CardLocation.Hand(0) &&
            s.Zone(donated) == CardLocation.Hand(0) && s.E<ShiScCostPaidEvent>().Length == 1 && s.G.GetHumanLegalActions().All(a => a.ProgramSkillId != Nigu), "One real ally gift and two actual refusals issue two damage charges without paying the original discard again.");
        s.Use("lose-nigu"); s.Play(); s.Need(s.V(0).Skills!.All(k => k.ContentId != Nigu), "The fixture genuinely removes Nigu before its already-issued damage charges are spent.");
        s.Use("source-less"); s.Play();
        s.Need(s.E<DamageAppliedEvent>().Any(e => e.SourceLess && e.TargetSeat == 0 && e.Amount == 1) && s.E<ShiScDamageConsumedEvent>().Length == 0, "An actual source-less damage does not borrow the placeholder source seat's outgoing Nigu charge.");
        s.Use("damage", [2]); s.Play(); s.Need(s.E<ShiScDamageConsumedEvent>().Length == 0, "Preventing the first real sourced damage preserves both next-damage charges.");
        for (var i = 0; i < 3; i++) { s.Use("damage", [2]); s.Play(); }
        s.Need(s.E<DamageAppliedEvent>().Where(e => e.SourceSeat == 0 && e.TargetSeat == 2 && !e.SourceLess).Select(e => e.Amount).SequenceEqual([2, 2, 1]) &&
            s.E<ShiScDamageConsumedEvent>().Length == 2 && s.E<ShiScDamageConsumedEvent>().All(e => e.ArmId == arm.FrameId), "The two issued actual-turn charges survive source loss and add one to two separate genuine damages, never to a third."); s.Cold(); s.Replay();

        var zero = Scene.Start(); zero.Play(); var before = zero.V(0).HandCount; zero.ActivateNigu(); zero.Reach(() => zero.Root?.ShiSunChen?.Stage == ShiScStage.Cards); zero.Answer(c => Branch(c) == "pay"); zero.Play();
        zero.Need(zero.E<ShiScCostPaidEvent>().Single().Count == 0 && zero.E<ShiScArmedEvent>().Single().Count == 2 && zero.V(0).HandCount == before + 1, "Zero discarded cards still resolve the original simultaneous choice and genuine gift.");
        zero.End(); zero.Play(); zero.Use("damage", [3]); zero.Play();
        zero.Need(zero.E<DamageAppliedEvent>().Last(e => e.TargetSeat == 3).Amount == 1 && zero.E<ShiScDamageConsumedEvent>().Length == 0, "Unspent charges expire with their original actual turn rather than following the next Play."); zero.Replay();
    }

    public static void CompletedCurrentCategoryChainsDrawAndForcedMomentumPaidChildCold()
    {
        var retained = Scene.Start("draw-two"); retained.Play(); retained.Native(CardKind.DrawTwo); retained.Play();
        retained.Need(retained.V(0).HandCount == 2 && retained.V(0).Hand.All(c => c.Kind == CardKind.DrawTwo) && retained.E<ShiScLulianBoundEvent>().Length == 0,
            "A real last Trick used from hand gains two same-category cards during its own full resolution, so Lulian checks the current category and grants no stale last-card reward."); retained.Cold(); retained.Replay();

        var s = Scene.Start("duel"); s.Play(); var physical = s.V(0).Hand.Single().Id; s.Native(CardKind.Duel, 3); s.Watch("draw");
        var original = s.Root!; var r = original.ShiSunChen!; var bound = s.E<ShiScLulianBoundEvent>().Single();
        s.Need(bound is { Chain: true, Draw: true, TargetCount: 1 } && s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == r.UseId && e.CardId == physical) &&
            s.E<CardUseAppearanceCapturedEvent>().Any(e => e.Action.ActionId == r.ActionId && e.Action.ActorSeat == 0 && e.Action.PhysicalCards.Single().From == CardLocation.Hand(0)) &&
            s.V(3).IsChained && s.E<ShiScDrawPaidEvent>().Single().Count == 1 && s.E<NativeDrawInvocationRecordedEvent>().Count(e => e.ParentFrameId == original.Id) == 1 &&
            s.E<ShiScMomentumIssuedEvent>().Length == 0, "The full native Duel finishes first; its independent HP/equipment predicates freeze both branches, genuinely chain the target and pay one native draw before momentum.");
        s.Cold(); s.Continue("draw"); s.Reach(() => s.Root?.ShiSunChen?.Stage == ShiScStage.Momentum);
        s.Need(s.P!.Choices.Any(c => c.Targets.SequenceEqual([0])) && s.P.Choices.All(c => c.Targets is [var target] && s.V(target).Hp > Enumerable.Range(0, 4).Where(i => s.V(i).IsAlive).Min(i => s.V(i).Hp)), "Forced momentum offers every currently nonminimal living HP role, including the owner when legal, without a decline.");
        var hp = s.V(0).Hp; s.Answer(c => c.Targets.SequenceEqual([0])); s.Play();
        s.Need(s.V(0).Hp == hp - 1 && s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Nature == DamageNature.Fire && e.Amount == 1) == 1 &&
            s.E<ShiScMomentumIssuedEvent>().Length == 1 && s.E<ShiScDrawPaidEvent>().Length == 1 && s.E<ShiScCompletedEvent>().Count(e => e.FrameId == original.Id) == 1,
            "Cold resuming the paid draw forces one genuine self-targeted fire damage and completes the original locked Lulian once."); s.Cold(); s.Replay();
    }
    private static string? Branch(PromptChoice c) => c.Parameters.GetValueOrDefault("branch");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.ShiSunChen is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void ActivateNigu() => Accept(new UseProgramSkillCommand(0, Nigu, "distinct-suits-private-gifts", [], [], G.Revision, P!.PromptId));
        public void Use(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Watch(string option) => Reach(() => P?.SkillPrompt?.SkillId == WatchSkill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option));
        public void Continue(string option) => Answer(c => c.Parameters.GetValueOrDefault("option-id") == option);
        public void Step() => Accept(new AdvanceOneStepCommand(G.Revision));
        public void Native(CardKind kind, int? target = null)
        {
            var a = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is { } id && V(0).Hand.Any(c => c.Id == id && c.Kind == kind) && a.ConversionSource is null && a.Kind != LegalActionKind.Recast && (target is null || a.TargetSeats.SequenceEqual([target.Value])));
            Need(a is not null, "The original physical Hand entity has its native action."); Accept(new PlayCardCommand(0, a!.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void Answer(Func<PromptChoice, bool> pick) { var p = P!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "The exact original native prompt choice exists."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 360; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                    if (p.Choices.Any(c => c.Parameters.ContainsKey("option-id"))) { Answer(c => c.Parameters.ContainsKey("option-id")); continue; }
                    if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null); continue; }
                    throw new InvalidOperationException("Unexpected Sun Chen native boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded table remains alive."); Step();
            }
            Need(false, "The original Sun Chen boundary was not reached.");
        }
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold recovery preserves all independent private commitments and original paid native child."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "The accepted journal reproduces original payment, actual-turn damage quota and completed-use effects."); }
        public void Need(bool good, string why) { if (!good) throw new InvalidOperationException(why + " " + Dump()); }
        private void Accept(GameCommand c) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Sun Chen native command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements, Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, P, Root?.ShiSunChen, Arm = E<ShiScArmedEvent>(), Hits = E<DamageAppliedEvent>(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray() });
        public static Scene Start(string scenario = "nigu")
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
            ShanheStartingCard[] own = scenario == "draw-two" ? [new(CardKind.DrawTwo, Suit.Heart, 7)] : scenario == "duel" ? [new(CardKind.Duel, Suit.Heart, 7)] : [new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Dodge, Suit.Heart, 4), new(CardKind.Slash, Suit.Spade, 7)];
            var setup = new ShanheBattleSetup(660, 1, "shi-sun-chen-native", Hero, 4, 0, 0, 0, 0, [Driver, WatchSkill], own, scenario == "nigu" ? [new(CardKind.BaguaFormation, Suit.Club, 2)] : [],
                [new(Peer, Hp: 10, ExtraSkillIds: scenario == "nigu" ? [Shield] : []) { StartingHand = [new(CardKind.Dodge, Suit.Spade, 2)] }, new(Peer, Hp: scenario == "duel" ? 4 : 8) { StartingHand = [new(CardKind.Peach, Suit.Club, 4)] }],
                [new(Peer, Hp: 6) { StartingHand = [new(CardKind.Peach, Suit.Diamond, 4)] }], new string('c', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-sun-chen", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0},{"id":"range","query":"attackRange","operation":"set","value":3,"priority":0}],
                "triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}],"activations":[
                  {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                  {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
                  {"id":"lose-nigu","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Nigu}}"],"sourceBind":"{{Spent}}"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},{"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Shield}}","revision":1,"triggers":[{"id":"first-prevent","window":"beforeDamageApplied","subject":"damageTarget","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"preventCurrentDamage","target":"owner"}]}]},
              {"id":"{{WatchSkill}}","revision":1,"triggers":[
                {"id":"paid-cost","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.mobile-shi-sun-chen.nigu.discard"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost","options":[{"id":"cost"}]}]},
                {"id":"paid-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.mobile-shi-sun-chen.lulian.draw"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw","options":[{"id":"draw"}]}]}]}
            ]}
            """;
            var presentations = new Dictionary<string, object> { [Driver] = new { name = "真实伤害与失源", description = "固定范围与实际伤害。" }, [Quiet] = new { name = "安静同桌", description = "固定短局。" }, [Shield] = new { name = "首次真实防伤", description = "防止第一次原生伤害。" },
                [WatchSkill] = new { name = "已付子结算", description = "支付后暂停。", optionLabels = new Dictionary<string, string> { ["cost"] = "继续原弃牌", ["draw"] = "继续原摸牌" } } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Spent, "逆固已失去", "原签发额度仍有效。")); b.AddGeneral(new(Peer, "真实同桌", "", Quiet, "wei", 10));
            b.AddDeck(new(Deck, "固定实际牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard(scenario == "draw-two" ? "standard:draw_two" : "standard:peach", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势孙綝原生流程", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
