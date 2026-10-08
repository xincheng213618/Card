using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using static DrawFundedBasicFixture;

internal static class DrawFundedDistinctBasicResponseChecks
{
    public static void NativeDodgeDyingAndProgramReturns()
    {
        OwnSlashDodge();
        HealthyRescuerAndDyingOwner();
        foreach (var (activation, intent) in new[]
        {
            ("target-slash", DrawFundedDistinctBasicIntent.ProgramSlash),
            ("nearest-slash", DrawFundedDistinctBasicIntent.ProgramNearestSlash),
            ("assisted-slash", DrawFundedDistinctBasicIntent.AssistedSlash),
            ("nearest-legal-slash", DrawFundedDistinctBasicIntent.NearestLegalSlash)
        }) ProgramReturn(activation, intent);
        ProvisionIsExcluded(CardKind.Duel);
        ProvisionIsExcluded(CardKind.ArrowBarrage);
        BorrowedSwordReturn();
        QinglongReturn();
        QinglongReturn(optionalCompleted: true);
    }

    private static bool IsPaid(PromptChoice choice, CardKind kind) => choice.Parameters.ContainsKey("draw-funded-basic") &&
        choice.Parameters.GetValueOrDefault("output-kind") == kind.ToString();

    private static void OwnSlashDodge()
    {
        var (g, r) = Create(optionalGain: true, foreignSlash: true); Play(g); End(g);
        ReachNative(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge } && p.Choices.Any(c => IsPaid(c, CardKind.Dodge)));
        var incoming = g.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardAttack is not null);
        var originalWindow = g.ResolutionStack.OfType<ResponseWindowFrame>().Last();
        var hand = V(g, 0).Hand.Select(c => c.Id).ToArray(); var hp = V(g, 0).Hp;
        Require(hand.Length == hp && hand.All(id => V(g, 0).Hand.Single(c => c.Id == id).Suit == Suit.Spade),
            "The actual ordinary Slash defender qualifies its complete nonempty hand before accepting a Dodge Use.");
        g = Cold(g, r); Answer(g, c => IsPaid(c, CardKind.Dodge));
        ReachNative(g, p => p.SkillPrompt?.SkillId == Gain);
        var paid = g.ResolutionStack.OfType<DrawFundedDistinctBasicFrame>().Single();
        var move = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
        Require(paid.Payment is { Intent: DrawFundedDistinctBasicIntent.OwnSlashDodge, ActualDrawCount: 1, Cursor: 0 } &&
            paid.Payment.ParentFrameId == incoming.Id && paid.Payment.RequestFrameId == originalWindow.Id &&
            paid.Payment.ParentActionId == incoming.Action!.ActionId && paid.ActiveChildFrameId == move.Id &&
            move.ResumeDrawFundedDistinctBasicFrameId == paid.Id && move.Step == ResolutionFrameStep.AwaitingResponse &&
            F<DrawFundedDistinctBasicIssuedEvent>(g).Length == 0,
            "The optional real gain suspends the once-paid Dodge on its original Slash, response window and successful-response cursor.");
        Frozen(paid.OriginalDecision); Private(g); Reject(g); g = Cold(g, r);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        ReachNative(g, p => p.SkillPrompt?.SkillId == Gain && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        g = Cold(g, r); Continue(g);
        ReachNative(g, p => p.SkillPrompt?.SkillId == Completed);
        var owner = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == incoming.Id);
        var receipt = owner.DrawFundedDistinctBasicResponse!;
        var action = F<CardActionAcceptedEvent>(g).Single(e => e.Action.ActionId == receipt.CardActionId).Action;
        Require(receipt.Payment.PaymentFrameId == paid.Id && receipt.OwnerFrameId == incoming.Id &&
            action is { Type: CardActionType.Response, EffectiveKind: CardKind.Dodge, PhysicalCards.Count: 0,
                EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: false } && action.ActorSeat == 0 &&
            action.ParentActionId == incoming.Action!.ActionId && action.ConversionChain.SequenceEqual([paid.Payment.Source]) &&
            owner.CardAttack!.SuccessfulDodgeResponses == paid.Payment.Cursor &&
            V(g, 0).HandCount == hand.Length + 1 && V(g, 0).Hp == hp &&
            F<DrawFundedDistinctBasicReturnedEvent>(g).Length == 0,
            "Dodge is an accepted neutral zero-material response Use on the incoming owner; completion keeps its receipt before the native cursor advances.");
        Private(g); g = Cold(g, r); Continue(g);
        UntilNative(g, e => F<DrawFundedDistinctBasicReturnedEvent>(e).Any(f => f.PaymentFrameId == paid.Id));
        Require(F<DrawFundedDistinctBasicPaidEvent>(g).Count(f => f.PaymentFrameId == paid.Id) == 1 &&
            F<DrawFundedDistinctBasicReturnedEvent>(g).Count(f => f.PaymentFrameId == paid.Id) == 1 &&
            !g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.DrawFundedDistinctBasicResponse?.Payment.PaymentFrameId == paid.Id) &&
            g.CardMovements.All(m => m.CardId != 0) && hand.All(id => g.CreateCardZoneDiagnostics().Any(z => z.CardId == id && z.Location == CardLocation.Hand(0))),
            "The native successful Dodge clears and returns the exact receipt once, preserves old hand entities and never pays an entity0.");
        _ = Cold(g, r);
    }

    private static void HealthyRescuerAndDyingOwner()
    {
        var (g, r) = Create(optionalGain: true, fragileTargets: true); Play(g); Use(g, "die-other", [1]);
        ReachNative(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying } && p.Choices.Any(c => IsPaid(c, CardKind.Peach)));
        var dying = g.ResolutionStack.OfType<DyingFrame>().Single(); var before = V(g, 0).HandCount;
        Require(V(g, 0).Hp == before && V(g, 1).Hp == 0 &&
            !P(g)!.Choices.Any(c => IsPaid(c, CardKind.Alcohol)),
            "A healthy owner with its complete qualifying hand may use Peach for another real dying victim; Wine is self-only.");
        g = Cold(g, r); Answer(g, c => IsPaid(c, CardKind.Peach));
        ReachNative(g, p => p.SkillPrompt?.SkillId == Gain);
        var paid = g.ResolutionStack.OfType<DrawFundedDistinctBasicFrame>().Single();
        Require(paid.Payment is { Intent: DrawFundedDistinctBasicIntent.Dying, EffectiveKind: CardKind.Peach, ActualDrawCount: 1 } &&
            paid.Payment.ParentFrameId == dying.Id && paid.Payment.RequestFrameId == dying.Id &&
            paid.Payment.TargetSeat == 1 && paid.Payment.Cursor == dying.ResponderIndex,
            "The rescue draw owns the original Dying responder cursor and victim before issuing a promised card.");
        Private(g); g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        ReachNative(g, p => p.SkillPrompt?.SkillId == Gain && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        Continue(g); ReachNative(g, p => p.SkillPrompt?.SkillId == Completed);
        var rescue = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.DrawFundedDistinctBasicUse?.Payment.PaymentFrameId == paid.Id);
        NeutralUse(rescue, paid.Id, DrawFundedDistinctBasicIntent.Dying);
        Require(rescue.TargetSeats.SequenceEqual([1]) && rescue.DyingResponse is { UsedPeach: true, PeachCardId: 0, UsedAlcohol: false } token &&
            token.ResolutionId == dying.Id && token.ResponderSeat == 0 && V(g, 1).Hp == 1 && V(g, 0).HandCount == before + 1,
            "The healthy rescuer issues a real native zero-material Peach and retains its exact Dying typed return through completion.");
        Private(g); g = Cold(g, r); Continue(g); ReachNative(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Once(g, paid.Payment, rescue.Id); Require(V(g, 1).IsAlive && V(g, 1).Hp == 1 && !g.ResolutionStack.OfType<DyingFrame>().Any(),
            "The real rescued victim survives and the original program resumes after the native rescue return.");
        _ = Cold(g, r);

        (g, r) = Create(legacyAlcohol: true); Play(g); Use(g, "die");
        ReachNative(g, p => p.SkillPrompt?.SkillId == Rescue);
        Require(V(g, 0).Hp == 0 && V(g, 0).HandCount > 0 && !P(g)!.Choices.Any(c => c.Parameters.ContainsKey("draw-funded-basic")) &&
            F<DrawFundedDistinctBasicStartedEvent>(g).Length == 0,
            "A self-dying owner at HP0 cannot newly qualify a nonempty hand or publish a draw-funded Wine.");
        Private(g); g = Cold(g, r); Continue(g); ReachNative(g, p => p.SkillPrompt?.SkillId == Hp);
        var legacy = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.LegacyDyingAlcoholReturn is not null);
        var producer = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Rescue);
        var recovery = g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        Require(legacy is { CardId: 0, CardKind: CardKind.Alcohol, PhysicalCardIds.Count: 0, Action: not null } &&
            legacy.LegacyDyingAlcoholReturn is { } returned && returned.ProgramFrameId == producer.Id &&
            returned.InstructionIndex == producer.InstructionIndex && returned.ProducerSource ==
                new CardConversionSource(Rescue, producer.TriggerId!, 0, producer.SkillInstanceId) &&
            legacy.Action.ConversionChain.SequenceEqual([returned.ProducerSource]) &&
            F<LegacyActualUseCompletionCapturedEvent>(g).Count(f => f.CardUseFrameId == legacy.Id &&
                f.ProgramParentFrameId == producer.Id && f.Action.ActionId == legacy.Action.ActionId) == 1 &&
            recovery.ResumeFrameId == legacy.Id && recovery.Change.ParentFrameId == legacy.Id &&
            recovery.Continuation == PostEventContinuation.CardUse && recovery.Change.Kind == HpChangeKind.Recovery &&
            recovery.Change.HpBefore == 0 && recovery.Change.HpAfter == 1 && recovery.Change.Amount == 1,
            "The separately accepted legacy Wine owns its normalized action, exact suspended rescue program and direct HP completion; an ineligible new conversion cannot fabricate that typed return.");
        Private(g); g = Cold(g, r); Continue(g); ReachNative(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Require(V(g, 0).Hp == 1 && F<DrawFundedDistinctBasicPaidEvent>(g).Length == 0 && F<DrawFundedDistinctBasicIssuedEvent>(g).Length == 0,
            "Legacy self-rescue completes without issuing or paying a newly ineligible draw-funded basic card.");
        _ = Cold(g, r);
    }

    private static void ProgramReturn(string activation, DrawFundedDistinctBasicIntent intent)
    {
        var (g, r) = Create(peerBingxin: true); Play(g);
        Use(g, activation, (intent is DrawFundedDistinctBasicIntent.ProgramSlash or DrawFundedDistinctBasicIntent.AssistedSlash) ? [1] : []);
        if (intent == DrawFundedDistinctBasicIntent.AssistedSlash)
        {
            ReachNative(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger } && p.Choices.Any(c => c.Targets.SequenceEqual([0])));
            Answer(g, c => c.Targets.SequenceEqual([0]));
        }
        ReachNative(g, p => p is { PlayerSeat: 1, Kind: DecisionKind.ProgramTrigger } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("draw-funded-basic") == intent.ToString()));
        var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver);
        var targets = parent.SelectedTargetSeats.ToArray(); var cursor = parent.InstructionIndex;
        Require(V(g, 1).Hp == 4 && V(g, 1).HandCount == 4 && V(g, 1).Hand.All(c => c.Suit == Suit.Spade),
            "The actual requested actor, rather than the requesting skill owner, has the entire qualifying hand.");
        if (intent == DrawFundedDistinctBasicIntent.ProgramNearestSlash)
            Require(targets.Length == 0 && parent.ReexecuteParticipantInstruction &&
                parent.NumberBindings.Single(b => b.Name == "participant-cursor-" + (cursor - 1)).Value == 1,
                "The native nearest walker advances its participant cursor and retains the zero-target activation selection.");
        Private(g); g = Cold(g, r);
        UntilNative(g, e => F<DrawFundedDistinctBasicIssuedEvent>(e).Any(f => f.ActorSeat == 1 && f.Intent == intent) &&
            e.ResolutionStack.OfType<CardUseFrame>().Any(f => f.DrawFundedDistinctBasicUse?.Payment.Intent == intent && f.SourceSeat == 1));
        var issued = F<DrawFundedDistinctBasicIssuedEvent>(g).Single(f => f.ActorSeat == 1 && f.Intent == intent);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == issued.OwnerFrameId);
        var payment = use.DrawFundedDistinctBasicUse!.Payment;
        var frozenParent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == parent.Id);
        NeutralUse(use, payment.PaymentFrameId, intent);
        Require(payment.ParentFrameId == parent.Id && payment.RequestFrameId == parent.Id && payment.Cursor == cursor &&
            frozenParent.InstructionIndex == cursor && frozenParent.SelectedTargetSeats.SequenceEqual(targets) &&
            use.CardAttack?.ProgramSkillCardUseFrameId == parent.Id && payment.Source.OwnerSeat == 1 && V(g, 1).HandCount == 5,
            "The native AI requested actor pays one Draw and owns a true Slash with the original program instruction, actor and untouched parent selection.");
        g = Cold(g, r); ReachNative(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Once(g, payment, use.Id);
        Require(!g.ResolutionStack.Any(f => f.Id == parent.Id) &&
            F<DrawFundedDistinctBasicIssuedEvent>(g).Count(f => f.ActorSeat == 1 && f.Intent == intent) == 1,
            "The requested Slash returns through the native instruction once; the participant walker or choice result can finish without replacing its parent.");
        _ = Cold(g, r);
    }

    private static void ProvisionIsExcluded(CardKind kind)
    {
        var (g, r) = StartNative(kind); Play(g);
        if (kind == CardKind.ArrowBarrage)
            Accept(g, new UseProgramSkillCommand(0, NativeDriver, "arrows", V(g, 0).Hand.Take(2).Select(c => c.Id).ToArray(), [], g.Revision, P(g)!.PromptId));
        else
        {
            var action = g.GetHumanLegalActions().First(a => a.PlayedCardKind == kind && a.ConversionSource?.SkillId == NativeDriver && a.TargetSeats.SequenceEqual([1]));
            SubmitPlay(g, action);
        }
        ReachNative(g, p => p.PlayerSeat == 1 && p.IncomingCard == kind &&
            p.Kind == (kind == CardKind.Duel ? DecisionKind.RespondSlash : DecisionKind.RespondDodge));
        Require(V(g, 1).Hp == 4 && V(g, 1).HandCount == 4 && V(g, 1).Hand.All(c => c.Suit == Suit.Spade) &&
            P(g)!.Choices.All(c => !c.Parameters.ContainsKey("draw-funded-basic")) &&
            F<DrawFundedDistinctBasicStartedEvent>(g).Length == 0,
            "A genuinely qualified actor's Duel Slash provision or Arrow Barrage Dodge provision is not its own basic-card Use direction.");
        Frozen(P(g)!); g = Cold(g, r);
        ReachNative(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Require(F<DrawFundedDistinctBasicPaidEvent>(g).Length == 0 && F<DrawFundedDistinctBasicIssuedEvent>(g).Length == 0 &&
            F<CardUseFinishedEvent>(g).Any(f => f.CardKind == kind),
            "The excluded real native provision completes without a draw-funded cost or issuance.");
        _ = Cold(g, r);
    }

    private static void BorrowedSwordReturn()
    {
        var (g, r) = StartNative(CardKind.BorrowedSword); Play(g); Equip(g, 1);
        var weapon = V(g, 1).Equipment.Single().Id;
        var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.BorrowedSword &&
            a.ConversionSource?.SkillId == NativeDriver && a.TargetSeats.SequenceEqual([1, 2]));
        SubmitPlay(g, action);
        ReachNative(g, p => p is { PlayerSeat: 1, Kind: DecisionKind.RespondSlash } && p.Choices.Any(c => IsPaid(c, CardKind.Slash)));
        var outer = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.BorrowedSword);
        var window = g.ResolutionStack.OfType<ResponseWindowFrame>().Last(); g = Cold(g, r);
        ReachNative(g, p => p.SkillPrompt?.SkillId == Gain && p.PlayerSeat == 1);
        var paid = g.ResolutionStack.OfType<DrawFundedDistinctBasicFrame>().Single();
        var move = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
        Require(paid.Payment is { Intent: DrawFundedDistinctBasicIntent.BorrowedSword, Cursor: 0, ActualDrawCount: 1 } &&
            paid.Payment.Source.OwnerSeat == 1 && paid.Payment.TargetSeat == 2 && paid.Payment.ParentFrameId == outer.Id &&
            paid.Payment.RequestFrameId == window.Id && paid.Payment.ParentActionId == outer.Action!.ActionId &&
            paid.ActiveChildFrameId == move.Id && move.ResumeDrawFundedDistinctBasicFrameId == paid.Id &&
            g.ResolutionStack.Any(f => f.Id == window.Id) && F<DrawFundedDistinctBasicIssuedEvent>(g).Length == 0,
            "Borrowed Sword keeps the actual parent material, original weapon-owner response and exact once-paid gain child while its native request is suspended.");
        Private(g); Frozen(paid.OriginalDecision); g = Cold(g, r); Advance(g);
        ReachNative(g, p => p.SkillPrompt?.SkillId == Completed && p.PlayerSeat == 1);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.DrawFundedDistinctBasicUse?.Payment.PaymentFrameId == paid.Id);
        NeutralUse(use, paid.Id, DrawFundedDistinctBasicIntent.BorrowedSword);
        Require(use.SourceSeat == 1 && use.TargetSeats.SequenceEqual([2]) && use.Action!.ParentActionId == outer.Action!.ActionId &&
            g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == outer.Id) && V(g, 1).Equipment.Any(c => c.Id == weapon),
            "The issued zero-material Slash belongs to the designated equipped actor and the original Borrowed Sword action through native completion.");
        Private(g); g = Cold(g, r); Advance(g); ReachNative(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Once(g, paid.Payment, use.Id);
        Require(F<BorrowedSwordResolvedEvent>(g).Single(f => f.ResolutionId == outer.Id) is
            { UsedSlash: true, SlashCardId: 0, WeaponOwnerSeat: 1, SlashTargetSeat: 2, TransferredWeaponCardId: null } &&
            F<CardUseFinishedEvent>(g).Count(f => f.ResolutionId == outer.Id) == 1 && V(g, 1).Equipment.Any(c => c.Id == weapon),
            "The original compound card finishes once after its designated actor's true Slash, without transferring the weapon or repaying the draw.");
        _ = Cold(g, r);
    }

    private static void QinglongReturn(bool optionalCompleted = false)
    {
        var (g, r) = StartNative(CardKind.QinglongCrescentBlade, optionalCompleted); Play(g); Equip(g, 0);
        Require(V(g, 1).Hand.Any(c => c.Kind == CardKind.Dodge), "The fixed native Qinglong target actually holds a physical Dodge.");
        var first = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource?.SkillId == NativeDriver && a.TargetSeats.SequenceEqual([1]));
        SubmitPlay(g, first); ReachNative(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.QinglongCrescentBlade } && p.Choices.Any(c => IsPaid(c, CardKind.FireSlash)));
        var outer = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardAttack is not null);
        Require(outer.CardAttack!.SuccessfulDodgeResponses == 1 && outer.PhysicalCardIds!.Count == 1 &&
            V(g, 0).HandCount == V(g, 0).Hp && F<DrawFundedDistinctBasicPaidEvent>(g).Length == 0,
            "An actual physical first Slash consumes its own material and receives a real Dodge before the distinct Qinglong follow-up qualifies.");
        g = Cold(g, r); Answer(g, c => IsPaid(c, CardKind.FireSlash));
        ReachNative(g, p => p.SkillPrompt?.SkillId == Gain);
        var paid = g.ResolutionStack.OfType<DrawFundedDistinctBasicFrame>().Single();
        Require(paid.Payment is { Intent: DrawFundedDistinctBasicIntent.Qinglong, Cursor: 0, ActualDrawCount: 1 } &&
            paid.Payment.ParentFrameId == outer.Id && paid.Payment.RequestFrameId == outer.Id &&
            paid.Payment.ParentActionId == outer.Action!.ActionId && paid.Payment.TargetSeat == 1 &&
            g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == outer.Id).CardAttack!.SuccessfulDodgeResponses == 1,
            "The follow-up Draw suspends on the exact original outer Slash and its completed Dodge cursor, before any second Slash exists.");
        Private(g); g = Cold(g, r); Continue(g);
        ReachNative(g, p => p.SkillPrompt?.SkillId == Completed && g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.DrawFundedDistinctBasicUse is not null));
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.DrawFundedDistinctBasicUse?.Payment.PaymentFrameId == paid.Id);
        NeutralUse(use, paid.Id, DrawFundedDistinctBasicIntent.Qinglong);
        Require(use.Id != outer.Id && use.SourceSeat == 0 && use.TargetSeats.SequenceEqual([1]) &&
            F<QinglongCrescentBladeResolvedEvent>(g).Any(f => f.ResolutionId == outer.Id && f.Used && f.SlashCardIds.Count == 0 && f.EffectiveSlashKind == CardKind.FireSlash),
            "Qinglong issues a second distinct neutral FireSlash with a native zero-material follow-up fact anchored on the original outer Use.");
        Private(g); g = Cold(g, r);
        if (optionalCompleted)
        {
            var suspended = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == outer.Id);
            var saved = suspended.Continuations.QinglongFollowup?.Decision ??
                throw new InvalidOperationException("The paid Qinglong follow-up lost its saved outer completion prompt.");
            var window = g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(f => f.ParentFrameId == outer.Id);
            var windowIndex = g.ResolutionStack.ToList().FindIndex(f => f.Id == window.Id);
            Require(saved is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true, PlayerSeat: 0 } &&
                saved.SkillPrompt?.SkillId == Completed && saved.Choices.Count == 2 &&
                saved.Choices.Select(c => c.Parameters.GetValueOrDefault("program-action")).Order().SequenceEqual(["activate", "skip"]) &&
                suspended.Step == ResolutionFrameStep.Completed && !window.Activated &&
                g.ResolutionStack[windowIndex + 1].Id == use.Id &&
                !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.WindowContext?.ParentFrameId == window.Id) &&
                P(g)!.PromptId != saved.PromptId && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"),
                "The exact outer completion remains an unbound optional candidate with its original private prompt saved beneath the paid follow-up.");
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            ReachNative(g, p => p.SkillPrompt?.SkillId == Completed && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "choose-option"));
            Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Completed).WindowContext?.CardUse?.ParentCardUseFrameId == use.Id &&
                F<ProgramBindingStartedEvent>(g).Count(f => f.SkillId == Completed) == 1,
                "Activating the follow-up completion binds only the child Use while the original optional candidate remains unbound.");
            Private(g); g = Cold(g, r); Continue(g);
            var restored = P(g)!;
            Require(restored is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true, PlayerSeat: 0 } &&
                restored.PromptId == saved.PromptId && restored.Revision == g.Revision && restored.Prompt == saved.Prompt &&
                restored.SourceSeat == saved.SourceSeat && restored.TargetSeat == saved.TargetSeat &&
                restored.ValidCardIds.SequenceEqual(saved.ValidCardIds) && restored.ValidTargetSeats.SequenceEqual(saved.ValidTargetSeats) &&
                restored.ValidContentIds.SequenceEqual(saved.ValidContentIds) &&
                JsonSerializer.Serialize(restored.SkillPrompt) == JsonSerializer.Serialize(saved.SkillPrompt) &&
                JsonSerializer.Serialize(restored.Choices) == JsonSerializer.Serialize(saved.Choices) &&
                !g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == use.Id) &&
                !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Completed) &&
                F<ProgramBindingStartedEvent>(g).Count(f => f.SkillId == Completed) == 1,
                "The child returns before the exact saved outer optional prompt is restored at the current command revision, without activating or rebinding that original candidate.");
            Once(g, paid.Payment, use.Id);
            Private(g); g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            ReachNative(g, p => p.SkillPrompt?.SkillId == Completed && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "choose-option"));
            Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Completed).WindowContext?.CardUse?.ParentCardUseFrameId == outer.Id &&
                F<ProgramBindingStartedEvent>(g).Count(f => f.SkillId == Completed) == 2,
                "The restored original optional candidate binds once to its own outer Use only after the paid child has returned.");
            Private(g); g = Cold(g, r);
        }
        Continue(g); ReachNative(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Once(g, paid.Payment, use.Id);
        Require(F<CardUseFinishedEvent>(g).Count(f => f.ResolutionId == outer.Id) == 1 &&
            !g.ResolutionStack.OfType<CardUseFrame>().Any() && g.CardMovements.All(m => m.CardId != 0),
            "Both original and follow-up native continuations finish exactly once, restore the suspended outer completion and retain physical conservation.");
        _ = Cold(g, r);
    }

    private static void NeutralUse(CardUseFrame use, long paymentId, DrawFundedDistinctBasicIntent intent) => Require(
        use is { CardId: 0, PhysicalCardIds.Count: 0, Action.Type: CardActionType.Use, Action.PhysicalCards.Count: 0,
            Action.EffectiveSuit: Suit.None, Action.EffectiveRank: 0, Action.EffectiveIsRed: false } &&
        use.DrawFundedDistinctBasicUse is { } receipt && receipt.Payment.PaymentFrameId == paymentId && receipt.Payment.Intent == intent &&
        use.Action!.ConversionChain.SequenceEqual([receipt.Payment.Source]),
        "A true paid native basic-card Use retains its exact source receipt, neutral appearance and zero physical materials.");

    private static void Once(GameEngine g, DrawFundedDistinctBasicPayment payment, long ownerId) => Require(
        F<DrawFundedDistinctBasicPaidEvent>(g).Count(f => f.PaymentFrameId == payment.PaymentFrameId && f.ActualDrawCount == 1) == 1 &&
        F<DrawFundedDistinctBasicIssuedEvent>(g).Count(f => f.PaymentFrameId == payment.PaymentFrameId && f.OwnerFrameId == ownerId) == 1 &&
        F<DrawFundedDistinctBasicReturnedEvent>(g).Count(f => f.PaymentFrameId == payment.PaymentFrameId && f.OwnerFrameId == ownerId) == 1 &&
        g.CardMovements.Count(m => m.Sequence > payment.SequenceBefore && m.Sequence <= payment.SequenceAfter &&
            m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(payment.Source.OwnerSeat) && m.Reason.Value == "skill-program.draw-funded-distinct-basic.draw") == 1 &&
        g.CardMovements.All(m => m.CardId != 0),
        "One actual native draw issues and returns the same owning basic Use once across cold restore and typed parent completion.");

    private static void ReachNative(GameEngine g, Func<PendingDecision, bool> predicate) => UntilNative(g, e => P(e) is { } p && predicate(p));
    private static void UntilNative(GameEngine g, Func<GameEngine, bool> predicate)
    {
        for (var i = 0; i < 240; i++) { if (predicate(g)) return; Advance(g); }
        throw new InvalidOperationException("The fixed response fixture did not reach its real boundary: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack }));
    }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.QinglongCrescentBlade }) Answer(g, c => c.Parameters.GetValueOrDefault("action") == "qinglong-skip");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying })
            Answer(g, c => c.Cards.Count == 0 && !c.Parameters.ContainsKey("draw-funded-basic") &&
                (c.Parameters.GetValueOrDefault("response") is "take-damage" or "pass" or "let-die" ||
                 c.Parameters.GetValueOrDefault("action") == "pass"));
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }

    private const string NativeDriver = "fixture:dfb-response-driver";
    private static (GameEngine, ContentRegistry) StartNative(CardKind direction, bool optionalCompleted = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new NativeFixture(direction, optionalCompleted));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = NativeFixture.ModeId(direction), UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand()); ReachNative(game, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Require(P(game)!.ValidContentIds.Contains("fixture:dfb-response-owner"), "The fixed response fixture publishes its real owner choice.");
        Accept(game, new SelectGeneralCommand(0, "fixture:dfb-response-owner", game.Revision, P(game)!.PromptId));
        return (game, registry);
    }
    private static void Equip(GameEngine g, int seat)
    {
        Accept(g, new UseProgramSkillCommand(0, NativeDriver, "equip", [], [seat], g.Revision, P(g)!.PromptId));
        ReachNative(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Require(V(g, seat).Equipment.Count == 1, "A real native deck equipment Use equipped the intended participant without touching its hand.");
    }

    private sealed class NativeFixture(CardKind direction, bool optionalCompleted) : IGameContentPackage
    {
        internal static string ModeId(CardKind kind) => "identity:classic-draw-funded-response-" + kind;
        public PackageManifest Manifest { get; } = new("fixture:draw-funded-response", "1.0.0", "真实响应与原生武器回返");
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-wang-xiang.rules.json")!);
            using var labels = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-wang-xiang.presentation.json")!);
            var production = SkillProgramCatalog.Load(rules.ReadToEnd(), labels.ReadToEnd());
            b.AddSkill(new(Bingxin, "冰心", "完整实际摸牌共享能力") { Program = production.Programs[Bingxin] });
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NativeDriver}}","revision":1,"viewAs":[
                {"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false},
                {"id":"borrowed","inputKinds":[],"inputSuits":[],"outputKind":"borrowedSword","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true},
                {"id":"duel","inputKinds":[],"inputSuits":[],"outputKind":"duel","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true},
                {"id":"arrows","inputKinds":[],"inputSuits":[],"inputCount":2,"sameSuit":true,"outputKind":"arrowBarrage","forPlay":true,"forResponse":false}],
                "activations":[
                  {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]},
                  {"id":"arrows","minCards":2,"maxCards":2,"sourceZones":["hand"],"selectedCardsSameSuit":true,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"owner","sourceBind":"arrows","outputKind":"arrowBarrage"}]}]},
              {"id":"{{Gain}}","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.draw-funded-distinct-basic.draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"{{Completed}}","revision":1,"triggers":[{"id":"done","window":"cardUseCompleted","optional":{{(optionalCompleted ? "true" : "false")}},"ownerRelation":"actor","includeResponseUses":true,"cardKinds":["slash","fireSlash","thunderSlash","dodge","peach"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            {
                [NativeDriver] = new { name = "实际实体准备", description = "实体转换和真实武器装备" },
                [Gain] = new { name = "实际支付摸牌", description = "真实取得暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Completed] = new { name = "实际使用完成", description = "原生完成暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            } }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "真实原生响应夹具") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:dfb-response-selection", "固定真实候选", "固定小夹具") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddCard(new("fixture:dfb-response-borrowed", "借刀杀人", "锦囊牌", "真实原生复合目标", LegacyKind: CardKind.BorrowedSword));
            b.AddCard(new("fixture:dfb-response-qinglong", "青龙偃月刀", "装备牌", "真实原生后续杀", LegacyKind: CardKind.QinglongCrescentBlade));
            b.AddGeneral(new("fixture:dfb-response-owner", "真实响应拥有者", "supporter", Bingxin, "jin",
                direction == CardKind.QinglongCrescentBlade ? 4 : 5, [NativeDriver, Gain, Completed], GeneralGender.Male));
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new($"fixture:dfb-response-peer-{seat}", "真实响应目标", "supporter",
                direction == CardKind.QinglongCrescentBlade ? "fixture:dfb-response-selection" : Bingxin, "qun", 4,
                direction == CardKind.BorrowedSword && seat == 1 ? [Gain, Completed] : [], GeneralGender.Male));
            var responseCard = direction == CardKind.Duel ? "standard:slash" : "standard:dodge";
            var deck = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(responseCard, Suit.Spade, i % 13 + 1));
            if (direction is CardKind.BorrowedSword or CardKind.QinglongCrescentBlade)
                deck = deck.Concat(Enumerable.Range(0, 20).Select(i => new ContentDeckPhysicalCard(
                    direction == CardKind.BorrowedSword ? "standard:crossbow" : "fixture:dfb-response-qinglong", Suit.Spade, i % 13 + 1)));
            b.AddDeck(new("fixture:dfb-response-deck", "固定黑色真实实体", 4, 2, []) { PhysicalCards = deck.ToArray() });
            b.AddMode(new(ModeId(direction), "真实原生响应", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:dfb-response-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:dfb-response-owner", "fixture:dfb-response-peer-1", "fixture:dfb-response-peer-2", "fixture:dfb-response-peer-3"]));
        }
    }
}
