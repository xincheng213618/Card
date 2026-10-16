using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.ViewModels;

internal static class SeatEquipmentUiChecks
{
    public static void PublicEquipmentAndClearing(string output)
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
            var empty = original with { Players = original.Players.Select(player => player with { Equipment = [] }).ToArray() };
            Refresh(empty);
            Program.Render(root, 1120, 740, null);
            var boundsBefore = Portraits().ToDictionary(pair => pair.Key, pair => Bounds(pair.Value));
            CardSnapshot Card(int id, CardKind kind, Suit suit = Suit.Spade, int rank = 2) =>
                new(id, kind, suit, rank, EquipmentCatalog.Get(kind).DisplayName, rank == 1 ? "A" : rank.ToString());
            var five = new[] { Card(900001, CardKind.QinggangSword), Card(900002, CardKind.BaguaFormation),
                Card(900003, CardKind.OffensiveHorse, Suit.Heart, 5), Card(900004, CardKind.DefensiveHorse, Suit.Club, 5),
                Card(900005, CardKind.WoodenOx, Suit.Diamond, 5) };
            var equipped = empty with { CurrentSeat = 1, Players = empty.Players.Select(player => player with
            {
                Hp = player.Seat switch { 0 => 5, 1 => 1, 2 => 2, 3 => 3, 4 => 0, 6 => 6, _ => 4 },
                MaxHp = player.Seat switch { 0 => 6, 3 => 5, 4 => 3, 6 => 8, _ => 4 },
                HandLimit = player.Seat == 1 ? 4 : null,
                Equipment = player.Seat switch
                {
                    0 or 1 => five,
                    2 => [five[1], five[3]],
                    3 => [],
                    6 => [new(900006, CardKind.GeneralWeapon, Suit.None, 0, "张飞", "")],
                    _ => [five[0]]
                }
            }).ToArray() };
            Refresh(equipped);
            var retained = model.Seats.Single(seat => seat.Seat == 2);
            foreach (var (width, height) in new[] { (1120, 740), (1440, 860), (1920, 1080) })
            {
                Program.Render(root, width, height, Path.Combine(output, $"portrait-equipment-{width}.png"));
                foreach (var (seat, portrait) in Portraits())
                {
                    var player = equipped.Players.Single(player => player.Seat == seat);
                    var rail = Program.Find<ScrollViewer>(portrait).Single(control => control.Name == "SeatEquipmentRail");
                    var items = (ItemsControl)((Viewbox)rail.Content).Child;
                    var expected = !player.IsHuman && player.Equipment.Count > 0;
                    var seatModel = (SeatViewModel)portrait.DataContext;
                    var hand = Program.Find<TextBlock>(portrait).Single(text => text.Name == "SeatHandCountText");
                    var badge = Program.Find<Border>(portrait).Single(control => control.Name == "SeatHandBadge");
                    var name = Program.Find<Grid>(portrait).Single(control => control.Name == "SingleGeneralTitle");
                    var health = Program.Find<Border>(portrait).Single(control => control.Name == "SeatHealthRail");
                    var pips = Program.Find<ItemsControl>(portrait).Single(control => control.Name == "SeatHealthIcons");
                    var numeric = Program.Find<Viewbox>(portrait).Single(control => control.Name == "SeatNumericHealth");
                    var color = ((SolidColorBrush)seatModel.HealthBrush).Color;
                    Program.Assert(hand.Text == (player.Seat == 1 ? $"{player.HandCount}/4" : player.HandCount.ToString()) &&
                        (player.IsHuman || Bounds(badge).Top >= Bounds(name).Bottom) && !Bounds(badge).IntersectsWith(Bounds(health)),
                        "Hand badges belong below the name; only the current actor shows its hand limit.");
                    Program.Assert(pips.Items.Count == (player.MaxHp > 5 ? 1 : player.MaxHp) &&
                        numeric.Visibility == (player.MaxHp > 5 ? Visibility.Visible : Visibility.Collapsed) &&
                        color == (Color)ColorConverter.ConvertFromString(player.Hp <= 1 ? "#EF7264" : player.Hp == 2 ? "#EFC64E" : "#6FBD48") &&
                        Program.Find<Grid>(pips).Where(grid => grid.DataContext is string).All(grid => grid.ActualWidth >= 19 && grid.ActualHeight >= 22),
                        "Five or fewer HP slots use larger colored pips; larger maxima use one pip and exact numeric HP.");
                    if (player.MaxHp <= 5)
                        Program.Assert(pips.Items.Cast<string>().Count(path => path.EndsWith("gallery-hp-empty.png", StringComparison.Ordinal)) ==
                            player.MaxHp - Math.Clamp(player.Hp, 0, player.MaxHp) &&
                            (player.Hp <= 0 || pips.Items.Cast<string>().Last().EndsWith("gallery-hp-wu.png", StringComparison.Ordinal)),
                            "Remaining HP fills from the bottom, with lost HP above it.");
                    Program.Assert(rail.Visibility == (expected ? Visibility.Visible : Visibility.Collapsed) &&
                        items.Items.Count == player.Equipment.Count,
                        "Only another player's occupied public equipment belongs on its portrait; empty slots and the human rail stay separate.");
                    if (width == 1120)
                        Program.Assert(Bounds(portrait) == boundsBefore[seat], "Equipment must not move or resize portrait target bounds.");
                    if (!expected) continue;
                    var rows = Rows(rail);
                    Program.Assert(rows.Length == player.Equipment.Count && Bounds(portrait).Contains(Bounds(rail)) &&
                        rail.ScrollableHeight == 0 && rows.All(row => Bounds(rail).Contains(Bounds(row))),
                        "All five ordinary equipment slots must be visible inside the portrait without scrolling or clipping.");
                    Program.Assert(!Bounds(rail).IntersectsWith(Bounds(health)) && !Bounds(rail).IntersectsWith(Bounds(badge)) &&
                        !Bounds(rail).IntersectsWith(Bounds(pips)), "Equipment must leave HP, hand count and health icons readable.");
                    Program.Assert(Bounds(rail).Height <= Bounds(portrait).Height * .4 &&
                        items.Items.Cast<EquipmentSlotViewModel>().Select(slot => slot.Slot).SequenceEqual(player.Equipment
                            .OrderBy(card => EquipmentSlotViewModel.DisplayOrder(EquipmentCatalog.Get(card.Kind).Slot)).Select(card => EquipmentCatalog.Get(card.Kind).Slot)),
                        "The compact equipment area keeps all five slots in weapon, armor, +1, -1 and treasure order.");
                    foreach (var row in rows)
                    {
                        var slot = (EquipmentSlotViewModel)row.DataContext;
                        Program.Assert(slot.Artwork is not null && row.ToolTip is string tooltip && tooltip.Contains(slot.Name) &&
                            tooltip.Contains(EquipmentCatalog.Get(slot.Card!.Kind).Description),
                            "Every equipment strip must expose its current public card's full effect on hover.");
                    }
                }
            }

