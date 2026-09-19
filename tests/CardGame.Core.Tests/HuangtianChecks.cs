using CardGame.Content.Standard;
using CardGame.Core;

internal static class HuangtianChecks
{
    public static void QunProviderGivesOncePerPhase()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 57, 0));
        for (var seed = 1; seed <= 8192; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, HumanSeat = 0, HumanRole = Role.Rebel, PlayerCount = 5,
                ModeId = "identity:classic-5", UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 80
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral } setup ||
                !setup.ValidContentIds.Contains("classic:gongsun-zan") ||
                !game.Submit(new SelectGeneralCommand(0, "classic:gongsun-zan", game.Revision, setup.PromptId)).Accepted)
                continue;

            for (var step = 0; step < 100 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } play)
                continue;
            var snapshot = game.CreateSnapshot(0, revealAll: true);
            var lord = snapshot.Players.SingleOrDefault(player =>
                player.Role == Role.Lord && player.GeneralId == "classic:zhang-jiao");
            var action = game.GetHumanLegalActions().SingleOrDefault(candidate =>
                candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Huangtian);
            if (lord is null || action is null) continue;

            Require(action.SelectableTargetSeats.SequenceEqual([lord.Seat]) &&
                    action.SelectableCardIds.Count > 0 && action.MinCardCount == 1 && action.MaxCardCount == 1,
                "Huangtian must publish one exact Zhang Jiao Lord target and private eligible cards.");
            var cardId = action.SelectableCardIds[0];
            var before = snapshot.Players[0].Hand.Single(card => card.Id == cardId);
            var accepted = game.Submit(new UseSkillCommand(0, SkillKind.Huangtian,
                [cardId], [lord.Seat], game.Revision, play.PromptId));
            Require(accepted.Accepted, accepted.Error?.Message ?? "Huangtian gift was rejected.");
            var after = game.CreateSnapshot(0, revealAll: true);
            Require(after.Players[0].Hand.All(card => card.Id != cardId) &&
                    after.Players[lord.Seat].Hand.Any(card => card.Id == cardId) &&
                    before.Kind is CardKind.Dodge or CardKind.Lightning &&
                    game.CardMovements.Count(move => move.CardId == cardId &&
                        move.Reason == CardMoveReasons.HuangtianGive) == 2 &&
                    game.Events.Any(item => item.Payload is HuangtianCardGivenEvent
                        { ProviderSeat: 0, CardId: var movedId } && movedId == cardId),
                "Huangtian must move the exact Dodge or Lightning from provider hand to Lord hand.");
            Require(game.GetHumanLegalActions().All(candidate => candidate.Skill != SkillKind.Huangtian),
                "A provider may respond to Huangtian only once per play phase.");

            var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
            Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                    SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                    restored.GetHumanLegalActions().All(candidate => candidate.Skill != SkillKind.Huangtian),
                "Completed Huangtian movement and phase limit must replay exactly.");
            return;
        }

        throw new InvalidOperationException("No bounded fixture exposed a Qun provider with Zhang Jiao as Lord.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
