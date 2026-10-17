using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class WuGuanYuYixianChecks
{
    private const string YxSkill = "wumiao:yixian", YxOwner = "fixture:wyx-owner", YxMode = "identity:classic-wu-yixian";
    private const string YxDriver = "fixture:wyx-driver", YxQuiet = "fixture:wyx-quiet", YxWatch = "fixture:wyx-watch", YxConditional = "fixture:wyx-conditional";
    private const string YxObtain = "skill-program.wu-guan-yu.yixian.obtain", YxDraw = "skill-program.wu-guan-yu.yixian.draw";

    public static void FieldEquipmentAtomicOriginalOwnersLionOxAndCompensationCold()
    {
        var s = YxScenario.Start("field"); s.Stock();
        var weapon = s.Place(CardKind.Crossbow, 0); var lion = s.Place(CardKind.SilverLion, 0); var ox = s.Place(CardKind.WoodenOx, 0);
        var peerWeapon = s.Place(CardKind.Crossbow, 1); var peerLion = s.Place(CardKind.SilverLion, 2);
        var grain = s.V().Hand.First().Id; s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain], [], s.G.Revision, s.P!.PromptId)); s.Play();
        s.Use("hurt", targets: [0]); s.Play(); s.Use("hurt", targets: [1]); s.Play(); s.Use("hurt", targets: [2]); s.Play();
        var beforeHp = s.V().Hp; var peerHp = s.V(2).Hp; var expected = new[] { weapon, lion, ox, peerWeapon, peerLion }.ToHashSet();
        s.Open(); s.Private(); s.Reject(); s.Cold(); var opening = s.P!; s.Branch("field"); s.RejectOld(opening);
        s.Reach(() => s.Hold("recovered")); var paid = s.E<YixianPaidEvent>().Single();
        var receipt = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == paid.FrameId).Yixian!;
        s.Need(receipt is { Stage: YixianStage.EquipmentChildren, Paid: true, Entities: IList { IsReadOnly: true }, Recipients: IList { IsReadOnly: true } } &&
            paid.EntityCount == 5 && paid.RecipientCount == 3 && receipt.Entities.Select(e => e.CardId).ToHashSet().SetEquals(expected) &&
            receipt.Recipients.SequenceEqual(new[] { new YixianRecipient(0, 3), new YixianRecipient(1, 1), new YixianRecipient(2, 1) }) &&
            !s.E<YixianRecipientDecisionEvent>().Any() && !s.E<YixianDrawReturnedEvent>().Any(),
            "The complete real all-seat equipment batch freezes the actual three-plus-one-plus-one owners before Lion/Ox children, including the issuer's own equipment.");
        s.Private(); s.Cold(); s.Continue(); s.Reach(() => s.Hold("cost"));
        s.Need(s.V().Hp == beforeHp + 1 && s.V(2).Hp == peerHp + 1 && s.E<SilverLionRemovedRecoveryEvent>().Count(e => e.Reason.Value == YxObtain) == 2 &&
            s.G.CardMovements.Count(m => m.CardId == grain && m.From == CardLocation.WoodenOxGrain(0) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1 &&
            s.E<YixianEntityObtainedEvent>().Count(e => e.FrameId == paid.FrameId && e.To == CardLocation.Hand(0)) == 5 &&
            Enumerable.Range(0, 4).All(i => s.V(i).Equipment.Count == 0) && !s.E<YixianRecipientDecisionEvent>().Any(),
            "Both real Silver Lion recoveries and the one real Ox spill finish before the first optional compensation menu, without counting grain as obtained equipment.");
        s.Private(); s.Cold(); s.Continue(); s.Reach(() => s.YixianChoice("benefit", 0)); s.Branch("benefit"); s.Reach(() => s.Hold("draw"));
        s.Need(s.E<YixianDrawReturnedEvent>().Single() is { RecipientSeat: 0, RequestedCount: 3, ActualCount: 3, ReplacementCount: 0 } &&
            !s.E<YixianRecoveryIssuedEvent>().Any() && s.V().Hp == beforeHp + 1,
            "Self-compensation uses one native Draw3 and waits for its real gain child before issuing the separate recovery.");
        s.Private(); s.Cold(); s.Continue(); s.Reach(() => s.YixianChoice("skip", 1)); s.Branch("skip");
        s.Reach(() => s.YixianChoice("benefit", 2)); s.Branch("benefit"); s.Reach(() => s.E<YixianCompletedEvent>().Any()); s.Play();
        s.Need(s.E<YixianRecipientDecisionEvent>().Select(e => (e.RecipientSeat, e.Accepted)).SequenceEqual(new[] { (0, true), (1, false), (2, true) }) &&
            s.E<YixianDrawReturnedEvent>().Select(e => (e.RecipientSeat, e.RequestedCount, e.ActualCount)).SequenceEqual(new[] { (0, 3, 3), (2, 1, 1) }) &&
            s.E<YixianRecoveryIssuedEvent>().Select(e => e.RecipientSeat).SequenceEqual(new[] { 0, 2 }) && s.V().Hp == beforeHp + 2 && s.V(2).Hp == peerHp + 2 &&
            s.E<YixianCompletedEvent>().Single() is { Paid: true, EntityCount: 5, AcceptedRecipients: 2 } &&
            expected.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From.Zone == CardZoneKind.Equipment && m.To == CardLocation.Hand(0) && m.Reason.Value == YxObtain) == 1) &&
            !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == YxSkill),
            "Each original owner can independently receive or decline its own X and one recovery, and one limited payment obtains every actual equipment entity exactly once."); s.Cold();

        s = YxScenario.Start("weapon"); s.Use("weapon"); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.ContainsKey("advanced-value")) == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("advanced-value") == "fixture:wyx-reserve");
        s.Answer(c => c.Parameters.GetValueOrDefault("advanced-value") == "finish"); s.Play();
        var generated = s.V().Equipment.Single(c => c.Kind == CardKind.GeneralWeapon).Id; s.Cold(); s.Open(); s.Branch("field"); s.Play();
        s.Need(s.E<YixianEntityObtainedEvent>().Single() is { PrintedKind: CardKind.GeneralWeapon, To: var outside } && outside == CardLocation.OutsideGame &&
            s.G.CardMovements.Count(m => m.CardId == generated && m.From == CardLocation.Equipment(0) && m.To == CardLocation.OutsideGame && m.Reason.Value == YxObtain) == 1 &&
            !s.E<YixianRecipientFrozenEvent>().Any() && !s.E<YixianDrawReturnedEvent>().Any() &&
            !s.V().Hand.Any(c => c.Id == generated) && !s.G.CardMovements.Any(m => m.CardId == generated && m.To == CardLocation.Processing),
            "A genuinely sampled GeneralWeapon follows native Equipment-to-Outside destruction and cannot become fake obtained Hand material or self-compensation."); s.Cold();
    }

    public static void DiscardEquipmentObtainLimitedAndEmptyBranchesCold()
    {
        var s = YxScenario.Start("discard"); s.Stock(); var equipment = s.V().Hand.Where(c => EquipmentCatalog.IsEquipment(c.Kind)).Take(3).Select(c => c.Id).ToArray();
        var slash = s.V().Hand.First(c => c.Kind == CardKind.Slash).Id; s.Use("discard", [.. equipment, slash]); s.Play();
        s.Open(); var original = s.P!; s.Private(); s.Reject(); s.Cold(); s.Branch("cancel"); s.Play();
        s.Need(s.E<YixianCompletedEvent>().Single() is { Paid: false } && !s.E<YixianPaidEvent>().Any() && s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == YxSkill),
            "Opening then cancelling the real branch picker does not consume the limited skill."); s.RejectOld(original);
        s.Open(); s.Cold(); s.Branch("discard"); s.Play();
        var paid = s.E<YixianPaidEvent>().Single(); var entities = s.E<YixianEntityObtainedEvent>().Where(e => e.FrameId == paid.FrameId).ToArray();
        s.Need(paid is { Branch: YixianBranch.DiscardPile, EntityCount: 3, RecipientCount: 0 } && entities.Select(e => e.CardId).ToHashSet().SetEquals(equipment) &&
            entities.All(e => e.From == CardLocation.DiscardPile && e.To == CardLocation.Hand(0)) &&
            equipment.All(id => s.V().Hand.Any(c => c.Id == id)) && s.G.CreateCardZoneDiagnostics().Any(e => e.CardId == slash && e.Location == CardLocation.DiscardPile) &&
            !s.E<YixianRecipientFrozenEvent>().Any() && !s.E<YixianDrawReturnedEvent>().Any() && !s.E<YixianRecoveryIssuedEvent>().Any() &&
            s.E<SkillUsageConsumedEvent>().Count(e => e.SkillOwnerSeat == 0 && e.SkillId == YxSkill && e.UsageId == "all-equipment" && e.Scope == SkillUsageScope.Game) == 1 &&
            !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == YxSkill),
            "The discard option really obtains all and only current discarded equipment, never invents historical owners or compensation, and consumes one reusable limited-usage key.");
        var state = s.State(); s.Need(!s.G.Submit(new UseProgramSkillCommand(0, YxSkill, "all-equipment", [], [], s.G.Revision, s.P!.PromptId)).Accepted && state == s.State(), "A spent limited skill rejects a second real activation without mutating state."); s.Cold();
        s = YxScenario.Start("empty"); s.Open(); s.Branch("discard"); s.Play();
        s.Need(s.E<YixianPaidEvent>().Single() is { EntityCount: 0, RecipientCount: 0, BatchId: 0 } &&
            !s.E<YixianEntityObtainedEvent>().Any() && !s.E<YixianDrawReturnedEvent>().Any() && s.E<YixianCompletedEvent>().Single().Paid,
            "An explicitly selected empty public source is a legal zero obtain and spends the same limited choice without fabricating a movement or recipient."); s.Cold();
    }

    public static void PaidSourceLossDeathNativeChildrenAndInputPrivacyCold()
    {
        foreach (var scene in new[] { "source-loss", "owner-death", "accepted-death", "recipient-death" })
        {
            var s = YxScenario.Start(scene); s.Stock(); var seat = scene is "accepted-death" or "recipient-death" ? 1 : 0;
            if (scene is "accepted-death" or "recipient-death")
                s.Need(s.V(1).Skills!.Any(k => k.ContentId == YxWatch), "The actual foreign draw recipient, rather than a candidate general's numeric suffix, owns the native draw-death observer.");
            var card = s.Place(CardKind.Crossbow, seat); if (scene == "owner-death") s.Place(CardKind.Crossbow, 1);
            if (scene is "accepted-death" or "recipient-death") { s.Use("hurt", targets: [1]); s.Play(); }
            s.Open(); s.Branch("field");
            if (scene is "source-loss" or "owner-death")
            {
                s.Reach(() => s.Hold("cost")); var paid = s.E<YixianPaidEvent>().Single();
                s.Need(paid.EntityCount == (scene == "owner-death" ? 2 : 1) && !s.E<YixianRecipientDecisionEvent>().Any(), "The physical all-equipment payment is frozen before its actual source-loss/death child.");
                s.Private(); s.Reject(); var old = s.P!; s.Cold(); s.Continue(); s.RejectOld(old);
                if (scene == "owner-death")
                {
                    s.Reach(() => !s.V().IsAlive && s.E<YixianCompletedEvent>().Any());
                    s.Need(s.E<YixianRecipientDecisionEvent>() is [{ FrameId: var skippedFrame, Index: 0, RecipientSeat: 0, ObtainedCount: 1, Accepted: false }] &&
                        skippedFrame == paid.FrameId && !s.E<YixianDrawReturnedEvent>().Any() && !s.E<YixianRecoveryIssuedEvent>().Any() &&
                        s.E<YixianCompletedEvent>().Single(e => e.FrameId == paid.FrameId) is { Paid: true, EntityCount: 2, AcceptedRecipients: 0 } &&
                        !s.G.ResolutionStack.Any(f => f.Id == paid.FrameId) && s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "wu-yixian") != true,
                        "The dead issuer's own dead recipient is automatically skipped once; the paid subtree completes without a fresh menu, accepted benefit, draw or recovery for any living recipient.");
                }
                else
                {
                    s.Reach(() => s.YixianChoice("benefit", 0));
                    s.Need(!s.V().Skills!.Any(k => k.ContentId == YxSkill), "The exact conditional Yixian grant really vanished after the batch was paid.");
                    s.Private(); s.Cold(); s.Branch("benefit"); s.Reach(() => s.E<YixianCompletedEvent>().Any()); s.Play();
                    s.Need(s.E<YixianDrawReturnedEvent>().Single() is { RequestedCount: 1, ActualCount: 1 } && s.E<YixianRecoveryIssuedEvent>().Length == 1 &&
                        !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == YxSkill), "Paid source loss keeps the original benefit, while healing/regrant cannot refresh the consumed game allowance.");
                }
            }
            else
            {
                s.Reach(() => s.YixianChoice("benefit", 1)); s.Branch("benefit"); s.Reach(() => s.Hold("draw-death"));
                var paid = s.E<YixianPaidEvent>().Single(); var returned = s.E<YixianDrawReturnedEvent>().Single();
                s.Need(returned is { RecipientSeat: 1, RequestedCount: 1, ActualCount: 1 } && !s.E<YixianRecoveryIssuedEvent>().Any(), "The accepted foreign recipient has actually drawn once, but its native child returns before recovery is issued.");
                s.Private(); s.Reject(); s.Cold(); s.Continue(); s.Reach(() => s.E<YixianCompletedEvent>().Any());
                s.Need(s.E<YixianRecoveryIssuedEvent>().Single() is { RecipientSeat: 1 } recovery &&
                    recovery.RecipientAlive == (scene == "accepted-death") && s.E<YixianDrawReturnedEvent>().Length == 1 &&
                    (scene == "accepted-death" ? !s.V().IsAlive && s.E<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == 1 :
                        !s.V(1).IsAlive && !s.E<RecoveryAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1)),
                    "Death during the real accepted draw child retains the dead issuer's one living-recipient recovery, while a dead recipient gets no fresh recovery.");
            }
            var finished = s.E<YixianPaidEvent>().Single(); s.PlayOrDead();
            s.Need(s.E<YixianCompletedEvent>().Count(e => e.FrameId == finished.FrameId && e.Paid) == 1 &&
                s.E<ProgramSkillResolvedEvent>().Count(e => e.FrameId == finished.FrameId && e.SkillId == YxSkill && e.Completed) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Equipment(seat) && m.To == CardLocation.Hand(0) && m.Reason.Value == YxObtain) == 1,
                "Every paid source/issuer/recipient lifetime keeps its original single movement and exact completed activation across cold replay."); s.Cold();
        }
    }

    private sealed class YxScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == YxWatch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public bool YixianChoice(string branch, int seat) => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "wu-yixian" && c.Parameters.GetValueOrDefault("branch") == branch && c.Targets.SequenceEqual([seat])) == true;
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, YxDriver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Open() => Accept(new UseProgramSkillCommand(0, YxSkill, "all-equipment", [], [], G.Revision, P!.PromptId));
        public void Stock() { Use("stock"); Play(); Use("stock"); Play(); }
        public int Place(CardKind kind, int seat) { var id = V().Hand.First(c => c.Kind == kind).Id; Use("place", [id], [seat]); Play(); Need(V(seat).Equipment.Any(c => c.Id == id), "The native equipment placement really created the required public entity."); return id; }
        public void Branch(string branch) => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "wu-yixian" && c.Parameters.GetValueOrDefault("branch") == branch);
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "Only a published canonical human decision can be submitted."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void PlayOrDead() => Reach(() => !V().IsAlive || P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 170; i++) { if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected terminal before native boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End(); else if (p.SkillPrompt?.SkillId == YxWatch) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind == DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else throw new InvalidOperationException("Unexpected native boundary " + line + " " + Diagnostic()); }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        public void Private() => Need(P is { IsPrivate: true, PlayerSeat: 0 } && Enumerable.Range(1, 3).All(i => G.CreateSnapshot(i).PendingDecision is null), "Only the real human chooser sees these private choices.");
        public void Reject() { var p = P!; var before = State(); Need(!G.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && State() == before && !G.Submit(new AnswerPromptCommand(0, p.PromptId, new("unpublished-yixian"), G.Revision)).Accepted && State() == before, "Foreign and unpublished inputs cannot mutate the limited invoice or private cursor."); }
        public void RejectOld(PendingDecision p) { var before = State(); Need(!G.Submit(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && State() == before, "A retired branch prompt cannot pay or compensate twice."); }
        public string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves every immutable original equipment, source, once-only benefit and private native child."); }
        public void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message + " " + Diagnostic()); }
        public void Accept(GameCommand command) { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + " " + JsonSerializer.Serialize(result.Error)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " command=" + command.GetType().Name + " " + e.Message + " " + Diagnostic(), e); } }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P is not { } p ? null : new { p.Kind, p.PlayerSeat, p.PromptId, Choices = p.Choices.Take(4) }, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).GeneralId, V(i).Hp, V(i).IsAlive, V(i).HandCount, V(i).Equipment, HasDeathWatch = V(i).Skills!.Any(k => k.ContentId == YxWatch) }), Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step }), Paid = E<YixianPaidEvent>(), Decisions = E<YixianRecipientDecisionEvent>(), Draw = E<YixianDrawReturnedEvent>(), Recovery = E<YixianRecoveryIssuedEvent>(), Completed = E<YixianCompletedEvent>(), Hp = E<RecoveryAppliedEvent>().TakeLast(6), Resolved = E<ProgramSkillResolvedEvent>().TakeLast(6) });
        public static YxScenario Start(string scene)
        {
            ContentRegistry r; try { r = ContentRegistry.Build(new StandardContentPackage(), new YxFixture(scene)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture=" + e.Message, e); }
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene is "owner-death" or "accepted-death" ? Role.Renegade : Role.Lord, ModeId = YxMode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new YxScenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); s.Accept(new SelectGeneralCommand(0, YxOwner, g.Revision, s.P!.PromptId)); s.Play(); return s;
        }
    }

    private sealed class YxFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wu-yixian", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.wumiao-guan-yu." + suffix + ".json") ?? throw new InvalidOperationException("Missing real Wu Guan Yu resource"); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var actual = SkillProgramCatalog.Load(Read("rules"), Read("presentation")); var production = actual.Programs[YxSkill]; b.AddSkill(new(YxSkill, actual.Presentations[YxSkill].Name, actual.Presentations[YxSkill].Description) { Program = production, ProgramPresentation = actual.Presentations[YxSkill], Tags = SkillTag.Limited });
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonNode A(string id, string effects, int cards = 0, int targets = 0, string more = "") => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," + more + "\"effects\":[" + effects + "]}");
            static string Choice(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var acts = new JsonArray(A("stock", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":16}"),
                A("hurt", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":2}", targets: 1),
                A("discard", "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":4}", cards: 4, more: "\"sourceZones\":[\"hand\"],"),
                A("place", "{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"gear\"},{\"op\":\"placeSelectedEquipment\",\"target\":\"selectedTarget\",\"sourceBind\":\"gear\"}", 1, 1, "\"sourceZones\":[\"hand\"],\"cardCategories\":[\"equipment\"],"));
            if (scene == "weapon") acts.Add(A("weapon", "{\"op\":\"equipSampledGenerals\",\"target\":\"owner\",\"amount\":1}"));
            var defs = new JsonArray(new JsonObject { ["id"] = YxDriver, ["revision"] = 1, ["modifiers"] = N("[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":40,\"priority\":0}]"), ["activations"] = acts },
                N($$"""{"id":"{{YxQuiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""));
            var own = new List<string> { YxDriver, scene == "source-loss" ? YxConditional : YxSkill };
            if (scene == "source-loss") defs.Add(N($$"""{"id":"{{YxConditional}}","revision":1,"hpSkillGrants":[{"id":"live-yixian","skillId":"{{YxSkill}}","minimumHp":13}]}"""));
            var watch = new JsonArray();
            if (scene == "field")
            {
                watch.Add(N("{\"id\":\"actual-lion\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + Choice("recovered") + "]}"));
                watch.Add(N("{\"id\":\"actual-draw\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + YxDraw + "\"],\"optional\":false,\"effects\":[" + Choice("draw") + "]}"));
            }
            if (scene is "field" or "source-loss" or "owner-death") watch.Add(N("{\"id\":\"actual-equipment-cost\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"equipment\"],\"movementOccurrence\":\"perOwnerBatch\",\"movementReasons\":[\"" + YxObtain + "\"],\"optional\":false,\"effects\":[" + Choice("cost") + (scene is "source-loss" or "owner-death" ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "owner-death" ? 20 : 1) + "}" : "") + "]}"));
            if (scene is "accepted-death" or "recipient-death") watch.Add(N("{\"id\":\"actual-foreign-draw\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + YxDraw + "\"],\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"chooseOption\",\"target\":\"selectedTarget\",\"resultBind\":\"draw-death\",\"options\":[{\"id\":\"draw-death\"}]},{\"op\":\"loseHp\",\"target\":\"" + (scene == "accepted-death" ? "selectedTarget" : "owner") + "\",\"amount\":20}]}"));
            if (watch.Count > 0) { defs.Add(new JsonObject { ["id"] = YxWatch, ["revision"] = 1, ["triggers"] = watch }); if (scene is not ("accepted-death" or "recipient-death")) own.Add(YxWatch); }
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实义贤原生夹具", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })); foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "真实义贤子流程") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            const string pick = "fixture:wyx-pick"; b.AddSkill(new(pick, "本人", "固定真人候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) }); b.AddGeneral(new(YxOwner, "真实义贤拥有者", "supporter", pick, "shu", 12, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:wyx-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++) { var pp = "fixture:wyx-peer-pick-" + i; b.AddSkill(new(pp, "其他角色", "固定公开候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) }); b.AddGeneral(new(peers[i - 1], "真实对局角色", "supporter", pp, "wei", 12, scene is "accepted-death" or "recipient-death" ? [YxQuiet, YxWatch] : [YxQuiet])); }
            if (scene == "weapon") { const string reserve = "fixture:wyx-reserve-pick"; b.AddSkill(new(reserve, "未选备用", "真实魂牌来源") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -200000000d) }); b.AddGeneral(new("fixture:wyx-reserve", "真实备用武将", "supporter", reserve, "shu", 4, [])); }
            var kinds = new[] { CardKind.Crossbow, CardKind.SilverLion, CardKind.WoodenOx, CardKind.Slash };
            foreach (var kind in kinds) b.AddCard(new("fixture:wyx-card-" + kind, CardCatalog.Get(kind).DisplayName, CardCatalog.Get(kind).CategoryName, "原生装备与基本牌实体", kind));
            b.AddDeck(new("fixture:wyx-deck", "固定四类小实体堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 36).Select(i => new ContentDeckPhysicalCard("fixture:wyx-card-" + kinds[i % 4], Suit.Club, 7)).ToArray() });
            var roles = scene is "owner-death" or "accepted-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(YxMode, "真实武关羽义贤全流程", 4, 4, roles, "fixture:wyx-deck", GeneralCandidateCount: scene == "weapon" ? 5 : 4, GeneralPoolIds: scene == "weapon" ? [YxOwner, .. peers, "fixture:wyx-reserve"] : [YxOwner, .. peers]));
        }
    }
}
