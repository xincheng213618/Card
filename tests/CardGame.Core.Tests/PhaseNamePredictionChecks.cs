using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class PhaseNamePredictionChecks
{
    private const string Prediction = "fixture:phase-name-prediction", Driver = "fixture:phase-name-driver";
    private const string Gain = "fixture:phase-name-gain", Hp = "fixture:phase-name-hp", Extra = "fixture:phase-name-extra";
    private const string Recovery = "fixture:phase-name-recovery";
    private const string Early = "fixture:phase-name-early", EndingExtra = "fixture:phase-name-ending-extra";
    private const string Mode = "identity:phase-name-prediction";
    private const string EquipmentMode = "identity:classic-phase-name-equipment";
    private const string CostReason = "program.phase-name-prediction.cost", ClaimReason = "program.phase-name-prediction.claim";

    public static void SecretExactPhaseAndSettlement()
    {
        LoaderContract();
        VerifyNoUseDamageAndSourceLoss();
        foreach (var zone in new[] { "hand", "equipment", "discard" }) VerifyWrongGuessClaim(zone);
        VerifyVirtualCanonicalAndExtraPhase();
        VerifyResponseDoesNotCount();
        VerifyRoundCostShortageAndEmptyHand();
    }

    public static void StolenArmorNativeOwnerRecoveryAndCollateralReturn()
    {
        var (g, r) = Create(CardKind.SilverLion);
        var origin = ArmNext(ref g, r, false);
        var armed = F<PhaseNamePredictionArmedEvent>(g).Single(e => e.FrameId == origin.FrameId);
        Play(g);
        var equip = g.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.Equip &&
            a.CardId == armed.CardId && a.ConversionSource is null);
        SubmitPlay(g, equip);
        Play(g);
        var fullHp = V(g, 0).Hp;
        var sourceHp = V(g, origin.Source.OwnerSeat).Hp;
        Use(g, "lose-hp", []);
        Play(g);
        Require(fullHp == V(g, 0).MaxHp && V(g, 0).Hp == fullHp - 1 &&
            V(g, origin.Source.OwnerSeat).Hp == sourceHp &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == armed.CardId).Location == CardLocation.Equipment(0) &&
            F<PhaseNamePredictionUseObservedEvent>(g).Single(e => e.OriginalFrameId == origin.FrameId) is
                { ActorSeat: 0, NameKind: CardKind.SilverLion } &&
            F<RecoveryAppliedEvent>(g).Length == 0,
            "The publicly shown original SilverLion is genuinely equipped, then its actual holder pays one real HP before the claim; another source has not recovered.");
        End(g);
        Reach(g, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Recovery);
        var settled = F<PhaseNamePredictionSettledEvent>(g).Single(e => e.OriginalFrameId == origin.FrameId);
        var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == settled.FrameId);
        var hp = g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single(f =>
            f.Change.ParentFrameId == parent.Id && f.ResumeFrameId == parent.Id &&
            f.Continuation == PostEventContinuation.AwaitedProgramMovement);
        var observer = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Recovery);
        Require(origin.TargetSeat == 0 && origin.Source.OwnerSeat != 0 &&
            settled is { PredictedUse: false, ActualUse: true, Correct: false, TargetSeat: 0 } &&
            settled.CardId == armed.CardId && parent.PendingMovementContinuation is not null &&
            parent.PhaseNamePrediction is { Stage: PhaseNamePredictionStage.ClaimChildren, Selected: [var selected], Eligible: [var material] } paid &&
            selected == armed.CardId && material.CardId == armed.CardId && material.From == CardLocation.Equipment(0) &&
            paid.IssuedPolicy?.Origin == origin && paid.SequenceAfter > paid.SequenceBefore &&
            hp.Change is { Kind: HpChangeKind.Recovery, SourceSeat: 0, TargetSeat: 0, Amount: 1 } &&
            hp.Change.HpBefore == fullHp - 1 && hp.Change.HpAfter == fullHp &&
            observer.OwnerSeat == 0 && observer.WindowContext?.Window == SkillProgramTriggerWindow.AfterHpRecovered &&
            observer.WindowContext.ParentFrameId == hp.Id && observer.WindowContext.HpChange == hp.Change &&
            V(g, 0).Hp == fullHp && V(g, origin.Source.OwnerSeat).Hp == sourceHp &&
            F<RecoveryAppliedEvent>(g).Single() is { SourceSeat: 0, TargetSeat: 0, Amount: 1 } recovery && recovery.RemainingHp == fullHp &&
            F<SilverLionRemovedRecoveryEvent>(g).Single() is { PlayerSeat: 0, RecoveredAmount: 1 } lion &&
            lion.Reason.Value == ClaimReason && lion.RemainingHp == fullHp &&
            g.CardMovements.Count(m => m.CardId == armed.CardId && m.From == material.From &&
                m.To == CardLocation.Hand(origin.Source.OwnerSeat) && m.Reason.Value == ClaimReason &&
                m.Sequence > paid.SequenceBefore && m.Sequence <= paid.SequenceAfter) == 1 &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == armed.CardId).Location == CardLocation.Hand(origin.Source.OwnerSeat),
            "A wrong prediction claims the exact equipped entity once; its original armor holder owns the real recovery and private HP child under the still-awaiting claim, rather than the different skill source.");
        Private(g);
        g = Cold(g, r);
        Continue(g);
        Reach(g, p => p.PlayerSeat == origin.Source.OwnerSeat && p.SkillPrompt?.SkillId == Gain);
        Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == settled.FrameId).PhaseNamePrediction is
                { Stage: PhaseNamePredictionStage.ClaimChildren, Selected: [var claimed] } && claimed == armed.CardId &&
            F<RecoveryAppliedEvent>(g).Length == 1 && V(g, 0).Hp == fullHp,
            "Returning from the restored original holder's HP child resumes the same paid claim and reaches the actual recipient's independent gain child without another recovery.");
        Private(g);
        Continue(g);
        Until(g, e => !e.ResolutionStack.Any(f => f.Id == settled.FrameId));
        Require(F<PhaseNamePredictionSettledEvent>(g).Count(e => e.OriginalFrameId == origin.FrameId) == 1 &&
            F<RecoveryAppliedEvent>(g).Length == 1 && F<SilverLionRemovedRecoveryEvent>(g).Length == 1 &&
            V(g, 0).Hp == fullHp && V(g, origin.Source.OwnerSeat).Hp == sourceHp &&
            g.CardMovements.Count(m => m.CardId == armed.CardId && m.From == CardLocation.Equipment(0) &&
                m.To == CardLocation.Hand(origin.Source.OwnerSeat) && m.Reason.Value == ClaimReason) == 1 &&
            V(g, origin.Source.OwnerSeat).Hand.Any(c => c.Id == armed.CardId),
            "Both genuine collateral children return before the original resolver completes once; no repeated settlement, movement or armor recovery occurs.");
    }

    public static void FrozenEndingDeathEarlyNativeUsesAndInsertedPhaseBoundaries()
    {
        VerifyDeadTargetExpiresFrozenCandidates();
        foreach (var windows in new[] { true, false }) VerifyEarlyNativeUseBeforeArming(windows);
        VerifyEndingInsertionReturnsToOriginalPhase();
        VerifyOwnPhaseNullificationIsGenuineUse();
    }

    private static void VerifyDeadTargetExpiresFrozenCandidates()
    {
        var (g, r) = Create(humanRole: Role.Renegade, lowHp: true);
        Reach(g, p => IsOffer(p, null) && g.CreateSnapshot(0).CurrentSeat == 0);
        var first = ArmNext(ref g, r, false);
        var second = ArmNext(ref g, r, true);
        var secondCard = F<PhaseNamePredictionArmedEvent>(g).Single(e => e.FrameId == second.FrameId).CardId;
        Play(g);
        Use(g, "wound", []);
        Play(g);
        Require(V(g, 0).Hp == 1 && first.TargetSeat == 0 && second.TargetSeat == 0 &&
            first.Source.OwnerSeat != second.Source.OwnerSeat && first.ActualTurnNumber == second.ActualTurnNumber &&
            first.PhaseInstanceId == second.PhaseInstanceId &&
            !F<PhaseNamePredictionSettledEvent>(g).Any() && !F<PhaseNamePredictionExpiredEvent>(g).Any(),
            "Two distinct sources really arm the same non-Lord's actual Play before a native three-HP payment leaves its living target at one HP.");
        g = Cold(g, r);
        End(g);
        Until(g, e => !V(e, 0).IsAlive && F<PhaseNamePredictionExpiredEvent>(e).Any(x => x.OriginalFrameId == second.FrameId));
        Require(g.CreateSnapshot(0).Status != EngineStatus.Completed &&
            F<PhaseNamePredictionSettledEvent>(g).Single() is
                { PredictedUse: false, ActualUse: false, Correct: true, TargetSeat: 0 } settled && settled.OriginalFrameId == first.FrameId &&
            F<DamageAppliedEvent>(g).Count(e => !e.SourceLess && e.TargetSeat == 0 && e.SourceSeat == first.Source.OwnerSeat && e.Amount == 1) == 1 &&
            F<PhaseNamePredictionExpiredEvent>(g).Count(e => e.OriginalFrameId == second.FrameId) == 1 &&
            !F<PhaseNamePredictionSettledEvent>(g).Any(e => e.OriginalFrameId == second.FrameId) &&
            F<PlayerDiedEvent>(g).Count(e => e.VictimSeat == 0 && e.KillerSeat == first.Source.OwnerSeat) == 1 &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == secondCard).Location == CardLocation.DiscardPile &&
            !g.CardMovements.Any(m => m.Reason.Value == ClaimReason),
            "The first frozen ending candidate kills the non-Lord once; a later wrong-guess candidate expires once without damaging the corpse or obtaining its death-discarded original card while the match remains live.");
        _ = Cold(g, r);
    }

    private static void VerifyEarlyNativeUseBeforeArming(bool windows)
    {
        var (g, r) = Create(CardKind.FireSlash, earlyUseWindows: windows);
        Reach(g, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Early && p.Choices.Any(c => c.Targets.SequenceEqual([1])));
        Answer(g, c => c.Targets.SequenceEqual([1]));
        Reach(g, p => IsOffer(p, null));
        var declared = F<CardUseDeclaredEvent>(g).Single(e => e.SourceSeat == 0 && e.CardId == 0 && e.CardKind == CardKind.Slash);
        var actual = F<PhaseNamePredictionActualUseEvent>(g).Single(e => e.ActorSeat == 0 && e.NameKind == CardKind.Slash);
        Require(!F<PhaseNamePredictionStartedEvent>(g).Any() &&
            F<CardUsedEvent>(g).Single(e => e.SourceSeat == 0 && e.CardId == 0 && e.CardKind == CardKind.Slash).TargetSeat == 1 &&
            declared.ResolutionId > 0,
            "An earlier mandatory real PlayStarting producer issues its native zero-entity Slash before any prediction is invoked or armed.");
        if (windows)
        {
            var action = F<CardUseAppearanceCapturedEvent>(g).Single(e => e.Action.ActorSeat == 0).Action;
            Require(action is { Type: CardActionType.Use, EffectiveKind: CardKind.Slash, PhysicalCards.Count: 0 } &&
                action.TargetSeats.SequenceEqual([1]) && action.ConversionChain.Single() is { SkillId: Early, BindingId: "early", OwnerSeat: 0 } &&
                actual.ActionId == action.ActionId && actual.NativeCardUseFrameId is null,
                "The action-window producer retains its exact genuine Use action, source and empty physical material in the actual phase ledger.");
        }
        else
            Require(actual.ActionId is null && actual.NativeCardUseFrameId == declared.ResolutionId &&
                !F<CardUseAppearanceCapturedEvent>(g).Any(e => e.Action.ActorSeat == 0),
                "The legacy useCardActionWindows=false producer records its actual native CardUseFrameId with no fabricated ActionId or appearance.");
        var origin = ArmNext(ref g, r, true);
        var observed = F<PhaseNamePredictionUseObservedEvent>(g).Single(e => e.OriginalFrameId == origin.FrameId);
        Require(observed.ActionId == actual.ActionId && observed.NativeCardUseFrameId == actual.NativeCardUseFrameId &&
            observed.ActorSeat == 0 && observed.NameKind == CardKind.Slash && observed.ActualTurnNumber == origin.ActualTurnNumber &&
            observed.PhaseInstanceId == origin.PhaseInstanceId && actual.ActualTurnNumber == origin.ActualTurnNumber &&
            actual.PhaseInstanceId == origin.PhaseInstanceId &&
            g.Events.ToList().FindIndex(e => e.Payload.Equals(actual)) < g.Events.ToList().FindIndex(e => e.Payload.Equals(origin)) &&
            g.Events.ToList().FindIndex(e => e.Payload.Equals(declared)) < g.Events.ToList().FindIndex(e => e.Payload.Equals(origin)),
            "Arming retroactively observes the already-issued same-name Use from the exact current actual Play, preserving its original typed identity once.");
        Play(g);
        g = Cold(g, r);
        End(g);
        Until(g, e => F<PhaseNamePredictionSettledEvent>(e).Any(x => x.OriginalFrameId == origin.FrameId));
        Require(F<PhaseNamePredictionSettledEvent>(g).Single(e => e.OriginalFrameId == origin.FrameId) is
                { PredictedUse: true, ActualUse: true, Correct: true } &&
            F<PhaseNamePredictionUseObservedEvent>(g).Count(e => e.OriginalFrameId == origin.FrameId) == 1 &&
            F<PhaseNamePredictionActualUseEvent>(g).Count(e => e == actual) == 1,
            "Cold restore preserves the pre-arming native Use and settles its one exact prediction correctly without recording that issuance twice.");
    }

    private static void VerifyEndingInsertionReturnsToOriginalPhase()
    {
        var (g, r) = Create(CardKind.FireSlash, endingExtra: true);
        var original = ArmNext(ref g, r, false);
        Play(g);
        End(g);
        Reach(g, p => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == EndingExtra && p.Choices.Any(c => c.Targets.SequenceEqual([0])));
        var parent = g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.Window == SkillProgramTriggerWindow.PlayEnding);
        var oldKey = new PhaseNamePredictionPhaseKey(original.ActualTurnNumber, original.ActualTurnOwnerSeat, original.PhaseInstanceId);
        Require(parent.PhaseNamePredictionPhase == oldKey && !F<PhaseNamePredictionSettledEvent>(g).Any(),
            "The higher-priority genuine ending producer owns the original frozen phase key before inserting a different Play.");
        Answer(g, c => c.Targets.SequenceEqual([0]));
        var inserted = ArmNext(ref g, r, true, original.Source.OwnerSeat);
        Play(g);
        Require(inserted.ActualTurnNumber == original.ActualTurnNumber && inserted.ActualRoundNumber == original.ActualRoundNumber &&
            inserted.PhaseInstanceId != original.PhaseInstanceId && inserted.RequiredDiscardCount == 1 &&
            !g.ResolutionStack.Any(f => f.Id == parent.Id),
            "The inserted real Play has a fresh phase identity while the original ending parent is detached, and retains the same-round invocation cost.");
        Use(g, "virtual", [original.Source.OwnerSeat]);
        Play(g);
        Require(F<PhaseNamePredictionUseObservedEvent>(g).Count(e => e.OriginalFrameId == inserted.FrameId) == 1 &&
            !F<PhaseNamePredictionUseObservedEvent>(g).Any(e => e.OriginalFrameId == original.FrameId),
            "A real Use in the inserted Play belongs to its own newly armed promise and cannot contaminate the suspended original phase.");
        g = Cold(g, r);
        End(g);
        Until(g, e => F<PhaseNamePredictionSettledEvent>(e).Any(s => s.OriginalFrameId == inserted.FrameId) && P(e)?.SkillPrompt?.SkillId == Hp);
        Require(!F<PhaseNamePredictionSettledEvent>(g).Any(e => e.OriginalFrameId == original.FrameId) &&
            g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.Window == SkillProgramTriggerWindow.PlayEnding).PhaseNamePredictionPhase ==
                new PhaseNamePredictionPhaseKey(inserted.ActualTurnNumber, inserted.ActualTurnOwnerSeat, inserted.PhaseInstanceId),
            "The inserted phase settles against its own ending parent while the original promise remains unconsumed.");
        Continue(g);
        Until(g, e => F<PhaseNamePredictionSettledEvent>(e).Any(s => s.OriginalFrameId == original.FrameId) && P(e)?.SkillPrompt?.SkillId == Hp);
        var restoredParent = g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.Id == parent.Id);
        var originalSettlement = F<PhaseNamePredictionSettledEvent>(g).Single(e => e.OriginalFrameId == original.FrameId);
        var resolver = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == originalSettlement.FrameId);
        Require(restoredParent.PhaseNamePredictionPhase == oldKey && resolver.WindowContext?.ParentFrameId == parent.Id &&
            resolver.WindowContext.PhaseNamePrediction?.Origin == original &&
            F<PhaseNamePredictionSettledEvent>(g).Single(e => e.OriginalFrameId == inserted.FrameId) is
                { PredictedUse: true, ActualUse: true, Correct: true } &&
            originalSettlement is { PredictedUse: false, ActualUse: false, Correct: true } &&
            F<ProgramPhaseScheduledEvent>(g).Count(e => e.SkillId == EndingExtra && e.Started) == 1 &&
            F<ProgramPhaseScheduledEvent>(g).Count(e => e.SkillId == EndingExtra && !e.Started) == 1,
            "Returning from the actual inserted phase restores the same original ending parent and frozen key, so its prior no-use promise settles correctly despite the advanced global phase serial.");
        Continue(g);
        Until(g, e => !e.ResolutionStack.Any(f => f.Id == originalSettlement.FrameId));
        Require(F<PhaseNamePredictionSettledEvent>(g).Count(e => e.OriginalFrameId == original.FrameId) == 1 &&
            F<PhaseNamePredictionSettledEvent>(g).Count(e => e.OriginalFrameId == inserted.FrameId) == 1,
            "The two actual phases each finish exactly one independent settlement after both typed damage children return.");
    }

    private static void VerifyOwnPhaseNullificationIsGenuineUse()
    {
        var (g, r) = Create(CardKind.Nullification);
        var origin = ArmNext(ref g, r, true);
        var shown = F<PhaseNamePredictionArmedEvent>(g).Single(e => e.FrameId == origin.FrameId).CardId;
        Play(g);
        var draw = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.DrawTwo && a.CardId is { } id && id != shown &&
            a.ConversionSource is { SkillId: Driver, BindingId: "draw-two" });
        SubmitPlay(g, draw);
        Reach(g, p => p.Kind == DecisionKind.Nullification && p.PlayerSeat == 0 &&
            p.Choices.Any(c => c.Cards.SequenceEqual([shown]) && c.Parameters.GetValueOrDefault("response") == "nullification"));
        Require(!F<PhaseNamePredictionUseObservedEvent>(g).Any(e => e.OriginalFrameId == origin.FrameId),
            "Using a different physical Nullification as converted DrawTwo material has the effective name DrawTwo and does not itself satisfy the shown Nullification prediction.");
        g = Cold(g, r);
        Answer(g, c => c.Cards.SequenceEqual([shown]) && c.Parameters.GetValueOrDefault("response") == "nullification");
        var action = F<CardActionAcceptedEvent>(g).Single(e => e.Action is
            { Type: CardActionType.Response, ActorSeat: 0, ProviderSeat: 0, RequesterSeat: null, EffectiveKind: CardKind.Nullification } &&
            e.Action.PhysicalCards is [{ CardId: var id }] && id == shown).Action;
        Require(action.PhysicalCards.Single().From == CardLocation.Hand(0) &&
            F<PhaseNamePredictionActualUseEvent>(g).Single(e => e.ActionId == action.ActionId) is
                { ActorSeat: 0, NameKind: CardKind.Nullification, NativeCardUseFrameId: null } actual &&
            actual.ActualTurnNumber == origin.ActualTurnNumber && actual.PhaseInstanceId == origin.PhaseInstanceId &&
            F<PhaseNamePredictionUseObservedEvent>(g).Single(e => e.OriginalFrameId == origin.FrameId) is
                { ActorSeat: 0, NameKind: CardKind.Nullification } observed && observed.ActionId == action.ActionId &&
            observed.ActualTurnNumber == origin.ActualTurnNumber && observed.PhaseInstanceId == origin.PhaseInstanceId,
            "A genuine physical Nullification used in its actor's own Play counts as Use through the native qualified-use boundary despite its typed Response action; an ordinary Duel response remains excluded by the separate retained check.");
        Play(g);
        End(g);
        Until(g, e => F<PhaseNamePredictionSettledEvent>(e).Any(s => s.OriginalFrameId == origin.FrameId));
        Require(F<PhaseNamePredictionSettledEvent>(g).Single(e => e.OriginalFrameId == origin.FrameId) is
            { PredictedUse: true, ActualUse: true, Correct: true }, "The exact own-phase Nullification promise settles correctly once.");
    }

    private static void VerifyNoUseDamageAndSourceLoss()
    {
        var (g, r) = Create();
        var origin = ArmNext(ref g, r, false, cold: true);
        Play(g);
        var hp = V(g, 0).Hp;
        Use(g, "suppress", [origin.Source.OwnerSeat]);
        Reach(g, p => p.SkillPrompt?.SkillId == Driver && p.Choices.Any(c => c.Parameters.GetValueOrDefault("choice") == Prediction));
        Answer(g, c => c.Parameters.GetValueOrDefault("choice") == Prediction);
        Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        var settled = F<PhaseNamePredictionSettledEvent>(g).Single(e => e.OriginalFrameId == origin.FrameId);
        var resolver = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == settled.FrameId);
        Require(F<ProgramSkillSuppressedEvent>(g).Any(e => e.TargetSeat == origin.Source.OwnerSeat && e.SkillId == Prediction && e.Suppressed) &&
            settled is { PredictedUse: false, ActualUse: false, Correct: true, TargetSeat: 0 } &&
            resolver.WindowContext?.PhaseNamePrediction?.Origin == origin &&
            resolver.PhaseNamePrediction?.IssuedPolicy?.Origin == origin &&
            V(g, 0).Hp == hp - 1 &&
            F<DamageAppliedEvent>(g).Count(e => !e.SourceLess && e.SourceSeat == origin.Source.OwnerSeat && e.TargetSeat == 0 && e.Amount == 1) == 1,
            "A real general-skill suppression after issuance cannot revoke the exact phase-end promise; a correct no-use guess causes one real source-attributed damage and owns its HP child.");
        Private(g);
        g = Cold(g, r);
        Continue(g);
        Until(g, e => !e.ResolutionStack.Any(f => f.Id == settled.FrameId));
        Require(F<PhaseNamePredictionSettledEvent>(g).Count(e => e.OriginalFrameId == origin.FrameId) == 1 &&
            F<DamageAppliedEvent>(g).Count(e => !e.SourceLess && e.SourceSeat == origin.Source.OwnerSeat && e.TargetSeat == 0 && e.Amount == 1) == 1,
            "Returning from the genuine HP observer after cold restore settles the accepted prediction exactly once.");
    }

    private static void VerifyWrongGuessClaim(string zone)
    {
        var kind = zone == "equipment" ? CardKind.Crossbow : zone == "discard" ? CardKind.FireSlash : CardKind.Dodge;
        var (g, r) = Create(kind);
        var origin = ArmNext(ref g, r, zone == "hand");
        var armed = F<PhaseNamePredictionArmedEvent>(g).Single(e => e.FrameId == origin.FrameId);
        Play(g);
        var hp = V(g, 0).Hp;
        var from = CardLocation.Hand(0);
        if (zone != "hand")
        {
            var action = g.GetHumanLegalActions().First(a => a.CardId == armed.CardId && a.ConversionSource is null &&
                a.Kind == (zone == "equipment" ? LegalActionKind.Equip : LegalActionKind.Slash));
            SubmitPlay(g, action);
            Play(g);
            from = zone == "equipment" ? CardLocation.Equipment(0) : CardLocation.DiscardPile;
            Require(g.CreateCardZoneDiagnostics().Single(c => c.CardId == armed.CardId).Location == from &&
                F<PhaseNamePredictionUseObservedEvent>(g).Count(e => e.OriginalFrameId == origin.FrameId) == 1,
                "The exact publicly revealed physical entity genuinely moves through its native equipment or Slash use before phase-end retrieval.");
        }
        End(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Gain && p.PlayerSeat == origin.Source.OwnerSeat);
        var settled = F<PhaseNamePredictionSettledEvent>(g).Single(e => e.OriginalFrameId == origin.FrameId);
        var owner = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == settled.FrameId);
        Require(!settled.Correct && settled.PredictedUse == (zone == "hand") && settled.ActualUse == (zone != "hand") &&
            settled.CardId == armed.CardId && settled.TargetSeat == 0 && V(g, 0).Hp == hp &&
            owner.PhaseNamePrediction is { Stage: PhaseNamePredictionStage.ClaimChildren, Selected: [var claimed] } receipt &&
            claimed == armed.CardId && receipt.Eligible is [var material] && material.CardId == armed.CardId && material.From == from &&
            g.CardMovements.Count(m => m.CardId == armed.CardId && m.From == from &&
                m.To == CardLocation.Hand(origin.Source.OwnerSeat) && m.Reason.Value == ClaimReason) == 1 &&
            V(g, origin.Source.OwnerSeat).Hand.Any(c => c.Id == armed.CardId),
            "A wrong guess obtains the original revealed entity from its exact allowed current hand/equipment/public-discard location, through one real movement and private gain child.");
        Private(g);
        g = Cold(g, r);
        Continue(g);
        Until(g, e => !e.ResolutionStack.Any(f => f.Id == settled.FrameId));
        Require(F<PhaseNamePredictionSettledEvent>(g).Count(e => e.OriginalFrameId == origin.FrameId) == 1 &&
            g.CardMovements.Count(m => m.CardId == armed.CardId && m.From == from &&
                m.To == CardLocation.Hand(origin.Source.OwnerSeat) && m.Reason.Value == ClaimReason) == 1,
            "A restored claim child returns once without reacquiring the card or paying another activation.");
    }

    private static void VerifyVirtualCanonicalAndExtraPhase()
    {
        var (g, r) = Create(CardKind.FireSlash, extraTarget: true);
        var first = ArmNext(ref g, r, true);
        Play(g);
        var shown = F<PhaseNamePredictionArmedEvent>(g).Single(e => e.FrameId == first.FrameId);
        Require(shown.NameKind == CardKind.Slash && V(g, 0).Hand.Single(c => c.Id == shown.CardId).Kind == CardKind.FireSlash,
            "The real shown FireSlash uses the shared canonical Slash name.");
        var eventCount = g.Events.Count;
        Use(g, "virtual", [first.Source.OwnerSeat]);
        var issued = g.Events.Skip(eventCount).Select(e => e.Payload).ToArray();
        var action = issued.OfType<CardUseAppearanceCapturedEvent>().Single().Action;
        Require(action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0, EffectiveKind: CardKind.Slash, PhysicalCards.Count: 0 } &&
            action.TargetSeats.SequenceEqual([first.Source.OwnerSeat]) &&
            action.ConversionChain.Single() is { SkillId: Driver, BindingId: "virtual", OwnerSeat: 0 } &&
            issued.OfType<CardUseDeclaredEvent>().Single() is { CardId: 0, CardKind: CardKind.Slash, SourceSeat: 0 } &&
            issued.OfType<CardUsedEvent>().Single() is { CardId: 0, CardKind: CardKind.Slash, SourceSeat: 0 } &&
            F<PhaseNamePredictionUseObservedEvent>(g).Single(e => e.OriginalFrameId == first.FrameId) is
                { ActorSeat: 0, NameKind: CardKind.Slash } observed && observed.ActionId == action.ActionId &&
            observed.ActualTurnNumber == first.ActualTurnNumber && observed.PhaseInstanceId == first.PhaseInstanceId,
            "A true zero-entity program Slash is an effective canonical Use in the exact predicted phase, without inventing physical material.");
        Play(g);
        End(g);
        Until(g, e => F<PhaseNamePredictionSettledEvent>(e).Any(s => s.OriginalFrameId == first.FrameId));
        var second = ArmNext(ref g, r, false, first.Source.OwnerSeat);
        Play(g);
        Require(first.ActualTurnNumber == second.ActualTurnNumber && first.ActualRoundNumber == second.ActualRoundNumber &&
            first.PhaseInstanceId != second.PhaseInstanceId && second.RequiredDiscardCount == 1 &&
            F<ProgramPhaseScheduledEvent>(g).Any(e => e.SkillId == Extra && e.Phase == TurnPhase.Play && e.Started) &&
            !F<PhaseNamePredictionUseObservedEvent>(g).Any(e => e.OriginalFrameId == second.FrameId),
            "A genuine inserted Play and the subsequent normal Play have different phase identities inside one actual turn; earlier use does not spill into the second prediction and its round cost does not reset.");
        End(g);
        Until(g, e => F<PhaseNamePredictionSettledEvent>(e).Any(s => s.OriginalFrameId == second.FrameId));
        Require(F<PhaseNamePredictionSettledEvent>(g).Single(s => s.OriginalFrameId == first.FrameId) is
                { PredictedUse: true, ActualUse: true, Correct: true } &&
            F<PhaseNamePredictionSettledEvent>(g).Single(s => s.OriginalFrameId == second.FrameId) is
                { PredictedUse: false, ActualUse: false, Correct: true },
            "Both exact actual Play phases settle their own opposite guesses once.");
        _ = Cold(g, r);
    }

    private static void VerifyResponseDoesNotCount()
    {
        var (g, r) = Create(CardKind.FireSlash);
        var origin = ArmNext(ref g, r, false);
        var shown = F<PhaseNamePredictionArmedEvent>(g).Single(e => e.FrameId == origin.FrameId);
        Play(g);
        Use(g, "opponent-duel", [origin.Source.OwnerSeat]);
        Reach(g, p => p is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 });
        Answer(g, c => c.Cards.Contains(shown.CardId));
        Until(g, e => F<CardRespondedEvent>(e).Any(c => c.ResponderSeat == 0 && c.CardId == shown.CardId));
        Require(!F<PhaseNamePredictionUseObservedEvent>(g).Any(e => e.OriginalFrameId == origin.FrameId),
            "The predicted actor's genuine physical Slash response to another actor's native Duel is a response, not a matching Use.");
        Play(g);
        End(g);
        Until(g, e => F<PhaseNamePredictionSettledEvent>(e).Any(s => s.OriginalFrameId == origin.FrameId));
        Require(F<PhaseNamePredictionSettledEvent>(g).Single(e => e.OriginalFrameId == origin.FrameId) is
            { PredictedUse: false, ActualUse: false, Correct: true },
            "The response-only phase correctly settles a no-use guess despite consuming a same-name physical entity.");
    }

    private static void VerifyRoundCostShortageAndEmptyHand()
    {
        var (g, r) = Create(humanPredictor: true, extraPeers: true, ownerInitialCards: 2);
        Play(g);
        Require(V(g, 0).HandCount == 2, "The fixed fixture genuinely deals exactly two cards to the human predictor.");
        End(g);
        var starts = new List<PhaseNamePredictionStartedEvent>();
        foreach (var expected in new[] { (Required: 0, Actual: 0), (Required: 1, Actual: 1), (Required: 2, Actual: 1), (Required: 3, Actual: 0) })
        {
            var before = g.CardMovements.Count;
            var origin = ArmNext(ref g, r, false, 0, cold: expected.Required == 2);
            starts.Add(origin);
            Require(origin.RequiredDiscardCount == expected.Required &&
                g.CardMovements.Skip(before).Count(m => m.From.OwnerSeat == 0 && m.To == CardLocation.DiscardPile && m.Reason.Value == CostReason) == expected.Actual &&
                F<PhaseNamePredictionCostPaidEvent>(g).Where(e => e.FrameId == origin.FrameId).Sum(e => e.ActualCount) == expected.Actual &&
                F<PhaseNamePredictionCostPaidEvent>(g).Count(e => e.FrameId == origin.FrameId) == (expected.Actual == 0 ? 0 : 1),
                "Actual-round prior invocation count determines X; partial and zero material still lead to a real armed prediction, with only one actual discard batch: " +
                JsonSerializer.Serialize(new { expected, origin, Hand = V(g, 0).HandCount, Paid = F<PhaseNamePredictionCostPaidEvent>(g) }));
            Until(g, e => F<PhaseNamePredictionSettledEvent>(e).Any(s => s.OriginalFrameId == origin.FrameId));
        }
        Require(starts.All(s => s.ActualRoundNumber == starts[0].ActualRoundNumber) && V(g, 0).HandCount == 0 &&
            starts[0].ActualTurnNumber == starts[1].ActualTurnNumber && starts[0].PhaseInstanceId != starts[1].PhaseInstanceId,
            "Real extra Play phases spend the same owner's round ledger and exhaust the two physical costs without an instance/phase refresh.");
        Until(g, e => F<RoundStartedEvent>(e).Any(x => x.RoundNumber > starts[0].ActualRoundNumber) && IsOffer(P(e), 0));
        var reset = ArmNext(ref g, r, false, 0);
        Require(reset.ActualRoundNumber > starts[0].ActualRoundNumber && reset.RequiredDiscardCount == 0 &&
            reset.Source == starts[0].Source && V(g, 0).HandCount == 0,
            "Only a genuine new RoundStarted resets the same stable owner's escalating cost, and empty-cost activation remains legal.");
        _ = Cold(g, r);

        (g, r) = Create(humanPredictor: true, ownerInitialCards: 0, peerInitialCards: 0);
        Play(g);
        End(g);
        Reach(g, p => IsOffer(p, 0));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        var empty = F<PhaseNamePredictionStartedEvent>(g).Single();
        Require(empty.RequiredDiscardCount == 0 && V(g, 0).HandCount == 0 && V(g, empty.TargetSeat).HandCount == 0,
            "A real first foreign phase with no target hand and no owner discard still accepts the optional non-prerequisite invocation.");
        Reach(g, p => IsOffer(p, 0));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        var emptyAgain = F<PhaseNamePredictionStartedEvent>(g).Last();
        Require(emptyAgain.RequiredDiscardCount == 1 && F<PhaseNamePredictionArmedEvent>(g).Length == 0 &&
            F<PhaseNamePredictionSettledEvent>(g).Length == 0 && F<CardsRevealedEvent>(g).Length == 0 &&
            !g.CardMovements.Any(m => m.Reason.Value == CostReason),
            "Zero executable discard does not prevent another accepted activation; an actually still-empty reveal step creates no false armed prediction or settlement.");
        _ = Cold(g, r);
    }

    private static PhaseNamePredictionStartedEvent ArmNext(ref GameEngine g, ContentRegistry r, bool guess, int? owner = null, bool cold = false)
    {
        Reach(g, p => IsOffer(p, owner));
        var chooser = P(g)!.PlayerSeat;
        var prior = F<PhaseNamePredictionStartedEvent>(g);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        var origin = F<PhaseNamePredictionStartedEvent>(g).Single(e => !prior.Contains(e));
        Require(origin.Source.SkillId == Prediction && origin.Source.OwnerSeat == chooser && origin.StateId == "forecast" &&
            origin.TargetSeat == origin.ActualTurnOwnerSeat && origin.TargetSeat != chooser && origin.PhaseInstanceId > 0,
            "The accepted optional trigger publishes one exact foreign-phase source, cursor and actual round identity.");
        while (P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "phase-name-cost") == true)
        {
            Private(g);
            var cost = P(g)!;
            Require(cost.Choices.All(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("card-id") == c.Cards[0].ToString()),
                "Each actual HE cost selector contains its exact own physical entity.");
            Answer(g, _ => true);
        }
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "phase-name-reveal"));
        var reveal = P(g)!;
        Require(reveal.PlayerSeat == chooser && reveal.ValidCardIds.Count == 0 &&
            reveal.Choices.All(c => c.Cards.Count == 0 && c.Parameters.ContainsKey("slot-index")) &&
            !F<PhaseNamePredictionArmedEvent>(g).Any(e => e.FrameId == origin.FrameId),
            "The source sees only frozen opaque slots in the actual target hand before public reveal.");
        Private(g);
        Reject(g);
        if (cold) g = Cold(g, r);
        Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == "0");
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "phase-name-guess"));
        var shown = F<CardsRevealedEvent>(g).Single(e => e.ResolutionId == origin.FrameId).Cards.Single().Id;
        Require(g.CreateCardZoneDiagnostics().Single(c => c.CardId == shown).Location == CardLocation.Hand(origin.TargetSeat) &&
            F<CardsRevealedEvent>(g).Single(e => e.ResolutionId == origin.FrameId).Cards is System.Collections.IList { IsReadOnly: true } &&
            !F<PhaseNamePredictionSettledEvent>(g).Any(e => e.OriginalFrameId == origin.FrameId),
            "The actual entity is publicly revealed without moving it; the future guess is not public before settlement.");
        foreach (var viewer in Enumerable.Range(0, 4))
        {
            var view = g.CreateSnapshot(viewer);
            Require(view.PublicRevealedCards.Select(c => c.Id).SequenceEqual([shown]) &&
                view.PublicRevealedCards is System.Collections.IList { IsReadOnly: true } &&
                (viewer == origin.TargetSeat || view.Players[origin.TargetSeat].Hand.Count == 0),
                "Every viewer sees only the one frozen public shown entity; another viewer receives no target-hand identities.");
        }
        Private(g);
        if (cold) g = Cold(g, r);
        Answer(g, c => c.Parameters.GetValueOrDefault("guess") == (guess ? "yes" : "no"));
        Require(F<PhaseNamePredictionArmedEvent>(g).Single(e => e.FrameId == origin.FrameId).CardId == shown &&
            !F<PhaseNamePredictionSettledEvent>(g).Any(e => e.OriginalFrameId == origin.FrameId),
            "One private answer arms the exact original revealed entity without publishing the guess prematurely.");
        return origin;
    }

    private static bool IsOffer(PendingDecision? p, int? owner) => p is not null && (owner is null || p.PlayerSeat == owner) &&
        p.SkillPrompt?.SkillId == Prediction && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] F<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Real phase prediction command rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Use(GameEngine g, string id, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, P(g)!.PromptId));
    private static void SubmitPlay(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> stop) => Until(g, e => P(e) is { } p && stop(p));
    private static void Until(GameEngine g, Func<GameEngine, bool> stop)
    {
        for (var i = 0; i < 200; i++)
        {
            if (stop(g)) return;
            var p = P(g);
            if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true) Continue(g);
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true)
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards })
                Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification or DecisionKind.RescueDying })
                Answer(g, c => c.Cards.Count == 0 && (c.Parameters.GetValueOrDefault("response") is "take-damage" or "pass" or "let-die" || c.Parameters.GetValueOrDefault("action") == "pass"));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Fixed phase prediction fixture did not reach its real boundary: " +
            JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack, Started = F<PhaseNamePredictionStartedEvent>(g), Settled = F<PhaseNamePredictionSettledEvent>(g) }));
    }
    private static void Private(GameEngine g)
    {
        var p = P(g)!;
        Require(p.IsPrivate, "The actual cost, opaque reveal, secret guess or typed observer prompt is private.");
        foreach (var viewer in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat))
            Require(g.CreateSnapshot(viewer).PendingDecision is null, "Another viewer receives no private choice or secret guess.");
        Require(p.ValidCardIds is System.Collections.IList { IsReadOnly: true } && p.ValidTargetSeats is System.Collections.IList { IsReadOnly: true } &&
            p.ValidContentIds is System.Collections.IList { IsReadOnly: true } && p.Choices is System.Collections.IList { IsReadOnly: true } &&
            p.Choices.All(c => c.Cards is System.Collections.IList { IsReadOnly: true } && c.Targets is System.Collections.IList { IsReadOnly: true } &&
                c.ContentIds is System.Collections.IList { IsReadOnly: true } && c.Parameters is System.Collections.IDictionary { IsReadOnly: true }),
            "All exposed prompt arrays and nested choice parameter dictionaries are frozen.");
        foreach (var receipt in g.ResolutionStack.OfType<ProgramSkillFrame>().Select(f => f.PhaseNamePrediction).OfType<ProgramPhaseNamePredictionReceipt>())
            Require(receipt.Eligible is System.Collections.IList { IsReadOnly: true } && receipt.Selected is System.Collections.IList { IsReadOnly: true } &&
                receipt.TargetHand is System.Collections.IList { IsReadOnly: true }, "The owning frame freezes every collection-bearing private receipt.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(restored) == State(g), "Four-view cold restore preserves the secret policy, exact actual phase, once-only costs, native typed children, public facts and command journal."); return restored; }
    private static void Reject(GameEngine g) { var p = P(g)!; var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished secret-slot answer cannot mutate the accepted prediction."); }

    private static void LoaderContract()
    {
        var rules = Rules();
        var presentation = Presentation(rules);
        _ = SkillProgramCatalog.Load(rules.ToJsonString(), presentation);
        foreach (var mutation in new Action<JsonNode>[]
        {
            n => n["skills"]![0]!["triggers"]!.AsArray().RemoveAt(1),
            n => n["skills"]![0]!["triggers"]![1]!["optional"] = true,
            n => n["skills"]![0]!["triggers"]![0]!["optional"] = false,
            n => n["skills"]![0]!["triggers"]![1]!["effects"]![0]!["stateId"] = "different",
            n => n["skills"]![0]!["triggers"]![0]!["effects"]!.AsArray().Add(JsonNode.Parse("""{"op":"draw","target":"owner","amount":1}"""))
        })
        {
            var invalid = rules.DeepClone(); mutation(invalid); var rejected = false;
            try { _ = SkillProgramCatalog.Load(invalid.ToJsonString(), presentation); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "The shared loader rejects unpaired, optional-settlement, mandatory-start, mismatched-state and extra-effect phase prediction programs.");
        }
    }

    private static (GameEngine, ContentRegistry) Create(CardKind kind = CardKind.Dodge, bool humanPredictor = false,
        bool extraTarget = false, bool extraPeers = false, int ownerInitialCards = 4, int peerInitialCards = 4,
        Role humanRole = Role.Lord, bool lowHp = false, bool? earlyUseWindows = null, bool endingExtra = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(kind, humanPredictor, extraTarget, extraPeers, ownerInitialCards, peerInitialCards,
            lowHp, earlyUseWindows, endingExtra));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = humanRole,
            ModeId = kind == CardKind.SilverLion ? EquipmentMode : Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Require(P(g)!.ValidContentIds.Contains("fixture:phase-name-owner"), "The fixed published real general choice contains the human fixture.");
        Accept(g, new SelectGeneralCommand(0, "fixture:phase-name-owner", g.Revision, P(g)!.PromptId)); return (g, r);
    }
    private static JsonNode Rules() => JsonNode.Parse($$$"""
    {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
      {"id":"{{{Prediction}}}","revision":1,"triggers":[
        {"id":"start","window":"playPhaseStarting","subject":"owner","turnOwnerScope":"otherLiving","optional":true,"effects":[{"op":"beginPhaseNamePrediction","target":"owner","stateId":"forecast"}]},
        {"id":"end","window":"playEnding","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"effects":[{"op":"settlePhaseNamePrediction","target":"owner","stateId":"forecast"}]}]},
      {"id":"{{{Driver}}}","revision":1,"activations":[
        {"id":"virtual","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useVirtualSlash","target":"selectedTarget"}]},
        {"id":"opponent-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
        {"id":"suppress","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"suppressGeneralSkill","target":"selectedTarget"}]}]},
      {"id":"{{{Gain}}}","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{{ClaimReason}}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
      {"id":"{{{Hp}}}","revision":1,"triggers":[{"id":"hp","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
      {"id":"{{{Extra}}}","revision":1,"triggers":[{"id":"extra","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
      {"id":"fixture:phase-name-owner-initial","revision":1,"modifiers":[{"id":"initial","query":"initialHandSize","operation":"add","value":4,"priority":0,"condition":{"kind":"always"}}]},
      {"id":"fixture:phase-name-peer-initial","revision":1,"modifiers":[{"id":"initial","query":"initialHandSize","operation":"add","value":4,"priority":0,"condition":{"kind":"always"}}]}
    ]}
    """)!;
    private static string Presentation(JsonNode rules) => JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
        skills = rules["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n =>
        {
            var labels = new Dictionary<string, object> { ["name"] = "真实阶段预测", ["description"] = "精确实际阶段的私密承诺与原生结算" };
            if (n!["id"]!.GetValue<string>() is Gain or Hp or Recovery) labels["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
            return (object)labels;
        }) });
    private sealed class Fixture(CardKind kind, bool humanPredictor, bool extraTarget, bool extraPeers, int ownerInitialCards, int peerInitialCards,
        bool lowHp, bool? earlyUseWindows, bool endingExtra) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:phase-name", "1.0.0", "完整真实阶段预测共享检查");
        public void Register(IContentRegistryBuilder b)
        {
            var rules = Rules();
            rules["skills"]![5]!["modifiers"]![0]!["value"] = Math.Max(1, ownerInitialCards);
            rules["skills"]![6]!["modifiers"]![0]!["value"] = Math.Max(1, peerInitialCards);
            if (lowHp)
                rules["skills"]![1]!["activations"]!.AsArray().Add(JsonNode.Parse("""{"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":3}]}"""));
            if (earlyUseWindows is { } windows)
            {
                var early = JsonNode.Parse("""{"id":"fixture:phase-name-early","revision":1,"triggers":[{"id":"early","window":"playPhaseStarting","subject":"owner","priority":100,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]}]}""")!;
                early["triggers"]![0]!["effects"]![1]!["useCardActionWindows"] = windows;
                rules["skills"]!.AsArray().Add(early);
            }
            if (endingExtra)
                rules["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:phase-name-ending-extra","revision":1,"triggers":[{"id":"insert","window":"playEnding","subject":"owner","priority":100,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"insertPhase","target":"selectedTarget","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}"""));
            if (kind == CardKind.Nullification)
                rules["skills"]![1]!["viewAs"] = JsonNode.Parse("""[{"id":"draw-two","inputKinds":["nullification"],"inputSuits":[],"sourceZones":["hand"],"outputKind":"drawTwo","singleCardTrickUse":true,"forPlay":true,"forResponse":false}]""");
            if (kind == CardKind.SilverLion)
            {
                rules["skills"]![1]!["activations"]!.AsArray().Add(JsonNode.Parse("""{"id":"lose-hp","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":1}]}"""));
                rules["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:phase-name-recovery","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}"""));
                b.AddCard(new ContentCardDefinition("fixture:phase-name-silver-lion", "白银狮子", "装备牌",
                    "失去装备区里的白银狮子后回复1点体力。", CardKind.SilverLion,
                    AiTags: new Dictionary<string, string> { ["slot"] = "armor" }));
            }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), Presentation(rules));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "真实原生阶段检查") { Program = program });
            b.AddSkill(new("fixture:phase-name-selection", "固定候选", "固定小夹具") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            if (lowHp)
            {
                b.AddSkill(new("fixture:phase-name-owner-selection", "反贼公开候选", "正式角色评分保留玩家候选")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Renegade ? 10000d : -10000d) });
                b.AddSkill(new("fixture:phase-name-peer-selection", "主公公开候选", "正式角色评分选择其他将")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? 10000d : -10000d) });
            }
            var owner = new List<string> { Driver, Hp };
            if (earlyUseWindows is not null) owner.Add(Early);
            if (endingExtra) owner.Add(EndingExtra);
            if (kind == CardKind.SilverLion) owner.Add(Recovery);
            if (ownerInitialCards > 0) owner.Add("fixture:phase-name-owner-initial");
            if (humanPredictor) owner.Add(Prediction);
            if (extraTarget) owner.Add(Extra);
            b.AddGeneral(new("fixture:phase-name-owner", "真实阶段玩家", "supporter",
                lowHp ? "fixture:phase-name-owner-selection" : "fixture:phase-name-selection", "jin", 4, owner, GeneralGender.Male));
            for (var i = 1; i < 4; i++)
            {
                var skills = new List<string> { Gain };
                if (peerInitialCards > 0) skills.Add("fixture:phase-name-peer-initial");
                if (!humanPredictor) skills.Add(Prediction);
                if (extraPeers) skills.Add(Extra);
                b.AddGeneral(new($"fixture:phase-name-peer-{i}", "真实阶段他人", "supporter",
                    lowHp ? "fixture:phase-name-peer-selection" : "fixture:phase-name-selection", "qun", 4, skills, GeneralGender.Male));
            }
            var card = kind switch { CardKind.Crossbow => "standard:crossbow", CardKind.FireSlash => "standard:fire_slash",
                CardKind.SilverLion => "fixture:phase-name-silver-lion", CardKind.Nullification => "standard:nullification", _ => "standard:dodge" };
            b.AddDeck(new("fixture:phase-name-deck", "固定真实实体", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 40).Select(i => new ContentDeckPhysicalCard(card, Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(kind == CardKind.SilverLion ? EquipmentMode : Mode, "真实阶段预测", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:phase-name-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:phase-name-owner", "fixture:phase-name-peer-1", "fixture:phase-name-peer-2", "fixture:phase-name-peer-3"]));
        }
    }
}

