using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryChengPuChecks
{
    private const string Mode = "identity:classic-boundary-cheng-pu-fixture";
    private const string Driver = "fixture:cp-driver", Hp = "fixture:cp-hp", Gain = "fixture:cp-gain";
    private const string Store = "fixture:cp-store", Cost = "fixture:cp-pile-cost", Completed = "fixture:cp-wine-completed";
    private const string Entry = "fixture:cp-dying-entry", Tail = "fixture:cp-tail";

    public static void NonFireKindsAndVirtualUseKeepWholeUsePaymentAndTail()
    {
        foreach (var kind in new[] { CardKind.Slash, CardKind.ThunderSlash, CardKind.Alcohol })
        {
            var (g, r) = Create(kind: kind, observeStore: true);
            var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.PlayedCardKind == CardKind.FireSlash &&
                a.TargetSeats.SequenceEqual([1, 3]) && (a.ConversionSource?.SkillId == "boundary:lihuo" || a.AdditionalConversionSources?.Any(s => s.SkillId == "boundary:lihuo") == true));
            PlayAction(g, action); ReachPayment(g);
            var root = Payment(g); var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == root.CompletedUsePayment!.CardUseFrameId);
            Require(use.Step == ResolutionFrameStep.Completed && use.CausedDamage && use.CardKind == CardKind.FireSlash &&
                Facts<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.Nature == DamageNature.Fire) == 2,
                "Slash, Thunder Slash and intrinsic Alcohol-as-Slash each resolve the full real Fire use before one completion payment.");
            Private(g); g = Cold(g, r); Reject(g);
            var discard = g.CreateSnapshot(0).Players[0].Hand.First().Id;
            Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "discard" && c.Cards.SequenceEqual([discard]));
            Reach(g, p => p.SkillPrompt?.SkillId == Store);
            Require(Payment(g).CompletedUsePayment is { Stage: CompletedUsePaymentStage.Paid } &&
                Facts<ProgramAdjacentDiscardStoredEvent>(g).Any(e => e.CardId == discard) &&
                g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w => w.ResumeProgramFrameId == root.Id && w.Batch.Movements.Any(m => m.CardId == discard)),
                "The standard completion discard stores its intrinsic Slash before the later exact payment-movement observer returns to the paid parent.");
            g = Cold(g, r); Reject(g); Continue(g); Play(g);
            var paid = Facts<ProgramCompletedUsePaymentEvent>(g).Single();
            Require(paid.DiscardedCardId == discard && paid.HpLost == 0 &&
                g.CardMovements.Count(m => m.CardId == discard && m.To == CardLocation.DiscardPile && m.Reason.Value.EndsWith(".PayCompletedUseDiscardOrLoseHp", StringComparison.Ordinal)) == 1 &&
                Facts<ProgramAdjacentDiscardStoredEvent>(g).Single().CardId == discard &&
                !Facts<ProgramAdjacentDiscardStoredEvent>(g).Any(e => e.CardId == action.CardId),
                "The cost is paid once; genuine completion discard enters stock while use cleanup is excluded.");
            g = Cold(g, r);
        }

        var (natural, nr) = Create(kind: CardKind.FireSlash);
        PlayAction(natural, natural.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.PlayedCardKind == CardKind.FireSlash && a.TargetSeats.SequenceEqual([1, 3])));
        Play(natural);
        Require(Facts<DamageAppliedEvent>(natural).Count(e => e.SourceSeat == 0 && e.Nature == DamageNature.Fire) == 2 &&
            !Facts<ProgramCompletedUsePaymentEvent>(natural).Any(), "A natural Fire Slash has the extra target and no conversion completion cost."); natural = Cold(natural, nr);

        var (v, vr) = Create(virtualUse: true, deferPlay: true);
        Reach(v, p => Activate(p, "boundary:shensu", "skip-judgment-and-draw"));
        Answer(v, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(v, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-targets"));
        Answer(v, c => c.Targets.SequenceEqual([2]));
        Reach(v, p => Activate(p, "boundary:lihuo", "committed-slash-fire-or-extra"));
        Answer(v, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(v, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "current-slash-fire"));
        v = Cold(v, vr); Answer(v, c => c.Parameters.GetValueOrDefault("branch") == "extra" && c.Targets.SequenceEqual([1]));
        Reach(v, p => p.SkillPrompt?.SkillId == Tail); Continue(v);
        Reach(v, p => p.SkillPrompt?.SkillId == Tail);
        var child = v.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CurrentSlashFirePolicy is not null);
        var producer = v.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "boundary:shensu");
        Require(child.CardId == 0 && child.PhysicalCardIds is { Count: 0 } && child.TargetIndex == 1 && child.TargetSeats.SequenceEqual([2, 1]) &&
            child.CardKind == CardKind.FireSlash && child.CurrentSlashFirePolicy!.OriginalAction.EffectiveKind == CardKind.Slash &&
            producer.SelectedTargetSeats.SequenceEqual([2]) && !Facts<ProgramCompletedUsePaymentEvent>(v).Any(),
            "The zero-entity producer keeps its original selected primary target through a real extra-target tail pause.");
        v = Cold(v, vr); Reject(v); Continue(v); ReachPayment(v);
        Answer(v, c => c.Parameters.GetValueOrDefault("branch") == "discard"); Play(v);
        Require(Facts<DamageAppliedEvent>(v).Count(e => e.SourceSeat == 0 && e.Nature == DamageNature.Fire) == 2 &&
            Facts<CardUseFinishedEvent>(v).Single(e => e.CardId == 0).CardKind == CardKind.FireSlash && Facts<ProgramCompletedUsePaymentEvent>(v).Count() == 1 &&
            !v.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == "boundary:shensu"),
            "Actual Fire damage on both targets completes one zero-entity use, then one cost and one typed primary producer return."); v = Cold(v, vr);
    }

    public static void CompletionPaymentWaitsForSilverLionAndFatalHpChildren()
    {
        var (g, r) = Create(kind: CardKind.SilverLion, observeHp: true);
        var equipment = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        PlayAction(g, equipment); Play(g); var equipped = equipment.CardId!.Value;
        var fire = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.PlayedCardKind == CardKind.FireSlash && a.TargetSeats.SequenceEqual([1, 3]) && a.CardId != equipped);
        PlayAction(g, fire); ReachPayment(g); Private(g); g = Cold(g, r);
        var hp = g.State.Players[0].Hp;
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "discard" && c.Cards.SequenceEqual([equipped]));
        Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        Require(g.State.Players[0].Hp == hp + 1 && Payment(g).CompletedUsePayment is { Stage: CompletedUsePaymentStage.Paid, CardId: var paid } && paid == equipped,
            "Actual equipped Silver Lion recovery pauses before the paid completion parent can finish.");
        g = Cold(g, r); Reject(g); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        Require(Payment(g).CompletedUsePayment is { Stage: CompletedUsePaymentStage.Paid } &&
            g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w => w.ResumeProgramFrameId is not null),
            "The recovery observer's real reward draw retains the paid completion receipt through its movement child.");
        g = Cold(g, r); Continue(g); Play(g);
        Require(g.CardMovements.Count(m => m.CardId == equipped && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile) == 1 &&
            Facts<ProgramCompletedUsePaymentEvent>(g).Count() == 1 && !Facts<ProgramAdjacentDiscardStoredEvent>(g).Any(e => e.CardId == equipped),
            "One genuine equipment payment recovers once; optional Wusheng does not rewrite a discarded equipment entity into a Slash."); g = Cold(g, r);

        var (fatal, fr) = Create(initialHp: 1, observeEntry: true);
        PlayAction(fatal, fatal.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.PlayedCardKind == CardKind.FireSlash && a.TargetSeats.SequenceEqual([1])));
        ReachPayment(fatal); Answer(fatal, c => c.Parameters.GetValueOrDefault("branch") == "lose-hp");
        Reach(fatal, p => p.SkillPrompt?.SkillId == Entry);
        var payment = Payment(fatal);
        Require(payment.CompletedUsePayment is { LostHp: true, HpBefore: 1, Stage: CompletedUsePaymentStage.Paid } && fatal.State.Players[0].Hp == 0 &&
            fatal.ResolutionStack.OfType<DyingFrame>().Any(f => f.VictimSeat == 0 && f.ParentFrameId == payment.Id),
            "Actual one-HP completion cost retains its original completed use and exact dying-entry child.");
        fatal = Cold(fatal, fr); Reject(fatal); Continue(fatal);
        Until(fatal, () => !fatal.State.Players[0].IsAlive && !fatal.ResolutionStack.OfType<CardUseFrame>().Any(f => f.SourceSeat == 0));
        Require(Facts<ProgramCompletedUsePaymentEvent>(fatal).Single().HpLost == 1 &&
            Facts<ProgramSkillHpLostEvent>(fatal).Count(e => e.FrameId == payment.Id && e.TargetSeat == 0 && e.Amount == 1) == 1 &&
            !Facts<DamageAppliedEvent>(fatal).Any(e => e.TargetSeat == 0) && Facts<DamageAppliedEvent>(fatal).Any(e => e.SourceSeat == 0 && e.TargetSeat == 1),
            "Fatal HP payment is not self damage; the already completed true Fire use cleans up without a second payment."); fatal = Cold(fatal, fr);
    }

    public static void RoundPricedAlcoholKeepsAtomicMaterialsAndVictimUseReturns()
    {
        var (g, r) = Create(fragilePeers: true, observeWine: true);
        Use(g, "discard-four"); Drain(g); Play(g);
        Require(Facts<ProgramAdjacentDiscardStoredEvent>(g).Count() == 4, "Four actual owned hand discards build four real public stock entities.");
        var victims = NonLordPeers(g).Take(2).ToArray();
        Use(g, "hurt-one", [victims[0]]); ReachWine(g); g = Cold(g, r); Private(g); Reject(g);
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "pass");
        Until(g, () => !g.State.Players[victims[0]].IsAlive); Play(g);
        Require(!Facts<ProgramRoundPileAlcoholIssuedEvent>(g).Any() && Stock(g, 0).Count == 4,
            "Declining after activation consumes neither prospective first price nor a real stock material.");

        for (var price = 1; price <= 2; price++)
        {
            Use(g, "hurt-one", [victims[1]]); ReachWine(g);
            var root = WineRoot(g); var token = root.RoundPileAlcohol!;
            Require(token.Price == price && token.Stage == RoundPileAlcoholStage.Choosing && token.SelectedIds.Count == 0,
                "The source/skill/state/actual-round bucket freezes the prospective price before material selection.");
            Private(g); g = Cold(g, r); Reject(g);
            for (var n = 0; n < price; n++) Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "material");
            var chosen = WineRoot(g).RoundPileAlcohol!.SelectedIds.ToArray();
            Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "use"); Reach(g, p => p.SkillPrompt?.SkillId == Cost);
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.RoundPileAlcoholReturn is not null);
            var window = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.ResumeRoundPileAlcoholUseFrameId == use.Id);
            Require(!use.RoundPileAlcoholCostDrained && use.SourceSeat == victims[1] && use.CardKind == CardKind.Alcohol &&
                use.Action!.ActorSeat == victims[1] && use.Action.ProviderSeat == 0 && use.Action.PhysicalCards.Select(c => c.CardId).SequenceEqual(chosen) &&
                use.Action.PhysicalCards.All(c => c.From == new CardLocation(CardZoneKind.Chunlao, 0)) &&
                window.Batch.ParentFrameId == use.Id && window.Batch.Movements.Count == price && g.State.Players[victims[1]].Hp == 0,
                "One atomic X-material cost belongs to the victim's actual Alcohol use and pauses before any recovery.");
            var issued = Facts<ProgramRoundPileAlcoholIssuedEvent>(g).Last();
            Require(issued.Price == price && issued.VictimSeat == victims[1] && issued.Source.OwnerSeat == 0 && WineRoot(g).RoundPileAlcohol!.Stage == RoundPileAlcoholStage.Issued,
                "Only the true victim use issuance consumes the next ordinal; provider and actor remain distinct.");
            g = Cold(g, r); Reject(g); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
            Require(g.State.Players[victims[1]].Hp == 1 && g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == use.Id).RoundPileAlcoholCostDrained,
                "Cost observers return before the true rescue recovery and its HP child.");
            g = Cold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
            g = Cold(g, r); Continue(g); Reach(g, p => p.SkillPrompt?.SkillId == Completed);
            Require(g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == use.Id && f.Step == ResolutionFrameStep.Completed) && WineRoot(g).RoundPileAlcohol!.Stage == RoundPileAlcoholStage.Issued,
                "Completed Alcohol observers return before the pricing program and original damage producer.");
            g = Cold(g, r); Reject(g); Continue(g); Drain(g); Play(g);
            Require(chosen.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == new CardLocation(CardZoneKind.Chunlao, 0) && m.To == CardLocation.Processing) == 1 &&
                g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1),
                "Each actual material pays and cleans up once after all cost, HP, gain and completed children."); g = Cold(g, r);
        }
        Use(g, "hurt-one", [victims[1]]);
        Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RescueDying);
        Require(!P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "boundary:chunlao") && Stock(g, 0).Count == 1 &&
            Facts<ProgramRoundPileAlcoholIssuedEvent>(g).Select(e => e.Price).SequenceEqual([1, 2]),
            "One remaining material cannot publish a prospective three-material rescue; issued ordinals are not refunded or repeated."); g = Cold(g, r);
    }

    public static void AdjacentOriginsResetByActualRoundAndNativeRescueUsesRealStock()
    {
        var (g, r) = Create();
        Use(g, "discard-other", [1]); SelectForeignDiscard(g); Drain(g); Play(g);
        var adjacent = Facts<ProgramAdjacentDiscardStoredEvent>(g).Single();
        var origin = Facts<ProgramAdjacentDiscardOriginEvent>(g).Single(e => e.CardId == adjacent.CardId);
        Require(origin.SourceSeat == 1 && (origin.PreviousLivingSeat == 0 || origin.NextLivingSeat == 0) &&
            g.CardMovements.Any(m => m.Sequence == origin.MovementSequence && m.To == CardLocation.DiscardPile),
            "A real opaque neighbor discard exposes its intrinsic Slash origin only at public discard entry.");
        Use(g, "discard-other", [2]); SelectForeignDiscard(g); Drain(g); Play(g);
        var remote = Facts<ProgramAdjacentDiscardOriginEvent>(g).Last(e => e.SourceSeat == 2);
        Require(Facts<ProgramAdjacentDiscardStoredEvent>(g).Count() == 1 && remote.PreviousLivingSeat != 0 && remote.NextLivingSeat != 0,
            "An opposite living seat's Slash remains a real public discard with no stock claim."); g = Cold(g, r);
        var deadSeat = new[] { 1, 3 }.First(s => g.State.Players[s].Role != Role.Lord);
        Use(g, "kill-other", [deadSeat]); Until(g, () => !g.State.Players[deadSeat].IsAlive); Play(g);
        Use(g, "discard-other", [2]); SelectForeignDiscard(g); Drain(g); Play(g);
        Require(Facts<ProgramAdjacentDiscardStoredEvent>(g).Count() == 2 && !Facts<ProgramAdjacentDiscardStoredEvent>(g).Any(e => e.CardId == remote.CardId),
            "A later true death changes later adjacency; the earlier nonneighbor discard never gains retroactive eligibility."); g = Cold(g, r);

        var (rounds, rr) = Create(fragilePeers: true);
        var victim = NonLordPeers(rounds).First(); Use(rounds, "discard-four"); Drain(rounds); Play(rounds);
        Use(rounds, "hurt-one", [victim]); ReachWine(rounds); IssueWine(rounds); Drain(rounds); Play(rounds);
        var first = Facts<ProgramRoundPileAlcoholIssuedEvent>(rounds).Single();
        Accept(rounds, new EndPlayPhaseCommand(0, rounds.Revision, P(rounds)!.PromptId));
        Reach(rounds, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0 && rounds.State.TurnNumber > 1);
        Use(rounds, "hurt-one", [victim]); ReachWine(rounds);
        Require(WineRoot(rounds).RoundPileAlcohol!.ActualRoundNumber > first.ActualRoundNumber && WineRoot(rounds).RoundPileAlcohol!.Price == 1,
            "A real later round resets prospective pricing without faking source instances or frame state."); rounds = Cold(rounds, rr);
        IssueWine(rounds); Drain(rounds); Play(rounds);
        Require(Facts<ProgramRoundPileAlcoholIssuedEvent>(rounds).Select(e => e.Price).SequenceEqual([1, 1]), "Two actual rounds issue separate first drinks."); rounds = Cold(rounds, rr);

        var (ai, ar) = Create(nativeRescuer: true);
        var rescuer = ai.State.Players.Single(p => p.GeneralId == "fixture:cp-other-1").Seat;
        Use(ai, "discard-other", [rescuer]); SelectForeignDiscard(ai); Drain(ai); Play(ai);
        Require(Stock(ai, rescuer).Count == 1 && !ai.State.Players[rescuer].IsHuman, "The native rescuer first receives an actual owned discarded Slash in its real stock.");
        Use(ai, ai.State.Players[rescuer].Hp == 1 ? "hurt-one" : "hurt-two", [rescuer]); Until(ai, () => Facts<ProgramRoundPileAlcoholIssuedEvent>(ai).Any()); Drain(ai); Play(ai);
        var actual = Facts<ProgramRoundPileAlcoholIssuedEvent>(ai).Single();
        Require(actual.Source.OwnerSeat == rescuer && actual.VictimSeat == rescuer && actual.Price == 1 && ai.State.Players[rescuer].Hp == 1 && Stock(ai, rescuer).Count == 0 &&
            Facts<ProgramRoundPileAlcoholMaterialPaidEvent>(ai).Count() == 1 && !ai.ResolutionStack.OfType<CardUseFrame>().Any(f => f.RoundPileAlcoholReturn is not null),
            "Native AI chooses a real first-price material, issues the victim's true Alcohol, pays once and returns all owning children."); ai = Cold(ai, ar);
    }

    private static IEnumerable<T> Facts<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static ProgramSkillFrame Payment(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.CompletedUsePayment is not null);
    private static ProgramSkillFrame WineRoot(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.RoundPileAlcohol is not null);
    private static IReadOnlyList<CardSnapshot> Stock(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat].ChunlaoCards ?? [];
    private static IEnumerable<int> NonLordPeers(GameEngine g) => g.State.Players.Where(p => p.Seat != 0 && p.Role != Role.Lord).Select(p => p.Seat);
    private static bool Activate(PendingDecision p, string skill, string binding) => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c =>
        c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static void ReachPayment(GameEngine g) => Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "completed-use-payment"));
    private static void ReachWine(GameEngine g)
    {
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "round-pile-alcohol") ||
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "boundary:chunlao"));
        if (!P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "round-pile-alcohol"))
            Answer(g, c => c.Parameters.GetValueOrDefault("skill-id") == "boundary:chunlao");
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "round-pile-alcohol"));
    }
    private static void IssueWine(GameEngine g)
    { while (P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "material")) Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "material"); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "use"); }
    private static void Use(GameEngine g, string binding, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, binding, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void PlayAction(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind)
        { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void SelectForeignDiscard(GameEngine g)
    { Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card")); ColdPrompt(g); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card" && c.Parameters.GetValueOrDefault("skip") != "true"); }
    private static void ColdPrompt(GameEngine g) => Require(P(g)!.PlayerSeat == 0 && g.CreateSnapshot(1).PendingDecision is null, "The chooser owns the opaque foreign hand selection.");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> done)
    { for (var i = 0; i < 220; i++) { if (P(g) is { } p && done(p)) return; Step(g); } throw new InvalidOperationException("Cheng Pu boundary missing: " + JsonSerializer.Serialize(P(g))); }
    private static void Until(GameEngine g, Func<bool> done)
    { for (var i = 0; i < 240; i++) { if (done()) return; Step(g); } throw new InvalidOperationException("Cheng Pu actual event boundary missing."); }
    private static void Drain(GameEngine g) => Until(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is not null && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand c)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static void Reject(GameEngine g)
    { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("not-published"), g.Revision)).Accepted && State(g) == before, "Rejected choices cannot repeat costs or ordinal issuance."); }
    private static void Private(GameEngine g)
    { Require(P(g) is { IsPrivate: true, PlayerSeat: 0 }, "The actual provider privately chooses payment materials."); for (var s = 1; s < 4; s++) Require(g.CreateSnapshot(s).PendingDecision is null && g.CreateSnapshot(s).Players[0].Hand.Count == 0, "Other prepared views reveal neither the payment hand nor its chooser."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Require(State(g) == State(restored), "Four views, exact owning frames, real movements and accepted commands cold-restore identically.");
        return restored;
    }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }

    private static (GameEngine, ContentRegistry) Create(CardKind kind = CardKind.Slash, int initialHp = 3, bool observeStore = false, bool observeHp = false,
        bool observeEntry = false, bool fragilePeers = false, bool observeWine = false, bool virtualUse = false, bool deferPlay = false, bool nativeRescuer = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new Fixture(kind, initialHp, observeStore, observeHp, observeEntry, fragilePeers, observeWine, virtualUse, nativeRescuer));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Renegade, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:cp-owner", g.Revision, P(g)!.PromptId)); if (!deferPlay) Play(g); return (g, r);
    }
    private sealed class Fixture(CardKind kind, int initialHp, bool observeStore, bool observeHp, bool observeEntry, bool fragilePeers, bool observeWine, bool virtualUse, bool nativeRescuer) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-cheng-pu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var labels = new Dictionary<string, object>();
            foreach (var id in new[] { Driver, Hp, Gain, Store, Cost, Completed, Entry, Tail, "fixture:cp-quiet" })
                labels[id] = id is Driver or "fixture:cp-quiet" ? (object)new { name = id, description = "实际 owning 子链夹具" } : new { name = id, description = "实际 owning 子链夹具", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var programs = SkillProgramCatalog.Load(FixtureRules.Replace("$SCHEMA$", SkillProgramCatalog.RulesSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)), JsonSerializer.Serialize(new { schemaVersion = 3, skills = labels })).Programs;
            foreach (var (id, program) in programs) b.AddSkill(new(id, id, "实际 owning 子链夹具") { Program = program, Tags = id == "fixture:cp-quiet" ? SkillTag.Locked : SkillTag.None });
            b.AddSkill(new("fixture:cp-pick-other", "固定既有角色", "公开选将评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100000d) });
            var skills = new List<string> { "boundary:lihuo" };
            if (!nativeRescuer) skills.Add("boundary:chunlao");
            if (kind == CardKind.Alcohol) skills.Add("boundary:jinjiu-current");
            if (kind == CardKind.SilverLion) skills.Add("classic:wusheng");
            if (observeStore) skills.Add(Store);
            if (observeHp) skills.AddRange([Hp, Gain]);
            if (observeEntry) skills.Add(Entry);
            if (observeWine) skills.Add(Cost);
            if (virtualUse) skills.AddRange(["boundary:shensu", Tail]);
            b.AddGeneral(new("fixture:cp-owner", "实际程普机制", "supporter", Driver, "wu", 5, skills.ToArray()) { InitialHp = initialHp });
            for (var i = 1; i < 4; i++)
            {
                var other = new List<string> { "fixture:cp-quiet" };
                if (observeWine) other.AddRange([Hp, Gain, Completed]);
                if (nativeRescuer && i == 1) other.Add("boundary:chunlao");
                b.AddGeneral(new($"fixture:cp-other-{i}", "固定其他角色", "supporter", "fixture:cp-pick-other", "wu", 6, other.ToArray())
                    { InitialHp = fragilePeers || nativeRescuer && i == 1 ? 1 : null });
            }
            var card = kind switch { CardKind.ThunderSlash => "standard:thunder_slash", CardKind.FireSlash => "standard:fire_slash", CardKind.Alcohol => "standard:alcohol", CardKind.SilverLion => "classic:silver-lion", _ => "standard:slash" };
            b.AddDeck(new("fixture:cp-deck", "固定真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 180).Select(_ => new ContentDeckPhysicalCard(card, kind == CardKind.SilverLion ? Suit.Heart : Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "界程普实际命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:cp-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:cp-owner", "fixture:cp-other-1", "fixture:cp-other-2", "fixture:cp-other-3"]));
        }
    }
    private const string FixtureRules = """
    {"schemaVersion":$SCHEMA$,"skills":[
     {"id":"fixture:cp-driver","revision":1,"activations":[
      {"id":"discard-four","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","amount":4,"zones":["hand"],"resultBind":"cost"},{"op":"moveBoundCards","target":"owner","sourceBind":"cost","destination":"discardPile","awaitMovementTriggers":true}]},
      {"id":"discard-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"count":1,"zones":["hand"],"destination":"discardPile","awaitMovementTriggers":true}]},
      {"id":"hurt-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
      {"id":"hurt-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
      {"id":"kill-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":8}]}]},
     {"id":"fixture:cp-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]},{"id":"quiet-discard","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["discard"]}]}]},
     {"id":"fixture:cp-hp","revision":1,"triggers":[{"id":"actual-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]},{"op":"draw","target":"owner","amount":1}]}]},
     {"id":"fixture:cp-gain","revision":1,"triggers":[{"id":"recovery-reward","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.fixture:cp-hp.Draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:cp-store","revision":1,"triggers":[{"id":"actual-payment-after-stock","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:lihuo.PayCompletedUseDiscardOrLoseHp"],"priority":-100,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"stock-seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:cp-pile-cost","revision":1,"triggers":[{"id":"atomic-stock-cost","window":"cardsMoved","subject":"owner","sourceZones":["chunlao"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:chunlao.round-pile-alcohol.pay"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"stock-paid","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:cp-wine-completed","revision":1,"triggers":[{"id":"true-victim-wine","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["alcohol"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"wine-seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:cp-dying-entry","revision":1,"triggers":[{"id":"actual-paid-dying","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"dying-seen","options":[{"id":"continue"}]}]}]},
     {"id":"fixture:cp-tail","revision":1,"triggers":[{"id":"each-fire-target","window":"slashBeforeResponse","ownerRelation":"actor","cardKinds":["fireSlash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"target-seen","options":[{"id":"continue"}]}]}]}
    ]}
    """;
}
