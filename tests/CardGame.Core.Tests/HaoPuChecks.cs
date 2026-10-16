using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class HaoPuChecks
{
    private const string Skill = "ol:zhenying", Binding = "simultaneous-hand-adjustment", Driver = "fixture:haopu-driver", Quiet = "fixture:haopu-quiet";
    private const string Owner = "fixture:haopu-owner", Mode = "identity:classic-haopu-fixture", Target = "fixture:haopu-target";
    private const string Loss = "fixture:haopu-loss", Gain = "fixture:haopu-gain", Health = "fixture:haopu-health", Dying = "fixture:haopu-dying";

    public static void SealedFixedAnswerPrivacyAdjustmentAndPhaseQuotaCold()
    {
        var privateViews = new List<string[]>();
        foreach (var count in new[] { 0, 1, 2 })
        {
            var (g, registry) = Start(); var originalMoves = g.CardMovements.Count; var originalRevision = g.Revision;
            var originalAnswers = g.AcceptedCommands.OfType<AnswerPromptCommand>().Count();
            Use(g); var seen = new List<string>();
            for (var step = 0; step < 3; step++)
            {
                Need(P(g) is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null),
                    "Every count and retained-slot choice is visible only to its actual participant.");
                var prompt = P(g)!; var before = State(g);
                Need(!g.Submit(new AnswerPromptCommand(2, prompt.PromptId, prompt.Choices[0].Id, g.Revision)).Accepted && State(g) == before,
                    "A foreign player cannot submit or alter an unpublished simultaneous hand commitment.");
                g = Cold(g, registry); Answer(g, step == 0 ? c => c.Parameters.GetValueOrDefault("count") == count.ToString() : _ => true);
                Need(g.CardMovements.Count == originalMoves && !E<ZhenyingAdjustmentPaidEvent>(g).Any() &&
                    V(g, 0).HandCount == 6 && V(g, 1).HandCount == 4,
                    "No participant hand moves or reveals the first commitment before both sides finish all sealed submissions.");
                var r = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Zhenying is not null).Zhenying!;
                Need(step < 2 ? r.SelectionIndex == 0 && r.SlotIndex == (step == 0 ? 0 : 1) :
                    r.SelectionIndex == 1 && r.SlotIndex == 0 && r.OwnerPlan.Committed && !r.OtherPlan.Committed,
                    "The typed private receipt freezes each fixed retained-slot cursor and advances to the other participant only after three answers.");
                seen.Add(SnapshotJson.Serialize(g.CreateSnapshot(2)));
            }
            Need(g.Revision - originalRevision == 4 && g.AcceptedCommands.OfType<AnswerPromptCommand>().Count() - originalAnswers == 3 &&
                E<ZhenyingChoiceCommittedEvent>(g) is [{ Seat: 0 }],
                "Each count, including zero, produces the same three accepted answers and one count-free public commitment fact.");
            privateViews.Add(seen.ToArray()); _ = Cold(g, registry);
        }
        Need(privateViews.Skip(1).All(v => v.SequenceEqual(privateViews[0])),
            "Every peer snapshot after every private step, including public revision and status, is identical for first commitments zero, one and two.");

        foreach (var initial in new[] { 0, 6, 2 })
        {
            const int desired = 2;
            var (g, registry) = Start(); Arrange(g, initial, initial == 6 ? 4 : initial);
            Use(g); g = Commit(g, 0, desired, registry); g = Commit(g, 1, desired, registry); Play(g);
            var paid = E<ZhenyingAdjustmentPaidEvent>(g).Single();
            Need(V(g, 0).HandCount == desired && V(g, 1).HandCount == desired &&
                paid.OwnerDrawCount == Math.Max(0, desired - initial) && paid.OtherDrawCount == Math.Max(0, desired - (initial == 6 ? 4 : initial)) &&
                E<ZhenyingCompletedEvent>(g) is [{ DuelIssued: false }] && !E<ZhenyingDuelIssuedEvent>(g).Any(),
                "Both real hands independently draw, discard or stay unchanged to their actual selected equal counts; equal actual counts issue no Duel.");
            g = Cold(g, registry); Use(g); g = Commit(g, 0, desired, registry); g = Commit(g, 1, desired, registry); Play(g);
            Need(E<ZhenyingCompletedEvent>(g).Length == 2 && !g.GetHumanLegalActions().Any(a => a.SkillId == Skill),
                "Two real activations consume the original Play quota even when the second adjustment moves no cards or creates no Duel.");
            _ = Cold(g, registry);
        }
        var (illegal, _) = Start(); Arrange(illegal, 0, 1);
        var beforeIllegal = State(illegal);
        Need(!illegal.Submit(new UseProgramSkillCommand(0, Skill, Binding, [], [1], illegal.Revision, P(illegal)!.PromptId)).Accepted && State(illegal) == beforeIllegal &&
            illegal.GetHumanLegalActions().Where(a => a.SkillId == Skill).All(a => !a.TargetSeats.Contains(1)),
            "Actual public hand counts filter the target and reject a forged target with more cards without consuming a phase use.");
    }

    public static void NativeDuelBothActorsAndAlternatingSlashResponsesCold()
    {
        {
            var (g, registry) = Start("responses"); UseDriver(g, "wound", [1]); Play(g);
            Use(g); g = Commit(g, 0, 1, registry); g = Commit(g, 1, 2, registry);
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Target);
            var origin = E<ZhenyingDuelIssuedEvent>(g).Single().Origin; var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.ZhenyingDuel is not null);
            Need(origin.ActorSeat == 0 && origin.TargetSeat == 1 &&
                use.Id == origin.CardUseFrameId && use.CardId == 0 && use.CardKind == CardKind.Duel && use.PhysicalCardIds is { Count: 0 } &&
                E<CardActionAcceptedEvent>(g).Any(e => e.Action.EffectiveKind == CardKind.Duel && e.Action.ActorSeat == origin.ActorSeat &&
                    e.Action.TargetSeats.SequenceEqual([origin.TargetSeat]) && e.Action.PhysicalCards.Count == 0 && e.Action.ConversionChain.Count == 0),
                "Either participant with fewer actual cards becomes the actor of one genuine colorless, cardless, committed native Duel.");
            g = Cold(g, registry); Continue(g);
            for (var response = 0; response < 4 && E<ZhenyingCompletedEvent>(g).Length == 0; response++)
            {
                Reach(g, () => E<ZhenyingCompletedEvent>(g).Length > 0 || P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
                if (E<ZhenyingCompletedEvent>(g).Length > 0) break;
                g = Cold(g, registry); Answer(g, c => c.Cards.Count == 1 || !P(g)!.Choices.Any(x => x.Cards.Count == 1) && c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
            }
            Play(g);
            Need(E<DuelResponseEvent>(g).Count(e => e.ResolutionId == origin.CardUseFrameId && e.UsedSlash) == 3 &&
                E<DuelResponseEvent>(g).Any(e => e.ResolutionId == origin.CardUseFrameId && !e.UsedSlash) &&
                E<DamageAppliedEvent>(g).Any(e => e.TargetSeat == origin.ActorSeat && e.SourceSeat == origin.TargetSeat && e.Amount == 1) &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == origin.CardUseFrameId) == 1 &&
                E<ZhenyingCompletedEvent>(g) is [{ DuelIssued: true }],
                "Three actual alternating physical Slash responses determine the loser and native damage, with one Duel completion and no surrogate damage effect.");
            _ = Cold(g, registry);
        }
        foreach (var count in new[] { 0, 1 })
        {
            var (g, registry) = Start("human-peer");
            var source = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Zhenying is not null).OwnerSeat;
            Need(V(g, source).GeneralId == Owner && V(g, source).Role == Role.Lord && !V(g, source).IsHuman && V(g, 1).IsHuman,
                "The fixed native setup gives the real AI Lord the skill while the other participant is the sole human.");
            g = Commit(g, source, 2, registry); g = Commit(g, 1, count, registry);
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Target);
            var origin = E<ZhenyingDuelIssuedEvent>(g).Single().Origin;
            var paid = E<ZhenyingAdjustmentPaidEvent>(g).Single();
            Need(origin.Source.OwnerSeat == source && origin.ActorSeat == 1 && origin.TargetSeat == source && origin.OwnerCount == 2 && origin.OtherCount == count &&
                paid.OwnerDesiredCount == 2 && paid.OtherDesiredCount == count && V(g, source).HandCount == 2 && V(g, 1).HandCount == count &&
                E<CardActionAcceptedEvent>(g).Any(e => e.Action.ActorSeat == 1 && e.Action.EffectiveKind == CardKind.Duel &&
                    e.Action.TargetSeats.SequenceEqual([source]) && e.Action.PhysicalCards.Count == 0),
                "A human target independently commits zero or one, both real discards finish, and the other participant becomes the true native Duel actor.");
            g = Cold(g, registry); Continue(g); Reach(g, () => E<ZhenyingCompletedEvent>(g).Length == 1);
            Need(E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 1 && e.TargetSeat == source && e.Amount == 1) &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == origin.CardUseFrameId) == 1 &&
                E<ZhenyingCompletedEvent>(g) is [{ DuelIssued: true }],
                "The reverse participant's genuine cardless Duel owns native damage and returns once through its AI issuer after cold replay.");
            _ = Cold(g, registry);
        }
    }

    public static void PaidAdjustmentSurvivesSourceLossAfterBothHandsMoveCold()
    {
        var (g, registry) = Start("source-loss"); Use(g); g = Commit(g, 0, 1, registry); g = Commit(g, 1, 2, registry);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Loss && Option(g, "continue"));
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Zhenying is not null); var paid = E<ZhenyingAdjustmentPaidEvent>(g).Single();
        NeedNative(g, root.Zhenying is { Stage: ZhenyingStage.AdjustmentChildren, OwnerPlan.Committed: true, OtherPlan.Committed: true } &&
            V(g, 0).HandCount == 1 && V(g, 1).HandCount == 2 && paid.OwnerActualCount == 1 && paid.OtherActualCount == 2 && !E<ZhenyingDuelIssuedEvent>(g).Any(),
            "The first real discard child opens only after both sealed hands have actually been adjusted and their common payment frozen.");
        g = Cold(g, registry); Continue(g); Reach(g, () => Option(g, "after-loss"));
        NeedNative(g, V(g, 0).Hp == 12 && V(g, 0).Skills!.All(s => s.ContentId != Skill), "A real HP change removes the issuing grant inside the paid movement child.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "after-loss");
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Target);
        var duel = E<ZhenyingDuelIssuedEvent>(g).Single().Origin;
        Need(duel.ProgramFrameId == root.Id && duel.ActorSeat == 0 && duel.TargetSeat == 1 && duel.OwnerCount == 1 && duel.OtherCount == 2,
            "Paid source loss retains the same actor comparison and real native Duel entitlement instead of discarding or repeating either payment.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(E<ZhenyingAdjustmentPaidEvent>(g).Length == 1 && E<ZhenyingCompletedEvent>(g) is [{ DuelIssued: true }] &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == duel.CardUseFrameId) == 1 &&
            g.CardMovements.Count(m => m.Reason.Value == "skill-program.hao-pu.zhenying.discard") == 7,
            "Every original discard remains paid once across source loss, native use and cold child returns.");
        _ = Cold(g, registry);
    }

    public static void NativeDuelDyingAndDeathReturnCold()
    {
        var (g, registry) = Start("duel-death"); Use(g); g = Commit(g, 0, 0, registry); g = Commit(g, 1, 2, registry);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Target); var origin = E<ZhenyingDuelIssuedEvent>(g).Single().Origin;
        g = Cold(g, registry); Continue(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Dying);
        NeedNative(g, origin.ActorSeat == 0 && origin.TargetSeat == 1 && V(g, 1).Hp == 0 &&
            E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) && !E<ZhenyingCompletedEvent>(g).Any(),
            "The true zero-entity Duel enters its own native dying tree before returning to the paid simultaneous adjustment.");
        var dying = g.ResolutionStack.OfType<DyingFrame>().Single();
        NeedNative(g, dying is { VictimSeat: 1, KillerSeat: 0, Continuation: DyingContinuationKind.Damage } &&
            g.ResolutionStack.OfType<DamageFrame>().Any(d => d.Id == dying.ParentFrameId && d.ParentFrameId == origin.CardUseFrameId &&
                d.SourceSeat == 0 && d.TargetSeat == 1 && d.Amount == 1) &&
            g.ResolutionStack.OfType<CardUseFrame>().Any(u => u.Id == origin.CardUseFrameId && u.ZhenyingDuel?.ProgramFrameId == origin.ProgramFrameId) &&
            g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w => w.ResumeDyingFrameId == dying.Id && w.Window == SkillProgramTriggerWindow.DyingEntering),
            "The native dying cursor returns through its exact DamageFrame, original Duel use, owning paid activation and real DyingEntering child.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(!V(g, 1).IsAlive && g.State.Winner == Winner.None && E<PlayerDiedEvent>(g).Count(e => e.VictimSeat == 1) == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == origin.CardUseFrameId) == 1 && E<ZhenyingCompletedEvent>(g) is [{ DuelIssued: true }],
            "A real other-player death completes the same Duel and owning activation once without an orphaned attack or ending the otherwise live game.");
        _ = Cold(g, registry);
    }

    public static void ActualDrawGainRecoveryChildPreservesBothPaidHandsCold()
    {
        var (g, registry) = Start("recovery"); UseDriver(g, "wound", [0]); Play(g); Arrange(g, 0, 0);
        Use(g); g = Commit(g, 0, 2, registry); g = Commit(g, 1, 2, registry); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Gain);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Zhenying is not null);
        Need(V(g, 0).HandCount == 2 && V(g, 1).HandCount == 2 && E<ZhenyingAdjustmentPaidEvent>(g) is [{ OwnerDrawCount: 2, OtherDrawCount: 2 }] &&
            !E<ZhenyingCompletedEvent>(g).Any(), "Both actual draws are paid before the owner's real gain child can request recovery.");
        g = Cold(g, registry); Continue(g); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health);
        NeedNative(g, V(g, 0).Hp == 12 && E<RecoveryAppliedEvent>(g) is [{ SourceSeat: 0, TargetSeat: 0, Amount: 1 }],
            "The genuine gained-card child actually recovers one HP before its real health window.");
        var gain = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Gain);
        NeedNative(g, gain.WindowContext is { Window: SkillProgramTriggerWindow.CardsGained, MovementBatch: { } batch } &&
            batch.ParentFrameId == root.Id && batch.AwaitingProgramFrameId == root.Id &&
            g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w => w.Id == batch.Id && w.ResumeProgramFrameId is null &&
                w.Batch == batch && w.Batch.Movements.All(m => m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.hao-pu.zhenying.draw")) &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(w => w.ResumeFrameId == gain.Id && w.Continuation == PostEventContinuation.Program &&
                w.Change is { Kind: HpChangeKind.Recovery, TargetSeat: 0, Amount: 1, HpBefore: 11, HpAfter: 12 } && w.Change.ParentFrameId == gain.Id) &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == root.Id && f.Zhenying is { Stage: ZhenyingStage.AdjustmentChildren }),
            "The genuine gain-to-recovery-to-health child retains the exact awaited movement batch and original paid simultaneous-adjustment root.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(E<ZhenyingCompletedEvent>(g) is [{ DuelIssued: false }] && E<ZhenyingAdjustmentPaidEvent>(g).Length == 1 && !E<ZhenyingDuelIssuedEvent>(g).Any(),
            "Native recovery returns cold to the equal actual hand comparison without drawing again or inventing a Duel.");
        _ = Cold(g, registry);
    }

    private static GameEngine Commit(GameEngine g, int seat, int count, ContentRegistry registry)
    {
        var before = g.AcceptedCommands.Count; var human = V(g, seat).IsHuman;
        for (var step = 0; step < 3; step++)
        {
            Reach(g, () => IsZhenying(g) && P(g)!.PlayerSeat == seat);
            g = Cold(g, registry);
            if (human) Answer(g, step == 0 ? c => c.Parameters.GetValueOrDefault("count") == count.ToString() : _ => true);
            else
            {
                Need(count == 2, "This AI independently prefers its normal two-card commitment; the test cannot choose its private count.");
                Accept(g, new AdvanceOneStepCommand(g.Revision));
            }
        }
        var commands = g.AcceptedCommands.Skip(before).ToArray();
        Need(commands.Length == 3 && (human ? commands.All(c => c is AnswerPromptCommand { ActorSeat: var chooser } && chooser == seat) :
            commands.All(c => c is AdvanceOneStepCommand)),
            "The human submits exactly three real answers; the AI commits the same three private slots through three actual engine steps.");
        return g;
    }
    private static bool IsZhenying(GameEngine g) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "zhenying") == true;
    private static bool Option(GameEngine g, string id) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id) == true;
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why); }
    private static void NeedNative(GameEngine g, bool yes, string why)
    { if (!yes) throw new InvalidOperationException(why + " " + NativeState(g)); }
    private static string NativeState(GameEngine g) => JsonSerializer.Serialize(new { g.State.Winner,
        Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).MaxHp, V(g, s).IsAlive, V(g, s).HandCount, V(g, s).Skills }),
        Prompt = P(g), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        Facts = g.Events.TakeLast(16).Select(e => new { Type = e.Payload.GetType().Name, Payload = JsonSerializer.Serialize(e.Payload, e.Payload.GetType()) }) });
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected native Zhenying command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick)
    { var p = P(g)!; Need(V(g, p.PlayerSeat).IsHuman, "Only the actual human can submit an AnswerPromptCommand.");
        Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static void Continue(GameEngine g)
    { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Use(GameEngine g) => Accept(g, new UseProgramSkillCommand(0, Skill, Binding, [], [1], g.Revision, P(g)!.PromptId));
    private static void UseDriver(GameEngine g, string binding, IReadOnlyList<int> targets, IReadOnlyList<int>? cards = null) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, binding, cards ?? [], targets, g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(P(g)!.PlayerSeat, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Arrange(GameEngine g, int ownerCount, int otherCount)
    {
        var removed = V(g, 0).Hand.Take(V(g, 0).HandCount - ownerCount).Select(c => c.Id).ToArray();
        if (removed.Length > 0) { UseDriver(g, "trim", [], removed); Play(g); }
        while (V(g, 1).HandCount > otherCount)
        {
            UseDriver(g, "trim-peer", [1]); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
            Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand) && c.Parameters.GetValueOrDefault("slot-index") == "0"); Play(g);
        }
        Need(V(g, 0).HandCount == ownerCount && V(g, 1).HandCount == otherCount, "Native setup movements establish the exact small real hand counts.");
    }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 180; step++)
        {
            if (done()) return; NeedNative(g, g.State.Winner == Winner.None, "The fixed native Zhenying fixture ended before its boundary."); var p = P(g);
            if (p is not null && !V(g, p.PlayerSeat).IsHuman) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p is { Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { Kind: DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.RescueDying or DecisionKind.Nullification })
                Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true)
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (Option(g, "continue")) Continue(g);
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing fixed native Zhenying boundary: " + JsonSerializer.Serialize(new { Prompt = P(g),
            Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = g.Events.TakeLast(10).Select(e => e.Payload.ToString()) }));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(copy), "Cold command replay preserves all sealed slots, private viewers, actual hands, paid movements, source loss and typed native Duel children.");
        return copy;
    }
    private static (GameEngine, ContentRegistry) Start(string scenario = "plain")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var human = scenario == "human-peer" ? 1 : 0;
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = human, HumanRole = human == 0 ? Role.Lord : Role.Rebel, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(human, human == 0 ? Owner : "fixture:haopu-peer-1", g.Revision, P(g)!.PromptId));
        if (human == 0) Play(g); else Reach(g, () => IsZhenying(g) && !V(g, P(g)!.PlayerSeat).IsHuman);
        return (g, registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-hao-pu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:hao-pu");
            var rules = JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"trim","minCards":1,"maxCards":20,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"trim"},{"op":"moveBoundCards","target":"owner","sourceBind":"trim","destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"trim-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Target}}","revision":1,"triggers":[{"id":"native-duel-target","window":"cardUseTargetsFinalized","ownerRelation":"target","cardKinds":["duel"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"target","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            var names = new Dictionary<string, object> { [Driver] = new { name = "真实手牌准备", description = "实际弃牌与失去体力" },
                [Quiet] = new { name = "安静的其他角色", description = "原生跳过出牌" },
                [Target] = new { name = "实际决斗目标窗口", description = "原生决斗确定目标", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            var extra = new List<string> { Skill, Target };
            const string oversupply = "fixture:haopu-large-hand", ownerPick = "fixture:haopu-ai-owner-pick";
            if (scenario == "human-peer")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{oversupply}}","revision":1,"triggers":[{"id":"actual-opening-cards","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":10},{"op":"awaitOwnedCardMovement","target":"owner"}]}]}"""));
                names[oversupply] = new { name = "真实开局供牌", description = "其他角色实际摸牌使其不满足镇荧目标条件" };
                b.AddSkill(new(ownerPick, "固定AI主公选将", "按公开身份固定当前夹具选将")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            }
            if (scenario == "source-loss")
            {
                extra.Remove(Skill); extra.AddRange(["fixture:haopu-conditional", Loss]);
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"fixture:haopu-conditional","revision":1,"hpSkillGrants":[{"id":"actual-thirteen","skillId":"{{Skill}}","minimumHp":13}]}"""));
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Loss}}","revision":1,"triggers":[{"id":"paid-discard-source-loss","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementDiscardOnly":true,"movementOccurrence":"perBatch","movementReasons":["skill-program.hao-pu.zhenying.discard"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"lost","options":[{"id":"after-loss"}]}]}]}"""));
                names["fixture:haopu-conditional"] = new { name = "实际体力技能来源", description = "主公13体力授予镇荧" };
                names[Loss] = new { name = "实际调整弃牌失源", description = "付款后原生失去体力", optionLabels = new Dictionary<string, string> { ["continue"] = "继续", ["after-loss"] = "继续已付调整" } };
            }
            if (scenario == "duel-death")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Dying}}","revision":1,"triggers":[{"id":"native-duel-dying","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"dying","options":[{"id":"continue"}]}]}]}"""));
                names[Dying] = new { name = "实际决斗濒死", description = "原生濒死孩子", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            }
            if (scenario == "recovery")
            {
                extra.AddRange([Gain, Health]);
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Gain}}","revision":1,"triggers":[{"id":"native-adjustment-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.hao-pu.zhenying.draw"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":1}]}]}"""));
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"native-recovery-health","window":"afterHealthChanged","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}"""));
                names[Gain] = new { name = "实际摸牌后回复", description = "真实摸牌触发回复", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
                names[Health] = new { name = "实际回复体力窗", description = "真实回复子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = p, ProgramPresentation = catalog.Presentations[id], SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => id == Quiet ? 10000d : -10000d) });
            b.AddGeneral(new(Owner, "实际郝普", "ol-hao-pu", scenario == "human-peer" ? ownerPick : Driver, "shu", 12, extra, GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:haopu-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "实际另一角色", "supporter", Quiet, "wei", scenario == "duel-death" ? 1 : 12,
                scenario == "duel-death" ? [Target, Dying] : scenario == "human-peer" && peer != peers[0] ? [Target, oversupply] : [Target]));
            b.AddDeck(new("fixture:haopu-deck", "固定原生手牌", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64)
                .Select(_ => new ContentDeckPhysicalCard(scenario == "responses" ? "standard:slash" : "standard:dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "真实同时调整与决斗", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:haopu-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
