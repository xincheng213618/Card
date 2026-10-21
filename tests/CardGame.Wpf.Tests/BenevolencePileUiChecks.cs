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
        using var model = new MainViewModel(false, 17, true, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsDeveloperView = false };
        var game = Program.Engine(model);
        var initial = game.CreateSnapshot(0);
        var cards = initial.Players.Single(player => player.IsHuman).Hand.Take(2).ToArray();
        Program.Assert(cards.Length == 2, "The UI fixture needs two real card faces.");
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
