using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class OrdinaryGuYongChecks
{
    private const string Owner = "fixture:ordinary-gy-owner", Mode = "identity:classic-ordinary-gu-yong";
    private const string Driver = "fixture:ordinary-gy-driver", Quiet = "fixture:ordinary-gy-quiet";
    private const string Watch = "fixture:ordinary-gy-watch", Phase = "fixture:ordinary-gy-phase", Conditional = "fixture:ordinary-gy-conditional";
    private const string Regrant = "fixture:ordinary-gy-regrant", Zenrun = "ol:zenrun";
    private const string Shenxing = "ol:shenxing", Bingyi = "ol:bingyi", DrawReason = "skill-program.gu-yong.bingyi.draw";
    private const string TakeReason = "skill-program.sun-hong.zenrun.obtain", CompensationReason = "skill-program.sun-hong.zenrun.compensation";

    public static void ShenxingExactMixedCostsAndNativeChildrenCold()
    {
        var s = Scenario.Start("lion"); s.Use("stock"); s.Play();
        var lion = s.V().Hand[0].Id; s.PlayCard(lion, []); s.Play();
        s.Use("wound"); s.Play();
        var handCost = s.V().Hand[0].Id; var before = s.G.CardMovements.Last().Sequence;
        var hp = s.V().Hp; var hand = s.V().HandCount;
        var state = s.State();
        s.Need(!s.G.Submit(new UseProgramSkillCommand(0, Shenxing, "discard-two-draw-one", [handCost], [], s.G.Revision, s.P!.PromptId)).Accepted && state == s.State() &&
            !s.G.Submit(new UseProgramSkillCommand(0, Shenxing, "discard-two-draw-one", [handCost, handCost], [], s.G.Revision, s.P!.PromptId)).Accepted && state == s.State(),
            "Shenxing rejects a short or repeated physical cost without paying or changing its native prompt.");
        s.Shenxing([handCost, lion]);
        s.Reach(() => s.Hold("recovery"));
        var parent = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Shenxing);
        s.Need(parent.InstructionIndex == 1 && s.V().Hp == hp + 1 && s.V().HandCount == hand - 1 &&
            new[] { handCost, lion }.All(id => s.G.CardMovements.Count(m => m.Sequence > before && m.CardId == id && m.To == CardLocation.DiscardPile) == 1) &&
            s.G.CardMovements.Single(m => m.Sequence > before && m.CardId == lion && m.To == CardLocation.DiscardPile).From == CardLocation.Equipment(0) &&
            s.G.CardMovements.Single(m => m.Sequence > before && m.CardId == handCost && m.To == CardLocation.DiscardPile).From == CardLocation.Hand(0),
            "A real mixed HE payment precedes Silver Lion's native recovery child and the original Shenxing draw.");
        s.Private(0); s.Cold(); s.Continue(); s.Offer();
        s.Need(s.G.ResolutionStack.Any(f => f.Id == parent.Id) && s.Draws(before).Length == 0,
            "The paid two-card parent remains suspended before both its draw and the accepted Bingyi benefit.");
        var id = s.BeginBingyi(); s.Confirm(); s.Play();
        s.Need(s.E<OrdinaryBingyiDrawReturnedEvent>().Single(e => e.FrameId == id) is { RecipientSeat: 0, ActualCount: 1, ReplacementCount: 0 } &&
            s.E<OrdinaryBingyiCompletedEvent>().Count(e => e.FrameId == id && e.RecipientCount == 1) == 1 &&
            s.E<ProgramSkillResolvedEvent>().Count(e => e.FrameId == parent.Id && e.Completed) == 1 &&
            s.Draws(before).Length == 2 && s.V().HandCount == hand + 1,
            "Zero selected others still pays Bingyi's mandatory self Draw1, then returns to exactly one original Shenxing Draw1.");
        var shown = s.E<OrdinaryBingyiShownEvent>().Length; before = s.G.CardMovements.Last().Sequence;
        s.Shenxing(s.V().Hand.Take(2).Select(c => c.Id).ToArray()); s.Play();
        s.Need(s.E<OrdinaryBingyiShownEvent>().Length == shown && s.Draws(before).Length == 1,
            "Shenxing remains repeatable while the accepted Bingyi quota is already spent in this exact Play phase.");
        s.Cold();
    }

    public static void BingyiActualPhaseQuotaForeignDiscardAndRestorationCold()
    {
        var s = Scenario.Start("six", play: false);
        for (var i = 0; i < 6; i++)
        {
            s.Reach(() => s.Action("select-owned-cards")); s.Answer(c => c.Cards.Count == 1); s.Offer();
            var id = s.BeginBingyi(); var receipt = s.Root(id).OrdinaryBingyi!;
            s.Need(receipt.Phase.TurnNumber == 1 && receipt.Phase.ActualTurnOwnerSeat == 0 && receipt.Phase.ActorSeat == 0 &&
                s.G.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w => w.Batch.Id == receipt.BatchId && w.Batch.DiscardRecoveryPhase == receipt.Phase),
                "A genuine discard batch freezes the current old actual phase, owner and movement mother.");
            if (i is 0 or 5) s.Cold();
            s.Confirm();
        }
        s.Reach(() => s.E<TurnEndedEvent>().Any(e => e.ActorSeat == 0));
        var phases = s.E<OrdinaryBingyiShownEvent>().Select(e => e.Phase).ToArray();
        s.Need(phases.Select(p => p.Kind).SequenceEqual(new[] { ActualDiscardRecoveryPhaseKind.Preparation, ActualDiscardRecoveryPhaseKind.Judgment,
                ActualDiscardRecoveryPhaseKind.Draw, ActualDiscardRecoveryPhaseKind.Play, ActualDiscardRecoveryPhaseKind.Discard, ActualDiscardRecoveryPhaseKind.Ending }) &&
            phases.Select(p => p.Token).Distinct().Count() == 6 && s.E<OrdinaryBingyiDrawReturnedEvent>().Length == 6,
            "Six actual phases each admit one accepted reveal and mandatory self draw, without a turn-wide or Play-only quota.");

        s = Scenario.Start("insert", play: false);
        s.Reach(() => s.Action("select-owned-cards")); s.Answer(c => c.Cards.Count == 1); s.Offer();
        var first = s.BeginBingyi(); var prep = s.Root(first).OrdinaryBingyi!.Phase; s.Confirm(); s.Play();
        var actualTurn = s.G.State.TurnNumber;
        s.Discard(); s.Offer(); var inserted = s.BeginBingyi(); var insertedPhase = s.Root(inserted).OrdinaryBingyi!.Phase;
        s.Need(insertedPhase.Kind == ActualDiscardRecoveryPhaseKind.Play && insertedPhase.Token != prep.Token && actualTurn == prep.TurnNumber,
            "A real inserted Play owns a distinct phase token inside the same actual turn.");
        s.Confirm(); s.Play(); s.Cold(); s.End();
        s.Reach(() => s.Action("select-owned-cards")); var shows = s.E<OrdinaryBingyiShownEvent>().Length;
        s.Answer(c => c.Cards.Count == 1); s.Play();
        var schedule = s.E<ProgramPhaseScheduledEvent>().Single(e => e.SkillId == Phase && e.Started);
        s.Need(s.E<ActualDiscardRecoveryPhaseRestoredEvent>().Any(e => e.Phase == prep) &&
            s.E<ProgramPhaseScheduledEvent>().Any(e => e.FrameId == schedule.FrameId && !e.Started) &&
            s.G.State.TurnNumber == actualTurn && s.E<OrdinaryBingyiShownEvent>().Length == shows && !s.Optional(),
            "Restoring the original Preparation token does not replenish an already accepted quota or invent an active scheduled parent.");
        s.Discard(); s.Offer(); var normal = s.BeginBingyi();
        s.Need(s.Root(normal).OrdinaryBingyi!.Phase is { Kind: ActualDiscardRecoveryPhaseKind.Play } fresh && fresh.Token != insertedPhase.Token,
            "The later ordinary Play has a genuinely fresh phase allowance.");
        s.Confirm(); s.Play(); s.Cold();

        s = Scenario.Start("foreign"); s.Use("stock"); s.Play();
        var foreignCard = s.V().Hand[0].Id; s.Use("give", [foreignCard], [1]); s.Play(); s.End(); s.Offer();
        var last = s.G.CardMovements.Last(m => m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.DismantlementFinished);
        var entry = s.G.CardMovements.Last(m => m.CardId == last.CardId && m.Sequence < last.Sequence);
        var foreignId = s.BeginBingyi(); var foreignPhase = s.Root(foreignId).OrdinaryBingyi!.Phase;
        s.Need(entry.From == CardLocation.Hand(0) && entry.To == CardLocation.Processing && last.From == CardLocation.Processing &&
            foreignPhase is { ActualTurnOwnerSeat: 1, ActorSeat: 1, Kind: ActualDiscardRecoveryPhaseKind.Play },
            "Native Dismantlement's Processing cleanup retains the original owned Hand discard and the real foreign Play phase.");
        s.Toggle(1); s.Private(0); s.Cold(); s.Confirm();
        s.Reach(() => s.E<TurnEndedEvent>().Any(e => e.ActorSeat == 1));
        s.Need(s.E<OrdinaryBingyiShownEvent>().Count(e => e.Phase == foreignPhase) == 1 &&
            s.E<OrdinaryBingyiDrawReturnedEvent>().Where(e => e.FrameId == foreignId).Select(e => e.RecipientSeat).SequenceEqual([0, 1]) &&
            s.G.CardMovements.Count(m => m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.DismantlementFinished) >= 2,
            "A second real foreign discard in the same phase cannot replenish the quota; both accepted live recipients draw in native order.");
        s.Cold();
    }

    public static void BingyiFrozenHandColorsSelfAndDistinctRecipientsCold()
    {
        var s = Scenario.Start("frozen"); s.Use("stock-three"); s.Play();
        s.Shenxing(s.V().Hand.Take(2).Select(c => c.Id).ToArray()); s.Offer();
        var original = s.V().Hand.Select(c => c.Id).ToArray(); var id = s.BeginBingyi(); var r = s.Root(id).OrdinaryBingyi!;
        s.Need(original.Length == 1 && r is { Paid: true, UniformColor: true, Color: false } &&
            r.ShownCards.Select(c => c.CardId).SequenceEqual(original) && r.ShownCards is System.Collections.IList { IsReadOnly: true } &&
            r.OtherRecipients is System.Collections.IList { IsReadOnly: true } &&
            s.E<ProgramCardsRevealedEvent>().Single(e => e.FrameId == id).Cards.Select(c => c.Id).SequenceEqual(original),
            "Acceptance publicly shows the exact current Hand and freezes the original one-card black limit and immutable sets.");
        s.Private(0); s.Reject(); var previous = s.P!; s.Toggle(1); s.RejectOld(previous);
        s.Need(!s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "toggle" && c.Parameters.GetValueOrDefault("peer-seat") is "2" or "3"),
            "Only distinct others up to the frozen Hand count can be selected; the owner is not a selectable extra.");
        s.Cold(); s.Confirm(); s.Reach(() => s.Hold("gained"));
        s.Need(s.Root(id).OrdinaryBingyi is { Stage: OrdinaryBingyiStage.DrawChildren, Cursor: 0, DrawOutcome.ActualDrawIds.Count: 1 } paid &&
            paid.Recipients.SequenceEqual([0, 1]) && paid.ShownCards.Count == 1 &&
            !s.E<OrdinaryBingyiDrawReturnedEvent>().Any(e => e.FrameId == id && e.RecipientSeat == 1),
            "The owner's actual Draw1 and its gain child finish before a later recipient or the original Shenxing draw.");
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<OrdinaryBingyiDrawReturnedEvent>().Where(e => e.FrameId == id).Select(e => e.RecipientSeat).SequenceEqual([0, 1]) &&
            s.E<OrdinaryBingyiRecipientsCommittedEvent>().Single(e => e.FrameId == id).Count == 2 &&
            s.V().HandCount == 6 && s.V(1).HandCount == 1,
            "An intervening real Draw3 changes the live Hand without increasing frozen X or the already committed recipient queue.");

        s = Scenario.Start("empty"); s.Use("stock-three"); s.Play();
        for (var left = 2; left >= 0; left--)
        {
            s.Discard();
            if (left > 0) { s.Offer(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); }
            s.Play();
            s.Need(s.V().HandCount == left && !s.E<OrdinaryBingyiShownEvent>().Any(),
                "Skipping a real opportunity is unspent, and an actual empty Hand cannot publish a show or benefit.");
        }
        s.Use("stock-three"); s.Play(); s.Discard(); s.Offer(); id = s.BeginBingyi(); s.Confirm(); s.Play();
        s.Need(s.E<OrdinaryBingyiShownEvent>().Length == 1 && s.E<OrdinaryBingyiDrawReturnedEvent>().Single(e => e.FrameId == id).ActualCount == 1,
            "Declining earlier nonempty opportunities does not prevent a later same-phase accepted show.");
        s.Cold();

        foreach (var scene in new[] { "mixed", "hongyan" })
        {
            s = Scenario.Start(scene); s.Use("stock-all"); s.Play(); s.Discard(); s.Offer();
            var raw = s.V().Hand.ToArray(); var shownBefore = s.E<OrdinaryBingyiShownEvent>().Length;
            s.Activate();
            if (scene == "mixed")
            {
                s.Play(); var show = s.E<OrdinaryBingyiShownEvent>().Single();
                s.Need(!show.UniformColor && show.Color is null && show.HandCount == raw.Length &&
                    !s.E<OrdinaryBingyiDrawReturnedEvent>().Any() && s.E<OrdinaryBingyiCompletedEvent>().Single().RecipientCount == 0,
                    "A mixed Hand may be shown and consumes the true phase opportunity without any self draw or recipient benefit.");
                s.Discard(); s.Play();
                s.Need(s.E<OrdinaryBingyiShownEvent>().Length == shownBefore + 1 && !s.Optional(), "A later same-phase discard cannot retry a paid mixed-color reveal.");
            }
            else
            {
                s.Reach(() => s.Branch("confirm")); id = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OrdinaryBingyi is not null).Id;
                r = s.Root(id).OrdinaryBingyi!;
                s.Need(raw.Any(c => c.Suit == Suit.Spade) && raw.Any(c => c.Suit == Suit.Heart) && r is { UniformColor: true, Color: true } &&
                    r.ShownCards.All(c => c.EffectiveSuit == Suit.Heart) &&
                    s.E<ProgramCardsRevealedEvent>().Single(e => e.FrameId == id).Cards.Select(c => (c.Id, c.Suit)).SequenceEqual(raw.Select(c => (c.Id, c.Suit))),
                    "Real Hongyan makes Spade and Heart Hand entities uniformly red while public printed faces and exact IDs remain unchanged.");
                s.Cold(); s.Confirm(); s.Play();
                s.Need(s.E<OrdinaryBingyiDrawReturnedEvent>().Single(e => e.FrameId == id).ActualCount == 1, "Zero selected others still gives the owner its real draw with effective-color qualification.");
            }
            s.Cold();
        }
    }

    public static void BingyiNativeOriginNegativesAndPaidSourceLossDeathCold()
    {
        foreach (var scene in new[] { "use", "played", "recast", "replace", "ox" })
        {
            var s = Scenario.Start(scene); s.Use("stock"); s.Play();
            var before = s.G.CardMovements.Last().Sequence;
            if (scene == "use") { var card = s.V().Hand[0].Id; s.PlayCard(card, [1]); s.Play(); s.Need(s.E<CardUseFinishedEvent>().Any(e => e.CardId == card), "A genuine paid native Slash completes rather than a fixture discard."); }
            else if (scene == "played")
            {
                s.Use("duel", targets: [1]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
                var card = s.P!.Choices.First(c => c.Cards.Count == 1).Cards.Single(); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([card])); s.Play();
                s.Need(s.E<CardRespondedEvent>().Any(e => e.ResponderSeat == 0 && e.CardId == card) && s.G.CardMovements.Any(m => m.Sequence > before && m.CardId == card && m.To == CardLocation.DiscardPile),
                    "A real Duel response pays its original Slash as Played, not as a discard.");
            }
            else if (scene == "recast")
            {
                var a = s.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Recast); var card = a.CardId!.Value;
                s.Accept(new RecastCardCommand(0, card, s.G.Revision, s.P!.PromptId) { ConversionSource = a.ConversionSource }); s.Play();
                s.Need(s.E<CardRecastEvent>().Any(e => e.CardId == card) && s.G.CardMovements.Any(m => m.Sequence > before && m.CardId == card && m.Reason == CardMoveReasons.RecastDiscard), "A real native recast keeps its separate semantic reason.");
            }
            else
            {
                var first = s.V().Hand[0].Id; s.PlayCard(first, []); s.Play(); int? grain = null;
                if (scene == "ox") { grain = s.V().Hand[0].Id; s.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain.Value], [], s.G.Revision, s.P!.PromptId)); s.Play(); }
                var second = s.V().Hand[0].Id; s.PlayCard(second, []);
                if (scene == "ox") { s.Reach(() => s.Hold("spill")); s.Cold(); s.Continue(); }
                s.Play();
                s.Need(s.G.CardMovements.Any(m => m.CardId == first && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile) &&
                    (grain is null || s.G.CardMovements.Count(m => m.CardId == grain && m.From == CardLocation.WoodenOxGrain(0) && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1),
                    "Actual equipment replacement and attached grain disposal are real payments with their native cleanup reasons.");
            }
            s.Need(!s.E<OrdinaryBingyiShownEvent>().Any() && !s.Optional(), "Use, pure Played, recast, equipment replacement and Ox spill cannot consume or create an owned discard opportunity.");
            s.Discard(); s.Offer(); var id = s.BeginBingyi(); s.Confirm(); s.Play();
            s.Need(s.E<OrdinaryBingyiShownEvent>().Length == 1 && s.E<OrdinaryBingyiCompletedEvent>().Single().FrameId == id,
                "A subsequent real same-phase Hand discard proves each adjacent negative left the phase quota unspent.");
            s.Cold();
        }
        foreach (var scene in new[] { "paid-loss", "paid-death" })
        {
            var s = Scenario.Start(scene); s.Use("stock"); s.Play(); s.Discard(); s.Offer();
            var id = s.BeginBingyi(); var source = s.Root(id).OrdinaryBingyi!.Source; s.Toggle(1); s.Confirm(); s.Reach(() => s.Hold("paid"));
            s.Need(s.Root(id).OrdinaryBingyi is { Paid: true, Stage: OrdinaryBingyiStage.DrawChildren, Cursor: 0 } r && r.Source == source &&
                r.Recipients.SequenceEqual([0, 1]) && s.E<OrdinaryBingyiDrawReturnedEvent>().Count(e => e.FrameId == id) == 1,
                "The owner's real first draw pauses under a paid frozen source and committed live-recipient queue.");
            s.Private(0); s.Cold(); s.Continue(); s.Reach(() => !s.G.ResolutionStack.Any(f => f.Id == id));
            s.Need((scene == "paid-loss" ? s.V().IsAlive && !s.V().Skills!.Any(k => k.ContentId == Bingyi) : !s.V().IsAlive) &&
                s.E<OrdinaryBingyiDrawReturnedEvent>().Where(e => e.FrameId == id).Select(e => (e.RecipientSeat, e.ActualCount)).SequenceEqual([(0, 1), (1, 1)]) &&
                s.E<OrdinaryBingyiShownEvent>().Count(e => e.FrameId == id && e.Source == source) == 1 &&
                s.E<OrdinaryBingyiCompletedEvent>().Count(e => e.FrameId == id && e.RecipientCount == 2) == 1 && s.V(1).HandCount == 1,
                "Paid source loss or actual owner death drains the real child and preserves the chosen living recipient, without a new reveal, queue or duplicate draw.");
            s.Cold();
            if (scene == "paid-loss")
            {
                var phase = s.E<OrdinaryBingyiShownEvent>().Single(e => e.FrameId == id).Phase;
                s.Play(); s.Use("sever-source"); s.Play();
                s.Need(!s.V().Skills!.Any(k => k.ContentId == Conditional || k.ContentId == Bingyi) && s.V().Skills!.Any(k => k.ContentId == Regrant) &&
                    s.E<ProgramOwnerSkillsReplacedEvent>().Any(e => e.OwnerSeat == 0 && e.LostSkillIds.SequenceEqual([Conditional]) && e.GrantedSkillId == Regrant),
                    "The actual terminal replacement removes the old HP-grant capability while the lost leaf is absent.");
                var hpBefore = s.V().Hp; var conditionalEvents = s.E<HpConditionalSkillGrantChangedEvent>().Length;
                s.Accept(new UseProgramSkillCommand(0, Regrant, "recover-and-regrant", [], [], s.G.Revision, s.P!.PromptId)); s.Play();
                s.Need(s.V().Hp == hpBefore + 1 && s.V().Skills!.Any(k => k.ContentId == Bingyi) &&
                    s.E<SkillsAcquiredEvent>().Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Regrant && e.SkillIds.Contains(Bingyi)) == 1 &&
                    !s.E<HpConditionalSkillGrantChangedEvent>().Skip(conditionalEvents).Any(e => e.SkillId == Bingyi && e.Added),
                    "Native recovery followed by a real independent acquisition creates a new leaf source, rather than restoring the HP cache's remembered instance.");
                var before = s.G.CardMovements.Last().Sequence; s.Cold(); s.Discard(); s.Play();
                s.Need(!s.Optional() && s.E<OrdinaryBingyiShownEvent>().Length == 1 &&
                    s.G.CardMovements.Count(m => m.Sequence > before && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1 &&
                    s.E<ActualDiscardRecoveryPhaseStartedEvent>().Last().Phase == phase,
                    "A new actual leaf source cannot refresh the owner's already-paid skill quota in the same genuine Play phase.");
                s.End(); s.Reach(() => s.Action("select-owned-cards")); s.Answer(c => c.Cards.Count == 1); s.Offer();
                var nextId = s.BeginBingyi(); var next = s.Root(nextId).OrdinaryBingyi!;
                s.Need(next.Phase.Kind == ActualDiscardRecoveryPhaseKind.Ending && next.Phase.TurnNumber == phase.TurnNumber && next.Phase.Token != phase.Token &&
                    next.Source.SkillInstanceId != source.SkillInstanceId && next.Source.OwnerSeat == source.OwnerSeat && next.Source.SkillId == source.SkillId,
                    "The next genuine phase exposes the newly acquired instance and admits it once, proving the previous phase blocked the new source itself.");
                s.Cold(); s.Confirm(); s.Reach(() => s.E<OrdinaryBingyiCompletedEvent>().Any(e => e.FrameId == nextId));
                s.Need(s.E<OrdinaryBingyiDrawReturnedEvent>().Single(e => e.FrameId == nextId) is { RecipientSeat: 0, ActualCount: 1 },
                    "The new phase's independent self benefit still returns through its own native draw.");
                s.Cold();
            }
        }
        BingyiReplacementReturnsToFrozenOtherRecipient();
    }

    private static void BingyiReplacementReturnsToFrozenOtherRecipient()
    {
        var s = Scenario.Start("replacement"); s.Use("stock"); s.Play();
        var original = s.V().Hand[0].Id; s.Use("give", [original], [1]); s.Play();
        s.Discard(); s.Offer(); var id = s.BeginBingyi(); s.Toggle(2); s.Confirm();
        s.Reach(() => s.Optional(Zenrun));
        var attempt = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(a => a.Context.ParentFrameId == id);
        s.Need(attempt.Return.Kind == NativeDrawAttemptReturnKind.ProgramOperation && attempt.Context is { RecipientSeat: 0, RequestedCount: 1 } &&
            attempt.Context.Reason.Value == DrawReason && s.Root(id).OrdinaryBingyi is { Stage: OrdinaryBingyiStage.Drawing, Cursor: 0, DrawOutcome: null } &&
            !s.E<OrdinaryBingyiDrawReturnedEvent>().Any(e => e.FrameId == id),
            "Production Zenrun is offered on the exact first Bingyi recipient's real Draw1 request before any result or next recipient.");
        s.Private(0); s.Cold(); s.Activate();
        s.Reach(() => s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Zenrun?.Stage == ZenrunStage.Target));
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([1]));
        s.Need(s.P is { IsPrivate: true, PlayerSeat: 0 } p &&
            p.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).All(c => c.Cards.Count == 0 && c.Parameters.ContainsKey("slot-index")) &&
            s.G.CreateSnapshot(0).Players[1].Hand.Count == 0,
            "The replacement exposes only the victim's actual opaque Hand slot, not its private face.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick" && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
        var zenrun = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Zenrun is not null);
        s.Need(zenrun.Zenrun!.Selected.Select(c => c.CardId).SequenceEqual([original]), "The real private slot commits the exact previously gifted physical entity.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "take"); s.Reach(() => s.Hold("obtained"));
        s.Need(s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(a => a.Id == attempt.Id).Replacement is { } replacement && replacement.GainedIds.SequenceEqual([original]) &&
            s.G.CardMovements.Count(m => m.CardId == original && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0) && m.Reason.Value == TakeReason) == 1 &&
            s.Root(id).NativeDraw is { AttemptReturned: false } && !s.E<OrdinaryBingyiDrawReturnedEvent>().Any(e => e.FrameId == id),
            "The actual HE obtain and its genuine gain child finish before the original native request reports any draw outcome.");
        s.Private(0); s.Cold(); s.Continue();
        s.Reach(() => s.G.CreateSnapshot(1).PendingDecision is { PlayerSeat: 1 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "draw"));
        s.Private(1); s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision));
        s.Reach(() => s.Hold("compensated"));
        s.Need(s.P!.PlayerSeat == 1 && s.Root(id).NativeDraw is { AttemptReturned: false } &&
            s.E<ZenrunCompensationReturnedEvent>().Single(e => e.FrameId == zenrun.Id) is { ActualDrawCount: 1, ReplacementGainedCount: 0 } &&
            !s.E<OrdinaryBingyiDrawReturnedEvent>().Any(e => e.FrameId == id),
            "The victim's actual compensation Draw1 and native gain child remain inside the original pending attempt, before Bingyi can draw for another person.");
        s.Private(1); s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision));
        s.Reach(() => s.Hold("other-draw"));
        var ownReturn = s.E<OrdinaryBingyiDrawReturnedEvent>().Single(e => e.FrameId == id && e.DrawIndex == 0);
        s.Need(s.P!.PlayerSeat == 2 && ownReturn is { RecipientSeat: 0, ActualCount: 0, ReplacementCount: 1 } && ownReturn.AttemptId == attempt.Id &&
            s.Root(id).OrdinaryBingyi is { Stage: OrdinaryBingyiStage.DrawChildren, Cursor: 1, DrawOutcome: { ActualDrawIds.Count: 1, Replacement: null } } r &&
            r.Recipients.SequenceEqual([0, 2]) && s.V().Hand.Any(c => c.Id == original) &&
            s.E<ZenrunPaidEvent>().Count(e => e.FrameId == zenrun.Id && e.AttemptFrameId == attempt.Id && e.Count == 1) == 1 &&
            s.E<ZenrunCompletedEvent>().Count(e => e.FrameId == zenrun.Id && e.Paid && e.Count == 1) == 1 &&
            !s.E<OrdinaryBingyiCompletedEvent>().Any(e => e.FrameId == id),
            "The replaced self benefit returns Actual0 with one genuine replacement entity; only after compensation does the frozen other recipient's distinct actual Draw1 begin.");
        s.Private(2); s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision)); s.Play();
        s.Need(s.E<OrdinaryBingyiDrawReturnedEvent>().Where(e => e.FrameId == id).Select(e => (e.RecipientSeat, e.ActualCount, e.ReplacementCount)).SequenceEqual([(0, 0, 1), (2, 1, 0)]) &&
            s.G.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.From == CardLocation.DrawPile && m.Reason.Value == DrawReason) == 0 &&
            s.G.CardMovements.Count(m => m.To == CardLocation.Hand(1) && m.From == CardLocation.DrawPile && m.Reason.Value == CompensationReason) == 1 &&
            s.G.CardMovements.Count(m => m.To == CardLocation.Hand(2) && m.From == CardLocation.DrawPile && m.Reason.Value == DrawReason) == 1 &&
            s.E<OrdinaryBingyiCompletedEvent>().Count(e => e.FrameId == id && e.RecipientCount == 2) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == original && m.Reason.Value == TakeReason) == 1,
            "Cold replacement and two native child returns preserve separate physical sources, one obtain, one compensation, one later draw and one whole completion.");
        s.Cold();
    }

    private sealed class Scenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public ProgramSkillFrame Root(long id) => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == id);
        public CardMovementRecord[] Draws(int before) => G.CardMovements.Where(m => m.Sequence > before && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)).ToArray();
        public bool Optional() => Optional(Bingyi);
        public bool Optional(string skill) => P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public bool Action(string action) => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action) == true;
        public bool Branch(string branch) => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "ordinary-bingyi" && c.Parameters.GetValueOrDefault("branch") == branch) == true;
        public bool Hold(string option) => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option);
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Offer() => Reach(Optional);
        public long BeginBingyi() { Activate(); Reach(() => Branch("confirm")); return G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OrdinaryBingyi is not null).Id; }
        public void Toggle(int seat) => Answer(c => c.Parameters.GetValueOrDefault("branch") == "toggle" && c.Parameters.GetValueOrDefault("peer-seat") == seat.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public void Confirm() => Answer(c => c.Parameters.GetValueOrDefault("branch") == "confirm");
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Shenxing(int[] cards) => Accept(new UseProgramSkillCommand(0, OrdinaryGuYongChecks.Shenxing, "discard-two-draw-one", cards, [], G.Revision, P!.PromptId));
        public void Discard() { Use("discard"); Reach(() => Action("select-owned-cards")); Answer(c => c.Cards.Count == 1); }
        public void PlayCard(int id, int[] targets)
        {
            var a = G.GetHumanLegalActions().First(a => a.CardId == id && a.TargetSeats.SequenceEqual(targets) && a.ConversionSource is null && a.Kind != LegalActionKind.Recast);
            Accept(new PlayCardCommand(0, id, targets, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void Answer(Func<PromptChoice, bool> test)
        {
            var p = P!; var c = p.Choices.FirstOrDefault(test); Need(p.PlayerSeat == 0 && c is not null, "Only a published choice of the real human can be answered.");
            Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision));
        }
        public void Private(int seat) => Need(P is { IsPrivate: true } p && p.PlayerSeat == seat && Enumerable.Range(0, 4).Where(i => i != seat).All(i => G.CreateSnapshot(i).PendingDecision is null), "Recipient and native child decisions are private to their actual chooser.");
        public void Reject()
        {
            var p = P!; var state = State();
            Need(!G.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && state == State() &&
                !G.Submit(new AnswerPromptCommand(0, p.PromptId, new ChoiceId("ordinary-gu-yong-unpublished"), G.Revision)).Accepted && state == State(), "Foreign and unpublished input cannot mutate frozen reveal, payments or quota.");
        }
        public void RejectOld(PendingDecision p) { var state = State(); Need(!G.Submit(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && state == State(), "A genuine stale recipient prompt cannot answer a consecutive decision."); }
        public void Cold() { var state = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(state == State(), "Cold replay preserves exact private views, frozen entities, native child returns, quota and commands."); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 190; i++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The bounded real fixture ended before its requested native boundary.");
                var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.SkillPrompt?.SkillId == Watch) Continue();
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                { Need(p.SkillPrompt?.SkillId != Bingyi, "An unexpected real Bingyi offer must not be silently skipped."); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); }
                else if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification) Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is not ("faction-slash-request" or "faction-defense-request"));
                else if (p.Kind == DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else throw new InvalidOperationException("Unexpected native prompt " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary " + Diagnostic());
        }
        public void Accept(GameCommand command)
        {
            try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + JsonSerializer.Serialize(result)); }
            catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " command=" + command.GetType().Name + " cause=" + e.Message + " " + Diagnostic(), e); }
        }
        public void Need(bool value, string text) { if (!value) throw new InvalidOperationException(text + " " + Diagnostic()); }
        public string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, Prompt = P, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).GeneralId, V(i).Hp, V(i).MaxHp, V(i).IsAlive, V(i).HandCount }), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Shown = E<OrdinaryBingyiShownEvent>(), Draws = E<OrdinaryBingyiDrawReturnedEvent>(), Moves = G.CardMovements.TakeLast(10) });
        public static Scenario Start(string scene, bool play = true)
        {
            ContentRegistry r;
            try { r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene)); }
            catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture=" + e.Message, e); }
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, ModeId = Mode, HumanSeat = 0, HumanRole = scene == "paid-death" ? Role.Renegade : Role.Lord, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 5, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new Scenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); s.Accept(new SelectGeneralCommand(0, Owner, g.Revision, s.P!.PromptId)); if (play) s.Play(); return s;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ordinary-gu-yong", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            Production(b, "ol-gu-yong", Shenxing, Bingyi);
            if (scene == "hongyan") CharacterFixtureContent.Register(b, "boundary:xiao-qiao");
            if (scene == "replacement") Production(b, "ol-sun-hong", Zenrun);
            static JsonNode N(string json) => JsonNode.Parse(json)!;
            static JsonNode A(string id, string effects, int cards = 0, int targets = 0, string more = "") => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," + more + "\"effects\":[" + effects + "]}");
            const string DiscardEffects = "{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"amount\":1,\"zones\":[\"hand\"],\"resultBind\":\"paid\"},{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"paid\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}";
            var duel = A("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", targets: 1).AsObject(); duel["targetKind"] = "otherLiving"; duel["usesPerPhase"] = 2;
            var modifiers = N("[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":40,\"priority\":0}]").AsArray();
            if (scene is "six" or "insert") modifiers.Add(N("{\"id\":\"phase-stock\",\"query\":\"initialHandSize\",\"operation\":\"add\",\"value\":8,\"priority\":0}"));
            var defs = new JsonArray(new JsonObject { ["id"] = Driver, ["revision"] = 1, ["modifiers"] = modifiers, ["activations"] = new JsonArray(
                A("stock", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":8}"), A("stock-three", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":3}"), A("stock-all", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20}"),
                A("wound", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}"), A("discard", DiscardEffects), A("give", "{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}", 1, 1), duel) });
            if (scene == "paid-loss")
            {
                defs[0]!["activations"]!.AsArray().Add(A("sever-source", "{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"" + Conditional + "\"],\"sourceBind\":\"" + Regrant + "\"}"));
                defs.Add(new JsonObject { ["id"] = Regrant, ["revision"] = 1,
                    ["activations"] = new JsonArray(A("recover-and-regrant", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1},{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"" + Bingyi + "\"]}")),
                    ["triggers"] = N("[{\"id\":\"new-instance-ending\",\"window\":\"turnEnding\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[" + DiscardEffects + "]}]") });
            }
            defs.Add(N($$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""));
            if (scene is "six" or "insert")
            {
                JsonNode Trigger(string id, string window, string effects, int priority = 0) => N("{\"id\":\"" + id + "\",\"window\":\"" + window + "\",\"subject\":\"owner\",\"priority\":" + priority + ",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[" + effects + "]}");
                var triggers = new JsonArray();
                if (scene == "six") foreach (var window in new[] { "turnStartBeforeNormalFlow", "judgmentPhaseStarting", "drawPhaseStarting", "playPhaseStarting", "discardPhaseStarting", "turnEnding" }) triggers.Add(Trigger("discard-" + window, window, DiscardEffects));
                else { triggers.Add(Trigger("before-insert", "turnStartBeforeNormalFlow", DiscardEffects, 100)); triggers.Add(Trigger("insert", "turnStartBeforeNormalFlow", "{\"op\":\"insertPhase\",\"target\":\"owner\",\"phase\":\"play\",\"phaseContinuation\":\"beforeNormalPreparation\"}")); triggers.Add(Trigger("after-insert", "turnStartBeforeNormalFlow", DiscardEffects, -100)); }
                defs.Add(new JsonObject { ["id"] = Phase, ["revision"] = 1, ["triggers"] = triggers });
            }
            if (scene == "paid-loss") defs.Add(N($$"""{"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy","skillId":"{{Bingyi}}","minimumHp":13}]}"""));
            var watch = new JsonArray();
            if (scene == "lion") watch.Add(N("{\"id\":\"real-lion\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"recovery\",\"options\":[{\"id\":\"recovery\"}]}]}"));
            if (scene is "frozen" or "paid-loss" or "paid-death")
            {
                var option = scene == "frozen" ? "gained" : "paid";
                var after = scene == "frozen" ? "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":3}" : "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "paid-loss" ? 1 : 20) + "}";
                watch.Add(N("{\"id\":\"real-gain\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + DrawReason + "\"],\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"gain\",\"options\":[{\"id\":\"" + option + "\"}]}," + after + "]}"));
            }
            if (scene == "ox") watch.Add(N("{\"id\":\"real-spill\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"movementOccurrence\":\"perBatch\",\"sourceZones\":[\"woodenOxGrain\"],\"movementReasons\":[\"equipment.wooden-ox.grain-discard\"],\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"spill\",\"options\":[{\"id\":\"spill\"}]}]}"));
            if (scene == "replacement")
                foreach (var (option, reason) in new[] { ("obtained", TakeReason), ("compensated", CompensationReason), ("other-draw", DrawReason) })
                    watch.Add(N("{\"id\":\"real-" + option + "\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + reason + "\"],\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"seen\",\"options\":[{\"id\":\"" + option + "\"}]}]}"));
            if (watch.Count > 0) defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watch });
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "普通顾雍真实弃牌阶段与原生收益子树", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(a => a!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id) });
            var c = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, id, "原生小夹具") { Program = p, ProgramPresentation = c.Presentations[id] });
            const string Pick = "fixture:ordinary-gy-pick";
            b.AddSkill(new(Pick, "本人", "固定真人候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            var skills = new List<string> { Driver, Shenxing, scene == "paid-loss" ? Conditional : Bingyi };
            if (watch.Count > 0) skills.Add(Watch); if (scene is "six" or "insert") skills.Add(Phase); if (scene == "hongyan") skills.Add("boundary:hongyan");
            if (scene == "replacement") skills.Add(Zenrun);
            b.AddGeneral(new(Owner, "普通顾雍边界拥有者", "supporter", Pick, "wu", 12, skills));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:ordinary-gy-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++) { var pick = "fixture:ordinary-gy-peer-pick-" + i; b.AddSkill(new(pick, "其他角色", "固定公开角色") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) }); b.AddGeneral(new(peers[i - 1], "实际其他角色", "supporter", pick, "wei", 12, scene == "foreign" && i == 1 ? [] : scene == "replacement" ? [Quiet, Watch] : [Quiet])); }
            var kind = scene switch { "lion" => CardKind.SilverLion, "foreign" => CardKind.Dismantlement, "recast" => CardKind.IronChain, "replace" => CardKind.Crossbow, "ox" => CardKind.WoodenOx, _ => CardKind.Slash };
            var cardId = "fixture:ordinary-gy-card"; b.AddCard(new(cardId, "真实" + kind, CardCatalog.Get(kind).CategoryName, "原印实体", kind));
            var colors = scene is "mixed" or "hongyan";
            b.AddDeck(new("fixture:ordinary-gy-deck", "固定小真实牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, colors ? 20 : 64).Select(i => new ContentDeckPhysicalCard(cardId, colors && i >= 10 ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray() });
            var roles = scene == "paid-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(Mode, "普通顾雍真实阶段与弃牌", 4, 4, roles, "fixture:ordinary-gy-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
        private static void Production(IContentRegistryBuilder b, string bundle, params string[] ids)
        {
            string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing production resource " + bundle + "." + suffix); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var c = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var id in ids) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
        }
    }
}
