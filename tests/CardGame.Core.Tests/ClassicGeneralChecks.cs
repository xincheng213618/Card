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
        var legacyClassic = StandardContentRegistry.CreateWithClassicGenerals(legacyRoster: true);
        var tianduClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 1, 0));
        var fanjianClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 2, 0));

        Require(!legacy.Packages.Any(package => package.Id == "standard-classic-generals"),
            "The legacy rescue registry must not silently gain the classic roster.");
        Require(classic.Packages.Select(package => $"{package.Id}@{package.Version}")
            .SequenceEqual([
                "standard@1.11.0",
                "standard-active-skills@1.0.0",
                "standard-rescue-skills@1.0.0",
                "standard-classic-generals@1.3.0"]),
            "The classic package signature must be explicit and dependency ordered.");
        Require(legacyClassic.Packages.Last().Version == new Version(1, 0, 0) &&
                legacyClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "standard:guo-jia",
                    StringComparer.Ordinal) &&
                !legacyClassic.Generals.ContainsKey("classic:guo-jia"),
            "The legacy classic registry must remain reproducible for 1.0 checkpoints.");
        Require(tianduClassic.Packages.Last().Version == new Version(1, 1, 0) &&
                tianduClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "standard:zhou-yu",
                    StringComparer.Ordinal) &&
                !tianduClassic.Generals.ContainsKey("classic:zhou-yu") &&
                !tianduClassic.Skills.ContainsKey("classic:fanjian"),
            "The Tiandu-era classic registry must remain reproducible for 1.1 checkpoints.");
        Require(fanjianClassic.Packages.Last().Version == new Version(1, 2, 0) &&
                fanjianClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "standard:zhuge-liang",
                    StringComparer.Ordinal) &&
                !fanjianClassic.Generals.ContainsKey("classic:zhuge-liang") &&
                !fanjianClassic.Skills.ContainsKey("classic:guanxing"),
            "The Fanjian-era classic registry must remain reproducible for 1.2 checkpoints.");
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
        var guoJia = classic.Generals["classic:guo-jia"];
        Require(guoJia.BaseHp == 3 &&
                guoJia.SkillIds.SequenceEqual(["classic:tiandu", "standard:yiji"]),
            "The current classic Guo Jia must expose Tiandu and Yiji in a stable order.");
        var zhouYu = classic.Generals["classic:zhou-yu"];
        Require(zhouYu.BaseHp == 3 &&
                zhouYu.SkillIds.SequenceEqual(["standard:yingzi", "classic:fanjian"]),
            "The current classic Zhou Yu must expose Yingzi and Fanjian in a stable order.");
        var zhugeLiang = classic.Generals["classic:zhuge-liang"];
        Require(zhugeLiang.BaseHp == 3 &&
                zhugeLiang.SkillIds.SequenceEqual(["classic:guanxing", "standard:kongcheng"]),
            "The current classic Zhuge Liang must expose Guanxing and Kongcheng in a stable order.");

        foreach (var modeId in new[] { "identity:classic-5", "identity:classic-8" })
        {
            var mode = classic.Modes[modeId];
            var pool = mode.GeneralPoolIds ?? [];
            Require(pool.Contains("classic:sima-yi", StringComparer.Ordinal) &&
                    pool.Contains("classic:hua-tuo", StringComparer.Ordinal) &&
                    pool.Contains("classic:zhuge-liang", StringComparer.Ordinal) &&
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

    public static void FormalYingziChoice()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(GameCheckpoint.CurrentRulesVersion >= 21,
            "Formal Yingzi must have an explicit rules version.");

        var skipped = SelectGeneral(registry, "classic:zhou-yu", GameCheckpoint.CurrentRulesVersion);
        var reachedChoice = skipped.Submit(new AdvanceCommand(skipped.Revision));
        Require(reachedChoice.Accepted, reachedChoice.Error?.Message ?? "Could not reach the Yingzi choice.");
        var skipPrompt = skipped.PendingDecision;
        Require(skipPrompt is { Kind: DecisionKind.Yingzi } &&
                skipPrompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                    .OrderBy(action => action, StringComparer.Ordinal)
                    .SequenceEqual(["yingzi-skip", "yingzi-use"]),
            "Rules v21 must publish complete use and skip choices for Yingzi.");
        var initialHandCount = skipped.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0).HandCount;

        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(skipped.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(restored.PendingDecision?.Kind == DecisionKind.Yingzi,
            "A paused Yingzi choice must restore from the command checkpoint.");

        var staleSnapshot = SnapshotJson.Serialize(skipped.CreateSnapshot(0, revealAll: true));
        var rejected = skipped.Submit(new AnswerPromptCommand(
            0,
            skipPrompt!.PromptId,
            new ChoiceId("yingzi.unknown"),
            skipped.Revision));
        Require(!rejected.Accepted && rejected.Error?.Code == CommandErrorCode.InvalidChoice &&
                SnapshotJson.Serialize(skipped.CreateSnapshot(0, revealAll: true)) == staleSnapshot,
            "A forged Yingzi choice must be rejected atomically.");

        var skipChoice = skipPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "yingzi-skip");
        var skippedResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipChoice.Id,
            skipped.Revision));
        Require(skippedResult.Accepted && skipped.State.Phase == TurnPhase.Play && skipped.PendingDecision is null,
            skippedResult.Error?.Message ??
            $"Skipping Yingzi did not continue to the play phase (status={skipped.State.Status}, prompt={skipped.PendingDecision?.Kind.ToString() ?? "none"}, phase={skipped.State.Phase}).");
        Require(skipped.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount ==
                initialHandCount + 2,
            "Skipping Yingzi must draw only the normal two cards.");

        var restoredSkip = restored.Submit(new AnswerPromptCommand(
            0,
            restored.PendingDecision!.PromptId,
            restored.PendingDecision.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "yingzi-skip").Id,
            restored.Revision));
        Require(restoredSkip.Accepted &&
                SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(skipped.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(skipped)),
            "A restored Yingzi choice must resolve deterministically.");

        var used = SelectGeneral(registry, "classic:zhou-yu", GameCheckpoint.CurrentRulesVersion);
        Require(used.Submit(new AdvanceCommand(used.Revision)).Accepted,
            "Could not reach the second Yingzi choice.");
        var usePrompt = used.PendingDecision!;
        var beforeUse = used.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount;
        var usedResult = used.Submit(new AnswerPromptCommand(
            0,
            usePrompt.PromptId,
            usePrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "yingzi-use").Id,
            used.Revision));
        Require(usedResult.Accepted &&
                used.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount ==
                beforeUse + 3 &&
                used.Events.Any(envelope => envelope.Payload is DrawSkillResolvedEvent
                {
                    SourceSeat: 0,
                    Skill: SkillKind.Yingzi,
                    Used: true,
                    DrawCount: 3
                }),
            usedResult.Error?.Message ?? "Using Yingzi must draw one extra card and publish its result.");

        var legacy = SelectGeneral(registry, "classic:zhou-yu", rulesVersion: 20);
        var legacyResult = legacy.Submit(new AdvanceCommand(legacy.Revision));
        Require(legacyResult.Accepted && legacy.PendingDecision?.Kind == DecisionKind.PlayCard &&
                legacy.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount == 7 &&
                legacy.Events.All(envelope => envelope.Payload is not DrawSkillResolvedEvent),
            legacyResult.Error?.Message ?? "Rules v20 must retain automatic Yingzi drawing.");
    }

    public static void FormalTianduJudgment()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(GameCheckpoint.CurrentRulesVersion >= 22,
            "Formal Tiandu must have an explicit rules version.");
        var tiandu = SkillRegistry.Get(SkillKind.Tiandu);
        var context = new JudgmentSkillContext(
            new PlayerSkillContext(0, 3, 4, 2, TurnPhase.Draw),
            TargetSeat: 0,
            JudgmentReasons.Lightning,
            JudgmentCardId: 9001,
            JudgmentCardKind: CardKind.Dodge,
            JudgmentSuit: Suit.Heart,
            JudgmentRank: 8);
        Require(tiandu.CanClaimResolvedJudgment(context) &&
                !tiandu.CanClaimResolvedJudgment(context with { TargetSeat = 1 }),
            "Tiandu must only claim its owner's resolved judgment card.");

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
        Require(prompt is { Kind: DecisionKind.Tiandu, PlayerSeat: 0 } &&
                prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                    .OrderBy(action => action, StringComparer.Ordinal)
                    .SequenceEqual(["tiandu-claim", "tiandu-skip"]),
            "Rules v22 must pause after the judgment result with complete Tiandu choices.");
        Require(game.CardMovements.Any(movement =>
                movement.CardId == judgment.CardId &&
                movement.To == CardLocation.Judgment(0)) &&
                game.CardMovements.All(movement =>
                    movement.CardId != judgment.CardId || movement.To != CardLocation.DiscardPile),
            "The resolved judgment card must remain in the public judgment zone while Tiandu is pending.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var pausedRestore = GameReplay.Restore(pausedCheckpoint, registry);
        Require(pausedRestore.PendingDecision?.Kind == DecisionKind.Tiandu,
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
                movement.Reason == CardMoveReasons.TianduClaim),
            "Tiandu must publish the exact judgment-to-hand card movement.");
        Require(game.Events.Any(envelope => envelope.Payload is JudgmentCardClaimedEvent
        {
            OwnerSeat: 0,
            Skill: SkillKind.Tiandu,
            Used: true
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

        var legacy = StartClassicGeneralAtPlay(registry, game.Seed, "classic:guo-jia", rulesVersion: 21) ??
            throw new InvalidOperationException("The rules-v21 Tiandu fixture did not reproduce.");
        var legacyLightning = legacy.GetHumanLegalActions().Single(action =>
            action.Kind == LegalActionKind.Lightning && action.CardId is not null);
        Require(legacy.Submit(new PlayCardCommand(
            0,
            legacyLightning.CardId!.Value,
            legacyLightning.TargetSeats,
            legacy.Revision,
            legacy.PendingDecision!.PromptId)).Accepted &&
                DriveUntilOwnLightningJudgment(legacy, expectTiandu: false, out var legacyJudgment) &&
                legacy.PendingDecision?.Kind != DecisionKind.Tiandu &&
                legacy.CardMovements.Any(movement =>
                    movement.CardId == legacyJudgment.CardId &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.JudgmentFinish),
            "Rules v21 must retain the historical automatic judgment discard path.");
    }

    public static void FormalFanjianFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(GameCheckpoint.CurrentRulesVersion >= 23,
            "Formal Fanjian must have an explicit rules version.");

        var game = ReachZhouYuPlayPhase(registry, GameCheckpoint.CurrentRulesVersion);
        var playPrompt = game.PendingDecision!;
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Fanjian);
        Require(action.MinCardCount == 0 && action.MaxCardCount == 0 &&
                action.MinTargetCount == 1 && action.MaxTargetCount == 1,
            "Fanjian must publish a target-only active-skill contract.");

        var sourceBefore = game.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0).HandCount;
        var targetBefore = game.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 1);
        var used = game.Submit(new UseSkillCommand(
            0,
            SkillKind.Fanjian,
            [],
            [1],
            game.Revision,
            playPrompt.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "Fanjian use was rejected.");

        var suitPrompt = game.CreateSnapshot(1).PendingDecision;
        Require(suitPrompt is { Kind: DecisionKind.Fanjian, PlayerSeat: 1 } &&
                suitPrompt.ValidCardIds.Count == 0 &&
                suitPrompt.Choices.Count == 4 &&
                suitPrompt.Choices.All(choice =>
                    choice.Cards.Count == 0 &&
                    choice.Targets.Count == 0 &&
                    choice.Parameters.GetValueOrDefault("action") == "fanjian-choose-suit") &&
                suitPrompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("suit"))
                    .OrderBy(suit => suit, StringComparer.Ordinal)
                    .SequenceEqual(["Club", "Diamond", "Heart", "Spade"]),
            $"Fanjian must ask the target for four complete suit choices without exposing a source card " +
            $"(kind={suitPrompt?.Kind}, seat={suitPrompt?.PlayerSeat}, valid={suitPrompt?.ValidCardIds.Count}, " +
            $"choices={suitPrompt?.Choices.Count}, suits={string.Join(',', suitPrompt?.Choices.Select(choice => choice.Parameters.GetValueOrDefault("suit")) ?? [])}).");
        Require(game.Events.All(envelope => envelope.Payload is not FanjianCardRevealedEvent) &&
                game.CreateSnapshot(1).Players.Single(player => player.Seat == 0).Hand.Count == 0,
            "The target must not see Zhou Yu's private hand before choosing a suit.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restoredPrompt = GameReplay.Restore(pausedCheckpoint, registry);
        Require(restoredPrompt.CreateSnapshot(1).PendingDecision?.Kind == DecisionKind.Fanjian,
            "A paused Fanjian suit choice must restore from its command checkpoint.");

        var unchanged = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var forged = game.Submit(new AnswerPromptCommand(
            1,
            suitPrompt!.PromptId,
            new ChoiceId("fanjian-suit-forged"),
            game.Revision));
        Require(!forged.Accepted && forged.Error?.Code == CommandErrorCode.InvalidChoice &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == unchanged,
            "A forged Fanjian suit choice must be rejected atomically.");

        GameEngine? matching = null;
        GameEngine? mismatching = null;
        FanjianCardRevealedEvent? matchingEvent = null;
        FanjianCardRevealedEvent? mismatchingEvent = null;
        foreach (var suitName in new[] { "Spade", "Heart", "Club", "Diamond" })
        {
            var branch = GameReplay.Restore(pausedCheckpoint, registry);
            var branchPrompt = branch.CreateSnapshot(1).PendingDecision!;
            var choice = branchPrompt.Choices.Single(candidate =>
                candidate.Parameters.GetValueOrDefault("suit") == suitName);
            var answered = branch.Submit(new AnswerPromptCommand(
                1,
                branchPrompt.PromptId,
                choice.Id,
                branch.Revision));
            Require(answered.Accepted, answered.Error?.Message ?? $"Fanjian rejected {suitName}.");
            var revealed = branch.Events.Select(envelope => envelope.Payload)
                .OfType<FanjianCardRevealedEvent>()
                .Last();
            if (revealed.DamageTriggered)
            {
                mismatching ??= branch;
                mismatchingEvent ??= revealed;
            }
            else
            {
                matching ??= branch;
                matchingEvent ??= revealed;
            }
        }

        Require(matching is not null && matchingEvent is not null &&
                mismatching is not null && mismatchingEvent is not null,
            "The four suit branches must contain one matching and three mismatching outcomes for the same random card.");
        var matchingGame = matching!;
        var mismatchingGame = mismatching!;
        var matchedCard = matchingEvent!;
        var mismatchedCard = mismatchingEvent!;
        Require(matchedCard.CardId == mismatchedCard.CardId &&
                matchedCard.CardSuit == mismatchedCard.CardSuit &&
                matchedCard.ChosenSuit == matchedCard.CardSuit &&
                mismatchedCard.ChosenSuit != mismatchedCard.CardSuit,
            "Fanjian must select the same deterministic random card after, not before, the target's suit choice.");
        Require(matchingGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount ==
                sourceBefore - 1 &&
                matchingGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 1).HandCount ==
                targetBefore.HandCount + 1 &&
                matchingGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 1).Hp ==
                targetBefore.Hp,
            "A matching Fanjian card must transfer to the target without damage.");
        Require(mismatchingGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 1).Hp ==
                targetBefore.Hp - 1 &&
                mismatchingGame.Events.Any(envelope => envelope.Payload is DamageAppliedEvent
                {
                    SourceSeat: 0,
                    TargetSeat: 1,
                    Amount: 1,
                    Nature: DamageNature.Normal
                }),
            "A mismatching Fanjian card must cause one point of ordinary damage through the shared damage pipeline.");
        Require(matchingGame.CardMovements.Count(movement =>
                    movement.CardId == matchedCard.CardId &&
                    movement.Reason == CardMoveReasons.FanjianGive) == 2,
            "Fanjian must record the exact hand-to-processing-to-hand transfer.");
        var matchingReturnedToPlay = matchingGame.Submit(new AdvanceCommand(matchingGame.Revision));
        Require(matchingGame.Events.Any(envelope =>
                envelope.Payload is ActiveSkillResolvedEvent
                {
                    Skill: SkillKind.Fanjian
                }) &&
                matchingReturnedToPlay.Accepted &&
                matchingGame.PendingDecision?.Kind == DecisionKind.PlayCard &&
                matchingGame.GetHumanLegalActions().All(candidate => candidate.Skill != SkillKind.Fanjian),
            "Fanjian must complete its active-skill frame and remain limited to once per play phase.");

        for (var step = 0; mismatchingGame.ResolutionStack.Count > 0 && step < 100; step++)
        {
            var advanced = mismatchingGame.Submit(new AdvanceOneStepCommand(mismatchingGame.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Fanjian damage continuation did not advance.");
        }
        Require(mismatchingGame.ResolutionStack.Count == 0 &&
                mismatchingGame.Events.Any(envelope =>
                    envelope.Payload is ActiveSkillResolvedEvent
                    {
                        Skill: SkillKind.Fanjian
                    }),
            "Fanjian damage must close its ordinary damage triggers and active-skill frame.");
        var replay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(mismatchingGame.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(mismatchingGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replay).SequenceEqual(EventSignatures(mismatchingGame)),
            "The chosen Fanjian suit, random transfer and damage branch must replay exactly.");

        var legacy = ReachZhouYuPlayPhase(registry, rulesVersion: 22);
        Require(legacy.GetHumanLegalActions().All(candidate => candidate.Skill != SkillKind.Fanjian),
            "Rules v22 must not expose the formal Fanjian active action.");
    }

    public static void FormalGuanxingFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(GameCheckpoint.CurrentRulesVersion >= 24,
            "Formal Guanxing must have an explicit rules version.");

        var game = SelectGeneral(registry, "classic:zhuge-liang", GameCheckpoint.CurrentRulesVersion);
        var reachedOffer = game.Submit(new AdvanceCommand(game.Revision));
        Require(reachedOffer.Accepted, reachedOffer.Error?.Message ?? "Could not reach the Guanxing offer.");
        var offer = game.PendingDecision;
        Require(offer is
        {
            Kind: DecisionKind.Guanxing,
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
            Kind: DecisionKind.Guanxing,
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

        var aiChoice = new SimpleAiBrain(0, seed: 24).ChooseGuanxing(
            game.CreateSnapshot(0),
            topPrompt.Choices,
            thoughtSequence: 1);
        Require(topPrompt.Choices.Any(choice => choice.Id == aiChoice.Choice) &&
                topPrompt.Choices.Single(choice => choice.Id == aiChoice.Choice).Cards.Count == 1 &&
                aiChoice.Thought.Summary.Contains("观星", StringComparison.Ordinal),
            "Guanxing AI must choose only from its private published card candidates.");

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
            Kind: DecisionKind.Guanxing,
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

        var legacy = SelectGeneral(registry, "classic:zhuge-liang", rulesVersion: 23);
        var legacyAdvanced = legacy.Submit(new AdvanceCommand(legacy.Revision));
        Require(legacyAdvanced.Accepted && legacy.PendingDecision?.Kind != DecisionKind.Guanxing &&
                legacy.Events.All(envelope => envelope.Payload is not GuanxingResolvedEvent),
            "Rules v23 must retain the historical turn start without a Guanxing prompt.");
    }

    private static GameEngine ReachZhouYuPlayPhase(ContentRegistry registry, int rulesVersion)
    {
        var game = SelectGeneral(registry, "classic:zhou-yu", rulesVersion);
        var reachedYingzi = game.Submit(new AdvanceCommand(game.Revision));
        Require(reachedYingzi.Accepted && game.PendingDecision?.Kind == DecisionKind.Yingzi,
            reachedYingzi.Error?.Message ?? "Could not reach Zhou Yu's Yingzi choice.");
        var prompt = game.PendingDecision!;
        var skipped = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "yingzi-skip").Id,
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
                if (!expectTiandu || game.PendingDecision?.Kind == DecisionKind.Tiandu)
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
