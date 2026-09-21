using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhuRongChecks
{
    public static void JuxiangAndLierenReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 60, 0));
        var juxiangVerified = false;
        var lierenVerified = false;
        for (var seed = 1; seed <= 4096 && (!juxiangVerified || !lierenVerified); seed++)
        {
            var game = Create(seed, registry);
            if (!SelectZhuRong(game)) continue;
            for (var step = 0; step < 900 && game.State.Winner == Winner.None; step++)
            {
                var claim = game.Events.Select(e => e.Payload).OfType<JuxiangCardClaimedEvent>().LastOrDefault();
                if (claim is not null)
                {
                    var used = game.Events.Select(e => e.Payload).OfType<GroupCardUsedEvent>()
                        .Last(e => e.ResolutionId == claim.ResolutionId);
                    Require(claim.OwnerSeat == 0 && !used.TargetSeats.Contains(0) &&
                            claim.CardIds.All(id => game.CardMovements.Any(move =>
                                move.CardId == id && move.Reason == CardMoveReasons.JuxiangGain)),
                        "Juxiang must make Barbarian Assault ineffective and claim its discarded physical cards.");
                    juxiangVerified = true;
                }
                if (game.PendingDecision is { Kind: DecisionKind.Lieren, PlayerSeat: 0 } offer &&
                    offer.Choices.Any(choice => choice.Parameters.GetValueOrDefault("action") == "lieren-use"))
                {
                    var checkpoint = game.CreateCheckpoint();
                    ResolveLieren(game);
                    var resolved = game.Events.Select(e => e.Payload).OfType<LierenResolvedEvent>().Last();
                    Require(resolved is { OwnerSeat: 0, Used: true } &&
                            game.Events.Select(e => e.Payload).OfType<PindianResolvedEvent>()
                                .Any(e => e.Skill == SkillKind.Lieren && e.ResolutionId == resolved.ResolutionId) &&
                            (resolved.GainedCardId is null || game.CardMovements.Any(move =>
                                move.CardId == resolved.GainedCardId && move.Reason == CardMoveReasons.LierenGain)),
                        "Lieren must resolve exact Pindian cards and gain one target card only after winning.");
                    var restored = GameReplay.Restore(checkpoint, registry);
                    ResolveLieren(restored);
                    Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                            SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
                        "A paused Lieren trigger must replay exactly.");
                    lierenVerified = true;
                    break;
                }
                if (!AdvanceAggressively(game)) break;
            }
        }
        Require(juxiangVerified && lierenVerified, "No bounded Zhu Rong fixtures verified Juxiang and Lieren.");
    }

    private static GameEngine Create(int seed, ContentRegistry registry) => GameEngine.CreateStandard(new GameOptions
    {
        Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
        ModeId = "identity:classic-5", UseInteractiveSetup = true, UseInteractiveDiscard = true,
        AdvanceAfterHumanCommands = false, MaxTurns = 80
    }, registry);

    private static bool SelectZhuRong(GameEngine game) =>
        game.Submit(new StartGameCommand()).Accepted &&
        game.PendingDecision is { Kind: DecisionKind.SelectGeneral } setup &&
        setup.ValidContentIds.Contains("classic:zhu-rong") &&
        game.Submit(new SelectGeneralCommand(0, "classic:zhu-rong", game.Revision, setup.PromptId)).Accepted;

    private static void ResolveLieren(GameEngine game)
    {
        for (var step = 0; step < 12; step++)
        {
            if (game.Events.Select(e => e.Payload).OfType<LierenResolvedEvent>().Any()) return;
            if (game.PendingDecision is { Kind: DecisionKind.Lieren, PlayerSeat: 0 } decision)
            {
                var choice = decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("action") == "lieren-use") ??
                             decision.Choices.First();
                Require(game.Submit(new AnswerPromptCommand(0, decision.PromptId, choice.Id, game.Revision)).Accepted,
                    "Human Lieren choice was rejected.");
            }
            else Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "AI Lieren continuation could not advance.");
        }
        throw new InvalidOperationException("Lieren did not finish within the bounded step count.");
    }

    private static bool AdvanceAggressively(GameEngine game)
    {
        if (game.PendingDecision is not { } pending)
            return game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted;
        if (pending.PlayerSeat != 0)
            return game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted;
        if (pending.Kind == DecisionKind.PlayCard)
        {
            var slash = game.GetHumanLegalActions().FirstOrDefault(action => action.Kind == LegalActionKind.Slash);
            if (slash is not null)
                return game.Submit(new PlayCardCommand(0, slash.CardId!.Value, [slash.TargetSeat!.Value],
                    game.Revision, pending.PromptId, slash.PlayedCardKind)).Accepted;
            return game.Submit(new EndPlayPhaseCommand(0, game.Revision, pending.PromptId)).Accepted;
        }
        if (pending.Kind == DecisionKind.DiscardCards)
            return game.Submit(new DiscardCardsCommand(0, pending.ValidCardIds.Take(pending.RequiredCardCount).ToArray(),
                pending.PromptId, game.Revision)).Accepted;
        var choice = pending.Choices.FirstOrDefault(c =>
            c.Parameters.GetValueOrDefault("action")?.Contains("skip", StringComparison.Ordinal) == true) ??
            pending.Choices.LastOrDefault();
        return choice is not null && game.Submit(new AnswerPromptCommand(0, pending.PromptId, choice.Id, game.Revision)).Accepted;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
