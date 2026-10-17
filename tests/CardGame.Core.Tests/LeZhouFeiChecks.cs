using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class LeZhouFeiChecks
{
    private const string LfLing = "le:lingkong", LfXian = "le:xianshu", LfOwner = "fixture:lzf-owner", LfMode = "identity:classic-le-zhou-fei";
    private const string LfDriver = "fixture:lzf-driver", LfQuiet = "fixture:lzf-quiet", LfWatch = "fixture:lzf-watch", LfInsert = "fixture:lzf-insert", LfConditional = "fixture:lzf-conditional";
    private const string LfGift = "skill-program.le-zhou-fei.xianshu.gift", LfDraw = "skill-program.le-zhou-fei.xianshu.draw";
    private const string LfZen = "ol:zenrun", LfObtain = "skill-program.sun-hong.zenrun.obtain", LfCompensation = "skill-program.sun-hong.zenrun.compensation";

    public static void LingkongOriginalAndTransferredEntitiesHandLimitCold()
    {
        var s = LfScenario.Start("initial"); var initial = s.E<LingkongInitialEntityMarkedEvent>().Where(e => e.OriginalOwnerSeat == 0).ToArray();
        s.Need(initial.Length == 4 && initial.Select(e => e.CardId).ToHashSet().SetEquals(s.V().Hand.Select(c => c.Id)) && initial.All(e =>
            s.G.CardMovements.Count(m => m.Sequence == e.EntrySequence && m.CardId == e.CardId && m.CardKind == e.PrintedKind && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.InitialDeal) == 1) && s.V().Hand.All(LfMarked),
            "The actual opening Hand receives four global exact-entity marks, each with its original once-only entry invoice."); s.Cold();
        var ordinaryLimit = s.V().HandLimit; s.Use("draw-four"); s.Play(); var firstGain = s.E<LingkongGainMarkedEvent>().Single(e => e.Source.OwnerSeat == 0);
        var gained = s.E<LingkongGainEntityMarkedEvent>().Where(e => e.FrameId == firstGain.FrameId).Select(e => e.CardId).ToArray();
        s.Need(gained.Length == 4 && gained.All(id => s.V().Hand.Any(c => c.Id == id && LfMarked(c))), "The first outside-Draw native gain marks the whole four-card batch.");
        s.Use("draw-four"); s.Play(); s.Need(s.V().Hand.Count(LfMarked) == 8 && s.V().HandCount == 12 && s.V().HandLimit == ordinaryLimit, "A second actual gain stays unmarked; exemption is not folded into the ordinary displayed limit.");
        var original = initial[0].CardId; s.Use("give", [original], [1]); s.Play();
        s.Need(s.V(1).Hand.Single(c => c.Id == original).SkillMarkLabel?.Contains("箜篌", StringComparison.Ordinal) == true && Enumerable.Range(1, 3).All(v => s.G.CreateSnapshot(v).Players[0].Hand.Count == 0), "The public membership label follows the entity without exposing another holder's private Hand faces.");
        var slot = s.V(1).Hand.ToList().FindIndex(c => c.Id == original); s.Use("take", targets: [1]); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.ContainsKey("slot-index")) == true);
        s.Need(s.P!.Choices.Where(c => c.Parameters.ContainsKey("slot-index")).All(c => c.Cards.Count == 0), "Reobtaining an original entity uses anonymous foreign-Hand slots."); s.Private(0); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("slot-index") == slot.ToString(System.Globalization.CultureInfo.InvariantCulture)); s.Play();
        s.Need(s.V().Hand.Single(c => c.Id == original) is var returned && LfMarked(returned) && s.E<LingkongInitialEntityMarkedEvent>().Count(e => e.CardId == original) == 1, "Leaving and reobtaining do not consume or duplicate initial membership.");
        var turn = s.G.State.TurnNumber; s.End(); s.Play();
        s.Need(s.V().HandCount == 12 && !s.G.CardMovements.Any(m => m.TurnNumber == turn && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile), "The real discard phase exempts eight marked entities and keeps the four eligible cards that fit the ordinary limit."); s.Cold();
        foreach (var scene in new[] { "recipient", "inherited" })
        {
            s = LfScenario.Start(scene); var id = s.V().Hand[0].Id; s.Use("give", [id], [1]); s.Play(); s.End(); s.Play();
            var peerTurn = s.E<TurnStartedEvent>().First(e => e.ActorSeat == 1).TurnNumber;
            var discarded = s.G.CardMovements.Count(m => m.TurnNumber == peerTurn && m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile);
            s.Need(scene == "inherited" ? discarded == 0 && s.V(1).HandCount == 5 : discarded == 2 && s.V(1).HandCount == 3,
                "Only a current Lingkong holder receives exemption for the global marked entity; an unskilled recipient keeps its label but must really discard to its ordinary limit."); s.Cold();
        }
    }

    public static void LingkongFirstWholeGainOutsideDrawPhaseActualTurnsCold()
    {
        var s = LfScenario.Start("timing");
        s.Need(s.V().HandCount == 3 && !s.V().Hand.Any(LfMarked) && !s.E<LingkongGainMarkedEvent>().Any(), "Normal Draw2 and a real skill Draw1 still inside the owner's Draw phase are both excluded without spending the outside-phase first gain.");
        s.Use("draw-two"); s.Play(); var first = s.E<LingkongGainMarkedEvent>().Single(e => e.Source.OwnerSeat == 0); var firstIds = s.E<LingkongGainEntityMarkedEvent>().Where(e => e.FrameId == first.FrameId).Select(e => e.CardId).ToArray();
        s.Need(first.EntityCount == 2 && firstIds.Length == 2 && firstIds.All(id => s.V().Hand.Any(c => c.Id == id && LfMarked(c))), "The first true Play-phase gain marks both actual identities as a whole batch.");
        s.Use("lose-ling"); s.Play(); s.Use("grant-ling"); s.Play(); s.Use("draw-two"); s.Play();
        s.Need(s.E<LingkongGainMarkedEvent>().Count(e => e.Source.OwnerSeat == 0) == 1 && s.V().Hand.Count(LfMarked) == 2, "Losing and regaining a real source does not reset this actual turn's first-gain history.");
        var phase = s.E<ActualDiscardRecoveryPhaseStartedEvent>().Last(e => e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && e.Phase.ActorSeat == 0).Phase;
        s.End(); s.Play(); var extra = s.E<ActualDiscardRecoveryPhaseStartedEvent>().Last(e => e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && e.Phase.ActorSeat == 0).Phase;
        s.Need(extra.Token != phase.Token && extra.TurnNumber == phase.TurnNumber, "A real inserted Play has a different phase token inside the same actual turn."); s.Use("draw-two"); s.Play();
        s.Need(s.E<LingkongGainMarkedEvent>().Count(e => e.Source.OwnerSeat == 0) == 1 && s.V().Hand.Count(LfMarked) == 2, "An extra Play cannot reset the shared actual-turn first-gain allowance."); s.Cold();
        s.End(); s.Reach(() => s.Hold("foreign-draw")); var foreignTurn = s.G.State.TurnNumber; s.Private(0); s.Cold(); s.Continue(); s.Play();
        var foreign = s.E<LingkongGainMarkedEvent>().Single(e => e.Source.OwnerSeat == 0 && e.ActualTurn == foreignTurn);
        var observed = s.E<LingkongOutsideGainObservedEvent>().Single(e => e.BatchId == foreign.BatchId && e.RecipientSeat == 0);
        s.Need(observed is { Phase: TurnPhase.Draw, PhaseActorSeat: 1, RecipientSeat: 0, EntityCount: 2 } && foreign.TurnOwner == 1 &&
            s.E<LingkongGainEntityMarkedEvent>().Count(e => e.FrameId == foreign.FrameId) == 2, "During somebody else's actual Draw phase, a genuine two-card gain by this owner is outside its own Draw phase and marks both identities."); s.Cold();

        s = LfScenario.Start("late"); s.Use("draw-two"); s.Play(); s.Need(!s.V().Hand.Any(LfMarked), "The first real gain while no skill is held creates no marks."); s.Use("grant-ling"); s.Play(); s.Use("draw-two"); s.Play();
        s.Need(s.V().Skills!.Any(k => k.ContentId == LfLing) && !s.V().Hand.Any(LfMarked) && !s.E<LingkongGainMarkedEvent>().Any(e => e.Source.OwnerSeat == 0), "Acquiring the skill after the actual first gain cannot mark a later batch or retroactively mark the first."); s.Cold();
        s = LfScenario.Start("empty"); var before = s.G.CardMovements.Count; s.Use("draw-two"); s.Play();
        s.Need(s.G.CardMovements.Count == before && !s.E<LingkongGainMarkedEvent>().Any(e => e.Source.OwnerSeat == 0), "A genuinely empty deck produces no gained entity and does not consume first-gain eligibility.");
        s.Use("take", targets: [1]); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.ContainsKey("slot-index")) == true); s.Cold(); s.Answer(c => c.Parameters.ContainsKey("slot-index")); s.Play();
        s.Need(s.E<LingkongGainMarkedEvent>().Single(e => e.Source.OwnerSeat == 0).EntityCount == 1 && s.V().Hand.Count(LfMarked) == 5, "After the empty native draw, a real anonymous foreign-Hand obtain becomes the first nonempty gain and marks its exact entity."); s.Cold();

        s = LfScenario.Start("gain-issued-loss"); s.Use("draw-two"); s.Reach(() => s.Hold("before-loss"));
        var issuedBatch = s.E<LingkongGainQualifiedEvent>().Single(e => e.Source.OwnerSeat == 0); var issuedEntities = s.E<LingkongGainEntityIssuedEvent>().Where(e => e.BatchId == issuedBatch.BatchId && e.RecipientSeat == 0).OrderBy(e => e.GainSequence).ToArray();
        s.Need(issuedBatch.EntityCount == 2 && issuedEntities.Length == 2 && issuedEntities.All(e => s.G.CardMovements.Count(m => m.Sequence == e.GainSequence && m.CardId == e.CardId && m.CardKind == e.PrintedKind && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) == 1) &&
            !s.E<LingkongGainMarkedEvent>().Any() && issuedEntities.All(e => s.V().Hand.Any(c => c.Id == e.CardId && !LfMarked(c))), "The real whole gain freezes its two exact entry invoices before the higher-priority observer runs, while neither entity is marked yet.");
        s.Private(0); s.Cold(); s.Continue(); s.Reach(() => s.Hold("after-loss"));
        s.Need(!s.V().Skills!.Any(k => k.ContentId == LfLing) && s.E<LingkongGainQualifiedEvent>().Single(e => e.Source.OwnerSeat == 0) == issuedBatch &&
            s.E<LingkongGainEntityIssuedEvent>().Where(e => e.BatchId == issuedBatch.BatchId && e.RecipientSeat == 0).OrderBy(e => e.GainSequence).SequenceEqual(issuedEntities) && !s.E<LingkongGainMarkedEvent>().Any(),
            "A real same-batch child removes Lingkong before its own candidate runs, preserving the issued source, hash and original entities without granting marks early.");
        s.Private(0); s.Cold(); s.Continue(); s.Play(); var settled = s.E<LingkongGainMarkedEvent>().Single(e => e.Source.OwnerSeat == 0);
        var settlementDiagnostic = JsonSerializer.Serialize(new
        {
            Settled = settled, Issued = issuedBatch,
            Entities = issuedEntities.Select(e => new { Issued = e, Marked = s.E<LingkongGainEntityMarkedEvent>().Where(m => m.CardId == e.CardId), Hand = s.V().Hand.Where(c => c.Id == e.CardId).Select(c => new { c.Id, c.SkillMarkLabel }) }),
            Resolved = s.E<ProgramBindingResolvedEvent>().Where(e => e.FrameId == settled.FrameId), Lingkong = s.V().Skills!.Where(k => k.ContentId == LfLing)
        });
        s.Need(settled.BatchId == issuedBatch.BatchId && settled.Source == issuedBatch.Source && settled.GameplayHash == issuedBatch.GameplayHash && settled.EntityCount == 2,
            "The settled whole gain must retain its exact issued batch, source, hash and count. " + settlementDiagnostic);
        s.Need(issuedEntities.All(e => s.E<LingkongGainEntityMarkedEvent>().Count(m => m.FrameId == settled.FrameId && m.BatchId == e.BatchId && m.RecipientSeat == e.RecipientSeat && m.CardId == e.CardId && m.PrintedKind == e.PrintedKind && m.GainSequence == e.GainSequence) == 1),
            "Each issued original entity must have one exact marked fact from the same settled frame. " + settlementDiagnostic);
        s.Need(issuedEntities.All(e => s.V().Hand.Any(c => c.Id == e.CardId && LfMarked(c))), "The two issued entities must retain their public membership labels after source loss. " + settlementDiagnostic);
        s.Need(s.E<ProgramBindingResolvedEvent>().Count(e => e.FrameId == settled.FrameId && e.SkillId == settled.Source.SkillId && e.BindingId == settled.Source.BindingId &&
            e.SkillInstanceId == settled.Source.SkillInstanceId && e.OwnerSeat == settled.Source.OwnerSeat && e.Window == SkillProgramTriggerWindow.CardsGained && e.Activated && e.Completed) == 1,
            "The already-issued source-lost frame must complete successfully once after marking both original entities. " + settlementDiagnostic);
        s.Need(!s.V().Skills!.Any(k => k.ContentId == LfLing), "Completing the issued entitlement must not reacquire its lost skill. " + settlementDiagnostic);
        s.Use("grant-ling"); s.Play(); s.Use("draw-two"); s.Play();
        s.Need(s.E<LingkongGainMarkedEvent>().Count(e => e.Source.OwnerSeat == 0) == 1 && issuedEntities.All(e => s.V().Hand.Any(c => c.Id == e.CardId && LfMarked(c))) && s.V().Hand.Count(LfMarked) == 6,
            "Regranting the source cannot issue a second batch in the same actual turn; only the four opening entities and two originally qualified gain entities are marked."); s.Cold();

        s = LfScenario.Start("replacement"); s.Use("draw-two"); s.Reach(() => s.P?.SkillPrompt?.SkillId == LfZen && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        s.Private(0); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); s.Reach(() => s.P?.SkillPrompt?.SkillId == LfZen && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "target"));
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([1]));
        for (var i = 0; i < 2; i++) s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "take"); s.Reach(() => s.Hold("obtained"));
        var replacement = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(f => f.Context.RecipientSeat == 0); var replacementIds = replacement.Replacement!.GainedIds.ToArray();
        var replacementGain = s.E<LingkongGainQualifiedEvent>().Single(e => e.Source.OwnerSeat == 0); var invocation = s.E<LingkongGainInvocationBoundEvent>().Single(e => e.BatchId == replacementGain.BatchId && e.RecipientSeat == 0);
        s.Need(replacement.Context.RequestedCount == 2 && replacementIds.Length == 2 && replacement.Replacement.GainedIds is IList { IsReadOnly: true } && replacementGain.EntityCount == 2 &&
            invocation is { NativeDrawInvocationId: null, EntityCount: 2 } && invocation.NativeDrawAttemptId == replacement.Id &&
            s.E<LingkongGainEntityIssuedEvent>().Where(e => e.BatchId == replacementGain.BatchId && e.RecipientSeat == 0).Select(e => e.CardId).SequenceEqual(replacementIds) &&
            replacementIds.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0) && m.Reason.Value == LfObtain) == 1) &&
            !s.G.CardMovements.Any(m => m.Sequence > replacement.Context.SequenceBefore && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)),
            "A real Draw2 replacement obtains exactly two foreign Hand entities, binds one native attempt with Actual0 and Replacement2, and freezes the whole gain before its obtain child returns.");
        s.Private(0); s.Cold(); s.Continue(); s.Reach(() => s.Hold("compensated") && s.P?.PlayerSeat == 0); s.Private(0); s.Cold(); s.Continue(); s.Play();
        var replacementMarked = s.E<LingkongGainMarkedEvent>().Single(e => e.Source.OwnerSeat == 0); var compensation = s.E<ZenrunCompensationEntityEvent>().Select(e => e.CardId).ToArray();
        s.Need(replacementMarked.BatchId == replacementGain.BatchId && replacementMarked.EntityCount == 2 &&
            s.E<LingkongGainEntityMarkedEvent>().Where(e => e.FrameId == replacementMarked.FrameId).Select(e => e.CardId).SequenceEqual(replacementIds) && replacementIds.All(id => s.V().Hand.Any(c => c.Id == id && LfMarked(c))) &&
            s.E<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == replacement.Id && e.RecipientSeat == 0 && e.RequestedCount == 2 && e.Replaced) == 1 &&
            s.E<ZenrunCompensationReturnedEvent>().Single() is { ActualDrawCount: 2, ReplacementGainedCount: 0 } && compensation.Length == 2 && !compensation.Intersect(replacementIds).Any() &&
            !s.E<LingkongGainEntityMarkedEvent>().Any(e => e.RecipientSeat == 0 && compensation.Contains(e.CardId)) && s.E<ZenrunCompletedEvent>().Count(e => e.Paid && e.Count == 2 && e.Choice == 1) == 1,
            "The original replacement gain marks only its two actual obtained entities once; the victim's distinct real compensation draw and children finish without contaminating that batch."); s.Cold();
    }

    public static void XianshuColorsFreshHealthDrawBoundsAndNeutralEntityCold()
    {
        foreach (var scene in new[] { "red", "black", "equal-red", "equal-black", "cap", "neutral" })
        {
            var s = LfScenario.Start(scene);
            if (scene is "red" or "neutral") { s.Use("hurt-three", targets: [1]); s.Play(); }
            if (scene == "black") { s.Use("hurt-three", targets: [0]); s.Play(); }
            if (scene is "equal-red" or "equal-black") { s.Use("hurt-two", targets: [0]); s.Play(); s.Use("hurt-one", targets: [1]); s.Play(); }
            if (scene == "cap") { s.Use("hurt-ten", targets: [1]); s.Play(); }
            var ownerHp = s.V().Hp; var targetHp = s.V(1).Hp; var card = s.V().Hand.First(LfMarked).Id; s.RejectActivation(card); s.Cold(); s.Xian(card, 1); s.Play();
            var start = s.E<XianshuGiftStartedEvent>().Single(); var count = Math.Min(5, Math.Abs(ownerHp - targetHp));
            s.Need(s.E<XianshuGiftPaidEvent>().Count(e => e.FrameId == start.FrameId && e.CardId == card && e.From == CardLocation.Hand(0) && e.To == CardLocation.Hand(1)) == 1 &&
                s.E<XianshuDrawRequestedEvent>().Single(e => e.FrameId == start.FrameId).RequestedCount == count && s.E<XianshuDrawReturnedEvent>().Single(e => e.FrameId == start.FrameId) is { ReplacementCount: 0 } draw && draw.RequestedCount == count && draw.ActualCount == count &&
                s.G.CardMovements.Count(m => m.Reason.Value == LfDraw && m.To == CardLocation.Hand(0)) == count && s.E<XianshuCompletedEvent>().Count(e => e.FrameId == start.FrameId && e.CardId == card && e.TargetSeat == 1) == 1,
                "One exact marked-Hand gift draws the capped absolute HP difference through its real native request, including a paid Draw0, with once-only completion.");
            var expectedHp = scene is "red" or "equal-red" or "cap" ? targetHp + 1 : scene is "black" or "equal-black" ? targetHp - 1 : targetHp;
            s.Need(s.V(1).Hp == expectedHp && (scene == "neutral" ? start.GiftSuit == Suit.None && s.E<XianshuHealthEvaluatedEvent>().Single().Action.ToString() == "None" :
                scene is "red" or "equal-red" or "cap" ? s.E<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == 1 : s.E<XianshuHpLossAppliedEvent>().Count(e => e.TargetSeat == 1 && e.Amount == 1) == 1),
                "Red and black include equality after the native draw; a genuine colorless marked physical card gives neither HP effect."); s.Cold();
            if (scene == "equal-red") { s.Xian(s.V().Hand.First(LfMarked).Id, 2); s.Play(); s.Need(s.E<XianshuGiftStartedEvent>().Length == 2 && s.E<XianshuCompletedEvent>().Length == 2, "The same real owner Play can pay another distinct marked entity: Xianshu has no invented phase or turn use cap."); s.Cold(); }
        }
    }

    public static void XianshuPaidDrawHealthChildrenSourceRightsAndPrivateInputsCold()
    {
        foreach (var scene in new[] { "paid-loss", "paid-death", "fresh-red", "fresh-black" })
        {
            var s = LfScenario.Start(scene);
            if (scene == "paid-loss") { s.Use("hurt-two", targets: [1]); s.Play(); }
            if (scene == "fresh-red") { s.Use("hurt-two", targets: [0]); s.Play(); s.Use("hurt-two", targets: [1]); s.Play(); }
            var targetBefore = s.V(1).Hp; s.Xian(s.V().Hand.First(LfMarked).Id, 1);
            if (scene.StartsWith("paid-", StringComparison.Ordinal)) { s.Reach(() => s.Hold("gift")); var old = s.P!; s.Private(0); s.RejectPrompt(); s.Cold(); s.Continue(); s.RejectOld(old); }
            if (scene != "paid-death")
            {
                s.Reach(() => s.Hold("draw")); var startAtDraw = s.E<XianshuGiftStartedEvent>().Single();
                var paidFrame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == startAtDraw.FrameId);
                s.Need(paidFrame.LeZhouFeiXianshu is { Stage: LeZhouFeiXianshuStage.DrawChildren, DrawOutcome: { } outcome } && outcome.ActualDrawIds is IList { IsReadOnly: true } && outcome.ReplacementGainedIds is IList { IsReadOnly: true } &&
                    !s.E<XianshuHealthEvaluatedEvent>().Any() && !s.E<XianshuCompletedEvent>().Any(), "Actual native gain observers remain descendants of the paid owning frame with frozen outcome identities before the HP comparison or completion.");
                s.Private(0); s.Cold(); s.Continue();
            }
            if (scene is "paid-loss" or "fresh-black")
            {
                s.Reach(() => s.Hold("health") && s.P?.PlayerSeat == 0 && s.G.ResolutionStack.LastOrDefault() is ProgramSkillFrame
                    { OwnerSeat: 1, WindowContext: { HpChange: { TargetSeat: 1 } } }); var healthStart = s.E<XianshuGiftStartedEvent>().Single();
                s.Need(s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == healthStart.FrameId).LeZhouFeiXianshu is { Stage: LeZhouFeiXianshuStage.HealthChildren } &&
                    !s.E<XianshuCompletedEvent>().Any() && s.V(1).Hp == (scene == "paid-loss" ? targetBefore + 1 : targetBefore - 1), "The recipient's real HP child has applied once but still awaits its living actual-turn owner's published choice before the paid parent completes.");
                s.Private(0); s.Cold(); s.Continue();
            }
            s.Reach(() => s.E<XianshuCompletedEvent>().Length == 1); s.PlayOrDead(); var start = s.E<XianshuGiftStartedEvent>().Single(); var request = s.E<XianshuDrawRequestedEvent>().Single(); var returned = s.E<XianshuDrawReturnedEvent>().Single(); var evaluated = s.E<XianshuHealthEvaluatedEvent>().Single();
            s.Need(s.E<XianshuGiftPaidEvent>().Count(e => e.FrameId == start.FrameId) == 1 && request.FrameId == start.FrameId && returned.FrameId == start.FrameId && evaluated.FrameId == start.FrameId &&
                s.E<ProgramSkillResolvedEvent>().Count(e => e.FrameId == start.FrameId && e.Completed) == 1,
                "Real gift, native draw and HP children drain through the original paid frame once, retaining its exact source and completion.");
            if (scene == "paid-loss") s.Need(s.V().IsAlive && !s.V().Skills!.Any(k => k.ContentId == LfXian) && request is { OwnerHp: 12, TargetHp: 10, RequestedCount: 2 } && returned.ActualCount == 2 && s.V(1).Hp == targetBefore + 1,
                "Source loss in the actual gift child cannot revoke payment; Draw2 samples HP only after that child and the red recovery still completes.");
            if (scene == "paid-death") s.Need(!s.V().IsAlive && request.RequestedCount == 5 && returned is { ActualCount: 0, ReplacementCount: 0 } && s.V(1).Hp == targetBefore - 1 && s.E<XianshuHpLossAppliedEvent>().Length == 1,
                "The dead paid source still requests the native capped draw (actual0), then its numeric HP permits the promised black loss on the living recipient.");
            if (scene == "fresh-red") s.Need(request is { OwnerHp: 11, TargetHp: 10, RequestedCount: 1 } && evaluated is { OwnerHp: 9, TargetHp: 10 } && evaluated.Action.ToString() == "None" && s.V(1).Hp == targetBefore,
                "The completed draw child changes owner HP, so a red comparison that was initially true becomes false before health resolution.");
            if (scene == "fresh-black") s.Need(request is { OwnerHp: 13, TargetHp: 12, RequestedCount: 1 } && evaluated is { OwnerHp: 11, TargetHp: 12 } && s.V(1).Hp == targetBefore - 1,
                "The real draw child makes the initially false black comparison true; the target loses HP only after fresh sampling."); s.Cold();
        }
    }

    private static bool LfMarked(CardSnapshot c) => c.SkillMarkLabel?.Contains("箜篌", StringComparison.Ordinal) == true;
    private sealed class LfScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Hold(string option) => P?.SkillPrompt?.SkillId == LfWatch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option);
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, LfDriver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Xian(int id, int target) => Accept(new UseProgramSkillCommand(0, LfXian, "marked-hand-gift", [id], [target], G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Advance() => Accept(new AdvanceOneStepCommand(G.Revision));
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "A human command chooses only a published canonical input."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Private(int seat) => Need(P?.PlayerSeat == seat && Enumerable.Range(0, 4).Where(i => i != seat).All(i => G.CreateSnapshot(i).PendingDecision is null), "Only the real chooser receives its private choices and card identities.");
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void PlayOrDead() => Reach(() => !V().IsAlive || P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 190; i++) { if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected terminal before boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Advance(); else if (p.Kind == DecisionKind.PlayCard) End(); else if (p.SkillPrompt?.SkillId == LfWatch && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target")) Answer(c => c.Targets.SequenceEqual([0])); else if (p.SkillPrompt?.SkillId == LfWatch) Continue();
                else if (p.SkillPrompt?.SkillId == LfInsert && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target")) Answer(c => c.Targets.SequenceEqual([0]));
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind == DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die"); else throw new InvalidOperationException("Unexpected native boundary " + line + " " + Diagnostic()); }
            throw new InvalidOperationException("Missing bounded boundary " + line + " " + Diagnostic());
        }
        public void Need(bool valid, string message) { if (!valid) throw new InvalidOperationException(message + " " + Diagnostic()); }
        public void Accept(GameCommand command) { try { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, "Rejected " + command.GetType().Name + " " + JsonSerializer.Serialize(r.Error)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " command=" + command.GetType().Name + " " + e.Message + " " + Diagnostic(), e); } }
        public string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves exact global marks, original actual-turn gain history, paid source and every native child cursor."); }
        public void RejectActivation(int id) { var before = State(); foreach (var c in new[] { new UseProgramSkillCommand(0, LfXian, "marked-hand-gift", [id], [0], G.Revision, P!.PromptId), new UseProgramSkillCommand(0, LfXian, "marked-hand-gift", [int.MaxValue], [1], G.Revision, P.PromptId), new UseProgramSkillCommand(0, LfXian, "marked-hand-gift", [id], [1], G.Revision, P.PromptId) { SkillOwnerSeat = 1 } }) Need(!G.Submit(c).Accepted && before == State(), "Forged recipient, physical entity or skill owner cannot spend a real marked Hand card."); }
        public void RejectPrompt() { var p = P!; var before = State(); Need(!G.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && State() == before && !G.Submit(new AnswerPromptCommand(0, p.PromptId, new("unpublished-xianshu-child"), G.Revision)).Accepted && State() == before, "Foreign or unpublished child inputs cannot mutate the paid parent."); }
        public void RejectOld(PendingDecision p) { var before = State(); Need(!G.Submit(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && before == State(), "A retired child prompt cannot repeat its payment or HP effect."); }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P is not { } p ? null : new { p.Kind, p.PlayerSeat, p.PromptId, Count = p.Choices.Count, Choices = p.Choices.Take(5) }, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).IsAlive, V(i).HandCount }), Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step }), Marked = E<LingkongGainMarkedEvent>(), Gifts = E<XianshuGiftStartedEvent>(), Requests = E<XianshuDrawRequestedEvent>(), Draws = E<XianshuDrawReturnedEvent>(), Health = E<XianshuHealthEvaluatedEvent>() });
        public static LfScenario Start(string scene)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new LfFixture(scene)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "paid-death" ? Role.Renegade : Role.Lord, ModeId = LfMode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new LfScenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); s.Accept(new SelectGeneralCommand(0, LfOwner, g.Revision, s.P!.PromptId)); s.Play(); return s;
        }
    }

    private sealed class LfFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-le-zhou-fei", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            string Read(string suffix, string bundle = "le-zhou-fei") { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing actual native content " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var production = SkillProgramCatalog.Load(Read("rules"), Read("presentation")); foreach (var id in new[] { LfLing, LfXian }) b.AddSkill(new(id, production.Presentations[id].Name, production.Presentations[id].Description) { Program = production.Programs[id], ProgramPresentation = production.Presentations[id] });
            if (scene == "replacement") { var zen = SkillProgramCatalog.Load(Read("rules", "ol-sun-hong"), Read("presentation", "ol-sun-hong")); b.AddSkill(new(LfZen, zen.Presentations[LfZen].Name, zen.Presentations[LfZen].Description) { Program = zen.Programs[LfZen], ProgramPresentation = zen.Presentations[LfZen] }); }
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonNode A(string id, string effects, int cards = 0, int targets = 0) => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," + (cards == 0 ? "" : "\"sourceZones\":[\"hand\"],") + "\"effects\":[" + effects + "]}");
            static string Choice(string id, string bind = "seen") => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"" + bind + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var driver = new JsonObject { ["id"] = LfDriver, ["revision"] = 1, ["activations"] = new JsonArray(A("draw-two", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":2}"), A("draw-four", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":4}"), A("give", "{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}", 1, 1),
                A("take", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1),
                A("hurt-one", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":1}", targets: 1), A("hurt-two", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":2}", targets: 1), A("hurt-three", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":3}", targets: 1), A("hurt-ten", "{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":10}", targets: 1),
                A("lose-ling", "{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"le:lingkong\"],\"sourceBind\":\"standard:none\"}"), A("grant-ling", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"le:lingkong\"]}")) };
            if (scene == "timing") driver["triggers"] = N("[{\"id\":\"real-own-draw-phase-skill\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}]");
            var defs = new JsonArray(driver, N($$"""{"id":"{{LfQuiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""));
            var own = new List<string> { LfDriver, scene == "paid-loss" ? LfConditional : LfXian }; if (scene != "late") own.Add(scene == "gain-issued-loss" ? LfConditional : LfLing);
            if (scene == "replacement") own.Add(LfZen);
            if (scene == "paid-loss") defs.Add(N($$"""{"id":"{{LfConditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-xianshu","skillId":"{{LfXian}}","minimumHp":13}]}"""));
            if (scene == "gain-issued-loss") defs.Add(N($$"""{"id":"{{LfConditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-lingkong","skillId":"{{LfLing}}","minimumHp":13}]}"""));
            if (scene == "timing") { own.Add(LfInsert); defs.Add(N($$"""{"id":"{{LfInsert}}","revision":1,"triggers":[{"id":"new-play","window":"playEnding","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"insertPhase","target":"selectedTarget","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}""")); }
            var watch = new JsonArray();
            if (scene == "replacement")
            {
                watch.Add(N("{\"id\":\"actual-replacement-gain\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + LfObtain + "\"],\"optional\":false,\"effects\":[" + Choice("obtained") + "]}"));
                watch.Add(N("{\"id\":\"actual-compensation-gain\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + LfCompensation + "\"],\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"chooseOption\",\"target\":\"selectedTarget\",\"resultBind\":\"seen\",\"options\":[{\"id\":\"compensated\"}]}]}"));
            }
            if (scene == "gain-issued-loss") watch.Add(N("{\"id\":\"qualified-gain-loses-source\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"priority\":1000,\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Choice("before-loss") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}," + Choice("after-loss", "seen-after-loss") + "]}"));
            if (scene == "timing") watch.Add(N("{\"id\":\"real-foreign-draw-phase-gain\",\"window\":\"drawPhaseStarting\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLivingHighestHand\"},{\"op\":\"chooseOption\",\"target\":\"selectedTarget\",\"resultBind\":\"seen\",\"options\":[{\"id\":\"foreign-draw\"}]},{\"op\":\"draw\",\"target\":\"selectedTarget\",\"amount\":2}]}"));
            if (scene is "paid-loss" or "paid-death") watch.Add(N("{\"id\":\"actual-gift-child\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + LfGift + "\"],\"optional\":false,\"effects\":[" + Choice("gift") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "paid-death" ? 20 : 1) + "}]}"));
            if (scene is "paid-loss" or "fresh-red" or "fresh-black") watch.Add(N("{\"id\":\"actual-draw-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + LfDraw + "\"],\"optional\":false,\"effects\":[" + Choice("draw") + (scene == "paid-loss" ? "" : ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}") + "]}"));
            if (scene is "paid-loss" or "paid-death" or "fresh-red" or "fresh-black") watch.Add(N("{\"id\":\"actual-health-child\",\"window\":\"afterHealthChanged\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + (scene == "paid-death" ? Choice("health") : "{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"chooseOption\",\"target\":\"selectedTarget\",\"resultBind\":\"seen\",\"options\":[{\"id\":\"health\"}]}") + "]}"));
            if (watch.Count > 0) { defs.Add(new JsonObject { ["id"] = LfWatch, ["revision"] = 1, ["triggers"] = watch }); if (scene != "timing") own.Add(LfWatch); }
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实乐周妃原生流程夹具", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var c = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })); foreach (var (id, p) in c.Programs) b.AddSkill(new(id, id, "真实乐周妃夹具") { Program = p, ProgramPresentation = c.Presentations[id] });
            const string pick = "fixture:lzf-pick"; b.AddSkill(new(pick, "本人", "固定真人候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            var small = scene is "initial" or "recipient" or "inherited"; var hp = small ? 3 : 12; b.AddGeneral(new(LfOwner, "真实乐周妃本人", "supporter", pick, "wu", hp, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:lzf-peer-" + i).ToArray(); for (var i = 1; i <= 3; i++) { var pp = "fixture:lzf-peer-pick-" + i; b.AddSkill(new(pp, "其他角色", "固定公开候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) }); var skills = new List<string> { LfQuiet }; if (i == 1 && scene == "inherited") skills.Add(LfLing); if (i == 1 && (scene is "paid-loss" or "paid-death" or "fresh-red" or "fresh-black" or "timing" or "replacement")) skills.Add(LfWatch); b.AddGeneral(new(peers[i - 1], "真实其他角色", "supporter", pp, "shu", hp, skills)); }
            var suit = scene is "black" or "equal-black" or "paid-death" or "fresh-black" ? Suit.Spade : scene == "neutral" ? Suit.None : Suit.Heart;
            b.AddDeck(new("fixture:lzf-deck", "真实同质箜篌实体", scene is "timing" or "late" ? 0 : 4, scene == "timing" ? 2 : 0, []) { PhysicalCards = Enumerable.Range(0, scene == "empty" ? 16 : 64).Select(_ => new ContentDeckPhysicalCard("standard:crossbow", suit, 7)).ToArray() });
            var roles = scene == "paid-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(LfMode, "真实乐周妃原生边界", 4, 4, roles, "fixture:lzf-deck", GeneralCandidateCount: 4, GeneralPoolIds: [LfOwner, .. peers]));
        }
    }
}
