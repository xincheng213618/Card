using CardGame.Content.Standard;
using CardGame.Core;
using System.Collections;
using System.Reflection;

internal static class WoodenOxChecks
{
    public static void StoresPrivatePlayableGrainAndReplays()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new ScenarioPackage());

        for (var seed = 1; seed <= 256; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                PlayerCount = 5,
                ModeId = ScenarioPackage.ModeId,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 20
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted, "Wooden Ox fixture failed to start.");
            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } play)
            {
                continue;
            }

            var equip = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.CardId is { } id && game.State.Players[0].Hand.Single(card => card.Id == id).Kind == CardKind.WoodenOx);
            if (equip is null)
            {
                continue;
            }

            Require(game.Submit(new PlayCardCommand(0, equip.CardId!.Value, [], game.Revision, play.PromptId)).Accepted,
                "Wooden Ox could not be equipped.");
            if (game.PendingDecision is null)
            {
                Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Wooden Ox play did not resume.");
            }

            var action = game.GetHumanLegalActions().Single(candidate =>
                candidate.Kind == LegalActionKind.UseEquipmentEffect && candidate.EquipmentKind == CardKind.WoodenOx);
            var storedId = action.SelectableCardIds.First(id =>
                game.State.Players[0].Hand.Single(card => card.Id == id).Kind == CardKind.Slash);
            var beforeStorage = game.CreateCheckpoint();
            Require(game.Submit(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [storedId], [],
                game.Revision, game.PendingDecision!.PromptId)).Accepted, "Wooden Ox storage was rejected.");
            if (game.PendingDecision is null)
            {
                Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                    "Wooden Ox storage did not return to the play prompt.");
            }

            var owner = game.CreateSnapshot(0).Players[0];
            var other = game.CreateSnapshot(1).Players[0];
            Require(owner.WoodenOxGrainCount == 1 && owner.WoodenOxGrain!.Single().Id == storedId,
                "The owner must see the exact stored grain.");
            Require(other.WoodenOxGrainCount == 1 && other.WoodenOxGrain!.Count == 0,
                "Other viewers must see only the public grain count.");
            Require(game.GetHumanLegalActions().Any(candidate => candidate.CardId == storedId),
                "A stored grain must remain available for normal use.");
            Require(game.GetHumanLegalActions().All(candidate => candidate.EquipmentKind != CardKind.WoodenOx),
                "Wooden Ox may be activated only once in the play phase.");

            var replayed = GameReplay.Restore(
                GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Require(SnapshotJson.Serialize(replayed.CreateSnapshot(0, revealAll: true)) ==
                    SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
                "Wooden Ox storage must replay exactly.");

            var transferred = GameReplay.Restore(beforeStorage, registry);
            var targetSeat = action.SelectableTargetSeats.First();
            Require(transferred.Submit(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [storedId], [targetSeat],
                transferred.Revision, transferred.PendingDecision!.PromptId)).Accepted,
                "Wooden Ox transfer was rejected.");
            var transferState = transferred.CreateSnapshot(0, revealAll: true);
            Require(transferState.Players[0].WoodenOxGrainCount == 0 &&
                    transferState.Players[targetSeat].Equipment.Any(card => card.Kind == CardKind.WoodenOx) &&
                    transferState.Players[targetSeat].WoodenOxGrainCount == 1 &&
                    transferState.Players[targetSeat].WoodenOxGrain!.Single().Id == storedId,
                "Moving Wooden Ox between equipment zones must carry all stored grain.");

            var players = (IList)typeof(GameEngine)
                .GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(transferred)!;
            var targetRuntime = players[targetSeat]!;
            var findCard = typeof(GameEngine).GetMethod(
                "FindOwnedPlayableCard", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var woodenOx = findCard.Invoke(transferred, [targetRuntime, transferState.Players[targetSeat]
                .Equipment.Single(card => card.Kind == CardKind.WoodenOx).Id])!;
            typeof(GameEngine).GetMethod("MoveCard", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(transferred,
                [woodenOx, CardLocation.Equipment(targetSeat), CardLocation.DiscardPile, CardMoveReasons.DismantlementFinished]);
            Require(transferred.CardMovements.Any(move =>
                    move.CardId == storedId &&
                    move.From == CardLocation.WoodenOxGrain(targetSeat) &&
                    move.To == CardLocation.DiscardPile &&
                    move.Reason == CardMoveReasons.WoodenOxGrainDiscard),
                "Wooden Ox grain must be discarded when the treasure leaves the equipment area.");
            return;
        }

        throw new InvalidOperationException("No bounded Wooden Ox fixture found.");
    }

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-wooden-ox-5";
        public PackageManifest Manifest { get; } = new("wooden-ox-scenario", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 35, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe("wooden-ox-scenario:deck", "木牛流马测试牌堆", 4, 2,
            [
                new ContentDeckCardCount("classic:wooden-ox", 24),
                new ContentDeckCardCount("standard:slash", 24),
                new ContentDeckCardCount("standard:dodge", 12),
                new ContentDeckCardCount("standard:peach", 8)
            ]));
            builder.AddMode(new ContentModeDefinition(ModeId, "五人经典身份（木牛流马场景）", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "wooden-ox-scenario:deck", 3,
                ["classic:sun-quan", "classic:huang-gai", "classic:gan-ning", "classic:lu-meng", "classic:zhang-fei"]));
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
