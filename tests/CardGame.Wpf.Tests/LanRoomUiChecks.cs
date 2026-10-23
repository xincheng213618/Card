using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CardGame.Core;
using CardGame.Lan;
using CardGame.Wpf;
using CardGame.Wpf.ViewModels;

internal static class LanRoomUiChecks
{
    public static void SelectedModesEnterRooms(string output)
    {
        using var model = new MainViewModel(false, 337, true, new MemorySaveStore(), useExpandedContent: true,
            wushuangSaveStore: new MemorySaveStore())
            { IsMotionEnabled = false };
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        var suspended = Program.Engine(model);
        var before = SnapshotJson.Serialize(suspended.CreateSnapshot(0));
        var views = Enumerable.Range(0, suspended.CreateSnapshot(0).Players.Count)
            .Select(seat => SnapshotJson.Serialize(suspended.CreateSnapshot(seat))).ToArray();
        var commands = CommandJson.Serialize(suspended.AcceptedCommands);
        try
        {
            Open("identity:classic-8");
            Program.Render(root, 1120, 740, Path.Combine(output, "mode-room-eight-empty.png"));
            var cards = (ItemsControl)window.FindName("SetupRoomSeatCards");
            var start = (Button)window.FindName("StartSetupRoomButton");
            Program.Assert(model.IsModeConfigurationOpen && cards.ActualHeight > 0 && cards.Items.Count == 8 && !start.IsEnabled &&
                model.SetupRoomSeats.Count(seat => seat.Kind == RoomSeatKind.Human) == 1 &&
                model.SetupRoomSeats.Count(seat => seat.Kind == RoomSeatKind.Empty) == 7,
                "Selecting eight-player identity must enter a room with the host and seven empty seats.");
            model.StartSetupRoomCommand.Execute(null);
            Program.Assert(ReferenceEquals(Program.Engine(model), suspended), "An incomplete room started a match.");
            var empty = model.SetupRoomSeats[1];
            model.AddSetupRoomAiCommand.Execute(empty);
            Program.Assert(empty.Kind == RoomSeatKind.Ai && empty.CanRemoveAi, "Adding AI did not occupy the selected seat.");
            model.RemoveSetupRoomAiCommand.Execute(empty);
            model.RemoveSetupRoomAiCommand.Execute(model.SetupRoomSeats[0]);
            Program.Assert(empty.CanAddAi && model.SetupRoomSeats[0].Kind == RoomSeatKind.Human,
                "Removing AI changed the human host or failed to reopen its seat.");
            CheckDifficultyRoomState(cards, start);
            foreach (var modeId in new[] { "team:standard-2v2", "national:ambitious-6", "activity:shenwu-2017", WushuangMode.ModeId })
            {
                Open(modeId);
                Program.Assert(model.SetupRoomSeats.Count == model.SelectedTableMode.PlayerCount &&
                    model.SetupRoomSeats.Count(seat => seat.CanAddAi) == model.SelectedTableMode.PlayerCount - 1 &&
                    !model.StartSetupRoomCommand.CanExecute(null), "A mode bypassed its room or reused another mode's seat count.");
            }
            Program.Assert(ReferenceEquals(Program.Engine(model), suspended) &&
                SnapshotJson.Serialize(suspended.CreateSnapshot(0)) == before, "Arranging room seats changed the suspended match.");
            Open("identity:classic-8");
            var roles = (ListBox)window.FindName("RoleChoices");
            var rebel = model.StartingRoles.Single(role => role.Role == Role.Rebel);
            roles.SetCurrentValue(Selector.SelectedItemProperty, rebel);
            roles.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
            var roomHost = model.SetupRoomSeats.Single(seat => seat.Kind == RoomSeatKind.Human);
            model.FillSetupRoomAiCommand.Execute(null);
            Program.Render(root, 1120, 740, Path.Combine(output, "mode-room-eight-ready.png"));
            Program.Assert(model.StartSetupRoomCommand.CanExecute(null) && start.IsEnabled &&
                model.SetupRoomSeats.Count(seat => seat.Kind == RoomSeatKind.Ai) == 7, "A full room did not become ready.");
            Program.Assert(model.SelectedStartingRole == rebel && Equals(roles.SelectedItem, rebel) &&
                ((ListBoxItem)roles.ItemContainerGenerator.ContainerFromItem(rebel)).IsSelected && SameRoomBoundary(),
                "Choosing a rebel in the real room identity list must preserve every suspended player view and accepted command.");
            var bounds = start.TransformToAncestor(root).TransformBounds(new Rect(start.RenderSize));
            Program.Assert(bounds.Top >= 0 && bounds.Bottom <= 740 && bounds.Left >= 0 && bounds.Right <= 1120,
                "The room start button is outside the compact window.");
            model.StartSetupRoomCommand.Execute(null);
            var game = Program.Engine(model);
            Program.Assert(!model.IsNewGameSetupOpen && game.ModeId == "identity:classic-8" &&
                game.CreateSnapshot(0).Players.Count == 8 && game.CreateSnapshot(0).Players.Count(player => player.IsHuman) == 1,
                "Starting the prepared room did not enter its selected eight-seat match.");
            var enteredView = game.CreateSnapshot(0);
            var humanSeat = model.Seats.Single(seat => seat.IsHuman);
            var lord = enteredView.Players.Single(player => player.Role == Role.Lord);
            Console.WriteLine($"Room slot numbering: room={roomHost.Seat}/{roomHost.Title}, " +
                $"human role={enteredView.Players.Single(player => player.IsHuman).Role}, table={humanSeat.DisplaySeatNumber}, " +
                $"lord table={model.Seats.Single(seat => seat.Seat == lord.Seat).DisplaySeatNumber}.");
            Program.Assert(roomHost.Seat == 0 && roomHost.Title == "房间席位 1" &&
                enteredView.Players.Single(player => player.IsHuman).Role == Role.Rebel && humanSeat.DisplaySeatNumber > 1 &&
                model.Seats.Single(seat => seat.Seat == lord.Seat).DisplaySeatNumber == 1,
                "Local room slots must be labelled separately from the actual table numbering, where the lord remains first and a chosen rebel does not.");
            model.NewGameCommand.Execute(null);
            Open("team:standard-2v2");
            model.StartNewGameFromLobbyCommand.Execute(null);
            Program.Assert(!model.IsNewGameSetupOpen && Program.Engine(model).ModeId == "team:standard-2v2" &&
                Program.Engine(model).CreateSnapshot(0).Players.Count(player => !player.IsHuman) == 3,
                "Quick start no longer fills the other seats with AI.");
            Program.AssertNoBindingErrors();
        }
        finally { window.Content = null; window.Close(); }

        void Open(string modeId)
        {
            model.OpenLobbyCategoryCommand.Execute("all");
            model.OpenModeConfigurationCommand.Execute(model.VisibleModeGroups.Single(group => group.Variants.Any(mode => mode.ModeId == modeId)));
            model.SelectedTableMode = model.TableModes.Single(mode => mode.ModeId == modeId);
        }

        void CheckDifficultyRoomState(ItemsControl cards, Button start)
        {
            Open("activity:jiange-normal");
            Program.Render(root, 1120, 740, null);
            var group = model.SelectedModeGroup!;
            var choices = (ListBox)window.FindName("TableModeChoices");
            Console.WriteLine($"Room difficulty fixture: group={group.Id}/{group.Name}, " +
                $"variants={string.Join(", ", group.Variants.Select(mode => $"{mode.ModeId}/{mode.PlayerCount}"))}, " +
                $"visibility={choices.Visibility}, inherited visible={choices.IsVisible}, loaded={choices.IsLoaded}, " +
                $"size={choices.ActualWidth:F3}x{choices.ActualHeight:F3}, items={choices.Items.Count}.");
            Program.Assert(group.Name == "剑阁决战" && group.Variants.Count > 1 && group.Variants.All(mode => mode.PlayerCount == 8) &&
                choices.Visibility == Visibility.Visible && choices.ActualWidth > 0 && choices.ActualHeight > 0 &&
                choices.Items.Count == group.Variants.Count,
                "The room-preservation fixture must expose actual same-size Jiange difficulty choices.");
            model.FillSetupRoomAiCommand.Execute(null);
            Program.Render(root, 1120, 740, null);
            Program.Assert(model.CanStartSetupRoom && start.IsEnabled && model.SetupRoomSeats.Count(seat => seat.Kind == RoomSeatKind.Ai) == 7,
                "The difficulty fixture must begin with a genuinely full eight-seat room.");
            SwitchDifficulty(group.Variants[1]);

            model.RemoveSetupRoomAiCommand.Execute(model.SetupRoomSeats[2]);
            Program.Render(root, 1120, 740, null);
            Program.Assert(!model.CanStartSetupRoom && !start.IsEnabled &&
                model.SetupRoomSeats.Count(seat => seat.Kind == RoomSeatKind.Empty) == 1 &&
                model.SetupRoomSeats.Count(seat => seat.Kind == RoomSeatKind.Ai) == 6,
                "The mixed-room fixture must contain the host, six AI and one deliberately empty seat.");
            SwitchDifficulty(group.Variants[0]);

            model.FillSetupRoomAiCommand.Execute(null);
            var activitySeats = model.SetupRoomSeats.ToArray();
            Open("identity:classic-8");
            AssertReset(activitySeats, 8);
            model.AddSetupRoomAiCommand.Execute(model.SetupRoomSeats[1]);
            var eightSeats = model.SetupRoomSeats.ToArray();
            Open("identity:classic-5");
            AssertReset(eightSeats, 5);

            void SwitchDifficulty(TableModeOption difficulty)
            {
                var seats = model.SetupRoomSeats.ToArray();
                var kinds = seats.Select(seat => seat.Kind).ToArray();
                var ready = model.StartSetupRoomCommand.CanExecute(null);
                var enabled = start.IsEnabled;
                choices.SetCurrentValue(Selector.SelectedItemProperty, difficulty);
                choices.GetBindingExpression(Selector.SelectedItemProperty)!.UpdateSource();
                Program.Render(root, 1120, 740, null);
                var row = (ListBoxItem)choices.ItemContainerGenerator.ContainerFromItem(difficulty);
                var retained = model.SetupRoomSeats.Count == seats.Length && cards.Items.Count == seats.Length && seats.Select((seat, index) =>
                    ReferenceEquals(model.SetupRoomSeats[index], seat) && seat.Kind == kinds[index] &&
                    ReferenceEquals(cards.Items[index], seat)).All(same => same);
                Console.WriteLine($"Room difficulty {difficulty.ModeId}: retained seats/kinds={retained}, " +
                    $"ready={ready}->{model.StartSetupRoomCommand.CanExecute(null)}, native start={enabled}->{start.IsEnabled}, same match={SameRoomBoundary()}.");
                Program.Assert(model.SelectedTableMode == difficulty && ReferenceEquals(model.SelectedModeGroup, group) &&
                    ReferenceEquals(choices.SelectedItem, difficulty) && row.IsSelected && retained &&
                    model.StartSetupRoomCommand.CanExecute(null) == ready && model.CanStartSetupRoom == ready &&
                    start.IsEnabled == enabled && SameRoomBoundary(),
                    "Changing only a same-size mode group's difficulty must retain every seat instance, human/AI/empty kind and native start readiness without changing the suspended match.");
            }

            void AssertReset(SetupRoomSeat[] previous, int count)
            {
                Program.Render(root, 1120, 740, null);
                Program.Assert(model.SetupRoomSeats.Count == count && cards.Items.Count == count &&
                    model.SetupRoomSeats.Select((seat, index) => seat.Seat == index &&
                        seat.Kind == (index == 0 ? RoomSeatKind.Human : RoomSeatKind.Empty) &&
                        previous.All(old => !ReferenceEquals(old, seat))).All(reset => reset) &&
                    !model.CanStartSetupRoom && !model.StartSetupRoomCommand.CanExecute(null) && !start.IsEnabled && SameRoomBoundary(),
                    "Changing mode group or player count must still rebuild the correct empty room while preserving the suspended game.");
            }
        }

        bool SameRoomBoundary() => ReferenceEquals(Program.Engine(model), suspended) &&
            CommandJson.Serialize(suspended.AcceptedCommands) == commands &&
            Enumerable.Range(0, views.Length).All(seat => SnapshotJson.Serialize(suspended.CreateSnapshot(seat)) == views[seat]);
    }

