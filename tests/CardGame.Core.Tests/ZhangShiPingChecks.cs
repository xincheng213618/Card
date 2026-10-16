using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhangShiPingChecks
{
    private const string Hongji = "ol:hongji", XingGu = "ol:xinggu", Driver = "fixture:zsp-driver", Owner = "fixture:zsp-owner";
    private const string Watch = "fixture:zsp-move", DrawWatch = "fixture:zsp-draw", Boundary = "fixture:zsp-boundary", Prep = "fixture:zsp-prep";
    private const string Conditional = "fixture:zsp-conditional", Skip = "fixture:zsp-skip", Quiet = "fixture:zsp-quiet";
    private const string ReceiverWatch = "fixture:zsp-receiver-watch";
    private const string PeerCost = "fixture:zsp-peer-cost", LateDeath = "fixture:zsp-late-death", Death = "fixture:zsp-death";
    private const string Mode = "identity:classic-zhang-shi-ping-fixture";

    public static void TiedSelfDrawForeignPlayTwoRoundBranchesActualPhasesAndCold()
    {
        var (g, registry) = Start("draw"); var initial = V(g, 0).HandCount; var turn = g.State.TurnNumber;
        Need(V(g, 0).PublicPersistentPileCards is { Count: 3 } pile && pile.Select(c => c.Id).Distinct().Count() == 3 &&
            Enumerable.Range(0, 4).All(s => V(g, s).HandCount == initial) &&
            E<ZhangShiPingXingGuCardStoredEvent>(g).Count() == 3 &&
            g.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To.Zone == CardZoneKind.PublicPersistentPile && m.To.OwnerSeat == 0) == 3,
            "Game start moves three distinct real random mounts from the draw pile to a public source pile without changing anyone's tied initial hands.");
        g = Cold(g, registry); Activate(g); Reach(g, () => Branch(P(g), "draw"));
        Need(Branch(P(g), "play") && P(g)!.Choices.Count == 2, "A tied self recipient qualifies for either independently unused branch, with one actual preparation choice.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "draw");
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == DrawWatch && E<ZhangShiPingHongjiConsumedEvent>(g).Any());
        Need(E<ZhangShiPingHongjiGrantedEvent>(g).Single() is { RecipientSeat: 0, Phase: TurnPhase.Draw } &&
            E<ProgramPhaseScheduledEvent>(g).Count(e => e.SkillId == Hongji && e.Started && e.Phase == TurnPhase.Draw) == 1 &&
            E<ActualDiscardRecoveryPhaseStartedEvent>(g).Count(e => e.Phase.TurnNumber == turn && e.Phase.ActorSeat == 0 && e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Draw) == 2,
            "The promise waits through a real Draw ending and inserts a second genuine Draw phase in the same actual turn.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(V(g, 0).HandCount == initial + 4 && E<ZhangShiPingHongjiReturnedEvent>(g).Count() == 1 &&
            E<TurnStartedEvent>(g).Count(e => e.ActorSeat == 0) == 1 &&
            E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == Boundary && e.BindingId == "draw-end" && e.OwnerSeat == 0 && e.Completed) == 2,
            "Both native Draw phases draw their actual two cards and complete real phase-end observers, without creating another preparation or whole turn.");
        Use(g, "draw-other", [2]); Play(g); End(g);
        Reach(g, () => Optional(P(g), Hongji) && P(g)!.TargetSeat == 2); Activate(g); Reach(g, () => Branch(P(g), "play"));
        Need(!Branch(P(g), "draw"), "The source has spent only its Draw quota; the later maximum-hand other recipient still qualifies for the independent Play branch in this round.");
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "play");
        Reach(g, () => E<ZhangShiPingHongjiReturnedEvent>(g).Any(e => e.RecipientSeat == 2));
        var promises = E<ZhangShiPingHongjiGrantedEvent>(g).ToArray();
        Need(promises.Length == 2 && promises.Select(e => e.RoundNumber).Distinct().Count() == 1 &&
            promises.Select(e => e.Phase).Distinct().Count() == 2 && E<PhaseChangedEvent>(g).Count(e => e.ActorSeat == 2 && e.Phase == TurnPhase.Play) == 2 &&
            E<ZhangShiPingHongjiConsumedEvent>(g).Count(e => e.RecipientSeat == 2) == 1 &&
            E<TurnStartedEvent>(g).Count(e => e.ActorSeat == 2) == 1,
            "A real AI other recipient gets two native Play phases in one actual turn, while the source spends each branch exactly once in the same round.");
        _ = Cold(g, registry);

        (g, registry) = Start("two-sources"); Activate(g); Reach(g, () => Branch(P(g), "play")); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "play"); Play(g);
        Need(E<ZhangShiPingHongjiGrantedEvent>(g).Count(e => e.RecipientSeat == 0 && e.Phase == TurnPhase.Play) == 2,
            "The real human and AI sources independently grant two Play promises to the same tied recipient at the same actual preparation.");
        End(g); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && E<ZhangShiPingHongjiConsumedEvent>(g).Count() == 1);
        var first = E<ZhangShiPingHongjiConsumedEvent>(g).Single(); g = Cold(g, registry); End(g);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Boundary && !E<ZhangShiPingHongjiReturnedEvent>(g).Any());
        Need(E<ZhangShiPingHongjiConsumedEvent>(g).Count() == 1,
            "The first inserted Play's own real ending cannot steal the second source's promise already reserved by the original phase-ending cursor.");
        g = Cold(g, registry); Continue(g);
        Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && E<ZhangShiPingHongjiConsumedEvent>(g).Count() == 2);
        var second = E<ZhangShiPingHongjiConsumedEvent>(g).Last(); var payloads = g.Events.Select(e => e.Payload).ToArray();
        Need(first.Source.OwnerSeat == 0 && second.Source.OwnerSeat == 1 && first.ParentWindowId == second.ParentWindowId &&
            E<ZhangShiPingHongjiReturnedEvent>(g).Count() == 1 &&
            Array.FindIndex(payloads, e => e is ZhangShiPingHongjiReturnedEvent r && r.FrameId == first.FrameId) <
            Array.FindIndex(payloads, e => e is ZhangShiPingHongjiConsumedEvent c && c.FrameId == second.FrameId),
            "Only after the first promise returns does the original native ending resume its exact second source candidate; it starts a sequential phase under the same original parent.");
        g = Cold(g, registry); End(g); Reach(g, () => E<ZhangShiPingHongjiReturnedEvent>(g).Count() == 2);
        Need(E<PhaseChangedEvent>(g).Count(e => e.ActorSeat == 0 && e.Phase == TurnPhase.Play) == 3 && E<TurnStartedEvent>(g).Count(e => e.ActorSeat == 0) == 1,
            "Two distinct source promises complete as three genuine Play phases in the original actual turn, preserving every sequential native return.");
        _ = Cold(g, registry);
    }

    public static void NestedActualPlayPaidSourceLossQuotasAndSkippedNoCarryCold()
    {
        var (g, registry) = Start("nested-loss"); Activate(g); Reach(g, () => Branch(P(g), "play")); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "play"); Play(g);
        var turn = g.State.TurnNumber; Slash(g, 1); Play(g);
        Need(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash), "A native physical Slash spends the first inserted Play phase's quota.");
        End(g); Reach(g, () => P(g)?.Kind == DecisionKind.PlayCard && E<ZhangShiPingHongjiConsumedEvent>(g).Any());
        Need(E<PhaseChangedEvent>(g).Count(e => e.ActorSeat == 0 && e.Phase == TurnPhase.Play) == 2 &&
            !E<ActualDiscardRecoveryPhaseStartedEvent>(g).Any(e => e.Phase.ActorSeat == 0 && e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Draw) &&
            g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "Hongji can attach to a real preparation-inserted Play phase, suspend that existing native schedule, and issue a second Play with a new native Slash quota before normal Draw.");
        g = Cold(g, registry); Use(g, "hurt"); Play(g);
        Need(!V(g, 0).Skills!.Any(s => s.Id == Hongji) && V(g, 0).Hp == 8, "Actual paid HP loss removes the conditional Hongji source inside its already-issued extra phase.");
        Slash(g, 1); Play(g); g = Cold(g, registry); End(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Prep);
        Need(E<ZhangShiPingHongjiReturnedEvent>(g).Count() == 1 && E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == Boundary && e.BindingId == "play-end" && e.OwnerSeat == 0) == 2,
            "The lost source returns through the exact suspended Play ending, then resumes the original preparation sibling once.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(g.State.TurnNumber == turn && E<PhaseChangedEvent>(g).Count(e => e.ActorSeat == 0 && e.Phase == TurnPhase.Play) == 3 &&
            !E<TurnEndedEvent>(g).Any(e => e.ActorSeat == 0) && !E<ProgramBindingResolvedEvent>(g).Any(e => e.SkillId == Boundary && e.BindingId == "discard-start"),
            "After both inserted phases, the genuine normal Draw and Play continue without an invented Discard, Ending or new turn.");
        _ = Cold(g, registry);

        foreach (var scenario in new[] { "skip-draw", "skip-play" })
        {
            (g, registry) = Start(scenario); Activate(g); Reach(g, () => Branch(P(g), scenario == "skip-draw" ? "draw" : "play"));
            Answer(g, c => c.Parameters.GetValueOrDefault("branch") == (scenario == "skip-draw" ? "draw" : "play"));
            var promise = E<ZhangShiPingHongjiGrantedEvent>(g).Single();
            Reach(g, () => g.State.TurnNumber > promise.ActualTurn); g = Cold(g, registry); Play(g);
            Need(!E<ZhangShiPingHongjiConsumedEvent>(g).Any(e => e.PromiseFrameId == promise.FrameId) &&
                !E<ProgramPhaseScheduledEvent>(g).Any(e => e.SkillId == Hongji && e.Started) &&
                !E<ActualDiscardRecoveryPhaseStartedEvent>(g).Any(e => e.Phase.TurnNumber == promise.ActualTurn && e.Phase.ActorSeat == 0 &&
                    e.Phase.Kind == (scenario == "skip-draw" ? ActualDiscardRecoveryPhaseKind.Draw : ActualDiscardRecoveryPhaseKind.Play)),
                "A genuinely skipped matching phase supplies no fake ending and no early insertion; the spent promise cannot leak into a later actual turn.");
        }
    }

    public static void PhysicalMountReplacementDisabledSlotDiamondNoMatchAndPaidLossCold()
    {
        foreach (var scenario in new[] { "equip", "equip-loss", "no-diamond", "equip-death" })
        {
            var (g, registry) = Start(scenario); Play(g); Use(g, "equip", [1]); Play(g); Use(g, "wound-other", [1]); Play(g); Use(g, "abolish", [2]); Play(g);
            Need(V(g, 1).Hp == 7 && V(g, 1).Skills!.Any(s => s.Id == Watch),
                "Only actual HP loss after the setup equipment use grants the receiver's real equipment-entry observer, without observing the earlier setup use.");
            var replaced = V(g, 1).Equipment.Single(c => c.Kind == CardKind.OffensiveHorse).Id;
            End(g); Reach(g, () => Optional(P(g), XingGu)); Activate(g); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "zhang-shi-ping-xinggu") == true);
            Need(P(g)!.Choices.All(c => c.Targets.Single() is not (0 or 2)), "Direct equipment placement requires another living player with an enabled mount slot, excluding the source and the abolished recipient.");
            var choice = P(g)!.Choices.First(c => c.Targets.SequenceEqual([1])); var mount = choice.Cards.Single(); var useCount = E<CardUsedEvent>(g).Count();
            g = Cold(g, registry); Answer(g, c => c.Id == choice.Id); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            Need(V(g, 1).Equipment.Any(c => c.Id == mount) && g.CardMovements.Count(m => m.CardId == replaced && m.Reason == CardMoveReasons.EquipmentReplace && m.To == CardLocation.DiscardPile) == 1 &&
                g.CardMovements.Count(m => m.CardId == mount && m.From.Zone == CardZoneKind.PublicPersistentPile && m.To == CardLocation.Equipment(1) && m.Reason == CardMoveReasons.EquipmentEnter) == 1 &&
                E<CardUsedEvent>(g).Count() == useCount && !E<ZhangShiPingXingGuDiamondIssuedEvent>(g).Any(),
                "The real public mount replaces the old same-slot entity directly, opens an actual payment movement child and has not yet obtained its Diamond; no card use is invented.");
            g = Cold(g, registry); Accept(g, new AdvanceOneStepCommand(g.Revision));
            if (scenario == "equip-death")
            {
                Reach(g, () => !V(g, 0).IsAlive);
                Need(E<ZhangShiPingXingGuPlacementIssuedEvent>(g).Count() == 1 && !E<ZhangShiPingXingGuDiamondIssuedEvent>(g).Any() && V(g, 1).Equipment.Any(c => c.Id == mount),
                    "Actual source death in the paid native placement child leaves the completed other-player equipment intact, without repeating payment or awarding a dead owner a Diamond.");
                _ = Cold(g, registry); continue;
            }
            Reach(g, () => E<ZhangShiPingXingGuDiamondIssuedEvent>(g).Any());
            Need(V(g, 0).Skills!.Any(s => s.Id == XingGu) == (scenario != "equip-loss"), "The paid-loss scenario actually removes the conditional pile skill inside the native placement child.");
            var diamond = E<ZhangShiPingXingGuDiamondIssuedEvent>(g).Single();
            Need(E<ZhangShiPingXingGuPlacementIssuedEvent>(g).Count() == 1 && (scenario == "no-diamond"
                ? diamond.CardId is null && diamond.SequenceAfter == diamond.SequenceBefore
                : diamond.CardId is { } id && V(g, 0).Hand.Any(c => c.Id == id && c.Suit == Suit.Diamond) &&
                    g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) == 1),
                "The issued placement obtains one actual random Diamond after its children, including after skill loss; an empty matching deck produces neither a fabricated card nor reshuffle.");
            _ = Cold(g, registry);
        }
    }

    public static void RealIssuedExtraDrawMarkerAndHongjiOwnedDrawParentsCold()
    {
        var (g, registry) = Start("marker"); Activate(g);
        Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-targets") == true);
        Answer(g, c => c.Targets.SequenceEqual([0])); Play(g);
        Need(E<ProgramLeastHandMarkerOrDrawEvent>(g).Single() is { TargetSeat: 0, GrantedMarker: true },
            "A real skipped Draw and the actual current Jiezi program issue one native extra-Draw marker to the tied least-hand owner.");
        End(g); Reach(g, () => g.State.TurnNumber > 1 && Optional(P(g), Hongji) && P(g)!.TargetSeat == 0);
        Activate(g); Reach(g, () => Branch(P(g), "draw")); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "draw");
        var turn = g.State.TurnNumber; var hand = V(g, 0).HandCount;
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == DrawWatch && E<ZhangShiPingHongjiConsumedEvent>(g).Any());
        Need(E<ZhangShiPingHongjiConsumedEvent>(g).Single().ActualTurn == turn,
            "Hongji joins the real DrawPhaseObligation-owned ending while its original issued-marker parent is pending.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(V(g, 0).HandCount == hand + 6 && E<ExtraDrawPhaseStartedEvent>(g).Count() == 1 &&
            E<ZhangShiPingHongjiReturnedEvent>(g).Count() == 1 &&
            E<ActualDiscardRecoveryPhaseStartedEvent>(g).Count(e => e.Phase.TurnNumber == turn && e.Phase.ActorSeat == 0 && e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Draw) == 3,
            "The restored native Draw parent consumes its one real marker once; the original Draw, Hongji Draw and marker Draw all complete with physical cards and no missing or repeated parent.");
        _ = Cold(g, registry);
    }

    public static void DeadSourcePromiseAndInsertedBeneficiaryDeathNativeReturnCold()
    {
        var (g, registry) = Start("dead-source"); Play(g);
        var promise = E<ZhangShiPingHongjiGrantedEvent>(g).Single(e => e.RecipientSeat == 0);
        Need(promise.Source.OwnerSeat == 1 && promise.Phase == TurnPhase.Play, "A real AI source grants the human recipient a Play promise at the tied preparation.");
        Use(g, "kill", [1]); Play(g); Need(!V(g, 1).IsAlive && g.State.Winner == Winner.None, "The real donor dies as a non-lord before its issued phase, without ending the game.");
        g = Cold(g, registry); End(g); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && E<ZhangShiPingHongjiConsumedEvent>(g).Any());
        Need(E<PhaseChangedEvent>(g).Count(e => e.ActorSeat == 0 && e.Phase == TurnPhase.Play) == 2 && E<ZhangShiPingHongjiConsumedEvent>(g).Single().Source.OwnerSeat == 1,
            "The dead source's exact issued binding still starts the living beneficiary's genuine extra phase.");
        g = Cold(g, registry); End(g); Reach(g, () => E<ZhangShiPingHongjiReturnedEvent>(g).Any());
        Need(E<ZhangShiPingHongjiReturnedEvent>(g).Count() == 1, "The dead donor cannot cancel or duplicate its already-paid native phase return.");
        _ = Cold(g, registry);

        (g, registry) = Start("dead-beneficiary"); Play(g); Use(g, "draw-other", [2]); Play(g); End(g);
        Reach(g, () => Optional(P(g), Hongji) && P(g)!.TargetSeat == 2); Activate(g); Reach(g, () => Branch(P(g), "play")); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "play");
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Death && E<ZhangShiPingHongjiConsumedEvent>(g).Any());
        Need(V(g, 2).Hp == 1 && V(g, 2).Skills!.Any(s => s.Id == Death), "A real first-Play physical card payment lowers the AI beneficiary to one HP and grants the late extra-phase death trigger through its real HP condition.");
        g = Cold(g, registry); Reach(g, () => !V(g, 2).IsAlive && g.State.TurnNumber > 3);
        Need(E<TurnEndedEvent>(g).Count(e => e.ActorSeat == 2) == 1 && E<ZhangShiPingHongjiConsumedEvent>(g).Count(e => e.RecipientSeat == 2) == 1 &&
            !E<ZhangShiPingHongjiReturnedEvent>(g).Any(e => e.RecipientSeat == 2) && g.State.Winner == Winner.None,
            "Actual beneficiary death in the extra phase takes one native actual-turn ending, abandons the old phase parent and advances without a fake resumed Draw/Discard or repeated promise.");
        _ = Cold(g, registry);
    }

    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Branch(PendingDecision? p, string branch) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch) == true;
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Slash(GameEngine g, int target)
    {
        var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]));
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind) { ConversionSource = a.ConversionSource });
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 320; step++)
        {
            if (done()) return; var p = P(g);
            if (g.State.Winner != Winner.None) throw new InvalidOperationException("Zhang Shiping completed before its required actual boundary: " + Diagnostic(g));
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected actual Zhang Shiping prompt: " + Diagnostic(g));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing actual Zhang Shiping boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.Winner, Pending = P(g), Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).IsAlive, Skills = V(g, s).Skills!.Select(x => x.Id) }),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Tail = g.Events.TakeLast(12).Select(e => e.Payload.ToString()), Facts = g.Events.Select(e => e.Payload).Where(e => e is ZhangShiPingHongjiGrantedEvent or ZhangShiPingHongjiConsumedEvent or ZhangShiPingHongjiReturnedEvent).Select(e => e.ToString()) });
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Zhang Shiping command."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(cold), "Cold Zhang Shiping preserves all viewer projections, exact public mount entities, actual phase promises, nested native schedule returns and paid movement/damage/rescue children."); return cold; }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        if (scenario is "draw" or "nested-loss" or "skip-draw" or "skip-play" or "two-sources") Reach(g, () => Optional(P(g), Hongji));
        else if (scenario == "marker") Reach(g, () => Optional(P(g), "boundary:jiezi-current"));
        else Play(g);
        return (g, registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-zhang-shi-ping", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            if (scenario == "marker") CharacterFixtureContent.Register(b, "ol:zhang-shi-ping", "boundary:xu-huang");
            else CharacterFixtureContent.Register(b, "ol:zhang-shi-ping");
            var loss = scenario is "equip-loss" or "equip-death" ?
                ",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":" +
                    (scenario == "equip-loss" ? "1" : "20") + "}" : "";
            var conditional = scenario == "equip-loss" ? XingGu : Hongji;
            var skipPhase = scenario == "skip-play" ? "play" : "draw";
            var skipWindow = scenario == "skip-play" ? "afterNormalDraw" : "turnStartBeforeNormalFlow";
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"range","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0},{"id":"hand","query":"handLimit","operation":"add","value":30,"priority":0}],"activations":[
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]},
                {"id":"wound-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                {"id":"draw-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":8}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"abolish","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"alterEquipmentSlots","target":"selectedTarget","equipmentSlots":["offensiveHorse","defensiveHorse"],"amount":0}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"real-placement","window":"cardsGained","subject":"owner","destinationZones":["equipment"],"movementOccurrence":"perBatch","movementReasons":[{{{JsonSerializer.Serialize(CardMoveReasons.EquipmentEnter.Value)}}}],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"placed","options":[{"id":"continue"}]} {{{loss}}} ]}]},
              {"id":"{{{ReceiverWatch}}}","revision":1,"hpSkillGrants":[{"id":"actual-receiver-wounded","skillId":"{{{Watch}}}","maximumHp":7}]},
              {"id":"{{{DrawWatch}}}","revision":1,"triggers":[{"id":"draw-start","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw","options":[{"id":"continue"}]}]}]},
              {"id":"{{{Boundary}}}","revision":1,"triggers":[
                {"id":"draw-end","window":"drawPhaseEnded","subject":"owner","optional":false,"priority":-100,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-end","options":[{"id":"continue"}]}]},
                {"id":"play-end","window":"playEnding","subject":"owner","optional":false,"priority":-100,"effects":[{"op":"chooseOption","target":"owner","resultBind":"play-end","options":[{"id":"continue"}]}]},
                {"id":"discard-start","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"discard","options":[{"id":"continue"}]}]}]},
              {"id":"{{{Prep}}}","revision":1,"triggers":[{"id":"real-prep-play","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"priority":-50,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"},{"op":"chooseOption","target":"owner","resultBind":"parent-return","options":[{"id":"continue"}]}]}]},
              {"id":"{{{Conditional}}}","revision":1,"hpSkillGrants":[{"id":"actual-nine","skillId":"{{{conditional}}}","minimumHp":9}]},
              {"id":"{{{Skip}}}","revision":1,"triggers":[{"id":"real-skip","window":"{{{skipWindow}}}","subject":"owner","optional":false,"priority":-10,"usageScope":"game","usageLimit":1,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["{{{skipPhase}}}"]}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{PeerCost}}}","revision":1,"triggers":[{"id":"real-first-use-hp","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
              {"id":"{{{LateDeath}}}","revision":1,"hpSkillGrants":[{"id":"late-one","skillId":"{{{Death}}}","maximumHp":1}]},
              {"id":"{{{Death}}}","revision":1,"triggers":[{"id":"real-extra-play-death","window":"playPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"dying","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}
            ]}
            """;
            var ids = new[] { Driver, Watch, ReceiverWatch, DrawWatch, Boundary, Prep, Conditional, Skip, Quiet, PeerCost, LateDeath, Death };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "张世平真实阶段与实体牌小夹具", optionLabels = id is Watch or DrawWatch or Boundary or Prep or Death
                    ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "张世平原生行为") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:zsp-owner-pick", "本人", "固定公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            var ownSkills = new List<string> { Driver, DrawWatch, Boundary };
            if (scenario != "dead-source" && scenario != "nested-loss") ownSkills.Add(Hongji);
            if (scenario != "equip-loss") ownSkills.Add(XingGu);
            if (scenario is "nested-loss" or "equip-loss") ownSkills.Add(Conditional);
            if (scenario == "nested-loss") ownSkills.Add(Prep);
            if (scenario is "skip-draw" or "skip-play" or "marker") ownSkills.Add(Skip);
            if (scenario == "marker") ownSkills.Add("boundary:jiezi-current");
            b.AddGeneral(new(Owner, "张世平真实本人", "ol-zhang-shi-ping", "fixture:zsp-owner-pick", "shu", 8, ownSkills));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:zsp-peer-{i}").ToArray();
            foreach (var (id, index) in peers.Select((id, index) => (id, index)))
            {
                var pick = "fixture:zsp-pick-" + index;
                b.AddSkill(new(pick, "固定其他顺序", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000000d : 100000000d * (3 - index)) });
                var skills = (scenario is "dead-source" or "two-sources") && index == 0 ? new[] { Hongji, Quiet } :
                    scenario == "dead-beneficiary" && index == 1 ? new[] { PeerCost, LateDeath } :
                    scenario == "draw" && index == 1 ? Array.Empty<string>() :
                    (scenario is "equip" or "equip-loss" or "no-diamond" or "equip-death") && index == 0 ? new[] { Quiet, ReceiverWatch } : new[] { Quiet };
                b.AddGeneral(new(id, "真实其他", "supporter", pick, "shu", 8, skills) { InitialHp = scenario == "dead-beneficiary" && index == 1 ? 2 : 8 });
            }
            b.AddCard(new("fixture:zsp-mount", "大宛", "装备牌", "真实实体进攻坐骑", CardKind.OffensiveHorse));
            b.AddDeck(new("fixture:zsp-deck", "固定实体非种子搜索", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 168).Select(i =>
                new ContentDeckPhysicalCard(i % 7 == 6 ? "fixture:zsp-mount" : "standard:slash", i % 7 == 6 ? Suit.Spade : scenario == "no-diamond" || i % 2 == 0 ? Suit.Heart : Suit.Diamond, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "张世平真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:zsp-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
