using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GiftedSlashHandPolicyChecks
{
    private const string Actual = "ol:fuman", Activation = "give-slash", Bind = "fuman-gift";
    private const string Driver = "fixture:gifted-slash-driver", GiftChild = "fixture:gifted-slash-gain";
    private const string Committed = "fixture:gifted-slash-committed", Completed = "fixture:gifted-slash-completed";
    private const string DrawChild = "fixture:gifted-slash-draw", ExtraPlay = "fixture:gifted-slash-extra-play";
    private const string ResponseChild = "fixture:gifted-slash-response";
    private const string SkipFirstPlay = "fixture:gifted-slash-skip-first-play";
    private const string Owner = "fixture:gifted-slash-owner", Mode = "identity:classic-gifted-slash";

    public static void GiftedNonSlashUsesFreezeDamageRewardAndColdChildren()
    {
        foreach (var damage in new[] { false, true })
        {
            var (game, registry) = Start();
            var before = game.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToArray();
            var retained = game.CreateSnapshot(2); var retainedJson = SnapshotJson.Serialize(retained);
            var given = Give(ref game, registry, 1);
            Require(before.Contains(given) && game.CreateSnapshot(1).Players[1].Hand.Single(c => c.Id == given).Kind == CardKind.Dodge &&
                    game.CardMovements.Count(m => m.CardId == given && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)) == 1,
                "The actual registered activation gives one printed non-Slash entity to the original recipient exactly once.");
            Accept(game, new EndPlayPhaseCommand(0, game.Revision));
            Reach(game, p => ContinuePrompt(p, Committed));
            var use = BenefitUse(game); var useId = use.Id; var benefit = use.GiftedSlashUseBenefit!;
            Require(use.SourceSeat == 1 && use.CardId == given && use.CardKind == CardKind.Slash &&
                    use.Action is { Type: CardActionType.Use, ActorSeat: 1, ProviderSeat: 1, EffectiveKind: CardKind.Slash } action &&
                    action.PhysicalCards is [{ CardId: var material, From: var from }] && material == given && from == CardLocation.Hand(1) &&
                    benefit.Policy.Source.OwnerSeat == 0 && benefit.Policy.Source.SkillId == Actual &&
                    benefit.Policy.RecipientSeat == 1 && benefit.Policy.CardId == given && benefit.CardUseFrameId == useId &&
                    benefit.ActionId == action.ActionId && benefit.OriginalActorSeat == 1 && benefit.ProviderSeat == 1 &&
                    benefit.Material == action.PhysicalCards.Single() &&
                    game.CardMovements.Count(m => m.CardId == given && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1,
                "Native AI freezes the exact continuous-Hand policy on its genuine one-material Use before payment children; the actual actor/provider remain the recipient.");
            AssertPrivate(game, 1); Frozen(use.Action!.PhysicalCards); Frozen(P(game)!.Choices);
            Reject(game, new AnswerPromptCommand(0, P(game)!.PromptId, P(game)!.Choices.First().Id, game.Revision));
            game = Cold(game, registry);
            Reach(game, p => p is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 });
            AssertPrivate(game, 0); game = Cold(game, registry);
            Answer(game, damage
                ? c => c.Parameters.GetValueOrDefault("response") == "take-damage"
                : c => c.Cards.Count == 1 && game.CreateSnapshot(0).Players[0].Hand.Any(card => card.Id == c.Cards[0] && card.Kind == CardKind.Dodge));
            Reach(game, p => ContinuePrompt(p, DrawChild));
            var reward = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.GiftedSlashRewardReceipt is not null);
            var receipt = reward.GiftedSlashRewardReceipt!; var count = damage ? 2 : 1;
            var drawWindow = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == reward.Id);
            Require(reward.OwnerSeat == 0 && reward.SkillId == Actual && reward.WindowContext?.Window == SkillProgramTriggerWindow.CardUseCompleted &&
                    reward.WindowContext.CardUse?.ParentCardUseFrameId == useId && receipt.Benefit == benefit &&
                    receipt.Benefit.CardUseFrameId == useId && receipt.Benefit.ActionId == benefit.ActionId && receipt.FrozenDrawCount == count &&
                    receipt.DrawActual == count && receipt.InstructionIndex == 1 && reward.InstructionIndex == 1 &&
                    drawWindow.Batch.OriginOwnerSeat == 0 && drawWindow.Batch.OriginSkillId == Actual &&
                    drawWindow.Batch.OriginSkillInstanceId == reward.SkillInstanceId && drawWindow.Batch.Movements is [var draw] &&
                    draw.From == CardLocation.DrawPile && draw.To == CardLocation.Hand(0) && draw.Reason.Value == DrawReason &&
                    draw.Sequence > receipt.Before && draw.Sequence <= receipt.After &&
                    game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == useId).GiftedSlashUseBenefit == benefit &&
                    game.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.From == CardLocation.DrawPile &&
                        m.Sequence > receipt.Before && m.Sequence <= receipt.After) == count,
                "The completed original Use freezes damage-dependent Draw1/Draw2 once, and the exact reward parent waits for its genuine native draw child. " +
                JsonSerializer.Serialize(new { receipt, reward.InstructionIndex, reward.WindowContext }));
            AssertPrivate(game, 0); Frozen(P(game)!.Choices); Frozen(drawWindow.Batch.Movements); RejectWrongActor(game); RejectUnpublished(game);
            game = Cold(game, registry);
            Reach(game, IsHumanPlay);
            Require(Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == useId && e.CardId == given && e.CardKind == CardKind.Slash) == 1 &&
                    Facts<DamageAppliedEvent>(game).Count(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1) == (damage ? 1 : 0) &&
                    Facts<CardRespondedEvent>(game).Count(e => e.ResponderSeat == 0 && e.EffectiveCardKind == CardKind.Dodge) == (damage ? 0 : 1) &&
                    Facts<ProgramBindingResolvedEvent>(game).Count(e => e.FrameId == reward.Id && e.SkillId == Actual && e.Activated && e.Completed) == 1 &&
                    Facts<ProgramBindingResolvedEvent>(game).Count(e => e.SkillId == Completed && e.OwnerSeat == 1 && e.Activated && e.Completed) == 1 &&
                    Facts<ProgramBindingResolvedEvent>(game).Count(e => e.SkillId == DrawChild && e.OwnerSeat == 0 && e.Activated && e.Completed) == count &&
                    game.CreateSnapshot(0).Players[0].HandCount == before.Length - 1 - (damage ? 0 : 1) + count &&
                    game.CardMovements.Count(m => m.CardId == given && m.To == CardLocation.DiscardPile && m.From == CardLocation.Processing) == 1 &&
                    game.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Sequence > receipt.Before && m.Sequence <= receipt.After) == count &&
                    Facts<GiftedSlashUseBenefitIssuedEvent>(game).Count(e => e.Benefit == benefit) == 1 &&
                    Facts<GiftedSlashRewardDrawIssuedEvent>(game).Count(e => e.FrameId == reward.Id && e.Benefit == benefit && e.RequestedCount == count && e.ActualCount == count) == 1 &&
                    Facts<GiftedSlashRewardResolvedEvent>(game).Count(e => e.FrameId == reward.Id && e.Benefit == benefit && e.RequestedCount == count && e.ActualCount == count) == 1 &&
                    !game.ResolutionStack.Any(f => f.Id == reward.Id || f.Id == useId) && SnapshotJson.Serialize(retained) == retainedJson,
                "Cold paid gift, Use, completion and draw children return once without duplicate payment, unrelated damage rewards, hidden-hand disclosure or observer mutation.");
            _ = Cold(game, registry);
        }
    }

    public static void DepartedGiftEntitiesCannotLendIdentityOrRewardToOtherActors()
    {
        foreach (var discard in new[] { false, true })
        {
            var (game, registry) = Start(); var given = Give(ref game, registry, 1);
            var policy = Facts<GiftedSlashHandPolicyGrantedEvent>(game).Single(e => e.Policy.CardId == given).Policy;
            var donorHand = game.CreateSnapshot(0).Players[0].HandCount;
            UseDriver(game, discard ? "discard-hand" : "take-hand", 1); Reach(game, IsHumanPlay);
            Require(Location(game, given) == (discard ? CardLocation.DiscardPile : CardLocation.Hand(0)) &&
                    game.CardMovements.Count(m => m.CardId == given && m.From == CardLocation.Hand(1)) == 1 &&
                    game.GetHumanLegalActions().All(a => a.CardId != given || a.Kind != LegalActionKind.Slash),
                "A genuine ordinary foreign-Hand cost or transfer ends the policy on the exact entity; the donor cannot borrow the recipient's identity.");
            Reject(game, new PlayCardCommand(0, given, [1], game.Revision, P(game)!.PromptId, CardKind.Slash) { ConversionSource = policy.Source });
            game = Cold(game, registry);
            if (!discard)
            {
                UseDriver(game, "send-hand", 2); Reach(game, IsHumanPlay);
                Require(Location(game, given) == CardLocation.Hand(2) && game.CreateSnapshot(2).Players[2].Hand.Single(c => c.Id == given).Kind == CardKind.Dodge,
                    "A real third-party transfer retains the printed entity and cannot inherit the old recipient's Slash policy.");
                game = Cold(game, registry);
                Accept(game, new EndPlayPhaseCommand(0, game.Revision)); Reach(game, IsHumanPlay);
                Require(Location(game, given) == CardLocation.Hand(2) && !Facts<CardUseFinishedEvent>(game).Any(e => e.CardId == given) &&
                        !Facts<ProgramBindingStartedEvent>(game).Any(e => e.SkillId == Actual && e.Window == SkillProgramTriggerWindow.CardUseCompleted),
                    "The third holder's true native Play cannot use a printed Dodge as the departed gifted Slash or issue an old provider reward.");
                UseDriver(game, "take-hand", 2); Reach(game, IsHumanPlay);
                UseDriver(game, "send-hand", 1); Reach(game, IsHumanPlay); game = Cold(game, registry);
                Require(Location(game, given) == CardLocation.Hand(1), "Native ordinary movement returns the same entity to its former recipient.");
            }
            Accept(game, new EndPlayPhaseCommand(0, game.Revision)); Reach(game, IsHumanPlay);
            Require(!Facts<CardUseFinishedEvent>(game).Any(e => e.CardId == given) &&
                    !Facts<ProgramBindingStartedEvent>(game).Any(e => e.SkillId == Actual && e.Window == SkillProgramTriggerWindow.CardUseCompleted) &&
                    !Facts<ProgramBindingResolvedEvent>(game).Any(e => e.SkillId == DrawChild) &&
                    !Facts<GiftedSlashUseBenefitIssuedEvent>(game).Any(e => e.Benefit.Policy == policy) &&
                    !Facts<GiftedSlashRewardDrawIssuedEvent>(game).Any(e => e.Benefit.Policy == policy) &&
                    Facts<ProgramSkillStartedEvent>(game).Count(e => e.SkillId == Actual && e.ActivationId == Activation) == 1 &&
                    Facts<SkillUsageConsumedEvent>(game).Count(e => e.SkillId == Actual && e.UsageId == Activation + ":target:1") == 1 &&
                    (discard ? game.CreateSnapshot(0).Players[0].HandCount == donorHand : Location(game, given) == CardLocation.Hand(1)),
                "Leaving the original Hand permanently invalidates this issuance; ordinary costs, third holders and a later return neither resurrect its identity nor draw a use reward.");
            _ = Cold(game, registry);
        }
    }

    public static void RespondedGiftedSlashConsumesEntityWithoutUseReward()
    {
        var (game, registry) = Start(duel: true);
        var initialHp = game.CreateSnapshot(1).Players[1].Hp; var preparationCards = new List<int>();
        for (var i = 0; i < 2; i++)
        {
            var preparation = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Duel && a.TargetSeats.SequenceEqual([1]));
            preparationCards.Add(preparation.CardId!.Value);
            Accept(game, PlayCommand(game, preparation)); Reach(game, IsHumanPlay);
        }
        Require(game.CreateSnapshot(1).Players[1].Hp == initialHp - 2 && game.CreateSnapshot(0).Players[0].HandCount == 2 &&
                Facts<DamageAppliedEvent>(game).Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == 2 &&
                preparationCards.Distinct().Count() == 2 && preparationCards.All(id =>
                    Facts<CardUseFinishedEvent>(game).Count(e => e.CardId == id && e.CardKind == CardKind.Duel) == 1 &&
                    game.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1),
            "Two genuine printed Duels pay two distinct original Hand entities and naturally injure the AI twice, making its normal published Slash response worthwhile without injected HP or decision policy.");
        game = Cold(game, registry); var given = Give(ref game, registry, 1);
        var action = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Duel && a.CardId != given && a.TargetSeats.SequenceEqual([1]));
        var duel = action.CardId!.Value;
        Accept(game, PlayCommand(game, action));
        Reach(game, p => p is { Kind: DecisionKind.RespondSlash, PlayerSeat: 1 });
        Require(P(game)!.Choices.Any(c => c.Cards.SequenceEqual([given]) && c.Parameters.GetValueOrDefault("response") == "slash"),
            "The true native AI response prompt publishes its only intrinsic gifted Slash as a legal one-entity response.");
        AssertPrivate(game, 1); Frozen(P(game)!.Choices);
        Reject(game, new AnswerPromptCommand(0, P(game)!.PromptId, P(game)!.Choices.First().Id, game.Revision));
        game = Cold(game, registry); Reach(game, p => ContinuePrompt(p, ResponseChild));
        var child = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == ResponseChild);
        var response = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(f => f.Id == child.WindowContext!.ParentFrameId);
        Require(Facts<CardRespondedEvent>(game).Single(e => e.CardId == given) is { ResponderSeat: 1, SourceSeat: 0, EffectiveCardKind: CardKind.Slash } &&
                response.Continuation == ProgramCardContinuation.DuelSlash && response.Action is { Type: CardActionType.Response, ActorSeat: 1, ProviderSeat: 1, EffectiveKind: CardKind.Slash } &&
                response.Action.PhysicalCards is [{ CardId: var paid, From: var origin }] && paid == given && origin == CardLocation.Hand(1) &&
                response.Action.ConversionChain.Contains(Facts<GiftedSlashHandPolicyGrantedEvent>(game).Single().Policy.Source) &&
                response.ParentFrameId == child.WindowContext!.CardUse?.ParentCardUseFrameId && Location(game, given) == CardLocation.Processing &&
                game.ResolutionStack.OfType<CardUseFrame>().All(f => f.GiftedSlashUseBenefit is null) &&
                game.CardMovements.Count(m => m.CardId == given && m.From == CardLocation.Hand(1)) == 1 &&
                !Facts<CardUseFinishedEvent>(game).Any(e => e.CardId == given),
            "Native AI can genuinely respond to Duel with its only intrinsic gifted Slash, while ordinary Response creates no rewarded Use frame.");
        AssertPrivate(game, 1); Frozen(response.Action.PhysicalCards); Frozen(P(game)!.Choices);
        Reject(game, new AnswerPromptCommand(0, P(game)!.PromptId, P(game)!.Choices.First().Id, game.Revision));
        game = Cold(game, registry); Reach(game, IsHumanPlay);
        Require(Location(game, given) == CardLocation.DiscardPile &&
                Facts<CardRespondedEvent>(game).Count(e => e.CardId == given && e.ResponderSeat == 1 && e.EffectiveCardKind == CardKind.Slash) == 1 &&
                Facts<DuelResponseEvent>(game).Count(e => e.ResolutionId == response.ParentFrameId && e.ResponderSeat == 1 && e.UsedSlash && e.SlashCardId == given) == 1 &&
                game.CardMovements.Count(m => m.CardId == given && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Respond) == 1 &&
                game.CardMovements.Count(m => m.CardId == given && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.ResponseFinished) == 1 &&
                Facts<CardUseFinishedEvent>(game).Count(e => e.CardId == duel && e.CardKind == CardKind.Duel) == 1 &&
                !Facts<CardUseFinishedEvent>(game).Any(e => e.CardId == given) &&
                Facts<DamageAppliedEvent>(game).Count(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1) == 1 &&
                !Facts<ProgramBindingStartedEvent>(game).Any(e => e.SkillId == Actual && e.Window == SkillProgramTriggerWindow.CardUseCompleted) &&
                !Facts<GiftedSlashUseBenefitIssuedEvent>(game).Any(e => e.Benefit.Policy.CardId == given) &&
                !Facts<GiftedSlashRewardDrawIssuedEvent>(game).Any(e => e.Benefit.Policy.CardId == given) &&
                Facts<ProgramBindingResolvedEvent>(game).Count(e => e.FrameId == child.Id && e.SkillId == ResponseChild && e.Activated && e.Completed) == 1 &&
                !Facts<ProgramBindingStartedEvent>(game).Any(e => e.SkillId == DrawChild),
            "The real response pays its one entity once; unrelated Duel damage and completion do not issue Draw1 or Draw2 for a gift used only as a response.");
        _ = Cold(game, registry);
    }

    public static void GiftTargetPhaseQuotaAndContinuousHandLifetimeUseRealPhasesAndTurns()
    {
        var (phase, phaseRegistry) = Start(extraPlay: true); var turn = phase.State.TurnNumber;
        _ = Give(ref phase, phaseRegistry, 1);
        Require(!GiftAction(phase).SelectableTargetSeats.Contains(1) && GiftAction(phase).SelectableTargetSeats.Contains(2),
            "The actual target-phase ledger removes only the paid recipient, preserving another legal target in the same real Play.");
        Reject(phase, new UseProgramSkillCommand(0, Actual, Activation, [], [1], phase.Revision, P(phase)!.PromptId));
        _ = Give(ref phase, phaseRegistry, 2);
        Accept(phase, new EndPlayPhaseCommand(0, phase.Revision)); Reach(phase, IsHumanPlay);
        Require(phase.State.TurnNumber == turn && GiftAction(phase).SelectableTargetSeats.Contains(1),
            "A genuinely inserted Play returns to normal Play in the same actual turn with a new target-phase quota.");
        _ = Give(ref phase, phaseRegistry, 1);
        Require(Facts<SkillUsageConsumedEvent>(phase).Count(e => e.SkillId == Actual && e.UsageId == Activation + ":target:1" && e.Scope == SkillUsageScope.Phase) == 2 &&
                Facts<SkillUsageConsumedEvent>(phase).Count(e => e.SkillId == Actual && e.UsageId == Activation + ":target:2" && e.Scope == SkillUsageScope.Phase) == 1 &&
                Facts<ProgramSkillResolvedEvent>(phase).Count(e => e.SkillId == Actual) == 3,
            "Two actual Play phases pay the same target separately, and another target in the first phase pays exactly once.");
        UseDriver(phase, "send-hand", 2); Reach(phase, IsHumanPlay);
        Require(phase.CreateSnapshot(0).Players[0].HandCount == 0 &&
                phase.GetHumanLegalActions().All(a => a.ProgramSkillId != Actual) &&
                !Facts<SkillUsageConsumedEvent>(phase).Any(e => e.SkillId == Actual && e.UsageId == Activation + ":target:3"),
            "A true empty giver Hand suppresses the published gift activation before any private selection or target-ledger payment.");
        Reject(phase, new UseProgramSkillCommand(0, Actual, Activation, [], [3], phase.Revision, P(phase)!.PromptId));
        UseDriver(phase, "take-hand", 2); Reach(phase, IsHumanPlay);
        Require(phase.State.TurnNumber == turn && phase.CreateSnapshot(0).Players[0].HandCount > 0 && GiftAction(phase).SelectableTargetSeats.Contains(3),
            "Real card acquisition in the same actual Play restores the activation and preserves the target quota rejected while the giver was empty.");
        _ = Give(ref phase, phaseRegistry, 3);
        Require(Facts<SkillUsageConsumedEvent>(phase).Count(e => e.SkillId == Actual && e.UsageId == Activation + ":target:3" && e.Scope == SkillUsageScope.Phase) == 1 &&
                Facts<ProgramSkillResolvedEvent>(phase).Count(e => e.SkillId == Actual) == 4,
            "The formerly rejected target can subsequently receive a genuine gift in the same Play, paying its ledger and entity exactly once.");
        _ = Cold(phase, phaseRegistry);

        var (game, registry) = Start(skipFirstPlay: true); var given = Give(ref game, registry, 1);
        var firstTurn = game.State.TurnNumber;
        Accept(game, new EndPlayPhaseCommand(0, game.Revision)); Reach(game, IsHumanPlay);
        Require(game.State.TurnNumber > firstTurn && Facts<TurnEndedEvent>(game).Any(e => e.ActorSeat == 1) &&
                Location(game, given) == CardLocation.Hand(1) && !Facts<CardUseFinishedEvent>(game).Any(e => e.CardId == given),
            "The actual recipient finishes its first turn with skipped Play while continuously holding the same gifted entity; this hand policy has no turn expiry.");
        game = Cold(game, registry);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision)); Reach(game, p => ContinuePrompt(p, Committed));
        var use = BenefitUse(game); var benefit = use.GiftedSlashUseBenefit!;
        Require(use.SourceSeat == 1 && use.CardId == given && benefit.Policy.CardId == given &&
                benefit.Policy.ActualTurnNumber == firstTurn && game.State.TurnNumber > firstTurn &&
                benefit.Policy.RecipientSeat == 1 && benefit.Policy.Source.OwnerSeat == 0,
            "The original continuous-Hand policy survives both intervening turn ends and cold replay, then freezes on the recipient's next native real Use.");
        var useId = use.Id; game = Cold(game, registry);
        Reach(game, p => p is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 });
        Answer(game, c => c.Cards.Count == 1); Reach(game, p => ContinuePrompt(p, DrawChild));
        var reward = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.GiftedSlashRewardReceipt is not null);
        Require(reward.GiftedSlashRewardReceipt is { FrozenDrawCount: 1, DrawActual: 1 } r && r.Benefit.CardUseFrameId == useId && r.Benefit == benefit,
            "The later damage-free real Use rewards the original donor once, rather than expiring at the next or recipient turn boundary.");
        game = Cold(game, registry); Reach(game, IsHumanPlay);
        Require(Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == useId && e.CardId == given) == 1 &&
                Facts<ProgramBindingResolvedEvent>(game).Count(e => e.FrameId == reward.Id && e.Activated && e.Completed) == 1 &&
                game.CardMovements.Count(m => m.CardId == given && m.From == CardLocation.Hand(1) && m.Reason == CardMoveReasons.Use) == 1,
            "The across-turn native Use completes exactly once with one material payment and one original-provider draw return.");
        _ = Cold(game, registry);
    }

    private static int Give(ref GameEngine game, ContentRegistry registry, int recipient)
    {
        var card = game.CreateSnapshot(0).Players[0].Hand.First().Id;
        Require(GiftAction(game).SelectableTargetSeats.Contains(recipient), "The registered gift activation must publish this actual legal recipient.");
        Accept(game, new UseProgramSkillCommand(0, Actual, Activation, [], [recipient], game.Revision, P(game)!.PromptId));
        Reach(game, p => p?.SkillPrompt?.SkillId == Actual && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
        AssertPrivate(game, 0); Frozen(P(game)!.Choices); RejectWrongActor(game); RejectUnpublished(game);
        game = Cold(game, registry); Answer(game, c => c.Cards.SequenceEqual([card]));
        Reach(game, p => ContinuePrompt(p, GiftChild));
        var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.GiftedSlashGiftReceipt is not null);
        var receipt = frame.GiftedSlashGiftReceipt!;
        var binding = frame.CardSetBindings.Single(b => b.Name == Bind);
        var batch = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.Movements.Any(m => m.CardId == card && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(recipient)));
        var child = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == GiftChild);
        Require(frame.OwnerSeat == 0 && frame.SkillId == Actual && frame.ActivationId == Activation && frame.InstructionIndex == 3 &&
                frame.SelectedTargetSeats.SequenceEqual([recipient]) && binding.CardIds.SequenceEqual([card]) && binding.SourceLocations.SequenceEqual([CardLocation.Hand(0)]) &&
                receipt.InstructionIndex == 3 && receipt.Policy.ProgramFrameId == frame.Id && receipt.Policy.InstructionIndex == 3 &&
                receipt.Policy.Source == new CardConversionSource(Actual, Activation, 0, frame.SkillInstanceId) &&
                receipt.Policy.GameplayHash == frame.GameplayHash && receipt.Policy.RecipientSeat == recipient && receipt.Policy.CardId == card &&
                receipt.Material.CardId == card && receipt.Material.From == CardLocation.Hand(0) &&
                batch.Batch.ParentFrameId == frame.Id && batch.Batch.OriginOwnerSeat == 0 && batch.Batch.OriginSkillId == Actual &&
                batch.Batch.OriginSkillInstanceId == frame.SkillInstanceId && batch.Batch.Movements.Count == 1 &&
                receipt.Policy.GiftBatchId == batch.Batch.Id && receipt.Policy.GiftMovementSequence == batch.Batch.Movements.Single().Sequence &&
                batch.Batch.Movements.Single().Sequence > receipt.Before && batch.Batch.Movements.Single().Sequence <= receipt.After &&
                Facts<GiftedSlashGiftStartedEvent>(game).Count(e => e.FrameId == frame.Id &&
                    e.Policy == (receipt.Policy with { GiftMovementSequence = 0, GiftBatchId = 0 }) && e.Material == receipt.Material && e.SequenceBefore == receipt.Before) == 1 &&
                Facts<GiftedSlashHandPolicyGrantedEvent>(game).Count(e => e.Policy == receipt.Policy && e.Material == receipt.Material) == 1 &&
                child.OwnerSeat == recipient && child.WindowContext?.ParentFrameId == batch.Id && child.WindowContext.MovementBatch?.Id == batch.Batch.Id &&
                !Facts<ProgramSkillResolvedEvent>(game).Any(e => e.FrameId == frame.Id),
            "The paid single-Hand gift waits on its exact original typed movement/gain child with its frozen binding and source instance, before advancing the parent cursor.");
        Frozen(binding.CardIds); Frozen(binding.SourceLocations); Frozen(batch.Batch.Movements); Frozen(P(game)!.Choices); AssertPrivate(game, recipient);
        Reject(game, new AnswerPromptCommand(0, P(game)!.PromptId, P(game)!.Choices.First().Id, game.Revision));
        game = Cold(game, registry); Reach(game, IsHumanPlay);
        Require(Facts<ProgramSkillResolvedEvent>(game).Count(e => e.FrameId == frame.Id && e.SkillId == Actual) == 1 &&
                Facts<GiftedSlashGiftResolvedEvent>(game).Count(e => e.FrameId == frame.Id && e.Policy == receipt.Policy) == 1 &&
                game.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(recipient)) == 1,
            "Cold restoration returns the actual paid gift once without repaying or duplicating its phase-ledger cost.");
        return card;
    }

    private static LegalAction GiftAction(GameEngine game) => game.GetHumanLegalActions().Single(a => a.ProgramSkillId == Actual && a.ProgramActivationId == Activation);
    private static CardUseFrame BenefitUse(GameEngine game) => game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.GiftedSlashUseBenefit is not null);
    private static CardLocation Location(GameEngine game, int id) => game.CreateCardZoneDiagnostics().Single(z => z.CardId == id).Location;
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsHumanPlay(PendingDecision? p) => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static bool ContinuePrompt(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static T[] Facts<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void UseDriver(GameEngine game, string activation, int target) => Accept(game,
        new UseProgramSkillCommand(0, Driver, activation, [], [target], game.Revision, P(game)!.PromptId));
    private static PlayCardCommand PlayCommand(GameEngine game, LegalAction action) => new(0, action.CardId!.Value, action.TargetSeats, game.Revision, P(game)!.PromptId, action.PlayedCardKind, action.TargetCardId)
    { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources };
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    {
        Require(P(game)?.PlayerSeat == 0, "Human Answer is restricted to the real human seat; foreign AI choices must use native Advance.");
        var selected = P(game)!.Choices.FirstOrDefault(predicate);
        Require(selected is not null, "No published human choice matches the intended actual response: " + Diagnostic(game));
        Accept(game, new AnswerPromptCommand(0, P(game)!.PromptId, selected!.Id, game.Revision));
    }
    private static void Reach(GameEngine game, Func<PendingDecision?, bool> stop)
    {
        for (var step = 0; step < 384; step++)
        {
            var prompt = P(game); if (stop(prompt)) return;
            if (prompt is { PlayerSeat: 0 })
            {
                if (prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
                else throw new InvalidOperationException("Unexpected real human gifted-Slash boundary: " + Diagnostic(game));
            }
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The bounded native gifted-Slash fixture missed its requested boundary: " + Diagnostic(game));
    }
    private static string Diagnostic(GameEngine game) => JsonSerializer.Serialize(new
    {
        Prompt = P(game), game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase,
        LastFacts = game.Events.Where(e => e.Payload is DuelResponseEvent or DamageAppliedEvent or ResponseRequestedEvent or CardUseFinishedEvent or
            GiftedSlashHandPolicyGrantedEvent or GiftedSlashUseBenefitIssuedEvent or GiftedSlashRewardDrawIssuedEvent or GiftedSlashRewardResolvedEvent or
            ProgramBindingStartedEvent or ProgramBindingResolvedEvent or ProgramSkillStartedEvent or ProgramSkillResolvedEvent)
            .TakeLast(12).Select(e => new { e.Sequence, Type = e.Payload.GetType().Name, Payload = JsonSerializer.Serialize(e.Payload, e.Payload.GetType()) }).ToArray()
    });
    private static void AssertPrivate(GameEngine game, int actor) => Require(P(game) is { IsPrivate: true, PlayerSeat: var seat } && seat == actor &&
        Enumerable.Range(0, 4).Where(s => s != actor).All(s => game.CreateSnapshot(s).PendingDecision is null && game.CreateSnapshot(s).Players[actor].Hand.Count == 0),
        "Only the actual chooser sees its private choices and undisclosed Hand entities; other player snapshots retain no private prompt or hand faces.");
    private static void Frozen<T>(IReadOnlyList<T> list)
    {
        Require(list.Count > 0, "Immutability needs a genuine published nonempty collection.");
        var refused = false; try { ((IList<T>)list)[0] = list[0]; } catch (NotSupportedException) { refused = true; }
        Require(refused, "Committed nested entity, origin and prompt collections must refuse observer mutation.");
    }
    private static void RejectWrongActor(GameEngine game) => Reject(game, new AnswerPromptCommand(1, P(game)!.PromptId, P(game)!.Choices.First().Id, game.Revision));
    private static void RejectUnpublished(GameEngine game) => Reject(game, new AnswerPromptCommand(0, P(game)!.PromptId, new ChoiceId("gifted-slash:not-published"), game.Revision));
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "The genuine serialized command was rejected.");
    }
    private static void Reject(GameEngine game, GameCommand command)
    {
        var before = State(game); var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(!result.Accepted && result.Error is not null && State(game) == before,
            "Wrong actors, unpublished choices, wrong holders and exhausted target-phase quota reject atomically without moving cards, advancing a parent or adding accepted history.");
    }
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
        Require(State(restored) == State(game), "Accepted-command cold replay preserves true gifted identity, paid costs, private views, typed children, frozen beneficiary and quota exactly."); return restored;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static string Rules() => $$$"""
    {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
      {"id":"{{{Driver}}}","revision":1,"activations":[
        {"id":"take-hand","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[
          {"op":"selectOwnedCards","target":"selectedTarget","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"taken"},
          {"op":"moveBoundCards","target":"owner","sourceBind":"taken","destination":"ownerHand","awaitMovementTriggers":true}]},
        {"id":"send-hand","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[
          {"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"sent"},
          {"op":"moveBoundCards","target":"owner","sourceBind":"sent","destination":"selectedTargetHand"}]},
        {"id":"discard-hand","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[
          {"op":"selectOwnedCards","target":"selectedTarget","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"discarded"},
          {"op":"moveBoundCards","target":"owner","sourceBind":"discarded","destination":"discardPile","awaitMovementTriggers":true}]}]},
      {"id":"{{{GiftChild}}}","revision":1,"triggers":[{"id":"paid-gift-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{{GiftReason}}}"],"optional":false,"effects":[
        {"op":"chooseOption","target":"owner","resultBind":"gift-child","options":[{"id":"continue"}]}]}]},
      {"id":"{{{Committed}}}","revision":1,"triggers":[{"id":"true-slash-committed","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[
        {"op":"chooseOption","target":"owner","resultBind":"committed-child","options":[{"id":"continue"}]}]}]},
      {"id":"{{{Completed}}}","revision":1,"triggers":[{"id":"true-slash-completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["slash","fireSlash","thunderSlash"],"singleActionInstance":true,"optional":false,"effects":[
        {"op":"chooseOption","target":"owner","resultBind":"completed-child","options":[{"id":"continue"}]}]}]},
      {"id":"{{{DrawChild}}}","revision":1,"triggers":[{"id":"reward-native-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{{DrawReason}}}"],"optional":false,"effects":[
        {"op":"chooseOption","target":"owner","resultBind":"draw-child","options":[{"id":"continue"}]}]}]},
      {"id":"{{{ResponseChild}}}","revision":1,"triggers":[{"id":"native-response-payment","window":"cardResponseAccepted","ownerRelation":"actor","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[
        {"op":"chooseOption","target":"owner","resultBind":"response-child","options":[{"id":"continue"}]}]}]},
      {"id":"{{{ExtraPlay}}}","revision":1,"triggers":[{"id":"genuine-inserted-play","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[
        {"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
      {"id":"{{{SkipFirstPlay}}}","revision":1,"triggers":[{"id":"first-real-recipient-turn","window":"afterNormalDraw","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[
        {"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
    """;
    private static string GiftReason => $"skill-program.{Actual}.GiveBoundHandAsSlashWithUseReward.gift";
    private static string DrawReason => $"skill-program.{Actual}.RewardGiftedSlashUse.draw";
    private static string Presentation() => JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
        skills = new[] { Driver, GiftChild, Committed, Completed, DrawChild, ResponseChild, ExtraPlay, SkipFirstPlay }.ToDictionary(id => id, id => new
        { name = id, description = "连续手牌杀身份与真实完成收益", optionLabels = id is GiftChild or Committed or Completed or DrawChild or ResponseChild
            ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) });
    private static (GameEngine, ContentRegistry) Start(bool duel = false, bool extraPlay = false, bool skipFirstPlay = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(duel, extraPlay, skipFirstPlay));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, P(game)!.PromptId)); Reach(game, IsHumanPlay);
        Require(game.CreateSnapshot(0).Players[0].Hand.Count == 4 && game.CreateSnapshot(1).Players[1].Hand.Count == 4,
            "The fixed physical fixture begins with four genuine entities per participant without seed searches or injected zones.");
        return (game, registry);
    }
    private sealed class Fixture(bool duel, bool extraPlay, bool skipFirstPlay) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("gifted-slash-hand-policy-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(Rules(), Presentation());
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, id, "原生指令共享机制") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddSkill(new("fixture:gifted-slash-pick-owner", "固定主公", "公开选将偏好") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? 100000d : -100000d) });
            builder.AddSkill(new("fixture:gifted-slash-pick-peer", "固定受赠者", "公开选将偏好") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? -100000d : 100000d) });
            builder.AddGeneral(new(Owner, "实际抚蛮费用本人", "supporter", "fixture:gifted-slash-pick-owner", "shu", 20,
                [Actual, Driver, DrawChild, .. (extraPlay ? new[] { ExtraPlay } : [])]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:gifted-slash-peer-{i}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "固定原生受赠者", "supporter", "fixture:gifted-slash-pick-peer", "wei", 20,
                [GiftChild, Committed, Completed, .. (duel ? new[] { ResponseChild } : []), .. (skipFirstPlay ? new[] { SkipFirstPlay } : [])]));
            builder.AddDeck(new("fixture:gifted-slash-deck", "固定真实赠牌实体", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ =>
                new ContentDeckPhysicalCard(duel ? "standard:duel" : "standard:dodge", Suit.Heart, 7)).ToArray() });
            builder.AddMode(new(Mode, "连续手牌杀身份与完成收益", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:gifted-slash-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
