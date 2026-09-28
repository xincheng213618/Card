using CardGame.Content.Standard;
using CardGame.Core;

internal sealed record QilinBowBoundary(
    GameEngine Game,
    GameCheckpoint BeforeSlash,
    LegalAction SlashAction,
    int SourceSeat,
    int TargetSeat,
    int WeaponCardId,
    IReadOnlyList<int> MountCardIds);

internal static class QilinBowScenario
{
    public static QilinBowBoundary FindHumanTrigger()
    {
        const int sourceSeat = 0;
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        for (var seed = 1; seed <= 32_768; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = sourceSeat,
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
                "Qilin Bow fixture failed to start.");
            var generalPrompt = game.PendingDecision;
            var generalChoice = generalPrompt?.Choices.FirstOrDefault(choice =>
                choice.ContentIds.Count == 1 &&
                registry.Generals[choice.ContentIds[0]].SkillIds
                    .Select(registry.GetSkill)
                    .All(skill => skill.Id is not ("classic:tieqi" or "classic:liegong")));
            if (generalPrompt is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: sourceSeat } ||
                generalChoice is null)
            {
                continue;
            }

            Require(game.Submit(new SelectGeneralCommand(
                sourceSeat,
                generalChoice.ContentIds[0],
                game.Revision,
                generalPrompt.PromptId)).Accepted,
                "Qilin Bow fixture could not select a general.");

            PendingDecision? play = null;
            for (var step = 0; step < 512 && game.State.Status != EngineStatus.Completed; step++)
            {
                var pending = game.PendingDecision;
                if (pending is { Kind: DecisionKind.PlayCard, PlayerSeat: sourceSeat })
                {
                    play = pending;
                    break;
                }

                GameCommand command;
                if (pending is null || pending.PlayerSeat != sourceSeat)
                {
                    command = new AdvanceOneStepCommand(game.Revision);
                }
                else if (pending.Kind == DecisionKind.DiscardCards)
                {
                    command = new DiscardCardsCommand(
                        sourceSeat,
                        pending.ValidCardIds.Take(pending.RequiredCardCount).ToArray(),
                        pending.PromptId,
                        game.Revision);
                }
                else
                {
                    command = new AnswerPromptCommand(
                        sourceSeat,
                        pending.PromptId,
                        DeclineChoice(pending).Id,
                        game.Revision);
                }

                var advanced = game.Submit(command);
                if (!advanced.Accepted)
                {
                    break;
                }
            }

            if (play is null)
            {
                continue;
            }

            var full = game.CreateSnapshot(sourceSeat, revealAll: true);
            var source = full.Players[sourceSeat];
            var weapon = source.Hand.FirstOrDefault(card => card.Kind == CardKind.QilinBow);
            if (weapon is null)
            {
                continue;
            }

