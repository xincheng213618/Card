using CardGame.Content.Standard;
using CardGame.Core;

internal static class MengHuoChecks
{
    public static void HuoshouAndZaiqiReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var huoshouVerified = false;
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
                !setup.ValidContentIds.Contains("classic:meng-huo") ||
                !game.Submit(new SelectGeneralCommand(0, "classic:meng-huo", game.Revision, setup.PromptId)).Accepted)
                continue;

            for (var step = 0; step < 900; step++)
            {
                var attributed = game.Events.Select(e => e.Payload).OfType<HuoshouAttributedEvent>().LastOrDefault();
                if (attributed is not null)
                {
                    var used = game.Events.Select(e => e.Payload).OfType<GroupCardUsedEvent>()
                        .Last(e => e.ResolutionId == attributed.ResolutionId);
                    Require(attributed.DamageSourceSeat == 0 && !used.TargetSeats.Contains(0),
                        "Huoshou must exclude Meng Huo from Barbarian Assault and attribute its damage to him.");
                    huoshouVerified = true;
                }
                if (game.PendingDecision is
                    {
                        Kind: DecisionKind.ProgramTrigger,
                        PlayerSeat: 0,
                        IsPrivate: true,
                        SkillPrompt.SkillId: "classic:zaiqi"
                    } offer)
                {
                    var checkpoint = game.CreateCheckpoint();
                    var ownerBefore = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
                    var lostHpBefore = ownerBefore.MaxHp - ownerBefore.Hp;
                    var skipped = GameReplay.Restore(checkpoint, registry);
                    var skippedOffer = skipped.PendingDecision!;
                    var skip = skippedOffer.Choices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("program-action") == "skip");
                    Require(skipped.Submit(new AnswerPromptCommand(
                                0, skippedOffer.PromptId, skip.Id, skipped.Revision)).Accepted &&
                            skipped.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0)
                                .HandCount == ownerBefore.HandCount + 2 &&
                            skipped.Events.Select(e => e.Payload).OfType<ProgramCardsRevealedEvent>()
                                .All(item => item.SkillId != "classic:zaiqi") &&
                            skipped.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                                item.SkillId == "classic:zaiqi" && !item.Activated && !item.Completed),
                        "Skipping generic Zaiqi must perform one normal two-card draw without revealing cards.");
                    var use = offer.Choices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("program-action") == "activate");
                    Require(game.Submit(new AnswerPromptCommand(0, offer.PromptId, use.Id, game.Revision)).Accepted,
                        "Zaiqi choice was rejected.");
                    var revealed = game.Events.Select(e => e.Payload).OfType<ProgramCardsRevealedEvent>()
                        .Last(item => item.SkillId == "classic:zaiqi");
                    var hearts = revealed.Cards.Where(card => card.Suit == Suit.Heart).Select(card => card.Id).ToArray();
                    var gained = revealed.Cards.Where(card => card.Suit != Suit.Heart).Select(card => card.Id).ToArray();
                    Require(lostHpBefore > 0 && revealed.Cards.Count == lostHpBefore &&
                            hearts.All(id => game.CardMovements.Any(move =>
                                move.CardId == id && move.To == CardLocation.DiscardPile &&
                                move.Reason.Value == "skill-program.classic:zaiqi.MoveBoundCards")) &&
                            gained.All(id => game.CardMovements.Any(move =>
                                move.CardId == id && move.To == CardLocation.Hand(0) &&
                                move.Reason.Value == "skill-program.classic:zaiqi.MoveBoundCards")) &&
                            (hearts.Length == 0 ||
                             game.Events.Select(e => e.Payload).OfType<RecoveryAppliedEvent>().Any(item =>
                                 item.SourceSeat == 0 && item.TargetSeat == 0 && item.Amount == hearts.Length)) &&
                            game.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                                item.SkillId == "classic:zaiqi" && item.Activated && item.Completed) &&
                            game.Events.Select(e => e.Payload).All(item => item is not ZaiqiResolvedEvent),
                        "Zaiqi must use generic reveal, partition, move, recovery and completion evidence.");
                    var restored = GameReplay.Restore(checkpoint, registry);
                    var restoredOffer = restored.PendingDecision!;
                    var restoredUse = restoredOffer.Choices.Single(choice => choice.Id == use.Id);
                    Require(restored.Submit(new AnswerPromptCommand(0, restoredOffer.PromptId, restoredUse.Id, restored.Revision)).Accepted &&
                            SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                            SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
                        "A paused Zaiqi choice must replay exactly.");
                    if (huoshouVerified) return;
                    break;
                }
                if (!AdvanceConservatively(game)) break;
            }
        }
        throw new InvalidOperationException("No bounded Meng Huo fixture verified both Huoshou and Zaiqi.");
    }

    private static bool AdvanceConservatively(GameEngine game)
    {
        if (game.PendingDecision is not { } pending)
            return game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted;
        if (pending.PlayerSeat != 0)
            return game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted;
        if (pending.Kind == DecisionKind.PlayCard)
            return game.Submit(new EndPlayPhaseCommand(0, game.Revision, pending.PromptId)).Accepted;
        if (pending.Kind == DecisionKind.DiscardCards)
            return game.Submit(new DiscardCardsCommand(0, pending.ValidCardIds.Take(pending.RequiredCardCount).ToArray(),
                pending.PromptId, game.Revision)).Accepted;
        var choice = pending.Choices.FirstOrDefault(candidate =>
            candidate.Parameters.GetValueOrDefault("action")?.Contains("skip", StringComparison.Ordinal) == true) ??
            pending.Choices.LastOrDefault();
        return choice is not null && game.Submit(new AnswerPromptCommand(0, pending.PromptId, choice.Id, game.Revision)).Accepted;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
