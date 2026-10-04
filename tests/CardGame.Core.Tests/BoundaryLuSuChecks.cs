using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryLuSuChecks
{
    private const string Haoshi = "boundary:haoshi-current", Dimeng = "boundary:dimeng-current", Driver = "fixture:ls-driver";
    private const string Extra = "fixture:ls-extra", Gift = "fixture:ls-gift", Exchange = "fixture:ls-exchange", Debt = "fixture:ls-debt", Support = "fixture:ls-support";
    private const string Gain = "fixture:ls-gain", Hp = "fixture:ls-hp", Mode = "identity:classic-lu-su-fixture";
    private const string DrawReason = "skill-program." + Haoshi + ".DrawExtraAndArmHalfHandSupport";
    private const string GiftReason = "skill-program." + Haoshi + ".GiveHalfHandAndIssueTargetSupport";
    private const string SupportReason = "skill-program." + Haoshi + ".OfferHalfHandRecipientSupport";
    private const string ExchangeReason = "skill-program." + Dimeng + ".exchange", DebtReason = "skill-program." + Dimeng + ".MoveBoundCards";

    // Staged real-command drafts only: no compilation, loader or test execution.
    public static void ExtraDrawAndHalfGiftOwnTheirGainChildren()
    {
        var (g, r) = Create(observers: true); Reach(g, IsExtra); ActivateExtra(g); Reach(g, p => p.SkillPrompt?.SkillId == Extra);
        var draw = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.HalfHandDraw is not null);
        var drawId = draw.Id; var receipt = draw.HalfHandDraw!;
        var extraWindow = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == draw.Id);
        Require(receipt.RequestedCount == 2 && receipt.ActualCount == 2 && Moves(g, DrawReason).Length == 2 &&
            extraWindow.Batch.Movements.Count == 1 && (extraWindow.Batch.AwaitingProgramFrameId is null || extraWindow.Batch.AwaitingProgramFrameId == draw.Id) &&
            extraWindow.ResumeProgramFrameId is null && V(g, 0).HandCount == 6 && !F<HalfHandTargetSupportIssuedEvent>(g).Any(),
            "Real extra Draw2 is paid in two inherited atomic batches; its first gain child retains the exact draw owner before normal draw or recipient issuance.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Haoshi && p.Choices.Any(c => c.Targets.Count == 1));
        Require(V(g, 0).HandCount == 8 && P(g)!.Choices.Where(c => c.Targets.Count == 1).All(c => V(g, c.Targets[0]).HandCount == 4),
            "Only after both extra and normal draw children does the least-hand target query see the real current hands.");
        Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, IsOwned); Private(g, 0); Reject(g);
        var handBefore = V(g, 0).Hand.Select(c => c.Id).Order().ToArray(); var ids = P(g)!.Choices.Where(c => c.Cards.Count == 1).Take(4).Select(c => c.Cards[0]).ToArray();
        var before = g.CardMovements.Count;
        for (var i = 0; i < 3; i++) Answer(g, c => c.Cards.SequenceEqual([ids[i]]));
        Require(g.CardMovements.Count == before && !F<HalfHandTargetSupportIssuedEvent>(g).Any(), "Three private choices do not pay or issue a partial half-hand recipient.");
        g = RestoreAfterCold(g, r); Answer(g, c => c.Cards.SequenceEqual([ids[3]])); Reach(g, p => p.SkillPrompt?.SkillId == Gift);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.HalfHandGift is not null); var rootId = root.Id;
        var gift = root.HalfHandGift!; var moved = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == root.Id);
        Require(root.InstructionIndex == 3 && root.PendingMovementContinuation is { SubjectSeat: 0, BeforeCount: 0 } && gift.DrawProgramFrameId == drawId &&
            gift.HandCountBefore == 8 && gift.CardIds.SequenceEqual(ids) && moved.Batch.Movements.Count == 4 && moved.ResumeProgramFrameId is null &&
            moved.Batch.AwaitingProgramFrameId == root.Id && Moves(g, GiftReason).All(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)) &&
            F<HalfHandTargetSupportIssuedEvent>(g).Single().ActualDeliveredCount == 4 && V(g, 0).HandCount == 4 && V(g, 1).HandCount == 8,
            "All four frozen entities move atomically to the original recipient and issue one scalar qualification before their exact movement children return.");
        Frozen(gift); PrivateGift(g, ids); g = RestoreAfterCold(g, r); Frozen(g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == rootId).HalfHandGift!);
        Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain); g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == rootId && f.HalfHandGift is not null) && Moves(g, GiftReason).Length == 4,
            "The actual recipient gain→Recovery→HP observer remains beneath the paid half-hand root without reopening selection or re-paying.");
        g = RestoreAfterCold(g, r); Continue(g); Play(g);
        Require(V(g, 0).Hand.Select(c => c.Id).Order().SequenceEqual(handBefore.Except(ids)) &&
            F<HalfHandTargetSupportIssuedEvent>(g).Count(e => e.ProgramFrameId == rootId) == 1 && Moves(g, GiftReason).Length == 4,
            "Cold continuation completes the same whole gift once and returns the original owner's play."); Cold(g, r);
    }

    public static void ExchangeFirstThenFrozenDebtPaysAfterSilverLionChildren()
    {
        var (g, r) = Create(observers: true); Play(g); SetHand(g, 1, 1); SetHand(g, 2, 3);
        Use(g, "equip", [0]); Play(g); Use(g, "hurt", [0]); Play(g);
        var lion = V(g, 0).Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
        var a = V(g, 1).Hand.Select(c => c.Id).Order().ToArray(); var b = V(g, 2).Hand.Select(c => c.Id).Order().ToArray();
        var own = V(g, 0).HandCount; StartExchange(g, 1, 2); Reach(g, p => p.SkillPrompt?.SkillId == Exchange);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PhaseHandExchange is not null); var receipt = root.PhaseHandExchange!;
        var issued = F<PhaseHandExchangeDebtIssuedEvent>(g).Single();
        Require(receipt.Issued && receipt.FrozenDifference == 2 && receipt.FirstCardIds.SequenceEqual(a) && receipt.SecondCardIds.SequenceEqual(b) &&
            issued.ProgramFrameId == root.Id && V(g, 1).Hand.Select(c => c.Id).Order().SequenceEqual(b) && V(g, 2).Hand.Select(c => c.Id).Order().SequenceEqual(a) &&
            V(g, 0).HandCount == own && Moves(g, DebtReason).Length == 0 && Moves(g, ExchangeReason).Length == 8,
            "The original two actual hands exchange completely before any owner cost, and frozen X=2 is issued with exact phase/source/ledger ownership.");
        Frozen(receipt); g = RestoreAfterCold(g, r); Continue(g); Play(g);
        Use(g, "draw", [1]); Play(g); Use(g, "draw", [1]); Play(g);
        Require(Math.Abs(V(g, 1).HandCount - V(g, 2).HandCount) == 4, "Real later card gain changes the pair's current difference without repricing the accepted debt.");
        End(g); Reach(g, IsOwned); var pay = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PhaseHandDebtPayment is not null);
        Require(pay.PhaseHandDebtPayment is { FrozenDifference: 2, RequiredPaymentCount: 2 } && P(g)!.PlayerSeat == 0,
            "Only the exact original PlayEnding candidate freezes the debt's legal payment count two.");
        var hand = P(g)!.Choices.First(c => c.Cards.Count == 1 && V(g, 0).Hand.Any(h => h.Id == c.Cards[0])).Cards[0];
        var before = g.CardMovements.Count; Answer(g, c => c.Cards.SequenceEqual([hand])); Require(g.CardMovements.Count == before, "The first private cost choice alone pays nothing.");
        g = RestoreAfterCold(g, r); Answer(g, c => c.Cards.SequenceEqual([lion])); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        pay = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PhaseHandDebtPayment is not null); var payId = pay.Id;
        Require(pay.PhaseHandDebtPayment!.ExchangeProgramFrameId == issued.ProgramFrameId && Moves(g, DebtReason).Length == 2 &&
            g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h => h.Change.ParentFrameId == pay.Id && h.ResumeFrameId == pay.Id &&
                h.Continuation == PostEventContinuation.AwaitedProgramMovement && h.Change.Kind == HpChangeKind.Recovery && h.Change.Amount == 1 && h.Change.TargetSeat == 0),
            "The real Silver Lion HE cost pays once before its exact AwaitedProgramMovement self-recovery HP child.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Debt);
        var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == payId);
        Require(movement.Batch.Movements.Select(m => m.CardId).Order().SequenceEqual(new[] { hand, lion }.Order()) &&
            (movement.Batch.AwaitingProgramFrameId is null || movement.Batch.AwaitingProgramFrameId == payId) && movement.ResumeProgramFrameId is null,
            "After HP returns, the same atomic cost batch retains its producer parent and exact resume even when MoveBound preceded Await.");
        g = RestoreAfterCold(g, r); Continue(g); Finish(g, payId);
        Require(F<PhaseHandExchangeDebtPaymentStartedEvent>(g).Count(e => e.ExchangeProgramFrameId == issued.ProgramFrameId) == 1 &&
            Moves(g, DebtReason).Length == 2 && Moves(g, ExchangeReason).Length == 8 && V(g, 1).HandCount == 5 && V(g, 2).HandCount == 1,
            "The restored paid child drains once; ending cost never re-exchanges or re-measures the current pair."); Cold(g, r);
    }

    public static void DebtShortfallAndSourceLossDoNotReverseExchange()
    {
        var (g, r) = Create(); Play(g); SetHand(g, 1, 1); SetHand(g, 2, 3); StartExchange(g, 1, 2); Play(g);
        var issued = F<PhaseHandExchangeDebtIssuedEvent>(g).Single(); SetHand(g, 0, 1); End(g); Reach(g, IsOwned);
        var pay = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PhaseHandDebtPayment is not null); var payId = pay.Id;
        Require(pay.PhaseHandDebtPayment is { FrozenDifference: 2, RequiredPaymentCount: 1 } && P(g)!.Choices.Count(c => c.Cards.Count == 1) == 1,
            "The shortfall default retains original X=2 and freezes the one currently legal actual HE entity.");
        g = RestoreAfterCold(g, r); Answer(g, c => c.Cards.Count == 1); Finish(g, payId);
        Require(Moves(g, DebtReason).Length == 1 && F<PhaseHandExchangeDebtPaymentStartedEvent>(g).Single().ExchangeProgramFrameId == issued.ProgramFrameId &&
            V(g, 1).HandCount == 3 && V(g, 2).HandCount == 1, "An unpaid shortage does not reverse the true exchange or create a second cost."); Cold(g, r);

        var (lost, lr) = Create(); Play(lost); SetHand(lost, 1, 1); SetHand(lost, 2, 3); StartExchange(lost, 1, 2); Play(lost);
        var x = F<PhaseHandExchangeDebtIssuedEvent>(lost).Single(); Use(lost, "remove"); Play(lost); var handBefore = V(lost, 0).HandCount;
        End(lost); Reach(lost, p => p.Kind == DecisionKind.DiscardCards || lost.State.TurnNumber > x.ActualTurnNumber || p.Kind == DecisionKind.PlayCard);
        Require(!F<PhaseHandExchangeDebtPaymentStartedEvent>(lost).Any(e => e.ExchangeProgramFrameId == x.ProgramFrameId) && Moves(lost, DebtReason).Length == 0 &&
            V(lost, 1).HandCount == 3 && V(lost, 2).HandCount == 1 && V(lost, 0).HandCount == handBefore,
            "Real loss of the exact source before Ending cancels its unpaid debt and preserves the completed pair exchange."); Cold(lost, lr);

        var (paid, pr) = Create(observers: true, removePaidSource: true); Play(paid); SetHand(paid, 1, 1); SetHand(paid, 2, 3);
        Use(paid, "equip", [0]); Play(paid); Use(paid, "hurt", [0]); Play(paid); var sl = V(paid, 0).Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
        StartExchange(paid, 1, 2); Play(paid); End(paid); Reach(paid, IsOwned); var h = P(paid)!.Choices.First(c => c.Cards.Count == 1 && c.Cards[0] != sl).Cards[0];
        Answer(paid, c => c.Cards.SequenceEqual([h])); Answer(paid, c => c.Cards.SequenceEqual([sl])); Reach(paid, p => p.SkillPrompt?.SkillId == Hp);
        var paymentId = paid.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PhaseHandDebtPayment is not null).Id;
        paid = RestoreAfterCold(paid, pr); Continue(paid); Finish(paid, paymentId);
        Require(Moves(paid, DebtReason).Length == 2 && F<PhaseHandExchangeDebtPaymentStartedEvent>(paid).Length == 1 &&
            F<ProgramSkillResolvedEvent>(paid).Any(e => e.FrameId == paymentId && !e.Completed),
            "Source loss inside a real paid recovery child retains both original costs, drains their children, and cancels only the now unpaid program tail."); Cold(paid, pr);
    }

    public static void HalfHandSupportUsesRealTargetsAndExpiresAtNextActualStart()
    {
        var (g, r) = Create(observers: true); Qualify(g); Play(g); GiveTrick(g, 0);
        var qualification = F<HalfHandTargetSupportIssuedEvent>(g).Single(); var trick = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.DrawTwo);
        Accept(g, new PlayCardCommand(0, trick.CardId ?? throw new InvalidOperationException("The real trick action lost its entity."), [], g.Revision, P(g)!.PromptId, trick.PlayedCardKind, trick.TargetCardId) { ConversionSource = trick.ConversionSource, AdditionalConversionSources = trick.AdditionalConversionSources }); Reach(g, IsSupport);
        Private(g, 1); Reject(g); var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.HalfHandSupport is not null);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == frame.HalfHandSupport!.Use.CardUseFrameId);
        Require(use.CardKind == CardKind.DrawTwo && use.SourceSeat == 0 && use.TargetSeats.Count == 0 && use.Action is { Type: CardActionType.Use } action &&
            action.TargetSeats.Count == 0 && frame.HalfHandSupport!.Use.TargetSeat == 0 && frame.HalfHandSupport.SupportProgramFrameId == qualification.ProgramFrameId &&
            g.ResolutionStack.OfType<ActualUseTargetWindowFrame>().Single().ReturnKind == ActualUseTargetReturnKind.OrdinaryTrick,
            "Only the new exact recipient support infers the mature physical DrawTwo self effect; accepted action and explicit targets remain empty.");
        var giftId = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards[0]; var useId = use.Id; g = RestoreAfterCold(g, r); Answer(g, c => c.Cards.SequenceEqual([giftId]));
        Reach(g, p => p.SkillPrompt?.SkillId == Support); frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.HalfHandSupport is { Paid: true });
        Require(frame.PendingMovementContinuation is { SubjectSeat: 0, BeforeCount: 0 } && frame.HalfHandSupport!.Use.CardUseFrameId == useId &&
            Moves(g, SupportReason).Single().CardId == giftId && Moves(g, SupportReason).Single().From == CardLocation.Hand(1) &&
            Moves(g, SupportReason).Single().To == CardLocation.Hand(0) && F<HalfHandSupportPaymentEvent>(g).Single().Paid,
            "Only one real donor hand entity pays inside the original actual-use target return, before the original trick effect.");
        g = RestoreAfterCold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain); g = RestoreAfterCold(g, r); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Hp); g = RestoreAfterCold(g, r); Continue(g); Play(g);
        Require(F<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == useId) && Moves(g, SupportReason).Length == 1,
            "The real gain/HP children return to the accepted DrawTwo effect once, without another donor selection.");

        GiveSlash(g, 1); Use(g, "request", [1]); Reach(g, p => Action(p, "request-slash")); var slash = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards[0];
        Answer(g, c => c.Cards.SequenceEqual([slash])); Reach(g, IsSupport); frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.HalfHandSupport is not null);
        use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == frame.HalfHandSupport!.Use.CardUseFrameId);
        Require(use.Action is { Type: CardActionType.Use, ActorSeat: 1, EffectiveKind: CardKind.Slash } && use.TargetSeats.SequenceEqual([0]) &&
            use.CardAttack?.ProgramSkillCardUseFrameId is not null && frame.HalfHandSupport!.Use.ActorSeat == 1,
            "A real requested physical Slash carries its mature original program parent and exact target; response-only cards do not manufacture support windows.");
        Private(g, 1); g = RestoreAfterCold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("support-option") == "pass"); Play(g);
        Require(F<HalfHandSupportPaymentEvent>(g).Count(e => e.Declined) == 1 && Moves(g, SupportReason).Length == 1, "A real donor decline pays nothing and returns to the original Slash.");

        GiveTrick(g, 0); trick = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.DrawTwo); var before = F<HalfHandSupportPaymentEvent>(g).Length;
        Accept(g, new PlayCardCommand(0, trick.CardId ?? throw new InvalidOperationException("The real trick action lost its entity."), [], g.Revision, P(g)!.PromptId, trick.PlayedCardKind, trick.TargetCardId) { ConversionSource = trick.ConversionSource, AdditionalConversionSources = trick.AdditionalConversionSources }); Reach(g, IsSupport);
        Require(P(g)!.PlayerSeat == 1, "The published current donor owns the native decision."); var prefix = g.AcceptedCommands.Count;
        for (var i = 0; i < 90 && !(P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }); i++) Accept(g, new AdvanceOneStepCommand(g.Revision));
        Require(P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && F<HalfHandSupportPaymentEvent>(g).Length == before + 1 &&
            g.AcceptedCommands.Skip(prefix).All(c => c is AdvanceOneStepCommand), "Native AI resolves its real current donor choice without reading another player's private hand."); Cold(g, r);
        End(g); Reach(g, p => p.SkillPrompt?.SkillId == Haoshi && p.Choices.Any(c => c.Parameters.GetValueOrDefault("binding-id") == "extra-draw"));
        Require(F<TurnStartedEvent>(g).Any(e => e.ActorSeat == 0 && e.TurnNumber > qualification.ActualTurnNumber), "Expiry is a real next owner TurnStarted, rather than a guessed round.");
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Play(g); GiveTrick(g, 0); trick = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.DrawTwo);
        before = F<HalfHandSupportPaymentEvent>(g).Length;
        Accept(g, new PlayCardCommand(0, trick.CardId ?? throw new InvalidOperationException("The real trick action lost its entity."), [], g.Revision, P(g)!.PromptId, trick.PlayedCardKind, trick.TargetCardId) { ConversionSource = trick.ConversionSource, AdditionalConversionSources = trick.AdditionalConversionSources }); Play(g);
        Require(F<HalfHandSupportPaymentEvent>(g).Length == before && !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.HalfHandSupport is not null),
            "After the next actual start, the original recipient qualification cannot open another self-target aid window."); Cold(g, r);

        // New uncovered composition: a genuine paid aid gain suspends an actual
        // physical Slash with a separate cardless Damage -> Dying -> recovery.
        // The mandatory pulse is a generic test-only response, not classic Niepan.
        {
            var (nested, nr) = Create(observers: true, supportDamage: true); Qualify(nested); Play(nested);
            for (var i = 0; V(nested, 0).Hp > 1 && i < 4; i++) { Use(nested, "hurt", [0]); Play(nested); }
            Require(V(nested, 0).Hp == 1, "Real bounded loseHp commands prepare the support owner at one HP.");
            GiveSlash(nested, 1); Use(nested, "request", [1]); Reach(nested, p => Action(p, "request-slash"));
            var slashEntity = P(nested)!.Choices.First(c => c.Cards.Count == 1).Cards[0];
            Answer(nested, c => c.Cards.SequenceEqual([slashEntity])); Reach(nested, IsSupport); Private(nested, 1);
            var aidRoot = nested.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.HalfHandSupport is not null);
            var heldUseId = aidRoot.HalfHandSupport!.Use.CardUseFrameId; var aidRootId = aidRoot.Id;
            var donorEntity = P(nested)!.Choices.First(c => c.Cards.Count == 1).Cards[0];
            nested = RestoreAfterCold(nested, nr); Answer(nested, c => c.Cards.SequenceEqual([donorEntity]));
            Reach(nested, p => p.SkillPrompt?.SkillId == Support); nested = RestoreAfterCold(nested, nr); Continue(nested);
            Reach(nested, p => p.SkillPrompt?.SkillId == "fixture:ls-support-damage");
            var damageProgram = nested.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "fixture:ls-support-damage");
            Require(damageProgram.WindowContext is { Window: SkillProgramTriggerWindow.CardsGained, MovementBatch: { } aidBatch } &&
                aidBatch.ParentFrameId == aidRootId && aidBatch.Movements.Single().CardId == donorEntity &&
                nested.ResolutionStack.OfType<CardUseFrame>().Any(u => u.Id == heldUseId && u.CardId == slashEntity) &&
                Moves(nested, SupportReason).Length == 1 && !F<CardUseFinishedEvent>(nested).Any(e => e.ResolutionId == heldUseId),
                "The exact paid support gain candidate owns the delivered entity while the original physical Slash remains in flight.");
            nested = RestoreAfterCold(nested, nr); Continue(nested);
            Reach(nested, p => p.SkillPrompt?.SkillId == "fixture:ls-source-pulse");
            var dying = nested.ResolutionStack.OfType<DyingFrame>().Single();
            var fatal = nested.ResolutionStack.OfType<DamageFrame>().Single(d => d.Id == dying.ParentFrameId);
            damageProgram = nested.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == fatal.ParentFrameId);
            var pulse = nested.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "fixture:ls-source-pulse");
            Require(dying.Continuation == DyingContinuationKind.Damage && dying.VictimSeat == 0 && dying.KillerSeat == 0 && fatal.Amount == 1 &&
                fatal.SourceSeat == 0 && fatal.TargetSeat == 0 && V(nested, 0).Hp == 0 &&
                damageProgram.AttackReturn?.ParentAttackOwnerFrameId == heldUseId && damageProgram.AttackAttempt is { SourceSeat: 0, TargetSeat: 0 } &&
                pulse.WindowContext is { Window: SkillProgramTriggerWindow.SelfDyingResponse } pulseContext && pulseContext.ParentFrameId == dying.Id &&
                pulseContext.DamageFrameId == fatal.Id && pulseContext.SourceSeat == 0 && pulseContext.TargetSeat == 0 &&
                F<DamageRequestedEvent>(nested).Count(e => e.ResolutionId == fatal.Id && e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1 && e.SourceCard is null) == 1 &&
                nested.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == aidRootId && f.HalfHandSupport is { Paid: true }),
                "Real one-point cardless damage owns Damage/Dying and the exact mandatory response, retaining the original paid aid and Slash return.");
            nested = RestoreAfterCold(nested, nr); Continue(nested); Reach(nested, p => p.SkillPrompt?.SkillId == Hp);
            Require(nested.ResolutionStack.OfType<DyingFrame>().Any(d => d.Id == dying.Id) && V(nested, 0).Hp == 3 &&
                nested.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(h => h.Change.ParentFrameId == pulse.Id && h.ResumeFrameId == pulse.Id),
                "The same real damage-Dying program recovery owns its HP child after a cold-restored response.");
            nested = RestoreAfterCold(nested, nr); Continue(nested); Play(nested);
            Require(F<HalfHandSupportPaymentEvent>(nested).Count(e => e.ProgramFrameId == aidRootId && e.Paid) == 1 && Moves(nested, SupportReason).Length == 1 &&
                F<CardUseFinishedEvent>(nested).Count(e => e.ResolutionId == heldUseId) == 1 &&
                F<DamageAppliedEvent>(nested).Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1 && e.RemainingHp == 0) == 1 &&
                F<AfterDamageEvent>(nested).Count(e => e.ResolutionId == fatal.Id && e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1) == 1,
                "Cold continuation drains the paid gain damage once, then the original Slash once, without reopening or duplicating its one-card aid.");
            Cold(nested, nr);
        }
    }

    private static (GameEngine, ContentRegistry) Create(bool observers = false, bool removePaidSource = false, bool supportDamage = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new Fixture(observers, removePaidSource, supportDamage));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:ls-owner", g.Revision, P(g)!.PromptId)); return (g, r);
    }
    private static void Qualify(GameEngine g)
    {
        Reach(g, IsExtra); ActivateExtra(g); Reach(g, p => p.SkillPrompt?.SkillId == Haoshi && p.Choices.Any(c => c.Targets.Count == 1));
        Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, IsOwned); while (P(g) is { } p && IsOwned(p)) Answer(g, c => c.Cards.Count == 1);
        Play(g); Require(F<HalfHandTargetSupportIssuedEvent>(g).Single().RecipientSeat == 1, "The actual full half-hand ledger qualifies only the originally selected recipient.");
    }
    private static void GiveTrick(GameEngine g, int seat) => Search(g, "trick", seat);
    private static void GiveSlash(GameEngine g, int seat) { Search(g, "basic", seat); Require(V(g, seat).Hand.Any(c => c.Kind == CardKind.Slash), "The fixed deck's only basic kind supplies one real Slash."); }
    private static void Search(GameEngine g, string criterion, int seat)
    { Use(g, "search"); Reach(g, p => Action(p, "declared-deck-criterion")); Answer(g, c => c.Parameters.GetValueOrDefault("criterion") == criterion); Reach(g, p => Action(p, "declared-deck-recipient")); Answer(g, c => c.Targets.SequenceEqual([seat])); Play(g); }
    private static void SetHand(GameEngine g, int seat, int count)
    { Clear(g, seat); for (var i = 0; i < count; i++) { Use(g, "draw", [seat]); Play(g); } }
    private static void Clear(GameEngine g, int seat)
    { if (V(g, seat).HandCount + V(g, seat).Equipment.Count == 0) return; Use(g, "clear", [seat]); Reach(g, IsOwned); while (P(g) is { } p && IsOwned(p)) Answer(g, c => c.Cards.Count == 1); Play(g); }
    private static void StartExchange(GameEngine g, int a, int b) => Accept(g, new UseProgramSkillCommand(0, Dimeng, "swap-first", [], [a, b], g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void ActivateExtra(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("binding-id") == "extra-draw");
    private static bool IsExtra(PendingDecision p) => p.SkillPrompt?.SkillId == Haoshi && p.Choices.Any(c => c.Parameters.GetValueOrDefault("binding-id") == "extra-draw");
    private static bool IsOwned(PendingDecision p) => Action(p, "select-owned-cards");
    private static bool IsSupport(PendingDecision p) => Action(p, "half-hand-support");
    private static bool Action(PendingDecision p, string id) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == id);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static T[] F<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardMovementRecord[] Moves(GameEngine g, string reason) => g.CardMovements.Where(m => m.Reason.Value == reason).ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Finish(GameEngine g, long id) { for (var i = 0; i < 160; i++) { if (g.ResolutionStack.All(f => f.Id != id)) return; Advance(g); } throw new InvalidOperationException("Paid original frame did not return."); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    { for (var i = 0; i < 220; i++) { var p = P(g); if (p is not null && predicate(p)) return; Advance(g); } throw new InvalidOperationException("Fixed Lu Su boundary absent: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack, Recent = g.Events.TakeLast(4).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is not null && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
        else if (p is not null && IsOwned(p)) Answer(g, c => c.Cards.Count == 1);
        else if (p is not null && Action(p, "half-hand-support")) Answer(g, c => c.Parameters.GetValueOrDefault("support-option") == "pass");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is not null && Action(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }) Answer(g, c => c.Cards.Count == 0);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine RestoreAfterCold(GameEngine g, ContentRegistry r)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(g) == State(restored), "All four prepared views, private choices, frozen receipts, typed parents and real ledger cold-restore identically."); return restored; }
    private static void Cold(GameEngine g, ContentRegistry r) => _ = RestoreAfterCold(g, r);
    private static void Private(GameEngine g, int chooser)
    {
        var decision = g.CreateSnapshot(chooser).PendingDecision!;
        Require(decision is { IsPrivate: true }, "The actual chooser owns the private selection.");
        ImmutableList(decision.Choices); foreach (var choice in decision.Choices) { ImmutableList(choice.Cards); ImmutableList(choice.Targets); }
        foreach (var seat in Enumerable.Range(0, 4).Where(s => s != chooser)) Require(g.CreateSnapshot(seat).PendingDecision is null, "Foreign views expose no private options, material IDs or selected entities.");
    }
    private static void PrivateGift(GameEngine g, int[] ids)
    { foreach (var seat in new[] { 2, 3 }) Require(g.CreateSnapshot(seat).Players[1].Hand.Count == 0 && g.CreateSnapshot(seat).Players[0].Hand.Count == 0, "Uninvolved prepared views expose only public hand counts after an actual hidden gift."); }
    private static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished private choice cannot pay or mutate the owning draft."); }
    private static void Frozen(ProgramHalfHandGiftPayment value)
    { Immutable(value.CardIds); var ids = value.CardIds.ToArray(); var copy = value with { CardIds = ids }; if (ids.Length > 0) ids[0] = -1; Require(copy.CardIds.SequenceEqual(value.CardIds), "with clones the half-hand invoice."); Immutable(JsonSerializer.Deserialize<ProgramHalfHandGiftPayment>(JsonSerializer.Serialize(value))!.CardIds); }
    private static void Frozen(ProgramPhaseHandExchangeReceipt value)
    { Immutable(value.FirstCardIds); Immutable(value.SecondCardIds); var first = value.FirstCardIds.ToArray(); var copy = value with { FirstCardIds = first }; if (first.Length > 0) first[0] = -1; Require(copy.FirstCardIds.SequenceEqual(value.FirstCardIds), "with clones the exact original exchange invoice."); var json = JsonSerializer.Deserialize<ProgramPhaseHandExchangeReceipt>(JsonSerializer.Serialize(value))!; Immutable(json.FirstCardIds); Immutable(json.SecondCardIds); }
    private static void Immutable(IReadOnlyList<int> values)
    { if (values.Count == 0) return; if (values is IList<int> list) { var rejected = false; try { list[0] = -1; } catch (NotSupportedException) { rejected = true; } Require(rejected, "Prepared/checkpoint receipt collections cannot be mutated through IList."); } else Require(values is not int[], "No mutable array is exposed."); }
    private static void ImmutableList<T>(IReadOnlyList<T> values)
    { if (values.Count == 0) return; if (values is IList<T> list) { var rejected = false; try { list[0] = values[0]; } catch (NotSupportedException) { rejected = true; } Require(rejected, "Prepared public or private choice collections cannot be mutated through IList."); } else Require(values is not T[], "Prepared choices expose no mutable array."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool observers, bool removePaidSource, bool supportDamage) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:lu-su", "1.0.0", "界鲁肃公共能力真实命令草稿");
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse("""
                {"schemaVersion":0,"skills":[
                  {"id":"fixture:ls-driver","revision":1,"activations":[
                    {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipped"}]},
                    {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                    {"id":"clear","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","numberExpression":"allOwnedZoneCards","zones":["hand","equipment"],"resultBind":"clear"},{"op":"moveBoundCards","target":"owner","sourceBind":"clear","destination":"discardPile","awaitMovementTriggers":true},{"op":"awaitBoundCardMovements","target":"owner"}]},
                    {"id":"draw","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":1}]},
                    {"id":"search","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"declareDeckCriterionAndGiveMatchingCard","target":"owner"}]},
                    {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"asked"}]},
                    {"id":"remove","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["boundary:dimeng-current"],"sourceBind":"fixture:ls-noop"}]}]},
                  {"id":"fixture:ls-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"fixture:ls-extra","revision":1,"triggers":[{"id":"extra","window":"cardsGained","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:haoshi-current.DrawExtraAndArmHalfHandSupport"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:ls-gift","revision":1,"triggers":[{"id":"gift","window":"cardsMoved","subject":"owner","optional":false,"priority":100,"sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:haoshi-current.GiveHalfHandAndIssueTargetSupport"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:ls-exchange","revision":1,"triggers":[{"id":"exchange","window":"cardsMoved","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:dimeng-current.exchange"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:ls-debt","revision":1,"triggers":[{"id":"debt","window":"cardsMoved","subject":"owner","optional":false,"sourceZones":["hand","equipment"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:dimeng-current.MoveBoundCards"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:ls-support","revision":1,"triggers":[{"id":"support","window":"cardsMoved","subject":"owner","optional":false,"priority":100,"sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:haoshi-current.OfferHalfHandRecipientSupport"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
                  {"id":"fixture:ls-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:haoshi-current.GiveHalfHandAndIssueTargetSupport","skill-program.boundary:haoshi-current.OfferHalfHandRecipientSupport"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":1}]}]},
                  {"id":"fixture:ls-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
                ]}
                """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            ((JsonArray)rules["skills"]!).Add(JsonNode.Parse("""
                {"id":"fixture:ls-support-damage","revision":1,"triggers":[{"id":"paid-gain-damage","window":"cardsGained","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:haoshi-current.OfferHalfHandRecipientSupport"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"damage","target":"owner","amount":1}]}]}
                """));
            ((JsonArray)rules["skills"]!).Add(JsonNode.Parse("""
                {"id":"fixture:ls-source-pulse","revision":1,"triggers":[{"id":"generic-damage-dying-return","window":"selfDyingResponse","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":3,"clampToMaxHp":true}]}]}
                """));
            if (supportDamage) ((JsonArray)rules["skills"]!).Single(n => n!["id"]!.GetValue<string>() == Gain)!["triggers"]![0]!["movementReasons"] =
                JsonNode.Parse("""["skill-program.boundary:haoshi-current.GiveHalfHandAndIssueTargetSupport"]""");
            if (removePaidSource) ((JsonArray)rules["skills"]![8]!["triggers"]![0]!["effects"]!).Add(JsonNode.Parse("""{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["boundary:dimeng-current"],"sourceBind":"fixture:ls-noop"}"""));
            var presentation = JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实命令", description = "小固定夹具" }, ["fixture:ls-quiet"] = new { name = "安静回合", description = "实际跳过出牌" },
                  [Extra] = Pause("额外摸牌孩子"), [Gift] = Pause("半手付款孩子"), [Exchange] = Pause("实际换手孩子"), [Debt] = Pause("Ending成本孩子"),
                  [Support] = Pause("受赠者援助孩子"), [Gain] = Pause("真实得牌"), [Hp] = Pause("真实回复"),
                  ["fixture:ls-support-damage"] = Pause("已付援助得牌伤害"), ["fixture:ls-source-pulse"] = Pause("真实伤害濒死返回") } });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), presentation);
            foreach (var id in new[] { Driver, "fixture:ls-quiet", Extra, Gift, Exchange, Debt, Support, Gain, Hp, "fixture:ls-support-damage", "fixture:ls-source-pulse" }) b.AddSkill(new(id, id, "真实程序夹具") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:ls-noop", "已替换来源", "无运行能力"));
            b.AddSkill(new("fixture:ls-selection", "固定选将", "无运行能力") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            var owner = new List<string> { Dimeng, Driver }; if (observers) owner.AddRange([Extra, Gift, Exchange, Debt, Support, Gain, Hp]);
            if (supportDamage) owner.AddRange(["fixture:ls-support-damage", "fixture:ls-source-pulse"]);
            b.AddGeneral(new("fixture:ls-owner", "界鲁肃机制", "supporter", Haoshi, "wu", 3, owner, Gender: GeneralGender.Male) { InitialHp = 1 });
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:ls-target-{i}", "固定目标", "supporter", "fixture:ls-selection", "wu", 6,
                observers ? ["fixture:ls-quiet", Exchange, Gain, Hp] : ["fixture:ls-quiet"], Gender: GeneralGender.Male) { InitialHp = 5 });
            b.AddDeck(new("fixture:ls-deck", "固定合法实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 60).Select(i =>
                new ContentDeckPhysicalCard(i % 3 == 0 ? "classic:silver-lion" : i % 3 == 1 ? "standard:draw_two" : "standard:slash", i % 3 == 0 ? Suit.Spade : Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "真实界鲁肃", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:ls-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:ls-owner", "fixture:ls-target-1", "fixture:ls-target-2", "fixture:ls-target-3"]));
        }
        private static object Pause(string name) => new { name, description = "真实子结算暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
