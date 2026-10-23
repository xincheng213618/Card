using System.Windows;
using System.Windows.Threading;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf;

public partial class LanRoomWindow : Window
{
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(700) };
    public LanRoomWindow(MainViewModel? singlePlayer = null, string? serverAddress = null)
    {
        InitializeComponent();
        var model = new LanRoomViewModel(singlePlayer is null ? null : mode =>
        {
            var local = singlePlayer.TableModes.FirstOrDefault(item => item.PlayerCount == mode.PlayerCount &&
                item.ModeId is "identity:classic-5" or "identity:classic-8" or "identity:standard-5" or "identity:standard-8");
            if (local is not null) singlePlayer.SelectedTableMode = local;
            singlePlayer.StartNewGameFromLobbyCommand.Execute(null);
            Close();
        }, singlePlayer?.SelectedTableMode.PlayerCount ?? 5, serverAddress);
        DataContext = model;
        _poll.Tick += async (_, _) => await model.PollAsync();
        Loaded += (_, _) => _poll.Start();
        Closed += (_, _) => { _poll.Stop(); model.Dispose(); };
    }
}
