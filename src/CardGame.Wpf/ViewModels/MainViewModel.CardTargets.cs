using CardGame.Core;
using System.Windows.Input;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private readonly HashSet<int> _selectedCardTargetSeats = [];
    public ICommand RecastSelectedCommand { get; private set; } = null!;
    public bool ShowRecastAction => IsMultiTargetCardSelected && _game.RulesVersion >= 6;
    public bool CanRecastSelected => ShowRecastAction && _selectedCardTargetSeats.Count == 0 && !IsTutorialActive &&
        _game.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Recast && action.CardId == _selectedCardId);

    private void RecastSelected()
    {
        if (!CanRecastSelected || _selectedCardId is not { } cardId || _snapshot.PendingDecision is not { } prompt) return;
        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new RecastCardCommand(_snapshot.HumanSeat, cardId, _snapshot.Revision, prompt.PromptId));
            if (!result.Accepted) return;
            ClearSelection();
            Refresh(result.State);
        });
    }

    public bool IsMultiTargetCardSelected => !IsActiveSkillSelectionPending &&
        _snapshot?.PendingDecision?.Kind == DecisionKind.PlayCard &&
        _snapshot.Players.Single(player => player.IsHuman).Hand.Any(card => card.Id == _selectedCardId && card.Kind == CardKind.IronChain);

    private LegalAction[] MultiTargetCardActions => IsMultiTargetCardSelected
        ? _game.GetHumanLegalActions().Where(action => action.CardId == _selectedCardId && action.Kind == LegalActionKind.IronChain).ToArray()
        : [];

    private int[] SelectedPlayTargets()
    {
        if (!IsMultiTargetCardSelected) return _selectedTargetSeat is { } seat ? [seat] : [];
        // Seat clicks are unordered; submit the canonical order supplied by Core.
        return MultiTargetCardActions.FirstOrDefault(action => _selectedCardTargetSeats.SetEquals(action.TargetSeats))?.TargetSeats.ToArray()
            ?? _selectedCardTargetSeats.Order().ToArray();
    }

    private void ToggleCardTarget(SeatViewModel seat)
    {
        if (!_selectedCardTargetSeats.Remove(seat.Seat))
        {
            if (!MultiTargetCardActions.Any(action => action.TargetSeats.Contains(seat.Seat) &&
                _selectedCardTargetSeats.All(action.TargetSeats.Contains))) return;
            _selectedCardTargetSeats.Add(seat.Seat);
        }
        RefreshTargetHighlights();
    }

    private string MultiTargetSelectionHint
    {
        get
        {
            var actions = MultiTargetCardActions;
            if (actions.Length == 0) return "铁索连环当前没有合法目标。";
            var range = FormatSelectionRange(actions.Min(action => action.TargetSeats.Count), actions.Max(action => action.TargetSeats.Count));
            return $"铁索连环 · 已选 {_selectedCardTargetSeats.Count} / {range} 个目标 · " +
                (_selectedCardTargetSeats.Count == 0 ? _game.RulesVersion >= 6
                    ? "点击武将，或重铸换一张牌" : "点击亮起的其他武将"
                    : "再次点击取消；主按钮或 Enter 确认");
        }
    }

    private string[] MultiTargetGuideSteps()
    {
        var changes = Seats.Where(seat => _selectedCardTargetSeats.Contains(seat.Seat))
            .Select(seat => $"{seat.Seat + 1} 号位 {seat.GeneralName}：{(seat.IsChained ? "解除连环" : "进入连环")}").ToArray();
        return [_game.RulesVersion >= 6
                ? "选择一到两名存活武将，可包含自己。再次点击可取消，选满后先取消一个再更换。"
                : "旧规则可选择一到两名其他存活武将；再次点击已选武将可取消。选满后须先取消一个，才能更换目标。",
            changes.Length > 0 ? string.Join("；", changes) + "。" : "尚未选择目标。",
            _game.RulesVersion >= 6
                ? "确认前不会使用手牌。选目标后用主按钮或 Enter 切换连环；不选目标时可点击「重铸换牌」，将铁索置入弃牌堆并摸一张牌，不会触发无懈响应。Esc 取消选择。"
                : "确认前不会使用手牌或改变连环状态。点击主按钮或按 Enter 使用铁索；Esc 取消整次选择。旧规则存档不支持重铸。",
            "若同时可通过武圣当作杀使用，仅选择一个合法攻击目标时才会显示可用的转化操作。"];
    }
}
