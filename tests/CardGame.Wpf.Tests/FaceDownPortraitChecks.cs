using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.ViewModels;

internal static class FaceDownPortraitChecks
{
    public static void OverlayAndClearing(string output)
    {
        using var model = new MainViewModel(false, 17, true, new MemorySaveStore())
            { IsMotionEnabled = false, IsNewGameSetupOpen = false };
        Program.StartLordFixture(model);
        model.SelectGeneralChoiceCommand.Execute(model.GeneralChoices[0]);
        Program.AdvanceToDecision(model);
        var game = Program.Engine(model);
        var original = game.CreateSnapshot(0) with { PendingDecision = null };
        var checkpoint = JsonSerializer.Serialize(game.CreateCheckpoint());
        var window = new MainWindow(model) { ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            Refresh(original);
            Program.Render(root, 1120, 740, null);
            var portraits = Portraits();
            var originalBounds = portraits.ToDictionary(pair => pair.Key, pair => Bounds(pair.Value));
            var portraitBrushes = portraits.ToDictionary(pair => pair.Key,
                pair => Program.Find<System.Windows.Shapes.Rectangle>(pair.Value).Single(shape => shape.Name == "SinglePortrait").Fill);
            var faceDown = original with { Players = original.Players.Select(player =>
                player.Seat is 0 or 1 ? player with { IsFaceDown = true, IsChained = player.Seat == 1 } : player).ToArray() };
            Refresh(faceDown);
            model.Seats.Single(seat => seat.Seat == 1).IsSelectedTarget = true;
            foreach (var (width, height) in new[] { (1120, 740), (1440, 860) })
            {
                Program.Render(root, width, height, Path.Combine(output, $"face-down-portraits-{width}.png"));
                foreach (var (seat, portrait) in Portraits())
                {
                    var veil = Program.Find<Border>(portrait).Single(element => element.Name == "FaceDownVeil");
                    var label = Program.Find<TextBlock>(portrait).Single(element => element.Name == "FaceDownLabel");
                    var expected = seat is 0 or 1 ? Visibility.Visible : Visibility.Collapsed;
                    Program.Assert(veil.Visibility == expected && label.Visibility == expected &&
                        !veil.IsHitTestVisible && !label.IsHitTestVisible && label.Text == "翻面",
                        "Only face-down public snapshots must show the parchment veil and central label, with target input passing through.");
                    Program.Assert(ReferenceEquals(portraitBrushes[seat], Program.Find<System.Windows.Shapes.Rectangle>(portrait)
                        .Single(shape => shape.Name == "SinglePortrait").Fill), "Flipping must retain the original general artwork.");
                    if (width == 1120)
                        Program.Assert(ReferenceEquals(portrait, portraits[seat]) && Bounds(portrait) == originalBounds[seat],
                            "Flipping must retain the live portrait controls and their exact target bounds.");
                    if (expected != Visibility.Visible) continue;
                    var chain = Program.Find<ChainOverlay>(portrait).Single();
                    var health = Program.Find<Border>(portrait).Single(element => element.Name == "SeatHealthRail");
                    var hand = Program.Find<TextBlock>(portrait).Single(element => element.Name == "SeatHandCountText");
                    var snapshot = original.Players.Single(player => player.Seat == seat);
                    Program.Assert(chain.Visibility == (seat == 1 ? Visibility.Visible : Visibility.Collapsed) &&
                        (seat != 1 || !Bounds(chain).IntersectsWith(Bounds(label))) &&
                        health.Visibility == Visibility.Visible && System.Windows.Automation.AutomationProperties.GetName(health) == $"{Math.Max(0, snapshot.Hp)}/{snapshot.MaxHp}" &&
                        hand.Text == ((SeatViewModel)portrait.DataContext).HandBadgeText &&
                        Program.Find<Border>(portrait).Single(element => element.Name == "KingdomBadge").Visibility == Visibility.Visible &&
                        portrait.ToolTip is string details && details.Contains(((SeatViewModel)portrait.DataContext).EquipmentText),
                        "Face-down and chained states must coexist with readable health, hand count, faction and equipment hover details.");
                }
                Program.Assert(Program.Find<Border>(Portraits()[1]).Single(element => element.Name == "SeatSelectedOutline").Opacity == 1,
                    "The selected target outline must remain visible above the face-down portrait.");
            }

            Refresh(faceDown with { Players = faceDown.Players.Select(player =>
                player.Seat == 1 ? player with { IsAlive = false } : player).ToArray() });
            Program.Render(root, 1120, 740, null);
            var dead = Portraits()[1];
            Program.Assert(Program.Find<Border>(dead).Single(element => element.Name == "DeathShade").Visibility == Visibility.Visible &&
                Program.Find<Border>(dead).Single(element => element.Name == "FaceDownVeil").Visibility == Visibility.Collapsed &&
                Program.Find<TextBlock>(dead).Single(element => element.Name == "FaceDownLabel").Visibility == Visibility.Collapsed,
                "Death must take precedence over a retained face-down flag.");

            Refresh(original);
            Program.Render(root, 1120, 740, null);
            Program.Assert(Portraits().All(pair => ReferenceEquals(pair.Value, portraits[pair.Key])) &&
                Program.Find<Border>(root).Where(element => element.Name == "FaceDownVeil").All(element => element.Visibility == Visibility.Collapsed) &&
                Program.Find<TextBlock>(root).Where(element => element.Name == "FaceDownLabel").All(element => element.Visibility == Visibility.Collapsed) &&
                Program.Find<ChainOverlay>(root).All(chain => chain.VerticalAlignment == VerticalAlignment.Center && chain.Margin == new Thickness()) &&
                JsonSerializer.Serialize(game.CreateCheckpoint()) == checkpoint,
                "Turning face up must remove every overlay and restore chain placement without replacing portraits or mutating game state.");
        }
        finally { window.Content = null; window.Close(); }

        void Refresh(GameSnapshot snapshot) => typeof(MainViewModel)
            .GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [snapshot]);
        Dictionary<int, Button> Portraits() => Program.Find<Button>(root).Where(button => button.Name == "SeatButton")
            .ToDictionary(button => ((SeatViewModel)button.DataContext).Seat);
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
    }
}
