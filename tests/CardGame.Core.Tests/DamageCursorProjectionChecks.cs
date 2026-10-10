using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DamageCursorProjectionChecks
{
    public static void TypedObserversRequireExactFrozenCandidateAndParentChain()
    {
        const string skillId = "damage-cursor-projection:skill";
        var registry = ContentRegistry.Build(new StandardContentPackage(), new FixturePackage());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 1, PlayerCount = 5, ModeId = "identity:standard-5" }, registry);
        var hash = registry.GetSkill(skillId).Program!.GameplayHash;
        var candidate = new DamageTriggerCandidate(0, "damage", ProgramId: skillId,
            ProgramTriggerId: "damage", SkillInstanceId: "root-instance", GameplayHash: hash, OccurrenceIndex: 1);
        var damage = new DamageFrame(1, 99, 1, 0, 1);
        var window = new DamageTriggerWindowFrame(2, 1, 1, 0, null, null, [candidate], SkillProgramTriggerWindow.AfterDamageApplied);
        var root = Frame(3, "damage", "root-instance", new(SkillProgramTriggerWindow.AfterDamageApplied, 2, 0,
            SourceSeat: 1, TargetSeat: 0, DamageFrameId: 1, Amount: 1, OccurrenceIndex: 1));
        var move = new CardMovementRecord(1, 1, 1, CardKind.Slash, CardLocation.DrawPile, CardLocation.Hand(0), new("fixture.draw"));
        var batch = new CardMovementBatchContext(4, 3, null, 1, [move], [], AwaitingProgramFrameId: 3,
            OriginSkillId: skillId, OriginSkillInstanceId: root.SkillInstanceId, OriginOwnerSeat: 0);
        var gainCandidate = new ProgramTriggerCandidate(0, skillId, "gain", "gain-instance", hash, 0);
        var gainContext = new ProgramSkillWindowContext(SkillProgramTriggerWindow.CardsGained, 4, 0, MovementBatch: batch);
        var movement = new CardsMovedTriggerWindowFrame(4, batch, [gainCandidate], Contexts: [gainContext]);
        var gain = Frame(5, "gain", "gain-instance", gainContext);
        var change = new HpChangeContext(6, 5, 0, 0, HpChangeKind.Loss, 1, 4, 3);
        var lossCandidate = new ProgramTriggerCandidate(0, skillId, "loss", "loss-instance", hash, 0);
        var lossContext = new ProgramSkillWindowContext(SkillProgramTriggerWindow.AfterHpLost, 6, 0, HpChange: change);
        var hp = new HpChangedTriggerWindowFrame(6, change, [lossCandidate], [lossContext], PostEventContinuation.Program, 5);
        var loss = Frame(7, "loss", "loss-instance", lossContext);
        ResolutionFrame[] frames = [damage, window, root, movement, gain, hp, loss];
        var store = typeof(GameEngine).GetField("_resolutionStack", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
        var push = store.GetType().GetMethod("Push")!;
        var replace = store.GetType().GetMethod("Replace")!;
        var project = typeof(GameEngine).GetMethod("TypedDamageProgramObserver", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var resolve = typeof(GameEngine).GetMethod("ResolveDamageCursor", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var frame in frames) push.Invoke(store, [frame]);
        Require(Project()?.Id == root.Id, "The registered movement/HP chain projects to its exact owning damage candidate.");
        Require(ResolvedTop()?.Id == root.Id, "The final cursor resolver accepts the same exact owning candidate.");

        Reject(root with { SkillInstanceId = "other-instance" });
        Reject(root with { GameplayHash = "other-hash" });
        Reject(root with { WindowContext = root.WindowContext! with { OccurrenceIndex = 0 } });
        Reject(root with { WindowContext = root.WindowContext! with { DamageFrameId = 99 } });
        Reject(root with { WindowContext = root.WindowContext! with { Amount = 2 } });
        Reject(root with { WindowContext = root.WindowContext! with { SourceSeat = 3 } });
        Reject(window with { SourceSeat = 3 });
        Reject(window with { TargetSeat = 3 });
        Reject(damage with { Amount = 2 });
        Reject(window with { Candidates = [candidate with { ProgramTriggerId = "other-trigger" }] });
        Reject(window with { CandidateIndex = window.Candidates.Count });
        Reject(movement with { Batch = batch with { ParentFrameId = 99 } });
        Reject(movement with { Batch = batch with { AwaitingProgramFrameId = 99 } });
        Reject(movement with { ResumeProgramFrameId = 99 });
        Reject(movement with { Batch = batch with { OriginSkillInstanceId = "other-instance" } });
        CardsMovedTriggerWindowFrame[] conflictingReturns =
        [
            movement with { ResumeDeclarationFrameId = 99 },
            movement with { ResumeDrawFundedDistinctBasicFrameId = 99 },
            movement with { ResumeRecoveryReplacementFrameId = 99 },
            movement with { ResumePaidCardUseFrameId = 99 },
            movement with { ResumeEquipmentRecastFrameId = 99 },
            movement with { ResumeColorFireAttackFrameId = 99 },
            movement with { ResumeCounterspellPaymentFrameId = 99 },
            movement with { ResumeHistoricalEndingUseFrameId = 99 },
            movement with { ResumeRoundPileAlcoholUseFrameId = 99 },
            movement with { ResumeDrawPhaseObligationFrameId = 99 },
            movement with { ResumeFactionRequestCostFrameId = 99 },
            movement with { ResumeCardSupplyCompletionFrameId = 99 },
            movement with { ResumeResponseCompletionFrameId = 99 },
            movement with { DeferredTurnEndReturn = new(99, 0, 1, batch.Id) }
        ];
        foreach (var conflicting in conflictingReturns) Reject(conflicting);
        Reject(movement with { ResumeFactionRequestCostFrameId = root.Id, ResumeProgramFrameId = null });
        Reject(gain with { SkillInstanceId = "other-instance" });
        Reject(gain with { GameplayHash = "other-hash" });
        Reject(gain with { WindowContext = gainContext with { SourceSeat = 3 } });
        Reject(movement with { Candidates = [gainCandidate with { OccurrenceIndex = 1 }] });
        Reject(hp with { ResumeFrameId = 99 });
        Reject(hp with { Continuation = PostEventContinuation.CardUse });
        Reject(hp with { Continuation = PostEventContinuation.AwaitedProgramMovement });
        Reject(hp with { Change = change with { ParentFrameId = 99 } });
        Reject(loss with { TriggerId = "gain" });
        Reject(loss with { WindowContext = lossContext with { HpChange = change with { Amount = 2 } } });
        CardMovementBatchContext[] malformedBatches =
        [
            batch with { AwaitingProgramFrameId = null },
            batch with { OriginOwnerSeat = null },
            batch with { OriginSkillId = null },
            batch with { OriginSkillInstanceId = null }
        ];
        foreach (var malformedBatch in malformedBatches) RejectSynchronizedBatch(malformedBatch);
        RejectSynchronizedHp(change with { Id = 99 });
        Require(Project()?.Id == root.Id, "Rejected projections leave the original frozen chain unchanged.");

        // Some paid producers move first, then install the owning pending
        // continuation. Both batch return IDs are then null by construction.
        var pendingBatch = batch with { AwaitingProgramFrameId = null };
        var pendingContext = gainContext with { MovementBatch = pendingBatch };
        replace.Invoke(store, [root with { PendingMovementContinuation = new(root.OwnerSeat, 0, null) }]);
        replace.Invoke(store, [movement with { Batch = pendingBatch, Contexts = [pendingContext] }]);
        replace.Invoke(store, [gain with { WindowContext = pendingContext }]);
        try
        {
            Require(Project()?.Id == root.Id && ResolvedTop()?.Id == root.Id,
                "The producer-owned pending movement is a valid return when payment preceded installing the continuation.");
        }
        finally
        {
            replace.Invoke(store, [root]); replace.Invoke(store, [movement]); replace.Invoke(store, [gain]);
        }

        // This marker used to enable the legacy weak observer ride. It cannot
        // override a rejected frozen context or candidate in the final resolver.
        replace.Invoke(store, [root with { ConvertingGift = new(true, "gift-movement", 1, 0, []) }]);
        try
        {
            Reject(gain with { WindowContext = gainContext with { SourceSeat = 3 } });
            Reject(gain with { SkillInstanceId = "other-instance" });
            Reject(loss with { WindowContext = lossContext with { HpChange = change with { Amount = 2 } } });
            foreach (var conflicting in conflictingReturns) Reject(conflicting);
            Reject(movement with { ResumeFactionRequestCostFrameId = root.Id, ResumeProgramFrameId = null });
            foreach (var malformedBatch in malformedBatches) RejectSynchronizedBatch(malformedBatch);
            RejectSynchronizedHp(change with { Id = 99 });
            Reject(hp with { Continuation = PostEventContinuation.CardUse });
            Reject(hp with { Continuation = PostEventContinuation.AwaitedProgramMovement });
        }
        finally { replace.Invoke(store, [root]); }

        TypedRecoveryReturnsRequireExactProducer();

        ProgramSkillFrame Frame(long id, string trigger, string instance, ProgramSkillWindowContext context) =>
            new(id, 0, skillId, trigger, hash, 1, [], []) { TriggerId = trigger, SkillInstanceId = instance, WindowContext = context };
        ProgramSkillFrame? Project() => (ProgramSkillFrame?)project.Invoke(game, [window.Id]);
        ResolutionFrame? ResolvedTop()
        {
            var result = resolve.Invoke(game, [window])!;
            Require(!(bool)result.GetType().GetProperty("HasPaidObserverSubtree")!.GetValue(result)!,
                "A malformed ordinary edge cannot gain a receipt-specific exemption.");
            return (ResolutionFrame?)result.GetType().GetProperty("Top")!.GetValue(result);
        }
        void RequireRejected(string label)
        {
            Require(Project() is null, $"Malformed {label} must not borrow a damage cursor exemption.");
            Require(ResolvedTop() is null, $"The final resolver must reject malformed {label} before any legacy fallback.");
        }
        void RejectSynchronizedBatch(CardMovementBatchContext malformedBatch)
        {
            var context = gainContext with { MovementBatch = malformedBatch };
            replace.Invoke(store, [movement with { Batch = malformedBatch, Contexts = [context] }]);
            replace.Invoke(store, [gain with { WindowContext = context }]);
            try { RequireRejected("producer batch with matching frozen contexts"); }
            finally { replace.Invoke(store, [movement]); replace.Invoke(store, [gain]); }
        }
        void RejectSynchronizedHp(HpChangeContext malformedChange)
        {
            var context = lossContext with { HpChange = malformedChange };
            replace.Invoke(store, [hp with { Change = malformedChange, Contexts = [context] }]);
            replace.Invoke(store, [loss with { WindowContext = context }]);
            try { RequireRejected("producer HP change with matching frozen contexts"); }
            finally { replace.Invoke(store, [hp]); replace.Invoke(store, [loss]); }
        }
        void Reject(ResolutionFrame malformed)
        {
            var original = frames.Single(frame => frame.Id == malformed.Id);
            replace.Invoke(store, [malformed]);
            try
            {
                RequireRejected(malformed.Kind.ToString());
            }
            finally { replace.Invoke(store, [original]); }
        }
    }

    private static void TypedRecoveryReturnsRequireExactProducer()
    {
        const string skillId = "damage-cursor-projection:skill";
        var registry = ContentRegistry.Build(new StandardContentPackage(), new FixturePackage());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 1, PlayerCount = 5, ModeId = "identity:standard-5" }, registry);
        var hash = registry.GetSkill(skillId).Program!.GameplayHash;
        var policy = registry.GetSkill(skillId).Program!.CardPolicies.Single();
        var candidate = new DamageTriggerCandidate(0, "recover", ProgramId: skillId, ProgramTriggerId: "recover",
            SkillInstanceId: "recovery-root", GameplayHash: hash, OccurrenceIndex: 0);
        var damage = new DamageFrame(1, 99, 1, 0, 1);
        var window = new DamageTriggerWindowFrame(2, 1, 1, 0, null, null, [candidate], SkillProgramTriggerWindow.AfterDamageApplied);
        var root = Frame(3, 0, "recover", "recovery-root", new(SkillProgramTriggerWindow.AfterDamageApplied, 2, 0,
            SourceSeat: 1, TargetSeat: 0, DamageFrameId: 1, Amount: 1));
        var replacement = new RecoveryReplacementCandidate(1, skillId, "replacement-instance", policy.Id, policy.Value, policy.ProviderDrawCount);
        var attempt = new RecoveryAttempt(4, 0, 0, 1, 2, 1, 0, [replacement], new(RecoveryAttemptProducer.Program, 1));
        var recovery = new RecoveryReplacementFrame(4, 3, attempt, new(PostEventContinuation.Program, 3), RecoveryReplacementStage.RecoveryApplied, 0);
        var change = new HpChangeContext(5, 4, 0, 1, HpChangeKind.Recovery, 1, 1, 2);
        var recoveredCandidate = new ProgramTriggerCandidate(1, skillId, "recovered", "recovered-instance", hash, 0);
        var recoveredContext = new ProgramSkillWindowContext(SkillProgramTriggerWindow.AfterHpRecovered, 5, 1,
            SourceSeat: 0, TargetSeat: 1, Amount: 1, HpChange: change);
        var hp = new HpChangedTriggerWindowFrame(5, change, [recoveredCandidate], [recoveredContext], PostEventContinuation.RecoveryReplacement, 4);
        var observer = Frame(6, 1, "recovered", "recovered-instance", recoveredContext);
        var dying = new DyingFrame(8, 6, 1, null, [1, 2, 3, 4, 0], 0, DyingContinuationKind.ProgramSkill);
        var response = Frame(9, 1, "self", "self-instance", new(SkillProgramTriggerWindow.SelfDyingResponse, 8, 1, TargetSeat: 1));
        ResolutionFrame[] frames = [damage, window, root, recovery, hp, observer, dying, response];
        var store = typeof(GameEngine).GetField("_resolutionStack", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var push = store.GetType().GetMethod("Push")!;
        var replace = store.GetType().GetMethod("Replace")!;
        var complete = store.GetType().GetMethod("CompleteTop")!;
        var project = typeof(GameEngine).GetMethod("ProjectTypedRecoveryDamageCursor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var resolve = typeof(GameEngine).GetMethod("ResolveDamageCursor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var events = (List<IGameEvent>)typeof(GameEngine).GetField("_pendingEvents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var pendingHp = (List<HpChangeContext>)typeof(GameEngine).GetField("_pendingHpChanges", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        events.Add(new RecoveryReplacementChosenEvent(4, 0, 1, replacement.SkillId, replacement.SkillInstanceId, replacement.PolicyId, 1, replacement.RecoveryAmount));
        events.Add(new ProgramSkillHpLostEvent(6, skillId, 1, 1, 0));
        events.Add(new PlayerDyingEvent(8, 1, null));
        events.Add(new ProgramBindingStartedEvent(9, skillId, "self", "self-instance", 1, SkillProgramTriggerWindow.SelfDyingResponse));
        pendingHp.Add(new(7, 6, null, 1, HpChangeKind.Loss, 1, 1, 0));
        foreach (var frame in frames) push.Invoke(store, [frame]);
        Require(ProjectedRoot()?.Id == root.Id && ProjectedDying() == dying.Id && ResolvedRoot()?.Id == root.Id,
            "A direct recovery's exact HP/LoseHp/Dying/response chain carries its root and owned Dying together.");
        foreach (var marker in new[] { false, true })
        {
            frames[2] = marker ? root with { ConvertingGift = new(true, "legacy-marker", 1, 0, []) } : root;
            replace.Invoke(store, [frames[2]]);
            Reject(recovery with { ParentFrameId = 99 });
            Reject(recovery with { Return = recovery.Return with { ResumeFrameId = 99 } });
            Reject(recovery with { Return = recovery.Return with { Continuation = PostEventContinuation.AwaitedProgramMovement } });
            Reject(recovery with { Return = recovery.Return with { CardId = 1 } });
            Reject(recovery with { Attempt = attempt with { Id = 99 } });
            Reject(recovery with { Attempt = attempt with { Amount = 2 } });
            Reject(recovery with { Attempt = attempt with { SourceSeat = 1 } });
            Reject(recovery with { Attempt = attempt with { Completion = attempt.Completion with { Producer = RecoveryAttemptProducer.SilverLion } } });
            Reject(recovery with { Attempt = attempt with { Completion = attempt.Completion with { InstructionIndex = 2 } } });
            Reject(((ProgramSkillFrame)frames[2]) with { InstructionIndex = 99 });
            Reject(((ProgramSkillFrame)frames[2]) with { SkillInstanceId = "foreign-root-instance" });
            Reject(((ProgramSkillFrame)frames[2]) with { OwnerSeat = 1 });
            Reject(((ProgramSkillFrame)frames[2]) with { TriggerId = "macro" });
            Reject(((ProgramSkillFrame)frames[2]) with { GameplayHash = "foreign-root-hash" });
            Reject(recovery with { Stage = RecoveryReplacementStage.RewardApplied });
            Reject(hp with { ResumeFrameId = 99 });
            Reject(hp with { Continuation = PostEventContinuation.Program });
            RejectSynchronizedHp(change with { SourceSeat = 1 });
            RejectSynchronizedHp(change with { TargetSeat = 0 });
            Reject(dying with { ParentFrameId = 99 });
            Reject(dying with { VictimSeat = 0 });
            Reject(dying with { Continuation = DyingContinuationKind.Damage });
            Reject(dying with { KillerSeat = 2 });
            Reject(response with { SkillInstanceId = "foreign-instance" });
            Reject(response with { GameplayHash = "foreign-hash" });
            Reject(response with { WindowContext = response.WindowContext! with { ParentFrameId = 99 } });
            Reject(response with { WindowContext = response.WindowContext! with { SourceSeat = 1 } });
            var enteredIndex = events.FindIndex(e => e is PlayerDyingEvent);
            var entered = events[enteredIndex];
            events[enteredIndex] = new PlayerDyingEvent(99, dying.VictimSeat, null);
            try { RequireRejected(); }
            finally { events[enteredIndex] = entered; }
        }
        replace.Invoke(store, [response with { InstructionIndex = 2 }]);
        var secondOrdinaryDying = dying with { Id = 16, ParentFrameId = response.Id };
        push.Invoke(store, [secondOrdinaryDying]);
        try { RequireRejected(); }
        finally { complete.Invoke(store, [secondOrdinaryDying.Id, secondOrdinaryDying.Kind]); replace.Invoke(store, [response]); }
        UnknownDyingEntryMacroRetainsItsOwningLegacyProof();
        replace.Invoke(store, [root]); frames[2] = root;
        foreach (var frame in frames.Skip(4).Reverse()) complete.Invoke(store, [frame.Id, frame.Kind]);
        var rewarded = recovery with { Stage = RecoveryReplacementStage.RewardApplied };
        replace.Invoke(store, [rewarded]);
        var ledger = (List<CardMovementRecord>)typeof(GameEngine).GetField("_cardMovements", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        var historicalReward = new CardMovementRecord(ledger.Count + 1, 1, 2, CardKind.Slash, CardLocation.DrawPile, CardLocation.Hand(0), new("skill-program.recovery-replacement.reward"));
        ledger.Add(historicalReward);
        var move = new CardMovementRecord(ledger.Count + 1, 1, 1, CardKind.Slash, CardLocation.DrawPile, CardLocation.Hand(0), new("skill-program.recovery-replacement.reward"));
        ledger.Add(move);
        var batch = new CardMovementBatchContext(10, 4, null, 1, [move], [], OriginSkillId: skillId, OriginSkillInstanceId: root.SkillInstanceId, OriginOwnerSeat: 0);
        var rewardReceipt = new RecoveryRewardMovementReceipt(move.Sequence - 1, move.Sequence) { Batches = [batch] };
        rewarded = rewarded with { RewardMovementReceipt = rewardReceipt };
        replace.Invoke(store, [rewarded]);
        Require(rewardReceipt.Batches is System.Collections.IList { IsReadOnly: true } &&
            rewardReceipt.Batches[0].Movements is System.Collections.IList { IsReadOnly: true } &&
            rewardReceipt.Batches[0].SourceCounts is System.Collections.IList { IsReadOnly: true },
            "The owning reward receipt freezes its issued batches and nested movement payloads.");
        var gainedCandidate = new ProgramTriggerCandidate(0, skillId, "gain", "reward-instance", hash, 0);
        var gainedContext = new ProgramSkillWindowContext(SkillProgramTriggerWindow.CardsGained, 10, 0, MovementBatch: batch);
        var movement = new CardsMovedTriggerWindowFrame(10, batch, [gainedCandidate], Contexts: [gainedContext])
            { ResumeRecoveryReplacementFrameId = 4 };
        var gained = Frame(11, 0, "gain", "reward-instance", gainedContext);
        push.Invoke(store, [movement]); push.Invoke(store, [gained]);
        Require(ProjectedRoot()?.Id == root.Id && ProjectedDying() is null && ResolvedRoot()?.Id == root.Id,
            "The exact recovery reward and frozen gain observer return without retaining a completed Dying.");
        foreach (var malformed in new[] { movement with { ResumeRecoveryReplacementFrameId = 99 },
            movement with { ResumeProgramFrameId = 3 }, movement with { ResumePaidCardUseFrameId = 99 },
            movement with { ResumeDeclarationFrameId = 99 } })
        {
            replace.Invoke(store, [malformed]);
            try { RequireRejected(); }
            finally { replace.Invoke(store, [movement]); }
        }
        foreach (var marker in new[] { false, true })
        {
            replace.Invoke(store, [marker ? root with { ConvertingGift = new(true, "legacy-marker", 1, 0, []) } : root]);
            RejectSynchronizedRewardBatch(batch with { Movements = [historicalReward] });
            RejectSynchronizedRewardBatch(batch with { SourceCounts = [new(CardLocation.DrawPile, 2, 1)] });
            RejectRewardReceipt(null);
            RejectRewardReceipt(rewardReceipt with { SequenceBefore = move.Sequence });
            RejectRewardReceipt(rewardReceipt with { SequenceAfter = move.Sequence - 1 });
            complete.Invoke(store, [gained.Id, gained.Kind]);
            complete.Invoke(store, [movement.Id, movement.Kind]);
            var foreignBatch = batch with { Id = 12 };
            var foreignContext = gainedContext with { ParentFrameId = 12, MovementBatch = foreignBatch };
            push.Invoke(store, [movement with { Id = 12, Batch = foreignBatch, Contexts = [foreignContext] }]);
            push.Invoke(store, [gained with { WindowContext = foreignContext }]);
            try { RequireRejected(); }
            finally
            {
                complete.Invoke(store, [gained.Id, gained.Kind]);
                complete.Invoke(store, [12L, movement.Kind]);
                push.Invoke(store, [movement]); push.Invoke(store, [gained]);
            }
        }
        replace.Invoke(store, [root]);
        var macroCandidate = candidate with { CandidateId = "macro", ProgramTriggerId = "macro" };
        replace.Invoke(store, [window with { Candidates = [macroCandidate] }]);
        replace.Invoke(store, [root with { ActivationId = "macro", TriggerId = "macro" }]);
        replace.Invoke(store, [rewarded with { Attempt = attempt with
            { Completion = new(RecoveryAttemptProducer.SilverLion, MoveReason: new("fixture.silver-lion")) } }]);
        var unknown = Projection();
        Require(unknown.GetType().GetProperty("Root")!.GetValue(unknown) is null &&
            !(bool)unknown.GetType().GetProperty("IsMalformed")!.GetValue(unknown)!,
            "A special compiled macro's recovery remains unknown to the primitive pilot and needs its owning receipt validator.");
        _ = ResolvedRoot();

        ProgramSkillFrame Frame(long id, int owner, string trigger, string instance, ProgramSkillWindowContext context) =>
            new(id, owner, skillId, trigger, hash, 1, [], []) { TriggerId = trigger, SkillInstanceId = instance, WindowContext = context };
        object Projection() => project.Invoke(game, [window.Id])!;
        ProgramSkillFrame? ProjectedRoot() => (ProgramSkillFrame?)Projection().GetType().GetProperty("Root")!.GetValue(Projection());
        long? ProjectedDying() => (long?)Projection().GetType().GetProperty("OwnedDyingId")!.GetValue(Projection());
        ResolutionFrame? ResolvedRoot()
        {
            var result = resolve.Invoke(game, [window])!;
            return (ResolutionFrame?)result.GetType().GetProperty("Top")!.GetValue(result);
        }
        bool ResolverRejects()
        {
            try { return ResolvedRoot() is null; }
            catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException) { return true; }
        }
        void RequireRejected() => Require(ProjectedRoot() is null && ProjectedDying() is null && ResolverRejects(),
            "A malformed recovery/producer/Dying return cannot borrow a legacy cursor exemption.");
        void Reject(ResolutionFrame malformed)
        {
            var original = frames.Single(f => f.Id == malformed.Id);
            replace.Invoke(store, [malformed]);
            try { RequireRejected(); }
            finally { replace.Invoke(store, [original]); }
        }
        void RejectSynchronizedHp(HpChangeContext malformed)
        {
            var context = recoveredContext with { SourceSeat = malformed.SourceSeat, TargetSeat = malformed.TargetSeat, HpChange = malformed };
            replace.Invoke(store, [hp with { Change = malformed, Contexts = [context] }]);
            replace.Invoke(store, [observer with { WindowContext = context }]);
            try { RequireRejected(); }
            finally { replace.Invoke(store, [hp]); replace.Invoke(store, [observer]); }
        }
        void RejectSynchronizedRewardBatch(CardMovementBatchContext malformed)
        {
            var context = gainedContext with { MovementBatch = malformed };
            replace.Invoke(store, [movement with { Batch = malformed, Contexts = [context] }]);
            replace.Invoke(store, [gained with { WindowContext = context }]);
            try { RequireRejected(); }
            finally { replace.Invoke(store, [movement]); replace.Invoke(store, [gained]); }
        }
        void RejectRewardReceipt(RecoveryRewardMovementReceipt? malformed)
        {
            replace.Invoke(store, [rewarded with { RewardMovementReceipt = malformed }]);
            try { RequireRejected(); }
            finally { replace.Invoke(store, [rewarded]); }
        }
        void UnknownDyingEntryMacroRetainsItsOwningLegacyProof()
        {
            replace.Invoke(store, [root]);
            complete.Invoke(store, [response.Id, response.Kind]);
            var roundField = typeof(GameEngine).GetField("_roundNumber", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var turnField = typeof(GameEngine).GetField("_turnNumber", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var originalRound = roundField.GetValue(game); var originalTurn = turnField.GetValue(game);
            roundField.SetValue(game, 1); turnField.SetValue(game, 1);
            var eventsBefore = events.Count; var hpBefore = pendingHp.Count;
            var moves = (List<CardMovementRecord>)typeof(GameEngine).GetField("_cardMovements", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
            var movesBefore = moves.Count;
            var entryCandidate = new ProgramTriggerCandidate(0, skillId, "entry-macro", "entry-macro-instance", hash, 0);
            var entry = new ProgramLifecycleTriggerWindowFrame(10, dying.VictimSeat, SkillProgramTriggerWindow.DyingEntering,
                [entryCandidate], ProgramLifecycleContinuation.ResumeDyingEntry, new(0, 0, true)) { ResumeDyingFrameId = dying.Id };
            var entryProgram = Frame(11, 0, "entry-macro", "entry-macro-instance",
                new(SkillProgramTriggerWindow.DyingEntering, entry.Id, 0, TargetSeat: dying.VictimSeat));
            push.Invoke(store, [entry]); push.Invoke(store, [entryProgram]);
            var capture = typeof(GameEngine).GetMethod("CaptureDyingSuitsOriginalCursor", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var originalCursor = (DyingSuitsOriginalCursor)capture.Invoke(game, [dying, entry])!;
            var cursorHashMethod = typeof(GameEngine).GetMethod("DyingSuitsCursorHash", BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(DyingSuitsOriginalCursor));
            var cursorHash = (string)cursorHashMethod.Invoke(null, [originalCursor])!;
            var source = new CardConversionSource(skillId, "entry-macro", 0, entryProgram.SkillInstanceId);
            var drawn = new CardMovementRecord(movesBefore + 1, 1, 1, CardKind.Slash, CardLocation.DrawPile,
                CardLocation.Hand(2), new($"skill-program.{skillId}.{SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach}.draw"));
            moves.Add(drawn);
            entryProgram = entryProgram with { PendingMovementContinuation = new(2, 0, null),
                DyingSuits = new(1, source, hash, 1, 1, 0, entry.Id, dying.Id, dying.VictimSeat, originalCursor,
                    DyingSuitsStage.Drawing, [], [], [], RecipientSeat: 2, DrawBefore: movesBefore, DrawAfter: drawn.Sequence, ActualDrawCount: 1) };
            replace.Invoke(store, [entryProgram]);
            var drawingBatch = new CardMovementBatchContext(12, entryProgram.Id, null, 1, [drawn], [], entryProgram.Id,
                OriginSkillId: skillId, OriginSkillInstanceId: entryProgram.SkillInstanceId, OriginOwnerSeat: 0);
            var drawContext = new ProgramSkillWindowContext(SkillProgramTriggerWindow.CardsGained, 12, 2, MovementBatch: drawingBatch);
            var drawing = new CardsMovedTriggerWindowFrame(12, drawingBatch,
                [new(2, skillId, "gain", "nested-loss-instance", hash, 0)], Contexts: [drawContext]);
            var nestedLoss = Frame(13, 2, "gain", "nested-loss-instance", drawContext);
            var nestedDying = new DyingFrame(15, nestedLoss.Id, 2, null, [2, 3, 4, 0, 1], 0, DyingContinuationKind.ProgramSkill);
            ResolutionFrame[] suffix = [drawing, nestedLoss, nestedDying];
            events.Add(new DyingSuitsOriginalCursorIssuedEvent(entryProgram.Id, cursorHash));
            events.Add(new DyingSuitsDrawIssuedEvent(entryProgram.Id, source, hash, 1, 1, 0, entry.Id, dying.Id, dying.VictimSeat, 2, movesBefore, drawn.Sequence, 1));
            events.Add(new ProgramBindingStartedEvent(nestedLoss.Id, skillId, "gain", nestedLoss.SkillInstanceId, 2, SkillProgramTriggerWindow.CardsGained));
            events.Add(new PlayerDyingEvent(nestedDying.Id, 2, null));
            foreach (var frame in suffix) push.Invoke(store, [frame]);
            try
            {
                var legacy = typeof(GameEngine).GetMethod("IsDyingSuitsProgramDying", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var unknown = Projection();
                Require((bool)legacy.Invoke(game, [])! && unknown.GetType().GetProperty("Root")!.GetValue(unknown) is null &&
                    !(bool)unknown.GetType().GetProperty("IsMalformed")!.GetValue(unknown)!,
                    "An exact issued Dying-suits macro owns its nested Dying; the primitive pilot delegates before requiring its outer Dying to remain active.");
                _ = ResolvedRoot();
            }
            finally
            {
                foreach (var frame in suffix.Reverse()) complete.Invoke(store, [frame.Id, frame.Kind]);
                complete.Invoke(store, [entryProgram.Id, entryProgram.Kind]); complete.Invoke(store, [entry.Id, entry.Kind]);
                events.RemoveRange(eventsBefore, events.Count - eventsBefore); pendingHp.RemoveRange(hpBefore, pendingHp.Count - hpBefore);
                moves.RemoveRange(movesBefore, moves.Count - movesBefore);
                roundField.SetValue(game, originalRound); turnField.SetValue(game, originalTurn); push.Invoke(store, [response]);
            }
        }
    }

    private sealed class FixturePackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("damage-cursor-projection", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"damage-cursor-projection:skill","revision":1,"cardPolicies":[
                 {"id":"replacement","kind":"redirectOwnTurnFactionRecovery","factionId":"wu","ownerRole":"lord","value":1,"providerDrawCount":1}],"triggers":[
                 {"id":"damage","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"draw","target":"owner","amount":1}]},
                 {"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                 {"id":"loss","window":"afterHpLost","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":1}]},
                 {"id":"recover","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"recover","target":"owner","amount":1}]},
                 {"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                 {"id":"macro","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":true,"effects":[{"op":"observeDamageSourceHandAndGive","target":"owner","amount":4}]},
                 {"id":"entry-macro","window":"dyingEntering","subject":"any","optional":true,"usageScope":"round","usageLimit":1,"effects":[{"op":"drawThenDiscardSuitsForDyingPeach","target":"owner"}]},
                 {"id":"self","window":"selfDyingResponse","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"return","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":1}]}]}]}
                """, """
                {"schemaVersion":3,"skills":{"damage-cursor-projection:skill":{"name":"Cursor fixture","description":"Typed observer identity fixture","optionLabels":{"continue":"Continue"}}}}
                """);
            builder.AddSkill(new ContentSkillDefinition("damage-cursor-projection:skill", "Cursor fixture", "Typed observer identity fixture")
                { Program = catalog.Programs["damage-cursor-projection:skill"] });
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
