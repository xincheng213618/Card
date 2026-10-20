using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheYujinChecks
{
    private const string Skill = StandardShanheS5YujinPackage.ZhenjunId, Boss = StandardShanheS5YujinPackage.YujinId;
    private const string Legacy = "boundary:zhenjun-current", Hongde = "ol:hongde";
    private const string Owner = "fixture:s5-yj-owner", Ally = "fixture:s5-yj-ally", Enemy = "fixture:s5-yj-enemy", Deck = "fixture:s5-yj-deck";
    private const string PaidWatch = "fixture:s5-yj-paid", Gain = "fixture:s5-yj-gain", Mutation = "fixture:s5-yj-mutation";
    private const string BeforeDraw = "fixture:s5-yj-before-draw", Health = "fixture:s5-yj-health", Gate = "fixture:s5-yj-gate", Quiet = "fixture:s5-yj-quiet";
    private const string HongdeGate = "fixture:s5-yj-hongde-gate", HongdeEntry = "fixture:s5-yj-hongde-entry";
    private const string Keep = "fixture:s5-yj-keep-four";
    private const string UndefinedMode = "identity:classic-s5-yj-undefined", Hidden = "fixture:s5-yj-hidden";
    private const string TargetReason = "skill-program.preparation-discard.target.ResolvePrepDiscardOrEnding";
    private const string OwnerReason = "skill-program.preparation-discard.owner.ResolvePrepDiscardOrEnding";
    private const string DrawReason = "program.preparation-discard.immediate-draw", EndingReason = "program.preparation-discard.ending-draw";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture:s5-yj-production", Boss, 4, 0, 0, 0, 0, [], [], [], [new("classic:cao-cao")], [], new string('e', 64))));

    public static void FrozenPreparationDiscardsUsePrintedNonEquipmentAndExactOwnerPaymentCold()
    {
        VerifyLoaderBoundaries();
        var s = Scene.Start("pay"); s.AtOffer(); s.Privacy(); s.Cold(); s.Activate(); s.SelectTarget(1);
        var frozen = s.E<PrepDiscardTargetFrozenEvent>().Single();
        Need(frozen is { HandCount: 4, Hp: 1, RequestedCount: 3, RequiredCount: 3, ActualTurnOwnerSeat: 0 } && frozen.Source.SkillId == Skill,
            "The real preparation freezes Hand four minus HP one, before any target material is selected.");
        var original = s.G.CreateSnapshot(1).Players[1].Hand.Select(c => c.Id).ToArray();
        s.SelectOne(); s.SelectOne();
        Need(s.Root.PrepDiscard!.SelectedCardIds.Count == 2 && !s.P!.Choices.Any(c => Step(c) == "finish") &&
            !s.G.CardMovements.Any(m => m.Reason.Value == TargetReason),
            "Two selected opaque slots cannot publish a three-card confirmation or move a card early.");
        s.Reject(); s.Cold(); s.SelectOne(); s.Answer(c => Step(c) == "finish"); s.Until(() => s.At(PaidWatch));
        var frameId = s.Root.Id; var target = s.Root.PrepDiscard!.TargetPayment!; s.CheckPayment(target, false);
        Need(target.ActualCount == 3 && target.NonEquipmentCount == 2 && target.CardIds.SequenceEqual(original.Take(3)) &&
            target.From.All(p => p == CardLocation.Hand(1)) && s.V(1).HandCount == 1 &&
            s.G.CardMovements.Any(m => target.CardIds.Contains(m.CardId) && m.CardKind == CardKind.DefensiveHorse && m.From == CardLocation.Hand(1)),
            "The three original foreign Hand entities include a printed equipment: it really costs one card but contributes zero to N.");
        s.Privacy(); s.Cold(); s.Continue(); s.AtStep("pay"); s.Answer(c => Step(c) == "pay");
        Need(s.Root.PrepDiscard is { RequiredCount: 2, Stage: PrepDiscardStage.SelectingOwnerCards }, "The owner now owes the frozen printed non-equipment count two, rather than target X three.");
        s.SelectAll(); s.Until(() => s.At(PaidWatch)); var owner = s.Root.PrepDiscard!.OwnerPayment!; s.CheckPayment(owner, true);
        Need(owner.PayerSeat == 0 && owner.ActualCount == 2 && s.E<PrepDiscardPaidEvent>().Length == 2,
            "The owner branch really pays exactly two original HE materials after the target's paid children finish.");
        s.Cold(); s.Continue(); s.Play(); s.CheckFinished(frameId); s.CheckPayment(target, false); s.CheckPayment(owner, true);
        Need(s.E<PrepDiscardImmediateBenefitChosenEvent>() is [{ DrawTarget: false, Count: 2 }] && s.ImmediateMoves().Length == 0 &&
            s.E<PrepDiscardImmediateDrawRequestedEvent>().Length == 0 && s.E<PrepDiscardEndingIssuedEvent>().Length == 0,
            "The paid owner branch issues neither an immediate draw nor the legacy Ending promise."); s.Cold();

        var zero = Scene.Start("zero"); zero.AtOffer(); zero.Activate(); zero.SelectTarget(0); zero.SelectAll(); zero.Until(() => zero.At(PaidWatch));
        var zeroFrame = zero.Root.Id; var zeroPaid = zero.Root.PrepDiscard!.TargetPayment!; zero.CheckPayment(zeroPaid, false);
        Need(zeroPaid is { ActualCount: 1, NonEquipmentCount: 0 } && zeroPaid.From.Single() == CardLocation.Hand(0),
            "A printed horse in the owner's Hand is a real discard and freezes N zero, independently of its original zone.");
        zero.Cold(); zero.Continue(); zero.AtStep("draw"); zero.Answer(c => Step(c) == "draw"); zero.Play(); zero.CheckFinished(zeroFrame);
        Need(zero.E<PrepDiscardImmediateBenefitChosenEvent>() is [{ DrawTarget: true, Count: 0 }] &&
            zero.E<PrepDiscardImmediateDrawRequestedEvent>().Length == 0 && zero.E<PrepDiscardImmediateDrawPaidEvent>().Length == 0 && zero.ImmediateMoves().Length == 0,
            "Choosing immediate Draw zero finishes without manufacturing a native attempt, payment or physical gain."); zero.Cold();

        var lion = Scene.Start("lion"); lion.AtOffer(); lion.Activate(); lion.SelectTarget(0); lion.SelectAll(CardZoneKind.Equipment); lion.Until(() => lion.At(Health));
        var lionFrame = lion.Root.Id; var lionPayment = lion.Root.PrepDiscard!.TargetPayment!;
        Need(lion.V().Hp == 3 && lion.V().Equipment.Count == 0 && lion.E<RecoveryAppliedEvent>() is [{ TargetSeat: 0, Amount: 1 }] &&
            lion.Root.PrepDiscard is { FrozenHp: 2, RequestedCount: 1, TargetPayment.NonEquipmentCount: 0 } && lionPayment.From.Single() == CardLocation.Equipment(0),
            "A genuinely equipped Silver Lion leaves, heals through its native child and changes live HP before the later choice; frozen X and printed N remain the original one and zero.");
        lion.CheckPayment(lionPayment, false); lion.Cold(); lion.Continue(); lion.Until(() => lion.At(PaidWatch)); lion.Continue();
        lion.AtStep("draw"); lion.Answer(c => Step(c) == "draw"); lion.Play(); lion.CheckFinished(lionFrame);
        Need(lion.E<PrepDiscardImmediateDrawRequestedEvent>().Length == 0 && lion.E<RecoveryAppliedEvent>().Length == 1,
            "Returning the real leave-equipment and discard children neither recalculates X nor repeats the Lion heal or an empty draw."); lion.Cold();

        var loss = Scene.Start("target-loss"); loss.AtOffer(); loss.Activate(); loss.SelectTarget(1); loss.SelectAll(); loss.Until(() => loss.At(PaidWatch));
        var lossFrame = loss.Root.Id; var paid = loss.Root.PrepDiscard!.TargetPayment!; loss.Cold(); loss.Continue(); loss.Until(() => loss.At(PaidWatch));
        Need(loss.V().Hp == 19 && !loss.HasSkill() && JsonSerializer.Serialize(loss.Root.PrepDiscard!.TargetPayment) == JsonSerializer.Serialize(paid),
            "The target discard's real child removes the HP-derived original source after its actual payment; the source is no longer active.");
        loss.Cold(); loss.Continue(); loss.AtStep("draw"); loss.Answer(c => Step(c) == "draw"); loss.Play(); loss.CheckFinished(lossFrame);
        Need(loss.E<PrepDiscardImmediateDrawPaidEvent>() is [{ ProgramFrameId: var id, TargetSeat: 1, RequestedCount: 2, ActualCount: 2 }] && id == lossFrame &&
            loss.ImmediateMoves().Length == 2 && loss.E<PrepDiscardEndingIssuedEvent>().Length == 0,
            "The local paid-obligation contract continues this already-paid target's original frozen benefit after source loss, without creating a legacy promise.");
        loss.CheckPayment(paid, false); loss.Cold();

        var hidden = Scene.Start("undefined"); hidden.AtOffer(); hidden.Activate(); hidden.AtStep("target");
        Need(Enumerable.Range(1, 3).All(seat => hidden.V(seat) is { HpUndefined: true, HandCount: 4 }) &&
            hidden.P!.Choices.All(c => c.Targets.SequenceEqual([0])) && hidden.P.Choices.Count == 1,
            "The real identity-mode Gaoling concealment seam leaves all three card-owning opponents without a comparable HP value; new 238 offers only its defined-HP owner rather than treating unknown HP as zero.");
        var before = State(hidden.G); var hiddenRoot = hidden.Root.Id;
        Need(!hidden.G.Submit(new AnswerPromptCommand(0, hidden.P!.PromptId, new($"prep-discard.{hiddenRoot}.target-1"), hidden.G.Revision)).Accepted && State(hidden.G) == before,
            "A forged hidden-HP target choice is atomically rejected instead of using its trusted stored raw HP."); hidden.Cold();
    }

    public static void ImmediateTargetDrawUsesNativeAttemptsGainChildrenAndPaidSourceLossCold()
    {
        var hongde = Scene.Start("hongde"); hongde.AtOffer(); hongde.Activate(); hongde.SelectTarget(1); hongde.SelectAll();
        hongde.Until(() => hongde.At(PaidWatch)); var rootId = hongde.Root.Id; hongde.Cold(); hongde.Continue();
        hongde.AtStep("draw");
        Need(hongde.V(1).Hp == 2 && hongde.V(1).Skills?.Any(s => s.ContentId == Hongde) == true &&
            hongde.E<RecoveryAppliedEvent>() is [{ TargetSeat: 1, Amount: 1 }] && hongde.E<BuZhiStartedEvent>().Length == 0,
            "The target's true paid-discard child recovers one HP and only then enables its HP-derived Hongde, so the earlier two-card loss cannot consume the gain-only control opportunity.");
        hongde.AtStep("draw"); hongde.Answer(c => Step(c) == "draw"); hongde.Until(() => hongde.At(Gain));
        var root = hongde.Root; var outcome = root.PrepDiscard!.ImmediateDraw!.Outcome!;
        Need(root.Id == rootId && root.OwnerSeat == 0 && outcome is { RecipientSeat: 1, RequestedCount: 2 } && outcome.ActualDrawIds.Count == 2 &&
            hongde.E<BuZhiStartedEvent>().Single(e => e.Source.SkillId == Hongde).Source.OwnerSeat == 1 &&
            hongde.E<BuZhiMovementPaidEvent>().Single(e => e.Target == 0) is { Actual: 1 } &&
            hongde.V().HandCount == 5 && hongde.V(1).HandCount == 3 && !hongde.E<PhaseChangedEvent>().Any(e => e.ActorSeat == 0 && e.Phase == TurnPhase.Play),
            "The other recipient's genuine preparation Draw two reaches native Hongde once; its public-team NPC decision pays one real card to the human source before that source's Play phase.");
        hongde.CheckDraw(rootId, 1, 2, outcome); hongde.Privacy(); hongde.Cold(); hongde.Continue(); hongde.Play(); hongde.CheckFinished(rootId);
        Need(hongde.E<BuZhiStartedEvent>().Count(e => e.Source.SkillId == Hongde) == 1 && hongde.E<BuZhiMovementPaidEvent>().Count(e => e.Target == 0) == 1 &&
            hongde.G.CardMovements.Count(m => m.Reason.Value == "program.bu-zhi.draw" && m.To == CardLocation.Hand(0)) == 1,
            "Cold returns from the actual recipient's Hongde gain child without repeating either native payment or entering an Ending promise."); hongde.Cold();

        foreach (var scene in new[] { "request", "request-death", "paid-loss", "paid-death" })
        {
            var s = Scene.Start(scene); s.AtOffer(); s.Activate(); s.SelectTarget(0); s.SelectAll(); s.Until(() => s.At(PaidWatch));
            var frame = s.Root.Id; s.Continue(); s.AtStep("draw"); s.Answer(c => Step(c) == "draw"); s.Until(() => s.At(BeforeDraw));
            var attempt = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f => f.Context.ParentFrameId == frame);
            Need(s.Root.PrepDiscard is { Stage: PrepDiscardStage.ImmediateDrawAttempt, ImmediateDraw.Outcome: null } &&
                s.Root.NativeDraw is { RequestedCount: 2, ReturnKind: NativeDrawAttemptReturnKind.ProgramOperation, Operation: SkillProgramEffectOp.ResolvePrepDiscardOrImmediateDraw } producer &&
                producer.ActiveAttemptId == attempt.Id && attempt.Context is { RecipientSeat: 0, RequestedCount: 2 } && attempt.Context.Reason.Value == DrawReason &&
                s.E<PrepDiscardImmediateDrawPaidEvent>().Length == 0 && s.ImmediateMoves().Length == 0,
                "The exact operation-owned native pre-draw child pauses after issuing the frozen request and before any material or paid event. " + s.Diagnostic());
            s.Privacy(); s.Cold(); s.Continue();
            if (scene == "request-death")
            {
                s.Until(() => !s.V().IsAlive && s.G.ResolutionStack.All(f => f.Id != frame) && s.E<PrepDiscardFinishedEvent>().Any(e => e.ProgramFrameId == frame));
                Need(s.G.State.Winner == Winner.None && s.V(1).IsAlive && s.E<PrepDiscardImmediateDrawRequestedEvent>().Length == 1 &&
                    s.E<PrepDiscardImmediateDrawPaidEvent>() is [{ ProgramFrameId: var cancelledFrame, RequestedCount: 2, ActualCount: 0, Cancelled: true }] && cancelledFrame == frame &&
                    s.E<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == attempt.Id && e.ParentFrameId == frame && !e.Replaced) == 1 &&
                    s.E<PrepDiscardFinishedEvent>().Single(e => e.ProgramFrameId == frame) is { Completed: false } && s.ImmediateMoves().Length == 0,
                    "Death in the genuine pre-draw child returns the already-issued attempt but cancels its still-unpaid physical cards; it drains to the owning producer rather than inventing a dead-owner draw. " + s.Diagnostic()); s.Cold(); continue;
            }
            s.Until(() => s.At(scene == "request" ? Gain : Mutation));
            var paidOutcome = s.Root.PrepDiscard!.ImmediateDraw!.Outcome!; s.CheckDraw(frame, 0, 2, paidOutcome);
            Need(paidOutcome.AttemptFrameId == attempt.Id && s.E<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == attempt.Id && e.ParentFrameId == frame && !e.Replaced) == 1,
                "The native return signs its exact real attempt and physical outcome once, before gain children run.");
            s.Cold(); s.Continue();
            if (scene == "paid-death")
            {
                s.Until(() => !s.V().IsAlive && s.G.ResolutionStack.All(f => f.Id != frame) && s.E<PrepDiscardFinishedEvent>().Any(e => e.ProgramFrameId == frame));
                Need(s.G.State.Winner == Winner.None && s.V(1).IsAlive && s.E<PlayerDiedEvent>().Count(e => e.VictimSeat == 0) == 1 &&
                    s.E<PrepDiscardImmediateDrawPaidEvent>().Length == 1 && s.ImmediateMoves().Length == 2 && s.E<PrepDiscardEndingIssuedEvent>().Length == 0,
                    "A genuine paid gain-child death drains the already-issued draw while a surviving real ally keeps the battle live; it cannot pay again or leave its producer frame behind. " + s.Diagnostic());
            }
            else
            {
                if (scene == "paid-loss")
                {
                    s.Until(() => s.At(Mutation)); Need(s.V().Hp == 1 && !s.HasSkill() && JsonSerializer.Serialize(s.Root.PrepDiscard!.ImmediateDraw!.Outcome) == JsonSerializer.Serialize(paidOutcome),
                        "The real gain child loses the original HP-derived source after payment while the exact frozen two-card outcome remains owned by its producer."); s.Cold(); s.Continue();
                }
                s.Play(); s.CheckFinished(frame); s.CheckDraw(frame, 0, 2, paidOutcome);
                Need(s.V().HandCount == 4 && s.E<PrepDiscardImmediateDrawPaidEvent>().Length == 1,
                    "The original discard-two/draw-two card flow resumes once, including after source loss, without recomputing from current HP or hand count.");
            }
            s.Cold();
        }

        var npc = Scene.Start("npc-request"); npc.Play();
        Need(npc.V() is { Hp: 2, HandCount: 4, HandLimit: 4 } && !npc.HasSkill() &&
            Enumerable.Range(1, 3).All(seat => npc.V(seat).HandCount == 0),
            "Only the human recipient owns HE cards; a real hand-limit modifier preserves its four cards through this initial Play's ordinary discard phase, while the source NPC owns no payment cards.");
        npc.EndPlay(); npc.Until(() => npc.At(BeforeDraw));
        var otherRoot = npc.Root; var otherFrame = otherRoot.Id; var draft = otherRoot.PrepDiscard!;
        var otherAttempt = npc.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f => f.Context.ParentFrameId == otherFrame);
        var recipientWatcher = npc.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == BeforeDraw);
        Need(otherRoot.OwnerSeat == 1 && draft is { ActualTurnOwnerSeat: 1, TargetSeat: 0, FrozenHandCount: 4, FrozenHp: 2, RequestedCount: 2,
                Stage: PrepDiscardStage.ImmediateDrawAttempt, TargetPayment.ActualCount: 2, TargetPayment.NonEquipmentCount: 2, OwnerPayment: null, ImmediateDraw.Outcome: null } &&
            otherRoot.NativeDraw is { RequestedCount: 2, ReturnKind: NativeDrawAttemptReturnKind.ProgramOperation, Operation: SkillProgramEffectOp.ResolvePrepDiscardOrImmediateDraw } native &&
            native.ActiveAttemptId == otherAttempt.Id && otherAttempt.Context is { RecipientSeat: 0, RequestedCount: 2, ActualTurnOwnerSeat: 1 } && otherAttempt.Context.Reason.Value == DrawReason &&
            recipientWatcher.OwnerSeat == 0 && recipientWatcher.WindowContext?.NativeDraw == otherAttempt.Context && npc.P is { PlayerSeat: 0 } &&
            npc.E<PrepDiscardImmediateBenefitChosenEvent>() is [{ DrawTarget: true, TargetSeat: 0, Count: 2 }] &&
            npc.E<PrepDiscardImmediateDrawPaidEvent>().Length == 0 && npc.ImmediateMoves().Length == 0 && npc.V().HandCount == 2,
            "The real NPC chooses its only legal target and cannot pay an owner cost; its operation issues Draw two to the human, whose own native pre-draw window—not the source's controller—pauses before any new cards are paid. " + npc.Diagnostic());
        var otherPayment = draft.TargetPayment!; npc.CheckPayment(otherPayment, false); npc.Privacy(); npc.Cold(); npc.Continue(); npc.Until(() => npc.At(Gain));
        var otherOutcome = npc.Root.PrepDiscard!.ImmediateDraw!.Outcome!;
        Need(npc.Root.OwnerSeat == 1 && otherOutcome is { RecipientSeat: 0, RequestedCount: 2 } && otherOutcome.AttemptFrameId == otherAttempt.Id &&
            npc.E<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == otherAttempt.Id && e.ParentFrameId == otherFrame && e.RecipientSeat == 0 && !e.Replaced) == 1,
            "The different-owner native return binds the human recipient's exact request child to the still-owning NPC preparation producer once.");
        npc.CheckDraw(otherFrame, 0, 2, otherOutcome); npc.Cold(); npc.Continue();
        npc.Until(() => npc.G.ResolutionStack.All(f => f.Id != otherFrame) && npc.E<PrepDiscardFinishedEvent>().Any(e => e.ProgramFrameId == otherFrame));
        npc.CheckFinished(otherFrame); npc.CheckDraw(otherFrame, 0, 2, otherOutcome); npc.CheckPayment(otherPayment, false);
        Need(npc.V().HandCount == 4 && npc.V(1).HandCount == 0 && npc.E<PrepDiscardImmediateDrawPaidEvent>().Length == 1,
            "Returning the human's paid gain children completes the NPC's original preparation exactly once, restoring the recipient's two discarded cards without charging the empty-handed source."); npc.Cold();
    }

    public static void LegacyEndingTimingAndActualRecipesStayIsolatedCold()
    {
        var s = Scene.Start("legacy"); s.AtOffer(); s.Activate(); s.SelectTarget(1); s.SelectAll(); s.Until(() => s.At(PaidWatch));
        var frame = s.Root.Id; s.Cold(); s.Continue(); s.AtStep("ending"); s.Answer(c => Step(c) == "ending"); s.Play();
        var promise = s.E<PrepDiscardEndingIssuedEvent>().Single().Promise;
        Need(promise.ProducerProgramFrameId == frame && promise is { TargetSeat: 1, Count: 2 } && promise.PaidSource.SkillId == Legacy &&
            s.G.CardMovements.All(m => m.Reason.Value != EndingReason) && s.V(1).HandCount == 2 &&
            s.E<PrepDiscardImmediateBenefitChosenEvent>().Length == 0 && s.E<PrepDiscardImmediateDrawRequestedEvent>().Length == 0,
            "The original boundary program still freezes a genuine Ending promise and pays nothing during preparation or Play; the new immediate operation is not substituted into the old skill.");
        s.Cold(); s.EndPlay(); s.Until(() => s.E<PrepDiscardEndingDrawnEvent>().Any());
        var drawn = s.E<PrepDiscardEndingDrawnEvent>().Single();
        Need(drawn is { TargetSeat: 1, RequestedCount: 2, ActualCount: 2 } && drawn.PromiseId == promise.Id &&
            s.E<PrepDiscardEndingConsumedEvent>().Single(e => e.Id == promise.Id).Applied && s.V(1).HandCount == 4 &&
            s.G.CardMovements.Count(m => m.Reason.Value == EndingReason && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(1)) == 2 &&
            !s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == frame),
            "Only the real same-turn Ending boundary consumes the old promise and produces its two physical cards once."); s.Cold();
        VerifyActualRecipes();
    }

    private static void VerifyActualRecipes()
    {
        var old = StandardShanheCampaignCatalog.CreateDefinition(3, StandardShanheCampaignCatalog.LocalEditionId);
        Need(old.Nodes.SelectMany(n => n.Encounters ?? []).All(e => e.Enemies.All(m => m.GeneralId != Boss)), "The new S5 immediate 238 form is absent from the 2024 encounter catalog.");
        for (var tier = 1; tier <= 3; tier++)
        {
            var definition = StandardShanheCampaignCatalog.CreateDefinition(tier, StandardShanheCampaignCatalog.Returning2026EditionId);
            var encounter = definition.Nodes.Single(n => n.Id == "huanyuan").Encounters!.Single(e => e.Id == "huanyuan:yujin-s5");
            Need(encounter.Enemies is [{ GeneralId: Boss, Hp: null, MaxHpBonus: 0, InitialHandBonus: 0, DrawBonus: 0, SlashBonus: 0, StartingHand: null }] &&
                (encounter.Allies?.Count ?? 0) == 0 && encounter.VictoryGold == 80 && encounter.HealAfterVictory == 2,
                "All three local tiers retain the raw complete four-HP single-238 form without higher-band skills, invented gear, growth or allies.");
            var setup = new ShanheBattleSetup(83, 1, "huanyuan", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [], encounter.Enemies, [], ShanheCampaignContent.GetContentHash(definition));
            var r = StandardShanheCampaignCatalog.CreateBattleRegistry(setup); var g = StandardShanheCampaignCatalog.CreateBattle(setup);
            Need(g.Submit(new StartGameCommand()).Accepted, "The actual current encounter starts through its conditional production registry.");
            var npc = g.CreateSnapshot(0).Players.Single(p => p.GeneralId == Boss);
            Need(npc is { Hp: 4, MaxHp: 4, HandCount: 4, Armor: 0 } && npc.Equipment.Count == 0 && npc.Skills?.Select(k => k.ContentId).SequenceEqual([Skill]) == true,
                "The actual encounter starts four/four HP with ordinary four-card initialization and only the new printed immediate Zhenjun.");
            Need(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "Actual recipe, source content and startup remain identical through JSON cold replay.");
            var plain = setup with { EnemyRoster = [new("classic:cao-cao")] }; var extra = plain with { ExtraSkillIds = [Skill] };
            Need(!StandardShanheCampaignCatalog.CreateBattleRegistry(plain).Skills.ContainsKey(Skill) &&
                StandardShanheCampaignCatalog.CreateBattleRegistry(extra).Skills.ContainsKey(Skill) &&
                !StandardShanheCampaignCatalog.CreateBattleRegistry(plain).Generals.ContainsKey(Boss),
                "Warm actual-alias, extra-skill and bare recipes preserve the conditional package and cache boundary in both directions.");
        }
    }

    private static void VerifyLoaderBoundaries()
    {
        string Read(string suffix) { var a = typeof(StandardContentPackage).Assembly; using var stream = a.GetManifestResourceStream(a.GetManifestResourceNames().Single(n => n.EndsWith('.' + suffix, StringComparison.Ordinal)))!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        var rules = Read("shanhe-s5-yujin.rules.json"); var presentation = Read("shanhe-s5-yujin.presentation.json"); _ = SkillProgramCatalog.Load(rules, presentation);
        foreach (var change in new Action<JsonObject>[] { t => t["optional"] = false, t => t["window"] = "turnEnding", t => t["subject"] = "any",
            t => t["effects"]![0]!["stateId"] = "not-an-ending-promise", t => t["effects"]![0]!["zones"] = new JsonArray("hand"), t => t["effects"]![0]!["target"] = "selectedTarget" })
        {
            var copy = JsonNode.Parse(rules)!; change(copy["skills"]![0]!["triggers"]![0]!.AsObject()); var rejected = false;
            try { _ = SkillProgramCatalog.Load(copy.ToJsonString(), presentation); } catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "The new operation is strictly an optional own preparation HE instruction without an Ending state or different recipient.");
        }
    }

    private static string? Step(PromptChoice c) => c.Parameters.GetValueOrDefault("prep-discard-step");
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, g.PlayerCount).Select(i => SnapshotJson.Serialize(g.CreateSnapshot(i))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
        g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(g.CreateCheckpoint()) });

    private sealed class Scene(GameEngine game, ContentRegistry registry, string scene)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public ProgramSkillFrame Root => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PrepDiscard is not null);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardMovementRecord[] ImmediateMoves() => G.CardMovements.Where(m => m.From == CardLocation.DrawPile && m.Reason.Value == DrawReason).ToArray();
        public bool HasSkill() => V().Skills?.Any(s => s.ContentId == Skill) == true;
        public bool At(string skill) => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.ContainsKey("option-id"));
        public void Accept(GameCommand command) { var result = G.Submit(command); Need(result.Accepted, (result.Error?.Message ?? "Real preparation command rejected.") + " " + Diagnostic()); }
        public void Answer(Func<PromptChoice, bool> choose)
        {
            var p = P!; var c = p.Choices.FirstOrDefault(choose); Need(c is not null, "The expected choice must actually be published. " + Diagnostic());
            Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision));
        }
        public void AtOffer() => Until(() => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == (scene == "legacy" ? Legacy : Skill) && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void AtStep(string step) => Until(() => P?.Choices.Any(c => Step(c) == step) == true);
        public void SelectTarget(int seat) { AtStep("target"); Answer(c => c.Targets.SequenceEqual([seat])); AtStep("card"); }
        public void SelectOne(CardZoneKind zone = CardZoneKind.Hand)
        {
            var choices = P!.Choices.Where(c => Step(c) == "card" && c.Parameters.GetValueOrDefault("source-zone") == zone.ToString()).ToArray();
            if (Root.PrepDiscard is { Stage: PrepDiscardStage.SelectingTargetCards, TargetSeat: not 0 } && zone == CardZoneKind.Hand)
                Need(choices.All(c => c.Cards.Count == 0), "Foreign Hand payment publishes only opaque region slots, never actual hidden card IDs.");
            var choice = choices.OrderBy(c => int.Parse(c.Parameters["slot-index"])).First(); Answer(c => c.Id == choice.Id);
        }
        public void SelectAll(CardZoneKind zone = CardZoneKind.Hand)
        { AtStep("card"); for (var i = 0; i < 12 && !P!.Choices.Any(c => Step(c) == "finish"); i++) SelectOne(zone); Answer(c => Step(c) == "finish"); }
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Until(Func<bool> done)
        {
            for (var i = 0; i < 200; i++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Choices.Any(c => c.Parameters.ContainsKey("option-id"))) { Continue(); continue; }
                    if (p.Kind == DecisionKind.RescueDying) { Answer(c => c.Parameters.GetValueOrDefault("response") == "let-die"); continue; }
                    if (p.Kind == DecisionKind.DiscardCards) { Accept(new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, G.Revision)); continue; }
                    if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification) { Answer(c => c.Cards.Count == 0); continue; }
                    if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                    throw new InvalidOperationException("Unexpected real preparation boundary: " + Diagnostic());
                }
                Need(G.State.Winner == Winner.None, "The bounded preparation fixture must remain live. " + Diagnostic()); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded real preparation/child/return was not reached: " + Diagnostic());
        }
        public void CheckPayment(PrepDiscardPayment p, bool owner)
        {
            var reason = owner ? OwnerReason : TargetReason; var moves = G.CardMovements.Where(m => m.Sequence > p.SequenceBefore && m.Sequence <= p.SequenceAfter && m.Reason.Value == reason).ToArray();
            Need(p.CardIds is IList<int> { IsReadOnly: true } && p.From is IList<CardLocation> { IsReadOnly: true } && p.CardIds.Count == p.ActualCount &&
                moves.Length == p.ActualCount && moves.Select(m => m.CardId).SequenceEqual(p.CardIds) && moves.Where((m, i) => m.From != p.From[i] || m.To != CardLocation.DiscardPile).Count() == 0 &&
                moves.Count(m => !EquipmentCatalog.IsEquipment(m.CardKind)) == p.NonEquipmentCount && p.CardIds.All(id => G.CardMovements.Count(m => m.CardId == id && m.Reason.Value == reason) == 1),
                "Each immutable payment proves its original materials, actual zones, printed non-equipment subtotal and exact native discard interval once. " + Diagnostic());
        }
        public void CheckDraw(long frame, int target, int count, NativeDrawOutcome outcome)
        {
            var paid = E<PrepDiscardImmediateDrawPaidEvent>().Single(e => e.ProgramFrameId == frame);
            var moves = G.CardMovements.Where(m => m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter && m.Reason.Value == DrawReason).ToArray();
            Need(outcome.RecipientSeat == target && outcome.RequestedCount == count && outcome.ActualDrawIds.Count == count && outcome.ActualDrawIds is IList<int> { IsReadOnly: true } &&
                outcome.Replacement is null && paid.TargetSeat == target && paid.RequestedCount == count && paid.ActualCount == count && !paid.Replaced && !paid.Cancelled &&
                paid.SequenceBefore == outcome.SequenceBefore && paid.SequenceAfter == outcome.SequenceAfter && paid.AttemptFrameId == outcome.AttemptFrameId &&
                moves.Length == count && moves.Select(m => m.CardId).SequenceEqual(outcome.ActualDrawIds) && moves.All(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(target)) &&
                outcome.ActualDrawIds.All(id => G.CardMovements.Count(m => m.CardId == id && m.Reason.Value == DrawReason) == 1) && E<PrepDiscardEndingIssuedEvent>().Length == 0,
                "The operation's frozen outcome and sole paid fact identify exactly the requested native Draw materials, real recipient, child attempt and movement interval. " + Diagnostic());
        }
        public void CheckFinished(long frame) => Need(E<PrepDiscardFinishedEvent>().Count(e => e.ProgramFrameId == frame && e.Completed) == 1 && G.ResolutionStack.All(f => f.Id != frame),
            "The actual preparation producer returns once and leaves no duplicate paid continuation. " + Diagnostic());
        public void Privacy() => Need(Enumerable.Range(0, G.PlayerCount).All(i => G.CreateSnapshot(i).Players.Where(p => p.Seat != i).All(p => p.Hand.Count == 0)) &&
            Enumerable.Range(1, G.PlayerCount - 1).All(i => G.CreateSnapshot(i).PendingDecision is null) &&
            !JsonSerializer.Serialize(E<PrepDiscardPaidEvent>()).Contains("CardIds", StringComparison.Ordinal),
            "Actual foreign hands and private discard choices stay hidden; public payment facts carry only scalar counts and intervals.");
        public void Reject() { var before = State(G); Need(!G.Submit(new AnswerPromptCommand(0, P!.PromptId, new("unpublished"), G.Revision)).Accepted && before == State(G), "An unpublished early confirmation is rejected atomically."); }
        public void Cold() { var before = State(G); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State(G) == before, "Cold replay preserves private selections, original paid materials, frozen native requests/outcomes and exact child returns without paying twice."); }
        public string Diagnostic() => JsonSerializer.Serialize(new { scene, G.State.TurnNumber, G.State.Winner,
            Seats = G.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.Hp, p.IsAlive, p.HandCount, Skills = p.Skills?.Select(s => s.ContentId) }),
            Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId, Choices = P.Choices.Select(c => new { c.Id, Step = ShanheYujinChecks.Step(c) }) },
            Frozen = E<PrepDiscardTargetFrozenEvent>(), Paid = E<PrepDiscardPaidEvent>(), Draw = E<PrepDiscardImmediateDrawPaidEvent>(), Finished = E<PrepDiscardFinishedEvent>(),
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind }) });
        public static Scene Start(string scene)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            if (scene == "undefined")
            {
                var identity = GameEngine.CreateStandard(new() { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = UndefinedMode,
                    UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
                var control = new Scene(identity, r, scene); control.Accept(new StartGameCommand()); control.Until(() => control.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
                control.Accept(new SelectGeneralCommand(0, Owner, identity.Revision, control.P!.PromptId)); return control;
            }
            var self = scene is "request" or "request-death" or "paid-loss" or "paid-death" or "lion" or "npc-request";
            ShanheStartingCard[] own = scene == "zero" ? [new(CardKind.DefensiveHorse)] : scene == "lion" ? [new(CardKind.Dodge)] : Enumerable.Range(1, 4).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Heart, i)).ToArray();
            ShanheStartingCard[] ally = scene is "pay" or "target-loss" ? [new(CardKind.Dodge, Suit.Club, 1), new(CardKind.DefensiveHorse, Suit.Spade, 2), new(CardKind.Dodge, Suit.Club, 3), new(CardKind.Dodge, Suit.Club, 4)] :
                scene == "npc-request" ? [] : Enumerable.Range(5, scene == "hongde" ? 3 : 4).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Club, i)).ToArray();
            var setup = new ShanheBattleSetup(83, 1, "fixture:s5-yj-" + scene, Owner, self ? 2 : 20, 0, 0, 0, 0, [], own,
                scene == "lion" ? [new(CardKind.SilverLion, Suit.Club, 1)] : [],
                [new(Enemy) { StartingHand = [] }, new(Enemy) { StartingHand = [] }],
                [new(Ally, Hp: scene == "npc-request" ? 20 : scene is "pay" or "target-loss" or "hongde" ? 1 : 2) { StartingHand = ally }], new string('f', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new Scene(g, r, scene); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-s5-yujin", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            foreach (var id in new[] { Skill, Legacy, Hongde }) b.AddSkill(Production.Value.GetSkill(id));
            var defs = new List<Dictionary<string, object>>(); var labels = new Dictionary<string, object>();
            object Pause(string id) => new { op = "chooseOption", target = "owner", resultBind = id, options = new[] { new { id } } };
            void Add(string id, string key, object value, params string[] options)
            { defs.Add(new() { ["id"] = id, ["revision"] = 1, [key] = value }); labels[id] = new { name = id, description = "真实准备弃牌、原生摸牌与子流程", optionLabels = options.ToDictionary(s => s, s => s) }; }
            var watchEffects = new List<object> { Pause("paid") };
            if (scene == "target-loss") watchEffects.AddRange([new { op = "loseHp", target = "owner", amount = 1 }, Pause("lost")]);
            Add(PaidWatch, "triggers", new object[] {
                new { id = "paid-owned-discard", window = "cardsMoved", subject = "owner", sourceZones = new[] { "hand", "equipment" }, movementReasons = new[] { TargetReason, OwnerReason }, movementOccurrence = "perOwnerBatch", optional = false, effects = watchEffects.ToArray() },
                new { id = "paid-foreign-discard", window = "discardPileReceived", subject = "owner", discardOwnerScope = "other", sourceZones = new[] { "hand", "equipment" }, movementReasons = new[] { TargetReason, OwnerReason }, movementOccurrence = "perBatch", optional = false, effects = watchEffects.ToArray() }
            }, scene == "target-loss" ? ["paid", "lost"] : ["paid"]);
            Add(Gain, "triggers", new[] { new { id = "actual-gained-materials", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" }, movementOccurrence = "perBatch",
                movementReasons = new[] { scene == "hongde" ? "program.bu-zhi.draw" : DrawReason }, optional = false, effects = new[] { Pause("continue") } } }, "continue");
            object[] beforeDraw = scene == "request-death" ? [Pause("before"), new { op = "loseHp", target = "owner", amount = 20 }] : [Pause("before")];
            Add(BeforeDraw, "triggers", new[] { new { id = "native-request-before-materials", window = "nativeDrawStarting", subject = "owner", optional = false, effects = beforeDraw } }, "before");
            var mutation = new List<object> { Pause("paid") }; mutation.Add(new { op = "loseHp", target = "owner", amount = scene == "paid-death" ? 20 : 1 }); if (scene != "paid-death") mutation.Add(Pause("lost"));
            Add(Mutation, "triggers", new[] { new { id = "first-paid-draw-source-change", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" }, movementReasons = new[] { DrawReason }, movementOccurrence = "perBatch",
                usageScope = "game", usageLimit = 1, priority = 200, optional = false, effects = mutation.ToArray() } }, scene == "paid-death" ? ["paid"] : ["paid", "lost"]);
            Add(Health, "triggers", new[] { new { id = "native-lion-recovery-child", window = "afterHpRecovered", subject = "owner", optional = false, effects = new[] { Pause("recovered") } } }, "recovered");
            Add(Gate, "hpSkillGrants", new[] { new { id = "real-health-threshold", skillId = Skill, minimumHp = scene == "target-loss" ? 20 : 2 } });
            Add(HongdeGate, "hpSkillGrants", new[] { new { id = "only-after-discard-recovery", skillId = Hongde, minimumHp = 2 } });
            Add(HongdeEntry, "triggers", new[] { new { id = "paid-discard-recovery", window = "cardsMoved", subject = "owner", sourceZones = new[] { "hand" }, movementReasons = new[] { TargetReason }, movementOccurrence = "perOwnerBatch",
                optional = false, effects = new[] { new { op = "recover", target = "owner", amount = 1 } } } });
            Add(Keep, "modifiers", new[] { new { id = "retain-four-through-human-discard", query = "handLimit", operation = "add", value = 2, priority = 0 } });
            Add(Quiet, "triggers", new[] { new { id = "quiet-real-other-turn", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var own = new List<string> { PaidWatch };
            own.Add(scene == "npc-request" ? Keep : scene == "legacy" ? Legacy : scene is "target-loss" or "paid-loss" ? Gate : Skill);
            if (scene is "request" or "request-death" or "paid-loss" or "paid-death" or "npc-request") own.Add(BeforeDraw);
            if (scene is "request" or "hongde" or "npc-request") own.Add(Gain);
            if (scene is "paid-loss" or "paid-death") own.Add(Mutation);
            if (scene == "lion") own.Add(Health);
            b.AddGeneral(new(Owner, "原生准备镇军拥有者", "", own[0], "wei", scene is "request" or "request-death" or "paid-loss" or "paid-death" or "lion" or "npc-request" ? 4 : 20, own.Skip(1).ToArray()));
            if (scene == "undefined") b.AddSkill(Production.Value.GetSkill("ol:gaoling"));
            b.AddGeneral(new(Ally, "公开同队实际受益者", "", Quiet, "wei", 20, scene == "hongde" ? [HongdeGate, HongdeEntry] : scene == "undefined" ? ["ol:gaoling"] : scene == "npc-request" ? [Skill] : []));
            b.AddGeneral(new(Enemy, "实际存活其他队伍", "", Quiet, "shu", 20, scene == "undefined" ? ["ol:gaoling"] : []));
            if (scene == "undefined") b.AddGeneral(new(Hidden, "真实无定义HP目标", "", Quiet, "shu", 4, ["ol:gaoling"]));
            var standard = ContentRegistry.Build(new StandardContentPackage());
            foreach (var kind in new[] { CardKind.SilverLion, CardKind.DefensiveHorse })
                if (standard.Cards.Values.All(c => c.LegacyKind != kind)) { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:s5-yj-card-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            b.AddDeck(new(Deck, "固定原生实体、零构造初手和自然摸牌", scene == "undefined" ? 4 : 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            if (scene == "undefined")
            {
                b.AddMode(new(UndefinedMode, "真实隐匿无定义HP目标", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, Deck,
                    GeneralCandidateCount: 4, GeneralPoolIds: [Owner, Ally, Enemy, Hidden])); return;
            }
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "真实镇军准备与结束阶段", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