    public static void SeatsPrivacyAndSelection(string output)
    {
        var directory = new RoomDirectory(() => 337);
        var host = directory.Create(new("好友身份局", "房主", "identity:lan-5"));
        var room = directory.Get(host.Connection.RoomId);
        var guest = room.Join("朋友", 2);
        var window = new LanRoomWindow();
        using var model = new LanRoomViewModel();
        window.DataContext = model;
        var root = (FrameworkElement)window.Content;
        try
        {
            Program.Render(root, 1280, 800, Path.Combine(output, "lan-room-list.png"));
            model.Apply(room.Read(host.Connection.Token));
            Program.Assert(model.InRoom && model.CanManageSeats && !model.CanStart && model.Seats.Count == 5,
                "Room did not present host controls and empty seats.");
            Program.Assert(model.Seats.Count(s => s.CanAddAi) == 3, "AI controls appeared on a human seat.");
            var fill = room.Edit(host.Connection.Token, new(room.Read(host.Connection.Token).Version, "ai:fill"));
            model.Apply(fill.View);
            Program.Assert(!model.CanStart && model.Seats.Count(s => s.CanRemoveAi) == 3, "Readiness or AI management was lost.");
            var ready = room.Edit(guest.Connection.Token, new(fill.View.Version, "ready", Ready: true));
            model.Apply(room.Read(host.Connection.Token));
            Program.Assert(model.CanStart, "A fully ready room could not start.");
            Program.Render(root, 1280, 800, Path.Combine(output, "lan-room-seats.png"));
            var started = room.Edit(host.Connection.Token, new(ready.View.Version, "start"));
            var view = started.View.Actions.Count > 0 ? started.View : room.Read(guest.Connection.Token);
            model.Apply(view);
            Program.Assert(model.Actions.Count > 0 && !model.CanManageSeats, "Starting did not expose the responding player's actions.");
            var action = model.Actions[0];
            model.SelectedAction = action;
            Program.Render(root, 1280, 800, Path.Combine(output, "lan-room-selection.png"));
            var list = Program.Find<ListBox>(root).Single(item => item.Name == "ActionList");
            Program.Assert(ReferenceEquals(list.SelectedItem, action), "The action list did not retain its selected action.");
            model.Apply(view);
            Program.Assert(ReferenceEquals(model.SelectedAction, action), "An unchanged poll cleared the player's selection.");
            using var other = new LanRoomViewModel();
            var otherView = room.Read(view.ViewerSeat == host.Connection.Seat ? guest.Connection.Token : host.Connection.Token);
            other.Apply(otherView);
            Program.Assert(other.Actions.Count == 0 && !other.CanManageSeats,
                "A waiting peer received another player's choices or host controls.");
            Program.AssertNoBindingErrors();
        }
        finally { window.Close(); }
    }
}
