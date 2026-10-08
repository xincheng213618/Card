using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryYangHuChecks
{
    private const string Huaiyuan = "ol:huaiyuan", Chongxin = "ol:chongxin", Dezhang = "ol:dezhang", Weishu = "ol:weishu";
    private const string Driver = "fixture:yang-hu-driver", Gain = "fixture:yang-hu-gain", Health = "fixture:yang-hu-health";
    private const string DiscardChild = "fixture:yang-hu-discard-child", Suppress = "fixture:yang-hu-suppress", Mode = "fixture:yang-hu-mode";
    private static readonly Lazy<ContentRegistry> Official = new(StandardContentRegistry.CreateWithClassicGenerals);
    private enum Scenario { Original, Nested, Awake, Death, Draw, EmptyDraw, Discard }

    public static void OriginalInitialEntitiesDepartOnceAndChongxinReallyRecasts()
    {
        var (g, registry) = Create();
        var initial = V(g, 0).Hand.Select(c => c.Id).ToArray();
        Require(E<OriginalHandEntitiesInitializedEvent>(g) is [{ OriginalCount: 4, RemainingCount: 4, StateId: "sui" }] &&
            !E<OriginalHandAwakeningMaximumPaidEvent>(g).Any(), "Only the four actual initial-deal entities initialize Sui; the first real turn does not awaken while they remain.");
        Tags(g, initial); g = Cold(g, registry);
        Use(g, "equip", [0], [initial[0]]);
        Benefit(g, 0, "hand-limit"); Play(g);
        Require(V(g, 0).Equipment.Select(c => c.Id).SequenceEqual([initial[0]]) &&
            E<OriginalHandEntityConsumedEvent>(g) is [{ RemainingCount: 3 }] &&
            E<OriginalHandBenefitStartedEvent>(g).Single().MovementSequence == g.CardMovements.Single(m => m.CardId == initial[0] &&
                m.From == CardLocation.Hand(0) && m.To == CardLocation.Equipment(0)).Sequence,
            "Real own-Hand to own-Equipment movement consumes that physical Sui once and awards its mandatory benefit.");
        Tags(g, initial.Skip(1).ToArray());
        Use(g, "return-equipment"); Reach(g, p => p.Choices.Any(c => c.Cards.SequenceEqual([initial[0]])));
        Answer(g, c => c.Cards.SequenceEqual([initial[0]])); Play(g);
        Require(V(g, 0).Hand.Any(c => c.Id == initial[0]), "The same real installed entity returns to its original owner's Hand through a native move.");
        Tags(g, initial.Skip(1).ToArray());
        Accept(g, new PlayCardCommand(0, initial[0], [], g.Revision, P(g)!.PromptId)); Play(g);
        Require(E<OriginalHandEntityConsumedEvent>(g).Length == 1 && E<OriginalHandBenefitStartedEvent>(g).Length == 1,
            "Returning and losing the same physical card cannot restore its initial mark or award a second benefit.");

        var partner = V(g, 1).Hand[0].Id;
        Accept(g, new UseProgramSkillCommand(0, Chongxin, "paired-hand-recast", [initial[1]], [1], g.Revision, P(g)!.PromptId));
        Reach(g, p => p.PlayerSeat == 1 && Has(p, "paired-hand-recast")); Private(g);
        Answer(g, c => c.Cards.SequenceEqual([partner])); Benefit(g, 0, "hand-limit"); Play(g);
        var pair = E<PairedHandRecastStartedEvent>(g).Single();
        Require(E<PairedHandRecastCompletedEvent>(g).Single(e => e.FrameId == pair.FrameId) is
                { OwnerPaid: true, PartnerPaid: true, OwnerDrawCount: 1, PartnerDrawCount: 1 } &&
            g.CardMovements.Count(m => m.CardId == initial[1] && m.Reason == CardMoveReasons.RecastDiscard) == 1 &&
            E<OriginalHandPermanentBonusGrantedEvent>(g).Count(e => e.RecipientSeat == 0 && e.Kind == OriginalHandBenefitKind.HandLimit) == 2 &&
            !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Chongxin),
            "Formal Chongxin pays two real Hand recasts and draws once per payer; its owner's original entity independently awards the same repeatable hand-limit option and the real phase quota remains spent.");
        Tags(g, initial.Skip(2).ToArray());
        Use(g, "discard-two", cards: initial.Skip(2).ToArray());
        Benefit(g, 1, "attack-range"); Benefit(g, 1, "attack-range"); Play(g);
        var last = E<OriginalHandBenefitStartedEvent>(g).TakeLast(2).ToArray();
        Require(last.Length == 2 && last[0].BatchId == last[1].BatchId && last[0].MovementSequence != last[1].MovementSequence &&
            E<OriginalHandEntityConsumedEvent>(g).Select(e => e.RemainingCount).SequenceEqual([3, 2, 1, 0]) &&
            g.GetAttackRange(1) == 3 && E<OriginalHandBenefitCompletedEvent>(g).Length == 4,
            "One true two-card discard batch consumes each distinct original entity and grants two separately completed, repeatable range increases.");
        Tags(g, []); _ = Cold(g, registry);
    }

    public static void IssuedBenefitDrawSurvivesNestedQualificationLoss()
    {
        var (g, registry) = Create(Scenario.Nested);
        var material = V(g, 0).Hand[0].Id;
        Use(g, "give", [1], [material]); Benefit(g, 0, "draw");
        Reach(g, p => p.SkillPrompt?.SkillId == Gain && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OriginalHandBenefit is not null);
        var receipt = root.OriginalHandBenefit!; var id = root.Id;
        var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == id);
        Require(receipt is { Stage: OriginalHandBenefitStage.DrawChildren, DrawIssued: true, ActualDrawCount: 1, TargetSeat: 0 } &&
            root.PendingMovementContinuation?.SubjectSeat == 0 && movement.Batch.AwaitingProgramFrameId == id &&
            movement.Batch.Movements is [var move] && move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(0) &&
            move.Sequence > receipt.DrawBefore && move.Sequence <= receipt.DrawAfter &&
            !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Gain) && !E<OriginalHandBenefitCompletedEvent>(g).Any(),
            "An already-issued real Draw1 owns its exact awaiting movement child while the optional native gain candidate pauses before binding.");
        Private(g); g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(g, p => p.SkillPrompt?.SkillId == Gain && IsContinue(p));
        Private(g); Continue(g); Play(g);
        Require(V(g, 0).Hp == 5 && E<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Gain && e.SkillIds.Contains(Suppress)) == 1 &&
            V(g, 0).Skills!.Any(s => s.Id == Suppress) && !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Driver) &&
            E<OriginalHandBenefitDrawIssuedEvent>(g).Single() is { ActualCount: 1, TargetSeat: 0 } draw && draw.FrameId == id &&
            E<OriginalHandBenefitCompletedEvent>(g).Single() is { DrawIssued: true, ActualDrawCount: 1 } done && done.FrameId == id &&
            E<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == id && e.Completed) == 1 &&
            g.CardMovements.Count(m => m.Sequence > receipt.DrawBefore && m.Sequence <= receipt.DrawAfter && m.To == CardLocation.Hand(0)) == 1,
            "The real nested acquisition suppresses live qualification, but its original issued draw and typed return complete once without a repeated payment, lost card or second benefit.");
        _ = Cold(g, registry);
    }

    public static void EmptyOriginalHandAwakensAtRealTurnStartOnce()
    {
        var (g, registry) = Create(Scenario.Awake);
        var initial = V(g, 0).Hand.Select(c => c.Id).ToArray();
        Use(g, "give-all", [1], initial);
        for (var n = 0; n < 4; n++) Benefit(g, 1, "hand-limit");
        Play(g); Tags(g, []);
        Require(!E<OriginalHandAwakeningMaximumPaidEvent>(g).Any(), "Losing the final original entity during Play does not awaken at that movement boundary.");
        var turn = g.State.TurnNumber; Use(g, "extra"); Play(g);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, p => p.SkillPrompt?.SkillId == Health && IsContinue(p));
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OriginalHandAwakening is not null);
        var r = root.OriginalHandAwakening!; var id = root.Id;
        var health = g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single(w => w.ResumeFrameId == id);
        Require(g.State.TurnNumber > turn && r.Stage == OriginalHandAwakeningStage.MaximumPaid && r.SourceSkillId == Huaiyuan &&
            r.StateId == "sui" && r.OriginalCount == 4 && r.MaximumBefore == 5 && r.MaximumAfter == 4 && r.HpAfter == 4 &&
            r.SkillIds.SequenceEqual([Weishu]) && root.WindowContext?.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
            health.Change is { Kind: HpChangeKind.MaximumHp, TargetSeat: 0, Amount: 1 } && health.Change.ParentFrameId == id &&
            health.Continuation == PostEventContinuation.Program && !E<OriginalHandAwakeningGrantIssuedEvent>(g).Any(),
            "The next real turn start pays exactly one max HP and preserves its genuine MaximumHp child before granting formal Weishu.");
        Private(g); g = Cold(g, registry); Continue(g); Play(g);
        Require(E<OriginalHandAwakeningMaximumPaidEvent>(g).Single().FrameId == id &&
            E<OriginalHandAwakeningGrantIssuedEvent>(g).Single() == new OriginalHandAwakeningGrantIssuedEvent(id, 0, Dezhang, Weishu) &&
            E<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Dezhang && e.SkillIds.SequenceEqual([Weishu])) == 1 &&
            V(g, 0).Skills!.Any(s => s.Id == Weishu) && V(g, 0).MaxHp == 4 &&
            E<OriginalHandAwakeningCompletedEvent>(g).Single() is { GrantsIssued: true, OwnerAlive: true },
            "Cold native health return grants the actual formal locked skill and completes the paid game-once awakening once.");
        Use(g, "extra"); Play(g); var after = g.State.TurnNumber;
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && g.State.TurnNumber > after);
        Require(E<OriginalHandAwakeningMaximumPaidEvent>(g).Length == 1 && E<OriginalHandAwakeningGrantIssuedEvent>(g).Length == 1 &&
            V(g, 0).MaxHp == 4, "A later actual turn cannot repay maximum HP, awaken or regrant the game-once skill.");
    }

    public static void DeathInheritanceTransfersOnlyOwnReceivedPermanentBonuses()
    {
        var (g, registry) = Create(Scenario.Death);
        var roles = Enumerable.Range(0, 4).Select(s => V(g, s)).ToArray();
        Require(roles[0].Role == Role.Renegade && roles.All(p => p.IsAlive) &&
            roles.Count(p => p.Seat != 0 && p.Role == Role.Lord && p.IsAlive) == 1 &&
            roles.Count(p => p.Seat != 0 && p.Role == Role.Renegade && p.IsAlive) == 2,
            "Real private self-views prove the dying owner is a non-Lord and that one real Lord plus two enemy survivors keep this death from determining a winner.");
        var initial = V(g, 0).Hand.Select(c => c.Id).ToArray();
        var choices = new[] { (Seat: 0, Option: "hand-limit"), (Seat: 0, Option: "attack-range"), (Seat: 1, Option: "hand-limit"), (Seat: 1, Option: "draw") };
        for (var i = 0; i < initial.Length; i++) { Use(g, "give", [1], [initial[i]]); Benefit(g, choices[i].Seat, choices[i].Option); Play(g); }
        Require(V(g, 0).Hp == 4 && g.GetAttackRange(0) == 2 && E<OriginalHandPermanentBonusGrantedEvent>(g).Length == 3,
            "The real non-Lord owner has personally received exactly two permanent increases, while a different recipient retains one permanent increase and one non-inheritable draw.");
        Use(g, "die");
        ReachDeathInheritanceOffer(g);
        Require(!V(g, 0).IsAlive && g.CreateSnapshot(0).Status != EngineStatus.Completed,
            "Native HP-loss, dying and death finish for the non-Lord owner while the surviving real Lord keeps inheritance actionable.");
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(g, p => Entity(p, "inheritance"));
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OriginalHandInheritance is not null);
        var r = root.OriginalHandInheritance!; var id = root.Id;
        Require(r.Bonuses.Count == 2 && r.Bonuses.All(b => b.RecipientSeat == 0 && b.Kind != OriginalHandBenefitKind.Draw) &&
            r.CandidateSeats.All(s => s != 0 && V(g, s).IsAlive) && E<OriginalHandInheritanceStartedEvent>(g).Single().BonusCount == 2,
            "The optional accepted death receipt freezes only bonuses currently received by the dead owner, rather than all bonuses it awarded to other roles.");
        Frozen(r.Bonuses); Frozen(r.CandidateSeats); g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([2]));
        var transferred = E<OriginalHandBonusTransferredEvent>(g);
        Require(transferred.Length == 2 && transferred.All(e => e.FrameId == id && e.PreviousRecipientSeat == 0 && e.RecipientSeat == 2) &&
            transferred.Select(e => e.Kind).ToHashSet().SetEquals([OriginalHandBenefitKind.HandLimit, OriginalHandBenefitKind.AttackRange]) &&
            E<OriginalHandInheritanceCompletedEvent>(g).Single() == new OriginalHandInheritanceCompletedEvent(id, 2, 2) &&
            g.GetAttackRange(0) == 1 && g.GetAttackRange(2) == 2 &&
            E<OriginalHandPermanentBonusGrantedEvent>(g).Count(e => e.RecipientSeat == 1 && e.Kind == OriginalHandBenefitKind.HandLimit) == 1 &&
            !g.ResolutionStack.Any(f => f.Id == id),
            "Exactly the dead owner's two permanent increments transfer once and leave its prior recipient's award intact; the old owner loses the transferred range and the living beneficiary gains it.");
        _ = Cold(g, registry);
    }

    public static void OutsideDrawCountsInvocationAndRejectsGiftInitialNormalAndEmptyDraw()
    {
        var (g, registry) = Create(Scenario.Draw);
        Require(V(g, 0).Hand.Count == 6 && !E<OutsidePhaseDrawDiscardStartedEvent>(g).Any() &&
            E<NativeDrawInvocationRecordedEvent>(g).Any(e => e.OwnerSeat == 0 && e.ActualCount == 2 && e.Phase?.Kind == ActualDiscardRecoveryPhaseKind.Draw),
            "Initial deal and the positive native normal Draw2 do not trigger the formal outside-Draw skill.");
        Use(g, "draw-three"); Reach(g, p => Outside(p, "draw-target"));
        var root = OutsideRoot(g); var r = root.OutsidePhaseDrawDiscard!; var id = root.Id;
        Require(r.OriginalDraw is { RequestedCount: 3, ActualCount: 3 } native && native.OwnerSeat == 0 &&
            native.Phase?.Kind == ActualDiscardRecoveryPhaseKind.Play && native.LastBatchId == r.OriginalBatchId &&
            r.OriginalMovementIndex >= 0 && E<OutsidePhaseDrawDiscardStartedEvent>(g).Single().NativeDrawInvocationId == native.InvocationId &&
            native.Materials.All(m => g.CardMovements.Any(c => c.CardId == m.CardId && c.Sequence == m.MovementSequence && c.From == CardLocation.DrawPile && c.To == CardLocation.Hand(0))),
            "One real DrawCards(3) invocation, including its three exact physical movements and final batch, produces one mandatory target decision.");
        Private(g); Frozen(r.EligibleTargets); g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([0])); Play(g);
        Require(E<OutsidePhaseDrawDiscardStartedEvent>(g).Length == 1 && E<OutsidePhaseDrawDiscardMovementIssuedEvent>(g).Single() is
                { ParticipantSeat: 0, ActualCount: 1, Op: SkillProgramEffectOp.DrawAfterActualOutsideDraw } issued && issued.FrameId == id &&
            E<NativeDrawInvocationRecordedEvent>(g).Count(e => e.DirectProducer?.SkillId == Weishu && e.DirectProducer.OwnerSeat == 0 && e.ActualCount == 1) == 1 &&
            E<OutsidePhaseDrawDiscardCompletedEvent>(g).Single() == new OutsidePhaseDrawDiscardCompletedEvent(id, 1),
            "Choosing oneself really draws once and the exact direct producer excludes recursive Weishu, independently of the original three-card count.");
        var before = V(g, 0).Hand.Count; Use(g, "collect", [1]); Play(g);
        Require(V(g, 0).Hand.Count == before + 4 && V(g, 1).Hand.Count == 0 && E<OutsidePhaseDrawDiscardStartedEvent>(g).Length == 1,
            "A real four-card foreign Hand gift gains actual entities but carries no native DrawCards proof and cannot trigger Weishu.");
        _ = Cold(g, registry);

        var (empty, _) = Create(Scenario.EmptyDraw);
        Require(empty.CreateSnapshot(0).DrawPileCount == 0 && !E<OutsidePhaseDrawDiscardStartedEvent>(empty).Any(), "The exact sixteen-entity initial deal exhausts the tiny physical deck without becoming outside draw.");
        Use(empty, "draw-three"); Play(empty);
        Require(empty.CreateSnapshot(0).DrawPileCount == 0 && !E<NativeDrawInvocationRecordedEvent>(empty).Any() &&
            !E<OutsidePhaseDrawDiscardStartedEvent>(empty).Any(), "A native Draw3 attempt with zero actual entities creates neither a positive invocation nor an outside-draw obligation.");
    }

    public static void OutsideDiscardUsesRealSanchenPaymentAndExcludesRecastAndDiscardPhase()
    {
        var (g, registry) = Create(Scenario.Discard);
        var ownerCard = V(g, 0).Hand[0].Id; var partnerCard = V(g, 1).Hand[0].Id;
        Accept(g, new UseProgramSkillCommand(0, Chongxin, "paired-hand-recast", [ownerCard], [1], g.Revision, P(g)!.PromptId));
        Reach(g, p => p.PlayerSeat == 1 && Has(p, "paired-hand-recast")); Answer(g, c => c.Cards.SequenceEqual([partnerCard]));
        Reach(g, p => Outside(p, "draw-target")); Answer(g, c => c.Targets.SequenceEqual([0])); Play(g);
        Require(E<PairedHandRecastCompletedEvent>(g).Single() is { OwnerPaid: true, PartnerPaid: true } &&
            !E<OutsidePhaseDrawDiscardStartedEvent>(g).Any(e => e.Op == SkillProgramEffectOp.DiscardAfterActualOutsideDiscard),
            "Formal paired recast really sends two cards to DiscardPile and issues real draw, while RecastDiscard remains excluded from discard-only Weishu.");
        Accept(g, new UseProgramSkillCommand(0, "ol:sanchen", "draw-three-discard-three", [], [0], g.Revision, P(g)!.PromptId));
        Reach(g, p => Outside(p, "draw-target")); Answer(g, c => c.Targets.SequenceEqual([0]));
        var paid = new List<int>();
        for (var n = 0; n < 3; n++) { Reach(g, p => Has(p, "draw-discard-category")); var card = P(g)!.Choices.First().Cards.Single(); paid.Add(card); Answer(g, c => c.Cards.SequenceEqual([card])); }
        Reach(g, p => Outside(p, "discard-card"));
        var root = OutsideRoot(g); var r = root.OutsidePhaseDrawDiscard!; var id = root.Id;
        Require(r.Op == SkillProgramEffectOp.DiscardAfterActualOutsideDiscard && r.OriginalDraw is null && r.OriginalDiscards.Count == 3 &&
            r.OriginalDiscards.Select(c => c.CardId).ToHashSet().SetEquals(paid) && r.OriginalPhase?.Kind == ActualDiscardRecoveryPhaseKind.Play &&
            g.CardMovements.Count(m => paid.Contains(m.CardId) && m.Reason.Value == "skill-program.ol:sanchen.draw-discard-category.discard") == 3 &&
            E<OutsidePhaseDrawDiscardStartedEvent>(g).Count(e => e.Op == SkillProgramEffectOp.DiscardAfterActualOutsideDiscard) == 1 &&
            P(g)!.Choices.Where(c => c.Targets.SequenceEqual([1])).All(c => c.Cards.Count == 0 && c.Parameters["source-zone"] == nameof(CardZoneKind.Hand)),
            "The real formal Sanchen simultaneously pays three own Hand cards during Play and produces one exact native discard obligation with genuinely opaque foreign Hand slots.");
        Private(g); Frozen(r.OriginalDiscards); Frozen(r.EligibleMaterials);
        Answer(g, c => c.Targets.SequenceEqual([1]) && c.Parameters.GetValueOrDefault("slot-index") == "0");
        Reach(g, p => p.SkillPrompt?.SkillId == DiscardChild && IsContinue(p));
        root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == id); r = root.OutsidePhaseDrawDiscard!;
        var child = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == id);
        Require(r is { Stage: OutsidePhaseDrawDiscardStage.MovementChildren, MovementIssued: true, ActualCount: 1, ParticipantSeat: 1, PaidMaterial: { } } &&
            root.PendingMovementContinuation?.SubjectSeat == 1 && child.Batch.AwaitingProgramFrameId == id &&
            child.Batch.Movements is [var discard] && discard.CardId == r.PaidMaterial.CardId && discard.From == CardLocation.Hand(1) &&
            discard.To == CardLocation.DiscardPile && discard.Sequence > r.SequenceBefore && discard.Sequence <= r.SequenceAfter &&
            !E<OutsidePhaseDrawDiscardCompletedEvent>(g).Any(e => e.FrameId == id),
            "The blind selection really discards its original foreign Hand entity once and keeps the owning receipt outstanding beneath that original owner's native child.");
        Private(g); g = Cold(g, registry); Continue(g); Play(g);
        Require(E<OutsidePhaseDrawDiscardCompletedEvent>(g).Count(e => e.FrameId == id && e.ActualCount == 1) == 1 &&
            E<OutsidePhaseDrawDiscardMovementIssuedEvent>(g).Count(e => e.FrameId == id && e.PaidMaterial?.CardId == r.PaidMaterial!.CardId) == 1 &&
            E<DrawDiscardCategoryCompletedEvent>(g).Single() is { DistinctNonEmpty: false, BonusIssued: false },
            "Cold child return settles the single native discard once without repeating Sanchen payment or inventing distinct categories from the all-equipment deck.");
        Use(g, "draw-three"); Reach(g, p => Outside(p, "draw-target")); Answer(g, c => c.Targets.SequenceEqual([0])); Play(g);
        var count = E<OutsidePhaseDrawDiscardStartedEvent>(g).Count(e => e.Op == SkillProgramEffectOp.DiscardAfterActualOutsideDiscard);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Reach(g, p => p is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 });
        var original = P(g)!; Accept(g, new DiscardCardsCommand(0, original.ValidCardIds.Take(original.RequiredCardCount).ToArray(), original.PromptId, g.Revision));
        Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        Require(E<HandLimitDiscardedEvent>(g).Any(e => e.ActorSeat == 0 && e.CardIds.Count > 0) &&
            E<OutsidePhaseDrawDiscardStartedEvent>(g).Count(e => e.Op == SkillProgramEffectOp.DiscardAfterActualOutsideDiscard) == count,
            "A real positive own hand-limit discard during the native Discard phase remains excluded even though its producer is genuine discard.");
    }

    private static void Benefit(GameEngine g, int seat, string option)
    {
        Reach(g, p => Entity(p, "benefit-target")); Answer(g, c => c.Targets.SequenceEqual([seat]));
        Reach(g, p => Entity(p, "benefit-option")); Answer(g, c => c.Parameters.GetValueOrDefault("option") == option);
    }
    private static void ReachDeathInheritanceOffer(GameEngine g)
    {
        for (var step = 0; step < 40; step++)
        {
            var p = P(g);
            if (p?.SkillPrompt?.SkillId == Huaiyuan && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate")) return;
            if (g.CreateSnapshot(0).Status == EngineStatus.Completed ||
                E<PlayerDiedEvent>(g).Any(e => e.VictimSeat == 0) &&
                !g.ResolutionStack.OfType<DeathFrame>().Any(f => f.VictimSeat == 0)) break;
            if (p is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 })
                Answer(g, c => c.Cards.Count == 0 && (c.Parameters.GetValueOrDefault("response") is "let-die" or "pass" || c.Parameters.GetValueOrDefault("action") == "pass"));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The genuine optional OwnerDied inheritance offer was not published: " + JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(s => new { Viewer = s, State = g.CreateSnapshot(s) }).ToArray(),
            Frames = g.ResolutionStack, Bonuses = E<OriginalHandPermanentBonusGrantedEvent>(g),
            Bindings = E<ProgramBindingResolvedEvent>(g).Where(e => e.SkillId == Huaiyuan).ToArray(),
            RecentFacts = g.Events.TakeLast(14).Select(e => new { Type = e.Payload.GetType().Name, Fact = JsonSerializer.Serialize(e.Payload, e.Payload.GetType()) }),
            RecentCommands = CommandJson.Serialize(g.AcceptedCommands.TakeLast(5))
        }));
    }
    private static bool Has(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static bool Entity(PendingDecision p, string action) => Has(p, "original-hand-entity") && p.Choices.Any(c => c.Parameters.GetValueOrDefault("entity-action") == action);
    private static bool Outside(PendingDecision p, string branch) => Has(p, "outside-phase-draw-discard") && p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch);
    private static bool IsContinue(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static ProgramSkillFrame OutsideRoot(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.OutsidePhaseDrawDiscard is not null);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(r.Accepted, r.Error?.Message ?? "A legal fixed Yang Hu command was rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> select) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string id, int[]? targets = null, int[]? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> stop)
    {
        for (var step = 0; step < 180; step++)
        {
            var p = P(g); if (p is not null && stop(p)) return;
            if (p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) break;
            if (p is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 })
                Answer(g, c => c.Cards.Count == 0 && (c.Parameters.GetValueOrDefault("response") is "let-die" or "pass" || c.Parameters.GetValueOrDefault("action") == "pass"));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The real fixed Yang Hu boundary was not reached: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack, Recent = g.Events.TakeLast(8).Select(e => e.Payload) }));
    }
    private static void Tags(GameEngine g, int[] ids)
    {
        Frozen(V(g, 0).OriginalHandEntities!);
        var state = V(g, 0).OriginalHandEntities!.Single(s => s.SkillId == Huaiyuan && s.StateId == "sui");
        Require(state.Name == "绥" && state.RemainingCount == ids.Length && state.CardIds is not null && state.CardIds.ToHashSet().SetEquals(ids),
            "The owner sees exactly its still-unconsumed original physical Sui identities, including no returned or newly drawn substitute.");
        Frozen(state.CardIds!);
        foreach (var viewer in new[] { 1, 2, 3 })
        {
            var other = g.CreateSnapshot(viewer).Players[0].OriginalHandEntities!.Single();
            Require(other.SkillId == Huaiyuan && other.StateId == "sui" && other.Name == "绥" && other.RemainingCount == ids.Length && other.CardIds is null,
                "Other prepared views receive only the public Sui count and name, never the owner's private initial entity list.");
        }
    }
    private static void Frozen<T>(IReadOnlyList<T> list) => Require(list is System.Collections.IList { IsReadOnly: true }, "The prepared nested collection is frozen.");
    private static void Private(GameEngine g)
    {
        var p = P(g)!; Require(p.IsPrivate, "The native private material or gain child remains private.");
        foreach (var viewer in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat)) Require(g.CreateSnapshot(viewer).PendingDecision is null &&
            g.CreateSnapshot(viewer).Players[p.PlayerSeat].Hand.Count == 0, "Foreign views expose neither the private pending prompt nor held Hand identities.");
        Frozen(p.Choices); Frozen(p.ValidCardIds); Frozen(p.ValidTargetSeats); Frozen(p.ValidContentIds);
        foreach (var c in p.Choices) { Frozen(c.Cards); Frozen(c.Targets); Frozen(c.ContentIds); Require(c.Parameters is System.Collections.IDictionary { IsReadOnly: true }, "Choice parameters are deeply frozen."); }
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements,
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(copy) == State(g), "Cold command replay preserves all four prepared views, durable original entities, frozen receipts, native children, scalar facts, card provenance and accepted commands."); return copy; }
    private static (GameEngine, ContentRegistry) Create(Scenario scenario = Scenario.Original)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = scenario == Scenario.Death ? Role.Renegade : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Require(P(g)!.ValidContentIds.Contains("fixture:yang-hu-owner"), "The fixed small mode genuinely publishes its intended owner under native role-weighted selection.");
        Accept(g, new SelectGeneralCommand(0, "fixture:yang-hu-owner", g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(Scenario scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ordinary-yang-hu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            foreach (var id in new[] { Huaiyuan, Chongxin, Dezhang, Weishu, "ol:sanchen" }) b.AddSkill(Official.Value.GetSkill(id));
            var rules = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"activations":[
                {"id":"equip","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"equipment"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"equipment"}]},
                {"id":"give","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":1}]},
                {"id":"give-all","minCards":4,"maxCards":4,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":4}]},
                {"id":"discard-two","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":2}]},
                {"id":"return-equipment","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","amount":1,"zones":["equipment"],"resultBind":"returned"},{"op":"moveBoundCards","target":"owner","sourceBind":"returned","destination":"ownerHand","awaitMovementTriggers":true}]},
                {"id":"collect","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelectedTargetHand","target":"selectedTarget"}]},
                {"id":"draw-three","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]},
                {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                {"id":"die","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":4}]}]},
              {"id":"{{{Gain}}}","revision":1,"triggers":[{"id":"native-benefit-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ol:huaiyuan.original-hand-benefit.draw"],"optional":true,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["{{{Suppress}}}"]}]}]},
              {"id":"{{{Health}}}","revision":1,"triggers":[{"id":"real-max-hp-child","window":"afterHealthChanged","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"{{{DiscardChild}}}","revision":1,"triggers":[{"id":"real-weishu-discard-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ol:weishu.outside-phase-discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:yang-hu-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """)!;
            var presentation = rules["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n =>
            {
                var id = n!["id"]!.GetValue<string>(); var p = new Dictionary<string, object> { ["name"] = id, ["description"] = "正式技能与真实原生边界夹具" };
                if (id is Gain or Health or DiscardChild) p["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                return (object)p;
            });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentation }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "固定原生驱动") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Suppress, "实际HP5资格抑制", "不修改任何隐藏状态") { SuppressionRule = new(5) });
            b.AddSkill(new("fixture:yang-hu-owner-selection", "固定拥有者候选", "正式身份评分")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Renegade ? 10000d : -10000d) });
            b.AddSkill(new("fixture:yang-hu-peer-selection", "固定其他候选", "正式身份评分")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 10000d : 100d) });
            var owner = new List<string> { Driver };
            if (scenario is Scenario.Draw or Scenario.EmptyDraw or Scenario.Discard) owner.Add(Weishu);
            else owner.AddRange([Huaiyuan, Chongxin, Dezhang]);
            if (scenario == Scenario.Nested) owner.Add(Gain);
            if (scenario == Scenario.Awake) owner.Add(Health);
            if (scenario == Scenario.Discard) owner.AddRange([Chongxin, "ol:sanchen"]);
            b.AddGeneral(new("fixture:yang-hu-owner", "羊祜真实机制拥有者", "supporter", "fixture:yang-hu-owner-selection", "jin", 4, owner));
            var peers = Enumerable.Range(1, 3).Select(s => $"fixture:yang-hu-peer-{s}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "固定原生其他角色", "supporter", "fixture:yang-hu-peer-selection", "qun", 4,
                scenario == Scenario.Discard ? ["fixture:yang-hu-quiet", DiscardChild] : ["fixture:yang-hu-quiet"]));
            b.AddDeck(new("fixture:yang-hu-deck", "同质小实体牌堆", 4, scenario == Scenario.Draw ? 2 : 0, [])
                { PhysicalCards = Enumerable.Range(0, scenario == Scenario.EmptyDraw ? 16 : 64).Select(i => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "小四人正式羊祜边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:yang-hu-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:yang-hu-owner", .. peers]));
        }
    }
}
