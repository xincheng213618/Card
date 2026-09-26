using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ClassicGeneralChecks
{
    public static void FormalJiuyuanRecoveryBonus()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var (current, providerSeat, peachCardId, selfPeachCardId, nonWuProviderSeat, nonWuPeachCardId) =
            FindJiuyuanFixture(registry);
        var checkpoint = current.CreateCheckpoint();
        var selfRescue = GameReplay.Restore(checkpoint, registry);
        var nonWuRescue = GameReplay.Restore(checkpoint, registry);

        ApplySyntheticDyingPeach(current, providerSeat, peachCardId);
        var currentSun = current.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        var applied = current.Events.Select(item => item.Payload).OfType<ProgramRecoveryPolicyAppliedEvent>().Single();
        Require(currentSun.Hp == 2 &&
                applied.OwnerSeat == 0 &&
                applied.ProviderSeat == providerSeat &&
                applied.PeachCardId == peachCardId &&
                applied.RecoveryAmount == 2 &&
                current.Events.Select(item => item.Payload).OfType<RecoveryAppliedEvent>().Last().Amount == 2,
            "A different Wu provider's Peach must recover the dying Lord Sun Quan for two through Jiuyuan.");

        ApplySyntheticDyingPeach(selfRescue, 0, selfPeachCardId);
        Require(selfRescue.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hp == 1 &&
                selfRescue.Events.Select(item => item.Payload).OfType<ProgramRecoveryPolicyAppliedEvent>().Count() == 0,
            "Sun Quan's own Peach must not receive Jiuyuan's recovery bonus.");

        ApplySyntheticDyingPeach(nonWuRescue, nonWuProviderSeat, nonWuPeachCardId);
        Require(nonWuRescue.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hp == 1 &&
                nonWuRescue.Events.Select(item => item.Payload).OfType<ProgramRecoveryPolicyAppliedEvent>().Count() == 0,
            "A non-Wu provider's Peach must not receive Jiuyuan's recovery bonus.");
    }

    public static void SetupHealthAndReplay()
    {
        var registry = CreatePreProgramClassicRegistry();
        var currentSun = SelectGeneral(registry, "classic:sun-quan", GameCheckpoint.CurrentRulesVersion);
        var currentSunPlayer = currentSun.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(currentSunPlayer.MaxHp == 5 && currentSunPlayer.Hp == 5,
            "A classic 4-HP Lord must receive the identity-mode +1 maximum HP.");
        Require(currentSunPlayer.Skills is { Count: 2 } &&
                currentSunPlayer.Skills.Select(skill => skill.ContentId).SequenceEqual(["classic:zhiheng", "classic:jiuyuan"]),
            "The current snapshot must publish the selected general's ordered skill list.");

        var simaYi = SelectGeneral(registry, "classic:sima-yi", GameCheckpoint.CurrentRulesVersion);
        var simaYiPlayer = simaYi.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(simaYiPlayer.MaxHp == 4 &&
                simaYiPlayer.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:feedback", "classic:guicai"]),
            "Sima Yi must combine base 3 HP, the Lord bonus, Feedback and Guicai.");
        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(simaYi.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(simaYi.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(simaYi)),
            "A selected multi-skill classic general must replay exactly.");
    }


    public static void FormalRendeFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(registry.Skills["classic:rende"] is
                { Program: { } program } &&
                program.Activations.Single().Id == "give-and-heal-after-two",
            "Current Rende must be an independently registered Program activation.");

        GameEngine FindRende()
        {
            for (var seed = 1; seed <= 4096; seed++)
            {
                var candidate = StartClassicGeneralAtPlay(
                    registry, seed, "classic:liu-bei", GameCheckpoint.CurrentRulesVersion);
                if (candidate?.GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.UseProgramSkill &&
                        action.ProgramSkillId == "classic:rende" &&
                        action.SelectableCardIds.Count >= 3 &&
                        action.SelectableTargetSeats.Count >= 2) == true)
                    return candidate;
            }
            throw new InvalidOperationException("No replayable Rende fixture exposed three cards and two targets.");
        }

        static void GiveOne(GameEngine game, int cardId, int targetSeat)
        {
            var prompt = game.PendingDecision ??
                throw new InvalidOperationException("Rende lost its human play prompt.");
            var result = game.Submit(new UseProgramSkillCommand(
                0, "classic:rende", "give-and-heal-after-two", [cardId], [targetSeat],
                game.Revision, prompt.PromptId));
            Require(result.Accepted, result.Error?.Message ?? "The Rende Program command was rejected.");
            if (game.PendingDecision is null && game.State.Status != EngineStatus.Completed)
                Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                    "Rende did not resume the human play boundary.");
        }

        static int GivenThisPhase(GameEngine game) =>
            game.CreateSnapshot(0, revealAll: true).Players[0].SkillRuntimeStates!
                .Single(state => state.SkillId == "classic:rende").Usages
                .SingleOrDefault(usage =>
                    usage.UsageId == "cards-given" && usage.Scope == SkillUsageScope.Phase)?.Count ?? 0;

        var current = FindRende();
        var action = current.GetHumanLegalActions().Single(item =>
            item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == "classic:rende");
        var cards = action.SelectableCardIds.Order().Take(3).ToArray();
        var targets = action.SelectableTargetSeats.Order().Take(2).ToArray();
        var ownerBefore = current.CreateSnapshot(0, revealAll: true).Players[0];
        SetRuntimeHp(current, 0, ownerBefore.MaxHp - 1);
        GiveOne(current, cards[0], targets[0]);
        Require(GivenThisPhase(current) == 1 &&
                current.CreateSnapshot(0, revealAll: true).Players[0].Hp == ownerBefore.MaxHp - 1,
            "The first one-card gift must not heal Liu Bei.");

        GiveOne(current, cards[1], targets[1]);
        Require(GivenThisPhase(current) == 2 &&
                current.CreateSnapshot(0, revealAll: true).Players[0].Hp == ownerBefore.MaxHp &&
                current.Events.Select(envelope => envelope.Payload).OfType<RecoveryAppliedEvent>()
                    .Count(recovery => recovery.SourceSeat == 0 && recovery.TargetSeat == 0) == 1,
            "The second cumulative gift must heal Liu Bei exactly once.");

        GiveOne(current, cards[2], targets[0]);
        Require(GivenThisPhase(current) == 3 &&
                current.Events.Select(envelope => envelope.Payload).OfType<RecoveryAppliedEvent>()
                    .Count(recovery => recovery.SourceSeat == 0 && recovery.TargetSeat == 0) == 1 &&
                current.GetHumanLegalActions().Any(item =>
                    item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == "classic:rende"),
            "Later gifts must remain legal without repeating the threshold heal.");

        var ended = current.Submit(new EndPlayPhaseCommand(0, current.Revision,
            current.PendingDecision!.PromptId));
        Require(ended.Accepted, ended.Error?.Message ?? "Rende play phase did not end.");
        if (GivenThisPhase(current) != 0 && current.State.Status != EngineStatus.Completed)
            Require(current.Submit(new AdvanceCommand(current.Revision)).Accepted,
                "Rende phase boundary did not advance.");
        Require(GivenThisPhase(current) == 0, "Rende phase ledger must reset at the phase boundary.");

        var replaySource = FindRende();
        var replayAction = replaySource.GetHumanLegalActions().Single(item =>
            item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == "classic:rende");
        var replayCards = replayAction.SelectableCardIds.Order().Take(2).ToArray();
        var replayTargets = replayAction.SelectableTargetSeats.Order().Take(2).ToArray();
        GiveOne(replaySource, replayCards[0], replayTargets[0]);
        var restored = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(replaySource.CreateCheckpoint())),
            registry);
        Require(GivenThisPhase(restored) == 1 &&
                SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(replaySource.CreateSnapshot(0, revealAll: true)),
            "The one-card phase ledger must restore from the accepted command prefix.");
        GiveOne(replaySource, replayCards[1], replayTargets[1]);
        GiveOne(restored, replayCards[1], replayTargets[1]);
        Require(GivenThisPhase(restored) == 2 &&
                SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(replaySource.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(replaySource)),
            "Restored Rende must cross the same threshold with identical events.");
    }
    public static void FormalQixiFlow()
    {
        var registry = CreatePreProgramClassicRegistry();
        Require(GameCheckpoint.CurrentRulesVersion >= 28,
            "Formal Qixi must have an explicit rules-version boundary.");

        Require(registry.Skills["classic:qixi"].Program?.ViewAs.Single().OutputKind ==
                CardKind.Dismantlement,
            "Qixi must publish a configured Dismantlement conversion.");

        GameEngine? current = null;
        CardSnapshot? blackEquipment = null;
        CardSnapshot? redCard = null;
        LegalAction? handConversion = null;
        for (var seed = 1; seed <= 16_384 && current is null; seed++)
        {
            var candidate = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:gan-ning",
                GameCheckpoint.CurrentRulesVersion);
            if (candidate is null)
            {
                continue;
            }

            var snapshot = candidate.CreateSnapshot(0, revealAll: true);
            var self = snapshot.Players.Single(player => player.Seat == 0);
            var candidateEquipment = self.Hand.FirstOrDefault(card =>
                EquipmentCatalog.IsEquipment(card.Kind) &&
                card.Suit is Suit.Spade or Suit.Club);
            var candidateRed = self.Hand.FirstOrDefault(card =>
                card.Kind != CardKind.Dismantlement &&
                card.Suit is Suit.Heart or Suit.Diamond);
            var conversion = candidateEquipment is null
                ? null
                : candidate.GetHumanLegalActions().FirstOrDefault(action =>
                    action.Kind == LegalActionKind.Dismantlement &&
                    action.CardId == candidateEquipment.Id &&
                    action.PlayedCardKind == CardKind.Dismantlement &&
                    action.TargetCardId is null);
            var hasNullificationResponder = snapshot.Players
                .Where(player => player.Seat != 0)
                .Any(player => player.Hand.Any(card => card.Kind == CardKind.Nullification));
            if (candidateEquipment is null || candidateRed is null || conversion is null || !hasNullificationResponder)
            {
                continue;
            }

            current = candidate;
            blackEquipment = candidateEquipment;
            redCard = candidateRed;
            handConversion = conversion;
        }

        if (current is null || blackEquipment is null || redCard is null || handConversion is null)
        {
            throw new InvalidOperationException("No deterministic Gan Ning fixture exposed black equipment and a Nullification responder.");
        }

        var selected = current.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(selected.GeneralId == "classic:gan-ning" &&
                selected.MaxHp == 5 &&
                selected.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:qixi"]),
            "Classic Gan Ning must combine base 4 HP, the Lord bonus and formal Qixi.");
        Require(current.GetHumanLegalActions().Any(action =>
                    action.Kind == LegalActionKind.Dismantlement &&
                    action.CardId == blackEquipment.Id &&
                    action.PlayedCardKind == CardKind.Dismantlement) &&
                !current.GetHumanLegalActions().Any(action =>
                    action.Kind == LegalActionKind.Dismantlement &&
                    action.CardId == redCard.Id &&
                    action.PlayedCardKind == CardKind.Dismantlement),
            "Formal Qixi must publish black hand-card conversions without converting red hand cards.");
        var prompt = current.PendingDecision ??
            throw new InvalidOperationException("Gan Ning fixture lost its play prompt.");
        var stateBeforeInvalid = SnapshotJson.Serialize(current.CreateSnapshot(0, revealAll: true));
        var invalid = current.Submit(new PlayCardCommand(
            0,
            blackEquipment.Id,
            handConversion.TargetSeats,
            current.Revision,
            prompt.PromptId,
            CardKind.Snatch,
            handConversion.TargetCardId));
        Require(!invalid.Accepted &&
                SnapshotJson.Serialize(current.CreateSnapshot(0, revealAll: true)) == stateBeforeInvalid,
            "A mismatched Qixi effective kind must reject atomically.");

        Equip(current, blackEquipment.Id);
        var equippedConversion = current.GetHumanLegalActions().FirstOrDefault(action =>
            action.Kind == LegalActionKind.Dismantlement &&
            action.CardId == blackEquipment.Id &&
            action.PlayedCardKind == CardKind.Dismantlement &&
            action.TargetCardId is null);
        Require(equippedConversion is not null,
            "Formal Qixi must convert a black card from the equipment zone.");
        var selectedEquippedConversion = equippedConversion ??
            throw new InvalidOperationException("The equipped Qixi action disappeared before submission.");

        var used = SubmitPlayAction(current, selectedEquippedConversion);
        Require(used.Accepted, used.Error?.Message ?? "Equipped Qixi conversion was rejected.");

        var nullificationFrame = current.ResolutionStack.OfType<NullificationWindowFrame>().SingleOrDefault();
        Require(nullificationFrame is not null &&
                nullificationFrame.EffectCardId == blackEquipment.Id &&
                nullificationFrame.EffectCardKind == CardKind.Dismantlement &&
                current.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>().Any(item =>
                    item.CardId == blackEquipment.Id && item.CardKind == CardKind.Dismantlement) &&
                current.Events.Select(item => item.Payload).OfType<NullificationRequestedEvent>().Any(item =>
                    item.EffectCardId == blackEquipment.Id && item.EffectCardKind == CardKind.Dismantlement),
            "Qixi must expose Dismantlement as the effective kind in the card-use and Nullification contracts.");
        Require(current.CardMovements.Any(movement =>
                movement.CardId == blackEquipment.Id &&
                movement.From == CardLocation.Equipment(0) &&
                movement.To == CardLocation.Processing &&
                movement.Reason == CardMoveReasons.Use),
            "Qixi must preserve the exact equipment source zone for its physical card cost.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(current.CreateCheckpoint()));
        var pausedRestored = GameReplay.Restore(pausedCheckpoint, registry);
        Require(SnapshotJson.Serialize(pausedRestored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(current.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(pausedRestored).SequenceEqual(EventSignatures(current)),
            "A paused Qixi Nullification window must restore with identical effective-card state.");

        for (var step = 0; step < 128; step++)
        {
            if (current.PendingDecision is { Kind: DecisionKind.PlayCard } && current.ResolutionStack.Count == 0)
            {
                break;
            }

            CommandResult next;
            if (current.PendingDecision is { Kind: DecisionKind.Nullification } nullification &&
                nullification.PlayerSeat == 0)
            {
                var pass = nullification.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "pass");
                next = current.Submit(new AnswerPromptCommand(0, nullification.PromptId, pass.Id, current.Revision));
            }
            else if (current.PendingDecision is { Kind: DecisionKind.SelectTargetCard } selection &&
                     selection.PlayerSeat == 0)
            {
                next = current.Submit(new AnswerPromptCommand(
                    0,
                    selection.PromptId,
                    selection.Choices[0].Id,
                    current.Revision));
            }
            else
            {
                next = current.Submit(new AdvanceOneStepCommand(current.Revision));
            }

            Require(next.Accepted, next.Error?.Message ?? "Qixi resolution did not advance.");
        }

        Require(current.PendingDecision?.Kind == DecisionKind.PlayCard &&
                current.CardMovements.Any(movement =>
                    movement.CardId == blackEquipment.Id &&
                    movement.From == CardLocation.Processing &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.UseFinished) &&
                current.Events.Select(item => item.Payload).OfType<CardUseFinishedEvent>().Any(item =>
                    item.CardId == blackEquipment.Id && item.CardKind == CardKind.Dismantlement),
            "Qixi must finish by discarding the physical equipment while publishing Dismantlement as the effective kind.");

        var restored = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(current.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(current.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(current)),
            "The completed equipped Qixi command must restore with identical state and events.");
    }

    public static void FormalKejiFlow()
    {
        var registry = CreatePreProgramClassicRegistry();
        var game = SelectGeneral(registry, "classic:lu-meng", GameCheckpoint.CurrentRulesVersion);
        var selected = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(selected.GeneralId == "classic:lu-meng" &&
                selected.MaxHp == 5 &&
                selected.Hp == 5 &&
                selected.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:keji"]),
            "Classic Lu Meng must combine base 4 HP, the Lord bonus and formal Keji.");

        var reachedPlay = game.Submit(new AdvanceCommand(game.Revision));
        Require(reachedPlay.Accepted && game.PendingDecision?.Kind == DecisionKind.PlayCard,
            reachedPlay.Error?.Message ?? "Classic Lu Meng did not reach the play phase.");
        var handBeforeDiscard = game.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0);
        Require(handBeforeDiscard.HandCount > handBeforeDiscard.Hp,
            "The Keji fixture must have at least one excess hand card to prove the skipped discard.");

        var ended = game.Submit(new EndPlayPhaseCommand(
            0,
            game.Revision,
            game.PendingDecision!.PromptId));
        Require(ended.Accepted &&
                game.State.Phase == TurnPhase.Discard &&
                game.PendingDecision is
                {
                    Kind: DecisionKind.SkipDiscardPolicy,
                    PlayerSeat: 0,
                    Choices.Count: 2
                } kejiPrompt &&
                kejiPrompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "discard-policy-use") &&
                kejiPrompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "discard-policy-skip"),
            ended.Error?.Message ?? "A Slash-free Lu Meng play phase must publish both Keji choices.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var skippedBranch = GameReplay.Restore(pausedCheckpoint, registry);
        Require(skippedBranch.PendingDecision?.Kind == DecisionKind.SkipDiscardPolicy &&
                SnapshotJson.Serialize(skippedBranch.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "A paused Keji choice must restore exactly from its command checkpoint.");

        var beforeForged = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var forged = game.Submit(new AnswerPromptCommand(
            0,
            game.PendingDecision!.PromptId,
            new ChoiceId("keji.forged"),
            game.Revision));
        Require(!forged.Accepted &&
                forged.Error?.Code == CommandErrorCode.InvalidChoice &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == beforeForged,
            "A forged Keji choice must be rejected atomically.");

        var usePrompt = game.PendingDecision!;
        var used = game.Submit(new AnswerPromptCommand(
            0,
            usePrompt.PromptId,
            usePrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "discard-policy-use").Id,
            game.Revision));
        var handAfterUse = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(used.Accepted &&
                game.PendingDecision is null &&
                game.State.Phase == TurnPhase.NotStarted &&
                handAfterUse.HandCount == handBeforeDiscard.HandCount &&
                game.Events.Select(item => item.Payload).OfType<ProgramCardPolicyResolvedEvent>().Any(resolved =>
                    resolved.OwnerSeat == 0 &&
                    resolved.SkillId == "classic:keji" &&
                    resolved.Applied),
            used.Error?.Message ?? "Using Keji must skip discard, retain the full hand and end the turn.");
        var usedReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(usedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(usedReplay).SequenceEqual(EventSignatures(game)),
            "The completed Keji branch must replay exactly.");

        var skipPrompt = skippedBranch.PendingDecision!;
        var skipped = skippedBranch.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "discard-policy-skip").Id,
            skippedBranch.Revision));
        Require(skipped.Accepted &&
                skippedBranch.State.Phase == TurnPhase.Discard &&
                skippedBranch.PendingDecision is null,
            skipped.Error?.Message ?? "Skipping Keji must continue the ordinary discard phase.");
        var discarded = skippedBranch.Submit(new AdvanceOneStepCommand(skippedBranch.Revision));
        var handAfterSkip = skippedBranch.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0);
        Require(discarded.Accepted &&
                handAfterSkip.HandCount == handAfterSkip.Hp &&
                skippedBranch.Events.Select(item => item.Payload).OfType<ProgramCardPolicyResolvedEvent>().Any(resolved =>
                    resolved.SkillId == "classic:keji" && !resolved.Applied),
            discarded.Error?.Message ?? "Skipping Keji must retain the ordinary hand-limit discard.");

        var slashGame = FindLuMengSlashFixture(registry);
        var slashAction = slashGame.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash);
        var slashPlayed = slashGame.Submit(new PlayCardCommand(
            0,
            slashAction.CardId!.Value,
            slashAction.TargetSeats,
            slashGame.Revision,
            slashGame.PendingDecision!.PromptId,
            slashAction.PlayedCardKind));
        Require(slashPlayed.Accepted, slashPlayed.Error?.Message ?? "Lu Meng's direct Slash was rejected.");
        var slashResolved = slashGame.Submit(new AdvanceCommand(slashGame.Revision));
        Require(slashResolved.Accepted && slashGame.PendingDecision?.Kind == DecisionKind.PlayCard,
            slashResolved.Error?.Message ?? "Lu Meng's direct Slash did not return to play.");
        var afterSlash = slashGame.Submit(new EndPlayPhaseCommand(
            0,
            slashGame.Revision,
            slashGame.PendingDecision!.PromptId));
        Require(afterSlash.Accepted &&
                slashGame.State.Phase == TurnPhase.Discard &&
                slashGame.PendingDecision?.Kind != DecisionKind.SkipDiscardPolicy,
            "Using a Slash during the play phase must suppress Keji.");

    }

    public static void FormalTuxiFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = SelectGeneral(registry, "classic:zhang-liao", GameCheckpoint.CurrentRulesVersion);
        var selected = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(selected.GeneralId == "classic:zhang-liao" &&
                selected.MaxHp == 5 &&
                selected.Hp == 5 &&
                selected.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:tuxi"]) &&
                selected.Skills!.Single().ContentId == "standard:none",
            "Classic Zhang Liao must combine base 4 HP, the Lord bonus and formal Tuxi.");

        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted && game.State.Phase == TurnPhase.Draw &&
                game.PendingDecision is
                {
                    Kind: DecisionKind.ProgramTrigger,
                    PlayerSeat: 0,
                    IsPrivate: true,
                    SkillPrompt.SkillId: "classic:tuxi"
                } activation &&
                activation.Choices.Select(choice => choice.Parameters.GetValueOrDefault("program-action"))
                    .OrderBy(action => action, StringComparer.Ordinal)
                    .SequenceEqual(["activate", "skip"]),
            advanced.Error?.Message ?? "Classic Zhang Liao must publish the generic Tuxi activation choice.");
        Require(game.CreateSnapshot(1).PendingDecision is null,
            "Another seat must not receive Zhang Liao's private Tuxi activation.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var skippedBranch = GameReplay.Restore(pausedCheckpoint, registry);
        Require(skippedBranch.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: "classic:tuxi" } &&
                SnapshotJson.Serialize(skippedBranch.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "A paused Tuxi activation must restore exactly from its command checkpoint.");

        var beforeForged = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var forged = game.Submit(new AnswerPromptCommand(
            0,
            game.PendingDecision!.PromptId,
            new ChoiceId("program-trigger.forged"),
            game.Revision));
        Require(!forged.Accepted &&
                forged.Error?.Code == CommandErrorCode.InvalidChoice &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == beforeForged,
            "A forged Tuxi choice must be rejected atomically.");

        var activationPrompt = game.PendingDecision!;
        var activated = game.Submit(new AnswerPromptCommand(
            0,
            activationPrompt.PromptId,
            activationPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate").Id,
            game.Revision));
        var targetPrompt = game.PendingDecision;
        var eligibleTargetCount = game.CreateSnapshot(0, revealAll: true).Players.Count(player =>
            player.Seat != 0 && player.IsAlive && player.HandCount > 0);
        var expectedChoiceCount = eligibleTargetCount + eligibleTargetCount * (eligibleTargetCount - 1) / 2;
        Require(activated.Accepted && targetPrompt is
                {
                    Kind: DecisionKind.ProgramTrigger,
                    PlayerSeat: 0,
                    IsPrivate: true,
                    SkillPrompt.SkillId: "classic:tuxi"
                } &&
                targetPrompt.ValidTargetSeats.Count == eligibleTargetCount &&
                targetPrompt.Choices.Count == expectedChoiceCount &&
                targetPrompt.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "select-targets" &&
                    choice.Targets.Count is 1 or 2),
            activated.Error?.Message ?? "Activated Tuxi must publish every legal one- or two-target combination.");
        Require(game.CreateSnapshot(1).PendingDecision is null,
            "Another seat must not receive Zhang Liao's private Tuxi target combinations.");

        var targetCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restoredTargetChoice = GameReplay.Restore(targetCheckpoint, registry);
        Require(SnapshotJson.Serialize(restoredTargetChoice.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                restoredTargetChoice.PendingDecision?.Choices.Select(choice => choice.Id)
                    .SequenceEqual(targetPrompt!.Choices.Select(choice => choice.Id)) == true,
            "A paused generic Tuxi target choice must restore exactly.");

        var usePrompt = targetPrompt!;
        var useChoice = usePrompt.Choices.First(choice => choice.Targets.Count == 2);
        var beforeUse = game.CreateSnapshot(0, revealAll: true);
        var sourceHandBefore = beforeUse.Players.Single(player => player.Seat == 0).Hand
            .Select(card => card.Id)
            .ToHashSet();
        var targetHandsBefore = useChoice.Targets.ToDictionary(
            seat => seat,
            seat => beforeUse.Players.Single(player => player.Seat == seat).Hand
                .Select(card => card.Id)
                .ToHashSet());
        var used = game.Submit(new AnswerPromptCommand(
            0,
            usePrompt.PromptId,
            useChoice.Id,
            game.Revision));
        var afterUse = game.CreateSnapshot(0, revealAll: true);
        var sourceAfter = afterUse.Players.Single(player => player.Seat == 0);
        var gainedIds = sourceAfter.Hand.Select(card => card.Id).Where(id => !sourceHandBefore.Contains(id)).ToArray();
        Require(used.Accepted &&
                game.PendingDecision is null &&
                game.State.Phase == TurnPhase.Play &&
                gainedIds.Length == 2 &&
                useChoice.Targets.All(seat =>
                    afterUse.Players.Single(player => player.Seat == seat).HandCount ==
                    targetHandsBefore[seat].Count - 1) &&
                useChoice.Targets.All(seat =>
                    targetHandsBefore[seat].Except(
                        afterUse.Players.Single(player => player.Seat == seat).Hand.Select(card => card.Id)).Count() == 1) &&
                game.Events.Select(item => item.Payload).OfType<ProgramRandomHandCardsTakenEvent>().Any(resolved =>
                    resolved.OwnerSeat == 0 &&
                    resolved.SkillId == "classic:tuxi" &&
                    resolved.CardCount == 2 &&
                    resolved.TargetSeats.SequenceEqual(useChoice.Targets)) &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(resolved =>
                    resolved.SkillId == "classic:tuxi" && resolved.Activated && resolved.Completed) &&
                gainedIds.All(cardId => game.CardMovements.Count(movement =>
                    movement.CardId == cardId &&
                    movement.Reason.Value ==
                    "skill-program.classic:tuxi.TakeRandomHandCardFromSelectedTargets") == 2),
            used.Error?.Message ?? "Using Tuxi must replace drawing with one hidden hand card from each selected target.");
        var usedReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(usedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(usedReplay).SequenceEqual(EventSignatures(game)),
            "The completed Tuxi branch must replay exactly.");

        var skipPrompt = skippedBranch.PendingDecision!;
        var sourceBeforeSkip = skippedBranch.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0).HandCount;
        var skipped = skippedBranch.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "skip").Id,
            skippedBranch.Revision));
        Require(skipped.Accepted &&
                skippedBranch.PendingDecision is null &&
                skippedBranch.State.Phase == TurnPhase.Play &&
                skippedBranch.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount ==
                sourceBeforeSkip + 2 &&
                skippedBranch.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(resolved =>
                    resolved.SkillId == "classic:tuxi" && !resolved.Activated && !resolved.Completed),
            skipped.Error?.Message ?? "Skipping Tuxi must preserve the ordinary two-card draw.");


    }


    public static void ProgramLuoyiFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(registry.Skills["classic:luoyi"] is
                {
                    Program: not null
                } current &&
                current.Program!.MinimumRulesVersion == 171,
            "Current classic Luoyi must publish its draw adjustment program.");

        var game = SelectGeneral(registry, "classic:xu-chu", GameCheckpoint.CurrentRulesVersion);
        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted && game.PendingDecision is
                {
                    Kind: DecisionKind.ProgramTrigger,
                    PlayerSeat: 0,
                    IsPrivate: true,
                    SkillPrompt.SkillId: "classic:luoyi"
                } prompt &&
                prompt.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "activate") == 1 &&
                prompt.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "skip") == 1 &&
                game.CreateSnapshot(1).PendingDecision is null,
            advanced.Error?.Message ?? "Current Luoyi must publish a private generic activation choice.");

        var paused = RoundTrip(game.CreateCheckpoint());
        var skipped = GameReplay.Restore(paused, registry);
        var handBeforeUse = game.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0).HandCount;
        var usePrompt = game.PendingDecision!;
        var used = game.Submit(new AnswerPromptCommand(
            0,
            usePrompt.PromptId,
            usePrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate").Id,
            game.Revision));
        Require(used.Accepted && game.State.Phase == TurnPhase.Play && game.PendingDecision is null &&
                game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount ==
                handBeforeUse + 1 &&
                game.Events.Select(item => item.Payload).OfType<ProgramNormalDrawAdjustedEvent>().Any(item =>
                    item.SkillId == "classic:luoyi" && item.Adjustment == -1 && item.TotalAdjustment == -1) &&
                game.Events.Select(item => item.Payload).OfType<CardDamageModifierGrantedEvent>().Any(item =>
                    item.Modifier.Source.SkillId == "classic:luoyi" &&
                    item.Modifier.Amount == 1 &&
                    item.Modifier.CardKinds.SequenceEqual(
                        [CardKind.Slash, CardKind.Duel, CardKind.FireSlash, CardKind.ThunderSlash])),
            used.Error?.Message ?? "Program Luoyi must reduce only the normal draw and grant its turn modifier.");

        var activatedReplay = GameReplay.Restore(paused, registry);
        var replayPrompt = activatedReplay.PendingDecision!;
        var replayed = activatedReplay.Submit(new AnswerPromptCommand(
            0,
            replayPrompt.PromptId,
            replayPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate").Id,
            activatedReplay.Revision));
        Require(replayed.Accepted &&
                SnapshotJson.Serialize(activatedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventTrace(activatedReplay).SequenceEqual(EventTrace(game)),
            replayed.Error?.Message ?? "Activated program Luoyi must replay exactly from its private prompt.");

        var handBeforeSkip = skipped.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0).HandCount;
        var skipPrompt = skipped.PendingDecision!;
        var skip = skipped.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "skip").Id,
            skipped.Revision));
        Require(skip.Accepted && skipped.State.Phase == TurnPhase.Play &&
                skipped.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount ==
                handBeforeSkip + 2 &&
                skipped.Events.Select(item => item.Payload).All(item =>
                    item is not CardDamageModifierGrantedEvent),
            skip.Error?.Message ?? "Skipping program Luoyi must keep the normal draw and grant no modifier.");

        var (slashGame, slashAction, slashTargetHp) = FindXuChuDirectAttackFixture(
            registry,
            LegalActionKind.Slash,
            target => target.Hand.All(card => card.Kind != CardKind.Dodge) &&
                      target.Equipment.Count == 0 &&
                      target.Skills?.All(skill => skill.ContentId is not
                          ("classic:yizhong" or "classic:zhenlie" or "classic:liuli" or "classic:renxin" or
                           "classic:qingguo" or "classic:longdan" or "classic:bazhen" or "classic:hujia") &&
                          skill.ContentId is not ("classic:qingguo" or "classic:longdan")) != false);
        var slashTargetSeat = slashAction.TargetSeat!.Value;
        var slashPlayed = slashGame.Submit(new PlayCardCommand(
            0,
            slashAction.CardId!.Value,
            slashAction.TargetSeats,
            slashGame.Revision,
            slashGame.PendingDecision!.PromptId,
            slashAction.PlayedCardKind));
        Require(slashPlayed.Accepted &&
                slashGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == slashTargetSeat).Hp ==
                slashTargetHp - 2 &&
                slashGame.Events.Select(item => item.Payload).OfType<ProgramCardDamageModifiedEvent>().Any(item =>
                    item.Source.SkillId == "classic:luoyi" && item.Source.OwnerSeat == 0 &&
                    item.TargetSeat == slashTargetSeat && item.BaseAmount == 1 && item.ModifiedAmount == 2),
            slashPlayed.Error?.Message ??
            $"Program Luoyi must increase direct Slash damage (targetHp={slashTargetHp}, " +
            $"after={slashGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == slashTargetSeat).Hp}, " +
            $"target={slashGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == slashTargetSeat).GeneralId}, " +
            $"skills={string.Join(',', slashGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == slashTargetSeat).Skills?.Select(skill => skill.ContentId ?? skill.Name) ?? [])}, " +
            $"pending={slashGame.PendingDecision?.Kind.ToString() ?? "none"}, " +
            $"modifierEvents={slashGame.Events.Select(item => item.Payload).OfType<ProgramCardDamageModifiedEvent>().Count()}, " +
            $"damage={slashGame.Events.Select(item => item.Payload).OfType<DamageRequestedEvent>().LastOrDefault(item => item.TargetSeat == slashTargetSeat)?.Amount.ToString() ?? "none"}).");

        var (reverseGame, reverseTargetSeat, reverseResolutionId, xuChuHpBefore) =
            FindXuChuReverseDuelFixture(registry);
        var reversePrompt = reverseGame.PendingDecision!;
        var declined = reverseGame.Submit(new AnswerPromptCommand(
            0,
            reversePrompt.PromptId,
            reversePrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("response") == "take-damage").Id,
            reverseGame.Revision));
        Require(declined.Accepted &&
                reverseGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hp ==
                xuChuHpBefore - 1 &&
                reverseGame.Events.Select(item => item.Payload).OfType<ProgramCardDamageModifiedEvent>()
                    .All(item => item.ResolutionId != reverseResolutionId) &&
                reverseTargetSeat != 0,
            declined.Error?.Message ??
            "Program Luoyi must not increase Duel damage when the opponent becomes the actual source.");
    }

    public static void FormalQiangxiFlow()
    {
        var registry = CreatePreProgramClassicRegistry();
        var (game, weaponAction, targetSeat, weaponCardId) = FindDianWeiQiangxiFixture(registry, requireWeapon: true);
        var hpAction = game.GetHumanLegalActions().Single(action =>
            action.Kind == LegalActionKind.UseProgramSkill &&
            action.ProgramSkillId == "classic:qiangxi" &&
            action.ProgramActivationId == "lose-hp-and-damage");
        var before = game.CreateSnapshot(0, revealAll: true);
        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var badCard = before.Players[0].Hand.FirstOrDefault(card =>
            !weaponAction.SelectableCardIds.Contains(card.Id));
        if (badCard is not null)
        {
            var state = SnapshotJson.Serialize(before);
            var rejected = game.Submit(new UseProgramSkillCommand(
                0, "classic:qiangxi", "discard-weapon-and-damage",
                [badCard.Id], [targetSeat], game.Revision, game.PendingDecision!.PromptId));
            Require(!rejected.Accepted &&
                    SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == state,
                "Qiangxi must reject a non-weapon payment without changing state.");
        }
        var hpResult = game.Submit(new UseProgramSkillCommand(
            0, "classic:qiangxi", hpAction.ProgramActivationId!, [], [targetSeat],
            game.Revision, game.PendingDecision!.PromptId));
        Require(hpResult.Accepted, hpResult.Error?.Message ?? "HP-cost Qiangxi was rejected.");
        var hpAfter = game.CreateSnapshot(0, revealAll: true);
        Require(hpAfter.Players[0].Hp == before.Players[0].Hp - 1 &&
                hpAfter.Players[targetSeat].Hp == before.Players[targetSeat].Hp - 1 &&
                game.Events.Select(item => item.Payload).OfType<ProgramSkillHpLostEvent>().Any(item =>
                    item.SkillId == "classic:qiangxi" && item.TargetSeat == 0 && item.Amount == 1) &&
                game.Events.Select(item => item.Payload).OfType<DamageRequestedEvent>().Any(item =>
                    item.SourceSeat == 0 && item.TargetSeat == targetSeat && item.SourceCard is null) &&
                game.GetHumanLegalActions().All(action => action.ProgramSkillId != "classic:qiangxi"),
            "HP-cost Qiangxi must deal cardless damage and share its activation limit.");
        Require(SnapshotJson.Serialize(GameReplay.Restore(game.CreateCheckpoint(), registry)
                    .CreateSnapshot(0, revealAll: true)) == SnapshotJson.Serialize(hpAfter),
            "Resolved Qiangxi must replay the same state.");

        var weaponGame = GameReplay.Restore(checkpoint, registry);
        var weaponBefore = weaponGame.CreateSnapshot(0, revealAll: true);
        var weaponResult = weaponGame.Submit(new UseProgramSkillCommand(
            0, "classic:qiangxi", weaponAction.ProgramActivationId!, [weaponCardId], [targetSeat],
            weaponGame.Revision, weaponGame.PendingDecision!.PromptId));
        Require(weaponResult.Accepted, weaponResult.Error?.Message ?? "Weapon-cost Qiangxi was rejected.");
        var weaponAfter = weaponGame.CreateSnapshot(0, revealAll: true);
        Require(weaponAfter.Players[0].Hp == weaponBefore.Players[0].Hp &&
                weaponAfter.Players[targetSeat].Hp == weaponBefore.Players[targetSeat].Hp - 1 &&
                weaponGame.CardMovements.Any(move =>
                    move.CardId == weaponCardId && move.To == CardLocation.DiscardPile) &&
                weaponGame.GetHumanLegalActions().All(action => action.ProgramSkillId != "classic:qiangxi"),
            "Weapon-cost Qiangxi must spend the exact weapon and share the one-use limit.");
    }
    public static void FormalDuanliangFlow()
    {
        var registry = CreatePreProgramClassicRegistry();
        var (game, physicalCardId, targetSeat, distanceTwoSeat) =
            FindXuHuangDuanliangFixture(registry);
        var before = game.CreateSnapshot(0, revealAll: true);
        var source = before.Players.Single(player => player.Seat == 0);
        var physicalCard = source.Equipment.Single(card => card.Id == physicalCardId);
        var convertedActions = game.GetHumanLegalActions()
            .Where(action =>
                action.Kind == LegalActionKind.SupplyShortage &&
                action.CardId == physicalCardId &&
                action.PlayedCardKind == CardKind.SupplyShortage)
            .ToArray();
        var targetAction = convertedActions.Single(action => action.TargetSeat == targetSeat);
        Require(physicalCard.Suit is Suit.Spade or Suit.Club &&
                EquipmentCatalog.IsEquipment(physicalCard.Kind) &&
                game.GetCombatDistance(0, distanceTwoSeat) == 2 &&
                convertedActions.Any(action => action.TargetSeat == distanceTwoSeat),
            "Duanliang must publish an equipped black card as Supply Shortage against distance-two targets.");

        var stateBeforeForged = SnapshotJson.Serialize(before);
        var forged = game.Submit(new PlayCardCommand(
            0,
            physicalCardId,
            targetAction.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            physicalCard.Kind));
        Require(!forged.Accepted &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == stateBeforeForged,
            "Duanliang must reject a forged physical effective kind atomically.");

        var aiActions = convertedActions
            .Where(action => action.TargetSeat == distanceTwoSeat)
            .ToArray();
        var aiChoice = new SimpleAiBrain(0, seed: 33).ChoosePlay(before, aiActions, thoughtSequence: 1);
        Require(aiChoice.Action.CardId == physicalCardId &&
                aiChoice.Action.PlayedCardKind == CardKind.SupplyShortage &&
                aiChoice.Thought.Candidates.All(candidate =>
                    candidate.Action.CardId == physicalCardId),
            "Duanliang AI must choose only from its published physical-card conversions.");

        var used = game.Submit(new PlayCardCommand(
            0,
            physicalCardId,
            targetAction.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            CardKind.SupplyShortage)
        { ConversionSource = targetAction.ConversionSource });
        var returnedToPlay = game.Submit(new AdvanceCommand(game.Revision));
        var placed = game.CreateSnapshot(0, revealAll: true);
        Require(used.Accepted &&
                returnedToPlay.Accepted &&
                game.PendingDecision?.Kind == DecisionKind.PlayCard &&
                placed.Players.Single(player => player.Seat == targetSeat).Judgment.Any(card =>
                    card.Id == physicalCardId && card.Kind == CardKind.SupplyShortage) &&
                game.CreateCardZoneDiagnostics().Any(card =>
                    card.CardId == physicalCardId &&
                    card.CardKind == physicalCard.Kind &&
                    card.Location == CardLocation.Judgment(targetSeat)) &&
                game.CardMovements.Any(movement =>
                    movement.CardId == physicalCardId &&
                    movement.From == CardLocation.Equipment(0) &&
                    movement.To == CardLocation.Processing) &&
                game.CardMovements.Any(movement =>
                    movement.CardId == physicalCardId &&
                    movement.From == CardLocation.Processing &&
                    movement.To == CardLocation.Judgment(targetSeat)) &&
                game.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>().Any(declared =>
                    declared.CardId == physicalCardId &&
                    declared.CardKind == CardKind.SupplyShortage) &&
                game.Events.Select(item => item.Payload).OfType<DelayedCardPlacedEvent>().Any(delayed =>
                    delayed.CardId == physicalCardId &&
                    delayed.CardKind == CardKind.SupplyShortage &&
                    delayed.TargetSeat == targetSeat),
            used.Error?.Message ?? returnedToPlay.Error?.Message ??
            "Duanliang must retain the physical equipment identity while publishing persistent Supply Shortage semantics.");

        var placedReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(placedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(placed) &&
                EventSignatures(placedReplay).SequenceEqual(EventSignatures(game)),
            "A placed Duanliang conversion must restore with the same effective judgment-card identity.");

        var ended = game.Submit(new EndPlayPhaseCommand(
            0,
            game.Revision,
            game.PendingDecision!.PromptId));
        var resolved = ended.Accepted
            ? AdvanceUntilDelayedCardResolves(game, physicalCardId)
            : null;
        Require(ended.Accepted &&
                resolved is { CardKind: CardKind.SupplyShortage, SkippedDrawPhase: true } &&
                game.CreateCardZoneDiagnostics().Any(card =>
                    card.CardId == physicalCardId &&
                    card.CardKind == physicalCard.Kind &&
                    card.Location == CardLocation.DiscardPile) &&
                game.CreateSnapshot(0, revealAll: true).Players
                    .Single(player => player.Seat == targetSeat).Judgment
                    .All(card => card.Id != physicalCardId),
            ended.Error?.Message ??
            "The converted Supply Shortage must resolve as a draw-skip judgment and discard its original physical card.");

        var resolvedReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(resolvedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(resolvedReplay).SequenceEqual(EventSignatures(game)),
            "A resolved Duanliang delayed card must replay exactly.");

    }

    public static void FormalLuoshenAndQingguoFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = FindZhenJiFirstBlackLuoshenFixture(registry);
        var owner = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(owner.GeneralId == "classic:zhen-ji" &&
                owner.MaxHp == 4 &&
                owner.Hp == 4 &&
                owner.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:luoshen", "classic:qingguo"]) &&
                game.PendingDecision is
                {
                    Kind: DecisionKind.ProgramRepeatJudgment,
                    PlayerSeat: 0,
                    IsPrivate: true,
                    Choices.Count: 2
                } repeatPrompt &&
                repeatPrompt.Prompt.Contains("再次", StringComparison.Ordinal) &&
                game.CreateSnapshot(1).PendingDecision is null,
            $"Classic Zhen Ji must publish a private repeated Luoshen choice after claiming a black judgment: general={owner.GeneralId}, hp={owner.Hp}/{owner.MaxHp}, skills={string.Join(',', owner.Skills!.Select(skill => skill.ContentId))}, prompt={game.PendingDecision?.Kind}/{game.PendingDecision?.PlayerSeat}/{game.PendingDecision?.Prompt}, opponentPrompt={game.CreateSnapshot(1).PendingDecision?.Kind}.");

        var blackJudgment = game.Events.Select(item => item.Payload)
            .OfType<JudgmentResolvedEvent>()
            .Last(item => item.Reason == JudgmentReasons.Luoshen);
        Require(blackJudgment is { CardId: not null, Suit: Suit.Spade or Suit.Club, Succeeded: true } &&
                game.CardMovements.Any(movement =>
                    movement.CardId == blackJudgment.CardId &&
                    movement.From == CardLocation.Judgment(0) &&
                    movement.To == CardLocation.Hand(0) &&
                    movement.Reason == new CardMoveReason("skill-program.classic:luoshen.repeatJudgment")) &&
                game.Events.Select(item => item.Payload).OfType<ProgramJudgmentCardClaimedEvent>().Any(claimed =>
                    claimed.JudgmentFrameId == blackJudgment.ResolutionId &&
                    claimed.SkillId == "classic:luoshen" &&
                    claimed.CardId == blackJudgment.CardId),
            "A black Luoshen judgment must enter Zhen Ji's hand through an auditable claim movement and event.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restored = GameReplay.Restore(pausedCheckpoint, registry);
        Require(restored.PendingDecision?.Kind == DecisionKind.ProgramRepeatJudgment &&
                SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(game)),
            "A repeated Luoshen prompt must restore with the same private choice and claimed black card.");

        ContinueLuoshenUntilPlay(game);
        var finalJudgment = game.Events.Select(item => item.Payload)
            .OfType<JudgmentResolvedEvent>()
            .Last(item => item.Reason == JudgmentReasons.Luoshen);
        Require(finalJudgment is { CardId: not null, Suit: Suit.Heart or Suit.Diamond, Succeeded: false } &&
                game.State.Phase == TurnPhase.Play &&
                game.PendingDecision?.Kind == DecisionKind.PlayCard &&
                game.CardMovements.Any(movement =>
                    movement.CardId == finalJudgment.CardId &&
                    movement.From == CardLocation.Judgment(0) &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.JudgmentFinish),
            "A red Luoshen judgment must stop the chain, discard the red card and continue to the play phase.");
        var completedReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(completedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(completedReplay).SequenceEqual(EventSignatures(game)),
            "The completed black-to-red Luoshen chain must replay exactly.");

        var skipped = GameReplay.Restore(pausedCheckpoint, registry);
        var skippedPrompt = skipped.PendingDecision!;
        var skippedResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skippedPrompt.PromptId,
            skippedPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "luoshen-skip").Id,
            skipped.Revision));
        Require(skippedResult.Accepted &&
                skipped.State.Phase == TurnPhase.Play &&
                skipped.Events.Select(item => item.Payload).OfType<LuoshenChoiceResolvedEvent>().Any(resolved =>
                    resolved.SourceSeat == 0 && resolved.IsRepeat && !resolved.Used),
            skippedResult.Error?.Message ?? "Stopping after a black Luoshen result must continue the turn without another judgment.");

    }

    public static void FormalJizhiAndQicaiFlow()
    {
        var registry = CreatePreProgramClassicRegistry();
        var (game, action) = FindHuangYueyingOrdinaryTrickFixture(
            registry,
            GameCheckpoint.CurrentRulesVersion,
            requireNullification: false);
        var owner = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(owner.GeneralId == "classic:huang-yueying" &&
                owner.MaxHp == 4 &&
                owner.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:jizhi", "classic:qicai"]),
            "Classic Huang Yueying must expose formal Shu, Lord-adjusted 4 HP, Jizhi and Qicai.");

        var played = game.Submit(new PlayCardCommand(
            0,
            action.CardId!.Value,
            action.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            action.PlayedCardKind,
            action.TargetCardId)
        {
            ConversionSource = action.ConversionSource,
            AdditionalConversionSources = action.AdditionalConversionSources,
        });
        var prompt = game.PendingDecision;
        Require(played.Accepted &&
                prompt is
                {
                    Kind: DecisionKind.ProgramTrigger,
                    PlayerSeat: 0,
                    IsPrivate: true,
                    Choices.Count: 2
                } &&
                prompt.IncomingCard == (action.PlayedCardKind ?? owner.Hand.Single(card => card.Id == action.CardId).Kind) &&
                game.CreateSnapshot(1).PendingDecision is null &&
                 game.ResolutionStack.OfType<CardUseFrame>().Any(frame => frame.Step == ResolutionFrameStep.Declared),
            played.Error?.Message ??
            "Using an ordinary trick must pause at a private Jizhi choice before the Nullification window.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restored = GameReplay.Restore(pausedCheckpoint, registry);
        Require(restored.PendingDecision?.Kind == DecisionKind.ProgramTrigger &&
                SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(game)),
            "A paused Jizhi choice must restore before the ordinary trick enters its Nullification window.");

        var useChoice = prompt!.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        var used = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            useChoice.Id,
            game.Revision));
        Require(used.Accepted &&
                 game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(resolved =>
                     resolved.OwnerSeat == 0 && resolved.SkillId == "classic:jizhi" &&
                     resolved.Activated && resolved.Completed) &&
                 game.CardMovements.Count(movement =>
                     movement.To == CardLocation.Hand(0) &&
                     movement.Reason.Value == "skill-program.classic:jizhi.Draw") == 1 &&
                game.PendingDecision?.Kind != DecisionKind.ProgramTrigger,
            used.Error?.Message ?? "Accepting Jizhi must draw exactly one card and resume the original trick.");
        var completedReplay = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(completedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(completedReplay).SequenceEqual(EventSignatures(game)),
            "The resumed ordinary-trick/Jizhi flow must replay exactly.");

        var skipped = GameReplay.Restore(pausedCheckpoint, registry);
        var skippedPrompt = skipped.PendingDecision!;
        var skippedResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skippedPrompt.PromptId,
            skippedPrompt.Choices.Single(choice =>
                 choice.Parameters.GetValueOrDefault("program-action") == "skip").Id,
            skipped.Revision));
        Require(skippedResult.Accepted &&
                 skipped.CardMovements.All(movement => movement.Reason.Value != "skill-program.classic:jizhi.Draw") &&
                 skipped.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(resolved =>
                     resolved.OwnerSeat == 0 && resolved.SkillId == "classic:jizhi" &&
                     !resolved.Activated && resolved.Completed),
            skippedResult.Error?.Message ?? "Skipping Jizhi must resume the trick without drawing a card.");

        var nullificationGame = FindHuangYueyingNullificationJizhiFixture(registry);
        var nullificationPrompt = nullificationGame.PendingDecision!;
        Require(nullificationPrompt.Kind == DecisionKind.ProgramTrigger &&
                nullificationPrompt.IncomingCard == CardKind.Nullification &&
                nullificationGame.CardMovements.Any(movement =>
                    movement.CardKind == CardKind.Nullification &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.NullificationFinished) &&
                 nullificationGame.ResolutionStack.OfType<NullificationWindowFrame>().Any(),
            "Using Nullification must open Jizhi while retaining the parent Nullification cursor.");
        var nullificationCheckpoint = nullificationGame.CreateCheckpoint();
        var nullificationUsed = nullificationGame.Submit(new AnswerPromptCommand(
            0,
            nullificationPrompt.PromptId,
            nullificationPrompt.Choices.Single(choice =>
                 choice.Parameters.GetValueOrDefault("program-action") == "activate").Id,
            nullificationGame.Revision));
        Require(nullificationUsed.Accepted &&
                 nullificationGame.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(resolved =>
                     resolved.OwnerSeat == 0 && resolved.SkillId == "classic:jizhi" &&
                     resolved.Activated && resolved.Completed),
            nullificationUsed.Error?.Message ??
            "Jizhi after Nullification must draw and resume the exact counter-chain cursor.");
        var nullificationReplay = GameReplay.Restore(nullificationGame.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(nullificationReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(nullificationGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(nullificationReplay).SequenceEqual(EventSignatures(nullificationGame)) &&
                GameReplay.Restore(nullificationCheckpoint, registry).PendingDecision?.Kind == DecisionKind.ProgramTrigger,
            "Paused and resumed Nullification-triggered Jizhi must replay exactly.");

    }

    public static void FormalTieqiAndMashuFlow()
    {
        var registry = CreatePreProgramClassicRegistry();
        var red = FindMaChaoTieqiFixture(registry, requireRedJudgment: true);
        var owner = red.Game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(owner.GeneralId == "classic:ma-chao" &&
                owner.MaxHp == 5 &&
                owner.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:tieqi", "classic:mashu"]),
            "Classic Ma Chao must expose formal Shu, Lord-adjusted 5 HP, Tieqi and Mashu.");
        Require(red.Prompt is
        {
            Kind: DecisionKind.ProgramTrigger,
            PlayerSeat: 0,
            IsPrivate: true,
            Choices.Count: 2
        } &&
                red.Prompt.TargetSeat == red.TargetSeat &&
                red.Game.CreateSnapshot(red.TargetSeat).PendingDecision is null &&
                red.Game.ResolutionStack.OfType<CardUseFrame>().Any(frame =>
                    frame.Step == ResolutionFrameStep.Declared),
            "Using Slash must pause at a private Tieqi choice before any Dodge response.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(red.Game.CreateCheckpoint()));
        var pausedReplay = GameReplay.Restore(pausedCheckpoint, registry);
        Require(pausedReplay.PendingDecision?.Kind == DecisionKind.ProgramTrigger &&
                SnapshotJson.Serialize(pausedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(red.Game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(pausedReplay).SequenceEqual(EventSignatures(red.Game)),
            "A paused Tieqi choice must restore before its judgment and Slash response.");

        var redEventCount = red.Game.Events.Count;
        var used = red.Game.Submit(new AnswerPromptCommand(
            0,
            red.Prompt.PromptId,
            red.Prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate").Id,
            red.Game.Revision));
        var redEvents = red.Game.Events.Skip(redEventCount).Select(item => item.Payload).ToArray();
        Require(used.Accepted &&
                redEvents.OfType<ProgramBindingResolvedEvent>().Any(resolved =>
                    resolved.OwnerSeat == 0 && resolved.SkillId == "classic:tieqi" &&
                    resolved.Activated && resolved.Completed) &&
                redEvents.OfType<JudgmentResolvedEvent>().Any(resolved =>
                    resolved.Reason == "skill.slash-response-judgment" &&
                    resolved.TargetSeat == 0 &&
                    resolved.Suit is Suit.Heart or Suit.Diamond) &&
                redEvents.OfType<ResponseRequestedEvent>().All(requested =>
                    requested.TargetSeat != red.TargetSeat || requested.RequiredCardKind != CardKind.Dodge) &&
                redEvents.OfType<DamageAppliedEvent>().Any(damage => damage.TargetSeat == red.TargetSeat),
            used.Error?.Message ??
            "A red Tieqi judgment must prohibit the target's published Dodge and continue to Slash damage.");
        var completedReplay = GameReplay.Restore(red.Game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(completedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(red.Game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(completedReplay).SequenceEqual(EventSignatures(red.Game)),
            "The completed red Tieqi Slash must replay exactly.");

        var skipped = GameReplay.Restore(pausedCheckpoint, registry);
        var skipPrompt = skipped.PendingDecision!;
        var judgmentCountBeforeSkip = skipped.Events.Select(item => item.Payload)
            .OfType<JudgmentRequestedEvent>().Count(item => item.Reason == "skill.slash-response-judgment");
        var skippedResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "skip").Id,
            skipped.Revision));
        Require(skippedResult.Accepted &&
                skipped.Events.Select(item => item.Payload).OfType<ResponseRequestedEvent>().Any(requested =>
                    requested.TargetSeat == red.TargetSeat &&
                    requested.RequiredCardKind == CardKind.Dodge) &&
                skipped.Events.Select(item => item.Payload)
                    .OfType<JudgmentRequestedEvent>().Count(item => item.Reason == "skill.slash-response-judgment") ==
                judgmentCountBeforeSkip &&
                skipped.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(resolved =>
                    resolved.OwnerSeat == 0 && resolved.SkillId == "classic:tieqi" &&
                    !resolved.Activated && resolved.Completed),
            skippedResult.Error?.Message ??
            $"Skipping Tieqi must open the ordinary Dodge response without creating a judgment " +
            $"(pending={skipped.PendingDecision?.Kind}/{skipped.PendingDecision?.PlayerSeat}, " +
            $"expected={red.TargetSeat}, judgments={skipped.Events.Select(item => item.Payload).OfType<JudgmentRequestedEvent>().Count(item => item.Reason == "skill.slash-response-judgment")}, " +
            $"choices={skipped.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Count(item => item.SkillId == "classic:tieqi")}).");

        var black = FindMaChaoTieqiFixture(registry, requireRedJudgment: false);
        var blackEventCount = black.Game.Events.Count;
        var blackUsed = black.Game.Submit(new AnswerPromptCommand(
            0,
            black.Prompt.PromptId,
            black.Prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate").Id,
            black.Game.Revision));
        var blackEvents = black.Game.Events.Skip(blackEventCount).Select(item => item.Payload).ToArray();
        Require(blackUsed.Accepted &&
                blackEvents.OfType<JudgmentResolvedEvent>().Any(resolved =>
                    resolved.Reason == "skill.slash-response-judgment" &&
                    resolved.Suit is Suit.Spade or Suit.Club) &&
                blackEvents.OfType<ResponseRequestedEvent>().Any(requested =>
                    requested.TargetSeat == black.TargetSeat &&
                    requested.RequiredCardKind == CardKind.Dodge),
            blackUsed.Error?.Message ??
            $"A black Tieqi judgment must retain the target's ordinary Dodge response " +
            $"(pending={black.Game.PendingDecision?.Kind}/{black.Game.PendingDecision?.PlayerSeat}, " +
            $"expected={black.TargetSeat}, valid={black.Game.PendingDecision?.ValidCardIds.Count}, " +
            $"judgment={string.Join(',', blackEvents.OfType<JudgmentResolvedEvent>().Where(item => item.Reason == "skill.slash-response-judgment").Select(item => item.Suit))}, " +
            $"responses={blackEvents.OfType<ResponseRequestedEvent>().Count()}).");

    }

    public static void FormalLiegongFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var eligible = FindHuangZhongLiegongFixture(
            registry,
            GameCheckpoint.CurrentRulesVersion,
            LiegongFixtureKind.EligibleByHp);
        var eligiblePrompt = eligible.Prompt ??
            throw new InvalidOperationException("Eligible Liegong did not publish its private choice.");
        var owner = eligible.Game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(owner.GeneralId == "classic:huang-zhong" &&
                owner.MaxHp == 4 &&
                owner.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:liegong"]),
            "Classic rebel Huang Zhong must expose formal Shu, 4 HP and Liegong.");
        Require(eligiblePrompt is
        {
            Kind: DecisionKind.ProgramTrigger,
            PlayerSeat: 0,
            IsPrivate: true,
            Choices.Count: 2
        } &&
                eligiblePrompt.TargetSeat == eligible.TargetSeat &&
                eligible.TargetHandCount >= eligible.SourceHp &&
                eligible.TargetHandCount > eligible.AttackRange &&
                eligible.Game.CreateSnapshot(eligible.TargetSeat).PendingDecision is null &&
                eligible.Game.ResolutionStack.OfType<CardUseFrame>().Any(frame =>
                    frame.Step == ResolutionFrameStep.Declared),
            "An HP-eligible Slash must pause at a private Liegong choice before any Dodge response.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(eligible.Game.CreateCheckpoint()));
        var pausedReplay = GameReplay.Restore(pausedCheckpoint, registry);
        Require(pausedReplay.PendingDecision?.Kind == DecisionKind.ProgramTrigger &&
                SnapshotJson.Serialize(pausedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(eligible.Game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(pausedReplay).SequenceEqual(EventSignatures(eligible.Game)),
            "A paused Liegong choice must restore before its Slash response.");

        var eventCount = eligible.Game.Events.Count;
        var used = eligible.Game.Submit(new AnswerPromptCommand(
            0,
            eligiblePrompt.PromptId,
            eligiblePrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate").Id,
            eligible.Game.Revision));
        var usedEvents = eligible.Game.Events.Skip(eventCount).Select(item => item.Payload).ToArray();
        Require(used.Accepted &&
                usedEvents.OfType<ProgramBindingResolvedEvent>().Any(resolved =>
                    resolved.OwnerSeat == 0 && resolved.SkillId == "classic:liegong" &&
                    resolved.Activated && resolved.Completed) &&
                usedEvents.OfType<ResponseRequestedEvent>().All(requested =>
                    requested.TargetSeat != eligible.TargetSeat ||
                    requested.RequiredCardKind != CardKind.Dodge) &&
                usedEvents.OfType<DamageAppliedEvent>().Any(damage =>
                    damage.TargetSeat == eligible.TargetSeat),
            used.Error?.Message ??
            $"Using eligible Liegong must prohibit the target's Dodge and continue to Slash damage " +
            $"(target={eligible.Game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == eligible.TargetSeat).GeneralId}, " +
            $"pending={eligible.Game.PendingDecision?.Kind}, events={string.Join(',', usedEvents.Select(item => item.GetType().Name))}).");
        var completedReplay = GameReplay.Restore(eligible.Game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(completedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(eligible.Game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(completedReplay).SequenceEqual(EventSignatures(eligible.Game)),
            "The completed Liegong Slash must replay exactly.");

        var skipped = GameReplay.Restore(pausedCheckpoint, registry);
        var skipPrompt = skipped.PendingDecision!;
        var skippedResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "skip").Id,
            skipped.Revision));
        Require(skippedResult.Accepted &&
                skipped.Events.Select(item => item.Payload).OfType<ResponseRequestedEvent>().Any(requested =>
                    requested.TargetSeat == eligible.TargetSeat &&
                    requested.RequiredCardKind == CardKind.Dodge) &&
                skipped.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(resolved =>
                    resolved.OwnerSeat == 0 && resolved.SkillId == "classic:liegong" &&
                    !resolved.Activated && resolved.Completed),
            skippedResult.Error?.Message ??
            "Skipping eligible Liegong must open the ordinary Dodge response.");

        var rangeEligible = FindHuangZhongLiegongFixture(
            registry,
            GameCheckpoint.CurrentRulesVersion,
            LiegongFixtureKind.EligibleByRange);
        Require(rangeEligible.Prompt?.Kind == DecisionKind.ProgramTrigger &&
                rangeEligible.TargetHandCount <= rangeEligible.AttackRange &&
                rangeEligible.TargetHandCount < rangeEligible.SourceHp,
            "A target whose hand count is within Huang Zhong's attack range must be Liegong-eligible independently of HP.");

        var ineligible = FindHuangZhongLiegongFixture(
            registry,
            GameCheckpoint.CurrentRulesVersion,
            LiegongFixtureKind.Ineligible);
        Require(ineligible.TargetHandCount < ineligible.SourceHp &&
                ineligible.TargetHandCount > ineligible.AttackRange &&
                ineligible.Game.Events.Select(item => item.Payload).OfType<ResponseRequestedEvent>().Any(requested =>
                    requested.TargetSeat == ineligible.TargetSeat &&
                    requested.RequiredCardKind == CardKind.Dodge) &&
                ineligible.Game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Count(item => item.SkillId == "classic:liegong") == 0,
            "A Slash target outside both Liegong hand-count conditions must receive the ordinary Dodge response.");

    }

    public static void FormalKuangguFlow()
    {
        // Keep this deterministic search on the roster it was authored for;
        // later generals add private response boundaries to the same seeds.
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var fixture = FindWeiYanKuangguFixture(registry);
        var game = fixture.Game;
        var events = game.Events.Skip(fixture.EventCount).Select(item => item.Payload).ToArray();
        var owner = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        var recovered = events.OfType<RecoveryAppliedEvent>().Single(recovery =>
            recovery.SourceSeat == 0 && recovery.TargetSeat == 0);
        Require(owner.GeneralId == "classic:wei-yan" &&
                owner.MaxHp == 4 &&
                owner.Hp == fixture.SourceHpBefore + 1 &&
                owner.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:kuanggu"]) &&
                fixture.Distance == 1 &&
                recovered.SourceSeat == 0 &&
                recovered.TargetSeat == 0 &&
                recovered.Amount == 1 &&
                recovered.RemainingHp == fixture.SourceHpBefore + 1 &&
                events.OfType<DamageAppliedEvent>().Any(damage =>
                    damage.SourceSeat == 0 &&
                    damage.TargetSeat == fixture.TargetSeat &&
                    damage.Amount == 1) &&
                events.OfType<RecoveryAppliedEvent>().Any(recovery =>
                    recovery.SourceSeat == 0 &&
                    recovery.TargetSeat == 0 &&
                    recovery.Amount == 1 &&
                    recovery.RemainingHp == fixture.SourceHpBefore + 1),
            "A wounded Wei Yan must recover after dealing damage to a distance-one target.");

        var damageIndex = Array.FindIndex(events, item => item is DamageAppliedEvent applied &&
            applied.TargetSeat == fixture.TargetSeat);
        var recoveryIndex = Array.FindIndex(events, item => item is RecoveryAppliedEvent recovery &&
            recovery.SourceSeat == 0 && recovery.TargetSeat == 0);
        Require(damageIndex >= 0 && recoveryIndex > damageIndex,
            "Kuanggu recovery must resolve after its damage is applied.");

        var replayed = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(replayed.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replayed).SequenceEqual(EventSignatures(game)),
            "A completed Kuanggu recovery must replay exactly.");

        var fullHealth = GameReplay.Restore(fixture.BeforeDamage, registry);
        SetPlayerHp(fullHealth, seat: 0, hp: 4);
        var fullHealthEventCount = fullHealth.Events.Count;
        var fullHealthResult = SubmitPlayAction(fullHealth, fixture.Action);
        var fullHealthEvents = fullHealth.Events.Skip(fullHealthEventCount).Select(item => item.Payload).ToArray();
        Require(fullHealthResult.Accepted &&
                fullHealth.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hp == 4 &&
                fullHealthEvents.OfType<RecoveryAppliedEvent>().All(recovery =>
                    recovery.SourceSeat != 0 || recovery.TargetSeat != 0),
            fullHealthResult.Error?.Message ?? "Full-health Wei Yan must not create a no-op Kuanggu recovery.");
    }

    public static void FormalWushuangFlow()
    {
        var registry = CreatePreProgramClassicRegistry();

        var slashFixture = FindLuBuWushuangSlashFixture(registry);
        var slashEventCount = slashFixture.Game.Events.Count;
        var slashResult = SubmitPlayAction(slashFixture.Game, slashFixture.Action);
        Require(slashResult.Accepted, slashResult.Error?.Message ?? "Lu Bu could not use the Wushuang Slash.");
        DriveAiUntil(slashFixture.Game, () => slashFixture.Game.Events.Skip(slashEventCount)
            .Select(item => item.Payload)
            .OfType<RequiredResponseProgressEvent>()
            .Count(progress => progress.RequiredCardKind == CardKind.Dodge) >= 1);
        var secondDodgePrompt = slashFixture.Game.CreateSnapshot(slashFixture.TargetSeat).PendingDecision;
        Require(secondDodgePrompt is
        {
            Kind: DecisionKind.RespondDodge,
            PlayerSeat: var responderSeat,
            RequiredCardKind: CardKind.Dodge
        } &&
                responderSeat == slashFixture.TargetSeat &&
                secondDodgePrompt.Prompt.Contains("第 2 张", StringComparison.Ordinal),
            "The first Wushuang Dodge must open a distinct second-Dodge response window.");

        var midSlashReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(slashFixture.Game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(midSlashReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(slashFixture.Game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(midSlashReplay).SequenceEqual(EventSignatures(slashFixture.Game)),
            "A Wushuang Slash must restore exactly between its first and second Dodge windows.");

        DriveAiUntil(slashFixture.Game, () => slashFixture.Game.Events.Skip(slashEventCount)
            .Select(item => item.Payload)
            .OfType<RequiredResponseProgressEvent>()
            .Count(progress => progress.RequiredCardKind == CardKind.Dodge) >= 2);
        DriveAiUntil(midSlashReplay, () => midSlashReplay.Events.Skip(slashEventCount)
            .Select(item => item.Payload)
            .OfType<RequiredResponseProgressEvent>()
            .Count(progress => progress.RequiredCardKind == CardKind.Dodge) >= 2);
        var slashEvents = slashFixture.Game.Events.Skip(slashEventCount).Select(item => item.Payload).ToArray();
        var slashProgress = slashEvents.OfType<RequiredResponseProgressEvent>()
            .Where(progress => progress.RequiredCardKind == CardKind.Dodge)
            .ToArray();
        Require(slashProgress.Select(progress => progress.ResponseCount).SequenceEqual([1, 2]) &&
                slashProgress.All(progress =>
                    progress.SkillOwnerSeat == 0 &&
                    progress.ResponderSeat == slashFixture.TargetSeat &&
                    progress.RequiredResponseCount == 2) &&
                slashEvents.OfType<CardRespondedEvent>().Count(response =>
                    response.ResponderSeat == slashFixture.TargetSeat) == 2 &&
                slashEvents.OfType<DamageAppliedEvent>().All(damage =>
                    damage.TargetSeat != slashFixture.TargetSeat),
            "A Wushuang Slash must consume two sequential Dodge responses before it is canceled.");

        Require(SnapshotJson.Serialize(midSlashReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(slashFixture.Game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(midSlashReplay).SequenceEqual(EventSignatures(slashFixture.Game)),
            "A resumed Wushuang Slash must finish identically after its second Dodge.");

        var slashReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(slashFixture.Game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(slashReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(slashFixture.Game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(slashReplay).SequenceEqual(EventSignatures(slashFixture.Game)),
            "A completed two-Dodge Wushuang Slash must replay exactly.");

        var duelFixture = FindLuBuWushuangDuelFixture(registry);
        SetPlayerHp(duelFixture.Game, duelFixture.TargetSeat, hp: 1);
        var duelEventCount = duelFixture.Game.Events.Count;
        var duelResult = SubmitPlayAction(duelFixture.Game, duelFixture.Action);
        Require(duelResult.Accepted, duelResult.Error?.Message ?? "Lu Bu could not use the Wushuang Duel.");
        DriveAiUntil(duelFixture.Game, () => duelFixture.Game.Events.Skip(duelEventCount)
            .Select(item => item.Payload)
            .OfType<DamageAppliedEvent>()
            .Any(damage => damage.TargetSeat == duelFixture.TargetSeat));
        var duelEvents = duelFixture.Game.Events.Skip(duelEventCount).Select(item => item.Payload).ToArray();
        var duelDiagnostics = string.Join(", ", duelEvents.Select(item => item switch
        {
            DuelResponseEvent response => $"duel:{response.ResponderSeat}:{response.UsedSlash}",
            RequiredResponseProgressEvent progress =>
                $"progress:{progress.ResponderSeat}:{progress.ResponseCount}/{progress.RequiredResponseCount}",
            DamageAppliedEvent damage => $"damage:{damage.SourceSeat}->{damage.TargetSeat}:{damage.Amount}",
            _ => item.GetType().Name
        }));
        Require(duelEvents.OfType<DuelResponseEvent>().Count(response =>
                    response.ResponderSeat == duelFixture.TargetSeat && response.UsedSlash) == 1 &&
                duelEvents.OfType<DuelResponseEvent>().Any(response =>
                    response.ResponderSeat == duelFixture.TargetSeat && !response.UsedSlash) &&
                duelEvents.OfType<RequiredResponseProgressEvent>().Any(progress =>
                    progress.SkillOwnerSeat == 0 &&
                    progress.ResponderSeat == duelFixture.TargetSeat &&
                    progress.IncomingCard == CardKind.Duel &&
                    progress.RequiredCardKind == CardKind.Slash &&
                    progress.ResponseCount == 1 &&
                    progress.RequiredResponseCount == 2),
            $"A Wushuang Duel opponent with one Slash must pay it and then fail the second response. {duelDiagnostics}");

    }

    public static void FormalPaoxiaoFlow()
    {
        var registry = CreatePreProgramClassicRegistry();
        var fixture = FindZhangFeiPaoxiaoFixture(registry);
        var game = fixture.Game;
        var before = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(before.GeneralId == "classic:zhang-fei" &&
                before.MaxHp == 5 &&
                before.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:paoxiao"]) &&
                before.Equipment.All(card => card.Kind != CardKind.Crossbow),
            "Classic Zhang Fei must enter the Lord fixture with formal Paoxiao and no Crossbow fallback.");

        var eventCount = game.Events.Count;
        var first = SubmitPlayAction(game, fixture.FirstAction);
        Require(first.Accepted, first.Error?.Message ?? "Classic Zhang Fei could not use his first Slash.");
        Require(TryReturnToHumanPlay(game),
            "Classic Zhang Fei did not return to the same play phase after his first Slash.");

        var secondAction = game.GetHumanLegalActions()
            .Where(action => action.Kind == LegalActionKind.Slash && action.CardId is not null)
            .OrderBy(action => action.CardId)
            .ThenBy(action => action.TargetSeat)
            .FirstOrDefault();
        Require(secondAction is not null,
            "Paoxiao must leave a second physical Slash legal in the same play phase.");
        var second = SubmitPlayAction(game, secondAction!);
        Require(second.Accepted, second.Error?.Message ?? "Paoxiao rejected Zhang Fei's second Slash.");

        var slashUses = game.Events.Skip(eventCount)
            .Select(item => item.Payload)
            .OfType<CardUsedEvent>()
            .Where(item => item.SourceSeat == 0 &&
                           item.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)
            .ToArray();
        Require(slashUses.Length == 2 &&
                slashUses.Select(item => item.CardId).Distinct().Count() == 2,
            "Formal Paoxiao must publish two distinct Slash uses in one play phase.");

        var replayed = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(replayed.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replayed).SequenceEqual(EventSignatures(game)),
            "A second in-flight Paoxiao Slash must replay exactly.");
    }

    public static void FormalLongdanFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var fixture = FindZhaoYunLongdanFixture(registry);
        var game = fixture.Game;
        var before = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        var physicalDodge = before.Hand.Single(card => card.Id == fixture.Action.CardId);
        Require(before.GeneralId == "classic:zhao-yun" &&
                before.MaxHp == 5 &&
                before.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:longdan"]) &&
                physicalDodge.Kind == CardKind.Dodge &&
                fixture.Action.PlayedCardKind == CardKind.Slash,
            "Classic Zhao Yun must publish a physical Dodge as a typed Slash through formal Longdan.");

        var eventCount = game.Events.Count;
        var used = SubmitPlayAction(game, fixture.Action);
        Require(used.Accepted, used.Error?.Message ?? "Classic Zhao Yun could not use Dodge as Slash.");
        Require(TryReturnToHumanPlay(game),
            "Classic Zhao Yun did not finish the converted Slash and return to play.");
        var playEvents = game.Events.Skip(eventCount).Select(item => item.Payload).ToArray();
        Require(playEvents.OfType<CardUseDeclaredEvent>().Any(item =>
                    item.CardId == physicalDodge.Id && item.CardKind == CardKind.Slash) &&
                playEvents.OfType<CardUsedEvent>().Any(item =>
                    item.CardId == physicalDodge.Id && item.CardKind == CardKind.Slash) &&
                game.CardMovements.Any(move =>
                    move.CardId == physicalDodge.Id &&
                    move.CardKind == CardKind.Dodge &&
                    move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Processing &&
                    move.Reason == CardMoveReasons.Use),
            "Formal Longdan must preserve the physical Dodge while publishing an effective Slash use.");

        var activeReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(activeReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(activeReplay).SequenceEqual(EventSignatures(game)),
            "A completed formal Longdan Dodge-to-Slash use must replay exactly.");

        var responseGame = WushengResponseScenario.FindLongdanDodge(
            registry,
            "identity:classic-8");
        var responseOwner = responseGame.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == 0);
        var prompt = responseGame.PendingDecision ??
            throw new InvalidOperationException("The formal Longdan response fixture lost its prompt.");
        var choice = prompt.Choices.First(candidate =>
            candidate.Parameters.GetValueOrDefault("response") == "dodge" &&
            candidate.Parameters.GetValueOrDefault("response-card-kind") == nameof(CardKind.Dodge) &&
            candidate.Cards.Count == 1 &&
            responseOwner.Hand.Single(card => card.Id == candidate.Cards[0]).Kind is
                CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash);
        var physicalSlash = responseOwner.Hand.Single(card => card.Id == choice.Cards[0]);
        Require(responseOwner.GeneralId == "classic:zhao-yun" &&
                prompt.Kind == DecisionKind.RespondDodge &&
                choice.Description.Contains("当作【闪】", StringComparison.Ordinal),
            "Classic Zhao Yun must publish a Slash-to-Dodge response without leaking the physical identity.");

        var response = responseGame.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            choice.Id,
            responseGame.Revision));
        Require(response.Accepted &&
                responseGame.Events.Any(item =>
                    item.Payload is CardRespondedEvent responded &&
                    responded.CardId == physicalSlash.Id &&
                    responded.ResponderSeat == 0 &&
                    responded.EffectiveCardKind == CardKind.Dodge) &&
                responseGame.CardMovements.Any(move =>
                    move.CardId == physicalSlash.Id &&
                    move.CardKind == physicalSlash.Kind &&
                    move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Processing &&
                    move.Reason == CardMoveReasons.Respond),
            response.Error?.Message ??
            "Formal Longdan must preserve the physical Slash while publishing an effective Dodge response.");

        var responseReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(responseGame.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(responseReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(responseGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(responseReplay).SequenceEqual(EventSignatures(responseGame)),
            "An in-flight formal Longdan Slash-to-Dodge response must replay exactly.");
    }

    public static void ConfiguredKujinFlow()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Skills["classic:kujin"] is
                { Program: { RuntimeVersion: "skill-program-v61", MinimumRulesVersion: 171 } program } &&
                program.Activations.Single() is
                { Id: "lose-hp-and-draw", UsesPerTurn: null },
            "Current Kujin must be a repeatable configured activation.");

        var game = SelectGeneral(current, "classic:huang-gai", GameCheckpoint.CurrentRulesVersion);
        Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
            "Configured Kujin could not reach the human play phase.");
        var before = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        for (var use = 0; use < 2; use++)
        {
            var action = game.GetHumanLegalActions().Single(candidate =>
                candidate.Kind == LegalActionKind.UseProgramSkill &&
                candidate.ProgramSkillId == "classic:kujin" &&
                candidate.ProgramActivationId == "lose-hp-and-draw");
            var result = game.Submit(new UseProgramSkillCommand(
                0, action.ProgramSkillId!, action.ProgramActivationId!, [], [],
                game.Revision, game.PendingDecision!.PromptId));
            Require(result.Accepted, result.Error?.Message ?? "Configured Kujin was rejected.");
            Require(TryReturnToHumanPlay(game), "Configured Kujin did not return to human play.");
        }
        var after = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        var events = game.Events.Select(item => item.Payload).ToArray();
        Require(after.Hp == before.Hp - 2 && after.HandCount == before.HandCount + 4 &&
                events.OfType<ProgramSkillHpLostEvent>().Count(item =>
                    item.SkillId == "classic:kujin" && item.Amount == 1) == 2 &&
                events.OfType<ProgramSkillResolvedEvent>().Count(item =>
                    item.SkillId == "classic:kujin" && item.Completed) == 2,
            "Two configured Kujin activations must each lose one HP and draw two cards.");
        var replay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), current);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replay).SequenceEqual(EventSignatures(game)),
            "Repeated configured Kujin must replay exactly.");
    }

    public static void ConfiguredKujinDyingContinuation()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = SelectGeneral(registry, "classic:huang-gai", GameCheckpoint.CurrentRulesVersion);
        Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
            "Configured Kujin dying fixture could not reach play.");
        for (var use = 0; use < 4; use++)
        {
            Activate();
            Require(TryReturnToHumanPlay(game), "Configured Kujin did not return from a nonlethal use.");
        }
        var before = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(before.Hp == 1, "Four Kujin uses must leave the five-HP Lord at one HP.");
        Activate();
        var suspended = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(suspended.Hp == 0 && suspended.HandCount == before.HandCount &&
                game.PendingDecision is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 } &&
                game.Events.Select(item => item.Payload).OfType<ProgramSkillHpLostEvent>().Last() is
                { SkillId: "classic:kujin", Amount: 1, RemainingHp: 0 } &&
                game.Events.Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>()
                    .Count(item => item.SkillId == "classic:kujin" && item.Completed) == 4,
            "Lethal configured Kujin must pause before its two-card draw.");
        var replay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replay).SequenceEqual(EventSignatures(game)),
            "Configured Kujin dying prompt must replay exactly.");
        foreach (var branch in new[] { game, replay })
        {
            var prompt = branch.PendingDecision ??
                throw new InvalidOperationException("Configured Kujin lost its dying prompt.");
            var decline = prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("response") == "let-die");
            var answered = branch.Submit(new AnswerPromptCommand(
                0, prompt.PromptId, decline.Id, branch.Revision));
            Require(answered.Accepted, answered.Error?.Message ?? "Configured Kujin dying answer failed.");
        }
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replay).SequenceEqual(EventSignatures(game)),
            "Configured Kujin dying continuation must remain deterministic.");

        void Activate()
        {
            var action = game.GetHumanLegalActions().Single(candidate =>
                candidate.Kind == LegalActionKind.UseProgramSkill &&
                candidate.ProgramSkillId == "classic:kujin");
            var result = game.Submit(new UseProgramSkillCommand(
                0, action.ProgramSkillId!, action.ProgramActivationId!, [], [],
                game.Revision, game.PendingDecision!.PromptId));
            Require(result.Accepted, result.Error?.Message ?? "Configured Kujin activation failed.");
        }
    }

    public static void ConfiguredLongdanFlow()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var skill = current.Skills["classic:longdan"];
        Require(skill is { Program: { RuntimeVersion: "skill-program-v61", MinimumRulesVersion: 171 } program } &&
                program.ViewAs.Select(rule => rule.Id).Order(StringComparer.Ordinal)
                    .SequenceEqual(["dodge-to-slash", "slash-to-dodge"]) &&
                current.Skills["classic:longdan"].Program?.GameplayHash == program.GameplayHash,
            "Current classic Longdan must publish both configured conversions.");

        var (game, action) = FindZhaoYunLongdanFixture(current);
        var source = action.ConversionSource;
        Require(action.PlayedCardKind == CardKind.Slash &&
                source is { SkillId: "classic:longdan", BindingId: "dodge-to-slash", OwnerSeat: 0 },
            "Current classic Zhao Yun must expose exactly attributed Dodge-to-Slash actions.");
        ArgumentNullException.ThrowIfNull(source);
        var before = game.CreateCheckpoint();
        var forged = game.Submit(new PlayCardCommand(
            0, action.CardId!.Value, action.TargetSeats, game.Revision,
            game.PendingDecision!.PromptId, action.PlayedCardKind)
        {
            ConversionSource = source with { BindingId = "forged" }
        });
        Require(!forged.Accepted && game.Revision == before.Revision,
            "A forged Longdan conversion binding must be rejected without advancing the match.");
        var start = game.Events.Count;
        var used = game.Submit(new PlayCardCommand(
            0, action.CardId.Value, action.TargetSeats, game.Revision,
            game.PendingDecision!.PromptId, action.PlayedCardKind)
        {
            ConversionSource = source
        });
        Require(used.Accepted, used.Error?.Message ?? "The configured Longdan Slash was rejected.");
        Require(TryReturnToHumanPlay(game), "The configured Longdan Slash did not finish.");
        var events = game.Events.Skip(start).Select(item => item.Payload).ToArray();
        Require(events.OfType<CardActionAcceptedEvent>().Any(item =>
                    item.Action.Type == CardActionType.Use &&
                    item.Action.EffectiveKind == CardKind.Slash &&
                    item.Action.ConversionChain.SequenceEqual([source])) &&
                events.OfType<CardUsedEvent>().Any(item =>
                    item.CardId == action.CardId && item.CardKind == CardKind.Slash),
            "The configured Longdan Slash must keep its physical card and exact source.");
        var replay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), current);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replay).SequenceEqual(EventSignatures(game)),
            "The configured Longdan Slash must replay exactly.");

        var responseGame = WushengResponseScenario.FindLongdanDodge(
            current, "identity:classic-8", "classic:longdan");
        var prompt = responseGame.PendingDecision ??
            throw new InvalidOperationException("The configured Longdan response lost its prompt.");
        var choice = prompt.Choices.First(candidate =>
            candidate.Parameters.GetValueOrDefault("response") == "dodge" &&
            candidate.Parameters.GetValueOrDefault("conversion-skill-id") == "classic:longdan" &&
            candidate.Parameters.GetValueOrDefault("conversion-binding-id") == "slash-to-dodge");
        var responseStart = responseGame.Events.Count;
        var response = responseGame.Submit(new AnswerPromptCommand(
            0, prompt.PromptId, choice.Id, responseGame.Revision));
        Require(response.Accepted, response.Error?.Message ?? "The configured Longdan Dodge was rejected.");
        var responseEvents = responseGame.Events.Skip(responseStart).Select(item => item.Payload).ToArray();
        Require(responseEvents.OfType<CardActionAcceptedEvent>().Any(item =>
                    item.Action.Type == CardActionType.Response &&
                    item.Action.EffectiveKind == CardKind.Dodge &&
                    item.Action.ConversionChain.SingleOrDefault() is
                    { SkillId: "classic:longdan", BindingId: "slash-to-dodge", OwnerSeat: 0 }) &&
                responseEvents.OfType<CardRespondedEvent>().Any(item =>
                    item.EffectiveCardKind == CardKind.Dodge && item.ResponderSeat == 0),
            "The configured Longdan Dodge must retain its exact conversion source.");
        var responseReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(responseGame.CreateCheckpoint())), current);
        Require(SnapshotJson.Serialize(responseReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(responseGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(responseReplay).SequenceEqual(EventSignatures(responseGame)),
            "The configured Longdan Dodge must replay exactly.");
    }

    public static void ConfiguredQingguoResponses()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var skill = current.Skills["classic:qingguo"];
        Require(skill is { Program: { RuntimeVersion: "skill-program-v61", MinimumRulesVersion: 171 } program } &&
                program.ViewAs.Single() is { Id: "black-hand-as-dodge", ForPlay: false, ForResponse: true } rule &&
                rule.InputSuits.Order().SequenceEqual(new[] { Suit.Spade, Suit.Club }.Order()),
            "Current Qingguo must configure black-hand Dodge responses.");

        foreach (var incoming in new[] { CardKind.Slash, CardKind.ArrowBarrage })
        {
            var game = WushengResponseScenario.FindQingguoDodge(
                incoming, skillContentId: "classic:qingguo");
            var prompt = game.PendingDecision ??
                throw new InvalidOperationException("Configured Qingguo response fixture lost its prompt.");
            var owner = game.CreateSnapshot(0, revealAll: true).Players[0];
            var choice = prompt.Choices.First(candidate =>
                candidate.Parameters.GetValueOrDefault("conversion-skill-id") == "classic:qingguo" &&
                candidate.Parameters.GetValueOrDefault("conversion-binding-id") == "black-hand-as-dodge");
            var physical = owner.Hand.Single(card => card.Id == choice.Cards.Single());
            Require(physical.Suit is Suit.Spade or Suit.Club && physical.Kind != CardKind.Dodge &&
                    prompt.Choices.Where(candidate =>
                            candidate.Parameters.GetValueOrDefault("conversion-skill-id") == "classic:qingguo")
                        .All(candidate => owner.Hand.Single(card => card.Id == candidate.Cards.Single()).Suit is
                            Suit.Spade or Suit.Club),
                $"Configured Qingguo must offer only black non-Dodge hand cards against {incoming}.");

            var paused = GameReplay.Restore(
                GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), current);
            Require(SnapshotJson.Serialize(paused.CreateSnapshot(0, revealAll: true)) ==
                    SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                    EventSignatures(paused).SequenceEqual(EventSignatures(game)),
                "A configured Qingguo response prompt must replay exactly.");
            var start = game.Events.Count;
            var result = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
            Require(result.Accepted, result.Error?.Message ?? "Configured Qingguo response was rejected.");
            var events = game.Events.Skip(start).Select(item => item.Payload).ToArray();
            Require(events.OfType<CardActionAcceptedEvent>().Any(item =>
                        item.Action.Type == CardActionType.Response &&
                        item.Action.EffectiveKind == CardKind.Dodge &&
                        item.Action.ConversionChain.SingleOrDefault() is
                        { SkillId: "classic:qingguo", BindingId: "black-hand-as-dodge", OwnerSeat: 0 }) &&
                    events.OfType<CardRespondedEvent>().Any(item =>
                        item.CardId == physical.Id && item.EffectiveCardKind == CardKind.Dodge) &&
                    game.CardMovements.Any(move => move.CardId == physical.Id &&
                        move.CardKind == physical.Kind && move.From == CardLocation.Hand(0) &&
                        move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Respond),
                "Configured Qingguo must retain the physical card and attribute its Dodge response.");
            var completed = GameReplay.Restore(
                GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), current);
            Require(SnapshotJson.Serialize(completed.CreateSnapshot(0, revealAll: true)) ==
                    SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                    EventSignatures(completed).SequenceEqual(EventSignatures(game)),
                "The configured Qingguo response must replay exactly after completion.");
        }
    }

    public static void ConfiguredWushengSources()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var skill = current.Skills["classic:wusheng"];
        Require(skill is { Program: { RuntimeVersion: "skill-program-v61", MinimumRulesVersion: 171 } program } &&
                program.ViewAs.Single() is { Id: "red-owned-as-slash", ForPlay: true, ForResponse: true } rule &&
                rule.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) &&
                rule.InputSuits.Order().SequenceEqual(new[] { Suit.Heart, Suit.Diamond }.Order()),
            "Current Wusheng must configure both owned source zones.");

        var handGame = WushengResponseScenario.FindClassicWushengHand(current);
        var handPrompt = handGame.PendingDecision ??
            throw new InvalidOperationException("Configured Wusheng hand response lost its prompt.");
        var handOwner = handGame.CreateSnapshot(0, revealAll: true).Players[0];
        var handChoice = handPrompt.Choices.First(candidate =>
            candidate.Parameters.GetValueOrDefault("conversion-skill-id") == "classic:wusheng" &&
            candidate.Parameters.GetValueOrDefault("conversion-binding-id") == "red-owned-as-slash" &&
            handOwner.Hand.Any(card => card.Id == candidate.Cards.Single() &&
                card.Kind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)));
        var handCard = handOwner.Hand.Single(card => card.Id == handChoice.Cards.Single());
        Require(handCard.Suit is Suit.Heart or Suit.Diamond &&
                handCard.Kind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash),
            "Configured Wusheng must only convert a red non-Slash hand card.");
        var handCheckpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(handGame.CreateCheckpoint()));
        var pausedHand = GameReplay.Restore(handCheckpoint, current);
        Require(SnapshotJson.Serialize(pausedHand.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(handGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(pausedHand).SequenceEqual(EventSignatures(handGame)),
            "A configured Wusheng hand response must restore at the prompt.");
        var handStart = handGame.Events.Count;
        var handResult = handGame.Submit(new AnswerPromptCommand(
            0, handPrompt.PromptId, handChoice.Id, handGame.Revision));
        Require(handResult.Accepted, handResult.Error?.Message ?? "Configured Wusheng hand response failed.");
        Require(handGame.Events.Skip(handStart).Select(item => item.Payload)
                    .OfType<CardActionAcceptedEvent>().Any(item =>
                        item.Action.Type == CardActionType.Response &&
                        item.Action.EffectiveKind == CardKind.Slash &&
                        item.Action.ConversionChain.SingleOrDefault() is
                        { SkillId: "classic:wusheng", BindingId: "red-owned-as-slash", OwnerSeat: 0 }) &&
                handGame.CardMovements.Any(move =>
                    move.CardId == handCard.Id && move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Respond),
            "Configured Wusheng hand response must preserve exact physical cost and source.");

        var equipmentFixture = FindGuanYuWushengEquipmentFixture(current);
        var action = equipmentFixture.ActiveAction;
        Require(action.ConversionSource is
                { SkillId: "classic:wusheng", BindingId: "red-owned-as-slash", OwnerSeat: 0 },
            "Configured Wusheng must publish an attributed equipped-Slash play action.");
        var activeGame = equipmentFixture.ActiveGame;
        var forged = activeGame.Submit(new PlayCardCommand(
            0, action.CardId!.Value, action.TargetSeats, activeGame.Revision,
            activeGame.PendingDecision!.PromptId, action.PlayedCardKind)
        {
            ConversionSource = action.ConversionSource! with { BindingId = "forged" }
        });
        Require(!forged.Accepted, "A forged equipped Wusheng source must be rejected.");
        var used = SubmitPlayAction(activeGame, action);
        Require(used.Accepted, used.Error?.Message ?? "Configured equipped Wusheng play failed.");
        Require(activeGame.CardMovements.Any(move =>
                    move.CardId == equipmentFixture.EquipmentCardId &&
                    move.From == CardLocation.Equipment(0) &&
                    move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Use) &&
                activeGame.Events.Select(item => item.Payload).OfType<CardActionAcceptedEvent>().Any(item =>
                    item.Action.Type == CardActionType.Use &&
                    item.Action.ConversionChain.SingleOrDefault() is
                    { SkillId: "classic:wusheng", BindingId: "red-owned-as-slash", OwnerSeat: 0 }),
            "Configured Wusheng must pay equipment and freeze its program source.");

        var responseGame = equipmentFixture.ResponseGame;
        var responsePrompt = responseGame.PendingDecision ??
            throw new InvalidOperationException("Configured equipped Wusheng response lost its prompt.");
        var responseChoice = responsePrompt.Choices.Single(candidate =>
            candidate.Cards.SequenceEqual([equipmentFixture.EquipmentCardId]) &&
            candidate.Parameters.GetValueOrDefault("conversion-skill-id") == "classic:wusheng" &&
            candidate.Parameters.GetValueOrDefault("conversion-binding-id") == "red-owned-as-slash");
        var pausedEquipment = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(responseGame.CreateCheckpoint())), current);
        Require(SnapshotJson.Serialize(pausedEquipment.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(responseGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(pausedEquipment).SequenceEqual(EventSignatures(responseGame)),
            "A configured equipped Wusheng response prompt must replay exactly.");
        var responseStart = responseGame.Events.Count;
        var answered = responseGame.Submit(new AnswerPromptCommand(
            0, responsePrompt.PromptId, responseChoice.Id, responseGame.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? "Configured equipped Wusheng response failed.");
        Require(responseGame.Events.Skip(responseStart).Select(item => item.Payload)
                    .OfType<CardActionAcceptedEvent>().Any(item =>
                        item.Action.Type == CardActionType.Response &&
                        item.Action.ConversionChain.SingleOrDefault() is
                        { SkillId: "classic:wusheng", BindingId: "red-owned-as-slash", OwnerSeat: 0 }) &&
                responseGame.CardMovements.Any(move =>
                    move.CardId == equipmentFixture.EquipmentCardId &&
                    move.From == CardLocation.Equipment(0) &&
                    move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Respond),
            "Configured Wusheng must preserve equipped response cost and source.");
    }

    public static void ConfiguredQingnangHealing()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Skills["classic:qingnang"] is
                { Program: { RuntimeVersion: "skill-program-v61", MinimumRulesVersion: 171 } program } &&
                program.Activations.Single() is
                { Id: "discard-and-heal", MinCards: 1, MaxCards: 1, MinTargets: 1,
                    MaxTargets: 1, TargetKind: SkillProgramTargetKind.AnyWounded, UsesPerTurn: null, UsesPerPhase: 1 },
            "Current Qingnang must use its phase allowance.");

        GameEngine? selected = null;
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var candidate = StartClassicGeneralAtPlay(
                current, seed, "classic:hua-tuo", GameCheckpoint.CurrentRulesVersion);
            if (candidate is null) continue;
            var slash = candidate.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash && action.CardId is not null &&
                action.TargetSeats.Count == 1);
            if (slash is null) continue;
            var played = SubmitPlayAction(candidate, slash);
            if (!played.Accepted) continue;
            try
            {
                if (!TryReturnToHumanPlay(candidate)) continue;
            }
            catch (InvalidOperationException exception) when (
                exception.Message.StartsWith("Unknown or unsupported turn phase:", StringComparison.Ordinal))
            {
                continue;
            }
            if (candidate.CreateSnapshot(0, revealAll: true).Players.Any(player =>
                    player.IsAlive && player.Hp < player.MaxHp) &&
                candidate.GetHumanLegalActions().Any(action =>
                    action.Kind == LegalActionKind.UseProgramSkill &&
                    action.ProgramSkillId == "classic:qingnang"))
            {
                selected = candidate;
                break;
            }
        }
        var game = selected ??
            throw new InvalidOperationException("No bounded current Hua Tuo Qingnang fixture was found.");
        var before = game.CreateSnapshot(0, revealAll: true);
        var owner = before.Players.Single(player => player.Seat == 0);
        var target = before.Players.First(player => player.IsAlive && player.Hp < player.MaxHp);
        var card = owner.Hand.First();
        var playPrompt = game.PendingDecision ??
            throw new InvalidOperationException("Current Qingnang lost its play prompt.");
        var rejected = game.Submit(new UseProgramSkillCommand(
            0, "classic:qingnang", "discard-and-heal", [card.Id],
            [before.Players.First(player => player.Hp == player.MaxHp).Seat],
            game.Revision, playPrompt.PromptId));
        Require(!rejected.Accepted, "Configured Qingnang must reject an unwounded target atomically.");
        var start = game.Events.Count;
        var result = game.Submit(new UseProgramSkillCommand(
            0, "classic:qingnang", "discard-and-heal", [card.Id], [target.Seat],
            game.Revision, playPrompt.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "Configured Qingnang was rejected.");
        Require(TryReturnToHumanPlay(game), "Configured Qingnang did not finish at human play.");
        var after = game.CreateSnapshot(0, revealAll: true);
        Require(after.Players.Single(player => player.Seat == target.Seat).Hp == target.Hp + 1,
            "Configured Qingnang did not heal its selected wounded target.");
        Require(after.Players[0].HandCount == owner.HandCount - 1,
            "Configured Qingnang did not spend exactly one hand card.");
        Require(game.CardMovements.Any(move =>
                    move.CardId == card.Id && move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.DiscardPile &&
                    move.Reason.Value == "skill-program.classic:qingnang.DiscardSelected"),
            "Configured Qingnang did not expose its exact physical discard movement: " +
            string.Join(", ", game.CardMovements.Where(move => move.CardId == card.Id)
                .Select(move => $"{move.From}->{move.To}:{move.Reason.Value}")));
        Require(game.Events.Skip(start).Select(item => item.Payload).OfType<ProgramSkillResolvedEvent>()
                    .Any(item => item.SkillId == "classic:qingnang" && item.Completed),
            "Configured Qingnang did not publish a completed program event.");
        Require(game.GetHumanLegalActions().All(action =>
                    action.Kind != LegalActionKind.UseProgramSkill ||
                    action.ProgramSkillId != "classic:qingnang"),
            "Configured Qingnang must exhaust its once-per-phase use.");
        var replay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), current);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replay).SequenceEqual(EventSignatures(game)),
            "A completed configured Qingnang use must replay exactly.");
    }

    public static void FormalWushengEquipmentFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(GameCheckpoint.CurrentRulesVersion >= 40,
            "Formal Wusheng equipment conversion must have an explicit rules-version boundary.");

        Require(registry.Skills["classic:wusheng"].Program?.ViewAs.Single().SourceZones.Contains(
                CardZoneKind.Equipment) == true,
            "Configured Wusheng must accept eligible equipment sources.");

        var fixture = FindGuanYuWushengEquipmentFixture(registry);
        var activeGame = fixture.ActiveGame;
        var owner = activeGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        var equipment = owner.Equipment.Single(card => card.Id == fixture.EquipmentCardId);
        Require(owner.GeneralId == "classic:guan-yu" &&
                owner.MaxHp == 5 &&
                owner.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:wusheng"]) &&
                equipment.Suit is Suit.Heart or Suit.Diamond &&
                fixture.ActiveAction.PlayedCardKind == CardKind.Slash,
            "Classic Guan Yu must publish a red equipment card as a typed Slash through formal Wusheng.");
        var eventCount = activeGame.Events.Count;
        var used = SubmitPlayAction(activeGame, fixture.ActiveAction);
        Require(used.Accepted, used.Error?.Message ??
            "Classic Guan Yu could not use red equipment as Slash.");
        Require(activeGame.CardMovements.Any(move =>
                    move.CardId == equipment.Id &&
                    move.CardKind == equipment.Kind &&
                    move.From == CardLocation.Equipment(0) &&
                    move.To == CardLocation.Processing &&
                    move.Reason == CardMoveReasons.Use) &&
                activeGame.Events.Skip(eventCount).Select(item => item.Payload)
                    .OfType<CardUsedEvent>().Any(item =>
                        item.CardId == equipment.Id && item.CardKind == CardKind.Slash),
            "Formal Wusheng must retain the physical equipment and publish an effective Slash use.");
        Require(TryReturnToHumanPlay(activeGame) &&
                activeGame.CardMovements.Any(move =>
                    move.CardId == equipment.Id &&
                    move.From == CardLocation.Processing &&
                    move.To == CardLocation.DiscardPile &&
                    move.Reason == CardMoveReasons.UseFinished),
            "The equipped Wusheng Slash did not finish through the ordinary Slash movement chain.");

        var activeReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(activeGame.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(activeReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(activeGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(activeReplay).SequenceEqual(EventSignatures(activeGame)),
            "A completed equipped Wusheng Slash must replay exactly.");

        var responseGame = fixture.ResponseGame;
        var responsePrompt = responseGame.PendingDecision ??
            throw new InvalidOperationException("The equipped Wusheng response fixture lost its prompt.");
        var responseOwner = responseGame.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == 0);
        var responseChoice = responsePrompt.Choices.Single(choice =>
            choice.Cards.SequenceEqual([equipment.Id]) &&
            choice.Parameters.GetValueOrDefault("response-card-kind") == nameof(CardKind.Slash));
        Require(responsePrompt.Kind == DecisionKind.RespondSlash &&
                responseOwner.Equipment.Any(card => card.Id == equipment.Id) &&
                responseChoice.Description.Contains("当作【杀】", StringComparison.Ordinal) &&
                responseGame.CreateSnapshot(1).PendingDecision is null,
            "Formal Wusheng must publish the equipped red card only in Guan Yu's private Slash response.");

        var responseCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(responseGame.CreateCheckpoint()));
        var pausedReplay = GameReplay.Restore(responseCheckpoint, registry);
        Require(SnapshotJson.Serialize(pausedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(responseGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(pausedReplay).SequenceEqual(EventSignatures(responseGame)),
            "An in-flight equipped Wusheng response must replay exactly.");

        var answered = responseGame.Submit(new AnswerPromptCommand(
            0,
            responsePrompt.PromptId,
            responseChoice.Id,
            responseGame.Revision));
        Require(answered.Accepted &&
                responseGame.Events.Any(item =>
                    item.Payload is CardRespondedEvent responded &&
                    responded.CardId == equipment.Id &&
                    responded.ResponderSeat == 0 &&
                    responded.EffectiveCardKind == CardKind.Slash) &&
                responseGame.CardMovements.Any(move =>
                    move.CardId == equipment.Id &&
                    move.CardKind == equipment.Kind &&
                    move.From == CardLocation.Equipment(0) &&
                    move.To == CardLocation.Processing &&
                    move.Reason == CardMoveReasons.Respond) &&
                responseGame.CardMovements.Any(move =>
                    move.CardId == equipment.Id &&
                    move.From == CardLocation.Processing &&
                    move.To == CardLocation.DiscardPile &&
                    move.Reason == CardMoveReasons.ResponseFinished),
            answered.Error?.Message ??
            "Formal Wusheng must pay the equipped physical card through the Slash response chain.");
    }

    public static void FormalTianduJudgment()
    {
        var registry = CreatePreProgramClassicRegistry();
        Require(GameCheckpoint.CurrentRulesVersion >= 22,
            "Formal Tiandu must have an explicit rules version.");
        GameEngine? current = null;
        JudgmentResolvedEvent? resolvedJudgment = null;
        for (var seed = 1; seed <= 8_192 && current is null; seed++)
        {
            var candidate = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:guo-jia",
                GameCheckpoint.CurrentRulesVersion);
            var lightningAction = candidate?.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Lightning && action.CardId is not null);
            if (candidate is null || lightningAction is null)
            {
                continue;
            }

            var usedLightning = candidate.Submit(new PlayCardCommand(
                0,
                lightningAction.CardId!.Value,
                lightningAction.TargetSeats,
                candidate.Revision,
                candidate.PendingDecision!.PromptId));
            if (!usedLightning.Accepted ||
                !DriveUntilOwnLightningJudgment(candidate, expectTiandu: true, out var candidateJudgment) ||
                candidateJudgment.Succeeded)
            {
                continue;
            }

            current = candidate;
            resolvedJudgment = candidateJudgment;
        }

        var game = current ??
            throw new InvalidOperationException("No deterministic non-lethal Tiandu Lightning fixture was found.");
        var judgment = resolvedJudgment!;
        var prompt = game.PendingDecision;
        Require(prompt is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } &&
                prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                    .OrderBy(action => action, StringComparer.Ordinal)
                    .SequenceEqual(["tiandu-claim", "tiandu-skip"]),
            "Rules v22 must pause after the judgment result with complete Tiandu choices.");
        Require(game.CardMovements.Last(movement => movement.CardId == judgment.CardId).To ==
                CardLocation.Judgment(0),
            "The resolved judgment card must remain in the public judgment zone while Tiandu is pending, even after a reshuffle reuses its physical card.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var pausedRestore = GameReplay.Restore(pausedCheckpoint, registry);
        Require(pausedRestore.PendingDecision?.Kind == DecisionKind.ProgramTrigger,
            "A paused Tiandu choice must restore from its command checkpoint.");

        var unchanged = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var forged = game.Submit(new AnswerPromptCommand(
            0,
            prompt!.PromptId,
            new ChoiceId("tiandu.unknown"),
            game.Revision));
        Require(!forged.Accepted && forged.Error?.Code == CommandErrorCode.InvalidChoice &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == unchanged,
            "A forged Tiandu choice must be rejected atomically.");

        var claimChoice = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "tiandu-claim");
        var claimed = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            claimChoice.Id,
            game.Revision));
        Require(claimed.Accepted,
            claimed.Error?.Message ?? "Tiandu did not accept the claim choice.");
        Require(game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0)
                .Hand.Any(card => card.Id == judgment.CardId),
            "Tiandu must add the exact resolved judgment card to its owner's hand.");
        Require(game.CardMovements.Any(movement =>
                movement.CardId == judgment.CardId &&
                movement.From == CardLocation.Judgment(0) &&
                movement.To == CardLocation.Hand(0) &&
                movement.Reason == new CardMoveReason("skill-program.classic:tiandu.claimJudgmentCard")),
            "Tiandu must publish the exact judgment-to-hand card movement.");
        Require(game.Events.Any(envelope => envelope.Payload is ProgramJudgmentCardClaimedEvent
        {
            OwnerSeat: 0,
            SkillId: "classic:tiandu"
        }),
            "Tiandu must publish its typed claim result event.");
        var claimedReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(claimedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(claimedReplay).SequenceEqual(EventSignatures(game)),
            "The claimed Tiandu branch must replay exactly.");

        var skipPrompt = pausedRestore.PendingDecision!;
        var skipped = pausedRestore.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "tiandu-skip").Id,
            pausedRestore.Revision));
        Require(skipped.Accepted && pausedRestore.CardMovements.Any(movement =>
                movement.CardId == judgment.CardId &&
                movement.From == CardLocation.Judgment(0) &&
                movement.To == CardLocation.DiscardPile &&
                movement.Reason == CardMoveReasons.JudgmentFinish),
            skipped.Error?.Message ?? "Skipping Tiandu did not discard the judgment card normally.");

    }

    public static void FormalFanjianFlow()
    {
        var registry = CreatePreProgramClassicRegistry();
        var game = ReachZhouYuPlayPhase(registry, GameCheckpoint.CurrentRulesVersion);
        var action = game.GetHumanLegalActions().Single(item =>
            item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == "classic:fanjian");
        Require(action.MinCardCount == 0 && action.MaxCardCount == 0 &&
                action.MinTargetCount == 1 && action.MaxTargetCount == 1 &&
                action.SelectableTargetSeats.Contains(1),
            "Fanjian must publish a target-only action against another living player.");
        var before = game.CreateSnapshot(0, revealAll: true);
        var used = game.Submit(new UseProgramSkillCommand(0, "classic:fanjian",
            action.ProgramActivationId!, [], [1], game.Revision, game.PendingDecision!.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "Fanjian activation failed.");
        var prompt = game.CreateSnapshot(1).PendingDecision ??
            throw new InvalidOperationException("Fanjian did not publish a suit choice.");
        Require(prompt is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 1, IsPrivate: true } &&
                prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("option-id"))
                    .Order(StringComparer.Ordinal).SequenceEqual(["club", "diamond", "heart", "spade"]) &&
                game.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
                    .All(item => item.SkillId != "classic:fanjian") &&
                game.CreateSnapshot(1).Players[0].Hand.Count == 0,
            "The recipient must guess privately before Zhou Yu's hand card is revealed.");
        var paused = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restored = GameReplay.Restore(paused, registry);
        Require(restored.CreateSnapshot(1).PendingDecision?.PromptId == prompt.PromptId,
            "A suspended Fanjian suit choice must replay.");
        var state = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var forged = game.Submit(new AnswerPromptCommand(1, prompt.PromptId,
            new ChoiceId("program-option.forged"), game.Revision));
        Require(!forged.Accepted && SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == state,
            "A forged Fanjian guess must reject atomically.");

        GameEngine? matched = null;
        GameEngine? mismatched = null;
        int? frozenCardId = null;
        Suit? frozenSuit = null;
        foreach (var suitId in new[] { "spade", "heart", "club", "diamond" })
        {
            var branch = GameReplay.Restore(paused, registry);
            var branchPrompt = branch.CreateSnapshot(1).PendingDecision!;
            var choice = branchPrompt.Choices.Single(item =>
                item.Parameters.GetValueOrDefault("option-id") == suitId);
            var answered = branch.Submit(new AnswerPromptCommand(
                1, branchPrompt.PromptId, choice.Id, branch.Revision));
            Require(answered.Accepted, answered.Error?.Message ?? $"Fanjian rejected {suitId}.");
            var shown = branch.Events.Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>()
                .Single(item => item.SkillId == "classic:fanjian").Cards.Single();
            frozenCardId ??= shown.Id;
            frozenSuit ??= shown.Suit;
            Require(shown.Id == frozenCardId && shown.Suit == frozenSuit &&
                    branch.CardMovements.Any(move => move.CardId == shown.Id &&
                        move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) &&
                    branch.CardMovements.Any(move => move.CardId == shown.Id &&
                        move.From == CardLocation.Processing && move.To == CardLocation.Hand(1)),
                "All guesses must receive the same deterministic random physical card after selection.");
            if (string.Equals(suitId, shown.Suit.ToString(), StringComparison.OrdinalIgnoreCase))
                matched = branch;
            else
                mismatched ??= branch;
        }
        Require(matched is not null && mismatched is not null,
            "One of the four choices must match the revealed suit and three must mismatch.");
        var matchingGame = matched ?? throw new InvalidOperationException("No matching Fanjian branch.");
        var mismatchingGame = mismatched ?? throw new InvalidOperationException("No mismatching Fanjian branch.");
        Require(matchingGame.CreateSnapshot(0, revealAll: true).Players[1].Hp == before.Players[1].Hp &&
                mismatchingGame.CreateSnapshot(0, revealAll: true).Players[1].Hp == before.Players[1].Hp - 1 &&
                mismatchingGame.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .Any(item => item.SourceSeat == 0 && item.TargetSeat == 1 && item.Amount == 1) &&
                matchingGame.CreateSnapshot(0, revealAll: true).Players[0].HandCount ==
                    before.Players[0].HandCount - 1,
            "Only a wrong suit guess may cause one ordinary damage after the card transfer.");
        Require(SnapshotJson.Serialize(GameReplay.Restore(mismatchingGame.CreateCheckpoint(), registry)
                    .CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(mismatchingGame.CreateSnapshot(0, revealAll: true)),
            "Resolved Fanjian must replay the same state.");
    }
    public static void FormalGuanxingFlow()
    {
        var registry = CreatePreProgramClassicRegistry();
        Require(GameCheckpoint.CurrentRulesVersion >= 24,
            "Formal Guanxing must have an explicit rules version.");

        var game = SelectGeneral(registry, "classic:zhuge-liang", GameCheckpoint.CurrentRulesVersion);
        var reachedOffer = game.Submit(new AdvanceCommand(game.Revision));
        Require(reachedOffer.Accepted, reachedOffer.Error?.Message ?? "Could not reach the Guanxing offer.");
        var offer = game.PendingDecision;
        Require(offer is
        {
            Kind: DecisionKind.ProgramTopReorder,
            PlayerSeat: 0,
            IsPrivate: true,
            Choices.Count: 2
        } &&
                offer.Choices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                    .OrderBy(action => action, StringComparer.Ordinal)
                    .SequenceEqual(["guanxing-skip", "guanxing-use"]) &&
                game.CreateSnapshot(1).PendingDecision is null,
            "Guanxing must first publish a private use/skip offer only to its owner.");

        var offerCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restoredOffer = GameReplay.Restore(offerCheckpoint, registry);
        Require(SnapshotJson.Serialize(restoredOffer.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restoredOffer).SequenceEqual(EventSignatures(game)),
            "A paused Guanxing offer must restore exactly.");

        var originalTopTwo = game.CreateCardZoneDiagnostics()
            .Where(card => card.Location == CardLocation.DrawPile)
            .OrderByDescending(card => card.ZoneIndex)
            .Take(2)
            .Select(card => card.CardId)
            .ToArray();
        var skipped = GameReplay.Restore(offerCheckpoint, registry);
        var skipPrompt = skipped.PendingDecision!;
        var skipResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "guanxing-skip").Id,
            skipped.Revision));
        Require(skipResult.Accepted &&
                skipped.Events.Select(envelope => envelope.Payload).OfType<GuanxingResolvedEvent>()
                    .Any(resolved => !resolved.Used && resolved.ViewedCount == 0) &&
                originalTopTwo.All(cardId => skipped.CreateSnapshot(0).Players[0].Hand.Any(card => card.Id == cardId)),
            $"Skipping Guanxing must retain the original top order and continue through the ordinary draw phase. " +
            $"Top={string.Join(',', originalTopTwo)}; hand={string.Join(',', skipped.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id))}; " +
            $"accepted={skipResult.Accepted}.");

        var unchangedSnapshot = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var unchangedDiagnostics = game.CreateCardZoneDiagnostics().ToArray();
        var unchangedCommands = game.AcceptedCommands.Count;
        var forged = game.Submit(new AnswerPromptCommand(
            0,
            offer!.PromptId,
            new ChoiceId("guanxing-forged"),
            game.Revision));
        Require(!forged.Accepted && forged.Error?.Code == CommandErrorCode.InvalidChoice &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == unchangedSnapshot &&
                game.CreateCardZoneDiagnostics().SequenceEqual(unchangedDiagnostics) &&
                game.AcceptedCommands.Count == unchangedCommands,
            "A forged Guanxing offer answer must be rejected without changing state, deck order or journal.");

        var used = game.Submit(new AnswerPromptCommand(
            0,
            offer.PromptId,
            offer.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "guanxing-use").Id,
            game.Revision));
        Require(used.Accepted, used.Error?.Message ?? "Guanxing use was rejected.");
        var topPrompt = game.PendingDecision;
        Require(topPrompt is
        {
            Kind: DecisionKind.ProgramTopReorder,
            PlayerSeat: 0,
            IsPrivate: true,
            ValidCardIds.Count: 5,
            Choices.Count: 6
        } &&
                topPrompt.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "guanxing-finish-top") == 1 &&
                topPrompt.Choices.Where(choice => choice.Cards.Count == 1).All(choice =>
                    choice.Parameters.GetValueOrDefault("stage") == "top" &&
                    choice.Parameters.ContainsKey("card-kind") &&
                    choice.Parameters.ContainsKey("suit") &&
                    choice.Parameters.ContainsKey("rank")) &&
                game.CreateSnapshot(1).PendingDecision is null,
            "Guanxing must privately reveal five exact top cards plus one finish-top action to the owner only.");
        var actualViewedTop = game.CreateCardZoneDiagnostics()
            .Where(card => card.Location == CardLocation.DrawPile)
            .OrderByDescending(card => card.ZoneIndex)
            .Take(5)
            .Select(card => card.CardId)
            .ToArray();
        Require(topPrompt!.ValidCardIds.SequenceEqual(actualViewedTop),
            "The Guanxing prompt must preserve the actual draw-pile top-first order.");

        var pausedOrderingCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restoredOrdering = GameReplay.Restore(pausedOrderingCheckpoint, registry);
        Require(SnapshotJson.Serialize(restoredOrdering.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                restoredOrdering.CreateCardZoneDiagnostics().SequenceEqual(game.CreateCardZoneDiagnostics()) &&
                EventSignatures(restoredOrdering).SequenceEqual(EventSignatures(game)),
            "A paused private Guanxing card view must restore with identical deck order and events.");

        var chosenTopId = actualViewedTop[^1];
        var selectTop = topPrompt.Choices.Single(choice =>
            choice.Cards.SequenceEqual([chosenTopId]) &&
            choice.Parameters.GetValueOrDefault("action") == "guanxing-top");
        var selectedTop = game.Submit(new AnswerPromptCommand(
            0,
            topPrompt.PromptId,
            selectTop.Id,
            game.Revision));
        Require(selectedTop.Accepted, selectedTop.Error?.Message ?? "Guanxing top-card selection was rejected.");

        var finishPrompt = game.PendingDecision!;
        var finishedTop = game.Submit(new AnswerPromptCommand(
            0,
            finishPrompt.PromptId,
            finishPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "guanxing-finish-top").Id,
            game.Revision));
        Require(finishedTop.Accepted && game.PendingDecision is
        {
            Kind: DecisionKind.ProgramTopReorder,
            ValidCardIds.Count: 4
        },
            finishedTop.Error?.Message ?? "Guanxing did not enter bottom ordering.");

        var bottomOrder = actualViewedTop.Where(cardId => cardId != chosenTopId).Reverse().ToArray();
        foreach (var cardId in bottomOrder)
        {
            var bottomPrompt = game.PendingDecision ??
                throw new InvalidOperationException("Guanxing bottom ordering ended early.");
            var bottomChoice = bottomPrompt.Choices.Single(choice =>
                choice.Cards.SequenceEqual([cardId]) &&
                choice.Parameters.GetValueOrDefault("action") == "guanxing-bottom");
            var selectedBottom = game.Submit(new AnswerPromptCommand(
                0,
                bottomPrompt.PromptId,
                bottomChoice.Id,
                game.Revision));
            Require(selectedBottom.Accepted, selectedBottom.Error?.Message ??
                $"Guanxing bottom-card selection {cardId} was rejected.");
        }

        var humanAfter = game.CreateSnapshot(0, revealAll: true).Players[0];
        var bottomDiagnostics = game.CreateCardZoneDiagnostics()
            .Where(card => bottomOrder.Contains(card.CardId))
            .OrderBy(card => card.ZoneIndex)
            .Select(card => card.CardId)
            .ToArray();
        var resolvedEvent = game.Events.Select(envelope => envelope.Payload)
            .OfType<GuanxingResolvedEvent>()
            .Last();
        Require(humanAfter.Hand.Any(card => card.Id == chosenTopId) &&
                bottomDiagnostics.SequenceEqual(bottomOrder) &&
                resolvedEvent is { SourceSeat: 0, Used: true, ViewedCount: 5, TopCount: 1, BottomCount: 4 } &&
                bottomOrder.All(cardId => game.CardMovements.All(movement => movement.CardId != cardId)),
            "Guanxing must make the first top card the next draw, preserve bottom-first order, and expose only public counts.");

        var resolvedReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(resolvedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                resolvedReplay.CreateCardZoneDiagnostics().SequenceEqual(game.CreateCardZoneDiagnostics()) &&
                EventSignatures(resolvedReplay).SequenceEqual(EventSignatures(game)),
            "Guanxing top/bottom ordering and the following draw must replay exactly.");

    }

    public static void FormalFactionDefenseFlow()
    {
        var registry = CreatePreProgramClassicRegistry();
        Require(GameCheckpoint.CurrentRulesVersion >= 25,
            "Formal FactionDefense must have an explicit rules version.");

        GameEngine? completed = null;
        FactionDefenseResolvedEvent? completedEvent = null;
        int ownerHpBefore = 0;
        for (var seed = 1; seed <= 8_192 && completed is null; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Classic FactionDefense fixture failed to start.");
            if (started.Result.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:cao-cao"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:cao-cao",
                game.Revision,
                game.PendingDecision!.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Classic Cao Cao selection was rejected.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Classic FactionDefense setup did not advance.");

            PendingDecision? ownerPrompt = null;
            for (var step = 0; game.State.Status != EngineStatus.Completed && step < 4_000; step++)
            {
                var prompt = game.PendingDecision;
                if (prompt is { Kind: DecisionKind.RespondDodge } &&
                    prompt.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "faction-defense-request"))
                {
                    ownerPrompt = prompt;
                    break;
                }

                DeclineOrAdvance(game);
            }

            if (ownerPrompt is null)
            {
                continue;
            }

            var boundaryState = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
            var boundaryRevision = game.Revision;
            var forged = game.Submit(new AnswerPromptCommand(
                0,
                ownerPrompt.PromptId,
                new ChoiceId("faction-defense.forged"),
                game.Revision));
            Require(!forged.Accepted && game.Revision == boundaryRevision &&
                    SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == boundaryState,
                "A forged FactionDefense choice must be rejected atomically.");

            ownerHpBefore = game.CreateSnapshot(0, revealAll: true).Players[0].Hp;
            var hujiaChoice = ownerPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("response") == "faction-defense-request");
            var requested = game.Submit(new AnswerPromptCommand(
                0,
                ownerPrompt.PromptId,
                hujiaChoice.Id,
                game.Revision));
            Require(requested.Accepted, requested.Error?.Message ?? "FactionDefense request was rejected.");

            var providerSeats = Enumerable.Range(1, game.PlayerCount)
                .Select(seat => seat % game.PlayerCount)
                .Where(seat => game.CreateSnapshot(seat).PendingDecision?.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") is "faction-defense-dodge" or "faction-defense-bagua") == true)
                .ToArray();
            if (providerSeats.Length != 1)
            {
                continue;
            }

            var providerSeat = providerSeats[0];
            Require(game.CreateSnapshot(0).PendingDecision is null &&
                    Enumerable.Range(0, game.PlayerCount)
                        .Where(seat => seat != providerSeat)
                        .All(seat => game.CreateSnapshot(seat).PendingDecision is null),
                "The FactionDefense provider prompt must remain private to exactly one Wei responder.");
            var pausedCheckpoint = GameCheckpointJson.Deserialize(
                GameCheckpointJson.Serialize(game.CreateCheckpoint()));
            var restoredPaused = GameReplay.Restore(pausedCheckpoint, registry);
            Require(SnapshotJson.Serialize(restoredPaused.CreateSnapshot(providerSeat, revealAll: true)) ==
                    SnapshotJson.Serialize(game.CreateSnapshot(providerSeat, revealAll: true)) &&
                    EventSignatures(restoredPaused).SequenceEqual(EventSignatures(game)),
                "The paused private FactionDefense provider prompt must replay exactly.");

            var eventCount = game.Events.Count;
            for (var step = 0; step < 32 && game.State.Status != EngineStatus.Completed; step++)
            {
                var resolved = game.Events.Skip(eventCount).Select(envelope => envelope.Payload)
                    .OfType<FactionDefenseResolvedEvent>()
                    .LastOrDefault();
                if (resolved is not null)
                {
                    if (resolved is { Succeeded: true, ResponseCardId: not null })
                    {
                        completed = game;
                        completedEvent = resolved;
                    }
                    break;
                }

                if (game.PendingDecision is not null)
                {
                    break;
                }

                var stepResult = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Require(stepResult.Accepted, stepResult.Error?.Message ?? "FactionDefense AI responder did not advance.");
            }
        }

        if (completed is null || completedEvent is null || completedEvent.ResponseCardId is not { } responseCardId)
        {
            throw new InvalidOperationException("No deterministic physical-Dodge FactionDefense boundary was found.");
        }

        Require(completedEvent.OwnerSeat == 0 &&
                completedEvent.ProviderSeat is { } provider &&
                completed.CreateSnapshot(0, revealAll: true).Players[0].Hp == ownerHpBefore &&
                completed.CardMovements.Any(movement =>
                    movement.CardId == responseCardId &&
                    movement.From == CardLocation.Hand(provider) &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.Respond) &&
                completed.CardMovements.Any(movement =>
                    movement.CardId == responseCardId &&
                    movement.From == CardLocation.Processing &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.ResponseFinished) &&
                completed.Events.Select(envelope => envelope.Payload)
                    .OfType<CardRespondedEvent>()
                    .Any(response => response.CardId == responseCardId && response.ResponderSeat == 0),
            "FactionDefense must spend the provider's exact physical Dodge while publishing the effective response as Cao Cao's.");

        var replayed = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(completed.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(replayed.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(completed.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replayed).SequenceEqual(EventSignatures(completed)),
            "The resolved physical-Dodge FactionDefense branch must replay exactly.");
    }

    public static void FormalFactionDefenseBaguaFallback()
    {
        const string modeId = "identity:classic-faction-defense-bagua-test";
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new SyntheticPackage(
                "faction-defense-bagua-test",
                builder =>
                {
                    builder.AddDeck(new ContentDeckRecipe(
                        "test:faction-defense-bagua-deck",
                        "护驾八卦测试牌堆",
                        InitialHandSize: 4,
                        DrawPerTurn: 2,
                        Cards:
                        [
                            new ContentDeckCardCount("standard:bagua", 20),
                            new ContentDeckCardCount("standard:slash", 50),
                            new ContentDeckCardCount("standard:peach", 20)
                        ]));
                    builder.AddMode(new ContentModeDefinition(
                        modeId,
                        "护驾八卦测试身份局",
                        MinPlayers: 5,
                        MaxPlayers: 5,
                        RoleCounts: new Dictionary<string, int>
                        {
                            [nameof(Role.Lord)] = 1,
                            [nameof(Role.Loyalist)] = 1,
                            [nameof(Role.Rebel)] = 2,
                            [nameof(Role.Renegade)] = 1
                        },
                        DeckId: "test:faction-defense-bagua-deck",
                        GeneralCandidateCount: 1,
                        GeneralPoolIds:
                        [
                            "classic:cao-cao",
                            "classic:xiahou-dun",
                            "standard:cao-cao",
                            "standard:guo-jia",
                            "standard:xun-yu"
                        ]));
                },
                new PackageDependency("standard-classic-generals", new Version(1, 4, 0))));

        GameEngine? failedBagua = null;
        JudgmentResolvedEvent? failedJudgment = null;
        int ownerSeat = -1;
        int ownerHpBefore = -1;
        var failedEventStart = -1;
        for (var seed = 1; seed <= 4_096 && failedBagua is null; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = 1,
                HumanRole = Role.Loyalist,
                ModeId = modeId,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 120
            }, registry);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "FactionDefense Bagua fixture failed to start.");
            var full = game.CreateSnapshot(1, revealAll: true);
            var lord = full.Players.Single(player => player.Role == Role.Lord);
            if (lord.GeneralId != "classic:cao-cao" || full.Players[1].GeneralId == "classic:cao-cao")
            {
                continue;
            }

            var equippedBagua = false;
            for (var step = 0; step < 2_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var prompt = game.PendingDecision;
                if (prompt is { Kind: DecisionKind.RespondDodge } &&
                    prompt.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "faction-defense-bagua"))
                {
                    var beforeEvents = game.Events.Count;
                    ownerSeat = prompt.TargetSeat ?? lord.Seat;
                    ownerHpBefore = game.CreateSnapshot(1, revealAll: true).Players[ownerSeat].Hp;
                    var bagua = prompt.Choices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "faction-defense-bagua");
                    var answered = game.Submit(new AnswerPromptCommand(
                        1,
                        prompt.PromptId,
                        bagua.Id,
                        game.Revision));
                    Require(answered.Accepted, answered.Error?.Message ?? "FactionDefense Bagua response was rejected.");
                    var judgment = game.Events.Skip(beforeEvents).Select(envelope => envelope.Payload)
                        .OfType<JudgmentResolvedEvent>()
                        .LastOrDefault(item => item.TargetSeat == 1 && item.Reason == JudgmentReasons.BaguaDefense);
                    if (judgment is { Succeeded: false })
                    {
                        failedBagua = game;
                        failedJudgment = judgment;
                        failedEventStart = beforeEvents;
                    }
                    break;
                }

                GameCommand command;
                if (prompt is null)
                {
                    command = new AdvanceOneStepCommand(game.Revision);
                }
                else if (prompt.Kind == DecisionKind.PlayCard)
                {
                    var baguaAction = game.GetHumanLegalActions().FirstOrDefault(action =>
                        action.Kind == LegalActionKind.Equip &&
                        action.CardId is { } cardId &&
                        game.CreateSnapshot(1).Players[1].Hand.Single(card => card.Id == cardId).Kind ==
                        CardKind.BaguaFormation);
                    if (!equippedBagua && baguaAction is not null)
                    {
                        command = new PlayCardCommand(
                            1,
                            baguaAction.CardId!.Value,
                            baguaAction.TargetSeats,
                            game.Revision,
                            prompt.PromptId);
                        equippedBagua = true;
                    }
                    else
                    {
                        command = new EndPlayPhaseCommand(1, game.Revision, prompt.PromptId);
                    }
                }
                else if (prompt.Kind == DecisionKind.DiscardCards)
                {
                    command = new DiscardCardsCommand(
                        1,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId,
                        game.Revision);
                }
                else
                {
                    var decline = prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("program-action") == "skip") ??
                        prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.Values.Any(value =>
                            value.StartsWith("skip", StringComparison.Ordinal) ||
                            value is "take-damage" or "no-nullification" or "ganglie-lose-hp")) ??
                        prompt.Choices.First();
                    command = new AnswerPromptCommand(1, prompt.PromptId, decline.Id, game.Revision);
                }

                var accepted = game.Submit(command);
                if (!accepted.Accepted)
                {
                    break;
                }
            }
        }

        if (failedBagua is null || failedJudgment is null)
        {
            throw new InvalidOperationException("No deterministic failed FactionDefense Bagua judgment was found.");
        }

        Require(failedJudgment.Succeeded == false &&
                failedBagua.CreateSnapshot(1, revealAll: true).Players[ownerSeat].Hp == ownerHpBefore &&
                failedBagua.ResolutionStack.OfType<ResponseWindowFrame>().Any(frame =>
                    frame.ResponderSeat == ownerSeat && frame.RequiredCardKind == CardKind.Dodge) &&
                failedBagua.Events.Skip(failedEventStart).Select(envelope => envelope.Payload)
                    .OfType<FactionDefenseResolvedEvent>()
                    .All(resolved => !resolved.Succeeded),
            "A failed allied Bagua judgment must keep Cao Cao unharmed and continue the original Dodge response window.");

        var replayed = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(failedBagua.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(replayed.CreateSnapshot(1, revealAll: true)) ==
                SnapshotJson.Serialize(failedBagua.CreateSnapshot(1, revealAll: true)) &&
                EventSignatures(replayed).SequenceEqual(EventSignatures(failedBagua)),
            "The failed FactionDefense Bagua continuation must replay exactly.");
    }

    public static void FormalFactionSlashActiveFlow()
    {
        var (registry, modeId) = CreateFactionSlashFixtureRegistry(
            "active",
            [new ContentDeckCardCount("standard:slash", 100)]);
        GameEngine? game = null;
        LegalAction? jijiang = null;
        int targetSeat = -1;
        for (var seed = 1; seed <= 256 && game is null; seed++)
        {
            var candidate = StartFactionSlashLordAtPlay(registry, modeId, seed);
            var full = candidate.CreateSnapshot(0, revealAll: true);
            var action = candidate.GetHumanLegalActions().Single(item =>
                item.Kind == LegalActionKind.UseProgramSkill &&
                item.ProgramSkillId == "classic:jijiang" &&
                item.ProgramActivationId == "request-shu-slash");
            var rebelTarget = action.SelectableTargetSeats.FirstOrDefault(seat =>
                full.Players[seat].Role == Role.Rebel, -1);
            if (rebelTarget < 0)
            {
                continue;
            }

            game = candidate;
            jijiang = action;
            targetSeat = rebelTarget;
        }

        if (game is null || jijiang is null)
        {
            throw new InvalidOperationException("No deterministic active FactionSlash fixture exposed an in-range Rebel.");
        }

        var lord = game.CreateSnapshot(0, revealAll: true).Players[0];
        Require(lord.MaxHp == 5 &&
                lord.Skills!.Select(skill => skill.ContentId).SequenceEqual(["classic:rende", "classic:jijiang"]),
            "Classic Liu Bei must combine the Lord HP bonus with Rende and FactionSlash in stable order.");
        var publishedPrograms = game.GetHumanLegalActions()
            .Where(action => action.Kind == LegalActionKind.UseProgramSkill).ToArray();
        Require(publishedPrograms.Any(action => action.ProgramSkillId == "classic:rende") &&
                publishedPrograms.Any(action => action.ProgramSkillId == "classic:jijiang") &&
                jijiang.MinCardCount == 0 && jijiang.MaxCardCount == 0 &&
                jijiang.MinTargetCount == 1 && jijiang.MaxTargetCount == 1 &&
                jijiang.SelectableTargetSeats.Contains(targetSeat),
            "The play boundary must publish Rende and FactionSlash as distinct typed active actions.");

        var prompt = game.PendingDecision!;
        var beforeForgery = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var beforeForgeryRevision = game.Revision;
        var forged = game.Submit(new UseProgramSkillCommand(
            0,
            "classic:jijiang",
            "request-shu-slash",
            [],
            [0],
            game.Revision,
            prompt.PromptId));
        Require(!forged.Accepted && game.Revision == beforeForgeryRevision &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == beforeForgery,
            "A forged active FactionSlash target must be rejected atomically.");

        var requested = game.Submit(new UseProgramSkillCommand(
            0,
            "classic:jijiang",
            "request-shu-slash",
            [],
            [targetSeat],
            game.Revision,
            prompt.PromptId));
        Require(requested.Accepted, requested.Error?.Message ?? "Active FactionSlash was rejected.");
        var providerPrompts = Enumerable.Range(0, game.PlayerCount)
            .Select(seat => game.CreateSnapshot(seat).PendingDecision)
            .Where(decision => decision?.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("response") == "faction-slash-slash") == true)
            .Cast<PendingDecision>()
            .ToArray();
        Require(providerPrompts is [{ Kind: DecisionKind.RespondSlash }],
            "Active FactionSlash must pause at one private Shu provider prompt.");
        var providerPrompt = providerPrompts[0];
        var providerSeat = providerPrompt.PlayerSeat;
        Require(game.CreateSnapshot(providerSeat).PendingDecision is not null &&
                Enumerable.Range(0, game.PlayerCount)
                    .Where(seat => seat != providerSeat)
                    .All(seat => game.CreateSnapshot(seat).PendingDecision is null),
            "The active FactionSlash provider prompt must be private to its current Shu candidate.");

        var paused = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(paused.CreateSnapshot(providerSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(providerSeat, revealAll: true)) &&
                EventSignatures(paused).SequenceEqual(EventSignatures(game)),
            "A paused active FactionSlash provider prompt must replay exactly.");

        CardUsedEvent? usedSlash = null;
        for (var step = 0; step < 16 && usedSlash is null; step++)
        {
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The FactionSlash provider cursor did not advance.");
            usedSlash = game.Events.Select(envelope => envelope.Payload)
                .OfType<CardUsedEvent>()
                .LastOrDefault(item => item.SourceSeat == 0 && item.TargetSeat == targetSeat &&
                    item.CardKind == CardKind.Slash);
        }

        if (usedSlash is null)
        {
            throw new InvalidOperationException("No allied Shu provider completed active FactionSlash.");
        }

        var slashCardId = usedSlash.CardId;
        var successfulProvider = game.CardMovements.Single(movement =>
            movement.CardId == slashCardId && movement.To == CardLocation.Processing &&
            movement.Reason == CardMoveReasons.Use).From.OwnerSeat!.Value;
        Require(successfulProvider != 0 &&
                game.CardMovements.Any(movement =>
                    movement.CardId == slashCardId &&
                    movement.From == CardLocation.Hand(successfulProvider) &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.Use) &&
                game.CardMovements.Any(movement =>
                    movement.CardId == slashCardId &&
                    movement.From == CardLocation.Processing &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.UseFinished) &&
                game.Events.Select(envelope => envelope.Payload).OfType<CardUsedEvent>().Any(cardUse =>
                    cardUse.CardId == slashCardId && cardUse.SourceSeat == 0 && cardUse.TargetSeat == targetSeat),
            "Active FactionSlash must spend the provider's exact Slash while making Liu Bei the effective user.");

        var returned = game.Submit(new AdvanceCommand(game.Revision));
        Require(returned.Accepted && game.PendingDecision?.Kind == DecisionKind.PlayCard &&
                game.GetHumanLegalActions().All(action => action.ProgramSkillId != "classic:jijiang"),
            "A successful active FactionSlash Slash must consume Liu Bei's Slash allowance for the turn.");
        var completed = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(completed.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(completed).SequenceEqual(EventSignatures(game)),
            "A completed active FactionSlash Slash must replay exactly.");

        var (failureRegistry, failureModeId) = CreateFactionSlashFixtureRegistry(
            "active-failure",
            [new ContentDeckCardCount("standard:peach", 100)]);
        var failed = StartFactionSlashLordAtPlay(failureRegistry, failureModeId, seed: 1);
        var failedAction = failed.GetHumanLegalActions().Single(action =>
            action.Kind == LegalActionKind.UseProgramSkill &&
            action.ProgramSkillId == "classic:jijiang" &&
            action.ProgramActivationId == "request-shu-slash");
        var failedTarget = failedAction.SelectableTargetSeats[0];
        var failedPrompt = failed.PendingDecision!;
        var failedEventStart = failed.Events.Count;
        var result = failed.Submit(new UseProgramSkillCommand(
            0,
            "classic:jijiang",
            "request-shu-slash",
            [],
            [failedTarget],
            failed.Revision,
            failedPrompt.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "A failed FactionSlash attempt was rejected before resolution.");
        for (var step = 0; step < 16 &&
            !failed.Events.Skip(failedEventStart).Select(envelope => envelope.Payload)
                .OfType<ProgramSkillResolvedEvent>()
                .Any(item => item.SkillId == "classic:jijiang" && item.ActivationId == "request-shu-slash"); step++)
        {
            var advanced = failed.Submit(new AdvanceOneStepCommand(failed.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The failed FactionSlash provider cursor did not advance.");
        }

        var failedEvents = failed.Events.Skip(failedEventStart).Select(envelope => envelope.Payload).ToArray();
        var failedResumed = failed.Submit(new AdvanceCommand(failed.Revision));
        Require(failedEvents.OfType<ProgramSkillResolvedEvent>().Count(item =>
                    item.SkillId == "classic:jijiang" && item.ActivationId == "request-shu-slash" &&
                    item.Completed) == 1 &&
                failedEvents.OfType<CardUsedEvent>().All(item => item.SourceSeat != 0 ||
                    item.TargetSeat != failedTarget) &&
                failedResumed.Accepted && failed.PendingDecision?.Kind == DecisionKind.PlayCard &&
                failed.GetHumanLegalActions().All(action => action.ProgramSkillId != "classic:jijiang"),
            "A declined FactionSlash request must resolve once without a Slash and consume its turn use.");
        var failedReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(failed.CreateCheckpoint())),
            failureRegistry);
        Require(SnapshotJson.Serialize(failedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(failed.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(failedReplay).SequenceEqual(EventSignatures(failed)),
            "A completed declined FactionSlash request must replay exactly.");
    }

    public static void FormalFactionSlashResponseFlow()
    {
        var (registry, modeId) = CreateFactionSlashFixtureRegistry(
            "response",
            [
                new ContentDeckCardCount("standard:slash", 60),
                new ContentDeckCardCount("standard:barbarian_assault", 40)
            ]);
        GameEngine? selectedGame = null;
        PendingDecision? selectedPrompt = null;
        FactionSlashRequestedEvent? selectedRequest = null;
        for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = 1,
                HumanRole = Role.Loyalist,
                ModeId = modeId,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 80
            }, registry);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "FactionSlash response fixture failed to start.");
            var full = game.CreateSnapshot(1, revealAll: true);
            if (full.Players.Single(player => player.Role == Role.Lord).GeneralId != "classic:liu-bei")
            {
                continue;
            }

            for (var step = 0; step < 2_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var decision = game.PendingDecision;
                if (decision?.PlayerSeat == 1 &&
                    decision.Kind == DecisionKind.RespondSlash &&
                    decision.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "faction-slash-slash"))
                {
                    var request = game.Events.Select(envelope => envelope.Payload)
                        .OfType<FactionSlashRequestedEvent>()
                        .LastOrDefault();
                    if (request is { IsActiveUse: false })
                    {
                        selectedGame = game;
                        selectedPrompt = decision;
                        selectedRequest = request;
                        break;
                    }
                }

                GameCommand command;
                if (decision is null || decision.PlayerSeat != 1)
                {
                    command = new AdvanceOneStepCommand(game.Revision);
                }
                else if (decision.Kind == DecisionKind.PlayCard)
                {
                    command = new EndPlayPhaseCommand(1, game.Revision, decision.PromptId);
                }
                else if (decision.Kind == DecisionKind.DiscardCards)
                {
                    command = new DiscardCardsCommand(
                        1,
                        decision.ValidCardIds.Take(decision.RequiredCardCount).ToArray(),
                        decision.PromptId,
                        game.Revision);
                }
                else
                {
                    command = new AnswerPromptCommand(
                        1,
                        decision.PromptId,
                        DeclineChoice(decision).Id,
                        game.Revision);
                }

                var advanced = game.Submit(command);
                if (!advanced.Accepted)
                {
                    break;
                }
            }
        }

        if (selectedGame is null || selectedPrompt is null || selectedRequest is null)
        {
            throw new InvalidOperationException("No deterministic response FactionSlash provider boundary was found.");
        }

        var gameWithResponse = selectedGame;
        var prompt = selectedPrompt;
        var requestEvent = selectedRequest;
        var ownerSeat = requestEvent.OwnerSeat;
        Require(prompt.IsPrivate && prompt.TargetSeat == ownerSeat && prompt.SourceSeat == ownerSeat &&
                gameWithResponse.CreateSnapshot(ownerSeat).PendingDecision is null &&
                Enumerable.Range(0, gameWithResponse.PlayerCount)
                    .Where(seat => seat != 1)
                    .All(seat => gameWithResponse.CreateSnapshot(seat).PendingDecision is null),
            "A response FactionSlash prompt must be private to exactly one Shu provider.");

        var beforeForgery = SnapshotJson.Serialize(gameWithResponse.CreateSnapshot(1, revealAll: true));
        var beforeForgeryRevision = gameWithResponse.Revision;
        var forged = gameWithResponse.Submit(new AnswerPromptCommand(
            1,
            prompt.PromptId,
            new ChoiceId("faction-slash.forged"),
            gameWithResponse.Revision));
        Require(!forged.Accepted && gameWithResponse.Revision == beforeForgeryRevision &&
                SnapshotJson.Serialize(gameWithResponse.CreateSnapshot(1, revealAll: true)) == beforeForgery,
            "A forged FactionSlash provider choice must be rejected atomically.");

        var paused = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(gameWithResponse.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(paused.CreateSnapshot(1, revealAll: true)) ==
                SnapshotJson.Serialize(gameWithResponse.CreateSnapshot(1, revealAll: true)) &&
                EventSignatures(paused).SequenceEqual(EventSignatures(gameWithResponse)),
            "A paused response FactionSlash provider prompt must replay exactly.");

        var slashChoice = prompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("response") == "faction-slash-slash");
        var slashCardId = slashChoice.Cards.Single();
        var answered = gameWithResponse.Submit(new AnswerPromptCommand(
            1,
            prompt.PromptId,
            slashChoice.Id,
            gameWithResponse.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? "The FactionSlash Slash response was rejected.");
        var resolved = gameWithResponse.Events.Select(envelope => envelope.Payload)
            .OfType<FactionSlashResolvedEvent>()
            .Last(item => item.ResolutionId == requestEvent.ResolutionId);
        Require(resolved is { Succeeded: true, IsActiveUse: false, ProviderSeat: 1 } &&
                resolved.OwnerSeat == ownerSeat && resolved.SlashCardId == slashCardId &&
                resolved.EffectiveSlashKind == CardKind.Slash &&
                gameWithResponse.CardMovements.Any(movement =>
                    movement.CardId == slashCardId &&
                    movement.From == CardLocation.Hand(1) &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.Respond) &&
                gameWithResponse.CardMovements.Any(movement =>
                    movement.CardId == slashCardId &&
                    movement.From == CardLocation.Processing &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.ResponseFinished) &&
                gameWithResponse.Events.Select(envelope => envelope.Payload).OfType<CardRespondedEvent>().Any(response =>
                    response.CardId == slashCardId && response.ResponderSeat == ownerSeat &&
                    response.EffectiveCardKind == CardKind.Slash),
            "Response FactionSlash must spend the provider's exact Slash while publishing Liu Bei as the responder.");

        var replayed = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(gameWithResponse.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(replayed.CreateSnapshot(1, revealAll: true)) ==
                SnapshotJson.Serialize(gameWithResponse.CreateSnapshot(1, revealAll: true)) &&
                EventSignatures(replayed).SequenceEqual(EventSignatures(gameWithResponse)),
            "A completed response FactionSlash branch must replay exactly.");
    }

    private static (ContentRegistry Registry, string ModeId) CreateFactionSlashFixtureRegistry(
        string suffix,
        IReadOnlyList<ContentDeckCardCount> cards)
    {
        var modeId = $"identity:classic-faction-slash-{suffix}-test";
        var deckId = $"test:faction-slash-{suffix}-deck";
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new SyntheticPackage(
                $"faction-slash-{suffix}-test",
                builder =>
                {
                    builder.AddDeck(new ContentDeckRecipe(
                        deckId,
                        $"激将{suffix}测试牌堆",
                        InitialHandSize: 4,
                        DrawPerTurn: 2,
                        Cards: cards));
                    builder.AddMode(new ContentModeDefinition(
                        modeId,
                        $"激将{suffix}测试身份局",
                        MinPlayers: 5,
                        MaxPlayers: 5,
                        RoleCounts: new Dictionary<string, int>
                        {
                            [nameof(Role.Lord)] = 1,
                            [nameof(Role.Loyalist)] = 1,
                            [nameof(Role.Rebel)] = 2,
                            [nameof(Role.Renegade)] = 1
                        },
                        DeckId: deckId,
                        GeneralCandidateCount: 5,
                        GeneralPoolIds:
                        [
                            "classic:liu-bei",
                            "standard:zhang-fei",
                            "standard:liu-bei",
                            "standard:zhuge-liang",
                            "classic:zhuge-liang"
                        ]));
                },
                new PackageDependency("standard-classic-generals", new Version(1, 5, 0))));
        return (registry, modeId);
    }

    private static GameEngine StartFactionSlashLordAtPlay(
        ContentRegistry registry,
        string modeId,
        int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = modeId,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 80
        }, registry);
        var started = game.Submit(new StartGameCommand());
        Require(started.Accepted && game.PendingDecision?.Choices.Any(choice =>
                choice.ContentIds.SequenceEqual(["classic:liu-bei"])) == true,
            started.Error?.Message ?? "The FactionSlash fixture did not offer classic Liu Bei.");
        var selected = game.Submit(new SelectGeneralCommand(
            0,
            "classic:liu-bei",
            game.Revision,
            game.PendingDecision!.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Classic Liu Bei selection was rejected.");
        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted && game.PendingDecision?.Kind == DecisionKind.PlayCard,
            advanced.Error?.Message ?? "The FactionSlash fixture did not reach Liu Bei's play phase.");
        return game;
    }

    public static void FormalGuoseAndLiuliFlow()
    {
        var registry = CreatePreProgramClassicRegistry();
        GameEngine? guoseGame = null;
        LegalAction? guoseAction = null;
        for (var seed = 1; seed <= 8_192 && guoseAction is null; seed++)
        {
            var candidate = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:da-qiao",
                GameCheckpoint.CurrentRulesVersion);
            guoseAction = candidate?.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Indulgence &&
                action.PlayedCardKind == CardKind.Indulgence &&
                action.CardId is { } cardId &&
                candidate.CreateSnapshot(0, revealAll: true).Players[0].Hand
                    .Concat(candidate.CreateSnapshot(0, revealAll: true).Players[0].Equipment)
                    .Any(card => card.Id == cardId && card.Suit == Suit.Diamond && card.Kind != CardKind.Indulgence));
            if (guoseAction is not null)
            {
                guoseGame = candidate;
            }
        }

        Require(guoseGame is not null && guoseAction is not null,
            "Could not find a deterministic Da Qiao Guose fixture.");
        var activeGuoseGame = guoseGame ?? throw new InvalidOperationException("Guose game missing.");
        var activeGuoseAction = guoseAction ?? throw new InvalidOperationException("Guose action missing.");
        var beforeGuose = activeGuoseGame.CreateCheckpoint();
        var physicalCard = activeGuoseGame.CreateSnapshot(0, revealAll: true).Players[0].Hand
            .Concat(activeGuoseGame.CreateSnapshot(0, revealAll: true).Players[0].Equipment)
            .Single(card => card.Id == activeGuoseAction.CardId);
        var used = activeGuoseGame.Submit(new PlayCardCommand(
            0,
            activeGuoseAction.CardId!.Value,
            activeGuoseAction.TargetSeats,
            activeGuoseGame.Revision,
            activeGuoseGame.PendingDecision!.PromptId,
            activeGuoseAction.PlayedCardKind)
        { ConversionSource = activeGuoseAction.ConversionSource });
        Require(used.Accepted &&
                physicalCard.Suit == Suit.Diamond &&
                activeGuoseGame.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>().Any(item =>
                    item.CardId == physicalCard.Id && item.CardKind == CardKind.Indulgence) &&
                activeGuoseGame.CardMovements.Any(item =>
                    item.CardId == physicalCard.Id && item.To == CardLocation.Processing),
            used.Error?.Message ?? "Guose must retain the diamond physical card while declaring Indulgence.");
        var liuliGame = FindDaQiaoLiuliFixture(registry);
        var prompt = liuliGame.PendingDecision!;
        var activate = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        var oldTarget = prompt.PlayerSeat;
        var activated = liuliGame.Submit(new AnswerPromptCommand(
            oldTarget,
            prompt.PromptId,
            activate.Id,
            liuliGame.Revision));
        Require(activated.Accepted && liuliGame.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: "classic:liuli" },
            activated.Error?.Message ?? "Liuli must request a legal redirect target after activation.");
        var targetPrompt = liuliGame.PendingDecision!;
        var redirect = targetPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-target");
        var selected = liuliGame.Submit(new AnswerPromptCommand(
            oldTarget, targetPrompt.PromptId, redirect.Id, liuliGame.Revision));
        Require(selected.Accepted && liuliGame.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: "classic:liuli" },
            selected.Error?.Message ?? "Liuli must request a hand or equipment payment.");
        var paymentPrompt = liuliGame.PendingDecision!;
        var payment = paymentPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card");
        var paidCardId = payment.Cards.Single();
        var answered = liuliGame.Submit(new AnswerPromptCommand(
            oldTarget, paymentPrompt.PromptId, payment.Id, liuliGame.Revision));
        Require(answered.Accepted &&
                liuliGame.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item.OwnerSeat == oldTarget && item.SkillId == "classic:liuli" &&
                    item.Activated && item.Completed) &&
                liuliGame.CardMovements.Any(item =>
                    item.CardId == paidCardId &&
                    item.Reason.Value == "skill-program.classic:liuli.SelectAndMoveOwnedCard" &&
                    item.To == CardLocation.DiscardPile),
            answered.Error?.Message ?? "Liuli must pay the selected card and redirect the same Slash.");
        var restored = GameReplay.Restore(liuliGame.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(liuliGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(liuliGame)),
            "The resolved Liuli branch must replay exactly.");
    }

    public static void FormalLijianAndBiyueFlow()
    {
        var registry = CreatePreProgramClassicRegistry();
        GameEngine? game = null;
        LegalAction? action = null;
        for (var seed = 1; seed <= 4_096 && action is null; seed++)
        {
            var candidate = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:diao-chan",
                GameCheckpoint.CurrentRulesVersion);
            action = candidate?.GetHumanLegalActions().SingleOrDefault(item =>
                item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == "classic:lijian");
            if (candidate is not null && action is not null &&
                !action.SelectableTargetSeats.Any(seat =>
                    candidate.CreateSnapshot(0, revealAll: true).Players
                        .Single(player => player.Seat == seat).Hand.Any(card => card.Kind == CardKind.Slash)))
            {
                action = null;
            }
            if (action is not null) game = candidate;
        }

        Require(game is not null && action is not null,
            "Could not find a deterministic Diao Chan Lijian fixture.");
        var active = game!;
        var lijian = action!;
        var cost = lijian.SelectableCardIds.First();
        var full = active.CreateSnapshot(0, revealAll: true);
        var responder = lijian.SelectableTargetSeats.First(seat =>
            full.Players.Single(player => player.Seat == seat).Hand.Any(card => card.Kind == CardKind.Slash));
        var source = lijian.SelectableTargetSeats.First(seat => seat != responder);
        var targets = new[] { source, responder };
        Require(targets.Length == 2 && targets.All(seat =>
                registry.Generals[active.CreateSnapshot(0, revealAll: true).Players
                    .Single(player => player.Seat == seat).GeneralId!].Gender == GeneralGender.Male),
            "Lijian must publish exactly male target candidates.");
        var used = active.Submit(new UseProgramSkillCommand(
            0,
            "classic:lijian", lijian.ProgramActivationId!,
            [cost],
            targets,
            active.Revision,
            active.PendingDecision!.PromptId));
        Require(used.Accepted &&
                active.CardMovements.Any(move =>
                    move.CardId == cost && move.Reason.Value == "skill-program.classic:lijian.DiscardSelected" &&
                    move.To == CardLocation.DiscardPile) &&
                active.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame =>
                    frame.SkillId == "classic:lijian") &&
                active.ResolutionStack.LastOrDefault() is ResponseWindowFrame
                {
                    IncomingCard: CardKind.Duel,
                    RequiredCardKind: CardKind.Slash
                },
            used.Error?.Message ?? $"Lijian must discard its exact cost and open a virtual Duel without Nullification " +
            $"(pending={active.PendingDecision?.Kind}, incoming={active.PendingDecision?.IncomingCard}, " +
            $"frames={string.Join(',', active.ResolutionStack.Select(frame => $"{frame.Kind}/{frame.Step}"))}, " +
            $"moves={string.Join(',', active.CardMovements.Where(move => move.CardId == cost).Select(move => move.Reason.Value))}).");
        for (var step = 0; step < 32 && active.ResolutionStack.Count > 0; step++)
        {
            var continued = active.Submit(new AdvanceOneStepCommand(active.Revision));
            Require(continued.Accepted, continued.Error?.Message ?? "The Lijian Duel could not continue.");
        }
        Require(active.ResolutionStack.Count == 0,
            "The Lijian Duel must close its active-skill and response frames.");
        var restored = GameReplay.Restore(active.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(active.CreateSnapshot(0, revealAll: true)),
            "A completed Lijian Duel must replay exactly.");

        var biyueRegistry = StandardContentRegistry.CreateWithClassicGenerals();
        var biyue = StartClassicGeneralAtPlay(
            biyueRegistry,
            1,
            "classic:diao-chan",
            GameCheckpoint.CurrentRulesVersion) ?? SelectGeneral(biyueRegistry, "classic:diao-chan", GameCheckpoint.CurrentRulesVersion);
        if (biyue.PendingDecision?.Kind != DecisionKind.PlayCard)
        {
            var reached = biyue.Submit(new AdvanceCommand(biyue.Revision));
            Require(reached.Accepted, reached.Error?.Message ?? "Diao Chan did not reach play.");
        }
        var ended = biyue.Submit(new EndPlayPhaseCommand(0, biyue.Revision, biyue.PendingDecision!.PromptId));
        Require(ended.Accepted, ended.Error?.Message ?? "Diao Chan could not end play.");
        var advanced = biyue.Submit(new AdvanceCommand(biyue.Revision));
        Require(advanced.Accepted && biyue.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, SkillPrompt.SkillId: "classic:biyue" },
            advanced.Error?.Message ?? "Diao Chan must receive the optional Biyue end-phase prompt.");
        var prompt = biyue.PendingDecision!;
        var handBefore = biyue.CreateSnapshot(0, revealAll: true).Players[0].Hand.Count;
        var drew = biyue.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate").Id,
            biyue.Revision));
        Require(drew.Accepted && biyue.CardMovements.Any(move =>
                    move.Reason.Value == "skill-program.classic:biyue.Draw") &&
                biyue.CreateSnapshot(0, revealAll: true).Players[0].Hand.Count == handBefore + 1,
            drew.Error?.Message ?? "Biyue must draw exactly one card before ending the turn.");
    }

    public static void FormalJieyinAndXiaojiFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        GameEngine? xiaojiGame = null;
        LegalAction[]? equipmentActions = null;
        for (var seed = 1; seed <= 8_192 && equipmentActions is null; seed++)
        {
            var candidate = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:sun-shangxiang",
                GameCheckpoint.CurrentRulesVersion);
            if (candidate is null) continue;
            var equipments = candidate.GetHumanLegalActions()
                .Where(action => action.Kind == LegalActionKind.Equip && action.CardId is not null)
                .Select(action => new
                {
                    Action = action,
                    Slot = EquipmentCatalog.Get(candidate.CreateSnapshot(0, revealAll: true).Players[0].Hand
                        .Single(card => card.Id == action.CardId).Kind).Slot
                })
                .GroupBy(item => item.Slot)
                .Select(group => group.Select(item => item.Action).Take(2).ToArray())
                .FirstOrDefault(group => group.Length == 2);
            if (equipments is not null)
            {
                xiaojiGame = candidate;
                equipmentActions = equipments;
            }
        }

        Require(xiaojiGame is not null && equipmentActions is { Length: 2 },
            "Could not find a deterministic Sun Shangxiang equipment-replacement fixture.");
        var xiaoji = xiaojiGame!;
        foreach (var equipment in equipmentActions!)
        {
            if (xiaoji.PendingDecision is null)
            {
                var continued = xiaoji.Submit(new AdvanceCommand(xiaoji.Revision));
                Require(continued.Accepted && xiaoji.PendingDecision?.Kind == DecisionKind.PlayCard,
                    continued.Error?.Message ?? "Sun Shangxiang did not return to play after equipping.");
            }
            var prompt = xiaoji.PendingDecision!;
            var equipped = xiaoji.Submit(new PlayCardCommand(
                0,
                equipment.CardId!.Value,
                [],
                xiaoji.Revision,
                prompt.PromptId));
            Require(equipped.Accepted, equipped.Error?.Message ?? "Sun Shangxiang could not equip the fixture card.");
        }

        Require(xiaoji.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0,
                  SkillPrompt.SkillId: "classic:xiaoji" } &&
                xiaoji.PendingDecision.Choices.Count == 2,
            "Replacing Sun Shangxiang's equipment must publish an optional Xiaoji prompt.");
        var xiaojiPrompt = xiaoji.PendingDecision!;
        var paused = xiaoji.CreateCheckpoint();
        var pausedRestore = GameReplay.Restore(paused, registry);
        Require(pausedRestore.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: "classic:xiaoji" },
            "A paused Xiaoji prompt must replay exactly.");
        var handBeforeXiaoji = xiaoji.CreateSnapshot(0, revealAll: true).Players[0].Hand.Count;
        var drew = xiaoji.Submit(new AnswerPromptCommand(
            0,
            xiaojiPrompt.PromptId,
            xiaojiPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate").Id,
            xiaoji.Revision));
        Require(drew.Accepted &&
                xiaoji.CardMovements.Count(move =>
                    move.Reason.Value == "skill-program.classic:xiaoji.Draw") == 2 &&
                xiaoji.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item.OwnerSeat == 0 && item.SkillId == "classic:xiaoji" &&
                    item.Window == SkillProgramTriggerWindow.CardsMoved &&
                    item.Activated && item.Completed) &&
                xiaoji.CreateSnapshot(0, revealAll: true).Players[0].Hand.Count == handBeforeXiaoji + 2,
            drew.Error?.Message ?? "Xiaoji must draw exactly two cards after one equipment leaves.");

        var jieyin = SelectGeneral(registry, "classic:sun-shangxiang", GameCheckpoint.CurrentRulesVersion);
        var reached = jieyin.Submit(new AdvanceCommand(jieyin.Revision));
        Require(reached.Accepted && jieyin.PendingDecision?.Kind == DecisionKind.PlayCard,
            reached.Error?.Message ?? "Sun Shangxiang did not reach her play phase.");
        var full = jieyin.CreateSnapshot(0, revealAll: true);
        var maleTarget = full.Players.First(player =>
            player.Seat != 0 &&
            registry.Generals[player.GeneralId!].Gender == GeneralGender.Male);
        SetRuntimeHp(jieyin, 0, full.Players[0].MaxHp - 1);
        SetRuntimeHp(jieyin, maleTarget.Seat, maleTarget.MaxHp - 1);
        var jieyinAction = jieyin.GetHumanLegalActions().Single(action =>
            action.Kind == LegalActionKind.UseProgramSkill && action.ProgramSkillId == "classic:jieyin");
        var cost = jieyinAction.SelectableCardIds.Take(2).ToArray();
        var used = jieyin.Submit(new UseProgramSkillCommand(
            0,
            "classic:jieyin", jieyinAction.ProgramActivationId!,
            cost,
            [maleTarget.Seat],
            jieyin.Revision,
            jieyin.PendingDecision!.PromptId));
        var after = jieyin.CreateSnapshot(0, revealAll: true);
        Require(used.Accepted &&
                after.Players[0].Hp == after.Players[0].MaxHp &&
                after.Players[maleTarget.Seat].Hp == after.Players[maleTarget.Seat].MaxHp &&
                jieyin.CardMovements.Count(move =>
                    cost.Contains(move.CardId) &&
                    move.Reason.Value == "skill-program.classic:jieyin.DiscardSelected" &&
                    move.To == CardLocation.DiscardPile) == 2,
            used.Error?.Message ?? "Jieyin must discard exactly two hand cards and recover both characters.");
    }

    public static void FormalQianxunAndLianyingFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var qianxun = registry.GetSkill("classic:qianxun").Program!.CardPolicies.Single(policy =>
            policy.Kind == SkillProgramCardPolicyKind.ProhibitTarget);
        Require(qianxun.CardKinds.Contains(CardKind.Snatch) &&
                qianxun.CardKinds.Contains(CardKind.Indulgence) &&
                !qianxun.CardKinds.Contains(CardKind.Dismantlement),
            "Qianxun must prohibit Snatch and Indulgence without blocking other tricks.");

        GameEngine? game = null;
        LegalAction? equipment = null;
        for (var seed = 1; seed <= 8_192 && equipment is null; seed++)
        {
            var candidate = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:lu-xun",
                GameCheckpoint.CurrentRulesVersion);
            var action = candidate?.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.Equip && item.CardId is not null);
            if (candidate is not null && action is not null)
            {
                game = candidate;
                equipment = action;
            }
        }

        Require(game is not null && equipment?.CardId is not null,
            "Could not find a deterministic Lu Xun last-hand equipment fixture.");
        var current = game!;
        var lastCardId = equipment!.CardId!.Value;
        KeepOnlyHandCard(current, 0, lastCardId);
        var handBefore = current.CreateSnapshot(0, revealAll: true).Players[0].Hand.Count;
        var equipped = current.Submit(new PlayCardCommand(
            0,
            lastCardId,
            [],
            current.Revision,
            current.PendingDecision!.PromptId));
        Require(equipped.Accepted &&
                current.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0,
                  SkillPrompt.SkillId: "classic:lianying" } &&
                current.PendingDecision.Choices.Count == 2,
            equipped.Error?.Message ?? "Losing Lu Xun's last hand card must publish an optional Lianying prompt.");
        var prompt = current.PendingDecision!;
        var drew = current.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate").Id,
            current.Revision));
        Require(drew.Accepted && handBefore == 1 &&
                current.CardMovements.Count(move =>
                    move.Reason.Value == "skill-program.classic:lianying.Draw") == 1 &&
                current.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item.OwnerSeat == 0 && item.SkillId == "classic:lianying" &&
                    item.Window == SkillProgramTriggerWindow.CardsMoved &&
                    item.Activated && item.Completed) &&
                current.CreateSnapshot(0, revealAll: true).Players[0].Hand.Count == 1,
            drew.Error?.Message ?? "Lianying must draw exactly one card after the last hand card is lost.");

    }

    private static void KeepOnlyHandCard(GameEngine game, int seat, int keptCardId)
    {
        var cardZonesField = typeof(GameEngine).GetField("_cardZones", BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The runtime card-zone store was not found.");
        var cardZones = cardZonesField.GetValue(game) ??
            throw new InvalidOperationException("The runtime card-zone store is unavailable.");
        var move = cardZones.GetType().GetMethod("Move", BindingFlags.Public | BindingFlags.Instance) ??
            throw new InvalidOperationException("The runtime card-zone move method was not found.");
        var handIds = game.CreateSnapshot(seat, revealAll: true).Players[seat].Hand
            .Select(card => card.Id)
            .Where(cardId => cardId != keptCardId)
            .ToArray();
        foreach (var cardId in handIds)
        {
            _ = move.Invoke(cardZones, [cardId, CardLocation.Hand(seat), CardLocation.DiscardPile]);
        }
    }

    private static void SetRuntimeHp(GameEngine game, int seat, int hp)
    {
        var playersField = typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The runtime player list was not found.");
        var players = (System.Collections.IList)(playersField.GetValue(game) ??
            throw new InvalidOperationException("The runtime player list is unavailable."));
        var player = players[seat] ?? throw new InvalidOperationException("The runtime player is unavailable.");
        var hpProperty = player.GetType().GetProperty("Hp") ??
            throw new InvalidOperationException("The runtime HP property was not found.");
        hpProperty.SetValue(player, hp);
    }

    private static GameEngine FindDaQiaoLiuliFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 8_192; seed++)
        {
            var game = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:da-qiao",
                GameCheckpoint.CurrentRulesVersion);
            if (game is null)
            {
                continue;
            }

            for (var boundary = 0; boundary < 80 && game.State.Status != EngineStatus.Completed; boundary++)
            {
                if (game.PendingDecision is
                    { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0,
                      SkillPrompt.SkillId: "classic:liuli" } liuli &&
                    liuli.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("program-action") == "activate"))
                {
                    return game;
                }

                CommandResult result;
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } play)
                {
                    result = game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId));
                }
                else
                {
                    result = game.Submit(new AdvanceCommand(game.Revision));
                }
                Require(result.Accepted, result.Error?.Message ?? "Could not advance the Da Qiao Liuli fixture.");
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Da Qiao Liuli fixture.");
    }

    private static GameEngine ReachZhouYuPlayPhase(ContentRegistry registry, int rulesVersion)
    {
        var game = SelectGeneral(registry, "classic:zhou-yu", rulesVersion);
        var reachedYingzi = game.Submit(new AdvanceCommand(game.Revision));
        Require(reachedYingzi.Accepted,
            reachedYingzi.Error?.Message ?? "Could not advance Zhou Yu through the draw phase.");
        if (game.PendingDecision is not
            { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: "classic:yingzi" })
        {
            Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
                "A historical package without the migrated Yingzi program must not revive its removed choice route.");
            return game;
        }
        var prompt = game.PendingDecision!;
        var skipped = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "skip").Id,
            game.Revision));
        Require(skipped.Accepted, skipped.Error?.Message ?? "Could not skip Yingzi for the Fanjian fixture.");
        var reachedPlay = game.Submit(new AdvanceCommand(game.Revision));
        Require(reachedPlay.Accepted && game.PendingDecision?.Kind == DecisionKind.PlayCard,
            reachedPlay.Error?.Message ?? "Could not reach Zhou Yu's play phase.");
        return game;
    }

    private static bool DriveUntilOwnLightningJudgment(
        GameEngine game,
        bool expectTiandu,
        out JudgmentResolvedEvent judgment)
    {
        var seenEventCount = game.Events.Count;
        for (var step = 0; step < 2_000 && game.State.Status != EngineStatus.Completed; step++)
        {
            var resolved = game.Events
                .Skip(seenEventCount)
                .Select(envelope => envelope.Payload)
                .OfType<JudgmentResolvedEvent>()
                .LastOrDefault(candidate =>
                    candidate.TargetSeat == 0 && candidate.Reason == JudgmentReasons.Lightning);
            if (resolved is not null)
            {
                if (!expectTiandu || game.PendingDecision?.Kind == DecisionKind.ProgramTrigger)
                {
                    judgment = resolved;
                    return true;
                }
            }

            DeclineOrAdvance(game);
        }

        judgment = null!;
        return false;
    }

    private static GameEngine? StartClassicGeneralAtPlay(
        ContentRegistry registry,
        int seed,
        string generalId,
        int rulesVersion)
    {
        var game = CreateInteractive(registry, seed);
        if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
        {
            game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
        }

        var started = game.Submit(new StartGameCommand());
        Require(started.Accepted, started.Error?.Message ?? "Classic active-skill fixture failed to start.");
        if (started.Result.PendingDecision?.Choices.Any(choice =>
                choice.ContentIds.SequenceEqual([generalId])) != true)
        {
            return null;
        }

        var selected = game.Submit(new SelectGeneralCommand(
            0,
            generalId,
            game.Revision,
            game.PendingDecision!.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? $"Could not select {generalId}.");
        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted, advanced.Error?.Message ?? "Classic active-skill setup did not advance.");
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Classic active-skill fixture did not stop at the human play phase.");
        return game;
    }

    private static GameEngine FindLuMengSlashFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:lu-meng",
                GameCheckpoint.CurrentRulesVersion);
            if (game?.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Slash) == true)
            {
                return game;
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Lu Meng Slash fixture.");
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static IReadOnlyList<string> EventTrace(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|" +
            System.Text.Json.JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))
        .ToArray();

    private static bool IsLuoyiActivationPrompt(PendingDecision? prompt) =>
        prompt is { Kind: DecisionKind.ProgramTrigger } ||
        prompt is { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: "classic:luoyi" };

    private static PromptChoice GetLuoyiActivateChoice(PendingDecision prompt) =>
        prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault(
                prompt.Kind == DecisionKind.ProgramTrigger ? "program-action" : "action") ==
            (prompt.Kind == DecisionKind.ProgramTrigger ? "activate" : "luoyi-use"));

    private static (GameEngine Game, LegalAction Action, int TargetHp) FindXuChuDirectAttackFixture(
        ContentRegistry registry,
        LegalActionKind actionKind,
        Func<PlayerSnapshot, bool> targetPredicate,
        bool requireNoNullification = false)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Xu Chu attack fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:xu-chu"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:xu-chu",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Xu Chu.");
            var reachedLuoyi = game.Submit(new AdvanceCommand(game.Revision));
            Require(reachedLuoyi.Accepted, reachedLuoyi.Error?.Message ?? "Xu Chu did not reach Luoyi.");
            var luoyi = game.PendingDecision;
            if (!IsLuoyiActivationPrompt(luoyi))
            {
                continue;
            }

            var used = game.Submit(new AnswerPromptCommand(
                0,
                luoyi.PromptId,
                GetLuoyiActivateChoice(luoyi).Id,
                game.Revision));
            Require(used.Accepted, used.Error?.Message ?? "Could not enable Luoyi for the attack fixture.");
            var reachedPlay = game.Submit(new AdvanceCommand(game.Revision));
            Require(reachedPlay.Accepted, reachedPlay.Error?.Message ?? "Xu Chu did not reach the play phase.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            if (requireNoNullification && full.Players.Any(player =>
                    player.Hand.Any(card => card.Kind == CardKind.Nullification)))
            {
                continue;
            }

            var action = game.GetHumanLegalActions()
                .Where(candidate => candidate.Kind == actionKind && candidate.TargetSeat is not null)
                .FirstOrDefault(candidate => targetPredicate(
                    full.Players.Single(player => player.Seat == candidate.TargetSeat)));
            if (action is null)
            {
                continue;
            }

            var target = full.Players.Single(player => player.Seat == action.TargetSeat);
            return (game, action, target.Hp);
        }

        throw new InvalidOperationException($"Could not find a deterministic Xu Chu {actionKind} fixture.");
    }

    private static (GameEngine Game, int PhysicalCardId, int TargetSeat, int DistanceTwoSeat)
        FindXuHuangDuanliangFixture(ContentRegistry registry)
    {
        var offered = 0;
        var equippedCandidates = 0;
        var convertedCandidates = 0;
        var placedCandidates = 0;
        var resolvedCandidates = 0;
        string? lastError = null;
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Xu Huang fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:xu-huang"])) != true)
            {
                continue;
            }
            offered++;

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:xu-huang",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Xu Huang.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Xu Huang did not reach the play phase.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            if (full.Players.SelectMany(player => player.Hand).Any(card => card.Kind == CardKind.Nullification))
            {
                continue;
            }

            var equipment = full.Players.Single(player => player.Seat == 0).Hand
                .Where(card =>
                    EquipmentCatalog.IsEquipment(card.Kind) &&
                    card.Suit is Suit.Spade or Suit.Club &&
                    EquipmentCatalog.Get(card.Kind).Slot != EquipmentSlot.OffensiveHorse)
                .OrderBy(card => card.Id)
                .FirstOrDefault();
            if (equipment is null)
            {
                continue;
            }
            equippedCandidates++;

            Equip(game, equipment.Id);
            var converted = game.GetHumanLegalActions()
                .Where(action =>
                    action.Kind == LegalActionKind.SupplyShortage &&
                    action.CardId == equipment.Id &&
                    action.PlayedCardKind == CardKind.SupplyShortage)
                .ToArray();
            var targetAction = converted.FirstOrDefault(action => action.TargetSeat == 1);
            var distanceTwoAction = converted.FirstOrDefault(action =>
                action.TargetSeat is { } seat && game.GetCombatDistance(0, seat) == 2);
            if (targetAction is null || distanceTwoAction?.TargetSeat is not { } distanceTwoSeat)
            {
                continue;
            }
            convertedCandidates++;

            var simulated = GameReplay.Restore(game.CreateCheckpoint(), registry);
            var simulatedAction = simulated.GetHumanLegalActions().Single(action =>
                action.Kind == LegalActionKind.SupplyShortage &&
                action.CardId == equipment.Id &&
                action.TargetSeat == 1 &&
                action.PlayedCardKind == CardKind.SupplyShortage);
            var used = simulated.Submit(new PlayCardCommand(
                0,
                equipment.Id,
                simulatedAction.TargetSeats,
                simulated.Revision,
                simulated.PendingDecision!.PromptId,
                CardKind.SupplyShortage)
            { ConversionSource = simulatedAction.ConversionSource });
            if (!used.Accepted ||
                simulated.Events.Select(item => item.Payload).OfType<DelayedCardPlacedEvent>()
                    .All(placed => placed.CardId != equipment.Id))
            {
                lastError = used.Error?.Message ??
                    $"placement accepted={used.Accepted}, status={used.Status}, " +
                    $"pending={simulated.PendingDecision?.Kind}, " +
                    $"events={string.Join(',', simulated.Events.TakeLast(4).Select(item => item.Payload.GetType().Name))}";
                continue;
            }
            var returnedToPlay = simulated.Submit(new AdvanceCommand(simulated.Revision));
            if (!returnedToPlay.Accepted || simulated.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                lastError = returnedToPlay.Error?.Message ??
                    $"return-to-play accepted={returnedToPlay.Accepted}, status={returnedToPlay.Status}, " +
                    $"pending={simulated.PendingDecision?.Kind}";
                continue;
            }
            placedCandidates++;

            var ended = simulated.Submit(new EndPlayPhaseCommand(
                0,
                simulated.Revision,
                simulated.PendingDecision.PromptId));
            var resolved = ended.Accepted
                ? AdvanceUntilDelayedCardResolves(simulated, equipment.Id)
                : null;
            if (!ended.Accepted || resolved?.SkippedDrawPhase != true)
            {
                lastError = ended.Error?.Message ?? $"resolution pending={simulated.PendingDecision?.Kind}";
                continue;
            }
            resolvedCandidates++;

            return (game, equipment.Id, 1, distanceTwoSeat);
        }

        throw new InvalidOperationException(
            $"Could not find a deterministic Xu Huang Duanliang equipment fixture " +
            $"(offered={offered}, equipment={equippedCandidates}, converted={convertedCandidates}, " +
            $"placed={placedCandidates}, resolved={resolvedCandidates}, last={lastError ?? "none"}).");
    }

    private static DelayedCardResolvedEvent? AdvanceUntilDelayedCardResolves(
        GameEngine game,
        int cardId)
    {
        for (var step = 0; step < 2_000 && game.State.Status != EngineStatus.Completed; step++)
        {
            var resolved = game.Events.Select(item => item.Payload)
                .OfType<DelayedCardResolvedEvent>()
                .LastOrDefault(item => item.CardId == cardId);
            if (resolved is not null)
            {
                return resolved;
            }

            if (game.PendingDecision?.PlayerSeat == 0)
            {
                return null;
            }

            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            if (!advanced.Accepted)
            {
                return null;
            }
        }

        return game.Events.Select(item => item.Payload)
            .OfType<DelayedCardResolvedEvent>()
            .LastOrDefault(item => item.CardId == cardId);
    }

    private static (GameEngine Game, LegalAction Action, int TargetSeat, int WeaponCardId)
        FindDianWeiQiangxiFixture(
        ContentRegistry registry,
        bool requireWeapon,
        string? targetSkill = null,
        bool requirePeach = false,
        bool requireTargetHeart = false)
    {
        var damageTriggerSkills = new HashSet<string>
        {
            "standard:feedback",
            "classic:yiji",
            "classic:jieming",
            "standard:yuanhu",
            "classic:ganglie"
        };
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Dian Wei fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:dian-wei"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:dian-wei",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Dian Wei.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Dian Wei did not reach the play phase.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
                candidate.Kind == LegalActionKind.UseProgramSkill &&
                candidate.ProgramSkillId == "classic:qiangxi" &&
                candidate.ProgramActivationId == "discard-weapon-and-damage");
            if (action is null)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            var source = full.Players.Single(player => player.Seat == 0);
            var weaponCardId = source.Hand
                .Where(card => action.SelectableCardIds.Contains(card.Id))
                .Select(card => card.Id)
                .FirstOrDefault();
            if (requireWeapon && weaponCardId == 0)
            {
                continue;
            }
            if (requirePeach && source.Hand.All(card => card.Kind != CardKind.Peach))
            {
                continue;
            }

            var target = full.Players
                .Where(player => action.SelectableTargetSeats.Contains(player.Seat))
                .FirstOrDefault(player =>
                    (targetSkill is { } required
                        ? player.Skills?.Any(skill => skill.ContentId == required) == true
                        : player.Skills?.All(skill => (skill.ContentId is null || !damageTriggerSkills.Contains(skill.ContentId))) != false) &&
                    (!requireTargetHeart || player.Hand.Any(card => card.Suit is Suit.Heart or Suit.Spade)));
            if (target is null)
            {
                continue;
            }

            return (game, action, target.Seat, weaponCardId);
        }

        throw new InvalidOperationException(
            $"Could not find a deterministic Dian Wei Qiangxi fixture " +
            $"(weapon={requireWeapon}, peach={requirePeach}, " +
            $"target-skill={targetSkill?.ToString() ?? "none"}, target-heart={requireTargetHeart}).");
    }

    private static void SetPlayerHp(GameEngine game, int seat, int hp)
    {
        var playersField = typeof(GameEngine).GetField(
            "_players",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine player store was not found.");
        var players = (System.Collections.IList)playersField.GetValue(game)!;
        players[seat]!.GetType().GetProperty("Hp")!.SetValue(players[seat], hp);
    }

    private static (GameEngine Game, int TargetSeat, long ResolutionId, int XuChuHp) FindXuChuReverseDuelFixture(
        ContentRegistry registry)
    {
        var offered = 0;
        var noNullification = 0;
        var duelFound = 0;
        var responsePromptFound = 0;
        var slashChoiceFound = 0;
        var lastPending = "none";
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Reverse Duel fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:xu-chu"])) != true)
            {
                continue;
            }
            offered++;

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:xu-chu",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Xu Chu.");
            var reachedLuoyi = game.Submit(new AdvanceCommand(game.Revision));
            Require(reachedLuoyi.Accepted && IsLuoyiActivationPrompt(game.PendingDecision),
                reachedLuoyi.Error?.Message ?? "Reverse Duel fixture did not reach Luoyi.");
            var luoyi = game.PendingDecision!;
            var used = game.Submit(new AnswerPromptCommand(
                0,
                luoyi.PromptId,
                GetLuoyiActivateChoice(luoyi).Id,
                game.Revision));
            Require(used.Accepted, used.Error?.Message ?? "Could not enable Luoyi for reverse Duel.");
            var reachedPlay = game.Submit(new AdvanceCommand(game.Revision));
            Require(reachedPlay.Accepted, reachedPlay.Error?.Message ?? "Reverse Duel fixture did not reach play.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            if (full.Players.Any(player => player.Hand.Any(card => card.Kind == CardKind.Nullification)))
            {
                continue;
            }
            noNullification++;

            var duelAction = game.GetHumanLegalActions()
                .Where(action => action.Kind == LegalActionKind.Duel && action.TargetSeat is not null)
                .FirstOrDefault(action => full.Players.Single(player => player.Seat == action.TargetSeat)
                    .Hand.Any(card => card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash));
            if (duelAction is null)
            {
                continue;
            }
            duelFound++;

            var targetSeat = duelAction.TargetSeat!.Value;
            var played = game.Submit(new PlayCardCommand(
                0,
                duelAction.CardId!.Value,
                duelAction.TargetSeats,
                game.Revision,
                game.PendingDecision.PromptId,
                duelAction.PlayedCardKind));
            if (!played.Accepted)
            {
                continue;
            }

            var hostPending = GetHostPendingDecision(game);
            lastPending = $"{hostPending?.Kind.ToString() ?? "none"}/" +
                $"{hostPending?.PlayerSeat.ToString() ?? "none"}/target-{targetSeat}/" +
                $"status-{game.State.Status}/stack-{string.Join(',', game.ResolutionStack.Select(frame => frame.Kind))}";

            var resolutionId = game.Events.Select(item => item.Payload)
                .OfType<CardUseDeclaredEvent>()
                .Last(item => item.CardId == duelAction.CardId).ResolutionId;
            if (hostPending is not { Kind: DecisionKind.RespondSlash } targetPrompt ||
                targetPrompt.PlayerSeat != targetSeat)
            {
                continue;
            }
            responsePromptFound++;

            var slashChoice = targetPrompt.Choices.FirstOrDefault(choice =>
                choice.Parameters.GetValueOrDefault("response") == "slash");
            if (slashChoice?.Cards.Count != 1)
            {
                continue;
            }
            slashChoiceFound++;

            ResolveSyntheticDuelSlash(game, targetSeat, slashChoice);
            if (game.PendingDecision is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 } prompt &&
                prompt.IncomingCard == CardKind.Duel)
            {
                var xuChuHp = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hp;
                return (game, targetSeat, resolutionId, xuChuHp);
            }
        }

        throw new InvalidOperationException(
            $"Could not find a deterministic Luoyi reverse-Duel fixture " +
            $"(offered={offered}, no-null={noNullification}, duel={duelFound}, prompt={responsePromptFound}, slash={slashChoiceFound}, last={lastPending}).");
    }

    private static void ResolveSyntheticDuelSlash(GameEngine game, int responderSeat, PromptChoice choice)
    {
        var slashCardId = choice.Cards.Single();
        var duelField = typeof(GameEngine).GetField(
            "_pendingDuel",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine Duel continuation was not found.");
        var duel = duelField.GetValue(game) ??
            throw new InvalidOperationException("The reverse-Duel fixture lost its continuation.");
        var playersField = typeof(GameEngine).GetField(
            "_players",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine player store was not found.");
        var players = (System.Collections.IList)playersField.GetValue(game)!;
        var responder = players[responderSeat]!;
        var getHand = typeof(GameEngine).GetMethod(
            "GetHand",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine hand accessor was not found.");
        var slash = ((System.Collections.IEnumerable)getHand.Invoke(game, [responder])!)
            .Cast<Card>()
            .Single(card => card.Id == slashCardId);
        var resolutionId = game.ResolutionStack.OfType<CardUseFrame>()
            .Single(frame => frame.CardKind == CardKind.Duel).Id;
        var popResponse = typeof(GameEngine).GetMethod(
            "PopResponseWindow",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine response-window popper was not found.");
        popResponse.Invoke(game, [resolutionId]);
        var setCardUseStep = typeof(GameEngine).GetMethod(
            "SetCardUseStep",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine card-use cursor updater was not found.");
        setCardUseStep.Invoke(game, [resolutionId, ResolutionFrameStep.ResolvingEffect]);
        var clearPending = typeof(GameEngine).GetMethod(
            "ClearPendingDecision",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine pending-decision clearer was not found.");
        typeof(GameEngine).GetMethod("CaptureSelectedResponseConversion", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [choice]);
        clearPending.Invoke(game, null);
        var resolve = typeof(GameEngine).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == "ResolveDuelResponse" && method.GetParameters().Length == 3);
        resolve.Invoke(game, [duel, responder, slash]);
    }

    private static PendingDecision? GetHostPendingDecision(GameEngine game)
    {
        var decisionField = typeof(GameEngine).GetField(
            "_pendingDecision",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine pending-decision store was not found.");
        return (PendingDecision?)decisionField.GetValue(game);
    }

    private static GameEngine FindZhenJiFirstBlackLuoshenFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Zhen Ji fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:zhen-ji"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:zhen-ji",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Zhen Ji.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Zhen Ji did not reach Luoshen.");
            if (game.PendingDecision is not { Kind: DecisionKind.ProgramRepeatJudgment } prompt)
            {
                continue;
            }

            var used = game.Submit(new AnswerPromptCommand(
                0,
                prompt.PromptId,
                prompt.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "luoshen-use").Id,
                game.Revision));
            Require(used.Accepted, used.Error?.Message ?? "Could not use Luoshen.");
            if (game.PendingDecision is null && game.State.Phase != TurnPhase.Play)
            {
                var resolvedAi = game.Submit(new AdvanceCommand(game.Revision));
                Require(resolvedAi.Accepted, resolvedAi.Error?.Message ?? "Could not resolve Luoshen replacement choices.");
            }

            var judgment = game.Events.Select(item => item.Payload)
                .OfType<JudgmentResolvedEvent>()
                .LastOrDefault(item => item.Reason == JudgmentReasons.Luoshen);
            if (game.PendingDecision is { Kind: DecisionKind.ProgramRepeatJudgment } &&
                judgment is { Suit: Suit.Spade or Suit.Club, Succeeded: true })
            {
                return game;
            }
        }

        throw new InvalidOperationException("Could not find a deterministic first-black Luoshen fixture.");
    }

    private static (
        GameEngine Game,
        GameCheckpoint BeforeDamage,
        LegalAction Action,
        int EventCount,
        int TargetSeat,
        int SourceHpBefore,
        int Distance) FindWeiYanKuangguFixture(
            ContentRegistry registry,
            int rulesVersion = GameCheckpoint.CurrentRulesVersion)
    {
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = CreateInteractive(registry, seed, Role.Rebel);
            if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
            {
                game = GameReplay.Restore(
                    game.CreateCheckpoint() with { RulesVersion = rulesVersion },
                    registry);
            }
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Wei Yan fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:wei-yan"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:wei-yan",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Wei Yan.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Wei Yan setup did not advance.");
            var result = advanced.Result;
            for (var step = 0; result.Status != EngineStatus.Completed && step < 1_200; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } prompt)
                {
                    if (game.Events.Any(item => item.Payload is StoneAxeResolvedEvent))
                    {
                        break;
                    }

                    var full = game.CreateSnapshot(0, revealAll: true);
                    var source = full.Players.Single(player => player.Seat == 0);
                    if (source.Hp > 0 && source.Hp < source.MaxHp)
                    {
                        var candidate = game.GetHumanLegalActions()
                            .Where(action => action.Kind == LegalActionKind.Slash &&
                                             action.CardId is not null &&
                                             action.TargetSeat is not null)
                            .Select(action => new
                            {
                                Action = action,
                                Target = full.Players.Single(player => player.Seat == action.TargetSeat),
                                Distance = game.GetCombatDistance(0, action.TargetSeat!.Value)
                            })
                            .Where(item => item.Distance == 1 &&
                                           item.Target.Hand.All(card => card.Kind != CardKind.Dodge) &&
                                           item.Target.Equipment.All(card =>
                                               card.Kind is not (CardKind.BaguaFormation or CardKind.RenwangShield)) &&
                                           item.Target.Skills?.All(skill =>
                                               skill.ContentId is not ("classic:qingguo" or "classic:longdan" or "classic:hujia")) != false)
                            .OrderBy(item => item.Action.CardId)
                            .ThenBy(item => item.Action.TargetSeat)
                            .FirstOrDefault();
                        if (candidate is not null)
                        {
                            var beforeDamage = GameCheckpointJson.Deserialize(
                                GameCheckpointJson.Serialize(game.CreateCheckpoint()));
                            var eventCount = game.Events.Count;
                            var played = SubmitPlayAction(game, candidate.Action);
                            if (!played.Accepted)
                            {
                                continue;
                            }

                            if (rulesVersion >= 38 &&
                                !game.Events.Skip(eventCount).Any(item =>
                                    item.Payload is RecoveryAppliedEvent recovery &&
                                    recovery.SourceSeat == 0 && recovery.TargetSeat == 0))
                            {
                                var resolved = game.Submit(new AdvanceOneStepCommand(game.Revision));
                                if (!resolved.Accepted)
                                {
                                    continue;
                                }
                            }

                            var newEvents = game.Events.Skip(eventCount)
                                .Select(item => item.Payload)
                                .ToArray();
                            if ((rulesVersion >= 38 && newEvents.Any(item =>
                                    item is RecoveryAppliedEvent recovery &&
                                    recovery.SourceSeat == 0 && recovery.TargetSeat == 0)) ||
                                (rulesVersion < 38 && newEvents.Any(item =>
                                    item is DamageAppliedEvent damage &&
                                    damage.SourceSeat == 0 &&
                                    damage.TargetSeat == candidate.Target.Seat)))
                            {
                                return (
                                    game,
                                    beforeDamage,
                                    candidate.Action,
                                    eventCount,
                                    candidate.Target.Seat,
                                    source.Hp,
                                    candidate.Distance);
                            }
                        }
                    }

                    result = DeclineOrAdvance(game, result);
                    continue;
                }

                result = DeclineOrAdvance(game, result);
            }
        }

        throw new InvalidOperationException("Could not find a deterministic classic Wei Yan Kuanggu fixture.");
    }

    private static (
        GameEngine Game,
        LegalAction FirstAction) FindZhangFeiPaoxiaoFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:zhang-fei",
                GameCheckpoint.CurrentRulesVersion);
            if (game is null)
            {
                continue;
            }

            var self = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
            if (self.Hand.Count(card =>
                    card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) < 2)
            {
                continue;
            }

            var beforeFirstSlash = GameCheckpointJson.Deserialize(
                GameCheckpointJson.Serialize(game.CreateCheckpoint()));
            foreach (var firstAction in game.GetHumanLegalActions()
                         .Where(action => action.Kind == LegalActionKind.Slash && action.CardId is not null)
                         .OrderBy(action => action.CardId)
                         .ThenBy(action => action.TargetSeat))
            {
                var probe = GameReplay.Restore(beforeFirstSlash, registry);
                var matchingAction = probe.GetHumanLegalActions().Single(action =>
                    action.Kind == firstAction.Kind &&
                    action.CardId == firstAction.CardId &&
                    action.TargetSeats.SequenceEqual(firstAction.TargetSeats) &&
                    action.PlayedCardKind == firstAction.PlayedCardKind);
                var used = SubmitPlayAction(probe, matchingAction);
                if (!used.Accepted ||
                    !TryReturnToHumanPlay(probe) ||
                    !probe.GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.Slash && action.CardId is not null))
                {
                    continue;
                }

                return (game, firstAction);
            }
        }

        throw new InvalidOperationException(
            "Could not find a deterministic classic Zhang Fei two-Slash Paoxiao fixture.");
    }

    private static (
        GameEngine Game,
        LegalAction Action) FindZhaoYunLongdanFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:zhao-yun",
                GameCheckpoint.CurrentRulesVersion);
            if (game is null)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            var self = full.Players.Single(player => player.Seat == 0);
            var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            {
                if (candidate.Kind != LegalActionKind.Slash ||
                    candidate.PlayedCardKind != CardKind.Slash ||
                    candidate.CardId is not { } cardId ||
                    candidate.TargetSeat is not { } targetSeat ||
                    self.Hand.Single(card => card.Id == cardId).Kind != CardKind.Dodge)
                {
                    return false;
                }

                var target = full.Players.Single(player => player.Seat == targetSeat);
                return target.Hp > 1 &&
                       target.Hand.All(card => card.Kind != CardKind.Dodge) &&
                       target.Equipment.All(card => card.Kind != CardKind.BaguaFormation) &&
                       target.Skills?.All(skill =>
                           skill.ContentId is not ("classic:qingguo" or "classic:longdan" or "classic:hujia")) != false;
            });
            if (action is not null)
            {
                return (game, action);
            }
        }

        throw new InvalidOperationException(
            "Could not find a deterministic classic Zhao Yun Longdan conversion fixture.");
    }

    private static (
        GameEngine ActiveGame,
        GameEngine ResponseGame,
        int EquipmentCardId,
        LegalAction ActiveAction) FindGuanYuWushengEquipmentFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var current = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:guan-yu",
                GameCheckpoint.CurrentRulesVersion);
            if (current is null)
            {
                continue;
            }

            var self = current.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
            var redEquipment = self.Hand.FirstOrDefault(card =>
                EquipmentCatalog.IsEquipment(card.Kind) &&
                card.Suit is Suit.Heart or Suit.Diamond);
            if (redEquipment is null)
            {
                continue;
            }

            Equip(current, redEquipment.Id);
            var activeAction = current.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash &&
                action.CardId == redEquipment.Id &&
                action.PlayedCardKind == CardKind.Slash);
            if (activeAction is null)
            {
                continue;
            }

            var response = GameReplay.Restore(
                GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(current.CreateCheckpoint())),
                registry);
            var playPrompt = response.PendingDecision ??
                throw new InvalidOperationException("The equipped Wusheng fixture lost its play prompt.");
            var ended = response.Submit(new EndPlayPhaseCommand(
                0,
                response.Revision,
                playPrompt.PromptId));
            Require(ended.Accepted, ended.Error?.Message ??
                "The equipped Wusheng response fixture could not end the play phase.");

            for (var step = 0; step < 4_000 && response.State.Status != EngineStatus.Completed; step++)
            {
                if (response.PendingDecision is
                    {
                        Kind: DecisionKind.RespondSlash,
                        PlayerSeat: 0
                    } responsePrompt &&
                    responsePrompt.IncomingCard is CardKind.Duel or CardKind.BarbarianAssault &&
                    responsePrompt.Choices.Any(choice =>
                        choice.Cards.SequenceEqual([redEquipment.Id]) &&
                        choice.Parameters.GetValueOrDefault("response-card-kind") == nameof(CardKind.Slash)))
                {
                    return (current, response, redEquipment.Id, activeAction);
                }

                var responseOwner = response.CreateSnapshot(0, revealAll: true).Players
                    .Single(player => player.Seat == 0);
                if (!responseOwner.IsAlive ||
                    responseOwner.Equipment.All(card => card.Id != redEquipment.Id))
                {
                    break;
                }

                DeclineOrAdvance(response);
            }
        }

        throw new InvalidOperationException(
            "Could not find a deterministic classic Guan Yu equipped Wusheng use-and-response fixture.");
    }

    private static bool TryReturnToHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 64 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard })
            {
                return true;
            }

            DeclineOrAdvance(game);
        }

        return false;
    }

    private static (
        GameEngine Game,
        GameCheckpoint BeforeAction,
        LegalAction Action,
        int TargetSeat) FindLuBuWushuangSlashFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:lu-bu",
                GameCheckpoint.CurrentRulesVersion);
            if (game is null)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            var candidate = game.GetHumanLegalActions()
                .Where(action => action.Kind == LegalActionKind.Slash && action.TargetSeat is not null)
                .Select(action => new
                {
                    Action = action,
                    Target = full.Players.Single(player => player.Seat == action.TargetSeat)
                })
                .Where(item => item.Target.GeneralId is "classic:lu-meng" or "classic:zhang-fei" or
                           "classic:xu-huang" or "classic:gan-ning" or "classic:dian-wei" &&
                               item.Target.Hand.Count(card => card.Kind == CardKind.Dodge) >= 2 &&
                               item.Target.Equipment.All(card =>
                                   card.Kind is not (CardKind.BaguaFormation or CardKind.RenwangShield)) &&
                               item.Target.Skills?.All(skill =>
                                   skill.ContentId is not ("classic:qingguo" or "classic:longdan" or "classic:hujia")) != false)
                .OrderBy(item => item.Action.CardId)
                .ThenBy(item => item.Action.TargetSeat)
                .FirstOrDefault();
            if (candidate is null)
            {
                continue;
            }

            return (
                game,
                GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
                candidate.Action,
                candidate.Target.Seat);
        }

        throw new InvalidOperationException("Could not find a deterministic classic Lu Bu two-Dodge fixture.");
    }

    private static (
        GameEngine Game,
        GameCheckpoint BeforeAction,
        LegalAction Action,
        int TargetSeat) FindLuBuWushuangDuelFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:lu-bu",
                GameCheckpoint.CurrentRulesVersion);
            if (game is null)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            if (full.Players.SelectMany(player => player.Hand)
                .Any(card => card.Kind == CardKind.Nullification))
            {
                continue;
            }

            var candidate = game.GetHumanLegalActions()
                .Where(action => action.Kind == LegalActionKind.Duel && action.TargetSeat is not null)
                .Select(action => new
                {
                    Action = action,
                    Target = full.Players.Single(player => player.Seat == action.TargetSeat)
                })
                .Where(item => item.Target.GeneralId is "classic:lu-meng" or "classic:zhang-fei" or
                           "classic:xu-huang" or "classic:gan-ning" or "classic:dian-wei" &&
                               item.Target.Hand.Count(card =>
                                   card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) == 1 &&
                               item.Target.Skills?.All(skill =>
                                   skill.ContentId is not ("classic:wusheng" or "classic:longdan") &&
                                   skill.ContentId != "classic:jijiang") != false)
                .OrderBy(item => item.Action.CardId)
                .ThenBy(item => item.Action.TargetSeat)
                .FirstOrDefault();
            if (candidate is null)
            {
                continue;
            }

            return (
                game,
                GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
                candidate.Action,
                candidate.Target.Seat);
        }

        throw new InvalidOperationException("Could not find a deterministic classic Lu Bu one-Slash Duel fixture.");
    }

    private static void DriveAiUntil(GameEngine game, Func<bool> completed)
    {
        for (var step = 0; step < 32 && !completed(); step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 })
            {
                throw new InvalidOperationException(
                    $"Wushuang fixture reached an unexpected human {game.PendingDecision.Kind} prompt: " +
                    string.Join(", ", game.Events.TakeLast(12).Select(item => item.Payload.GetType().Name)));
            }

            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Could not advance the Wushuang AI response.");
        }

        Require(completed(), "Wushuang fixture did not reach the expected response boundary.");
    }

    private static CommandResult SubmitPlayAction(GameEngine game, LegalAction action) =>
        game.Submit(new PlayCardCommand(
            0,
            action.CardId ?? throw new InvalidOperationException("The play action has no physical card."),
            action.TargetSeats,
            game.Revision,
            game.PendingDecision?.PromptId ??
            throw new InvalidOperationException("The play action has no current prompt."),
            action.PlayedCardKind,
            action.TargetCardId)
        {
            ConversionSource = action.ConversionSource,
            AdditionalConversionSources = action.AdditionalConversionSources,
        });

    private static (
        GameEngine Game,
        PendingDecision? Prompt,
        int TargetSeat,
        int SourceHp,
        int TargetHandCount,
        int AttackRange) FindHuangZhongLiegongFixture(
        ContentRegistry registry,
        int rulesVersion,
        LiegongFixtureKind kind)
    {
        var humanRole = kind == LiegongFixtureKind.Ineligible ? Role.Lord : Role.Rebel;
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = CreateInteractive(registry, seed, humanRole);
            if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
            {
                game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
            }

            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Huang Zhong fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:huang-zhong"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:huang-zhong",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Huang Zhong.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Huang Zhong setup did not advance.");
            var result = advanced.Result;
            for (var step = 0; result.Status != EngineStatus.Completed && step < 1_200; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
                {
                    var full = game.CreateSnapshot(0, revealAll: true);
                    var source = full.Players.Single(player => player.Seat == 0);
                    var attackRange = game.GetAttackRange(0);
                    var candidate = game.GetHumanLegalActions()
                        .Where(action => action.Kind == LegalActionKind.Slash &&
                                         action.CardId is not null &&
                                         action.TargetSeat is not null)
                        .Select(action =>
                        {
                            var target = full.Players.Single(player => player.Seat == action.TargetSeat);
                            var targetHandCount = target.Hand.Count;
                            var eligibleByHp = targetHandCount >= source.Hp;
                            var eligibleByRange = targetHandCount <= attackRange;
                            return new
                            {
                                Action = action,
                                Target = target,
                                TargetHandCount = targetHandCount,
                                EligibleByHp = eligibleByHp,
                                EligibleByRange = eligibleByRange
                            };
                        })
                        .Where(item => item.Target.Hand.Any(card => card.Kind == CardKind.Dodge))
                        .Where(item => item.Target.GeneralId != "classic:da-qiao")
                        .Where(item => item.Target.Equipment.All(card =>
                            !EquipmentCatalog.IsEquipment(card.Kind) ||
                            EquipmentCatalog.Get(card.Kind).Slot != EquipmentSlot.Armor))
                        .Where(item => kind switch
                        {
                            LiegongFixtureKind.EligibleByHp =>
                                item.EligibleByHp && !item.EligibleByRange,
                            LiegongFixtureKind.EligibleByRange =>
                                item.EligibleByRange && !item.EligibleByHp,
                            LiegongFixtureKind.Ineligible =>
                                !item.EligibleByHp && !item.EligibleByRange,
                            _ => false
                        })
                        .OrderBy(item => item.Action.CardId)
                        .ThenBy(item => item.Action.TargetSeat)
                        .FirstOrDefault();
                    if (candidate is not null)
                    {
                        var slashEventStart = game.Events.Count;
                        var played = game.Submit(new PlayCardCommand(
                            0,
                            candidate.Action.CardId!.Value,
                            candidate.Action.TargetSeats,
                            game.Revision,
                            game.PendingDecision.PromptId,
                            candidate.Action.PlayedCardKind,
                            candidate.Action.TargetCardId)
                        { ConversionSource = candidate.Action.ConversionSource });
                        Require(played.Accepted, played.Error?.Message ?? "Huang Zhong could not use Slash.");
                        if (kind == LiegongFixtureKind.Ineligible)
                        {
                            // The shared card-trigger window may pause after acceptance, before Dodge is requested.
                            for (var resume = 0; resume < 64 &&
                                    !game.Events.Select(item => item.Payload).OfType<ResponseRequestedEvent>()
                                        .Any(item => item.TargetSeat == candidate.Target.Seat &&
                                                     item.RequiredCardKind == CardKind.Dodge) &&
                                    game.ResolutionStack.Count != 0 && game.PendingDecision is null; resume++)
                            {
                                var next = game.Submit(new AdvanceOneStepCommand(game.Revision));
                                Require(next.Accepted, next.Error?.Message ?? "The ineligible Slash stalled before Dodge.");
                            }
                            // Other target skills can nullify this Slash before its ordinary Dodge window.
                            // Such a card never tests Liegong's ineligible branch.
                            if (game.Events.Skip(slashEventStart).Any(item =>
                                    item.Payload is ProgramCardEffectNullifiedEvent))
                                break;
                        }
                        var prompt = game.PendingDecision;
                        if (prompt is null &&
                            rulesVersion >= 37 &&
                            kind != LiegongFixtureKind.Ineligible)
                        {
                            break;
                        }
                        return (
                            game,
                            prompt,
                            candidate.Target.Seat,
                            source.Hp,
                            candidate.TargetHandCount,
                            attackRange);
                    }
                }

                result = DeclineOrAdvance(game, result);
            }
        }

        throw new InvalidOperationException($"Could not find a deterministic Huang Zhong {kind} fixture.");
    }

    private static (
        GameEngine Game,
        PendingDecision Prompt,
        int TargetSeat,
        int Seed) FindMaChaoTieqiFixture(
        ContentRegistry registry,
        bool requireRedJudgment)
    {
        var attempted = 0;
        var tieqiPrompts = 0;
        var resolvedJudgments = 0;
        var offeredWithoutFixture = 0;
        string? lastFailure = null;
        string? lastOfferedFailure = null;
        for (var seed = 1; seed <= 8_192; seed++)
        {
            GameEngine game;
            int targetSeat;
            try
            {
                (game, targetSeat) = FindMaChaoSlashFixture(
                    registry,
                    seed,
                    GameCheckpoint.CurrentRulesVersion);
            }
            catch (InvalidOperationException error)
            {
                lastFailure = error.Message;
                if (error.Message != "Ma Chao was not offered.")
                {
                    offeredWithoutFixture++;
                    lastOfferedFailure = error.Message;
                }
                continue;
            }
            attempted++;

            if (game.PendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt)
            {
                continue;
            }
            tieqiPrompts++;

            var probe = GameReplay.Restore(game.CreateCheckpoint(), registry);
            var probePrompt = probe.PendingDecision!;
            var used = probe.Submit(new AnswerPromptCommand(
                0,
                probePrompt.PromptId,
                probePrompt.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "activate").Id,
                probe.Revision));
            if (!used.Accepted || probe.PendingDecision?.Kind == DecisionKind.ProgramJudgmentReplacement)
            {
                continue;
            }

            var judgment = probe.Events.Select(item => item.Payload)
                .OfType<JudgmentResolvedEvent>()
                .LastOrDefault(item => item.Reason == "skill.slash-response-judgment");
            if (judgment is not null) resolvedJudgments++;
            if (judgment is not null &&
                (judgment.Suit is Suit.Heart or Suit.Diamond) == requireRedJudgment)
            {
                return (game, prompt, targetSeat, seed);
            }
        }

        throw new InvalidOperationException(
            $"Could not find a deterministic Ma Chao Tieqi fixture for a " +
            $"{(requireRedJudgment ? "red" : "black")} judgment " +
            $"(attempted={attempted}, offered-failures={offeredWithoutFixture}, prompts={tieqiPrompts}, " +
            $"judgments={resolvedJudgments}, last={lastFailure}, offered-last={lastOfferedFailure}).");
    }

    private static (GameEngine Game, int TargetSeat) FindMaChaoSlashFixture(
        ContentRegistry registry,
        int seed,
        int rulesVersion)
    {
        var game = CreateInteractive(registry, seed);
        if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
        {
            game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
        }

        var started = game.Submit(new StartGameCommand());
        Require(started.Accepted, started.Error?.Message ?? "Ma Chao fixture failed to start.");
        if (game.PendingDecision?.Choices.Any(choice =>
                choice.ContentIds.SequenceEqual(["classic:ma-chao"])) != true)
        {
            throw new InvalidOperationException("Ma Chao was not offered.");
        }

        var selected = game.Submit(new SelectGeneralCommand(
            0,
            "classic:ma-chao",
            game.Revision,
            game.PendingDecision.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Ma Chao.");
        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted, advanced.Error?.Message ?? "Ma Chao did not reach the play phase.");
        if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
        {
            throw new InvalidOperationException("Ma Chao did not stop at a PlayCard decision.");
        }

        var full = game.CreateSnapshot(0, revealAll: true);
        Require(game.GetCombatDistance(0, 2) == 1,
            "Classic Ma Chao must reduce the public distance-two seat to distance one through Mashu.");
        var action = game.GetHumanLegalActions()
            .Where(candidate => candidate.Kind == LegalActionKind.Slash &&
                                candidate.CardId is not null &&
                                candidate.TargetSeat is { } targetSeat &&
                                full.Players.Single(player => player.Seat == targetSeat).Hand.Any(card =>
                                    card.Kind == CardKind.Dodge))
            .OrderBy(candidate => candidate.CardId)
            .ThenBy(candidate => candidate.TargetSeat)
            .FirstOrDefault() ??
            throw new InvalidOperationException("Ma Chao has no Slash target holding Dodge.");
        var target = action.TargetSeat!.Value;
        var played = game.Submit(new PlayCardCommand(
            0,
            action.CardId!.Value,
            action.TargetSeats,
            game.Revision,
            game.PendingDecision.PromptId,
            action.PlayedCardKind,
            action.TargetCardId));
        Require(played.Accepted, played.Error?.Message ?? "Ma Chao could not use Slash.");
        return (game, target);
    }

    private static (GameEngine Game, LegalAction Action) FindHuangYueyingOrdinaryTrickFixture(
        ContentRegistry registry,
        int rulesVersion,
        bool requireNullification)
    {
        for (var seed = 1; seed <= 8_192; seed++)
        {
            var game = CreateInteractive(registry, seed);
            if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
            {
                game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
            }

            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Huang Yueying fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:huang-yueying"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:huang-yueying",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Huang Yueying.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Huang Yueying did not reach the play phase.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var hand = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hand;
            if (requireNullification && hand.All(card => card.Kind != CardKind.Nullification))
            {
                continue;
            }

            var action = game.GetHumanLegalActions()
                .Where(candidate => candidate.CardId is not null && candidate.Kind is
                    LegalActionKind.DrawTwo or
                    LegalActionKind.BarbarianAssault or
                    LegalActionKind.ArrowBarrage or
                    LegalActionKind.PeachGarden or
                    LegalActionKind.FiveGrains or
                    LegalActionKind.IronChain or
                    LegalActionKind.Dismantlement or
                    LegalActionKind.Snatch or
                    LegalActionKind.FireAttack or
                    LegalActionKind.Duel)
                .OrderBy(candidate => candidate.Kind == LegalActionKind.DrawTwo ? 0 : 1)
                .ThenBy(candidate => candidate.CardId)
                .FirstOrDefault();
            if (action is not null)
            {
                return (game, action);
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Huang Yueying ordinary-trick fixture.");
    }

    private static GameEngine FindHuangYueyingNullificationJizhiFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 8_192; seed++)
        {
            GameEngine game;
            LegalAction action;
            try
            {
                (game, action) = FindHuangYueyingOrdinaryTrickFixtureForSeed(registry, seed);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            var played = game.Submit(new PlayCardCommand(
                0,
                action.CardId!.Value,
                action.TargetSeats,
                game.Revision,
                game.PendingDecision!.PromptId,
                action.PlayedCardKind,
                action.TargetCardId));
            if (!played.Accepted || game.PendingDecision is not { Kind: DecisionKind.ProgramTrigger } initialJizhi)
            {
                continue;
            }

            var skipped = game.Submit(new AnswerPromptCommand(
                0,
                initialJizhi.PromptId,
                initialJizhi.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "jizhi-skip").Id,
                game.Revision));
            if (!skipped.Accepted)
            {
                continue;
            }

            for (var step = 0; step < 128; step++)
            {
                if (game.PendingDecision is
                    {
                        Kind: DecisionKind.Nullification,
                        PlayerSeat: 0
                    } nullificationPrompt)
                {
                    var choice = nullificationPrompt.Choices.FirstOrDefault(candidate =>
                        candidate.Parameters.GetValueOrDefault("response") == "nullification");
                    if (choice is null)
                    {
                        break;
                    }

                    var used = game.Submit(new AnswerPromptCommand(
                        0,
                        nullificationPrompt.PromptId,
                        choice.Id,
                        game.Revision));
                    if (used.Accepted && game.PendingDecision?.Kind == DecisionKind.ProgramTrigger)
                    {
                        return game;
                    }
                    break;
                }

                if (!game.ResolutionStack.Any(frame => frame is NullificationWindowFrame))
                {
                    break;
                }

                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                if (!advanced.Accepted)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Nullification-triggered Jizhi fixture.");
    }

    private static (GameEngine Game, LegalAction Action) FindHuangYueyingOrdinaryTrickFixtureForSeed(
        ContentRegistry registry,
        int seed)
    {
        var game = CreateInteractive(registry, seed);
        var started = game.Submit(new StartGameCommand());
        Require(started.Accepted, started.Error?.Message ?? "Huang Yueying Nullification fixture failed to start.");
        if (game.PendingDecision?.Choices.Any(choice =>
                choice.ContentIds.SequenceEqual(["classic:huang-yueying"])) != true)
        {
            throw new InvalidOperationException("Huang Yueying was not offered.");
        }

        var selected = game.Submit(new SelectGeneralCommand(
            0,
            "classic:huang-yueying",
            game.Revision,
            game.PendingDecision.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Huang Yueying.");
        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted && game.PendingDecision?.Kind == DecisionKind.PlayCard,
            advanced.Error?.Message ?? "Huang Yueying did not reach play for Nullification.");
        var hand = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hand;
        if (hand.All(card => card.Kind != CardKind.Nullification))
        {
            throw new InvalidOperationException("Huang Yueying has no Nullification.");
        }

        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            candidate.CardId is not null && candidate.Kind is
                LegalActionKind.DrawTwo or
                LegalActionKind.BarbarianAssault or
                LegalActionKind.ArrowBarrage or
                LegalActionKind.PeachGarden or
                LegalActionKind.FiveGrains or
                LegalActionKind.IronChain or
                LegalActionKind.Dismantlement or
                LegalActionKind.Snatch or
                LegalActionKind.FireAttack or
                LegalActionKind.Duel) ??
            throw new InvalidOperationException("Huang Yueying has no ordinary trick action.");
        return (game, action);
    }

    private static void ContinueLuoshenUntilPlay(GameEngine game)
    {
        for (var step = 0; step < 256 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
        {
            CommandResult result;
            if (game.PendingDecision is { Kind: DecisionKind.ProgramRepeatJudgment } prompt)
            {
                result = game.Submit(new AnswerPromptCommand(
                    0,
                    prompt.PromptId,
                    prompt.Choices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("action") == "luoshen-use").Id,
                    game.Revision));
            }
            else
            {
                result = game.Submit(new AdvanceCommand(game.Revision));
            }

            Require(result.Accepted, result.Error?.Message ?? "Could not continue the Luoshen chain.");
        }

        Require(game.State.Phase == TurnPhase.Play &&
                game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "The bounded Luoshen chain did not reach the play phase.");
    }

    private static void Equip(GameEngine game, int cardId)
    {
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Equipment fixture lost its play prompt.");
        var equipped = game.Submit(new PlayCardCommand(
            0,
            cardId,
            [],
            game.Revision,
            prompt.PromptId));
        Require(equipped.Accepted, equipped.Error?.Message ?? "Could not equip the Zhiheng fixture card.");
        if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
        {
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ??
                "Could not return the Zhiheng fixture to the human play boundary.");
        }
        Require(game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0)
                .Equipment.Any(card => card.Id == cardId),
            "The Zhiheng fixture card did not enter the equipment zone.");
    }

    private static ContentRegistry CreatePreProgramClassicRegistry() =>
        StandardContentRegistry.CreateWithClassicGenerals();

    private static GameEngine SelectGeneral(ContentRegistry registry, string generalId, int rulesVersion)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateInteractive(registry, seed);
            if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
                game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Classic selection fixture failed to start.");
            if (started.Result.PendingDecision?.Choices.Any(choice => choice.ContentIds.SequenceEqual([generalId])) != true)
                continue;
            var selected = game.Submit(new SelectGeneralCommand(
                0,
                generalId,
                game.Revision,
                game.PendingDecision!.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? $"Could not select {generalId}.");
            return game;
        }

        throw new InvalidOperationException($"No deterministic selection fixture exposed {generalId}.");
    }

    private static (
        GameEngine Game,
        int ProviderSeat,
        int PeachCardId,
        int SelfPeachCardId,
        int NonWuProviderSeat,
        int NonWuPeachCardId) FindJiuyuanFixture(
        ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Jiuyuan fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:sun-quan"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:sun-quan",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Sun Quan.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ??
                "The Jiuyuan fixture could not finish AI general selection.");
            var players = game.CreateSnapshot(0, revealAll: true).Players;
            var selfPeach = players.Single(player => player.Seat == 0).Hand
                .FirstOrDefault(card => card.Kind == CardKind.Peach);
            var provider = players
                .Where(player => player.Seat != 0 &&
                                 string.Equals(
                                     registry.Generals[player.GeneralId].FactionId,
                                     "wu",
                                     StringComparison.Ordinal))
                .Select(player => new
                {
                    player.Seat,
                    Peach = player.Hand.FirstOrDefault(card => card.Kind == CardKind.Peach)
                })
                .FirstOrDefault(candidate => candidate.Peach is not null);
            var nonWuProvider = players
                .Where(player => player.Seat != 0 &&
                                 !string.Equals(
                                     registry.Generals[player.GeneralId].FactionId,
                                     "wu",
                                     StringComparison.Ordinal))
                .Select(player => new
                {
                    player.Seat,
                    Peach = player.Hand.FirstOrDefault(card => card.Kind == CardKind.Peach)
                })
                .FirstOrDefault(candidate => candidate.Peach is not null);
            if (provider is not null && selfPeach is not null && nonWuProvider is not null)
            {
                return (
                    game,
                    provider.Seat,
                    provider.Peach!.Id,
                    selfPeach.Id,
                    nonWuProvider.Seat,
                    nonWuProvider.Peach!.Id);
            }
        }

        throw new InvalidOperationException("No deterministic Jiuyuan fixture exposed a Wu provider with Peach.");
    }

    private static void ApplySyntheticDyingPeach(
        GameEngine game,
        int providerSeat,
        int peachCardId)
    {
        var playersField = typeof(GameEngine).GetField(
            "_players",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine player store was not found.");
        var players = (System.Collections.IList)playersField.GetValue(game)!;
        var target = players[0]!;
        var provider = players[providerSeat]!;
        target.GetType().GetProperty("Hp")!.SetValue(target, 0);

        var getHand = typeof(GameEngine).GetMethod(
            "GetHand",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine hand accessor was not found.");
        var providerHand = ((System.Collections.IEnumerable)getHand.Invoke(game, [provider])!)
            .Cast<Card>();
        var peach = providerHand.Single(card => card.Id == peachCardId);
        var resolvePeach = typeof(GameEngine).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == "ResolvePeach" && method.GetParameters().Length == 5);
        resolvePeach.Invoke(game, [provider, target, peach, true, null]);

        var commitEvents = typeof(GameEngine).GetMethod(
            "CommitPendingEvents",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine event commit method was not found.");
        commitEvents.Invoke(game, null);
    }

    private static GameEngine CreateInteractive(
        ContentRegistry registry,
        int seed,
        Role humanRole = Role.Lord) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = humanRole,
            ModeId = "identity:classic-5",
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 220
        }, registry);

    private enum LiegongFixtureKind
    {
        EligibleByHp,
        EligibleByRange,
        Ineligible
    }

    private static EngineRunResult DeclineOrAdvance(GameEngine game, EngineRunResult? result = null)
    {
        var prompt = game.PendingDecision;
        GameCommand command = prompt?.Kind switch
        {
            null => new AdvanceOneStepCommand(game.Revision),
            DecisionKind.PlayCard => new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId),
            DecisionKind.DiscardCards => new DiscardCardsCommand(
                0,
                prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                prompt.PromptId,
                game.Revision),
            DecisionKind.SelectGeneral => new SelectGeneralCommand(
                0,
                prompt.ValidContentIds[0],
                game.Revision,
                prompt.PromptId),
            _ => new AnswerPromptCommand(
                0,
                prompt.PromptId,
                DeclineChoice(prompt).Id,
                game.Revision)
        };
        var accepted = game.Submit(command);
        if (!accepted.Accepted)
            throw new InvalidOperationException(accepted.Error?.Message ?? $"Could not advance from {result?.Status} / {prompt?.Kind}.");
        return accepted.Result;
    }

    private static PromptChoice DeclineChoice(PendingDecision prompt) =>
        prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.Values.Any(value =>
                value.StartsWith("skip", StringComparison.Ordinal) ||
                value is "take-damage" or "no-nullification" or "ganglie-lose-hp"))
        ?? prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0)
        ?? prompt.Choices.First();

    private static IReadOnlyList<string> EventSignatures(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
