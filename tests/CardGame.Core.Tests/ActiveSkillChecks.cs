using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ActiveSkillChecks
{
    public static void KujinFlow()
    {
        var registry = StandardContentRegistry.CreateWithActiveSkills();
        Require(
            registry.Packages.Any(package => package.Id == "standard-active-skills"),
            "The active-skill expansion package was not registered.");
        Require(
            registry.Skills.TryGetValue("standard:kujin", out var skill) &&
            skill.LegacyKind == SkillKind.Kujin,
            "The active skill must retain a typed legacy projection.");

        var game = FindKujinGame(registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The active-skill fixture failed to start.");
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("The fixture did not reach human play.");
        var action = game.GetHumanLegalActions().SingleOrDefault(candidate =>
            candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Kujin);
        Require(action is not null, "Kujin must be published as a legal play action.");
        Require(
            prompt.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("action") == "use-skill" &&
                choice.Parameters.GetValueOrDefault("skill") == SkillKind.Kujin.ToString()),
            "The active skill must be represented as a cardless prompt choice.");

        var before = game.CreateSnapshot(0, revealAll: true);
        var beforePlayer = before.Players.Single(player => player.Seat == 0);
        var beforeState = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var beforeEventCount = game.Events.Count;
        var beforeCommandCount = game.AcceptedCommands.Count;
        var invalid = game.Submit(new UseSkillCommand(
            0,
            SkillKind.Kujin,
            [beforePlayer.Hand[0].Id],
            [],
            game.Revision,
            prompt.PromptId));
        Require(!invalid.Accepted && invalid.Error?.Code == CommandErrorCode.InvalidCard,
            "A card selection must be rejected by the cardless active-skill boundary.");
        Require(beforeState == SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "A rejected active-skill command mutated state.");
        Require(beforeEventCount == game.Events.Count && beforeCommandCount == game.AcceptedCommands.Count,
            "A rejected active-skill command entered the journal or event stream.");

        var used = game.Submit(new UseSkillCommand(
            0,
            SkillKind.Kujin,
            [],
            [],
            game.Revision,
            prompt.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "The legal active-skill command was rejected.");
        var after = game.CreateSnapshot(0, revealAll: true);
        var afterPlayer = after.Players.Single(player => player.Seat == 0);
        Require(afterPlayer.Hp == beforePlayer.Hp - 1, "Kujin must cost exactly one HP.");
        Require(afterPlayer.Hand.Count == beforePlayer.Hand.Count + 2, "Kujin must draw exactly two cards.");
        Require(game.ResolutionStack.Count == 0, "The synchronous active-skill frame must close at its boundary.");
        Require(game.State.ProcessingCardCount == 0, "Kujin must not strand a processing card.");

        var requested = game.Events.Select(item => item.Payload).OfType<ActiveSkillRequestedEvent>().Single();
        var hpLost = game.Events.Select(item => item.Payload).OfType<SkillHpLostEvent>().Single();
        var drawn = game.Events.Select(item => item.Payload).OfType<SkillCardsDrawnEvent>().Single();
        var resolved = game.Events.Select(item => item.Payload).OfType<ActiveSkillResolvedEvent>().Single();
        Require(requested.Skill == SkillKind.Kujin && hpLost.ResolutionId == requested.ResolutionId,
            "The active-skill request and HP lifecycle must share one resolution id.");
        Require(hpLost.Amount == 1 && hpLost.RemainingHp == afterPlayer.Hp,
            "The HP-loss event must expose the committed public result.");
        Require(drawn.ResolutionId == requested.ResolutionId && drawn.CardIds.Count == 2,
            "The draw event must expose exactly the two trusted-host card ids.");
        Require(resolved.ResolutionId == requested.ResolutionId && resolved.Effect == ActiveSkillEffectKind.LoseHpAndDraw,
            "The active-skill resolution event must close the same typed frame.");
        Require(
            drawn.CardIds.All(cardId => game.CardMovements.Any(movement =>
                movement.CardId == cardId && movement.To == CardLocation.Hand(0) &&
                movement.Reason == CardMoveReasons.KujinDraw)),
            "Each active-skill draw must use the named movement reason.");

        var observer = game.CreateSnapshot(1);
        var hiddenHuman = observer.Players.Single(player => player.Seat == 0);
        Require(hiddenHuman.Hand.Count == 0 && hiddenHuman.HandCount == afterPlayer.Hand.Count,
            "An observer must see only the human hand count, not the drawn card ids.");
        Require(!SnapshotJson.Serialize(observer).Contains("SkillCardsDrawn", StringComparison.Ordinal),
            "Typed host events must not enter an ordinary player snapshot.");

        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(
            SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
            SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "The active-skill command did not replay the same full state.");
        Require(
            game.Events.Select(EventSignature).SequenceEqual(restored.Events.Select(EventSignature)),
            "The active-skill event stream was not deterministic under replay.");
        var journal = CommandJson.Deserialize(CommandJson.Serialize(game.AcceptedCommands));
        Require(journal.OfType<UseSkillCommand>().Single().CardIds.Count == 0,
            "The serialized active-skill command must preserve its cardless contract.");

        ZhihengFlow(registry);
    }

    public static void KujinDyingContinuation()
    {
        var registry = StandardContentRegistry.CreateWithActiveSkills();
        var game = FindKujinDyingGame(registry);
        var before = game.CreateSnapshot(0, revealAll: true);
        var beforePlayer = before.Players.Single(player => player.Seat == 0);
        Require(beforePlayer.Hp == 1 && beforePlayer.Hand.Any(card => card.Kind == CardKind.Peach),
            "The dying continuation fixture must leave the active-skill owner at one HP with a Peach.");

        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("The Kujin dying fixture did not return to human play.");
        Require(prompt.Kind == DecisionKind.PlayCard, "The Kujin dying fixture must wait at the play boundary.");
        Require(game.GetHumanLegalActions().Any(candidate =>
                candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Kujin),
            "Kujin must remain a legal active skill at one HP.");
        var used = game.Submit(new UseSkillCommand(
            0,
            SkillKind.Kujin,
            [],
            [],
            game.Revision,
            prompt.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "Kujin should enter its dying continuation.");

        var dyingPrompt = game.PendingDecision ??
            throw new InvalidOperationException("Lethal Kujin did not publish a dying prompt.");
        Require(dyingPrompt.Kind == DecisionKind.RescueDying && dyingPrompt.PlayerSeat == 0,
            "Lethal Kujin must reuse the private dying prompt for the owner.");
        Require(game.ResolutionStack.Count == 2 &&
                game.ResolutionStack[0] is ActiveSkillFrame activeSkill &&
                game.ResolutionStack[1] is DyingFrame dyingFrame &&
                dyingFrame.ParentFrameId == activeSkill.Id &&
                activeSkill.Skill == SkillKind.Kujin &&
                activeSkill.Step == ResolutionFrameStep.ResolvingEffect,
            "Lethal Kujin must retain an ActiveSkill frame beneath the Dying frame.");

        var observer = game.CreateSnapshot(1);
        Require(observer.PendingDecision is null &&
                observer.Players.Single(player => player.Seat == 0).Hand.Count == 0 &&
                observer.Players.Single(player => player.Seat == 0).HandCount == beforePlayer.Hand.Count,
            "The active-skill dying prompt must remain private to its responder.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restoredPaused = GameReplay.Restore(pausedCheckpoint, registry);
        Require(SnapshotJson.Serialize(restoredPaused.State) == SnapshotJson.Serialize(game.State) &&
                game.Events.Select(EventSignature).SequenceEqual(restoredPaused.Events.Select(EventSignature)),
            "A paused active-skill dying continuation did not replay deterministically.");

        var peachChoice = dyingPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("response") == "peach");
        var rescued = game.Submit(new AnswerPromptCommand(
            0,
            dyingPrompt.PromptId,
            peachChoice.Id,
            game.Revision));
        Require(rescued.Accepted, rescued.Error?.Message ?? "The Peach rescue was rejected.");

        var after = game.CreateSnapshot(0, revealAll: true);
        var afterPlayer = after.Players.Single(player => player.Seat == 0);
        Require(afterPlayer.Hp == 1 && afterPlayer.Hand.Count == beforePlayer.Hand.Count + 1,
            "Surviving active-skill dying must resume the draw effect after Peach rescue.");
        Require(game.ResolutionStack.Count == 0 && game.State.ProcessingCardCount == 0,
            "The resumed active-skill dying chain left a frame or processing card behind.");

        var events = game.Events.Select(item => item.Payload).ToArray();
        var request = events.OfType<ActiveSkillRequestedEvent>().Last(item => item.Skill == SkillKind.Kujin);
        var hpLost = events.OfType<SkillHpLostEvent>().Single(item => item.ResolutionId == request.ResolutionId);
        var dyingEvent = events.OfType<PlayerDyingEvent>().Single();
        var response = events.OfType<DyingResponseEvent>().Single();
        var drawn = events.OfType<SkillCardsDrawnEvent>().Single(item => item.ResolutionId == request.ResolutionId);
        var resolved = events.OfType<ActiveSkillResolvedEvent>().Single(item => item.ResolutionId == request.ResolutionId);
        Require(hpLost.RemainingHp == 0 && dyingEvent.KillerSeat is null &&
                response.UsedPeach && response.ResponderSeat == 0 &&
                events.OfType<DyingResolvedEvent>().Single(item => item.ResolutionId == dyingEvent.ResolutionId).Survived &&
                drawn.CardIds.Count == 2 &&
                resolved.Effect == ActiveSkillEffectKind.LoseHpAndDraw,
            "Active-skill dying must publish the shared HP, rescue, draw and resolution events.");

        var finalCheckpoint = game.CreateCheckpoint();
        var restored = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(finalCheckpoint)),
            registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(after) &&
                game.Events.Select(EventSignature).SequenceEqual(restored.Events.Select(EventSignature)),
            "The completed active-skill dying chain did not replay the same state and events.");
    }

    public static void QingnangFlow()
    {
        var registry = StandardContentRegistry.CreateWithActiveSkills();
        Require(
            registry.Skills.TryGetValue("standard:qingnang", out var skill) &&
            skill.LegacyKind == SkillKind.Qingnang,
            "The Qingnang content definition must retain a typed legacy projection.");

        var directEffect = SkillRegistry.GetActive(SkillKind.Qingnang)!.GetEffect(new ActiveSkillContext(
            new PlayerSkillContext(0, 3, 4, 4, TurnPhase.Play),
            SelectedCardCount: 1,
            SelectedTargetCount: 1));
        Require(directEffect.Kind == ActiveSkillEffectKind.DiscardAndRecover &&
                directEffect.MinCardCount == 1 && directEffect.MaxCardCount == 1 &&
                directEffect.MinTargetCount == 1 && directEffect.MaxTargetCount == 1 &&
                directEffect.RecoveryAmount == 1,
            "Qingnang must expose a one-card, one-target, one-recovery contract.");

        var game = FindQingnangGame(registry);
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("The Qingnang fixture did not return to human play.");
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Qingnang);
        var validCardIds = prompt.ActiveSkillValidCardIds ?? [];
        var validTargetSeats = prompt.ActiveSkillValidTargetSeats ?? [];
        Require(action.MinCardCount == 1 && action.MaxCardCount == 1 &&
                action.MinTargetCount == 1 && action.MaxTargetCount == 1 &&
                validCardIds.Count == game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hand.Count,
            "Qingnang must publish exact one-card and one-target bounds.");

        var before = game.CreateSnapshot(0, revealAll: true);
        var source = before.Players.Single(player => player.Seat == 0);
        var wounded = before.Players.Single(player => player.IsAlive && player.Hp < player.MaxHp);
        var fullTarget = before.Players.First(player => player.IsAlive && player.Hp == player.MaxHp);
        Require(validTargetSeats.Contains(wounded.Seat) &&
                !validTargetSeats.Contains(fullTarget.Seat) &&
                validTargetSeats.All(seat => before.Players.Single(player => player.Seat == seat).IsAlive &&
                                             before.Players.Single(player => player.Seat == seat).Hp <
                                             before.Players.Single(player => player.Seat == seat).MaxHp),
            "Qingnang must publish only living wounded characters as target candidates.");

        var selectedCardId = validCardIds.Order().First();
        var beforeState = SnapshotJson.Serialize(before);
        var beforeEventCount = game.Events.Count;
        var beforeCommandCount = game.AcceptedCommands.Count;
        var invalid = game.Submit(new UseSkillCommand(
            0,
            SkillKind.Qingnang,
            [selectedCardId],
            [fullTarget.Seat],
            game.Revision,
            prompt.PromptId));
        Require(!invalid.Accepted && invalid.Error?.Code == CommandErrorCode.InvalidTarget &&
                beforeState == SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                beforeEventCount == game.Events.Count && beforeCommandCount == game.AcceptedCommands.Count,
            "Qingnang must reject a full-health target atomically.");

        var used = game.Submit(new UseSkillCommand(
            0,
            SkillKind.Qingnang,
            [selectedCardId],
            [wounded.Seat],
            game.Revision,
            prompt.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "The legal Qingnang command was rejected.");

        var after = game.CreateSnapshot(0, revealAll: true);
        var afterSource = after.Players.Single(player => player.Seat == 0);
        var afterTarget = after.Players.Single(player => player.Seat == wounded.Seat);
        Require(afterSource.Hand.Count == source.Hand.Count - 1 &&
                !afterSource.Hand.Any(card => card.Id == selectedCardId) &&
                afterTarget.Hp == wounded.Hp + 1 &&
                !game.GetHumanLegalActions().Any(candidate =>
                    candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Qingnang) &&
                game.ResolutionStack.Count == 0 && game.State.ProcessingCardCount == 0,
            "Qingnang must discard one card, recover the selected target and close its frame.");

        var events = game.Events.Select(item => item.Payload).ToArray();
        var requested = events.OfType<ActiveSkillRequestedEvent>().Single(item => item.Skill == SkillKind.Qingnang);
        var discarded = events.OfType<SkillCardsDiscardedEvent>().Single(item => item.Skill == SkillKind.Qingnang);
        var recovery = events.OfType<RecoveryAppliedEvent>().Single(item =>
            item.SourceSeat == 0 && item.TargetSeat == wounded.Seat);
        var resolved = events.OfType<ActiveSkillResolvedEvent>().Single(item => item.Skill == SkillKind.Qingnang);
        Require(requested.ResolutionId == discarded.ResolutionId &&
                requested.CardIds is not null && requested.CardIds.SequenceEqual([selectedCardId]) &&
                discarded.CardIds.SequenceEqual([selectedCardId]) &&
                recovery.Amount == 1 && recovery.RemainingHp == afterTarget.Hp &&
                resolved.ResolutionId == requested.ResolutionId &&
                resolved.Effect == ActiveSkillEffectKind.DiscardAndRecover &&
                game.CardMovements.Count(movement => movement.CardId == selectedCardId &&
                    movement.Reason == CardMoveReasons.QingnangDiscard) == 2 &&
                game.CardMovements.Any(movement => movement.CardId == selectedCardId &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.QingnangDiscard),
            "Qingnang must publish one typed discard/recovery lifecycle and move through Processing.");

        var observer = game.CreateSnapshot(1);
        var hiddenSource = observer.Players.Single(player => player.Seat == 0);
        Require(hiddenSource.Hand.Count == 0 && hiddenSource.HandCount == afterSource.Hand.Count &&
                !SnapshotJson.Serialize(observer).Contains("SkillCardsDiscarded", StringComparison.Ordinal),
            "Qingnang private discard ids must not enter an observer snapshot.");

        var restored = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(after) &&
                game.Events.Select(EventSignature).SequenceEqual(restored.Events.Select(EventSignature)),
            "Qingnang did not replay the same state and typed event stream.");
    }

    public static void HuichunFlow()
    {
        var registry = StandardContentRegistry.CreateWithActiveSkills();
        Require(
            registry.Skills.TryGetValue("standard:huichun", out var skill) &&
            skill.LegacyKind == SkillKind.Huichun,
            "The Huichun content definition must retain a typed legacy projection.");

        var directEffect = SkillRegistry.GetActive(SkillKind.Huichun)!.GetEffect(new ActiveSkillContext(
            new PlayerSkillContext(0, 3, 4, 4, TurnPhase.Play),
            SelectedCardCount: 2,
            SelectedTargetCount: 2));
        Require(directEffect.Kind == ActiveSkillEffectKind.DiscardAndRecoverTargets &&
                directEffect.MinCardCount == 2 && directEffect.MaxCardCount == 2 &&
                directEffect.MinTargetCount == 2 && directEffect.MaxTargetCount == 3 &&
                directEffect.RecoveryAmount == 1,
            "Huichun must expose an exact two-card, two-to-three-target recovery contract.");

        var game = FindHuichunGame(registry);
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("The Huichun fixture did not return to human play.");
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Huichun);
        var before = game.CreateSnapshot(0, revealAll: true);
        var source = before.Players.Single(player => player.Seat == 0);
        var validCardIds = prompt.ActiveSkillValidCardIds ?? [];
        var validTargetSeats = prompt.ActiveSkillValidTargetSeats ?? [];
        Require(action.MinCardCount == 2 && action.MaxCardCount == 2 &&
                action.MinTargetCount == 2 && action.MaxTargetCount == 3 &&
                validCardIds.Count == source.Hand.Count &&
                validTargetSeats.Count >= 2 &&
                validTargetSeats.All(seat =>
                {
                    var target = before.Players.Single(player => player.Seat == seat);
                    return target.IsAlive && target.Hp < target.MaxHp;
                }),
            "Huichun must publish private hand candidates and at least two public wounded targets.");

        var selectedCardIds = validCardIds.Order().Take(2).ToArray();
        var selectedTargetSeats = validTargetSeats.Order().Take(2).ToArray();
        var beforeState = SnapshotJson.Serialize(before);
        var beforeEventCount = game.Events.Count;
        var beforeCommandCount = game.AcceptedCommands.Count;
        var invalid = game.Submit(new UseSkillCommand(
            0,
            SkillKind.Huichun,
            selectedCardIds,
            [selectedTargetSeats[0]],
            game.Revision,
            prompt.PromptId));
        Require(!invalid.Accepted && invalid.Error?.Code == CommandErrorCode.InvalidTarget &&
                beforeState == SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                beforeEventCount == game.Events.Count &&
                beforeCommandCount == game.AcceptedCommands.Count,
            "Huichun must reject too few targets atomically.");

        var duplicate = game.Submit(new UseSkillCommand(
            0,
            SkillKind.Huichun,
            selectedCardIds,
            [selectedTargetSeats[0], selectedTargetSeats[0]],
            game.Revision,
            prompt.PromptId));
        Require(!duplicate.Accepted && duplicate.Error?.Code == CommandErrorCode.InvalidTarget &&
                beforeState == SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                beforeEventCount == game.Events.Count &&
                beforeCommandCount == game.AcceptedCommands.Count,
            "Huichun must reject duplicate targets atomically.");

        var beforeTargets = selectedTargetSeats.ToDictionary(
            seat => seat,
            seat => before.Players.Single(player => player.Seat == seat).Hp);
        var used = game.Submit(new UseSkillCommand(
            0,
            SkillKind.Huichun,
            selectedCardIds,
            selectedTargetSeats,
            game.Revision,
            prompt.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "The legal Huichun command was rejected.");

        var after = game.CreateSnapshot(0, revealAll: true);
        var afterSource = after.Players.Single(player => player.Seat == 0);
        Require(afterSource.Hand.Count == source.Hand.Count - selectedCardIds.Length &&
                selectedCardIds.All(cardId => !afterSource.Hand.Any(card => card.Id == cardId)) &&
                selectedTargetSeats.All(seat =>
                {
                    var target = after.Players.Single(player => player.Seat == seat);
                    return target.Hp == beforeTargets[seat] + 1;
                }) &&
                !game.GetHumanLegalActions().Any(candidate =>
                    candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Huichun) &&
                game.ResolutionStack.Count == 0 &&
                game.State.ProcessingCardCount == 0,
            "Huichun must discard two cards, recover each selected target and close all frames.");

        var events = game.Events.Select(item => item.Payload).ToArray();
        var requested = events.OfType<ActiveSkillRequestedEvent>()
            .Single(item => item.Skill == SkillKind.Huichun);
        var discarded = events.OfType<SkillCardsDiscardedEvent>()
            .Single(item => item.Skill == SkillKind.Huichun);
        var recoveries = events.OfType<RecoveryAppliedEvent>()
            .Where(item => item.SourceSeat == 0 && selectedTargetSeats.Contains(item.TargetSeat))
            .ToArray();
        var resolved = events.OfType<ActiveSkillResolvedEvent>()
            .Single(item => item.Skill == SkillKind.Huichun);
        Require(requested.CardIds is not null &&
                requested.CardIds.SequenceEqual(selectedCardIds) &&
                requested.TargetSeats is not null &&
                requested.TargetSeats.SequenceEqual(selectedTargetSeats) &&
                discarded.ResolutionId == requested.ResolutionId &&
                discarded.CardIds.SequenceEqual(selectedCardIds) &&
                recoveries.Length == selectedTargetSeats.Length &&
                selectedTargetSeats.All(seat =>
                {
                    var recovery = recoveries.Single(item => item.TargetSeat == seat);
                    return recovery.Amount == 1 &&
                           recovery.RemainingHp == beforeTargets[seat] + 1;
                }) &&
                resolved.ResolutionId == requested.ResolutionId &&
                resolved.Effect == ActiveSkillEffectKind.DiscardAndRecoverTargets &&
                selectedCardIds.All(cardId =>
                    game.CardMovements.Count(movement =>
                        movement.CardId == cardId &&
                        movement.Reason == CardMoveReasons.HuichunDiscard) == 2 &&
                    game.CardMovements.Any(movement =>
                        movement.CardId == cardId &&
                        movement.To == CardLocation.DiscardPile &&
                        movement.Reason == CardMoveReasons.HuichunDiscard)),
            "Huichun must publish exact multi-target recovery events and named Processing movements.");

        var observer = game.CreateSnapshot(1);
        var hiddenSource = observer.Players.Single(player => player.Seat == 0);
        Require(hiddenSource.Hand.Count == 0 &&
                hiddenSource.HandCount == afterSource.Hand.Count &&
                !SnapshotJson.Serialize(observer).Contains("SkillCardsDiscarded", StringComparison.Ordinal),
            "Huichun private discard ids must not enter an observer snapshot.");

        var restored = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(
            SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(after) &&
            game.Events.Select(EventSignature).SequenceEqual(restored.Events.Select(EventSignature)),
            "Huichun did not replay the same state and typed event stream.");

        var journal = CommandJson.Deserialize(CommandJson.Serialize(game.AcceptedCommands));
        var command = journal.OfType<UseSkillCommand>()
            .Single(item => item.Skill == SkillKind.Huichun);
        Require(command.CardIds.SequenceEqual(selectedCardIds) &&
                command.TargetSeats.SequenceEqual(selectedTargetSeats),
            "The serialized Huichun command must preserve both selected cards and targets.");
    }

    private static void ZhihengFlow(ContentRegistry registry)
    {
        Require(
            registry.Skills.TryGetValue("standard:zhiheng", out var skill) &&
            skill.LegacyKind == SkillKind.Zhiheng,
            "The card-selection active skill must retain a typed legacy projection.");

        var game = FindZhihengGame(registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The Zhiheng fixture failed to start.");
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("The Zhiheng fixture did not reach human play.");
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Zhiheng);
        Require(action.MinCardCount == 1 && action.MaxCardCount > 1,
            "Zhiheng must publish a non-empty variable card-selection contract.");
        var activeCardIds = prompt.ActiveSkillValidCardIds ?? [];
        Require(prompt.ActiveSkillKind == SkillKind.Zhiheng && activeCardIds.Count > 0,
            "The Zhiheng prompt must publish its private candidate card ids.");
        Require(activeCardIds.Order().SequenceEqual(
                game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hand.Select(card => card.Id).Order()),
            "The Zhiheng prompt card candidates must match the human hand.");
        Require(prompt.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("skill") == SkillKind.Zhiheng.ToString() &&
                choice.Parameters.GetValueOrDefault("min-card-count") == "1" &&
                choice.Parameters.GetValueOrDefault("max-card-count") == action.MaxCardCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            "The Zhiheng prompt choice must carry its selection bounds.");

        var before = game.CreateSnapshot(0, revealAll: true);
        var beforePlayer = before.Players.Single(player => player.Seat == 0);
        var selected = beforePlayer.Hand.OrderBy(card => card.Id).Take(2).Select(card => card.Id).ToArray();
        var beforeState = SnapshotJson.Serialize(before);
        var beforeEventCount = game.Events.Count;
        var beforeCommandCount = game.AcceptedCommands.Count;
        var invalid = game.Submit(new UseSkillCommand(
            0,
            SkillKind.Zhiheng,
            [selected[0], selected[0]],
            [],
            game.Revision,
            prompt.PromptId));
        Require(!invalid.Accepted && invalid.Error?.Code == CommandErrorCode.InvalidCard,
            "Duplicate Zhiheng card selections must be rejected.");
        Require(beforeState == SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                beforeEventCount == game.Events.Count && beforeCommandCount == game.AcceptedCommands.Count,
            "A rejected Zhiheng selection must be atomic.");

        var used = game.Submit(new UseSkillCommand(
            0,
            SkillKind.Zhiheng,
            selected,
            [],
            game.Revision,
            prompt.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "The legal Zhiheng command was rejected.");
        var after = game.CreateSnapshot(0, revealAll: true);
        var afterPlayer = after.Players.Single(player => player.Seat == 0);
        Require(afterPlayer.Hand.Count == beforePlayer.Hand.Count,
            "Zhiheng must exchange the selected cards one-for-one.");
        Require(selected.All(cardId => !afterPlayer.Hand.Any(card => card.Id == cardId)),
            "Zhiheng must not return discarded physical cards to the actor's hand.");
        Require(game.ResolutionStack.Count == 0 && game.State.ProcessingCardCount == 0,
            "The synchronous Zhiheng frame must close without stranded processing cards.");

        var requested = game.Events.Select(item => item.Payload).OfType<ActiveSkillRequestedEvent>().Single(item => item.Skill == SkillKind.Zhiheng);
        var discarded = game.Events.Select(item => item.Payload).OfType<SkillCardsDiscardedEvent>().Single();
        var drawn = game.Events.Select(item => item.Payload).OfType<SkillCardsDrawnEvent>().Single(item => item.Skill == SkillKind.Zhiheng);
        var resolved = game.Events.Select(item => item.Payload).OfType<ActiveSkillResolvedEvent>().Single(item => item.Skill == SkillKind.Zhiheng);
        Require(requested.CardIds is not null && requested.CardIds.SequenceEqual(selected) &&
                discarded.ResolutionId == requested.ResolutionId && discarded.CardIds.SequenceEqual(selected),
            "Zhiheng must record the exact private discard selection on one resolution.");
        Require(drawn.ResolutionId == requested.ResolutionId && drawn.CardIds.Count == selected.Length &&
                drawn.CardIds.All(cardId => afterPlayer.Hand.Any(card => card.Id == cardId)),
            "Zhiheng must draw exactly as many cards as it discarded.");
        Require(resolved.ResolutionId == requested.ResolutionId &&
                resolved.Effect == ActiveSkillEffectKind.DiscardAndDraw,
            "Zhiheng must close its typed discard-and-draw effect.");
        Require(selected.All(cardId => game.CardMovements.Count(movement => movement.CardId == cardId &&
                movement.Reason == CardMoveReasons.ZhihengDiscard) == 2),
            "Each Zhiheng card must cross Hand, Processing and DiscardPile with its named reason.");
        Require(drawn.CardIds.All(cardId => game.CardMovements.Any(movement => movement.CardId == cardId &&
                movement.To == CardLocation.Hand(0) && movement.Reason == CardMoveReasons.ZhihengDraw)),
            "Each Zhiheng draw must use the named movement reason.");

        var observer = game.CreateSnapshot(1);
        Require(observer.Players.Single(player => player.Seat == 0).Hand.Count == 0 &&
                observer.Players.Single(player => player.Seat == 0).HandCount == afterPlayer.Hand.Count,
            "An observer must see only the Zhiheng actor's hand count.");
        var observerJson = SnapshotJson.Serialize(observer);
        Require(!observerJson.Contains("SkillCardsDiscarded", StringComparison.Ordinal),
            "Zhiheng private card ids must not enter an ordinary observer snapshot.");

        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(after) &&
                game.Events.Select(EventSignature).SequenceEqual(restored.Events.Select(EventSignature)),
            "Zhiheng did not replay the same full state and typed event stream.");
        var journal = CommandJson.Deserialize(CommandJson.Serialize(game.AcceptedCommands));
        Require(journal.OfType<UseSkillCommand>().Single(command => command.Skill == SkillKind.Zhiheng).CardIds.SequenceEqual(selected),
            "The serialized Zhiheng command must preserve its exact card selection.");

        RendeFlow(registry);
    }

    private static void RendeFlow(ContentRegistry registry)
    {
        Require(
            registry.Skills.TryGetValue("standard:rende", out var skill) &&
            skill.LegacyKind == SkillKind.Rende,
            "The target-selection active skill must retain a typed legacy projection.");

        var directEffect = SkillRegistry.GetActive(SkillKind.Rende)!.GetEffect(new ActiveSkillContext(
            new PlayerSkillContext(0, 3, 4, 4, TurnPhase.Play),
            SelectedCardCount: 2,
            SelectedTargetCount: 1));
        Require(directEffect.RecoveryAmount == 1 && directEffect.MinTargetCount == 1 && directEffect.MaxTargetCount == 1,
            "Rende must expose its two-card recovery and one-target contract.");

        var game = FindRendeGame(registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "The Rende fixture failed to start.");
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("The Rende fixture did not reach human play.");
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Rende);
        var validCardIds = prompt.ActiveSkillValidCardIds ?? [];
        var validTargetSeats = prompt.ActiveSkillValidTargetSeats ?? [];
        Require(action.MinCardCount == 1 && action.MaxCardCount == validCardIds.Count &&
                action.MinTargetCount == 1 && action.MaxTargetCount == 1,
            "Rende must publish variable card and exact one-target bounds.");
        Require(validCardIds.Count > 0 && validTargetSeats.Count > 0 &&
                !validTargetSeats.Contains(0) &&
                validTargetSeats.All(seat => game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == seat).IsAlive),
            "Rende must publish only the other living seats as private target candidates.");

        var before = game.CreateSnapshot(0, revealAll: true);
        var beforeSource = before.Players.Single(player => player.Seat == 0);
        var targetSeat = validTargetSeats.Order().First();
        var beforeTarget = before.Players.Single(player => player.Seat == targetSeat);
        var selectedCardId = validCardIds.Order().First();
        var beforeState = SnapshotJson.Serialize(before);
        var beforeEventCount = game.Events.Count;
        var beforeCommandCount = game.AcceptedCommands.Count;
        var invalid = game.Submit(new UseSkillCommand(
            0,
            SkillKind.Rende,
            [selectedCardId],
            [0],
            game.Revision,
            prompt.PromptId));
        Require(!invalid.Accepted && invalid.Error?.Code == CommandErrorCode.InvalidTarget,
            "Rende must reject the actor as a target.");
        Require(beforeState == SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                beforeEventCount == game.Events.Count && beforeCommandCount == game.AcceptedCommands.Count,
            "A rejected Rende target must be atomic.");

        var used = game.Submit(new UseSkillCommand(
            0,
            SkillKind.Rende,
            [selectedCardId],
            [targetSeat],
            game.Revision,
            prompt.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "The legal Rende command was rejected.");
        var after = game.CreateSnapshot(0, revealAll: true);
        var afterSource = after.Players.Single(player => player.Seat == 0);
        var afterTarget = after.Players.Single(player => player.Seat == targetSeat);
        Require(afterSource.Hand.Count == beforeSource.Hand.Count - 1 &&
                afterTarget.Hand.Count == beforeTarget.Hand.Count + 1 &&
                !afterSource.Hand.Any(card => card.Id == selectedCardId) &&
                afterTarget.Hand.Any(card => card.Id == selectedCardId),
            "Rende must transfer the selected physical card to the chosen target.");
        Require(!game.GetHumanLegalActions().Any(candidate =>
                candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Rende),
            "Rende must be unavailable after one use in the same turn.");
        Require(game.ResolutionStack.Count == 0 && game.State.ProcessingCardCount == 0,
            "The synchronous Rende frame must close without stranded processing cards.");

        var requested = game.Events.Select(item => item.Payload).OfType<ActiveSkillRequestedEvent>()
            .Single(item => item.Skill == SkillKind.Rende);
        var given = game.Events.Select(item => item.Payload).OfType<SkillCardsGivenEvent>().Single();
        var resolved = game.Events.Select(item => item.Payload).OfType<ActiveSkillResolvedEvent>()
            .Single(item => item.Skill == SkillKind.Rende);
        Require(requested.CardIds is not null && requested.CardIds.SequenceEqual([selectedCardId]) &&
                requested.TargetSeats is not null && requested.TargetSeats.SequenceEqual([targetSeat]) &&
                given.ResolutionId == requested.ResolutionId && given.SourceSeat == 0 &&
                given.TargetSeat == targetSeat && given.CardIds.SequenceEqual([selectedCardId]),
            "Rende must record the exact private card and target selections on one resolution.");
        Require(resolved.ResolutionId == requested.ResolutionId &&
                resolved.Effect == ActiveSkillEffectKind.GiveCardsAndRecover,
            "Rende must close its typed give-card effect.");
        Require(game.CardMovements.Count(movement => movement.CardId == selectedCardId &&
                movement.Reason == CardMoveReasons.RendeGive) == 2 &&
                game.CardMovements.Any(movement => movement.CardId == selectedCardId &&
                    movement.To == CardLocation.Hand(targetSeat) && movement.Reason == CardMoveReasons.RendeGive),
            "Rende must move each given card through Processing with its named reason.");

        var observer = game.CreateSnapshot(1);
        var hiddenSource = observer.Players.Single(player => player.Seat == 0);
        Require(hiddenSource.Hand.Count == 0 && hiddenSource.HandCount == afterSource.Hand.Count,
            "An observer must see only the Rende source hand count.");
        Require(!SnapshotJson.Serialize(observer).Contains("SkillCardsGiven", StringComparison.Ordinal),
            "Rende private card ids must not enter an ordinary observer snapshot.");

        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(after) &&
                game.Events.Select(EventSignature).SequenceEqual(restored.Events.Select(EventSignature)),
            "Rende did not replay the same full state and typed event stream.");
        var journal = CommandJson.Deserialize(CommandJson.Serialize(game.AcceptedCommands));
        var command = journal.OfType<UseSkillCommand>().Single(item => item.Skill == SkillKind.Rende);
        Require(command.CardIds.SequenceEqual([selectedCardId]) && command.TargetSeats.SequenceEqual([targetSeat]),
            "The serialized Rende command must preserve both private selections.");
    }

    private static GameEngine FindKujinGame(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = "identity:active-skills-5",
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2
            }, registry);
            var human = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
            if (human.Skill == SkillKind.Kujin)
            {
                return game;
            }
        }

        throw new InvalidOperationException("Could not find a deterministic active-skill fixture.");
    }

    private static GameEngine FindKujinDyingGame(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = "identity:active-skills-5",
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = true,
                AiPolicyVersion = 2
            }, registry);
            var human = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
            if (human.Skill != SkillKind.Kujin || !game.Submit(new StartGameCommand()).Accepted)
            {
                continue;
            }

            var canReachOneHp = true;
            for (var use = 0; use < 4; use++)
            {
                var prompt = game.PendingDecision;
                if (prompt?.Kind != DecisionKind.PlayCard ||
                    !game.GetHumanLegalActions().Any(candidate =>
                        candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Kujin) ||
                    !game.Submit(new UseSkillCommand(
                        0,
                        SkillKind.Kujin,
                        [],
                        [],
                        game.Revision,
                        prompt.PromptId)).Accepted)
                {
                    canReachOneHp = false;
                    break;
                }
            }

            if (canReachOneHp)
            {
                var atOne = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
                if (atOne.Hp == 1 && atOne.Hand.Any(card => card.Kind == CardKind.Peach))
                {
                    return game;
                }
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Kujin dying continuation fixture.");
    }

    private static GameEngine FindZhihengGame(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = "identity:active-skills-5",
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2
            }, registry);
            var human = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
            if (human.Skill == SkillKind.Zhiheng)
            {
                return game;
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Zhiheng fixture.");
    }

    private static GameEngine FindRendeGame(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = "identity:active-skills-5",
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2
            }, registry);
            var human = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
            if (human.Skill == SkillKind.Rende)
            {
                return game;
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Rende fixture.");
    }

    private static GameEngine FindQingnangGame(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 8192; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = "identity:active-skills-5",
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = true,
                AiPolicyVersion = 2
            }, registry);
            var initial = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
            if (initial.Skill != SkillKind.Qingnang || !game.Submit(new StartGameCommand()).Accepted)
            {
                continue;
            }

            var prompt = game.PendingDecision;
            var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash &&
                action.CardId is not null &&
                action.TargetSeats.Count == 1);
            if (prompt?.Kind != DecisionKind.PlayCard || slash is null)
            {
                continue;
            }

            var played = game.Submit(new PlayCardCommand(
                0,
                slash.CardId!.Value,
                slash.TargetSeats.ToArray(),
                game.Revision,
                prompt.PromptId,
                slash.PlayedCardKind,
                slash.TargetCardId));
            if (!played.Accepted || game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var snapshot = game.CreateSnapshot(0, revealAll: true);
            if (snapshot.Players.Any(player => player.IsAlive && player.Hp < player.MaxHp) &&
                game.GetHumanLegalActions().Any(action =>
                    action.Kind == LegalActionKind.UseSkill && action.Skill == SkillKind.Qingnang))
            {
                return game;
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Qingnang recovery fixture.");
    }

    private static GameEngine FindHuichunGame(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 8192; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = "identity:active-skills-5",
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = true,
                AiPolicyVersion = 2
            }, registry);
            var initial = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
            if (initial.Skill != SkillKind.Huichun || !game.Submit(new StartGameCommand()).Accepted)
            {
                continue;
            }

            for (var round = 0; round < 24 && game.State.Status != EngineStatus.Completed; round++)
            {
                var snapshot = game.CreateSnapshot(0, revealAll: true);
                if (snapshot.Players.Count(player => player.IsAlive && player.Hp < player.MaxHp) >= 2 &&
                    game.GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.UseSkill && action.Skill == SkillKind.Huichun))
                {
                    return game;
                }

                var prompt = game.PendingDecision;
                if (prompt?.Kind != DecisionKind.PlayCard)
                {
                    break;
                }

                var legalSlash = game.GetHumanLegalActions()
                    .Where(action =>
                        action.Kind == LegalActionKind.Slash &&
                        action.CardId is not null &&
                        action.TargetSeats.Count == 1)
                    .OrderBy(action =>
                    {
                        var target = snapshot.Players.Single(player => player.Seat == action.TargetSeats[0]);
                        return target.Hp < target.MaxHp ? 1 : 0;
                    })
                    .ThenBy(action => action.TargetSeats[0])
                    .ThenBy(action => action.CardId)
                    .FirstOrDefault();
                if (legalSlash is not null)
                {
                    var played = game.Submit(new PlayCardCommand(
                        0,
                        legalSlash.CardId!.Value,
                        legalSlash.TargetSeats.ToArray(),
                        game.Revision,
                        prompt.PromptId,
                        legalSlash.PlayedCardKind,
                        legalSlash.TargetCardId));
                    if (!played.Accepted)
                    {
                        break;
                    }

                    continue;
                }

                var ended = game.Submit(new EndPlayPhaseCommand(
                    0,
                    game.Revision,
                    prompt.PromptId));
                if (!ended.Accepted)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Huichun multi-target recovery fixture.");
    }

    private static string EventSignature(EventEnvelope item) => JsonSerializer.Serialize(new
    {
        item.Id,
        item.ParentId,
        item.Sequence,
        item.Revision,
        item.CorrelationId,
        Payload = JsonSerializer.Serialize(item.Payload, item.Payload.GetType())
    });

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
