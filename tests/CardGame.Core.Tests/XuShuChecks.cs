using System.Collections;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class XuShuChecks
{
    public static void JujianBenefitsAndReplay()
    {
        Require(GameCheckpoint.CurrentRulesVersion >= 78,
            "Jujian requires the rules v78 compatibility boundary.");
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 63, 0));
        var (_, ownerCheckpoint) = FindOwnerPrompt(registry);
        var owner = GameReplay.Restore(ownerCheckpoint, registry);
        var prompt = owner.PendingDecision ??
            throw new InvalidOperationException("Xu Shu did not retain the Jujian owner prompt.");
        Require(prompt is { Kind: DecisionKind.Jujian, PlayerSeat: 0, IsPrivate: true } &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("action") == "jujian-skip") &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("action") == "jujian-use"),
            "Xu Shu must receive a private optional Jujian owner prompt at the end phase.");
        var ownerView = owner.CreateSnapshot(0, revealAll: true).Players[0];
        var ownerCards = ownerView.Hand.Concat(ownerView.Equipment).ToDictionary(card => card.Id);
        Require(prompt.ValidCardIds.All(id =>
                    ownerCards.TryGetValue(id, out var card) &&
                    CardCatalog.Get(card.Kind).CategoryName != "基本牌") &&
                prompt.ValidCardIds.Count > 0,
            "Jujian must publish only non-basic cards from Xu Shu's hand or equipment.");

        var targetSeat = prompt.ValidTargetSeats[0];
        var draw = RunBranch(ownerCheckpoint, registry, targetSeat, "jujian-draw", _ => { });
        Require(draw.Event is { Benefit: JujianBenefitKind.DrawTwo, DrawnCards: 2, RecoveredHp: 0 } &&
                draw.AfterTarget.HandCount == draw.BeforeTarget.HandCount + 2 &&
                draw.Game.CardMovements.Count(move =>
                    move.Reason == CardMoveReasons.JujianDraw && move.To == CardLocation.Hand(targetSeat)) == 2,
            "The Jujian draw branch must move exactly two physical cards to the target's hand.");
        var replayedDraw = GameReplay.Restore(draw.Game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(replayedDraw.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(draw.Game.CreateSnapshot(0, revealAll: true)) &&
                replayedDraw.CardMovements.SequenceEqual(draw.Game.CardMovements),
            "A completed Jujian draw branch must restore to the same public and physical state.");

        var recovery = RunBranch(ownerCheckpoint, registry, targetSeat, "jujian-recover", player =>
            player.GetType().GetProperty("Hp")!.SetValue(player,
                (int)player.GetType().GetProperty("MaxHp")!.GetValue(player)! - 1));
        Require(recovery.Event is { Benefit: JujianBenefitKind.RecoverOne, DrawnCards: 0, RecoveredHp: 1 } &&
                recovery.AfterTarget.Hp == recovery.BeforeTarget.Hp + 1 &&
                recovery.Game.Events.Any(envelope => envelope.Payload is RecoveryAppliedEvent
                {
                    SourceSeat: 0,
                    TargetSeat: var seat,
                    Amount: 1
                } && seat == targetSeat),
            "The Jujian recovery branch must restore exactly one HP through the recovery event path.");

        var restored = RunBranch(ownerCheckpoint, registry, targetSeat, "jujian-restore", player =>
        {
            player.GetType().GetProperty("IsFaceDown")!.SetValue(player, true);
            player.GetType().GetProperty("IsChained")!.SetValue(player, true);
        });
        Require(restored.Event is
        {
            Benefit: JujianBenefitKind.RestoreGeneral,
            TargetIsFaceDown: false,
            TargetIsChained: false
        } &&
                restored.BeforeTarget.IsFaceDown && restored.BeforeTarget.IsChained &&
                !restored.AfterTarget.IsFaceDown && !restored.AfterTarget.IsChained &&
                restored.Game.Events.Any(envelope => envelope.Payload is IronChainStateChangedEvent
                {
                    TargetSeat: var seat,
                    IsChained: false
                } && seat == targetSeat),
            "The Jujian restore branch must turn the general face up and remove chaining.");

        var repeatedRecovery = RunBranch(ownerCheckpoint, registry, targetSeat, "jujian-recover", player =>
            player.GetType().GetProperty("Hp")!.SetValue(player,
                (int)player.GetType().GetProperty("MaxHp")!.GetValue(player)! - 1));
        var repeatedRestore = RunBranch(ownerCheckpoint, registry, targetSeat, "jujian-restore", player =>
        {
            player.GetType().GetProperty("IsFaceDown")!.SetValue(player, true);
            player.GetType().GetProperty("IsChained")!.SetValue(player, true);
        });
        Require(SnapshotJson.Serialize(repeatedRecovery.Game.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(recovery.Game.CreateSnapshot(0, revealAll: true)) &&
                SnapshotJson.Serialize(repeatedRestore.Game.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(restored.Game.CreateSnapshot(0, revealAll: true)),
            "Jujian recovery and restoration must be deterministic from the same controlled boundary.");

        var skipped = GameReplay.Restore(ownerCheckpoint, registry);
        var skippedPrompt = skipped.PendingDecision!;
        var skip = skippedPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "jujian-skip");
        var movementsBeforeSkip = skipped.CardMovements.Count;
        Require(skipped.Submit(new AnswerPromptCommand(
                    0, skippedPrompt.PromptId, skip.Id, skipped.Revision)).Accepted &&
                skipped.CardMovements.Count == movementsBeforeSkip &&
                skipped.Events.Select(envelope => envelope.Payload).OfType<JujianResolvedEvent>().Last() is
                { Used: false, DiscardedCardId: null, TargetSeat: null },
            "Skipping Jujian must end the optional window without moving a card.");
    }

    private static (GameCheckpoint PreEnd, GameCheckpoint OwnerPrompt) FindOwnerPrompt(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                PlayerCount = 5,
                ModeId = "identity:classic-5",
                UseInteractiveSetup = true,
                UseInteractiveDiscard = true,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 80
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } setup ||
                !setup.ValidContentIds.Contains("classic:xu-shu") ||
                !game.Submit(new SelectGeneralCommand(
                    0, "classic:xu-shu", game.Revision, setup.PromptId)).Accepted)
            {
                continue;
            }

            for (var step = 0; step < 64 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            {
                if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
            }
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } play)
            {
                continue;
            }
            var self = game.CreateSnapshot(0, revealAll: true).Players[0];
            if (!self.Hand.Concat(self.Equipment).Any(card =>
                    CardCatalog.Get(card.Kind).CategoryName != "基本牌"))
            {
                continue;
            }

            var preEnd = game.CreateCheckpoint();
            if (!game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId)).Accepted)
            {
                continue;
            }
            for (var step = 0; step < 16 && game.PendingDecision?.Kind != DecisionKind.Jujian; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 } discard)
                {
                    if (!game.Submit(new DiscardCardsCommand(
                        0,
                        discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(),
                        discard.PromptId,
                        game.Revision)).Accepted)
                    {
                        break;
                    }
                }
                else if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted)
                {
                    break;
                }
            }
            if (game.PendingDecision is { Kind: DecisionKind.Jujian, PlayerSeat: 0 })
            {
                return (preEnd, game.CreateCheckpoint());
            }
        }

        throw new InvalidOperationException("No bounded Xu Shu fixture reached Jujian with a legal non-basic cost.");
    }

    private static BranchResult RunBranch(
        GameCheckpoint ownerCheckpoint,
        ContentRegistry registry,
        int targetSeat,
        string action,
        Action<object> prepareTarget)
    {
        var game = GameReplay.Restore(ownerCheckpoint, registry);
        var targetRuntime = GetPlayerRuntime(game, targetSeat);
        prepareTarget(targetRuntime);
        var beforeTarget = game.CreateSnapshot(0, revealAll: true).Players[targetSeat];
        var ownerPrompt = game.PendingDecision!;
        var use = ownerPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "jujian-use" &&
            choice.Targets.SequenceEqual([targetSeat]));
        Require(game.Submit(new AnswerPromptCommand(
                    0, ownerPrompt.PromptId, use.Id, game.Revision)).Accepted,
            "Jujian owner use choice was rejected.");
        Require(game.CardMovements.Any(move =>
                    move.CardId == use.Cards[0] &&
                    move.To == CardLocation.DiscardPile &&
                    move.Reason == CardMoveReasons.JujianDiscard),
            "Jujian must discard the exact published non-basic cost.");
        var targetPrompt = game.CreateSnapshot(targetSeat, revealAll: false).PendingDecision ??
            throw new InvalidOperationException(
                $"Jujian did not publish a target benefit prompt to its responder (status={game.State.Status}, " +
                $"current={game.State.CurrentSeat}, lastEvent={game.Events.LastOrDefault()?.Payload.GetType().Name ?? "none"}).");
        Require(targetPrompt is { Kind: DecisionKind.Jujian, PlayerSeat: var responder } &&
                responder == targetSeat && targetPrompt.IsPrivate,
            "Jujian must transfer a private benefit choice to the selected target.");
        var benefit = targetPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == action);
        Require(game.Submit(new AnswerPromptCommand(
                    targetSeat, targetPrompt.PromptId, benefit.Id, game.Revision)).Accepted,
            $"Jujian target choice {action} was rejected.");
        var afterTarget = game.CreateSnapshot(0, revealAll: true).Players[targetSeat];
        var resolved = game.Events.Select(envelope => envelope.Payload)
            .OfType<JujianResolvedEvent>()
            .Last(item => item.Used);
        Require(resolved.OwnerSeat == 0 && resolved.TargetSeat == targetSeat &&
                resolved.DiscardedCardId == use.Cards[0] &&
                resolved.DiscardedCardKind is not null,
            "Jujian must publish its owner, target, exact discarded card and selected benefit.");
        return new BranchResult(game, beforeTarget, afterTarget, resolved);
    }

    private static object GetPlayerRuntime(GameEngine game, int seat)
    {
        var players = ((IEnumerable)typeof(GameEngine)
            .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game)!).Cast<object>().ToArray();
        return players[seat];
    }

    private sealed record BranchResult(
        GameEngine Game,
        PlayerSnapshot BeforeTarget,
        PlayerSnapshot AfterTarget,
        JujianResolvedEvent Event);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
