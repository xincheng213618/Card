using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class BenevolencePileUiChecks
{
    public static void SharedPublicCardsOpenReadOnly(string output)
    {
        var scenario = ShenMaChaoChecks.CreateForeignMountUiScenario();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, scenario.Game.CreateCheckpoint()));
        using var model = new MainViewModel(false, scenario.Game.Seed, true, store, contentRegistry: scenario.Registry,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsDeveloperView = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        var game = Program.Engine(model);
        var initial = game.CreateSnapshot(0);
        // Exercise only the snapshot-to-view boundary; physical Ren movements have Core coverage.
        var cards = new[] { new CardSnapshot(910001, CardKind.Slash, Suit.Spade, 7, "杀", "7"),
            new CardSnapshot(910002, CardKind.Peach, Suit.Heart, 3, "桃", "3") };
        var window = new MainWindow(model);
        try
        {
            Refresh(initial with { BenevolenceCards = cards });
            var root = (FrameworkElement)window.Content;
            Program.Render(root, 1120, 740, Path.Combine(output, "benevolence-public-table.png"));
            var button = (Button)window.FindName("BenevolencePileButton");
            Program.Assert(button.Visibility == Visibility.Visible && button.IsEnabled && button.ActualWidth > 0 &&
                model.BenevolencePileText == "仁区 · 2", "A populated shared public zone must have its own visible inspection entry.");
            var revision = game.Revision;
            var commands = game.AcceptedCommands.Count;
            model.OpenBenevolencePileCommand.Execute(null);
            Program.Assert(model.IsReserveDialogOpen && !model.IsReserveExchange &&
                model.ReserveDialogTitle == "公共牌区 · 仁区" && model.ReserveDialogStars.Select(card => card.Card.Id).SequenceEqual(cards.Select(card => card.Id)) &&
                model.ReserveDialogHand.Count == 0 && model.ReserveDialogBacks.Count == 0 &&
                model.ReserveDialogStars.All(card => !card.Card.IsPlayable && !card.Card.IsPrivateReveal &&
                    !model.MoveReserveCardCommand.CanExecute(card)),
                "The global zone must display the published ordered faces without a personal owner or editable exchange.");
            Program.Render(root, 1120, 740, Path.Combine(output, "benevolence-public-inspection.png"));
            model.CloseReserveDialogCommand.Execute(null);
            Program.Assert(!model.IsReserveDialogOpen && game.Revision == revision && game.AcceptedCommands.Count == commands,
                "Inspecting a shared pile must leave gameplay and command history untouched.");
            Refresh(initial with { BenevolenceCards = null });
            root.UpdateLayout();
            Program.Assert(!model.HasBenevolenceCards && button.Visibility == Visibility.Collapsed &&
                !model.OpenBenevolencePileCommand.CanExecute(null), "An emptied zone must immediately hide and disable its inspection entry.");
        }
        finally { window.Content = null; window.Close(); }
        void Refresh(GameSnapshot snapshot) => typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(model, [snapshot]);
    }
}
