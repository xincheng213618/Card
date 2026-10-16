using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class YanPuChecks
{
    private const string Huan = "ol:huantu", Bi = "ol:bihuo-yan-pu", Driver = "fixture:yp-driver", Quiet = "fixture:yp-quiet";
    private const string Paid = "fixture:yp-paid", Gain = "fixture:yp-gain", Health = "fixture:yp-health", Skipped = "fixture:yp-skipped";
    private const string OldExit = "fixture:yp-old-exit", Conditional = "fixture:yp-hp-source", LossAtDying = "fixture:yp-dying-source-loss";
    private const string EndingKill = "fixture:yp-earlier-ending-damage";
    private const string PeerIdentity = "fixture:yp-peer-peach-identity";
    private const string Owner = "fixture:yp-owner", Mode = "identity:classic-yan-pu-fixture";

    public static void ActualForeignDrawGiftEndingBranchesRoundQuotaAndSourceLossCold()
    {
        foreach (var scenario in new[] { "huantu", "loss" })
        {
            var (g, registry) = Start(scenario); Use(g, "hurt-peer", [1]); Play(g);
            var firstTurn = g.State.TurnNumber; End(g); Reach(g, () => Optional(P(g), Huan));
            Need(g.State.CurrentSeat == 1 && g.State.Phase == TurnPhase.Draw && V(g, 1).HandCount == 4 &&
                g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w => w.OwnerSeat == 1 && w.Window == SkillProgramTriggerWindow.DrawPhaseStarting),
                "Huantu offers before the other actual native Draw, while the range-one recipient still has only its initial physical hand.");
            g = Cold(g, registry); Activate(g); Reach(g, () => Branch(P(g), "gift"));
            Need(P(g) is { IsPrivate: true, PlayerSeat: 0 } && g.CreateSnapshot(2).PendingDecision is null,
                "The owner alone chooses its actual owned hand/equipment gift; no other viewer receives the private materials.");
            var card = P(g)!.Choices.First().Cards.Single(); g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([card]));
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Paid);
            var promise = E<YanPuHuantuIssuedEvent>(g).Single().Promise;
            Need(promise.RecipientSeat == 1 && promise.ActualTurnOwner == 1 && promise.ActualTurn > firstTurn &&
                V(g, 1).Hand.Any(c => c.Id == card) && g.CardMovements.Count(m => m.CardId == card && m.From == promise.From && m.To == CardLocation.Hand(1) && m.Reason.Value == "skill-program.yan-pu.huantu.gift") == 1 &&
                !E<YanPuHuantuConsumedEvent>(g).Any() && V(g, 0).Skills!.Any(s => s.Id == Huan) == (scenario != "loss"),
                "A single real transfer commits the exact recipient/actual-turn promise before its movement child; source loss does not undo the gift or actual Draw substitution.");
            g = Cold(g, registry); Continue(g); Reach(g, () => Branch(P(g), "recover-draw"), scenario + ": issued actual Ending after the gift child");
            var skipEvents = E<DrawPhaseSkippedEvent>(g).Where(e => e.OwnerSeat == 1 && e.ActualTurnNumber == promise.ActualTurn).ToArray();
            Need(skipEvents.Length == 1, "The substituted actual recipient Draw must issue one native Skipped boundary. Actual=" + JsonSerializer.Serialize(new { promise, Skipped = E<DrawPhaseSkippedEvent>(g).ToArray(), Ended = E<ActualDrawPhaseCompletedEvent>(g).ToArray(), Bindings = E<ProgramBindingResolvedEvent>(g).Where(e => e.SkillId == Skipped).ToArray() }));
            var nativeDraws = g.CardMovements.Where(m => m.TurnNumber == promise.ActualTurn && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(1) && m.Reason == CardMoveReasons.Draw).ToArray();
            Need(nativeDraws.Length == 0, "The exact substituted Draw must produce no normal physical cards. Actual=" + JsonSerializer.Serialize(nativeDraws));
            Need(E<YanPuHuantuConsumedEvent>(g).Single().PromiseId == promise.Id && g.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single().TurnNumber == promise.ActualTurn,
                "Only the same real later Ending consumes the original gift obligation. Actual=" + JsonSerializer.Serialize(new { Consumed = E<YanPuHuantuConsumedEvent>(g).ToArray(), Ending = g.ResolutionStack.OfType<TurnEndingBoundaryFrame>().ToArray() }));
            g = Cold(g, registry);
            if (scenario == "loss")
            {
                var hp = V(g, 1).Hp; var hand = V(g, 1).HandCount;
                Answer(g, c => c.Parameters.GetValueOrDefault("option") == "recover-draw"); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Gain);
                Need(V(g, 1).Hp == hp + 1 && V(g, 1).HandCount == hand + 2 && E<RecoveryAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1),
                    "The issued first branch performs real native recovery before the recipient's two actual private draws, while its original source skill is gone.");
                g = Cold(g, registry); Continue(g);
            }
            else
            {
                var own = V(g, 0).HandCount; var other = V(g, 1).HandCount;
                Answer(g, c => c.Parameters.GetValueOrDefault("option") == "draw-give"); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Gain);
                Need(V(g, 0).HandCount == own + 3 && V(g, 1).HandCount == other,
                    "The second branch actually draws three before selecting any repayment hand cards.");
                g = Cold(g, registry); Continue(g); Reach(g, () => Branch(P(g), "ending-card"));
                var give = P(g)!.Choices.First().Cards.Single(); Answer(g, c => c.Cards.SequenceEqual([give]));
                g = Cold(g, registry); var second = P(g)!.Choices.First().Cards.Single(); Answer(g, c => c.Cards.SequenceEqual([second]));
                Reach(g, () => g.State.TurnNumber > promise.ActualTurn);
                Need(new[] { give, second }.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1) && m.Reason.Value == "skill-program.yan-pu.huantu.ending-gift") == 1) &&
                    E<YanPuHuantuIssuedEvent>(g).Count() == 1,
                    "Exactly two selected current hand entities transfer once, and later Draw phases in the same actual round cannot spend Huantu again.");
                Reach(g, () => g.State.TurnNumber >= 5 && P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }); End(g);
                Reach(g, () => Optional(P(g), Huan)); Activate(g); Reach(g, () => Branch(P(g), "gift")); Answer(g, _ => true);
                Reach(g, () => P(g)?.SkillPrompt?.SkillId == Paid);
                Need(E<YanPuHuantuIssuedEvent>(g).Count() == 2 && E<YanPuHuantuIssuedEvent>(g).Last().Promise.ActualRound > promise.ActualRound,
                    "The real next RoundStarted boundary refreshes Huantu exactly once, without borrowing an earlier skipped phase or Ending.");
            }
            _ = Cold(g, registry);
        }
    }

    public static void RealEquipmentGiftRemovalRecoveryAndPaidDeathChildrenCold()
    {
        var (g, registry) = Start("equipment"); var equip = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        Accept(g, new PlayCardCommand(0, equip.CardId!.Value, [], g.Revision, P(g)!.PromptId)); Play(g); Use(g, "hurt-self"); Play(g);
        var before = V(g, 0).Hp; End(g); Reach(g, () => Optional(P(g), Huan)); Activate(g); Reach(g, () => Branch(P(g), "gift"));
        Answer(g, c => c.Cards.SequenceEqual([equip.CardId.Value])); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health);
        Need(V(g, 0).Hp == before + 1 && V(g, 1).Hand.Any(c => c.Id == equip.CardId) && V(g, 0).Equipment.Count == 0 &&
            E<YanPuHuantuIssuedEvent>(g).Single().Promise.From == CardLocation.Equipment(0) &&
            E<RecoveryAppliedEvent>(g).Any(e => e.TargetSeat == 0 && e.Amount == 1),
            "Giving the real equipped Silver Lion is a native removal and transfer: its owner really recovers before HP/movement observers or the deferred Ending.");
        g = Cold(g, registry); Continue(g); Reach(g, () => Branch(P(g), "recover-draw"));
        Answer(g, c => c.Parameters.GetValueOrDefault("option") == "recover-draw"); Reach(g, () => g.State.TurnNumber > 2);
        Need(g.CardMovements.Count(m => m.CardId == equip.CardId && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Hand(1)) == 1,
            "Cold native removal children neither transfer the equipment twice nor replay its recovery."); _ = Cold(g, registry);

        var (dead, deadRegistry) = Start("death"); End(dead); Reach(dead, () => Optional(P(dead), Huan)); Activate(dead); Reach(dead, () => Branch(P(dead), "gift")); Answer(dead, _ => true);
        Reach(dead, () => dead.ResolutionStack.OfType<DamageFrame>().Any() || dead.ResolutionStack.OfType<DyingFrame>().Any() || !V(dead, 0).IsAlive);
        dead = Cold(dead, deadRegistry); Reach(dead, () => dead.State.Status == EngineStatus.Completed);
        Need(!V(dead, 0).IsAlive && E<YanPuHuantuIssuedEvent>(dead).Count() == 1 && !E<YanPuHuantuConsumedEvent>(dead).Any() &&
            E<DamageAppliedEvent>(dead).Any(e => e.TargetSeat == 0 && e.Amount == 20),
            "Real lethal damage in a paid native gift child completes death before an unstarted Ending choice; the committed gift is never paid again or refunded.");
        _ = Cold(dead, deadRegistry);
        RecipientDeathInsideAnAlreadyEnteredActualEnding();
    }

    private static void RecipientDeathInsideAnAlreadyEnteredActualEnding()
    {
        var (g, registry) = Start("ending-recipient-death"); End(g); Reach(g, () => Optional(P(g), Huan));
        Activate(g); Reach(g, () => Branch(P(g), "gift")); Answer(g, _ => true); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Paid);
        var promise = E<YanPuHuantuIssuedEvent>(g).Single().Promise; g = Cold(g, registry); Continue(g);
        Reach(g, () => Branch(P(g), "draw-give"));
        Need(!V(g, 1).IsAlive && V(g, 0).IsAlive && g.State.TurnNumber == promise.ActualTurn &&
            g.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single() is { OwnerSeat: 1 } ending && ending.TurnNumber == promise.ActualTurn &&
            E<ProgramBindingResolvedEvent>(g).Any(e => e.SkillId == EndingKill && e.Completed) && E<YanPuHuantuConsumedEvent>(g).Single().PromiseId == promise.Id,
            "A higher-priority real Ending sibling kills the recipient after the actual Ending already entered; the same original boundary still offers the living donor's issued Huantu benefit.");
        var hand = V(g, 0).HandCount; g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option") == "draw-give");
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Gain);
        Need(V(g, 0).HandCount == hand + 3 && !V(g, 1).IsAlive,
            "The living donor really draws three in the existing Ending although its original recipient died in the earlier sibling.");
        g = Cold(g, registry); Continue(g); Reach(g, () => g.State.TurnNumber > promise.ActualTurn);
        Need(!g.CardMovements.Any(m => m.Reason.Value == "skill-program.yan-pu.huantu.ending-gift") && E<YanPuHuantuConsumedEvent>(g).Count() == 1,
            "Only the impossible transfer to the dead recipient is skipped; the original obligation and valid owner draw are neither cancelled nor repeated."); _ = Cold(g, registry);
    }

    public static void SurvivedDyingLimitedDrawRoundDistanceLivingCountAndColdNativeReturn()
    {
        foreach (var self in new[] { true, false })
        {
            var (g, registry) = Start(self ? "bihuo-loss" : "bihuo-other");
            Use(g, self ? "dying-self" : "dying-peer", self ? [] : [1]);
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }); Answer(g, c => c.Parameters.GetValueOrDefault("response") == "peach");
            Reach(g, () => Optional(P(g), Bi)); var target = self ? 0 : 1;
            var resolved = E<DyingResolvedEvent>(g).Last(e => e.Survived && e.VictimSeat == target);
            Need(V(g, target).Hp > 0 && !g.ResolutionStack.OfType<DyingFrame>().Any() &&
                g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(w => w.Window == SkillProgramTriggerWindow.DyingExited).YanPuDyingExit?.DyingFrameId == resolved.ResolutionId &&
                g.CardMovements.Any(m => m.To == CardLocation.Hand(target) && m.Reason.Value == "program.dying-transition.draw") &&
                !E<YanPuStartedEvent>(g).Any(),
                "The old mandatory survived-Dying draw resolves before optional Bihuo opens, for self or another character, while the exact completed native Dying retains its paused producer return.");
            var hand = V(g, target).HandCount; var oldDraws = g.CardMovements.Count(m => m.Reason.Value == "program.dying-transition.draw" && m.To == CardLocation.Hand(target));
            g = Cold(g, registry); Activate(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Gain);
            var moreOldDraws = g.CardMovements.Count(m => m.Reason.Value == "program.dying-transition.draw" && m.To == CardLocation.Hand(target)) - oldDraws;
            Need(V(g, target).HandCount == hand + 3 + moreOldDraws && g.CardMovements.Count(m => m.Reason.Value == "skill-program.yan-pu.bihuo.draw" && m.To == CardLocation.Hand(target)) == 3 && !E<YanPuRoundDistanceIssuedEvent>(g).Any() &&
                (!self || V(g, 0).Skills!.All(s => s.Id != Bi)),
                "The exited character really draws three before the incoming-distance policy is issued; paid draw children may remove the source skill without cancelling the issued effect.");
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.YanPu?.Stage == YanPuStage.ExitDrawChildren);
            var gainFrame = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == Gain);
            var activeBatch = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Id == gainFrame.WindowContext!.ParentFrameId).Batch;
            // Inspect only native, command-produced queued invoices. DrawCards
            // creates three real single-card batches; this reads them without
            // injecting a batch, frame, card, use or event into the engine.
            var pending = ((IEnumerable<CardMovementBatchContext>)typeof(GameEngine).GetField("_pendingCardsMovedBatches", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!).ToArray();
            var drawBatches = pending.Append(activeBatch).Where(b => b.ParentFrameId == root.Id && b.Movements.Any(m => m.Reason.Value == "skill-program.yan-pu.bihuo.draw")).ToArray();
            Need(drawBatches.Length == 3 && drawBatches.Select(b => b.Id).Distinct().Count() == 3 && drawBatches.All(b => b.Movements is [var move] &&
                    move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(target) && move.Reason.Value == "skill-program.yan-pu.bihuo.draw") &&
                gainFrame.WindowContext!.MovementBatch?.Id == activeBatch.Id && E<ProgramBindingStartedEvent>(g).Count(e => e.FrameId == gainFrame.Id && e.SkillId == Gain) == 1 &&
                E<YanPuMovementIssuedEvent>(g).Count(e => e.ProgramFrameId == root.Id && e.Movement.Actual == 3) == 1,
                "The actual first Gain binding rides its own live parent batch, while the other two distinct one-card Draw3 batches remain native queued invoices; one accepted draw is not a repeated payment or effect.");
            g = Cold(g, registry); Continue(g); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }, self ? "bihuo-loss: paid gain/native source-loss return" : "bihuo-other: paid gain/native original return");
            var policy = E<YanPuRoundDistanceIssuedEvent>(g).Single(); var source = self ? 1 : 0;
            Need(policy.TargetSeat == target && policy.DyingFrameId == resolved.ResolutionId && Distance(g, source, target) == 5 && Distance(g, target, source) == 1,
                "The exact issued effect adds four living players only to other characters' incoming distance, preserving the beneficiary's outgoing distance.");
            Need(E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == Gain && e.OwnerSeat == target) == (self ? 1 : 3),
                "The fixture's destructive gain child runs once by its own game quota; the unrestricted other-recipient watcher observes all three genuine one-card batches after one Bihuo draw.");
            g = Cold(g, registry); Use(g, "kill", [3]); Play(g);
            var scenario = self ? "bihuo-loss" : "bihuo-other";
            Need(!V(g, 3).IsAlive, "The real lethal native HP loss must finish seat three's death. Actual=" + JsonSerializer.Serialize(new { scenario, g.State.CurrentSeat, g.State.TurnNumber,
                Players = g.State.Players.Select(p => new { p.Seat, p.Hp, p.IsAlive, p.HandCount }).ToArray(), Dying = E<DyingResolvedEvent>(g).ToArray(), Policies = E<YanPuRoundDistanceIssuedEvent>(g).ToArray() }));
            Need(E<YanPuRoundDistanceIssuedEvent>(g).Count() == 1, "A later genuine death must not repeat the already-issued limited effect. Actual=" + JsonSerializer.Serialize(new { scenario, Policies = E<YanPuRoundDistanceIssuedEvent>(g).ToArray(), Starts = E<YanPuStartedEvent>(g).ToArray() }));
            var distance = Distance(g, source, target);
            var contributions = (IEnumerable<RuleQueryContribution>)typeof(GameEngine).GetMethod("YanPuIncomingDistanceContributions", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(g, [target])!;
            Need(distance == 4, "The current round's incoming-distance X must follow the three actually living players, including after source loss. Actual=" + JsonSerializer.Serialize(new { scenario, source, target, distance,
                Round = typeof(GameEngine).GetField("_roundNumber", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g), g.State.CurrentSeat, g.State.TurnNumber, g.State.Phase,
                Players = g.State.Players.Select(p => new { p.Seat, p.Hp, p.IsAlive, p.HandCount }).ToArray(), Policies = E<YanPuRoundDistanceIssuedEvent>(g).ToArray(),
                RoundEvents = E<RoundStartedEvent>(g).ToArray(), Contributions = contributions.Select(c => JsonSerializer.Serialize(c, c.GetType())).ToArray() }));
            if (self)
            {
                var beforeSurvivals = E<DyingResolvedEvent>(g).Count(e => e.Survived && e.VictimSeat == 0);
                Use(g, "restore-bihuo"); Play(g); Use(g, "hurt-self"); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
                Answer(g, c => c.Parameters.GetValueOrDefault("response") == "peach"); Play(g);
                Need(E<DyingResolvedEvent>(g).Count(e => e.Survived && e.VictimSeat == 0) == beforeSurvivals + 1 && E<YanPuRoundDistanceIssuedEvent>(g).Count() == 1,
                    "Regaining the limited skill and genuinely escaping Dying a second time cannot spend its already consumed game quota again.");
            }
            var round = policy.Round; End(g); Reach(g, () => E<RoundStartedEvent>(g).Any(e => e.RoundNumber > round));
            Need(Distance(g, source, target) == 1, "The next actual RoundStarted expires the exact incoming-distance policy; turn arithmetic and seat-count guesses do not control expiry.");
            _ = Cold(g, registry);
        }
        DrawChildDeathKeepsTheLimitedSpendAcrossRegrant();
    }

    private static void DrawChildDeathKeepsTheLimitedSpendAcrossRegrant()
    {
        var (g, registry) = Start("bihuo-target-death"); Use(g, "dying-peer", [1]);
        Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }); Answer(g, c => c.Parameters.GetValueOrDefault("response") == "peach"); Reach(g, () => Optional(P(g), Bi));
        Activate(g); Reach(g, () => g.ResolutionStack.OfType<DyingFrame>().Any() || !V(g, 1).IsAlive);
        Need(E<YanPuStartedEvent>(g).Count(e => e.Stage == YanPuStage.ExitDrawChildren) == 1 && !E<YanPuRoundDistanceIssuedEvent>(g).Any(),
            "A genuine Bihuo draw commits its permanent limited spend before a recipient's actual gain child can kill that recipient.");
        g = Cold(g, registry); Play(g);
        Need(!V(g, 1).IsAlive && V(g, 0).IsAlive && !E<YanPuRoundDistanceIssuedEvent>(g).Any() &&
            E<DyingResolvedEvent>(g).Any(e => e.VictimSeat == 1 && e.Survived) && E<DyingResolvedEvent>(g).Any(e => e.VictimSeat == 1 && !e.Survived),
            "Nested real gain damage and death return to the original already-survived Dying producer without granting a distance effect to the dead beneficiary.");
        Use(g, "lose-bihuo"); Play(g); Need(V(g, 0).Skills!.All(s => s.Id != Bi), "The original source is actually removed before regrant.");
        Use(g, "restore-bihuo"); Play(g); Use(g, "dying-self"); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        Answer(g, c => c.Parameters.GetValueOrDefault("response") == "peach"); Play(g);
        Need(E<DyingResolvedEvent>(g).Any(e => e.VictimSeat == 0 && e.Survived) && E<YanPuStartedEvent>(g).Count(e => e.Stage == YanPuStage.ExitDrawChildren) == 1 &&
            !E<YanPuRoundDistanceIssuedEvent>(g).Any(),
            "A new skill instance cannot repeat Bihuo after the earlier beneficiary died during its paid Draw3 children; permanent spent authority comes from the issued start, independent of distance-policy issuance.");
        _ = Cold(g, registry);
    }

    private static int Distance(GameEngine g, int source, int target)
    {
        var before = State(g); var result = g.GetCombatDistance(source, target);
        Need(State(g) == before, "The native combat-distance query is pure on the real command-produced state."); return result;
    }
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static PlayerSnapshot V(GameEngine g, int s) => g.CreateSnapshot(s).Players[s];
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Branch(PendingDecision? p, string branch) => p?.SkillPrompt?.SkillId == Huan && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == branch);
    private static void Need(bool c, string message) { if (!c) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected YanPu command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done, string diagnostic = "")
    {
        for (var i = 0; i < 300; i++)
        {
            if (done()) return;
            if (g.State.Status == EngineStatus.Completed) throw new InvalidOperationException("YanPu completed before the required actual boundary: " + diagnostic + " " + JsonSerializer.Serialize(new
            { g.State.Status, g.State.Winner, g.State.CurrentSeat, g.State.TurnNumber, g.State.Phase, Players = g.State.Players.Select(p => new { p.Seat, p.Hp, p.IsAlive, p.HandCount }).ToArray(),
                LastFacts = g.Events.Where(e => e.Payload.GetType().Name.Contains("Dying", StringComparison.Ordinal) || e.Payload.GetType().Name.Contains("Damage", StringComparison.Ordinal) ||
                    e.Payload.GetType().Name.Contains("Hp", StringComparison.Ordinal) || e.Payload.GetType().Name.Contains("Turn", StringComparison.Ordinal) || e.Payload.GetType().Name.Contains("YanPu", StringComparison.Ordinal))
                    .TakeLast(40).Select(e => new { Kind = e.Payload.GetType().Name, Payload = JsonSerializer.Serialize(e.Payload, e.Payload.GetType()) }).ToArray() }));
            if (P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true) Continue(g);
            else if (P(g) is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
            else if (P(g) is { PlayerSeat: 0 } human) throw new InvalidOperationException("Unexpected YanPu prompt: " + diagnostic + " " + JsonSerializer.Serialize(new { Prompt = human,
                g.State.CurrentSeat, g.State.TurnNumber, g.State.Phase, Issued = E<YanPuHuantuIssuedEvent>(g).ToArray(), Consumed = E<YanPuHuantuConsumedEvent>(g).ToArray(),
                Bindings = E<ProgramBindingResolvedEvent>(g).Where(e => e.SkillId == Huan || e.SkillId == Bi).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray() }));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("YanPu actual boundary not reached: " + JsonSerializer.Serialize(P(g)));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(cold), "Cold replay preserves every private choice, original gift, actual round/phase/Ending obligation, native Dying return and issued distance policy."); return cold; }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-yan-pu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:yan-pu");
            var bihuo = scenario.StartsWith("bihuo", StringComparison.Ordinal);
            var gainDamageQuota = scenario is "bihuo-loss" or "bihuo-target-death" ? "\"usageScope\":\"game\",\"usageLimit\":1," : "";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"hurt-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                {"id":"hurt-self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"dying-self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":5}]},
                {"id":"dying-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":8}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]},
                {"id":"lose-bihuo","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Bi}}"],"sourceBind":"standard:none"}]},
                {"id":"restore-bihuo","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Bi}}"]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Paid}}","revision":1,"triggers":[{"id":"paid-native-gift","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.yan-pu.huantu.gift"],"optional":false,"effects":[{{(scenario == "loss" ? "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}," : scenario == "death" ? "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}," : "")}}{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Gain}}","revision":1,"triggers":[{"id":"real-benefit-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.yan-pu.huantu.draw","skill-program.yan-pu.bihuo.draw"],"optional":false,{{gainDamageQuota}}"effects":[{{(scenario == "bihuo-loss" ? "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"fixture:yp-dying-source-loss\"]},{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}," : scenario == "bihuo-target-death" ? "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}," : "")}}{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]},
              {"id":"{{Health}}","revision":1,"triggers":[{"id":"real-removal-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]},
              {"id":"{{Skipped}}","revision":1,"triggers":[{"id":"actual-skipped-draw","window":"drawPhaseSkipped","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"skip","options":[{"id":"continue"}]}]}]},
              {"id":"{{OldExit}}","revision":1,"triggers":[{"id":"old-mandatory-exit-draw","window":"dyingExited","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
              {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-huantu","skillId":"{{Huan}}","minimumHp":5}]},
              {"id":"{{LossAtDying}}","revision":1,"triggers":[{"id":"restore-and-lose","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":1,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Bi}}"],"sourceBind":"standard:none"}]}]},
              {"id":"{{EndingKill}}","revision":1,"triggers":[{"id":"actual-prior-sibling","window":"turnEnding","subject":"owner","turnOwnerScope":"otherLiving","priority":1000,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"damage","target":"owner","targetRef":{"kind":"eventTarget"},"amount":20}]}]},
              {"id":"{{PeerIdentity}}","revision":1,"cardIdentities":[{"id":"printed-peach-is-dodge","zones":["hand"],"inputKinds":["peach"],"inputSuits":[],"outputKind":"dodge"}]}
            ]}
            """;
            var ids = new[] { Driver, Quiet, Paid, Gain, Health, Skipped, OldExit, Conditional, LossAtDying, EndingKill, PeerIdentity };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "阎圃真实边界夹具", optionLabels = id is Paid or Gain or Health or Skipped ? new Dictionary<string,string> { ["continue"] = "继续" } : new Dictionary<string,string>() }) }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "真实阎圃规则夹具") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:yp-owner-pick", "固定主人", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            b.AddSkill(new("fixture:yp-peer-pick", "固定其他", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000d : 100000d) });
            var skills = new List<string> { Driver, Gain }; if (bihuo) skills.AddRange([Bi, OldExit]); else skills.AddRange([scenario == "loss" ? Conditional : Huan, Paid]); if (scenario == "equipment") skills.Add(Health);
            if (scenario == "ending-recipient-death") skills.Add(EndingKill);
            b.AddGeneral(new(Owner, "阎圃实际主人", "ol-yan-pu", "fixture:yp-owner-pick", "qun", 4, skills));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:yp-peer-{i}").ToArray();
            foreach (var id in peers) b.AddGeneral(new(id, "实际受益角色", "supporter", "fixture:yp-peer-pick", "qun", 8, bihuo ? [Quiet, Gain, Skipped, OldExit, PeerIdentity] : [Quiet, Gain, Skipped]));
            b.AddCard(new("fixture:yp-lion", "白银狮子", "装备牌", "真实原生移除防具", CardKind.SilverLion));
            b.AddDeck(new("fixture:yp-deck", "固定真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(scenario == "equipment" ? "fixture:yp-lion" : bihuo ? "standard:peach" : "standard:dodge", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "阎圃真实命令", 4, 4, new Dictionary<string,int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:yp-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
