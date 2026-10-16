using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.ViewModels;

internal static class SeatStatusUiChecks
{
    public static void PublicStatusesFollowRecipientsAndClear(string output)
    {
        using var model = new MainViewModel(false, 17, true, new MemorySaveStore())
            { IsMotionEnabled = false, IsNewGameSetupOpen = false };
        Program.StartLordFixture(model);
        model.SelectGeneralChoiceCommand.Execute(model.GeneralChoices[0]);
        Program.AdvanceToDecision(model);
        var game = Program.Engine(model);
        var original = game.CreateSnapshot(0);
        var checkpoint = JsonSerializer.Serialize(game.CreateCheckpoint());
        var window = new MainWindow(model) { ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            Refresh(model, original with { PendingDecision = null });
            Program.Render(root, 1120, 740, null);
            var before = Portraits().ToDictionary(pair => pair.Key, pair => Bounds(pair.Value));
            var hiddenCard = new CardSnapshot(675299, CardKind.Peach, Suit.Heart, 7, "不可泄露的牌面", "7");
            var projected = original with
            {
                PendingDecision = null,
                Players = original.Players.Select(player => player.Seat switch
                {
                    0 => player with { Markers = [new(PlayerMarkerKind.Rage, "暴怒", 2), new(PlayerMarkerKind.Zi, "辎", 1)], IsChained = true, IsFaceDown = true,
                        Judgment = [new CardSnapshot(675298, CardKind.Indulgence, Suit.Heart, 6, "乐不思蜀", "6"),
                            new CardSnapshot(675297, CardKind.SupplyShortage, Suit.Club, 4, "兵粮寸断", "4"),
                            new CardSnapshot(675296, CardKind.Lightning, Suit.Spade, 1, "闪电", "A")] },
                    1 => player with { Markers = [new(PlayerMarkerKind.Lie, "烈", 1), new(PlayerMarkerKind.Jue, "爵", 3)], IsChained = true },
                    3 => player with { Markers = [new(PlayerMarkerKind.Lie, "烈", 0)] },
                    5 => player with
                    {
                        Markers = [new(PlayerMarkerKind.Lie, "烈", 1), new(PlayerMarkerKind.Ren, "忍", 3), new(PlayerMarkerKind.Nightmare, "梦魇", 2),
                            new(PlayerMarkerKind.Rage, "暴怒", 2), new(PlayerMarkerKind.Zi, "辎", 1)],
                        IsChained = true, IsFaceDown = true, HasAlcoholEffect = true,
                        NamedAmbushPiles = [new("fixture:status-pile", "伏兵", 3, [new(hiddenCard, CardKind.Slash)])]
                    },
                    _ => player with { Markers = [new(PlayerMarkerKind.Lie, "烈", player.Seat == 2 ? 2 : 1)], IsChained = player.Seat == 1 }
                }).ToArray()
            };
            Refresh(model, projected);
            Program.Assert(model.HumanPlayer is { IsChained: true, IsFaceDown: true } &&
                model.Seats.Single(seat => seat.Seat == 5) is { IsChained: true, IsFaceDown: true } &&
                model.Seats.SelectMany(seat => seat.StatusBadges).All(status => status.Label is not ("连环" or "翻面")),
                "Chained and face-down flags must remain available to the portrait UI without duplicate badges on local or other seats.");
            var retainedSeat = model.Seats.Single(seat => seat.Seat == 2);
            Program.Assert(model.Seats.Where(seat => seat.StatusBadges.Any(status => status.Label == "烈"))
                    .Select(seat => seat.Seat).Order().SequenceEqual(new[] { 1, 2, 4, 5, 6, 7 }) &&
                retainedSeat.StatusBadges.Single().Count == 2 && !model.Seats.Single(seat => seat.Seat == 3).HasStatusBadges &&
                model.Seats.Single(seat => seat.Seat == 5).VisibleStatusBadges is { Count: 5 } overflow &&
                overflow[^1].Label == "+3" && overflow[^1].Tooltip.Contains("伏兵") &&
                model.Seats.SelectMany(seat => seat.StatusBadges).All(status => !status.Tooltip.Contains(hiddenCard.DisplayName)),
                "Public marks belong to their recipients, preserve independent counts, ignore zero marks and fold excess states without exposing private faces.");

            foreach (var (width, height) in new[] { (1120, 740), (1440, 860), (1920, 1080) })
            {
                Program.Render(root, width, height, Path.Combine(output, $"seat-status-badges-{width}.png"));
                foreach (var (seatNumber, portrait) in Portraits())
                {
                    var seat = (SeatViewModel)portrait.DataContext;
                    var rail = Program.Find<ItemsControl>(portrait).Single(items => items.Name == "SeatStatusBadges");
                    Program.Assert(rail.Visibility == (seat.ShowPortraitStatuses ? Visibility.Visible : Visibility.Collapsed),
                        "Only another seat's active statuses belong on its portrait; the human uses the hand-dock rail.");
                    if (width == 1120)
                        Program.Assert(Bounds(portrait) == before[seatNumber], "Adding statuses must preserve portrait layout and target bounds.");
                    if (!seat.ShowPortraitStatuses) continue;
                    var portraitBounds = Bounds(portrait);
                    var badges = Program.Find<Grid>(rail).Where(grid => grid.Name == "SeatStatusBadge").ToArray();
                    Program.Assert(badges.Length == seat.VisibleStatusBadges.Count && badges.Length <= 5,
                        "The real XAML must show one independent round badge for each visible public state.");
                    var expectedLeft = model.RightSeats.Contains(seat);
                    foreach (var badge in badges)
                    {
                        var badgeBounds = Bounds(badge);
                        Program.Assert(badgeBounds.Left >= 0 && badgeBounds.Top >= 0 && badgeBounds.Right <= width && badgeBounds.Bottom <= height &&
                            (expectedLeft ? badgeBounds.Right <= portraitBounds.Left + portraitBounds.Width * .05
                                : badgeBounds.Left >= portraitBounds.Right - portraitBounds.Width * .05),
                            $"Seat {seatNumber} status badges must sit outside its portrait toward available space at {width}x{height}: {badgeBounds}, portrait {portraitBounds}.");
                        Program.Assert(badge.ToolTip is string { Length: > 0 }, "Each public status must retain its full tooltip.");
                    }
                }
                var localRail = (ItemsControl)window.FindName("HumanStatusBadges");
                var localBounds = Bounds(localRail);
                var handDock = Bounds((FrameworkElement)window.FindName("HandDock"));
                var humanPortrait = Bounds(Portraits()[0]);
                var sidebar = Bounds((FrameworkElement)window.FindName("TableSidebar"));
                var judgments = Bounds((FrameworkElement)window.FindName("HumanJudgmentStatuses"));
                Program.Assert(judgments.Left >= handDock.Left && judgments.Right < handDock.Left + handDock.Width * .25 &&
                    judgments.Bottom <= handDock.Top && Math.Abs(judgments.Top - localBounds.Top) < 1 &&
                    ((ItemsControl)window.FindName("HumanJudgmentStatuses")).Items.Count == 3,
                    "Local Lightning, Indulgence and Supply Shortage must share the left edge above the hand dock while active states stay on the right.");
                var rageBadge = Program.Find<Grid>(localRail).Single(grid => grid.Name == "SeatStatusBadge" &&
                    grid.DataContext is SeatStatusViewModel { Label: "怒" });
                var ziBadge = Program.Find<Grid>(localRail).Single(grid => grid.Name == "SeatStatusBadge" &&
                    grid.DataContext is SeatStatusViewModel { Label: "辎" });
                Program.Assert(localRail.Visibility == Visibility.Visible && localRail.Items.Count == 2 &&
                    localBounds.Bottom <= handDock.Top + 2 && localBounds.Top >= handDock.Top - 76 &&
                    localBounds.Right <= sidebar.Left && localBounds.Right >= sidebar.Left - 10 &&
                    !localBounds.IntersectsWith(judgments) && !localBounds.IntersectsWith(sidebar) &&
                    Program.Find<TextBlock>(rageBadge).Single(text => text.Name == "StatusCountText").Text == "2" &&
                    Program.Find<Border>(rageBadge).Single(border => border.Name == "StatusCountBadge").Visibility == Visibility.Visible,
                    $"The human's 怒² must sit at the play area's upper-right edge, clear of judgments and sidebar controls at {width}x{height}: {localBounds}, dock {handDock}, sidebar {sidebar}, judgments {judgments}, count {localRail.Items.Count}, rage {Program.Find<TextBlock>(rageBadge).Single(text => text.Name == "StatusCountText").Text}/{Program.Find<Border>(rageBadge).Single(border => border.Name == "StatusCountBadge").Visibility}.");
                Program.Assert(ziBadge.DataContext is SeatStatusViewModel { IsDrawPhaseBonus: true, HasCount: false } zi &&
                    zi.Tooltip.Contains("截辎") && zi.Tooltip.Contains("额外的摸牌阶段") &&
                    Program.Find<Border>(ziBadge).Single(border => border.Name == "StatusCountBadge").Visibility == Visibility.Collapsed &&
                    Program.Find<Border>(ziBadge).Single(border => border.Name == "StatusBadgeSurface").Background is SolidColorBrush { Color: var color } &&
                    color == Color.FromRgb(0x17, 0x29, 0x54),
                    "Jiezi must show a blue 辎 state beside the local counters without a numeric badge.");
                var jueBadge = Program.Find<Grid>(Portraits()[1]).Single(grid => grid.Name == "SeatStatusBadge" &&
                    grid.DataContext is SeatStatusViewModel { Label: "爵" });
                Program.Assert(Program.Find<TextBlock>(jueBadge).Single(text => text.Name == "StatusCountText").Text == "3" &&
                    Program.Find<Border>(jueBadge).Single(border => border.Name == "StatusCountBadge").Visibility == Visibility.Visible &&
                    Program.Find<Border>(jueBadge).Single(border => border.Name == "StatusBadgeSurface").Background is SolidColorBrush { Color: var titleColor } &&
                    titleColor == Color.FromRgb(0x31, 0x20, 0x46), "Another player's 爵³ must retain its purple round mark and exact count on the portrait edge.");
            }

            window.Show();
            root.UpdateLayout();
            foreach (var badge in Program.Find<Grid>(root).Where(grid => grid.Name == "SeatStatusBadge" && grid.IsVisible))
            {
                var bounds = Bounds(badge);
                var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                var hit = root.InputHitTest(center) as DependencyObject;
                var hitDescription = hit is FrameworkElement element ? $"{element.GetType().Name}/{element.Name}" : hit?.GetType().Name;
                while (hit is not null && hit != badge) hit = VisualTreeHelper.GetParent(hit);
                Program.Assert(hit == badge, $"Exterior badges must receive pointer hover for their tooltip: {((SeatStatusViewModel)badge.DataContext).Label}, hit {hitDescription}.");
            }

            Refresh(model, projected with { Players = projected.Players.Select(player => player.Seat switch
                {
                    0 => player with { Markers = [new(PlayerMarkerKind.Rage, "暴怒", 1), new(PlayerMarkerKind.Zi, "辎", 1)] },
                    2 => player with { Markers = [new(PlayerMarkerKind.Lie, "烈", 1)] },
                    _ => player
                }).ToArray() });
            Program.Render(root, 1120, 740, null);
            Program.Assert(ReferenceEquals(retainedSeat, model.Seats.Single(seat => seat.Seat == 2)) &&
                retainedSeat.StatusBadges.Single() is { Count: 1, HasCount: false },
                "A consumed shield must update the existing seat and retire its stale count indicator.");
            var humanRail = (ItemsControl)window.FindName("HumanStatusBadges");
            var remainingRage = Program.Find<Grid>(humanRail).Single(grid => grid.Name == "SeatStatusBadge" &&
                grid.DataContext is SeatStatusViewModel { Label: "怒" });
            Program.Assert(model.HumanPlayer!.StatusBadges.Single(status => status.Label == "怒") is { Count: 1, HasCount: true } &&
                Program.Find<TextBlock>(remainingRage).Single(text => text.Name == "StatusCountText").Text == "1" &&
                Program.Find<Border>(remainingRage).Single(border => border.Name == "StatusCountBadge").Visibility == Visibility.Visible,
                "The local counter must update from 2 to 1 and keep the last unit visible in actual XAML.");
            Refresh(model, original with { PendingDecision = null });
            Program.Render(root, 1120, 740, null);
            Program.Assert(model.Seats.All(seat => !seat.HasStatusBadges) &&
                Program.Find<ItemsControl>(root).Where(items => items.Name == "SeatStatusBadges")
                    .All(items => items.Visibility == Visibility.Collapsed && items.Items.Count == 0) &&
                humanRail.Visibility == Visibility.Collapsed && humanRail.Items.Count == 0 &&
                JsonSerializer.Serialize(game.CreateCheckpoint()) == checkpoint,
                "Cleared snapshots must remove every stale badge without changing command-driven game state.");
        }
        finally { window.Content = null; window.Close(); }

        Dictionary<int, Button> Portraits() => Program.Find<Button>(root)
            .Where(button => button.Name == "SeatButton").ToDictionary(button => ((SeatViewModel)button.DataContext).Seat);
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
    }

    private static void Refresh(MainViewModel model, GameSnapshot snapshot) =>
        typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [snapshot]);
}
