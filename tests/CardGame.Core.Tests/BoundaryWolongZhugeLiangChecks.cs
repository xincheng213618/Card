using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class BoundaryWolongZhugeLiangChecks
{
    private const string Mode = "identity:classic-current-wolong-fixture";
    private const string Driver = "fixture:wolong-driver";
    private const string Hp = "fixture:wolong-hp";
    private const string Gain = "fixture:wolong-gain";
    private const string Loss = "fixture:wolong-loss";
    private const string Completed = "fixture:wolong-completed";
    private const string EquipmentChild = "fixture:wolong-equipment-child";
    private const string FaceChild = "fixture:wolong-face-child";
    private const string AlcoholCompleted = "fixture:wolong-alcohol-completed";

    public static void RandomRevealColorHePaymentAndChildrenCold()
    {
        var (game, registry) = Create();
        Require(game.State.Players[0].Hp == 2 && game.State.Players[0].MaxHp == 4,
            "The real formal Lord begins wounded at two HP.");
        var armor = Hand(game, 0).First().Id;
        Play(game, armor, []); ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[0].Equipment.Any(card => card.Id == armor), "A real red Silver Lion is equipped.");
        var material = Hand(game, 0).First().Id;
        var beforeTargetHp = game.State.Players[1].Hp;
        PlayAs(game, material, [1], CardKind.FireAttack); Reach(game, prompt => prompt.Kind == DecisionKind.FireAttackDiscard);
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.ColorFireAttack is { RevealedCardId: not null });
        var receipt = use.ColorFireAttack!;
        Require(P(game) is { PlayerSeat: 0 } && P(game)!.ValidCardIds.Contains(armor) &&
            receipt.RevealedIsRed == true && receipt.PaidCardId is null &&
            Facts<FireAttackCardRevealedEvent>(game).Count(fact => fact.ResolutionId == use.Id) == 1 &&
            !game.AcceptedCommands.OfType<AnswerPromptCommand>().Any(command => command.ActorSeat == 1),
            "The target's real hand is randomly revealed once, without a target choice; the source can pay equipped same-color armor.");
        PrivateAndFrozen(game); Cold(game, registry);
        var before = State(game);
        Require(!game.Submit(new AnswerPromptCommand(0, P(game)!.PromptId, new("unpublished-payment"), game.Revision)).Accepted && State(game) == before,
            "Unpublished payment cannot change the frozen reveal, RNG or accepted prefix.");
        Answer(game, choice => choice.Cards.SequenceEqual([armor])); Reach(game, prompt => prompt.SkillPrompt?.SkillId == Hp);
        var paid = game.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == use.Id);
        var hp = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single(frame => frame.ResumeFrameId == use.Id);
        Require(paid.ColorFireAttack is { PaidCardId: var paidId, PaidFrom: var paidFrom } && paidId == armor &&
            paidFrom == CardLocation.Equipment(0) && hp.Continuation == PostEventContinuation.ColorFireAttackPayment &&
            hp.Change.ParentFrameId == use.Id && game.State.Players[0].Hp == 3 && game.State.Players[1].Hp == beforeTargetHp &&
            CostMoves(game, armor).Length == 1 && !Facts<FireAttackResolvedEvent>(game).Any(fact => fact.ResolutionId == use.Id),
            "Real Silver Lion equipment payment and its exact HP child precede the fire effect, once.");
        Cold(game, registry); Continue(game); Reach(game, prompt => prompt.SkillPrompt?.SkillId == Gain &&
            game.ResolutionStack.OfType<CardUseFrame>().Any(frame => frame.Id == use.Id && frame.ColorFireAttack?.PaidCardId == armor));
        Require(game.State.Players[1].Hp == beforeTargetHp && CostMoves(game, armor).Length == 1,
            "The recovery observer's actual draw/gain child still returns to the same already-paid Fire Attack.");
        Cold(game, registry); Continue(game); Reach(game, prompt => prompt.SkillPrompt?.SkillId == Loss);
        var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(frame => frame.ResumeColorFireAttackFrameId == use.Id);
        Require(movement.Batch.ParentFrameId == use.Id && movement.Batch.AwaitingProgramFrameId is null &&
            movement.ResumeProgramFrameId is null && movement.Batch.Movements.Any(move => move.CardId == armor && move.From == CardLocation.Equipment(0)),
            "The real armor loss window has a direct typed paid CardUse return, not a borrowed Program continuation.");
        Cold(game, registry); Continue(game); ReachPlay(game);
        Require(CostMoves(game, armor).Length == 1 && Facts<ColorFireAttackPaidEvent>(game).Count(fact => fact.CardUseFrameId == use.Id) == 1 &&
            Facts<FireAttackResolvedEvent>(game).Single(fact => fact.ResolutionId == use.Id).CausedDamage &&
            game.State.Players[1].Hp == beforeTargetHp - 1 &&
            Facts<CardUseFinishedEvent>(game).Any(fact => fact.ResolutionId == use.Id && fact.CardKind == CardKind.FireAttack) &&
            !game.CreateCardZoneDiagnostics().Any(card => card.Location == CardLocation.Processing),
            "Only after recovery/gain/loss children does the original real fire use damage and clean Processing.");
        Cold(game, registry);

        // A separate fixed native turn exercises the AI's published HE payment
        // chooser; neither selected target card nor hidden opponent hand is fed to it.
        var (native, nativeRegistry) = Create(nativeFire: true);
        Accept(native, new EndPlayPhaseCommand(0, native.Revision, P(native)!.PromptId));
        ReachUntil(native, () => Facts<ColorFireAttackPaidEvent>(native).Any(fact => fact.SourceSeat != 0));
        var nativePaid = Facts<ColorFireAttackPaidEvent>(native).First(fact => fact.SourceSeat != 0);
        Require(native.AiThoughts.Any(thought => thought.ActorSeat == nativePaid.SourceSeat && thought.Summary.Contains("颜色火攻", StringComparison.Ordinal)),
            "A native AI actually pays a published color Fire Attack candidate using its own filtered view.");
        Cold(native, nativeRegistry);
    }

    public static void UnrespondableCounterspellEquipmentPaymentAndCompletedCold()
    {
        var (game, registry) = Create(black: true, emptyOtherHands: true);
        var armor = Hand(game, 0).First().Id; Play(game, armor, []); ReachPlay(game);
        UseDriver(game, "equip-other", 1); ReachPlay(game);
        var targetArmor = game.CreateSnapshot(0).Players[1].Equipment.Single().Id;
        var trick = Hand(game, 0).First().Id; PlayAs(game, trick, [1], CardKind.Dismantlement);
        Reach(game, prompt => prompt.Kind == DecisionKind.Nullification && prompt.PlayerSeat == 0);
        var original = game.ResolutionStack.OfType<NullificationWindowFrame>().Single();
        var initialUses = Facts<ActualTurnTrickUseRecordedEvent>(game).Length;
        Answer(game, choice => choice.Cards.SequenceEqual([armor])); Reach(game, prompt => prompt.SkillPrompt?.SkillId == Hp);
        var pending = game.ResolutionStack.OfType<NullificationWindowFrame>().Single(frame => frame.Id == original.Id);
        var action = pending.CounterspellPayment!.Action;
        Require(pending.UnrespondableCounterspell is { } issued && issued.WindowFrameId == original.Id &&
            issued.ParentCardUseFrameId == original.ParentFrameId && issued.ActionId == action.ActionId && issued.ChainDepth == 1 &&
            action.PhysicalCards is [var cost] && cost.CardId == armor && cost.From == CardLocation.Equipment(0) &&
            game.State.Players[0].Hp == 3 && game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>()
                .Any(frame => frame.ResumeFrameId == original.Id && frame.Continuation == PostEventContinuation.CounterspellPayment),
            "The real black equipped material pays once and freezes only its exact counterspell node, with a typed armor recovery child.");
        Cold(game, registry); Continue(game); Reach(game, prompt => prompt.SkillPrompt?.SkillId == Gain);
        Cold(game, registry); Continue(game); Reach(game, prompt => prompt.SkillPrompt?.SkillId == Completed);
        Require(game.ResolutionStack.OfType<NullificationWindowFrame>().Single().CounterspellPayment is null &&
            game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(frame =>
                frame.CompletedResponseReturn?.Kind == ProgramCompletedResponseKind.Nullification && frame.Action.ActionId == action.ActionId) &&
            !Facts<NullificationRequestedEvent>(game).Any(fact => fact.ResolutionId == original.ParentFrameId && fact.ChainDepth > 0),
            "The original completed-use observer pauses before the node closes; no counter-response has opened early.");
        PrivateAndFrozen(game); Cold(game, registry); Continue(game); ReachPlay(game);
        Require(Facts<UnrespondableCounterspellIssuedEvent>(game).Count(fact => fact.WindowFrameId == original.Id) == 1 &&
            !Facts<NullificationRequestedEvent>(game).Any(fact => fact.ResolutionId == original.ParentFrameId && fact.ChainDepth > 0) &&
            Facts<ActualTurnTrickUseRecordedEvent>(game).Length == initialUses + 1 &&
            game.CreateSnapshot(0).Players[1].Equipment.Any(card => card.Id == targetArmor) &&
            game.CardMovements.Count(move => move.CardId == armor && move.From == CardLocation.Equipment(0)) == 1,
            "The completed accepted Nullification prevents the original dismantlement, ends only its issued node and records one true trick use.");
        Cold(game, registry);

        var (physical, physicalRegistry) = Create(black: true, physicalCounterspell: true);
        // All real materials are native Nullification cards. The fixture's small
        // view-as creates a real physical trick, and the response stays native.
        var card = Hand(physical, 0).First().Id;
        PlayAs(physical, card, [1], CardKind.Dismantlement);
        Reach(physical, prompt => prompt.Kind == DecisionKind.Nullification && prompt.PlayerSeat == 0);
        Answer(physical, choice => choice.Cards.Count == 1 && !choice.Parameters.ContainsKey("conversion-skill-id"));
        Reach(physical, prompt => prompt.SkillPrompt?.SkillId == Completed);
        var physicalWindow = physical.ResolutionStack.OfType<NullificationWindowFrame>().Single();
        var physicalAction = physical.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(frame => frame.CompletedResponseReturn is not null).Action;
        Require(physicalWindow.UnrespondableCounterspell!.ActionId == physicalAction.ActionId &&
            physicalAction.PhysicalCards.Single().CardKind == CardKind.Nullification && physicalAction.ConversionChain.Count == 0,
            "The same owner policy covers a genuine physical Nullification without inventing a conversion.");
        Cold(physical, physicalRegistry); Continue(physical);
        Require(Facts<NullificationResolvedEvent>(physical).Any(fact => fact.ResolutionId == physicalWindow.ParentFrameId &&
                fact.ChainDepth == physicalWindow.ChainDepth && fact.EffectNullified == physicalWindow.EffectNullified) &&
            !Facts<NullificationRequestedEvent>(physical).Any(fact => fact.ResolutionId == physicalWindow.ParentFrameId &&
                fact.ChainDepth >= physicalWindow.ChainDepth) &&
            P(physical)?.Kind == (physicalWindow.EffectNullified ? DecisionKind.PlayCard : DecisionKind.SelectTargetCard),
            "The physical counterspell closes its exact node and preserves the real chain parity, including a prior native counterspell.");
        Cold(physical, physicalRegistry);

        var (native, nativeRegistry) = Create(black: true, nativeCounterspell: true, emptyOtherHands: true);
        UseDriver(native, "equip-other", 1); ReachPlay(native);
        UseDriver(native, "hurt-other", 1); ReachPlay(native);
        var nativeArmor = native.CreateSnapshot(0).Players[1].Equipment.Single().Id;
        Require(Hand(native, 1).Count == 0 && native.State.Players[1].Hp == 7, "Native responder has only one actual black armor material and is wounded.");
        PlayAs(native, Hand(native, 0).First().Id, [1], CardKind.Dismantlement);
        ReachUntil(native, () => Facts<UnrespondableCounterspellIssuedEvent>(native).Any(fact => fact.ResponderSeat == 1));
        var nativeWindow = native.ResolutionStack.OfType<NullificationWindowFrame>().Single();
        Require(nativeWindow.CounterspellPayment?.Action.PhysicalCards is [var nativeCost] &&
            nativeCost.CardId == nativeArmor && nativeCost.From == CardLocation.Equipment(1) &&
            native.AiThoughts.Any(thought => thought.ActorSeat == 1 && thought.Decision.Contains("无懈", StringComparison.Ordinal)),
            "A real native AI uses its only published HE counterspell material; its private hand has no fallback.");
        Cold(native, nativeRegistry); ReachPlay(native);
        Require(native.State.Players[1].Hp == 8 && !Facts<NullificationRequestedEvent>(native)
                .Any(fact => fact.ResolutionId == nativeWindow.ParentFrameId && fact.ChainDepth > 0) &&
            native.CardMovements.Count(move => move.CardId == nativeArmor && move.From == CardLocation.Equipment(1)) == 1,
            "Native armor recovery and response children finish before the exact unrespondable node returns, with one material payment.");
        Cold(native, nativeRegistry);

        // Actual Qingxian is the HP observer; only its resulting real random
        // equipment use supplies the extra child Processing entity.
        var (nested, nestedRegistry) = Create(black: true, qingxian: true, emptyOtherHands: true);
        var paidArmor = Hand(nested, 0).First().Id; Play(nested, paidArmor, []); ReachPlay(nested);
        UseDriver(nested, "equip-other", 1); ReachPlay(nested);
        var replacedArmor = nested.CreateSnapshot(0).Players[1].Equipment.Single().Id;
        var originalMaterial = Hand(nested, 0).First().Id;
        PlayAs(nested, originalMaterial, [1], CardKind.Dismantlement);
        Reach(nested, prompt => prompt.Kind == DecisionKind.Nullification && prompt.PlayerSeat == 0);
        var paidWindowId = nested.ResolutionStack.OfType<NullificationWindowFrame>().Single().Id;
        Answer(nested, choice => choice.Cards.SequenceEqual([paidArmor]));
        Reach(nested, prompt => prompt.SkillPrompt?.SkillId == "classic:qingxian" &&
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        var recoveryOwner = nested.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single(frame => frame.ResumeFrameId == paidWindowId);
        Require(recoveryOwner.Continuation == PostEventContinuation.CounterspellPayment &&
            recoveryOwner.Change.ParentFrameId == paidWindowId && nested.State.Players[0].Hp == 3,
            "The actual paid black Silver Lion recovery opens real Qingxian under the original counterspell node.");
        Cold(nested, nestedRegistry);
        Answer(nested, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(nested, prompt => prompt.SkillPrompt?.SkillId == "classic:qingxian" && prompt.Choices.Any(choice => choice.Targets.SequenceEqual([1])));
        Answer(nested, choice => choice.Targets.SequenceEqual([1]));
        Reach(nested, prompt => prompt.SkillPrompt?.SkillId == "classic:qingxian" &&
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == "lose"));
        Require(P(nested)!.PlayerSeat == 1, "The real selected Qingxian participant receives its own published choice.");
        Answer(nested, choice => choice.Parameters.GetValueOrDefault("option-id") == "lose");
        Reach(nested, prompt => prompt.SkillPrompt?.SkillId == EquipmentChild);
        var node = nested.ResolutionStack.OfType<NullificationWindowFrame>().Single(frame => frame.Id == paidWindowId);
        var qing = nested.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == "classic:qingxian");
        var equipmentUse = nested.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.CardKind == CardKind.SilverLion);
        var equipmentWindow = nested.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(frame => frame.ParentFrameId == equipmentUse.Id);
        Require(node.CounterspellPayment?.Action.PhysicalCards is [var originalCost] && originalCost.CardId == paidArmor &&
            node.UnrespondableCounterspell?.ActionId == node.CounterspellPayment.Action.ActionId &&
            qing.TriggerId == "afterHpRecovered" && qing.WindowContext?.ParentFrameId == recoveryOwner.Id &&
            qing.SelectedTargetSeats.SequenceEqual([1]) && qing.PendingMovementContinuation?.SubjectSeat == 1 &&
            qing.CardSetBindings.Single(binding => binding.Name == "used").CardIds.SequenceEqual([equipmentUse.CardId]) &&
            equipmentUse.Action is { Type: CardActionType.Use, ActorSeat: 1, ProviderSeat: 1, PhysicalCards: [var actualEquipment] } &&
            actualEquipment.CardId == equipmentUse.CardId && actualEquipment.From == CardLocation.DrawPile &&
            equipmentUse.Action.ConversionChain.Any(source => source.SkillId == qing.SkillId && source.SkillInstanceId == qing.SkillInstanceId && source.BindingId == qing.TriggerId) &&
            equipmentWindow.Continuation == ProgramCardContinuation.FinalizedSimpleCard &&
            equipmentWindow.SimpleContinuation is { Effect: SimpleCardUseEffect.EquipmentPlacement } entry && entry.CardId == equipmentUse.CardId &&
            equipmentWindow.Action.ActionId == equipmentUse.Action.ActionId && P(nested)!.PlayerSeat == 1 &&
            nested.CreateCardZoneDiagnostics().Where(card => card.Location == CardLocation.Processing).Select(card => card.CardId).Order()
                .SequenceEqual(new[] { originalMaterial, equipmentUse.CardId }.Order()) &&
            nested.State.Players[1].Hp == 7 && nested.CreateSnapshot(0).Players[1].Equipment.Any(card => card.Id == replacedArmor) &&
            !nested.CardMovements.Any(move => move.CardId == replacedArmor && move.From == CardLocation.Equipment(1) && move.To == CardLocation.DiscardPile) &&
            nested.CardMovements.Count(move => move.CardId == paidArmor && move.From == CardLocation.Equipment(0)) == 1 &&
            !Facts<NullificationRequestedEvent>(nested).Any(fact => fact.ResolutionId == node.ParentFrameId && fact.ChainDepth > 0),
            "The supported equipment-category target window pauses before slot replacement while the exact random equipment remains Processing and its original paid counterspell waits; no cost or counter-response repeats.");
        PrivateAndFrozen(nested); Cold(nested, nestedRegistry); Continue(nested);
        Reach(nested, prompt => prompt.SkillPrompt?.SkillId == Completed); Cold(nested, nestedRegistry);
        Continue(nested); ReachPlay(nested);
        Require(nested.State.Players[1].Hp == 8 && nested.CreateSnapshot(0).Players[1].Equipment.Any(card => card.Id == equipmentUse.CardId) &&
            nested.CardMovements.Count(move => move.CardId == equipmentUse.CardId && move.From == CardLocation.DrawPile && move.To == CardLocation.Processing && move.Reason == CardMoveReasons.EquipmentUse) == 1 &&
            nested.CardMovements.Count(move => move.CardId == replacedArmor && move.From == CardLocation.Equipment(1)) == 1 &&
            nested.CardMovements.Count(move => move.CardId == paidArmor && move.From == CardLocation.Equipment(0)) == 1 &&
            Facts<UnrespondableCounterspellIssuedEvent>(nested).Count(fact => fact.WindowFrameId == paidWindowId) == 1 &&
            Facts<NullificationResolvedEvent>(nested).Single(fact => fact.ResolutionId == node.ParentFrameId).EffectNullified &&
            !nested.CreateCardZoneDiagnostics().Any(card => card.Location == CardLocation.Processing),
            "Qingxian's actual equipment, armor replacement recovery and original completed response return once to the exact unrespondable node and clear Processing.");
        Cold(nested, nestedRegistry);

        // A separate real paid node makes its chosen participant dying at one
        // HP. Native Niepan, including its HP/gain children, must return to
        // Qingxian before the real random equipment and original node finish.
        var (rescued, rescuedRegistry) = Create(black: true, qingxian: true, qingxianDying: true, emptyOtherHands: true);
        var rescueCost = Hand(rescued, 0).First().Id; Play(rescued, rescueCost, []); ReachPlay(rescued);
        UseDriver(rescued, "equip-other", 2); ReachPlay(rescued);
        var protectedArmor = rescued.CreateSnapshot(0).Players[2].Equipment.Single().Id;
        Require(rescued.State.Players[1].Hp == 8 && rescued.CreateSnapshot(0).Players[1].Equipment.Count == 0,
            "The real chosen rescue participant begins at eight HP with no armor or hand rescue material.");
        UseDriver(rescued, "prepare-one-hp", 1); ReachPlay(rescued);
        Require(rescued.State.Players[1].Hp == 1 && Hand(rescued, 1).Count == 0 &&
            !Facts<ProgramBindingStartedEvent>(rescued).Any(fact => fact.OwnerSeat == 1 && fact.SkillId == "classic:niepan"),
            "One real seven-HP loss establishes one HP without consuming actual Niepan beforehand.");
        var rescuedMaterial = Hand(rescued, 0).First().Id;
        PlayAs(rescued, rescuedMaterial, [2], CardKind.Dismantlement);
        Reach(rescued, prompt => prompt.Kind == DecisionKind.Nullification && prompt.PlayerSeat == 0);
        var rescuedNodeId = rescued.ResolutionStack.OfType<NullificationWindowFrame>().Single().Id;
        Answer(rescued, choice => choice.Cards.SequenceEqual([rescueCost]));
        Reach(rescued, prompt => prompt.SkillPrompt?.SkillId == "classic:qingxian" &&
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Answer(rescued, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(rescued, prompt => prompt.SkillPrompt?.SkillId == "classic:qingxian" && prompt.Choices.Any(choice => choice.Targets.SequenceEqual([1])));
        Answer(rescued, choice => choice.Targets.SequenceEqual([1]));
        Reach(rescued, prompt => prompt.SkillPrompt?.SkillId == "classic:qingxian" &&
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == "lose"));
        Answer(rescued, choice => choice.Parameters.GetValueOrDefault("option-id") == "lose");
        var originalDying = rescued.ResolutionStack.OfType<DyingFrame>().Single();
        var losingQing = rescued.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == "classic:qingxian");
        Require(originalDying.VictimSeat == 1 && originalDying.ParentFrameId == losingQing.Id && originalDying.KillerSeat is null &&
            originalDying.Continuation == DyingContinuationKind.ProgramSkill && rescued.State.Players[1].Hp == 0 &&
            Facts<ProgramSkillHpLostEvent>(rescued).Any(fact => fact.FrameId == losingQing.Id && fact.TargetSeat == 1 && fact.Amount == 1 && fact.RemainingHp == 0) &&
            Facts<PlayerDyingEvent>(rescued).Single(fact => fact.ResolutionId == originalDying.Id).VictimSeat == 1,
            "The published real participant choice loses its actual last HP and creates the exact original Dying continuation.");
        Cold(rescued, rescuedRegistry);
        Reach(rescued, prompt => prompt.PlayerSeat == 1 && prompt.SkillPrompt?.SkillId == Hp &&
            rescued.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == "classic:niepan"));
        var niepan = rescued.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == "classic:niepan");
        var restoredHp = rescued.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single(frame => frame.ResumeFrameId == niepan.Id);
        var stillDying = rescued.ResolutionStack.OfType<DyingFrame>().Single(frame => frame.Id == originalDying.Id);
        Require(niepan.OwnerSeat == 1 && niepan.TriggerId == "activation" &&
            niepan.GameplayHash == rescuedRegistry.Skills["classic:niepan"].Program!.GameplayHash &&
            niepan.WindowContext is { Window: SkillProgramTriggerWindow.SelfDyingResponse, OwnerSeat: 1, TargetSeat: 1 } niepanContext &&
            niepanContext.ParentFrameId == originalDying.Id && stillDying.ResponderSeat == 1 &&
            Facts<ProgramBindingStartedEvent>(rescued).Count(fact => fact.FrameId == niepan.Id && fact.SkillInstanceId == niepan.SkillInstanceId && fact.Window == SkillProgramTriggerWindow.SelfDyingResponse) == 1 &&
            restoredHp.Continuation == PostEventContinuation.Program && restoredHp.Change.ParentFrameId == niepan.Id &&
            restoredHp.Change.Kind == HpChangeKind.Recovery && restoredHp.Change.TargetSeat == 1 && restoredHp.Change.SourceSeat == 1 &&
            restoredHp.Change.Amount == 3 && restoredHp.Change.HpBefore == 0 && restoredHp.Change.HpAfter == 3 &&
            Facts<RecoveryAppliedEvent>(rescued).Count(fact => fact.SourceSeat == 1 && fact.TargetSeat == 1 && fact.Amount == 3 && fact.RemainingHp == 3) == 1 &&
            rescued.State.Players[1].Hp == 3 &&
            rescued.CardMovements.Count(move => move.CardId == rescueCost && move.From == CardLocation.Equipment(0)) == 1 &&
            rescued.CreateCardZoneDiagnostics().Where(card => card.Location == CardLocation.Processing).Select(card => card.CardId).SequenceEqual([rescuedMaterial]) &&
            !Facts<CardUseDeclaredEvent>(rescued).Any(fact => fact.SourceSeat == 1 && fact.CardKind == CardKind.SilverLion),
            "Native real Niepan pauses in its exact recovery child at zero-to-three HP; random equipment and a second counter-response have not run.");
        PrivateAndFrozen(rescued); Cold(rescued, rescuedRegistry); Continue(rescued);
        Reach(rescued, prompt => prompt.PlayerSeat == 1 && prompt.SkillPrompt?.SkillId == Gain &&
            rescued.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.Id == niepan.Id));
        Require(rescued.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last().Batch.ParentFrameId is { } gainParent &&
            rescued.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.Id == gainParent && (frame.Id == niepan.Id || frame.SkillId == Hp)) &&
            rescued.State.Players[1].Hp == 3 && rescued.ResolutionStack.OfType<DyingFrame>().Single().Id == originalDying.Id &&
            rescued.ResolutionStack.OfType<NullificationWindowFrame>().Single().CounterspellPayment is not null,
            "Real recovery-observer draw/gain children remain above the same issued Niepan/Dying and already-paid node.");
        PrivateAndFrozen(rescued); Cold(rescued, rescuedRegistry); Continue(rescued);
        Reach(rescued, prompt => prompt.SkillPrompt?.SkillId == Completed);
        var equipmentFact = Facts<CardUseDeclaredEvent>(rescued).Single(fact => fact.SourceSeat == 1 && fact.CardKind == CardKind.SilverLion);
        var acceptedNode = rescued.ResolutionStack.OfType<NullificationWindowFrame>().Single(frame => frame.Id == rescuedNodeId);
        Require(Facts<DyingResolvedEvent>(rescued).Single(fact => fact.ResolutionId == originalDying.Id) is { VictimSeat: 1, Survived: true } &&
            Facts<ProgramBindingResolvedEvent>(rescued).Single(fact => fact.FrameId == niepan.Id) is { Completed: true } &&
            rescued.State.Players[1].Hp == 3 && rescued.CreateSnapshot(0).Players[1].Equipment.Any(card => card.Id == equipmentFact.CardId) &&
            Facts<CardUseFinishedEvent>(rescued).Count(fact => fact.ResolutionId == equipmentFact.ResolutionId && fact.CardKind == CardKind.SilverLion) == 1 &&
            acceptedNode.CounterspellPayment is null && acceptedNode.UnrespondableCounterspell is not null &&
            !Facts<NullificationRequestedEvent>(rescued).Any(fact => fact.ResolutionId == acceptedNode.ParentFrameId && fact.ChainDepth > 0),
            "Only after actual Niepan and all HP/gain children does Qingxian use real equipment and return to the original completed unrespondable response.");
        Cold(rescued, rescuedRegistry); Continue(rescued); ReachPlay(rescued);
        Require(rescued.CardMovements.Count(move => move.CardId == rescueCost && move.From == CardLocation.Equipment(0)) == 1 &&
            Facts<UnrespondableCounterspellIssuedEvent>(rescued).Count(fact => fact.WindowFrameId == rescuedNodeId) == 1 &&
            Facts<ProgramBindingStartedEvent>(rescued).Count(fact => fact.OwnerSeat == 1 && fact.SkillId == "classic:niepan") == 1 &&
            Facts<NullificationResolvedEvent>(rescued).Single(fact => fact.ResolutionId == acceptedNode.ParentFrameId).EffectNullified &&
            rescued.CreateSnapshot(0).Players[2].Equipment.Any(card => card.Id == protectedArmor) &&
            !rescued.CreateCardZoneDiagnostics().Any(card => card.Location == CardLocation.Processing),
            "The genuine self rescue, random equipment and original paid counterspell finish once without losing the protected original target armor.");
        Cold(rescued, rescuedRegistry);

        foreach (var rescueKind in new[] { "virtual", "bound" })
        {
            var (alcoholGame, alcoholRegistry) = Create(black: true, qingxian: true, otherRescue: rescueKind,
                emptyOtherHands: rescueKind == "virtual");
            int? chunId = null;
            if (rescueKind == "bound")
            {
                // Store an actual printed Slash through ordinary classic
                // Chunlao's real turn-ending choice, preserving it at discard.
                chunId = Hand(alcoholGame, 0).First(card => card.Kind == CardKind.Slash).Id;
                Accept(alcoholGame, new EndPlayPhaseCommand(0, alcoholGame.Revision, P(alcoholGame)!.PromptId));
                Reach(alcoholGame, prompt => prompt.PlayerSeat == 0 && (prompt.Kind == DecisionKind.DiscardCards ||
                    prompt.SkillPrompt?.SkillId == "classic:chunlao"));
                if (P(alcoholGame)!.Kind == DecisionKind.DiscardCards)
                {
                    var discard = P(alcoholGame)!;
                    var discarded = discard.ValidCardIds.Where(id => id != chunId.Value).Take(discard.RequiredCardCount).ToArray();
                    Require(discarded.Length == discard.RequiredCardCount, "The genuine discard retains one published actual Slash for ordinary Chunlao.");
                    Accept(alcoholGame, new DiscardCardsCommand(0, discarded, discard.PromptId, alcoholGame.Revision));
                }
                Reach(alcoholGame, prompt => prompt.SkillPrompt?.SkillId == "classic:chunlao" &&
                    prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
                Answer(alcoholGame, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
                Reach(alcoholGame, prompt => prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
                Answer(alcoholGame, choice => choice.Cards.SequenceEqual([chunId.Value]));
                Answer(alcoholGame, choice => choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
                ReachPlay(alcoholGame);
                Require(alcoholGame.CreateSnapshot(0).Players[0].ChunlaoCards is [var stored] && stored.Id == chunId.Value &&
                    alcoholGame.CardMovements.Count(move => move.CardId == chunId.Value && move.From == CardLocation.Hand(0) &&
                        move.To == CardLocation.Chunlao(0)) == 1,
                    "A real ordinary turn stores exactly the original hand Slash in the provider's public Chun pile.");
                Cold(alcoholGame, alcoholRegistry);
            }
            UseDriver(alcoholGame, "equip-self", 0); ReachPlay(alcoholGame);
            var alcoholArmor = alcoholGame.CreateSnapshot(0).Players[0].Equipment.Single().Id;
            UseDriver(alcoholGame, "equip-other", 2); ReachPlay(alcoholGame);
            var untouchedArmor = alcoholGame.CreateSnapshot(0).Players[2].Equipment.Single().Id;
            // Bound Chunlao genuinely crosses a round before this preparation;
            // those peers have drawn, so retain their real hand-clearing commands.
            if (rescueKind == "bound")
                foreach (var seat in new[] { 1, 2, 3 }) { UseDriver(alcoholGame, "empty-other-hand", seat); ReachPlay(alcoholGame); }
            Require(alcoholGame.State.Players[1].Hp == 8, "The real alcohol rescue participant begins its preparation at eight HP.");
            UseDriver(alcoholGame, "prepare-one-hp", 1); ReachPlay(alcoholGame);
            Require(alcoholGame.State.Players[1].Hp == 1 && Hand(alcoholGame, 1).Count == 0,
                "The exact chosen participant reaches one HP via a genuine seven-HP loss before either program rescue starts.");
            var alcoholTrick = Hand(alcoholGame, 0).First().Id;
            PlayAs(alcoholGame, alcoholTrick, [2], CardKind.Dismantlement);
            Reach(alcoholGame, prompt => prompt.Kind == DecisionKind.Nullification && prompt.PlayerSeat == 0);
            var alcoholNodeId = alcoholGame.ResolutionStack.OfType<NullificationWindowFrame>().Single().Id;
            var alcoholParentUseId = alcoholGame.ResolutionStack.OfType<NullificationWindowFrame>().Single().ParentFrameId;
            Answer(alcoholGame, choice => choice.Cards.SequenceEqual([alcoholArmor]));
            Reach(alcoholGame, prompt => prompt.SkillPrompt?.SkillId == "classic:qingxian" &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
            Answer(alcoholGame, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(alcoholGame, prompt => prompt.SkillPrompt?.SkillId == "classic:qingxian" && prompt.Choices.Any(choice => choice.Targets.SequenceEqual([1])));
            Answer(alcoholGame, choice => choice.Targets.SequenceEqual([1]));
            Reach(alcoholGame, prompt => prompt.SkillPrompt?.SkillId == "classic:qingxian" &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == "lose"));
            Answer(alcoholGame, choice => choice.Parameters.GetValueOrDefault("option-id") == "lose");
            var alcoholDying = alcoholGame.ResolutionStack.OfType<DyingFrame>().Single();
            Require(alcoholDying.VictimSeat == 1 && alcoholDying.Continuation == DyingContinuationKind.ProgramSkill &&
                alcoholGame.State.Players[1].Hp == 0 && alcoholGame.ResolutionStack.OfType<ProgramSkillFrame>()
                    .Single(frame => frame.Id == alcoholDying.ParentFrameId).SkillId == "classic:qingxian",
                "The exact paid counterspell HP observer causes a real Program-owned zero-HP Dying occurrence.");
            if (rescueKind == "virtual")
            {
                Reach(alcoholGame, prompt => prompt.SkillPrompt?.SkillId == "boundary:jiushi" &&
                    prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == "flip"));
                Cold(alcoholGame, alcoholRegistry);
                Answer(alcoholGame, choice => choice.Parameters.GetValueOrDefault("option-id") == "flip");
                Reach(alcoholGame, prompt => prompt.PlayerSeat == 1 && prompt.SkillPrompt?.SkillId == FaceChild);
                var self = alcoholGame.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == "boundary:jiushi");
                var face = alcoholGame.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(frame => frame.Window == SkillProgramTriggerWindow.CharacterTurnedOver);
                Require(self.WindowContext is { Window: SkillProgramTriggerWindow.SelfDyingResponse, OwnerSeat: 1, TargetSeat: 1 } context &&
                    context.ParentFrameId == alcoholDying.Id && face.ResumeProgramFrameId == self.Id && face.OwnerSeat == 1 &&
                    face.CharacterStateContinuation == CharacterStateContinuation.Program && alcoholGame.State.Players[1].IsFaceDown &&
                    alcoholGame.State.Players[1].Hp == 0 && !Facts<ProgramDyingRescueEvent>(alcoholGame).Any(fact => fact.DyingFrameId == alcoholDying.Id) &&
                    alcoholGame.ResolutionStack.OfType<NullificationWindowFrame>().Single().CounterspellPayment is not null,
                    "Real 629 Jiushi pays its face transition and pauses under the exact current SelfDying response before zero-entity Alcohol is issued.");
                PrivateAndFrozen(alcoholGame); Cold(alcoholGame, alcoholRegistry); Continue(alcoholGame);
            }
            else
            {
                Reach(alcoholGame, prompt => prompt.PlayerSeat == 0 && prompt.Kind == DecisionKind.RescueDying &&
                    prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") == "program-trigger" &&
                        choice.Parameters.GetValueOrDefault("skill-id") == "classic:chunlao"));
                Cold(alcoholGame, alcoholRegistry);
                Answer(alcoholGame, choice => choice.Parameters.GetValueOrDefault("response") == "program-trigger" &&
                    choice.Parameters.GetValueOrDefault("skill-id") == "classic:chunlao");
                Reach(alcoholGame, prompt => prompt.SkillPrompt?.SkillId == "classic:chunlao" &&
                    prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-source-card"));
                var heldAlcoholId = chunId ?? throw new InvalidOperationException("The bound rescue has no paid stored Slash.");
                Answer(alcoholGame, choice => choice.Cards.SequenceEqual([heldAlcoholId]));
                Reach(alcoholGame, prompt => prompt.PlayerSeat == 1 && prompt.SkillPrompt?.SkillId == Hp);
                var provider = alcoholGame.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == "classic:chunlao");
                var boundUse = alcoholGame.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.CardId == heldAlcoholId);
                var hpChild = alcoholGame.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single(frame => frame.ResumeFrameId == boundUse.Id);
                Require(provider.OwnerSeat == 0 && provider.WindowContext is { Window: SkillProgramTriggerWindow.DyingResponse, TargetSeat: 1 } context &&
                    context.ParentFrameId == alcoholDying.Id && alcoholGame.ResolutionStack.OfType<DyingFrame>().Single().ResponderSeat == 0 &&
                    boundUse.DyingResponse is null && boundUse.Action is { ActorSeat: 1, ProviderSeat: 0, Type: CardActionType.Use, EffectiveKind: CardKind.Alcohol } boundAlcoholAction &&
                    boundAlcoholAction.PhysicalCards is [var boundAlcoholCost] && boundAlcoholCost.CardId == heldAlcoholId && boundAlcoholCost.From == CardLocation.Chunlao(0) &&
                    boundAlcoholAction.ConversionChain is [var conversion] && conversion.SkillId == provider.SkillId && conversion.SkillInstanceId == provider.SkillInstanceId &&
                    conversion.OwnerSeat == provider.OwnerSeat && conversion.BindingId == provider.TriggerId &&
                    hpChild.Continuation == PostEventContinuation.CardUse && hpChild.Change.ParentFrameId == boundUse.Id && hpChild.Change.TargetSeat == 1 &&
                    alcoholGame.State.Players[1].Hp == 1 && alcoholGame.CardMovements.Count(move => move.CardId == heldAlcoholId &&
                        move.From == CardLocation.Chunlao(0) && move.To == CardLocation.Processing) == 1 &&
                    alcoholGame.CardMovements.Count(move => move.CardId == heldAlcoholId && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile) == 1,
                    "Real bound-pile Alcohol pauses in its exact HP child with separate actor/provider, exact physical pile provenance and one payment.");
                PrivateAndFrozen(alcoholGame); Cold(alcoholGame, alcoholRegistry); Continue(alcoholGame);
                Reach(alcoholGame, prompt => prompt.PlayerSeat == 1 && prompt.SkillPrompt?.SkillId == AlcoholCompleted);
                var completedAlcohol = alcoholGame.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(frame => frame.ParentFrameId == boundUse.Id);
                Require(completedAlcohol.Continuation == ProgramCardContinuation.CompletedCard && completedAlcohol.Action.ActionId == boundUse.Action!.ActionId &&
                    alcoholGame.ResolutionStack.OfType<DyingFrame>().Single().Id == alcoholDying.Id &&
                    alcoholGame.ResolutionStack.OfType<NullificationWindowFrame>().Single().CounterspellPayment is not null,
                    "The same actual bound Alcohol owns its completed-card child while Dying and the paid counterspell continue to wait.");
                PrivateAndFrozen(alcoholGame); Cold(alcoholGame, alcoholRegistry); Continue(alcoholGame);
            }
            Reach(alcoholGame, prompt => prompt.PlayerSeat == 1 && prompt.SkillPrompt?.SkillId == EquipmentChild);
            var realEquipment = alcoholGame.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.CardKind == CardKind.SilverLion);
            var alcoholFacts = Facts<ProgramDyingRescueEvent>(alcoholGame).Where(fact => fact.DyingFrameId == alcoholDying.Id).ToArray();
            Require(alcoholFacts is [var alcoholRescue] && alcoholRescue.CardId == (chunId ?? 0) && alcoholRescue.VictimSeat == 1 &&
                alcoholRescue.SkillId == (rescueKind == "virtual" ? "boundary:jiushi" : "classic:chunlao") && alcoholRescue.VictimHp == 1 &&
                alcoholGame.State.Players[1].Hp == 1 && Facts<DyingResolvedEvent>(alcoholGame).Single(fact => fact.ResolutionId == alcoholDying.Id).Survived &&
                Facts<CardUseDeclaredEvent>(alcoholGame).Count(fact => fact.CardId == (chunId ?? 0) && fact.CardKind == CardKind.Alcohol && fact.SourceSeat == 1) == 1 &&
                alcoholGame.CreateCardZoneDiagnostics().Where(card => card.Location == CardLocation.Processing).Select(card => card.CardId).Order()
                    .SequenceEqual(new[] { alcoholTrick, realEquipment.CardId }.Order()) &&
                alcoholGame.CardMovements.Count(move => move.CardId == alcoholArmor && move.From == CardLocation.Equipment(0)) == 1,
                "The genuine program Alcohol returns before Qingxian issues its exact random equipment; only the original trick and real equipment are Processing, with no repeated cost.");
            PrivateAndFrozen(alcoholGame); Cold(alcoholGame, alcoholRegistry); Continue(alcoholGame);
            Reach(alcoholGame, prompt => prompt.SkillPrompt?.SkillId == Completed); Cold(alcoholGame, alcoholRegistry);
            Continue(alcoholGame); ReachPlay(alcoholGame);
            Require(Facts<UnrespondableCounterspellIssuedEvent>(alcoholGame).Count(fact => fact.WindowFrameId == alcoholNodeId) == 1 &&
                Facts<NullificationResolvedEvent>(alcoholGame).Single(fact => fact.ResolutionId == alcoholParentUseId).EffectNullified &&
                !Facts<NullificationRequestedEvent>(alcoholGame).Any(fact => fact.ResolutionId == alcoholParentUseId && fact.ChainDepth > 0) &&
                alcoholGame.CardMovements.Count(move => move.CardId == alcoholArmor && move.From == CardLocation.Equipment(0)) == 1 &&
                alcoholGame.CreateSnapshot(0).Players[2].Equipment.Any(card => card.Id == untouchedArmor) &&
                alcoholGame.CreateSnapshot(0).Players[1].Equipment.Any(card => card.Id == realEquipment.CardId) &&
                !alcoholGame.CreateCardZoneDiagnostics().Any(card => card.Location == CardLocation.Processing) &&
                (chunId is null || alcoholGame.CreateSnapshot(0).Players[0].ChunlaoCount == 0 &&
                    alcoholGame.CardMovements.Count(move => move.CardId == chunId.Value && move.From == CardLocation.Chunlao(0) && move.To == CardLocation.Processing) == 1 &&
                    alcoholGame.CardMovements.Count(move => move.CardId == chunId.Value && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile) == 1 &&
                    Facts<CardUseFinishedEvent>(alcoholGame).Count(fact => fact.CardId == chunId.Value && fact.CardKind == CardKind.Alcohol) == 1),
                "Both genuine program rescue producers finish the protected original unrespondable response once and clean their exact physical costs.");
            Cold(alcoholGame, alcoholRegistry);
        }
    }

    public static void ActualTurnTrickHistoryAndCangzhuoDiscardCold()
    {
        var (unused, unusedRegistry) = Create(tricks: true);
        var ids = Hand(unused, 0).Select(card => card.Id).ToArray();
        Require(ids.Length > unused.State.Players[0].Hp, "The fixed real trick hand exceeds ordinary HP hand limit.");
        Accept(unused, new EndPlayPhaseCommand(0, unused.Revision, P(unused)!.PromptId));
        ReachUntil(unused, () => Facts<TurnHandLimitCardKindExemptionGrantedEvent>(unused).Any(fact => fact.Policy.Source.SkillId == "boundary:cangzhuo"));
        var policy = Facts<TurnHandLimitCardKindExemptionGrantedEvent>(unused).Single(fact => fact.Policy.Source.SkillId == "boundary:cangzhuo").Policy;
        Require(policy.TurnNumber == 1 && policy.Source.OwnerSeat == 0 && policy.CardKinds.Contains(CardKind.DrawTwo) &&
            policy.CardKinds.Contains(CardKind.Nullification) && policy.CardKinds.Contains(CardKind.Lightning) &&
            !policy.CardKinds.Contains(CardKind.Slash) && Facts<ActualTurnTrickUseRecordedEvent>(unused).Length == 0,
            "No actual own trick use grants all current instant and delayed trick kinds, while Basic cards remain counted.");
        Frozen(policy.CardKinds); Cold(unused, unusedRegistry);
        ReachUntil(unused, () => unused.State.TurnNumber > 1);
        Require(!unused.CardMovements.Any(move => ids.Contains(move.CardId) && move.Reason == CardMoveReasons.HandLimitDiscard) &&
            Enumerable.Range(0, 4).All(seat => unused.CreateSnapshot(seat).Players[0].TurnHandLimitCardKindExemptions is null),
            "Exempt actual tricks survive that ordinary discard phase; the issued turn policy expires at the actual turn end.");
        Cold(unused, unusedRegistry);

        var (used, usedRegistry) = Create(tricks: true);
        var useCard = Hand(used, 0).First().Id; Play(used, useCard, []); ReachPlay(used);
        var accepted = Facts<ActualTurnTrickUseRecordedEvent>(used).Single();
        Require(accepted.ActorSeat == 0 && accepted.TurnNumber == 1 && accepted.EffectiveKind == CardKind.DrawTwo &&
            Facts<CardActionAcceptedEvent>(used).Any(fact => fact.Action.ActionId == accepted.ActionId),
            "The actual physical trick records one action despite declared/accepted/completed windows.");
        Cold(used, usedRegistry); Accept(used, new EndPlayPhaseCommand(0, used.Revision, P(used)!.PromptId));
        Reach(used, prompt => prompt.Kind == DecisionKind.DiscardCards && prompt.PlayerSeat == 0);
        Require(Facts<TurnHandLimitCardKindExemptionGrantedEvent>(used).All(fact => fact.Policy.Source.SkillId != "boundary:cangzhuo") &&
            P(used)!.RequiredCardCount == Hand(used, 0).Count - used.State.Players[0].Hp &&
            P(used)!.ValidCardIds.Order().SequenceEqual(Hand(used, 0).Select(card => card.Id).Order()),
            "After one real trick use, Cangzhuo does not hide trick cards from the ordinary exact-ID discard prompt.");
        PrivateAndFrozen(used); Cold(used, usedRegistry);
    }

    private static CardMovementRecord[] CostMoves(GameEngine game, int id) => game.CardMovements.Where(move => move.CardId == id && move.Reason == CardMoveReasons.FireAttackDiscard).ToArray();
    private static IReadOnlyList<CardSnapshot> Hand(GameEngine game, int seat) => game.CreateSnapshot(seat).Players[seat].Hand;
    private static T[] Facts<T>(GameEngine game) where T : IGameEvent => game.Events.Select(item => item.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void Play(GameEngine game, int id, int[] targets) => Accept(game, new PlayCardCommand(0, id, targets, game.Revision, P(game)!.PromptId));
    private static void PlayAs(GameEngine game, int id, int[] targets, CardKind kind)
    {
        var action = game.GetHumanLegalActions().First(item => item.CardId == id && item.PlayedCardKind == kind &&
            (item.TargetSeats.Count > 0 ? item.TargetSeats : item.TargetSeat is { } seat ? [seat] : Array.Empty<int>()).SequenceEqual(targets));
        Accept(game, new PlayCardCommand(0, id, targets, game.Revision, P(game)!.PromptId, kind, action.TargetCardId)
        { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
    }
    private static void UseDriver(GameEngine game, string activation, int target) => Accept(game,
        new UseProgramSkillCommand(0, Driver, activation, [], [target], game.Revision, P(game)!.PromptId));
    private static void Answer(GameEngine game, Func<PromptChoice, bool> choose)
    { var prompt = P(game)!; Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(choose).Id, game.Revision)); }
    private static void Continue(GameEngine game) => Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "choose-option" && choice.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void ReachPlay(GameEngine game) => Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard && prompt.PlayerSeat == 0);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> stop) => ReachUntil(game, () => P(game) is { } prompt && stop(prompt));
    private static void ReachUntil(GameEngine game, Func<bool> stop)
    { for (var step = 0; step < 400 && !stop(); step++) Step(game); Require(stop(), $"Expected real boundary was not reached: {P(game)?.Kind}/{P(game)?.SkillPrompt?.SkillId}."); }
    private static void Step(GameEngine game)
    {
        var prompt = P(game);
        if (prompt is { PlayerSeat: 0 } && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "choose-option")) Continue(game);
        else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.Nullification }) Answer(game, choice => choice.Parameters.GetValueOrDefault("response") == "pass");
        else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(game, new DiscardCardsCommand(0, prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, game.Revision));
        else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(game, choice => choice.Parameters.GetValueOrDefault("response") == "let-die");
        else if (prompt is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) Accept(game, new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId));
        else if (prompt is { PlayerSeat: 0 } && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Actual command rejected."); }
    private static void Frozen<T>(IReadOnlyList<T> values)
    { Require(values is IList<T> { IsReadOnly: true }, "The prepared collection is frozen."); try { ((IList<T>)values).Add(default!); throw new InvalidOperationException("Prepared collection allowed mutation."); } catch (NotSupportedException) { } }
    private static void PrivateAndFrozen(GameEngine game)
    {
        var state = State(game); var prompt = P(game)!;
        foreach (var seat in Enumerable.Range(0, 4))
        {
            var view = game.CreateSnapshot(seat);
            Require(view.Players.Where(player => player.Seat != seat).All(player => player.Hand.Count == 0), "No other hand IDs leak through prepared views.");
            if (seat != prompt.PlayerSeat) Require(view.PendingDecision is null, "Only the exact picker receives private payment/option choices.");
            else
            {
                var own = view.PendingDecision!; Frozen(own.ValidCardIds); Frozen(own.ValidTargetSeats); Frozen(own.Choices);
                foreach (var choice in own.Choices)
                {
                    Frozen(choice.Cards); Frozen(choice.Targets); Frozen(choice.ContentIds);
                    Require(choice.Parameters is IDictionary<string, string> { IsReadOnly: true }, "Prepared nested choice parameters are frozen.");
                    try { ((IDictionary<string, string>)choice.Parameters).Add("mutation", "probe"); throw new InvalidOperationException("Choice parameters allowed mutation."); } catch (NotSupportedException) { }
                }
            }
        }
        Require(State(game) == state, "Four-view mutation probes preserve the accepted prefix and RNG.");
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(), Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(), game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine game, ContentRegistry registry) => Require(State(game) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry)), "Real accepted commands cold-restore four views, RNG, typed payment/response parents and the exact card ledger.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool black = false, bool tricks = false, bool nativeFire = false, bool physicalCounterspell = false, bool nativeCounterspell = false, bool qingxian = false, bool qingxianDying = false, string? otherRescue = null, bool emptyOtherHands = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(black, tricks, nativeFire, physicalCounterspell, nativeCounterspell, qingxian, qingxianDying, otherRescue, emptyOtherHands));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, prompt => prompt.Kind == DecisionKind.SelectGeneral && prompt.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, "fixture:wolong-owner", game.Revision, P(game)!.PromptId)); ReachPlay(game);
        if (emptyOtherHands)
            Require(Hand(game, 0).Count == 6 && Enumerable.Range(1, 3).All(seat => Hand(game, seat).Count == 0),
                "A legal zero-card base deal and owner-only initial-hand bonus preserve the owner's six real cards while other participants begin without a hand fallback.");
        return (game, registry);
    }

    private sealed class Fixture(bool black, bool tricks, bool nativeFire, bool physicalCounterspell, bool nativeCounterspell, bool qingxian, bool qingxianDying, string? otherRescue, bool emptyOtherHands) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-current-wolong", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = JsonNode.Parse("""
                {"skills":[
                 {"id":"fixture:wolong-driver","revision":1,"viewAs":[{"id":"he-dismantlement","sourceZones":["hand"],"inputKinds":[],"inputSuits":[],"outputKind":"dismantlement","forPlay":true,"forResponse":false}],"activations":[
                  {"id":"equip-self","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"self-equipment"}]},
                  {"id":"equip-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipment"}]},
                  {"id":"hurt-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                  {"id":"prepare-one-hp","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":7}]},
                  {"id":"empty-other-hand","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardParticipantCards","target":"selectedTarget","amount":64,"zones":["hand"]}]}]},
                 {"id":"fixture:wolong-hp","revision":1,"triggers":[{"id":"recover","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]},{"op":"draw","target":"owner","amount":1}]}]},
                 {"id":"fixture:wolong-gain","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"fixture:wolong-loss","revision":1,"triggers":[{"id":"paid-loss","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementReasons":["card.effect.fire-attack-discard"],"movementOccurrence":"perOwnerBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"loss-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"fixture:wolong-completed","revision":1,"triggers":[{"id":"counterspell-completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["nullification"],"includeResponseUses":true,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"completed-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"fixture:wolong-equipment-child","revision":1,"triggers":[{"id":"exact-random-equipment","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardCategories":["equipment"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"equipment-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"fixture:wolong-face-child","revision":1,"triggers":[{"id":"face","window":"characterTurnedOver","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"face-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"fixture:wolong-alcohol-completed","revision":1,"triggers":[{"id":"alcohol-completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["alcohol"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"alcohol-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"fixture:wolong-quiet","revision":1,"triggers":[{"id":"quiet-turn","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            if (emptyOtherHands)
                rules["skills"]!.AsArray().Single(skill => skill!["id"]!.GetValue<string>() == Driver)!["modifiers"] =
                    JsonNode.Parse("""[{"id":"owner-initial-cards","query":"initialHandSize","operation":"add","value":4,"priority":0}]""");
            var presentation = new Dictionary<string, object>();
            foreach (var skill in new[] { Driver, Hp, Gain, Loss, Completed, EquipmentChild, FaceChild, AlcoholCompleted, "fixture:wolong-quiet" })
                presentation[skill] = skill is Hp or Gain or Loss or Completed or EquipmentChild or FaceChild or AlcoholCompleted ? new { name = skill, description = "真实子窗暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } : new { name = skill, description = "固定命令夹具" };
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = 3, skills = presentation }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, id, "真实命令夹具") { Program = program });
            builder.AddSkill(new("fixture:wolong-pick", "稳定选将偏好", "无运行程序的前序AI选择") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new("fixture:wolong-owner", "当前卧龙真实能力", "supporter", Driver, "shu", 3,
                new[] { "classic:bazhen", "boundary:huoji-current", "boundary:kanpo-current", "boundary:cangzhuo", Hp, Gain, Loss, Completed }
                    .Concat(qingxian ? new[] { "classic:qingxian" } : Array.Empty<string>())
                    .Concat(otherRescue == "bound" ? new[] { "classic:chunlao" } : Array.Empty<string>()).ToArray()) { InitialHp = 1 });
            for (var index = 1; index < 4; index++) builder.AddGeneral(new($"fixture:wolong-other-{index}", "固定其他角色", "supporter", "fixture:wolong-pick", "wei", 8,
                otherRescue == "virtual" ? ["fixture:wolong-quiet", "boundary:jiushi", FaceChild, EquipmentChild] : otherRescue == "bound" ? ["fixture:wolong-quiet", Hp, Gain, AlcoholCompleted, EquipmentChild] : nativeFire ? ["boundary:huoji-current"] : nativeCounterspell ? ["fixture:wolong-quiet", "boundary:kanpo-current", Hp, Gain, Completed] : qingxianDying ? ["fixture:wolong-quiet", "classic:niepan", Hp, Gain] : qingxian ? ["fixture:wolong-quiet", EquipmentChild] : ["fixture:wolong-quiet"]));
            var kind = tricks ? "standard:draw_two" : physicalCounterspell ? "standard:nullification" : "classic:silver-lion";
            builder.AddDeck(new("fixture:wolong-deck", "固定小牌组", emptyOtherHands ? 0 : 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(index => new ContentDeckPhysicalCard(otherRescue == "bound" && index % 2 == 1 ? "standard:slash" : kind, black ? Suit.Spade : Suit.Diamond, 7)).ToArray() });
            builder.AddMode(new(Mode, "当前卧龙公共能力命令检查", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:wolong-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:wolong-owner", "fixture:wolong-other-1", "fixture:wolong-other-2", "fixture:wolong-other-3"]));
        }
    }
}
