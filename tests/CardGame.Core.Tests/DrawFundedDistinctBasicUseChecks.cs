using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DrawFundedDistinctBasicUseChecks
{
    public static void QualificationLedgerAndPaidChildren()
    {
        DrawFundedBasicFixture.LoaderContract();
        var (g, registry) = DrawFundedBasicFixture.Create(optionalGain: true);
        DrawFundedBasicFixture.Play(g);
        var original = DrawFundedBasicFixture.V(g, 0).Hand.Select(c => c.Id).ToArray();
        DrawFundedBasicFixture.Require(original.Length == DrawFundedBasicFixture.V(g, 0).Hp &&
            DrawFundedBasicFixture.V(g, 0).Hand.All(c => c.Suit == Suit.Spade), "The actual whole nonempty hand qualifies before acceptance.");
        var action = g.GetHumanLegalActions().First(a => a.DrawFundedDistinctBasicUse is not null &&
            a.PlayedCardKind == CardKind.FireSlash && a.TargetSeats.SequenceEqual([1]));
        g = DrawFundedBasicFixture.Cold(g, registry);
        DrawFundedBasicFixture.SubmitPlay(g, action);
        DrawFundedBasicFixture.Reach(g, p => p.SkillPrompt?.SkillId == DrawFundedBasicFixture.Gain);
        var paid = g.ResolutionStack.OfType<DrawFundedDistinctBasicFrame>().Single();
        var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
        DrawFundedBasicFixture.Require(paid.Payment.ActualDrawCount == 1 && paid.ActiveChildFrameId == movement.Id &&
            movement.ResumeDrawFundedDistinctBasicFrameId == paid.Id && movement.Batch.ParentFrameId == paid.Id &&
            movement.Step == ResolutionFrameStep.AwaitingResponse && !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == DrawFundedBasicFixture.Gain) &&
            DrawFundedBasicFixture.F<DrawFundedDistinctBasicPaidEvent>(g).Length == 1 &&
            DrawFundedBasicFixture.F<DrawFundedDistinctBasicIssuedEvent>(g).Length == 0,
            "An optional actual gain prompt suspends on its owning paid movement before a binding or any promised Use exists.");
        DrawFundedBasicFixture.Frozen(paid.OriginalDecision);
        DrawFundedBasicFixture.Private(g); g = DrawFundedBasicFixture.Cold(g, registry);
        DrawFundedBasicFixture.Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        DrawFundedBasicFixture.Reach(g, p => p.SkillPrompt?.SkillId == DrawFundedBasicFixture.Gain && p.Choices.Any(c => c.Parameters.ContainsKey("option-id")));
        DrawFundedBasicFixture.Reject(g); g = DrawFundedBasicFixture.Cold(g, registry); DrawFundedBasicFixture.Continue(g);
        DrawFundedBasicFixture.Reach(g, p => p.SkillPrompt?.SkillId == DrawFundedBasicFixture.Completed);
        var issued = DrawFundedBasicFixture.F<DrawFundedDistinctBasicIssuedEvent>(g).Single();
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == issued.OwnerFrameId);
        DrawFundedBasicFixture.Require(use.DrawFundedDistinctBasicUse?.Payment.PaymentFrameId == paid.Id &&
            use.CardId == 0 && use.PhysicalCardIds is { Count: 0 } && use.Action is
                { Type: CardActionType.Use, EffectiveKind: CardKind.FireSlash, PhysicalCards.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: false } &&
            use.Action.ConversionChain.SequenceEqual([action.ConversionSource!]) &&
            DrawFundedBasicFixture.V(g, 0).HandCount == original.Length + 1 &&
            original.All(id => g.CreateCardZoneDiagnostics().Any(c => c.CardId == id && c.Location == CardLocation.Hand(0))),
            "Exactly one real draw issues the accepted neutral zero-material FireSlash even though hand count now differs from HP.");
        g = DrawFundedBasicFixture.Cold(g, registry); DrawFundedBasicFixture.Continue(g); DrawFundedBasicFixture.Play(g);
        DrawFundedBasicFixture.Require(DrawFundedBasicFixture.F<DrawFundedDistinctBasicReturnedEvent>(g).Count(e => e.PaymentFrameId == paid.Id) == 1 &&
            g.CardMovements.All(m => m.CardId != 0) && DrawFundedBasicFixture.F<DrawFundedDistinctBasicPaidEvent>(g).Length == 1 &&
            !g.GetHumanLegalActions().Any(a => a.DrawFundedDistinctBasicUse is not null),
            "The original Use returns once without discarding, processing or manufacturing an entity0 payment.");
        DrawFundedBasicFixture.Use(g, "budget"); DrawFundedBasicFixture.Play(g); DrawFundedBasicFixture.Trim(g);
        var quotaActions = g.GetHumanLegalActions();
        var restoredQualification = DrawFundedBasicFixture.V(g, 0).Hp == DrawFundedBasicFixture.V(g, 0).HandCount &&
            DrawFundedBasicFixture.V(g, 0).Hand.All(c => c.Suit == Suit.Spade);
        var nativeSlashAvailable = quotaActions.Any(a => a.Kind == LegalActionKind.Slash && a.CardId > 0 && a.ConversionSource is null);
        var methodSlashBlocked = !quotaActions.Any(a => a.DrawFundedDistinctBasicUse is not null && a.PlayedCardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash);
        DrawFundedBasicFixture.Require(restoredQualification && nativeSlashAvailable && methodSlashBlocked,
            "With qualification and native Slash allowance restored, all three Slash variants share the paid method's one actual-turn name: " +
            JsonSerializer.Serialize(new { restoredQualification, nativeSlashAvailable, methodSlashBlocked, Self = DrawFundedBasicFixture.V(g, 0), Actions = quotaActions }));
        var oldInstance = issued.Source.SkillInstanceId;
        DrawFundedBasicFixture.Use(g, "remove"); DrawFundedBasicFixture.Play(g);
        DrawFundedBasicFixture.Use(g, "regain"); DrawFundedBasicFixture.Play(g);
        DrawFundedBasicFixture.Require(!g.GetHumanLegalActions().Any(a => a.DrawFundedDistinctBasicUse is not null && a.PlayedCardKind == CardKind.Slash) &&
            g.GetHumanLegalActions().Any(a => a.DrawFundedDistinctBasicUse is not null && a.ConversionSource!.SkillInstanceId != oldInstance),
            "A genuine physical skill retirement and regrant changes source identity but retains the stable method name allowance.");
        DrawFundedBasicFixture.Use(g, "extra"); DrawFundedBasicFixture.Play(g); DrawFundedBasicFixture.End(g);
        DrawFundedBasicFixture.Until(g, e => DrawFundedBasicFixture.F<TurnStartedEvent>(e).Count(t => t.ActorSeat == 0) == 2 &&
            DrawFundedBasicFixture.P(e) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        DrawFundedBasicFixture.Trim(g);
        DrawFundedBasicFixture.Require(DrawFundedBasicFixture.F<RoundStartedEvent>(g).Length == 1 &&
            g.GetHumanLegalActions().Any(a => a.DrawFundedDistinctBasicUse is not null && a.PlayedCardKind == CardKind.Slash),
            "An actual extra turn releases the name without a new Round.");
        _ = DrawFundedBasicFixture.Cold(g, registry);

        foreach (var colors in new[] { "colorless", "mixed" })
        {
            (g, registry) = DrawFundedBasicFixture.Create(qualificationColors: colors); DrawFundedBasicFixture.Play(g);
            var hand = DrawFundedBasicFixture.V(g, 0).Hand;
            DrawFundedBasicFixture.Require(hand.Count > 0 && hand.Count == DrawFundedBasicFixture.V(g, 0).Hp &&
                (colors == "colorless" ? hand.All(c => c.Suit == Suit.None) : hand.Select(c => c.Suit).ToHashSet().SetEquals([Suit.Spade, Suit.Heart])) &&
                !g.GetHumanLegalActions().Any(a => a.DrawFundedDistinctBasicUse is not null) && DrawFundedBasicFixture.F<DrawFundedDistinctBasicStartedEvent>(g).Length == 0,
                "Equal HP/count alone cannot qualify a colorless whole hand or a real whole hand containing both effective colors.");
            _ = DrawFundedBasicFixture.Cold(g, registry);
        }

        (g, registry) = DrawFundedBasicFixture.Create(qualificationColors: "empty-draw"); DrawFundedBasicFixture.Play(g);
        var emptyOriginal = DrawFundedBasicFixture.V(g, 0).Hand.Select(c => c.Id).ToArray();
        action = g.GetHumanLegalActions().First(a => a.DrawFundedDistinctBasicUse is not null && a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([1]));
        DrawFundedBasicFixture.Require(g.CreateSnapshot(0).DrawPileCount == 0 && g.CreateSnapshot(0).DiscardPileCount == 0 && emptyOriginal.Length == 6,
            "All six real cards are in the qualifying owner hand before a genuinely empty native draw attempt.");
        DrawFundedBasicFixture.SubmitPlay(g, action); DrawFundedBasicFixture.Reach(g, p => p.SkillPrompt?.SkillId == DrawFundedBasicFixture.Completed);
        var emptyPaid = DrawFundedBasicFixture.F<DrawFundedDistinctBasicPaidEvent>(g).Single();
        DrawFundedBasicFixture.Require(emptyPaid.ActualDrawCount == 0 && emptyPaid.SequenceBefore == emptyPaid.SequenceAfter &&
            DrawFundedBasicFixture.F<DrawFundedDistinctBasicIssuedEvent>(g).Single().PaymentFrameId == emptyPaid.PaymentFrameId &&
            DrawFundedBasicFixture.V(g, 0).Hand.Select(c => c.Id).SequenceEqual(emptyOriginal) &&
            !g.CardMovements.Any(m => m.Reason.Value == "skill-program.draw-funded-distinct-basic.draw"),
            "The one empty Draw1 attempt still issues the promised Use exactly once without inventing a gain or card identity.");
        g = DrawFundedBasicFixture.Cold(g, registry); DrawFundedBasicFixture.Continue(g); DrawFundedBasicFixture.Play(g);
        DrawFundedBasicFixture.Require(DrawFundedBasicFixture.F<DrawFundedDistinctBasicReturnedEvent>(g).Single().PaymentFrameId == emptyPaid.PaymentFrameId,
            "The genuinely empty paid attempt returns once through native Slash completion.");

        (g, registry) = DrawFundedBasicFixture.Create(cancelTarget: true); DrawFundedBasicFixture.Play(g);
        action = g.GetHumanLegalActions().First(a => a.DrawFundedDistinctBasicUse is not null && a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([1]));
        DrawFundedBasicFixture.SubmitPlay(g, action); DrawFundedBasicFixture.Reach(g, p => p.SkillPrompt?.SkillId == DrawFundedBasicFixture.Gain);
        var cancelledPayment = DrawFundedBasicFixture.F<DrawFundedDistinctBasicPaidEvent>(g).Single().PaymentFrameId;
        DrawFundedBasicFixture.Continue(g);
        DrawFundedBasicFixture.Reach(g, p => p.SkillPrompt?.SkillId == DrawFundedBasicFixture.Gain && p.Choices.Any(c => c.Targets.SequenceEqual([1])));
        DrawFundedBasicFixture.Answer(g, c => c.Targets.SequenceEqual([1])); DrawFundedBasicFixture.Play(g);
        DrawFundedBasicFixture.Require(!DrawFundedBasicFixture.V(g, 1).IsAlive && DrawFundedBasicFixture.V(g, 2).IsAlive && DrawFundedBasicFixture.V(g, 3).IsAlive &&
            DrawFundedBasicFixture.F<DrawFundedDistinctBasicCancelledEvent>(g).Count(e => e.PaymentFrameId == cancelledPayment) == 1 &&
            !DrawFundedBasicFixture.F<DrawFundedDistinctBasicIssuedEvent>(g).Any(e => e.PaymentFrameId == cancelledPayment) &&
            !DrawFundedBasicFixture.F<DrawFundedDistinctBasicReturnedEvent>(g).Any(e => e.PaymentFrameId == cancelledPayment) &&
            DrawFundedBasicFixture.F<DrawFundedDistinctBasicPaidEvent>(g).Length == 1,
            "A real paid gain child kills the original target; the attempt cancels once with neither an issued name nor a fabricated Use return.");
        g = DrawFundedBasicFixture.Cold(g, registry); DrawFundedBasicFixture.Trim(g);
        action = g.GetHumanLegalActions().First(a => a.DrawFundedDistinctBasicUse is not null && a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([3]));
        DrawFundedBasicFixture.SubmitPlay(g, action); DrawFundedBasicFixture.Play(g);
        DrawFundedBasicFixture.Require(DrawFundedBasicFixture.F<DrawFundedDistinctBasicPaidEvent>(g).Length == 2 &&
            DrawFundedBasicFixture.F<DrawFundedDistinctBasicIssuedEvent>(g) is [var reissued] && reissued.NormalizedName == CardKind.Slash &&
            reissued.PaymentFrameId != cancelledPayment && DrawFundedBasicFixture.F<DrawFundedDistinctBasicReturnedEvent>(g).Single().PaymentFrameId == reissued.PaymentFrameId,
            "After restoring actual hand/HP qualification in the same turn the unissued Slash name remains available for another live target.");
        _ = DrawFundedBasicFixture.Cold(g, registry);

        (g, registry) = DrawFundedBasicFixture.Create(sourceLoss: true); DrawFundedBasicFixture.Play(g);
        action = g.GetHumanLegalActions().First(a => a.DrawFundedDistinctBasicUse is not null && a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([1]));
        DrawFundedBasicFixture.SubmitPlay(g, action); DrawFundedBasicFixture.Reach(g, p => p.SkillPrompt?.SkillId == DrawFundedBasicFixture.Gain);
        g = DrawFundedBasicFixture.Cold(g, registry); DrawFundedBasicFixture.Continue(g); DrawFundedBasicFixture.Play(g);
        DrawFundedBasicFixture.Require(DrawFundedBasicFixture.F<SkillsAcquiredEvent>(g).Any(e => e.SkillIds.Contains(DrawFundedBasicFixture.Suppress)) &&
            DrawFundedBasicFixture.F<DrawFundedDistinctBasicIssuedEvent>(g) is [var lost] && lost.Source == action.ConversionSource &&
            DrawFundedBasicFixture.F<DrawFundedDistinctBasicReturnedEvent>(g).Length == 1 && DrawFundedBasicFixture.F<DrawFundedDistinctBasicPaidEvent>(g).Length == 1 &&
            !g.GetHumanLegalActions().Any(a => a.DrawFundedDistinctBasicUse is not null),
            "A real post-payment skill qualification suppressor cannot revoke or repay the accepted source receipt.");
        _ = DrawFundedBasicFixture.Cold(g, registry);

        // This exact legacy producer obtains an accepted zero-entity Alcohol
        // action during completion and returns through direct HP, not a queued recovery.
        (g, registry) = DrawFundedBasicFixture.Create(legacyAlcohol: true); DrawFundedBasicFixture.Play(g);
        action = g.GetHumanLegalActions().First(a => a.DrawFundedDistinctBasicUse is not null && a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([1]));
        DrawFundedBasicFixture.SubmitPlay(g, action); DrawFundedBasicFixture.Reach(g, p => p.SkillPrompt?.SkillId == DrawFundedBasicFixture.Gain);
        DrawFundedBasicFixture.Continue(g); DrawFundedBasicFixture.Reach(g, p => p.SkillPrompt?.SkillId == DrawFundedBasicFixture.Rescue);
        DrawFundedBasicFixture.Require(!DrawFundedBasicFixture.P(g)!.Choices.Any(c => c.Parameters.ContainsKey("draw-funded-basic")),
            "A currently dying owner cannot newly qualify its nonempty hand at HP0.");
        g = DrawFundedBasicFixture.Cold(g, registry); DrawFundedBasicFixture.Continue(g);
        DrawFundedBasicFixture.Reach(g, p => p.SkillPrompt?.SkillId == DrawFundedBasicFixture.Hp);
        paid = g.ResolutionStack.OfType<DrawFundedDistinctBasicFrame>().Single();
        var dying = g.ResolutionStack.OfType<DyingFrame>().Single();
        var rescue = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.LegacyDyingAlcoholReturn is not null);
        var hp = g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        DrawFundedBasicFixture.Require(dying.Continuation == DyingContinuationKind.ProgramSkill && rescue is
            { CardId: 0, CardKind: CardKind.Alcohol, PhysicalCardIds.Count: 0, Action: not null } &&
            rescue.LegacyDyingAlcoholReturn!.ProgramFrameId == g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == DrawFundedBasicFixture.Rescue).Id &&
            hp.ResumeFrameId == rescue.Id && hp.Change.ParentFrameId == rescue.Id && hp.Continuation == PostEventContinuation.CardUse &&
            hp.Change.HpBefore == 0 && hp.Change.HpAfter == 1 && DrawFundedBasicFixture.F<DrawFundedDistinctBasicIssuedEvent>(g).Length == 0,
            "The paid Draw subtree retains its exact Program Dying, accepted legacy Alcohol and direct native HP completion child.");
        DrawFundedBasicFixture.Private(g); g = DrawFundedBasicFixture.Cold(g, registry); DrawFundedBasicFixture.Continue(g); DrawFundedBasicFixture.Play(g);
        DrawFundedBasicFixture.Require(DrawFundedBasicFixture.V(g, 0).Hp == 1 && DrawFundedBasicFixture.F<DrawFundedDistinctBasicPaidEvent>(g).Length == 1 &&
            DrawFundedBasicFixture.F<DrawFundedDistinctBasicIssuedEvent>(g).Single().PaymentFrameId == paid.Id &&
            DrawFundedBasicFixture.F<DrawFundedDistinctBasicReturnedEvent>(g).Single().PaymentFrameId == paid.Id,
            "After genuine self-dying rescue the original owed Slash issues and returns once, without rechecking HP/hand qualification or drawing twice.");
        _ = DrawFundedBasicFixture.Cold(g, registry);

        foreach (var kind in new[] { CardKind.Peach, CardKind.Alcohol })
        {
            (g, registry) = DrawFundedBasicFixture.Create(simpleUseCompletion: true); DrawFundedBasicFixture.Play(g);
            if (kind == CardKind.Peach)
            {
                DrawFundedBasicFixture.Use(g, "hurt-one"); DrawFundedBasicFixture.Play(g); DrawFundedBasicFixture.Trim(g);
            }
            var hpBefore = DrawFundedBasicFixture.V(g, 0).Hp;
            var cardsBefore = DrawFundedBasicFixture.V(g, 0).Hand.Select(c => c.Id).ToArray();
            DrawFundedBasicFixture.Require(cardsBefore.Length > 0 && cardsBefore.Length == hpBefore &&
                DrawFundedBasicFixture.V(g, 0).Hand.All(c => c.Suit == Suit.Spade),
                "The genuine wounded Peach or healthy Alcohol player qualifies its whole hand before acceptance.");
            action = g.GetHumanLegalActions().Single(a => a.DrawFundedDistinctBasicUse is not null && a.PlayedCardKind == kind);
            DrawFundedBasicFixture.SubmitPlay(g, action); DrawFundedBasicFixture.Reach(g, p => p.SkillPrompt?.SkillId == DrawFundedBasicFixture.Gain);
            paid = g.ResolutionStack.OfType<DrawFundedDistinctBasicFrame>().Single();
            DrawFundedBasicFixture.Require(paid.Payment.Intent == DrawFundedDistinctBasicIntent.Play && paid.Payment.EffectiveKind == kind &&
                paid.Payment.ActualDrawCount == 1 && paid.Payment.ParentFrameId is null && paid.Payment.RequestFrameId is null &&
                DrawFundedBasicFixture.F<DrawFundedDistinctBasicIssuedEvent>(g).Length == 0 && DrawFundedBasicFixture.V(g, 0).Hp == hpBefore,
                "The real simple-card Play draws once and suspends its gain child before issuing or applying its accepted effect.");
            DrawFundedBasicFixture.Private(g); g = DrawFundedBasicFixture.Cold(g, registry); DrawFundedBasicFixture.Continue(g);
            DrawFundedBasicFixture.Reach(g, p => p.SkillPrompt?.SkillId == DrawFundedBasicFixture.Completed);
            issued = DrawFundedBasicFixture.F<DrawFundedDistinctBasicIssuedEvent>(g).Single();
            use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == issued.OwnerFrameId);
            DrawFundedBasicFixture.Require(issued.PaymentFrameId == paid.Id && issued.EffectiveKind == kind && issued.NormalizedName == kind &&
                use.DrawFundedDistinctBasicUse?.Payment.PaymentFrameId == paid.Id && use.DyingResponse is null && use.CardId == 0 &&
                use.CardKind == kind && use.PhysicalCardIds is { Count: 0 } && use.Action is
                    { Type: CardActionType.Use, PhysicalCards.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: false } &&
                use.Action.ConversionChain.SequenceEqual([action.ConversionSource!]) &&
                DrawFundedBasicFixture.V(g, 0).HandCount == cardsBefore.Length + 1 &&
                (kind == CardKind.Peach ? DrawFundedBasicFixture.V(g, 0).Hp == hpBefore + 1 && use.TargetSeats.SequenceEqual([0]) :
                    DrawFundedBasicFixture.V(g, 0).Hp == hpBefore && DrawFundedBasicFixture.V(g, 0).HasAlcoholEffect),
                "The exact paid neutral zero-material Play reaches its native Peach recovery or Alcohol effect before its original completion observer returns.");
            DrawFundedBasicFixture.Private(g); g = DrawFundedBasicFixture.Cold(g, registry); DrawFundedBasicFixture.Continue(g); DrawFundedBasicFixture.Play(g);
            DrawFundedBasicFixture.Require(DrawFundedBasicFixture.F<DrawFundedDistinctBasicPaidEvent>(g).Length == 1 &&
                DrawFundedBasicFixture.F<DrawFundedDistinctBasicIssuedEvent>(g).Length == 1 &&
                DrawFundedBasicFixture.F<DrawFundedDistinctBasicReturnedEvent>(g).Single().PaymentFrameId == paid.Id &&
                DrawFundedBasicFixture.F<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == issued.OwnerFrameId && e.CardId == 0 && e.CardKind == kind) == 1 &&
                cardsBefore.All(id => g.CreateCardZoneDiagnostics().Any(c => c.CardId == id && c.Location == CardLocation.Hand(0))) &&
                g.CardMovements.All(m => m.CardId != 0),
                "The genuine simple-card Use finishes and returns exactly once after cold restoration, preserving every original hand entity and one native draw payment.");
            _ = DrawFundedBasicFixture.Cold(g, registry);
        }
    }
}

