using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class NativeDrawAttemptChecks
{
    private const string Owner = "fixture:native-draw-owner", Mode = "identity:classic-native-draw-attempt",
        Driver = "fixture:native-draw-driver", Watch = "fixture:native-draw-watch",
        Gain = "fixture:native-draw-gain", Quiet = "fixture:native-draw-quiet",
        PhaseWatch = "fixture:native-draw-phase-watch", Skip = "fixture:native-draw-skip",
        Extra = "fixture:native-draw-extra";
    private const string DrawReason = "skill-program." + Driver + ".Draw";
    private static string _scene = "";

    public static void ProgramDrawWaitsForPrivateDecisionAndBindsOnlyActualCardsCold()
    {
        foreach (var scene in new[] { "mandatory", "optional-accept", "optional-skip" })
        {
            var (g, registry) = Start(scene);
            var handBefore = HandCount(g, 0);
            var pileBefore = g.CreateSnapshot(0).DrawPileCount;
            Use(g, "draw");
            Drive(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var attempt = g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single();
            var producer = Producer(g);
            Need(attempt.Context.ParentFrameId == producer.Id && attempt.Context.RecipientSeat == 0 &&
                attempt.Context.RequestedCount == 2 && attempt.Context.Reason.Value == DrawReason &&
                attempt.Context.ActualTurnNumber == g.State.TurnNumber && attempt.Context.ActualTurnOwnerSeat == 0 &&
                producer.InstructionIndex == 1 && producer.NativeDraw is
                    { RecipientIndex: 0, RecipientApplied: false, AttemptReturned: false } &&
                producer.CardSetBindings.All(b => b.Name != "actual") &&
                PaidDraws(g).Length == 0 && HandCount(g, 0) == handBefore &&
                g.CreateSnapshot(0).DrawPileCount == pileBefore && !Option(g, Driver, "after"),
                "The real Draw instruction owns its request before any material, result bind or following instruction.");
            AssertPrivate(g, 0);
            g = Cold(g, registry);
            if (scene.StartsWith("optional", StringComparison.Ordinal))
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") ==
                    (scene == "optional-skip" ? "skip" : "activate"));
                if (scene == "optional-accept") Drive(g, () => Option(g, Watch, "before"));
            }
            if (scene != "optional-skip")
            {
                Need(Option(g, Watch, "before") && PaidDraws(g).Length == 0 &&
                    Producer(g).InstructionIndex == 1, "An accepted/mandatory pre-draw choice remains before physical draw.");
                g = Cold(g, registry);
                Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "before");
            }
            Drive(g, () => Option(g, Driver, "after"));
            var actual = PaidDraws(g);
            var result = Producer(g).CardSetBindings.Single(b => b.Name == "actual");
            Need(actual.Length == 2 && actual.All(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) &&
                actual.Select(m => m.CardId).SequenceEqual(result.CardIds) &&
                result.SourceLocations.SequenceEqual(new[] { CardLocation.Hand(0), CardLocation.Hand(0) }) &&
                result.Visibility == SkillProgramCardSetVisibility.Private &&
                result.CardIds is System.Collections.IList { IsReadOnly: true } &&
                HandCount(g, 0) == handBefore + 2 && g.CreateSnapshot(0).DrawPileCount == pileBefore - 2 &&
                Producer(g).NativeDraw is null &&
                E<NativeDrawAttemptDecidedEvent>(g).Single() is { Replaced: false, RequestedCount: 2 } decided &&
                decided.AttemptFrameId == attempt.Id && decided.ParentFrameId == producer.Id,
                "Only the original producer's actual DrawPile materials bind the completed result, once after its exact child returns.");
            Need(Enumerable.Range(0, 4).All(s => !g.CreateSnapshot(s).PublicRevealedCards
                .Any(c => result.CardIds.Contains(c.Id))), "A private draw result bind does not publish its faces to other viewers.");
            AssertPrivate(g, 0);
            g = Cold(g, registry);
            Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "after");
            Play(g);
            Need(PaidDraws(g).Length == 2 && E<NativeDrawAttemptStartedEvent>(g).Length == 1 &&
                E<ProgramSkillResolvedEvent>(g).Count(e => e.FrameId == producer.Id && e.Completed) == 1,
                "Cold request and result continuations do not redraw or resolve the producing instruction twice.");
        }
        // This child finishes inside the original Draw handler. Its typed return
        // must not let that outer handler dispatch past the already-open next choice.
        {
            var (g, registry) = Start("synchronous");
            var before = HandCount(g, 0);
            Use(g, "draw");
            Drive(g, () => Option(g, Driver, "after"));
            var producer = Producer(g);
            Need(producer.InstructionIndex == 2 && producer.NativeDraw is null &&
                producer.CardSetBindings.Single(b => b.Name == "actual").CardIds.Count == 2 &&
                HandCount(g, 0) == before + 2 && PaidDraws(g).Length == 2 &&
                E<NativeDrawAttemptStartedEvent>(g).Length == 1 &&
                E<NativeDrawAttemptDecidedEvent>(g).Length == 1 &&
                E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == Watch && e.Activated && e.Completed) == 1 &&
                !E<ProgramSkillResolvedEvent>(g).Any(e => e.FrameId == producer.Id),
                "A synchronously completed mandatory pre-child returns once and preserves the genuine following choice.");
            g = Cold(g, registry);
            Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "after");
            Play(g);
            Need(PaidDraws(g).Length == 2 && E<ProgramSkillResolvedEvent>(g).Count(e =>
                e.FrameId == producer.Id && e.Completed) == 1,
                "Synchronous reentry and cold return do not skip, repeat or prematurely complete the following instruction.");
        }
    }

    public static void SelectedRecipientsWaitForEachActualDrawAndObserverCold()
    {
        var (g, registry) = Start("recipients");
        var ownBefore = HandCount(g, 0);
        var peerBefore = HandCount(g, 2);
        Use(g, "pair", [2, 0]);
        Drive(g, () => Option(g, Watch, "before", 2));
        var producerId = Producer(g).Id;
        Need(Producer(g).NativeDraw is { RecipientIndex: 0, RecipientApplied: false } paid &&
            paid.Recipients.SequenceEqual([2, 0]) && paid.Recipients is System.Collections.IList { IsReadOnly: true } &&
            E<NativeDrawAttemptStartedEvent>(g).Select(e => e.Context.RecipientSeat).SequenceEqual([2]) &&
            PaidDraws(g).Length == 0 && HandCount(g, 0) == ownBefore && HandCount(g, 2) == peerBefore,
            "The selected recipient order is frozen, with the actual AI participant's first request pending.");
        AssertPrivate(g, 2);
        g = Cold(g, registry);
        // Advance the actual AI choice through its native driver rather than answering for it.
        Accept(g, new AdvanceOneStepCommand(g.Revision));
        Drive(g, () => Option(g, Gain, "gained", 2));
        Need(PaidDraws(g) is [{ To: var firstDestination }] && firstDestination == CardLocation.Hand(2) &&
            Producer(g).NativeDraw is { RecipientIndex: 0, RecipientApplied: true } &&
            E<NativeDrawAttemptStartedEvent>(g).Length == 1 && HandCount(g, 2) == peerBefore + 1 &&
            HandCount(g, 0) == ownBefore && !Option(g, Driver, "after"),
            "The first physical draw's real CardsGained observer must finish before the second recipient is attempted.");
        g = Cold(g, registry);
        Accept(g, new AdvanceOneStepCommand(g.Revision));
        Drive(g, () => Option(g, Watch, "before"));
        Need(Producer(g).NativeDraw is { RecipientIndex: 1, RecipientApplied: false } &&
            E<NativeDrawAttemptStartedEvent>(g).Select(e => e.Context.RecipientSeat).SequenceEqual([2, 0]) &&
            PaidDraws(g).Length == 1, "The second attempt keeps the same paid instruction and follows the first completed participant.");
        var facts = g.Events.Select(e => e.Payload).ToArray();
        var firstGainReturned = Array.FindIndex(facts, e => e is ProgramBindingResolvedEvent b &&
            b.SkillId == Gain && b.OwnerSeat == 2 && b.Activated && b.Completed);
        var secondAttempt = Array.FindIndex(facts, e => e is NativeDrawAttemptStartedEvent n && n.Context.RecipientSeat == 0);
        Need(firstGainReturned >= 0 && secondAttempt > firstGainReturned,
            "The native history records the first gain-child return before it issues the next participant's request.");
        AssertPrivate(g, 0);
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "before");
        Drive(g, () => Option(g, Gain, "gained"));
        Need(PaidDraws(g).Select(m => m.To.OwnerSeat).SequenceEqual(new int?[] { 2, 0 }) &&
            Producer(g).NativeDraw is { RecipientIndex: 1, RecipientApplied: true } &&
            HandCount(g, 0) == ownBefore + 1 && !Option(g, Driver, "after"),
            "The final recipient's own gain child also precedes the producing program's following instruction.");
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "gained");
        Drive(g, () => Option(g, Driver, "after"));
        Need(Producer(g).NativeDraw is null && E<NativeDrawAttemptDecidedEvent>(g).Length == 2 &&
            E<NativeDrawAttemptDecidedEvent>(g).All(e => e.ParentFrameId == producerId && !e.Replaced),
            "Both exact request results settle under one original selected-target draw instruction.");
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "after");
        Play(g);
        Need(PaidDraws(g).Length == 2 && E<ProgramSkillResolvedEvent>(g).Count(e =>
            e.FrameId == producerId && e.Completed) == 1, "Each selected participant receives exactly one true draw, including after cold child returns.");
    }

    public static void ZeroRequestAndAbsentCapabilityKeepSynchronousDrawContract()
    {
        foreach (var scene in new[] { "zero", "no-capability" })
        {
            var (g, registry) = Start(scene);
            var before = HandCount(g, 0);
            Use(g, scene == "zero" ? "zero" : "draw");
            Drive(g, () => Option(g, Driver, "after"));
            var producer = Producer(g);
            var bound = producer.CardSetBindings.Single(b => b.Name == "actual");
            var expected = scene == "zero" ? 0 : 2;
            Need(producer.NativeDraw is null && !g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any() &&
                E<NativeDrawAttemptStartedEvent>(g).Length == 0 && E<NativeDrawAttemptDecidedEvent>(g).Length == 0 &&
                PaidDraws(g).Length == expected && bound.CardIds.Count == expected && HandCount(g, 0) == before + expected,
                "A genuine zero-count expression and the absent-capability path retain completed actual results without a synthetic request.");
            if (scene == "no-capability")
                Need(!JsonSerializer.Serialize(producer).Contains("NativeDraw", StringComparison.Ordinal),
                    "The old-capability frame serialization omits the nullable new cursor entirely.");
            g = Cold(g, registry);
            Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "after");
            Play(g);
            Need(PaidDraws(g).Length == expected, "A completed zero or legacy result does not draw again on replay/return.");
        }
    }

    public static void ActualNormalAndExtraDrawWaitForExactAttemptCold()
    {
        foreach (var scene in new[] { "phase-normal", "phase-skip", "phase-extra" })
        {
            var (g, registry) = Start(scene);
            if (scene != "phase-normal")
            {
                var skipped = g.ResolutionStack.OfType<DrawPhaseObligationFrame>().Single();
                Need(skipped is { OwnerSeat: 0, Skipped: true, DrawCount: 0, NativeDraw: null } &&
                    HandCount(g, 0) == 4 && E<NativeDrawAttemptStartedEvent>(g).Length == 0 &&
                    !E<ActualDrawPhaseCompletedEvent>(g).Any(e => e.FrameId == skipped.Id),
                    "A genuinely skipped initial Draw opens its own native observer without inventing a pre-draw request or material.");
                AssertPrivate(g, 0);
                g = Cold(g, registry);
                Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "skipped");
                if (scene == "phase-extra")
                {
                    Drive(g, () => P(g) is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Extra &&
                        p.Choices.Any(c => c.Targets.SequenceEqual([1])));
                    // All four genuine initial hands have four cards: recipient1
                    // is a real least-hand target, with no pre-existing marker.
                    Answer(g, c => c.Targets.SequenceEqual([1]));
                }
                Play(g);
                Need(E<ActualDrawPhaseCompletedEvent>(g).Single(e => e.FrameId == skipped.Id) is
                    { IsExtra: false, Skipped: true, DrawCount: 0 } &&
                    E<DrawPhaseSkippedEvent>(g).Count(e => e.FrameId == skipped.Id) == 1 &&
                    !g.CardMovements.Any(m => m.TurnNumber == skipped.ActualTurnNumber &&
                        m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw),
                    "The skip returns once without a physical normal draw or an early synthetic attempt.");
                if (scene == "phase-skip") { _ = Cold(g, registry); continue; }
                Need(E<ProgramLeastHandMarkerOrDrawEvent>(g).Single() is { TargetSeat: 1, GrantedMarker: true } &&
                    E<ExtraDrawPhaseStartedEvent>(g).Length == 0,
                    "The future extra phase is issued through the real skipped-draw marker operation, not through injected state.");
                g = Cold(g, registry);
                Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
                Drive(g, () => Option(g, Watch, "before", 1));
            }

            var recipient = scene == "phase-extra" ? 1 : 0;
            var normal = g.ResolutionStack.OfType<DrawPhaseObligationFrame>().Single();
            var normalAttempt = g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single();
            var handBefore = HandCount(g, recipient);
            var pileBefore = g.CreateSnapshot(recipient).DrawPileCount;
            Need(normal is { IsExtra: false, Skipped: false, Stage: DrawPhaseObligationStage.RequestingDraw, DrawCount: 0 } &&
                normal.OwnerSeat == recipient && normal.NativeDraw is { ReturnKind: NativeDrawAttemptReturnKind.DrawPhase,
                    RequestedCount: 2, AttemptReturned: false } &&
                normalAttempt.Context.ParentFrameId == normal.Id && normalAttempt.Context.RecipientSeat == recipient &&
                normalAttempt.Context.ActualTurnNumber == normal.ActualTurnNumber &&
                normalAttempt.Context.ActualTurnOwnerSeat == recipient && normalAttempt.Context.Reason == CardMoveReasons.Draw &&
                g.State.CurrentSeat == recipient && g.State.Phase == TurnPhase.Draw &&
                !PhaseDraws(g, recipient, normal.ActualTurnNumber, false).Any(),
                "Normal Draw owns the exact old actual turn, requested count and pre-material native attempt.");
            AssertPrivate(g, recipient);
            g = Cold(g, registry);
            AdvanceRecipient(g, recipient, "before");
            Drive(g, () => Option(g, Gain, "gained", recipient));
            var normalMaterials = PhaseDraws(g, recipient, normal.ActualTurnNumber, false);
            Need(normalMaterials.Length == 2 && HandCount(g, recipient) == handBefore + 2 &&
                g.CreateSnapshot(recipient).DrawPileCount == pileBefore - 2 &&
                g.ResolutionStack.OfType<DrawPhaseObligationFrame>().Single() is
                    { Stage: DrawPhaseObligationStage.DrawChildren, DrawCount: 2, NativeDraw: null } &&
                !E<ActualDrawPhaseCompletedEvent>(g).Any(e => e.FrameId == normal.Id) &&
                E<ExtraDrawPhaseStartedEvent>(g).Length == 0,
                "Real normal Draw2 and its native gain observer precede phase completion and any extra phase.");
            g = CompletePhaseGainObservers(g, registry, normal.Id, recipient, normalMaterials);
            Drive(g, () => Option(g, PhaseWatch, "ended", recipient));
            Need(g.State.Phase == TurnPhase.Draw && !E<ActualDrawPhaseCompletedEvent>(g).Any(e => e.FrameId == normal.Id),
                "The exact normal DrawEnded observer remains inside its owning phase until its native return.");
            g = Cold(g, registry);
            AdvanceRecipient(g, recipient, "ended");

            if (scene == "phase-extra")
            {
                Drive(g, () => Option(g, Watch, "before", recipient));
                var extra = g.ResolutionStack.OfType<DrawPhaseObligationFrame>().Single(f => f.IsExtra);
                var started = E<ExtraDrawPhaseStartedEvent>(g).Single();
                var attempt = g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single();
                Need(extra.ParentFrameId == normal.Id && extra.Id == started.FrameId && started.ParentFrameId == normal.Id &&
                    extra.ActualTurnNumber == normal.ActualTurnNumber && started.ActualTurnNumber == normal.ActualTurnNumber &&
                    g.ResolutionStack.OfType<DrawPhaseObligationFrame>().Single(f => f.Id == normal.Id).Stage == DrawPhaseObligationStage.Finished &&
                    attempt.Context.ParentFrameId == extra.Id && attempt.Context.ActualTurnOwnerSeat == recipient &&
                    attempt.Context.Reason.Value == "program.extra-draw-phase.draw" &&
                    PhaseDraws(g, recipient, normal.ActualTurnNumber, true).Length == 0 && HandCount(g, recipient) == handBefore + 2 &&
                    E<ProgramMarkerExtraDrawConsumedEvent>(g).Length == 1,
                    "The consumed genuine marker creates a distinct extra Draw parent/attempt under the same actual turn, before its own materials.");
                AssertPrivate(g, recipient);
                g = Cold(g, registry);
                AdvanceRecipient(g, recipient, "before");
                Drive(g, () => Option(g, Gain, "gained", recipient));
                Need(PhaseDraws(g, recipient, normal.ActualTurnNumber, true).Length == 2 &&
                    HandCount(g, recipient) == handBefore + 4 &&
                    !E<ActualDrawPhaseCompletedEvent>(g).Any(e => e.FrameId == extra.Id),
                    "Extra Draw2 has its own physical gain child, rather than borrowing the old normal Draw invoice.");
                g = CompletePhaseGainObservers(g, registry, extra.Id, recipient,
                    PhaseDraws(g, recipient, normal.ActualTurnNumber, true));
                Drive(g, () => Option(g, PhaseWatch, "ended", recipient));
                g = Cold(g, registry);
                AdvanceRecipient(g, recipient, "ended");
                Drive(g, () => E<ActualDrawPhaseCompletedEvent>(g).Any(e => e.FrameId == normal.Id));
                Need(E<ActualDrawPhaseCompletedEvent>(g).Single(e => e.FrameId == extra.Id) is
                    { IsExtra: true, Skipped: false, DrawCount: 2 } &&
                    E<NativeDrawAttemptDecidedEvent>(g).Count(e => e.ParentFrameId == extra.Id && !e.Replaced) == 1 &&
                    PhaseDraws(g, recipient, normal.ActualTurnNumber, true).Length == 2,
                    "The extra phase finishes exactly once after cold native children and returns to its original normal parent.");
            }
            else Play(g);
            Need(E<ActualDrawPhaseCompletedEvent>(g).Single(e => e.FrameId == normal.Id) is
                { IsExtra: false, Skipped: false, DrawCount: 2 } &&
                E<NativeDrawAttemptDecidedEvent>(g).Count(e => e.ParentFrameId == normal.Id && !e.Replaced) == 1 &&
                PhaseDraws(g, recipient, normal.ActualTurnNumber, false).Length == 2,
                "Normal Draw completes once with its own actual count/materials, after every exact request and observer return.");
            _ = Cold(g, registry);
        }
    }

    private static CardMovementRecord[] PhaseDraws(GameEngine g, int seat, int turn, bool extra) => g.CardMovements.Where(m =>
        m.TurnNumber == turn && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(seat) &&
        m.Reason == (extra ? new CardMoveReason("program.extra-draw-phase.draw") : CardMoveReasons.Draw)).ToArray();
    private static GameEngine CompletePhaseGainObservers(GameEngine g, ContentRegistry registry, long phaseId,
        int recipient, IReadOnlyList<CardMovementRecord> actual)
    {
        var seenCards = new List<int>();
        var seenBatches = new HashSet<long>();
        // DrawCards currently pays each DrawOne as a separate real batch. Keep
        // the check about actual material coverage rather than assuming that a
        // whole Draw2 is one CardsGained/perBatch observer.
        for (var i = 0; i <= actual.Count; i++)
        {
            if (Option(g, PhaseWatch, "ended", recipient)) break;
            Need(Option(g, Gain, "gained", recipient), "Every actual gain child must be answered at its own exact private prompt. " + Diagnostic(g));
            var window = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
            var batch = window.Batch;
            Need(batch.ParentFrameId == phaseId && seenBatches.Add(batch.Id) && batch.Movements.Count > 0 &&
                batch.Movements.All(m => actual.Any(a => a.Sequence == m.Sequence && a.CardId == m.CardId) &&
                    m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(recipient)) &&
                batch.Movements.All(m => !seenCards.Contains(m.CardId)) &&
                !E<ActualDrawPhaseCompletedEvent>(g).Any(e => e.FrameId == phaseId),
                "A distinct real gain batch covers only this phase's original drawn entities before phase completion.");
            seenCards.AddRange(batch.Movements.Select(m => m.CardId));
            AssertPrivate(g, recipient);
            g = Cold(g, registry);
            AdvanceRecipient(g, recipient, "gained");
            Drive(g, () => Option(g, Gain, "gained", recipient) || Option(g, PhaseWatch, "ended", recipient));
        }
        Need(Option(g, PhaseWatch, "ended", recipient) && seenCards.SequenceEqual(actual.Select(m => m.CardId)),
            "Every original draw material's genuine gain observer returns once, in native batch order, before DrawEnded.");
        return g;
    }
    private static void AdvanceRecipient(GameEngine g, int seat, string option)
    {
        Need(P(g) is { } p && p.PlayerSeat == seat && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option),
            "The exact requested recipient menu must still be active. " + Diagnostic(g));
        if (seat == 0) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == option);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static ProgramSkillFrame Producer(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver);
    private static CardMovementRecord[] PaidDraws(GameEngine g) => g.CardMovements.Where(m => m.Reason.Value == DrawReason).ToArray();
    private static int HandCount(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat].Hand.Count;
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4)
        .Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Option(GameEngine g, string skill, string option, int seat = 0) => P(g) is { } p &&
        p.PlayerSeat == seat && p.SkillPrompt?.SkillId == skill &&
        p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option);
    private static void AssertPrivate(GameEngine g, int recipient)
        => Need(P(g) is { IsPrivate: true } p && p.PlayerSeat == recipient &&
            Enumerable.Range(0, 4).Where(s => s != recipient).All(s => g.CreateSnapshot(s).PendingDecision is null),
            "Only the real recipient sees the private native pre-draw/continuation menu.");
    private static void Need(bool value, string message)
    { if (!value) throw new InvalidOperationException("Native draw scene=" + _scene + ": " + message); }
    private static void Accept(GameEngine g, GameCommand command)
    {
        try
        {
            var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, (result.Error?.Message ?? "Native command rejected") + " " + Diagnostic(g));
        }
        catch (Exception error)
        { throw new InvalidOperationException("Native draw scene=" + _scene + ", " + command.GetType().Name +
            ", cause=" + error.GetType().Name + ": " + error.Message + " " + Diagnostic(g), error); }
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> match)
    {
        var p = P(g)!;
        var c = p.Choices.FirstOrDefault(match);
        Need(c is not null, "Expected exact native choice is absent. " + Diagnostic(g));
        Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision));
    }
    private static void Use(GameEngine g, string activation, IReadOnlyList<int>? targets = null)
        => Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Drive(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Drive(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 50; i++)
        {
            if (done()) return;
            Need(g.State.Winner == Winner.None, "Fixture ended before the required draw boundary. " + Diagnostic(g));
            var p = P(g);
            if (p is { PlayerSeat: 0 })
                throw new InvalidOperationException("Unexpected native draw choice. " + Diagnostic(g));
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Bounded native draw progression missed its boundary. " + Diagnostic(g));
    }
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
        Need(State(g) == State(restored), "Cold replay preserves private choices, exact recipient cursor, actual IDs and once-only child history.");
        return restored;
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new
    {
        _scene, g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase,
        Prompt = P(g) is { } p ? new { p.PlayerSeat, p.Kind, Skill = p.SkillPrompt?.SkillId,
            Choices = p.Choices.Select(c => c.Parameters) } : null,
        Frames = g.ResolutionStack.Select(f => new { f.Id, f.Kind, Type = f.GetType().Name,
            Skill = (f as ProgramSkillFrame)?.SkillId, Cursor = (f as ProgramSkillFrame)?.InstructionIndex,
            Draw = (f as ProgramSkillFrame)?.NativeDraw }),
        Attempts = E<NativeDrawAttemptStartedEvent>(g), Draws = PaidDraws(g)
    });
    private static (GameEngine, ContentRegistry) Start(string scene)
    {
        _scene = scene;
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(g, new StartGameCommand());
        Drive(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        if (scene.StartsWith("phase-", StringComparison.Ordinal))
            Drive(g, () => scene == "phase-normal" ? Option(g, Watch, "before") : Option(g, PhaseWatch, "skipped"));
        else Play(g);
        return (g, registry);
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-native-draw-attempt", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var definitions = new List<Dictionary<string, object>>();
            var presentation = new Dictionary<string, object>();
            void Add(string id, string property, object value, params string[] options)
            {
                definitions.Add(new() { ["id"] = id, ["revision"] = 1, [property] = value });
                presentation[id] = new { name = id, description = "真实摸牌前母帧边界",
                    optionLabels = options.ToDictionary(s => s, s => s) };
            }
            object Choice(string id) => new { op = "chooseOption", target = "owner", resultBind = id,
                options = new[] { new { id } } };
            object Activation(string id, object draw, int targets = 0) => new
            {
                id, minCards = 0, maxCards = 0, sourceZones = new[] { "hand" },
                minTargets = targets, maxTargets = targets, targetKind = "anyLiving", usesPerTurn = (int?)null,
                effects = new[] { draw, Choice("after") }
            };
            Add(Driver, "activations", new[]
            {
                Activation("draw", new { op = "draw", target = "owner", amount = 2, resultBind = "actual" }),
                Activation("pair", new { op = "draw", target = "selectedTargets", amount = 1 }, 2),
                Activation("zero", new { op = "draw", target = "owner", numberExpression = "ownerLostHp", resultBind = "actual" })
            }, "after");
            Add(Quiet, "triggers", new[]
            {
                new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false,
                    effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } }
            });
            var native = scene != "no-capability";
            var phase = scene.StartsWith("phase-", StringComparison.Ordinal);
            object watchCondition = phase
                ? new { kind = "not", children = new[] { new { kind = "phaseIsPlay" } } }
                : new { kind = "phaseIsPlay" };
            if (native) Add(Watch, "triggers", new[]
            {
                new { id = "pre-draw", window = "nativeDrawStarting", subject = "owner",
                    condition = watchCondition,
                    optional = scene.StartsWith("optional", StringComparison.Ordinal),
                    effects = scene == "synchronous"
                        ? new object[] { new { op = "setBooleanState", target = "owner", stateId = "observed", value = true } }
                        : new[] { Choice("before") } }
            }, scene == "synchronous" ? [] : ["before"]);
            if (scene == "synchronous") definitions.Single(d => (string)d["id"] == Watch)["states"] = new[]
            {
                new { id = "observed", initialValue = false, visibility = "private", resetScope = "game",
                    reacquirePolicy = "preserveUntilGameEnd" }
            };
            if (scene == "recipients" || phase) Add(Gain, "triggers", new[]
            {
                new { id = "actual-draw-child", window = "cardsGained", subject = "owner",
                    destinationZones = new[] { "hand" }, movementOccurrence = "perBatch",
                    movementReasons = phase ? new[] { CardMoveReasons.Draw.Value, "program.extra-draw-phase.draw" } : new[] { DrawReason },
                    optional = false, effects = new[] { Choice("gained") } }
            }, "gained");
            if (phase) Add(PhaseWatch, "triggers", new object[]
            {
                new { id = "phase-ended", window = "drawPhaseEnded", subject = "owner", optional = false,
                    effects = new[] { Choice("ended") } },
                new { id = "phase-skipped", window = "drawPhaseSkipped", subject = "owner", optional = false,
                    effects = new[] { Choice("skipped") } }
            }, "ended", "skipped");
            if (scene is "phase-skip" or "phase-extra") Add(Skip, "triggers", new[]
            {
                new { id = "initial-skip", window = "turnStartBeforeNormalFlow", subject = "owner",
                    usageScope = "game", usageLimit = 1, optional = false,
                    effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "draw" } } } }
            });
            if (scene == "phase-extra") Add(Extra, "triggers", new[]
            {
                new { id = "real-marker", window = "drawPhaseSkipped", subject = "owner", priority = -10,
                    usageScope = "game", usageLimit = 1, optional = false, effects = new object[]
                    {
                        new { op = "selectTargets", target = "owner", targetKind = "anyLiving", minimumTargets = 1, maximumTargets = 1,
                            targetAiOrder = "supportDraw" },
                        new { op = "grantLeastHandMarkerOrDraw", target = "selectedTarget", marker = "zi", amount = 1 }
                    } }
            });
            var catalog = SkillProgramCatalog.Load(
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentation }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, id, "原生摸牌请求测试")
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var shared = new List<string>();
            if (native) shared.Add(Watch);
            if (scene == "recipients" || phase) shared.Add(Gain);
            if (phase) shared.Add(PhaseWatch);
            var ownerSkills = new List<string>(shared);
            if (scene is "phase-skip" or "phase-extra") ownerSkills.Add(Skip);
            if (scene == "phase-extra") ownerSkills.Add(Extra);
            b.AddGeneral(new(Owner, "真实摸牌主人", "supporter", Driver, "qun", 8, ownerSkills));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:native-draw-peer-" + i).ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "真实摸牌同桌", "supporter", Quiet, "wei", 8, shared));
            b.AddDeck(new("fixture:native-draw-deck", "固定真实实体", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 72).Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, 7)).ToArray() });
            b.AddMode(new(Mode, "原生摸牌请求", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:native-draw-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
