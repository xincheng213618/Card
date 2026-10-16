using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.ViewModels;

internal static class IdentityMarkUiChecks
{
    public static void LocalChoicesAndPublicBoundaries(string output)
    {
        using var model = new MainViewModel(false, 17, true, new MemorySaveStore())
            { IsAutoAdvance = false, IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        Program.StartLordFixture(model);
        model.SelectGeneralChoiceCommand.Execute(model.GeneralChoices[0]);
        Program.AdvanceToDecision(model);
        var game = Program.Engine(model);
        var original = game.CreateSnapshot(0);
        var checkpoint = JsonSerializer.Serialize(game.CreateCheckpoint());
        var views = Enumerable.Range(0, original.Players.Count).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
        var window = new MainWindow(model) { ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        var root = (FrameworkElement)window.Content;
        try
        {
            window.Show();
            Program.Render(root, 1120, 740, null);
            var seat = model.Seats.First(item => item.CanMarkIdentity);
            var marker = Marker(seat);
            foreach (var (width, height) in new[] { (1120, 740), (1440, 860) })
            {
                Program.Render(root, width, height, Path.Combine(output, $"identity-mark-layout-{width}.png"));
                var bounds = marker.TransformToVisual(root).TransformBounds(new Rect(marker.RenderSize));
                var hit = root.InputHitTest(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2)) as DependencyObject;
                while (hit is not null && hit != marker && hit is not Button) hit = VisualTreeHelper.GetParent(hit);
                Program.Assert(hit == marker && marker.ActualWidth >= 17 && marker.ActualHeight >= 18 &&
                    !Program.Find<Button>(root).Where(button => button.Name == "SeatButton")
                        .Any(button => marker.IsDescendantOf(button)),
                    $"The top-right identity badge must receive its own pointer input outside the portrait's target button: bounds={bounds}, size={marker.ActualWidth}x{marker.ActualHeight}, visible={marker.IsVisible}, enabled={marker.IsEnabled}, hit={hit?.GetType().Name}/{(hit as FrameworkElement)?.Name}, lobby={model.IsNewGameSetupOpen}, reveal={model.IsIdentityRevealOpen}, deal={model.IsOpeningDealVisible}.");
            }

            Invoke(marker);
            var menu = marker.ContextMenu!;
            Program.Assert(menu.IsOpen && ReferenceEquals(menu.PlacementTarget, marker) &&
                menu.Items.Cast<MenuItem>().Select(item => (string)item.Header).SequenceEqual(new[] { "忠臣", "反贼", "内奸", "清除标记" }),
                "Clicking the actual unknown identity button must open all three local guesses plus clearing.");
            Program.Render(menu, 150, 140, Path.Combine(output, "identity-mark-menu.png"));
            Choose("反贼");
            Program.Render(root, 1440, 860, Path.Combine(output, "identity-mark-rebel.png"));
            Program.Assert(seat.IdentityMark == "反贼" && seat.RoleBadgeText == "反贼" && seat.RoleLabel == "?" && !menu.IsOpen &&
                marker.ToolTip is string tooltip && tooltip.Contains("仅本机可见"),
                "A local guess must update the real badge without rewriting the projected hidden role.");
            Refresh(original);
            Program.Render(root, 1120, 740, null);
            Program.Assert(ReferenceEquals(seat, model.Seats.Single(item => item.Seat == seat.Seat)) && seat.RoleBadgeText == "反贼",
                "Ordinary view refresh must retain a personal mark on the same live seat.");
            Invoke(marker); Choose("忠臣");
            Program.Assert(seat.IdentityMark == "忠臣", "A mark must be changeable.");
            Invoke(marker); Choose("清除标记");
            Program.Assert(seat.IdentityMark is null && seat.RoleBadgeText == "?", "Clearing must restore the unknown identity badge.");

            var card = model.Hand.First(item => item.IsPlayable);
            model.SelectCardCommand.Execute(card);
            Program.Assert(model.HasSelection, "The fixed fixture must have a selected hand draft.");
            marker.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
                { RoutedEvent = Mouse.PreviewMouseDownEvent });
            Program.Assert(model.HasSelection && card.IsSelected, "Right-clicking an identity must preserve the current card draft.");
            marker.Focus();
            Program.Assert(Keyboard.FocusedElement == marker && (bool)typeof(MainWindow).GetMethod("HandleKeyInput", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, [Key.Enter, ModifierKeys.None, false])! && menu.IsOpen,
                "Enter on the focused identity button must open its menu instead of confirming the card draft.");
            Choose("内奸");
            Program.Assert(card.IsSelected && JsonSerializer.Serialize(game.CreateCheckpoint()) == checkpoint &&
                Enumerable.Range(0, original.Players.Count).All(viewer => views[viewer] == SnapshotJson.Serialize(game.CreateSnapshot(viewer))),
                "Marking and opening the menu must not submit commands, mutate any viewer snapshot or change the rules checkpoint.");

            var retiredRevealChoice = seat.IdentityMarkChoices[0];
            Invoke(marker);
            Refresh(original with { Players = original.Players.Select(player => player.Seat == seat.Seat
                ? player with { Role = Role.Rebel, IsAlive = false } : player).ToArray() });
            Program.Render(root, 1120, 740, null);
            retiredRevealChoice.Command.Execute(null);
            Program.Assert(seat.RoleBadgeText == "反贼" && seat.IdentityMark is null && !seat.CanMarkIdentity &&
                !retiredRevealChoice.Command.CanExecute(null) && marker.Visibility == Visibility.Collapsed && !menu.IsOpen,
                "A revealed or dead role must supersede a guess, close its menu and reject retired choices.");
            Refresh(original);
            Program.Render(root, 1120, 740, null);
            model.SaveGameCommand.Execute(null);
            Invoke(marker); Choose("忠臣");
            var retiredLoadChoice = seat.IdentityMarkChoices[1];
            Invoke(marker);
            model.LoadManualGameCommand.Execute(null);
            Program.Render(root, 1120, 740, null);
            retiredLoadChoice.Command.Execute(null);
            Program.Assert(!model.HasSaveError && model.Seats.All(item => item.IdentityMark is null) && !retiredLoadChoice.Command.CanExecute(null),
                "Loading a game must clear personal guesses and invalidate menus from the replaced session.");
            seat = model.Seats.First(item => item.CanMarkIdentity);
            marker = Marker(seat);
            Invoke(marker); Choose("反贼");
            var retiredNewChoice = seat.IdentityMarkChoices[2];
            Invoke(marker);
            model.StartNewGameCommand.Execute(null);
            retiredNewChoice.Command.Execute(null);
            Program.Assert(model.Seats.All(item => item.IdentityMark is null) && !retiredNewChoice.Command.CanExecute(null),
                "A new match must not inherit a previous seat's guesses or accept its retired menu commands.");
            foreach (var protectedSeat in model.Seats.Where(item => item.IsHuman || item.RoleLabel != "?"))
                Program.Assert(!protectedSeat.CanMarkIdentity, "Known roles and the human role must not be editable.");
            var protectedUnknown = UnknownSeat();
            protectedUnknown.IsNationalSeat = true;
            Program.Assert(!protectedUnknown.CanMarkIdentity, "National faction relationships are not identity guesses.");
            protectedUnknown.IsNationalSeat = false;
            protectedUnknown.TeamId = "team:blue";
            Program.Assert(!protectedUnknown.CanMarkIdentity, "Published teams are not editable hidden identities.");

            IdentityMarkButton Marker(SeatViewModel current) => Program.Find<IdentityMarkButton>(root)
                .Single(button => ReferenceEquals(button.DataContext, current));
            void Refresh(GameSnapshot snapshot) => typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(model, [snapshot]);
            void Choose(string label)
            {
                var item = marker.ContextMenu!.Items.Cast<MenuItem>().Single(choice => (string)choice.Header == label);
                var peer = new MenuItemAutomationPeer(item);
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
                root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            }
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void Invoke(IdentityMarkButton marker)
    {
        var peer = new ButtonAutomationPeer(marker);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
        marker.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static SeatViewModel UnknownSeat() => new()
    {
        Seat = 1, Name = "未知角色", Kingdom = "魏", RoleLabel = "?", HpText = "体力 4/4", HandText = "手牌 0",
        EquipmentText = "装备 —", JudgmentText = "判定区 —", DistanceText = "距你 1", SkillText = "技能",
        IsAlive = true, IsCurrent = false, IsHuman = false
    };
}
