using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class DiscardUiChecks
{
    public static void PartialPaymentsShareActionRowAndFinishAutomatically(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new DiscardDeck());
        var game = GameEngine.CreateStandard(new GameOptions
            { Seed = 87381, PlayerCount = 5, UseInteractiveSetup = true, DeckId = "fixture:discard-ui" }, registry);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "Discard fixture failed to start.");
        var setup = game.PendingDecision!;
        Program.Assert(game.Submit(new SelectGeneralCommand(0, setup.Choices[0].ContentIds[0], game.Revision, setup.PromptId)).Accepted,
            "Discard fixture failed to select its general.");
        Program.Assert(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)).Accepted &&
            game.PendingDecision is { Kind: DecisionKind.DiscardCards, RequiredCardCount: > 1 },
            "The fixed fixture must publish a multi-card hand-limit discard.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, 87381, showSetup: false, saveStore: store, contentRegistry: registry)
            { IsMotionEnabled = false, IsMusicEnabled = false, IsSoundEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        using var host = new NativeResponseStatusChecks.NativeHost(model);
        var window = host.Window;
        var root = host.Root;
        var confirm = (Button)window.FindName("PlayCardButton");
        var recommend = (Button)window.FindName("RecommendDiscardButton");
        var cancel = (Button)window.FindName("CancelActionButton");
        var hint = (TextBlock)window.FindName("ActionDockHint");
        var required = model.RequiredDiscardCount;
        var hand = model.Hand.Count;
        foreach (var width in new[] { 1120, 1440 })
        {
            root.Width = width; root.Height = 820;
            window.SizeToContent = SizeToContent.WidthAndHeight;
            Program.Render(root, width, 820, null);
            var confirmPoint = confirm.TranslatePoint(new Point(), root);
            var recommendPoint = recommend.TranslatePoint(new Point(), root);
            Program.Assert(recommend.IsVisible && ReferenceEquals(confirm.Parent, recommend.Parent) &&
                Math.Abs(confirmPoint.Y - recommendPoint.Y) < 1 && recommendPoint.X > confirmPoint.X &&
                cancel.TranslatePoint(new Point(), root).X > recommendPoint.X && hint.Visibility == Visibility.Collapsed,
                "Discard, recommend and cancel must share one row, with no hand-limit hint above it.");
        }
        Program.Assert(!model.CanConfirmSelected, "An empty discard selection must not be confirmable.");
        model.SelectCardCommand.Execute(model.Hand[0]);
        Program.Assert(model.SelectedDiscardCount == 1 && model.CanConfirmSelected && model.Hand.Count == hand,
            "Selecting one card must enable confirmation without paying before the click.");
        model.CancelActionCommand.Execute(null);
        Program.Assert(model.SelectedDiscardCount == 0 && model.Hand.Count == hand, "Cancel must preserve the unpaid hand.");
        model.SelectCardCommand.Execute(model.Hand[0]);
        Program.Render(root, 1440, 820, Path.Combine(output, "discard-one-card-action-row.png"));
        model.ConfirmSelectedCommand.Execute(null);
        Program.Assert(model.IsDiscardSelectionPending && model.RequiredDiscardCount == required - 1 &&
            model.Hand.Count == hand - 1 && model.SelectedDiscardCount == 0,
            "A one-card confirmation must remove only that card and show the remaining quantity.");
        model.SaveGameCommand.Execute(null);
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(model.IsDiscardSelectionPending && model.RequiredDiscardCount == required - 1 && model.Hand.Count == hand - 1,
            "Reloading a partial discard must preserve its paid card and remaining quantity.");
        model.RecommendDiscardCommand.Execute(null);
        Program.Assert(model.SelectedDiscardCount == required - 1 && model.CanConfirmSelected && model.Hand.Count == hand - 1,
            "Recommendation must only select the remaining cards for review.");
        model.ConfirmSelectedCommand.Execute(null);
        Program.Assert(!model.IsDiscardSelectionPending && model.SelectedDiscardCount == 0,
            "Meeting the remaining quota must automatically end the discard decision.");
    }

    private sealed class DiscardDeck : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-discard-ui", new Version(1, 0));
        public void Register(IContentRegistryBuilder builder) => builder.AddDeck(
            StandardContentRegistry.Create().GetDeck("standard:basic-demo") with
                { Id = "fixture:discard-ui", InitialHandSize = 6 });
    }
}
