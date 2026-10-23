using System.Collections.ObjectModel;
using System.Windows.Input;
using CardGame.Lan;

namespace CardGame.Wpf.ViewModels;

public sealed class SetupRoomSeat(int seat, RoomSeatKind kind) : ObservableObject
{
    private RoomSeatKind _kind = kind;
    public int Seat { get; } = seat;
    public RoomSeatKind Kind => _kind;
    public string Title => $"{Seat + 1} 号位";
    public string Name => Kind switch { RoomSeatKind.Human => "你 · 房主", RoomSeatKind.Ai => $"AI {Seat + 1}", _ => "空位" };
    public string Detail => Kind switch { RoomSeatKind.Human => "真人 · 已入座", RoomSeatKind.Ai => "电脑控制 · 已准备", _ => "点击下方添加 AI" };
    public bool CanAddAi => Kind == RoomSeatKind.Empty;
    public bool CanRemoveAi => Kind == RoomSeatKind.Ai;
    internal void SetKind(RoomSeatKind kind)
    {
        if (!SetProperty(ref _kind, kind, nameof(Kind))) return;
        foreach (var property in new[] { nameof(Name), nameof(Detail), nameof(CanAddAi), nameof(CanRemoveAi) })
            RaisePropertyChanged(property);
    }
}

public sealed partial class MainViewModel
{
    private string? _setupRoomKey;
    public ObservableCollection<SetupRoomSeat> SetupRoomSeats { get; } = [];
    public string SetupRoomTitle => $"{SelectedTableMode?.Name} · 房间";
    public string SetupRoomStatus => $"成员 {SetupRoomSeats.Count(seat => seat.Kind != RoomSeatKind.Empty)}/{SetupRoomSeats.Count} · " +
        (SetupRoomSeats.Any(seat => seat.Kind == RoomSeatKind.Empty) ? "安排 AI 后开始；快速开始会自动补齐 AI。" : "座位已满，可以开始对局。");
    public bool CanStartSetupRoom => IsModeConfigurationOpen && !IsTutorialActive &&
        SetupRoomSeats.Count == SelectedTableMode.PlayerCount && SetupRoomSeats.All(seat => seat.Kind != RoomSeatKind.Empty);
    public bool CanOpenSelectedLanMode => SelectedTableMode?.ModeId is
        "identity:classic-5" or "identity:classic-8" or "identity:standard-5" or "identity:standard-8";
    public ICommand AddSetupRoomAiCommand { get; private set; } = null!;
    public ICommand RemoveSetupRoomAiCommand { get; private set; } = null!;
    public ICommand FillSetupRoomAiCommand { get; private set; } = null!;
    public ICommand StartSetupRoomCommand { get; private set; } = null!;

    private void InitializeRoomSetup()
    {
        AddSetupRoomAiCommand = new RelayCommand<SetupRoomSeat>(seat => ChangeSetupRoomSeat(seat, RoomSeatKind.Ai),
            seat => IsModeConfigurationOpen && SetupRoomSeats.Contains(seat) && seat.CanAddAi);
        RemoveSetupRoomAiCommand = new RelayCommand<SetupRoomSeat>(seat => ChangeSetupRoomSeat(seat, RoomSeatKind.Empty),
            seat => IsModeConfigurationOpen && SetupRoomSeats.Contains(seat) && seat.CanRemoveAi);
        FillSetupRoomAiCommand = new RelayCommand(() =>
        {
            if (!IsModeConfigurationOpen) return;
            foreach (var seat in SetupRoomSeats.Where(seat => seat.CanAddAi)) seat.SetKind(RoomSeatKind.Ai);
            NotifySetupRoom();
        }, () => IsModeConfigurationOpen && SetupRoomSeats.Any(seat => seat.CanAddAi));
        StartSetupRoomCommand = new RelayCommand(() =>
        {
            if (CanStartSetupRoom) StartConfiguredGame(showOpeningDeal: true);
        }, () => CanStartSetupRoom);
    }

    private void PrepareSetupRoom()
    {
        var roomKey = SelectedModeGroup?.Id ?? SelectedTableMode.ModeId;
        if (_setupRoomKey != roomKey || SetupRoomSeats.Count != SelectedTableMode.PlayerCount)
        {
            _setupRoomKey = roomKey;
            SetupRoomSeats.Clear();
            for (var seat = 0; seat < SelectedTableMode.PlayerCount; seat++)
                SetupRoomSeats.Add(new(seat, seat == 0 ? RoomSeatKind.Human : RoomSeatKind.Empty));
        }
        NotifySetupRoom();
    }

    private void ChangeSetupRoomSeat(SetupRoomSeat seat, RoomSeatKind kind)
    {
        if (!IsModeConfigurationOpen || !SetupRoomSeats.Contains(seat) || seat.Kind == RoomSeatKind.Human) return;
        seat.SetKind(kind);
        NotifySetupRoom();
    }

    private void NotifySetupRoom()
    {
        foreach (var property in new[] { nameof(SetupRoomTitle), nameof(SetupRoomStatus), nameof(CanStartSetupRoom), nameof(CanOpenSelectedLanMode) })
            RaisePropertyChanged(property);
        (AddSetupRoomAiCommand as RelayCommand<SetupRoomSeat>)?.NotifyCanExecuteChanged();
        (RemoveSetupRoomAiCommand as RelayCommand<SetupRoomSeat>)?.NotifyCanExecuteChanged();
        (FillSetupRoomAiCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (StartSetupRoomCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }
}
