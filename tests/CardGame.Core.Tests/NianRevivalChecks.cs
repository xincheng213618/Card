using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class NianRevivalChecks
{
    private const string Mode = "fixture:nian-revival-mode", Owner = "fixture:nian-revival-owner";
    private const string Driver = "fixture:nian-revival-driver", Quiet = "fixture:nian-revival-quiet";
    private const string Observer = "fixture:nian-revival-observer";
    private const string Replace = "fixture:nian-revival-replace";
    private const string NativeWatch = "fixture:nian-revival-native-draw-watch";

    public static void SharedRevivalRestoresRealDeadAllyThreeTimes()
    {
        var (g, registry) = Start();
        Need(!g.GetHumanLegalActions().Any(a => a.ProgramSkillId == "nian:revive"),
            "A living or merely wounded ally is never a revival target.");
        var original = V(g, 1);
        Use(g, Driver, "flip", 1); Play(g);
        Use(g, Driver, "chain", 1); Play(g);
        for (var ordinal = 1; ordinal <= 3; ordinal++)
        {
            Use(g, Driver, "kill", 1); Play(g);
            Need(!V(g, 1).IsAlive && Facts<PlayerDiedEvent>(g).Count(e => e.VictimSeat == 1) == ordinal,
                "Revival requires the completed real death pipeline, rather than an unresolved dying character.");
            Cold(g, registry);
            var action = g.GetHumanLegalActions().Single(a => a.ProgramSkillId == "nian:revive");
            Need(action.TargetSeats.Contains(1) || action.SelectableTargetSeats.Contains(1),
                "The exceptional dead-ally domain is exposed by the actual legal skill action.");
            var revision = g.Revision;
            var rejected = g.Submit(new UseProgramSkillCommand(0, "nian:revive", "revive", [], [2],
                g.Revision, P(g)!.PromptId));
            Need(!rejected.Accepted && g.Revision == revision && Facts<NianRevivalPaidEvent>(g).Count() == ordinal - 1,
                "An invalid living target neither commits a command nor consumes the team resource.");
            var before = g.Events.Count;
            Use(g, "nian:revive", "revive", 1); Play(g);
            var revived = V(g, 1);
            Need(revived.IsAlive && revived.Hp == 2 && revived.MaxHp == original.MaxHp &&
                revived.GeneralId == original.GeneralId && !revived.IsFaceDown && !revived.IsChained &&
                revived.HandCount == 2 && revived.Equipment.Count == 0 && revived.Judgment.Count == 0 &&
                g.Events.Skip(before).All(e => e.Payload is not RecoveryAppliedEvent { TargetSeat: 1 }) &&
                Facts<NianRevivalPaidEvent>(g).Count() == ordinal &&
                Facts<NianAllyRevivedEvent>(g).Last() is { ActorSeat: 0, TargetSeat: 1, Hp: 2 } fact && fact.UsedRevives == ordinal &&
                Facts<NianRevivalCompletedEvent>(g).Last().DrawnCards == 2,
                "Each revival restores the same original character at two HP, pays one shared charge and draws two actual cards without a recovery event.");
            Cold(g, registry);
        }
        Use(g, Driver, "kill", 1); Play(g);
        Need(!g.GetHumanLegalActions().Any(a => a.ProgramSkillId == "nian:revive") &&
            Facts<NianRevivalPaidEvent>(g).Select(e => e.UsedRevives).SequenceEqual([1, 2, 3]),
            "The fourth death does not replenish the shared pool or offer another revival.");
        Cold(g, registry);
        Use(g, Driver, "kill", 2); Play(g);
        Use(g, Driver, "kill", 0);
        Until(g, () => g.CreateSnapshot(0).Status == EngineStatus.Completed);
        Need(g.CreateSnapshot(0).WinnerTeamId == "team:nian" &&
            g.CreateSnapshot(0).Players.Where(p => p.TeamId == "team:allies").All(p => !p.IsAlive) &&
            Facts<NianRevivalPaidEvent>(g).Count() == 3,
            "The complete allied team's death is a real defeat, even though a resurrection mechanism exists.");
        Cold(g, registry);

        var (low, lowRegistry) = Start(lowMaximum: true);
        Use(low, Driver, "kill", 1); Play(low);
        Use(low, "nian:revive", "revive", 1); Play(low);
        Need(V(low, 1).IsAlive && V(low, 1).Hp == 1 && V(low, 1).MaxHp == 1,
            "A retained one-HP maximum is respected rather than raised to pay for revival.");
        Cold(low, lowRegistry);
    }

    public static void AiRevivesHumanWithColdDrawAndPreservesLimitedUsage()
    {
        var (g, registry) = Start(limited: true);
        var initial = V(g, 0);
        Need(initial.Hp == 6, "The limited-skill fixture uses the activity's real five-tier maximum HP.");
        Use(g, Driver, "empty-own-hp", 0);
        Until(g, () => P(g) is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 } dying &&
            dying.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "program-trigger" &&
                c.Parameters.GetValueOrDefault("skill-id") == "nian:niepan" &&
                c.Parameters.GetValueOrDefault("binding-id") == "limited-dying-rescue"));
        Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("response") == "program-trigger" &&
            c.Parameters.GetValueOrDefault("skill-id") == "nian:niepan" &&
            c.Parameters.GetValueOrDefault("binding-id") == "limited-dying-rescue"); Play(g);
        Need(V(g, 0).Hp == 3 && V(g, 0).HandCount == initial.HandCount + 3 &&
            Facts<SkillUsageConsumedEvent>(g).Count(e => e.SkillId == "nian:niepan") == 1,
            "The local five-tier dying skill recovers three, draws three and consumes its original once-per-game ledger.");
        Use(g, Driver, "kill", 0);
        Until(g, () => !V(g, 0).IsAlive);
        Need(g.CreateSnapshot(0).Winner == Winner.None, "One dead human does not end a live allied team's game.");
        Cold(g, registry);
        Until(g, () => P(g) is { PlayerSeat: 0, SkillPrompt.SkillId: Observer } &&
            Facts<NianAllyRevivedEvent>(g).Any(e => e.TargetSeat == 0));
        Need(g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.NianRevival is
                { TargetSeat: 0, Stage: NianRevivalStage.DrawChildren }) &&
            Facts<NianRevivalPaidEvent>(g).Single().ActorSeat is 1 or 2 && V(g, 0).HandCount == 2,
            "A real AI ally spends the team charge while the dead human's restored draw opens a nested observable child.");
        Cold(g, registry);
        var revivalId = Facts<NianRevivalPaidEvent>(g).Single().FrameId;
        CompleteRevivalObservers(g, revivalId);
        Need(Facts<NianRevivalPaidEvent>(g).Count() == 1 && V(g, 0).Hp == 2 &&
            V(g, 0).MaxHp == initial.MaxHp && V(g, 0).GeneralId == initial.GeneralId &&
            V(g, 0).HandCount == 2 && Facts<NianRevivalCompletedEvent>(g).Single(e => e.FrameId == revivalId).DrawnCards == 2 &&
            g.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) &&
                m.Reason.Value == "activity.nian.revival-draw") == 2 &&
            Facts<SkillUsageConsumedEvent>(g).Count(e => e.SkillId == "nian:niepan") == 1,
            "Resuming the nested draw pays and draws once, preserving the original general and limited skill usage.");
        Play(g);
        Use(g, Driver, "kill", 0);
        Until(g, () => !V(g, 0).IsAlive);
        Need(Facts<SkillUsageConsumedEvent>(g).Count(e => e.SkillId == "nian:niepan") == 1,
            "The already consumed dying skill does not reappear after revival.");
        Cold(g, registry);
    }

    public static void TimeoutCancelsPaidRevivalDrawWithoutRefundOrReplay()
    {
        var (g, registry) = Start(observeRevival: true);
        Use(g, Driver, "kill", 0);
        Until(g, () => P(g) is { PlayerSeat: 0, SkillPrompt.SkillId: Observer } &&
            Facts<NianAllyRevivedEvent>(g).Any(e => e.TargetSeat == 0));
        var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.NianRevival is
            { TargetSeat: 0, Stage: NianRevivalStage.DrawChildren });
        var prompt = P(g)!;
        Need(Facts<NianRevivalPaidEvent>(g).Count() == 1 && V(g, 0).HandCount == 2,
            "The timeout fixture pauses after a real AI payment and two physical revival draws, before the movement child returns.");
        Cold(g, registry);
        Accept(g, new TickNianClockCommand(30 * 60 * 1000, g.Revision));
        Need(g.CreateSnapshot(0) is { Status: EngineStatus.Completed, WinnerTeamId: "team:nian", ProcessingCardCount: 0 } &&
            g.ResolutionStack.Count == 0 && P(g) is null && V(g, 0) is { IsAlive: true, Hp: 2, HandCount: 2 } &&
            Facts<NianRevivalPaidEvent>(g).Count() == 1 &&
            Facts<NianResolutionCancelledEvent>(g).Any(e => e.FrameId == parent.Id) &&
            !Facts<NianRevivalCompletedEvent>(g).Any(e => e.FrameId == parent.Id) &&
            g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value == "activity.nian.revival-draw") == 2,
            "Timeout retires the exact paid revival frame and nested prompt without refunding its team charge, repeating its draw, or undoing already restored life.");
        var revision = g.Revision;
        Need(!g.Submit(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices[0].Id, revision)).Accepted &&
            g.Revision == revision, "A retired revival child cannot answer or resume after the timeout verdict.");
        Cold(g, registry);
    }

    public static void RevivalWaitsForNativeDrawAttemptBeforePhysicalCards()
    {
        var (g, registry) = Start(nativeDraw: true);
        Use(g, Driver, "kill", 0);
        Until(g, () => P(g) is { PlayerSeat: 0, SkillPrompt.SkillId: NativeWatch });
        var producer = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.NianRevival is not null);
        var attempt = g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single();
        Need(producer.NianRevival is { Stage: NianRevivalStage.DrawAttempt } && producer.NativeDraw is
                { ReturnKind: NativeDrawAttemptReturnKind.ProgramOperation, Operation: SkillProgramEffectOp.ReviveNianAlly } &&
            attempt.Context.ParentFrameId == producer.Id && attempt.Context.RecipientSeat == 0 &&
            attempt.Context.RequestedCount == 2 && attempt.Context.Reason.Value == "activity.nian.revival-draw" &&
            Facts<NianRevivalPaidEvent>(g).Count() == 1 && V(g, 0) is { IsAlive: true, Hp: 2, HandCount: 0 } &&
            !g.CardMovements.Any(m => m.Reason.Value == "activity.nian.revival-draw"),
            "The paid revival restores life but waits at the actual native pre-draw window before moving or counting any draw material.");
        Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Until(g, () => Facts<NianRevivalCompletedEvent>(g).Any(e => e.FrameId == producer.Id));
        Need(Facts<NativeDrawAttemptDecidedEvent>(g).Count(e => e.AttemptFrameId == attempt.Id &&
                e.ParentFrameId == producer.Id && e.RequestedCount == 2 && !e.Replaced) == 1 &&
            Facts<NianRevivalPaidEvent>(g).Count() == 1 && V(g, 0).HandCount == 2 &&
            Facts<NianRevivalCompletedEvent>(g).Single(e => e.FrameId == producer.Id).DrawnCards == 2 &&
            g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value == "activity.nian.revival-draw") == 2,
            "Only the exact native draw return performs the two real draws and completes the already paid revival once.");
        Cold(g, registry);
    }

    public static void TimeoutDisposesOnlyActiveJudgmentEntity()
    {
        var (g, registry) = Start(judgment: true);
        var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Indulgence && a.TargetSeats.Contains(1));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision,
            P(g)!.PromptId, action.PlayedCardKind));
        Play(g);
        var delayed = V(g, 1).Judgment.Single().Id;
        Use(g, Driver, "judge", 1);
        Until(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.ProgramJudgmentReplacement });
        var frame = g.ResolutionStack.OfType<JudgmentFrame>().Single();
        var revealed = frame.CardId!.Value;
        Need(revealed != delayed && V(g, 1).Judgment.Select(c => c.Id).ToHashSet().SetEquals([delayed, revealed]),
            "The actual rejudge pause holds one temporary revealed entity beside an unrelated physical delayed trick.");
        Cold(g, registry);
        Accept(g, new TickNianClockCommand(30 * 60 * 1000, g.Revision));
        Need(g.CreateSnapshot(0) is { Status: EngineStatus.Completed, WinnerTeamId: "team:nian", ProcessingCardCount: 0 } &&
            g.ResolutionStack.Count == 0 && P(g) is null &&
            V(g, 1).Judgment.Select(c => c.Id).SequenceEqual([delayed]) &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == revealed).Location == CardLocation.DiscardPile &&
            Facts<NianResolutionCancelledEvent>(g).Any(e => e.FrameId == frame.Id) &&
            !Facts<JudgmentResolvedEvent>(g).Any(e => e.ResolutionId == frame.Id) &&
            g.CardMovements.Count(m => m.CardId == revealed && m.From == CardLocation.Judgment(1) &&
                m.To == CardLocation.DiscardPile && m.Reason.Value == "activity.nian.timeout-judgment") == 1,
            "Timeout discards exactly the cancelled native judgment's temporary reveal while preserving the real delayed trick and suppressing the unreturned judgment result.");
        Cold(g, registry);
    }

    private static (GameEngine, ContentRegistry) Start(bool limited = false, bool lowMaximum = false,
        bool observeRevival = false, bool judgment = false, bool nativeDraw = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(limited, lowMaximum, observeRevival, judgment, nativeDraw));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
            HumanRole = null, HumanTeamId = "team:allies", ModeId = Mode, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 24 }, registry);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g);
        return (g, registry);
    }

    private sealed class Fixture(bool limited, bool lowMaximum, bool observeRevival, bool judgment, bool nativeDraw) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("nian-revival-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            const string prefix = "CardGame.Content.Standard.SkillPrograms.nian-activity";
            using var rulesStream = assembly.GetManifestResourceStream(prefix + ".rules.json")!;
            using var presentationStream = assembly.GetManifestResourceStream(prefix + ".presentation.json")!;
            using var rulesReader = new StreamReader(rulesStream);
            using var presentationReader = new StreamReader(presentationStream);
            var actual = SkillProgramCatalog.Load(rulesReader.ReadToEnd(), presentationReader.ReadToEnd());
            foreach (var (id, program) in actual.Programs) b.AddSkill(new(id, actual.Presentations[id].Name,
                actual.Presentations[id].Description) { Program = program, ProgramPresentation = actual.Presentations[id],
                Tags = id == "nian:niepan" ? SkillTag.Limited : SkillTag.None });
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"hold-original-cards","query":"handLimit","operation":"add","value":30,"priority":0}],"activations":[
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                {"id":"empty-own-hp","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":6}]},
                {"id":"flip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"selectedTarget"}]},
                {"id":"judge","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startJudgment","target":"selectedTarget","judgmentReason":"skill.fixture.nian-timeout","resultBind":"judged","visibility":"public"},{"op":"moveBoundCards","target":"owner","sourceBind":"judged","destination":"discardPile"}]},
                {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]}]},
              {"id":"{{{Quiet}}}","revision":1,"modifiers":[{"id":"retain-cards","query":"handLimit","operation":"add","value":30,"priority":0}]},
              {"id":"{{{Observer}}}","revision":1,"triggers":[{"id":"observe-restored-draw","window":"cardsGained","subject":"owner","movementOccurrence":"perBatch","optional":true,"destinationZones":["hand"],"movementReasons":["activity.nian.revival-draw"],"effects":[{"op":"draw","target":"owner","amount":1}]}]},
              {"id":"{{{Replace}}}","revision":1,"triggers":[{"id":"replace","window":"judgmentReplacing","subject":"any","excludedReasons":[],"optional":true,"effects":[{"op":"replaceJudgment","target":"owner","zones":["hand"],"suits":["spade","club","heart","diamond","none"],"oldCardDestination":"discardPile"}]}]},
              {"id":"{{{NativeWatch}}}","revision":1,"triggers":[{"id":"before-revival-draw","window":"nativeDrawStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continued","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var fixture = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new[] { Driver, Quiet, Observer, Replace, NativeWatch }
                .ToDictionary(id => id, id => new { name = id, description = "Native Nian revival fixture.",
                    optionLabels = id == NativeWatch ? new Dictionary<string, string> { ["continue"] = "Continue" } : [] }) }));
            foreach (var (id, program) in fixture.Programs) b.AddSkill(new(id, id, "Native Nian revival fixture.")
            { Program = program, ProgramPresentation = fixture.Presentations[id] });
            List<string> extras = ["nian:revive"];
            if (limited) extras.Add("nian:niepan");
            if (limited || observeRevival) extras.Add(Observer);
            if (judgment) extras.Add(Replace);
            if (nativeDraw) extras.Add(NativeWatch);
            b.AddGeneral(new(Owner, "Original human general", "", Driver, "wei", 4, extras));
            string[] peers = ["fixture:nian-revival-peer-1", "fixture:nian-revival-peer-2"];
            foreach (var id in peers) b.AddGeneral(new(id, id, "", Quiet, "wei", lowMaximum ? 1 : 4,
                ["nian:revive"]));
            var forms = Enumerable.Range(0, 5).Select(i => $"fixture:nian-revival-boss-{i}").ToArray();
            foreach (var id in forms) b.AddGeneral(new(id, "Quiet real Nian enemy", "", Quiet, "qun", 12));
            b.AddDeck(new("fixture:nian-revival-deck", "Actual non-recovery cards", 2, 0,
                [new ContentDeckCardCount(judgment ? "standard:indulgence" : "standard:dodge", 120)]));
            b.AddMode(new(Mode, "Native shared Nian revival", 4, 4, new Dictionary<string, int>(),
                "fixture:nian-revival-deck", 3, [Owner, .. peers, .. forms], ContentModeKind.Team,
                new Dictionary<string, int> { ["team:allies"] = 3, ["team:nian"] = 1 })
            { BossActivity = new([Owner, .. peers], [new("nian", forms, 4)], limited ? 5 : 1, [],
                EnemyTeamId: "team:nian"), NianActivity = new(forms, 3) });
        }
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => g.CreateSnapshot(0).Players.Select(p => p.Seat)
        .Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static void Use(GameEngine g, string skill, string id, int target) => Accept(g,
        new UseProgramSkillCommand(0, skill, id, [], id == "empty-own-hp" ? [] : [target], g.Revision, P(g)!.PromptId));
    private static bool IsPlay(GameEngine g) => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static void Play(GameEngine g) => Until(g, () => IsPlay(g));
    private static void Until(GameEngine g, Func<bool> ready)
    { for (var i = 0; i < 240; i++) { if (ready()) return; Step(g); }
        throw new InvalidOperationException("Nian revival fixture failed to reach the expected boundary: " + Boundary(g)); }
    private static void CompleteRevivalObservers(GameEngine g, long revivalId)
    {
        for (var i = 0; i < 32; i++)
        {
            if (Facts<NianRevivalCompletedEvent>(g).Any(e => e.FrameId == revivalId)) return;
            if (P(g) is { PlayerSeat: 0, SkillPrompt.SkillId: Observer } &&
                g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == revivalId &&
                    f.NianRevival is { TargetSeat: 0, Stage: NianRevivalStage.DrawChildren }))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                continue;
            }
            Step(g);
        }
        throw new InvalidOperationException("The exact paid revival observers did not drain: " + Boundary(g));
    }
    private static void Step(GameEngine g)
    {
        if (P(g) is { PlayerSeat: 0 } p)
        {
            var choice = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("response") == "pass") ??
                p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("response") == "take-damage");
            if (choice is not null) { Accept(g, new AnswerPromptCommand(0, p.PromptId, choice.Id, g.Revision)); return; }
        }
        Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, g.Revision)); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(command); Need(result.Accepted, "Native Nian command rejected: " + JsonSerializer.Serialize(result)); }
    private static void Need(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static string Boundary(GameEngine g)
    {
        var view = g.CreateSnapshot(0); var prompt = P(g);
        return JsonSerializer.Serialize(new
        {
            view.Status, view.Winner, view.TurnNumber, view.CurrentSeat, view.Phase,
            Players = view.Players.Select(p => new { p.Seat, p.IsAlive, p.Hp, p.MaxHp, p.HandCount }),
            Prompt = prompt is null ? null : new { prompt.Kind, prompt.PlayerSeat, prompt.SourceSeat,
                prompt.TargetSeat, Skill = prompt.SkillPrompt?.SkillId,
                Choices = prompt.Choices.Take(12).Select(c => new { Id = c.Id.Value,
                    Response = c.Parameters.GetValueOrDefault("response"),
                    Action = c.Parameters.GetValueOrDefault("program-action"),
                    Skill = c.Parameters.GetValueOrDefault("skill-id") }) },
            Frames = g.ResolutionStack.Select(f => new { Type = f.GetType().Name, f.Id, f.Kind, f.Step,
                Skill = (f as ProgramSkillFrame)?.SkillId,
                Instruction = (f as ProgramSkillFrame)?.InstructionIndex,
                Revival = (f as ProgramSkillFrame)?.NianRevival?.Stage })
        });
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    { Views = g.CreateSnapshot(0).Players.Select(p => SnapshotJson.Serialize(g.CreateSnapshot(p.Seat))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        g.CardMovements, Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
    private static void Cold(GameEngine g, ContentRegistry registry)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "Cold replay preserves the original general, shared team payment, physical draw and native child return."); }
}
