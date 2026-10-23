using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ChenglueUiChecks
{
    public static void BatchAndIncrementalDiscards(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        foreach (var batch in new[] { true, false })
        {
            var source = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 17, PlayerCount = 4, HumanRole = Role.Lord, UseInteractiveSetup = true,
                ModeId = Fixture.ModeId, AdvanceAfterHumanCommands = false
            }, registry);
            Accept(source.Submit(new StartGameCommand()));
            Accept(source.Submit(new SelectGeneralCommand(0, "classic:xu-you", source.Revision, source.PendingDecision!.PromptId)));
            for (var step = 0; step < 32 && source.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                Accept(source.Submit(new AdvanceOneStepCommand(source.Revision)));
            Accept(source.Submit(new UseProgramSkillCommand(0, "classic:chenglue", "alternate", [], [],
                source.Revision, source.PendingDecision!.PromptId)));
            for (var step = 0; step < 32 && source.CreateSnapshot(0).PendingDecision?.SkillPrompt?.SkillId != "classic:chenglue"; step++)
                Accept(source.Submit(new AdvanceOneStepCommand(source.Revision)));
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, source.CreateCheckpoint()));
            using var model = new MainViewModel(false, 17, showSetup: false, saveStore: store, contentRegistry: registry)
                { IsMotionEnabled = false, IsMusicEnabled = false, IsSoundEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError && model.IsOwnedCardSelectionPending && !model.IsCardPaymentSelectionPending,
                "Cold Chenglue must use the shared multi-card draft rather than a singleton payment.");
            var game = Program.Engine(model);
            var prompt = game.CreateSnapshot(0).PendingDecision!;
            var ids = prompt.ValidCardIds.Take(2).ToArray();
            var handCount = model.Hand.Count;
            var commands = game.AcceptedCommands.Count;
            Program.Assert(prompt is { RequiredCardCount: 2, MinimumCardCount: 1 } && ids.Length == 2 &&
                !model.CanConfirmSelected && Enumerable.Range(1, 3).All(seat => game.CreateSnapshot(seat).PendingDecision is null),
                "The chooser sees the two-card obligation and other players see no private choices.");
            Select(ids[0]);
            Select(ids[1]);
            var third = prompt.ValidCardIds.First(id => !ids.Contains(id));
            Select(third);
            typeof(MainViewModel).GetMethod("RefreshCurrentView", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)!.Invoke(model, null);
            Program.Assert(model.Hand.Count(card => card.IsSelected) == 2 && model.CanConfirmSelected &&
                game.AcceptedCommands.Count == commands && model.Hand.Count == handCount && !model.HasCenterChoices,
                "Both selected cards survive refresh, selection is capped at two, and no card is paid before confirmation.");
            model.CancelActionCommand.Execute(null);
            Program.Assert(!model.HasSelection && model.Hand.Count == handCount && game.AcceptedCommands.Count == commands,
                "Cancel clears the entire unpaid draft without submitting a choice.");
            Select(ids[0]);
            if (batch) Select(ids[1]);
            using var host = new NativeResponseStatusChecks.NativeHost(model);
            host.Capture(output, batch ? "chenglue-two-selected" : "chenglue-one-selected", 1120);
            model.ConfirmSelectedCommand.Execute(null);
            if (!batch)
            {
                Program.Assert(model.IsOwnedCardSelectionPending && game.CreateSnapshot(0).PendingDecision is
                    { RequiredCardCount: 1 } && model.Hand.Count == handCount && !model.HasSelection &&
                    game.AcceptedCommands.Count == commands + 1,
                    "Confirming one card keeps the remaining selection open without discarding the unpaid set.");
                model.SaveGameCommand.Execute(null);
                model.LoadManualGameCommand.Execute(null);
                Program.Assert(!model.HasSaveError && model.IsOwnedCardSelectionPending,
                    "The partially confirmed discard can be saved and cold loaded.");
                game = Program.Engine(model);
                Select(ids[1]);
                Program.Assert(model.CanConfirmSelected, "The remaining card must be confirmable after cold recovery.");
                model.ConfirmSelectedCommand.Execute(null);
            }
            var moves = game.CardMovements.Where(move => move.Reason.Value == "skill-program.alternating-suit.discard").ToArray();
            var allowance = game.Events.Select(item => item.Payload).OfType<PlayPhaseSuitAllowanceGrantedEvent>().Single();
            Program.Assert(!model.IsOwnedCardSelectionPending && moves.Length == 2 &&
                moves.Select(move => move.CardId).ToHashSet().SetEquals(ids) &&
                moves.All(move => move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile) &&
                allowance.DiscardedCardIds.ToHashSet().SetEquals(ids) && model.Hand.Count == handCount - 2,
                "Batch and incremental confirmations discard the same exact two physical cards once and grant one suit allowance.");
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Program.Assert(JsonSerializer.Serialize(game.CreateSnapshot(0)) == JsonSerializer.Serialize(restored.CreateSnapshot(0)),
                "The committed batch remains replayable after either UI confirmation path.");

            void Select(int id) => model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == id));
        }
    }

    private static void Accept(CommandResult result) => Program.Assert(result.Accepted, result.Error?.Message ?? "Chenglue fixture command rejected.");

    private sealed class Fixture : IGameContentPackage
    {
        internal const string ModeId = "identity:classic-ui-chenglue";
        public PackageManifest Manifest { get; } = new("fixture-ui-chenglue", new Version(1, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "classic:xu-you");
            for (var seat = 1; seat < 4; seat++)
                builder.AddGeneral(new($"fixture:ui-chenglue-{seat}", $"目标{seat}", "supporter", "standard:none", "wei", 4));
            builder.AddMode(new(ModeId, "成略界面测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "standard:basic-demo", GeneralCandidateCount: 4,
                GeneralPoolIds: ["classic:xu-you", "fixture:ui-chenglue-1", "fixture:ui-chenglue-2", "fixture:ui-chenglue-3"]));
        }
    }
}
