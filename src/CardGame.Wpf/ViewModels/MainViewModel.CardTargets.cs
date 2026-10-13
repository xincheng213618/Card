using CardGame.Core;
using System.Windows.Input;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private readonly HashSet<int> _selectedCardTargetSeats = [];
    public ICommand RecastSelectedCommand { get; private set; } = null!;
    public bool ShowRecastAction => IsMultiTargetCardSelected && SelectedRecastAction() is not null;
    public bool CanRecastSelected => ShowRecastAction && _selectedCardTargetSeats.Count == 0 &&
        !IsTutorialActive;

    private LegalAction? SelectedRecastAction() => _selectedCardId is { } cardId
        ? GetViewLegalActions().SingleOrDefault(action =>
            action.Kind == LegalActionKind.Recast && action.CardId == cardId &&
            action.ConversionSource == _selectedConversionSource)
        : null;

    private void RecastSelected()
    {
        if (!CanRecastSelected || _selectedCardId is not { } cardId || _snapshot.PendingDecision is not { } prompt) return;
        var action = SelectedRecastAction();
        if (action is null) return;
        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new RecastCardCommand(_snapshot.HumanSeat, cardId, _snapshot.Revision, prompt.PromptId)
            { ConversionSource = action.ConversionSource });
            if (!result.Accepted) return;
            ClearSelection();
            RefreshCommandResult(result.State);
        });
    }

    public bool IsMultiTargetCardSelected => !IsActiveSkillSelectionPending &&
        _snapshot?.PendingDecision?.Kind == DecisionKind.PlayCard &&
        GetViewLegalActions().Any(action => action.CardId == _selectedCardId &&
            action.ConversionSource == _selectedConversionSource &&
            action.Kind is LegalActionKind.IronChain or LegalActionKind.Recast);

    private LegalAction[] MultiTargetCardActions => IsMultiTargetCardSelected
        ? GetViewLegalActions().Where(action => action.CardId == _selectedCardId &&
            action.ConversionSource == _selectedConversionSource && action.Kind == LegalActionKind.IronChain).ToArray()
        : [];

    private IReadOnlyList<int> SelectedPlayTargets()
    {
        if (!IsMultiTargetCardSelected) return _selectedTargetSeat is { } seat ? [seat] : [];
        // Seat clicks are unordered; submit the canonical order supplied by Core.
        return MultiTargetCardActions.FirstOrDefault(action => _selectedCardTargetSeats.SetEquals(action.TargetSeats))?.TargetSeats
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
                (_selectedCardTargetSeats.Count == 0 ? CanRecastSelected
                    ? "点击武将，或重铸换一张牌" : "点击亮起的武将选择目标"
                    : "再次点击取消；主按钮或 Enter 确认");
        }
    }

    private string[] MultiTargetGuideSteps()
    {
        var changes = Seats.Where(seat => _selectedCardTargetSeats.Contains(seat.Seat))
            .Select(seat => $"{seat.DisplaySeatNumber} 号位 {seat.GeneralName}：{(seat.IsChained ? "解除连环" : "进入连环")}").ToArray();
        var actions = MultiTargetCardActions;
        var range = actions.Length == 0 ? "0" :
            FormatSelectionRange(actions.Min(action => action.TargetSeats.Count), actions.Max(action => action.TargetSeats.Count));
        var recast = ShowRecastAction && !IsTutorialActive
            ? "不选目标时可点击「重铸换牌」，将铁索置入弃牌堆并摸一张牌，不会触发无懈响应。" : string.Empty;
        return [$"选择 {range} 名存活武将，可包含自己。再次点击可取消，选满后先取消一个再更换。",
            changes.Length > 0 ? string.Join("；", changes) + "。" : "尚未选择目标。",
            $"确认前不会使用手牌。选目标后用主按钮或 Enter 切换连环。{recast}Esc 取消选择。",
            "同一张牌有其他使用方式时，先在中央选择相应技能；目标会按所选方式更新。"];
    }
}
