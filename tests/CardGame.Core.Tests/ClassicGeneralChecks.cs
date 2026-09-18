using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ClassicGeneralChecks
{
    public static void ContentContract()
    {
        var legacy = StandardContentRegistry.CreateWithRescueSkills();
        var classic = StandardContentRegistry.CreateWithClassicGenerals();

        Require(!legacy.Packages.Any(package => package.Id == "standard-classic-generals"),
            "The legacy rescue registry must not silently gain the classic roster.");
        Require(classic.Packages.Select(package => $"{package.Id}@{package.Version}")
            .SequenceEqual([
                "standard@1.11.0",
                "standard-active-skills@1.0.0",
                "standard-rescue-skills@1.0.0",
                "standard-classic-generals@1.0.0"]),
            "The classic package signature must be explicit and dependency ordered.");
        Require(classic.ContentHash != legacy.ContentHash,
            "The opt-in classic roster must have its own content fingerprint.");

        var simaYi = classic.Generals["classic:sima-yi"];
        Require(simaYi.Name == "司马懿" && simaYi.BaseHp == 3 &&
                simaYi.SkillIds.SequenceEqual(["classic:feedback", "standard:guicai"]),
            "Sima Yi must expose Feedback and Guicai in a stable order.");
        var huaTuo = classic.Generals["classic:hua-tuo"];
        Require(huaTuo.Name == "华佗" && huaTuo.BaseHp == 3 &&
                huaTuo.SkillIds.SequenceEqual(["standard:qingnang", "standard:jijiu"]),
            "Hua Tuo must expose Qingnang and Jijiu in a stable order.");
        Require(classic.Generals["classic:liu-bei"].SkillIds.SequenceEqual(["standard:rende"]) &&
                classic.Generals["classic:sun-quan"].SkillIds.SequenceEqual(["standard:zhiheng"]) &&
                classic.Generals["classic:xiahou-dun"].SkillIds.SequenceEqual(["standard:ganglie"]),
            "The first classic roster must point at the implemented formal skills.");

        foreach (var modeId in new[] { "identity:classic-5", "identity:classic-8" })
        {
            var mode = classic.Modes[modeId];
            var pool = mode.GeneralPoolIds ?? [];
            Require(pool.Contains("classic:sima-yi", StringComparer.Ordinal) &&
                    pool.Contains("classic:hua-tuo", StringComparer.Ordinal) &&
                    !pool.Any(id => id.StartsWith("standard:demo-", StringComparison.Ordinal)),
                $"{modeId} must publish formal generals instead of demo placeholders.");
        }

        Require(GameCheckpoint.CurrentRulesVersion >= 11,
            "Classic rules 10 and suit-specific delayed judgments in rules 11 must remain replay-versioned.");
        var feedback = SkillRegistry.Get(SkillKind.Feedback);
        var damaged = new PlayerSkillContext(0, 2, 3, 2, TurnPhase.Play);
        var feedbackContext = new DamageSkillContext(
            damaged,
            SourceSeat: 1,
            SourceCard: CardKind.Slash,
            SourceCardIsInProcessing: true,
            Amount: 1,
            TargetSeat: 0,
            SourceCardCount: 2);
        Require(feedback.ClaimsDamageCard(feedbackContext) &&
                feedback.GetDamageSkillEffect(feedbackContext) == DamageSkillEffectKind.TakeSourceCard,
            "Feedback must retain the legacy hook while declaring its formal source-card effect.");

        var jijiu = SkillRegistry.Get(SkillKind.Jijiu);
        var red = new Card(9001, CardKind.Slash, Suit.Heart, 7);
        Require(!jijiu.CanUseAsDyingRescue(damaged with { IsOwnTurn = true }, red) &&
                jijiu.CanUseAsDyingRescue(damaged with { IsOwnTurn = false }, red),
            "Formal Jijiu must only convert red cards outside the owner's turn.");
    }

    public static void SetupHealthAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var currentSun = SelectGeneral(registry, "classic:sun-quan", GameCheckpoint.CurrentRulesVersion);
        var currentSunPlayer = currentSun.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(currentSunPlayer.MaxHp == 5 && currentSunPlayer.Hp == 5,
            "A classic 4-HP Lord must receive the identity-mode +1 maximum HP.");
        Require(currentSunPlayer.Skills is { Count: 1 } && currentSunPlayer.Skills[0].Kind == SkillKind.Zhiheng,
            "The current snapshot must publish the selected general's ordered skill list.");

        var legacySun = SelectGeneral(registry, "classic:sun-quan", rulesVersion: 9);
        var legacySunPlayer = legacySun.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(legacySunPlayer.MaxHp == 5 && legacySunPlayer.Hp == 5 && legacySunPlayer.Skills is null,
            $"A rules-v9 replay must retain the old fixed 5-HP Lord rule and singular skill projection " +
            $"(rules={legacySun.RulesVersion}, hp={legacySunPlayer.Hp}/{legacySunPlayer.MaxHp}, skills={legacySunPlayer.Skills?.Count.ToString() ?? "null"}).");

        var simaYi = SelectGeneral(registry, "classic:sima-yi", GameCheckpoint.CurrentRulesVersion);
        var simaYiPlayer = simaYi.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(simaYiPlayer.MaxHp == 4 &&
                simaYiPlayer.Skills!.Select(skill => skill.Kind).SequenceEqual([SkillKind.Feedback, SkillKind.Guicai]),
            "Sima Yi must combine base 3 HP, the Lord bonus, Feedback and Guicai.");
        var legacySimaYi = SelectGeneral(registry, "classic:sima-yi", rulesVersion: 9)
            .CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(legacySimaYi.MaxHp == 5 && legacySimaYi.Skills is null,
            "Rules v9 must ignore the new base HP and additional-skill fields.");

        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(simaYi.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(simaYi.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(simaYi)),
            "A selected multi-skill classic general must replay exactly.");
    }

    public static void FormalFeedbackFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        GameEngine? selectedGame = null;
        PendingDecision? selectedPrompt = null;

        for (var seed = 1; seed <= 8_192 && selectedGame is null; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Classic Feedback fixture failed to start.");
            var simaYiChoice = started.Result.PendingDecision?.Choices.FirstOrDefault(choice =>
                choice.ContentIds.SequenceEqual(["classic:sima-yi"]));
            if (simaYiChoice is null) continue;

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:sima-yi",
                game.Revision,
                game.PendingDecision!.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Sima Yi selection was rejected.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Classic setup did not advance.");
            var result = advanced.Result;
            for (var step = 0; result.Status != EngineStatus.Completed && step < 4_000; step++)
            {
                if (result.Status == EngineStatus.AwaitingHumanResponse &&
                    result.PendingDecision is { Kind: DecisionKind.Feedback } feedbackPrompt)
                {
                    selectedGame = game;
                    selectedPrompt = feedbackPrompt;
                    break;
                }

                result = DeclineOrAdvance(game, result);
            }
        }

        if (selectedGame is null || selectedPrompt is null)
            throw new InvalidOperationException("No deterministic classic Feedback source-card boundary was found.");

        var gameWithFeedback = selectedGame;
        var prompt = selectedPrompt;
        var frame = gameWithFeedback.ResolutionStack.OfType<DamageSkillFrame>().Single();
        Require(frame.Skill == SkillKind.Feedback && frame.Effect == DamageSkillEffectKind.TakeSourceCard,
            "Classic Feedback must pause with the source-card effect.");
        Require(prompt.IsPrivate && prompt.SourceSeat is not null && prompt.TargetSeat == prompt.SourceSeat,
            "The source-card choice must be visible only to the skill owner.");
        var sourceSeat = prompt.SourceSeat!.Value;
        Require(gameWithFeedback.CreateSnapshot(sourceSeat).PendingDecision is null,
            "The damage source must not receive the private Feedback choice.");

        var sourceBefore = gameWithFeedback.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == sourceSeat);
        var takeChoice = prompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("response") == "feedback-source-card");
        var sourceZone = takeChoice.Parameters["source-zone"];
        int cardId;
        CardLocation from;
        if (sourceZone == "hand")
        {
            var slot = int.Parse(takeChoice.Parameters["slot-index"], System.Globalization.CultureInfo.InvariantCulture);
            cardId = sourceBefore.Hand[slot].Id;
            from = CardLocation.Hand(sourceSeat);
            Require(takeChoice.Cards.Count == 0,
                "A hidden source hand choice must expose an opaque slot, not a card id.");
        }
        else
        {
            cardId = takeChoice.Cards.Single();
            from = CardLocation.Equipment(sourceSeat);
        }

        var accepted = gameWithFeedback.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            takeChoice.Id,
            gameWithFeedback.Revision));
        Require(accepted.Accepted, accepted.Error?.Message ?? "Classic Feedback choice was rejected.");
        Require(gameWithFeedback.CardMovements.Any(movement =>
                movement.CardId == cardId &&
                movement.From == from &&
                movement.To == CardLocation.Hand(0) &&
                movement.Reason == CardMoveReasons.FeedbackTakeSourceCard),
            "Classic Feedback must transfer the exact selected source card into the owner's hand.");
        Require(gameWithFeedback.Events.Any(item =>
                item.Payload is DamageSkillCardTakenEvent taken &&
                taken.OwnerSeat == 0 &&
                taken.SourceSeat == sourceSeat &&
                taken.CardId == cardId &&
                taken.From == from),
            "Classic Feedback must publish a typed trusted-host card-taken event.");
        Require(gameWithFeedback.CreateSnapshot(0).Players.Single(player => player.Seat == 0)
                .Hand.Any(card => card.Id == cardId),
            "The Feedback owner must see the acquired physical card.");

        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(gameWithFeedback.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(gameWithFeedback.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(gameWithFeedback)),
            "The formal Feedback choice must restore with identical state and events.");
    }

    public static void FormalJianxiongFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(GameCheckpoint.CurrentRulesVersion >= 16,
            "Formal Jianxiong must have an explicit rules version.");

        var duelContext = new DamageSkillContext(
            new PlayerSkillContext(0, 3, 4, 2, TurnPhase.Play),
            SourceSeat: 1,
            SourceCard: CardKind.Duel,
            SourceCardIsInProcessing: true,
            Amount: 1,
            SourceCardId: 9001,
            TargetSeat: 0);
        var effectMethod = typeof(GameEngine).GetMethod(
            "ResolveDamageSkillEffect",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("Damage-skill effect resolver not found.");
        var currentRules = CreateInteractive(registry, seed: 1);
        var legacyRules = GameReplay.Restore(
            CreateInteractive(registry, seed: 1).CreateCheckpoint() with { RulesVersion = 15 },
            registry);
        var jianxiong = SkillRegistry.Get(SkillKind.Jianxiong);
        Require((DamageSkillEffectKind)effectMethod.Invoke(currentRules, [jianxiong, duelContext])! ==
                DamageSkillEffectKind.ClaimDamageCard &&
                (DamageSkillEffectKind)effectMethod.Invoke(legacyRules, [jianxiong, duelContext])! ==
                DamageSkillEffectKind.None,
            "Rules v16 must accept a Duel damage card while rules v15 retains Slash-only Jianxiong.");

        GameEngine? selectedGame = null;
        PendingDecision? selectedPrompt = null;
        DamageSkillFrame? selectedFrame = null;
        for (var seed = 1; seed <= 8_192 && selectedGame is null; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Classic Jianxiong fixture failed to start.");
            var caoCaoChoice = started.Result.PendingDecision?.Choices.FirstOrDefault(choice =>
                choice.ContentIds.SequenceEqual(["standard:cao-cao"]));
            if (caoCaoChoice is null) continue;

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "standard:cao-cao",
                game.Revision,
                game.PendingDecision!.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Cao Cao selection was rejected.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Classic Jianxiong setup did not advance.");
            var result = advanced.Result;
            for (var step = 0; result.Status != EngineStatus.Completed && step < 4_000; step++)
            {
                var frame = game.ResolutionStack.OfType<DamageSkillFrame>().SingleOrDefault(candidate =>
                    candidate.OwnerSeat == 0 &&
                    candidate.Skill == SkillKind.Jianxiong &&
                    candidate.Effect == DamageSkillEffectKind.ClaimDamageCard);
                if (result.Status == EngineStatus.AwaitingHumanResponse &&
                    result.PendingDecision is { Kind: DecisionKind.Feedback } prompt &&
                    frame is not null &&
                    frame.CardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))
                {
                    selectedGame = game;
                    selectedPrompt = prompt;
                    selectedFrame = frame;
                    break;
                }

                result = DeclineOrAdvance(game, result);
            }
        }

        if (selectedGame is null || selectedPrompt is null || selectedFrame is null)
            throw new InvalidOperationException("No deterministic non-Slash Jianxiong boundary was found.");

        var gameWithJianxiong = selectedGame;
        var promptAtBoundary = selectedPrompt;
        var frameAtBoundary = selectedFrame;
        Require(promptAtBoundary.IsPrivate && promptAtBoundary.PlayerSeat == 0 &&
                promptAtBoundary.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "feedback") &&
                promptAtBoundary.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "take-damage"),
            "Formal Jianxiong must publish private take/skip choices.");
        Require(gameWithJianxiong.CreateSnapshot(1).PendingDecision is null,
            "Other viewers must not receive the private Jianxiong choice.");

        var boundaryCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(gameWithJianxiong.CreateCheckpoint()));
        var claimBranch = GameReplay.Restore(boundaryCheckpoint, registry);
        Require(SnapshotJson.Serialize(claimBranch.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(gameWithJianxiong.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(claimBranch).SequenceEqual(EventSignatures(gameWithJianxiong)),
            "The pending formal Jianxiong choice must restore exactly.");

        var skipChoice = promptAtBoundary.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("response") == "take-damage");
        var skipped = gameWithJianxiong.Submit(new AnswerPromptCommand(
            0,
            promptAtBoundary.PromptId,
            skipChoice.Id,
            gameWithJianxiong.Revision));
        Require(skipped.Accepted, skipped.Error?.Message ?? "Formal Jianxiong skip was rejected.");
        Require(gameWithJianxiong.Events.Select(item => item.Payload)
                .OfType<DamageSkillResolvedEvent>()
                .Any(resolved => resolved.Skill == SkillKind.Jianxiong && !resolved.Used) &&
                !gameWithJianxiong.Events.Select(item => item.Payload)
                    .OfType<DamageCardClaimedEvent>()
                    .Any(claimed => claimed.Skill == SkillKind.Jianxiong &&
                                    claimed.CardId == frameAtBoundary.CardId),
            "Skipping formal Jianxiong must leave the damage card unclaimed.");

        var claimPrompt = claimBranch.PendingDecision ??
            throw new InvalidOperationException("Restored Jianxiong branch lost its prompt.");
        var claimChoice = claimPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("response") == "feedback");
        var claimed = claimBranch.Submit(new AnswerPromptCommand(
            0,
            claimPrompt.PromptId,
            claimChoice.Id,
            claimBranch.Revision));
        Require(claimed.Accepted, claimed.Error?.Message ?? "Formal Jianxiong claim was rejected.");
        Require(claimBranch.CardMovements.Any(movement =>
                movement.CardId == frameAtBoundary.CardId &&
                movement.From == CardLocation.Processing &&
                movement.To == CardLocation.Hand(0) &&
                movement.Reason == CardMoveReasons.JianxiongClaim) &&
                claimBranch.Events.Select(item => item.Payload)
                    .OfType<DamageCardClaimedEvent>()
                    .Any(claim => claim.Skill == SkillKind.Jianxiong &&
                                  claim.CardId == frameAtBoundary.CardId) &&
                claimBranch.CreateSnapshot(0).Players[0].Hand.Any(card =>
                    card.Id == frameAtBoundary.CardId),
            "Formal Jianxiong must move the exact non-Slash damage card into Cao Cao's hand.");

        var claimedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(claimBranch.CreateCheckpoint()));
        var replayedClaim = GameReplay.Restore(claimedCheckpoint, registry);
        Require(SnapshotJson.Serialize(replayedClaim.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(claimBranch.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replayedClaim).SequenceEqual(EventSignatures(claimBranch)),
            "The claimed non-Slash Jianxiong branch must replay exactly.");
    }

    public static void FormalZhihengEquipmentFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(GameCheckpoint.CurrentRulesVersion >= 17,
            "Formal Zhiheng must have an explicit rules version.");

        GameEngine? current = null;
        GameEngine? legacy = null;
        LegalAction? equipmentAction = null;
        for (var seed = 1; seed <= 8_192 && current is null; seed++)
        {
            var candidate = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:sun-quan",
                GameCheckpoint.CurrentRulesVersion);
            var candidateEquipment = candidate?.GetHumanLegalActions()
                .FirstOrDefault(action => action.Kind == LegalActionKind.Equip && action.CardId is not null);
            if (candidate is null || candidateEquipment is null)
            {
                continue;
            }

            current = candidate;
            legacy = StartClassicGeneralAtPlay(registry, seed, "classic:sun-quan", rulesVersion: 16) ??
                throw new InvalidOperationException("The rules-v16 Zhiheng fixture did not reproduce.");
            equipmentAction = candidateEquipment;
        }

        if (current is null || legacy is null || equipmentAction?.CardId is not { } equipmentCardId)
            throw new InvalidOperationException("No deterministic classic Sun Quan equipment fixture was found.");

        Equip(current, equipmentCardId);
        Equip(legacy, equipmentCardId);

        var currentBefore = current.CreateSnapshot(0, revealAll: true);
        var currentPlayerBefore = currentBefore.Players.Single(player => player.Seat == 0);
        var currentPrompt = current.PendingDecision ??
            throw new InvalidOperationException("Current Zhiheng fixture lost its play prompt.");
        var currentAction = current.GetHumanLegalActions().Single(action =>
            action.Kind == LegalActionKind.UseSkill && action.Skill == SkillKind.Zhiheng);
        Require(currentPrompt.ActiveSkillValidCardIds?.Contains(equipmentCardId) == true &&
                currentAction.MaxCardCount == currentPlayerBefore.Hand.Count + currentPlayerBefore.Equipment.Count,
            "Rules v17 Zhiheng must publish hand and owned equipment cards in one private selection contract.");
        var equipmentOnlyView = currentBefore with
        {
            Players = currentBefore.Players.Select(player => player.Seat == 0
                ? player with { HandCount = 0, Hand = Array.Empty<CardSnapshot>() }
                : player).ToArray()
        };
        Require(new SimpleAiBrain(0, seed: 17).ChooseActiveSkillCards(equipmentOnlyView, currentAction)
                .SequenceEqual([equipmentCardId]),
            "Formal Zhiheng AI must be able to select its own equipment when no hand card is available.");

        var currentUsed = current.Submit(new UseSkillCommand(
            0,
            SkillKind.Zhiheng,
            [equipmentCardId],
            [],
            current.Revision,
            currentPrompt.PromptId));
        Require(currentUsed.Accepted, currentUsed.Error?.Message ?? "Equipment Zhiheng was rejected.");
        var currentAfter = current.CreateSnapshot(0, revealAll: true);
        var currentPlayerAfter = currentAfter.Players.Single(player => player.Seat == 0);
        Require(currentPlayerAfter.Equipment.All(card => card.Id != equipmentCardId) &&
                currentPlayerAfter.Hand.Count == currentPlayerBefore.Hand.Count + 1 &&
                !current.GetHumanLegalActions().Any(action =>
                    action.Kind == LegalActionKind.UseSkill && action.Skill == SkillKind.Zhiheng),
            "Rules v17 Zhiheng must discard the equipment, draw one card and enforce once per play phase.");
        Require(current.CardMovements.Count(movement =>
                    movement.CardId == equipmentCardId &&
                    movement.Reason == CardMoveReasons.ZhihengDiscard) == 2 &&
                current.CardMovements.Any(movement =>
                    movement.CardId == equipmentCardId &&
                    movement.From == CardLocation.Equipment(0) &&
                    movement.To == CardLocation.Processing) &&
                current.Events.Select(item => item.Payload)
                    .OfType<SkillCardsDiscardedEvent>()
                    .Any(discarded => discarded.Skill == SkillKind.Zhiheng &&
                                      discarded.CardIds.SequenceEqual([equipmentCardId])),
            "Equipment Zhiheng must retain the exact public source zone, processing move and typed event.");

        var restored = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(current.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(currentAfter) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(current)),
            "Equipment Zhiheng must restore with identical state and events.");

        var legacyBefore = legacy.CreateSnapshot(0, revealAll: true);
        var legacyPlayerBefore = legacyBefore.Players.Single(player => player.Seat == 0);
        var legacyPrompt = legacy.PendingDecision ??
            throw new InvalidOperationException("Legacy Zhiheng fixture lost its play prompt.");
        var legacyAction = legacy.GetHumanLegalActions().Single(action =>
            action.Kind == LegalActionKind.UseSkill && action.Skill == SkillKind.Zhiheng);
        Require(legacyPrompt.ActiveSkillValidCardIds?.Contains(equipmentCardId) == false &&
                legacyAction.MaxCardCount == legacyPlayerBefore.Hand.Count,
            "Rules v16 must retain the hand-only Zhiheng candidate set.");
        var legacyState = SnapshotJson.Serialize(legacyBefore);
        var legacyRejected = legacy.Submit(new UseSkillCommand(
            0,
            SkillKind.Zhiheng,
            [equipmentCardId],
            [],
            legacy.Revision,
            legacyPrompt.PromptId));
        Require(!legacyRejected.Accepted && legacyRejected.Error?.Code == CommandErrorCode.InvalidCard &&
                SnapshotJson.Serialize(legacy.CreateSnapshot(0, revealAll: true)) == legacyState,
            "Rules v16 must reject an equipment Zhiheng selection atomically.");

        var legacyHandCardId = legacyPlayerBefore.Hand.First().Id;
        var legacyUsed = legacy.Submit(new UseSkillCommand(
            0,
            SkillKind.Zhiheng,
            [legacyHandCardId],
            [],
            legacy.Revision,
            legacyPrompt.PromptId));
        Require(legacyUsed.Accepted, legacyUsed.Error?.Message ??
            "Rules-v16 hand-only Zhiheng was rejected.");
        if (legacy.PendingDecision?.Kind != DecisionKind.PlayCard)
        {
            var advanced = legacy.Submit(new AdvanceCommand(legacy.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ??
                "Rules-v16 Zhiheng did not return to the play boundary.");
        }
        Require(legacy.GetHumanLegalActions().Any(action =>
                action.Kind == LegalActionKind.UseSkill && action.Skill == SkillKind.Zhiheng),
            "Rules v16 must retain the historical repeatable hand-only Zhiheng behavior.");
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

    private static GameEngine CreateInteractive(ContentRegistry registry, int seed) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = "identity:classic-5",
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 220
        }, registry);

    private static EngineRunResult DeclineOrAdvance(GameEngine game, EngineRunResult result)
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
            throw new InvalidOperationException(accepted.Error?.Message ?? $"Could not advance from {result.Status} / {prompt?.Kind}.");
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
