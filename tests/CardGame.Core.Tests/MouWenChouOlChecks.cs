using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouWenChouOlChecks
{
    private const string Hero = "mou:wen-chou-ol", Quiet = "fixture:mwc-quiet", Ally = "fixture:mwc-ally", EnemyA = "fixture:mwc-enemy-a", EnemyB = "fixture:mwc-enemy-b", Deck = "fixture:mwc-deck";
    public static void NativeCountsAndFirstAllHand()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "mou-wen-chou", Hero, 4, 0, 0, 0, 0, [],
            [new(CardKind.Dodge, Suit.Club, 2), new(CardKind.Dodge, Suit.Spade, 3)], [],
            [new(EnemyA, Hp: 8) { StartingHand = [] }, new(EnemyB, Hp: 8) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Club, 4), new(CardKind.Dodge, Suit.Spade, 5)] }],
            [new(Ally, Hp: 8) { StartingHand = [] }], new string('a', 64));
        var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        PendingDecision? P() => Enumerable.Range(0, 4).Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        T[] E<T>() => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
        MouWenChouOlReceipt? R() => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.MouWenChouOl;
        string Diagnostic() => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase, Prompt = P(), Players = g.CreateSnapshot(0).Players,
            Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = g.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouWenChou", StringComparison.Ordinal) || e.Payload is CardUseDeclaredEvent or CardUseFinishedEvent or DamageAppliedEvent).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + Diagnostic()); }
        void Accept(GameCommand command) { var result = g.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
        void Answer(Func<PromptChoice, bool> predicate)
        { var p = P()!; Need(p.PlayerSeat == 0, "Only the actual human chooses."); var c = p.Choices.FirstOrDefault(predicate); Need(c is not null, "Expected real choice is absent."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, g.Revision)); }
        void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var step = 0; step < 180; step++)
            {
                if (done()) return; Need(g.State.Winner == Winner.None, "Game ended before native main flow at " + line);
                if (P() is not { PlayerSeat: 0 } prompt) { Accept(new AdvanceOneStepCommand(g.Revision)); continue; }
                if (prompt.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is not "bagua"); continue; }
                throw new InvalidOperationException("Unexpected human input at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded native Wen Chou flow not reached at " + line + Diagnostic());
        }
        void Play() => Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void Use(int count, int target, int[] cards) => Accept(new UseProgramSkillCommand(0, "mou-ol:lunzhan", "duel-" + count, cards, [target], g.Revision, P()!.PromptId));
        void Reward(string branch) { Reach(() => P() is { PlayerSeat: 0 } && R() is { Stage: MouWenChouOlStage.Reward }); Answer(c => c.Parameters.GetValueOrDefault("branch") == branch); Play(); }
        Accept(new StartGameCommand()); Play();
        var initial = g.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToArray();
        Need(initial.Length == 4, "Two fixed initial entities plus the real normal Draw2 establish a four-card hand.");
        Use(0, 2, []); Reach(() => P() is { PlayerSeat: 0 } && R() is { Stage: MouWenChouOlStage.Reward });
        var first = E<MouWenChouLunzhanIssuedEvent>().Single().Origin;
        Need(first.MaterialCount == 0 && E<MouWenChouUseDamageEvent>() is [{ SourceSeat: 0, TargetSeat: 2, Amount: 1 }] && E<MouWenChouJuejueQualifiedEvent>().Length == 0 && E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == first.UseId && e.CardId == 0 && e.CardKind == CardKind.Duel) == 1,
            "The zero-material option is a genuine native Duel and does not vacuously consume the first all-Hand condition.");
        var view = SnapshotJson.Serialize(g.CreateSnapshot(0));
        g = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(view == SnapshotJson.Serialize(g.CreateSnapshot(0)) && E<MouWenChouLunzhanIssuedEvent>().Length == 1 && R() is { Stage: MouWenChouOlStage.Reward },
            "One representative pending reward Cold preserves the original native Duel and optional unpaid draw without issuing another Use.");
        Answer(c => c.Parameters.GetValueOrDefault("branch") == "decline"); Play();
        Need(E<MouWenChouLunzhanReturnedEvent>().Count(e => e.UseId == first.UseId) == 1 && E<MouWenChouRewardAcceptedEvent>().Length == 0 && g.GetHumanLegalActions().All(a => a.ProgramSkillId != "mou-ol:lunzhan" || a.ProgramActivationId != "duel-0"),
            "Declining the reward preserves the target but the zero material count remains spent for the actual turn.");
        Use(1, 2, [initial[0]]); Reward("draw");
        var second = E<MouWenChouLunzhanIssuedEvent>().Last().Origin;
        Need(E<MouWenChouRewardAcceptedEvent>() is [{ OwnerSeat: 0, TargetSeat: 2, Amount: 2 }] && E<MouWenChouDrawReturnedEvent>() is [{ Requested: 2, Actual: 2 }] && E<MouWenChouJuejueQualifiedEvent>().Length == 0 && E<MouWenChouLunzhanReturnedEvent>().Count(e => e.UseId == second.UseId) == 1 &&
            g.GetHumanLegalActions().Where(a => a.ProgramSkillId == "mou-ol:lunzhan").All(a => !a.TargetSeats.Contains(2)),
            "A second real designation of the same target freezes X2, really draws2, then bans that target for every Lunzhan material count.");
        var all = g.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToArray(); Need(all.Length == 5, "Native material payment1 then actual Draw2 leaves exactly five actual Hand entities.");
        Use(5, 3, all); Reward("decline");
        var last = E<MouWenChouLunzhanIssuedEvent>().Last().Origin;
        Need(E<MouWenChouJuejueQualifiedEvent>() is [{ Entry: { TargetSeat: 3, Amount: 1 } }] && E<MouWenChouAllHandPaidEvent>().Any(e => e.UseId == last.UseId && e.Count == 5) &&
            E<NativeLogicalDiscardInvocationRecordedEvent>().Where(e => e.ExecutorSeat == 3 && e.Reason.Value == "skill-program.mou-ol:juejue.discard").Sum(e => e.ActualCount) == 1 &&
            E<MouWenChouLunzhanIssuedEvent>().Select(e => e.Origin.MaterialCount).SequenceEqual([0, 1, 5]) && E<MouWenChouLunzhanReturnedEvent>().Length == 3 &&
            E<CardUseFinishedEvent>().Count(e => e.ResolutionId == last.UseId) == 1 && g.CreateSnapshot(0).Players[0].HandCount == 0 && all.All(id => g.CardMovements.Any(m => m.CardId == id && m.Reason == CardMoveReasons.Use && m.To == CardLocation.Processing) && g.CardMovements.Any(m => m.CardId == id && m.Reason == CardMoveReasons.UseFinished && m.To == CardLocation.DiscardPile)),
            "The first qualifying nonempty all-Hand native Duel retains all five physical materials, forces the real recipient to choose one non-recast discard, then completes and returns once.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-wen-chou-ol", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var kind in new[] { CardKind.Dodge, CardKind.Duel }) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mwc-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var definitions = new[] { new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } } };
            var labels = new Dictionary<string, object> { [Quiet] = new { name = "安静同行", description = "跳过真实出牌阶段" } };
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            foreach (var peer in new[] { Ally, EnemyA, EnemyB }) b.AddGeneral(new(peer, "真实同行", "supporter", Quiet, peer == Ally ? "qun" : "wei", 8));
            b.AddDeck(new(Deck, "固定小牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 24).Select(_ => new ContentDeckPhysicalCard("fixture:mwc-card-Dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋文丑原生主流程", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
