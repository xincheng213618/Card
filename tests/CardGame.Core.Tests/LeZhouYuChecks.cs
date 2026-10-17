using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class LeZhouYuChecks
{
    private const string ZyGu = "le:guyin", ZyPing = "le:pinglu", ZyOwner = "fixture:lzy-owner";
    private const string ZyDriver = "fixture:lzy-driver", ZyQuiet = "fixture:lzy-quiet", ZyWatch = "fixture:lzy-watch";
    private const string ZyConditional = "fixture:lzy-conditional", ZyNeutral = "fixture:lzy-neutral", ZyInsert = "fixture:lzy-insert";
    private const string ZyMode = "identity:classic-le-zhou-yu", ZyActivation = "all-in-range-random-hand";
    private const string ZyDraw = "skill-program.le-zhou-yu.guyin.draw", ZyObtain = "skill-program.le-zhou-yu.pinglu.obtain";

    public static void GuYinOriginalOpeningEntitiesAndMultiOwnerInitializationCold()
    {
        var s = ZyScenario.Start("initial");
        s.Need(s.V().HandCount == 0 && Enumerable.Range(1, 3).All(i => s.V(i).HandCount == 5),
            "The real opening has no GuYin-owner cards and one additional entity for every other participant.");
        var initial = s.E<GuYinInitialEntityRegisteredEvent>().Where(e => e.OwnerSeat == 0).ToArray();
        s.Need(initial.Length == 15 && initial.All(e => e.OriginalOwnerSeat != 0 && s.G.CardMovements.Count(m =>
            m.Sequence == e.EntrySequence && m.CardId == e.CardId && m.CardKind == e.PrintedKind &&
            m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(e.OriginalOwnerSeat) && m.Reason == CardMoveReasons.InitialDeal) == 1),
            "Every original foreign entity has exactly one real opening entry, rather than a later hand-size or current-holder guess.");
        s.Need(s.E<GuYinInitializedEvent>().Single(e => e.Source.OwnerSeat == 0).EntityCount == 15,
            "One actual initializer freezes the complete other-participant opening set."); s.PrivateHands(); s.Cold();

        s = ZyScenario.Start("dual");
        s.Need(s.V().HandCount == 0 && s.V(1).HandCount == 0 && s.V(2).HandCount == 6 && s.V(3).HandCount == 6,
            "Two genuine GuYin holders each have zero opening cards; both source instances add to the other two deals.");
        foreach (var owner in new[] { 0, 1 })
            s.Need(s.E<GuYinInitialEntityRegisteredEvent>().Count(e => e.OwnerSeat == owner) == 12 &&
                s.E<GuYinInitializedEvent>().Count(e => e.Source.OwnerSeat == owner && e.EntityCount == 12) == 1,
                "Each holder independently freezes the same original foreign entities without another holder acquiring a fake initial Hand.");
        s.Cold();

        s = ZyScenario.Start("opening"); var oldHand = s.V().Hand.Select(c => c.Id).ToHashSet();
        s.Need(s.P is { Kind: DecisionKind.OpeningHand } && oldHand.Count == 5 && !s.E<GuYinInitializedEvent>().Any(),
            "A real nonholder can customize its five opening entities before the foreign GuYin initializer runs.");
        var replacement = s.P!.Choices.First(c => c.Parameters.GetValueOrDefault("action") == "replace");
        s.Private(0); s.Cold(); s.Answer(c => c.Id == replacement.Id);
        var actualOpening = s.V().Hand.Select(c => c.Id).ToHashSet(); var incoming = actualOpening.Single(id => !oldHand.Contains(id));
        var entry = s.G.CardMovements.Last(m => m.CardId == incoming && m.To == CardLocation.Hand(0));
        s.Answer(c => c.Parameters.GetValueOrDefault("action") == "confirm");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == ZyDriver && s.P.Choices.Any(c => c.Cards.SequenceEqual([incoming])));
        var frozen = s.G.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.GuYinInitialEntities is not null).GuYinInitialEntities!;
        s.Need(!s.E<GuYinInitializedEvent>().Any() && frozen is IList { IsReadOnly: true } &&
            frozen.Where(e => e.OriginalOwnerSeat == 0).Select(e => e.CardId).ToHashSet().SetEquals(actualOpening),
            "The GameStarting mother freezes a defensive copy of the actual replaced opening before its earlier real discard child and initialization.");
        s.Private(0); s.Cold(); s.Answer(c => c.Cards.SequenceEqual([incoming])); s.Play();
        var registered = s.E<GuYinInitialEntityRegisteredEvent>().Where(e => e.OwnerSeat == 1 && e.OriginalOwnerSeat == 0).ToArray();
        s.Need(registered.Select(e => e.CardId).ToHashSet().SetEquals(actualOpening) && registered.Single(e => e.CardId == incoming).EntrySequence == entry.Sequence &&
            !registered.Any(e => oldHand.Except(actualOpening).Contains(e.CardId)) && s.G.CardMovements.Any(m => m.CardId == incoming && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile),
            "Post-replacement original IDs and their real entry invoice survive an earlier GameStarting movement; the outgoing opening entity is not registered."); s.Cold();

        s = ZyScenario.Start("late"); var originalForeign = s.V(1).Hand[0].Id;
        s.Need(s.V().HandCount == 4 && Enumerable.Range(0, 4).All(i => !s.V(i).Skills!.Any(k => k.ContentId == ZyGu)) &&
            s.E<GuYinOpeningCapturedEvent>().Single().EntityCount == 16 && s.E<GuYinOpeningEntityEvent>().Any(e => e.OriginalOwnerSeat == 1 && e.CardId == originalForeign) && !s.E<GuYinInitializedEvent>().Any(),
            "The real final opening has a global original-entity witness even when nobody initially holds GuYin.");
        s.Use("grant-guyin"); s.Play();
        s.Need(s.V().Skills!.Any(k => k.ContentId == ZyGu) && s.V().HandCount == 4 && !s.E<GuYinInitializedEvent>().Any(),
            "A legal late grant neither redeals the opening Hand nor pretends to rerun the GameStarting initializer.");
        s.Take(1, originalForeign); s.Play(); s.Use("discard-one", [originalForeign]); s.Play();
        s.Need(s.E<GuYinDiscardQualifiedEvent>().Count(e => e.Source.OwnerSeat == 0 && e.OriginalOwnerSeat == 1 && e.CardId == originalForeign) == 1 &&
            s.E<GuYinDrawReturnedEvent>().Count(e => e.CardId == originalForeign && e.ActualCount == 1) == 1,
            "The newly held skill recognizes another participant's genuine original entity on its later real discard, independently of initial skill ownership."); s.Cold();
    }

    public static void GuYinTrueUseDiscardPlayedAndNativeDrawChildrenCold()
    {
        var s = ZyScenario.Start("discard"); var ids = s.V().Hand.Take(2).Select(c => c.Id).ToArray();
        s.Use("discard-two", ids); s.Reach(() => s.Hold("guyin"));
        var started = s.E<GuYinDrawStartedEvent>().Single(); var first = s.E<GuYinDrawRequestedEvent>().Single();
        s.Need(started.EntityCount == 2 && first.Index == 0 && ids.Contains(first.CardId) &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == started.FrameId).GuYin is { Index: 0, Stage: GuYinStage.DrawChildren, Entities: IList { IsReadOnly: true } } && !s.E<GuYinCompletedEvent>().Any(),
            "A real two-entity discard owns one ordered reward receipt and suspends at the first native Draw1 child.");
        s.Private(0); var stale = s.P!; s.RejectPrompt(); s.Cold(); s.Continue(); s.Reach(() => s.Hold("guyin"));
        s.Need(s.E<GuYinDrawRequestedEvent>().Count(e => e.FrameId == started.FrameId) == 2 &&
            s.E<GuYinDrawReturnedEvent>().Count(e => e.FrameId == started.FrameId) == 2 &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == started.FrameId).GuYin is { Index: 1, Stage: GuYinStage.DrawChildren } && !s.E<GuYinCompletedEvent>().Any(),
            "The second original entity waits for the first actual gain child; its own returned material is signed before its still-pending child, with two Draw1 requests rather than a synthetic Draw2.");
        s.RejectOld(stale); s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<GuYinCompletedEvent>().Single(e => e.FrameId == started.FrameId).DrawCount == 2 &&
            s.E<GuYinDrawReturnedEvent>().Where(e => e.FrameId == started.FrameId).All(e => e.ActualCount == 1 && e.ReplacementCount == 0) &&
            ids.All(id => s.E<GuYinDiscardQualifiedEvent>().Count(e => e.CardId == id && e.Source.OwnerSeat == 1) == 1),
            "Both exact discard arrivals receive one native draw and the whole original receipt completes once."); s.Cold();

        s = ZyScenario.Start("slash"); var slash = s.V().Hand[0]; s.Native(s.Actions().First(a => a.CardId == slash.Id && a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1])));
        s.Reach(() => s.Hold("guyin"));
        s.Need(s.E<CardUseDeclaredEvent>().Count(e => e.CardId == slash.Id && e.SourceSeat == 0 && e.CardKind == CardKind.Slash) == 1 &&
            s.E<GuYinDiscardQualifiedEvent>().Count(e => e.CardId == slash.Id) == 1 && s.G.CardMovements.Count(m => m.CardId == slash.Id && m.To == CardLocation.DiscardPile) == 1,
            "A natural initial Slash qualifies on its actual true-Use discard arrival, with one real original-material payment.");
        s.Cold(); s.Continue(); s.Play();

        s = ZyScenario.Start("dodge"); var dodge = s.V().Hand[0]; s.Use("incoming", targets: [1]);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0])) == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0]));
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Answer(c => c.Cards.SequenceEqual([dodge.Id])); s.Reach(() => s.Hold("guyin"));
        s.Need(s.E<CardRespondedEvent>().Count(e => e.CardId == dodge.Id && e.ResponderSeat == 0 && e.EffectiveCardKind == CardKind.Dodge) == 1 &&
            s.E<GuYinDiscardQualifiedEvent>().Count(e => e.CardId == dodge.Id) == 1,
            "A real native Slash-defense Dodge is true Use and qualifies for GuYin, even though it is submitted through a response prompt.");
        s.Cold(); s.Continue(); s.Play();

        s = ZyScenario.Start("played"); var played = s.V().Hand[0]; s.Use("duel", targets: [1]);
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash }); s.Answer(c => c.Cards.SequenceEqual([played.Id])); s.Play();
        s.Need(s.E<CardRespondedEvent>().Count(e => e.CardId == played.Id && e.ResponderSeat == 0) == 1 &&
            s.G.CardMovements.Any(m => m.CardId == played.Id && m.To == CardLocation.DiscardPile) && !s.E<GuYinDrawStartedEvent>().Any() && !s.E<GuYinDiscardQualifiedEvent>().Any(e => e.CardId == played.Id),
            "The same original Slash printed face really enters Discard as a Duel Played response and does not become a GuYin true Use or discard."); s.Cold();

        s = ZyScenario.Start("recast"); var recast = s.V().Hand[0];
        s.Accept(new RecastCardCommand(0, recast.Id, s.G.Revision, s.P!.PromptId)); s.Play();
        s.Need(s.E<CardRecastEvent>().Count(e => e.CardId == recast.Id) == 1 && s.G.CardMovements.Any(m => m.CardId == recast.Id && m.Reason == CardMoveReasons.RecastDiscard) &&
            !s.E<GuYinDiscardQualifiedEvent>().Any(e => e.CardId == recast.Id) && !s.E<GuYinDrawStartedEvent>().Any(),
            "A real original IronChain recast retains its native draw but is neither a Use arrival nor an ordinary discard for GuYin."); s.Cold();

        s = ZyScenario.Start("own-original"); s.Ping(); s.Play(); var received = s.E<PingluEntityObtainedEvent>().First(e => e.TargetSeat == 1);
        s.Need(s.E<GuYinInitialEntityRegisteredEvent>().Any(e => e.OwnerSeat == 0 && e.OriginalOwnerSeat == 1 && e.CardId == received.CardId) && !s.E<GuYinDiscardQualifiedEvent>().Any(),
            "Transferring an original foreign entity to the GuYin holder is not itself a qualifying discard.");
        s.Use("discard-one", [received.CardId]); s.Play();
        s.Need(s.E<GuYinDiscardQualifiedEvent>().Count(e => e.CardId == received.CardId && e.OriginalOwnerSeat == 1 && e.Source.OwnerSeat == 0) == 1 &&
            s.E<GuYinDrawReturnedEvent>().Count(e => e.CardId == received.CardId && e.ActualCount == 1) == 1,
            "Original participant identity survives transfer: the holder's own later discard of that foreign initial entity still qualifies."); s.Cold();

        s = ZyScenario.Start("repeat"); var repeat = s.V().Hand[0].Id; s.Use("give", [repeat], [2]); s.Play();
        s.Need(!s.E<GuYinDiscardQualifiedEvent>().Any(), "A real initial Hand transfer is not a discard arrival.");
        s.Take(2, repeat); s.Play(); s.Use("discard-one", [repeat]); s.Play(); s.End(); s.Play();
        s.Need(s.V().Hand.Any(c => c.Id == repeat) && s.E<QiqinCollectedEntityEvent>().Any(e => e.CardId == repeat),
            "The actual preparation Qiqin collector reobtains the same physical entity without resetting GuYin's original identity.");
        s.Use("discard-one", [repeat]); s.Play();
        var arrivals = s.E<GuYinDiscardQualifiedEvent>().Where(e => e.CardId == repeat).ToArray();
        s.Need(arrivals.Length == 2 && arrivals.Select(e => e.MovementSequence).Distinct().Count() == 2 &&
            s.E<GuYinDrawReturnedEvent>().Count(e => e.CardId == repeat && e.ActualCount == 1) == 2,
            "Two distinct real discard arrivals of one original entity each draw once; invoice deduplication is not a permanent once-per-card limit."); s.Cold();

        s = ZyScenario.Start("draw-loss"); var cost = s.V().Hand[0].Id; s.Use("discard-one", [cost]); s.Reach(() => s.Hold("guyin"));
        started = s.E<GuYinDrawStartedEvent>().Single(); s.Private(0); s.Cold(); s.Continue(); s.Reach(() => s.Hold("after-loss"));
        s.Need(!s.V(1).Skills!.Any(k => k.ContentId == ZyGu) && !s.E<GuYinCompletedEvent>().Any(e => e.FrameId == started.FrameId),
            "A real conditional skill loss inside the native gain child cannot retire the paid GuYin parent before the child returns.");
        s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<GuYinCompletedEvent>().Count(e => e.FrameId == started.FrameId && e.DrawCount == 1) == 1 &&
            s.E<GuYinDrawReturnedEvent>().Count(e => e.FrameId == started.FrameId && e.CardId == cost && e.ActualCount == 1) == 1 &&
            s.E<GuYinDrawStartedEvent>().Single().Source == started.Source,
            "The already-issued native reward drains once under its frozen source after losing the skill."); s.Cold();
    }

    public static void PingluRealRangeWholeBatchAndSamePlayResidencyLatchCold()
    {
        var s = ZyScenario.Start("ping-range"); s.Need(s.CanPing(), "A real Play with in-range foreign Hand material publishes the zero-input activation."); s.RejectPing();
        s.Ping(); s.Reach(() => s.Hold("pinglu")); var start = s.E<PingluStartedEvent>().Single(); var paid = s.E<PingluObtainPaidEvent>().Single();
        var sample = s.E<PingluTargetSampledEvent>().Where(e => e.FrameId == start.FrameId).OrderBy(e => e.Index).ToArray();
        var gained = s.E<PingluEntityObtainedEvent>().Where(e => e.FrameId == start.FrameId).ToArray();
        var parent = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == start.FrameId);
        s.Need(sample.Select(e => e.TargetSeat).SequenceEqual([1, 3]) && sample.All(e => e.HandCount == 4 && e.CardId is not null) &&
            paid.ActualCount == 2 && paid.PrimaryCount == 2 && gained.Select(e => e.BatchId).Distinct().Single() == paid.BatchId &&
            parent.Pinglu?.Targets is IList { IsReadOnly: true } && !s.E<PingluCompletedEvent>().Any(),
            "Current native attack range selects both adjacent participants, freezes real random entities, and obtains both in one owning batch before movement children complete.");
        s.Need(gained.All(e => s.G.CardMovements.Count(m => m.CardId == e.CardId && m.Sequence == e.Sequence && m.From == CardLocation.Hand(e.TargetSeat) && m.To == CardLocation.Hand(0) && m.Reason.Value == ZyObtain) == 1),
            "Each randomly sampled source entity has one actual Hand-to-Hand payment and retains its printed face.");
        s.Private(0); s.PrivateHands(); var stale = s.P!; s.RejectPrompt(); s.Cold(); s.Continue(); s.Play(); s.RejectOld(stale);
        s.Need(!s.CanPing() && s.E<PingluCompletedEvent>().Count(e => e.FrameId == start.FrameId && e.ActualCount == 2) == 1,
            "Any member still in the original obtained Hand residency blocks another activation in the same actual Play.");
        s.Use("give", [gained[0].CardId], [1]); s.Play(); s.Need(!s.CanPing(), "Releasing only one member cannot prematurely unlock the cohort.");
        s.Use("give", [gained[1].CardId], [1]); s.Play(); s.Need(s.CanPing(), "The final actual departure unlocks a second activation in the same Play.");
        s.Take(1, gained[0].CardId); s.Play(); s.Need(s.CanPing(), "Reobtaining an old entity creates a new residency and does not resurrect a released cohort latch.");
        s.Ping(); s.Reach(() => s.Hold("pinglu")); s.Cold(); s.Continue(); s.Play();
        s.Need(s.E<PingluStartedEvent>().Count(e => e.Phase == start.Phase) == 2 && s.E<PingluCompletedEvent>().Length == 2,
            "A legitimately released same-Play second obtain has a new once-only native batch and completion.");

        s = ZyScenario.Start("ping-empty"); foreach (var id in s.V(1).Hand.Select(c => c.Id).ToArray()) { s.Take(1, id); s.Play(); }
        s.Need(s.V(1).HandCount == 0 && s.V(3).HandCount == 4 && s.CanPing(), "An empty in-range participant does not hide the other legal recipient's material.");
        s.Ping(); s.Play(); sample = s.E<PingluTargetSampledEvent>().OrderBy(e => e.Index).ToArray();
        s.Need(sample.Length == 2 && sample[0] is { TargetSeat: 1, HandCount: 0, CardId: null, From: null } &&
            sample[0].RandomBefore == sample[0].RandomAfter && sample[1].TargetSeat == 3 && sample[1].CardId is not null &&
            s.E<PingluObtainPaidEvent>().Single().ActualCount == 1,
            "The full participant set is retained, but an empty Hand emits no fake card or RNG draw and only the other participant actually pays."); s.Cold();

        s = ZyScenario.Start("ping-weapon"); var weapon = s.V().Hand.First(c => c.Kind == CardKind.QinggangSword);
        s.Native(s.Actions().Single(a => a.CardId == weapon.Id && a.Kind == LegalActionKind.Equip)); s.Play(); s.Ping(); s.Play();
        s.Need(s.E<PingluTargetSampledEvent>().OrderBy(e => e.Index).Select(e => e.TargetSeat).SequenceEqual([1, 2, 3]) &&
            s.E<PingluEntityObtainedEvent>().Length == 3 && s.V().Equipment.Any(c => c.Id == weapon.Id),
            "A genuinely equipped native weapon expands the current attack range; Pinglu does not use a hardcoded adjacency list."); s.Cold();
    }

    public static void PingluDepartureReobtainNewPhaseAndPaidSourceRightsCold()
    {
        var s = ZyScenario.Start("ping-phase"); s.Ping(); s.Play(); var first = s.E<PingluStartedEvent>().Single();
        var firstIds = s.E<PingluEntityObtainedEvent>().Select(e => e.CardId).ToArray(); s.Use("lose-pinglu"); s.Play(); s.Use("grant-pinglu"); s.Play();
        s.Need(!s.CanPing() && firstIds.All(id => s.V().Hand.Any(c => c.Id == id)),
            "Losing and regaining the source instance cannot refresh an unreleased actual-Play cohort.");
        s.End(); s.Play(); var phase = s.E<ActualDiscardRecoveryPhaseStartedEvent>().Last(e => e.Phase.ActorSeat == 0 && e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play).Phase;
        s.Need(phase.Token != first.Phase.Token && phase.TurnNumber == first.Phase.TurnNumber && firstIds.All(id => s.V().Hand.Any(c => c.Id == id)) && s.CanPing(),
            "A genuine inserted Play has a new phase token inside the same actual turn and allows Pinglu while the prior phase's obtained cards are still held.");
        s.Cold(); s.Ping(); s.Play(); s.Need(s.E<PingluStartedEvent>().Last().Phase == phase && s.E<PingluCompletedEvent>().Length == 2,
            "The new phase creates its own once-completed cohort and does not mutate the original phase invoice."); s.Cold();

        foreach (var scene in new[] { "ping-loss", "ping-death" })
        {
            s = ZyScenario.Start(scene); s.Ping(); s.Reach(() => s.Hold("pinglu")); var begun = s.E<PingluStartedEvent>().Single();
            var paid = s.E<PingluObtainPaidEvent>().Single(); var entities = s.E<PingluEntityObtainedEvent>().ToArray();
            s.Need(paid.ActualCount == 2 && entities.Length == 2 && !s.E<PingluCompletedEvent>().Any() && s.G.ResolutionStack.Any(f => f.Id == begun.FrameId),
                "All random original entities have really been paid before the first gain observer; the paid parent remains live.");
            s.Private(0); var old = s.P!; s.RejectPrompt(); s.Cold(); s.Continue();
            if (scene == "ping-loss")
            {
                s.Reach(() => s.Hold("after-loss")); s.Need(!s.V().Skills!.Any(k => k.ContentId == ZyPing) && !s.E<PingluCompletedEvent>().Any(),
                    "Actual health-driven source loss happens inside the paid native gain child, without prematurely completing its parent.");
                s.Cold(); s.Continue(); s.Play(); s.RejectOld(old);
            }
            else s.Reach(() => !s.V().IsAlive && s.E<PingluCompletedEvent>().Any(e => e.FrameId == begun.FrameId));
            s.Need(s.E<PingluStartedEvent>().Single() == begun && s.E<PingluObtainPaidEvent>().Single() == paid &&
                s.E<PingluCompletedEvent>().Count(e => e.FrameId == begun.FrameId && e.ActualCount == 2) == 1 && entities.All(e =>
                    s.G.CardMovements.Count(m => m.CardId == e.CardId && m.Sequence == e.Sequence && m.From == e.From && m.To == e.To && m.Reason.Value == ZyObtain) == 1),
                "Source loss or nonterminal owner death drains the original children under frozen source/hash and cannot reroll, repay, cancel or replay the obtained cohort."); s.Cold();
        }
    }

    private sealed class ZyScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public LegalAction[] Actions() => G.GetHumanLegalActions().Where(a => a.ConversionSource is null).ToArray();
        public bool CanPing() => G.GetHumanLegalActions().Any(a => a.ProgramSkillId == ZyPing && a.ProgramActivationId == ZyActivation);
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == ZyWatch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, ZyDriver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Ping() => Accept(new UseProgramSkillCommand(0, ZyPing, ZyActivation, [], [], G.Revision, P!.PromptId));
        public void Native(LegalAction a) => Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Advance() => Accept(new AdvanceOneStepCommand(G.Revision));
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var choice = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && choice is not null, "The actual human must answer an exact published native choice."); Accept(new AnswerPromptCommand(0, p.PromptId, choice!.Id, G.Revision)); }
        public void Take(int seat, int id)
        { var slot = V(seat).Hand.ToList().FindIndex(c => c.Id == id); Need(slot >= 0, "The anonymous foreign-Hand slot refers to the actual chosen existing entity."); Use("take", targets: [seat]); Reach(() => P?.Choices.Any(c => c.Parameters.ContainsKey("slot-index")) == true); Private(0); Answer(c => c.Parameters.GetValueOrDefault("slot-index") == slot.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        public void Private(int chooser) => Need(P?.PlayerSeat == chooser && Enumerable.Range(0, 4).Where(i => i != chooser).All(i => G.CreateSnapshot(i).PendingDecision is null), "Other viewers do not receive a private chooser's native menu.");
        public void PrivateHands() => Need(Enumerable.Range(0, 4).All(viewer => G.CreateSnapshot(viewer).Players.Where(p => p.Seat != viewer).All(p => p.Hand.Count == 0)), "Original registrations and random samples cannot reveal private foreign Hand identities through player snapshots.");
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 220; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Fixture ended before the bounded native boundary at " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Advance();
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.SkillPrompt?.SkillId == ZyWatch && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([0])))
                    Answer(c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([0]));
                else if (p.SkillPrompt?.SkillId == ZyWatch) Continue();
                else if (p.SkillPrompt?.SkillId == ZyInsert && p.Choices.Any(c => c.Targets.SequenceEqual([0]))) Answer(c => c.Targets.SequenceEqual([0]));
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Kind == DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") == "let-die");
                else throw new InvalidOperationException("Unexpected native boundary at " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary at " + line + " " + Diagnostic());
        }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Diagnostic()); }
        public void Accept(GameCommand command)
        { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + " " + result.Error?.Code + ": " + result.Error?.Message); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " " + e.Message + " " + Diagnostic(), e); } }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay retains all four private views, exact initial/payment identities, RNG state, actual phase cohort, native children and accepted-command history."); }
        public void RejectPrompt()
        { var p = P!; var before = State(); Need(!G.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)).Accepted && State() == before && !G.Submit(new AnswerPromptCommand(0, p.PromptId, new("unpublished-le-zhou-yu"), G.Revision)).Accepted && State() == before, "Foreign and unpublished answers cannot mutate a paid native parent."); }
        public void RejectOld(PendingDecision old)
        { var before = State(); Need(!G.Submit(new AnswerPromptCommand(0, old.PromptId, old.Choices[0].Id, G.Revision)).Accepted && State() == before, "A retired choice cannot duplicate a reward, random sample or payment."); }
        public void RejectPing()
        {
            var before = State(); var id = V().Hand.FirstOrDefault()?.Id ?? int.MaxValue;
            foreach (var command in new[] { new UseProgramSkillCommand(0, ZyPing, ZyActivation, [id], [], G.Revision, P!.PromptId), new UseProgramSkillCommand(0, ZyPing, ZyActivation, [], [2], G.Revision, P.PromptId), new UseProgramSkillCommand(0, ZyPing, ZyActivation, [], [], G.Revision, P.PromptId) { SkillOwnerSeat = 2 } })
                Need(!G.Submit(command).Accepted && State() == before, "Pinglu never accepts a forged material, selected random target or foreign skill source.");
        }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner,
            Prompt = P is not { } p ? null : new { p.Kind, p.PlayerSeat, p.PromptId, Choices = p.Choices.Take(4) },
            Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).IsAlive, V(i).HandCount }),
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step }), GuYin = E<GuYinDrawStartedEvent>().TakeLast(3), Pinglu = E<PingluStartedEvent>().TakeLast(3), Paid = E<PingluObtainPaidEvent>().TakeLast(3) });
        public static ZyScenario Start(string scene)
        {
            ContentRegistry r; try { r = ContentRegistry.Build(new StandardContentPackage(), new ZyFixture(scene)); } catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture=" + e.Message, e); }
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "ping-death" ? Role.Renegade : Role.Lord,
                ModeId = ZyMode, UseInteractiveSetup = true, ConfirmOpeningHand = scene == "opening", UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 10, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new ZyScenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            s.Accept(new SelectGeneralCommand(0, ZyOwner, g.Revision, s.P!.PromptId)); if (scene == "opening") s.Reach(() => s.P?.Kind == DecisionKind.OpeningHand); else s.Play(); return s;
        }
    }

    private sealed class ZyFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-le-zhou-yu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            static void Production(IContentRegistryBuilder builder, string bundle, params string[] ids)
            {
                string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing real embedded bundle " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var c = SkillProgramCatalog.Load(Read("rules"), Read("presentation")); foreach (var id in ids) builder.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
            }
            Production(b, "le-zhou-yu", ZyGu, ZyPing); if (scene == "repeat") Production(b, "le-xiao-qiao", "le:qiqin");
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonObject A(string id, string effects, int cards = 0, int targets = 0) => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null," + (cards == 0 ? "" : "\"sourceZones\":[\"hand\"],") + "\"effects\":[" + effects + "]}").AsObject();
            static string Choice(string id) => "{\"op\":\"chooseOption\",\"target\":\"selectedTarget\",\"resultBind\":\"seen-" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            var duel = A("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", targets: 1); duel["targetKind"] = "otherLiving"; duel["usesPerPhase"] = 2;
            var incoming = A("incoming", "{\"op\":\"offerVirtualSlashOrDraw\",\"target\":\"selectedTarget\"}", targets: 1); incoming["targetKind"] = "otherLiving";
            var acts = new JsonArray(A("draw-one", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}"),
                A("give", "{\"op\":\"giveSelected\",\"target\":\"selectedTarget\"}", 1, 1),
                A("take", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1),
                A("discard-one", "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":1}", 1), A("discard-two", "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":2}", 2),
                A("lose-pinglu", "{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"le:pinglu\"],\"sourceBind\":\"" + ZyNeutral + "\"}"),
                A("grant-pinglu", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"le:pinglu\"]}"),
                A("grant-guyin", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"le:guyin\"]}"), duel, incoming);
            var driver = new JsonObject { ["id"] = ZyDriver, ["revision"] = 1, ["activations"] = acts,
                ["modifiers"] = N("[{\"id\":\"bounded-hand\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":40,\"priority\":0}]") };
            if (scene == "opening") driver["triggers"] = N("[{\"id\":\"actual-earlier-opening-discard\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"priority\":100,\"optional\":false,\"effects\":[{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"owner\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}]}]");
            var defs = new JsonArray(driver,
                N("{\"id\":\"" + ZyQuiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"),
                N("{\"id\":\"" + ZyNeutral + "\",\"revision\":1,\"modifiers\":[{\"id\":\"neutral\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":1,\"priority\":0}]}"));
            var donor = scene is "opening" or "discard" or "slash" or "dodge" or "played" or "recast" or "repeat" or "draw-loss";
            var ping = scene.StartsWith("ping-", StringComparison.Ordinal); var conditional = scene is "ping-loss" or "ping-death" or "draw-loss";
            var own = new List<string> { ZyDriver }; if (!donor && !ping && scene != "late") own.Add(ZyGu); if (ping || scene == "own-original") own.Add(conditional ? ZyConditional : ZyPing);
            if (scene == "repeat") own.Add("le:qiqin");
            if (conditional) defs.Add(N("{\"id\":\"" + ZyConditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-source\",\"skillId\":\"" + (scene == "draw-loss" ? ZyGu : ZyPing) + "\",\"minimumHp\":" + (scene == "ping-loss" ? 13 : 12) + "}]}"));
            var watched = scene is "discard" or "slash" or "dodge" or "draw-loss" or "ping-range" or "ping-loss" or "ping-death";
            if (watched)
            {
                var option = ping ? "pinglu" : "guyin"; var effects = "{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"}," + Choice(option);
                if (conditional) effects += ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "ping-death" ? 20 : 1) + "}" + (scene == "ping-death" ? "" : "," + Choice("after-loss"));
                defs.Add(N("{\"id\":\"" + ZyWatch + "\",\"revision\":1,\"triggers\":[{\"id\":\"actual-native-gain-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + (ping ? ZyObtain : ZyDraw) + "\"],\"optional\":false,\"effects\":[" + effects + "]}]}"));
                if (ping) own.Add(ZyWatch);
            }
            if (scene == "ping-phase")
            {
                defs.Add(N("{\"id\":\"" + ZyInsert + "\",\"revision\":1,\"triggers\":[{\"id\":\"new-actual-play\",\"window\":\"playEnding\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"insertPhase\",\"target\":\"selectedTarget\",\"phase\":\"play\",\"phaseContinuation\":\"beforeNormalPreparation\"}]}]}")); own.Add(ZyInsert);
            }
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实顾音平虏边界", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "真实顾音平虏夹具") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            const string pick = "fixture:lzy-owner-pick"; b.AddSkill(new(pick, "本人", "固定真人候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            b.AddGeneral(new(ZyOwner, "真实乐周瑜检查本人", "supporter", pick, "wu", 12, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:lzy-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++)
            {
                var pp = "fixture:lzy-peer-pick-" + i; b.AddSkill(new(pp, "其他角色", "固定公开候选") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) });
                var skills = new List<string> { ZyQuiet }; if (i == 1 && (donor || scene == "dual")) skills.Add(scene == "draw-loss" ? ZyConditional : ZyGu);
                if (i == 1 && watched && !ping) skills.Add(ZyWatch);
                b.AddGeneral(new(peers[i - 1], "真实其他角色", "supporter", pp, "shu", 12, skills));
            }
            var kind = scene == "dodge" ? "standard:dodge" : scene == "recast" ? "standard:iron_chain" : scene is "slash" or "played" or "repeat" ? "standard:slash" : scene == "ping-weapon" ? "standard:qinggang_sword" : "standard:crossbow";
            string[] openingKinds = ["standard:slash", "standard:dodge", "standard:peach", "standard:duel", "standard:crossbow"];
            b.AddDeck(new("fixture:lzy-deck", "真实顾音平虏小牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard(scene == "opening" ? openingKinds[i % 5] : kind, Suit.Heart, 7)).ToArray() });
            var roles = scene == "ping-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(ZyMode, "真实顾音和平虏", 4, 4, roles, "fixture:lzy-deck", GeneralCandidateCount: 4, GeneralPoolIds: [ZyOwner, .. peers]));
        }
    }
}
