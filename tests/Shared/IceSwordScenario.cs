using CardGame.Content.Standard;
using CardGame.Core;

internal sealed record IceSwordBoundary(
    GameEngine Game,
    GameCheckpoint BeforeSlash,
    LegalAction SlashAction,
    int TargetSeat,
    int WeaponCardId);

internal static class IceSwordScenario
{
    public static IceSwordBoundary FindHumanTrigger()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        for (var seed = 1; seed <= 32_768; seed++)
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
                "Ice Sword fixture failed to start.");
            var generalChoice = game.PendingDecision?.Choices.FirstOrDefault(choice =>
                choice.ContentIds.Count == 1 &&
                registry.Generals[choice.ContentIds[0]].SkillIds
                    .Select(registry.GetSkill)
                    .All(skill => skill.Id is not ("classic:tieqi" or "classic:liegong")));
            if (generalChoice is null)
            {
                continue;
            }

            Require(game.Submit(new SelectGeneralCommand(
                0,
                generalChoice.ContentIds[0],
                game.Revision,
                game.PendingDecision!.PromptId)).Accepted,
                "Ice Sword fixture could not select a general.");
            Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                "Ice Sword fixture did not reach play.");
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } play)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            var source = full.Players[0];
            var weapon = source.Hand.FirstOrDefault(card => card.Kind == CardKind.IceSword);
            if (weapon is null ||
                source.Hand.All(card =>
                    card.Kind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)))
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
                // Pool drift: conversion-locked generals can declare their hand cards as
                // Slashes, so an arbitrary Ice Sword card may not be equippable; abandon
                // this seed the same way the Qilin Bow fixture does.
                continue;
            }
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                    "Ice Sword fixture did not return to play after equipping.");
            }
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } afterEquip)
            {
                continue;
            }

            full = game.CreateSnapshot(0, revealAll: true);
            source = full.Players[0];
            var slash = game.GetHumanLegalActions()
                .Where(action =>
                    action.Kind == LegalActionKind.Slash &&
                    action.CardId is not null &&
                    action.TargetSeat is not null &&
                    source.Hand.Any(card =>
                        card.Id == action.CardId &&
                        card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))
                .Select(action => new
                {
                    Action = action,
                    Target = full.Players.Single(player => player.Seat == action.TargetSeat)
                })
                .Where(item =>
                    item.Target.Hp > 1 &&
                    item.Target.Hand.Count + item.Target.Equipment.Count >= 2 &&
                    item.Target.Hand.All(card => card.Kind != CardKind.Dodge) &&
                    item.Target.Equipment.All(card =>
                        card.Kind is not (CardKind.BaguaFormation or CardKind.RenwangShield)) &&
                    item.Target.Skills?.All(skill =>
                        skill.ContentId is not ("classic:qingguo" or "classic:longdan" or "classic:hujia")) != false)
                .OrderBy(item => item.Action.CardId)
                .ThenBy(item => item.Action.TargetSeat)
                .FirstOrDefault();
            if (slash is null)
            {
                continue;
            }

            var beforeSlash = game.CreateCheckpoint();
            var played = game.Submit(new PlayCardCommand(
                0,
                slash.Action.CardId!.Value,
                slash.Action.TargetSeats,
                game.Revision,
                afterEquip.PromptId,
                slash.Action.PlayedCardKind));
            Require(played.Accepted,
                played.Error?.Message ?? "Ice Sword fixture could not use Slash.");
            for (var step = 0; step < 16 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.IceSword, PlayerSeat: 0 })
                {
                    return new IceSwordBoundary(
                        game,
                        beforeSlash,
                        slash.Action,
                        slash.Target.Seat,
                        weapon.Id);
                }

                if (game.PendingDecision is { PlayerSeat: 0 })
                {
                    break;
                }

                Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                    "Ice Sword fixture could not advance to its damage replacement window.");
            }
        }

        throw new InvalidOperationException(
            "No bounded classic Ice Sword trigger with a human source was found.");
    }

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
