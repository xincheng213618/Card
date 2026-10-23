using System.Collections.ObjectModel;
using System.Windows.Input;
using CardGame.Core;
using CardGame.Lan;

namespace CardGame.Wpf.ViewModels;

public sealed class LanSelectionItem(int id, string label) : ObservableObject
{
    private bool _selected;
    public int Id { get; } = id;
    public string Label { get; } = label;
    public bool IsSelected { get => _selected; set => SetProperty(ref _selected, value); }
}

public sealed record LanSeatItem(int Seat, string Name, string Detail, string Badge, bool CanAddAi, bool CanRemoveAi);
public sealed record LanRoomItem(RoomSummary Room)
{
    public string Id => Room.Id;
    public string Name => Room.Name;
    public string Occupancy => $"{Room.Humans + Room.Ai}/{Room.Capacity} · 真人 {Room.Humans} / AI {Room.Ai}";
    public string Status => Room.Phase switch { RoomPhase.Waiting => "等待中", RoomPhase.Playing => "对局中", RoomPhase.Completed => "已结束", _ => "已暂停" };
}

public sealed class LanRoomViewModel : ObservableObject, IDisposable
{
    // Reopening a room window in the same app can reclaim the existing seat without creating another player.
    private static readonly Dictionary<string, RoomConnection> Connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _io = new(1);
    private readonly Action<LanMode>? _quickStart;
    private LanClient? _client;
    private string? _clientAddress;
    private RoomConnection? _connection;
    private RoomView? _view;
    private bool _busy;
    private string _serverAddress = "http://127.0.0.1:5088";
    private string _playerName = Environment.UserName;
    private string _roomName = "一起打牌";
    private string _status = "选择模式后刷新房间，或快速开始单机对局。";
    private LanMode _selectedMode = LanContent.Modes[0];
    private LanRoomItem? _selectedRoom;
    private LanAction? _selectedAction;
    private RoomInput? _uncertainInput;
    private bool _disposed;
    private readonly List<RelayCommand> _commands = [];

    public LanRoomViewModel(Action<LanMode>? quickStart = null, int playerCount = 5, string? serverAddress = null)
    {
        _quickStart = quickStart;
        _selectedMode = Modes.FirstOrDefault(mode => mode.PlayerCount == playerCount) ?? Modes[0];
        if (serverAddress is not null) _serverAddress = serverAddress;
        RefreshCommand = Command(RefreshRoomsAsync, () => !InRoom);
        CreateCommand = Command(CreateAsync, () => !InRoom);
        JoinCommand = Command(JoinAsync, () => !InRoom && SelectedRoom is not null);
        FillAiCommand = Command(() => EditAsync("ai:fill"), () => CanManageSeats);
        ReadyCommand = Command(() => EditAsync("ready", ready: !(_view?.Seats[_view.ViewerSeat].Ready ?? false)), () => IsWaiting);
        StartCommand = Command(() => EditAsync("start"), () => CanStart);
        BackCommand = Command(BackAsync, () => InRoom);
        SubmitCommand = Command(SubmitAsync, () => SelectedAction is not null && _view?.Game?.PendingDecision is not null && _uncertainInput is null);
        RetryCommand = Command(RetryAsync, () => _uncertainInput is not null);
        QuickStartCommand = new RelayCommand(() => _quickStart?.Invoke(SelectedMode), () => _quickStart is not null && !InRoom && !IsBusy);
        AddAiCommand = new RelayCommand<LanSeatItem>(seat => _ = RunAsync(() => EditAsync("ai:add", seat.Seat)), seat => seat.CanAddAi && !IsBusy);
        RemoveAiCommand = new RelayCommand<LanSeatItem>(seat => _ = RunAsync(() => EditAsync("ai:remove", seat.Seat)), seat => seat.CanRemoveAi && !IsBusy);
    }

