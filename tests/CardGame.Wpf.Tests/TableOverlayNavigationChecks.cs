using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TableOverlayNavigationChecks
{
    public static void ModalsCoverElevatedBadges(string output)
    {
        using var model = new MainViewModel(false, 721019, showSetup: false, saveStore: new MemorySaveStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        Program.StartLordFixture(model);
        model.SelectGeneralChoiceCommand.Execute(model.GeneralChoices[0]);
        Program.AdvanceToDecision(model);
        var host = new NativeResponseStatusChecks.NativeHost(model);
        var window = host.Window;
        var root = (FrameworkElement)window.Content;
        try
        {
            var tactics = (ScrollViewer)window.FindName("HumanWushuangTactics");
            tactics.Visibility = Visibility.Visible;
            Program.Render(root, 1120, 740, null);
            Program.Find<ItemsControl>(tactics).Single().ItemsSource =
                WushuangTactics.All.Take(12).Select(tactic => new WushuangTacticViewModel(tactic.Id)).ToArray();
            var rails = new UIElement[] { tactics, (UIElement)window.FindName("HumanJudgmentStatuses"),
                (UIElement)window.FindName("HumanStatusBadges") };
            var overlays = new[] { "OpeningDealOverlay", "PrivateReserveOverlay", "GeneralSelectionOverlay",
                "MatchResultOverlay", "BattleLogOverlay" }.Select(name => (Border)window.FindName(name)).ToArray();
            foreach (var overlay in overlays) overlay.Visibility = Visibility.Collapsed;

            foreach (var (width, height) in new[] { (1120, 740), (1440, 880) })
            {
                root.Width = width;
                root.Height = height;
                window.SizeToContent = SizeToContent.WidthAndHeight;
                Program.Render(root, width, height, null);
                var icon = Program.Find<System.Windows.Shapes.Ellipse>(tactics).Last();
                var point = icon.TranslatePoint(new Point(icon.ActualWidth / 2, icon.ActualHeight / 2), root);
                RequireHit(tactics, "Uncovered tactic badges must remain reachable.");
                foreach (var overlay in overlays)
                {
                    Program.Assert(rails.All(rail => ReferenceEquals(VisualTreeHelper.GetParent(rail), overlay.Parent) &&
                        Panel.GetZIndex(overlay) > Panel.GetZIndex(rail)),
                        $"{overlay.Name} must draw above all elevated table rails.");
                    overlay.Visibility = Visibility.Visible;
                    Program.Render(root, width, height, overlay.Name == "MatchResultOverlay"
                        ? Path.Combine(output, $"table-result-covers-tactics-{width}.png") : null);
                    RequireHit(overlay, $"{overlay.Name} must cover tactic artwork and intercept its pointer input.");
                    overlay.Visibility = Visibility.Collapsed;
                }
                var result = overlays.Single(overlay => overlay.Name == "MatchResultOverlay");
                var log = overlays.Single(overlay => overlay.Name == "BattleLogOverlay");
                result.Visibility = log.Visibility = Visibility.Visible;
                Program.Render(root, width, height, null);
                RequireHit(log, "The battle log must still open above the match result.");
                log.Visibility = Visibility.Collapsed;
                RequireHit(result, "Closing the battle log must uncover the match result.");
                result.Visibility = Visibility.Collapsed;
                RequireHit(tactics, "Retiring the overlays must restore tactic badge input.");

                void RequireHit(FrameworkElement expected, string message)
                {
                    root.UpdateLayout();
                    var hit = root.InputHitTest(point) as DependencyObject;
                    Program.Assert(hit is not null && (ReferenceEquals(expected, hit) || expected.IsAncestorOf(hit)),
                        $"{message} Hit: {hit}, point: {point}.");
                }
            }
        }
        finally { host.Dispose(); }
    }

    [DllImport("user32.dll")] private static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint handle, int index, int value);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint SetFocus(nint handle);
    [DllImport("user32.dll")] private static extern nint GetFocus();
}
