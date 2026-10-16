using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class PortraitPrivatePileChecks
{
    public static void ForeignBacksAndEverySeat(string output)
    {
        using var model = new MainViewModel(false, 17, false, new MemorySaveStore(), useExpandedContent: true,
            preferencesStore: new MemoryPlayerPreferencesStore()) { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        var game = Program.Engine(model);
        var original = game.CreateSnapshot(0);
        var checkpoint = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        Program.Assert(original.Players.Count == 8, "The portrait fixture must exercise an actual eight-seat table layout.");
        var view = original with
        {
            PendingDecision = null,
            Players = original.Players.Select(player =>
            {
                var stars = player.Seat % 2 != 0;
                var cards = Enumerable.Range(0, stars ? 7 : 4).Select(index => new CardSnapshot(
                    870000 + player.Seat * 10 + index, CardKind.Peach, Suit.Heart, index + 1,
                    player.Seat == 0 ? "拥有者的牌" : "他人不应可见的牌", (index + 1).ToString())).ToArray();
                return player with
                {
                    GeneralId = stars ? "ol:shen-zhuge-liang" : "boundary:lu-xun",
                    GeneralName = stars ? "神诸葛亮" : "界陆逊",
                    PrivateReserveCount = stars ? 7 : 0,
                    // Even a retained developer DTO containing foreign faces must render backs.
                    PrivateReserveCards = stars ? Array.AsReadOnly(cards) : null,
                    PrivateTurnHolds = stars ? null : Array.AsReadOnly(new[] { new PrivateTurnHoldSnapshot(
                        8700 + player.Seat, player.Seat, "boundary:qianxun", "fixture:portrait-hold", "fixture:portrait-hold",
                        original.TurnNumber, 4, Array.AsReadOnly(cards)) }),
                    HasAlcoholEffect = stars,
                    Markers = stars ? Array.AsReadOnly(new[] { new PlayerMarkerSnapshot(PlayerMarkerKind.Gale, "狂风", 1),
                        new PlayerMarkerSnapshot(PlayerMarkerKind.Mist, "大雾", 1) }) : null
                };
            }).ToArray()
        };
        Refresh(view);
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            foreach (var (width, height) in new[] { (1440, 920), (1120, 780) })
            {
                Render($"portrait-private-piles-{width}", width, height);
                Program.Assert(model.LeftSeats.Any(seat => seat.HasPrivateReserve) && model.RightSeats.Any(seat => seat.HasPrivateReserve) &&
                    model.TopSeats.Any(seat => seat.HasPrivateReserve), "The same Zhuge portrait pile must be exercised on the left, right and top of the real table.");
                foreach (var seat in model.Seats.Where(seat => !seat.IsHuman))
                {
                    var portrait = Find<Button>("SeatButton", seat.Seat);
                    var badge = Find<Button>(seat.HasPrivateReserve ? "PrivateReserveBadge" : "TurnHandHoldBadge", seat.Seat);
                    var portraitBounds = Bounds(portrait);
                    var badgeBounds = Bounds(badge);
                    Program.Assert(badge.IsEnabled && portraitBounds.Contains(badgeBounds) &&
                        badgeBounds.Top < portraitBounds.Top + portraitBounds.Height * .35 &&
                        !badgeBounds.IntersectsWith(Bounds(Find<TextBlock>("SeatVerticalName", seat.Seat))) &&
                        !badgeBounds.IntersectsWith(Bounds(Find<ItemsControl>("SeatHealthIcons", seat.Seat))),
                        $"Seat {seat.Seat} pile badge must remain in the upper portrait without covering its general name or health at {width}px.");
                    if (seat.HasPrivateReserve)
                    {
                        var gale = Find<Border>("GaleBadge", seat.Seat);
                        var mist = Find<Border>("MistBadge", seat.Seat);
                        var statuses = Find<ItemsControl>("SeatStatusBadges", seat.Seat);
                        Program.Assert(new Rect(0, 0, width, height).Contains(Bounds(gale)) &&
                            new Rect(0, 0, width, height).Contains(Bounds(mist)) &&
                            !Bounds(gale).IntersectsWith(Bounds(mist)) && !Bounds(gale).IntersectsWith(badgeBounds) &&
                            !Bounds(mist).IntersectsWith(badgeBounds) &&
                            (!seat.StatusBadgesOnLeft || !Bounds(statuses).IntersectsWith(Bounds(mist))),
                            $"Seat {seat.Seat} wind, mist, pile and other status badges must not overlap or clip at {width}px.");
                    }
                }
            }
            var own = model.Seats.Single(seat => seat.Seat == 0);
            var ownBadge = Find<Button>("TurnHandHoldBadge", 0);
            Program.Assert((string)ownBadge.Content == "谦逊[4]", "Qianxun's actual portrait entry must name its four held cards.");
            ownBadge.Command!.Execute(ownBadge.CommandParameter);
            Render("qianxun-owner-faces", 1120, 780);
            Program.Assert(model.ReserveDialogStars.Count == 4 && model.ReserveDialogBacks.Count == 0 &&
                !model.IsReserveExchange && model.ReserveDialogStars.All(card => !model.MoveReserveCardCommand.CanExecute(card)),
                "The owner sees exactly four read-only Qianxun faces, separate from hand and exchange actions.");
            model.CloseReserveDialogCommand.Execute(null);
            var previousDeveloper = typeof(MainViewModel).GetField("_isDeveloperView", BindingFlags.Instance | BindingFlags.NonPublic)!;
            previousDeveloper.SetValue(model, true);
            try
            {
                foreach (var seat in model.Seats.Where(seat => !seat.IsHuman))
                {
                    var command = seat.HasPrivateReserve ? model.OpenPrivateReserveCommand : model.OpenTurnHandHoldCommand;
                    command.Execute(seat);
                    Render(seat.HasPrivateReserve ? "qixing-foreign-backs" : "qianxun-foreign-backs", 1120, 780);
                    var modal = (Border)window.FindName("PrivateReserveOverlay");
                    var expected = seat.HasPrivateReserve ? 7 : 4;
                    Program.Assert(model.IsReserveDialogOpen && !model.IsReserveExchange && model.ReserveDialogStars.Count == 0 &&
                        model.ReserveDialogHand.Count == 0 && model.ReserveDialogBacks.Count == expected &&
                        model.ReserveDialogTitle == $"{seat.GeneralName} · {(seat.HasPrivateReserve ? "七星" : "谦逊")}" &&
                        Program.Find<Border>(modal).Count(border => border.Name == "PrivatePileCardBack") == expected &&
                        !Program.Find<TextBlock>(modal).Any(text => text.Text.Contains("他人不应可见的牌", StringComparison.Ordinal)),
                        "Viewing another seat must show its own pile title and exact back count without retaining foreign faces or entity IDs, including developer mode.");
                    model.CloseReserveDialogCommand.Execute(null);
                    Program.Assert(model.ReserveDialogBacks.Count == 0 && model.ReserveDialogStars.Count == 0,
                        "Closing a foreign pile withdraws both its opaque slots and any previous owner faces.");
                }
            }
            finally { previousDeveloper.SetValue(model, false); }
            Refresh(view with { Players = view.Players.Select(player => player with { PrivateTurnHolds = null,
                PrivateReserveCount = 0, PrivateReserveCards = null, Markers = null }).ToArray() });
            Render("portrait-private-piles-cleared", 1120, 780);
            Program.Assert(model.Seats.All(seat => !seat.HasTurnHandHold && !seat.HasPrivateReserve && !seat.HasGale && !seat.HasMist) &&
                !model.IsReserveDialogOpen && model.ReserveDialogBacks.Count == 0 &&
                GameCheckpointJson.Serialize(game.CreateCheckpoint()) == checkpoint,
                "Returning the held cards or consuming stars clears every portrait entry and stale view without changing the engine checkpoint.");

            T Find<T>(string name, int seat) where T : FrameworkElement => Program.Find<T>(root)
                .Single(element => element.Name == name && element.DataContext is SeatViewModel owner && owner.Seat == seat);
            Rect Bounds(FrameworkElement element) => element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            void Render(string name, int width, int height) => Program.Render(root, width, height, Path.Combine(output, name + ".png"));
        }
        finally { Refresh(original); window.Content = null; window.Close(); }

        void Refresh(GameSnapshot snapshot) => typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [snapshot]);
    }
}
