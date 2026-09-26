using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class XuShuChecks
{
    private const int OwnerSeat = 0;

    public static void JujianBenefitsAndReplay()
    {
        Require(GameCheckpoint.CurrentRulesVersion >= 135,
            "The program-backed Jujian requires the rules v135 compatibility boundary.");
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var ownerCheckpoint = FindOwnerPrompt(registry);
        var owner = GameReplay.Restore(ownerCheckpoint, registry);
        var prompt = RequireJujianPrompt(owner, "activate");
        Require(prompt is { PlayerSeat: OwnerSeat, IsPrivate: true } &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip") &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"),
            "Xu Shu must receive the ordinary private optional-program prompt at the turn-ending boundary.");

        const int targetSeat = 1;
        var draw = RunBranch(ownerCheckpoint, registry, targetSeat, "draw", _ => { });
        Require(draw.Choice is { ResultBind: "benefit", OptionId: "draw", ChooserSeat: targetSeat } &&
                draw.AfterTarget.HandCount == draw.BeforeTarget.HandCount + 2 &&
                draw.Game.CardMovements.Count(move =>
                    move.Reason.Value == "skill-program.classic:jujian.Draw" &&
                    move.To == CardLocation.Hand(targetSeat)) == 2,
            "The generic Jujian draw branch must move exactly two physical cards to the selected target's hand.");
        var replayedDraw = GameReplay.Restore(draw.Game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(replayedDraw.CreateSnapshot(OwnerSeat, revealAll: true)) ==
                SnapshotJson.Serialize(draw.Game.CreateSnapshot(OwnerSeat, revealAll: true)) &&
                replayedDraw.CardMovements.SequenceEqual(draw.Game.CardMovements),
            "A completed program-backed Jujian draw branch must restore to the same public and physical state.");

        var recovery = RunBranch(ownerCheckpoint, registry, targetSeat, "recover", player =>
            player.Hp = player.MaxHp - 1);
        Require(recovery.Choice is { OptionId: "recover", ChooserSeat: targetSeat } &&
                recovery.AfterTarget.Hp == recovery.BeforeTarget.Hp + 1 &&
                recovery.AfterTarget.HandCount == recovery.BeforeTarget.HandCount &&
                recovery.Game.Events.Any(envelope => envelope.Payload is RecoveryAppliedEvent
                {
                    SourceSeat: OwnerSeat,
                    TargetSeat: targetSeat,
                    Amount: 1
                }),
            "The generic Jujian recovery branch must restore exactly one HP through the ordinary recovery path.");

        var restored = RunBranch(ownerCheckpoint, registry, targetSeat, "restore", player =>
        {
            player.IsFaceDown = true;
            player.IsChained = true;
        });
        Require(restored.Choice is { OptionId: "restore", ChooserSeat: targetSeat } &&
                restored.BeforeTarget.IsFaceDown && restored.BeforeTarget.IsChained &&
                !restored.AfterTarget.IsFaceDown && !restored.AfterTarget.IsChained &&
                restored.Game.Events.Any(envelope => envelope.Payload is ProgramChainedStateSetEvent
                {
                    TargetSeat: targetSeat,
                    IsChained: false
                }),
            "The generic Jujian restore branch must turn the selected target face up and remove chaining.");

        var repeatedRecovery = RunBranch(ownerCheckpoint, registry, targetSeat, "recover", player =>
            player.Hp = player.MaxHp - 1);
        var repeatedRestore = RunBranch(ownerCheckpoint, registry, targetSeat, "restore", player =>
        {
            player.IsFaceDown = true;
            player.IsChained = true;
        });
        Require(SnapshotJson.Serialize(repeatedRecovery.Game.CreateSnapshot(OwnerSeat, revealAll: true)) ==
                SnapshotJson.Serialize(recovery.Game.CreateSnapshot(OwnerSeat, revealAll: true)) &&
                SnapshotJson.Serialize(repeatedRestore.Game.CreateSnapshot(OwnerSeat, revealAll: true)) ==
                SnapshotJson.Serialize(restored.Game.CreateSnapshot(OwnerSeat, revealAll: true)),
            "Jujian recovery and restoration must remain deterministic from the same controlled boundary.");

        var skipped = GameReplay.Restore(ownerCheckpoint, registry);
        var skippedPrompt = RequireJujianPrompt(skipped, "skip");
        var movementsBeforeSkip = skipped.CardMovements.Count;
        AnswerProgram(skipped, skippedPrompt, "skip");
        Require(skipped.CardMovements.Count == movementsBeforeSkip &&
                !skipped.Events.Select(envelope => envelope.Payload).OfType<ProgramOptionChosenEvent>()
                    .Any(item => item.SkillId == "classic:jujian") &&
                skipped.Events.Select(envelope => envelope.Payload).OfType<ProgramBindingResolvedEvent>().Last(item =>
                    item.SkillId == "classic:jujian") is { Activated: false, Completed: false },
            "Skipping Jujian must leave cards untouched and close the ordinary optional program without a result choice.");
    }

    private static GameCheckpoint FindOwnerPrompt(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = OwnerSeat,
                HumanRole = Role.Lord,
                PlayerCount = 5,
                ModeId = "identity:classic-5",
                UseInteractiveSetup = true,
                UseInteractiveDiscard = true,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 80
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted ||
                game.PendingDecision is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: OwnerSeat } setup ||
                !setup.ValidContentIds.Contains("classic:xu-shu") ||
                !game.Submit(new SelectGeneralCommand(
                    OwnerSeat, "classic:xu-shu", game.Revision, setup.PromptId)).Accepted)
                continue;

            for (var step = 0; step < 64 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) break;
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: OwnerSeat } play)
                continue;
            var self = game.CreateSnapshot(OwnerSeat, revealAll: true).Players[OwnerSeat];
            if (!self.Hand.Concat(self.Equipment).Any(card => IsNonBasic(card.Kind))) continue;
            if (!game.Submit(new EndPlayPhaseCommand(OwnerSeat, game.Revision, play.PromptId)).Accepted)
                continue;

            for (var step = 0; step < 32; step++)
            {
                if (game.PendingDecision is
                    {
                        Kind: DecisionKind.ProgramTrigger,
                        PlayerSeat: OwnerSeat,
                        SkillPrompt.SkillId: "classic:jujian"
                    })
                    return game.CreateCheckpoint();
                if (game.PendingDecision is { Kind: DecisionKind.DiscardCards, PlayerSeat: OwnerSeat } discard)
                {
                    if (!game.Submit(new DiscardCardsCommand(
                            OwnerSeat,
                            discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(),
                            discard.PromptId,
                            game.Revision)).Accepted)
                        break;
                }
                else if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException("No bounded Xu Shu fixture reached the program-backed Jujian prompt with a legal non-basic cost.");
    }

    private static BranchResult RunBranch(
        GameCheckpoint ownerCheckpoint,
        ContentRegistry registry,
        int targetSeat,
        string optionId,
        Action<CharacterState> prepareTarget)
    {
        var game = GameReplay.Restore(ownerCheckpoint, registry);
        prepareTarget(Players(game)[targetSeat]);
        var beforeTarget = game.CreateSnapshot(OwnerSeat, revealAll: true).Players[targetSeat];

        var activation = RequireJujianPrompt(game, "activate");
        AnswerProgram(game, activation, "activate");
        var targetPrompt = RequireJujianPrompt(game, "select-target");
        var targetChoice = targetPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-target" &&
            choice.Targets.SequenceEqual([targetSeat]));
        Answer(game, targetPrompt, targetChoice);

        var paymentPrompt = RequireJujianPrompt(game, "select-and-move-owned-card");
        var payment = paymentPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card");
        var paidCard = payment.Cards.Single();
        var ownerView = game.CreateSnapshot(OwnerSeat, revealAll: true).Players[OwnerSeat];
        var paidKind = ownerView.Hand.Concat(ownerView.Equipment).Single(card => card.Id == paidCard).Kind;
        Require(IsNonBasic(paidKind),
            "Jujian must expose only Trick or Equipment cards through the generic payment prompt.");
        Answer(game, paymentPrompt, payment);
        Require(game.CardMovements.Any(move =>
                move.CardId == paidCard && move.To == CardLocation.DiscardPile &&
                move.Reason.Value == "skill-program.classic:jujian.SelectAndMoveOwnedCard"),
            "Jujian must discard the exact selected non-basic physical card through the generic payment operation.");

        var optionPrompt = RequireJujianPrompt(game, "choose-option", targetSeat);
        Require(optionPrompt.PlayerSeat == targetSeat && optionPrompt.IsPrivate &&
                optionPrompt.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("result-bind") == "benefit"),
            "Jujian must transfer the private generic benefit choice to the selected target.");
        var option = optionPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("option-id") == optionId);
        Answer(game, optionPrompt, option);

        var afterTarget = game.CreateSnapshot(OwnerSeat, revealAll: true).Players[targetSeat];
        var chosen = game.Events.Select(envelope => envelope.Payload)
            .OfType<ProgramOptionChosenEvent>()
            .Last(item => item.SkillId == "classic:jujian");
        Require(chosen.OwnerSeat == OwnerSeat && chosen.ChooserSeat == targetSeat &&
                chosen.ResultBind == "benefit" && chosen.OptionId == optionId,
            "Jujian must publish its owner, selected responder and generic named-choice result.");
        return new BranchResult(game, beforeTarget, afterTarget, chosen);
    }

    private static PendingDecision RequireJujianPrompt(
        GameEngine game,
        string action,
        int? viewerSeat = null) =>
        (viewerSeat is { } seat
            ? game.CreateSnapshot(seat, revealAll: false).PendingDecision
            : game.PendingDecision) is
        {
            Kind: DecisionKind.ProgramTrigger,
            SkillPrompt.SkillId: "classic:jujian"
        } prompt && prompt.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == action)
            ? prompt
            : throw new InvalidOperationException(
                $"Expected generic Jujian action '{action}' for viewer {viewerSeat?.ToString() ?? "human"}.");

    private static void AnswerProgram(GameEngine game, PendingDecision prompt, string action) =>
        Answer(game, prompt, prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == action));

    private static void Answer(GameEngine game, PendingDecision prompt, PromptChoice choice)
    {
        var result = game.Submit(new AnswerPromptCommand(
            prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? $"The generic Jujian action '{choice.Id}' was rejected.");
    }

    private static bool IsNonBasic(CardKind kind) =>
        CardCatalog.Get(kind).CategoryName != "基本牌";

    private static IReadOnlyList<CharacterState> Players(GameEngine game) =>
        (IReadOnlyList<CharacterState>)(typeof(GameEngine)
            .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game) ?? throw new InvalidOperationException("The Xu Shu players are unavailable."));

    private sealed record BranchResult(
        GameEngine Game,
        PlayerSnapshot BeforeTarget,
        PlayerSnapshot AfterTarget,
        ProgramOptionChosenEvent Choice);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
