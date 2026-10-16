using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ActualPhaseEndChecks
{
    private const string Owner = "fixture:phase-end-owner", Watch = "fixture:phase-end-watch",
        Insert = "fixture:phase-end-insert", Skip = "fixture:phase-end-skip", Mode = "identity:actual-phase-end";

    public static void SixActualPhaseEndsKeepTypedColdReturnsAndSkipRestorationBoundaries()
    {
        foreach (var scenario in new[] { "plain", "insert", "skip" })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4,
                HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3 }, registry);
            Accept(game, new StartGameCommand());
            Drive(game, () => Prompt(game) is { Kind: DecisionKind.SelectGeneral });
            Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, Prompt(game)!.PromptId));
            if (scenario != "skip")
            {
                Drive(game, () => Prompt(game)?.SkillPrompt?.SkillId == Watch ||
                    Events<ActualTurnCursorReturnedEvent>(game).Any(e => e.ActualTurn == 1 && e.OwnerSeat == 0));
                Need(Prompt(game)?.SkillPrompt?.SkillId == Watch && game.ResolutionStack.OfType<ActualPhaseEndFrame>().Any(),
                    "The genuine own hand-limit discard must suspend its phase mother. " + Diagnostic(game));
                var mother = game.ResolutionStack.OfType<ActualPhaseEndFrame>().Single();
                var child = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
                Need(mother.Phase.Kind == ActualDiscardRecoveryPhaseKind.Discard && !mother.ItemsCaptured &&
                    child.ResumeActualPhaseEndFrameId == mother.Id && mother.InheritedMovementBatchIds.Contains(child.Batch.Id),
                    "A real hand-limit movement child suspends the exact Discard-end producer before issued items are captured.");
                Need(mother.Items is IList<ActualPhaseEndItem> { IsReadOnly: true } &&
                    mother.InheritedMovementBatchIds is IList<long> { IsReadOnly: true },
                    "The actual phase mother freezes both its pending batch identities and candidate collection.");
                Need(Enumerable.Range(1, 3).All(s => game.CreateSnapshot(s).PendingDecision is null),
                    "An unrelated viewer cannot inspect the genuine private movement child.");
                game = Cold(game, registry);
                Answer(game);
            }
            Drive(game, () => Events<ActualTurnCursorReturnedEvent>(game).Any(e => e.ActualTurn == 1 && e.OwnerSeat == 0));
            var ended = Events<ActualPhaseEndedEvent>(game).Where(e => e.Phase.TurnNumber == 1).ToArray();
            ActualDiscardRecoveryPhaseKind[] expected = scenario switch
            {
                "insert" => [ActualDiscardRecoveryPhaseKind.Play, ActualDiscardRecoveryPhaseKind.Preparation,
                    ActualDiscardRecoveryPhaseKind.Judgment, ActualDiscardRecoveryPhaseKind.Draw,
                    ActualDiscardRecoveryPhaseKind.Play, ActualDiscardRecoveryPhaseKind.Discard, ActualDiscardRecoveryPhaseKind.Ending],
                "skip" => [ActualDiscardRecoveryPhaseKind.Preparation, ActualDiscardRecoveryPhaseKind.Play, ActualDiscardRecoveryPhaseKind.Ending],
                _ => [ActualDiscardRecoveryPhaseKind.Preparation, ActualDiscardRecoveryPhaseKind.Judgment,
                    ActualDiscardRecoveryPhaseKind.Draw, ActualDiscardRecoveryPhaseKind.Play,
                    ActualDiscardRecoveryPhaseKind.Discard, ActualDiscardRecoveryPhaseKind.Ending]
            };
            Need(ended.Select(e => e.Phase.Kind).SequenceEqual(expected) && ended.Select(e => e.Phase.Token).Distinct().Count() == ended.Length &&
                ended.All(e => Events<ActualPhaseEndingEvent>(game).Count(s => s.BoundaryFrameId == e.BoundaryFrameId && s.Phase == e.Phase) == 1),
                $"Each genuine {scenario} phase closes exactly once; skipped stages never manufacture an actual phase-ending window.");
            if (scenario == "insert")
            {
                var preparation = Events<ActualDiscardRecoveryPhaseStartedEvent>(game).Single(e => e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Preparation && e.Phase.TurnNumber == 1).Phase;
                Need(Events<ActualDiscardRecoveryPhaseRestoredEvent>(game).Any(e => e.Phase == preparation) &&
                    ended.Count(e => e.Phase == preparation) == 1 && ended.Count(e => e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play) == 2,
                    "An inserted Play owns a new end while restoring the original Preparation token does not create a second phase.");
            }
            Need(!game.ResolutionStack.Any(f => f is ActualPhaseEndFrame), "Every typed mother returns to the native turn cursor without leftovers.");
            _ = Cold(game, registry);
        }
    }

    private static T[] Events<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? Prompt(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected actual-phase command."); }
    private static void Answer(GameEngine g) { var p = Prompt(g)!; Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "continue").Id, g.Revision)); }
    private static void Drive(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 140; i++)
        {
            if (done()) return;
            var p = Prompt(g);
            if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
            else if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId == Watch) Answer(g);
            else if (p is { PlayerSeat: 0 }) throw new InvalidOperationException($"Unexpected actual-phase prompt {p.Kind}/{p.SkillPrompt?.SkillId}.");
            else
            {
                if (g.CreateSnapshot(0).Winner != Winner.None)
                    throw new InvalidOperationException("Actual-phase fixture completed before its requested boundary. " + Diagnostic(g));
                Accept(g, new AdvanceOneStepCommand(g.Revision));
            }
        }
        throw new InvalidOperationException("The fixed actual-phase fixture exceeded its bounded native turn.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), g.CardMovements });
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new {
        Prompt = Prompt(g)?.Kind, Skill = Prompt(g)?.SkillPrompt?.SkillId,
        Frames = g.ResolutionStack.Select(f => new { f.Id, f.Kind }),
        Players = g.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.Hp, p.MaxHp, p.IsAlive }),
        Ends = Events<ActualPhaseEndedEvent>(g).TakeLast(8),
        Moves = g.CardMovements.TakeLast(8).Select(m => new { m.CardId, m.From, m.To, m.Reason }) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(restored), "Cold replay keeps exact actual phase keys, frozen mother/child returns and once-only native continuation."); return restored; }

    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-actual-phase-end", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:ya-dan");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-discard-child","window":"discardPileReceived","subject":"owner","discardOwnerScope":"own","movementReasons":["rule.hand-limit-discard"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"{{Insert}}","revision":1,"triggers":[{"id":"extra-play","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
              {"id":"{{Skip}}","revision":1,"triggers":[{"id":"skip-judgment-draw","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["judgment","draw"]}]},{"id":"skip-discard","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["discard"]}]}]}]}
            """;
            var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            { [Watch] = new { name = "实际弃牌子窗", description = "原生移动", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
              [Insert] = new { name = "插入出牌", description = "实际原生插入" }, [Skip] = new { name = "跳过阶段", description = "不制造实际结束" } } });
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "实际阶段结束") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:phase-end-idle", "固定角色", "无运行能力"));
            b.AddGeneral(new(Owner, "实际阶段主人", "supporter", "fixture:phase-end-idle", "shu", 3,
                scenario == "insert" ? [Watch, Insert] : scenario == "skip" ? [Watch, Skip] : [Watch]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:phase-end-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "固定其他角色", "supporter", "fixture:phase-end-idle", "wei", 8, []));
            b.AddDeck(new("fixture:phase-end-deck", "同质真实连弩", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "实际阶段母框架", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:phase-end-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, ..peers]));
        }
    }
}
