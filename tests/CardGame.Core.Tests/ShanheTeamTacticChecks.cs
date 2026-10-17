using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheTeamTacticChecks
{
    private const string Jiban = "shanhe:jiban-2026", Duichu = "shanhe:duichu-2026", Duipai = "shanhe:duipai-2026";
    private const string Owner = "fixture:team-tactic-owner", Driver = "fixture:team-tactic-driver";
    private const string Giver = "fixture:team-tactic-giver", Quiet = "fixture:team-tactic-quiet";
    private const string Weishu = "ol:weishu", WeishuWatch = "fixture:team-tactic-weishu-child-watch";
    private const string Zhiyi = "ol:zhiyi", GainInstruction = "fixture:team-tactic-gained-draw";
    private const string Ally = "fixture:team-tactic-ally", Other = "fixture:team-tactic-other", Enemy = "fixture:team-tactic-enemy";
    private const string Deck = "fixture:team-tactic-deck", IdentityDeck = "fixture:team-tactic-identity-deck";
    private const string IdentityMode = "identity:classic-team-tactic-fixture";
    private const string AuraBoundary = "fixture:team-aura-boundary", AuraGain = "fixture:team-aura-gain", AuraLoss = "fixture:team-aura-loss";
    private const string AuraNativeCapability = "fixture:team-aura-native-capability";
    private static readonly Lazy<ContentSkillDefinition> ActualDuipai = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(43, 1, "fixture-team-aura-program", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [],
            [new("standard:cao-cao")], [], new string('e', 64))).GetSkill(Duipai));
    private static readonly ShanheStartingCard[] FenghuangFaces =
    [new(CardKind.Slash, Suit.Heart, 5), new(CardKind.Dodge, Suit.Heart, 13),
        new(CardKind.ArrowBarrage, Suit.Heart, 1), new(CardKind.Alcohol, Suit.Spade, 3)];

    public static void TeamDrawAuraStacksForOtherPublicAlliesOnlyDuringActualDrawPhasesCold()
    {
        var s = Scenario.Create("aura-stacked");
        s.Need(Enumerable.Range(0, 4).All(seat => s.V(seat).HandCount == 0),
            "Two real repeatable team-draw grants do not manufacture constructor hand cards or alter the initial zero-card recipe.");
        s.StartGame(); s.Play();
        s.Need(s.V(0).HandCount == 2 && s.V(0).Skills!.Any(skill => skill.ContentId == Duipai) &&
            s.G.CreateCheckpoint().Options.ShanheBattle!.TacticSkillIds.SequenceEqual([Duipai, Duipai]),
            "Both independently acquired instances remain in the genuine frozen opening recipe, while their holder's own real normal draw stays at the base two.");
        s.Use("draw-one", [1]);
        s.Need(s.V(1).HandCount == 1 && s.G.CardMovements.Count(m => m.To == CardLocation.Hand(1) &&
            m.Reason.Value == "skill-program." + Driver + ".Draw") == 1,
            "A genuine program Draw1 to an eligible teammate remains exactly one; the aura only modifies the actual draw-phase numeric query.");
        s.Cold(); s.EndPlay(); s.Play();
        s.Need(PhaseDraws(s, 0).Length == 4 && PhaseDraws(s, 1).Length == 6 && PhaseDraws(s, 2).Length == 6 &&
            PhaseDraws(s, 3).Length == 2 && s.V(1).HandCount == 7 && s.V(2).HandCount == 6 && s.V(3).HandCount == 2,
            "In one bounded real round, each other public teammate receives base two plus both independent +2 instances; the owner and enemy retain base two without replaying the separate program card.");
        s.Conserved(); s.Cold();

        var unknown = Scenario.Create("aura-unknown", publicTeams: false); unknown.StartGame(); unknown.Play();
        unknown.Need(Enumerable.Range(0, 4).All(seat => unknown.V(seat).TeamId is null) && PhaseDraws(unknown, 0).Length == 2,
            "A printed aura and shared faction do not reveal concealed identities or imply an extra personal draw.");
        unknown.Cold(); unknown.EndPlay(); unknown.Play();
        unknown.Need(PhaseDraws(unknown, 0).Length == 4 && Enumerable.Range(1, 3).All(seat => PhaseDraws(unknown, seat).Length == 2),
            "No concealed-team recipient gains a numeric aura bonus during its actual first draw phase.");
        unknown.Conserved(); unknown.Cold();
    }

    public static void TeamDrawAuraRechecksBeforeIssueAndKeepsIssuedRequestsFrozenCold()
    {
        foreach (var issued in new[] { false, true })
        foreach (var lose in new[] { false, true })
        {
            var scene = "aura-" + (issued ? "issued-" : "before-") + (lose ? "loss" : "death");
            var s = Scenario.Create(scene); s.StartGame();
            s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.SkillPrompt?.SkillId == AuraBoundary);
            var phase = s.G.ResolutionStack.OfType<DrawPhaseObligationFrame>().Single();
            var attempt = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().SingleOrDefault();
            s.Need(s.V(1).IsAlive && s.V(1).Skills!.Any(skill => skill.ContentId == Duipai) && s.V(0).HandCount == 0 &&
                (issued ? phase is { Stage: DrawPhaseObligationStage.RequestingDraw, NativeDraw.RequestedCount: 4 } &&
                    attempt is { Context.RequestedCount: 4, Return.Kind: NativeDrawAttemptReturnKind.DrawPhase } &&
                    attempt.Context.ParentFrameId == phase.Id
                    : phase is { Stage: DrawPhaseObligationStage.Drawing, NativeDraw: null } && attempt is null),
                "The real pre-phase window precedes request capture, whereas nativeDrawStarting owns an already-issued four-card request before any physical card moves.");
            var phaseId = phase.Id;
            s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "proceed");
            s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.SkillPrompt?.SkillId == AuraBoundary && s.P.Choices.Any(c => c.Targets.SequenceEqual([1])));
            s.Cold(); s.Answer(c => c.Targets.SequenceEqual([1]));
            s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.SkillPrompt?.SkillId == AuraGain);
            var requested = issued ? 4 : 2;
            var gainFrame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == AuraGain);
            var gainContext = gainFrame.WindowContext;
            var gainBatch = gainContext?.MovementBatch;
            var proof = gainBatch?.NativeDrawInvocation;
            if (proof is null) s.Need(false, "The genuine gained-card stop must belong to the final batch of the actual normal Draw invocation. " +
                JsonSerializer.Serialize(new
                {
                    scene, issued, lose, requested, gainFrame.Id, gainFrame.SkillId, gainFrame.TriggerId, gainFrame.InstructionIndex,
                    Window = gainContext?.Window,
                    Batch = gainBatch is null ? null : new { gainBatch.Id, gainBatch.ParentFrameId, gainBatch.Movements, gainBatch.SourceCounts, gainBatch.DestinationCounts },
                    Source = new { s.V(1).IsAlive, s.V(1).Hp, s.V(1).Armor, Skills = s.V(1).Skills?.Select(skill => skill.ContentId) },
                    phaseId, Hand = s.V(0).HandCount, Draws = s.Events<NativeDrawInvocationRecordedEvent>()
                        .Select(draw => new { draw.InvocationId, draw.Requested, draw.ActualCount, draw.LastBatchId, draw.ParentFrameId }),
                    Gains = s.G.CardMovements.Where(movement => movement.To.Zone == CardZoneKind.Hand)
                }));
            var actualProof = proof ?? throw new InvalidOperationException("The missing actual draw invoice must fail at the detailed native-batch diagnostic above.");
            var sourceInactive = lose ? s.V(1).IsAlive && s.V(1).Hp == 1 && s.V(1).Skills?.Any(skill => skill.ContentId == Duipai) != true : !s.V(1).IsAlive;
            var exactRequested = actualProof.OwnerSeat == 0 && actualProof.ActualCount == requested && actualProof.RequestedCount == requested;
            var exactParent = actualProof.ParentFrameId == phaseId && actualProof.Reason == CardMoveReasons.Draw;
            var exactMaterials = s.V(0).HandCount == requested && PhaseDraws(s, 0).Length == requested;
            var exactDamage = s.Events<DamageAppliedEvent>().Count(e => e.TargetSeat == 1 && e.Amount == 1) == 1;
            if (!(sourceInactive && exactRequested && exactParent && exactMaterials && exactDamage)) s.Need(false,
                "An actual one-damage death or legal dying-entry recovery/skill replacement disables the aura: unissued draw becomes two, while the already-issued four remains four through genuine CardsGained children. " +
                JsonSerializer.Serialize(new
                {
                    scene, issued, lose, requested, sourceInactive, exactRequested, exactParent, exactMaterials, exactDamage,
                    Source = new { s.V(1).IsAlive, s.V(1).Hp, s.V(1).Armor, Skills = s.V(1).Skills?.Select(skill => skill.ContentId) },
                    Proof = new { actualProof.OwnerSeat, actualProof.RequestedCount, actualProof.ActualCount, actualProof.ParentFrameId, actualProof.Reason, actualProof.InvocationId },
                    phaseId, Hand = s.V(0).HandCount, PhaseMaterials = PhaseDraws(s, 0).Length,
                    Phases = s.G.ResolutionStack.OfType<DrawPhaseObligationFrame>().Select(frame => new { frame.Id, frame.Stage, frame.FrozenBaseDrawCount, frame.DrawCount, Requested = frame.NativeDraw?.RequestedCount }),
                    Damage = s.Events<DamageAppliedEvent>(), Recovery = s.Events<ProgramRecoveryAppliedEvent>(),
                    Replaced = s.Events<ProgramOwnerSkillsReplacedEvent>()
                }));
            var invocationId = actualProof.InvocationId;
            s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Play();
            s.Need(s.Events<NativeDrawInvocationRecordedEvent>().Count(e => e.InvocationId == invocationId && e.ActualCount == requested && e.Requested == requested) == 1 &&
                s.Events<ActualDrawPhaseCompletedEvent>().Single(e => e.FrameId == phaseId).DrawCount == requested,
                "The owning real draw phase, exact native invoice and actual material count settle once after cold child recovery.");
            s.Cold(); s.EndPlay(); s.Play();
            s.Need(PhaseDraws(s, 0).Length == requested + 2 && s.V(0).HandCount == requested + 2 &&
                s.Events<DamageAppliedEvent>().Count(e => e.TargetSeat == 1) == 1,
                "The next real normal draw rechecks the dead or lost holder and returns to two without repeating the previous mutation or issued payment.");
            s.Conserved(); s.Cold();
        }
    }

    private static CardMovementRecord[] PhaseDraws(Scenario s, int seat) => s.G.CardMovements.Where(m =>
        m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(seat) && m.Reason == CardMoveReasons.Draw).ToArray();

    public static void JibanRechecksLivingPublicAlliesNativeSlashDebitsAndSkillLossCold()
    {
        var maximum = Scenario.Create("quota"); maximum.StartGame(); maximum.Play();
        for (var use = 0; use < 5; use++) maximum.Slash(converted: use == 1);
        maximum.Need(maximum.V(0).Hand.Any(c => c.Kind == CardKind.Slash) && !maximum.CanSlash() &&
            maximum.Debits() == 5, "Two other living public teammates supply exactly four additional native Slash uses; a converted Slash shares the same five paid debits.");
        maximum.Cold();

        var death = Scenario.Create("quota"); death.StartGame(); death.Play();
        for (var use = 0; use < 3; use++) death.Slash(converted: use == 1);
        death.Need(death.CanSlash() && death.Debits() == 3, "With two actual teammates a fourth Slash remains legal after three real uses.");
        death.Use("kill", [2]);
        death.Need(!death.V(2).IsAlive && death.V(1).IsAlive && death.V(0).Hand.Any(c => c.Kind == CardKind.Slash) && !death.CanSlash() && death.Debits() == 3,
            "An actual teammate death immediately lowers the current total quota from five to three without resetting already-paid Slash uses.");
        death.Cold();

        var loss = Scenario.Create("quota"); loss.StartGame(); loss.Play(); loss.Slash();
        loss.Need(loss.CanSlash(), "The enabled team policy permits further real Slashes before native skill loss.");
        loss.Use("lose-jiban");
        loss.Need(loss.V(0).Skills?.Any(s => s.ContentId == Jiban) != true && loss.V(0).Hand.Any(c => c.Kind == CardKind.Slash) && !loss.CanSlash() && loss.Debits() == 1,
            "Losing the actual owner skill immediately restores the ordinary one-use limit while preserving the existing debit.");
        loss.Cold();

        var unknown = Scenario.Create("quota", publicTeams: false); unknown.StartGame(); unknown.Play(); unknown.Slash();
        unknown.Need(Enumerable.Range(0, 4).All(seat => unknown.V(seat).TeamId is null) &&
            !unknown.CanSlash() && unknown.Debits() == 1,
            "Concealed identities and the shared printed Wu faction do not substitute for public teammates or count the owner itself.");
        unknown.Cold();
    }

    private static void TeamOpeningRunsOriginalWeishuChildExactlyOnceCold()
    {
        var s = Scenario.Create("weishu"); s.StartGame();
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("program-action") == "outside-phase-draw-discard"));
        var program = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OutsidePhaseDrawDiscard is not null);
        var original = program.OutsidePhaseDrawDiscard!;
        var parentDraw = original.OriginalDraw!;
        s.Need(program.SkillId == Weishu && original.Stage == OutsidePhaseDrawDiscardStage.Choosing &&
            parentDraw is { OwnerSeat: 0, ActualTurnNumber: 0, RequestedCount: 2, ActualCount: 2 } &&
            parentDraw.GameStartingOrigin is { Source.SkillId: Duichu, Source.OwnerSeat: 1, OutsideDrawChild: null } &&
            TeamDraws(s).Count(m => m.To == CardLocation.Hand(0)) == 2,
            "The original Weishu receives the teammate's one actual startup Draw2 invoice and publishes its genuine native target choice.");
        var frameId = program.Id;
        s.Cold(); s.Answer(c => c.Targets.SequenceEqual([0]));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == WeishuWatch);
        var pending = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == frameId).OutsidePhaseDrawDiscard!;
        var invoice = s.Events<NativeDrawInvocationRecordedEvent>().Single(e => e.OwnerSeat == 0 && e.Requested == 1 &&
            e.DirectProducer == original.Source && e.GameStartingOrigin?.OutsideDrawChild?.ProgramFrameId == frameId);
        var child = invoice.GameStartingOrigin!.OutsideDrawChild!;
        s.Need(pending is { Stage: OutsidePhaseDrawDiscardStage.MovementChildren, MovementIssued: true, ParticipantSeat: 0, ActualCount: 1 } &&
            s.V(0).HandCount == 3 && invoice is { ActualCount: 1, TurnNumber: 0 } && invoice.ParentFrameId == frameId &&
            invoice.GameStartingOrigin.ProgramFrameId == parentDraw.GameStartingOrigin!.ProgramFrameId &&
            invoice.GameStartingOrigin.Source == parentDraw.GameStartingOrigin.Source &&
            invoice.GameStartingOrigin.ReturnKind == NativeDrawAttemptReturnKind.ProgramInstruction && invoice.GameStartingOrigin.Operation is null &&
            child.Source == original.Source && child.InstructionIndex == original.InstructionIndex && child.GameplayHash == original.GameplayHash &&
            child.WindowFrameId == original.WindowFrameId && child.OriginalBatchId == original.OriginalBatchId &&
            child.ParentInvocationId == parentDraw.InvocationId && child.ParticipantSeat == 0 && child.SequenceBefore == pending.SequenceBefore &&
            s.Events<OutsidePhaseDrawDiscardMovementIssuedEvent>().Count(e => e.FrameId == frameId && e.ActualCount == 1 && e.ParticipantSeat == 0) == 1,
            "The actual Weishu Draw1 is paid before its real CardsGained choice child; its invoice retains the startup producer and the exact separate native child frame, source, parent invoice, batch and material interval.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Play();
        s.Need(s.Events<OutsidePhaseDrawDiscardStartedEvent>().Count(e => e.Source.SkillId == Weishu) == 1 &&
            s.Events<OutsidePhaseDrawDiscardCompletedEvent>().Count(e => e.FrameId == frameId && e.ActualCount == 1) == 1 &&
            s.Events<NativeDrawInvocationRecordedEvent>().Count(e => e.InvocationId == invoice.InvocationId) == 1 &&
            s.G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) &&
                m.Reason.Value == "skill-program.ol:weishu.outside-phase-draw") == 1 && s.V(0).HandCount == 3,
            "Choosing itself is a legal Weishu target: the skill's own actual Draw1 cannot retrigger Weishu, and cold replay does not issue, pay or complete its native child twice.");
        s.Conserved(); s.Cold();
    }

    private static void TeamOpeningHongdeThenOtherOwnersWeishuKeepsExactInvoicesCold()
    {
        var s = Scenario.Create("hongde-weishu"); s.StartGame();
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "bu-zhi"));
        var hongde = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.BuZhi is not null);
        var hongdeSource = hongde.BuZhi!.Source;
        var rootDraw = hongde.WindowContext!.MovementBatch!.NativeDrawInvocation!;
        s.Need(rootDraw is { OwnerSeat: 0, ActualTurnNumber: 0, ActualCount: 2 } &&
            rootDraw.GameStartingOrigin?.Source.SkillId == Duichu,
            "The deeper chain begins with the original native Hongde entitlement from the teammate's actual startup Draw2.");
        s.Cold(); s.Answer(c => c.Targets.SequenceEqual([1]));
        s.Reach(() => s.P is { PlayerSeat: 1 } && s.P.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("program-action") == "outside-phase-draw-discard"));
        var weishu = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OutsidePhaseDrawDiscard is not null);
        var entitlement = weishu.OutsidePhaseDrawDiscard!;
        var hongdeDraw = entitlement.OriginalDraw!;
        var gainChild = hongdeDraw.GameStartingOrigin!.OutsideDrawChild!;
        s.Need(weishu.OwnerSeat == 1 && weishu.SkillId == Weishu && hongdeDraw is
            { OwnerSeat: 1, RequestedCount: 1, ActualCount: 1, ActualTurnNumber: 0, DirectProducer: null } &&
            hongdeDraw.Reason.Value == "program.bu-zhi.draw" && gainChild.Kind.ToString() == "NativeGainBenefit" &&
            gainChild.ProgramFrameId == hongde.Id && gainChild.Source == hongdeSource &&
            gainChild.ParentInvocationId == rootDraw.InvocationId && gainChild.ParticipantSeat == 1 &&
            gainChild.RequestedCount == 1 && hongdeDraw.GameStartingOrigin.ProgramFrameId == rootDraw.GameStartingOrigin!.ProgramFrameId,
            "Hongde really grants another original Weishu holder one card; its exact native-benefit child invoice retains Hongde's provenance without forging a DirectProducer for the underlying raw DrawCards call.");
        var weishuId = weishu.Id;
        var weishuSource = entitlement.Source;
        s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision));
        s.Reach(() => s.P is { PlayerSeat: 1 } && s.P.SkillPrompt?.SkillId == WeishuWatch);
        var pending = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == weishuId).OutsidePhaseDrawDiscard!;
        var issued = s.Events<NativeDrawInvocationRecordedEvent>().Single(e => e.GameStartingOrigin?.OutsideDrawChild is { } child &&
            child.ProgramFrameId == weishuId && child.ParentInvocationId == hongdeDraw.InvocationId);
        s.Need(pending is { Stage: OutsidePhaseDrawDiscardStage.MovementChildren, ParticipantSeat: 1, ActualCount: 1, MovementIssued: true } &&
            issued is { OwnerSeat: 1, Requested: 1, ActualCount: 1, TurnNumber: 0 } &&
            issued.DirectProducer == weishuSource && issued.GameStartingOrigin!.OutsideDrawChild!.Source == weishuSource &&
            issued.GameStartingOrigin.OutsideDrawChild.Kind.ToString() == "OutsidePhaseDraw" &&
            issued.GameStartingOrigin.ProgramFrameId == rootDraw.GameStartingOrigin!.ProgramFrameId &&
            s.Events<BuZhiMovementPaidEvent>().Count(e => e.FrameId == hongde.Id && e.Target == 1 && e.Actual == 1) == 1,
            "The NPC uses its real published Weishu choice and draws for itself; its actual gained-card child suspends with a separate invoice linked to Hongde's exact one-card parent invoice.");
        s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision)); s.Play();
        s.Need(s.Events<BuZhiStartedEvent>().Count(e => e.FrameId == hongde.Id) == 1 &&
            s.Events<BuZhiMovementPaidEvent>().Count(e => e.FrameId == hongde.Id) == 1 &&
            s.Events<OutsidePhaseDrawDiscardStartedEvent>().Count(e => e.FrameId == weishuId) == 1 &&
            s.Events<OutsidePhaseDrawDiscardCompletedEvent>().Count(e => e.FrameId == weishuId && e.ActualCount == 1) == 1 &&
            s.Events<NativeDrawInvocationRecordedEvent>().Count(e => e.InvocationId == hongdeDraw.InvocationId) == 1 &&
            s.Events<NativeDrawInvocationRecordedEvent>().Count(e => e.InvocationId == issued.InvocationId) == 1 &&
            s.G.CardMovements.Count(m => m.To == CardLocation.Hand(1) && m.Reason.Value == "program.bu-zhi.draw") == 1 &&
            s.G.CardMovements.Count(m => m.To == CardLocation.Hand(1) && m.Reason.Value == "skill-program.ol:weishu.outside-phase-draw") == 1 &&
            s.V(1).HandCount == 2,
            "Both native choices, both issued one-card invoices and their real entity payments complete once through cold replay; Weishu does not recurse on its own card gain.");
        s.Conserved(); s.Cold();
    }

    private static void TeamOpeningGainProducersThenWeishuKeepExactInvoicesCold()
    {
        foreach (var scene in new[] { "compiled-gain-weishu", "zhiyi-weishu" })
        {
            var zhiyi = scene == "zhiyi-weishu";
            var s = Scenario.Create(scene);
            var oldCard = zhiyi ? s.V(0).Hand.Single() : null;
            if (oldCard is not null)
                s.Need(oldCard.Kind == CardKind.Slash && oldCard.Suit == Suit.Club &&
                    s.G.CardMovements.Count(m => m.CardId == oldCard.Id && m.From == CardLocation.OutsideGame && m.To == CardLocation.Hand(0)) == 1,
                    "The original Zhiyi owns a genuine predealt black entity before the real startup Draw2; the two newly drawn cards cannot witness one another.");
            s.Cold(); s.StartGame();
            if (zhiyi)
            {
                s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.SkillPrompt?.SkillId == Zhiyi &&
                    s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
                s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            }
            s.Reach(() => s.P is { PlayerSeat: 0 } && (zhiyi
                ? s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.TangTang is { Stage: TangTangStage.BenefitTarget })
                : s.P.SkillPrompt?.SkillId == GainInstruction));
            var producer = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == (zhiyi ? Zhiyi : GainInstruction));
            var context = producer.WindowContext!;
            var rootDraw = context.MovementBatch!.NativeDrawInvocation!;
            var source = zhiyi ? producer.TangTang!.Source : new CardConversionSource(producer.SkillId, producer.TriggerId!, producer.OwnerSeat, producer.SkillInstanceId);
            s.Need(rootDraw is { OwnerSeat: 0, RequestedCount: 2, ActualCount: 2, ActualTurnNumber: 0 } &&
                rootDraw.GameStartingOrigin is { Source.SkillId: Duichu, Source.OwnerSeat: 1, OutsideDrawChild: null } &&
                rootDraw.LastBatchId == context.MovementBatch.Id && TeamDraws(s).Count(m => m.To == CardLocation.Hand(0)) == 2,
                "The selected gain program is owned by the final physical batch of one genuine startup Draw2, with the exact native invoice rather than an unrelated draw or constructor deal.");
            if (zhiyi)
                s.Need(s.V(0).GeneralId == "ol:tang-tang" && producer.TangTang is { Paid: true, ActualTurn: 0, Stage: TangTangStage.BenefitTarget } receipt &&
                    receipt.Gains.Count == 2 && receipt.Gains.All(gain => gain.NativeDrawId == rootDraw.InvocationId &&
                        gain.OtherHandCardId == oldCard!.Id && gain.OtherHandSuit == Suit.Club && gain.Suit == Suit.Club) &&
                    s.Events<ZhiyiOpportunityPaidEvent>().Count(e => e.FrameId == producer.Id && e.ActualTurn == 0) == 1,
                    "The original Tang Tang Zhiyi freezes both actual black gains against the old entity, pays exactly one turn-zero opportunity and publishes its genuine beneficiary choice.");
            else
                s.Need(context.Window == SkillProgramTriggerWindow.CardsGained &&
                    context.Facts is { DestinationZoneCountAfter: 2 } && producer.SelectedTargetSeats.Count == 0,
                    "The generic gained-card program selects only the last frozen Duichu Draw2 batch; its later one-card benefit has a different reason and cannot retrigger this observer.");
            s.Cold(); s.Answer(c => c.Targets.SequenceEqual([1]) &&
                (!zhiyi || c.Parameters.GetValueOrDefault("branch") == "benefit"));
            s.Reach(() => s.P is { PlayerSeat: 1 } && s.P.Choices.Any(c =>
                c.Parameters.GetValueOrDefault("program-action") == "outside-phase-draw-discard"));
            var weishu = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OutsidePhaseDrawDiscard is not null);
            var original = weishu.OutsidePhaseDrawDiscard!;
            var benefit = original.OriginalDraw!;
            var child = benefit.GameStartingOrigin!.OutsideDrawChild!;
            var reason = zhiyi ? "skill-program.tang-tang.zhiyi.draw" : "skill-program." + GainInstruction + ".Draw";
            s.Need(benefit is { OwnerSeat: 1, RequestedCount: 1, ActualCount: 1, ActualTurnNumber: 0, DirectProducer: null } &&
                benefit.ParentFrameId == producer.Id && benefit.Reason.Value == reason && child.ProgramFrameId == producer.Id &&
                child.Kind == (zhiyi ? GameStartingDrawChildKind.NativeGainColorBenefit : GameStartingDrawChildKind.ProgramInstruction) && child.Source == source &&
                child.InstructionIndex == (zhiyi ? 1 : 2) && child.RequestedCount == 1 && child.Operation is null &&
                child.ParticipantSeat == 1 && child.ParentInvocationId == rootDraw.InvocationId &&
                child.OriginalBatchId == rootDraw.LastBatchId && child.WindowFrameId == context.ParentFrameId &&
                benefit.GameStartingOrigin.ProgramFrameId == rootDraw.GameStartingOrigin!.ProgramFrameId &&
                benefit.GameStartingOrigin.Source == rootDraw.GameStartingOrigin.Source &&
                benefit.GameStartingOrigin.ReturnKind == NativeDrawAttemptReturnKind.ProgramInstruction && benefit.GameStartingOrigin.Operation is null,
                "The real selected Draw1 reaches the other owner's original Weishu; its exact native-color or compiled-instruction child invoice preserves the Duichu root, real producer, parent Draw2, instruction and recipient without forging DirectProducer.");
            s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision));
            s.Reach(() => s.P is { PlayerSeat: 1 } && s.P.SkillPrompt?.SkillId == WeishuWatch);
            var invoice = s.Events<NativeDrawInvocationRecordedEvent>().Single(e => e.GameStartingOrigin?.OutsideDrawChild is { } grandchild &&
                grandchild.ProgramFrameId == weishu.Id && grandchild.ParentInvocationId == benefit.InvocationId);
            var pending = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == weishu.Id).OutsidePhaseDrawDiscard!;
            var paidProducer = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == producer.Id);
            s.Need(pending is { Stage: OutsidePhaseDrawDiscardStage.MovementChildren, MovementIssued: true, ParticipantSeat: 1, ActualCount: 1 } &&
                invoice is { OwnerSeat: 1, Requested: 1, ActualCount: 1, TurnNumber: 0 } && invoice.DirectProducer == original.Source &&
                invoice.GameStartingOrigin!.OutsideDrawChild!.Source == original.Source &&
                invoice.GameStartingOrigin.OutsideDrawChild.Kind == GameStartingDrawChildKind.OutsidePhaseDraw &&
                invoice.GameStartingOrigin.ProgramFrameId == rootDraw.GameStartingOrigin!.ProgramFrameId &&
                (zhiyi ? paidProducer.TangTang is { Paid: true, Step: 1, Stage: TangTangStage.DrawChildren, Recipient: 1, ActualCount: 1 }
                    : paidProducer.NativeDraw is { InstructionIndex: 2, RequestedCount: 1, RecipientApplied: true, ReturnKind: NativeDrawAttemptReturnKind.ProgramInstruction, Operation: null }),
                "Native NPC Weishu actually selects itself and pays one Draw1 before its real CardsGained child; both the original producer's typed paid cursor and the separate descendant invoice remain on their owning frames.");
            s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision)); s.Play();
            s.Need(s.Events<NativeDrawInvocationRecordedEvent>().Count(e => e.InvocationId == benefit.InvocationId) == 1 &&
                s.Events<NativeDrawInvocationRecordedEvent>().Count(e => e.InvocationId == invoice.InvocationId) == 1 &&
                s.Events<OutsidePhaseDrawDiscardStartedEvent>().Count(e => e.FrameId == weishu.Id) == 1 &&
                s.Events<OutsidePhaseDrawDiscardCompletedEvent>().Count(e => e.FrameId == weishu.Id && e.ActualCount == 1) == 1 &&
                s.G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(1) && m.Reason.Value == reason) == 1 &&
                s.G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(1) &&
                    m.Reason.Value == "skill-program.ol:weishu.outside-phase-draw") == 1 &&
                s.V(1).HandCount == 2 && s.V(0).HandCount == (zhiyi ? 3 : 2),
                "Both genuine one-card benefits and their physical entity movements complete exactly once after all gain, selection and typed-return cold boundaries.");
            if (zhiyi)
                s.Need(s.Events<TangTangStartedEvent>().Count(e => e.FrameId == producer.Id && e.Source == source) == 1 &&
                    s.Events<ZhiyiOpportunityPaidEvent>().Count(e => e.FrameId == producer.Id) == 1 &&
                    s.Events<ZhiyiRecipientChosenEvent>().Count(e => e.FrameId == producer.Id && e.RecipientSeat == 1) == 1 &&
                    s.Events<TangTangMovementIssuedEvent>().Count(e => e.FrameId == producer.Id && e.Step == 1 && e.Stage == TangTangStage.DrawChildren && e.Count == 1) == 1 &&
                    s.Events<TangTangCompletedEvent>().Count(e => e.FrameId == producer.Id && e.Applied) == 1 &&
                    s.V(0).Hand.Any(c => c.Id == oldCard!.Id && c.Suit == Suit.Club),
                    "Zhiyi retains its original witness entity and exactly one native started, opportunity, recipient, issued Draw1 and completion receipt; the borrowed startup context never resets its real usage.");
            else
                s.Need(s.Events<ProgramBindingStartedEvent>().Count(e => e.FrameId == producer.Id && e.SkillId == GainInstruction) == 1,
                    "The generic CardsGained Draw instruction is issued once and its own benefit does not create a second activation.");
            s.Conserved(); s.Cold();
        }
    }

    private static void NativeFudaoGiftThenHongdeAndWeishuKeepMovementOriginCold()
    {
        var s = Scenario.Create("fudao-gift-weishu");
        s.Need(Enumerable.Range(0, 4).All(seat => s.V(seat).HandCount == 0),
            "The gift-origin fixture starts with genuine zero ordinary initial hands; the original Fudao must first draw its real three cards.");
        s.StartGame();
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.DingShangWan is
            { Source.OwnerSeat: 0, Stage: DingShangWanStage.OpeningGiftChoice }));
        var fudao = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.DingShangWan is not null);
        var opening = fudao.DingShangWan!;
        var gifted = s.V(0).Hand.Take(2).ToArray();
        s.Need(s.V(0).GeneralId == StandardShanheBossPackage.FenghuangId && s.V(2).GeneralId == "ol:bu-zhi" &&
            opening is { Operation: SkillProgramEffectOp.InitializeFudaoHandRecord, Window: SkillProgramTriggerWindow.GameStarting, Turn: 0 } &&
            s.V(0).HandCount == 3 && TeamDraws(s).Length == 0 && s.V(2).TeamId == s.V(3).TeamId && s.V(2).TeamId != s.V(0).TeamId,
            "The original Fenghuang Fudao owns the real startup gift choice; the original Bu Zhi has exactly one public teammate, the other native Weishu holder.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "recipient" && c.Targets.SequenceEqual([2]));
        foreach (var card in gifted) s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick" && c.Cards.SequenceEqual([card.Id]));
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "finish");
        s.Reach(() => s.P is { PlayerSeat: 2 } && s.P.SkillPrompt?.SkillId == "ol:hongde");
        var hongde = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.BuZhi is not null);
        var batch = hongde.WindowContext!.MovementBatch!;
        var movementRoot = batch.GameStartingMovementOrigin!;
        var giftReceipt = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == fudao.Id).DingShangWan!;
        s.Need(hongde.OwnerSeat == 2 && hongde.SkillId == "ol:hongde" && hongde.BuZhi!.Stage == "beneficiary" &&
            s.P!.Choices.Where(choice => choice.Parameters.GetValueOrDefault("branch") == "benefit")
                .SelectMany(choice => choice.Targets).Order().SequenceEqual([0, 1, 3]) &&
            batch.NativeDrawInvocation is null && batch.Movements.Count == 2 &&
            batch.Movements.Select(m => m.CardId).SequenceEqual(gifted.Select(c => c.Id)) &&
            batch.Movements.All(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(2) &&
                m.Reason.Value == "skill-program.ding-shang-wan.fudao.give") &&
            movementRoot.ProgramFrameId == fudao.Id && movementRoot.WindowFrameId == opening.WindowId &&
            movementRoot.InstructionIndex == 1 && movementRoot.GameplayHash == opening.GameplayHash && movementRoot.Source == opening.Source &&
            movementRoot.Operation == SkillProgramEffectOp.InitializeFudaoHandRecord && movementRoot.BatchId == batch.Id && movementRoot.RecipientSeat == 2 &&
            movementRoot.Kind == GameStartingNativeMovementKind.OpeningHandGift && movementRoot.ActualTurnOwnerSeat == 0 &&
            giftReceipt is { Stage: DingShangWanStage.OpeningGiftChildren, Payer: 0, Recipient: 2, ActualCount: 2 } &&
            giftReceipt.Selected.SequenceEqual(gifted.Select(c => c.Id)) &&
            movementRoot.SequenceBefore == giftReceipt.Before && movementRoot.SequenceAfter == giftReceipt.After &&
            movementRoot.Materials.SequenceEqual(batch.Movements) && movementRoot.Materials is System.Collections.IList { IsReadOnly: true } &&
            s.Events<GameStartingNativeMovementRecordedEvent>().Count(e => e.Origin.ProgramFrameId == fudao.Id &&
                e.Origin.BatchId == batch.Id && e.Origin.Materials.SequenceEqual(batch.Movements)) == 1 &&
            !s.Events<NativeDrawInvocationRecordedEvent>().Any(e => e.LastBatchId == batch.Id),
            "The exact two original Hand-to-Hand gifts reach Hongde as one issued immutable startup movement root; no draw invoice is fabricated for the gift batch.");
        s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision));
        if (s.Events<BuZhiBranchSelectedEvent>().Count(e => e.FrameId == hongde.Id && e.ChooserSeat == 2 && e.Branch == "benefit") != 1 ||
            s.Events<BuZhiMovementPaidEvent>().Count(e => e.FrameId == hongde.Id && e.Target == 3 && e.Actual == 1) != 1)
            s.Need(false, "The genuine native Hongde beneficiary must be its sole public teammate before expecting that owner's Weishu choice: " +
            JsonSerializer.Serialize(new
            {
                Seats = Enumerable.Range(0, 4).Select(seat => new { Seat = seat, s.V(seat).GeneralId, s.V(seat).TeamId, s.V(seat).HandCount }),
                Gifts = s.G.CardMovements.Where(m => m.Reason.Value == "skill-program.ding-shang-wan.fudao.give"),
                Roots = s.Events<GameStartingNativeMovementRecordedEvent>(),
                Hongde = s.Events<BuZhiStartedEvent>(), Choices = s.Events<BuZhiBranchSelectedEvent>(), Paid = s.Events<BuZhiMovementPaidEvent>(),
                Draws = s.Events<NativeDrawInvocationRecordedEvent>(), Weishu = s.Events<OutsidePhaseDrawDiscardStartedEvent>(),
                Prompt = s.P, Frames = s.G.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType()))
            }));
        s.Reach(() => s.P is { PlayerSeat: 3 } && s.P.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("program-action") == "outside-phase-draw-discard"));
        var weishu = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OutsidePhaseDrawDiscard is not null);
        var original = weishu.OutsidePhaseDrawDiscard!;
        var benefit = original.OriginalDraw!;
        var origin = benefit.GameStartingMovementOrigin!;
        var child = origin.DrawChild;
        s.Need(weishu.OwnerSeat == 3 && weishu.SkillId == Weishu && benefit is
            { OwnerSeat: 3, RequestedCount: 1, ActualCount: 1, ActualTurnNumber: 0, DirectProducer: null, GameStartingOrigin: null } &&
            benefit.Reason.Value == "program.bu-zhi.draw" && child.Kind == GameStartingDrawChildKind.NativeGainBenefit &&
            child.Source == hongde.BuZhi!.Source && child.ProgramFrameId == hongde.Id && child.ParentInvocationId == 0 &&
            child.InstructionIndex == 1 && child.RequestedCount == 1 && child.ParticipantSeat == 3 &&
            child.OriginalBatchId == batch.Id && child.WindowFrameId == hongde.WindowContext!.ParentFrameId &&
            origin.MovementRoot.ProgramFrameId == fudao.Id && origin.MovementRoot.BatchId == batch.Id &&
            origin.MovementRoot.Materials.SequenceEqual(batch.Movements) &&
            s.Events<BuZhiMovementPaidEvent>().Count(e => e.FrameId == hongde.Id && e.Target == 3 && e.Actual == 1) == 1,
            "Native Hongde AI actually selects its sole public teammate and draws one card; the invoice descends from the genuine gift movement, with no invented parent draw or Duichu origin.");
        s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision));
        s.Reach(() => s.P is { PlayerSeat: 3 } && s.P.SkillPrompt?.SkillId == WeishuWatch);
        var issued = s.Events<NativeDrawInvocationRecordedEvent>().Single(e => e.GameStartingMovementOrigin?.DrawChild is { } descendant &&
            descendant.ProgramFrameId == weishu.Id && descendant.ParentInvocationId == benefit.InvocationId);
        var pending = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == weishu.Id).OutsidePhaseDrawDiscard!;
        s.Need(pending is { Stage: OutsidePhaseDrawDiscardStage.MovementChildren, MovementIssued: true, ParticipantSeat: 3, ActualCount: 1 } &&
            issued is { OwnerSeat: 3, Requested: 1, ActualCount: 1, TurnNumber: 0, GameStartingOrigin: null } &&
            issued.DirectProducer == original.Source && issued.GameStartingMovementOrigin!.DrawChild.Source == original.Source &&
            issued.GameStartingMovementOrigin.DrawChild.Kind == GameStartingDrawChildKind.OutsidePhaseDraw &&
            issued.GameStartingMovementOrigin.MovementRoot.ProgramFrameId == fudao.Id &&
            issued.GameStartingMovementOrigin.MovementRoot.BatchId == batch.Id &&
            issued.GameStartingMovementOrigin.MovementRoot.Materials.SequenceEqual(batch.Movements),
            "The native Weishu target choice pays its real self Draw1 before a real gained-card child; the descendant links the actual Hongde draw ID while preserving the distinct immutable gift root.");
        s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision));
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == fudao.Id &&
            f.DingShangWan is { Stage: DingShangWanStage.OpeningDiscardChoice }));
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "finish"); s.Play();
        s.Need(s.Events<DingShangWanStartedEvent>().Count(e => e.FrameId == fudao.Id) == 1 &&
            s.Events<DingShangWanMovementIssuedEvent>().Count(e => e.FrameId == fudao.Id && e.Stage == DingShangWanStage.OpeningDraw && e.Count == 3) == 1 &&
            s.Events<DingShangWanMovementIssuedEvent>().Count(e => e.FrameId == fudao.Id && e.Stage == DingShangWanStage.OpeningGiftChildren && e.Count == 2) == 1 &&
            s.Events<FudaoHandRecordedEvent>().Single(e => e.FrameId == fudao.Id).HandCount == 1 &&
            s.Events<DingShangWanCompletedEvent>().Count(e => e.FrameId == fudao.Id) == 1 &&
            s.Events<BuZhiStartedEvent>().Count(e => e.FrameId == hongde.Id) == 1 && s.Events<BuZhiMovementPaidEvent>().Count(e => e.FrameId == hongde.Id) == 1 &&
            s.Events<OutsidePhaseDrawDiscardStartedEvent>().Count(e => e.FrameId == weishu.Id) == 1 &&
            s.Events<OutsidePhaseDrawDiscardCompletedEvent>().Count(e => e.FrameId == weishu.Id && e.ActualCount == 1) == 1 &&
            s.Events<NativeDrawInvocationRecordedEvent>().Count(e => e.InvocationId == benefit.InvocationId) == 1 &&
            s.Events<NativeDrawInvocationRecordedEvent>().Count(e => e.InvocationId == issued.InvocationId) == 1 &&
            s.G.CardMovements.Count(m => m.Reason.Value == "program.bu-zhi.draw" && m.To == CardLocation.Hand(3)) == 1 &&
            s.G.CardMovements.Count(m => m.Reason.Value == "skill-program.ol:weishu.outside-phase-draw" && m.To == CardLocation.Hand(3)) == 1 &&
            s.V(0).HandCount == 1 && s.V(2).HandCount == 2 && s.V(3).HandCount == 2 &&
            gifted.All(card => s.V(2).Hand.Any(current => current.Id == card.Id && current.Kind == card.Kind && current.Suit == card.Suit && current.Rank == card.Rank)),
            "The original Fudao draw3, two-card gift, optional discard and record1, native Hongde benefit, and Weishu child all finish exactly once through cold replay, retaining every originally gifted entity and paid descendant.");
        s.Conserved(); s.Cold();
    }

    private static void NativeFudaoOpeningDrawThenWeishuKeepsOperationOriginCold()
    {
        var s = Scenario.Create("fudao-draw-weishu");
        s.Need(Enumerable.Range(0, 4).All(seat => s.V(seat).HandCount == 0),
            "The original Fudao Draw3 starts after genuine zero ordinary initial hands, with no fixed-face or constructor hand entities.");
        s.Cold(); s.StartGame();
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.SkillPrompt?.SkillId == Weishu);
        var fudao = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.DingShangWan is not null);
        var weishu = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.OutsidePhaseDrawDiscard is not null);
        var original = weishu.OutsidePhaseDrawDiscard!;
        var draw = original.OriginalDraw!;
        var root = draw.GameStartingOrigin!;
        s.Need(s.V(0).GeneralId == StandardShanheBossPackage.FenghuangId && s.V(0).HandCount == 3 &&
            draw is { OwnerSeat: 0, RequestedCount: 3, ActualCount: 3, ActualTurnNumber: 0, DirectProducer: null, GameStartingMovementOrigin: null } &&
            root.Source == fudao.DingShangWan!.Source && root.Source.SkillId == "ol:fudao" && root.Source.OwnerSeat == 0 &&
            root.ProgramFrameId == fudao.Id && root.InstructionIndex == 1 && root.OutsideDrawChild is null &&
            root.ReturnKind == NativeDrawAttemptReturnKind.ProgramOperation && root.Operation == SkillProgramEffectOp.InitializeFudaoHandRecord &&
            fudao.DingShangWan is { Stage: DingShangWanStage.OpeningDraw, ActualCount: 3, Payer: 0 } &&
            original is { Stage: OutsidePhaseDrawDiscardStage.Choosing, Source.SkillId: Weishu } &&
            s.Events<DingShangWanMovementIssuedEvent>().Count(e => e.FrameId == fudao.Id && e.Stage == DingShangWanStage.OpeningDraw && e.Count == 3) == 1,
            "The original startup Fudao operation issues one real Draw3 invoice and its exact native movement return before the original Weishu choice; no initial-size scalar or gift movement stands in for that root.");
        s.Cold(); s.Answer(choice => choice.Targets.SequenceEqual([0]));
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.SkillPrompt?.SkillId == WeishuWatch);
        var child = s.Events<NativeDrawInvocationRecordedEvent>().Single(e => e.GameStartingOrigin?.OutsideDrawChild is { } derived &&
            derived.ProgramFrameId == weishu.Id && derived.ParentInvocationId == draw.InvocationId);
        var pending = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.Id == weishu.Id).OutsidePhaseDrawDiscard!;
        s.Need(child is { OwnerSeat: 0, Requested: 1, ActualCount: 1, TurnNumber: 0, GameStartingMovementOrigin: null } &&
            child.DirectProducer == original.Source && child.GameStartingOrigin!.ProgramFrameId == fudao.Id &&
            child.GameStartingOrigin.Source == root.Source && child.GameStartingOrigin.ReturnKind == NativeDrawAttemptReturnKind.ProgramOperation &&
            child.GameStartingOrigin.Operation == SkillProgramEffectOp.InitializeFudaoHandRecord &&
            child.GameStartingOrigin.OutsideDrawChild is { Kind: GameStartingDrawChildKind.OutsidePhaseDraw, RequestedCount: 1, ParticipantSeat: 0 } &&
            child.GameStartingOrigin.OutsideDrawChild.Source == original.Source &&
            pending is { Stage: OutsidePhaseDrawDiscardStage.MovementChildren, MovementIssued: true, ParticipantSeat: 0, ActualCount: 1 } &&
            s.V(0).HandCount == 4,
            "The actual self-targeted Weishu Draw1 owns a separate native child invoice and paid movement while preserving the Fudao operation's Draw3 root through the real CardsGained child.");
        s.Cold(); s.Answer(choice => choice.Parameters.GetValueOrDefault("option-id") == "continue");
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.Id == fudao.Id &&
            frame.DingShangWan is { Stage: DingShangWanStage.OpeningGiftChoice }));
        s.Cold(); s.Answer(choice => choice.Parameters.GetValueOrDefault("branch") == "finish");
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.Id == fudao.Id &&
            frame.DingShangWan is { Stage: DingShangWanStage.OpeningDiscardChoice }));
        s.Cold(); s.Answer(choice => choice.Parameters.GetValueOrDefault("branch") == "finish"); s.Play();
        s.Need(s.Events<NativeDrawInvocationRecordedEvent>().Count(e => e.InvocationId == draw.InvocationId && e.Requested == 3 && e.ActualCount == 3) == 1 &&
            s.Events<NativeDrawInvocationRecordedEvent>().Count(e => e.InvocationId == child.InvocationId) == 1 &&
            s.Events<OutsidePhaseDrawDiscardStartedEvent>().Count(e => e.Source.SkillId == Weishu && e.Source.OwnerSeat == 0) == 1 &&
            s.Events<OutsidePhaseDrawDiscardCompletedEvent>().Count(e => e.FrameId == weishu.Id && e.ActualCount == 1) == 1 &&
            s.Events<DingShangWanStartedEvent>().Count(e => e.FrameId == fudao.Id) == 1 &&
            s.Events<DingShangWanCompletedEvent>().Count(e => e.FrameId == fudao.Id) == 1 &&
            s.Events<FudaoHandRecordedEvent>().Single(e => e.FrameId == fudao.Id).HandCount == 4 && s.V(0).HandCount == 4 &&
            s.G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) &&
                m.Reason.Value == "skill-program.ding-shang-wan.draw") == 3 &&
            s.G.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) &&
                m.Reason.Value == "skill-program.ol:weishu.outside-phase-draw") == 1 &&
            !s.G.CardMovements.Any(m => m.From == CardLocation.OutsideGame && m.To == CardLocation.Hand(0)) &&
            s.Events<GameStartingNativeMovementRecordedEvent>().Length == 0,
            "Fudao's actual Draw3 and Weishu's actual Draw1 pay once without self-recursion; zero gift/discard records the genuine four-card hand exactly once after all original typed child returns.");
        s.Conserved(); s.Cold();
    }

    public static void TeamOpeningUsesRealDrawBatchesWithNativeHongdeCold()
    {
        var s = Scenario.Create("hongde");
        s.Need(Enumerable.Range(0, 4).All(seat => s.V(seat).HandCount == 0) && TeamDraws(s).Length == 0,
            "Team Opening is absent from constructor-time initial-hand sizing; all four ordinary bases are zero in this small fixture.");
        s.Cold(); s.StartGame();
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "bu-zhi"));
        var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.BuZhi is not null);
        var batch = root.WindowContext!.MovementBatch!;
        s.Need(s.V(0).GeneralId == "ol:bu-zhi" && root.SkillId == "ol:hongde" && root.BuZhi!.Stage == "beneficiary" &&
            batch.NativeDrawInvocation is { ActualCount: 2 } && batch.Movements.All(m => m.To == CardLocation.Hand(0)) &&
            TeamDraws(s).Count(m => m.To == CardLocation.Hand(0)) == 2 &&
            s.Events<BuZhiStartedEvent>().Count(e => e.Source.SkillId == "ol:hongde") == 1,
            "A teammate's real Draw2 reaches the original OL Bu Zhi Hongde native gain window once, as a single two-card invocation rather than an initial-size scalar or two Draw1 calls.");
        var frame = root.Id;
        s.Cold(); s.Answer(c => c.Targets.SequenceEqual([1])); s.Play();
        s.Need(TeamDraws(s).Count(m => m.To == CardLocation.Hand(0)) == 2 &&
            TeamDraws(s).Count(m => m.To == CardLocation.Hand(2)) == 2 &&
            !TeamDraws(s).Any(m => m.To == CardLocation.Hand(1) || m.To == CardLocation.Hand(3)) &&
            s.Events<BuZhiStartedEvent>().Count(e => e.Source.SkillId == "ol:hongde") == 1 &&
            s.Events<BuZhiMovementPaidEvent>().Count(e => e.FrameId == frame) == 1 &&
            s.G.CardMovements.Count(m => m.Reason.Value == "program.bu-zhi.draw" && m.To == CardLocation.Hand(1)) == 1,
            "Hongde pays its chosen one-card benefit once; Team Opening draws only for the holder's other two public allies, excluding its owner and the enemy.");
        s.Cold(); s.Conserved();

        var unknown = Scenario.Create("hongde", publicTeams: false); unknown.StartGame(); unknown.Play();
        unknown.Need(Enumerable.Range(0, 4).All(seat => unknown.V(seat).TeamId is null) && TeamDraws(unknown).Length == 0 &&
            unknown.Events<BuZhiStartedEvent>().Length == 0,
            "A registered, living opening holder in an identity game has no public teammate targets and produces neither bonus cards nor fabricated Hongde gains.");
        unknown.Cold();
        TeamOpeningRunsOriginalWeishuChildExactlyOnceCold();
        TeamOpeningHongdeThenOtherOwnersWeishuKeepsExactInvoicesCold();
        TeamOpeningGainProducersThenWeishuKeepExactInvoicesCold();
        NativeFudaoGiftThenHongdeAndWeishuKeepMovementOriginCold();
        NativeFudaoOpeningDrawThenWeishuKeepsOperationOriginCold();
    }

    public static void FixedFenghuangOpeningKeepsFacesBeforeTeamDrawAndNativeFudaoCold()
    {
        foreach (var copies in new[] { 1, 2 })
        {
            var s = Scenario.Create("fenghuang", copies: copies);
            var fixedCards = s.V(1).Hand.ToArray();
            s.Need(fixedCards.Select(c => new ShanheStartingCard(c.Kind, c.Suit, c.Rank)).SequenceEqual(FenghuangFaces) &&
                s.G.CardMovements.Count(m => m.From == CardLocation.OutsideGame && m.To == CardLocation.Hand(1)) == 4 &&
                !s.G.CardMovements.Any(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(1)) &&
                TeamDraws(s).Length == 0, "The actual fixed four faces are placed first, with no constructor-time Team Opening cards.");
            s.Cold(); s.StartGame();
            // StartGame may natively finish every NPC-owned opening choice in
            // one command. The original Fudao completion fact is the boundary.
            s.Reach(() => s.Events<FudaoHandRecordedEvent>().Any(e => e.Source.OwnerSeat == 1));
            s.Need(s.V(1).Skills?.Any(skill => skill.ContentId == "ol:fudao") == true &&
                fixedCards.All(original => s.V(1).Hand.Where(card => card.Id == original.Id)
                    .All(card => card.Kind == original.Kind && card.Suit == original.Suit && card.Rank == original.Rank)) &&
                TeamDraws(s).Count(m => m.To == CardLocation.Hand(1)) == copies * 2 &&
                TeamDraws(s).Count(m => m.To == CardLocation.Hand(2)) == copies * 2 &&
                !TeamDraws(s).Any(m => m.To == CardLocation.Hand(0) || m.To == CardLocation.Hand(3)) &&
                s.G.CardMovements.Count(m => m.To == CardLocation.Hand(1) && m.Reason.Value == "skill-program.ding-shang-wan.draw") == 3,
                "Native game-start ordering executes each real Team Draw2 and the original Fudao Draw3 after the fixed four; an NPC's actual gift/discard choices may finish inside StartGame.");
            var record = s.Events<FudaoHandRecordedEvent>().Single(e => e.Source.OwnerSeat == 1);
            var cutoff = s.Events<DingShangWanMovementIssuedEvent>().Where(e => e.FrameId == record.FrameId).Max(e => e.After);
            var recordedHandFlow = s.G.CardMovements.Where(m => m.Sequence <= cutoff)
                .Sum(m => (m.To == CardLocation.Hand(1) ? 1 : 0) - (m.From == CardLocation.Hand(1) ? 1 : 0));
            s.Need(record.HandCount == recordedHandFlow &&
                s.Events<ProgramBindingStartedEvent>().Where(e => e.SkillId == Duichu)
                    .Select(e => e.SkillInstanceId).Distinct(StringComparer.Ordinal).Count() == copies &&
                fixedCards.All(card => s.G.CreateCardZoneDiagnostics().Count(zone => zone.CardId == card.Id && zone.CardKind == card.Kind) == 1),
                "The original Fudao gift/discard choices run through native AI and record the actual remaining hand; every fixed entity still occupies exactly one genuine zone.");
            s.Conserved(); s.Cold();
        }
        HumanFenghuangKeepsNativeOpeningChoiceChildrenCold();
    }

    private static void HumanFenghuangKeepsNativeOpeningChoiceChildrenCold()
    {
        var s = Scenario.Create("human-fenghuang");
        var fixedCards = s.V(0).Hand.ToArray();
        s.Need(fixedCards.Select(c => new ShanheStartingCard(c.Kind, c.Suit, c.Rank)).SequenceEqual(FenghuangFaces),
            "The human-controlled original Fenghuang starts with four genuine configured faces and a zero random base.");
        s.StartGame();
        s.Reach(() => s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.DingShangWan is
            { Source.OwnerSeat: 0, Stage: DingShangWanStage.OpeningGiftChoice }));
        var before = TeamDraws(s).Count(m => m.To == CardLocation.Hand(0));
        s.Need(s.V(0).GeneralId == StandardShanheBossPackage.FenghuangId && s.V(0).HandCount == 7 + before &&
            fixedCards.All(original => s.V(0).Hand.Any(card => card.Id == original.Id && card.Kind == original.Kind &&
                card.Suit == original.Suit && card.Rank == original.Rank)),
            "The original Fudao's actual three-card draw preserves all four faces before a human-owned native gift choice, without assigning a new opening priority.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "finish");
        s.Reach(() => s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.DingShangWan is
            { Source.OwnerSeat: 0, Stage: DingShangWanStage.OpeningDiscardChoice }));
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "finish");
        s.Reach(() => s.Events<FudaoHandRecordedEvent>().Any(e => e.Source.OwnerSeat == 0)); s.Play();
        s.Need(s.Events<FudaoHandRecordedEvent>().Single(e => e.Source.OwnerSeat == 0).HandCount == 7 + before &&
            TeamDraws(s).Count(m => m.To == CardLocation.Hand(0)) == 2 &&
            s.G.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.ding-shang-wan.draw") == 3 &&
            s.V(0).HandCount == 9 && fixedCards.All(original => s.V(0).Hand.Any(card => card.Id == original.Id &&
                card.Kind == original.Kind && card.Suit == original.Suit && card.Rank == original.Rank)),
            "Both genuine Fudao choice children replay without repeating draw, gift or discard; other original game-start candidates then contribute one Team Draw2 in their native order.");
        s.Conserved(); s.Cold();
    }

    private static CardMovementRecord[] TeamDraws(Scenario s) => s.G.CardMovements.Where(m =>
        m.From == CardLocation.DrawPile && m.Reason.Value == "skill-program." + Duichu + ".Draw").ToArray();

    private sealed class Scenario(GameEngine game, ContentRegistry registry, bool interactive, string player)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Need(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }
        public void Accept(GameCommand command)
        { var result = G.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Team-tactic command rejected."); }
        public void Answer(Func<PromptChoice, bool> choose)
        { var p = P!; Need(p.PlayerSeat == 0, "NPC choices must use native AI commands."); Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.First(choose).Id, G.Revision)); }
        public void StartGame()
        {
            Accept(new StartGameCommand());
            if (!interactive) return;
            Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            Accept(new SelectGeneralCommand(0, player, G.Revision, P!.PromptId));
        }
        public bool CanSlash() => G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([3]));
        public int Debits() => Events<CardUseDebitRecordedEvent>().Count(e => e.Debit.ActorSeat == 0 && e.Debit.Query == SkillRuleQuery.SlashLimit);
        public void Slash(bool converted = false)
        {
            var a = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([3]) &&
                (converted ? a.ConversionSource?.SkillId == "classic:wusheng" && V(0).Hand.Any(c => c.Id == a.CardId && c.Kind == CardKind.Dodge) : a.ConversionSource is null));
            Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)
            { ConversionSource = a.ConversionSource }); Play();
        }
        public void Use(string activation, int[]? targets = null)
        { Accept(new UseProgramSkillCommand(0, Driver, activation, [], targets ?? [], G.Revision, P!.PromptId)); Play(); }
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Reach(Func<bool> ready)
        {
            for (var step = 0; step < 220; step++)
            {
                if (ready()) return;
                Need(G.State.Winner == Winner.None, "Team-tactic fixture ended before its actual boundary.");
                if (P is { PlayerSeat: 0 } human)
                {
                    var choice = human.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("response") is "take-damage" or "pass" or "let-die" ||
                        c.Parameters.GetValueOrDefault("program-action") == "skip" || c.Parameters.GetValueOrDefault("branch") == "decline" ||
                        human.SkillPrompt?.SkillId == "ol:fudao" &&
                        c.Parameters.GetValueOrDefault("program-action") == "ding-shang-wan" &&
                        c.Parameters.GetValueOrDefault("branch") == "finish");
                    Need(choice is not null, "Unexpected native human team-tactic boundary: " + JsonSerializer.Serialize(human));
                    Answer(c => c.Id == choice!.Id);
                }
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded team-tactic boundary: " + JsonSerializer.Serialize(P));
        }
        public void Conserved()
        { var zones = G.CreateCardZoneDiagnostics(); Need(zones.Select(c => c.CardId).Distinct().Count() == zones.Count, "Every actual physical card has exactly one zone."); }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), G.CardMovements,
            Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold replay retains native debits, queued gain choices, physical faces and paid child movements exactly once."); }
        public static Scenario Create(string scene, int copies = 1, bool publicTeams = true)
        {
            var aura = scene.StartsWith("aura-", StringComparison.Ordinal);
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardShanheBossPackage(), new Fixture(scene));
            var hongde = scene is "hongde" or "hongde-weishu";
            var player = hongde ? "ol:bu-zhi" : scene == "zhiyi-weishu" ? "ol:tang-tang" :
                scene is "human-fenghuang" or "fudao-gift-weishu" or "fudao-draw-weishu" ? StandardShanheBossPackage.FenghuangId : Owner;
            GameOptions options;
            if (publicTeams)
            {
                var allies = scene switch
                {
                    "fenghuang" => new ShanheCombatantDefinition[] { new(StandardShanheBossPackage.FenghuangId) { StartingHand = FenghuangFaces }, new(Ally) },
                    "hongde" or "hongde-weishu" or "weishu" or "compiled-gain-weishu" or "zhiyi-weishu" => [new(Giver), new(Ally)],
                    "human-fenghuang" => [new(Giver), new(Ally)],
                    "fudao-gift-weishu" => [new(Ally)],
                    "fudao-draw-weishu" => [new(Ally), new(Other)],
                    _ => [new(Ally, Hp: aura && scene != "aura-stacked" ? 1 : null), new(Other, Hp: aura ? null : 1)]
                };
                ShanheStartingCard[] opening = scene == "quota" ? Enumerable.Range(0, 6).Select(i => new ShanheStartingCard(CardKind.Slash, Suit.Spade, i + 1))
                    .Append(new(CardKind.Dodge, Suit.Heart, 5)).ToArray() : scene == "human-fenghuang" ? FenghuangFaces :
                    scene == "zhiyi-weishu" ? [new(CardKind.Slash, Suit.Club, 9)] : [];
                ShanheCombatantDefinition[] enemies = scene == "fudao-gift-weishu" ? [new("ol:bu-zhi"), new(Enemy)] : [new(Enemy, MaxHpBonus: 20)];
                var setup = new ShanheBattleSetup(23, 1, "fixture-team-" + scene, player, hongde || scene == "zhiyi-weishu" ? 3 :
                    scene is "human-fenghuang" or "fudao-gift-weishu" or "fudao-draw-weishu" ? 4 : 12,
                    0, 0, 0, 0, hongde ? [Driver] : scene == "fudao-draw-weishu" ? [Weishu, WeishuWatch] : [], opening, [], enemies, allies, new string('d', 64))
                { TacticSkillIds = scene == "fenghuang" && copies == 2 ? [Duichu] : scene == "aura-stacked" ? [Duipai, Duipai] : [] };
                options = new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                    HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                    UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 };
            }
            else options = new GameOptions { Seed = 23, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                ModeId = IdentityMode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 8 };
            return new(GameEngine.CreateStandard(options, registry), registry, !publicTeams, player);
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-team-tactics", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var aura = scene.StartsWith("aura-", StringComparison.Ordinal);
            static string Resource(string suffix)
            {
                var assembly = typeof(StandardContentPackage).Assembly;
                using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + suffix, StringComparison.Ordinal)))!;
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            var production = SkillProgramCatalog.Load(Resource("shanhe-team-2026.rules.json"), Resource("shanhe-team-2026.presentation.json"));
            foreach (var (id, program) in production.Programs)
                b.AddSkill(new(id, production.Presentations[id].Name, production.Presentations[id].Description)
                { Program = program, ProgramPresentation = production.Presentations[id] });
            if (aura) b.AddSkill(ActualDuipai.Value);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"draw-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":1}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                {"id":"lose-jiban","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Jiban}}"],"sourceBind":"standard:none"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{WeishuWatch}}","revision":1,"triggers":[{"id":"real-weishu-startup-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.ol:weishu.outside-phase-draw"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"resumed","options":[{"id":"continue"}]}]}]},
              {"id":"{{GainInstruction}}","revision":1,"triggers":[{"id":"startup-gain-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.{{Duichu}}.Draw"],"optional":false,"usageScope":"game","usageLimit":1,"condition":{"kind":"compare","left":{"kind":"destinationZoneCountAfter"},"operator":"equal","right":{"kind":"integerConstant","value":2} },"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"draw","target":"selectedTarget","amount":1}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet, WeishuWatch, GainInstruction }.ToDictionary(id => id, id => new
                {
                    name = "真实队友战法检查", description = "原生技能失去、伤害及独立队伍",
                    optionLabels = id == WeishuWatch ? new Dictionary<string, string> { ["continue"] = "继续卫戍真实摸牌子流程" } : []
                })
            }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, id, id) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            if (aura) RegisterAuraBoundaries(b, scene);
            b.AddGeneral(new(Owner, "队友战法持有者", "", Driver, "wu", 12,
                aura ? scene == "aura-unknown" ? [Duipai] : scene == "aura-stacked" ? [] : [AuraBoundary, AuraGain] :
                scene == "quota" ? [Jiban, "classic:wusheng"] : scene == "weishu" ? [Weishu, WeishuWatch] : scene == "compiled-gain-weishu" ? [GainInstruction] : [Duichu]));
            b.AddGeneral(new(Giver, "队初持有者", "", Duichu, "wu", 12,
                scene is "hongde-weishu" or "compiled-gain-weishu" or "zhiyi-weishu" ? [Weishu, WeishuWatch] : []));
            foreach (var id in new[] { Ally, Other, Enemy })
                b.AddGeneral(new(id, "同桌角色", "", Quiet, "wu", 12,
                    aura && scene is not "aura-stacked" and not "aura-unknown" && id == Ally
                        ? scene.EndsWith("loss", StringComparison.Ordinal) ? [Duipai, AuraLoss] : [Duipai] :
                    scene == "fudao-gift-weishu" && id == Enemy ? [Weishu, WeishuWatch] : []));
            foreach (var (id, initial) in new[] { (Deck, 0), (IdentityDeck, scene == "quota" ? 4 : 0) })
                b.AddDeck(new(id, "真实固定牌堆", initial, aura ? 2 : 0, [])
                { PhysicalCards = Enumerable.Range(0, 120).Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Club, i % 13 + 1)).ToArray() });
            string[] pool = scene switch
            {
                "fenghuang" => [Owner, StandardShanheBossPackage.FenghuangId, Ally, Enemy],
                "hongde" or "hongde-weishu" => ["ol:bu-zhi", Giver, Ally, Enemy],
                "weishu" or "compiled-gain-weishu" => [Owner, Giver, Ally, Enemy],
                "zhiyi-weishu" => ["ol:tang-tang", Giver, Ally, Enemy],
                "human-fenghuang" => [StandardShanheBossPackage.FenghuangId, Giver, Ally, Enemy],
                "fudao-gift-weishu" => [StandardShanheBossPackage.FenghuangId, Ally, "ol:bu-zhi", Enemy],
                "fudao-draw-weishu" => [StandardShanheBossPackage.FenghuangId, Ally, Other, Enemy],
                _ => [Owner, Ally, Other, Enemy]
            };
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "真实队友战法", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: pool, ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int>
                { [ShanheBattleSetup.PlayerTeamId] = scene == "fudao-gift-weishu" ? 2 : 3,
                    [ShanheBattleSetup.EnemyTeamId] = scene == "fudao-gift-weishu" ? 2 : 1 }));
            b.AddMode(new(IdentityMode, "未知公开队伍", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                IdentityDeck, GeneralCandidateCount: 4, GeneralPoolIds: pool));
        }

        private static void RegisterAuraBoundaries(IContentRegistryBuilder builder, string scene)
        {
            var window = scene.StartsWith("aura-issued-", StringComparison.Ordinal) ? "nativeDrawStarting" : "drawPhaseStarting";
            var actualRequested = scene.StartsWith("aura-issued-", StringComparison.Ordinal) ? 4 : 2;
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{AuraBoundary}}}","revision":1,"triggers":[{"id":"mutate-source-once","window":"{{{window}}}","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"proceed","options":[{"id":"proceed"}]},{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"damage","target":"selectedTarget","amount":1}]}]},
              {"id":"{{{AuraGain}}}","revision":1,"triggers":[{"id":"real-phase-materials","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["rule.draw"],"optional":false,"usageScope":"game","usageLimit":1,"condition":{"kind":"compare","left":{"kind":"destinationZoneCountAfter"},"operator":"equal","right":{"kind":"integerConstant","value":{{{actualRequested}}}}},"effects":[{"op":"chooseOption","target":"owner","resultBind":"resumed","options":[{"id":"continue"}]}]}]},
              {"id":"{{{AuraLoss}}}","revision":1,"triggers":[{"id":"real-dying-source-loss","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"recover","target":"owner","amount":1},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{{Duipai}}}"],"sourceBind":"standard:none"}]}]},
              {"id":"{{{AuraNativeCapability}}}","revision":1,"triggers":[{"id":"unowned-native-capability","window":"nativeDrawStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"unused","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { AuraBoundary, AuraGain, AuraLoss, AuraNativeCapability }.ToDictionary(id => id, id => new
                {
                    name = "真实队牌边界", description = "真实摸牌请求与伤害失技检查",
                    optionLabels = id == AuraBoundary ? new Dictionary<string, string> { ["proceed"] = "继续真实伤害" } :
                        id is AuraGain or AuraNativeCapability ? new Dictionary<string, string> { ["continue"] = "完成真实摸牌子流程" } : []
                })
            }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, id, id) { Program = program, ProgramPresentation = catalog.Presentations[id] });
        }
    }
}
