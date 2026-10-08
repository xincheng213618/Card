using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class PhaseEndingVirtualTrickChecks
{
    private const string Qianya = "ol:qianya";
    private const string Shuomeng = "ol:shuomeng";
    private const string Owner = "fixture:ending-sun-qian";
    private const string Rank = "fixture:ending-contest-rank";
    private const string Pause = "fixture:ending-gift-child";
    private const string Completed = "fixture:ending-trick-completed";
    private const string Tail = "fixture:ending-tail";
    private const string Peer = "fixture:ending-peer-pick";
    private const string Mode = "identity:classic-ending-virtual-trick-4";
    private const string GiftReason = "skill-program.ol:qianya.MoveBoundCards";

    public static void EndingPindianWinLossTieUseExactVirtualTricksAndColdReturn()
    {
        foreach (var rank in new[] { 13, 1, 7 }) ExerciseEnding(rank, giftAll: false, counter: false);
    }

    public static void QianyaGiftEmptyDismantleAndRealNullificationReturn()
    {
        ExerciseEnding(1, giftAll: true, counter: false);
        ExerciseEnding(1, giftAll: false, counter: true);
        ExerciseEnding(13, giftAll: false, counter: true);
    }

    public static void EndingVirtualDrawTwoResolvesRealAdjustedTargetsAndReturnsOnce() =>
        ExerciseEnding(13, giftAll: false, counter: false, adjusted: true);

    private static void ExerciseEnding(int rank, bool giftAll, bool counter, bool adjusted = false)
    {
        var (game, registry) = Start(rank, delayed: false, counterMaterial: !giftAll, adjusted: adjusted);
        long? adjustmentGrantId = null;
        if (adjusted)
        {
            var legal = game.GetHumanLegalActions().Single(a => a.ProgramSkillId == "boundary:qiaoshui-current" && a.ProgramActivationId == "contest");
            var cost = game.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.Crossbow).Id;
            Accept(game, new UseProgramSkillCommand(0, legal.ProgramSkillId!, legal.ProgramActivationId!, [cost], [1], game.Revision, P(game)!.PromptId));
            Reach(game, p => p.PlayerSeat == 1 && HasNative(p, "pindian-card"));
            game = Cold(game, registry);
            RejectWrongActor(game);
            Answer(game, c => c.Cards.Count == 1);
            Reach(game, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
            var grant = Facts<NextActualUseTargetAdjustmentGrantedEvent>(game).Single();
            adjustmentGrantId = grant.ProgramFrameId;
            Require(grant.Source is { OwnerSeat: 0, SkillId: "boundary:qiaoshui-current" } &&
                    Facts<PindianResultDeterminedEvent>(game).Single().Result.SourceWon &&
                    game.State.Players[0].HandCount == 3 && !Facts<NextActualUseTargetAdjustmentConsumedEvent>(game).Any(),
                "A real earlier paid winning Qiaoshui contest issues the one unconsumed actual-use target token before ending.");
        }
        var before = game.State.Players[0].HandCount;
        var turn = game.State.TurnNumber;
        Require(before == (adjusted ? 3 : 4) && (giftAll
                ? game.CreateCardZoneDiagnostics().All(c => c.CardKind != CardKind.Nullification)
                : game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == 35 && c.Kind == CardKind.Nullification) &&
                    game.CreateCardZoneDiagnostics().All(c => c.CardKind != CardKind.Nullification || c.Location == CardLocation.Hand(0))),
            "Seed 7 must supply the human's sole real counterspell: hand=" + JsonSerializer.Serialize(game.CreateSnapshot(0).Players[0].Hand) +
                "; counterspell zones=" + JsonSerializer.Serialize(game.CreateCardZoneDiagnostics().Where(c => c.CardKind == CardKind.Nullification)));
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
        Reach(game, p => p.SkillPrompt?.SkillId == Shuomeng && Has(p, "activate"));
        Require(game.State is { CurrentSeat: 0, Phase: TurnPhase.Play } &&
                game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single() is
                    { OwnerSeat: 0, Window: SkillProgramTriggerWindow.PlayEnding, Continuation: ProgramLifecycleContinuation.CompletePlayPhase },
            "The actual play-ending coordinator pauses before discard at Sun Qian's optional native content binding.");
        game = Cold(game, registry);
        RejectWrongActor(game);
        Answer(game, c => Action(c) == "activate");
        Reach(game, p => p.SkillPrompt?.SkillId == Shuomeng && Has(p, "select-target"));
        Answer(game, c => c.Targets.SequenceEqual([1]));
        Reach(game, p => HasNative(p, "pindian-source-card"));
        var producer = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Shuomeng);
        var producerId = producer.Id;
        var contest = game.ResolutionStack.OfType<PindianFrame>().Single();
        var contestId = contest.Id;
        var endingId = producer.WindowContext!.ParentFrameId;
        var sourceCard = P(game)!.Choices.First(c => c.Cards.Count == 1 && c.Cards[0] != 35).Cards.Single();
        Require(contest.ParentFrameId == producerId && contest.SourceSeat == 0 && contest.OpponentSeat == 1 &&
                contest.ProgramResultBind == "shuomeng-contest" && producer.PindianResultBindings.Count == 0 &&
                producer.WindowContext.Window == SkillProgramTriggerWindow.PlayEnding &&
                game.ResolutionStack[^2].Id == producerId && game.ResolutionStack[^1].Id == contestId,
            "Ending Pindian owns its exact suspended content program and typed ending parent before either physical commitment.");
        AssertPrivate(game, 0);
        game = Cold(game, registry);
        RejectWrongActor(game);
        Answer(game, c => c.Cards.SequenceEqual([sourceCard]));
        Reach(game, p => HasNative(p, "pindian-card") && p.PlayerSeat == 1);
        var opponentCard = P(game)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        AssertPrivate(game, 1);
        Require(game.CreateSnapshot(2).PublicRevealedCards.Count == 0 &&
                !game.CardMovements.Any(m => (m.CardId == sourceCard || m.CardId == opponentCard) && m.To == CardLocation.Processing),
            "The source's selected hand identity stays uncommitted and hidden until the opponent makes its real choice.");
        game = Cold(game, registry);
        RejectWrongActor(game);
        Answer(game, c => c.Cards.SequenceEqual([opponentCard]));
        var won = rank == 13;
        var expectedKind = won ? CardKind.DrawTwo : CardKind.Dismantlement;
        var expectedActor = won ? 0 : 1;
        if (adjusted)
        {
            Reach(game, p => p.SkillPrompt?.SkillId == Shuomeng && Has(p, "use-virtual-ordinary-trick"));
            Require(P(game) is { PlayerSeat: 0 } choice && choice.Choices.All(c => c.Cards.Count == 0),
                "The still-pending real token publishes the virtual trick's legal target adjustment to its actual human user.");
            game = Cold(game, registry);
            RejectWrongActor(game);
            Answer(game, c => c.Targets.SequenceEqual([0, 2]));
        }
        Reach(game, p => p.SkillPrompt?.SkillId == Qianya && Has(p, "activate"));
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == 0 && f.CardKind == expectedKind);
        var useId = use.Id;
        var action = use.Action ?? throw new InvalidOperationException("A virtual ordinary trick must retain its accepted action.");
        var result = Facts<PindianResultDeterminedEvent>(game).Single(e => e.FrameId == contestId).Result;
        var frozen = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == producerId).PindianResultBindings.Single();
        Require(result is { SourceSeat: 0, OpponentSeat: 1, OpponentRank: 7 } && result.SourceRank == rank && result.SourceWon == won &&
                result.SourceCardId == sourceCard && result.OpponentCardId == opponentCard &&
                frozen.Name == "shuomeng-contest" && frozen.SourceWon == won && frozen.SourceRank == rank &&
                frozen.Visibility == SkillProgramCardSetVisibility.Public &&
                action is { Type: CardActionType.Use, PhysicalCards.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0 } &&
                action.EffectiveKind == expectedKind && action.ActorSeat == expectedActor && action.ProviderSeat == expectedActor &&
                action.TargetSeats.SequenceEqual(adjusted ? [0, 2] : [0]) && action.ConversionChain.Count == 0 &&
                use.VirtualOrdinaryTrickOrigin is { } origin && origin.ParentProgramFrameId == producerId &&
                origin.LifecycleFrameId == endingId && origin.CardActionId == action.ActionId &&
                origin.SkillId == Shuomeng && origin.OwnerSeat == 0 && origin.SkillInstanceId == producer.SkillInstanceId &&
                origin.InitialActorSeat == expectedActor && origin.InitialTargetSeat == 0 &&
                origin.OutputKind == expectedKind && origin.PindianBind == "shuomeng-contest" && origin.FrozenSourceWon == won &&
                use.PhysicalCardIds is { Count: 0 } && use.SourceSeat == expectedActor &&
                Facts<CardUseDeclaredEvent>(game).Count(e => e.ResolutionId == useId && e.CardId == 0 && e.SourceSeat == expectedActor && e.CardKind == expectedKind) == 1 &&
                !Facts<ProgramBindingResolvedEvent>(game).Any(e => e.FrameId == producerId) &&
                !Facts<CardUseFinishedEvent>(game).Any(e => e.ResolutionId == useId),
            "The frozen win, loss or tie issues exactly the correct user and target with zero entities, while its original ending program still waits for completion.");
        AssertPindianPayment(game, result);
        game = Cold(game, registry);
        RejectWrongActor(game);
        Answer(game, c => Action(c) == "activate");
        Reach(game, p => p.SkillPrompt?.SkillId == Qianya && Has(p, "select-target"));
        Answer(game, c => c.Targets.SequenceEqual([2]));
        Reach(game, p => p.SkillPrompt?.SkillId == Qianya && Has(p, "select-owned-cards"));
        var qianya = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Qianya);
        var giftIds = game.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToArray();
        Require(qianya.WindowContext?.CardUse?.CardActionId == action.ActionId &&
                qianya.WindowContext.CardUse.ParentCardUseFrameId == useId &&
                qianya.OwnedCardSelection is { MinimumCount: 0, SelectedCardIds.Count: 0, AllowEarlyFinish: true },
            "Actual Qianya reads the exact virtual target window and publishes a legal zero-card finish before moving any hand entities.");
        game = Cold(game, registry);
        RejectWrongActor(game);
        if (giftAll)
        {
            foreach (var id in giftIds) Answer(game, c => c.Cards.SequenceEqual([id]));
            Reach(game, p => p.SkillPrompt?.SkillId == Pause && HasOption(p, "gift-return"));
            var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
            Require(movement.Batch.ParentFrameId == qianya.Id && movement.ResumeProgramFrameId == qianya.Id &&
                    movement.Batch.OriginSkillId == Qianya && movement.Batch.Movements.Count == giftIds.Length &&
                    movement.Batch.Movements.Select(m => m.CardId).Order().SequenceEqual(giftIds.Order()) &&
                    movement.Batch.Movements.All(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(2) && m.Reason.Value == GiftReason) &&
                    game.State.Players[0].HandCount == 0 && game.State.Players[2].HandCount == 4 + giftIds.Length &&
                    game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == qianya.Id).BoundParticipantGift is not null &&
                    game.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == useId) &&
                    !Facts<CardUseFinishedEvent>(game).Any(e => e.ResolutionId == useId) &&
                    !Facts<TargetCardDiscardedEvent>(game).Any(e => e.ResolutionId == useId),
                "All actual remaining hand cards move once in one awaited Qianya batch; the exact trick and ending parent wait while the recipient's native child is suspended.");
            AssertPrivate(game, 2);
            game = Cold(game, registry);
            RejectWrongActor(game);
            Continue(game);
        }
        else
        {
            Answer(game, c => Action(c) == "finish-owned-cards");
            Require(!game.CardMovements.Any(m => m.Reason.Value == GiftReason),
                "Finishing the real zero-card Qianya selection creates no synthetic payment or gift movement.");
        }
        var counterCard = 0;
        var secondTargetSeen = false;
        for (var step = 0; step < 50; step++)
        {
            var prompt = P(game);
            if (prompt?.SkillPrompt?.SkillId == Completed && HasOption(prompt, "completion-return")) break;
            if (prompt is { Kind: DecisionKind.Nullification })
            {
                Require(prompt.PlayerSeat == 0, "Only the real human owns the fixture's printed counterspell response boundary.");
                var window = game.ResolutionStack.OfType<NullificationWindowFrame>().Single();
                Require(window.ParentFrameId == useId && window.EffectCardId == 0 && window.EffectCardKind == expectedKind &&
                        window.SourceSeat == expectedActor && (window.TargetSeats.SequenceEqual([0]) || adjusted && window.TargetSeats.SequenceEqual([2])),
                    "The genuine Nullification window stays attached to the exact zero-entity trick, actor and target.");
                if (adjusted && window.TargetSeats.SequenceEqual([2]))
                {
                    secondTargetSeen = true;
                    var currentUse = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == useId);
                    Require(currentUse is { TargetIndex: 1, SequentialTrick: not null, CardId: 0 } &&
                            currentUse.VirtualOrdinaryTrickOrigin == use.VirtualOrdinaryTrickOrigin &&
                            game.State.Players[0].HandCount == before + 1 && game.State.Players[2].HandCount == 4 &&
                            !Facts<CardUseFinishedEvent>(game).Any(e => e.ResolutionId == useId) &&
                            game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == producerId),
                        "Target zero's two real draws finish before the zero-entity sequential cursor reaches target two, with the same typed producer still waiting.");
                }
                game = Cold(game, registry);
                RejectWrongActor(game);
                if (counter && counterCard == 0)
                {
                    var choice = P(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("response") == "nullification" && c.Cards.SequenceEqual([35]));
                    counterCard = choice.Cards.Single();
                    Answer(game, c => c.Id == choice.Id);
                }
                else Answer(game, c => c.Parameters.GetValueOrDefault("response") == "pass");
            }
            else if (prompt is { Kind: DecisionKind.SelectTargetCard, PlayerSeat: not 0 })
                Accept(game, new AdvanceOneStepCommand(game.Revision));
            else if (prompt is null) Accept(game, new AdvanceOneStepCommand(game.Revision));
            else throw new InvalidOperationException("Unexpected ending trick boundary: " + Boundary(game));
        }
        Require(P(game)?.SkillPrompt?.SkillId == Completed && HasOption(P(game)!, "completion-return"),
            "The small native trick must reach its exact completion child within fifty steps: " + Boundary(game));
        var completion = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(f => f.Action.ActionId == action.ActionId);
        Require(completion.ParentFrameId == useId && completion.Continuation == ProgramCardContinuation.CompletedCard &&
                Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == useId && e.CardId == 0 && e.CardKind == expectedKind) == 1 &&
                game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == producerId) &&
                !Facts<ProgramBindingResolvedEvent>(game).Any(e => e.FrameId == producerId) &&
                game.State.Phase == TurnPhase.Play,
            "One exact use finishes before its completion child, while the producer and play-ending return are still suspended.");
        game = Cold(game, registry);
        RejectWrongActor(game);
        Continue(game);
        Reach(game, p => p.SkillPrompt?.SkillId == Tail && HasOption(p, "ending-tail"));
        Require(game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single().Id == endingId &&
                !game.ResolutionStack.Any(f => f.Id == producerId || f.Id == useId || f.Id == contestId) &&
                Facts<ProgramBindingResolvedEvent>(game).Count(e => e.FrameId == producerId && e.SkillId == Shuomeng && e.Completed) == 1 &&
                Facts<CardActionAcceptedEvent>(game).Count(e => e.Action.ActionId == action.ActionId) == 1 &&
                Facts<PindianResultDeterminedEvent>(game).Count(e => e.FrameId == contestId) == 1 &&
                Facts<CardUseDeclaredEvent>(game).Count(e => e.ResolutionId == useId) == 1 &&
                Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == useId) == 1 &&
                Facts<ProgramBindingResolvedEvent>(game).Count(e => e.FrameId == qianya.Id && e.Completed) == 1 &&
                !game.CardMovements.Any(m => m.CardId == 0),
            "All restored children return once to the original ending coordinator, with one contest, one virtual action and no fictitious card movement.");
        var expectedHand = giftAll ? 0 : before - 1 + (won && !counter ? 2 : 0) - (counter || !won ? 1 : 0);
        Require(game.State.Players[0].HandCount == expectedHand && game.State.TurnNumber == turn,
            $"Exactly the native contest, draw/discard or printed counterspell changes the human hand: expected {expectedHand}, actual {game.State.Players[0].HandCount}.");
        var nullification = Facts<NullificationResolvedEvent>(game).Where(e => e.ResolutionId == useId).ToArray();
        Require(nullification.Length == (adjusted ? 2 : 1) && nullification.All(resolved =>
                resolved.EffectCardKind == expectedKind && resolved.EffectCardId == 0 &&
                resolved.EffectNullified == counter && resolved.ChainDepth == (counter ? 1 : 0)),
            "Passing or paying the real response chain produces one exact effective-kind terminal fact.");
        if (adjusted)
            Require(secondTargetSeen && game.State.Players[2].HandCount == 6 &&
                    Facts<NextActualUseTargetAdjustmentConsumedEvent>(game).ToArray() is [var consumed] &&
                    consumed.GrantProgramFrameId == adjustmentGrantId && consumed.OriginFrameId == useId &&
                    consumed.CardActionId == action.ActionId && consumed.EffectiveKind == CardKind.DrawTwo && !consumed.IsNullificationUse &&
                    Facts<ProgramOptionChosenEvent>(game).Count(e => e.SkillId == Completed && e.ResultBind == "completion-return") == 1,
                "Both native target effects cold-return through one virtual action, one actual paid-token consumption and one whole-use completion child.");
        if (counter)
        {
            Require(counterCard == 35 && Facts<NullificationRespondedEvent>(game).Where(e => e.ResolutionId == useId).ToArray() is
                    [{ ResponderSeat: 0, NullificationCardId: 35, ChainDepth: 1, EffectCardId: 0 } response] && response.EffectCardKind == expectedKind &&
                    game.CardMovements.Count(m => m.CardId == 35 && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                    game.CardMovements.Count(m => m.CardId == 35 && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
                    !Facts<TargetCardDiscardedEvent>(game).Any(e => e.ResolutionId == useId),
                "A genuine printed counterspell pays and finishes its entity once and prevents the virtual target effect.");
        }
        else if (giftAll)
            Require(Facts<CardEffectSkippedEvent>(game).Where(e => e.ResolutionId == useId).ToArray() is
                    [{ SourceSeat: 1, TargetSeat: 0, CardKind: CardKind.Dismantlement, Reason: CardEffectSkipReason.TargetHandEmpty }] &&
                    !Facts<TargetCardDiscardedEvent>(game).Any(e => e.ResolutionId == useId) &&
                    game.CardMovements.Count(m => m.Reason.Value == GiftReason) == giftIds.Length,
                "After Qianya's real gift empties all HEJ, Dismantlement skips legally and never charges the gift a second time.");
        else Require(won || Facts<TargetCardDiscardedEvent>(game).Where(e => e.ResolutionId == useId).ToArray() is [{ SourceSeat: 1, TargetSeat: 0 }],
            "Loss and tie each perform one native opponent-to-owner Dismantlement after the zero-card gift.");
        AssertPindianPayment(game, result);
        game = Cold(game, registry);
        Continue(game);
        for (var step = 0; step < 8 && game.State.Phase == TurnPhase.Play; step++) Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(game.State.Phase != TurnPhase.Play && Facts<ProgramOptionChosenEvent>(game).Count(e => e.SkillId == Tail && e.ResultBind == "ending-tail") == 1 &&
                Facts<PindianResultDeterminedEvent>(game).Count(e => e.FrameId == contestId) == 1 &&
                game.CreateCardZoneDiagnostics().All(c => c.Location != CardLocation.Processing),
            "Completing one tail advances the ending phase once without restarting Pindian or retaining a physical payment in processing.");
        _ = Cold(game, registry);
    }

    public static void DelayedIndulgenceTriggersActualQianyaAndReturnsAfterGiftChild()
    {
        var (game, registry) = Start(7, delayed: true);
        var sun = game.CreateSnapshot(0).Players.Single(p => p.GeneralId == "ol:sun-qian").Seat;
        Require(sun != 0 && game.State.Players[sun].HandCount == 4, "The actual registered Sun Qian occupies a living foreign target seat.");
        var legal = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Indulgence && a.TargetSeats.SequenceEqual([sun]));
        var cardId = legal.CardId!.Value;
        Accept(game, new PlayCardCommand(0, cardId, legal.TargetSeats, game.Revision, P(game)!.PromptId, legal.PlayedCardKind, legal.TargetCardId)
        { ConversionSource = legal.ConversionSource, AdditionalConversionSources = legal.AdditionalConversionSources });
        Reach(game, p => p.SkillPrompt?.SkillId == Qianya && Has(p, "activate"));
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == cardId && f.CardKind == CardKind.Indulgence);
        var useId = use.Id;
        Require(use.Action is { ActorSeat: 0, EffectiveKind: CardKind.Indulgence, PhysicalCards.Count: 1 } action &&
                action.PhysicalCards[0].CardId == cardId && action.TargetSeats.SequenceEqual([sun]) &&
                game.CreateCardZoneDiagnostics().Single(c => c.CardId == cardId).Location == CardLocation.Processing &&
                !Facts<DelayedCardPlacedEvent>(game).Any(e => e.ResolutionId == useId),
            "A real physical delayed trick opens actual Qianya before judgment placement, retaining its original accepted entity and target.");
        game = Cold(game, registry);
        RejectWrongActor(game);
        Answer(game, c => Action(c) == "activate");
        Reach(game, p => p.SkillPrompt?.SkillId == Qianya && Has(p, "select-target"));
        Answer(game, c => c.Targets.SequenceEqual([0]));
        Reach(game, p => p.SkillPrompt?.SkillId == Qianya && Has(p, "select-owned-cards"));
        var qianyaId = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Qianya).Id;
        var giftIds = game.CreateSnapshot(sun).Players[sun].Hand.Select(c => c.Id).ToArray();
        AssertPrivate(game, sun);
        game = Cold(game, registry);
        RejectWrongActor(game);
        foreach (var id in giftIds) Answer(game, c => c.Cards.SequenceEqual([id]));
        Reach(game, p => p.SkillPrompt?.SkillId == Pause && HasOption(p, "gift-return"));
        var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
        Require(movement.Batch.ParentFrameId == qianyaId && movement.ResumeProgramFrameId == qianyaId &&
                movement.Batch.Movements.Select(m => m.CardId).Order().SequenceEqual(giftIds.Order()) &&
                movement.Batch.Movements.All(m => m.From == CardLocation.Hand(sun) && m.To == CardLocation.Hand(0) && m.Reason.Value == GiftReason) &&
                game.State.Players[sun].HandCount == 0 && game.State.Players[0].HandCount == 3 + giftIds.Length &&
                !Facts<DelayedCardPlacedEvent>(game).Any(e => e.ResolutionId == useId) &&
                game.CreateCardZoneDiagnostics().Single(c => c.CardId == cardId).Location == CardLocation.Processing,
            "The target's real hand gift finishes payment in one awaited batch before the still-physical delayed card can enter judgment.");
        game = Cold(game, registry);
        RejectWrongActor(game);
        Continue(game);
        Reach(game, p => p.SkillPrompt?.SkillId == Completed && HasOption(p, "completion-return"));
        Require(Facts<DelayedCardPlacedEvent>(game).Where(e => e.ResolutionId == useId).ToArray() is
                    [{ SourceSeat: 0, CardKind: CardKind.Indulgence } placed] && placed.TargetSeat == sun && placed.CardId == cardId &&
                Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == useId && e.CardKind == CardKind.Indulgence) == 1 &&
                game.CreateSnapshot(0).Players[sun].Judgment.Single().Id == cardId,
            "After the restored gift child returns, the exact physical Indulgence is placed once even though Qianya emptied its target's hand.");
        game = Cold(game, registry);
        Continue(game);
        Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        Require(Facts<ProgramBindingResolvedEvent>(game).Count(e => e.FrameId == qianyaId && e.Completed) == 1 &&
                Facts<CardUseDeclaredEvent>(game).Count(e => e.ResolutionId == useId) == 1 &&
                Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == useId) == 1 &&
                game.CardMovements.Count(m => m.Reason.Value == GiftReason) == giftIds.Length &&
                game.CardMovements.Count(m => m.CardId == cardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                game.CardMovements.Count(m => m.CardId == cardId && m.From == CardLocation.Processing && m.To == CardLocation.Judgment(sun)) == 1 &&
                !game.CardMovements.Any(m => m.CardId == cardId && m.To == CardLocation.DiscardPile) &&
                game.CreateCardZoneDiagnostics().All(c => c.Location != CardLocation.Processing),
            "Actual delayed-card Qianya, gift payment and completion return each finish once through real commands and cold restoration.");
        _ = Cold(game, registry);
    }

    private static void AssertPindianPayment(GameEngine game, PindianResult result)
    {
        foreach (var (seat, card) in new[] { (result.SourceSeat, result.SourceCardId), (result.OpponentSeat, result.OpponentCardId) })
            Require(game.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(seat) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal) == 1 &&
                    game.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.PindianFinish) == 1,
                "Each exact contest entity is revealed and discarded once, including after resumed virtual-trick children.");
    }
    private static IEnumerable<T> Facts<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static string? Action(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static bool Has(PendingDecision p, string action) => p.Choices.Any(c => Action(c) == action);
    private static bool HasNative(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == action);
    private static bool HasOption(PendingDecision p, string binding) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == binding);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> done)
    {
        for (var step = 0; step < 70; step++)
        {
            var p = P(game);
            if (p is not null && done(p)) return;
            if (p is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected human boundary while seeking the exact ending child: " + Boundary(game));
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The bounded ending fixture did not reach its exact child: " + Boundary(game));
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> choose)
    {
        var p = P(game) ?? throw new InvalidOperationException("The real ending prompt is absent.");
        Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, game.Revision));
    }
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void AssertPrivate(GameEngine game, int chooser) => Require(P(game) is { IsPrivate: true } p && p.PlayerSeat == chooser &&
        Enumerable.Range(0, 4).Where(s => s != chooser).All(s => game.CreateSnapshot(s).PendingDecision is null && game.CreateSnapshot(s).Players[chooser].Hand.Count == 0),
        "Only the exact native chooser receives its private prompt and hand identities.");
    private static void RejectWrongActor(GameEngine game)
    {
        var p = P(game)!;
        var before = State(game);
        Require(!game.Submit(new AnswerPromptCommand((p.PlayerSeat + 1) % 4, p.PromptId, p.Choices.First().Id, game.Revision)).Accepted && State(game) == before,
            "A wrong actor cannot answer the suspended native child or mutate any accepted history or private view.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "Rejected real ending-trick command.");
    }
    private static string Boundary(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.State.CurrentSeat, game.State.Phase,
        Prompt = P(game) is { } prompt ? new
        {
            prompt.Kind, prompt.PlayerSeat, Skill = prompt.SkillPrompt?.SkillId,
            Choices = prompt.Choices.Select(c => new { c.Id, c.Cards, c.Targets, c.Parameters }).ToArray()
        } : null,
        Frames = game.ResolutionStack.Select(f => f switch
        {
            ProgramSkillFrame program => $"{f.Id}:program:{program.SkillId}:instruction-{program.InstructionIndex}:parent-{program.WindowContext?.ParentFrameId}",
            CardUseFrame use => $"{f.Id}:use:{use.CardKind}:actor-{use.SourceSeat}:targets-{string.Join(',', use.TargetSeats)}:targetIndex-{use.TargetIndex}:virtualParent-{use.VirtualOrdinaryTrickOrigin?.ParentProgramFrameId}",
            NullificationWindowFrame window => $"{f.Id}:nullification:parent-{window.ParentFrameId}:targets-{string.Join(',', window.TargetSeats)}:depth-{window.ChainDepth}",
            _ => $"{f.Id}:{f.Kind}:{f.Step}"
        }).ToArray()
    });
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), game.CardMovements,
        Events = game.Events.Select(e => $"{e.Sequence}|{JsonSerializer.Serialize(e.Payload, e.Payload.GetType())}").ToArray(),
        Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(restored) == State(game), "All ending parents, private child drafts, frozen actions, actual entity movements and accepted commands cold-restore identically.");
        return restored;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Start(int rank, bool delayed, bool counterMaterial = true, bool adjusted = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(rank, delayed, counterMaterial, adjusted));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, P(game)!.PromptId));
        Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        return (game, registry);
    }
    private sealed class Fixture(int rank, bool delayed, bool counterMaterial, bool adjusted) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("ending-virtual-trick-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                  {"id":"{{{Rank}}}","revision":1,"cardPolicies":[{"id":"spade-rank","kind":"pindianRankBySuit","inputSuit":"spade","value":{{{rank}}}}]},
                  {"id":"{{{Pause}}}","revision":1,"triggers":[{"id":"gift-movement-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["{{{GiftReason}}}"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gift-return","options":[{"id":"continue"}]}]}]},
                  {"id":"{{{Completed}}}","revision":1,"triggers":[{"id":"exact-completion-child","window":"cardUseCompleted","ownerRelation":"observer","cardKinds":["drawTwo","dismantlement","indulgence"],"singleActionInstance":true,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"completion-return","options":[{"id":"continue"}]}]}]},
                  {"id":"{{{Tail}}}","revision":1,"triggers":[{"id":"after-content-ending","window":"playEnding","subject":"owner","priority":-1000,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"ending-tail","options":[{"id":"continue"}]}]}]}]}
                """, JsonSerializer.Serialize(new
                {
                    schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new[] { Rank, Pause, Completed, Tail }.ToDictionary(id => id, id => new
                    {
                        name = id, description = "真实阶段结束拼点与虚拟锦囊子窗",
                        optionLabels = id == Rank ? new Dictionary<string, string>() : new Dictionary<string, string> { ["continue"] = "继续" }
                    })
                }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "原生结束与付款观察")
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Peer, "固定其他角色", "角色评分")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? -10000d : 10000d) });
            // The production Sun Qian programs run unchanged; extra fixture observers expose real return boundaries.
            b.AddGeneral(new(Owner, "真实说盟当事人", "supporter", delayed ? Peer : Qianya, "shu", 8,
                delayed ? [Pause, Completed, Tail] : adjusted ? [Shuomeng, Rank, Pause, Completed, Tail, "boundary:qiaoshui-current"] : [Shuomeng, Rank, Pause, Completed, Tail]));
            var peers = Enumerable.Range(1, delayed ? 2 : 3).Select(i => $"fixture:ending-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "真实拼点角色", "supporter", Peer, "wei", 8, [Pause]));
            b.AddDeck(new("fixture:ending-deck", "固定真实牌", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(
                    delayed ? "standard:indulgence" : counterMaterial && i == 34 ? "standard:nullification" : "standard:crossbow", Suit.Spade, 7)).ToArray()
            });
            b.AddMode(new(Mode, "共享阶段结束虚拟锦囊", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:ending-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: delayed ? [Owner, .. peers, "ol:sun-qian"] : [Owner, .. peers]));
        }
    }
}
