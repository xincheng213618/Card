using System.Security.Cryptography;
using System.Text.Json;
using CardGame.Core;

namespace CardGame.Lan;

/// <summary>One serialized authority per room. Tokens bind a connection to a seat, never to client-supplied actor ids.</summary>
public sealed class GameRoom
{
    private sealed class Member(string name, string token)
    {
        public string Name { get; } = name;
        public string Token { get; } = token;
        public bool Ready { get; set; }
        public Dictionary<Guid, (string Input, bool Accepted, string? Error)> Requests { get; } = [];
    }
    private readonly object _gate = new();
    private readonly Member?[] _members;
    private readonly bool[] _ai;
    private readonly List<string> _messages = [];
    private readonly Dictionary<int, RoomView> _views = [];
    private GameEngine? _game;
    private RoomPhase _phase;
    private int _hostSeat;
    private long _version;
    private readonly Func<int> _seedSource;
    public string Id { get; }
    public string Name { get; }
    public LanMode Mode { get; }

    public GameRoom(string id, string name, LanMode mode, Func<int>? seedSource = null)
    {
        Id = id; Name = ValidateName(name, "房间名称"); Mode = mode;
        _members = new Member?[mode.PlayerCount]; _ai = new bool[mode.PlayerCount];
        _seedSource = seedSource ?? (() => RandomNumberGenerator.GetInt32(int.MaxValue));
    }

    public JoinRoomResult Join(string name, int? requestedSeat = null)
    {
        lock (_gate)
        {
            if (_phase != RoomPhase.Waiting) throw new RoomException("本局已开始，不能加入新玩家。", 409);
            var seat = requestedSeat ?? Array.FindIndex(_members, 0, _members.Length, m => m is null);
            if (requestedSeat is null)
                seat = Enumerable.Range(0, _members.Length).FirstOrDefault(i => _members[i] is null && !_ai[i], -1);
            if (seat < 0 || seat >= _members.Length || _members[seat] is not null || _ai[seat])
                throw new RoomException("这个房间没有可用的空位。", 409);
            var firstHuman = _members.All(m => m is null);
            if (firstHuman) _hostSeat = seat;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var member = new Member(ValidateName(name, "玩家昵称"), token) { Ready = firstHuman };
            _members[seat] = member;
            Changed($"{member.Name} 加入了 {seat + 1} 号位。");
            return new(new(Id, token, seat), View(seat));
        }
    }

    public RoomSummary Summary() { lock (_gate) return SummaryCore(); }
    private RoomSummary SummaryCore() => new(Id, Name, Mode.Id, Mode.Name, _members.Length,
        _members.Count(m => m is not null), _ai.Count(ai => ai), _phase);
    public RoomView Read(string token) { lock (_gate) return View(Authenticate(token)); }

    public RoomReply Edit(string token, RoomEditRequest edit)
    {
        lock (_gate)
        {
            var actor = Authenticate(token);
            if (_phase != RoomPhase.Waiting) return new(false, "本局已经开始，座位不能再修改。", View(actor));
            if (edit.ExpectedVersion != _version) return new(false, "房间已更新，请重试。", View(actor));
            if (edit.Operation == "ready")
            {
                _members[actor]!.Ready = edit.Ready ?? !_members[actor]!.Ready;
                Changed($"{_members[actor]!.Name} {(_members[actor]!.Ready ? "已准备" : "取消准备")}。");
            }
            else
            {
                if (actor != _hostSeat) throw new RoomException("只有房主可以安排 AI 或开始对局。", 403);
                switch (edit.Operation)
                {
                    case "ai:add":
                    case "ai:remove":
                        if (edit.Seat is not { } seat || seat < 0 || seat >= _members.Length || _members[seat] is not null)
                            throw new RoomException("请选择空位或 AI 座位。");
                        _ai[seat] = edit.Operation == "ai:add";
                        Changed($"{seat + 1} 号位{(_ai[seat] ? "已添加 AI" : "已设为空位")}。");
                        break;
                    case "ai:fill":
                        for (var i = 0; i < _members.Length; i++) if (_members[i] is null) _ai[i] = true;
                        Changed("空位已补齐 AI。");
                        break;
                    case "start":
                        if (Enumerable.Range(0, _members.Length).Any(i => _members[i] is null && !_ai[i]) ||
                            _members.Any(m => m is { Ready: false }))
                            return new(false, "请补齐所有空位，并等待每位真人准备。", View(actor));
                        Start();
                        break;
                    default: throw new RoomException("未知的房间操作。");
                }
            }
            return new(true, null, View(actor));
        }
    }

    public void Leave(string token)
    {
        lock (_gate)
        {
            var seat = Authenticate(token);
            if (_phase != RoomPhase.Waiting) throw new RoomException("对局中请保留窗口，网络恢复后会自动重新连接。", 409);
            var name = _members[seat]!.Name;
            _members[seat] = null;
            if (seat == _hostSeat)
            {
                _hostSeat = Array.FindIndex(_members, m => m is not null);
                if (_hostSeat >= 0) _members[_hostSeat]!.Ready = true;
            }
            Changed($"{name} 离开房间。");
        }
    }

