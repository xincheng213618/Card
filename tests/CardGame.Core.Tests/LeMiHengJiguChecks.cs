using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static partial class LeMiHengChecks
{
    private const string JmSkill = "le:jigu", JmOwner = "fixture:lmj-owner", JmMode = "identity:classic-le-mi-heng-jigu";
    private const string JmDriver = "fixture:lmj-driver", JmQuiet = "fixture:lmj-quiet", JmWatch = "fixture:lmj-watch";
    private const string JmConditional = "fixture:lmj-conditional", JmZenrun = "ol:zenrun";
    private const string JmDraw = "skill-program.mi-heng.jigu.draw", JmObtain = "skill-program.sun-hong.zenrun.obtain", JmCompensation = "skill-program.sun-hong.zenrun.compensation";

    public static void JiguOriginalEntitiesEquipmentSlotsAndHandLimitCold()
    {
        var opening = JmScenario.Start("opening"); var originalHand = opening.V().Hand.Select(c => c.Id).ToHashSet();
        opening.Need(opening.P is { Kind: DecisionKind.OpeningHand } && !opening.E<JiguInitializedEvent>().Any(), "Actual opening customization precedes the GameStarting capture.");
        var replace = opening.P!.Choices.First(c => c.Parameters.GetValueOrDefault("action") == "replace"); var removed = replace.Cards.Single();
        opening.Private(0); opening.Cold(); opening.Answer(c => c.Id == replace.Id);
        var frozenOpening = opening.V().Hand.Select(c => c.Id).ToArray(); var incoming = frozenOpening.Single(id => !originalHand.Contains(id));
        var entry = opening.G.CardMovements.Last(m => m.CardId == incoming && m.To == CardLocation.Hand(0));
        opening.Need(!frozenOpening.Contains(removed) && !opening.E<JiguInitializedEvent>().Any(), "A real published replacement swaps one actual entity before any initial mark is issued.");
        opening.Cold(); opening.Answer(c => c.Parameters.GetValueOrDefault("action") == "confirm");
        opening.Reach(() => opening.P?.SkillPrompt?.SkillId == JmDriver && opening.P.Choices.Any(c => c.Cards.Count == 1));
        var captured = opening.G.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.JiguInitialEntities is not null).JiguInitialEntities!;
        opening.Need(captured.Where(e => e.OwnerSeat == 0).Select(e => e.CardId).ToHashSet().SetEquals(frozenOpening) && !opening.E<JiguInitializedEvent>().Any(), "The real GameStarting mother freezes the replaced opening Hand before its earlier movement child runs.");
        opening.Private(0); opening.Cold(); opening.Answer(c => c.Cards.SequenceEqual([incoming])); opening.Play();
        opening.Need(opening.E<JiguInitialEntityEvent>().Where(e => e.OwnerSeat == 0).Select(e => e.CardId).ToHashSet().SetEquals(frozenOpening) &&
            opening.E<JiguInitialEntityEvent>().Single(e => e.CardId == incoming).DealSequence == entry.Sequence && !opening.E<JiguInitialEntityEvent>().Any(e => e.CardId == removed) &&
            !opening.V().Hand.Any(c => c.Id == incoming) && opening.G.CardMovements.Any(m => m.CardId == incoming && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile),
            "Opening replacement entry is the exact mark provenance; an earlier real GameStarting discard cannot rewrite the already frozen initial set."); opening.Cold();
        var s = JmScenario.Start("marks"); var initial = s.E<JiguInitialEntityEvent>().Where(e => e.OwnerSeat == 0).ToArray();
        s.Need(initial.Length == 4 && initial.Select(e => e.CardId).ToHashSet().SetEquals(s.V().Hand.Select(c => c.Id)) &&
            initial.All(e => s.G.CardMovements.Count(m => m.Sequence == e.DealSequence && m.CardId == e.CardId && m.CardKind == e.PrintedKind && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.InitialDeal) == 1) &&
            s.V().Hand.All(c => c.SkillMarkLabel?.Contains("激鼓", StringComparison.Ordinal) == true),
            "Only four actual InitialDeal identities receive permanent marks, each backed by its original once-only deal invoice.");
        var ownerLimit = s.V().HandLimit; var peerLimit = s.V(1).HandLimit; var marked = initial[0].CardId;
        s.Use("stock-four"); s.Play();
        s.Need(s.V().Hand.Count(c => c.SkillMarkLabel?.Contains("激鼓", StringComparison.Ordinal) == true) == 4 && s.V().HandLimit == ownerLimit &&
            Enumerable.Range(1, 3).All(v => s.G.CreateSnapshot(v).Players[0].Hand.Count == 0),
            "Later real draws do not acquire marks or inflate the original exemption, and foreign views expose no private Hand faces.");
        s.Use("give", [marked], [1]); s.Play();
        s.Need(s.V().HandLimit == ownerLimit && s.V(1).HandLimit == peerLimit && !s.V().Hand.Any(c => c.Id == marked),
            "The ordinary public HandLimit stays unchanged; exact exemption is exercised by the real discard phase, not folded into that UI number.");
        var slot = s.V(1).Hand.ToList().FindIndex(c => c.Id == marked); s.Use("take", targets: [1]);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.ContainsKey("slot-index")) == true);
        s.Need(s.P!.Choices.Where(c => c.Parameters.ContainsKey("slot-index")).All(c => c.Cards.Count == 0), "A foreign Hand obtain uses real opaque slots instead of publishing marked card identities.");
        s.Private(0); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("slot-index") == slot.ToString(System.Globalization.CultureInfo.InvariantCulture)); s.Play();
        s.Need(s.V().Hand.Single(c => c.Id == marked).SkillMarkLabel?.Contains("激鼓", StringComparison.Ordinal) == true && s.V().HandLimit == ownerLimit &&
            s.E<JiguInitialEntityEvent>().Count(e => e.CardId == marked) == 1,
            "Reobtaining the original exact entity restores its mark without manufacturing another initial-deal membership."); s.Cold();
        var ownTurn = s.G.State.TurnNumber; s.End(); s.Play();
        s.Need(s.V().HandCount == 8 && !s.G.CardMovements.Any(m => m.TurnNumber == ownTurn && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile),
            "At the genuine discard phase four unmarked cards fit the ordinary limit while all four returned initial marked entities are exempt."); s.Cold();
        foreach (var scene in new[] { "recipient", "inherited" })
        {
            s = JmScenario.Start(scene); var given = s.V().Hand[0].Id; s.Use("give", [given], [1]); s.Play(); s.End(); s.Play();
            var peerTurn = s.E<TurnStartedEvent>().First(e => e.ActorSeat == 1).TurnNumber;
            var discarded = s.G.CardMovements.Count(m => m.TurnNumber == peerTurn && m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile);
            s.Need(scene == "inherited" ? discarded == 0 && s.V(1).HandCount == 5 && s.V(1).Hand.Single(c => c.Id == given).SkillMarkLabel?.Contains("激鼓", StringComparison.Ordinal) == true : discarded == 2 && s.V(1).HandCount == 3,
                "Permanent physical marks benefit another live Jigu holder, while the same real transfer grants no exemption to a holder without the skill."); s.Cold();
        }

        s = JmScenario.Start("equipment"); var equip = s.V().Hand[0].Id; s.Native(equip, []); s.Play();
        foreach (var id in s.V().Hand.Take(2).Select(c => c.Id).ToArray()) { s.Use("give", [id], [1]); s.Play(); }
        s.Use("self-hit"); s.Play(); var activation = s.E<JiguActivationPaidEvent>().Single();
        s.Need(activation is { MarkedHandCount: 1, EquipmentCount: 1, EmptySlotCount: 4, StartedSeatCount: 1, UseNumber: 1, DisabledForRound: true } &&
            s.E<JiguDrawReturnedEvent>().Single() is { ActualCount: 4, ReplacementCount: 0 } && s.V().Equipment.Single().Id == equip,
            "A genuine equipped initial entity is no longer a Hand mark; exact equality draws four actual unoccupied equipment slots once."); s.Cold();

        s = JmScenario.Start("abolished"); s.Use("abolish-armor"); s.Play(); s.Use("self-hit"); s.Play();
        s.Need(s.E<JiguActivationPaidEvent>().Single() is { MarkedHandCount: 0, EquipmentCount: 0, EmptySlotCount: 4 } &&
            s.E<JiguDrawReturnedEvent>().Single().ActualCount == 4,
            "A genuinely abolished armor slot contributes no empty capacity; the rule does not hard-code five minus equipped cards."); s.Cold();
    }

    public static void JiguHistoricalStartedSeatsRoundLatchAndExtraTurnsCold()
    {
        var s = JmScenario.Start("round"); s.Use("self-hit-two"); s.Play(); var first = s.E<JiguActivationPaidEvent>().Single();
        s.Need(first is { StartedSeatCount: 1, UseNumber: 1, DisabledForRound: true } && s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 2) == 1 &&
            s.E<JiguCompletedEvent>().Length == 1, "One real self Damage2 qualifies once, rather than once per damage point or once for each source/target role.");
        s.Use("extra-turn"); s.Play(); s.End(); s.Play();
        s.Need(s.G.State.TurnNumber > first.ActualTurn && s.E<RoundStartedEvent>().Last().RoundNumber == first.RoundNumber &&
            s.E<ActualDiscardRecoveryPhaseStartedEvent>().Where(e => e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Preparation && e.Phase.ActorSeat == e.Phase.ActualTurnOwnerSeat).Select(e => e.Phase.ActorSeat).Distinct().Count() == 1,
            "A real extra turn starts the same historical character again and preserves the old actual round.");
        s.Use("self-hit"); s.Play(); s.Need(s.E<JiguActivationPaidEvent>().Length == 1, "An extra turn cannot reopen the round's consumed, latched entitlement.");
        s.End(); s.Reach(() => s.Hold("turn-hit"));
        s.Need(s.G.State.CurrentSeat == 1 && s.E<ActualDiscardRecoveryPhaseStartedEvent>().Any(e => e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Preparation && e.Phase.ActorSeat == 1),
            "A second real character reaches its genuine Preparation before the foreign-turn self-damage child.");
        s.Private(0); s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<JiguActivationPaidEvent>().Length == 1 && s.E<RoundStartedEvent>().Last().RoundNumber > first.RoundNumber,
            "Later newly started characters do not unlock the old round's latch; only a real new RoundStarted boundary resets it.");
        s.Use("kill", targets: [1]); s.Play(); s.Use("self-hit"); s.Play(); var next = s.E<JiguActivationPaidEvent>().Last();
        s.Need(!s.V(1).IsAlive && next.RoundNumber > first.RoundNumber && next is { StartedSeatCount: 4, UseNumber: 1, DisabledForRound: false } &&
            s.E<JiguActivationPaidEvent>().Length == 2,
            "A dead character that already began a real turn remains in the four-seat historical threshold; the new round begins its own counter at one."); s.Cold();

        s = JmScenario.Start("skipped"); s.Use("self-hit"); s.Play(); var skipped = s.E<JiguActivationPaidEvent>().Single();
        s.Need(s.E<TurnStartedEvent>().Count(e => e.ActorSeat == 0) == 2 && s.E<ActualDiscardRecoveryPhaseStartedEvent>().Count(e => e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Preparation && e.Phase.ActorSeat == 0) == 1 &&
            skipped.StartedSeatCount == 4 && skipped.UseNumber == 1,
            "A first face-down scheduled skip supplies no Preparation start; repeated scheduling does not count an extra historical character."); s.Cold();
    }

    public static void JiguNativeDrawReplacementPaidRightsAndPrivacyCold()
    {
        var s = JmScenario.Start("zero"); s.Use("abolish-all"); s.Play(); s.Use("self-hit"); s.Play();
        s.Need(s.E<JiguActivationPaidEvent>().Single() is { EmptySlotCount: 0, UseNumber: 1, DisabledForRound: true } &&
            s.E<JiguDrawReturnedEvent>().Single() is { ActualCount: 0, ReplacementCount: 0 } && s.E<JiguCompletedEvent>().Length == 1,
            "A mathematically qualified Draw0 is a genuine paid activation and consumes the round entitlement exactly once."); s.Cold();
        foreach (var scene in new[] { "paid-loss", "paid-death" })
        {
            s = JmScenario.Start(scene); s.Use("self-hit"); s.Reach(() => s.Hold("paid"));
            var paid = s.E<JiguActivationPaidEvent>().Single(); var own = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Jigu is not null);
            s.Need(own.Id == paid.FrameId && own.Jigu is { Stage: JiguStage.DrawChildren } && s.E<JiguDrawReturnedEvent>().Single() is { ActualCount: 5, ReplacementCount: 0 } &&
                !s.E<JiguCompletedEvent>().Any(), "The exact paid native Draw5 has returned its identities while its real gain observer still owns the pending subtree.");
            s.Need(own.Jigu!.DrawOutcome!.ActualDrawIds is IList { IsReadOnly: true } && own.Jigu.DrawOutcome.ReplacementGainedIds is IList { IsReadOnly: true }, "Both native outcome collections remain frozen on the owning paid frame.");
            s.Private(0); s.Reject(); s.Cold(); s.Continue(); s.Reach(() => !s.G.ResolutionStack.Any(f => f.Id == paid.FrameId));
            s.Need((scene == "paid-loss" ? s.V().IsAlive && !s.V().Skills!.Any(k => k.ContentId == JmSkill) : !s.V().IsAlive) &&
                s.E<JiguActivationPaidEvent>().Length == 1 && s.E<JiguDrawReturnedEvent>().Length == 1 && s.E<JiguCompletedEvent>().Count(e => e.FrameId == paid.FrameId && e.DamageFrameId == paid.DamageFrameId) == 1 &&
                s.G.CardMovements.Count(m => m.Reason.Value == JmDraw && m.To == CardLocation.Hand(0)) == 5,
                "Source loss or actual owner death drains the paid native children and the original damage return without repeating qualification or drawing."); s.Cold();
        }
        s = JmScenario.Start("replacement"); s.Use("stock-six"); s.Play();
        foreach (var id in s.V().Hand.Take(5).Select(c => c.Id).ToArray()) { s.Use("give", [id], [1]); s.Play(); }
        var selected = s.V(1).Hand.Select(c => c.Id).ToArray(); s.Use("self-hit"); s.Reach(() => s.Optional(JmZenrun));
        var frame = s.E<JiguActivationPaidEvent>().Single().FrameId; s.Private(0); s.Cold(); s.Activate(); s.Reach(() => s.Zen("target"));
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([1]));
        for (var i = 0; i < 5; i++) s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "take"); s.Reach(() => s.Hold("obtained"));
        s.Need(!s.E<JiguDrawReturnedEvent>().Any() && selected.All(id => s.V().Hand.Any(c => c.Id == id)), "Five actually obtained foreign entities and their native movement child remain inside the still-unreturned draw replacement.");
        s.Private(0); s.Cold(); s.Continue(); s.Reach(() => s.Hold("compensated")); s.Private(1); s.Cold(); s.Advance(); s.Play();
        s.Need(s.E<JiguDrawReturnedEvent>().Single(e => e.FrameId == frame) is { ActualCount: 0, ReplacementCount: 5 } &&
            s.E<JiguCompletedEvent>().Count(e => e.FrameId == frame) == 1 && !s.G.CardMovements.Any(m => m.Reason.Value == JmDraw) &&
            selected.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0) && m.Reason.Value == JmObtain) == 1),
            "Native replacement keeps Actual0 separate from five obtained identities and waits for the victim's real compensation children before returning once."); s.Cold();
    }

    private sealed class JmScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == JmWatch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public bool Optional(string skill) => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public bool Zen(string branch) => P?.SkillPrompt?.SkillId == JmZenrun && P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch);
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, JmDriver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Native(int id, int[] targets) { var a = G.GetHumanLegalActions().First(a => a.CardId == id && a.ConversionSource is null && a.TargetSeats.SequenceEqual(targets)); Accept(new PlayCardCommand(0, id, targets, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)); }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Advance() => Accept(new AdvanceOneStepCommand(G.Revision));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Private(int seat) => Need(P is { IsPrivate: true } p && p.PlayerSeat == seat && Enumerable.Range(0, 4).Where(i => i != seat).All(i => G.CreateSnapshot(i).PendingDecision is null), "Only the actual native chooser sees the pending private child.");
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "The human chooses a genuine published choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 180; i++) { if (done()) return; Need(G.State.Winner == Winner.None, "Native fixture ended before boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Advance(); else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.SkillPrompt?.SkillId == JmWatch) Continue(); else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind == DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die"); else throw new InvalidOperationException("Unexpected native boundary " + line + " " + Diagnostic()); }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        public void Accept(GameCommand command) { try { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, "Rejected " + command.GetType().Name + ": " + JsonSerializer.Serialize(r)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " command=" + command.GetType().Name + " cause=" + e.Message + " " + Diagnostic(), e); } }
        public void Need(bool valid, string message) { if (!valid) throw new InvalidOperationException(message + " " + Diagnostic()); }
        public string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves exact initial entity membership, historical starts, round latch, native draw outcomes and pending children."); }
        public void Reject() { var p = P!; var before = State(); Need(!G.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && State() == before && !G.Submit(new AnswerPromptCommand(0, p.PromptId, new ChoiceId("unpublished-jigu-child"), G.Revision)).Accepted && State() == before, "Foreign and unpublished inputs cannot mutate the prepared damage/draw child."); }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).IsAlive, V(i).HandCount, V(i).HandLimit }), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Paid = E<JiguActivationPaidEvent>(), Returned = E<JiguDrawReturnedEvent>() });
        public static JmScenario Start(string scene)
        {
            ContentRegistry r; try { r = ContentRegistry.Build(new StandardContentPackage(), new JmFixture(scene)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture=" + e.Message, e); }
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "paid-death" ? Role.Renegade : Role.Lord, ModeId = JmMode, UseInteractiveSetup = true, ConfirmOpeningHand = scene == "opening", UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new JmScenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); s.Accept(new SelectGeneralCommand(0, JmOwner, g.Revision, s.P!.PromptId)); if (scene == "opening") s.Reach(() => s.P?.Kind == DecisionKind.OpeningHand); else s.Play(); return s;
        }
    }
    private static void LmProduction(IContentRegistryBuilder b, string bundle, params string[] ids)
    {
        string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing real production bundle " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var c = SkillProgramCatalog.Load(Read("rules"), Read("presentation")); foreach (var id in ids) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
    }
    private sealed class JmFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-le-mi-heng-jigu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            LmProduction(b, "le-mi-heng", JmSkill); if (scene == "replacement") LmProduction(b, "ol-sun-hong", JmZenrun);
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonNode A(string id, string effects, int cards = 0, int targets = 0) => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," + (cards == 0 ? "" : "\"sourceZones\":[\"hand\"],") + "\"effects\":[" + effects + "]}");
            static string Choice(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"seen\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var acts = new JsonArray(A("stock-four", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":4}"), A("stock-six", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":6}"),
                A("give", "{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}", 1, 1), A("take", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1),
                A("self-hit", "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}"), A("self-hit-two", "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":2}"), A("kill", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":20}", targets: 1),
                A("abolish-armor", "{\"op\":\"abolishEquipmentSlotGroup\",\"target\":\"owner\",\"equipmentSlots\":[\"armor\"]}"), A("abolish-all", "{\"op\":\"abolishEquipmentSlotGroup\",\"target\":\"owner\",\"equipmentSlots\":[\"weapon\",\"armor\",\"offensiveHorse\",\"defensiveHorse\",\"treasure\"]}"), A("extra-turn", "{\"op\":\"pendExtraTurn\",\"target\":\"owner\"}"));
            var defs = new JsonArray(new JsonObject { ["id"] = JmDriver, ["revision"] = 1, ["activations"] = acts },
                N($$"""{"id":"{{JmQuiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""));
            var own = new List<string> { JmDriver, scene == "paid-loss" ? JmConditional : JmSkill };
            if (scene == "paid-loss") defs.Add(N($$"""{"id":"{{JmConditional}}","revision":1,"hpSkillGrants":[{"id":"healthy","skillId":"{{JmSkill}}","minimumHp":12}]}"""));
            if (scene == "skipped") ((JsonObject)defs[0]!)["triggers"] = N("[{\"id\":\"real-first-skip\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"setFaceState\",\"target\":\"owner\",\"faceDown\":true}]}]");
            if (scene == "opening") ((JsonObject)defs[0]!)["triggers"] = N("[{\"id\":\"earlier-real-opening-move\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"priority\":100,\"optional\":false,\"effects\":[{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"owner\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}]}]");
            var watch = new JsonArray();
            void Gain(string id, string reason, string after = "") => watch.Add(N("{\"id\":\"actual-" + id + "\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + reason + "\"],\"optional\":false,\"effects\":[" + Choice(id) + (after.Length == 0 ? "" : "," + after) + "]}"));
            if (scene is "paid-loss" or "paid-death") Gain("paid", JmDraw, "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "paid-loss" ? 1 : 20) + "}");
            if (scene == "replacement") { own.Add(JmZenrun); Gain("obtained", JmObtain); Gain("compensated", JmCompensation); }
            if (scene == "round") watch.Add(N("{\"id\":\"other-real-start\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"turnOwnerScope\":\"otherLiving\",\"optional\":false,\"effects\":[" + Choice("turn-hit") + ",{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}]}"));
            if (watch.Count > 0) { defs.Add(new JsonObject { ["id"] = JmWatch, ["revision"] = 1, ["triggers"] = watch }); own.Add(JmWatch); }
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实激鼓实体与原生伤害摸牌夹具", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var c = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, id, "真实激鼓夹具") { Program = p, ProgramPresentation = c.Presentations[id] });
            const string pick = "fixture:lmj-pick"; b.AddSkill(new(pick, "本人", "固定真人候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            var baseHp = scene is "marks" or "recipient" or "inherited" ? 3 : 12; b.AddGeneral(new(JmOwner, "真实乐祢衡激鼓者", "supporter", pick, "qun", baseHp, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:lmj-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++) { var pp = "fixture:lmj-peer-pick-" + i; b.AddSkill(new(pp, "其他角色", "固定公开候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) }); b.AddGeneral(new(peers[i - 1], "真实对局角色", "supporter", pp, "shu", baseHp, scene == "replacement" ? [JmQuiet, JmWatch] : scene == "inherited" && i == 1 ? [JmQuiet, JmSkill] : [JmQuiet])); }
            var openingNames = new[] { "standard:slash", "standard:dodge", "standard:peach", "standard:duel", "standard:crossbow" };
            b.AddDeck(new("fixture:lmj-deck", "真实开局实体与装备", scene is "marks" or "equipment" or "recipient" or "inherited" or "opening" ? 4 : 0, 0, []) { PhysicalCards = scene == "opening" ? Enumerable.Range(0, 40).Select(i => new ContentDeckPhysicalCard(openingNames[i % 5], Suit.Club, 7)).ToArray() : Enumerable.Range(0, 36).Select(i => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, 7)).ToArray() });
            var roles = scene == "paid-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(JmMode, "真实激鼓边界", 4, 4, roles, "fixture:lmj-deck", GeneralCandidateCount: 4, GeneralPoolIds: [JmOwner, .. peers]));
        }
    }
}
