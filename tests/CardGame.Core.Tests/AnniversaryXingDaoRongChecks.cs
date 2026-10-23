using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class AnniversaryXingDaoRongChecks
{
    private const string Hero = "anniversary:xing-dao-rong", Skill = "anniversary:xuhe", Peer = "fixture:xing-dao-rong-peer",
        Quiet = "fixture:xing-dao-rong-quiet", Watch = "fixture:xing-dao-rong-watch", Deck = "fixture:xing-dao-rong-deck";
    public static void PaidNearbyGroupAndIndependentEndingCold()
    {
        var s = Scene.Start(); s.Offer(); var original = Enumerable.Range(0, 4).ToDictionary(i => i, i => s.V(i).Hand.Single().Id);
        s.Activate(); s.Menu(XingDaoRongXuHeStage.Mode);
        s.Need(s.V(0).Hp == 5 && s.V(0).MaxHp == 5 && s.E<MaximumHpChangedEvent>().Count(e => e.PlayerSeat == 0 && e.SkillId == Skill && e.Delta == -1) == 1,
            "The native maximum-HP cost and actual HP clamp finish before choosing one whole nearby-group effect.");
        s.Answer(c => Mode(c) == "discard"); s.Menu(XingDaoRongXuHeStage.Card);
        var firstFrame = s.Root!.Id;
        s.Need(s.Root.XingDaoRongXuHe!.Targets.SequenceEqual([0, 1, 3]) && s.Root.XingDaoRongXuHe.Index == 0,
            "Execution-time distance includes the owner at zero and the two actual neighbors, excluding the opposite seat.");
        s.Answer(c => Mode(c) == "card"); s.PaidWatch();
        s.Need(s.Root!.XingDaoRongXuHe is { Stage: XingDaoRongXuHeStage.DiscardChildren, Index: 0 } && s.Zone(original[0]) == CardLocation.DiscardPile &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == firstFrame && e.ExecutorSeat == 0 && e.SubjectSeat == 0 && e.ActualCount == 1) == 1,
            "The original frame has paid the owner's real logical discard exactly once before its real movement observer child.");
        s.Cold(); s.Continue(); s.Menu(XingDaoRongXuHeStage.Card);
        s.Need(s.Root!.XingDaoRongXuHe!.Targets[s.Root.XingDaoRongXuHe.Index] == 1 && s.P!.PlayerSeat == 0 &&
            s.P.Choices.All(c => c.Cards.Count == 0 && c.Parameters["card-owner-seat"] == "1" && c.Parameters["source-zone"] == "Hand"),
            "Cold return advances to the first other participant's opaque Hand slot without revealing its entity ID or repeating the paid owner discard.");
        s.Answer(c => Mode(c) == "card"); s.Menu(XingDaoRongXuHeStage.Card);
        s.Need(s.Root!.XingDaoRongXuHe!.Targets[s.Root.XingDaoRongXuHe.Index] == 3, "The original table-order cursor reaches the other neighbor once.");
        s.Answer(c => Mode(c) == "card"); s.Play();
        s.Need(new[] { 0, 1, 3 }.All(i => s.V(i).HandCount == 0 && s.Zone(original[i]) == CardLocation.DiscardPile) && s.Zone(original[2]) == CardLocation.Hand(2) &&
            s.E<XingDaoRongXuHeDiscardedEvent>().Where(e => e.FrameId == firstFrame).Select(e => e.TargetSeat).SequenceEqual([0, 1, 3]) &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == firstFrame && e.ExecutorSeat == 0 && e.ActualCount == 1) == 3,
            "A unified discard pays one genuine card from every eligible participant, while the distance-two participant retains its actual card.");
        s.End(); s.Ending(); s.Answer(c => Option(c) == "recover"); s.EndWatch();
        s.Need(s.V(0).MaxHp == 6 && s.V(0).Hp == 6 && s.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Amount == 1) == 1,
            "Ending raises native maximum HP and separately recovers one, rather than treating a maximum increase as recovery.");
        s.Continue(); s.Offer(); s.Activate(); s.Menu(XingDaoRongXuHeStage.Mode); var secondFrame = s.Root!.Id;
        s.Answer(c => Mode(c) == "draw"); s.Play();
        var draws = s.E<XingDaoRongXuHeDrawnEvent>().Where(e => e.FrameId == secondFrame).ToArray();
        s.Need(draws.Select(e => e.TargetSeat).SequenceEqual([0, 1, 3]) && draws.All(e => e.ActualCount == 1 && e.After > e.Before) &&
            new[] { 0, 1, 3 }.All(i => s.V(i).HandCount == 1) && s.V(2).HandCount == 1 && s.Zone(original[2]) == CardLocation.Hand(2) &&
            draws.All(e => s.G.CardMovements.Count(m => m.Sequence > e.Before && m.Sequence <= e.After && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(e.TargetSeat) && m.Reason.Value == "skill-program.anniversary:xuhe.nearby-draw") == 1),
            "The other uniform option issues three ordered native draws with real materials and actual recipients, including self.");
        s.End(); s.Ending(); var hand = s.V(0).HandCount; s.Answer(c => Option(c) == "draw"); s.EndWatch();
        s.Need(s.V(0).MaxHp == 6 && s.V(0).Hp == 5 && s.V(0).HandCount == hand + 2,
            "The ending draw choice obtains two real cards and does not silently recover the prior HP clamp.");
        s.Continue(); s.Offer(); var paidStarts = s.E<XingDaoRongXuHeStartedEvent>().Length; s.Skip(); s.Play();
        s.End(); s.Ending(); s.Answer(c => Option(c) == "recover"); s.EndWatch();
        s.Need(s.V(0).MaxHp == 7 && s.V(0).Hp == 6 && s.E<XingDaoRongXuHeStartedEvent>().Length == paidStarts,
            "The independently required ending benefit occurs even when the start opportunity was declined.");
        s.Continue(); s.Offer(); s.Skip(); s.Play();
        var raised = s.E<MaximumHpChangedEvent>().Count(e => e.PlayerSeat == 0 && e.SkillId == Skill && e.Delta == 1);
        s.End(); s.EndWatch();
        s.Need(s.V(0).MaxHp == 7 && s.V(0).Hp == 6 && s.E<MaximumHpChangedEvent>().Count(e => e.PlayerSeat == 0 && e.SkillId == Skill && e.Delta == 1) == raised &&
            s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Skill && e.BindingId == XingDaoRongComposition.Ending) == 3 && paidStarts == 2,
            "Tying the highest maximum counts as highest: no extra maximum, no benefit menu and no invented dependence on the declined start.");
        s.Replay();
    }
    private static string? Mode(PromptChoice c) => c.Parameters.GetValueOrDefault("mode");
    private static string? Option(PromptChoice c) => c.Parameters.GetValueOrDefault("option-id");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.XingDaoRongXuHe is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int card) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location;
        public void Offer() => Reach(() => P?.SkillPrompt?.SkillId == Skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("binding-id") == XingDaoRongComposition.Starting));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Menu(XingDaoRongXuHeStage stage) => Reach(() => Root?.XingDaoRongXuHe?.Stage == stage && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-xing-dao-rong") == true);
        public void PaidWatch() => Reach(() => P?.SkillPrompt?.SkillId == Watch && Option(P.Choices[0]) == "paid");
        public void EndWatch() => Reach(() => P?.SkillPrompt?.SkillId == Watch && Option(P.Choices[0]) == "ending");
        public void Ending() => Reach(() => P?.SkillPrompt?.SkillId == Skill && P.Choices.Any(c => Option(c) == "recover"));
        public void Continue() => Answer(c => Option(c) is "paid" or "ending");
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(choice is not null, "The exact original Xuhe choice must be published.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision));
        }
        public void Reach(Func<bool> ready)
        {
            for (var step = 0; step < 360; step++)
            {
                if (ready()) return;
                if (P is { Kind: DecisionKind.SelectFaction }) { Answer(_ => true); continue; }
                if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Xuhe native boundary. " + Dump());
                Need(G.State.Winner == Winner.None, "The bounded nearby-group scene keeps all participants alive."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Xuhe did not reach the bounded original native continuation.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "The once-paid original group cursor and its real movement child restore identically."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "Group payments, native draws and independent native maximum/HP benefits replay identically."); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Root = Root?.XingDaoRongXuHe,
            Views = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).MaxHp, V(i).HandCount }), Last = G.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected genuine Xuhe command."); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(47, 1, "anniversary-xing-dao-rong", Hero, 6, 0, 0, 0, 0, [Watch], [new(CardKind.Dodge, Suit.Club, 2)], [],
                [new(Peer) { StartingHand = [new(CardKind.Dodge, Suit.Club, 5)] }, new(Peer) { StartingHand = [new(CardKind.Dodge, Suit.Diamond, 6)] }],
                [new(Peer) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 4)] }], new string('b', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, AiPolicyVersion = AiDecisionPolicy.CurrentVersion, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-xing-dao-rong", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Quiet}}}","revision":1,"modifiers":[{"id":"retain-input","query":"handLimit","operation":"add","value":20,"priority":0}],
                "triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[
                {"id":"paid-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["skill-program.anniversary:xuhe.discard"],"movementOccurrence":"perBatch","optional":false,"isSkillInvocation":false,
                  "effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"paid"}]}]},
                {"id":"ending-boundary","window":"playEnding","subject":"owner","turnOwnerScope":"own","priority":-999,"optional":false,"isSkillInvocation":false,
                  "effects":[{"op":"chooseOption","target":"owner","resultBind":"ending","options":[{"id":"ending"}]}]}
              ]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> {
                [Quiet] = new { name = "安静同桌", description = "保留真实手牌、回合与上限，仅跳过主动出牌。" },
                [Watch] = new { name = "原生付款与阶段子窗", description = "在真正原生移牌及结束阶段边界暂停。", optionLabels = new Dictionary<string,string> { ["paid"] = "继续已付弃牌", ["ending"] = "继续实际阶段" } }
            } }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实高上限同桌", "", Quiet, "wei", 7));
            b.AddDeck(new(Deck, "固定原生无攻击库存", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "虚猲真实临近群体与独立结束阶段", 4, 4, new Dictionary<string,int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string,int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
