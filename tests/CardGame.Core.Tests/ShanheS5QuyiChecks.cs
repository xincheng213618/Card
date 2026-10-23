using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5QuyiChecks
{
    private const string Driver = "fixture:s5-quyi-driver", Quiet = "fixture:s5-quyi-quiet";
    private const string Peer = "fixture:s5-quyi-peer", Caller = "fixture:s5-quyi-caller", Deck = "fixture:s5-quyi-deck";

    public static void FuqiDistantDuelAndActualCounterspellUserRecoverCold()
    {
        var s = Scene.Start("duel"); s.Play();
        var slash = s.V(2).Hand.Single(c => c.Kind == CardKind.Slash).Id;
        Need(s.G.GetCombatDistance(0, 2) == 2, "The real Duel target must be beyond ordinary Fuqi's distance-one boundary.");
        s.BeginCard(CardKind.Duel, 2);
        s.Until(() => s.P is { Kind: DecisionKind.Nullification, PlayerSeat: 0 });
        var use = s.E<CardUseDeclaredEvent>().Last(e => e.CardKind == CardKind.Duel).ResolutionId;
        Need(s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "nullification"),
            "Quyi may still respond to his own card while every other character is prohibited.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("response") == "pass"); s.Play();
        Need(s.E<NullificationRequestedEvent>().Where(e => e.ResolutionId == use).All(e => e.ResponderSeat == 0) &&
            s.E<DamageAppliedEvent>().Single(e => e.SourceCardUseResolutionId == use) is { TargetSeat: 2, Amount: 1 } &&
            s.V(2).Hand.Any(c => c.Id == slash) && !s.E<CardRespondedEvent>().Any(e => e.ResponderSeat == 2),
            "Non-target counterspell holders and the distant Duel target cannot respond; its actual Slash remains in Hand and native Duel damage resolves.");
        s.Cold();

        s = Scene.Start("counter"); s.Play(); s.EndPlay();
        s.Until(() => s.P is { Kind: DecisionKind.Nullification, PlayerSeat: 0 });
        var pending = s.G.ResolutionStack.OfType<NullificationWindowFrame>().Single();
        Need(pending.SourceSeat == 1 && pending.EffectCardKind == CardKind.DrawTwo && pending.ChainDepth == 0,
            "A different actual card user must initiate this counterspell witness.");
        var original = pending.ParentFrameId;
        s.Answer(c => c.Parameters.GetValueOrDefault("response") == "nullification");
        s.Until(() => s.P is { Kind: DecisionKind.Nullification, PlayerSeat: 0 } &&
            s.E<NullificationRespondedEvent>().Any(e => e.ResolutionId == original && e.ChainDepth == 1));
        Need(s.E<NullificationRespondedEvent>().Single(e => e.ResolutionId == original) is { ResponderSeat: 0, ChainDepth: 1 } &&
            !s.E<NullificationRequestedEvent>().Any(e => e.ResolutionId == original && e.ChainDepth == 1 && e.ResponderSeat != 0),
            "After Quyi's actual Nullification, other characters cannot counter that card even though the initial DrawTwo user was not Quyi; Quyi may counter his own card.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("response") == "nullification");
        s.Until(() => s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == original));
        Need(s.E<NullificationRespondedEvent>().Where(e => e.ResolutionId == original).Select(e => e.ResponderSeat).SequenceEqual([0, 0]) &&
            !s.E<NullificationRequestedEvent>().Any(e => e.ResolutionId == original && e.ChainDepth > 0 && e.ResponderSeat != 0) &&
            s.G.CardMovements.Count(m => m.From == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Nullification) == 2,
            "Both real counterspells pay their original entities once and other holders never receive a counter-response opportunity.");
        s.Cold();
    }

    public static void JiaoziTiedMaximumRoundTwoDamageAndPaidCardPredictionRecoverCold()
    {
        var s = Scene.Start("damage"); s.Play(); s.EndPlay();
        s.Until(() => s.P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && s.E<RoundStartedEvent>().LastOrDefault()?.RoundNumber == 2);
        Need(s.V().HandCount == 6 && s.V(1).HandCount == 6 && s.E<RoundStartedEvent>().LastOrDefault()?.RoundNumber == 2,
            "The real second round begins with a tied maximum of six physical hand cards.");
        s.Cold();
        s.Use("outgoing", [1]);
        Need(s.E<DamageAppliedEvent>().Last() is { SourceSeat: 0, TargetSeat: 1, Amount: 3, SourceLess: false },
            "A tied maximum increases actual cardless outgoing damage by the current round, two, rather than one.");
        s.Use("incoming", [1]);
        Need(s.E<DamageAppliedEvent>().Last() is { SourceSeat: 1, TargetSeat: 0, Amount: 3, SourceLess: false },
            "The same tied maximum also increases actual incoming damage by two.");
        s.Use("self");
        Need(s.E<DamageAppliedEvent>().Last() is { SourceSeat: 0, TargetSeat: 0, Amount: 3, SourceLess: false },
            "Self damage counts one owning skill instance rather than duplicating it for both participant roles.");
        s.Use("source-less");
        Need(s.E<DamageAppliedEvent>().Last() is { TargetSeat: 0, Amount: 3, SourceLess: true },
            "Source-less damage still receives the victim's round-scaled modifier without inventing a source skill.");
        s.Cold();
        var action = s.Action(CardKind.Slash, 3);
        Need(s.G.PredictPublicActionDamage(0, action, 3).DamageAmount == 1,
            "The public proposed Slash pays one hand material before comparing maxima, so its formerly tied owner gets no speculative bonus.");
        s.Begin(action); s.Play();
        Need(s.V().HandCount == 5 && s.V(1).HandCount == 6 &&
            s.E<DamageAppliedEvent>().Last() is { SourceSeat: 0, TargetSeat: 3, Amount: 1, SourceCardUseResolutionId: not null },
            "The native Slash's actual payment loses the tied maximum and causes exactly its original one damage.");
        var count = s.E<DamageAppliedEvent>().Length; var hp = s.V().Hp;
        s.Use("lose");
        Need(s.V().Hp == hp - 1 && s.E<DamageAppliedEvent>().Length == count,
            "Direct HP loss remains a one-point non-damage payment.");
        s.Cold();
    }

    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        // Trusted test driver inspects only each actual recipient's seat view.
        public PendingDecision? P => Enumerable.Range(0, G.PlayerCount).Select(seat => G.CreateSnapshot(seat).PendingDecision)
            .FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Accept(GameCommand command)
        { var result = G.Submit(command); Need(result.Accepted, result.Error?.ToString() ?? "The native Quyi command was rejected."); }
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Use(string id, int[]? targets = null)
        { Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId)); Play(); }
        public LegalAction Action(CardKind kind, int target)
        {
            var card = V().Hand.First(c => c.Kind == kind);
            return G.GetHumanLegalActions().Single(a => a.CardId == card.Id && a.TargetSeats.SequenceEqual([target]));
        }
        public void BeginCard(CardKind kind, int target) => Begin(Action(kind, target));
        public void Begin(LegalAction action) => Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
        public void Until(Func<bool> done)
        {
            for (var n = 0; n < 160; n++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The small Quyi fixture must keep its combatants alive.");
                if (P?.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Quyi choice: " + JsonSerializer.Serialize(P));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded Quyi native continuation was not reached.");
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, G.PlayerCount).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves real Quyi responses, paid hand counts, rounds, damage and every private player view.");
        }
        public static Scene Start(string mode)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardShanheS5QuyiPackage(), new Fixture());
            ShanheStartingCard[] starting = mode switch
            {
                "damage" => [new(CardKind.Slash), new(CardKind.Duel), new(CardKind.DrawTwo), new(CardKind.Nullification), new(CardKind.Nullification), new(CardKind.Nullification)],
                "duel" => [new(CardKind.Duel), new(CardKind.Nullification)],
                _ => [new(CardKind.Nullification), new(CardKind.Nullification)]
            };
            var roster = mode switch
            {
                "damage" => new ShanheCombatantDefinition[] { new(Peer, Hp: 24, MaxHpBonus: 20) { StartingHand = Enumerable.Range(0, 6).Select(_ => new ShanheStartingCard(CardKind.Dodge)).ToArray() },
                    new(Peer, Hp: 24, MaxHpBonus: 20) { StartingHand = [] }, new(Peer, Hp: 24, MaxHpBonus: 20) { StartingHand = [] } },
                "duel" => [new(Peer, Hp: 24, MaxHpBonus: 20) { StartingHand = [new(CardKind.Nullification)] },
                    new(Peer, Hp: 24, MaxHpBonus: 20) { StartingHand = [new(CardKind.Slash), new(CardKind.Nullification)] },
                    new(Peer, Hp: 24, MaxHpBonus: 20) { StartingHand = [new(CardKind.Nullification)] }],
                _ => [new(Caller, Hp: 24, MaxHpBonus: 20) { StartingHand = [new(CardKind.DrawTwo), new(CardKind.Nullification)] },
                    new(Peer, Hp: 24, MaxHpBonus: 20) { StartingHand = [new(CardKind.Nullification)] },
                    new(Peer, Hp: 24, MaxHpBonus: 20) { StartingHand = [new(CardKind.Nullification)] }]
            };
            var setup = new ShanheBattleSetup(91, 1, "fixture-s5-quyi", StandardShanheS5QuyiPackage.QuyiId, 24, 20, 0, 0, 0,
                [Driver], starting, [], roster, [], new string('a', 64));
            var g = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12
            }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-s5-quyi", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":40,"priority":0}],"activations":[
                {"id":"outgoing","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"sourceRef":{"kind":"owner"}}]},
                {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1,"sourceRef":{"kind":"selectedTarget"}}]},
                {"id":"self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1,"sourceRef":{"kind":"owner"}}]},
                {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
                {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet }.ToDictionary(id => id, id => new { name = id, description = "实际伤害和固定NPC过牌" })
            }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "静默目标", "supporter", Quiet, "qun", 4));
            b.AddGeneral(new(Caller, "真实锦囊使用者", "supporter", "standard:none", "qun", 4));
            b.AddDeck(new(Deck, "固定无摸牌流程", 0, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray()
            });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "麹义原生流程", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [StandardShanheS5QuyiPackage.QuyiId, Peer, Caller], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