            var equipped = game.Submit(new PlayCardCommand(
                sourceSeat,
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
                    "Qilin Bow fixture did not return to play after equipping.");
            }
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } afterEquip)
            {
                continue;
            }

            Require(game.Submit(new EndPlayPhaseCommand(
                sourceSeat,
                game.Revision,
                afterEquip.PromptId)).Accepted,
                "Qilin Bow fixture could not preserve the weapon for a later round.");

            for (var step = 0; step < 4_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var pending = game.PendingDecision;
                full = game.CreateSnapshot(sourceSeat, revealAll: true);
                source = full.Players[sourceSeat];
                if (!source.IsAlive || source.Equipment.All(card => card.Id != weapon.Id))
                {
                    break;
                }

                if (pending is { Kind: DecisionKind.PlayCard, PlayerSeat: sourceSeat })
                {
                    var slash = FindSlash(game, full, sourceSeat);
                    if (slash is null)
                    {
                        Require(game.Submit(new EndPlayPhaseCommand(
                            sourceSeat,
                            game.Revision,
                            pending.PromptId)).Accepted,
                            "Qilin Bow fixture could not end a play phase without a mounted target.");
                        continue;
                    }

                    var beforeSlash = game.CreateCheckpoint();
                    var played = game.Submit(new PlayCardCommand(
                        sourceSeat,
                        slash.Action.CardId!.Value,
                        slash.Action.TargetSeats,
                        game.Revision,
                        pending.PromptId,
                        slash.Action.PlayedCardKind));
                    if (!played.Accepted)
                    {
                        break;
                    }
                    for (var damageStep = 0;
                         damageStep < 16 && game.State.Status != EngineStatus.Completed;
                         damageStep++)
                    {
                        if (game.PendingDecision is { Kind: DecisionKind.QilinBow, PlayerSeat: sourceSeat })
                        {
                            if (!BoundaryAnswersAreLegal(registry, game))
                            {
                                break;
                            }

                            return new QilinBowBoundary(
                                game,
                                beforeSlash,
                                slash.Action,
                                sourceSeat,
                                slash.Target.Seat,
                                weapon.Id,
                                slash.Mounts.Select(card => card.Id).ToArray());
                        }

                        if (game.PendingDecision is { PlayerSeat: sourceSeat })
                        {
                            break;
                        }

                        var timing = game.Submit(new AdvanceOneStepCommand(game.Revision));
                        if (!timing.Accepted)
                        {
                            break;
                        }
                    }

                    break;
                }

                GameCommand command;
                if (pending is null || pending.PlayerSeat != sourceSeat)
                {
                    command = new AdvanceOneStepCommand(game.Revision);
                }
                else if (pending.Kind == DecisionKind.DiscardCards)
                {
                    command = new DiscardCardsCommand(
                        sourceSeat,
                        pending.ValidCardIds.Take(pending.RequiredCardCount).ToArray(),
                        pending.PromptId,
                        game.Revision);
                }
                else
                {
                    command = new AnswerPromptCommand(
                        sourceSeat,
                        pending.PromptId,
                        DeclineChoice(pending).Id,
                        game.Revision);
                }

                var advanced = game.Submit(command);
                if (!advanced.Accepted)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException(
            "No bounded classic Qilin Bow trigger with a human source and mounted target was found.");
    }

    private static bool BoundaryAnswersAreLegal(ContentRegistry registry, GameEngine game)
    {
        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var prompt = game.PendingDecision!;
        var discard = prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("action") == "qilin-bow-discard");
        var skip = prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("action") == "qilin-bow-skip");
        if (discard is null || skip is null)
        {
            return false;
        }

        foreach (var choice in new[] { discard, skip })
        {
            var restored = GameReplay.Restore(checkpoint, registry);
            var answered = restored.Submit(new AnswerPromptCommand(
                restored.PendingDecision!.PlayerSeat,
                restored.PendingDecision.PromptId,
                choice.Id,
                restored.Revision));
            if (!answered.Accepted)
            {
                return false;
            }
        }

        return true;
    }

    private static SlashCandidate? FindSlash(
        GameEngine game,
        GameSnapshot full,
        int sourceSeat)
    {
        var source = full.Players[sourceSeat];
        return game.GetHumanLegalActions()
            .Where(action =>
                action.Kind == LegalActionKind.Slash &&
                action.CardId is not null &&
                action.TargetSeat is not null &&
                source.Hand.Any(card =>
                    card.Id == action.CardId &&
                    card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))
            .Select(action => new SlashCandidate(
                action,
                full.Players.Single(player => player.Seat == action.TargetSeat),
                full.Players.Single(player => player.Seat == action.TargetSeat)
                    .Equipment.Where(card => EquipmentCatalog.Get(card.Kind).Slot is
                        EquipmentSlot.OffensiveHorse or EquipmentSlot.DefensiveHorse).ToArray()))
            .Where(item =>
                item.Mounts.Count > 0 &&
                item.Target.Hp > 1 &&
                item.Target.Hand.All(card => card.Kind != CardKind.Dodge) &&
                item.Target.Equipment.All(card =>
                    card.Kind is not (CardKind.BaguaFormation or CardKind.RenwangShield)) &&
                item.Target.Skills?.All(skill =>
                    skill.ContentId is not ("classic:qingguo" or "classic:longdan" or "classic:hujia")) != false)
            .OrderBy(item => item.Action.CardId)
            .ThenBy(item => item.Action.TargetSeat)
            .FirstOrDefault();
    }

    private static PromptChoice DeclineChoice(PendingDecision prompt) =>
        prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.Values.Any(value =>
                value.StartsWith("skip", StringComparison.Ordinal) ||
                value is "take-damage" or "no-nullification" or "ganglie-lose-hp"))
        ?? prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0)
        ?? prompt.Choices.First();

    private sealed record SlashCandidate(
        LegalAction Action,
        PlayerSnapshot Target,
        IReadOnlyList<CardSnapshot> Mounts);

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
