using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class SunJianChecks
{
    private const string SkillId = "classic:yinghun";

    public static void YinghunChoiceAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(registry.Skills[SkillId] is { Program.RuntimeVersion: SkillProgramCatalog.RuntimeVersion },
            "Current Yinghun must use its configured program.");
        for (var seed = 1; seed <= 4096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
                ModeId = "identity:classic-5", UseInteractiveSetup = true,
                UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 80
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral } setup ||
                !setup.ValidContentIds.Contains("classic:sun-jian") ||
                !game.Submit(new SelectGeneralCommand(0, "classic:sun-jian", game.Revision, setup.PromptId)).Accepted)
                continue;
            var offer = ReachWoundedYinghun(game, 900);
            if (offer is null) continue;
            var view = game.CreateSnapshot(0, revealAll: true);
            var lostHp = view.Players[0].MaxHp - view.Players[0].Hp;
            if (lostHp <= 1) continue;
            var target = view.Players.FirstOrDefault(player => player.IsAlive && player.Seat != 0 && player.HandCount >= lostHp);
            if (target is null) continue;
            var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
            foreach (var branch in new[] { "draw-many", "discard-many" })
            {
                var played = GameReplay.Restore(checkpoint, registry);
                AnswerBranch(played, branch, target.Seat);
                var frame = played.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == SkillId);
                Require(frame.OwnedCardSelection is { CardOwnerSeat: var seat } && seat == target.Seat &&
                        played.CreateSnapshot(0).PendingDecision is null &&
                        played.CreateSnapshot(target.Seat).PendingDecision is { IsPrivate: true },
                    "The selected target, not the skill owner, must privately choose the cards.");
                var selectionCheckpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(played.CreateCheckpoint()));
                var restored = GameReplay.Restore(selectionCheckpoint, registry);
                ReachAfterYinghun(played, branch, 128);
                ReachAfterYinghun(restored, branch, 128);
                var drawn = branch == "draw-many" ? lostHp : 1;
                var discarded = branch == "draw-many" ? 1 : lostHp;
                var after = played.CreateSnapshot(0, revealAll: true).Players[target.Seat];
                var moves = played.CardMovements.Where(move =>
                    move.Reason.Value == $"skill-program.{SkillId}.MoveBoundCards").ToArray();
                Require(moves.Length == discarded &&
                        moves.All(move => move.From.OwnerSeat == target.Seat &&
                            move.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
                            move.To == CardLocation.DiscardPile) &&
                        after.HandCount + after.Equipment.Count == target.HandCount + target.Equipment.Count + drawn - discarded,
                    "Both Yinghun branches must draw then consume the exact privately selected owned cards.");
                Require(State(played) == State(restored) &&
                        played.CardMovements.SequenceEqual(restored.CardMovements) &&
                        played.Events.Select(item => item.Payload.GetType()).SequenceEqual(restored.Events.Select(item => item.Payload.GetType())),
                    "The target selection pause and completion must replay with the same snapshot, ledger and event types.");
                var completeReplay = GameReplay.Restore(played.CreateCheckpoint(), registry);
                Require(State(completeReplay) == State(played), "A completed exchange must replay without duplicate effects.");
            }
            var skipped = GameReplay.Restore(checkpoint, registry);
            var prompt = skipped.PendingDecision!;
            var beforeMoves = skipped.CardMovements.Count;
            var skip = prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
            Answer(skipped, prompt, skip);
            Require(skipped.CardMovements.Skip(beforeMoves).All(move =>
                        !move.Reason.Value.StartsWith($"skill-program.{SkillId}.", StringComparison.Ordinal)),
                "Skipping the shared choice group must neither draw for a target nor select/discard its cards.");
            return;
        }
        throw new InvalidOperationException("No bounded wounded Sun Jian fixture exposed the configured exchange.");
    }

    private static void VerifyHistoricalPackageDoesNotReviveLegacyYinghun(ContentRegistry historical)
    {
        GameEngine? game = null;
        for (var seed = 1; seed <= 4096 && game is null; seed++)
        {
            var candidate = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
                ModeId = "identity:classic-5", UseInteractiveSetup = true,
                UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 24
            }, historical);
            if (!candidate.Submit(new StartGameCommand()).Accepted ||
                candidate.PendingDecision is not { Kind: DecisionKind.SelectGeneral } setup ||
                !setup.ValidContentIds.Contains("classic:sun-jian") ||
                !candidate.Submit(new SelectGeneralCommand(0, "classic:sun-jian", candidate.Revision, setup.PromptId)).Accepted)
                continue;
            game = candidate;
        }
        if (game is null)
            throw new InvalidOperationException("The historical package fixture could not select Sun Jian.");
        for (var step = 0; step < 256 && game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
            Require(AdvanceConservatively(game), "The historical package fixture could not reach Sun Jian's first Play phase.");
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "The historical package fixture did not reach Sun Jian's first Play phase.");

        // Test-only setup: isolate the old package boundary without depending on combat damage RNG.
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine)
            .GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        players[0].Hp = Math.Max(1, players[0].MaxHp - 2);
        var firstTurnStarts = game.Events.Count(item => item.Payload is TurnStartedEvent { ActorSeat: 0 });
        for (var step = 0; step < 1200; step++)
        {
            Require(game.PendingDecision is not { Kind: DecisionKind.ProgramTrigger } &&
                    game.PendingDecision?.SkillPrompt?.SkillId != SkillId,
                "The historical package revived the removed Yinghun execution route.");
            if (game.Events.Count(item => item.Payload is TurnStartedEvent { ActorSeat: 0 }) > firstTurnStarts &&
                game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
            {
                Require(!game.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == SkillId),
                    "Historical Yinghun metadata must not auto-bind the current program.");
                return;
            }
            Require(AdvanceConservatively(game), "The historical package fixture stopped before Sun Jian's next Play phase.");
        }
        throw new InvalidOperationException("The historical package fixture did not reach Sun Jian's next Play phase.");
    }

    private static void AnswerBranch(GameEngine game, string branch, int targetSeat)
    {
        var offer = game.PendingDecision!;
        Answer(game, offer, offer.Choices.Single(choice => choice.Parameters.GetValueOrDefault("binding-id") == branch &&
            choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        var targets = game.PendingDecision!;
        Require(targets.Choices.All(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-target"),
            "A branch must next use the shared target selector.");
        Answer(game, targets, targets.Choices.Single(choice => choice.Targets.SequenceEqual([targetSeat])));
    }

    private static PendingDecision? ReachWoundedYinghun(GameEngine game, int limit)
    {
        for (var step = 0; step < limit; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, SkillPrompt.SkillId: SkillId } prompt)
                return prompt;
            if (!AdvanceConservatively(game)) return null;
        }
        return null;
    }

    private static void ReachAfterYinghun(GameEngine game, string branch, int limit)
    {
        for (var step = 0; step < limit; step++)
        {
            if (game.Events.Any(item => item.Payload is ProgramBindingResolvedEvent
                { SkillId: SkillId, Completed: true, BindingId: var id } && id == branch)) return;
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The shared AI selection and parent continuation could not advance.");
        }
        throw new InvalidOperationException("The configured exchange did not complete in bounded steps.");
    }

    private static bool AdvanceConservatively(GameEngine game)
    {
        if (game.PendingDecision is not { } pending || pending.PlayerSeat != 0)
            return game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted;
        if (pending.Kind == DecisionKind.PlayCard)
            return game.Submit(new EndPlayPhaseCommand(0, game.Revision, pending.PromptId)).Accepted;
        if (pending.Kind == DecisionKind.DiscardCards)
            return game.Submit(new DiscardCardsCommand(0, pending.ValidCardIds.Take(pending.RequiredCardCount).ToArray(),
                pending.PromptId, game.Revision)).Accepted;
        var choice = pending.Choices.FirstOrDefault(candidate =>
                         candidate.Parameters.GetValueOrDefault("action")?.Contains("skip", StringComparison.Ordinal) == true ||
                         candidate.Parameters.GetValueOrDefault("program-action") == "skip" ||
                         candidate.Cards.Count == 0 && candidate.Targets.Count == 0) ?? pending.Choices.LastOrDefault();
        return choice is not null && game.Submit(new AnswerPromptCommand(0, pending.PromptId, choice.Id, game.Revision)).Accepted;
    }

    private static void Answer(GameEngine game, PendingDecision prompt, PromptChoice choice)
    {
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The configured choice was rejected.");
    }

    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