    public RoomReply Submit(string token, RoomInput input)
    {
        lock (_gate)
        {
            var seat = Authenticate(token);
            var member = _members[seat]!;
            if (input.RequestId == Guid.Empty) throw new RoomException("操作缺少请求编号。");
            var fingerprint = JsonSerializer.Serialize(input, LanProtocol.Json);
            if (member.Requests.TryGetValue(input.RequestId, out var previous))
                return previous.Input == fingerprint ? new(previous.Accepted, previous.Error, View(seat))
                    : new(false, "同一个请求编号不能用于不同操作。", View(seat));
            var accepted = false;
            string? error = null;
            if (_phase != RoomPhase.Playing || _game is null) error = "当前牌局不能接受操作。";
            else
            {
                try
                {
                    var command = LanActions.CreateCommand(_game, seat, input);
                    var eventIndex = _game.Events.Count;
                    var result = _game.Submit(command);
                    accepted = result.Accepted;
                    error = result.Error?.Message;
                    if (accepted)
                    {
                        Pump();
                        AddPublicEvents(eventIndex);
                        _phase = _game.State.Status == EngineStatus.Completed ? RoomPhase.Completed : RoomPhase.Playing;
                        Changed(_phase == RoomPhase.Completed ? "本局已结束。" : null);
                    }
                }
                catch (RoomException exception) { error = exception.Message; }
                catch
                {
                    _phase = RoomPhase.Faulted;
                    error = "对局遇到异常，房间已暂停。请查看服务端日志。";
                    Changed(error);
                    throw; // Server logs the failure; no private diagnostic data is sent to a player.
                }
            }
            member.Requests[input.RequestId] = (fingerprint, accepted, error);
            if (member.Requests.Count > 128) member.Requests.Remove(member.Requests.Keys.First());
            return new(accepted, error, View(seat));
        }
    }

    private void Start()
    {
        _game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = _seedSource(), PlayerCount = Mode.PlayerCount,
            ModeId = Mode.Id, HumanSeat = _hostSeat, HumanRole = null,
            HumanSeats = Enumerable.Range(0, _members.Length).Where(i => _members[i] is not null).ToArray(),
            UseInteractiveSetup = true, ConfirmOpeningHand = false,
            AiPolicyVersion = AiDecisionPolicy.CurrentVersion
        }, LanContent.Content);
        try
        {
            var start = _game.Submit(new StartGameCommand());
            if (!start.Accepted) throw new InvalidOperationException(start.Error?.Message);
            Pump();
            _phase = RoomPhase.Playing;
            Changed("对局开始，请按各自的提示选将，AI 自动选将。");
        }
        catch { _phase = RoomPhase.Faulted; Changed("开局失败，请查看服务端日志。"); throw; }
    }

    private void Pump()
    {
        // Some native continuations intentionally return Running before the next decision.
        for (var step = 0; step < 4 && _game!.State.Status != EngineStatus.Completed; step++)
        {
            if (Enumerable.Range(0, _members.Length).Any(i => _members[i] is not null && _game.CreateSnapshot(i).PendingDecision is not null)) return;
            var result = _game.Submit(new AdvanceCommand(_game.Revision));
            if (!result.Accepted) throw new InvalidOperationException(result.Error?.Message);
        }
        if (_game!.State.Status != EngineStatus.Completed && !Enumerable.Range(0, _members.Length)
                .Any(i => _members[i] is not null && _game.CreateSnapshot(i).PendingDecision is not null))
            throw new InvalidOperationException("The match did not reach a player boundary.");
    }

    private RoomView View(int seat)
    {
        if (_views.TryGetValue(seat, out var cached)) return cached;
        var seats = Enumerable.Range(0, _members.Length).Select(i => new RoomSeat(i,
            _members[i] is not null ? RoomSeatKind.Human : _ai[i] ? RoomSeatKind.Ai : RoomSeatKind.Empty,
            _members[i]?.Name ?? (_ai[i] ? $"AI {i + 1}" : "空位"), _members[i]?.Ready ?? _ai[i], i == _hostSeat)).ToArray();
        // Never use GameEngine.State/CommandResult.State here: they belong to the legacy primary viewer.
        var game = _game is not null && !_game.IsFaulted ? _game.CreateSnapshot(seat) : null;
        var view = new RoomView(SummaryCore(), _version, seat, seat == _hostSeat, seats, game,
            _phase == RoomPhase.Playing ? LanActions.Describe(_game!, seat) : [], _messages.ToArray());
        _views[seat] = view;
        return view;
    }

    private void AddPublicEvents(int start)
    {
        foreach (var envelope in _game!.Events.Skip(start))
        {
            var message = envelope.Payload switch
            {
                CardUsedEvent card => $"{card.SourceSeat + 1} 号位对 {card.TargetSeat + 1} 号位使用【{CardCatalog.Get(card.CardKind).DisplayName}】。",
                GroupCardUsedEvent card => $"{card.SourceSeat + 1} 号位使用【{CardCatalog.Get(card.CardKind).DisplayName}】。",
                TurnStartedEvent turn => $"轮到 {turn.ActorSeat + 1} 号位行动。",
                _ => null
            };
            if (message is not null) _messages.Add(message);
        }
    }
    private int Authenticate(string token)
    {
        var seat = Array.FindIndex(_members, member => member is not null && member.Token == token);
        return seat >= 0 ? seat : throw new RoomException("房间连接凭证无效，请重新加入。", 401);
    }
    private void Changed(string? message)
    {
        _version++; _views.Clear();
        if (message is not null) _messages.Add(message);
        if (_messages.Count > 60) _messages.RemoveRange(0, _messages.Count - 60);
    }
    public static string ValidateName(string? name, string label)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 30 || name.Any(char.IsControl))
            throw new RoomException($"{label}须为 1–30 个字符。");
        return name;
    }
}
