using CardGame.Content.Standard;
using CardGame.Core;
using System.Reflection;

internal static class WolongChecks
{
    public static void ConversionsBazhenAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = Create(seed, registry);
            if (!SelectWolong(game)) continue;
            var play = Reach(game, DecisionKind.PlayCard, 64);
            if (play is null) continue;
            var huoji = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.FireAttack && action.PlayedCardKind == CardKind.FireAttack &&
                action.ConversionSource is { SkillId: "classic:huoji" } &&
                action.CardId is { } id && game.CreateSnapshot(0).Players[0].Hand.Any(card =>
                    card.Id == id && card.Kind != CardKind.FireAttack && card.Suit is Suit.Heart or Suit.Diamond));
            if (huoji is null) continue;
            var physicalId = huoji.CardId!.Value;
            var owner = ((IReadOnlyList<CharacterState>)typeof(GameEngine)
                .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(game)!)[0];
            Require(owner.SkillGrants.Grants.Any(grant =>
                    grant.SkillId == "classic:huoji" &&
                    grant.SkillInstanceId == huoji.ConversionSource!.SkillInstanceId),
                "Huoji must publish a real active skill grant, not an inferred source identity.");
            var missingSource = GameReplay.Restore(game.CreateCheckpoint(), registry);
            var omitted = missingSource.Submit(new PlayCardCommand(0, physicalId, huoji.TargetSeats,
                missingSource.Revision, missingSource.PendingDecision!.PromptId, CardKind.FireAttack));
            Require(!omitted.Accepted && missingSource.PendingDecision is { Kind: DecisionKind.PlayCard } &&
                    missingSource.CreateSnapshot(0).Players[0].Hand.Any(card => card.Id == physicalId),
                "A converted FireAttack without its published source must be rejected before payment.");
            var used = game.Submit(new PlayCardCommand(0, physicalId, huoji.TargetSeats, game.Revision,
                play.PromptId, CardKind.FireAttack) { ConversionSource = huoji.ConversionSource });
            Require(used.Accepted, used.Error?.Message ?? "Huoji conversion was rejected.");
            PendingDecision? kanpo = null;
            for (var step = 0; step < 32; step++)
            {
                var pending = game.PendingDecision;
                if (pending is { Kind: DecisionKind.Nullification, PlayerSeat: 0 }) { kanpo = pending; break; }
                if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
            }
            if (kanpo is null) continue;
            Require(game.Events.Any(item => item.Payload is CardUseDeclaredEvent declared &&
                    declared.CardId == physicalId && declared.CardKind == CardKind.FireAttack) &&
                    game.Events.Any(item => item.Payload is CardMovedEvent moved &&
                        moved.CardId == physicalId && moved.To == CardLocation.Processing),
                "Huoji must pay its red physical card as FireAttack under the published source.");
            var hand = game.CreateSnapshot(0).Players[0].Hand.ToDictionary(card => card.Id);
            var converted = kanpo.ValidCardIds.Where(id => hand[id].Kind != CardKind.Nullification).ToArray();
            if (converted.Length == 0) continue;
            Require(converted.All(id => hand[id].Suit is Suit.Spade or Suit.Club) &&
                    kanpo.Choices.Any(choice => choice.Cards.SequenceEqual([converted[0]]) &&
                        choice.Parameters.GetValueOrDefault("conversion-skill-id") == "classic:kanpo" &&
                        !string.IsNullOrWhiteSpace(choice.Parameters.GetValueOrDefault("conversion-instance-id"))),
                "Kanpo must publish exact black hand cards as private Nullification choices.");
            var paused = GameReplay.Restore(game.CreateCheckpoint(), registry);
            Require(paused.PendingDecision is { Kind: DecisionKind.Nullification, PlayerSeat: 0 } &&
                    paused.PendingDecision.ValidCardIds.SequenceEqual(kanpo.ValidCardIds),
                "A paused Kanpo choice must replay exactly.");
            var choice = kanpo.Choices.Single(item => item.Cards.SequenceEqual([converted[0]]) &&
                item.Parameters.GetValueOrDefault("conversion-skill-id") == "classic:kanpo");
            Require(owner.SkillGrants.Grants.Any(grant =>
                    grant.SkillId == "classic:kanpo" &&
                    grant.SkillInstanceId == choice.Parameters["conversion-instance-id"]),
                "Kanpo must publish the current active grant identity in its private response choice.");
            var answered = game.Submit(new AnswerPromptCommand(0, kanpo.PromptId, choice.Id, game.Revision));
            Require(answered.Accepted && game.Events.Any(item => item.Payload is CardRespondedEvent response &&
                    response.CardId == converted[0] && response.EffectiveCardKind == CardKind.Nullification),
                answered.Error?.Message ?? "Kanpo must respond with the black physical hand card as Nullification.");
            var replayed = paused.Submit(new AnswerPromptCommand(0, paused.PendingDecision!.PromptId,
                choice.Id, paused.Revision));
            Require(replayed.Accepted && paused.Events.Any(item => item.Payload is CardRespondedEvent response &&
                    response.CardId == converted[0] && response.EffectiveCardKind == CardKind.Nullification),
                replayed.Error?.Message ?? "A paused Kanpo source choice must replay its payment.");
            Require(FindBazhenBoundary(registry), "No bounded Wolong fixture exposed virtual Bagua without armor.");
            return;
        }
        throw new InvalidOperationException("No bounded Wolong fixture exposed Huoji and Kanpo conversions.");
    }

    private static bool FindBazhenBoundary(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = Create(seed, registry);
            if (!SelectWolong(game)) continue;
            for (var step = 0; step < 500 && game.State.Status != EngineStatus.Completed; step++)
            {
                var pending = game.PendingDecision;
                if (pending is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 } &&
                    pending.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") == "bagua") &&
                    game.CreateSnapshot(0).Players[0].Equipment.All(card =>
                        card.Kind is not (CardKind.BaguaFormation or CardKind.RenwangShield or CardKind.Tengjia or CardKind.SilverLion)))
                    return true;
                GameCommand command = pending is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }
                    ? new EndPlayPhaseCommand(0, game.Revision, pending.PromptId)
                    : pending is { PlayerSeat: 0 }
                        ? new AnswerPromptCommand(0, pending.PromptId, pending.Choices.Last().Id, game.Revision)
                        : new AdvanceOneStepCommand(game.Revision);
                if (!game.Submit(command).Accepted) break;
            }
        }
        return false;
    }

    private static GameEngine Create(int seed, ContentRegistry registry) => GameEngine.CreateStandard(new GameOptions
    {
        Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
        ModeId = "identity:classic-5", UseInteractiveSetup = true,
        UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 100
    }, registry);

    private static bool SelectWolong(GameEngine game)
    {
        if (!game.Submit(new StartGameCommand()).Accepted) return false;
        var selection = game.PendingDecision;
        return selection is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } &&
               selection.ValidContentIds.Contains("classic:wolong-zhuge-liang") &&
               game.Submit(new SelectGeneralCommand(0, "classic:wolong-zhuge-liang", game.Revision, selection.PromptId)).Accepted;
    }

    private static PendingDecision? Reach(GameEngine game, DecisionKind kind, int limit)
    {
        for (var step = 0; step < limit; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 } pending && pending.Kind == kind) return pending;
            if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) return null;
        }
        return null;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
