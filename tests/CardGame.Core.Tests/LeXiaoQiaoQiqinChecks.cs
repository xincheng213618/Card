using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class LeXiaoQiaoQiqinChecks
{
    private const string QSkill = "le:qiqin", QOwner = "fixture:lqq-owner", QDriver = "fixture:lqq-driver",
        QWatch = "fixture:lqq-watch", QQuiet = "fixture:lqq-quiet", QConditional = "fixture:lqq-conditional",
        QMode = "identity:classic-le-xiao-qiao-qiqin", QReason = "skill-program.le-xiao-qiao.qiqin.obtain";

    public static void QiqinOpeningPermanentGlobalEntitiesAndHandLimitCold()
    {
        var s = QScenario.Start("opening");
        var original = s.V().Hand.Select(c => c.Id).ToHashSet();
        s.Need(s.P is { Kind: DecisionKind.OpeningHand } && s.E<QiqinInitializedEvent>().Length == 0,
            "Actual opening replacement precedes Qiqin initialization.");
        s.Private(0); s.Cold(); var replace = s.P!.Choices.First(c => c.Parameters.GetValueOrDefault("action") == "replace");
        s.Answer(c => c.Id == replace.Id);
        var opening = s.V().Hand.Select(c => c.Id).ToHashSet(); var incoming = opening.Single(id => !original.Contains(id));
        var entry = s.G.CardMovements.Last(m => m.CardId == incoming && m.To == CardLocation.Hand(0));
        s.Answer(c => c.Parameters.GetValueOrDefault("action") == "confirm");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == QDriver && s.P.Choices.Any(c => c.Cards.Count == 1));
        var frozen = s.G.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.QiqinInitialEntities is not null).QiqinInitialEntities!;
        s.Need(frozen is IList { IsReadOnly: true } && frozen.Where(e => e.OwnerSeat == 0).Select(e => e.CardId).ToHashSet().SetEquals(opening) &&
            !s.E<QiqinInitializedEvent>().Any(), "The real GameStarting mother freezes post-replacement physical IDs before its earlier discard child.");
        s.Private(0); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([incoming])); s.Play();
        s.Need(s.E<QiqinInitialEntityMarkedEvent>().Where(e => e.OriginalOwnerSeat == 0).Select(e => e.CardId).ToHashSet().SetEquals(opening) &&
            s.E<QiqinInitialEntityMarkedEvent>().Single(e => e.CardId == incoming).EntrySequence == entry.Sequence &&
            !s.E<QiqinInitialEntityMarkedEvent>().Any(e => original.Except(opening).Contains(e.CardId)) &&
            s.E<QiqinCollectedEntityEvent>().Count(e => e.CardId == incoming) == 1,
            "An earlier child cannot replace the frozen initial invoice; the first real preparation then obtains that same discarded Qin once."); s.Cold();

        s = QScenario.Start("six"); s.Need(s.E<QiqinInitializedEvent>().Single().EntityCount == 6 && s.V().Hand.All(QMarked),
            "Initialization uses all six actual opening entities rather than assuming a four-card initial Hand.");
        var turn = s.G.State.TurnNumber; s.End(); s.Play();
        s.Need(s.V().HandCount == 6 && !s.G.CardMovements.Any(m => m.TurnNumber == turn && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile),
            "Six actual Qin remain exempt through a real discard phase despite the ordinary Lord Hand limit of four."); s.Cold();

        s = QScenario.Start("marks"); var mark = s.V().Hand[0].Id; var ordinaryLimit = s.V().HandLimit;
        s.Use("give", [mark], [1]); s.Play();
        s.Need(QMarked(s.V(1).Hand.Single(c => c.Id == mark)) && !s.V(1).Skills!.Any(k => k.ContentId == QSkill),
            "The physical Qin label survives transfer to a character without Qiqin.");
        s.Take(1, mark); s.Play(); s.Need(QMarked(s.V().Hand.Single(c => c.Id == mark)) &&
            s.E<QiqinInitialEntityMarkedEvent>().Count(e => e.CardId == mark) == 1 && s.V().HandLimit == ordinaryLimit,
            "Leaving and reentering Hand does not consume or duplicate permanent membership or change the ordinary limit.");
        s.Use("draw-four"); s.Play(); turn = s.G.State.TurnNumber; s.End(); s.Play();
        s.Need(s.V().HandCount == 8 && s.V().Hand.Count(QMarked) == 4 &&
            !s.G.CardMovements.Any(m => m.TurnNumber == turn && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile),
            "The four real unmarked draws fit the ordinary limit while the four marked entities are independently exempt."); s.Cold();
        foreach (var scene in new[] { "recipient", "inherited" })
        {
            s = QScenario.Start(scene); mark = s.V().Hand[0].Id; s.Use("give", [mark], [1]); s.Play(); s.End(); s.Play();
            var peerTurn = s.E<TurnStartedEvent>().First(e => e.ActorSeat == 1).TurnNumber;
            var discards = s.G.CardMovements.Count(m => m.TurnNumber == peerTurn && m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile);
            s.Need(scene == "inherited" ? discards == 0 && s.V(1).HandCount == 5 : discards == 2 && s.V(1).HandCount == 3,
                "Only a live Qiqin holder inherits exemption for global Qin; a recipient without Qiqin really discards to its unchanged limit."); s.Cold();
        }
    }

    public static void QiqinPreparationObtainsWholeSharedDiscardPileCold()
    {
        var s = QScenario.Start("shared"); var own = s.V().Hand.Select(c => c.Id).ToArray();
        var foreign = s.V(1).Hand[0].Id; s.Take(1, foreign); s.Play(); var collected = new[] { own[0], foreign };
        s.Use("discard-two", collected); s.Play(); s.Native(s.Actions().First(a => a.CardId == own[1] && a.Kind == LegalActionKind.Equip)); s.Play();
        s.Use("give", [own[2]], [2]); s.Play(); s.Use("draw-one"); s.Play();
        var ordinary = s.V().Hand.Single(c => !QMarked(c)).Id; s.Use("discard-one", [ordinary]); s.Play();
        var draws = s.E<NativeDrawInvocationRecordedEvent>().Length; s.End(); s.Reach(() => s.Hold("obtained") && s.P!.PlayerSeat == 1);
        var start = s.E<QiqinCollectStartedEvent>().Last(e => e.EntityCount > 0); var f = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == start.FrameId);
        s.Need(start.Source.OwnerSeat == 1 && f.Qiqin is { Stage: QiqinStage.ObtainChildren, Entities: IList { IsReadOnly: true } } receipt &&
            receipt.Entities.Select(e => e.CardId).ToHashSet().SetEquals(collected) &&
            s.E<QiqinInitialEntityMarkedEvent>().Where(e => collected.Contains(e.CardId)).Select(e => e.OriginalOwnerSeat).ToHashSet().SetEquals(new[] { 0, 1 }) &&
            s.V().Equipment.Single().Id == own[1] && s.V(2).Hand.Any(c => c.Id == own[2]) &&
            s.E<QiqinCollectPaidEvent>().Single(e => e.FrameId == f.Id).EntityCount == 2 &&
            !s.E<QiqinCollectCompletedEvent>().Any(e => e.FrameId == f.Id),
            "Another real Qiqin holder obtains the complete shared discard pool in one paid batch before its gain observer returns.");
        s.Private(1); s.Cold(); s.Advance(); s.Play();
        var actual = s.E<QiqinCollectedEntityEvent>().Where(e => e.FrameId == f.Id).ToArray();
        s.Need(actual.Length == 2 && actual.Select(e => e.CardId).ToHashSet().SetEquals(collected) && actual.All(e =>
                s.G.CardMovements.Count(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId && m.From == CardLocation.DiscardPile && m.To == CardLocation.Hand(1) && m.Reason.Value == QReason) == 1) &&
            s.E<QiqinCollectCompletedEvent>().Count(e => e.FrameId == f.Id && e.EntityCount == 2) == 1 &&
            s.G.CreateCardZoneDiagnostics().Any(c => c.CardId == ordinary && c.Location == CardLocation.DiscardPile) &&
            s.V().Equipment.Single().Id == own[1] &&
            s.E<NativeDrawInvocationRecordedEvent>().Length == draws,
            "Only marked Discard entities enter the collector Hand: ordinary discard, owned equipment and another Hand remain; an obtain never becomes a draw."); s.Cold();

        s = QScenario.Start("ox"); var ox = s.V().Hand[0].Id; s.Native(s.Actions().First(a => a.CardId == ox && a.Kind == LegalActionKind.Equip)); s.Play();
        var grain = s.V().Hand[0].Id; s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain], [], s.G.Revision, s.P!.PromptId)); s.Play();
        var discard = s.V().Hand[0].Id; s.Use("discard-one", [discard]); s.Play(); s.End(); s.Reach(() => s.Hold("obtained"));
        start = s.E<QiqinCollectStartedEvent>().Last(e => e.EntityCount > 0); s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<QiqinCollectedEntityEvent>().Where(e => e.FrameId == start.FrameId).Select(e => e.CardId).SequenceEqual([discard]) &&
            s.V().Equipment.Single().Id == ox && s.V().WoodenOxGrain!.Single().Id == grain &&
            Enumerable.Range(1, 3).All(v => (s.G.CreateSnapshot(v).Players[0].WoodenOxGrain?.Count ?? 0) == 0),
            "The preparation search is strictly Discard: actual marked equipment and private Ox grain are not acquired or disclosed."); s.Cold();
        s = QScenario.Start("empty"); s.Need(s.E<QiqinInitializedEvent>().Single().EntityCount == 0 &&
            s.E<QiqinCollectStartedEvent>().Single().EntityCount == 0 && s.E<QiqinCollectCompletedEvent>().Single().EntityCount == 0 &&
            !s.E<QiqinCollectPaidEvent>().Any() && !s.E<NativeDrawInvocationRecordedEvent>().Any(),
            "A genuine empty opening and empty preparation finish without inventing entities, movement or replacement draws."); s.Cold();
    }

    public static void QiqinPaidObtainChildrenSourceLossDeathAndReplayCold()
    {
        foreach (var scene in new[] { "paid-loss", "paid-damage", "paid-death" })
        {
            var s = QScenario.Start(scene); var ids = s.V().Hand.Take(2).Select(c => c.Id).ToArray();
            s.Use("discard-two", ids); s.Play(); s.End(); s.Reach(() => s.Hold("obtained"));
            var started = s.E<QiqinCollectStartedEvent>().Last(e => e.EntityCount > 0); var paid = s.E<QiqinCollectPaidEvent>().Single(e => e.FrameId == started.FrameId);
            s.Need(paid.EntityCount == 2 && paid.SequenceAfter - paid.SequenceBefore == 2 && !s.E<QiqinCollectCompletedEvent>().Any(e => e.FrameId == started.FrameId),
                "The entire real obtain is paid exactly once before the first private gain/health child and remains unfinished there.");
            s.Private(0); s.RejectPrompt(); var old = s.P!; s.Cold(); s.Continue(); s.RejectOld(old);
            if (scene == "paid-death")
            {
                s.Reach(() => s.P?.Kind == DecisionKind.RescueDying || !s.V().IsAlive);
                s.Cold(); s.Reach(() => !s.V().IsAlive && s.E<QiqinCollectCompletedEvent>().Any(e => e.FrameId == started.FrameId));
                s.Need(s.G.State.Winner == Winner.None && s.E<QiqinCollectCompletedEvent>().Count(e => e.FrameId == started.FrameId) == 1,
                    "Nonterminal collector death drains native dying/death children and closes its already-paid obtain without reacquiring cards discarded by that death.");
            }
            else
            {
                s.Reach(() => s.Hold("after-health")); s.Private(0); s.Cold();
                s.Need(scene != "paid-loss" || !s.V().Skills!.Any(k => k.ContentId == QSkill),
                    "A genuine HP conditional source loss occurs during the original obtain observer.");
                s.Continue(); s.Play(); s.Need(s.E<QiqinCollectCompletedEvent>().Count(e => e.FrameId == started.FrameId) == 1 &&
                    (scene != "paid-damage" || s.E<DamageAppliedEvent>().Count(e => e.TargetSeat == 0 && e.Amount == 1) == 1),
                    "The exact paid frame resumes through actual HP/damage children despite source loss, then completes once.");
            }
            s.Need(s.E<QiqinCollectPaidEvent>().Count(e => e.FrameId == started.FrameId) == 1 &&
                s.E<QiqinCollectedEntityEvent>().Where(e => e.FrameId == started.FrameId).Select(e => e.CardId).ToHashSet().SetEquals(ids) &&
                ids.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.DiscardPile && m.To == CardLocation.Hand(0) && m.Reason.Value == QReason) == 1),
                "Cold continuation never repeats any entity payment or replaces the original signed source and batch."); s.Cold();
        }
    }

    private static bool QMarked(CardSnapshot c) => c.SkillMarkLabel?.Split(" / ").Contains("琴", StringComparer.Ordinal) == true;
    private sealed class QScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public LegalAction[] Actions() => G.GetHumanLegalActions().Where(a => a.ConversionSource is null).ToArray();
        public bool Hold(string option) => P?.SkillPrompt?.SkillId == QWatch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option);
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, QDriver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Native(LegalAction a) => Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Advance() => Accept(new AdvanceOneStepCommand(G.Revision));
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "Only the actual human answers a published canonical choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Take(int seat, int id)
        { var slot = V(seat).Hand.ToList().FindIndex(c => c.Id == id); Use("take", targets: [seat]); Reach(() => P?.Choices.Any(c => c.Parameters.ContainsKey("slot-index")) == true); Private(0); Answer(c => c.Parameters.GetValueOrDefault("slot-index") == slot.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        public void Private(int seat) => Need(P?.PlayerSeat == seat && Enumerable.Range(0, 4).Where(i => i != seat).All(i => G.CreateSnapshot(i).PendingDecision is null), "Other viewers cannot see the chooser's private native prompt.");
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 180; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Native fixture ended before boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Advance(); else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.SkillPrompt?.SkillId == QWatch) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind == DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") == "let-die");
                else throw new InvalidOperationException("Unexpected boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        public void Accept(GameCommand command)
        { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + " " + result.Error?.Code + " " + result.Error?.Message); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " " + e.Message + " " + Diagnostic(), e); } }
        public void Need(bool value, string text) { if (!value) throw new InvalidOperationException(text + " " + Diagnostic()); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves permanent IDs, exact source/hash/whole gain invoice, original cursor and all private views."); }
        public void RejectPrompt()
        { var p = P!; var before = State(); Need(!G.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && before == State() && !G.Submit(new AnswerPromptCommand(0, p.PromptId, new("unpublished-qiqin"), G.Revision)).Accepted && before == State(), "Foreign and unpublished child answers cannot mutate a paid invoice."); }
        public void RejectOld(PendingDecision p)
        { var before = State(); Need(!G.Submit(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && before == State(), "A retired prompt cannot repeat an obtain or its health child."); }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner,
            Prompt = P is { } p ? new { p.Kind, p.PlayerSeat, p.PromptId, Choices = p.Choices.Take(3) } : null,
            Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).IsAlive, V(i).HandCount }),
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step }), Started = E<QiqinCollectStartedEvent>().TakeLast(4), Paid = E<QiqinCollectPaidEvent>(), Completed = E<QiqinCollectCompletedEvent>().TakeLast(4) });
        public static QScenario Start(string scene)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new QFixture(scene));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "paid-death" ? Role.Renegade : Role.Lord,
                ModeId = QMode, UseInteractiveSetup = true, ConfirmOpeningHand = scene == "opening", UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 12, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new QScenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            s.Accept(new SelectGeneralCommand(0, QOwner, g.Revision, s.P!.PromptId)); if (scene == "opening") s.Reach(() => s.P?.Kind == DecisionKind.OpeningHand); else s.Play(); return s;
        }
    }

    private sealed class QFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-le-xiao-qiao-qiqin", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.le-xiao-qiao." + suffix + ".json") ?? throw new InvalidOperationException("Missing real le-xiao-qiao bundle"); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var production = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            b.AddSkill(new(QSkill, production.Presentations[QSkill].Name, production.Presentations[QSkill].Description) { Program = production.Programs[QSkill], ProgramPresentation = production.Presentations[QSkill] });
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonObject A(string id, string effects, int cards = 0, int targets = 0) => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," + (cards == 0 ? "" : "\"sourceZones\":[\"hand\"],") + "\"effects\":[" + effects + "]}").AsObject();
            static string Choice(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"seen-" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var acts = new JsonArray(A("draw-one", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}"), A("draw-four", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":4}"),
                A("give", "{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}", 1, 1),
                A("take", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1),
                A("discard-one", "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":1}", 1), A("discard-two", "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":2}", 2));
            var driver = new JsonObject { ["id"] = QDriver, ["revision"] = 1, ["activations"] = acts };
            if (scene == "opening") driver["triggers"] = N("[{\"id\":\"earlier-real-opening-discard\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"priority\":100,\"optional\":false,\"effects\":[{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"owner\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}]}]");
            var defs = new JsonArray(driver, N("{\"id\":\"" + QQuiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            var paidScene = scene.StartsWith("paid-", StringComparison.Ordinal);
            var own = new List<string> { QDriver, paidScene && scene != "paid-damage" ? QConditional : QSkill };
            if (paidScene && scene != "paid-damage") defs.Add(N("{\"id\":\"" + QConditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-qiqin\",\"skillId\":\"le:qiqin\",\"minimumHp\":" + (scene == "paid-death" ? 12 : 13) + "}]}"));
            if (scene is "shared" or "ox" || paidScene)
            {
                own.Add(QWatch); var effects = Choice("obtained") + (paidScene ? "," + (scene == "paid-damage" ? "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}" : "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "paid-death" ? 20 : 1) + "}") + (scene == "paid-death" ? "" : "," + Choice("after-health")) : "");
                defs.Add(N("{\"id\":\"" + QWatch + "\",\"revision\":1,\"triggers\":[{\"id\":\"actual-whole-qin-obtain\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + QReason + "\"],\"optional\":false,\"effects\":[" + effects + "]}]}"));
            }
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实琴原生边界", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "真实琴原生夹具") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            const string pick = "fixture:lqq-owner-pick"; b.AddSkill(new(pick, "本人", "固定真人候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            var hp = paidScene ? 12 : 3; b.AddGeneral(new(QOwner, "真实绮琴本人", "supporter", pick, "wu", hp, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:lqq-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++)
            {
                var peerPick = "fixture:lqq-peer-pick-" + i; b.AddSkill(new(peerPick, "他人", "固定公开候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) });
                var skills = new List<string> { QQuiet }; if (i == 1 && (scene is "shared" or "inherited")) skills.Add(QSkill); if (i == 1 && scene == "shared") skills.Add(QWatch);
                b.AddGeneral(new(peers[i - 1], "真实其他角色", "supporter", peerPick, "shu", hp, skills));
            }
            const string cardId = "fixture:lqq-physical"; var kind = scene == "ox" ? CardKind.WoodenOx : CardKind.Crossbow; var card = CardCatalog.Get(kind); b.AddCard(new(cardId, card.DisplayName, card.CategoryName, "真实琴物理实体", kind));
            string[] openingKinds = ["standard:slash", "standard:dodge", "standard:peach", "standard:duel", "standard:crossbow"];
            b.AddDeck(new("fixture:lqq-deck", "真实初始琴牌堆", scene == "six" ? 6 : scene == "empty" ? 0 : 4, 0, [])
                { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard(scene == "opening" ? openingKinds[i % openingKinds.Length] : cardId, Suit.Heart, 7)).ToArray() });
            var roles = scene == "paid-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(QMode, "真实绮琴整批与子窗", 4, 4, roles, "fixture:lqq-deck", GeneralCandidateCount: 4, GeneralPoolIds: [QOwner, .. peers]));
        }
    }
}
