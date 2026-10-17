using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static partial class TaoQianChecks
{
    private const string WenOwner = "fixture:tq-wen-owner", WenMode = "identity:classic-tq-wen";
    private const string WenSkill = "ol:wenren", WenDriver = "fixture:tq-wen-driver", WenQuiet = "fixture:tq-wen-quiet";
    private const string WenWatch = "fixture:tq-wen-watch", WenConditional = "fixture:tq-wen-conditional", WenInsert = "fixture:tq-wen-insert";
    private const string WenReason = "skill-program.tao-qian.wenren.draw", WenZenrun = "ol:zenrun";
    private const string WenObtain = "skill-program.sun-hong.zenrun.obtain", WenCompensation = "skill-program.sun-hong.zenrun.compensation";

    public static void WenrenPerRecipientNativeDrawReplacementPaidRightsCold()
    {
        {
            var s = WenScenario.Start("ordered"); s.Use("stock-two"); s.Play();
            s.Use("give", [s.V().Hand[0].Id], [1]); s.Play();
            s.Use("give", [s.V().Hand[0].Id], [2]); s.Play();
            s.Need(s.V().HandCount == 0 && s.V(1).HandCount == 1 && s.V(2).HandCount == 1 && s.V(3).HandCount == 0,
                "The fresh-sampling counterexample begins with real owned entities and no fabricated hands.");
            s.Begin(); s.Private(0); s.Reject(); s.Branch("cancel"); s.Play();
            s.Need(s.E<WenrenCommittedEvent>().Length == 0, "Opening and cancelling an unpaid recipient draft does not spend the Play-phase quota.");
            var frame = s.Begin(); s.Toggle(1); s.Toggle(0); s.Toggle(2); s.Toggle(3);
            var old = s.P!; s.Cold(); s.Confirm(); s.Reach(() => s.Hold("draw"));
            var r = s.Root(frame).TaoQian!;
            s.Need(r is { Paid: true, Stage: TaoQianStage.DrawChildren, Cursor: 1, DrawAmount: 2 } && r.Targets.SequenceEqual([1, 0, 2, 3]) &&
                r.DrawOutcome is { RecipientSeat: 0, ActualDrawIds.Count: 2, Replacement: null } &&
                s.E<WenrenRecipientEvaluatedEvent>().Where(e => e.FrameId == frame).Select(e => (e.RecipientSeat, e.OwnerHand, e.RecipientHand, e.Amount)).SequenceEqual([(1, 0, 1, 0), (0, 0, 0, 2)]) &&
                s.E<WenrenDrawReturnedEvent>().Count(e => e.FrameId == frame) == 1 && !s.E<TaoQianCompletedEvent>().Any(e => e.FrameId == frame),
                "One zero-result recipient is skipped, then both self conditions issue one genuine Draw2 whose gain child precedes later recipients.");
            s.Frozen(r.Targets); s.Frozen(r.Selected); s.Frozen(r.Materials); s.Frozen(r.DrawOutcome!.ActualDrawIds);
            s.Private(0); s.RejectOld(old); s.Cold(); s.Continue(); s.Play();
            s.Need(s.E<WenrenRecipientEvaluatedEvent>().Where(e => e.FrameId == frame).Select(e => (e.RecipientSeat, e.OwnerHand, e.RecipientHand, e.Amount)).SequenceEqual([(1, 0, 1, 0), (0, 0, 0, 2), (2, 2, 1, 1), (3, 2, 0, 2)]) &&
                s.E<WenrenDrawReturnedEvent>().Where(e => e.FrameId == frame).Select(e => (e.RecipientSeat, e.RequestedCount, e.ActualCount, e.ReplacementCount)).SequenceEqual([(0, 2, 2, 0), (2, 1, 1, 0), (3, 2, 2, 0)]) &&
                s.Moves(WenReason).Count(m => m.To == CardLocation.Hand(0)) == 2 &&
                s.E<TaoQianCompletedEvent>().Count(e => e.FrameId == frame && e.Paid && e.Op == SkillProgramEffectOp.ResolveWenrenSequentialDraw) == 1,
                "Fresh recipient sampling preserves the issued order: the owner's earlier actual draw changes the later nonempty recipient to Draw1, and two conditions remain one whole Draw2.");
            s.Need(!s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == WenSkill), "A paid queue consumes the current real Play token once.");
            var state = s.State();
            s.Need(!s.G.Submit(new UseProgramSkillCommand(0, WenSkill, "distribute-draw", [], [], s.G.Revision, s.P!.PromptId)).Accepted && state == s.State(),
                "Submitting the same skill directly cannot bypass the already-spent actual-phase quota.");
            var first = s.E<WenrenCommittedEvent>().Single().Phase; s.Cold(); s.End(); s.Play();
            s.Need(s.G.State.TurnNumber == first.TurnNumber && s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == WenSkill),
                "The later normal Play in the same actual turn is a distinct real phase, following the completed inserted Play.");
            var next = s.Begin(); s.Toggle(0); s.Confirm(); s.Reach(() => s.Hold("draw")); s.Cold(); s.Continue(); s.Play();
            var phases = s.E<WenrenCommittedEvent>().Select(e => e.Phase).ToArray();
            s.Need(phases.Length == 2 && phases[0].Token != phases[1].Token && phases.All(p => p.TurnNumber == first.TurnNumber) &&
                s.E<WenrenDrawReturnedEvent>().Single(e => e.FrameId == next) is { RecipientSeat: 0, RequestedCount: 1, ActualCount: 1 },
                "A genuinely new Play token admits one new commitment and self Draw1 without refreshing the old token."); s.Cold();
        }
        {
            var s = WenScenario.Start("replacement"); s.Use("stock-two"); s.Play();
            var original = s.V().Hand[0].Id; s.Use("give", [original], [1]); s.Play();
            s.Need(s.V().HandCount == 1 && s.V(1).HandCount == 1,
                "The real nonempty owner requests Draw1, which the single foreign Hand entity can exactly replace.");
            var frame = s.Begin(); s.Toggle(0); s.Toggle(2); s.Confirm(); s.Reach(() => s.Optional(WenZenrun));
            var attempt = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(a => a.Context.ParentFrameId == frame);
            s.Need(attempt.Context is { RecipientSeat: 0, RequestedCount: 1 } && attempt.Context.Reason.Value == WenReason &&
                s.Root(frame).TaoQian is { Stage: TaoQianStage.Drawing, Cursor: 0, DrawOutcome: null } && !s.E<WenrenDrawReturnedEvent>().Any(e => e.FrameId == frame),
                "The first frozen recipient's native Draw1 can be replaced before any actual DrawPile gain or Wenren return.");
            s.Cold(); s.Activate(); s.Reach(() => s.ZenBranch("target"));
            s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([1]));
            s.Reach(() => s.ZenBranch("pick")); s.Private(0);
            s.Need(s.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "pick" && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).All(c => c.Cards.Count == 0) && s.G.CreateSnapshot(0).Players[1].Hand.Count == 0,
                "A real replacement exposes only the foreign Hand slot, without leaking the original entity to other viewers.");
            s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick" && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
            var zen = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Zenrun is not null);
            s.Need(zen.Zenrun!.Selected.Select(c => c.CardId).SequenceEqual([original]), "The actual private slot resolves to the previously gifted physical entity.");
            s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "take"); s.Reach(() => s.Hold("obtained"));
            s.Need(s.Root(frame).NativeDraw is { AttemptReturned: false } && !s.E<WenrenDrawReturnedEvent>().Any(e => e.FrameId == frame) &&
                s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(a => a.Id == attempt.Id).Replacement!.GainedIds.SequenceEqual([original]),
                "The replacement's actual Hand obtain and its own gain child still belong to the original pending draw attempt.");
            s.Private(0); s.Cold(); s.Continue();
            s.Reach(() => s.P is { PlayerSeat: 1 } p && p.SkillPrompt?.SkillId == WenZenrun && p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "draw"));
            s.Private(1); s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision)); s.Reach(() => s.Hold("compensated"));
            s.Need(s.P!.PlayerSeat == 1 && s.Root(frame).NativeDraw is { AttemptReturned: false } &&
                s.E<ZenrunCompensationReturnedEvent>().Single(e => e.FrameId == zen.Id) is { ActualDrawCount: 1, ReplacementGainedCount: 0 } &&
                !s.E<WenrenDrawReturnedEvent>().Any(e => e.FrameId == frame),
                "The original attempt waits for the victim's separate real compensation draw and all of its gain children.");
            s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision)); s.Reach(() => s.Hold("draw"));
            var returned = s.E<WenrenDrawReturnedEvent>().Single(e => e.FrameId == frame && e.Index == 0);
            s.Need(s.P!.PlayerSeat == 2 && returned is { RecipientSeat: 0, RequestedCount: 1, ActualCount: 0, ReplacementCount: 1 } && returned.AttemptId == attempt.Id &&
                s.Root(frame).TaoQian is { Stage: TaoQianStage.DrawChildren, Cursor: 1, DrawAmount: 2, DrawOutcome.ActualDrawIds.Count: 2 } &&
                s.V().Hand.Any(c => c.Id == original) && !s.E<TaoQianCompletedEvent>().Any(e => e.FrameId == frame),
                "Actual0/Replacement1 is retained exactly, then the next empty recipient's fresh native Draw2 begins after the replacement subtree completes.");
            s.Private(2); s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision)); s.Play();
            s.Need(s.E<WenrenDrawReturnedEvent>().Where(e => e.FrameId == frame).Select(e => (e.RecipientSeat, e.RequestedCount, e.ActualCount, e.ReplacementCount)).SequenceEqual([(0, 1, 0, 1), (2, 2, 2, 0)]) &&
                s.Moves(WenReason).All(m => m.To == CardLocation.Hand(2)) && s.Moves(WenReason).Length == 2 && s.Moves(WenObtain).Count(m => m.CardId == original) == 1 &&
                s.Moves(WenCompensation).Count(m => m.To == CardLocation.Hand(1)) == 1 &&
                s.E<TaoQianCompletedEvent>().Count(e => e.FrameId == frame && e.Paid) == 1,
                "Cold native replacement preserves one real obtain, one compensation, two later actual draw entities and one whole Wenren completion."); s.Cold();
        }
        foreach (var scene in new[] { "paid-loss", "paid-death" })
        {
            var s = WenScenario.Start(scene); var frame = s.Begin(); s.Toggle(0); s.Toggle(1); s.Toggle(2); s.Confirm(); s.Reach(() => s.Hold("paid"));
            var r = s.Root(frame).TaoQian!; var source = r.Source;
            s.Need(r is { Paid: true, Stage: TaoQianStage.DrawChildren, Cursor: 0, DrawOutcome.ActualDrawIds.Count: 2 } && r.Targets.SequenceEqual([0, 1, 2]) &&
                s.E<WenrenCommittedEvent>().Count(e => e.FrameId == frame) == 1 && s.E<WenrenDrawReturnedEvent>().Count(e => e.FrameId == frame) == 1,
                "Source loss and death occur in an actual first-recipient gain child after the queue, phase quota and first Draw2 are already paid.");
            s.Private(0); s.Cold(); s.Continue(); s.Reach(() => !s.G.ResolutionStack.Any(f => f.Id == frame));
            s.Need((scene == "paid-loss" ? s.V().IsAlive && !s.V().Skills!.Any(k => k.ContentId == WenSkill) : !s.V().IsAlive) &&
                s.E<WenrenDrawReturnedEvent>().Where(e => e.FrameId == frame).Select(e => (e.RecipientSeat, e.ActualCount)).SequenceEqual([(0, 2), (1, 2), (2, 2)]) &&
                s.E<WenrenCommittedEvent>().Count(e => e.FrameId == frame && e.Source == source) == 1 &&
                s.E<TaoQianCompletedEvent>().Count(e => e.FrameId == frame && e.Paid) == 1 && s.V(1).HandCount == 2 && s.V(2).HandCount == 2,
                "Paid source loss or actual issuer death drains its true child and completes the unchanged living queue without a new commitment or repeated draw.");
            s.Cold();
        }
    }

    private sealed class WenScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardMovementRecord[] Moves(string reason) => G.CardMovements.Where(m => m.Reason.Value == reason).ToArray();
        public ProgramSkillFrame Root(long id) => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == id);
        public bool Hold(string option) => P?.SkillPrompt?.SkillId == WenWatch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option);
        public bool Optional(string skill) => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public bool ZenBranch(string branch) => P?.SkillPrompt?.SkillId == WenZenrun && P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch);
        public void Branch(string branch) => Answer(c => c.Parameters.GetValueOrDefault("tao-qian") == branch);
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Use(string activation, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, WenDriver, activation, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public long Begin()
        {
            Accept(new UseProgramSkillCommand(0, WenSkill, "distribute-draw", [], [], G.Revision, P!.PromptId));
            Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("tao-qian") == "recipient-toggle") == true);
            return G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.TaoQian is not null).Id;
        }
        public void Toggle(int seat)
        {
            var selected = G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.TaoQian is not null).TaoQian!.Targets;
            var after = selected.Contains(seat) ? selected.Where(s => s != seat).ToArray() : selected.Append(seat).ToArray();
            Answer(c => c.Parameters.GetValueOrDefault("tao-qian") == "recipient-toggle" && c.Targets.SequenceEqual(after));
        }
        public void Confirm() => Branch("recipient-confirm");
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Private(int seat) => Need(P is { IsPrivate: true } p && p.PlayerSeat == seat && Enumerable.Range(0, 4).Where(i => i != seat).All(i => G.CreateSnapshot(i).PendingDecision is null), "Only the actual chooser sees the private native draft or gain-child input.");
        public void Frozen<T>(IReadOnlyList<T> values)
        {
            var rejected = false; try { ((IList)values).Add(default(T)); } catch (NotSupportedException) { rejected = true; }
            Need(rejected, "Every exposed owning selection and native outcome collection is immutable, including empty lists.");
        }
        public void Reject()
        {
            var p = P!; var state = State();
            Need(!G.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && State() == state &&
                !G.Submit(new AnswerPromptCommand(0, p.PromptId, new ChoiceId("unpublished-wenren"), G.Revision)).Accepted && State() == state,
                "A foreign chooser or unpublished input does not alter the private draft or spend its quota.");
        }
        public void RejectOld(PendingDecision old)
        { var state = State(); Need(!G.Submit(new AnswerPromptCommand(old.PlayerSeat, old.PromptId, old.Choices[0].Id, G.Revision)).Accepted && state == State(), "An old selection token cannot answer a real paid child."); }
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P; var c = p?.Choices.FirstOrDefault(predicate); Need(p is { PlayerSeat: 0 } && c is not null, "The human answers a currently published canonical input.");
            Accept(new AnswerPromptCommand(0, p!.PromptId, c!.Id, G.Revision));
        }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 190; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Native fixture completed before boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.SkillPrompt?.SkillId == WenWatch) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind == DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else throw new InvalidOperationException("Unexpected Wenren boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded Wenren boundary " + line + " " + Diagnostic());
        }
        public void Accept(GameCommand command)
        {
            try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + JsonSerializer.Serialize(result)); }
            catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " command=" + command.GetType().Name + " cause=" + e.Message + " " + Diagnostic(), e); }
        }
        public void Need(bool value, string text) { if (!value) throw new InvalidOperationException(text + " " + Diagnostic()); }
        public string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves private selections, ordered recipients, native replacement materials, paid quota and all typed child returns."); }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P,
            Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).IsAlive, V(i).HandCount }), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Evaluated = E<WenrenRecipientEvaluatedEvent>(), Draws = E<WenrenDrawReturnedEvent>(), Completed = E<TaoQianCompletedEvent>(), Moves = G.CardMovements.TakeLast(8) });
        public static WenScenario Start(string scene)
        {
            ContentRegistry r;
            try { r = ContentRegistry.Build(new StandardContentPackage(), new WenFixture(scene)); }
            catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture=" + e.Message, e); }
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "paid-death" ? Role.Renegade : Role.Lord,
                ModeId = WenMode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 7, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new WenScenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            s.Accept(new SelectGeneralCommand(0, WenOwner, g.Revision, s.P!.PromptId)); s.Play(); return s;
        }
    }

    private sealed class WenFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-tao-qian-wenren", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            WenProduction(b, "ol-tao-qian", WenSkill);
            if (scene == "replacement") WenProduction(b, "ol-sun-hong", WenZenrun);
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonNode A(string id, string effects, int cards = 0, int targets = 0) => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," + (cards > 0 ? "\"sourceZones\":[\"hand\"]," : "") + "\"effects\":[" + effects + "]}");
            static string Choose(string option) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"seen\",\"options\":[{\"id\":\"" + option + "\"}]}";
            var definitions = new JsonArray(new JsonObject { ["id"] = WenDriver, ["revision"] = 1,
                ["modifiers"] = N("[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":30,\"priority\":0}]"),
                ["activations"] = new JsonArray(A("stock-one", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}"), A("stock-two", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":2}"),
                    A("give", "{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}", 1, 1)) },
                N($$"""{"id":"{{WenQuiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""));
            if (scene == "ordered") definitions.Add(N($$"""{"id":"{{WenInsert}}","revision":1,"triggers":[{"id":"actual-inserted-play","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}"""));
            if (scene == "paid-loss") definitions.Add(N($$"""{"id":"{{WenConditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-source","skillId":"{{WenSkill}}","minimumHp":13}]}"""));
            var watch = new JsonArray();
            void Watch(string option, string reason, string after = "") => watch.Add(N("{\"id\":\"real-" + option + "\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + reason + "\"],\"optional\":false,\"effects\":[" + Choose(option) + (after.Length == 0 ? "" : "," + after) + "]}"));
            if (scene.StartsWith("paid-", StringComparison.Ordinal)) Watch("paid", WenReason, "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "paid-loss" ? 1 : 20) + "}");
            else Watch("draw", WenReason);
            if (scene == "replacement") { Watch("obtained", WenObtain); Watch("compensated", WenCompensation); }
            definitions.Add(new JsonObject { ["id"] = WenWatch, ["revision"] = 1, ["triggers"] = watch });
            var labels = definitions.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实原生逐人温仁收益与typed子流程",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(a => a!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var c = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, id, "真实温仁行为夹具") { Program = p, ProgramPresentation = c.Presentations[id] });
            const string Pick = "fixture:tq-wen-pick";
            b.AddSkill(new(Pick, "本人", "固定真人候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            var own = new List<string> { WenDriver, WenWatch, scene == "paid-loss" ? WenConditional : WenSkill };
            if (scene == "ordered") own.Add(WenInsert); if (scene == "replacement") own.Add(WenZenrun);
            b.AddGeneral(new(WenOwner, "真实温仁拥有者", "supporter", Pick, "qun", 12, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:tq-wen-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++)
            {
                var pick = "fixture:tq-wen-peer-pick-" + i;
                b.AddSkill(new(pick, "其他角色", "固定公开候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) });
                b.AddGeneral(new(peers[i - 1], "真实温仁收件者", "supporter", pick, "shu", 12, scene == "replacement" ? [WenQuiet, WenWatch] : [WenQuiet]));
            }
            b.AddDeck(new("fixture:tq-wen-deck", "固定小真实实体牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Heart, i % 13 + 1)).ToArray() });
            var roles = scene == "paid-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(WenMode, "真实温仁四人小模式", 4, 4, roles, "fixture:tq-wen-deck", GeneralCandidateCount: 4, GeneralPoolIds: [WenOwner, .. peers]));
        }
        private static void WenProduction(IContentRegistryBuilder b, string bundle, params string[] ids)
        {
            string Read(string suffix)
            {
                using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing production resource " + bundle + "." + suffix);
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            var c = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var id in ids) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
        }
    }
}
