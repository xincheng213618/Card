using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private bool _isSelectingWusheng;
    private bool IsWushengSelectionPending => _isSelectingWusheng &&
        IsWushengSource(_selectedConversionSource) && _snapshot.PendingDecision?.Kind == DecisionKind.PlayCard;

    private static bool IsWushengSource(CardConversionSource? source) =>
        source?.SkillId is "standard:wusheng" or "classic:wusheng" or "boundary:wusheng";

    private bool TrySelectWusheng(HumanSkillViewModel skill)
    {
        if (_snapshot.PendingDecision?.Kind != DecisionKind.PlayCard) return false;
        var source = GetViewLegalActions().FirstOrDefault(action =>
            IsWushengSource(action.ConversionSource) && action.ConversionSource!.SkillId == skill.ContentId)?.ConversionSource;
        if (source is null) return false;
        var cancel = IsWushengSelectionPending && _selectedConversionSource == source;
        ClearSelection();
        if (cancel) return true;
        _isSelectingWusheng = true;
        _selectedConversionSource = source;
        _humanSkillSnapshot = null;
        SelectedCardText = "已选择【武圣】· 请选择红色牌当【杀】使用";
        RefreshCurrentView();
        return true;
    }
}
