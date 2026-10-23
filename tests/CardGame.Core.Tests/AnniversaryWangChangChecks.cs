using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryWangChangChecks
{
    private const string Hero = "anniversary:wang-chang", Kaiji = "anniversary:kaiji", Pingxi = "anniversary:pingxi", Peer = "fixture:wang-chang-peer", Driver = "fixture:wang-chang-driver", Watch = "fixture:wang-chang-watch", Quiet = "fixture:wang-chang-quiet", Deck = "fixture:wang-chang-deck";
    public static void ActualPhaseConversionHePaymentAndWholeGroupSlashesCold()
    {
        var s = Scene.Start(); s.Play(); var turn = s.G.State.TurnNumber;
        var weapon = s.V(0).Hand.Single(c => c.Kind == CardKind.Crossbow).Id;
        s.Native(weapon); s.Play(); s.UseKaiji(); s.Play();
        s.Need(s.V(0).HandCount == 3 && s.V(0).Equipment.Count == 1 && s.E<AnniversaryWangChangDrawReturnedEvent>() is [{ Count: 3 }], "The initial Yang draws the real maximum-HP count inside the first actual extra Play phase.");
        s.Need(!s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Kaiji), "Kaiji cannot be used twice in one actual Play phase.");
        s.End(); s.Play(); s.UseKaiji(); s.Menu(AnniversaryWangChangStage.OwnCards);
        var material = s.V(0).Hand.First().Id;
        s.Answer(c => c.Cards.SequenceEqual([weapon])); s.Menu(AnniversaryWangChangStage.OwnCards);
        s.Answer(c => c.Cards.SequenceEqual([material])); s.Menu(AnniversaryWangChangStage.OwnCards); s.Answer(c => Verb(c) == "pay");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        var paid = s.Root!;
        s.Need(paid.AnniversaryWangChang is { Stage: AnniversaryWangChangStage.MovementChildren, Polarity: SkillPolarity.Yin, Moves.Count: 2 } r && r.Cards.SequenceEqual([weapon, material]) &&
            r.Moves.Any(m => m.CardId == weapon && m.From == CardLocation.Equipment(0)) && r.Moves.Any(m => m.CardId == material && m.From == CardLocation.Hand(0)) &&
            r.Moves.All(m => m.To == CardLocation.DiscardPile) && s.V(0).Equipment.Count == 0, "The next actual Play phase pays two genuine HE materials before the native movement child pauses.");
        var paidId = paid.Id; s.Cold(); s.Continue(); s.Play();
        var conversions = s.E<ProgramConversionPolarityCommittedEvent>().Where(e => e.SkillId == Kaiji).ToArray();
        s.Need(conversions is [{ PreviousState: SkillPolarity.Yang, CurrentState: SkillPolarity.Yin }, { PreviousState: SkillPolarity.Yin, CurrentState: SkillPolarity.Yang }] &&
            conversions.All(e => e.TurnNumber == turn) && conversions[0].PhaseInstanceId != conversions[1].PhaseInstanceId &&
            s.E<AnniversaryWangChangDiscardPaidEvent>().Count(e => e.FrameId == paidId && e.Count == 2) == 1 && s.V(0).HandCount == 4,
            "Cold resumes the original once-paid Yin cost and one conversion; the two actual phases share the turn but have independent phase use limits.");
        s.End(); s.Offer(); s.Activate(); s.Menu(AnniversaryWangChangStage.Targets);
        var pingxi = s.Root!.Id;
        s.Need(s.Root.AnniversaryWangChang is { Maximum: 3 } && s.G.State.TurnNumber == turn, "Pingxi freezes three completed current-turn discards: two Kaiji HE costs plus one ordinary hand-limit discard.");
        foreach (var target in new[] { 1, 2, 3 }) { s.Answer(c => c.Targets.SequenceEqual([target])); s.Menu(AnniversaryWangChangStage.Targets); }
        s.Answer(c => Verb(c) == "seal");
        for (var target = 1; target <= 3; target++)
        {
            s.Menu(AnniversaryWangChangStage.ForeignCard);
            s.Need(s.Root!.AnniversaryWangChang!.Targets.SequenceEqual([1, 2, 3]) && s.Root.AnniversaryWangChang.Cursor == target - 1 && s.E<AnniversaryWangChangSlashIssuedEvent>().Length == 0 &&
                s.P!.Choices.All(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("card-owner-seat") == target.ToString()), "All original targets discard first, with no premature Slash or foreign hidden-hand identity in their slot menu.");
            s.Answer(c => c.Parameters.GetValueOrDefault("source-zone") == "Hand" && c.Parameters.GetValueOrDefault("slot-index") == "0");
        }
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        var first = s.E<AnniversaryWangChangSlashIssuedEvent>().Single();
        s.Need(first.FrameId == pingxi && first.Cursor == 0 && first.TargetSeat == 1 && s.Root!.AnniversaryWangChang is { Stage: AnniversaryWangChangStage.NativeSlash, Cursor: 3, Maximum: 3 } &&
            s.E<AnniversaryWangChangDiscardPaidEvent>().Count(e => e.FrameId == pingxi) == 3 && s.G.ResolutionStack.OfType<CardUseFrame>().Any(u => u.Id == first.UseId && u.CardId == 0 && u.Action?.ActionId == first.ActionId && u.Action.Type == CardActionType.Use),
            "Only after the whole group's three true discards does the first genuine zero-material native Slash commit; its original X stays frozen.");
        s.Cold(); s.Continue(); s.Reach(() => s.G.ResolutionStack.All(f => f.Id != pingxi));
        var uses = s.E<AnniversaryWangChangSlashIssuedEvent>().Where(e => e.FrameId == pingxi).ToArray();
        s.Need(uses.Select(e => e.TargetSeat).SequenceEqual([1, 3]) && uses.Select(e => e.Cursor).SequenceEqual([0, 2]) && uses.All(e =>
                s.E<CardUseDeclaredEvent>().Count(d => d.ResolutionId == e.UseId && d.CardId == 0 && d.CardKind == CardKind.Slash && d.SourceSeat == 0) == 1 &&
                s.E<CardUseFinishedEvent>().Count(d => d.ResolutionId == e.UseId && d.CardId == 0) == 1 && s.E<AnniversaryWangChangSlashReturnedEvent>().Count(d => d.FrameId == pingxi && d.Cursor == e.Cursor && d.UseId == e.UseId && d.ActionId == e.ActionId) == 1) &&
            s.E<AnniversaryWangChangCompletedEvent>().Count(e => e.FrameId == pingxi) == 1,
            "Both adjacent original targets receive separate complete native Slash chains and once-only typed returns after cold recovery; the original distant target still discarded but its presently illegal Slash is skipped.");
        s.Replay();
    }
    private static string? Verb(PromptChoice c) => c.Parameters.GetValueOrDefault("verb");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryWangChang is not null);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void UseKaiji() => Accept(new UseProgramSkillCommand(0, Kaiji, "convert", [], [], G.Revision, P!.PromptId));
        public void Offer() => Reach(() => P?.SkillPrompt?.SkillId == Pingxi && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Menu(AnniversaryWangChangStage stage) => Reach(() => Root?.AnniversaryWangChang?.Stage == stage && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-wang-chang") == true);
        public void Native(int id)
        {
            var a = G.GetHumanLegalActions().First(a => a.CardId == id && a.ConversionSource is null);
            Accept(new PlayCardCommand(0, id, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "The original human must receive the exact current Wang Chang choice.");
            Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision));
        }
        public void Reach(Func<bool> ready)
        {
            for (var step = 0; step < 160; step++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) { Answer(c => c.Cards.Count == 0); continue; }
                    throw new InvalidOperationException("Unexpected Wang Chang native boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The short native Wang Chang scene retains its participants."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Wang Chang missed the bounded original native continuation.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var state = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(state == State(), "Cold preserves the original once-paid HE movement child and current native Slash cursor identically."); }
        public void Replay() { var state = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(state == State(), "The accepted journal replays both actual conversion phases, whole-group discards and ordered native Slash returns exactly."); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Receipt = Root?.AnniversaryWangChang, Started = E<AnniversaryWangChangStartedEvent>(), Last = G.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected exact original Wang Chang command."); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var peerHand = new[] { new ShanheStartingCard(CardKind.Dodge, Suit.Heart, 2), new ShanheStartingCard(CardKind.Dodge, Suit.Club, 3) };
            var setup = new ShanheBattleSetup(47, 1, "anniversary-wang-chang", Hero, 3, 0, 0, 0, 0, [Driver, Watch], [new(CardKind.Crossbow, Suit.Spade, 1)], [],
                [new(Peer) { StartingHand = peerHand }, new(Peer) { StartingHand = peerHand }], [new(Peer) { StartingHand = peerHand }], new string('c', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
            var scene = new Scene(g, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-wang-chang", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var c = SkillProgramCatalog.Load(Rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            {
                [Driver] = new { name = "真实额外出牌", description = "同一回合的两个实际出牌阶段。" }, [Quiet] = new { name = "安静同桌", description = "保留原生摸牌和弃牌。" },
                [Watch] = new { name = "原生已付子窗", description = "真实付款或原生杀提交后暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            } }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            b.AddGeneral(new(Peer, "固定同桌", "", Quiet, "wei", 12));
            b.AddDeck(new(Deck, "固定小闪牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 2)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "王昶原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        private static string Rules => $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
          {"id":"{{Driver}}","revision":1,"triggers":[{"id":"extra-play","window":"turnStartBeforeNormalFlow","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"isSkillInvocation":false,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
          {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
          {"id":"{{Watch}}","revision":1,"triggers":[
            {"id":"paid-he","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementReasons":["skill-program.anniversary:wang-chang.discard"],"movementOccurrence":"perBatch","usageScope":"game","usageLimit":1,"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid-he","options":[{"id":"continue"}]}]},
            {"id":"native-slash","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"usageScope":"game","usageLimit":1,"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"native-slash","options":[{"id":"continue"}]}]}
          ]}
        ]}
        """;
    }
}
