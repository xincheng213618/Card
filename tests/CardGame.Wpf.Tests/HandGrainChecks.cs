using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class HandGrainChecks
{
    public static void StoredCardKeepsItsControlAndUpdatesItsZone(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 2, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5, ModeId = Fixture.ModeId,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        var ox = game.GetHumanLegalActions().First(action => action.CardId is { } id &&
            game.CreateSnapshot(0).Players[0].Hand.Single(card => card.Id == id).Kind == CardKind.WoodenOx);
        Accept(game.Submit(new PlayCardCommand(0, ox.CardId!.Value, [], game.Revision, game.PendingDecision!.PromptId)));
        for (var step = 0; step < 8 && game.PendingDecision is null; step++) Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var vm = new MainViewModel(false, 2, true, store, useExpandedContent: true,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore(), contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false };
        vm.LoadManualGameCommand.Execute(null);
        Program.Assert(!vm.HasSaveError, vm.SaveStatus);
        game = Program.Engine(vm);
        var action = game.GetHumanLegalActions().Single(item => item.EquipmentKind == CardKind.WoodenOx);
        var id = action.SelectableCardIds.First(candidate => vm.Hand.Single(card => card.Id == candidate).Kind == CardKind.Slash);
        var card = vm.Hand.Single(card => card.Id == id);
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        try
        {
            Program.Render(root, 1120, 740, Path.Combine(output, "grain-before-storage.png"));
            var items = (ItemsControl)window.FindName("HandCards");
            var container = items.ItemContainerGenerator.ContainerFromItem(card);
            var hint = card.CardHint;
            var count = vm.Hand.Count;
            vm.SelectActiveSkillCommand.Execute(action);
            vm.SelectCardCommand.Execute(card);
            Program.Assert(vm.CanConfirmActiveSkill, "The real Wooden Ox action did not accept a hand card without transfer.");
            vm.UseActiveSkillCommand.Execute(null);
            for (var step = 0; step < 8 && vm.CanStepAi; step++) vm.StepAiCommand.Execute(null);
            Program.Render(root, 1120, 740, Path.Combine(output, "grain-after-storage.png"));
            var human = game.CreateSnapshot(0).Players[0];
            Program.Assert(human.WoodenOxGrain?.Single().Id == id && human.Hand.All(item => item.Id != id),
                "The UI fixture did not actually move the card from hand into Wooden Ox.");
            Program.Assert(ReferenceEquals(card, vm.Hand.Single(item => item.Id == id)) &&
                           ReferenceEquals(container, items.ItemContainerGenerator.ContainerFromItem(card)) && vm.Hand.Count == count,
                "Storing a still-visible card recreated its hand control or removed its physical card.");
            Console.WriteLine($"Stored card {id}: marker={card.IsStoredGrain}, label={card.KindLabel}, hand={human.Hand.Count}, grain={human.WoodenOxGrainCount}, header={vm.HandCountText}");
            Program.Assert(card.IsStoredGrain && card.KindLabel.StartsWith("粮 ·", StringComparison.Ordinal) &&
                           card.CardHint != hint && card.CardHint.Contains(card.KindLabel),
                "A reused hand card kept its old zone label and tooltip after becoming stored grain.");
            Program.Assert(vm.HandCountText == $"手牌  {human.Hand.Count:00} · 粮 {human.WoodenOxGrainCount:00}",
                "Stored grain was counted as hand cards in the action area.");
            var badge = Program.Find<TextBlock>((DependencyObject)container).Single(text => text.Text == "粮");
            Program.Assert(badge.Visibility == Visibility.Visible, "The existing card control did not show its grain badge.");
            var button = Program.Find<Button>((DependencyObject)container).Single(item => item.Name == "CardButton");
            Program.Assert(Equals(button.ToolTip, card.CardHint), "The bound hand tooltip retained the previous zone description.");
            var zoneNotifications = 0;
            card.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(CardViewModel.KindLabel) or nameof(CardViewModel.IsStoredGrain)) zoneNotifications++;
            };
            typeof(MainViewModel).GetMethod("RefreshCurrentView", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(vm, null);
            root.UpdateLayout();
            Program.Assert(zoneNotifications == 0 && ReferenceEquals(container, items.ItemContainerGenerator.ContainerFromItem(card)),
                "An unchanged hand refresh repeated zone notifications or rebuilt the grain control.");

            var storedState = SnapshotJson.Serialize(game.CreateSnapshot(0));
            vm.SaveGameCommand.Execute(null);
            vm.LoadManualGameCommand.Execute(null);
            Program.Assert(!vm.HasSaveError && !ReferenceEquals(game, Program.Engine(vm)) &&
                           SnapshotJson.Serialize(Program.Engine(vm).CreateSnapshot(0)) == storedState,
                "The stored-grain fixture did not restore the saved match through the actual load command.");
            game = Program.Engine(vm);
            card = vm.Hand.Single(item => item.Id == id);
            Program.Assert(card.IsStoredGrain && vm.HandCountText == $"手牌  {human.Hand.Count:00} · 粮 01",
                "Reloading the stored card changed the zone marker or counts.");
            var play = game.GetHumanLegalActions().First(item => item.CardId == id && item.TargetSeat is not null);
            vm.SelectCardCommand.Execute(card);
            foreach (var seat in play.TargetSeats) vm.SelectTargetCommand.Execute(vm.Seats.Single(item => item.Seat == seat));
            Program.Assert(vm.CanConfirmSelected, "The marked grain could no longer be played through the ordinary hand controls.");
            vm.ConfirmSelectedCommand.Execute(null);
            Program.Assert(vm.Hand.All(item => item.Id != id) && !vm.HandCountText.Contains("粮") &&
                           game.CardMovements.Any(move => move.CardId == id && move.From == CardLocation.WoodenOxGrain(0)),
                "Playing stored grain retained a stale badge/count or spent a different physical card.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void Accept(CommandResult result) => Program.Assert(result.Accepted, result.Error?.Message ?? "Fixture command rejected.");

    private sealed class Fixture : IGameContentPackage
    {
        public const string ModeId = "identity:classic-ui-grain-5";
        public PackageManifest Manifest { get; } = new("fixture-ui-grain", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe("fixture:ui-grain-deck", "存粮界面场景", 4, 2,
                [new("classic:wooden-ox", 24), new("standard:slash", 24), new("standard:dodge", 12), new("standard:peach", 8)]));
            builder.AddMode(new ContentModeDefinition(ModeId, "存粮界面场景", 5, 5,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 }, "fixture:ui-grain-deck", 3,
                ["classic:sun-quan", "classic:huang-gai", "classic:gan-ning", "classic:lu-meng", "classic:zhang-fei"]));
        }
    }
}
