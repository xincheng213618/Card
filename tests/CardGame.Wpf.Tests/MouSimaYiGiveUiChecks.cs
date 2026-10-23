using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class MouSimaYiGiveUiChecks
{
    public static void CancelReselectAndConfirm(string output)
    {
        var (game, registry) = MouSimaYiGiveScenario.Start();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            var prompt = game.CreateSnapshot(0).PendingDecision!;
            var before = game.Revision;
            var moves = game.CardMovements.Count;
            var first = model.Hand.First(card => card.Kind == CardKind.CixiongDoubleSwords);
            model.SelectCardCommand.Execute(first);
            Program.Assert(model.CanConfirmSelected && model.ActionHint.Contains("权谋") && model.ActionHint.Contains("交给"),
                "Selecting the physical sword must describe the actual Quanmou Give and enable confirmation.");
            Program.Render(root, 1120, 780, Path.Combine(output, "quanmou-give-selected.png"));
            var cancel = (Button)window.FindName("CancelActionButton");
            Click(cancel);
            Program.Assert(!model.HasSelection && !model.CanConfirmSelected && game.Revision == before && game.CardMovements.Count == moves &&
                game.CreateSnapshot(0).PendingDecision!.PromptId == prompt.PromptId,
                "The actual cancel control clears this mandatory Give draft without paying or skipping the rule.");
            model.SelectPaymentEquipmentCommand.Execute(model.HumanEquipmentSlots.Single(slot => slot.Card?.Kind == CardKind.RenwangShield));
            Program.Assert(model.CanConfirmSelected, "The giver may select a legal equipped entity instead.");
            model.ClearSelectionCommand.Execute(null);
            var selected = model.Hand.First(card => card.Kind == CardKind.ZhuqueFan);
            model.SelectCardCommand.Execute(selected);
            typeof(MainViewModel).GetMethod("RefreshCommandResult", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(model, [game.CreateSnapshot(0)]);
            Program.Render(root, 1120, 780, Path.Combine(output, "quanmou-give-reselected.png"));
            Program.Assert(selected.IsSelected && model.CanConfirmSelected, "Reconciliation preserves the actual replacement card draft.");
            Click((Button)window.FindName("PlayCardButton"));
            Program.Assert(game.Revision == before + 1 && !game.IsFaulted && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                answer.Choice == prompt.Choices.Single(c => c.Cards.SequenceEqual(new[] { selected.Id })).Id &&
                game.Events.Select(e => e.Payload).OfType<MouSmGivePaidEvent>().Single().CardId == selected.Id &&
                game.CardMovements.Count(m => m.CardId == selected.Id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)) == 1,
                "The actual confirmation button answers the reselected entity once without the duplicate-ID exception.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void Click(Button button)
    {
        Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible, "The actual action button must be reachable.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }
}
