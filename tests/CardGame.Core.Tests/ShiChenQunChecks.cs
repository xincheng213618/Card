using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiChenQunChecks
{
    private const string Hero = "mobile:shi-chen-qun", Dingpin = "mobile:shi-dingpin", Faen = "mobile:shi-faen",
        Driver = "fixture:shi-chen-qun-driver", Watch = "fixture:shi-chen-qun-watch", Quiet = "fixture:shi-chen-qun-quiet",
        Peer = "fixture:shi-chen-qun-peer", Deck = "fixture:shi-chen-qun-deck";

    public static void NativeSelfBenefitsOppositeDebtPaidDiscardColdAndPurePlayedSlashes()
    {
        var s = Scene.Start(); s.Play();
        s.Native(CardKind.Alcohol); s.Play();
        s.Need(s.E<MobileShiChenQunUseRecordedEvent>() is [{ ActorSeat: 0, PreviousActorSeat: null }],
            "The first genuine use has no predecessor and never offers Faen.");
        s.Native(CardKind.Peach); s.OfferFaen(); s.Activate(); s.Effect("discard");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        var paid = s.Root!;
        s.Need(paid.MobileShiChenQunFaen is { Effect: "discard", Debt: null, Completed: false, ActorSeat: 0 } &&
            paid.PendingMovementContinuation is not null && s.E<NativeLogicalDiscardInvocationRecordedEvent>().Single(e => e.ParentFrameId == paid.Id).ActualCount == 1,
            "The original native Peach-use frame retains Faen's exact source while its actual one-card discard is fully paid and its movement child is paused.");
        var movementCount = s.G.CardMovements.Count;
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.V(0).Hp == 3 && s.E<MobileShiChenQunFaenEffectCompletedEvent>() is [{ ActualCount: 1, Effect: "discard", WasDebt: false }] &&
            s.E<MobileShiChenQunFaenDebtIssuedEvent>() is [{ Effect: "draw", AfterUseSequence: 2 }] && s.G.CardMovements.Count == movementCount + 1,
            "Cold recovery returns the paid discard once, completes the original Peach and issues exactly its opposite draw for the next real use.");

        var before = s.V(0).HandCount; s.Native(CardKind.DrawTwo); s.OfferFaen();
        s.Need(s.E<MobileShiChenQunFaenEffectCompletedEvent>().Count(e => e.WasDebt && e.Effect == "draw" && e.ActualCount == 1) == 1,
            "The exact next real use first pays the older one-card native draw obligation.");
        s.Activate(); s.Effect("draw"); s.Play();
        s.Need(s.V(0).HandCount == before + 3 && s.E<MobileShiChenQunFaenDebtIssuedEvent>().Last() is { Effect: "discard", AfterUseSequence: 3 } &&
            s.E<MobileShiChenQunFaenEffectCompletedEvent>().Count(e => !e.WasDebt) == 2,
            "The same DrawTwo use then receives the second optional Faen draw and its own native two draws, with a separate opposite discard issued once.");

        s.Native(CardKind.Duel, 1);
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        s.Answer(c => c.Cards.Count == 1 && s.V(0).Hand.Any(card => card.Id == c.Cards[0] && card.Kind == CardKind.Slash));
        s.Play();
        s.Need(s.E<DuelResponseEvent>().Count(e => e.UsedSlash) == 2 &&
            s.E<MobileShiChenQunUseRecordedEvent>().Length == 4 &&
            s.E<MobileShiChenQunFaenDebtClaimedEvent>().Length == 2 &&
            s.E<MobileShiChenQunFaenEffectCompletedEvent>().Count(e => e.WasDebt && e.Effect == "discard" && e.ActualCount == 1) == 1,
            "Both players really play a hand Slash in Duel; those responses neither become used cards nor advance or repay the use-only debt.");
        s.Native(CardKind.DrawTwo); s.Play();
        s.Need(s.E<MobileShiChenQunFaenEffectCompletedEvent>().Count(e => !e.WasDebt) == 2 &&
            s.V(0).SkillRuntimeStates!.Single(state => state.SkillId == Faen).Usages.Where(usage => usage.Scope == SkillUsageScope.Turn).Sum(usage => usage.Count) == 2,
            "A later real own use cannot create a third optional Faen invocation in the same actual turn.");
        s.Replay();
    }

    public static void DodgeUseTriggersFaenAndDingpinCountsActualLossesTiesAndRoundQuota()
    {
        var s = Scene.Start(response: true); s.Play();
        s.Native(CardKind.Slash, 1); s.OfferFaen();
        s.Need(s.Root is null && s.E<MobileShiChenQunUseRecordedEvent>().Last() is { ActorSeat: 1, PreviousActorSeat: 0 } &&
            s.E<CardActionAcceptedEvent>().Any(e => e.Action is { ActorSeat: 1, Type: CardActionType.Response, EffectiveKind: CardKind.Dodge }),
            "The real defender's use-qualified Dodge is the next used card and offers Faen before finishing the original Slash.");
        s.Activate(); s.Effect("draw"); s.Play();
        s.Need(s.E<MobileShiChenQunFaenEffectCompletedEvent>() is [{ ActorSeat: 1, ActualCount: 1, Effect: "draw" }] && s.V(1).Hp == 8,
            "A true used Dodge receives its actual native draw and still successfully offsets the original Slash.");
        s.Native(CardKind.Alcohol); s.Play();
        s.Need(s.E<MobileShiChenQunFaenDebtClaimedEvent>() is [{ ActorSeat: 0, UseSequence: 3 }] &&
            s.E<MobileShiChenQunFaenEffectCompletedEvent>().Last() is { WasDebt: true, Effect: "discard", ActualCount: 1 },
            "The next ordinary use pays the Dodge-origin opposite discard exactly once; the prior used card belongs to the defender, so no extra optional activation is offered.");
        s.Driver("strip-three", [2]); s.Play(); s.End(); s.OfferDingpin(); s.Activate();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Dingpin && s.P.Choices.Any(c => c.Targets.Count > 0));
        var targets = s.P!.Choices.Where(c => c.Targets.Count > 0).SelectMany(c => c.Targets).Distinct().Order().ToArray();
        s.Need(targets.SequenceEqual([0, 2]),
            "Three real owned departures tie with one actual three-card discard batch. Processing-to-discard completion never counts the two used physical cards a second time.");
        s.Answer(c => c.Targets.SequenceEqual([2])); s.Play();
        s.Need(s.V(2).HandCount == 2 && s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Dingpin) == 1,
            "The tied other recipient really draws two; the other actual endings in the same round cannot activate Dingpin again.");
        s.Need(s.V(0).SkillRuntimeStates!.Single(state => state.SkillId == Dingpin).PublicRuleStates!
            .Single(state => state.Scope == SkillUsageScope.Round).Used == 0,
            "The next genuine round resets the public Dingpin allowance without changing the already-paid first-round draw.");
        s.Replay();
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(player => player.Seat == seat);
        public T[] E<T>() => G.Events.Select(envelope => envelope.Payload).OfType<T>().ToArray();
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(frame => frame.MobileShiChenQunFaen is not null);
        public void Native(CardKind kind, int? target = null)
        {
            var action = G.GetHumanLegalActions().First(a => a.CardId is > 0 && a.ConversionSource is null &&
                V(0).Hand.Any(card => card.Id == a.CardId && card.Kind == kind) && (target is null || a.TargetSeats.SequenceEqual([target.Value])));
            Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId));
        }
        public void Driver(string id, int[] targets) => Accept(new UseProgramSkillCommand(0, ShiChenQunChecks.Driver, id, [], targets, G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void OfferFaen() => Reach(() => P?.SkillPrompt?.SkillId == Faen && P.Choices.Any(c => Key(c) == "activate"));
        public void OfferDingpin() => Reach(() => P?.SkillPrompt?.SkillId == Dingpin && P.Choices.Any(c => Key(c) == "activate"));
        public void Activate() => Answer(c => Key(c) == "activate");
        public void Effect(string effect)
        {
            Reach(() => P?.SkillPrompt?.SkillId == Faen && P.Choices.Any(c => c.Parameters.GetValueOrDefault("effect") == effect));
            Answer(c => c.Parameters.GetValueOrDefault("effect") == effect);
        }
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Answer(Func<PromptChoice, bool> pick)
        {
            var pending = P!; var choice = pending.Choices.FirstOrDefault(pick); Need(choice is not null, "The expected real Shi Chen Qun choice must be published.");
            Accept(new AnswerPromptCommand(pending.PlayerSeat, pending.PromptId, choice!.Id, G.Revision));
        }
        public void Reach(Func<bool> ready)
        {
            for (var step = 0; step < 250; step++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.SkillPrompt?.SkillId == Watch) { Continue(); continue; }
                    if (p.Choices.Any(c => Key(c) == "skip")) { Answer(c => Key(c) == "skip"); continue; }
                    if (p.Choices.Any(c => Key(c) == "finish-owned-cards")) { Answer(c => Key(c) == "finish-owned-cards"); continue; }
                    if (p.Choices.Any(c => Key(c) == "select-owned-cards"))
                    {
                        var dodge = p.Choices.FirstOrDefault(c => Key(c) == "select-owned-cards" &&
                            c.Cards.Count == 1 && V(0).Hand.Any(card => card.Id == c.Cards[0] && card.Kind == CardKind.Dodge));
                        Answer(c => dodge is null ? Key(c) == "select-owned-cards" : c.Id == dodge.Id); continue;
                    }
                    if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null); continue; }
                    throw new InvalidOperationException("Unexpected Shi Chen Qun boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The short real native scene must retain its participants.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Shi Chen Qun missed the bounded native boundary.");
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "The exact paid original Faen frame, source, next-use debt and native movement child cold-restore identically."); }
        public void Replay()
        { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "All actual uses, pure played responses, original paid Faen debts and tied Dingpin movement counts replay exactly."); }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Shi Chen Qun command."); }
        private static string? Key(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action");
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))).ToArray(),
            Frames = G.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())).ToArray(), G.CardMovements,
            Events = G.Events.Select(envelope => JsonSerializer.Serialize(envelope.Payload, envelope.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, P, Receipt = Root?.MobileShiChenQunFaen,
            Uses = E<MobileShiChenQunUseRecordedEvent>(), Debts = E<MobileShiChenQunFaenDebtIssuedEvent>(), Completed = E<MobileShiChenQunFaenEffectCompletedEvent>(),
            Players = Enumerable.Range(0, 4).Select(seat => new { seat, V(seat).Hp, V(seat).HandCount }) });
        public static Scene Start(bool response = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] opening = response ? [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Alcohol, Suit.Heart, 3), new(CardKind.Dodge, Suit.Club, 9), new(CardKind.DrawTwo, Suit.Diamond, 4)] :
                [new(CardKind.Alcohol, Suit.Heart, 3), new(CardKind.Peach, Suit.Heart, 4), new(CardKind.DrawTwo, Suit.Diamond, 5), new(CardKind.Duel, Suit.Spade, 1),
                    new(CardKind.Slash, Suit.Club, 7), new(CardKind.Slash, Suit.Heart, 8), new(CardKind.Dodge, Suit.Club, 9), new(CardKind.Dodge, Suit.Heart, 10), new(CardKind.DrawTwo, Suit.Spade, 11)];
            var setup = new ShanheBattleSetup(73, 1, response ? "shi-chen-qun-response" : "shi-chen-qun-faen", Hero, 2, 0, 0, 0, 0,
                [ShiChenQunChecks.Driver, Watch], opening, [],
                [new(Peer, Hp: 8) { StartingHand = response ? [new(CardKind.Dodge, Suit.Heart, 2)] : [new(CardKind.Slash, Suit.Spade, 7)] },
                 new(Peer, Hp: 8) { StartingHand = response ? [new(CardKind.Dodge, Suit.Heart, 3), new(CardKind.Dodge, Suit.Club, 4), new(CardKind.Dodge, Suit.Diamond, 5)] : [] },
                 new(Peer, Hp: 8) { StartingHand = [] }], [], new string('c', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-chen-qun", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep-materials","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[
                {"id":"strip-three","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","zones":["hand"],"amount":3,"resultBind":"stripped"},{"op":"moveBoundCards","target":"owner","sourceBind":"stripped","destination":"discardPile","awaitMovementTriggers":true}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-faen-discard","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["skill-program.mobile:shi-faen.MoveBoundCards"],"movementOccurrence":"perBatch","optional":false,"isSkillInvocation":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"skip-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "真实移动驱动", description = "三张实际手牌一次离开。" },
                    [Watch] = new { name = "已支付法恩弃牌", description = "保留原生移动子窗。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                    [Quiet] = new { name = "安静同桌", description = "保留实际回合并跳过出牌。" } } }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Peer, "实际同桌角色", "", Quiet, "wei", 10));
            builder.AddDeck(new(Deck, "固定真实闪牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "势陈群原生法恩与实际失牌", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
