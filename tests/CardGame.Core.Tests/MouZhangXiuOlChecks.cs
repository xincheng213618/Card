using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouZhangXiuOlChecks
{
    private const string Hero = "mou:zhang-xiu-ol", Quiet = "fixture:mzx-quiet", Ally = "fixture:mzx-ally", EnemyA = "fixture:mzx-enemy-a", EnemyB = "fixture:mzx-enemy-b", Deck = "fixture:mzx-deck";
    public static void ActualPhasesAndPreviousTrueUse()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "mou-zhang-xiu", Hero, 4, 0, 0, 0, 0, [],
            Enumerable.Range(1, 6).Select(rank => new ShanheStartingCard(CardKind.Dodge, Suit.Club, rank)).ToArray(), [],
            [new(EnemyA, Hp: 8) { StartingHand = [] }, new(EnemyB, Hp: 8) { StartingHand = [] }],
            [new(Ally, Hp: 8) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 7), new(CardKind.Dodge, Suit.Diamond, 8)] }], new string('a', 64));
        var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        PendingDecision? P() => Enumerable.Range(0, 4).Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        T[] E<T>() => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
        MouZhangXiuOlReceipt? R() => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.MouZhangXiuOl;
        string Diagnostic() => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase, Prompt = P(),
            Players = g.CreateSnapshot(0).Players, Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = g.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouZhangXiuOl", StringComparison.Ordinal) || e.Payload is CardUseDeclaredEvent or CardUseFinishedEvent or DamageAppliedEvent).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message + Diagnostic()); }
        void Accept(GameCommand command) { var result = g.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
        void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P()!; Need(p.PlayerSeat == 0, "Only actual human choices may be submitted."); var c = p.Choices.FirstOrDefault(predicate); Need(c is not null, "Expected published choice is absent.");
            Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, g.Revision));
        }
        var declineRest = false;
        void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var step = 0; step < 220; step++)
            {
                if (done()) return; Need(g.State.Winner == Winner.None, "Game ended before native flow at " + line);
                if (P() is not { PlayerSeat: 0 } p) { Accept(new AdvanceOneStepCommand(g.Revision)); continue; }
                if (declineRest && R() is { Stage: MouZhangXiuOlStage.Cost }) { Answer(c => c.Parameters.GetValueOrDefault("branch") == "decline"); continue; }
                if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is not "bagua"); continue; }
                throw new InvalidOperationException("Unexpected human choice at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded actual-phase native flow not reached at " + line + Diagnostic());
        }
        bool Cost(ActualDiscardRecoveryPhaseKind phase) => P() is { PlayerSeat: 0 } && R() is { Stage: MouZhangXiuOlStage.Cost, Phase: { } p } && p.Kind == phase;
        void Pay() => Answer(c => c.Parameters.GetValueOrDefault("branch") == "card");
        Accept(new StartGameCommand()); Reach(() => P() is { PlayerSeat: 0 } && R() is { Stage: MouZhangXiuOlStage.Target });
        Answer(c => c.Targets.SequenceEqual([1])); Need(Cost(ActualDiscardRecoveryPhaseKind.Preparation), "Limited target acceptance retains the real Preparation opportunity."); Pay();
        Reach(() => Cost(ActualDiscardRecoveryPhaseKind.Judgment));
        var first = E<MouZhangXiuOlQiulieIssuedEvent>().Single().Origin;
        Need(E<MouZhangXiuOlQiulieNullifiedEvent>() is [{ TargetSeat: 1 }] && E<MouZhangXiuOlQiulieReturnedEvent>().Count(e => e.UseId == first.UseId) == 1 &&
            !E<MouZhangXiuOlQualifiedEvent>().Any(e => e.Entry.UseId == first.UseId) && E<DamageAppliedEvent>().Length == 0,
            "The first true Use has no previous card. A real NPC basic discard nullifies the paid native Slash, which still finishes and returns exactly once.");
        Pay(); Reach(() => P() is { PlayerSeat: 0 } && R() is { Stage: MouZhangXiuOlStage.Penalty });
        var second = E<MouZhangXiuOlQiulieIssuedEvent>().Last().Origin;
        Need(E<MouZhangXiuOlQualifiedEvent>().Single().Entry.Previous.OriginFrameId == first.UseId && E<MouZhangXiuOlDrawReturnedEvent>() is [{ Requested: 1, Actual: 1 }] && E<MouZhangXiuOlQiulieNullifiedEvent>().Length == 2 &&
            E<CardUseFinishedEvent>().Count(e => e.ResolutionId == second.UseId) == 1,
            "The next Slash uses the preceding declared no-damage card, really draws1 and retains its native whole-Use completion before compulsory discard.");
        var view = SnapshotJson.Serialize(g.CreateSnapshot(0)); var costs = E<MouZhangXiuOlDiscardPaidEvent>().Length;
        g = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(SnapshotJson.Serialize(g.CreateSnapshot(0)) == view && E<MouZhangXiuOlDiscardPaidEvent>().Length == costs && R() is { Stage: MouZhangXiuOlStage.Penalty },
            "One representative Cold restores the original completed native Use and compulsory discard without repaying either earlier cost.");
        Pay(); Reach(() => Cost(ActualDiscardRecoveryPhaseKind.Draw));
        Need(E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.Reason.Value == "skill-program.mou-ol:zhuijiao.discard" && e.ActualCount == 1) == 1,
            "A nullified qualifying Slash still causes a genuine, non-recast penalty discard.");
        Pay(); Reach(() => Cost(ActualDiscardRecoveryPhaseKind.Play));
        var third = E<MouZhangXiuOlQiulieIssuedEvent>().Last().Origin;
        Need(E<MouZhangXiuOlQiulieIssuedEvent>().Length == 3 && E<MouZhangXiuOlQiulieReturnedEvent>().Length == 3 && E<MouZhangXiuOlDrawReturnedEvent>().Length == 2 &&
            E<MouZhangXiuOlUseDamageEvent>().Single(e => e.OriginFrameId == third.UseId).Amount == 2 && g.CreateSnapshot(0).Players[1].Hp == 6 &&
            E<MouZhangXiuOlPenaltyStartedEvent>().Length == 1,
            "The third real phase Slash inherits the second no-damage Use, draws1 and deals native2; its actual damage suppresses the final discard.");
        declineRest = true; Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Accept(new EndPlayPhaseCommand(0, g.Revision, P()!.PromptId));
        Reach(() => g.State.TurnNumber > 1 && g.State.CurrentSeat == 0 && P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Need(E<MouZhangXiuOlHuntChosenEvent>().Length == 1 && E<MouZhangXiuOlPhaseOfferedEvent>().Select(e => e.Phase.Kind).SequenceEqual([
            ActualDiscardRecoveryPhaseKind.Preparation, ActualDiscardRecoveryPhaseKind.Judgment, ActualDiscardRecoveryPhaseKind.Draw,
            ActualDiscardRecoveryPhaseKind.Play, ActualDiscardRecoveryPhaseKind.Discard, ActualDiscardRecoveryPhaseKind.Ending]) &&
            E<MouZhangXiuOlPhaseOfferedEvent>().Select(e => e.Phase.Token).Distinct().Count() == 6,
            "The remaining actual phases each retain one optional opportunity; the complete limited hunt does not restart next turn.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-zhang-xiu-ol", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var kind in new[] { CardKind.Dodge, CardKind.Slash }) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mzx-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var definitions = new[] { new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false,
                effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } } };
            var labels = new Dictionary<string, object> { [Quiet] = new { name = "安静同行", description = "跳过真实出牌阶段" } };
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            foreach (var peer in new[] { Ally, EnemyA, EnemyB }) b.AddGeneral(new(peer, "真实同行", "supporter", Quiet, peer == Ally ? "qun" : "wei", 8));
            b.AddDeck(new(Deck, "固定小牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 24).Select(_ => new ContentDeckPhysicalCard("fixture:mzx-card-Dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋张绣真实阶段主流程", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
