using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.ViewModels;

internal static class NamedPrivatePileUiChecks
{
    public static void OwnerPairsPublicCountsAndClearingUseSharedReadOnlyFaces(string output)
    {
        using var model = new MainViewModel(false, 17, true, new MemorySaveStore(), useExpandedContent: true)
            { IsMotionEnabled = false, IsNewGameSetupOpen = false };
        var game = Program.Engine(model); var original = game.CreateSnapshot(0);
        var checkpointBefore = JsonSerializer.Serialize(game.CreateCheckpoint());
        var first = new CardSnapshot(675101, CardKind.Peach, Suit.Heart, 7, "桃", "7");
        var second = new CardSnapshot(675102, CardKind.Dodge, Suit.Club, 12, "闪", "Q");
        var unauthorized = new CardSnapshot(675199, CardKind.FireAttack, Suit.Diamond, 3, "不应公开的实体", "3");
        var owned = new NamedAmbushPileSnapshot("fixture:named-private-pile", "伏兵", 2,
            Array.AsReadOnly(new[] { new NamedAmbushPrivateEntrySnapshot(first, CardKind.Slash), new NamedAmbushPrivateEntrySnapshot(second, CardKind.DrawTwo) }));
        // A retained developer/malformed DTO must not bypass the table's viewer gate.
        var foreign = new NamedAmbushPileSnapshot("fixture:named-private-pile", "伏兵", 3,
            Array.AsReadOnly(new[] { new NamedAmbushPrivateEntrySnapshot(unauthorized, CardKind.Nullification) }));
        var projected = original with { PendingDecision = null, PrivateRevealedCards = null,
            Players = original.Players.Select(p => p.Seat switch
            {
                0 => p with { GeneralName = "拥有者", NamedAmbushPiles = Array.AsReadOnly(new[] { owned }) },
                1 => p with { GeneralName = "他人", NamedAmbushPiles = Array.AsReadOnly(new[] { foreign }) },
                _ => p
            }).ToArray() };
        Refresh(model, projected);
        var self = model.NamedPrivatePiles.Single(p => p.OwnerSeat == 0);
        var other = model.NamedPrivatePiles.Single(p => p.OwnerSeat == 1);
        Program.Assert(model.HasNamedPrivatePiles && self.HasPrivateEntries && self.Title == "拥有者 · 伏兵 2张" && self.Entries.Count == 2 &&
            self.Entries[0].Card.Id == first.Id && self.Entries[0].RecordedName == "记录：" + CardCatalog.Get(CardKind.Slash).DisplayName &&
            self.Entries[1].Card.Id == second.Id && self.Entries[1].RecordedName == "记录：" + CardCatalog.Get(CardKind.DrawTwo).DisplayName &&
            self.Entries.All(e => e.Card.IsPrivateReveal && !e.Card.IsPlayable && !e.Card.IsPublicChoice && !model.SelectRevealedCardCommand.CanExecute(e.Card)) &&
            !other.HasPrivateEntries && other.Entries.Count == 0 && other.Title == "他人 · 伏兵 3张" &&
            model.Seats.Single(s => s.Seat == 1).DeferredPileText.Contains("伏兵 ×3") && model.HumanSummary.Contains("伏兵 ×2") &&
            !model.Seats.Single(s => s.Seat == 1).DeferredPileTooltip.Contains("不应公开的实体") && !model.HasCenterChoices && model.IsTableIdle,
            "Persistent named private piles display the owner's exact entity-to-name pairings and public seat counts without revealing another seat's retained entries or creating a choice prompt.");
        var window = new MainWindow(model); window.ApplyTemplate(); var root = (FrameworkElement)window.Content;
        var overview = Program.Find<Expander>(root).Single(e => e.Name == "NamedPrivatePileOverview"); overview.IsExpanded = true;
        Program.Render(root, 1120, 780, Path.Combine(output, "named-private-pile-owner-pairs.png"));
        var panel = Program.Find<ItemsControl>(root).Single(e => e.Name == "NamedPrivatePiles");
        var pairs = Program.Find<ItemsControl>(panel).Where(e => e.Name == "NamedPrivatePileEntries" && e.Visibility == Visibility.Visible).ToArray();
        var faces = Program.Find<Button>(panel).Where(b => b.Command == model.SelectRevealedCardCommand).ToArray();
        var names = Program.Find<TextBlock>(panel).Where(t => t.Name == "NamedPrivatePileRecordedName").Select(t => t.Text).ToArray();
        Program.Assert(overview.Visibility == Visibility.Visible && panel.ActualHeight > 0 && pairs.Length == 1 && pairs[0].Items.Count == 2 &&
            faces.Length == 2 && faces.All(b => !b.IsEnabled && b.ActualWidth > 0 && b.ActualHeight > 0) &&
            names.SequenceEqual(self.Entries.Select(e => e.RecordedName)) && !Program.Find<TextBlock>(panel).Any(t => t.Text.Contains("不应公开的实体") || t.Text == "记录：" + CardCatalog.Get(CardKind.Nullification).DisplayName),
            "The real WPF overview renders exactly the two authorized read-only card faces beside their recorded names and no hidden foreign pairing.");
        var retained = self.Entries[0].Card;
        Refresh(model, projected with { Players = projected.Players.Select(p => p with { NamedAmbushPiles = null }).ToArray() });
        Program.Render(root, 1120, 780, Path.Combine(output, "named-private-pile-cleared.png"));
        Program.Assert(!model.HasNamedPrivatePiles && model.NamedPrivatePiles.Count == 0 && overview.Visibility == Visibility.Collapsed &&
            model.Seats.All(s => !s.DeferredPileText.Contains("伏兵")) && !model.HumanSummary.Contains("伏兵") && !model.SelectRevealedCardCommand.CanExecute(retained) &&
            !model.HasCenterChoices && model.IsTableIdle && JsonSerializer.Serialize(game.CreateCheckpoint()) == checkpointBefore,
            "Removing or invalidating deposits clears every stale face, recorded name and badge; read-only snapshot refresh never pays cards or changes the command-driven game.");
        Refresh(model, original); window.Content = null; window.Close();
    }
    private static void Refresh(MainViewModel model, GameSnapshot snapshot) =>
        typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [snapshot]);
}
