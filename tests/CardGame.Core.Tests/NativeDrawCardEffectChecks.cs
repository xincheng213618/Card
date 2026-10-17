using System.IO;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class NativeDrawCardEffectChecks
{
    private const string Owner = "fixture:native-card-draw-owner", Mode = "identity:classic-native-card-draw",
        Driver = "fixture:native-card-draw-driver", Quiet = "fixture:native-card-draw-quiet",
        Before = "fixture:native-card-draw-before", Gain = "fixture:native-card-draw-gain",
        Obtained = "fixture:native-card-draw-obtained", Fengzi = "ol:fengzi", Zenrun = "ol:zenrun";
    private const string TakeReason = "skill-program.sun-hong.zenrun.obtain",
        CompensationReason = "skill-program.sun-hong.zenrun.compensation";
    private static string _scene = "";

    public static void NativeDrawTwoWaitsForAttemptAndActualGainChildrenCold()
    {
        foreach (var scene in new[] { "decline", "repeat" })
        {
            var (g, registry) = Start(scene);
            var handBefore = Hand(g, 0).HandCount;
            var sequenceBefore = g.CardMovements.Last().Sequence;
            var material = PlayDrawTwo(g);
            if (scene == "repeat")
            {
                Drive(g, () => Optional(g, Fengzi));
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                Drive(g, () => P(g)?.SkillPrompt?.SkillId == Fengzi &&
                    P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "cost"));
                Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "cost");
            }
            Drive(g, () => Optional(g, Before));
            var useId = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == material).Id;
            var attempts = new List<long>();
            var effectWindows = new List<long>();
            var allDrawn = new List<int>();
            var effects = scene == "repeat" ? 2 : 1;
            for (var round = 0; round < effects; round++)
            {
                Need(Optional(g, Before), "Each real effect must publish its own pre-draw choice.", g);
                var use = Use(g, useId);
                var attempt = g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(a => a.Context.ParentFrameId == useId);
                var receipt = use.NativeCardEffectDraw;
                Need(receipt is { Stage: NativeCardEffectDrawStage.Requesting, Outcome: null, RecipientSeat: 0 } &&
                    use.NativeDraw is { ReturnKind: NativeDrawAttemptReturnKind.CardUseDrawTwo,
                        RequestedCount: 2, RecipientIndex: 0, RecipientApplied: false, AttemptReturned: false } paid &&
                    paid.ActiveAttemptId == attempt.Id && attempt.Return.Kind == NativeDrawAttemptReturnKind.CardUseDrawTwo &&
                    attempt.Context.RecipientSeat == 0 && attempt.Context.RequestedCount == 2 &&
                    attempt.Context.Reason == CardMoveReasons.Draw && attempt.Context.ActualTurnNumber == g.State.TurnNumber &&
                    attempt.Context.ActualTurnOwnerSeat == 0 &&
                    !g.ResolutionStack.OfType<NullificationWindowFrame>().Any() &&
                    E<NullificationResolvedEvent>(g).Count(e => e.ResolutionId == useId &&
                        e.EffectCardId == material && !e.EffectNullified) == round + 1 &&
                    NewDraws(g, sequenceBefore, 0).Length == round * 2 &&
                    g.CardMovements.Last(m => m.CardId == material).To == CardLocation.Processing &&
                    !E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == useId),
                    "The popped native counterspell window yields to the same paid Use before DrawPile material or use completion.", g);
                Need(!attempts.Contains(attempt.Id) && !effectWindows.Contains(receipt!.NullificationFrameId),
                    "Extra effects have independent real windows/attempts rather than a reused Use-id sentinel.", g);
                attempts.Add(attempt.Id); effectWindows.Add(receipt!.NullificationFrameId);
                AssertPrivate(g, 0);
                g = Cold(g, registry);
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                Drive(g, () => Option(g, Gain, "gained"));
                var applied = Use(g, useId).NativeCardEffectDraw;
                Need(applied is { Stage: NativeCardEffectDrawStage.DrawingChildren, Outcome: { } } &&
                    applied.Outcome.ActualDrawIds.Count == 2 && applied.Outcome.AttemptFrameId == attempt.Id &&
                    applied.Outcome.Replacement is null &&
                    applied.Outcome.ActualDrawIds is System.Collections.IList { IsReadOnly: true } &&
                    Use(g, useId).NativeDraw is null &&
                    E<NativeDrawAttemptDecidedEvent>(g).Count(e => e.AttemptFrameId == attempt.Id &&
                        e.ParentFrameId == useId && !e.Replaced && e.RequestedCount == 2) == 1,
                    "Declining the pre-draw replacement binds only this effect's two real DrawPile entities.", g);
                var outcome = applied!.Outcome!;
                var actual = NewDraws(g, outcome.SequenceBefore, 0)
                    .Where(m => m.Sequence <= outcome.SequenceAfter).ToArray();
                Need(actual.Length == 2 && actual.Select(m => m.CardId).SequenceEqual(outcome.ActualDrawIds),
                    "The exact native outcome contains actual original material movement, not replacement gains.", g);
                allDrawn.AddRange(actual.Select(m => m.CardId));
                g = CompleteGainChildren(g, registry, useId, material, actual);
                if (round + 1 < effects)
                    Need(Optional(g, Before) && E<ExtraCardEffectStartedEvent>(g).Count(e => e.CardUseFrameId == useId) == 1 &&
                        !E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == useId),
                        "The original Use remains live while its paid extra effect starts the next distinct Draw2.", g);
            }
            Drive(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
            Need(E<NativeCardEffectDrawStartedEvent>(g).Count(e => e.UseFrameId == useId) == effects &&
                E<NativeCardEffectDrawAppliedEvent>(g).Where(e => e.UseFrameId == useId).All(e => e.ActualDrawCount == 2) &&
                E<NativeCardEffectDrawAppliedEvent>(g).Count(e => e.UseFrameId == useId) == effects &&
                E<NativeDrawAttemptStartedEvent>(g).Count(e => e.Context.ParentFrameId == useId) == effects &&
                E<NativeDrawAttemptDecidedEvent>(g).Count(e => e.ParentFrameId == useId && !e.Replaced) == effects &&
                allDrawn.Distinct().Count() == effects * 2 &&
                NewDraws(g, sequenceBefore, 0).Select(m => m.CardId).SequenceEqual(allDrawn) &&
                Hand(g, 0).HandCount == handBefore - 1 - (scene == "repeat" ? 1 : 0) + effects * 2 &&
                E<CardUseDeclaredEvent>(g).Count(e => e.ResolutionId == useId) == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == useId) == 1 &&
                g.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) &&
                    m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
                g.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Processing &&
                    m.To == CardLocation.DiscardPile) == 1 &&
                !g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == useId),
                "Every genuine effect draws once, while the original physical card is paid/finished only once after all children.", g);
            if (scene == "repeat")
                Need(E<ExtraCardEffectIssuedEvent>(g).Count(e => e.CardUseFrameId == useId) == 1 &&
                    g.CardMovements.Count(m => m.Reason.Value == "skill-program.extra-effect.cost") == 1,
                    "The repeated Draw2 comes from one real production Fengzi cost, not a second use or fabricated effect.", g);
            _ = Cold(g, registry);
        }
    }

    public static void ZenrunReplacesNativeDrawTwoWithoutForgingDrawMaterialsCold()
    {
        var (g, registry) = Start("zenrun");
        var handBefore = Hand(g, 0).HandCount;
        var victimBefore = Hand(g, 1).HandCount;
        var sequenceBefore = g.CardMovements.Last().Sequence;
        var material = PlayDrawTwo(g);
        Drive(g, () => Optional(g, Zenrun));
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == material);
        var useId = use.Id;
        var attempt = g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(a => a.Context.ParentFrameId == useId);
        Need(use.NativeDraw is { ReturnKind: NativeDrawAttemptReturnKind.CardUseDrawTwo, RequestedCount: 2 } &&
            use.NativeCardEffectDraw is { Stage: NativeCardEffectDrawStage.Requesting, RecipientSeat: 0 } &&
            NewDraws(g, sequenceBefore, 0).Length == 0 &&
            !E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == useId),
            "Production Zenrun is offered on the actual native DrawTwo CardUse, not a generic Draw substitute.", g);
        AssertPrivate(g, 0); g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Drive(g, () => ZenrunFrame(g)?.Zenrun?.Stage == ZenrunStage.Target);
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([1]));
        Need(ZenrunFrame(g)?.Zenrun is { Stage: ZenrunStage.Cards, Paid: false } selection && selection.Draw == attempt.Context &&
            P(g) is { IsPrivate: true, PlayerSeat: 0 } choice &&
            choice.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand))
                .All(c => c.Cards.Count == 0 && c.Parameters.ContainsKey("slot-index")) &&
            Hand(g, 1).Hand.Count == 0,
            "The replacement selects opaque original foreign Hand slots and never publishes the victim's faces.", g);
        AssertPrivate(g, 0); g = Cold(g, registry);
        for (var i = 0; i < 2; i++)
            Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "pick" &&
                c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
        var selected = ZenrunFrame(g)!.Zenrun!.Selected.Select(s => s.CardId).ToArray();
        Need(selected.Length == 2 && selected.Distinct().Count() == 2, "Two original private entities are selected exactly once.", g);
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "take");
        Drive(g, () => Option(g, Obtained, "obtained"));
        Need(ZenrunFrame(g)?.Zenrun is { Paid: true } &&
            g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(a => a.Id == attempt.Id).Replacement is { } replacement &&
            replacement.GainedIds.SequenceEqual(selected) &&
            g.CardMovements.Where(m => m.Sequence > sequenceBefore && m.Reason.Value == TakeReason)
                .Select(m => m.CardId).SequenceEqual(selected) &&
            NewDraws(g, sequenceBefore, 0).Length == 0 &&
            !E<NativeCardEffectDrawAppliedEvent>(g).Any(e => e.UseFrameId == useId) &&
            g.CardMovements.Last(m => m.CardId == material).To == CardLocation.Processing &&
            !E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == useId),
            "Real HE obtains and their actual gain child precede the original draw's typed decision/finish.", g);
        AssertPrivate(g, 0); g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "obtained");
        Drive(g, () => ZenrunFrame(g)?.Zenrun?.Stage == ZenrunStage.VictimChoice);
        var zenrunId = ZenrunFrame(g)!.Id;
        Need(g.CreateSnapshot(1).PendingDecision is { IsPrivate: true, PlayerSeat: 1 } victim &&
            victim.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "draw") &&
            victim.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "block") &&
            Hand(g, 0).HandCount == handBefore + 1 && Hand(g, 1).HandCount == victimBefore - 2 &&
            Use(g, useId).NativeDraw is { AttemptReturned: false } &&
            !E<NativeDrawAttemptDecidedEvent>(g).Any(e => e.AttemptFrameId == attempt.Id) &&
            !E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == useId),
            "The actual AI victim privately owns compensation while the original paid Use still awaits its exact return.", g);
        AssertPrivate(g, 1); g = Cold(g, registry);
        // This is the real victim's native AI command; no answer is sent for it.
        Accept(g, new AdvanceOneStepCommand(g.Revision));
        Drive(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        var compensation = g.CardMovements.Where(m => m.Sequence > sequenceBefore &&
            m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(1) && m.Reason.Value == CompensationReason).ToArray();
        Need(E<NativeCardEffectDrawAppliedEvent>(g).Single(e => e.UseFrameId == useId).ActualDrawCount == 0 &&
            E<NativeDrawAttemptDecidedEvent>(g).Count(e => e.AttemptFrameId == attempt.Id &&
                e.ParentFrameId == useId && e.Replaced && e.RequestedCount == 2) == 1 &&
            NewDraws(g, sequenceBefore, 0).Length == 0 && compensation.Length == 2 &&
            compensation.Select(m => m.CardId).Intersect(selected).Count() == 0 &&
            E<ZenrunPaidEvent>(g).Count(e => e.FrameId == zenrunId && e.AttemptFrameId == attempt.Id && e.Count == 2) == 1 &&
            E<ZenrunCompensationReturnedEvent>(g).Single(e => e.FrameId == zenrunId) is
                { ActualDrawCount: 2, ReplacementGainedCount: 0 } &&
            E<ZenrunCompletedEvent>(g).Count(e => e.FrameId == zenrunId && e.Paid && e.Count == 2) == 1 &&
            Hand(g, 0).HandCount == handBefore + 1 && Hand(g, 1).HandCount == victimBefore &&
            selected.All(id => Hand(g, 0).Hand.Any(c => c.Id == id)) &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == useId) == 1 &&
            g.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) &&
                m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
            g.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Processing &&
                m.To == CardLocation.DiscardPile) == 1 &&
            !g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == useId),
            "Replaced DrawTwo reports zero original draw materials; HE2 and victim compensation Draw2 remain distinct and settle once.", g);
        _ = Cold(g, registry);
    }

    private static GameEngine CompleteGainChildren(GameEngine g, ContentRegistry registry, long useId, int material,
        IReadOnlyList<CardMovementRecord> actual)
    {
        var seen = new List<int>(); var batches = new HashSet<long>();
        for (var i = 0; i <= actual.Count && Option(g, Gain, "gained"); i++)
        {
            var window = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
            Need(window.ResumeNativeCardEffectDrawFrameId == useId && window.Batch.ParentFrameId == useId &&
                batches.Add(window.Batch.Id) && window.Batch.Movements.Count > 0 &&
                window.Batch.Movements.All(m => actual.Any(a => a.Sequence == m.Sequence && a.CardId == m.CardId)) &&
                window.Batch.Movements.All(m => !seen.Contains(m.CardId)) &&
                g.CardMovements.Last(m => m.CardId == material).To == CardLocation.Processing &&
                !E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == useId),
                "Every exact native DrawOne gain batch returns to the unchanged CardUse before its physical trick cleanup.", g);
            seen.AddRange(window.Batch.Movements.Select(m => m.CardId));
            AssertPrivate(g, 0);
            Need(Enumerable.Range(0, 4).All(s => !g.CreateSnapshot(s).PublicRevealedCards.Any(c => actual.Any(m => m.CardId == c.Id))),
                "Drawn Hand faces remain private while the native material observer is paused.", g);
            g = Cold(g, registry);
            Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "gained");
            Drive(g, () => Option(g, Gain, "gained") || Optional(g, Before) || P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        }
        Need(seen.SequenceEqual(actual.Select(m => m.CardId)), "All original material gain children complete once in native order.", g);
        return g;
    }

    private static CardMovementRecord[] NewDraws(GameEngine g, long before, int seat) => g.CardMovements.Where(m =>
        m.Sequence > before && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(seat) && m.Reason == CardMoveReasons.Draw).ToArray();
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PlayerSnapshot Hand(GameEngine g, int seat) => g.CreateSnapshot(0).Players[seat];
    private static PendingDecision? P(GameEngine g) => g.PendingDecision;
    private static CardUseFrame Use(GameEngine g, long id) => g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == id);
    private static ProgramSkillFrame? ZenrunFrame(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.Zenrun is not null);
    private static bool Optional(GameEngine g, string skill) => P(g)?.SkillPrompt?.SkillId == skill &&
        P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Option(GameEngine g, string skill, string option) => P(g) is { PlayerSeat: 0 } p &&
        p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option);
    private static void AssertPrivate(GameEngine g, int seat) => Need(g.CreateSnapshot(seat).PendingDecision is { IsPrivate: true } p && p.PlayerSeat == seat &&
        Enumerable.Range(0, 4).Where(s => s != seat).All(s => g.CreateSnapshot(s).PendingDecision is null),
        "Only the real recipient/chooser receives the pending private native prompt.", g);
    private static int PlayDrawTwo(GameEngine g)
    {
        var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.DrawTwo && a.CardId is > 0);
        var id = action.CardId!.Value;
        Accept(g, new PlayCardCommand(0, id, action.TargetSeats, g.Revision, P(g)!.PromptId,
            action.PlayedCardKind, action.TargetCardId)
            { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        return id;
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        var p = P(g) ?? throw new InvalidOperationException("No native prompt. " + Diagnostic(g));
        Need(p.PlayerSeat == 0, "Only the human answers; actual AI victims advance themselves.", g);
        var selected = p.Choices.FirstOrDefault(predicate);
        Need(selected is not null, "The expected exact native choice is absent.", g);
        Accept(g, new AnswerPromptCommand(0, p.PromptId, selected!.Id, g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command)
    {
        try
        {
            var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + JsonSerializer.Serialize(result), g);
        }
        catch (Exception error) { throw new InvalidOperationException("Native card draw " + _scene + ": " + error.Message + " " + Diagnostic(g), error); }
    }
    private static void Drive(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 90; i++)
        {
            if (done()) return;
            Need(g.State.Winner == Winner.None, "The bounded native effect fixture ended before its exact boundary.", g);
            if (P(g) is { PlayerSeat: 0 } p)
            {
                if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                    Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind == DecisionKind.Nullification)
                    Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
                else throw new InvalidOperationException("Unexpected native card draw boundary. " + Diagnostic(g));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing bounded native card draw boundary. " + Diagnostic(g));
    }
    private static void Need(bool condition, string why, GameEngine g)
    { if (!condition) throw new InvalidOperationException(why + " " + Diagnostic(g)); }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new
    {
        _scene, g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase,
        Prompt = P(g) is { } p ? new { p.PlayerSeat, p.Kind, Skill = p.SkillPrompt?.SkillId, p.Choices } : null,
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
        Attempts = E<NativeDrawAttemptStartedEvent>(g), Applied = E<NativeCardEffectDrawAppliedEvent>(g)
    });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands)
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "Cold replay preserves exact native effect/material/request/child history and all viewer privacy.", g);
        return restored;
    }
    private static (GameEngine, ContentRegistry) Start(string scene)
    {
        _scene = scene;
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Accept(g, new StartGameCommand());
        Drive(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        Drive(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        return (g, registry);
    }
    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-native-card-effect-draw", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var definitions = new List<Dictionary<string, object>>();
            var labels = new Dictionary<string, object>();
            void Add(string id, string property, object value, params string[] options)
            {
                definitions.Add(new() { ["id"] = id, ["revision"] = 1, [property] = value });
                labels[id] = new { name = id, description = "原生无中真实摸牌母帧", optionLabels = options.ToDictionary(s => s, s => s) };
            }
            object Choice(string id) => new { op = "chooseOption", target = "owner", resultBind = id, options = new[] { new { id } } };
            Add(Driver, "modifiers", new[] { new { id = "keep", query = "handLimit", operation = "add", value = 20, priority = 0 } });
            Add(Quiet, "triggers", new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false,
                effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } });
            var ownerSkills = new List<string>();
            if (scene == "zenrun")
            {
                RegisterProduction(b, "ol-sun-hong", Zenrun);
                ownerSkills.Add(Zenrun); ownerSkills.Add(Obtained);
                Add(Obtained, "triggers", new[] { new { id = "actual-obtain-child", window = "cardsGained", subject = "owner",
                    destinationZones = new[] { "hand" }, movementOccurrence = "perBatch", movementReasons = new[] { TakeReason },
                    optional = false, effects = new[] { Choice("obtained") } } }, "obtained");
            }
            else
            {
                ownerSkills.Add(Before); ownerSkills.Add(Gain);
                Add(Before, "triggers", new[] { new { id = "native-before", window = "nativeDrawStarting", subject = "owner",
                    condition = new { kind = "phaseIsPlay" }, optional = true, effects = new[] { Choice("before") } } }, "before");
                Add(Gain, "triggers", new[] { new { id = "actual-draw-child", window = "cardsGained", subject = "owner",
                    condition = new { kind = "phaseIsPlay" }, destinationZones = new[] { "hand" }, movementOccurrence = "perBatch",
                    movementReasons = new[] { CardMoveReasons.Draw.Value }, optional = false, effects = new[] { Choice("gained") } } }, "gained");
                if (scene == "repeat") { RegisterProduction(b, "ol-wang-rong", Fengzi); ownerSkills.Add(Fengzi); }
            }
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, id, "原生无中子窗") { Program = program, ProgramPresentation = catalog.Presentations[id],
                    SelectionWeights = id == Driver ? Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000d) : null });
            b.AddGeneral(new(Owner, "原生无中拥有者", "supporter", Driver, "wu", 8, ownerSkills));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:native-card-draw-peer-" + i).ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "原生无中同桌", "supporter", Quiet, "shu", 8));
            b.AddDeck(new("fixture:native-card-draw-deck", "固定原生无中实体", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 72).Select(_ => new ContentDeckPhysicalCard("standard:draw_two", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(Mode, "原生无中摸牌请求", 4, 4, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:native-card-draw-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
        private static void RegisterProduction(IContentRegistryBuilder b, string bundle, string skill)
        {
            string Read(string suffix)
            {
                using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream(
                    "CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json")
                    ?? throw new InvalidOperationException("Missing production resource " + bundle + "." + suffix);
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            var catalog = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            var p = catalog.Presentations[skill];
            b.AddSkill(new(skill, p.Name, p.Description) { Program = catalog.Programs[skill], ProgramPresentation = p });
        }
    }
}
