using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OwnerLifecycleReplacementChecks
{
    private const string Kunfen = "ol:kunfen";
    private const string Fengliang = "ol:fengliang";
    private const string Modified = "ol:kunfen-awakened";
    private const string Tiaoxin = "ol:sp-jiang-wei-tiaoxin";
    private const string Driver = "fixture:owner-replace-driver";
    private const string Entry = "fixture:owner-replace-entry";
    private const string Hp = "fixture:owner-replace-hp";
    private const string Gain = "fixture:owner-replace-gain";
    private const string Tail = "fixture:owner-replace-tail";
    private const string Peer = "fixture:owner-replace-peer";
    private const string Owner = "fixture:owner-replace-owner";
    private const string Mode = "identity:classic-owner-replacement-fixture";
    private const string DrawReason = "skill-program.ol:kunfen.Draw";

    public static void DyingAwakeningReplacesOwnerSkillsAndResumesPaidHpTailOnce()
    {
        RejectInvalidReplacementCallers();
        foreach (var paidByKunfen in new[] { true, false })
        {
            var run = ExerciseAwakening(paidByKunfen, permanentTiaoxin: false);
            var game = run.Game;
            Require(V(game) is { IsAlive: true, Hp: 2, MaxHp: 4 } &&
                    Facts<ProgramSkillHpLostEvent>(game).Count(e => e.SkillId == Kunfen) == (paidByKunfen ? 1 : 0) &&
                    Moves(game, DrawReason).Length == (paidByKunfen ? 2 : 0) &&
                    Facts<ProgramBindingStartedEvent>(game).Count(e => e.SkillId == Fengliang) == 1 &&
                    Facts<ProgramBindingResolvedEvent>(game).Count(e => e.SkillId == Fengliang && e.Activated && e.Completed) == 1 &&
                    Facts<ProgramOwnerSkillsReplacedEvent>(game).Count(e => e.SkillId == Fengliang) == 1 &&
                    Facts<ProgramReplacedPaidHpContinuationIssuedEvent>(game).Count() == (paidByKunfen ? 1 : 0) &&
                    Facts<MaximumHpChangedEvent>(game).Count(e => e.SkillId == Fengliang && e.Delta == -1 && e.MaximumHp == 4) == 1 &&
                    Facts<DyingResolvedEvent>(game).Count(e => e.ResolutionId == run.DyingId && e.Survived) == 1 &&
                    !game.ResolutionStack.Any(f => f.Id == run.ParentId || f.Id == run.DyingId) &&
                    !game.CardMovements.Any(m => m.CardId <= 0),
                "Both real entry causes awaken once; only the already-paid original Kunfen ancestor resumes its two-entity Draw tail.");
            _ = Cold(game, run.Registry);
        }
    }

    public static void DyingAwakeningPreservesPermanentSkillAndOptionalEndingAcrossReplay()
    {
        var samePhase = ExerciseAwakening(paidByKunfen: false, permanentTiaoxin: true);
        var nextPlay = samePhase.Game;
        Reach(nextPlay, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && nextPlay.State.TurnNumber > samePhase.TurnNumber);
        var nextPlayInstance = UseActualTiaoxin(nextPlay, target: 3);
        Require(nextPlayInstance == samePhase.PermanentTiaoxinInstance &&
                nextPlayInstance.StartsWith(CharacterState.PrimarySkillSource + ":", StringComparison.Ordinal) &&
                V(nextPlay).Skills!.Count(s => s.ContentId == Tiaoxin) == 1 &&
                V(nextPlay).SkillRuntimeStates!.Count(s => s.SkillId == Tiaoxin) == 1 &&
                Facts<ProgramOwnerSkillsReplacedEvent>(nextPlay).Count(e => e.SkillId == Fengliang) == 1 &&
                !Facts<ProgramReplacedPaidHpContinuationIssuedEvent>(nextPlay).Any(),
            "Acquiring the same named skill during a used Play does not create a replacement instance; the retained permanent instance becomes usable again only in the next real Play.");
        _ = Cold(nextPlay, samePhase.Registry);

        var run = ExerciseAwakening(paidByKunfen: true, permanentTiaoxin: true);
        var game = run.Game;
        var firstTurn = run.TurnNumber;
        Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && game.State.TurnNumber > firstTurn);
        var laterInstance = UseActualTiaoxin(game, target: 3);
        Require(laterInstance == run.PermanentTiaoxinInstance && laterInstance.StartsWith(CharacterState.PrimarySkillSource + ":", StringComparison.Ordinal) &&
                V(game).Skills!.Count(s => s.ContentId == Tiaoxin) == 1 &&
                V(game).SkillRuntimeStates!.Count(s => s.SkillId == Tiaoxin) == 1 &&
                Facts<SkillsAcquiredEvent>(game).Where(e => e.SourceSkillId == Fengliang).All(e => !e.SkillIds.Contains(Tiaoxin)),
            "The same printed permanent Tiaoxin source and exact effective instance survive awakening, without a second independent public skill state.");

        Use(game, "hurt");
        Reach(game, p => p is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 });
        var repeated = game.ResolutionStack.OfType<DyingFrame>().Single();
        var driver = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver);
        var peach = P(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "peach").Cards.Single();
        Require(repeated.ParentFrameId == driver.Id && repeated.ResumesProgramSkill && driver.InstructionIndex == 1 &&
                V(game) is { IsAlive: true, Hp: 0, MaxHp: 4 } &&
                Facts<ProgramBindingStartedEvent>(game).Count(e => e.SkillId == Fengliang) == 1 &&
                Facts<ProgramOwnerSkillsReplacedEvent>(game).Count(e => e.SkillId == Fengliang) == 1 &&
                P(game)!.Choices.All(c => c.Parameters.GetValueOrDefault("skill-id") != Fengliang) &&
                V(game).Hand.Any(c => c.Id == peach && c.Kind == CardKind.Peach),
            "A later real zero-HP entry has its exact live driver parent and a real physical rescue choice; game-scoped awakening cannot run twice.");
        AssertHandPrivacy(game);
        RejectWrongActor(game);
        game = Cold(game, run.Registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("response") == "peach" && c.Cards.SequenceEqual([peach]));
        ReachOption(game, Driver, "driver-tail");
        Require(V(game) is { Hp: 1, MaxHp: 4, IsAlive: true } &&
                Facts<DyingResponseEvent>(game).Count(e => e.ResolutionId == repeated.Id && e.ResponderSeat == 0 && e.UsedPeach && e.PeachCardId == peach) == 1 &&
                Facts<DyingResolvedEvent>(game).Count(e => e.ResolutionId == repeated.Id && e.Survived) == 1 &&
                game.CardMovements.Count(m => m.CardId == peach && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                game.CardMovements.Count(m => m.CardId == peach && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "One actual Peach pays once and cold-restores through the exact second dying return, without another maximum-HP reduction or replacement.");
        game = Cold(game, run.Registry);
        Continue(game);
        ReachPlay(game);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision));
        Reach(game, p => IsOptional(p, Modified));
        var ending = game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single();
        var hpBefore = V(game).Hp;
        var handBefore = V(game).Hand.Select(c => c.Id).ToArray();
        var moveCount = game.CardMovements.Count;
        var losses = Facts<ProgramSkillHpLostEvent>(game).Count();
        Require(V(game).Skills!.All(s => s.ContentId != Kunfen) && V(game).Skills!.Count(s => s.ContentId == Modified) == 1 &&
                IsOptional(P(game)!, Modified) && ending.OwnerSeat == 0,
            "The modified skill offers a real optional ending choice under the original owner's next native ending boundary.");
        AssertPrivate(game);
        RejectWrongActor(game);
        game = Cold(game, run.Registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        ReachOption(game, Tail, "ending-tail");
        Require(V(game).Hp == hpBefore && V(game).Hand.Select(c => c.Id).SequenceEqual(handBefore) &&
                game.CardMovements.Count == moveCount && Facts<ProgramSkillHpLostEvent>(game).Count() == losses &&
                Facts<ProgramBindingResolvedEvent>(game).Count(e => e.FrameId == ending.Id && e.SkillId == Modified && !e.Activated && !e.Completed) == 1 &&
                Facts<ProgramOwnerSkillsReplacedEvent>(game).Count(e => e.SkillId == Fengliang) == 1 &&
                Facts<MaximumHpChangedEvent>(game).Count(e => e.SkillId == Fengliang) == 1 &&
                Moves(game, DrawReason).Length == 2 && !Moves(game, "skill-program.ol:kunfen-awakened.Draw").Any(),
            "Declining the modified Kunfen leaves HP and every actual entity unchanged and cannot replay its loss, Draw, awakening or replacement.");
        game = Cold(game, run.Registry);
        Continue(game);
        Require(!game.ResolutionStack.Any(f => f.Id == ending.Id) &&
                Facts<ProgramBindingResolvedEvent>(game).Count(e => e.FrameId == ending.Id && e.SkillId == Modified) == 1,
            "The declined optional candidate returns to its exact ending cursor once.");
        _ = Cold(game, run.Registry);
    }

    private sealed record AwakeningRun(GameEngine Game, ContentRegistry Registry, long ParentId, long DyingId,
        int TurnNumber, string? PermanentTiaoxinInstance);

    private static AwakeningRun ExerciseAwakening(bool paidByKunfen, bool permanentTiaoxin)
    {
        var (game, registry) = Start(permanentTiaoxin);
        var permanent = permanentTiaoxin ? UseActualTiaoxin(game, target: 1) : null;
        var turn = game.State.TurnNumber;
        var originalHand = V(game).Hand.Select(c => c.Id).ToArray();
        if (paidByKunfen)
        {
            Use(game, "prepare");
            ReachOption(game, Driver, "driver-tail");
            Require(V(game) is { Hp: 1, MaxHp: 5 } && V(game).Hand.Select(c => c.Id).SequenceEqual(originalHand),
                "A serialized real activation pays one preparatory HP, leaving the original entities intact before mandatory Kunfen.");
            game = Cold(game, registry);
            Continue(game);
            ReachPlay(game);
            Accept(game, new EndPlayPhaseCommand(0, game.Revision));
        }
        else Use(game, "hurt");
        ReachOption(game, Entry, "entry-return");
        var dying = game.ResolutionStack.OfType<DyingFrame>().Single();
        var parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == (paidByKunfen ? Kunfen : Driver));
        var entry = game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.Window == SkillProgramTriggerWindow.DyingEntering);
        var parentId = parent.Id;
        var parentInstance = parent.SkillInstanceId;
        var endingId = paidByKunfen ? game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single().Id : (long?)null;
        Require(V(game) is { Hp: 0, MaxHp: 5, IsAlive: true } && parent.InstructionIndex == 1 &&
                parent.ReplacedPaidHpContinuation is null && !JsonSerializer.Serialize(parent).Contains(nameof(ProgramSkillFrame.ReplacedPaidHpContinuation), StringComparison.Ordinal) &&
                dying.ParentFrameId == parentId && dying.ResumesProgramSkill && entry.ResumeDyingFrameId == dying.Id &&
                entry.Continuation == ProgramLifecycleContinuation.ResumeDyingEntry && entry.OwnerSeat == 0 &&
                parent.OwnerSeat == 0 && parent.GameplayHash == registry.GetSkill(parent.SkillId).Program!.GameplayHash &&
                Facts<ProgramSkillHpLostEvent>(game).Count(e => e.FrameId == parentId && e.TargetSeat == 0 && e.RemainingHp == 0 && e.Amount == (paidByKunfen ? 1 : 2)) == 1 &&
                !Facts<ProgramBindingStartedEvent>(game).Any(e => e.SkillId == Fengliang) && Moves(game, DrawReason).Length == 0,
            "The exact real HP-payment parent is paused at cursor one beneath one native zero-HP dying entry; awakening and the Draw tail have not run.");
        AssertPrivate(game);
        RejectWrongActor(game);
        game = Cold(game, registry);
        Continue(game);
        ReachOption(game, Hp, "hp-return");
        var fengliang = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Fengliang);
        var recovery = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        var fengliangId = fengliang.Id;
        Require(V(game) is { Hp: 2, MaxHp: 4, IsAlive: true } && fengliang.InstructionIndex == 2 &&
                fengliang.WindowContext?.Window == SkillProgramTriggerWindow.DyingEntering && fengliang.WindowContext.ParentFrameId == entry.Id &&
                recovery.ResumeFrameId == fengliang.Id && recovery.Continuation == PostEventContinuation.Program &&
                recovery.Change is { Kind: HpChangeKind.Recovery, TargetSeat: 0, HpBefore: 0, HpAfter: 2, Amount: 2 } &&
                game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == parentId).InstructionIndex == 1 &&
                !Facts<ProgramOwnerSkillsReplacedEvent>(game).Any(e => e.SkillId == Fengliang) &&
                !Facts<DyingResolvedEvent>(game).Any(e => e.ResolutionId == dying.Id) && Moves(game, DrawReason).Length == 0,
            "Recovery pauses after exactly max-HP minus one and zero-to-two recovery, before granting, replacing or returning the already-paid parent.");
        AssertPrivate(game);
        RejectWrongActor(game);
        game = Cold(game, registry);
        Continue(game);
        if (paidByKunfen)
        {
            ReachOption(game, Gain, "gain-return");
            var actual = Moves(game, DrawReason);
            var seen = new HashSet<int>();
            Require(actual.Length == 2 && actual.Select(m => m.CardId).Distinct().Count() == 2 &&
                    actual.All(m => m.CardId > 0 && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) &&
                    V(game).Hand.Select(c => c.Id).Order().SequenceEqual(originalHand.Concat(actual.Select(m => m.CardId)).Order()),
                "The original already-paid Kunfen executes one fixed Draw instruction that moves exactly two real entities before its per-card children.");
            for (var child = 0; child < 2; child++)
            {
                ReachOption(game, Gain, "gain-return");
                parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == parentId);
                var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Batch.ParentFrameId == parentId);
                var receipt = parent.ReplacedPaidHpContinuation;
                Require(parent is { InstructionIndex: 2, SkillId: Kunfen } && parent.SkillInstanceId == parentInstance &&
                        receipt is { PaidInstructionIndex: 1, DrawAmount: 2, OwnerSeat: 0, ActualTurnOwnerSeat: 0, ReplacementInstructionIndex: 4 } &&
                        receipt.ProgramFrameId == parentId && receipt.SkillId == Kunfen && receipt.BindingId == parent.TriggerId &&
                        receipt.SkillInstanceId == parentInstance && receipt.GameplayHash == parent.GameplayHash &&
                        receipt.DyingFrameId == dying.Id && receipt.EntryWindowFrameId == entry.Id && receipt.ReplacementFrameId == fengliangId &&
                        receipt.ReplacementSkillId == Fengliang && receipt.ReplacementBindingId == fengliang.TriggerId &&
                        receipt.ReplacementSkillInstanceId == fengliang.SkillInstanceId && receipt.ReplacementGameplayHash == fengliang.GameplayHash &&
                        receipt.GrantedSkillId == Modified && receipt.ActualTurnNumber == turn &&
                        Facts<ProgramReplacedPaidHpContinuationIssuedEvent>(game).Count(e => e.Continuation == receipt) == 1 &&
                        movement.ResumeProgramFrameId == parentId && movement.Batch.AwaitingProgramFrameId is null &&
                        movement.Batch.OriginOwnerSeat == 0 && movement.Batch.OriginSkillId == Kunfen && movement.Batch.OriginSkillInstanceId == parentInstance &&
                        movement.Batch.Movements is [var moved] && actual.Contains(moved) && seen.Add(moved.CardId) &&
                        Facts<ProgramOwnerSkillsReplacedEvent>(game).Single(e => e.SkillId == Fengliang) is { } replaced &&
                        replaced.ResolutionId == fengliangId && replaced.OwnerSeat == 0 && replaced.LostSkillIds.SequenceEqual([Kunfen]) && replaced.GrantedSkillId == Modified &&
                        Facts<DyingResolvedEvent>(game).Count(e => e.ResolutionId == dying.Id && e.Survived) == 1 &&
                        V(game).Skills!.All(s => s.ContentId != Kunfen) && V(game).Skills!.Count(s => s.ContentId == Modified) == 1,
                    "Each actual single-card batch waits for the same removed original instance at cursor two, after one exact awakening replacement and dying return.");
                AssertPrivate(game);
                RejectWrongActor(game);
                game = Cold(game, registry);
                Continue(game);
            }
            ReachOption(game, Tail, "ending-tail");
            Require(Moves(game, DrawReason).SequenceEqual(actual) && seen.Count == 2 &&
                    Facts<ProgramBindingResolvedEvent>(game).Count(e => e.FrameId == parentId && e.SkillId == Kunfen && e.BindingId == parent.TriggerId &&
                        e.SkillInstanceId == parentInstance && e.Window == SkillProgramTriggerWindow.TurnEnding && e.Activated && e.Completed) == 1 &&
                    !game.ResolutionStack.Any(f => f.Id == parentId),
                "Both Draw children return once and finish the original ending candidate without repaying HP, creating a third entity or canceling its paid tail. " + JsonSerializer.Serialize(new
                {
                    endingId, parentId, parentInstance, Seen = seen.Count, SameMoves = Moves(game, DrawReason).SequenceEqual(actual),
                    ParentStillLive = game.ResolutionStack.Any(f => f.Id == parentId),
                    Resolved = Facts<ProgramBindingResolvedEvent>(game).Where(e => e.SkillId == Kunfen).ToArray()
                }));
        }
        else
        {
            ReachOption(game, Driver, "driver-tail");
            Require(Moves(game, DrawReason).Length == 0 && V(game).Hand.Select(c => c.Id).SequenceEqual(originalHand) &&
                    game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == parentId) is { InstructionIndex: 2, ReplacedPaidHpContinuation: null } &&
                    !Facts<ProgramReplacedPaidHpContinuationIssuedEvent>(game).Any(),
                "A normal real loss can awaken without any deleted Kunfen ancestor or invented paid Draw continuation.");
            game = Cold(game, registry);
            Continue(game);
            ReachPlay(game);
            if (permanentTiaoxin)
            {
                Require(game.State.TurnNumber == turn && game.State.CurrentSeat == 0 && game.State.Phase == TurnPhase.Play &&
                        !game.GetHumanLegalActions().Any(a => a.ProgramSkillId == Tiaoxin) &&
                        Facts<ProgramSkillStartedEvent>(game).Count(e => e.OwnerSeat == 0 && e.SkillId == Tiaoxin) == 1 &&
                        Facts<ProgramSkillResolvedEvent>(game).Count(e => e.OwnerSeat == 0 && e.SkillId == Tiaoxin && e.Completed) == 1,
                    "The ordinary loss returns to the same already-used Play, where acquiring another source retains the original Tiaoxin phase quota. " + JsonSerializer.Serialize(new
                    {
                        ExpectedTurn = turn, game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase,
                        Legal = game.GetHumanLegalActions().Where(a => a.ProgramSkillId == Tiaoxin).ToArray(),
                        Runtime = V(game).SkillRuntimeStates!.Single(s => s.SkillId == Tiaoxin)
                    }));
                var activation = registry.GetSkill(Tiaoxin).Program!.Activations.Single().Id;
                var unchanged = State(game);
                Require(!game.Submit(new UseProgramSkillCommand(0, Tiaoxin, activation, [], [3], game.Revision, P(game)!.PromptId)).Accepted &&
                        State(game) == unchanged,
                    "A real second Tiaoxin command after same-phase awakening is rejected atomically instead of resetting the existing permanent skill's quota.");
                game = Cold(game, registry);
            }
            Accept(game, new EndPlayPhaseCommand(0, game.Revision));
            Reach(game, p => IsOptional(p, Modified));
            var modifiedEndingId = game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single().Id;
            var unchangedHp = V(game).Hp;
            var unchangedHand = V(game).Hand.Select(c => c.Id).ToArray();
            var unchangedMovements = game.CardMovements.Count;
            var unchangedLosses = Facts<ProgramSkillHpLostEvent>(game).Count();
            game = Cold(game, registry);
            Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            ReachOption(game, Tail, "ending-tail");
            Require(V(game).Hp == unchangedHp && V(game).Hand.Select(c => c.Id).SequenceEqual(unchangedHand) &&
                    game.CardMovements.Count == unchangedMovements && Facts<ProgramSkillHpLostEvent>(game).Count() == unchangedLosses &&
                    Facts<ProgramBindingResolvedEvent>(game).Count(e => e.FrameId == modifiedEndingId && e.SkillId == Modified && !e.Activated && !e.Completed) == 1,
                "After a genuine ordinary-loss awakening, skipping modified Kunfen returns once without any new HP payment or entity draw.");
        }
        Require(V(game).Skills!.Count(s => s.ContentId == Tiaoxin) == 1 &&
                V(game).SkillRuntimeStates!.Single(s => s.SkillId == Fengliang).Usages.Where(u => u.Scope == SkillUsageScope.Game).Sum(u => u.Count) == 1 &&
                Facts<ProgramOptionChosenEvent>(game).Count(e => e.SkillId == Gain) == (paidByKunfen ? 2 : 0),
            "The owner acquires exactly one effective Tiaoxin state and retains one game-scoped awakening use after all real children return.");
        game = Cold(game, registry);
        Continue(game);
        return new(game, registry, parentId, dying.Id, turn, permanent);
    }

    private static string UseActualTiaoxin(GameEngine game, int target)
    {
        var activation = game.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.UseProgramSkill && a.ProgramSkillId == Tiaoxin);
        var before = game.CreateSnapshot(0).Players[target].HandCount;
        Require(before > 0 && activation.SelectableTargetSeats.Contains(target), "The actual in-range target retains a real hand entity for native Tiaoxin payment.");
        Accept(game, new UseProgramSkillCommand(0, Tiaoxin, activation.ProgramActivationId!, [], [target], game.Revision, P(game)!.PromptId));
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"));
        var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Tiaoxin);
        var instance = frame.SkillInstanceId;
        var choice = P(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card" &&
            c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
        Require(choice.Cards.Count == 0 && game.CreateSnapshot(0).Players[target].Hand.Count == 0,
            "The owner chooses the real foreign hand by its published slot, without learning the target's hidden entity identity.");
        var movementBefore = game.CardMovements.Count == 0 ? 0 : game.CardMovements[^1].Sequence;
        Answer(game, c => c.Id == choice.Id);
        ReachPlay(game);
        var payment = game.CardMovements.Single(m => m.Sequence > movementBefore && m.From == CardLocation.Hand(target) &&
            m.To == CardLocation.DiscardPile && m.Reason.Value == $"skill-program.{Tiaoxin}.SelectAndMoveOwnedCard").CardId;
        Require(game.CreateSnapshot(0).Players[target].HandCount == before - 1 && payment > 0 &&
                game.CardMovements.Count(m => m.CardId == payment && m.From == CardLocation.Hand(target) && m.To == CardLocation.DiscardPile) == 1 &&
                !game.GetHumanLegalActions().Any(a => a.ProgramSkillId == Tiaoxin),
            "One real declined Slash discards one exact target entity and consumes the current Play-phase Tiaoxin allowance.");
        var state = State(game);
        Require(!game.Submit(new UseProgramSkillCommand(0, Tiaoxin, activation.ProgramActivationId!, [], [target], game.Revision, P(game)!.PromptId)).Accepted && State(game) == state,
            "A second same-phase Tiaoxin invocation is rejected without any private, entity or accepted-prefix change.");
        return instance;
    }

    private static void RejectInvalidReplacementCallers()
    {
        var assembly = typeof(StandardClassicGeneralPackage).Assembly;
        string Embedded(string suffix)
        {
            using var stream = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-sp-jiang-wei." + suffix)
                ?? throw new InvalidOperationException("The production owner lifecycle replacement rules are missing.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        var original = Embedded("rules.json");
        var presentation = Embedded("presentation.json");
        foreach (var mutation in new Action<JsonObject>[]
        {
            trigger => trigger["subject"] = "any",
            trigger => trigger["optional"] = true,
            trigger => trigger["usageScope"] = "turn",
            trigger => ((JsonArray)trigger["effects"]!).Add(new JsonObject { ["op"] = "draw", ["target"] = "owner", ["amount"] = 1 })
        })
        {
            var root = JsonNode.Parse(original)!.AsObject();
            var skill = root["skills"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == Fengliang)!;
            mutation(skill["triggers"]![0]!.AsObject());
            var rejected = false;
            try { _ = SkillProgramCatalog.Load(root.ToJsonString(), presentation); }
            catch (InvalidOperationException exception) when (exception.Message.Contains("replacement", StringComparison.OrdinalIgnoreCase)) { rejected = true; }
            Require(rejected, "The shared owner replacement extension rejects observer/optional/non-game/nonterminal callers at the exact loader boundary.");
        }
    }

    private static IEnumerable<T> Facts<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>();
    private static CardMovementRecord[] Moves(GameEngine game, string reason) => game.CardMovements.Where(m => m.Reason.Value == reason).ToArray();
    private static PlayerSnapshot V(GameEngine game) => game.CreateSnapshot(0).Players[0];
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsOption(PendingDecision prompt, string skill, string bind) => prompt.SkillPrompt?.SkillId == skill && prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == bind);
    private static bool IsOptional(PendingDecision prompt, string skill) => prompt.SkillPrompt?.SkillId == skill && prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
    private static void ReachOption(GameEngine game, string skill, string bind) => Reach(game, p => IsOption(p, skill, bind));
    private static void ReachPlay(GameEngine game) => Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine game, Func<PendingDecision, bool> done)
    {
        for (var step = 0; step < 160; step++)
        {
            var prompt = P(game);
            if (prompt is not null && done(prompt)) return;
            if (prompt is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected actual owner boundary: " + Boundary(game));
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The bounded owner replacement fixture did not reach its exact child: " + Boundary(game));
    }
    private static void Use(GameEngine game, string activation) => Accept(game,
        new UseProgramSkillCommand(0, Driver, activation, [], [], game.Revision, P(game)!.PromptId));
    private static void Answer(GameEngine game, Func<PromptChoice, bool> choose)
    {
        var prompt = P(game) ?? throw new InvalidOperationException("The actual owner prompt is absent.");
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(choose).Id, game.Revision));
    }
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void AssertHandPrivacy(GameEngine game) => Require(Enumerable.Range(1, 3).All(s => game.CreateSnapshot(s).Players[0].Hand.Count == 0),
        "Other viewer snapshots expose the owner's hand count without its physical identities.");
    private static void AssertPrivate(GameEngine game)
    {
        Require(P(game) is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(s => game.CreateSnapshot(s).PendingDecision is null),
            "Only the exact owner receives the private native child choice.");
        AssertHandPrivacy(game);
    }
    private static void RejectWrongActor(GameEngine game)
    {
        var prompt = P(game)!;
        var before = State(game);
        Require(!game.Submit(new AnswerPromptCommand(1, prompt.PromptId, prompt.Choices.First().Id, game.Revision)).Accepted && State(game) == before,
            "A wrong actor cannot answer this real parent-owned prompt or mutate views, entities, frames or accepted commands.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "The serialized owner replacement command was rejected.");
    }
    private static string Boundary(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.State.CurrentSeat, game.State.Phase,
        Prompt = P(game) is { } p ? new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId, p.Choices } : null,
        Frames = game.ResolutionStack.Select(f => f is ProgramSkillFrame p ? $"{p.Id}:{p.SkillId}:cursor-{p.InstructionIndex}:parent-{p.WindowContext?.ParentFrameId}" : $"{f.Id}:{f.Kind}:{f.Step}").ToArray()
    });
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), game.CardMovements,
        Events = game.Events.Select(e => $"{e.Sequence}|{JsonSerializer.Serialize(e.Payload, e.Payload.GetType())}").ToArray(),
        Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(restored) == State(game), "Exact owner lifecycle parents, private choices, game usage, paid cursors and physical entities cold-restore identically.");
        return restored;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Start(bool permanentTiaoxin)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(permanentTiaoxin));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, P(game)!.PromptId));
        ReachPlay(game);
        Require(V(game) is { Hp: 2, MaxHp: 5, HandCount: 1 } && V(game).Hand.Single().Kind == CardKind.Peach,
            "The fixed fixture starts from a real dealt Peach and naturally wounded Lord HP, without injected HP, grants or card zones.");
        return (game, registry);
    }
    private sealed class Fixture(bool permanentTiaoxin) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("owner-lifecycle-replacement-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                  {"id":"{{{Driver}}}","revision":1,"activations":[
                    {"id":"prepare","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"driver-tail","options":[{"id":"continue"}]}]},
                    {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":2},{"op":"chooseOption","target":"owner","resultBind":"driver-tail","options":[{"id":"continue"}]}]}]},
                  {"id":"{{{Entry}}}","revision":1,"triggers":[{"id":"actual-zero-entry","window":"dyingEntering","subject":"owner","priority":1000,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"entry-return","options":[{"id":"continue"}]}]}]},
                  {"id":"{{{Hp}}}","revision":1,"triggers":[{"id":"actual-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-return","options":[{"id":"continue"}]}]}]},
                  {"id":"{{{Gain}}}","revision":1,"triggers":[{"id":"actual-kunfen-card","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{{DrawReason}}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-return","options":[{"id":"continue"}]}]}]},
                  {"id":"{{{Tail}}}","revision":1,"triggers":[{"id":"after-owner-ending","window":"turnEnding","subject":"owner","priority":-1000,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"ending-tail","options":[{"id":"continue"}]}]}]}]}
                """, JsonSerializer.Serialize(new
                {
                    schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new[] { Driver, Entry, Hp, Gain, Tail }.ToDictionary(id => id, id => new
                    {
                        name = id, description = "真实失血与技能替换返回观察",
                        optionLabels = new Dictionary<string, string> { ["continue"] = "继续" }
                    })
                }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, id, "原生付款与返回子窗")
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddSkill(new(Peer, "固定其他角色", "角色评分")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? -10000d : 10000d) });
            builder.AddGeneral(new(Owner, "实际逢亮当事人", "supporter", Kunfen, "shu", 4,
                permanentTiaoxin ? [Fengliang, Driver, Entry, Hp, Gain, Tail, Tiaoxin] : [Fengliang, Driver, Entry, Hp, Gain, Tail]) { InitialHp = 1 });
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:owner-replace-peer-{i}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "固定其他角色", "supporter", Peer, "wei", 4));
            builder.AddDeck(new("fixture:owner-replace-deck", "固定真实桃", 1, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 7)).ToArray()
            });
            builder.AddMode(new(Mode, "共享濒死技能替换", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:owner-replace-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
