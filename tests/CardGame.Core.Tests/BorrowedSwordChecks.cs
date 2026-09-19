using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BorrowedSwordChecks
{
    public static void TransferSlashAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var sourceBoundary = BorrowedSwordScenario.FindHumanSourcePlay();
        var legacyRules = GameReplay.Restore(
            RoundTrip(sourceBoundary.CreateCheckpoint()) with { RulesVersion = 41 },
            registry);
        Require(sourceBoundary.GetHumanLegalActions().Any(action =>
                    action.Kind == LegalActionKind.BorrowedSword) &&
                legacyRules.GetHumanLegalActions().All(action =>
                    action.Kind != LegalActionKind.BorrowedSword),
            "Rules v42 must enable Borrowed Sword while rules v41 preserves the same checkpoint without that action.");

        var boundary = BorrowedSwordScenario.FindHumanOwnerResponse();
        var prompt = boundary.PendingDecision ??
            throw new InvalidOperationException("Borrowed Sword fixture lost its private response prompt.");
        var sourceSeat = prompt.SourceSeat ??
            throw new InvalidOperationException("Borrowed Sword prompt omitted its trick source.");
        var slashTargetSeat = prompt.TargetSeat ??
            throw new InvalidOperationException("Borrowed Sword prompt omitted its forced Slash target.");
        var ownerBefore = boundary.CreateSnapshot(0, revealAll: true).Players[0];
        var weapon = ownerBefore.Equipment.Single(card =>
            EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon);

        Require(prompt.IsPrivate && prompt.PlayerSeat == 0 &&
                boundary.CreateSnapshot(sourceSeat).PendingDecision is null,
            "Borrowed Sword must expose the use-or-transfer choice only to the weapon owner.");
        Require(boundary.ResolutionStack.OfType<CardUseFrame>().Single(frame =>
                    frame.CardKind == CardKind.BorrowedSword).TargetSeats
                .SequenceEqual([0, slashTargetSeat]),
            "Borrowed Sword must retain its ordered weapon-owner and Slash-target pair.");

        var boundaryCheckpoint = RoundTrip(boundary.CreateCheckpoint());
        var transferBranch = GameReplay.Restore(boundaryCheckpoint, registry);
        Require(State(transferBranch) == State(boundary) &&
                Events(transferBranch).SequenceEqual(Events(boundary)),
            "The paused Borrowed Sword owner choice must restore exactly.");

        var transferPrompt = transferBranch.PendingDecision!;
        var giveChoice = transferPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("response") == "borrowed-sword-give-weapon");
        var gave = transferBranch.Submit(new AnswerPromptCommand(
            0,
            transferPrompt.PromptId,
            giveChoice.Id,
            transferBranch.Revision));
        Require(gave.Accepted, gave.Error?.Message ?? "Borrowed Sword weapon transfer was rejected.");
        var transferSnapshot = transferBranch.CreateSnapshot(0, revealAll: true);
        Require(transferSnapshot.Players[0].Equipment.All(card => card.Id != weapon.Id) &&
                transferSnapshot.Players[sourceSeat].Hand.Any(card => card.Id == weapon.Id),
            "Declining the forced Slash must transfer the exact equipped weapon to the trick source.");
        Require(transferBranch.CardMovements.Any(movement =>
                    movement.CardId == weapon.Id &&
                    movement.From == CardLocation.Equipment(0) &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.BorrowedSwordGive) &&
                transferBranch.CardMovements.Any(movement =>
                    movement.CardId == weapon.Id &&
                    movement.From == CardLocation.Processing &&
                    movement.To == CardLocation.Hand(sourceSeat) &&
                    movement.Reason == CardMoveReasons.BorrowedSwordGive) &&
                transferBranch.Events.Select(item => item.Payload)
                    .OfType<BorrowedSwordResolvedEvent>()
                    .Any(resolved => !resolved.UsedSlash &&
                                     resolved.TransferredWeaponCardId == weapon.Id &&
                                     resolved.TransferredWeaponKind == weapon.Kind),
            "The weapon branch must publish exact two-step movement and a typed resolution event.");

        var slashBranch = GameReplay.Restore(boundaryCheckpoint, registry);
        var slashPrompt = slashBranch.PendingDecision!;
        var slashChoice = slashPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("response") == "borrowed-sword-slash");
        var slashCardId = slashChoice.Cards.Single();
        var used = slashBranch.Submit(new AnswerPromptCommand(
            0,
            slashPrompt.PromptId,
            slashChoice.Id,
            slashBranch.Revision));
        Require(used.Accepted, used.Error?.Message ?? "Borrowed Sword forced Slash was rejected.");
        var nestedFrames = slashBranch.ResolutionStack.OfType<CardUseFrame>().ToArray();
        var nestedResponse = slashBranch.ResolutionStack.OfType<ResponseWindowFrame>().LastOrDefault();
        Require(nestedFrames.Length == 2 &&
                nestedFrames[0].CardKind == CardKind.BorrowedSword &&
                nestedFrames[1].CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash &&
                nestedResponse is { RequiredCardKind: CardKind.Dodge } &&
                nestedResponse.ParentFrameId == nestedFrames[1].Id &&
                nestedResponse.ResponderSeat == slashTargetSeat,
            $"The forced Slash must become a real nested card use with its own Dodge window " +
            $"(frames={string.Join(',', nestedFrames.Select(frame => frame.CardKind))}, " +
            $"response={nestedResponse?.RequiredCardKind}/{nestedResponse?.ResponderSeat}, " +
            $"target={slashTargetSeat}, status={slashBranch.State.Status}).");
        Require(slashBranch.CardMovements.Any(movement =>
                    movement.CardId == slashCardId &&
                    movement.From == CardLocation.Hand(0) &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.Use),
            "Borrowed Sword must pay the forced Slash as a use, not as a discarded response shortcut.");

        var nestedCheckpoint = RoundTrip(slashBranch.CreateCheckpoint());
        var nestedReplay = GameReplay.Restore(nestedCheckpoint, registry);
        Require(State(nestedReplay) == State(slashBranch) &&
                Events(nestedReplay).SequenceEqual(Events(slashBranch)),
            "The nested Borrowed Sword Slash and Dodge window must restore exactly.");

        for (var step = 0; step < 128 && !HasUsedSlashResolution(slashBranch); step++)
        {
            BorrowedSwordScenario.Step(slashBranch);
        }

        var resolvedSlash = slashBranch.Events.Select(item => item.Payload)
            .OfType<BorrowedSwordResolvedEvent>()
            .LastOrDefault(resolved => resolved.UsedSlash);
        Require(resolvedSlash is not null &&
                resolvedSlash.SourceSeat == sourceSeat &&
                resolvedSlash.WeaponOwnerSeat == 0 &&
                resolvedSlash.SlashTargetSeat == slashTargetSeat &&
                resolvedSlash.SlashCardId == slashCardId &&
                slashBranch.ResolutionStack.All(frame =>
                    frame is not CardUseFrame cardUse || cardUse.CardKind != CardKind.BorrowedSword),
            "The nested Slash must finish before its Borrowed Sword parent closes.");
        Require(slashBranch.CreateSnapshot(0, revealAll: true).Players[0].Equipment
                .Any(card => card.Id == weapon.Id),
            "Using a hand Slash must retain the weapon instead of transferring it.");

        var completedReplay = GameReplay.Restore(RoundTrip(slashBranch.CreateCheckpoint()), registry);
        Require(State(completedReplay) == State(slashBranch) &&
                Events(completedReplay).SequenceEqual(Events(slashBranch)),
            "A completed Borrowed Sword forced-Slash branch must replay exactly.");
    }

    public static void JijiangProvidesForcedSlash()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = BorrowedSwordScenario.FindHumanOwnerResponse(requireJijiang: true);
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Borrowed Sword Jijiang fixture lost its owner prompt.");
        var targetSeat = prompt.TargetSeat ??
            throw new InvalidOperationException("Borrowed Sword Jijiang fixture omitted its target.");
        var weapon = game.CreateSnapshot(0, revealAll: true).Players[0].Equipment.Single(card =>
            EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon);
        var jijiangChoice = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("response") == "jijiang-request");
        var requested = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            jijiangChoice.Id,
            game.Revision));
        Require(requested.Accepted, requested.Error?.Message ??
            "Borrowed Sword could not request Jijiang.");
        Require(game.Events.Select(item => item.Payload)
                .OfType<JijiangRequestedEvent>()
                .Any(item => item.OwnerSeat == 0 &&
                             item.IsActiveUse &&
                             item.TargetSeat == targetSeat) &&
                game.ResolutionStack.OfType<ResponseWindowFrame>().Any(frame =>
                    frame.IncomingCard == CardKind.BorrowedSword &&
                    frame.ResponderSeat == 0),
            "Borrowed Sword Jijiang must retain the parent response window while providers are queried.");

        var requestedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(requestedReplay) == State(game) &&
                Events(requestedReplay).SequenceEqual(Events(game)),
            "A pending Borrowed Sword Jijiang provider cursor must replay exactly.");

        for (var step = 0; step < 128 && !HasUsedSlashResolution(game); step++)
        {
            BorrowedSwordScenario.Step(game);
        }

        var jijiang = game.Events.Select(item => item.Payload)
            .OfType<JijiangResolvedEvent>()
            .LastOrDefault(item => item.OwnerSeat == 0 && item.Succeeded && item.IsActiveUse);
        var borrowed = game.Events.Select(item => item.Payload)
            .OfType<BorrowedSwordResolvedEvent>()
            .LastOrDefault(item => item.UsedSlash);
        Require(jijiang is not null &&
                jijiang.ProviderSeat is { } providerSeat &&
                jijiang.SlashCardId is { } slashCardId &&
                jijiang.TargetSeat == targetSeat &&
                borrowed is not null &&
                borrowed.SlashCardId == slashCardId &&
                game.CardMovements.Any(movement =>
                    movement.CardId == slashCardId &&
                    movement.From == CardLocation.Hand(providerSeat) &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.Use),
            "A Shu provider must supply the exact physical Slash for Liu Bei's nested forced use.");
        Require(game.CreateSnapshot(0, revealAll: true).Players[0].Equipment
                .Any(card => card.Id == weapon.Id),
            "Successful Borrowed Sword Jijiang must retain Liu Bei's weapon.");

        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "A completed Borrowed Sword Jijiang branch must replay exactly.");
    }

    private static bool HasUsedSlashResolution(GameEngine game) =>
        game.Events.Select(item => item.Payload)
            .OfType<BorrowedSwordResolvedEvent>()
            .Any(resolved => resolved.UsedSlash);

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
