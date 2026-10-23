using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class AnniversaryMuShunChecks
{
    private const string Hero = "anniversary:mu-shun", Driver = "fixture:mu-shun-driver", Peer = "fixture:mu-shun-peer",
        Passive = "fixture:mu-shun-passive", Deck = "fixture:mu-shun-deck";
    public static void OncePerActualTurnNativeSourcePindianAndCold()
    {
        var s = Scene.Start(); s.Play(); var original = s.V(0).Hand.Single().Id;
        s.Need(s.Jin == 0 && s.G.GetAttackRange(0) == 1, "Uninjured Mu Shun has no invented Jin or extra native attack range.");
        s.End(); s.Reach(() => s.Root?.MuShunJinjian?.Stage == MuShunContestStage.Choice && s.P?.Choices.Any(c => Mode(c) == "contest") == true);
        var parent = s.Root!; var issued = s.E<MuShunJinjianIssuedEvent>().Single(); var firstTurn = s.G.State.TurnNumber;
        s.Need(s.G.State.CurrentSeat == 1 && s.V(0).Hp == 2 && s.Jin == 1 && s.G.GetAttackRange(0) == 2 && issued.DamageSourceSeat == 1 &&
            issued.MarkerBefore == 0 && issued.MarkerAfter == 1 && s.E<DamageAppliedEvent>().Single(e => e.TargetSeat == 0).Amount == 2,
            "One real two-point damage gives exactly one locked Jin, before the optional contest, and freezes its actual source in the other character's turn.");
        s.Cold(); s.Answer(c => Mode(c) == "contest");
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-source-card"));
        s.Answer(c => c.Cards.SequenceEqual([original])); s.Watch("first");
        var result = s.E<PindianResultDeterminedEvent>().Single(e => e.SkillId == MuShunComposition.Jinjian).Result;
        s.Need(s.E<MuShunShizhaoIssuedEvent>().Length == 1, "The native Pindian last-Hand movement must settle its original Shizhao before returning to the paused driver.");
        var first = s.E<MuShunShizhaoIssuedEvent>().Single();
        s.Need(result.SourceSeat == 0 && result.OpponentSeat == 1 && result.SourceCardId == original && result.SourceRank == 13 && result.OpponentRank == 2 && result.SourceWon &&
            s.Zone(result.SourceCardId) == CardLocation.DiscardPile && s.Zone(result.OpponentCardId) == CardLocation.DiscardPile &&
            s.E<MuShunContestClosedEvent>().Single(e => e.FrameId == parent.Id) is { Accepted: true, Won: true } &&
            s.E<MuShunJinjianIssuedEvent>().Length == 1 && s.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Amount == 1) == 1 && s.V(0).Hp == 3,
            "The restored original source contest really pays both entities, strictly wins and returns to exactly one native recovery without granting Jin again.");
        s.Need(first.ActualTurn == firstTurn && first.ActualTurnOwner == 1 && first.MarkerBefore == 1 && first.MarkerAfter == 0 && first.Draw &&
            s.E<CardsDrawnEvent>().Count(e => e.PlayerSeat == 0 && e.Count == 2) == 1 && s.Jin == 0 && s.G.GetAttackRange(0) == 1 && s.V(0).HandCount == 0 &&
            s.E<MuShunShizhaoIssuedEvent>().Length == 1,
            "The actual Pindian last-Hand loss removes one Jin and natively draws two; losing those actual two cards again in the same foreign turn does not reissue Shizhao.");
        s.Continue(); s.Play(); s.Use("prepare"); s.Play();
        s.Need(s.V(0).Hp == 4 && s.V(0).HandCount == 1 && s.Jin == 0, "A genuine native draw and recovery prepare a new actual-turn empty-Hand opportunity without altering marker state.");
        s.End(); s.Watch("second"); var second = s.E<MuShunShizhaoIssuedEvent>().Last();
        var armIndex = Array.FindIndex(s.G.Events.Select(e => e.Payload).ToArray(), e => Equals(e, second));
        var damages = s.G.Events.Skip(armIndex + 1).Select(e => e.Payload).OfType<DamageAppliedEvent>().Where(e => e.TargetSeat == 0).ToArray();
        s.Need(second.ActualTurn > firstTurn && !second.Draw && second.MarkerBefore == 0 && second.MarkerAfter == 0 && second.ActualTurnOwner == 1 &&
            damages is [{ SourceSeat: 1, Amount: 2 }, { SourceSeat: 1, Amount: 1 }] && s.V(0).Hp == 1 && s.V(0).HandCount == 0 &&
            s.E<MuShunShizhaoDamageSpentEvent>() is [var spent] && spent.ArmId == second.FrameId && spent.ActualTurn == second.ActualTurn &&
            s.E<CardsDrawnEvent>().Count(e => e.PlayerSeat == 0 && e.Count == 2) == 1 && s.Jin == 2 && s.G.GetAttackRange(0) == 3,
            "The next actual turn resets the first-loss opportunity; zero Jin adds one only to the next real damage, without a fake draw, and both real damage events then grant their own Jin.");
        s.Continue(); s.Play(); var before = s.Jin; var hp = s.V(2).Hp;
        s.Use("outgoing", [2]); s.Play();
        s.Need(s.V(2).Hp == hp - 2 && s.Jin == before + 1 && s.G.GetAttackRange(0) == 1 + s.Jin &&
            s.E<PindianResultDeterminedEvent>().Count(e => e.SkillId == MuShunComposition.Jinjian) == 1 &&
            s.E<TurnEndedEvent>().Any(e => e.TurnNumber == second.ActualTurn),
            "Causing a real two-point damage likewise gives one Jin; its own damage source cannot be an illegal self-Pindian, and range has no invented cap.");
        s.Replay();
    }
    private static string? Mode(PromptChoice c) => c.Parameters.GetValueOrDefault("mode");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public int Jin => V(0).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.MuShunJin)?.Count ?? 0;
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.MuShunJinjian is not null);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Watch(string option) => Reach(() => P?.SkillPrompt?.SkillId == Driver && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option));
        public void Continue() => Answer(_ => true);
        public void Use(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(choice is not null, "The real original Mu Shun choice must be published.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision));
        }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 320; i++)
            {
                if (ready()) return;
                if (P is { Kind: DecisionKind.SelectFaction }) { Answer(_ => true); continue; }
                if (P is { PlayerSeat: 0, SkillPrompt.SkillId: Driver } sourceChoice &&
                    G.ResolutionStack.LastOrDefault() is ProgramSkillFrame { SkillId: Driver,
                        TriggerId: "first-foreign-damage" or "second-foreign-damage" } &&
                    sourceChoice.Choices is [var source] && source.Targets.SequenceEqual([G.State.CurrentSeat]) &&
                    source.Parameters.GetValueOrDefault("program-action") == "select-target" &&
                    source.Parameters.GetValueOrDefault("target-kind") == SkillProgramTargetKind.CurrentTurnPlayer.ToString())
                { Answer(c => c.Id == source.Id); continue; }
                if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Mu Shun native boundary. " + Dump());
                Need(G.State.Winner == Winner.None, "The short original-turn scenario keeps all participants alive."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Mu Shun did not reach the bounded native continuation.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            Moves = G.CardMovements, Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "The once-issued Jin and its original damage-source choice cold-restore identically."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "Actual Pindian, last-Hand batches, native draws and one-damage debt consumption replay identically."); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Root = Root?.MuShunJinjian, Jin,
            Views = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).HandCount }), Shizhao = E<MuShunShizhaoIssuedEvent>(),
            Last = G.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected genuine Mu Shun command."); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(47, 1, "anniversary-mu-shun", Hero, 4, 0, 0, 0, 0, [Driver], [new(CardKind.Slash, Suit.Heart, 13)], [],
                [new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }], [new(Peer) { StartingHand = [new(CardKind.Dodge, Suit.Club, 2)] }], new string('d', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, AiPolicyVersion = AiDecisionPolicy.CurrentVersion, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-mu-shun", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var catalog = SkillProgramCatalog.Load($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"states":[{"id":"debt-ready","initialValue":false,"visibility":"public","resetScope":"game","reacquirePolicy":"preserveUntilGameEnd"}],
                "activations":[
                  {"id":"prepare","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1},{"op":"recover","target":"owner","amount":1},{"op":"setBooleanState","target":"owner","stateId":"debt-ready","value":true}]},
                  {"id":"outgoing","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]}],
                "triggers":[
                  {"id":"first-foreign-damage","window":"playPhaseStarting","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"isSkillInvocation":false,"usageScope":"game","usageLimit":1,"effects":[
                    {"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},
                    {"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2},
                    {"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]},
                    {"op":"chooseOption","target":"owner","resultBind":"first-done","options":[{"id":"first"}]}]},
                  {"id":"second-foreign-damage","window":"playPhaseStarting","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"isSkillInvocation":false,"usageScope":"game","usageLimit":1,
                    "condition":{"kind":"booleanState","stateId":"debt-ready","expectedValue":true},"effects":[
                      {"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},
                      {"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]},
                      {"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1},
                      {"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1},
                      {"op":"chooseOption","target":"owner","resultBind":"second-done","options":[{"id":"second"}]}]}]},
              {"id":"{{{Passive}}}","revision":1,"modifiers":[{"id":"keep-original-input","query":"handLimit","operation":"add","value":20,"priority":0}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            { [Driver] = new { name = "原生实际回合驱动", description = "真实伤害、弃牌、摸牌与回复。", optionLabels = new Dictionary<string, string> { ["first"] = "继续原回合", ["second"] = "继续原回合" } },
              [Passive] = new { name = "安静同桌", description = "保留拼点的原手牌，不增加主动行为。" } } }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实同桌", "", Passive, "wei", 10));
            b.AddDeck(new(Deck, "固定原生摸牌库存", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "穆顺真实来源拼点与实际回合失诏", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
