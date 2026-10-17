using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class RoundEndingChecks
{
    private const string Owner = "fixture:round-ending-owner", Mode = "identity:classic-round-ending",
        Driver = "fixture:round-ending-driver", Quiet = "fixture:round-ending-quiet",
        Ending = "fixture:round-ending-program", Movement = "fixture:round-ending-movement",
        Health = "fixture:round-ending-health", Budget = "fixture:round-ending-budget",
        HoldWatch = "fixture:round-ending-hold-return";
    private const string EndingMove = "skill-program." + Ending + ".MoveBoundCards";
    private static string _scene = "";

    public static void OldRoundMovementChildrenAndColdKeepActualTurnIdentity()
    {
        var (g, registry) = Start("movement");
        Play(g);
        var firstCard = V(g, 0).Hand.First().Id;
        Use(g, "top", [firstCard]);
        Drive(g, () => Option(g, Budget, "spent"));
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "spent");
        Play(g);
        Need(BudgetSpentOnce(g) && BudgetRoundUsage(g) == 1,
            "A real first-round movement spends the representative once-per-round binding before closing.");

        End(g);
        Drive(g, () => Option(g, Ending, "before"));
        var started = E<RoundEndingStartedEvent>(g).Single();
        Need(started.RoundNumber == 1 && started.ActualTurnNumber == 4 && started.ActorSeat == 3 &&
            started.NextNormalSeat == 0, "The first old-round root belongs to the last real normal actor. " + Diagnostic(g));
        AssertOldBoundary(g, started);
        Need(Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null),
            "The actual private round-return choice is hidden from unrelated viewers.");
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "before");

        var selected = new List<int>();
        for (var i = 0; i < 2; i++)
        {
            Drive(g, () => P(g) is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Ending &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
            var id = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards").Cards.Single();
            selected.Add(id);
            Answer(g, c => c.Cards.SequenceEqual([id]));
        }
        Drive(g, () => Option(g, Movement, "movement"));
        var moved = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
        var paid = moved.Batch.Movements.Where(m => m.Reason.Value == EndingMove).ToArray();
        Need(paid.Length == 2 && paid.Select(m => m.CardId).SequenceEqual(selected) &&
            paid.All(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.DrawPile) &&
            moved.Batch.SourceCounts.Single(s => s.Location == CardLocation.Hand(0)).CountBefore -
                moved.Batch.SourceCounts.Single(s => s.Location == CardLocation.Hand(0)).CountAfter == 2,
            "The genuine old-round return pays exactly the selected Hand2 in one native atomic batch.");
        AssertOldBoundary(g, started);
        Need(BudgetSpentOnce(g) && BudgetRoundUsage(g) == 1,
            "Round usage has not reset while the old-round material child is active.");
        g = Cold(g, registry);
        var hp = V(g, 0).Hp;
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "movement");
        Drive(g, () => Option(g, Health, "health"));
        Need(V(g, 0).Hp == hp - 1 && g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(),
            "The actual movement observer's native HP-loss child suspends the same old round.");
        AssertOldBoundary(g, started);
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "health");
        Drive(g, () => Option(g, Movement, "after-hp"));
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "after-hp");
        Drive(g, () => Option(g, Ending, "after"));
        AssertOldBoundary(g, started);
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "after");
        Drive(g, () => E<RoundEndedEvent>(g).Any(e => e.BoundaryFrameId == started.BoundaryFrameId));
        Play(g);
        AssertClosedBeforeNextRound(g, started);
        Need(g.CardMovements.Count(m => m.Reason.Value == EndingMove) == 2 &&
            BudgetSpentOnce(g) && BudgetRoundUsage(g) == 0 &&
            E<ProgramSkillHpLostEvent>(g).Count(e => e.SkillId == Movement && e.Amount == 1) == 1,
            "Cold continuations do not repay the Hand2, HP loss or old once-per-round observer.");
        _ = Cold(g, registry);
    }

    public static void LegacyRoundHoldsReturnBeforeOldRoundEndsCold()
    {
        foreach (var scene in new[] { "holds-boundary", "holds-legacy" })
        {
            var (g, registry) = Start(scene);
            Drive(g, () => P(g) is { PlayerSeat: 0 } p && p.Choices.Any(c =>
                c.Parameters.GetValueOrDefault("skill-id") == "magic:hanguo" &&
                c.Parameters.GetValueOrDefault("program-action") == "activate"));
            Answer(g, c => c.Parameters.GetValueOrDefault("skill-id") == "magic:hanguo" &&
                c.Parameters.GetValueOrDefault("program-action") == "activate");
            Drive(g, () => P(g) is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == "magic:hanguo" &&
                p.Choices.Any(c => c.Targets.SequenceEqual([1])));
            Answer(g, c => c.Targets.SequenceEqual([1]));
            Play(g);
            var focus = E<DemonRoundFocusEvent>(g).Single();
            var hold = g.CardMovements.Where(m => m.Reason.Value == "skill-program.hanguo.hold").ToArray();
            var ids = hold.Select(m => m.CardId).ToArray();
            Need(focus.Source.OwnerSeat == 0 && focus.TargetSeat == 1 && focus.RoundNumber == 1 &&
                ids.Length == 4 && ids.Distinct().Count() == 4 &&
                hold.All(m => m.From == CardLocation.Hand(1) && m.To == CardLocation.RoundHold(1)) &&
                g.CreateCardZoneDiagnostics().Where(c => c.Location == CardLocation.RoundHold(1))
                    .Select(c => c.CardId).Order().SequenceEqual(ids.Order()),
                "The genuine first-round Hanguo invocation holds the peer's exact original Hand4, without injected cards.");
            End(g);
            Drive(g, () => P(g) is { PlayerSeat: 1 } p && p.SkillPrompt?.SkillId == HoldWatch &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "hold-return"));
            var moved = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
            var returns = moved.Batch.Movements.Where(m => m.Reason.Value == "skill-program.hanguo.return").ToArray();
            Need(returns.Length == 4 && returns.Select(m => m.CardId).Order().SequenceEqual(ids.Order()) &&
                returns.All(m => m.From == CardLocation.RoundHold(1) && m.To == CardLocation.Hand(1)) &&
                moved.Batch.SourceCounts.Single(c => c.Location == CardLocation.RoundHold(1)) is
                    { CountBefore: 4, CountAfter: 0 } &&
                g.CreateSnapshot(0).PendingDecision is null &&
                P(g) is { PlayerSeat: 1, IsPrivate: true } &&
                g.CreateCardZoneDiagnostics().All(c => c.Location != CardLocation.RoundHold(1)),
                "The real AI holder's gain window observes one actual whole return batch after physical payment.");

            RoundEndingStartedEvent? started = null;
            if (scene == "holds-boundary")
            {
                started = E<RoundEndingStartedEvent>(g).Single();
                var root = g.ResolutionStack.OfType<RoundEndingBoundaryFrame>().Single();
                Need(root.Id == started.BoundaryFrameId && root.Stage == RoundEndingBoundaryStage.Programs &&
                    root.ActiveWindowFrameId == moved.Id && moved.ResumeRoundEndingBoundaryId == root.Id &&
                    moved.Batch.ParentFrameId == root.Id && moved.Batch.AwaitingProgramFrameId is null &&
                    !g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>()
                        .Any(w => w.Window == SkillProgramTriggerWindow.RoundEnding) &&
                    E<RoundStartedEvent>(g).Select(e => e.RoundNumber).SequenceEqual([1]) &&
                    E<RoundEndedEvent>(g).Length == 0 && g.State.TurnNumber == 4 && g.State.CurrentSeat == 3 &&
                    !E<ActualTurnCursorReturnedEvent>(g).Any(e => e.ActualTurn == 4),
                    "Native old holds drain under their exact StagePrograms movement return before RoundEnding programs, cursor return or round reset.");
            }
            else
            {
                var facts = g.Events.Select(e => e.Payload).ToArray();
                var lastReturn = Array.FindLastIndex(facts, e => e is CardMovedEvent m &&
                    ids.Contains(m.CardId) && m.Reason.Value == "skill-program.hanguo.return");
                var roundTwo = Array.FindIndex(facts, e => e is RoundStartedEvent r && r.RoundNumber == 2);
                Need(E<RoundEndingStartedEvent>(g).Length == 0 && E<RoundEndedEvent>(g).Length == 0 &&
                    !g.ResolutionStack.OfType<RoundEndingBoundaryFrame>().Any() &&
                    moved.ResumeRoundEndingBoundaryId is null && lastReturn >= 0 && roundTwo > lastReturn,
                    "Without any new RoundEnding capability the original BeginRound fallback returns the same real holds before its own RoundStarted.");
            }
            g = Cold(g, registry);
            // The actual holder is an AI. Its published native private choice must advance normally.
            Accept(g, new AdvanceOneStepCommand(g.Revision));
            if (scene == "holds-boundary")
            {
                Drive(g, () => Option(g, Ending, "before"));
                Need(E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == HoldWatch &&
                    e.OwnerSeat == 1 && e.Activated && e.Completed) == 1,
                    "The real return-gain observer fully resolves before old-round lifecycle items are entered.");
                AssertOldBoundary(g, started!);
                g = Cold(g, registry);
                Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "before");
            }
            Play(g);
            Need(g.State.TurnNumber == 5 && g.State.CurrentSeat == 0 &&
                E<RoundStartedEvent>(g).Select(e => e.RoundNumber).SequenceEqual([1, 2]) &&
                g.CardMovements.Count(m => m.Reason.Value == "skill-program.hanguo.return") == ids.Length &&
                ids.All(id => g.CardMovements.Count(m => m.CardId == id &&
                    m.Reason.Value == "skill-program.hanguo.hold") == 1 &&
                    g.CardMovements.Count(m => m.CardId == id &&
                    m.Reason.Value == "skill-program.hanguo.return") == 1) &&
                E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == HoldWatch &&
                    e.OwnerSeat == 1 && e.Activated && e.Completed) == 1 &&
                E<DemonRoundFocusEvent>(g).Length == 1,
                "Both paths return every genuine held entity once, drain the true observer and preserve the next normal turn without reholding or repayment.");
            if (started is not null) AssertClosedBeforeNextRound(g, started);
            _ = Cold(g, registry);
        }
    }

    public static void PendingAndClosingExtraTurnsSkippedTurnsAndPopulationCold()
    {
        foreach (var scene in new[] { "pending", "closing-extra", "population" })
        {
            var (g, registry) = Start(scene);
            Play(g);
            if (scene == "pending")
            {
                Use(g, "extra");
                Play(g);
                End(g);
                Play(g);
                Need(g.State.TurnNumber == 2 && g.State.CurrentSeat == 0 &&
                    E<TurnStartedEvent>(g).Select(e => e.ActorSeat).SequenceEqual([0, 0]) &&
                    E<RoundEndingStartedEvent>(g).Length == 0 && E<RoundStartedEvent>(g).Length == 1,
                    "The already pending genuine extra turn runs before any normal-successor round closure.");
                g = Cold(g, registry);
            }
            else if (scene == "population")
            {
                Use(g, "flip", targets: [1]); Play(g);
                Use(g, "flip", targets: [3]); Play(g);
                Need(V(g, 1).IsFaceDown && V(g, 3).IsFaceDown,
                    "Both skipped actual turns are prepared by real native face-state commands.");
            }
            End(g);
            Drive(g, () => Option(g, Ending, "before"));
            var started = E<RoundEndingStartedEvent>(g).Single();
            AssertOldBoundary(g, started);
            if (scene == "pending")
                Need(started.ActualTurnNumber == 5 && started.ActorSeat == 3 &&
                    E<TurnStartedEvent>(g).Select(e => e.ActorSeat).SequenceEqual([0, 0, 1, 2, 3]),
                    "The deferred normal seat resumes after the real extra turn, without a spurious new round.");
            if (scene == "population")
                Need(g.ResolutionStack.OfType<RoundEndingBoundaryFrame>().Single().SkippedTurn &&
                    !V(g, 1).IsFaceDown && !V(g, 3).IsFaceDown &&
                    !E<PhaseChangedEvent>(g).Any(e => e.Phase == TurnPhase.Play && e.ActorSeat is 1 or 3),
                    "Face-up skipped turns retain their actual start/end cursors without manufacturing Play phases.");

            g = Cold(g, registry);
            Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "before");
            if (scene == "closing-extra")
            {
                Drive(g, () => Option(g, Ending, "pended"));
                Need(E<ProgramExtraTurnPendedEvent>(g).Count(e => e.SkillId == Ending && e.Seat == 0) == 1,
                    "A real native child may pend an extra turn while the old actual cursor still exists.");
                AssertOldBoundary(g, started);
                g = Cold(g, registry);
                Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "pended");
                Play(g);
                Need(g.State.CurrentSeat == 0 && g.State.TurnNumber == started.ActualTurnNumber + 1 &&
                    E<RoundEndedEvent>(g).Count(e => e.RoundNumber == 1) == 1 &&
                    E<RoundStartedEvent>(g).Length == 1,
                    "The closing child's extra turn starts in the still-numbered old round after exactly one RoundEnded.");
                g = Cold(g, registry);
                End(g);
                Play(g);
                Need(g.State.TurnNumber == started.ActualTurnNumber + 2 &&
                    E<RoundEndingStartedEvent>(g).Count(e => e.RoundNumber == 1) == 1 &&
                    E<RoundEndedEvent>(g).Count(e => e.RoundNumber == 1) == 1,
                    "The extra turn cannot close or repay the already closed old round before the saved normal successor.");
            }
            else if (scene == "population")
            {
                Drive(g, () => P(g) is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Ending &&
                    p.Choices.Any(c => c.Targets.SequenceEqual([started.ActorSeat])));
                Answer(g, c => c.Targets.SequenceEqual([started.ActorSeat]));
                Drive(g, () => Option(g, Ending, "after-death"));
                Need(!V(g, started.ActorSeat).IsAlive && V(g, 1).IsAlive && V(g, 2).IsAlive &&
                    E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == started.ActorSeat && e.Amount == 20) == 1 &&
                    E<PlayerDiedEvent>(g).Count(e => e.VictimSeat == started.ActorSeat) == 1,
                    "The closing child's real native damage/dying/death completes while another Rebel keeps the game live.");
                AssertOldBoundary(g, started);
                g = Cold(g, registry);
                Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "after-death");
                Play(g);
                Need(g.State.CurrentSeat == 0 && !V(g, 3).IsAlive,
                    "A dead old actual owner does not receive a corpse turn or block the surviving normal successor.");
            }
            else
            {
                Drive(g, () => E<RoundEndedEvent>(g).Any(e => e.BoundaryFrameId == started.BoundaryFrameId));
                Play(g);
            }
            AssertClosedBeforeNextRound(g, started);
            Need(E<TurnEndedEvent>(g).Count(e => e.TurnNumber == started.ActualTurnNumber &&
                e.ActorSeat == started.ActorSeat) == 1, "Round closing must never replay the old TurnEnded or expiration work.");
            _ = Cold(g, registry);
        }
    }

    public static void BeiMiHuPreludeStartsOnlyAfterOldRoundEndingCold()
    {
        var (g, registry) = Start("prelude");
        Play(g);
        Need(E<RoundEndingStartedEvent>(g).Length == 0 && E<RoundStartedEvent>(g).Length == 1 &&
            E<BeiMiHuKuiPaidEvent>(g).Count(e => e.Kind == "round" && e.Round == 1) == 1,
            "The genuine first-round Bei Mi Hu prelude has no imaginary previous round to close.");
        End(g);
        Drive(g, () => Option(g, Ending, "before"));
        var started = E<RoundEndingStartedEvent>(g).Single();
        AssertOldBoundary(g, started);
        Need(E<BeiMiHuKuiPaidEvent>(g).All(e => e.Round == 1),
            "The next Bei Mi Hu round prelude cannot enter while an old-round child still owns the boundary.");
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "before");
        Drive(g, () => g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f =>
            f.BeiMiHu is { Stage: "round-choice", Round: 2 }));
        var prelude = g.ResolutionStack.OfType<BeiMiHuTurnPreludeFrame>().Single();
        Need(prelude.RoundAdvanced && prelude.Proof.ActualRoundNumber == 2 &&
            prelude.Proof.UpcomingTurnNumber == 5 && prelude.Proof.UpcomingOwnerSeat == 0 &&
            E<TurnStartedEvent>(g).Last().TurnNumber == 4 &&
            E<RoundEndedEvent>(g).Count(e => e.RoundNumber == 1) == 1,
            "The exact new-round prelude still precedes its upcoming TurnStarted and retains RoundAdvanced.");
        g = Cold(g, registry);
        Answer(g, c => c.Targets.Count == 1);
        Play(g);
        AssertClosedBeforeNextRound(g, started);
        Need(E<BeiMiHuKuiPaidEvent>(g).Count(e => e.Kind == "round" && e.Round == 1) == 1 &&
            E<BeiMiHuKuiPaidEvent>(g).Count(e => e.Kind == "round" && e.Round == 2) == 1 &&
            E<RoundStartedEvent>(g).Select(e => e.RoundNumber).SequenceEqual([1, 2]),
            "The old round closes once; each actual new-round prelude pays exactly one real first-round marker.");
        _ = Cold(g, registry);
    }

    private static void AssertOldBoundary(GameEngine g, RoundEndingStartedEvent started)
    {
        var parent = g.ResolutionStack.OfType<RoundEndingBoundaryFrame>().Single();
        var window = g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>()
            .Single(w => w.Window == SkillProgramTriggerWindow.RoundEnding);
        Need(parent.Id == started.BoundaryFrameId &&
            window.Continuation == ProgramLifecycleContinuation.CompleteRoundEndingBoundary &&
            window.RoundEndingBoundaryId == parent.Id &&
            g.State.TurnNumber == started.ActualTurnNumber && g.State.CurrentSeat == started.ActorSeat &&
            E<TurnStartedEvent>(g).Last() == new TurnStartedEvent(started.ActualTurnNumber, started.ActorSeat) &&
            E<RoundStartedEvent>(g).Last().RoundNumber == started.RoundNumber &&
            !E<RoundEndedEvent>(g).Any(e => e.RoundNumber == started.RoundNumber) &&
            !E<ActualTurnCursorReturnedEvent>(g).Any(e => e.ActualTurn == started.ActualTurnNumber &&
                e.OwnerSeat == started.ActorSeat),
            "Every child keeps the exact old-round root, actual owner, turn and pre-cursor identity. " + Diagnostic(g));
    }

    private static void AssertClosedBeforeNextRound(GameEngine g, RoundEndingStartedEvent started)
    {
        var facts = g.Events.Select(e => e.Payload).ToArray();
        var endIndex = Array.FindIndex(facts, e => e is RoundEndedEvent r && r.BoundaryFrameId == started.BoundaryFrameId);
        var cursorIndex = Array.FindIndex(facts, e => e is ActualTurnCursorReturnedEvent t &&
            t.ActualTurn == started.ActualTurnNumber && t.OwnerSeat == started.ActorSeat);
        var newRoundIndex = Array.FindIndex(facts, e => e is RoundStartedEvent r && r.RoundNumber == started.RoundNumber + 1);
        var newTurnIndex = Array.FindIndex(facts, e => e is TurnStartedEvent t &&
            t.TurnNumber > started.ActualTurnNumber);
        Need(E<RoundEndedEvent>(g).Count(e => e.RoundNumber == started.RoundNumber &&
                e.BoundaryFrameId == started.BoundaryFrameId && e.ActualTurnNumber == started.ActualTurnNumber &&
                e.ActorSeat == started.ActorSeat) == 1 &&
            endIndex >= 0 && cursorIndex > endIndex && newRoundIndex > cursorIndex && newTurnIndex > cursorIndex &&
            !g.ResolutionStack.OfType<RoundEndingBoundaryFrame>().Any(),
            "Exactly one old RoundEnded precedes actual cursor return and the next native round/turn initialization. " + Diagnostic(g));
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static bool BudgetSpentOnce(GameEngine g) =>
        E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == Budget && e.Activated && e.Completed) == 1 &&
        E<SkillUsageConsumedEvent>(g).Where(e => e.SkillOwnerSeat == 0 && e.SkillId == Budget &&
            e.Scope == SkillUsageScope.Round).Sum(e => e.Count) == 1;
    private static int BudgetRoundUsage(GameEngine g) => (V(g, 0).SkillRuntimeStates ?? [])
        .Where(s => s.SkillId == Budget).SelectMany(s => s.Usages)
        .Where(u => u.Scope == SkillUsageScope.Round).Sum(u => u.Count);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4)
        .Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Option(GameEngine g, string skill, string option) => P(g) is { PlayerSeat: 0 } p &&
        p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option);
    private static void Need(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("RoundEnding scene=" + _scene + ": " + message); }
    private static void Accept(GameEngine g, GameCommand command)
    {
        try
        {
            var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, (result.Error?.Message ?? "Rejected genuine boundary command.") + " " + Diagnostic(g));
        }
        catch (Exception error)
        {
            throw new InvalidOperationException("RoundEnding scene=" + _scene + ", command=" +
                command.GetType().Name + ", cause=" + error.GetType().Name + ": " + error.Message +
                " " + Diagnostic(g), error);
        }
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        var p = P(g)!;
        var choice = p.Choices.FirstOrDefault(predicate);
        Need(choice is not null, "The exact expected native choice is absent. " + Diagnostic(g));
        Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, g.Revision));
    }
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null)
        => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g)
        => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Drive(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Drive(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 220; step++)
        {
            if (done()) return;
            Need(g.CreateSnapshot(0).Winner == Winner.None, "The bounded fixture ended before its requested boundary. " + Diagnostic(g));
            var p = P(g);
            if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { PlayerSeat: 0 } && g.ResolutionStack.OfType<ProgramSkillFrame>()
                .Any(f => f.BeiMiHu?.Stage is "round-choice" or "before-turn-choice"))
                Answer(g, c => c.Targets.Count == 1);
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c =>
                c.Parameters.GetValueOrDefault("program-action") == "skip"))
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId is Budget or Movement or Health)
                Answer(g, _ => true);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying or DecisionKind.RespondDodge or
                DecisionKind.RespondSlash or DecisionKind.Nullification })
                Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else if (p is { PlayerSeat: 0 })
                throw new InvalidOperationException("Unexpected real RoundEnding choice. " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("RoundEnding bounded progression did not reach the requested native child. " + Diagnostic(g));
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
        Need(State(g) == State(restored), "Cold replay preserves the exact old-round stage, real materials, source and once-only typed child return.");
        return restored;
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new
    {
        Scene = _scene, g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase,
        Prompt = P(g) is { } p ? new { p.PlayerSeat, p.Kind, Skill = p.SkillPrompt?.SkillId,
            Choices = p.Choices.Take(5).Select(c => new { c.Cards, c.Targets, c.Parameters }) } : null,
        Frames = g.ResolutionStack.Select(f => new { f.Id, f.Kind, Type = f.GetType().Name,
            Skill = (f as ProgramSkillFrame)?.SkillId, Index = (f as ProgramSkillFrame)?.InstructionIndex }),
        RoundStarts = E<RoundStartedEvent>(g), RoundEnding = E<RoundEndingStartedEvent>(g),
        RoundEnds = E<RoundEndedEvent>(g), Turns = E<TurnStartedEvent>(g),
        Players = g.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.Hp, p.IsAlive, p.IsFaceDown })
    });
    private static (GameEngine, ContentRegistry) Start(string scene)
    {
        _scene = scene;
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
        Accept(g, new StartGameCommand());
        Drive(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        return (g, registry);
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-round-ending", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            if (scene == "prelude") CharacterFixtureContent.Register(b, "ol:bei-mi-hu");
            if (scene is "holds-boundary" or "holds-legacy")
                // The original grant leaves (magic:wushen/rende/qingnang/longyin)
                // are registered by this other original manifest in the same bundle.
                CharacterFixtureContent.Register(b, "magic:guan-yu", "mou:guan-yu-mobile");
            var definitions = new List<Dictionary<string, object>>();
            var presentations = new Dictionary<string, object>();
            void Add(string id, string property, object nodes, params string[] options)
            {
                definitions.Add(new() { ["id"] = id, ["revision"] = 1, [property] = nodes });
                presentations[id] = new
                {
                    name = id, description = "真实旧轮边界及原生子树",
                    optionLabels = options.ToDictionary(option => option, option => option)
                };
            }
            object Choice(string option) => new
            {
                op = "chooseOption", target = "owner", resultBind = option,
                options = new[] { new { id = option } }
            };
            object[] Top(string bind) =>
            [
                new { op = "captureSelectedCards", target = "owner", resultBind = bind },
                new { op = "moveBoundCards", target = "owner", sourceBind = bind,
                    destination = "drawPileTop", awaitMovementTriggers = true }
            ];
            object Activation(string id, object[] effects, int cards = 0, int targets = 0) => new
            {
                id, minCards = cards, maxCards = cards, sourceZones = new[] { "hand" },
                minTargets = targets, maxTargets = targets, targetKind = "anyLiving", usesPerTurn = (int?)null, effects
            };
            Add(Driver, "activations", new[]
            {
                Activation("top", Top("top"), cards: 1),
                Activation("extra", [new { op = "pendExtraTurn", target = "owner" }]),
                Activation("flip", [new { op = "setFaceState", target = "selectedTarget", faceDown = true }], targets: 1)
            });
            Add(Quiet, "triggers", new[]
            {
                new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false,
                    effects = new object[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } }
            });
            var endingEffects = new List<object> { Choice("before") };
            if (scene == "movement")
            {
                endingEffects.Add(new { op = "selectOwnedCards", target = "owner", minimumCards = 2,
                    maximumCards = 2, zones = new[] { "hand" }, resultBind = "return" });
                endingEffects.Add(new { op = "moveBoundCards", target = "owner", sourceBind = "return",
                    destination = "drawPileTop", awaitMovementTriggers = true });
                endingEffects.Add(Choice("after"));
            }
            else if (scene == "closing-extra")
            {
                endingEffects.Add(new { op = "pendExtraTurn", target = "owner" });
                endingEffects.Add(Choice("pended"));
            }
            else if (scene == "population")
            {
                endingEffects.Add(new { op = "selectTarget", target = "owner", targetKind = "otherLiving" });
                endingEffects.Add(new { op = "damage", target = "selectedTarget", amount = 20 });
                endingEffects.Add(Choice("after-death"));
            }
            if (scene != "holds-legacy") Add(Ending, "triggers", new[]
            {
                new { id = "old-round-return", window = "roundEnding", subject = "owner", optional = false,
                    effects = endingEffects.ToArray() }
            }, scene == "movement" ? ["before", "after"] : scene == "closing-extra" ? ["before", "pended"] :
                scene == "population" ? ["before", "after-death"] : ["before"]);
            Add(Budget, "triggers", new[]
            {
                new { id = "once-in-old-round", window = "cardsMoved", subject = "owner",
                    sourceZones = new[] { "hand" }, movementOccurrence = "perBatch",
                    movementReasons = new[] { "skill-program." + Driver + ".MoveBoundCards", EndingMove },
                    usageScope = "round", usageLimit = 1, optional = false, effects = new[] { Choice("spent") } }
            }, "spent");
            Add(Movement, "triggers", new[]
            {
                new { id = "real-return-child", window = "cardsMoved", subject = "owner",
                    sourceZones = new[] { "hand" }, movementOccurrence = "perBatch",
                    movementReasons = new[] { EndingMove }, optional = false,
                    effects = new object[] { Choice("movement"), new { op = "loseHp", target = "owner", amount = 1 }, Choice("after-hp") } }
            }, "movement", "after-hp");
            Add(Health, "triggers", new[]
            {
                new { id = "real-hp-child", window = "afterHpLost", subject = "owner", optional = false,
                    effects = new[] { Choice("health") } }
            }, "health");
            if (scene is "holds-boundary" or "holds-legacy")
                Add(HoldWatch, "triggers", new[]
                {
                    new { id = "actual-round-hold-return", window = "cardsGained", subject = "owner",
                        destinationZones = new[] { "hand" }, movementOccurrence = "perBatch",
                        movementReasons = new[] { "skill-program.hanguo.return" }, optional = false,
                        effects = new[] { Choice("hold-return") } }
                }, "hold-return");
            var catalog = SkillProgramCatalog.Load(
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, id, "真实旧轮与原生返回")
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var ownerSkills = scene == "holds-legacy" ? new List<string>() : new List<string> { Ending };
            if (scene == "movement") ownerSkills.AddRange([Budget, Movement, Health]);
            if (scene == "prelude") ownerSkills.Add("ol:zongkui");
            if (scene is "holds-boundary" or "holds-legacy") ownerSkills.Add("magic:hanguo");
            b.AddGeneral(new(Owner, "真实旧轮主人", "supporter", Driver, "qun", 8, ownerSkills));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:round-ending-peer-" + i).ToArray();
            foreach (var peer in peers)
                b.AddGeneral(new(peer, "真实安静同桌", "supporter", Quiet, "wei", 8,
                    scene is "holds-boundary" or "holds-legacy" ? [HoldWatch] : []));
            b.AddDeck(new("fixture:round-ending-deck", "固定连弩实体", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 96)
                    .Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, 7)).ToArray()
            });
            b.AddMode(new(Mode, "真实旧轮结束", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:round-ending-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
