using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class BattleLogUiChecks
{
    public static void SeatsFilterNamedHistoryAndKeepStyledScrolling(string output)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 8, HumanSeat = 0, HumanRole = Role.Rebel,
            UseInteractiveSetup = false, AdvanceAfterHumanCommands = false
        }, ContentRegistry.Build(new StandardContentPackage()));
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "The log fixture must start.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, 31, true, store,
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError && model.HumanPlayer?.Seat == 0, model.SaveStatus);
        var actors = model.Seats.OrderBy(seat => seat.DisplaySeatNumber).ToArray();
        var human = actors.Single(seat => seat.IsHuman);
        Program.Assert(human.DisplaySeatNumber != 1, "The fixture must cover a human displayed away from the first seat.");
        var opponents = actors.Where(seat => !seat.IsHuman).ToArray();
        var source = opponents[0];
        var target = opponents[1];
        var provider = opponents[2];
        var playerNames = Program.Engine(model).CreateSnapshot(0).Players.ToDictionary(player => player.Seat, player => player.Name);
        var next = model.FilteredBattleLog.Max(entry => entry.Sequence) + 1;
        var first = next;
        var addLog = typeof(MainViewModel).GetMethod("OnLogAdded", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Add("CardUsed", $"{Name(source)} 对 {Name(target)} 使用【杀】。", source.Seat, target.Seat);
        Add("Damage", $"{Name(human)} 受到 {Name(source)} 造成的 1 点伤害。", source.Seat, human.Seat);
        Add("SkillTriggered", $"{Name(provider)} 响应【激将】，由 {Name(source)} 对 {Name(target)} 使用【杀】。", source.Seat, target.Seat);
        Add("Rules", "【你】与【AI 2】是原文中的标题。");

        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            Program.Render(root, 1120, 780, Path.Combine(output, "battle-log-all-seats.png"));
            var filters = (ItemsControl)window.FindName("TableBattleLogSeats");
            var log = (ListBox)window.FindName("TableBattleLog");
            Program.Assert(ReferenceEquals(filters.ItemsSource, model.BattleLogSeats) &&
                ReferenceEquals(log.ItemsSource, model.FilteredBattleLog), "The table sidebar must expose the shared seat filters and public log.");
            Program.Assert(model.BattleLogSeats.Take(9).Select(option => option.Name)
                .SequenceEqual(new[] { "全部" }.Concat(Enumerable.Range(1, 8).Select(number => $"{number}号位"))),
                "All seats must follow 全部 in the table's displayed order, including the human away from the first position.");
            var visibleButtons = Program.Find<Button>(filters).Where(button => button.Visibility == Visibility.Visible).ToArray();
            Program.Assert(visibleButtons.Select(button => button.Content).SequenceEqual(new[] { "全", "主", "二", "三", "四", "五", "六", "七", "八" }) && visibleButtons.All(button =>
                button.TranslatePoint(new Point(), filters).X + button.ActualWidth <= filters.ActualWidth + 1) &&
                visibleButtons.All(button => Math.Abs(button.TranslatePoint(new Point(), filters).Y - visibleButtons[0].TranslatePoint(new Point(), filters).Y) < 1) &&
                visibleButtons.Skip(1).Select(button => button.ToolTip).SequenceEqual(Enumerable.Range(1, 8).Select(number => $"{number}号位")),
                "The narrow sidebar must fit all nine compact tabs on one line and expose the full seat number on hover.");
            var sidebar = (FrameworkElement)window.FindName("TableSidebar");
            Program.Assert(visibleButtons[0].TranslatePoint(new Point(), sidebar).X >= 12 &&
                Program.Find<TextBlock>(log).Where(text => text.DataContext is BattleLogEntryViewModel)
                    .All(text => text.TranslatePoint(new Point(), sidebar).X >= 12),
                "Seat tabs and log text must leave a visible inset from the sidebar frame.");
            var scroller = Program.Find<ScrollViewer>(log).Single();
            var scrollbar = Program.Find<ScrollBar>(log).Single(bar => bar.Orientation == Orientation.Vertical);
            var track = (Track)scrollbar.Template.FindName("PART_Track", scrollbar);
            Program.Assert(scrollbar.ActualWidth <= 10 && track.Thumb is not null && scroller.ScrollableHeight > 0,
                $"The log scrollbar must use a narrow draggable track with overflowing public history (width={scrollbar.ActualWidth}, thumb={track.Thumb is not null}, scrollable={scroller.ScrollableHeight}).");
            scroller.ScrollToEnd();
            Program.Render(root, 1120, 780, null);
            var atEnd = scroller.VerticalOffset;
            var pageUp = track.DecreaseRepeatButton;
            ((System.Windows.Input.RoutedCommand)pageUp.Command).Execute(pageUp.CommandParameter, pageUp.CommandTarget ?? pageUp);
            Program.Render(root, 1120, 780, null);
            Program.Assert(scroller.VerticalOffset < atEnd, "Clicking the styled scrollbar track must still page through public history.");
            scroller.ScrollToTop();
            Program.Render(root, 1120, 780, null);
            track.Thumb!.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            track.Thumb.RaiseEvent(new DragDeltaEventArgs(0, 30) { RoutedEvent = Thumb.DragDeltaEvent });
            track.Thumb.RaiseEvent(new DragCompletedEventArgs(0, 30, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Program.Render(root, 1120, 780, null);
            Program.Assert(scroller.VerticalOffset > 0, "Dragging the custom log thumb must scroll the real list.");
            scroller.ScrollToTop();
            Program.Render(root, 1120, 780, Path.Combine(output, "battle-log-all-seats.png"));
            var richLogs = Program.Find<BattleLogTextBlock>(log).ToArray();
            Program.Assert(richLogs.Length > 0 && richLogs.All(block =>
                string.Concat(block.Inlines.OfType<Run>().Select(run => run.Text)) == block.Entry!.Message) &&
                richLogs.Any(block => block.Inlines.OfType<Run>().Select(run => run.Foreground).Distinct().Count() > 1),
                "Colored log runs must preserve the exact public text while distinguishing participant names and card titles.");
            var reused = new BattleLogTextBlock { Entry = new(1, 1, "Recovery", "damage", "", $"{Display(human)} 回复 1 点体力。", human.Seat, human.Seat)
                { ParticipantNames = [Display(human)] } };
            var recoveryColor = ((SolidColorBrush)reused.Inlines.OfType<Run>().Single(run => run.Text == "回复 1 点体力").Foreground).Color;
            reused.Entry = new(2, 1, "Damage", "damage", "", $"{Display(target)} 受到 1 点火焰伤害。", source.Seat, target.Seat)
                { ParticipantNames = [Display(target)] };
            var damageColor = ((SolidColorBrush)reused.Inlines.OfType<Run>().Single(run => run.Text == "1 点火焰伤害").Foreground).Color;
            Program.Assert(recoveryColor != damageColor && !reused.Text.Contains("回复", StringComparison.Ordinal),
                "Recycled rich log controls must replace old text and use distinct recovery and damage colors.");
            var all = Rows();
            Program.Assert(model.FilteredBattleLog.Where(entry => entry.Sequence < first &&
                (entry.ActorSeat is not null || entry.TargetSeat is not null))
                    .All(entry => !entry.Message.Contains("AI ", StringComparison.Ordinal) && !entry.Message.Contains("号位", StringComparison.Ordinal)),
                "Existing engine-authored history must also replace raw player names rather than portrait display names.");
            Program.Assert(all[0].Message == "【你】与【AI 2】是原文中的标题。" &&
                all[3].Message == $"{Display(source)} 对 {Display(target)} 使用【杀】。" &&
                all[2].Message == $"{Display(human)} 受到 {Display(source)} 造成的 1 点伤害。" &&
                model.GameLog.Any(message => message.EndsWith(all[2].Message, StringComparison.Ordinal)),
                "Public history must use general names for AI-to-AI and incoming damage while preserving literal titles.");

            Click($"seat:{target.Seat}");
            Program.Assert(Rows().Select(entry => entry.Sequence).SequenceEqual(new[] { first + 2, first }),
                "Selecting a seat must include its received actions and exclude unrelated events.");
            Program.Assert(Program.Find<Button>(filters).Single(button => Equals(button.CommandParameter, $"seat:{target.Seat}")).Tag is true,
                "The selected seat button must remain visibly selected after filtering.");
            Add("CardUsed", $"{Name(human)} 对 {Name(target)} 使用【火杀】。", human.Seat, target.Seat);
            Add("CardsDrawn", $"{Name(provider)} 摸了 1 张牌。", provider.Seat);
            Program.Assert(Rows().Select(entry => entry.Sequence).SequenceEqual(new[] { first + 4, first + 2, first }),
                "New public entries must respect the active seat filter.");
            model.SelectBattleLogCategoryCommand.Execute("damage");
            Program.Assert(Rows().Length == 0, "Seat and category filters must apply together.");
            Click($"seat:{human.Seat}");
            Program.Assert(Rows().Single().Sequence == first + 1, "The human seat must use the same received-action filter as other seats.");
            model.SelectBattleLogCategoryCommand.Execute("all");
            Click($"seat:{provider.Seat}");
            Program.Assert(Rows().Select(entry => entry.Sequence).SequenceEqual(new[] { first + 5, first + 2 }),
                "A third public participant must be included even when it is outside the primary actor/target pair.");

            model.ToggleLogCommand.Execute(null);
            Program.Render(root, 1120, 780, null);
            Program.Assert(ReferenceEquals(((ListBox)window.FindName("FilteredBattleLogList")).ItemsSource, log.ItemsSource),
                "The detail view must show the same filtered public history as the sidebar.");
            model.ToggleLogCommand.Execute(null);
            Click("all");
            Program.Assert(Rows().Length == 6, "全部 must restore all retained public entries.");

            var engine = Program.Engine(model);
            var before = engine.CreateSnapshot(human.Seat);
            var rawLog = engine.Log.ToArray();
            var order = before.TableOrder!.ToArray();
            var sourceIndex = Array.IndexOf(order, source.Seat);
            var targetIndex = Array.IndexOf(order, target.Seat);
            (order[sourceIndex], order[targetIndex]) = (order[targetIndex], order[sourceIndex]);
            typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(model, [before with { TableOrder = Array.AsReadOnly(order) }]);
            Program.Assert(Rows().Single(entry => entry.Sequence == first).Message ==
                $"{Display(source)} 对 {Display(target)} 使用【杀】。" && engine.Log.SequenceEqual(rawLog),
                "Seat movement must preserve named public history and participant filters without changing stored facts.");
            Click($"seat:{target.Seat}");
            Program.Render(root, 1120, 780, Path.Combine(output, "battle-log-selected-seat.png"));
            typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(model, [before with { Players = Array.AsReadOnly(before.Players.Select(player =>
                    player.Seat == source.Seat ? player with { GeneralName = "不可见将名", IsGeneralPublic = false } : player).ToArray()) }]);
            Program.Assert(Rows().Single(entry => entry.Sequence == first).Message.StartsWith("暗将", StringComparison.Ordinal) &&
                model.FilteredBattleLog.All(entry => !entry.Message.Contains("不可见将名", StringComparison.Ordinal) &&
                    !entry.ParticipantNames.Contains("不可见将名")),
                "Named log projection must not expose a concealed general even when a diagnostic snapshot contains its name.");
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError && model.SelectedBattleLogSeat == "all" &&
                model.SelectedBattleLogCategory == "all" && model.FilteredBattleLog.All(entry => entry.Sequence < first) &&
                model.FilteredBattleLog.Where(entry => entry.ActorSeat == human.Seat || entry.TargetSeat == human.Seat)
                    .All(entry => !entry.Message.StartsWith("你", StringComparison.Ordinal) && !entry.Message.Contains("号位", StringComparison.Ordinal)),
                "Loading a match must start at 全部 and rebuild named history from its existing public log.");
            Program.AssertNoBindingErrors();

            void Click(string id)
            {
                filters.UpdateLayout();
                var button = Program.Find<Button>(filters).Single(button => Equals(button.CommandParameter, id));
                Program.Assert(button.Command?.CanExecute(button.CommandParameter) == true, "The visible log filter must have a usable command.");
                button.Command!.Execute(button.CommandParameter);
            }
        }
        finally { window.Content = null; window.Close(); }

        BattleLogEntryViewModel[] Rows() => model.FilteredBattleLog.Where(entry => entry.Sequence >= first).ToArray();
        string Name(SeatViewModel seat) => playerNames[seat.Seat];
        string Display(SeatViewModel seat) => seat.GeneralName + (seat.IsHuman ? "（你）" : "");
        void Add(string type, string message, int? actor = null, int? targetSeat = null) =>
            addLog.Invoke(model, [new GameLogEntry(next++, 1, type, message, actor, targetSeat)]);
    }
}
