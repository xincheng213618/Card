using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryFaZhengChecks
{
    private const string Xuanhuo = "boundary:xuanhuo";
    private const string Enyuan = "boundary:enyuan";
    private const string Driver = "fixture:fz-driver";
    private const string Recovery = "fixture:fz-recovery";
    private const string Reward = "fixture:fz-reward";
    private const string RepaymentGain = "fixture:fz-repayment-gain";
    private const string RepaymentHp = "fixture:fz-repayment-hp";
    private const string DyingEntry = "fixture:fz-dying-entry";
    private const string RescueCommitted = "fixture:fz-rescue-committed";
    private const string TwoCardRescue = "fixture:fz-two-card-rescue";
    private const string Mode = "identity:classic-boundary-fa-zheng-fixture";

    public static void XuanhuoRealSlashAndNativeAiRetainPaidGift()
    {
        var (game, registry) = Create(); ReachXuanhuo(game);
        var given = BeginGift(game, registry, 1);
        Reach(game, p => Action(p, "assisted-physical-slash") && p.PlayerSeat == 0);
        var owner = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Xuanhuo);
        Require(owner.BoundParticipantGift is null && owner.AssistedSlashRequest is { ActorSeat: 1, TargetSeat: null } &&
            given.All(id => game.CreateSnapshot(1).Players[1].Hand.Any(c => c.Id == id)),
            "The actual atomic two-card gift finishes before the owner specifies the other actor's real Slash target.");
        Require(!Prompt(game)!.Choices.Any(c => c.Targets.SequenceEqual([3])),
            "The designated target uses the commanded actor's actual range, excluding distance two.");
        Replay(game, registry); Reject(game); Answer(game, c => c.Targets.SequenceEqual([2]));
        Reach(game, p => Action(p, "assisted-physical-slash") && p.PlayerSeat == 1);
        var offered = Prompt(game)!.Choices.Where(c => c.Parameters.GetValueOrDefault("request-option") == "use")
            .SelectMany(c => c.Cards).Distinct().ToArray();
        Require(offered.Length > 0 && Prompt(game)!.Choices.Any(c => c.Parameters.GetValueOrDefault("request-option") == "decline"),
            "The actual giver chooses a physical Slash or an explicit refusal.");
        for (var viewer = 0; viewer < 4; viewer++) if (viewer != 1)
            Require(game.CreateSnapshot(viewer).PendingDecision is null,
                "The commanded actor's physical payment choices stay private to that actor.");
        Replay(game, registry);
        // The actor is a native AI. Its own command advances the real published use choice.
        Accept(game, new AdvanceOneStepCommand(game.Revision)); ReachPlay(game);
        Require(game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Count(e => e.SourceSeat == 1 && e.TargetSeat == 2) == 1 &&
            game.CardMovements.Any(m => offered.Contains(m.CardId) && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing) &&
            !game.Events.Any(e => e.Payload is ProgramPrivateHandViewedEvent),
            "Native AI pays its real Slash and resolves actual damage, then bypasses the private-hand refusal branch.");
        foreach (var id in given) Require(GiftMoves(game, id).Count() == 1, "Resuming the Slash child never pays the two-card gift twice.");
        Replay(game, registry);
    }

    public static void XuanhuoDeclinePrivateSelectionAndEnyuanSourceReward()
    {
        var (game, registry) = Create(equipment: true); ReachXuanhuo(game);
        BeginGift(game, registry, 1); Reach(game, p => Action(p, "assisted-physical-slash") && p.PlayerSeat == 0);
        Answer(game, c => c.Targets.SequenceEqual([2]));
        Reach(game, p => Action(p, "assisted-physical-slash") && p.PlayerSeat == 1);
        Require(Prompt(game)!.Choices.All(c => c.Parameters.GetValueOrDefault("request-option") == "decline"),
            "A real equipment-only hand has no physical Slash and publishes the explicit refusal.");
        Replay(game, registry); Accept(game, new AdvanceOneStepCommand(game.Revision));
        Reach(game, p => Action(p, "private-hand-take"));
        var viewed = game.CreateSnapshot(1).Players[1].Hand.Select(c => c.Id).ToArray();
        var draft = HandTake(game).PrivateHandTake!; var ownerHand = game.State.Players[0].HandCount;
        Require(draft.RequiredCount == 2 && draft.ViewedCardIds.SequenceEqual(viewed) &&
            (game.CreateSnapshot(0).PrivateRevealedCards ?? []).Select(c => c.Id).Order().SequenceEqual(viewed.Order()) &&
            Prompt(game)!.Choices.All(c => c.Cards.Count == 1 && viewed.Contains(c.Cards[0])),
            "Refusal privately reveals the source's actual hand and offers only those frozen entities.");
        AssertPrivateTake(game, viewed); AssertFrozen(draft.ViewedCardIds); Replay(game, registry); Reject(game);
        var first = Prompt(game)!.Choices[0].Cards.Single(); Answer(game, c => c.Cards.SequenceEqual([first]));
        Require(game.State.Players[0].HandCount == ownerHand && game.State.Players[1].HandCount == viewed.Length &&
            HandTake(game).PrivateHandTake!.SelectedCardIds.SequenceEqual([first]) &&
            !Prompt(game)!.Choices.Any(c => c.Cards.Contains(first)),
            "Selecting the first entity only freezes a private draft; no partial acquisition occurs.");
        AssertPrivateTake(game, viewed); Replay(game, registry);
        var second = Prompt(game)!.Choices[0].Cards.Single(); Answer(game, c => c.Cards.SequenceEqual([second]));
        Reach(game, p => ActivateChoice(p, Enyuan, "reward-card-giver") && p.PlayerSeat == 0);
        var paid = HandTake(game);
        Require(paid.PrivateHandTake is { Paid: true } && game.State.Players[0].HandCount == ownerHand + 2 &&
            game.CreateSnapshot(0).PrivateRevealedCards is null,
            "Both selected entities move together before the exact two-card source Enyuan child; temporary private revelation ends.");
        AssertMovementReturn(game, paid, [first, second], CardLocation.Hand(1), CardLocation.Hand(0),
            "skill-program.boundary:xuanhuo.private-hand-take");
        var sourceHand = game.State.Players[1].HandCount; Replay(game, registry); Activate(game, Enyuan, "reward-card-giver"); ReachPlay(game);
        Require(game.State.Players[1].HandCount == sourceHand + 1 && Started(game, Enyuan, "reward-card-giver", 0) == 1 &&
            game.CardMovements.Count(m => new[] { first, second }.Contains(m.CardId) && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0)) == 2,
            "The one atomic acquisition rewards its actual source once, with two acquisitions and no repeated selection payment.");
        Replay(game, registry);
    }

    public static void MixedHandEquipmentGiftIsAtomicAndWaitsForRecoveryChildren()
    {
        var (game, registry) = Create(equipment: true, giftObservers: true); ReachXuanhuo(game); Skip(game); ReachPlay(game);
        var equip = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        Accept(game, new PlayCardCommand(0, equip.CardId!.Value, equip.TargetSeats, game.Revision, Prompt(game)!.PromptId)); ReachPlay(game);
        var armor = game.CreateSnapshot(0).Players[0].Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId)); ReachXuanhuo(game);
        var hp = game.State.Players[0].Hp; var actorHand = game.State.Players[1].HandCount;
        Require(hp < game.State.Players[0].MaxHp, "The real armor cost belongs to an injured owner.");
        Activate(game, Xuanhuo, "give-two-and-request"); Reach(game, p => Action(p, "select-target")); Answer(game, c => c.Targets.SequenceEqual([1]));
        Reach(game, p => Action(p, "select-owned-cards"));
        var handCard = game.CreateSnapshot(0).Players[0].Hand.First().Id;
        Replay(game, registry); Answer(game, c => c.Cards.SequenceEqual([armor]));
        Require(game.CreateSnapshot(0).Players[0].Equipment.Any(c => c.Id == armor), "The first HE selection does not pay the armor early.");
        Answer(game, c => c.Cards.SequenceEqual([handCard])); Reach(game, p => p.SkillPrompt?.SkillId == Recovery);
        var paid = GiftOwner(game);
        Require(paid.BoundParticipantGift is { RecipientSeat: 1 } && paid.AssistedSlashRequest is null &&
            game.State.Players[0].Hp == hp + 1 && game.State.Players[1].HandCount == actorHand + 2 &&
            !game.CreateSnapshot(0).Players[0].Equipment.Any(c => c.Id == armor) &&
            new[] { armor, handCard }.All(id => game.CreateSnapshot(1).Players[1].Hand.Any(c => c.Id == id)),
            "One atomic mixed HE gift commits both entities before Silver Lion's real HP observer, while no Slash request starts.");
        Replay(game, registry); Continue(game);
        Reach(game, p => ActivateChoice(p, Enyuan, "reward-card-giver") && p.PlayerSeat == 1);
        paid = GiftOwner(game);
        var window = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.ResumeProgramFrameId == paid.Id);
        Require(window.Batch.ParentFrameId == paid.Id && window.Batch.AwaitingProgramFrameId is null &&
            window.Batch.Movements.Count == 2 && window.Batch.Movements.Any(m => m.CardId == armor && m.From == CardLocation.Equipment(0)) &&
            window.Batch.Movements.Any(m => m.CardId == handCard && m.From == CardLocation.Hand(0)) &&
            window.Batch.Movements.All(m => m.To == CardLocation.Hand(1)),
            "The recipient's two-card Enyuan candidate is counted from one source in the exact atomic mixed-zone batch.");
        Replay(game, registry); Activate(game, Enyuan, "reward-card-giver"); Reach(game, p => p.SkillPrompt?.SkillId == Reward);
        Require(GiftOwner(game).BoundParticipantGift is not null && GiftOwner(game).AssistedSlashRequest is null,
            "The recipient's actual reward draw child still belongs beneath the paid gift, before the Slash target tail.");
        Replay(game, registry); Continue(game); Reach(game, p => Action(p, "assisted-physical-slash") && p.PlayerSeat == 0);
        Require(Started(game, Enyuan, "reward-card-giver", 1) == 1 && GiftMoves(game, armor).Count() == 1 && GiftMoves(game, handCard).Count() == 1 &&
            game.Events.Select(e => e.Payload).OfType<SilverLionRemovedRecoveryEvent>().Count(e => e.PlayerSeat == 0 && e.RecoveredAmount == 1) == 1,
            "Both real children return once, retaining one source reward and one Silver Lion recovery without repaying the gift.");
        Answer(game, c => c.Targets.SequenceEqual([2])); Reach(game, p => Action(p, "assisted-physical-slash") && p.PlayerSeat == 1);
        Accept(game, new AdvanceOneStepCommand(game.Revision)); Reach(game, p => Action(p, "private-hand-take"));
        Answer(game, c => c.Cards.Count == 1); Answer(game, c => c.Cards.Count == 1); ReachPlay(game); Replay(game, registry);
    }

    public static void EnyuanPerPointEffectiveRedAndHpLossKeepDamageCursor()
    {
        foreach (var effectiveRed in new[] { true, false })
        {
            var (game, registry) = Create(damageObservers: true, effectiveRed: effectiveRed); ReachXuanhuo(game); Skip(game); ReachPlay(game);
            var hp = game.State.Players[1].Hp; var sourceHand = game.State.Players[1].HandCount;
            Use(game, "incoming", [1]);
            for (var point = 0; point < 2; point++)
            {
                Reach(game, p => ActivateChoice(p, Enyuan, "repay-each-damage")); Activate(game, Enyuan, "repay-each-damage");
                Reach(game, p => Action(p, "select-target")); Answer(game, c => c.Targets.SequenceEqual([1]));
                Reach(game, p => Action(p, "hand-repayment"));
                var prompt = Prompt(game)!; var gifts = prompt.Choices.Where(c => c.Parameters.GetValueOrDefault("repayment") == "give").ToArray();
                Require(prompt.PlayerSeat == 1 && prompt.IsPrivate && prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("repayment") == "lose-hp") &&
                    (effectiveRed ? gifts.Length == game.State.Players[1].HandCount && gifts.All(c =>
                        game.CreateSnapshot(1).Players[1].Hand.Single(card => card.Id == c.Cards.Single()).Suit == Suit.Spade) : gifts.Length == 0),
                    "Repayment belongs to the actual giver: its current Hongyan makes printed spades red, while unfiltered spades are never eligible.");
                for (var viewer = 0; viewer < 4; viewer++) if (viewer != 1)
                    Require(game.CreateSnapshot(viewer).PendingDecision is null && game.CreateSnapshot(viewer).PrivateRevealedCards is null,
                        "Neither the victim nor another spectator receives the giver's private repayment choices.");
                Replay(game, registry); Reject(game);
                if (effectiveRed && point == 0) Answer(game, c => c.Parameters.GetValueOrDefault("repayment") == "give");
                else Accept(game, new AdvanceOneStepCommand(game.Revision)); // native AI pays its effective red Slash, or the sole HP-loss choice
                Reach(game, p => p.SkillPrompt?.SkillId == (effectiveRed ? RepaymentGain : RepaymentHp));
                var paid = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Enyuan && f.HandRepayment is { Paid: true });
                Require(paid.HandRepayment!.LostHp == !effectiveRed && paid.WindowContext is { Window: SkillProgramTriggerWindow.AfterDamageApplied } context &&
                    game.ResolutionStack.OfType<DamageTriggerWindowFrame>().Any(f => f.Id == context.ParentFrameId),
                    "Each real per-point payment retains its exact original ordered damage window while its gain or HP observer is suspended.");
                if (effectiveRed)
                    AssertMovementReturn(game, paid, [paid.HandRepayment.PaidCardId!.Value], CardLocation.Hand(1), CardLocation.Hand(0),
                        "skill-program.boundary:enyuan.hand-repayment");
                else
                    Require(game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(f => f.ResumeFrameId == paid.Id &&
                        f.Continuation == PostEventContinuation.Program && f.Change.ParentFrameId == paid.Id && f.Change.TargetSeat == 1),
                        "The real source HP loss has an exact typed return to its paid Enyuan instruction.");
                Require(Started(game, Enyuan, "repay-each-damage", 0) == point + 1,
                    "The next per-point Enyuan candidate cannot advance past this still-owned child.");
                Replay(game, registry); Continue(game);
            }
            ReachPlay(game);
            var receipts = game.Events.Select(e => e.Payload).OfType<ProgramHandRepaymentResolvedEvent>().ToArray();
            Require(receipts.Length == 2 && receipts.All(e => e.SourceSeat == 1 && e.RecipientSeat == 0 && e.GaveCard == effectiveRed && e.LostHp == !effectiveRed) &&
                game.State.Players[1].Hp == hp - (effectiveRed ? 0 : 2) &&
                game.State.Players[1].HandCount == sourceHand - (effectiveRed ? 2 : 0) &&
                Started(game, Enyuan, "repay-each-damage", 0) == 2 &&
                game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Single(e => e.SourceSeat == 1 && e.TargetSeat == 0).Amount == 2,
                "One actual two-point damage resolves two separate choices and exactly two card or HP payments after their real children return.");
            Replay(game, registry);
        }
        var (dyingGame, dyingRegistry) = Create(damageObservers: true, sourceInitialHp: 1);
        ReachXuanhuo(dyingGame); Skip(dyingGame); ReachPlay(dyingGame); Use(dyingGame, "incoming", [1]);
        Reach(dyingGame, p => ActivateChoice(p, Enyuan, "repay-each-damage")); Activate(dyingGame, Enyuan, "repay-each-damage");
        Reach(dyingGame, p => Action(p, "select-target")); Answer(dyingGame, c => c.Targets.SequenceEqual([1]));
        Reach(dyingGame, p => Action(p, "hand-repayment"));
        Require(Prompt(dyingGame)!.Choices is [var only] && only.Parameters.GetValueOrDefault("repayment") == "lose-hp" &&
            dyingGame.State.Players[1].Hp == 1, "The live one-HP source has no eligible red card or rescue card.");
        var repaymentFrame = dyingGame.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Enyuan && f.HandRepayment is not null);
        Accept(dyingGame, new AdvanceOneStepCommand(dyingGame.Revision));
        for (var step = 0; step < 12 && !dyingGame.ResolutionStack.OfType<DyingFrame>().Any(f => f.ParentFrameId == repaymentFrame.Id && f.VictimSeat == 1); step++)
            Advance(dyingGame);
        var sourceDying = dyingGame.ResolutionStack.OfType<DyingFrame>().Single(f => f.ParentFrameId == repaymentFrame.Id && f.VictimSeat == 1);
        var paidDying = dyingGame.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == repaymentFrame.Id);
        Require(sourceDying.Continuation == DyingContinuationKind.ProgramSkill && dyingGame.State.Players[1].Hp == 0 &&
            paidDying.HandRepayment is { Paid: true, LostHp: true, PaidCardId: null } &&
            paidDying.WindowContext is { Window: SkillProgramTriggerWindow.AfterDamageApplied } damageContext &&
            dyingGame.ResolutionStack.OfType<DamageTriggerWindowFrame>().Any(f => f.Id == damageContext.ParentFrameId),
            "The actual one-HP payment owns a precise Dying child beneath its unchanged original damage cursor.");
        Reach(dyingGame, p => p.SkillPrompt?.SkillId == DyingEntry);
        var entryWindow = dyingGame.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.ResumeDyingFrameId == sourceDying.Id);
        var entryProgram = dyingGame.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == DyingEntry);
        Require(entryWindow.Window == SkillProgramTriggerWindow.DyingEntering && entryWindow.Continuation == ProgramLifecycleContinuation.ResumeDyingEntry &&
            entryWindow.OwnerSeat == 1 && entryProgram.OwnerSeat == 1 && entryProgram.TriggerId == "entering" &&
            entryProgram.WindowContext is { Window: SkillProgramTriggerWindow.DyingEntering, ParentFrameId: var entryParent } && entryParent == entryWindow.Id &&
            entryWindow.Candidates[entryWindow.CandidateIndex] is var entryCandidate && entryCandidate.SkillId == entryProgram.SkillId &&
            entryCandidate.OwnerSeat == entryProgram.OwnerSeat && entryCandidate.SkillInstanceId == entryProgram.SkillInstanceId &&
            Started(dyingGame, Enyuan, "repay-each-damage", 0) == 1 && dyingGame.State.Players[1].Hp == 0 && dyingGame.State.Players[1].IsAlive,
            "The actual DyingEntering pause retains its exact lifecycle return, current candidate and paid damage-point ancestor before source death.");
        Replay(dyingGame, dyingRegistry); Continue(dyingGame); ReachPlay(dyingGame);
        Require(!dyingGame.State.Players[1].IsAlive &&
            dyingGame.Events.Select(e => e.Payload).OfType<PlayerDiedEvent>().Count(e => e.VictimSeat == 1) == 1 &&
            dyingGame.Events.Select(e => e.Payload).OfType<DyingResolvedEvent>().Single(e => e.ResolutionId == sourceDying.Id).Survived == false &&
            dyingGame.Events.Select(e => e.Payload).OfType<ProgramHandRepaymentResolvedEvent>().Count() == 1 &&
            Started(dyingGame, Enyuan, "repay-each-damage", 0) == 1 &&
            dyingGame.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Single(e => e.SourceSeat == 1 && e.TargetSeat == 0).Amount == 2 &&
            !dyingGame.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.HandRepayment is not null),
            "No one can rescue with this actual deck: source death returns once, and the next damage-point candidate cannot charge a dead source.");
        Replay(dyingGame, dyingRegistry);

        // Printed spade Peaches and two-spade Slash conversions are deliberate fixture materials; neither qualifies for a red hand payment.
        foreach (var twoMaterials in new[] { false, true })
        {
            var (rescueGame, rescueRegistry) = Create(damageObservers: true, sourceInitialHp: 1,
                rescueWithPeach: !twoMaterials, rescueWithTwoCards: twoMaterials);
            ReachXuanhuo(rescueGame); Skip(rescueGame); ReachPlay(rescueGame); Use(rescueGame, "incoming", [1]);
            var peachPayments = new List<int>();
            for (var point = 0; point < 2; point++)
            {
                Reach(rescueGame, p => ActivateChoice(p, Enyuan, "repay-each-damage")); Activate(rescueGame, Enyuan, "repay-each-damage");
                Reach(rescueGame, p => Action(p, "select-target")); Answer(rescueGame, c => c.Targets.SequenceEqual([1]));
                Reach(rescueGame, p => Action(p, "hand-repayment"));
                Require(Prompt(rescueGame)!.Choices is [var noRed] && noRed.Parameters.GetValueOrDefault("repayment") == "lose-hp",
                    "Printed spade materials remain ineligible red repayment cards even when they can produce a rescue Peach.");
                Accept(rescueGame, new AdvanceOneStepCommand(rescueGame.Revision)); Reach(rescueGame, p => p.SkillPrompt?.SkillId == DyingEntry);
                Replay(rescueGame, rescueRegistry); Continue(rescueGame); Reach(rescueGame, p => p.SkillPrompt?.SkillId == RescueCommitted);
                var committedUse = rescueGame.ResolutionStack.OfType<CardUseFrame>().Single(f => f.DyingResponse is not null);
                var committedWindow = rescueGame.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(f => f.ParentFrameId == committedUse.Id);
                var committedProgram = rescueGame.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == RescueCommitted);
                var materials = committedUse.Action!.PhysicalCards.Select(c => c.CardId).ToArray();
                Require(committedUse.CardKind == CardKind.Peach && materials.Length == (twoMaterials ? 2 : 1) &&
                    materials.SequenceEqual(committedUse.PhysicalCardIds ?? [committedUse.CardId]) &&
                    committedUse.Action.PhysicalCards.All(c => c.From == CardLocation.Hand(1) && c.CardKind == (twoMaterials ? CardKind.Slash : CardKind.Peach)) &&
                    (twoMaterials ? committedUse.Action.ConversionChain is [var conversion] && conversion.SkillId == TwoCardRescue && conversion.BindingId == "two-spades"
                        : committedUse.Action.ConversionChain.Count == 0) &&
                    committedWindow.Continuation == ProgramCardContinuation.CommittedSimpleCard && committedWindow.Action.ActionId == committedUse.Action.ActionId &&
                    committedProgram.WindowContext is { Window: SkillProgramTriggerWindow.CardUseCommitted, ParentFrameId: var committedParent } && committedParent == committedWindow.Id &&
                    rescueGame.State.Players[1].Hp == 0 && Started(rescueGame, Enyuan, "repay-each-damage", 0) == point + 1,
                    "A real native or registered two-material rescue commits its exact paid costs, then pauses in its own CardUseCommitted program before HP recovery.");
                Replay(rescueGame, rescueRegistry); Continue(rescueGame); Reach(rescueGame, p => p.SkillPrompt?.SkillId == Recovery);
                var rescueUse = rescueGame.ResolutionStack.OfType<CardUseFrame>().Single(f => f.DyingResponse is not null);
                var rescueDying = rescueGame.ResolutionStack.OfType<DyingFrame>().Single(f => f.Id == rescueUse.DyingResponse!.ResolutionId);
                var paidRescue = rescueGame.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Enyuan && f.HandRepayment is { Paid: true, LostHp: true });
                peachPayments.AddRange(materials);
                Require(rescueUse.CardKind == CardKind.Peach && rescueUse.SourceSeat == 1 && rescueUse.TargetSeats.SequenceEqual([1]) &&
                    rescueUse.DyingResponse is { UsedPeach: true, UsedAlcohol: false, ResponderSeat: 1 } response && response.PeachCardId == rescueUse.CardId &&
                    rescueUse.Action is { EffectiveKind: CardKind.Peach } actual && actual.PhysicalCards.Select(c => c.CardId).SequenceEqual(materials) &&
                    rescueDying.ParentFrameId == paidRescue.Id && rescueGame.State.Players[1].Hp == 1 && rescueGame.State.Players[1].IsAlive &&
                    rescueGame.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(f => f.ResumeFrameId == rescueUse.Id &&
                        f.Continuation == PostEventContinuation.CardUse && f.Change.ParentFrameId == rescueUse.Id && f.Change.TargetSeat == 1) &&
                    Started(rescueGame, Enyuan, "repay-each-damage", 0) == point + 1,
                    "Native AI pays the actual frozen Peach-use materials; its HP observer retains the rescue use, original Dying, paid repayment and unchanged per-point damage cursor.");
                Replay(rescueGame, rescueRegistry); Continue(rescueGame);
            }
            ReachPlay(rescueGame);
            Require(peachPayments.Count == (twoMaterials ? 4 : 2) && peachPayments.Distinct().Count() == peachPayments.Count && peachPayments.All(id => rescueGame.CardMovements.Count(m => m.CardId == id &&
                    m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1) &&
                rescueGame.State.Players[1].IsAlive && rescueGame.State.Players[1].Hp == 1 &&
                rescueGame.Events.Select(e => e.Payload).OfType<ProgramHandRepaymentResolvedEvent>().Count(e => e.LostHp) == 2 &&
                rescueGame.Events.Select(e => e.Payload).OfType<DyingResolvedEvent>().Count(e => e.VictimSeat == 1 && e.Survived) == 2,
                "Each actual per-point HP cost and Peach rescue completes once after its observer, with two distinct payments and no duplicate use on typed return.");
            Replay(rescueGame, rescueRegistry);
        }
    }

    private static IEnumerable<CardMovementRecord> GiftMoves(GameEngine game, int id) => game.CardMovements.Where(m =>
        m.CardId == id && m.From.OwnerSeat == 0 && m.To == CardLocation.Hand(1) && m.Reason.Value == "skill-program.boundary:xuanhuo.MoveBoundCards");
    private static int Started(GameEngine game, string skill, string binding, int owner) => game.Events.Select(e => e.Payload)
        .OfType<ProgramBindingStartedEvent>().Count(e => e.SkillId == skill && e.BindingId == binding && e.OwnerSeat == owner);
    private static ProgramSkillFrame HandTake(GameEngine game) => game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Xuanhuo && f.PrivateHandTake is not null);
    private static ProgramSkillFrame GiftOwner(GameEngine game) => game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Xuanhuo && f.BoundParticipantGift is not null);
    private static void AssertMovementReturn(GameEngine game, ProgramSkillFrame paid, IReadOnlyList<int> cards, CardLocation from, CardLocation to, string reason)
    {
        var window = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.ResumeProgramFrameId == paid.Id);
        Require(window.Batch.ParentFrameId == paid.Id && window.Batch.AwaitingProgramFrameId is null && paid.PendingMovementContinuation is null &&
            window.Batch.OriginSkillId == paid.SkillId && window.Batch.OriginSkillInstanceId == paid.SkillInstanceId &&
            window.Batch.Movements.Count == cards.Count && window.Batch.Movements.All(m => cards.Contains(m.CardId) && m.From == from && m.To == to && m.Reason.Value == reason),
            "The actual atomic movement carries an exact typed return and source instance for the already-paid instruction.");
    }
    private static void AssertPrivateTake(GameEngine game, IReadOnlyList<int> viewed)
    {
        Require(Prompt(game) is { PlayerSeat: 0, IsPrivate: true }, "The victim hand is privately visible only to the actual skill viewer.");
        for (var viewer = 1; viewer < 4; viewer++)
            Require(game.CreateSnapshot(viewer).PendingDecision is null && game.CreateSnapshot(viewer).PrivateRevealedCards is null &&
                (viewer == 1 || game.CreateSnapshot(viewer).Players[1].Hand.All(c => !viewed.Contains(c.Id))),
                "The private revelation and selected draft never enter another player's snapshot.");
    }
    private static void AssertFrozen(IReadOnlyList<int> ids)
    {
        var rejected = false; try { ((IList<int>)ids)[0] = -1; } catch (NotSupportedException) { rejected = true; }
        Require(rejected, "The nested viewed-card collection is frozen before observers receive it.");
    }
    private static int[] BeginGift(GameEngine game, ContentRegistry registry, int recipient)
    {
        Activate(game, Xuanhuo, "give-two-and-request"); Reach(game, p => Action(p, "select-target"));
        Answer(game, c => c.Targets.SequenceEqual([recipient])); Reach(game, p => Action(p, "select-owned-cards"));
        var cards = game.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c => c.Id).ToArray();
        Replay(game, registry); Reject(game); Answer(game, c => c.Cards.SequenceEqual([cards[0]])); Replay(game, registry);
        Answer(game, c => c.Cards.SequenceEqual([cards[1]])); return cards;
    }
    private static (GameEngine, ContentRegistry) Create(bool equipment = false, bool giftObservers = false, bool damageObservers = false, bool effectiveRed = false, int? sourceInitialHp = null, bool rescueWithPeach = false, bool rescueWithTwoCards = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(equipment, giftObservers, damageObservers, effectiveRed, sourceInitialHp, rescueWithPeach, rescueWithTwoCards));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, "fixture:fz-owner", game.Revision, Prompt(game)!.PromptId)); return (game, registry);
    }
    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static bool ActivateChoice(PendingDecision p, string skill, string binding) => p.Choices.Any(c =>
        c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static void ReachXuanhuo(GameEngine game) => Reach(game, p => p.PlayerSeat == 0 && ActivateChoice(p, Xuanhuo, "give-two-and-request"));
    private static void ReachPlay(GameEngine game) => Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Activate(GameEngine game, string skill, string binding) => Answer(game, c => c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding && c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Skip(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine game, string id, IReadOnlyList<int> targets) => Accept(game, new UseProgramSkillCommand(0, Driver, id, [], targets, game.Revision, Prompt(game)!.PromptId));
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 180; step++) { var p = Prompt(game); if (p is not null && predicate(p)) return; Advance(game); }
        throw new InvalidOperationException("Fixed Fa Zheng fixture did not reach boundary: " + JsonSerializer.Serialize(Prompt(game)));
    }
    private static void Advance(GameEngine game)
    {
        var p = Prompt(game);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards })
            Accept(game, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, game.Revision));
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Skip(game);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(game, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else if (p?.SkillPrompt?.SkillId is Recovery or Reward or RepaymentGain or RepaymentHp or DyingEntry or RescueCommitted) Continue(game);
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var p = Prompt(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, game.Revision)); }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static void Reject(GameEngine game)
    { var before = State(game); var p = Prompt(game)!; Require(!game.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), game.Revision)).Accepted && State(game) == before, "An unpublished choice cannot change a frozen hand draft or repeat a paid cost."); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static void Replay(GameEngine game, ContentRegistry registry) => Require(State(game) == State(GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry)),
        "All four private views, exact owning payment stages, movement facts and real command prefixes cold-restore exactly.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool equipment, bool giftObservers, bool damageObservers, bool effectiveRed, int? sourceInitialHp, bool rescueWithPeach, bool rescueWithTwoCards) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-fa-zheng", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                 {"id":"{{Driver}}","revision":1,"activations":[{"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]}]},
                 {"id":"fixture:fz-quiet","revision":1,"triggers":[{"id":"quiet-turn","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]},
                 {"id":"{{Recovery}}","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Reward}}","revision":1,"triggers":[{"id":"reward","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:enyuan.Draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"reward-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{RepaymentGain}}","revision":1,"triggers":[{"id":"paid","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:enyuan.hand-repayment"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{RepaymentHp}}","revision":1,"triggers":[{"id":"paid","window":"afterHpLost","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"loss-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{DyingEntry}}","revision":1,"triggers":[{"id":"entering","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"entry-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{RescueCommitted}}","revision":1,"triggers":[{"id":"paid-rescue","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["peach"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"rescue-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{TwoCardRescue}}","revision":1,"viewAs":[{"id":"two-spades","inputKinds":["slash"],"inputSuits":["spade"],"outputKind":"peach","forPlay":false,"forResponse":true,"inputCount":2,"sameSuit":true,"sourceZones":["hand","equipment"],"extendedUse":true}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实伤害驱动", description = "来自实际指定来源的两点伤害" },
                  ["fixture:fz-quiet"] = new { name = "安静回合", description = "固定夹具不发动普通杀" },
                  [Recovery] = Observer("银狮实际回复"), [Reward] = Observer("来源实际摸牌"),
                  [RepaymentGain] = Observer("红牌实际取得"), [RepaymentHp] = Observer("来源实际失血"), [DyingEntry] = Observer("真实濒死进入"),
                  [RescueCommitted] = Observer("真实救援提交"), [TwoCardRescue] = new { name = "两张实际材料救援", description = "固定无红实体的真实转化救援" } } }));
            foreach (var id in new[] { Driver, "fixture:fz-quiet", Recovery, Reward, RepaymentGain, RepaymentHp, DyingEntry, RescueCommitted, TwoCardRescue })
                builder.AddSkill(new(id, id, "真实边界夹具") { Program = catalog.Programs[id] });
            builder.AddSkill(new("fixture:fz-selection", "固定选将", "无运行技能") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new("fixture:fz-owner", "当前界法正机制", "supporter", Xuanhuo, "shu", 3,
                giftObservers ? [Enyuan, Driver, Recovery, Reward] : damageObservers ? [Enyuan, Driver, RepaymentGain] : [Enyuan, Driver]) { InitialHp = equipment ? 2 : 3 });
            for (var i = 1; i < 4; i++)
            {
                var skills = new List<string> { "fixture:fz-quiet" };
                if (giftObservers) skills.Add(Enyuan);
                if (damageObservers) skills.Add(RepaymentHp);
                if (sourceInitialHp is not null) skills.Add(DyingEntry);
                if (rescueWithPeach || rescueWithTwoCards) { skills.Add(Recovery); skills.Add(RescueCommitted); }
                if (rescueWithTwoCards) skills.Add(TwoCardRescue);
                if (effectiveRed) skills.Add("classic:hongyan");
                builder.AddGeneral(new($"fixture:fz-target-{i}", "固定实际来源", "supporter", "fixture:fz-selection", "wu", 8, skills) { InitialHp = sourceInitialHp });
            }
            builder.AddDeck(new("fixture:fz-deck", "固定真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 100)
                .Select(i => new ContentDeckPhysicalCard(equipment ? "classic:silver-lion" : rescueWithPeach ? "standard:peach" : "standard:slash", equipment ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "真实界法正", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:fz-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:fz-owner", "fixture:fz-target-1", "fixture:fz-target-2", "fixture:fz-target-3"]));
        }
        private static object Observer(string name) => new { name, description = "付款后的真实子窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
