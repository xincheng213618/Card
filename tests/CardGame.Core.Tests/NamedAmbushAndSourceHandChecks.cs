using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class NamedAmbushAndSourceHandChecks
{
    private const string Shefu = "ol:shefu", Benyu = "ol:benyu", Driver = "fixture:ambush-driver", Mode = "identity:classic-ambush";
    public static void PrivateNamesAndAllTargetUseStateSurviveColdRecovery()
    {
        var (g, registry) = Start(CardKind.BarbarianAssault);
        Deposit(ref g, registry, 0, CardKind.BarbarianAssault);
        var d = E<NamedAmbushDepositedEvent>(g).Single().Deposit;
        var own = V(g, 0).NamedAmbushPiles!.Single(); var foreign = g.CreateSnapshot(1).Players[0].NamedAmbushPiles!.Single();
        Need(own.Count == 1 && own.Entries is [{ RecordedKind: CardKind.BarbarianAssault }] && own.Entries[0].Card.Id == d.CardId &&
            foreign.Count == 1 && foreign.Entries is null && !g.CreateSnapshot(1).Players[0].Hand.Any(c => c.Id == d.CardId),
            "Only the owner sees each recorded name and private entity; other viewers see only the pile count.");
        g = Cold(g, registry);
        Reach(g, () => Optional(P(g), Shefu)); Activate(g); Reach(g, () => Branch(P(g), "named-ambush", "remove"));
        var window = g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last(); var id = window.ParentFrameId; var before = g.CreateSnapshot(0).Players.Select(p => p.Hp).ToArray();
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "remove");
        Reach(g, () => E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == id));
        Need(E<NamedAmbushUseInvalidatedEvent>(g).Count(e => e.Use.ParentFrameId == id) == 1 && E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == id) == 1 &&
            g.CreateSnapshot(0).Players.Select(p => p.Hp).SequenceEqual(before) &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == d.CardId).Location == CardLocation.DiscardPile,
            "One real hand-use state cancels all actual AOE targets and retains one paid entity and one use completion."); Cold(g, registry);
    }
    public static void DelayedAndAlcoholUsesFinishWithoutEffects()
    {
        foreach (var kind in new[] { CardKind.Indulgence, CardKind.SupplyShortage, CardKind.Lightning, CardKind.Alcohol, CardKind.DrawTwo })
        {
            var (g, registry) = Start(kind, quiet: true); Deposit(ref g, registry, 1, kind); Play(g);
            var play = g.GetHumanLegalActions().First(a => a.Kind == Enum.Parse<LegalActionKind>(kind.ToString()) && a.CardId is not null);
            Accept(g, new PlayCardCommand(0, play.CardId!.Value, play.TargetSeats, g.Revision, P(g)!.PromptId, play.PlayedCardKind, play.TargetCardId));
            Reach(g, () => Optional(P(g), Shefu)); Activate(g); Reach(g, () => Branch(P(g), "named-ambush", "remove"));
            var window = g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last(); var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == window.ParentFrameId);
            var hand = V(g, use.SourceSeat).HandCount; g = Cold(g, registry);
            Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "remove"); Reach(g, () => E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == use.Id));
            Need(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
                g.CreateCardZoneDiagnostics().Single(c => c.CardId == use.CardId).Location == CardLocation.DiscardPile &&
                !E<DelayedCardPlacedEvent>(g).Any(e => e.ResolutionId == use.Id), "Invalidated real delayed cards never enter judgment and every use finishes once.");
            if (kind == CardKind.Alcohol) Need(!V(g, use.SourceSeat).HasAlcoholEffect, "Canceled real Alcohol retains its committed use without applying the drink effect.");
            if (kind == CardKind.DrawTwo) Need(V(g, use.SourceSeat).HandCount == hand, "Implicit self-target DrawTwo does not draw after whole-use cancellation.");
            Cold(g, registry);
        }
    }
    public static void DodgeAndCounterspellRetainRealCompletedUse()
    {
        foreach (var kind in new[] { CardKind.Dodge, CardKind.Nullification })
        {
            var (g, registry) = Start(CardKind.Slash, mixed: true); Use(g, "draw", []); Play(g);
            var owner = 1;
            Deposit(ref g, registry, owner, kind);
            Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.Kind == (kind == CardKind.Dodge ? DecisionKind.RespondDodge : DecisionKind.Nullification) &&
                p.Choices.Any(c => c.Cards.Count > 0 && c.Cards.All(id => g.CreateCardZoneDiagnostics().Single(z => z.CardId == id).CardKind == kind)));
            var response = P(g)!; var responder = response.PlayerSeat; var hp = V(g, responder).Hp;
            var nullBefore = g.ResolutionStack.OfType<NullificationWindowFrame>().LastOrDefault()?.EffectNullified;
            Answer(g, c => c.Cards.Count > 0 && c.Cards.All(id => g.CreateCardZoneDiagnostics().Single(z => z.CardId == id).CardKind == kind));
            Reach(g, () => Optional(P(g), Shefu)); Activate(g); Reach(g, () => Branch(P(g), "named-ambush", "remove"));
            var action = g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last().Action;
            g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "remove");
            Reach(g, () => E<NamedAmbushUseInvalidatedEvent>(g).Any(e => e.Use.ActionId == action.ActionId) &&
                !g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(w => w.Action.ActionId == action.ActionId));
            Need(E<CardActionAcceptedEvent>(g).Any(e => e.Action.ActionId == action.ActionId) &&
                E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == Shefu && e.Completed) >= 2 &&
                action.PhysicalCards.All(c => g.CreateCardZoneDiagnostics().Single(z => z.CardId == c.CardId).Location == CardLocation.DiscardPile),
                "A canceled response remains a real accepted, once-paid use rather than a fabricated missing response.");
            if (kind == CardKind.Dodge) Reach(g, () => V(g, responder).Hp < hp);
            else Need(g.ResolutionStack.OfType<NullificationWindowFrame>().LastOrDefault()?.EffectNullified == nullBefore ||
                E<NullificationRespondedEvent>(g).Last().EffectNullified == nullBefore, "An invalidated counterspell does not flip the original chain boolean.");
            Cold(g, registry);
        }
    }
    public static void SourceHandChoicesKeepCurrentTwoBranchesAndZeroDraw()
    {
        foreach (var keep in new[] { 2, 5, 8 })
        {
            var (g, registry) = Start(CardKind.Slash, benyu: true); Use(g, "draw", []); Play(g);
            Use(g, "trim", V(g, 0).Hand.Skip(keep).Select(c => c.Id).ToArray()); Play(g); End(g);
            Reach(g, () => Optional(P(g), Benyu)); Activate(g); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "damage-source-hand") == true);
            var r = g.ResolutionStack.OfType<ProgramSkillFrame>().Last().DamageSourceHandChoice!;
            Need(P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "draw") &&
                P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "retaliate") == (keep > r.SourceHandCount),
                "The current sourced damage offers draw regardless of the old hand-comparison split, and retaliation uses frozen X plus one hand cards.");
            var id = g.ResolutionStack.OfType<ProgramSkillFrame>().Last().Id;
            g = Cold(g, registry);
            if (keep <= 5)
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "draw"); Reach(g, () => E<DamageSourceHandChoiceCompletedEvent>(g).Any(e => e.ProgramFrameId == id));
                var paid = E<DamageSourceHandChoicePaidEvent>(g).Single(e => e.ProgramFrameId == id).Receipt;
                Need(paid.Draw && paid.DrawActual == Math.Max(0, Math.Min(5, r.SourceHandCount) - keep) && V(g, 0).HandCount == keep + paid.DrawActual,
                    "Capped refill and an equal-hand zero draw both complete as actual chosen activations.");
            }
            else
            {
                var before = V(g, r.DamageSourceSeat).Hp;
                Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "retaliate"); Reach(g, () => Branch(P(g), "damage-source-hand", "card"));
                for (var i = 0; i < r.SourceHandCount + 2; i++) Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "card");
                g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "pay");
                Reach(g, () => E<DamageSourceHandChoiceCompletedEvent>(g).Any(e => e.ProgramFrameId == id));
                var paid = E<DamageSourceHandChoicePaidEvent>(g).Single(e => e.ProgramFrameId == id).Receipt;
                Need(!paid.Draw && paid.SelectedCards.Count == r.SourceHandCount + 2 && V(g, r.DamageSourceSeat).Hp == before - 1 &&
                    g.CardMovements.Count(m => m.Sequence > paid.MovementBefore && m.Sequence <= paid.MovementAfter && m.Reason.Value == "skill-program.damage-source-hand.discard") == paid.SelectedCards.Count,
                    "The at-least payment can exceed X plus one and produces one true retaliatory damage after its exact hand cost.");
            }
            Cold(g, registry);
        }
    }
    public static void PaidSourceLossAndZeroEntityEligibilityAreExact()
    {
        var (g, registry) = Start(CardKind.Slash, sourceLoss: true); Deposit(ref g, registry, 0, CardKind.Slash);
        Reach(g, () => Optional(P(g), Shefu)); Activate(g); Reach(g, () => Branch(P(g), "named-ambush", "remove"));
        var action = g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last().Action;
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "remove");
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == "fixture:ambush-replace" && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(g, () => E<NamedAmbushUseInvalidatedEvent>(g).Any(e => e.Use.ActionId == action.ActionId));
        Need(E<NamedAmbushRemovedEvent>(g).Count(e => e.Use?.ActionId == action.ActionId && e.Reason == "used") == 1 &&
            !V(g, 0).Skills!.Any(s => s.Id == Shefu), "Already-paid ambush completes once after its source is genuinely removed during its movement child.");
        Cold(g, registry);
        var (dead, dr) = Start(CardKind.Slash, sourceLoss: true, deathOwner: true);
        Deposit(ref dead, dr, 1, CardKind.Slash); Reach(dead, () => Optional(P(dead), Shefu)); Activate(dead);
        Reach(dead, () => Branch(P(dead), "named-ambush", "remove"));
        var paidFrame = dead.ResolutionStack.OfType<ProgramSkillFrame>().Last().Id;
        var deadAction = dead.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last().Action;
        dead = Cold(dead, dr); Answer(dead, c => c.Parameters.GetValueOrDefault("branch") == "remove");
        Reach(dead, () => E<ProgramBindingResolvedEvent>(dead).Any(e => e.FrameId == paidFrame));
        Need(!V(dead, 1).IsAlive && dead.State.Winner == Winner.None &&
            E<NamedAmbushUseInvalidatedEvent>(dead).Count(e => e.Use.ActionId == deadAction.ActionId) == 1,
            "An already-paid removal completes its ineffective state after the non-Lord owner really dies while the game continues."); Cold(dead, dr);
        var (zero, zr) = Start(CardKind.Slash, zero: true); Deposit(ref zero, zr, 0, CardKind.Slash);
        Reach(zero, () => Optional(P(zero), "fixture:ambush-zero")); Activate(zero);
        Reach(zero, () => P(zero)?.SkillPrompt?.SkillId == "fixture:ambush-zero" && P(zero)!.Choices.Any(c => c.Targets.Count == 1));
        var zeroChoice = P(zero)!.Choices.First(c => c.Targets.Count == 1); var zeroActor = P(zero)!.PlayerSeat;
        Need(zeroActor != 0 && zeroChoice.Targets.Single() != zeroActor,
            "The actual published foreign zero-use target is legal: " + JsonSerializer.Serialize(P(zero)));
        Answer(zero, c => c.Id == zeroChoice.Id); Reach(zero, () => E<CardUseFinishedEvent>(zero).Any(e => e.CardId == 0));
        var zeroFinished = E<CardUseFinishedEvent>(zero).Single(e => e.CardId == 0);
        Need(E<CardActionAcceptedEvent>(zero).Any(e => e.Action.ActorSeat == zeroActor && e.Action.PhysicalCards.Count == 0 && e.Action.Type == CardActionType.Use) &&
            E<CardUseFinishedEvent>(zero).Count(e => e.ResolutionId == zeroFinished.ResolutionId) == 1 &&
            !E<NamedAmbushUseInvalidatedEvent>(zero).Any() && V(zero, 0).NamedAmbushPiles!.Single().Count == 1,
            "A real zero-entity Slash completes without consuming a hand-card ambush."); Cold(zero, zr);
    }
    public static void MultipleDepositsTrueSourceLossCleansOnce()
    {
        var (cleanup, cr) = Start(CardKind.Slash, sourceLoss: true);
        Deposit(ref cleanup, cr, 0, CardKind.Duel); Play(cleanup); End(cleanup);
        Reach(cleanup, () => Optional(P(cleanup), Shefu) && P(cleanup)!.PlayerSeat == 0); Activate(cleanup);
        Reach(cleanup, () => Branch(P(cleanup), "named-ambush", "card")); Answer(cleanup, c => c.Parameters.GetValueOrDefault("branch") == "card");
        Reach(cleanup, () => Branch(P(cleanup), "named-ambush", "name")); Answer(cleanup, c => c.Parameters.GetValueOrDefault("recorded-kind") == nameof(CardKind.Duel));
        Reach(cleanup, () => E<NamedAmbushDepositedEvent>(cleanup).Count(e => e.Deposit.Source.OwnerSeat == 0) == 2); Play(cleanup);
        var deposits = E<NamedAmbushDepositedEvent>(cleanup).Where(e => e.Deposit.Source.OwnerSeat == 0).Select(e => e.Deposit).ToArray();
        Need(V(cleanup, 0).NamedAmbushPiles!.Single().Count == 2, "Current repeated names have two distinct actual deposit identities before true source loss.");
        Use(cleanup, "hurt", []); Reach(cleanup, () => P(cleanup)?.SkillPrompt?.SkillId == "fixture:ambush-replace" &&
            P(cleanup)!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        cleanup = Cold(cleanup, cr); Answer(cleanup, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(cleanup, () => !V(cleanup, 0).Skills!.Any(s => s.Id == Shefu));
        Need(V(cleanup, 0).NamedAmbushPiles is null && deposits.All(d => cleanup.CreateCardZoneDiagnostics().Single(z => z.CardId == d.CardId).Location == CardLocation.DiscardPile) &&
            deposits.All(d => E<NamedAmbushRemovedEvent>(cleanup).Count(e => e.Deposit.DepositId == d.DepositId && e.Reason == "source-lost") == 1),
            "True loss atomically clears every exact deposit once, including reentrant movement rule notifications."); Cold(cleanup, cr);
    }
    public static void ConsumedEntityCanBeRecordedAgainWithNewName()
    {
        var (g, registry) = Start(CardKind.Slash, reuse: true); Deposit(ref g, registry, 0, CardKind.Slash);
        var first = E<NamedAmbushDepositedEvent>(g).Single().Deposit;
        Reach(g, () => Optional(P(g), Shefu)); Activate(g); Reach(g, () => Branch(P(g), "named-ambush", "remove"));
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "remove");
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == "fixture:ambush-gift" && P(g)!.Choices.Any(c => c.Targets.SequenceEqual([0])));
        Answer(g, c => c.Targets.SequenceEqual([0]));
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == "fixture:ambush-gift" && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-source-card"));
        var giftOwner = P(g)!.PlayerSeat; var slot = V(g, giftOwner).Hand.Select((c, index) => (c, index)).Single(x => x.c.Id == first.CardId).index;
        Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Play(g); Need(V(g, 0).Hand.Any(c => c.Id == first.CardId), "The consumed entity was really claimed from discard by another owner and gifted back to hand.");
        End(g); Reach(g, () => Optional(P(g), Shefu)); Activate(g); Reach(g, () => Branch(P(g), "named-ambush", "card"));
        Answer(g, c => c.Cards.SequenceEqual([first.CardId])); Reach(g, () => Branch(P(g), "named-ambush", "name"));
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("recorded-kind") == nameof(CardKind.Duel));
        Reach(g, () => E<NamedAmbushDepositedEvent>(g).Count() == 2);
        var view = V(g, 0).NamedAmbushPiles!.Single();
        Need(view.Count == 1 && view.Entries is [{ RecordedKind: CardKind.Duel }] && view.Entries[0].Card.Id == first.CardId &&
            E<NamedAmbushDepositedEvent>(g).Select(e => e.Deposit.DepositId).Distinct().Count() == 2,
            "The exact consumed deposit stays consumed when the same physical entity returns as a new differently named deposit."); Cold(g, registry);
    }
    public static void PaidSourceHandRetaliationSurvivesSkillLossAndOwnerDeath()
    {
        foreach (var deadOwner in new[] { false, true })
        {
            var (g, registry) = Start(CardKind.Slash, benyu: !deadOwner, sourceLoss: true, deathOwner: deadOwner, benyuPeer: deadOwner);
            if (!deadOwner)
            {
                Use(g, "draw", []); Play(g); Use(g, "trim", V(g, 0).Hand.Skip(8).Select(c => c.Id).ToArray()); Play(g); End(g);
            }
            else
            {
                Use(g, "trim", V(g, 0).Hand.Skip(1).Select(c => c.Id).ToArray()); Play(g);
                var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
                Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId));
            }
            Reach(g, () => Optional(P(g), Benyu)); Activate(g); Reach(g, () => Branch(P(g), "damage-source-hand", "retaliate"));
            var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(); var r = frame.DamageSourceHandChoice!; var hp = V(g, r.DamageSourceSeat).Hp;
            Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "retaliate"); Reach(g, () => Branch(P(g), "damage-source-hand", "card"));
            for (var i = 0; i < r.SourceHandCount + 1; i++) Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "card");
            g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "pay");
            if (!deadOwner)
            {
                Reach(g, () => P(g)?.SkillPrompt?.SkillId == "fixture:ambush-replace" && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
                g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            }
            Reach(g, () => E<DamageSourceHandChoiceCompletedEvent>(g).Any(e => e.ProgramFrameId == frame.Id));
            var paid = E<DamageSourceHandChoicePaidEvent>(g).Single(e => e.ProgramFrameId == frame.Id).Receipt;
            Need(g.State.Winner == Winner.None && (deadOwner ? !V(g, frame.OwnerSeat).IsAlive : !V(g, frame.OwnerSeat).Skills!.Any(s => s.Id == Benyu)) &&
                V(g, r.DamageSourceSeat).Hp == hp - 1 && g.CardMovements.Count(m => m.Sequence > paid.MovementBefore && m.Sequence <= paid.MovementAfter &&
                    m.Reason.Value == "skill-program.damage-source-hand.discard") == r.SourceHandCount + 1 &&
                E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == frame.OwnerSeat && e.TargetSeat == r.DamageSourceSeat && e.Amount == 1),
                "One paid hand retaliation continues with its real source after skill loss or non-Lord owner death, and never repeats its cost. " +
                JsonSerializer.Serialize(new { deadOwner, frame.OwnerSeat, ownerAlive = V(g, frame.OwnerSeat).IsAlive,
                    ownerSkills = V(g, frame.OwnerSeat).Skills?.Select(s => s.Id), sourceSeat = r.DamageSourceSeat, hpBefore = hp, hpAfter = V(g, r.DamageSourceSeat).Hp,
                    X = r.SourceHandCount, paid.SelectedCards, paid.MovementBefore, paid.MovementAfter,
                    actualMoves = g.CardMovements.Where(m => m.Sequence > paid.MovementBefore && m.Sequence <= paid.MovementAfter),
                    damages = E<DamageAppliedEvent>(g), completed = E<DamageSourceHandChoiceCompletedEvent>(g), winner = g.State.Winner })); Cold(g, registry);
        }
    }
    public static void InvalidatedUseRetainsStateAfterRealActorReplacement()
    {
        var (g, registry) = Start(CardKind.Duel, quiet: true, actorSwap: true);
        Deposit(ref g, registry, 1, CardKind.Duel); Play(g);
        string? firstObserver = null, secondObserver = null; var frozenCollections = 0;
        g.EventCommitted += envelope =>
        {
            if (envelope.Payload is not NamedAmbushOfferedEvent { Receipt.OriginalAction: { } original }) return;
            firstObserver = JsonSerializer.Serialize(original);
            if (RejectsMutation(original.TargetSeats)) frozenCollections++;
            if (RejectsMutation(original.PhysicalCards)) frozenCollections++;
            if (RejectsMutation(original.ConversionChain)) frozenCollections++;
        };
        g.EventCommitted += envelope =>
        { if (envelope.Payload is NamedAmbushOfferedEvent { Receipt.OriginalAction: { } original }) secondObserver = JsonSerializer.Serialize(original); };
        var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Duel && a.TargetSeats.SequenceEqual([1]));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId));
        Reach(g, () => Optional(P(g), Shefu)); Activate(g); Reach(g, () => Branch(P(g), "named-ambush", "remove"));
        var useId = g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last().ParentFrameId;
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "remove");
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == "fixture:ambush-swap" && P(g)!.Choices.Any(c => c.Targets.SequenceEqual([2])));
        g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([2]));
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == "fixture:ambush-swap" && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == useId);
        Need(use.SourceSeat == 2 && use.Action!.ActorSeat == 2 && use.Action.ProviderSeat == 0 && use.NamedAmbushInvalidation!.Use.ActorSeat == 0 &&
            E<ProgramCardUseActorReplacedEvent>(g).Count(e => e.CardUseFrameId == useId && e.PreviousActorSeat == 0 && e.ActorSeat == 2) == 1,
            "The real finalized role-change occurs after the committed paid ambush, preserving the actual provider and action ID.");
        Need(frozenCollections == 3 && firstObserver is not null && firstObserver == secondObserver &&
            firstObserver == JsonSerializer.Serialize(E<NamedAmbushOfferedEvent>(g).Single(e => e.Receipt.Use?.ParentFrameId == useId).Receipt.OriginalAction),
            "The original paid action's targets, materials and conversion chain reject observer mutation, and later observers and cold history retain the same issuance.");
        var hp = V(g, 1).Hp; g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(g, () => E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == useId));
        Need(V(g, 1).Hp == hp && E<NamedAmbushUseInvalidatedEvent>(g).Count(e => e.Use.ParentFrameId == useId) == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == useId) == 1,
            "A later genuine actor replacement cannot revoke the paid ineffective state or repeat its private-pile cost."); Cold(g, registry);
    }
    private static (GameEngine, ContentRegistry) Start(CardKind kind, bool mixed = false, bool benyu = false, bool sourceLoss = false, bool zero = false, bool reuse = false, bool deathOwner = false, bool quiet = false, bool benyuPeer = false, bool actorSwap = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(kind, mixed, benyu, sourceLoss, zero, reuse, deathOwner, quiet, benyuPeer, actorSwap));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, "fixture:ambush-owner", g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private static void Deposit(ref GameEngine g, ContentRegistry registry, int owner, CardKind name)
    {
        var engine = g; End(engine); Reach(engine, () => Optional(P(engine), Shefu) && P(engine)!.PlayerSeat == owner);
        Activate(engine); Reach(engine, () => Branch(P(engine), "named-ambush", "card"));
        engine = Cold(engine, registry); Answer(engine, c => c.Parameters.GetValueOrDefault("branch") == "card");
        Reach(engine, () => Branch(P(engine), "named-ambush", "name")); Answer(engine, c => c.Parameters.GetValueOrDefault("recorded-kind") == name.ToString());
        Reach(engine, () => E<NamedAmbushDepositedEvent>(engine).Any(e => e.Deposit.Source.OwnerSeat == owner)); g = engine;
    }
    private static PlayerSnapshot V(GameEngine g, int s) => g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Branch(PendingDecision? p, string action, string branch) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action && c.Parameters.GetValueOrDefault("branch") == branch) == true;
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string activation, IReadOnlyList<int> cards)
    { if (activation == "trim" && cards.Count == 0) return; Accept(g, new UseProgramSkillCommand(0, Driver, activation, cards, [], g.Revision, P(g)!.PromptId)); }
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<bool> stop)
    {
        for (var i = 0; i < 300; i++)
        {
            if (stop()) return; var p = P(g);
            if (g.State.Winner != Winner.None) throw new InvalidOperationException("Ambush fixture ended before boundary: " + JsonSerializer.Serialize(p));
            if (p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) End(g);
            else if (p is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p?.Choices.Count > 0 && (p.PlayerSeat == 0 || p.Kind == DecisionKind.ProgramTrigger))
            {
                var c = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip") ??
                    p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "decline") ??
                    p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die") ??
                    p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option-id") == "continue") ?? p.Choices[0];
                Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c.Id, g.Revision));
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Ambush exceeded bounded real prompts: " + JsonSerializer.Serialize(new { prompt = P(g), stack = g.ResolutionStack, events = E<NamedAmbushDepositedEvent>(g) }));
    }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected actual ambush command."); }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s)) == SnapshotJson.Serialize(copy.CreateSnapshot(s))) &&
            JsonSerializer.Serialize(g.ResolutionStack) == JsonSerializer.Serialize(copy.ResolutionStack) && JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(copy.CardMovements),
            "All viewer-filtered private choices, exact receipts and movements restore cold unchanged."); return copy;
    }
    private static void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why); }
    private static bool RejectsMutation<T>(IReadOnlyList<T> values)
    { if (values is not IList<T> list) return false; try { list.Add(default!); } catch (NotSupportedException) { return true; } return false; }
    private sealed class Fixture(CardKind kind, bool mixed, bool benyu, bool sourceLoss, bool zero, bool reuse, bool deathOwner, bool quiet, bool benyuPeer, bool actorSwap) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ambush", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-cheng-yu.rules.json")!;
            using var reader = new StreamReader(stream); using var ls = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-cheng-yu.presentation.json")!;
            using var lr = new StreamReader(ls); RegisterCatalog(SkillProgramCatalog.Load(reader.ReadToEnd(), lr.ReadToEnd()));
            void RegisterCatalog(SkillProgramCatalog catalog)
            { foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] }); }
            void Add(string id, string body, bool label = false)
            {
                var rule = "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"" + id + "\",\"revision\":1," + body + "}]}";
                var entry = new Dictionary<string, object> { ["name"] = id, ["description"] = "Actual ambush fixture" };
                if (label) entry["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [id] = entry } });
                RegisterCatalog(SkillProgramCatalog.Load(rule, presentation));
            }
            Add(Driver, """
            "activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":12}]},
             {"id":"trim","minCards":1,"maxCards":16,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"trimmed"},{"op":"moveBoundCards","target":"owner","sourceBind":"trimmed","destination":"discardPile","awaitMovementTriggers":true}]},
             {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]
            """);
            if (reuse)
            {
                Add("fixture:ambush-recycle", """
                "triggers":[{"id":"recycle","window":"discardPileReceived","subject":"owner","movementReasons":["skill-program.named-ambush.remove"],"optional":false,"effects":[{"op":"claimMovedCards","target":"owner"}]}]
                """);
                Add("fixture:ambush-gift", """
                "triggers":[{"id":"return-recovered","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.fixture:ambush-recycle.ClaimMovedCards"],"optional":false,
                "effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"selectSourceCard","target":"owner","cardSource":"owner","zones":["hand"],"resultBind":"recovered"},{"op":"moveBoundCards","target":"owner","sourceBind":"recovered","destination":"selectedTargetHand"}]}]
                """);
            }
            if (zero) Add("fixture:ambush-zero", """
            "triggers":[{"id":"zero","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]}]
            """);
            if (quiet)
            {
                var body = JsonSerializer.Serialize(new { triggers = new[] { new { id = "quiet-real-use", window = "drawPhaseStarting", subject = "owner", optional = false,
                    effects = new[] { new { op = "grantTurnCardActionProhibition", target = "owner", cardKinds = new[] { JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()) }, actionTypes = new[] { "use" } } } } } });
                Add("fixture:ambush-quiet", body[1..^1]);
            }
            if (actorSwap) Add("fixture:ambush-swap", """
            "triggers":[{"id":"real-finalized-actor-swap","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["duel"],"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"},{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]
            """, true);
            if (sourceLoss)
            {
                Add("fixture:ambush-loss", """
                "triggers":[{"id":"lose","window":"cardsMoved","subject":"owner","sourceZones":["privatePersistentPile"],"movementOccurrence":"perBatch","movementReasons":["skill-program.named-ambush.remove"],"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":20}]},
                {"id":"lose-after-hand-payment","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.damage-source-hand.discard"],"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]
                """);
                Add("fixture:ambush-replace", """
                "triggers":[{"id":"replacement","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":10,"clampToMaxHp":true},{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:shefu","ol:benyu"],"sourceBind":"fixture:ambush-spent"}]}]
                """, true); b.AddSkill(new("fixture:ambush-spent", "Spent", "Actually removed source"));
            }
            b.AddSkill(new("fixture:ambush-peer", "Peer", "Fixed role choices") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -10000d : 10000d) });
            b.AddGeneral(new("fixture:ambush-owner", "Ambush owner", "supporter", Driver, "wei", 12,
                [Shefu, .. (benyu ? new[] { Benyu } : []), .. (sourceLoss && !deathOwner ? new[] { "fixture:ambush-loss", "fixture:ambush-replace" } : []),
                    .. (actorSwap ? new[] { "fixture:ambush-swap" } : [])]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:ambush-peer-{i}", "Actual peer", "supporter", "fixture:ambush-peer", "wei", 12,
                [Shefu, .. (zero ? new[] { "fixture:ambush-zero" } : []), .. (reuse && i == 1 ? new[] { "fixture:ambush-recycle", "fixture:ambush-gift" } : []),
                    .. (sourceLoss && deathOwner ? new[] { "fixture:ambush-loss" } : []), .. (quiet ? new[] { "fixture:ambush-quiet" } : []), .. (benyuPeer ? new[] { Benyu } : [])]));
            var cards = mixed ? new[] { "standard:slash", "standard:dodge", "standard:duel", "standard:nullification", "standard:slash" } : [kind switch
            { CardKind.BarbarianAssault => "standard:barbarian_assault", CardKind.Indulgence => "standard:indulgence", CardKind.SupplyShortage => "standard:supply_shortage",
                CardKind.Lightning => "standard:lightning", CardKind.Alcohol => "standard:alcohol", CardKind.DrawTwo => "standard:draw_two", CardKind.Duel => "standard:duel", _ => "standard:slash" }];
            b.AddDeck(new("fixture:ambush-deck", "Fixed real ambush deck", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 100).Select(i => new ContentDeckPhysicalCard(cards[i % cards.Length], Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "Ambush", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:ambush-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:ambush-owner", "fixture:ambush-peer-1", "fixture:ambush-peer-2", "fixture:ambush-peer-3"]));
        }
    }
}