    public IReadOnlyList<LanMode> Modes => LanContent.Modes;
    public ObservableCollection<LanRoomItem> Rooms { get; } = [];
    public ObservableCollection<LanSeatItem> Seats { get; } = [];
    public ObservableCollection<LanAction> Actions { get; } = [];
    public ObservableCollection<LanSelectionItem> Cards { get; } = [];
    public ObservableCollection<LanSelectionItem> Targets { get; } = [];
    public string ServerAddress { get => _serverAddress; set => SetProperty(ref _serverAddress, value); }
    public string PlayerName { get => _playerName; set => SetProperty(ref _playerName, value); }
    public string RoomName { get => _roomName; set => SetProperty(ref _roomName, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); Notify(); } }
    public bool InRoom => _view is not null;
    public bool InLobby => !InRoom;
    public bool IsWaiting => _view?.Room.Phase == RoomPhase.Waiting;
    public bool HasGame => _view?.Game is not null;
    public bool CanManageSeats => IsWaiting && _view?.IsHost == true;
    public bool CanStart => CanManageSeats && _view!.Seats.All(seat => seat.Kind != RoomSeatKind.Empty && seat.Ready);
    public bool HasUncertainInput => _uncertainInput is not null;
    public string Title => _view is null ? "模式与房间" : $"{_view.Room.Name}  ·  房间 {_view.Room.Id}  ·  {_view.Room.ModeName}";
    public string Prompt => _view?.Game is { } game
        ? game.Status == EngineStatus.Completed ? $"本局结束：{WinnerText(game.Winner)}"
            : game.PendingDecision?.Kind == DecisionKind.SelectGeneral ? "请选择你的武将，候选仅对你可见。其他真人依次选择，AI 自动选将。"
            : game.PendingDecision?.Prompt ?? $"等待其他玩家 · 第 {game.TurnNumber} 回合 · {game.CurrentSeat + 1} 号位"
        : _view?.Room.Phase == RoomPhase.Faulted ? "房间已暂停，请查看服务端日志。"
        : "空位可以等待真人加入，或由房主添加 AI。真人准备后，房主开始对局。";
    public string SelectionHint => SelectedAction is { } action
        ? action.MaxCards == 0 && action.MaxTargets == 0 ? $"{action.Label}\n点击确认执行这项操作。"
            : $"{action.Label}\n选择牌 {action.MinCards}–{action.MaxCards} 张，目标 {action.MinTargets}–{action.MaxTargets} 人，然后确认。"
        : "请选择当前可用的操作。出牌操作已包含牌和目标；技能需要选择材料时，下方会列出候选。";
    public string HandText => _view?.Game?.Players.FirstOrDefault(p => p.Seat == _view.ViewerSeat) is { } player
        ? string.Join("  ·  ", player.Hand.Select(CardText)) : "尚未发牌";
    public string LogText => _view is null ? string.Empty : string.Join(Environment.NewLine, _view.Messages);
    public string ReadyLabel => _view?.Seats[_view.ViewerSeat].Ready == true ? "取消准备" : "准备";
    public LanMode SelectedMode
    {
        get => _selectedMode;
        set { if (value is not null && SetProperty(ref _selectedMode, value)) { Rooms.Clear(); SelectedRoom = null; Status = "已切换模式，请刷新房间列表。"; } }
    }
    public LanRoomItem? SelectedRoom { get => _selectedRoom; set { SetProperty(ref _selectedRoom, value); Notify(); } }
    public LanAction? SelectedAction
    {
        get => _selectedAction;
        set { if (SetProperty(ref _selectedAction, value)) { BuildSelections(); RaisePropertyChanged(nameof(SelectionHint)); Notify(); } }
    }
    public ICommand RefreshCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand JoinCommand { get; }
    public ICommand FillAiCommand { get; }
    public ICommand AddAiCommand { get; }
    public ICommand RemoveAiCommand { get; }
    public ICommand ReadyCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SubmitCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand QuickStartCommand { get; }

    private RelayCommand Command(Func<Task> action, Func<bool> canExecute)
    {
        var command = new RelayCommand(() => _ = RunAsync(action), () => !IsBusy && canExecute());
        _commands.Add(command);
        return command;
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_disposed || IsBusy) return;
        IsBusy = true;
        try
        {
            await _io.WaitAsync(_lifetime.Token);
            try { await action(); }
            finally { _io.Release(); }
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception error) { Status = error.Message; }
        finally { IsBusy = false; }
    }
    private LanClient Client()
    {
        if (_client is not null && (InRoom || _clientAddress == ServerAddress.Trim())) return _client;
        _client?.Dispose();
        _clientAddress = ServerAddress.Trim();
        return _client = new LanClient(_clientAddress);
    }
    private string ConnectionKey(string roomId) => _clientAddress!.TrimEnd('/') + "/" + roomId;
    private async Task RefreshRoomsAsync()
    {
        var rooms = await Client().Rooms(SelectedMode.Id, _lifetime.Token);
        Rooms.Clear(); foreach (var room in rooms) Rooms.Add(new(room));
        Status = rooms.Length == 0 ? "这个模式暂无房间，可以创建一个。" : $"找到 {rooms.Length} 个房间。";
    }
    private async Task CreateAsync() => Attach(await Client().Create(new(RoomName, PlayerName, SelectedMode.Id), _lifetime.Token));
    private async Task JoinAsync()
    {
        if (SelectedRoom is not { } room) return;
        var client = Client();
        if (Connections.TryGetValue(ConnectionKey(room.Id), out var previous))
        {
            try { Attach(new(previous, await client.Read(previous, _lifetime.Token))); return; }
            catch (RoomException error) when (error.StatusCode is 401 or 404) { Connections.Remove(ConnectionKey(room.Id)); }
        }
        Attach(await client.Join(room.Id, new(PlayerName), _lifetime.Token));
    }
    private void Attach(JoinRoomResult result)
    {
        _connection = result.Connection;
        Connections[ConnectionKey(_connection.RoomId)] = _connection;
        Apply(result.View);
        Status = $"已连接 · 你在 {_connection.Seat + 1} 号位。";
    }
    private async Task EditAsync(string operation, int? seat = null, bool? ready = null)
    {
        if (_view is null || _connection is null) return;
        var reply = await Client().Edit(_connection, new(_view.Version, operation, seat, ready), _lifetime.Token);
        Apply(reply.View); Status = reply.Error ?? "房间已更新。";
    }
    private async Task BackAsync()
    {
        if (_connection is null) return;
        if (IsWaiting)
        {
            await Client().Leave(_connection, _lifetime.Token);
            Connections.Remove(ConnectionKey(_connection.RoomId));
        }
        _connection = null; _view = null; _uncertainInput = null;
        Seats.Clear(); Actions.Clear(); SelectedAction = null;
        Notify(); await RefreshRoomsAsync();
    }
    private async Task SubmitAsync()
    {
        if (_connection is null || _view?.Game?.PendingDecision is not { } prompt || SelectedAction is null) return;
        _uncertainInput = new(Guid.NewGuid(), _view.Game.Revision, prompt.PromptId.Value, SelectedAction.Id,
            Cards.Where(c => c.IsSelected).Select(c => c.Id).ToArray(), Targets.Where(t => t.IsSelected).Select(t => t.Id).ToArray());
        await RetryAsync();
    }
    private async Task RetryAsync()
    {
        if (_uncertainInput is null || _connection is null) return;
        try
        {
            var reply = await Client().Submit(_connection, _uncertainInput, _lifetime.Token);
            _uncertainInput = null;
            Apply(reply.View); Status = reply.Error ?? "操作已确认。";
        }
        catch
        {
            Status = "操作结果尚未确认。连接恢复后点击“确认上次操作”，不会重复出牌。";
            Notify();
            throw new InvalidOperationException(Status);
        }
    }
    public async Task PollAsync()
    {
        if (_disposed || IsBusy || _connection is null || !_io.Wait(0)) return;
        try
        {
            var view = await Client().Read(_connection, _lifetime.Token);
            if (_disposed) return;
            Apply(view);
            if (Status.StartsWith("连接中断", StringComparison.Ordinal)) Status = "连接已恢复，座位保持不变。";
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception error) { Status = "连接中断，正在重试：" + error.Message; }
        finally { _io.Release(); }
    }
    public void Apply(RoomView view)
    {
        if (_view?.Version == view.Version && _view.Room.Id == view.Room.Id) return;
        _view = view;
        Seats.Clear();
        foreach (var seat in view.Seats)
        {
            var player = view.Game?.Players.FirstOrDefault(p => p.Seat == seat.Seat);
            var detail = player is null ? seat.Kind == RoomSeatKind.Empty ? "等待加入" : seat.Ready ? "已准备" : "未准备"
                : $"{player.GeneralName} · {RoleText(player.Role)}\n体力 {player.Hp}/{player.MaxHp} · 手牌 {player.HandCount}\n" +
                  (!player.IsAlive ? "已阵亡" : player.Equipment.Count > 0 ? string.Join(" / ", player.Equipment.Select(c => CardCatalog.Get(c.Kind).DisplayName)) : "无装备");
            Seats.Add(new(seat.Seat, $"{seat.Seat + 1} 号位 · {seat.Name}", detail,
                (seat.IsHost ? "房主 " : "") + (seat.Seat == view.ViewerSeat ? "你" : seat.Kind == RoomSeatKind.Ai ? "AI" : seat.Kind == RoomSeatKind.Human ? "真人" : "空位"),
                CanManageSeats && seat.Kind == RoomSeatKind.Empty, CanManageSeats && seat.Kind == RoomSeatKind.Ai));
        }
        Actions.Clear(); foreach (var action in view.Actions) Actions.Add(action);
        SelectedAction = null; BuildSelections();
        Notify();
    }
    private void BuildSelections()
    {
        Cards.Clear(); Targets.Clear();
        if (_view?.Game is not { } game || SelectedAction is not { } action) return;
        var own = game.Players.Single(player => player.Seat == _view.ViewerSeat);
        foreach (var id in action.CardIds ?? [])
        {
            var card = own.Hand.Concat(own.Equipment).FirstOrDefault(card => card.Id == id);
            Cards.Add(new(id, card is null ? $"牌 {id}" : CardText(card)));
        }
        foreach (var seat in action.TargetSeats ?? [])
            Targets.Add(new(seat, $"{seat + 1} 号位 · {game.Players.Single(p => p.Seat == seat).GeneralName}"));
    }
    private void Notify()
    {
        foreach (var name in new[] { nameof(InRoom), nameof(InLobby), nameof(IsWaiting), nameof(CanManageSeats), nameof(CanStart),
            nameof(Title), nameof(Prompt), nameof(HandText), nameof(LogText), nameof(ReadyLabel), nameof(HasGame), nameof(HasUncertainInput) }) RaisePropertyChanged(name);
        foreach (var command in _commands) command.NotifyCanExecuteChanged();
        if (QuickStartCommand is RelayCommand quick) quick.NotifyCanExecuteChanged();
    }
    private static string CardText(CardSnapshot card) => $"{SuitText(card.Suit)} {card.RankText} {card.DisplayName}";
    private static string SuitText(Suit suit) => suit switch { Suit.Spade => "♠", Suit.Heart => "♥", Suit.Club => "♣", Suit.Diamond => "♦", _ => suit.ToString() };
    private static string RoleText(Role? role) => role switch { Role.Lord => "主公", Role.Loyalist => "忠臣", Role.Rebel => "反贼", Role.Renegade => "内奸", _ => "身份未知" };
    private static string WinnerText(Winner winner) => winner switch
    {
        Winner.LordAndLoyalists => "主公与忠臣获胜", Winner.Rebels => "反贼获胜",
        Winner.Renegade => "内奸获胜", Winner.Draw => "平局", _ => winner.ToString()
    };
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _client?.Dispose();
        // Semaphore/lifetime may still be used by the pending async call's finally block.
    }
}
