using System.Collections;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class InvariantHistoryIndexChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void EmptyAndIssuedLedgersKeepHistoricalValidationAfterSourceLoss()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new HistoricalCapabilities());
        var empty = Create();
        Assert(empty, "AssertOriginalHandEntityState");
        Assert(empty, "AssertFixedDistanceOnePolicies");
        Assert(empty, "AssertJudgedRankSlashPolicies");
        Assert(empty, "AssertPhaseHandSeizurePrograms");
        TypedSnapshotsReuseOnlyUnchangedFacts(empty);

        // This is a narrow host-state audit, not command replay: the source is
        // absent while its historical facts and durable state remain present.
        OriginalHand();
        FixedDistance();
        BasicDiscard();
        PhaseHandDebt();

        GameEngine Create()
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 1, PlayerCount = 5, ModeId = "identity:standard-5", UseInteractiveSetup = true
            }, registry);
            foreach (var player in Field<IReadOnlyList<CharacterState>>(game, "_players"))
                foreach (var grant in player.SkillGrants.Grants.ToArray()) player.SkillGrants.RemoveGrant(grant.GrantId);
            Require(Field<IReadOnlyList<CharacterState>>(game, "_players").All(p => p.SkillGrants.Grants.Count == 0),
                "The invariant audit cannot rely on a currently loaded source skill.");
            return game;
        }

        void OriginalHand()
        {
            var game = Create();
            Assert(game, "AssertOriginalHandEntityState"); // Warm the empty typed history before issuance.
            const string skill = "ol:huaiyuan", stateId = "sui";
            var source = new CardConversionSource(skill, "initialize-sui", 0, "historical-original-hand");
            var hash = registry.GetSkill(skill).Program!.GameplayHash;
            var keyType = typeof(GameEngine).GetNestedType("OriginalHandStateKey", BindingFlags.NonPublic)!;
            var stateType = typeof(GameEngine).GetNestedType("OriginalHandEntityState", BindingFlags.NonPublic)!;
            var entityType = typeof(GameEngine).GetNestedType("OriginalHandEntity", BindingFlags.NonPublic)!;
            var key = Activator.CreateInstance(keyType, [0, skill, stateId])!;
            var state = Activator.CreateInstance(stateType, [100L, source, hash, Array.CreateInstance(entityType, 0)])!;
            Field<IDictionary>(game, "_originalHandEntityStates").Add(key, state);
            Field<IDictionary>(game, "_originalHandPermanentBonuses").Add(101L,
                new OriginalHandPermanentBonus(101, 0, skill, stateId, 0, OriginalHandBenefitKind.HandLimit));
            Pending(game).AddRange([
                new OriginalHandEntitiesInitializedEvent(100, source, hash, stateId, 0, 0),
                new OriginalHandPermanentBonusGrantedEvent(101, 0, skill, stateId, 0, OriginalHandBenefitKind.HandLimit),
                new OriginalHandBonusTransferredEvent(102, 101, 0, skill, stateId, 0, 1, OriginalHandBenefitKind.HandLimit),
                new OriginalHandBonusTransferredEvent(103, 101, 0, skill, stateId, 1, 0, OriginalHandBenefitKind.HandLimit)
            ]);
            Assert(game, "AssertOriginalHandEntityState");
            CommitFacts(game);
            Assert(game, "AssertOriginalHandEntityState");
            Pending(game).Add(new OriginalHandBonusTransferredEvent(104, 101, 0, skill, stateId, 1, 2, OriginalHandBenefitKind.HandLimit));
            Reject(game, "AssertOriginalHandEntityState", "A transferred bonus still verifies its historical previous recipient after source loss.");

            var orphan = Create();
            Assert(orphan, "AssertOriginalHandEntityState");
            Pending(orphan).Add(new OriginalHandEntitiesInitializedEvent(100, source, hash, stateId, 0, 0));
            Reject(orphan, "AssertOriginalHandEntityState", "A newly indexed initialization cannot hide behind empty durable collections.");
        }

        void FixedDistance()
        {
            var game = Create();
            Assert(game, "AssertFixedDistanceOnePolicies");
            const string skill = "ol:fenxun";
            var program = registry.GetSkill(skill).Program!;
            var activation = program.Activations.Single(a => a.Effects is [{ Op: SkillProgramEffectOp.GrantFixedDistanceOneTurnPolicy }]);
            var ending = program.Triggers.Single(t => t.Effects is [{ Op: SkillProgramEffectOp.SettleFixedDistanceOneEndingDebt }]);
            var grant = new FixedDistanceOneTurnGrant(200, 1, new(skill, activation.Id, 0, "historical-fixed-distance"),
                program.GameplayHash, 1, 0, 1, ending.Id);
            Pending(game).AddRange([new TurnStartedEvent(1, 0), new ProgramSkillStartedEvent(200, 0, skill, activation.Id),
                new FixedDistanceOneTurnGrantedEvent(grant), new FixedDistanceOneDebtConsumedEvent(grant, false)]);
            Assert(game, "AssertFixedDistanceOnePolicies");
            CommitFacts(game);
            Assert(game, "AssertFixedDistanceOnePolicies");
            Pending(game).Add(new FixedDistanceOneDebtConsumedEvent(grant, true));
            Reject(game, "AssertFixedDistanceOnePolicies", "Committed and pending debt consumptions cannot repeat after the source disappears.");

            var invalid = Create();
            Assert(invalid, "AssertFixedDistanceOnePolicies");
            Pending(invalid).AddRange([new TurnStartedEvent(1, 0), new ProgramSkillStartedEvent(200, 0, skill, activation.Id),
                new FixedDistanceOneTurnGrantedEvent(grant with { GameplayHash = "foreign-gameplay-hash" })]);
            Reject(invalid, "AssertFixedDistanceOnePolicies", "An empty index must notice a later issuance and retain its immutable program hash check.");
        }

        void BasicDiscard()
        {
            var game = Create();
            typeof(GameEngine).GetField("_turnNumber", Flags)!.SetValue(game, 1);
            typeof(GameEngine).GetField("_turnProgression", Flags)!.SetValue(game, new ActualTurnProgression(ActualTurnKind.Normal, 1, 1));
            Assert(game, "AssertJudgedRankSlashPolicies");
            const string skill = "ol:shenxian";
            var program = registry.GetSkill(skill).Program!;
            var trigger = program.Triggers.Single(t => t.Effects is [{ Op: SkillProgramEffectOp.DrawFromOtherActualBasicDiscard }]);
            var movements = Field<List<CardMovementRecord>>(game, "_cardMovements");
            var sequence = movements.Count + 1;
            movements.Add(new CardMovementRecord(sequence, 1, 999, CardKind.Slash,
                CardLocation.Hand(1), CardLocation.DiscardPile, CardMoveReasons.HandLimitDiscard));
            var source = new CardUseEffectSource(skill, trigger.Id, 0, "historical-basic-discard");
            var issued = new OtherActualBasicDiscardDrawIssuedEvent(300, source, program.GameplayHash, 1, 1, 400, sequence, 1, 999);
            Pending(game).AddRange([new ProgramBindingStartedEvent(300, skill, trigger.Id, source.SkillInstanceId, 0,
                SkillProgramTriggerWindow.DiscardPileReceived), issued]);
            Assert(game, "AssertJudgedRankSlashPolicies");
            CommitFacts(game);
            Assert(game, "AssertJudgedRankSlashPolicies");
            Require((bool)typeof(GameEngine).GetMethod("OtherActualBasicDiscardAlreadyIssued", Flags)!.Invoke(game, [0, skill])!,
                "The quota query and invariant share the complete issued history.");
            Pending(game).Add(issued with { Source = source with { OwnerSeat = 2, SkillInstanceId = "foreign-instance" } });
            Reject(game, "AssertJudgedRankSlashPolicies", "Historical discard receipts still reject a foreign producer without a current source.");
        }

        void PhaseHandDebt()
        {
            var game = Create();
            Assert(game, "AssertPhaseHandSeizurePrograms");
            const string skill = "ol:lihun";
            var program = registry.GetSkill(skill).Program!;
            var activation = program.Activations.Single(a => a.Effects is [{ Op: SkillProgramEffectOp.DiscardTurnOverAndTakeHand }]);
            var continuation = program.Triggers.Single(t => t.Effects is [{ Op: SkillProgramEffectOp.ReturnIssuedPhaseHandDebt }]);
            typeof(GameEngine).GetField("_turnNumber", Flags)!.SetValue(game, 1);
            typeof(GameEngine).GetField("_currentSeat", Flags)!.SetValue(game, 0);
            typeof(GameEngine).GetField("_phase", Flags)!.SetValue(game, TurnPhase.Play);
            typeof(GameEngine).GetField("_cardUseDebitPhaseInstanceId", Flags)!.SetValue(game, 1);
            var issued = new PhaseHandSeizureIssuedEvent(500, new(skill, activation.Id, 0, "historical-phase-hand"),
                program.GameplayHash, continuation.Id, 1, 1, 1, 2, false, 0, 2);
            Pending(game).Add(issued);
            Assert(game, "AssertPhaseHandSeizurePrograms");
            Require(Unsettled().SequenceEqual([issued]), "Pending issuance remains due with its original source absent.");
            CommitFacts(game);
            Assert(game, "AssertPhaseHandSeizurePrograms");
            Require(Unsettled().SequenceEqual([issued]), "Committing the journal does not duplicate or lose the original debt.");
            typeof(GameEngine).GetField("_phase", Flags)!.SetValue(game, TurnPhase.Draw);
            Reject(game, "AssertPhaseHandSeizurePrograms", "An issued debt cannot escape its original Play phase when its source is absent.");
            typeof(GameEngine).GetField("_phase", Flags)!.SetValue(game, TurnPhase.Play);
            var settled = new PhaseHandDebtSettledEvent(500, 501, 0, 1, "returned", 2, 2, 2, 4);
            Pending(game).Add(settled);
            Assert(game, "AssertPhaseHandSeizurePrograms");
            Require(!Unsettled().Any(), "A pending settlement closes the exact committed issuance immediately.");
            CommitFacts(game);
            Assert(game, "AssertPhaseHandSeizurePrograms");
            Require(!Unsettled().Any(), "A committed settlement retains the closed debt after its source disappears.");
            Pending(game).Add(settled with { ReturnFrameId = 502 });
            Reject(game, "AssertPhaseHandSeizurePrograms", "A later pending settlement cannot repeat a historical closed debt.");

            IEnumerable<PhaseHandSeizureIssuedEvent> Unsettled() =>
                (IEnumerable<PhaseHandSeizureIssuedEvent>)typeof(GameEngine).GetMethod("UnsettledPhaseHandSeizures", Flags)!.Invoke(game, [])!;
        }
    }

    private static void TypedSnapshotsReuseOnlyUnchangedFacts(GameEngine game)
    {
        // A host-only audit of the journal index, independent of gameplay grants.
        var read = typeof(GameEngine).GetMethod("ProgramEventHistory", Flags)!
            .MakeGenericMethod(typeof(TargetsConfirmedEvent)).CreateDelegate<Func<IReadOnlyList<TargetsConfirmedEvent>>>(game);
        var pending = Pending(game);
        var events = Field<List<EventEnvelope>>(game, "_events");
        var initial = read();
        Require(initial.Count == 0, "The independent typed history audit starts without target facts.");
        pending.Add(new TurnStartedEvent(1, 0));
        Require(ReferenceEquals(initial, read()), "Unrelated pending facts do not replace an empty typed snapshot.");
        CommitFacts(game);
        Require(ReferenceEquals(initial, read()), "Unrelated committed facts advance the journal cursor without replacing the typed snapshot.");
        var first = new TargetsConfirmedEvent(701, new List<int> { 1, 2 });
        var second = new TargetsConfirmedEvent(702, new List<int> { 3 });
        pending.Add(first);
        var one = read();
        Require(one.Count == 1 && ReferenceEquals(one[0], first) && initial.Count == 0 && !ReferenceEquals(one, initial),
            "A new typed pending fact appears immediately without changing an older snapshot.");
        events.Add(new EventEnvelope(new EventId(events.Count + 1), null, events.Count + 1, game.Revision,
            "invariant-history-index", new TurnStartedEvent(2, 1)));
        Require(ReferenceEquals(one, read()), "Unrelated committed appends do not replace a snapshot containing pending typed facts.");
        pending.Add(new TurnStartedEvent(2, 1));
        Require(ReferenceEquals(one, read()), "An unrelated pending append preserves the nonempty typed snapshot.");
        pending[1] = new TurnStartedEvent(3, 2);
        Require(ReferenceEquals(one, read()), "A same-count unrelated replacement preserves the exact typed fact tail.");
        var equalValue = first with { };
        pending[0] = equalValue;
        var replacedIdentity = read();
        Require(ReferenceEquals(replacedIdentity[0], equalValue) && !ReferenceEquals(replacedIdentity, one) && equalValue == first,
            "A same-count equal-value typed replacement still selects the actual new pending reference.");
        var replacement = first with { TargetSeats = new List<int> { 4 } };
        pending[0] = replacement;
        var replaced = read();
        Require(replaced.Count == 1 && ReferenceEquals(replaced[0], replacement) && ReferenceEquals(one[0], first) &&
                !ReferenceEquals(replaced, one), "A same-count typed replacement refreshes its identity and leaves the older snapshot unchanged.");
        pending.Add(second);
        var two = read();
        Require(two.Count == 2 && ReferenceEquals(two[0], replacement) && ReferenceEquals(two[1], second),
            "The typed pending tail preserves its original order across unrelated facts.");
        pending[0] = second; pending[2] = replacement;
        var reordered = read();
        Require(reordered.Count == 2 && ReferenceEquals(reordered[0], second) && ReferenceEquals(reordered[1], replacement) &&
                ReferenceEquals(two[0], replacement), "A same-count reorder refreshes the typed snapshot while preserving the previous order.");
        for (var index = 0; index < 32; index++) _ = read();
        var pendingBytes = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 128; index++) _ = read();
        Require(GC.GetAllocatedBytesForCurrentThread() == pendingBytes,
            "Warm reads compare a nonempty typed pending tail without allocating a snapshot or enumerator.");
        pending.Add(replacement);
        var duplicate = read();
        Require(duplicate.Count == 3 && ReferenceEquals(duplicate[1], replacement) && ReferenceEquals(duplicate[2], replacement),
            "Repeated issuance facts remain in history even when the same event reference appears twice.");
        pending.Clear();
        Require(read().Count == 0 && reordered.Count == 2, "Cancellation clears typed pending facts without altering already exposed snapshots.");
        pending.Add(first); pending.Add(new TurnStartedEvent(4, 3)); pending.Add(second);
        var beforeCommit = read();
        var frozenFirst = (TargetsConfirmedEvent)CommittedEventProjection.Freeze(first);
        var frozenSecond = (TargetsConfirmedEvent)CommittedEventProjection.Freeze(second);
        foreach (var payload in new IGameEvent[] { frozenFirst, new TurnStartedEvent(4, 3), frozenSecond })
            events.Add(new EventEnvelope(new EventId(events.Count + 1), null, events.Count + 1, game.Revision, "invariant-history-index", payload));
        pending.Clear();
        var committed = read();
        Require(committed.Count == 2 && ReferenceEquals(committed[0], frozenFirst) && ReferenceEquals(committed[1], frozenSecond) &&
                !ReferenceEquals(frozenFirst, first) && !ReferenceEquals(frozenSecond, second) &&
                ReferenceEquals(beforeCommit[0], first) && ReferenceEquals(beforeCommit[1], second),
            "Pending-to-committed transition uses the newly frozen committed identities exactly once, retaining the old pending snapshot.");
        ((List<int>)first.TargetSeats)[0] = 4;
        Require(committed[0].TargetSeats.SequenceEqual([1, 2]) && first.TargetSeats.SequenceEqual([4, 2]),
            "The indexed committed payload retains its frozen nested collection independently of the original pending producer.");
        var exposed = (IList<TargetsConfirmedEvent>)committed;
        var rejected = false;
        try { exposed[0] = second; }
        catch (NotSupportedException) { rejected = true; }
        Require(exposed.IsReadOnly && rejected, "The indexed typed snapshot remains read-only.");
        pending.Add(new TurnStartedEvent(5, 4));
        for (var index = 0; index < 32; index++) _ = read();
        pending.Add(new TurnStartedEvent(6, 0));
        var beforeRead = GC.GetAllocatedBytesForCurrentThread();
        var same = read();
        Require(GC.GetAllocatedBytesForCurrentThread() == beforeRead && ReferenceEquals(committed, same),
            "An unrelated pending append performs no typed-snapshot allocation.");
        // Ignore allocations of the deliberately appended fixture fact above.
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 128; index++) _ = read();
        Require(GC.GetAllocatedBytesForCurrentThread() == bytes, "Warm reads with an unchanged nonempty typed tail allocate no snapshot or enumerator.");
        CommitFacts(game);
        Require(ReferenceEquals(committed, read()), "Committing unrelated facts updates all cursors while retaining the frozen typed snapshot.");
        var third = new TargetsConfirmedEvent(703, new List<int> { 0 });
        pending.Add(third);
        Require(read().Count == 3 && ReferenceEquals(read()[2], third), "A later typed append remains visible after unrelated cursor advances.");
        pending.Clear();
        Require(read().Count == 2 && ReferenceEquals(read()[0], frozenFirst) && committed.Count == 2,
            "Cancelling a later typed pending fact retains all committed historical facts and earlier snapshots.");
    }

    private static T Field<T>(GameEngine game, string name) => (T)typeof(GameEngine).GetField(name, Flags)!.GetValue(game)!;
    private static List<IGameEvent> Pending(GameEngine game) => Field<List<IGameEvent>>(game, "_pendingEvents");
    private static void Assert(GameEngine game, string name) => typeof(GameEngine).GetMethod(name, Flags)!.Invoke(game, []);
    private static void Reject(GameEngine game, string name, string message)
    {
        try { Assert(game, name); }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { return; }
        throw new InvalidOperationException(message);
    }
    private static void CommitFacts(GameEngine game)
    {
        var events = Field<List<EventEnvelope>>(game, "_events");
        foreach (var fact in Pending(game))
            events.Add(new EventEnvelope(new EventId(events.Count + 1), null, events.Count + 1, game.Revision, "invariant-history-index", fact));
        Pending(game).Clear();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class HistoricalCapabilities : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("invariant-history-index", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var name in new[] { "OrdinaryDingFengContent", "OrdinaryYangHuContent", "OrdinaryZhangXingCaiContent", "OrdinarySpDiaoChanContent" })
                typeof(StandardClassicGeneralPackage).Assembly.GetType("CardGame.Content.Standard." + name)!
                    .GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [builder]);
        }
    }
}
