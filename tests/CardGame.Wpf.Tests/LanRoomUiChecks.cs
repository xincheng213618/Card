using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Lan;
using CardGame.Wpf;
using CardGame.Wpf.ViewModels;

internal static class LanRoomUiChecks
{
    public static void SelectedModesEnterRooms(string output)
    {
        using var model = new MainViewModel(false, 337, true, new MemorySaveStore(), useExpandedContent: true)
            { IsMotionEnabled = false };
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        var suspended = Program.Engine(model);
        var before = SnapshotJson.Serialize(suspended.CreateSnapshot(0));
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
            model.FillSetupRoomAiCommand.Execute(null);
            Program.Render(root, 1120, 740, Path.Combine(output, "mode-room-eight-ready.png"));
            Program.Assert(model.StartSetupRoomCommand.CanExecute(null) && start.IsEnabled &&
                model.SetupRoomSeats.Count(seat => seat.Kind == RoomSeatKind.Ai) == 7, "A full room did not become ready.");
            var bounds = start.TransformToAncestor(root).TransformBounds(new Rect(start.RenderSize));
            Program.Assert(bounds.Top >= 0 && bounds.Bottom <= 740 && bounds.Left >= 0 && bounds.Right <= 1120,
                "The room start button is outside the compact window.");
            model.StartSetupRoomCommand.Execute(null);
            var game = Program.Engine(model);
            Program.Assert(!model.IsNewGameSetupOpen && game.ModeId == "identity:classic-8" &&
                game.CreateSnapshot(0).Players.Count == 8 && game.CreateSnapshot(0).Players.Count(player => player.IsHuman) == 1,
                "Starting the prepared room did not enter its selected eight-seat match.");
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