internal static class DrawFundedBasicFixture
{
    internal const string Bingxin = "ol:bingxin", Driver = "fixture:dfb-driver", Gain = "fixture:dfb-gain", Completed = "fixture:dfb-completed";
    internal const string Rescue = "fixture:dfb-rescue", Hp = "fixture:dfb-hp", Suppress = "fixture:dfb-suppress", Mode = "identity:classic-draw-funded-basic";
    internal static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    internal static T[] F<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    internal static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    internal static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real draw-funded fixture command."); }
    internal static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    internal static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    internal static void Play(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    internal static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    internal static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null, IReadOnlyList<int>? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    internal static void SubmitPlay(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId,
        a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    internal static void Trim(GameEngine g) { while (V(g, 0).HandCount > V(g, 0).Hp) { Use(g, "trim", cards: [V(g, 0).Hand[0].Id]); Play(g); } }
    internal static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) => Until(g, e => P(e) is { } p && predicate(p));
    internal static void Until(GameEngine g, Func<GameEngine, bool> predicate)
    {
        for (var i = 0; i < 240; i++) { if (predicate(g)) return; Advance(g); }
        throw new InvalidOperationException("Fixed draw-funded fixture did not reach its real boundary: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack, Last = g.Events.TakeLast(4).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) }));
    }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    internal static GameEngine Cold(GameEngine g, ContentRegistry r) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(State(restored) == State(g), "Four-view cold restore preserves the exact payment, original decisions, typed children, scalar facts and native journal."); return restored; }
    internal static void Private(GameEngine g) { var p = P(g)!; Require(p.IsPrivate, "The actual child decision is private."); foreach (var viewer in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat)) Require(g.CreateSnapshot(viewer).PendingDecision is null && g.CreateSnapshot(viewer).Players[p.PlayerSeat].Hand.Count == 0, "Another viewer receives neither the private decision nor hand identities."); Frozen(p); }
    internal static void Frozen(PendingDecision p)
    {
        Require(p.ValidCardIds is System.Collections.IList { IsReadOnly: true } && p.ValidTargetSeats is System.Collections.IList { IsReadOnly: true } &&
            p.ValidContentIds is System.Collections.IList { IsReadOnly: true } && p.Choices is System.Collections.IList { IsReadOnly: true } &&
            p.Choices.All(c => c.Cards is System.Collections.IList { IsReadOnly: true } && c.Targets is System.Collections.IList { IsReadOnly: true } &&
                c.ContentIds is System.Collections.IList { IsReadOnly: true } &&
                c.Parameters is System.Collections.IDictionary { IsReadOnly: true }), "The original decision freezes all nested arrays and choice parameter maps.");
    }
    internal static void Reject(GameEngine g) { var before = State(g); var p = P(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished answer cannot mutate or repay the suspended real attempt."); }
    internal static (GameEngine, ContentRegistry) Create(bool optionalGain = false, bool sourceLoss = false, bool legacyAlcohol = false, bool foreignSlash = false, bool fragileTargets = false, bool peerBingxin = false, string? qualificationColors = null, bool cancelTarget = false, bool simpleUseCompletion = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(optionalGain, sourceLoss, legacyAlcohol, foreignSlash, fragileTargets, peerBingxin, qualificationColors, cancelTarget, simpleUseCompletion));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true,
            UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Require(P(g)!.ValidContentIds.Contains("fixture:dfb-owner"), "The real published fixed general choice contains the fixture owner.");
        Accept(g, new SelectGeneralCommand(0, "fixture:dfb-owner", g.Revision, P(g)!.PromptId)); return (g, r);
    }
    internal static void LoaderContract()
    {
        var root = JsonNode.Parse("""{"skills":[{"id":"fixture:dfb-loader","revision":1,"viewAs":[{"id":"physical","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"useOnly":true}]}]}""")!;
        root["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
        var presentation = """{"schemaVersion":3,"skills":{"fixture:dfb-loader":{"name":"加载合同","description":"共享可选字段"}}}""";
        var old = SkillProgramCatalog.Load(root.ToJsonString(), presentation).Programs["fixture:dfb-loader"];
        root["skills"]![0]!["viewAs"]![0]!["drawFundedDistinctBasic"] = null;
        var omitted = SkillProgramCatalog.Load(root.ToJsonString(), presentation).Programs["fixture:dfb-loader"];
        Require(old.GameplayHash == omitted.GameplayHash && omitted.ViewAs.Single().DrawFundedDistinctBasic is null,
            "Absent and JSON-null optional policy retain the exact old gameplay hash and entity direction.");
        root["skills"]![0]!["viewAs"]![0]!["drawFundedDistinctBasic"] = JsonNode.Parse("""{"methodLedgerId":"fixture:loader"}""");
        var rejected = false; try { _ = SkillProgramCatalog.Load(root.ToJsonString(), presentation); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "The policy cannot silently attach to a positive-material conversion.");
        var rule = root["skills"]![0]!["viewAs"]![0]!;
        rule["inputCount"] = 0; rule["sourceZones"] = new JsonArray();
        _ = SkillProgramCatalog.Load(root.ToJsonString(), presentation);
        rule["drawFundedDistinctBasic"]!["methodLedgerId"] = "fixture:loader\n";
        rejected = false; try { _ = SkillProgramCatalog.Load(root.ToJsonString(), presentation); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "A bounded stable method identifier rejects an actual trailing newline, including the .NET dollar-anchor edge.");
    }
    private sealed class Fixture(bool optionalGain, bool sourceLoss, bool legacyAlcohol, bool foreignSlash, bool fragileTargets, bool peerBingxin, string? qualificationColors, bool cancelTarget, bool simpleUseCompletion) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:draw-funded-basic", "1.0.0", "实际摸牌基本牌的真实原生路径");
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rs = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-wang-xiang.rules.json")!;
            using var ps = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-wang-xiang.presentation.json")!;
            using var rr = new StreamReader(rs); using var pr = new StreamReader(ps);
            var production = SkillProgramCatalog.Load(rr.ReadToEnd(), pr.ReadToEnd());
            b.AddSkill(new(Bingxin, "冰心", "完整实际摸牌共享能力") { Program = production.Programs[Bingxin] });
            var root = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:dfb-driver","revision":1,"activations":[
                {"id":"trim","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
                {"id":"budget","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":8}]},
                {"id":"remove","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:bingxin"],"sourceBind":"fixture:dfb-noop"}]},
                {"id":"regain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:bingxin"]}]},
                {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]},
                {"id":"die","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":6}]},
                {"id":"hurt-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"die-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                {"id":"target-slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWhoseAttackRangeIncludesOwner","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"answer"}]},
                {"id":"nearest-slash","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByNearest","target":"owner","targetKind":"otherLiving","amount":1}]},
                {"id":"assisted-slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashAgainstChosenTarget","target":"selectedTarget","resultBind":"answer"}]},
                {"id":"nearest-legal-slash","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestLegalSlashByNearest","target":"owner","targetKind":"otherLiving","amount":1}]}]},
              {"id":"fixture:dfb-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"fixture:dfb-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.draw-funded-distinct-basic.draw"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:dfb-completed","revision":1,"triggers":[{"id":"done","window":"cardUseCompleted","optional":false,"ownerRelation":"actor","includeResponseUses":true,"cardKinds":["slash","fireSlash","thunderSlash","dodge","peach"],"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:dfb-rescue","revision":1,"triggers":[{"id":"rescue","window":"selfDyingResponse","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]},{"op":"useVirtualDyingAlcohol","target":"owner"}]}]},
              {"id":"fixture:dfb-hp","revision":1,"triggers":[{"id":"hp","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:dfb-foreign","revision":1,"viewAs":[{"id":"real","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"useOnly":true}],"triggers":[{"id":"slash","window":"turnEnding","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"effects":[{"op":"useOwnerSlashAgainstTurnOwner","target":"owner","ignoreDistance":true}]}]}
            ]}
            """)!;
            root["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            root["skills"]![2]!["triggers"]![0]!["optional"] = optionalGain;
            if (simpleUseCompletion) root["skills"]![3]!["triggers"]![0]!["cardKinds"]!.AsArray().Add("alcohol");
            var gainEffects = root["skills"]![2]!["triggers"]![0]!["effects"]!.AsArray();
            if (sourceLoss) gainEffects.Add(JsonNode.Parse("""{"op":"grantSkills","target":"owner","skillIds":["fixture:dfb-suppress"]}"""));
            if (legacyAlcohol) gainEffects.Add(JsonNode.Parse("""{"op":"loseHp","target":"owner","amount":6}"""));
            if (qualificationColors == "mixed") root["skills"]![0]!["modifiers"] = JsonNode.Parse("""[{"id":"owner-initial","query":"initialHandSize","operation":"add","value":4,"priority":0,"condition":{"kind":"always"}}]""");
            if (qualificationColors == "empty-draw") root["skills"]![0]!["modifiers"] = JsonNode.Parse("""[{"id":"owner-initial","query":"initialHandSize","operation":"add","value":6,"priority":0,"condition":{"kind":"always"}}]""");
            if (cancelTarget)
            {
                root["skills"]![2]!["triggers"]![0]!["usageScope"] = "game"; root["skills"]![2]!["triggers"]![0]!["usageLimit"] = 1;
                gainEffects.Add(JsonNode.Parse("""{"op":"selectTarget","target":"owner","targetKind":"otherLiving"}"""));
                gainEffects.Add(JsonNode.Parse("""{"op":"loseHp","target":"selectedTarget","amount":4}"""));
            }
            // An unowned, mature dynamic-round binding enables the public Round
            // clock solely for the independent extra-turn comparison. Bingxin's
            // owner/method/actual-turn name ledger never reads this binding.
            root["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:dfb-round-clock","revision":1,"triggers":[{"id":"clock","window":"afterTurnEnded","subject":"owner","optional":true,"usageScope":"round","dynamicUsageLimit":{"kind":"alivePlayersCapped","cap":1},"effects":[{"op":"pendExtraTurn","target":"owner"}]}]}"""));
            // Load the mature normalization dependency only for this legacy
            // producer scenario. No actor owns or can execute this binding.
            if (legacyAlcohol) root["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:dfb-legacy-ordinal-anchor","revision":1,"triggers":[{"id":"ordinal","window":"cardUseCompleted","ownerRelation":"observer","singleActionInstance":true,"optional":false,"condition":{"kind":"cardActionActualTurnUseOrdinalIs","value":1},"effects":[{"op":"recover","target":"owner","amount":1}]}]}"""));
            var labels = root["skills"]!.AsArray().ToDictionary(node => node!["id"]!.GetValue<string>(), node =>
            {
                var label = new Dictionary<string, object> { ["name"] = "实际规则子链", ["description"] = "原生真实结算" };
                if (node!["id"]!.GetValue<string>() is Gain or Completed or Rescue or Hp)
                    label["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                return (object)label;
            });
            var catalog = SkillProgramCatalog.Load(root.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = 3, skills = labels }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "真实小夹具") { Program = catalog.Programs[id] });
            b.AddSkill(new(Suppress, "真实资格抑制", "只抑制资格，不物理删除旧实例") { SuppressionRule = new(6) });
            b.AddSkill(new("fixture:dfb-noop", "来源替换", "无运行能力"));
            b.AddSkill(new("fixture:dfb-selection", "固定候选", "固定小夹具") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            var ownerSkills = new List<string> { Driver, Gain, Completed };
            if (legacyAlcohol) { ownerSkills.Add(Rescue); ownerSkills.Add(Hp); }
            b.AddGeneral(new("fixture:dfb-owner", "共享机制拥有者", "supporter", Bingxin, "jin", qualificationColors == "mixed" ? 3 : 5, ownerSkills, GeneralGender.Male));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:dfb-other-{i}", "固定真实目标", "supporter", "fixture:dfb-selection", "qun", fragileTargets ? 1 : 4,
                foreignSlash ? ["fixture:dfb-quiet", "fixture:dfb-foreign"] : peerBingxin ? [Bingxin, "fixture:dfb-quiet"] : ["fixture:dfb-quiet"], GeneralGender.Male));
            // The four-card mixed deck belongs wholly to the owner because only
            // this actor has a native initial-hand modifier of +4. Thus its
            // two real colors are fixed independently of shuffle order or seed.
            var tinyDeck = qualificationColors is "mixed" or "empty-draw";
            b.AddDeck(new("fixture:dfb-deck", "固定真实实体杀", tinyDeck ? 0 : 4, tinyDeck ? 0 : 2, [])
            { PhysicalCards = Enumerable.Range(0, qualificationColors == "mixed" ? 4 : qualificationColors == "empty-draw" ? 6 : 80).Select(i => new ContentDeckPhysicalCard("standard:slash",
                qualificationColors == "colorless" ? Suit.None : qualificationColors == "mixed" && i % 2 == 1 ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "实际摸牌共享方向", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:dfb-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:dfb-owner", "fixture:dfb-other-1", "fixture:dfb-other-2", "fixture:dfb-other-3"]));
        }
    }
}
