using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiWangChangChecks
{
    private const string Hero = "mobile:shi-wang-chang", Kaiji = "mobile:shi-kaiji", Shepan = "mobile:shi-shepan",
        Driver = "fixture:shi-wang-chang-driver", Watch = "fixture:shi-wang-chang-watch", Quiet = "fixture:shi-wang-chang-quiet",
        Peer = "fixture:shi-wang-chang-peer", Source = "fixture:shi-wang-chang-source", Deck = "fixture:shi-wang-chang-deck";

    public static void KaijiDyingPopulationNativeDrawsRewardAndPaidChildCold()
    {
        var s = Scene.Start(true); s.Offer(Kaiji); s.Activate();
        s.Need(s.Root!.MobileShiWangChang is { Maximum: 1 }, "Kaiji starts at one without inventing a dying participant.");
        s.Branch("pick", 0); s.Branch("done"); s.PaidWatch();
        var original = s.Root!.Id; var actualMoves = s.G.CardMovements.Count;
        s.Need(s.Root.MobileShiWangChang is { Stage: MobileShiWangChangStage.DrawChildren, Index: 0, Draw.ActualDrawIds.Count: 1 },
            "The actual native recipient draw is fully paid before its movement observer is answered.");
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<MobileShiKaijiDrawReturnedEvent>().Count(e => e.FrameId == original) == 1 &&
            s.E<MobileShiKaijiRewardBoundEvent>().Single(e => e.FrameId == original).Qualified &&
            s.G.CardMovements.Count >= actualMoves + 3,
            "Cold return retains the already-paid draw, then gives only one nonbasic reward and the genuine normal phase draw.");
        s.Drive("hurt", [1]); s.Rescue(); s.Play();
        s.Drive("hurt", [2]); s.Rescue(); s.Play();
        s.Need(s.E<PlayerDyingEvent>().Select(e => e.VictimSeat).Distinct().Order().SequenceEqual([1, 2]) && s.V(1).IsAlive && s.V(2).IsAlive,
            "Two real one-HP peers enter native dying and are actually rescued by physical Peaches.");
        s.Drive("extra"); s.Play(); var round = s.E<RoundStartedEvent>().Last().RoundNumber;
        s.End(); s.Offer(Kaiji); s.Activate();
        s.Need(s.Root!.MobileShiWangChang is { Maximum: 2 } &&
            s.V(0).SkillRuntimeStates!.Single(v => v.SkillId == Kaiji).PublicRuleStates!.Single(v => v.StateId == "kaiji-maximum").Value == 2,
            "The original preparation freezes X=2 from distinct previously-dying survivors and publishes the same visible count.");
        s.Branch("pick", 1); s.Branch("pick", 0); s.Branch("unpick", 1); s.Branch("pick", 1);
        var batch = s.Root!.Id; s.Branch("done"); s.PaidWatch(); s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<MobileShiKaijiTargetCommittedEvent>().Where(e => e.FrameId == batch).Select(e => e.Target).SequenceEqual([0, 1]) &&
            s.E<MobileShiKaijiDrawReturnedEvent>().Count(e => e.FrameId == batch && e.Actual == 1 && e.NonBasicGained) == 2 &&
            s.E<MobileShiKaijiRewardBoundEvent>().Count(e => e.FrameId == batch && e.Qualified) == 1 && s.E<RoundStartedEvent>().Last().RoundNumber == round,
            "Confirmed targets retain their final order through cold recovery; two recipients each draw once, but their two nonbasic gains grant only one reward in the actual extra turn.");
        s.Drive("hurt", [2]); s.Reach(() => s.P?.Kind == DecisionKind.RescueDying); s.Decline(); s.Play();
        s.Need(!s.V(2).IsAlive && s.V(0).SkillRuntimeStates!.Single(v => v.SkillId == Kaiji).PublicRuleStates!.Single(v => v.StateId == "kaiji-maximum").Value == 1,
            "A previously-dying participant's actual death removes that participant from the current living X immediately.");
        s.Replay();
    }

    public static void ShepanHiddenTopDeckExactUseTurnAndRoundNameLimitsCold()
    {
        var s = Scene.Start(false); s.Play(); var hp = s.V(1).Hp;
        s.Native(CardKind.Slash, 1); s.Offer(Shepan); s.Activate(); s.Branch("top");
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
        var hidden = s.P!;
        s.Need(hidden.PlayerSeat == 1 && hidden.IsPrivate && hidden.Choices.All(c => c.Cards.Count == 0),
            "The defender selects the actual source's opaque hand slots without receiving private source card IDs.");
        var opaque = hidden.Choices.Last().Id;
        s.Cold(); s.Answer(c => c.Id == opaque); s.PaidWatch();
        var use = s.Root!.MobileShiWangChang!; var paid = s.G.CardMovements.Count;
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<MobileShiShepanResolvedEvent>() is [{ Owner: 1, OwnerHand: 7, ActorHand: 7, Ineffective: true }] && s.V(1).Hp == hp &&
            s.G.CardMovements.Count == paid + 1 && s.E<CardEffectSkippedEvent>().Any(e => e.ResolutionId == use.UseId && e.TargetSeat == 1),
            "The paid opaque hand-to-deck move resumes once; equal live hand counts nullify only the original incoming Slash against this defender.");
        s.Native(CardKind.Duel, 1); s.Play();
        s.Need(s.E<MobileShiShepanChosenEvent>().Length == 1 && s.V(1).Hp == hp - 1,
            "A different damage-card name still cannot bypass the one-invocation limit of the same actual turn.");
        var oldRound = s.E<RoundStartedEvent>().Last().RoundNumber;
        s.Drive("extra"); s.Play(); s.End(); s.Play();
        s.Native(CardKind.FireSlash, 1); s.Play();
        s.Need(s.E<MobileShiShepanChosenEvent>().Length == 1 && s.E<RoundStartedEvent>().Last().RoundNumber == oldRound,
            "A genuine extra turn resets the turn quota, while FireSlash still shares the first Slash's round-level name restriction.");
        s.Native(CardKind.Duel, 1); s.Offer(Shepan); s.Activate(); s.Branch("draw"); s.Play();
        s.Need(s.E<MobileShiShepanChosenEvent>().Select(e => e.Name).SequenceEqual([CardKind.Slash, CardKind.Duel]) &&
            s.V(1).SkillRuntimeStates!.Single(v => v.SkillId == Shepan).PublicRuleStates!.Count(v => v.Scope == SkillUsageScope.Round && v.Used == 1) == 2,
            "A distinct Duel name is eligible in the extra turn and both spent round names are visible.");
        s.End(); s.Play(); s.Native(CardKind.ThunderSlash, 1); s.Offer(Shepan); s.Activate(); s.Branch("draw"); s.Play();
        s.Need(s.E<MobileShiShepanChosenEvent>().Length == 3 && s.E<MobileShiShepanChosenEvent>().Last().Name == CardKind.Slash &&
            s.E<MobileShiShepanChosenEvent>().Last().Round > oldRound,
            "A true new round reopens the shared Slash name for ThunderSlash.");
        s.Replay();
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(s => G.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.MobileShiWangChang is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Offer(string skill) => Reach(() => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => Key(c) == "activate"));
        public void Activate() => Answer(c => Key(c) == "activate");
        public void Branch(string branch, int? target = null)
        {
            Reach(() => P?.Choices.Any(c => Key(c) == "shi-wang-chang" && c.Parameters.GetValueOrDefault("branch") == branch &&
                (target is null || c.Targets.SequenceEqual([target.Value]))) == true);
            Answer(c => Key(c) == "shi-wang-chang" && c.Parameters.GetValueOrDefault("branch") == branch && (target is null || c.Targets.SequenceEqual([target.Value])));
        }
        public void PaidWatch() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Drive(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Native(CardKind kind, int target)
        {
            var a = G.GetHumanLegalActions().First(a => a.CardId is > 0 && a.ConversionSource is null && a.TargetSeats.SequenceEqual([target]) && V(0).Hand.Any(c => c.Id == a.CardId && c.Kind == kind));
            Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void Rescue()
        {
            Reach(() => P is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 });
            Answer(c => c.Cards.Count == 1 && V(0).Hand.Any(card => card.Id == c.Cards[0] && card.Kind == CardKind.Peach));
        }
        public void Decline() => Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null);
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(c is not null, "The expected actual native Shi Wang Chang choice must exist.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision));
        }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 300; i++)
            {
                if (ready()) return;
                if (P is { } p)
                {
                    if (p.SkillPrompt?.SkillId == Watch) { Continue(); continue; }
                    if (p.Choices.Any(c => Key(c) == "skip")) { Answer(c => Key(c) == "skip"); continue; }
                    if (p.Kind is DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)
                    { if (p.PlayerSeat == 0 || p.PlayerSeat == 1 && p.SkillPrompt?.SkillId == Shepan) { Decline(); continue; } }
                    if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected human Shi Wang Chang prompt. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The short real native scene retains living participants.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Shi Wang Chang missed its bounded native boundary.");
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold recovery preserves the same native frame, private choices, paid cards and recipient cursor."); }
        public void Replay()
        { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "Accepted native commands reproduce all Kaiji draws and the exact Shepan use/turn/round limits."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements,
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, P, Receipt = Root?.MobileShiWangChang,
            Started = E<MobileShiWangChangStartedEvent>(), Draws = E<MobileShiKaijiDrawReturnedEvent>(), Resolved = E<MobileShiShepanResolvedEvent>(),
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }) });
        private void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Shi Wang Chang command rejected."); }
        public static Scene Start(bool kaiji)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] opening = kaiji ? [new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Peach, Suit.Heart, 4), new(CardKind.Peach, Suit.Heart, 5), new(CardKind.Dodge, Suit.Club, 2)] :
                [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.FireSlash, Suit.Heart, 8), new(CardKind.ThunderSlash, Suit.Club, 9),
                 new(CardKind.Duel, Suit.Spade, 1), new(CardKind.Duel, Suit.Club, 1), new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Dodge, Suit.Diamond, 2)];
            var enemies = kaiji ? new ShanheCombatantDefinition[] { new(Peer, Hp: 1) { StartingHand = [] }, new(Peer, Hp: 1) { StartingHand = [] }, new(Peer, Hp: 8) { StartingHand = [] } } :
                [new(Hero, ExtraSkillIds: [Quiet]) { StartingHand = Enumerable.Range(0, 7).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Heart, i + 1)).ToArray() }, new(Peer, Hp: 8) { StartingHand = [] }, new(Peer, Hp: 8) { StartingHand = [] }];
            var setup = new ShanheBattleSetup(61, 1, "shi-wang-chang-native", kaiji ? Hero : Source, 2, 0, 0, 0, 0,
                kaiji ? [Driver, Watch] : [Watch], opening, [], enemies, [], new string('a', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var scene = new Scene(g, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:shi-wang-chang", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"hold","query":"handLimit","operation":"add","value":20,"priority":0},{"id":"many-slashes","query":"slashLimit","operation":"unlimited","value":0,"priority":0}],"activations":[
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"paid-kaiji","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["skill-program.mobile:shi-kaiji.recipient-draw"],"movementOccurrence":"perBatch","optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause-kaiji","options":[{"id":"continue"}]}]},
                {"id":"paid-shepan","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["skill-program.mobile:shi-shepan.SelectAndMoveOwnedCard"],"movementOccurrence":"perBatch","optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause-shepan","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "原生势王昶驱动", description = "实际伤害与额外回合。" },
                    [Watch] = new { name = "已付款子窗", description = "真实移动结束前保留原始父帧。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                    [Quiet] = new { name = "安静同桌", description = "跳过出牌。" } } }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Source, "实际使用者", "", Driver, "wei", 8));
            b.AddGeneral(new(Peer, "原生濒死角色", "", Quiet, "wei", 8));
            b.AddDeck(new(Deck, "小固定非基本牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("standard:draw_two", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势王昶原生准备与目标", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Source, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
