using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DyingSuitsAndEndingHistoryChecks
{
    private const string Chenqing = "ol:chenqing", Moshi = "ol:moshi", Driver = "fixture:dseh-driver";
    private const string Gain = "fixture:dseh-gain", Entry = "fixture:dseh-entry", Mode = "identity:classic-dying-suits-ending-history-fixture";

    public static void ChenqingOriginalDyingFourSuitsPrivateCostAndColdReplay()
    {
        var (g, registry) = Start(); Prepare(g, 2); EnterOriginal(g);
        var original = g.ResolutionStack.OfType<DyingFrame>().Single();
        Activate(g, Chenqing, "round-original-dying-suits"); Answer(g, c => c.Targets.SequenceEqual([2]));
        Reach(g, p => Action(p, "dying-suits") && p.PlayerSeat == 2);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Chenqing);
        var draw = Events<DyingSuitsDrawIssuedEvent>(g).Single();
        Require(draw.ActualCount == 4 && g.State.Players[1] is { IsAlive: true, Hp: 0 } &&
            g.ResolutionStack.OfType<DyingFrame>().Single(f => f.Id == original.Id) == original,
            "Four actual draw entities retain the living original Dying token and every original cursor.");
        Require(g.CreateSnapshot(0).PendingDecision is null && g.CreateSnapshot(1).PendingDecision is null &&
            g.CreateSnapshot(3).PendingDecision is null && P(g)!.PlayerSeat == 2,
            "Only the actual recipient receives its private legal HE discard selection.");
        Cold(g, registry); Reject(g);
        var own = g.CreateSnapshot(2).Players[2];
        var ids = own.Hand.Concat(own.Equipment).Where(c => P(g)!.ValidCardIds.Contains(c.Id)).GroupBy(c => c.Suit)
            .Where(group => group.Key != Suit.None).Select(group => group.First().Id).Take(4).ToArray();
        Require(ids.Length == 4, "The fixed actual deck and preparation must offer all four legal suits.");
        foreach (var id in ids) { Answer(g, c => c.Cards.SequenceEqual([id])); if (P(g) is not null) Cold(g, registry); }
        ReachPlay(g);
        var paid = Events<DyingSuitsDiscardPaidEvent>(g).Single(); var issued = Events<DyingSuitsPeachIssuedEvent>(g).Single().Return;
        Require(paid.CardIds.SequenceEqual(ids) && paid.Suits.Distinct().Count() == 4 &&
            ids.All(id => g.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.DiscardPile &&
                m.Sequence > paid.Before && m.Sequence <= paid.After) == 1) && issued.ActorSeat == 2 && issued.VictimSeat == 1 && issued.DyingFrameId == original.Id &&
            Events<CardUseDeclaredEvent>(g).Count(e => e.ResolutionId == issued.CardUseFrameId && e.CardId == 0 && e.CardKind == CardKind.Peach && e.SourceSeat == 2) == 1 &&
            Events<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == issued.CardUseFrameId) == 1 &&
            Events<DyingSuitsPeachReturnedEvent>(g).Count(e => e.Return == issued) == 1 && g.State.Players[1] is { IsAlive: true, Hp: 1 },
            "Exactly four actual discard costs issue and finish one zero-material real Peach for the original victim.");
        Cold(g, registry);
    }

    public static void ChenqingDuplicateSuitsAndNestedHpDyingRetainOriginalCursor()
    {
        var (duplicate, duplicateRegistry) = Start(duplicateSuits: true); Prepare(duplicate, 2); EnterOriginal(duplicate);
        Activate(duplicate, Chenqing, "round-original-dying-suits"); Answer(duplicate, c => c.Targets.SequenceEqual([2]));
        Reach(duplicate, p => Action(p, "dying-suits") && p.PlayerSeat == 2);
        var ids = P(duplicate)!.ValidCardIds.Take(4).ToArray(); foreach (var id in ids) Answer(duplicate, c => c.Cards.SequenceEqual([id]));
        Require(Events<DyingSuitsDiscardPaidEvent>(duplicate).Single().Suits.All(s => s == Suit.Club) &&
            Events<DyingSuitsPeachIssuedEvent>(duplicate).Length == 0 && duplicate.State.Players[1].Hp == 0,
            "A real duplicate-suit cost cannot manufacture a Peach or direct recovery."); Cold(duplicate, duplicateRegistry);

        foreach (var nestedOp in new[] { "loseHp", "damage" })
        {
            var (g, registry) = Start(nestedOp: nestedOp); Prepare(g, 2); EnterOriginal(g);
            var original = g.ResolutionStack.OfType<DyingFrame>().Single();
            Activate(g, Chenqing, "round-original-dying-suits"); Answer(g, c => c.Targets.SequenceEqual([2]));
            Reach(g, p => p.SkillPrompt?.SkillId == Gain); Cold(g, registry); AssertDyingMovementOwnerBoundary(g, registry);
            Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            Reach(g, p => p.SkillPrompt?.SkillId == Entry && p.PlayerSeat == 2);
            var nested = g.ResolutionStack.OfType<DyingFrame>().Single(f => f.Id != original.Id);
            Require(nested.VictimSeat == 2 && g.State.Players[1].Hp == 0 && g.State.Players[2].Hp == 0 &&
                g.ResolutionStack.OfType<DyingFrame>().Single(f => f.Id == original.Id) == original &&
                g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Chenqing).DyingSuits!.DyingFrameId == original.Id,
                "A real paid gain child creates its own HP/Damage Dying while the original live token and response cursor remain exact.");
            Cold(g, registry); Reject(g); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            Reach(g, p => Action(p, "dying-suits") && p.PlayerSeat == 2);
            Require(g.State.Players[2].Hp == 3 && Events<DyingSuitsDrawIssuedEvent>(g).Length == 1 &&
                g.ResolutionStack.OfType<DyingFrame>().Single(f => f.Id == original.Id) == original,
                "The nested native entry/recovery returns to the same once-paid Draw and original Dying."); Cold(g, registry);
        }
    }

    public static void MoshiFirstUseCompletionBeforeSecondMultiSlashAndSkip()
    {
        var (g, registry) = Start(ending: true); Prepare(g, 0);
        Play(g, LegalActionKind.DrawTwo); Play(g, LegalActionKind.Slash, [1]);
        UseDriver(g, "multi", []); ReachPlay(g);
        var history = Events<OwnPlayEligibleUseRecordedEvent>(g);
        Require(history is [{ EffectiveKind: CardKind.DrawTwo }, { EffectiveKind: CardKind.Slash }],
            "Only the original first two real own-Play Basic/InstantTrick Uses enter the issued history.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Reach(g, p => Activation(p, Moshi, "own-ending-first-two-uses"));
        Activate(g, Moshi, "own-ending-first-two-uses"); Reach(g, p => Action(p, "historical-ending")); Cold(g, registry); Reject(g);
        Answer(g, c => c.Cards.Count == 1);
        Reach(g, p => Action(p, "historical-ending"));
        var first = Events<EndingHistoricalUseIssuedEvent>(g).Single().Return;
        Require(first.SlotIndex == 0 && first.EffectiveKind == CardKind.DrawTwo && Events<EndingHistoricalUseReturnedEvent>(g).Count(e => e.Return == first) == 1 &&
            Events<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == first.CardUseFrameId) == 1,
            "The targetless first Use must finish its whole native producer before the second choice is published."); Cold(g, registry);
        var choice = P(g)!.Choices.First(c => c.Cards.Count == 1 && c.Targets.Count == 2); var targets = choice.Targets.ToArray();
        var beforeHp = targets.ToDictionary(s => s, s => g.State.Players[s].Hp); Answer(g, c => c.Id == choice.Id);
        DrainHistorical(g, expectedCount: 2);
        var second = Events<EndingHistoricalUseIssuedEvent>(g).Last().Return;
        Require(second.SlotIndex == 1 && second.EffectiveKind == CardKind.Slash && targets.All(s => g.State.Players[s].Hp == beforeHp[s] - 1) &&
            Events<EndingHistoricalUseReturnedEvent>(g).Count(e => e.Return == second) == 1 &&
            Events<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == second.CardUseFrameId) == 1 &&
            Events<CardUseDebitRecordedEvent>(g).All(e => e.Debit.CardActionId != second.CardActionId),
            "The exact ordered multi-target Slash damages both targets and returns once without an Ending debit of finite Play quota."); Cold(g, registry);

        var (skip, skipRegistry) = Start(ending: true); Prepare(skip, 0); Play(skip, LegalActionKind.DrawTwo); Play(skip, LegalActionKind.Slash, [1]);
        Accept(skip, new EndPlayPhaseCommand(0, skip.Revision, P(skip)!.PromptId)); Reach(skip, p => Activation(p, Moshi, "own-ending-first-two-uses"));
        Activate(skip, Moshi, "own-ending-first-two-uses"); Reach(skip, p => Action(p, "historical-ending"));
        Answer(skip, c => c.Parameters.GetValueOrDefault("option") == "skip");
        Require(Events<EndingHistoricalUseIssuedEvent>(skip).Length == 0 && skip.ResolutionStack.OfType<ProgramSkillFrame>().All(f => f.SkillId != Moshi),
            "Skipping the first slot terminates this activation and never pays or publishes the second slot."); Cold(skip, skipRegistry);
    }

    public static void DyingSuitsCollectionsAndCompositionContracts()
    {
        var ids = new[] { 1, 2, 3, 4 }; var suits = new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond };
        var paid = new DyingSuitsDiscardPaidEvent(10, 2, 20, 24, ids, suits); ids[0] = 999; suits[0] = Suit.None;
        Require(paid.CardIds[0] == 1 && paid.Suits[0] == Suit.Spade, "Constructors clone actual cost collections.");
        var initialized = paid with { CardIds = ids, Suits = suits }; ids[1] = 999; suits[1] = Suit.None;
        Require(initialized.CardIds[1] == 2 && initialized.Suits[1] == Suit.Heart, "Explicit init cloning also freezes with assignments.");
        var restored = JsonSerializer.Deserialize<DyingSuitsDiscardPaidEvent>(JsonSerializer.Serialize(paid))!;
        Require(restored.CardIds.SequenceEqual(paid.CardIds) && restored.Suits.SequenceEqual(paid.Suits) &&
            restored.CardIds is IList<int> { IsReadOnly: true } && restored.Suits is IList<Suit> { IsReadOnly: true }, "JSON round-trips remain read-only.");
        var paidDiscardReason = new CardMoveReason($"skill-program.{Chenqing}.{SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach}.discard");
        Require(GameEngine.IsDiscardMovementReason(paidDiscardReason), "Chenqing's real hand/equipment payment remains a discard for movement-discard observers.");
        foreach (var excluded in new[] { $"skill-program.{Chenqing}.{SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach}.draw",
                     "skill-program.fixture:other.Unknown.discard", "card.use-finished.discard", "card.recast.test" })
            Require(!GameEngine.IsDiscardMovementReason(new(excluded)), "Draw, unknown skill-program, use cleanup and recast must not acquire paid-discard provenance.");
        var bad = new[]
        {
            "\"window\":\"dyingEntering\",\"subject\":\"owner\",\"optional\":true,\"usageScope\":\"round\",\"usageLimit\":1,\"effects\":[{\"op\":\"drawThenDiscardSuitsForDyingPeach\",\"target\":\"owner\"}]",
            "\"window\":\"dyingEntering\",\"subject\":\"any\",\"optional\":true,\"effects\":[{\"op\":\"drawThenDiscardSuitsForDyingPeach\",\"target\":\"owner\"}]",
            "\"window\":\"turnEnding\",\"subject\":\"owner\",\"turnOwnerScope\":\"otherLiving\",\"optional\":true,\"effects\":[{\"op\":\"useOwnPlayHistoryAtEnding\",\"target\":\"owner\"}]"
        };
        foreach (var trigger in bad)
        {
            var rejected = false;
            try { _ = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:invalid","revision":1,"triggers":[{"id":"bad",{{trigger}}}]}]}""",
                """{"schemaVersion":3,"skills":{"fixture:invalid":{"name":"非法组合","description":"窗口和轮次必须精确"}}}"""); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Original-Dying and own-Ending operations reject an incompatible subject, scope or missing round quota.");
        }
    }

    public static void MoshiFireAttackDamageCompletesOneHistoricalReturn()
    {
        var (g, registry) = Start(ending: true, scenario: "fire"); Prepare(g, 0); Play(g, LegalActionKind.FireAttack, [1]);
        Require(Events<DamageAppliedEvent>(g).Any(e => e.TargetSeat == 1 && e.Nature == DamageNature.Fire), "The original issued history is a real damaging FireAttack.");
        BeginEnding(g); var hp = g.State.Players[1].Hp; Answer(g, c => c.Cards.Count == 1 && c.Targets.SequenceEqual([1]));
        DrainHistorical(g);
        var ret = Events<EndingHistoricalUseIssuedEvent>(g).Single().Return;
        Require(ret.EffectiveKind == CardKind.FireAttack && g.State.Players[1].Hp == hp - 1 &&
            Events<EndingHistoricalUseReturnedEvent>(g).Count(e => e.Return == ret) == 1 &&
            Events<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == ret.CardUseFrameId) == 1,
            "A real FireAttack attack tail owns exactly one historical return after pop; no second attack-completion return is attempted."); Cold(g, registry);
    }

    public static void MoshiMultiDuelLastNullifiedTargetReturnsAfterPriorAttack()
    {
        var (g, registry) = Start(ending: true, scenario: "duel"); Prepare(g, 0); Play(g, LegalActionKind.Duel, [1]);
        var cost = g.CreateSnapshot(0).Players[0].Hand.First().Id;
        Accept(g, new UseProgramSkillCommand(0, "boundary:qiaoshui-current", "contest", [cost], [1], g.Revision, P(g)!.PromptId)); ReachPlay(g);
        Require(Events<NextActualUseTargetAdjustmentGrantedEvent>(g).Length == 1, "The real paid Pindian producer grants the historical Duel's additional native target.");
        BeginEnding(g);
        var hp1 = g.State.Players[1].Hp; var hp2 = g.State.Players[2].Hp;
        Answer(g, c => c.Cards.Count == 1 && c.Targets.SequenceEqual([1, 2]));
        for (var step = 0; step < 140; step++)
        {
            if (P(g) is { Kind: DecisionKind.Nullification, PlayerSeat: 0 } &&
                g.ResolutionStack.OfType<NullificationWindowFrame>().Last().TargetSeats.SequenceEqual([2])) break;
            Advance(g);
        }
        Require(g.State.Players[1].Hp == hp1 - 1 && g.State.Players[2].Hp == hp2 &&
            P(g) is { Kind: DecisionKind.Nullification, PlayerSeat: 0 }, "The first real Duel attack finishes before the final target's counterspell.");
        var liveReturn = Events<EndingHistoricalUseIssuedEvent>(g).Single().Return;
        Require(Events<EndingHistoricalUseReturnedEvent>(g).Length == 0 &&
            g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == liveReturn.CardUseFrameId) &&
            g.CardMovements.Last(m => m.CardId == liveReturn.PhysicalCardId).To == CardLocation.Processing,
            "The final target's Nullification pauses the live whole-Use owner with its paid material still Processing.");
        Cold(g, registry); AssertHistoricalOwnerBoundary(g, registry, liveReturn);
        Answer(g, c => c.Parameters.GetValueOrDefault("response") == "nullification"); DrainHistorical(g);
        var ret = Events<EndingHistoricalUseIssuedEvent>(g).Single().Return;
        Require(g.State.Players[2].Hp == hp2 && Events<EndingHistoricalUseReturnedEvent>(g).Count(e => e.Return == ret) == 1 &&
            Events<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == ret.CardUseFrameId) == 1,
            "A retained inactive first-target CardAttack does not suppress the final nullified whole-Duel return."); Cold(g, registry);
    }

    public static void MoshiLastHandFangtianUsesRealOrderedOwner()
    {
        var (g, registry) = Start(ending: true, scenario: "fangtian"); Play(g, LegalActionKind.Equip); Play(g, LegalActionKind.Slash, [1]);
        var discard = g.CreateSnapshot(0).Players[0].Hand.First().Id;
        Accept(g, new UseProgramSkillCommand(0, Driver, "trim", [discard], [], g.Revision, P(g)!.PromptId)); ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[0] is { HandCount: 1 } && Events<OwnPlayEligibleUseRecordedEvent>(g).Length == 1,
            "The actual equipment and discard leave one real Hand material without adding a history Use."); BeginEnding(g);
        var choice = P(g)!.Choices.First(c => c.Cards.Count == 1 && c.Targets.Count == 3); var hp = choice.Targets.ToDictionary(s => s, s => g.State.Players[s].Hp);
        Answer(g, c => c.Id == choice.Id); DrainHistorical(g);
        var ret = Events<EndingHistoricalUseIssuedEvent>(g).Single().Return;
        Require(Events<FangtianHalberdUsedEvent>(g).Count(e => e.ResolutionId == ret.CardUseFrameId) == 1 &&
            Events<FangtianHalberdUsedEvent>(g).Single(e => e.ResolutionId == ret.CardUseFrameId).TargetSeats.SequenceEqual(choice.Targets) &&
            choice.Targets.All(s => g.State.Players[s].Hp == hp[s] - 1) && Events<EndingHistoricalUseReturnedEvent>(g).Count(e => e.Return == ret) == 1,
            "Last-hand Fangtian owns all three ordered targets and its actual equipment fact before the single whole-Use return."); Cold(g, registry);
    }

    public static void MoshiBasicConsumesActualNextUseGrantAtEnding()
    {
        var (g, registry) = Start(ending: true, scenario: "next"); Play(g, LegalActionKind.Slash, [1]);
        var cost = g.CreateSnapshot(0).Players[0].Hand.First().Id;
        Accept(g, new UseProgramSkillCommand(0, "boundary:qiaoshui-current", "contest", [cost], [1], g.Revision, P(g)!.PromptId)); ReachPlay(g);
        var grant = Events<NextActualUseTargetAdjustmentGrantedEvent>(g).Single(); BeginEnding(g);
        var choice = P(g)!.Choices.First(c => c.Cards.Count == 1 && c.Targets.SequenceEqual([1, 3])); var hp = choice.Targets.ToDictionary(s => s, s => g.State.Players[s].Hp);
        Cold(g, registry); Answer(g, c => c.Id == choice.Id); DrainHistorical(g);
        var ret = Events<EndingHistoricalUseIssuedEvent>(g).Single().Return; var consumed = Events<NextActualUseTargetAdjustmentConsumedEvent>(g).Single();
        Require(consumed.GrantProgramFrameId == grant.ProgramFrameId && consumed.CardActionId == ret.CardActionId && consumed.EffectiveKind == CardKind.Slash &&
            choice.Targets.All(s => g.State.Players[s].Hp == hp[s] - 1) && Events<EndingHistoricalUseReturnedEvent>(g).Count(e => e.Return == ret) == 1 &&
            Events<CardUseDebitRecordedEvent>(g).All(e => e.Debit.CardActionId != ret.CardActionId),
            "The real paid Pindian grant survives to Ending, is consumed by one genuine Basic Use, and owns both targets without a Play debit."); Cold(g, registry);
    }

    private static void BeginEnding(GameEngine g)
    { Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Reach(g, p => Activation(p, Moshi, "own-ending-first-two-uses")); Activate(g, Moshi, "own-ending-first-two-uses"); Reach(g, p => Action(p, "historical-ending")); }
    private static void DrainHistorical(GameEngine g, int expectedCount = 1)
    { for (var step = 0; step < 160; step++) { if (Events<EndingHistoricalUseReturnedEvent>(g).Length >= expectedCount) return; Advance(g); } throw new InvalidOperationException("Exact historical whole-Use did not return."); }

    private static void AssertDyingMovementOwnerBoundary(GameEngine g, ContentRegistry registry)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var assertion = typeof(GameEngine).GetMethod("AssertDyingSuitsSubtree", flags)!;
        Require(assertion.Invoke(g, null) is true, "The real awaited draw's suspended original owner remains valid while its native gain child is active.");
        // Corrupt a journal-restored diagnostic clone only; the accepted command game remains intact.
        var invalid = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        var stack = (FrameStore)typeof(GameEngine).GetField("_resolutionStack", flags)!.GetValue(invalid)!;
        var root = stack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Chenqing);
        var movement = (CardsMovedTriggerWindowFrame)stack[stack.FindIndex(f => f.Id == root.Id) + 1];
        stack.Replace(movement with { Batch = movement.Batch with { AwaitingProgramFrameId = long.MaxValue } });
        var rejected = false;
        try { assertion.Invoke(invalid, null); }
        catch (System.Reflection.TargetInvocationException exception) when
            (exception.InnerException is InvalidOperationException { Message: "Original Dying suits lost its exact paid subtree and original entry token." })
        { rejected = true; }
        Require(rejected, "An awaited movement cannot substitute another program owner for the original Dying receipt.");
    }

    private static void AssertHistoricalOwnerBoundary(GameEngine g, ContentRegistry registry, EndingHistoricalUseReturn receipt)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var assertion = typeof(GameEngine).GetMethod("AssertHistoricalEndingUse", flags)!;
        var actual = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == receipt.CardUseFrameId);
        assertion.Invoke(g, [actual]);
        foreach (var boundary in new[] { "missing", "other-same-seat", "missing-receipt" })
        {
            // Each host invariant audit starts from the exact real cold prefix and never enters its command history.
            var invalid = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
            var stack = (FrameStore)typeof(GameEngine).GetField("_resolutionStack", flags)!.GetValue(invalid)!;
            var use = stack.OfType<CardUseFrame>().Single(f => f.Id == receipt.CardUseFrameId);
            var parent = stack.OfType<ProgramSkillFrame>().Single(f => f.Id == receipt.ProgramFrameId);
            if (boundary == "missing-receipt") stack.Replace(parent with { EndingHistoricalUses = null });
            else
            {
                if (boundary == "other-same-seat") stack.Push(parent with { Id = long.MaxValue });
                use = use with { EndingHistoricalUseReturn = receipt with { ProgramFrameId = long.MaxValue } };
            }
            var rejected = false;
            try { assertion.Invoke(invalid, [use]); }
            catch (System.Reflection.TargetInvocationException exception) when
                (exception.InnerException is InvalidOperationException { Message: "Historical Ending lost its exact suspended program owner." })
            { rejected = true; }
            Require(rejected, "A whole historical Use rejects a missing owner, an unrelated same-seat program or an owner without its exact receipt.");
        }
    }

    private static (GameEngine, ContentRegistry) Start(bool duplicateSuits = false, string? nestedOp = null, bool ending = false, string? scenario = null)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(duplicateSuits, nestedOp, ending, scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 5 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:dseh-owner", g.Revision, P(g)!.PromptId)); ReachPlay(g); return (g, registry);
    }
    private static void Prepare(GameEngine g, int seat) { UseDriver(g, "prepare", [seat]); ReachPlay(g); }
    private static void EnterOriginal(GameEngine g) { UseDriver(g, "dying", [1]); Reach(g, p => Activation(p, Chenqing, "round-original-dying-suits")); }
    private static void UseDriver(GameEngine g, string activation, IReadOnlyList<int> targets) => Accept(g,
        new UseProgramSkillCommand(0, Driver, activation, [], targets, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g, LegalActionKind kind, IReadOnlyList<int>? targets = null)
    { var a = g.GetHumanLegalActions().First(a => a.Kind == kind && (targets is null || a.TargetSeats.SequenceEqual(targets)));
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind)); ReachPlay(g); }
    private static T[] Events<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string name) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == name);
    private static bool Activation(PendingDecision p, string skill, string binding) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static void Activate(GameEngine g, string skill, string binding) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void ReachPlay(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    { for (var step = 0; step < 240; step++) { var p = P(g); if (p is not null && predicate(p)) return; Advance(g); } throw new InvalidOperationException("Fixed real boundary not reached: " + JsonSerializer.Serialize(P(g))); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { Kind: DecisionKind.Nullification } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.FireAttackDiscard }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "fire-attack-discard");
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished command leaves all actual frames, invoices and private projections unchanged."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "Exact frame-owned costs, original cursors, issued events and all four private projections cold-restore identically.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool duplicateSuits, string? nestedOp, bool ending, string? scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-dying-suits-ending-history", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var nested = nestedOp is null ? "" : $$""",{"id":"{{Gain}}","revision":1,"triggers":[{"id":"actual-draw","window":"cardsGained","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ol:chenqing.DrawThenDiscardSuitsForDyingPeach.draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["{{Entry}}"]},{"op":"{{nestedOp}}","target":"owner","amount":1}]}]},{"id":"{{Entry}}","revision":1,"triggers":[{"id":"actual-entry","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"entry-seen","options":[{"id":"continue"}]},{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":3,"clampToMaxHp":true}]}]}""";
            var rules = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"activations":[{"id":"prepare","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":20}]},{"id":"dying","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},{"id":"multi","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"cardTargetCount","ruleOperation":"add","amount":1,"cardKinds":["slash"]},{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"attackRange","ruleOperation":"unlimited"}]}]},{"id":"fixture:dseh-quiet","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]}{{nested}}]}""";
            var names = new Dictionary<string, object> { [Driver] = new { name = "真实准备与濒死", description = "固定种子的实体付款夹具" }, ["fixture:dseh-quiet"] = new { name = "安静其他回合", description = "有限杀额度" } };
            if (nestedOp is not null) { names[Gain] = Observer("实际摸牌中濒死"); names[Entry] = Observer("嵌套原生濒死入口"); }
            var tree = JsonNode.Parse(rules)!;
            tree["skills"]![0]!["activations"]!.AsArray().Add(JsonNode.Parse("""{"id":"trim","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]}"""));
            var catalog = SkillProgramCatalog.Load(tree.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = 3, skills = names }));
            foreach (var pair in catalog.Programs) b.AddSkill(new(pair.Key, pair.Key, "真实共享能力夹具") { Program = pair.Value });
            foreach (var owner in new[] { false, true }) b.AddSkill(new(owner ? "fixture:dseh-owner-pick" : "fixture:dseh-peer-pick", "固定选将", "公开选将评分")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == Role.Lord) == owner ? 10000d : -10000d) });
            b.AddGeneral(new("fixture:dseh-owner", "原始濒死与历史用牌", "supporter", "fixture:dseh-owner-pick", "wei", 6,
                scenario is "next" or "duel" ? [Chenqing, Moshi, Driver, "boundary:qiaoshui-current", "classic:tianbian"] : [Chenqing, Moshi, Driver]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:dseh-peer-{i}", "真实其他角色", "supporter", "fixture:dseh-peer-pick", "wei", 8,
                nestedOp is null ? ["fixture:dseh-quiet"] : ["fixture:dseh-quiet", Gain]) { InitialHp = ending ? 8 : 1 });
            // Dying needs one real HP loss from HP1 in every variant.
            b.AddDeck(new("fixture:dseh-deck", "固定实体的四花色", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 96)
                .Select(i => new ContentDeckPhysicalCard(CardIdentity(i), scenario is not null ? Suit.Heart : duplicateSuits ? Suit.Club : (Suit)(i % 4), scenario is not null ? 7 : i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "原始濒死与结束历史", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:dseh-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:dseh-owner", "fixture:dseh-peer-1", "fixture:dseh-peer-2", "fixture:dseh-peer-3"]));
        }
        private string CardIdentity(int index)
        {
            // Seed31 consumes two role and three general shuffle draws before
            // CompleteSetup's 96-card shuffle. This fixed source-derived map is
            // recorded in fixture-layout.json; DrawPerTurn is zero.
            if (scenario is null) return index % 2 == 0 ? "standard:slash" : "standard:draw_two";
            var own = Array.IndexOf(OwnerInitialRecipeIndices, index);
            if (own < 0) return InitialRecipeIndices.Contains(index) ? "standard:draw_two" :
                scenario == "duel" ? "standard:nullification" : "standard:slash";
            return scenario switch
            {
                "fire" => own == 0 ? "standard:fire_attack" : "standard:slash",
                "duel" => own == 0 ? "standard:duel" : own == 1 ? "standard:nullification" : "standard:draw_two",
                "fangtian" => own == 0 ? "classic:fangtian-halberd" : "standard:slash",
                _ => "standard:slash"
            };
        }
        private static readonly int[] InitialRecipeIndices = [60, 23, 57, 71, 79, 39, 48, 85, 94, 73, 84, 65, 30, 88, 9, 12];
        private static readonly int[] OwnerInitialRecipeIndices = [60, 79, 94, 30];
        private static object Observer(string name) => new { name, description = "真实事件暂停与原始帧返回", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
