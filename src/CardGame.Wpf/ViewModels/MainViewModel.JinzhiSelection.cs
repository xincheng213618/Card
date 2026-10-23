using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private const string JinzhiSkillId = "wushuang:skill-3252";
    private const string JinzhiDraftId = "jinzhi-material-selection";
    private CardKind? _selectedJinzhiKind;
    private WushuangJinzhiMaterialSelection? _jinzhiMaterials;
    private LegalAction? _jinzhiOutputAction;
    private LegalAction? _jinzhiDraftAction;

    private static bool IsJinzhiDraft(LegalAction action) =>
        action.ProgramSkillId == JinzhiSkillId && action.ProgramActivationId == JinzhiDraftId;

    private IEnumerable<LegalAction> JinzhiMatchingActions => GetViewLegalActions().Where(action =>
        action.ProgramSkillId == JinzhiSkillId && action.ConversionSource is not null &&
        _selectedActiveSkillCardIds.SetEquals(action.SelectableCardIds));

    private LegalAction? SelectedJinzhiAction => _selectedJinzhiKind is { } kind
        ? JinzhiMatchingActions.FirstOrDefault(action => action.PlayedCardKind == kind) : null;

    // This entry describes a local, unpaid material draft. It is never submitted
    // to Core: confirmation resolves the exact current published native action.
    private LegalAction? JinzhiSelectionAction
    {
        get
        {
            var materials = GetViewAction().JinzhiMaterials;
            if (materials is null) return null;
            var output = SelectedJinzhiAction;
            if (ReferenceEquals(materials, _jinzhiMaterials) && ReferenceEquals(output, _jinzhiOutputAction))
                return _jinzhiDraftAction;
            _jinzhiMaterials = materials;
            _jinzhiOutputAction = output;
            return _jinzhiDraftAction = new(LegalActionKind.UseProgramSkill, null, null, "发动【锦织】",
                output?.PlayedCardKind, MinCardCount: materials.RequiredCount, MaxCardCount: materials.RequiredCount,
                MinTargetCount: output?.MinTargetCount ?? 0, MaxTargetCount: output?.MaxTargetCount ?? 0)
            {
                ProgramSkillId = JinzhiSkillId, ProgramActivationId = JinzhiDraftId,
                SelectableCardIds = materials.CardIds, SelectableTargetSeats = output?.SelectableTargetSeats ?? []
            };
        }
    }

    private bool IsJinzhiSelectionPending => _isSelectingActiveSkillCards &&
        JinzhiSelectionAction is { } draft && _selectedActiveSkillIdentity?.Matches(draft) == true;

    private void AddJinzhiCardNameChoices()
    {
        if (!IsJinzhiSelectionPending || _selectedJinzhiKind is not null) return;
        foreach (var action in JinzhiMatchingActions.DistinctBy(action => action.PlayedCardKind))
        {
            var kind = action.PlayedCardKind!.Value;
            EquipmentPlayChoices.Add(new(new ChoiceId($"jinzhi-card-name-{kind}"),
                $"锦织：视为使用【{CardCatalog.Get(kind).DisplayName}】", [], [],
                new Dictionary<string, string> { ["action"] = "select-jinzhi-card-name", ["played-kind"] = kind.ToString() }));
        }
    }

    private bool TrySelectJinzhiCardName(PromptChoice choice)
    {
        if (choice.Parameters.GetValueOrDefault("action") != "select-jinzhi-card-name") return false;
        if (!IsJinzhiSelectionPending || !Enum.TryParse<CardKind>(choice.Parameters.GetValueOrDefault("played-kind"), out var kind) ||
            !JinzhiMatchingActions.Any(action => action.PlayedCardKind == kind)) return true;
        _selectedJinzhiKind = kind;
        _selectedActiveSkillTargetSeats.Clear();
        SelectOnlyActiveSkillTarget();
        RefreshCurrentView();
        return true;
    }

    private void RefreshJinzhiMaterialSelection()
    {
        if (!IsJinzhiSelectionPending) return;
        _selectedJinzhiKind = null;
        _selectedActiveSkillTargetSeats.Clear();
        RebuildEquipmentPlayChoices(_snapshot.Players.Single(player => player.IsHuman), GetViewLegalActions());
    }

    private string JinzhiSelectionHint
    {
        get
        {
            var count = GetViewAction().JinzhiMaterials!.RequiredCount;
            var selected = _selectedActiveSkillCardIds.Count;
            var prefix = $"【锦织】本轮第 {count} 次 · 选择 {count} 张同颜色的牌（已选 {selected} 张）";
            if (selected != count) return prefix + " · 选够后选择基本牌；取消不发动。";
            if (!JinzhiMatchingActions.Any()) return prefix + " · 颜色不一致或当前无可用基本牌；请重新选牌或取消。";
            if (_selectedJinzhiKind is not { } kind) return prefix + " · 请选择要使用的基本牌。";
            return prefix + $" · 视为【{CardCatalog.Get(kind).DisplayName}】 · " +
                (CanConfirmActiveSkill ? "点击「确定」使用；取消不发动。" : "请选择目标后确认；取消不发动。");
        }
    }
}
