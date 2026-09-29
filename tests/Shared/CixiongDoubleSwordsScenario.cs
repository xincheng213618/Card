using CardGame.Content.Standard;
using CardGame.Core;

internal sealed record CixiongDoubleSwordsBoundary(
    GameEngine Game,
    GameCheckpoint BeforeSlash,
    LegalAction SlashAction,
    int TargetSeat,
    int WeaponCardId);

internal static class CixiongDoubleSwordsScenario
{
    public static CixiongDoubleSwordsBoundary FindHumanTrigger(Role? targetRole = null)
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
                "Cixiong Double Swords fixture failed to start.");
            var generalChoice = game.PendingDecision?.Choices.FirstOrDefault(choice =>
                choice.ContentIds.Count == 1 &&
                registry.Generals[choice.ContentIds[0]].Gender == GeneralGender.Male &&
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
                "Cixiong Double Swords fixture could not select a male general.");
            Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                "Cixiong Double Swords fixture did not reach play.");
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } play)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            var source = full.Players[0];
            var weapon = source.Hand.FirstOrDefault(card => card.Kind == CardKind.CixiongDoubleSwords);
            if (weapon is null ||
                source.Hand.All(card => card.Kind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)))
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
                // Slashes, so an arbitrary Cixiong card may not be equippable; abandon
                // this seed the same way the Qilin Bow fixture does.
                continue;
            }
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                Require(game.Submit(new AdvanceCommand(game.Revision)).Accepted,
                    "Cixiong Double Swords fixture did not return to play after equipping.");
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
                    registry.Generals[item.Target.GeneralId].Gender == GeneralGender.Female &&
                    item.Target.Hp > 1 &&
                    (targetRole is null || item.Target.Role == targetRole))
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
            Require(played.Accepted, played.Error?.Message ??
                "Cixiong Double Swords fixture could not use Slash.");
            if (game.PendingDecision is { Kind: DecisionKind.CixiongDoubleSwords, PlayerSeat: 0 })
            {
                return new CixiongDoubleSwordsBoundary(
                    game,
                    beforeSlash,
                    slash.Action,
                    slash.Target.Seat,
                    weapon.Id);
            }
        }

        throw new InvalidOperationException(
            "No bounded classic Cixiong Double Swords trigger with a human source was found.");
    }

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
