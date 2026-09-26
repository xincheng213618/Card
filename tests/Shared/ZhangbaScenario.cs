using CardGame.Content.Standard;
using CardGame.Core;

internal sealed record ZhangbaBoundary(
    GameEngine Game,
    GameCheckpoint BeforeUse,
    LegalAction Action,
    IReadOnlyList<int> CostCardIds,
    int TargetSeat,
    int WeaponCardId);

internal sealed record ZhangbaResponseBoundary(
    GameEngine Game,
    int ResponderSeat,
    CardKind IncomingCard,
    int WeaponCardId);

internal static class ZhangbaScenario
{
    public static ZhangbaBoundary FindHumanActiveUse()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                PlayerCount = 5,
                ModeId = "identity:classic-5",
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 220,
                AiPolicyVersion = 2
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted,
                "Zhangba fixture failed to start.");
            var generalChoice = game.PendingDecision?.Choices.FirstOrDefault(choice =>
                choice.ContentIds.Count == 1 &&
                registry.Generals[choice.ContentIds[0]].SkillIds
                    .Select(registry.GetSkill)
                    .All(skill => skill.LegacyKind is not (SkillKind.Tieqi or SkillKind.Liegong)));
            if (generalChoice is null)
            {
                continue;
            }

            Require(game.Submit(new SelectGeneralCommand(
                0,
                generalChoice.ContentIds[0],
                game.Revision,
                game.PendingDecision!.PromptId)).Accepted,
                "Zhangba fixture could not select a general.");
            Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                "Zhangba fixture did not reach play.");
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } play)
            {
                continue;
            }

            var source = game.CreateSnapshot(0, revealAll: true).Players[0];
            var weapon = source.Hand.FirstOrDefault(card => card.Kind == CardKind.ZhangbaSerpentSpear);
            if (weapon is null || source.Hand.Count < 3)
            {
                continue;
            }

            var equipped = game.Submit(new PlayCardCommand(
                0,
                weapon.Id,
                [],
                game.Revision,
                play.PromptId));
            Require(equipped.Accepted, equipped.Error?.Message ??
                "Zhangba fixture could not equip the weapon.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                    "Zhangba fixture did not return to play after equipping.");
            }
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard })
            {
                continue;
            }

            var action = game.GetHumanLegalActions().SingleOrDefault(candidate =>
                candidate.Kind == LegalActionKind.UseEquipmentEffect &&
                candidate.EquipmentKind == CardKind.ZhangbaSerpentSpear);
            if (action is null || action.SelectableCardIds.Count < 2 ||
                action.SelectableTargetSeats.Count == 0)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            var targetSeat = action.SelectableTargetSeats
                .Where(seat =>
                {
                    var target = full.Players.Single(player => player.Seat == seat);
                    return target.Hp > 1 &&
                           target.Skills?.All(skill =>
                               skill.Kind is not (SkillKind.Jianxiong or SkillKind.Ganglie)) != false;
                })
                .Order()
                .FirstOrDefault(-1);
            if (targetSeat < 0)
            {
                continue;
            }

            return new ZhangbaBoundary(
                game,
                game.CreateCheckpoint(),
                action,
                action.SelectableCardIds.Take(2).ToArray(),
                targetSeat,
                weapon.Id);
        }

        throw new InvalidOperationException(
            "No bounded classic Zhangba active-use boundary was found.");
    }

    public static ZhangbaResponseBoundary FindHumanResponse()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                PlayerCount = 5,
                ModeId = "identity:classic-5",
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 220,
                AiPolicyVersion = 2
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted,
                "Zhangba response fixture failed to start.");
            var generalChoice = game.PendingDecision?.Choices.FirstOrDefault(choice =>
                choice.ContentIds.Count == 1 &&
                registry.Generals[choice.ContentIds[0]].SkillIds
                    .Select(registry.GetSkill)
                    .All(skill => skill.LegacyKind != SkillKind.Hujia && skill.Id != "classic:jijiang"));
            if (generalChoice is null)
            {
                continue;
            }

            Require(game.Submit(new SelectGeneralCommand(
                0,
                generalChoice.ContentIds[0],
                game.Revision,
                game.PendingDecision!.PromptId)).Accepted,
                "Zhangba response fixture could not select a general.");
            Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                "Zhangba response fixture did not reach play.");
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } play)
            {
                continue;
            }

            var source = game.CreateSnapshot(0, revealAll: true).Players[0];
            var weapon = source.Hand.FirstOrDefault(card => card.Kind == CardKind.ZhangbaSerpentSpear);
            if (weapon is null || source.Hand.Count < 3)
            {
                continue;
            }

            var equipped = game.Submit(new PlayCardCommand(
                0,
                weapon.Id,
                [],
                game.Revision,
                play.PromptId));
            if (!equipped.Accepted)
            {
                continue;
            }
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                    "Zhangba response fixture did not return to play after equipping.");
            }
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } afterEquip)
            {
                continue;
            }
            Require(game.Submit(new EndPlayPhaseCommand(
                0,
                game.Revision,
                afterEquip.PromptId)).Accepted,
                "Zhangba response fixture could not end its setup play phase.");

            for (var step = 0; step < 256 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 } response &&
                    response.IncomingCard != CardKind.BorrowedSword &&
                    response.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "zhangba-slash" &&
                        choice.Cards.Count == 2))
                {
                    return new ZhangbaResponseBoundary(
                        game,
                        0,
                        response.IncomingCard ?? CardKind.Duel,
                        weapon.Id);
                }

                if (game.PendingDecision is { PlayerSeat: 0 } humanDecision)
                {
                    if (humanDecision.Kind == DecisionKind.PlayCard)
                    {
                        Require(game.Submit(new EndPlayPhaseCommand(
                            0,
                            game.Revision,
                            humanDecision.PromptId)).Accepted,
                            "Zhangba response fixture could not skip a later play phase.");
                        continue;
                    }

                    break;
                }

                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                if (!advanced.Accepted)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException(
            "No bounded classic Zhangba Slash-response boundary was found.");
    }

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