            Refresh(equipped with { CurrentSeat = 2, Players = equipped.Players.Select(player => player with
                { HandLimit = player.Seat == 2 ? 2 : null }).ToArray() });
            Program.Render(root, 1120, 740, null);
            Program.Assert(ReferenceEquals(retained, model.Seats.Single(seat => seat.Seat == 2)) && retained.HandBadgeText == $"{retained.HandCount}/2" &&
                model.Seats.Single(seat => seat.Seat == 1).HandBadgeText == equipped.Players[1].HandCount.ToString(),
                "Turn changes must move the limit readout on existing portrait controls without leaving stale ratios.");
            Refresh(equipped);

            window.Show();
            Program.Render(root, 1120, 740, null);
            foreach (var portrait in Portraits().Values.Where(button => ((SeatViewModel)button.DataContext).ShowPortraitEquipment))
            foreach (var row in Rows(portrait))
            {
                var rect = Bounds(row);
                var hit = root.InputHitTest(new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2)) as DependencyObject;
                var hitName = hit is FrameworkElement element ? $"{element.GetType().Name}/{element.Name}" : hit?.GetType().Name;
                while (hit is not null && hit != row) hit = VisualTreeHelper.GetParent(hit);
                Program.Assert(hit == row && ReferenceEquals(portrait.Command, model.SelectTargetCommand),
                    $"Equipment must receive tooltip hover inside the portrait target button: {((EquipmentSlotViewModel)row.DataContext).Name}, hit {hitName}.");
            }

            // Same name, different physical suit/rank must invalidate the old strip too.
            var replacement = Card(900007, CardKind.BaguaFormation, Suit.Heart, 1);
            Refresh(equipped with { Players = equipped.Players.Select(player => player.Seat == 2
                ? player with { Equipment = [replacement] } : player).ToArray() });
            Program.Render(root, 1120, 740, null);
            var changedRail = Program.Find<ScrollViewer>(Portraits()[2]).Single(control => control.Name == "SeatEquipmentRail");
            var changed = (EquipmentSlotViewModel)Rows(changedRail).Single().DataContext;
            Program.Assert(ReferenceEquals(retained, model.Seats.Single(seat => seat.Seat == 2)) &&
                changed.Card == replacement && changed.Tooltip.Contains("♥A") && changed.SuitArtwork is not null && changed.RankArtwork is not null,
                "Equipment replacement must update the retained seat's card face, suit, rank and tooltip and remove the old horse.");

            Refresh(empty with { Players = empty.Players.Select(player => player.Seat == 1
                ? player with { Equipment = five.Prepend(Card(900008, CardKind.QilinBow)).ToArray() } : player).ToArray() });
            Program.Render(root, 1120, 740, null);
            var overflow = Program.Find<ScrollViewer>(Portraits()[1]).Single(control => control.Name == "SeatEquipmentRail");
            Program.Assert(Rows(overflow).Length == 6 && overflow.ScrollableHeight > 0 && Bounds(Portraits()[1]).Contains(Bounds(overflow)),
                "Additional weapon slots must remain scrollable within the existing portrait.");
            overflow.ScrollToBottom();
            root.UpdateLayout();
            Program.Assert(overflow.VerticalOffset > 0, "The final equipment in an expanded slot must remain reachable.");

            Refresh(empty);
            Program.Render(root, 1120, 740, null);
            Program.Assert(model.Seats.All(seat => seat.EquipmentSlots.Count == 0 && !seat.ShowPortraitEquipment) &&
                Program.Find<ScrollViewer>(root).Where(control => control.Name == "SeatEquipmentRail")
                    .All(control => control.Visibility == Visibility.Collapsed && Rows(control).Length == 0) &&
                JsonSerializer.Serialize(game.CreateCheckpoint()) == checkpoint,
                "Losing equipment must clear every stale portrait strip without mutating command-driven game state.");
        }
        finally { window.Content = null; window.Close(); }

        void Refresh(GameSnapshot snapshot) => typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(model, [snapshot]);
        Dictionary<int, Button> Portraits() => Program.Find<Button>(root).Where(button => button.Name == "SeatButton")
            .ToDictionary(button => ((SeatViewModel)button.DataContext).Seat);
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        static Grid[] Rows(DependencyObject rail) => Program.Find<Grid>(rail)
            .Where(grid => grid.DataContext is EquipmentSlotViewModel && grid.ToolTip is string).ToArray();
    }
}
