using System.Collections.ObjectModel;
using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    public bool IsNationalModeSelection => SelectedTableMode is { } selection &&
        _contentRegistry.Modes.TryGetValue(selection.ModeId, out var mode) &&
        mode.ModeKind == ContentModeKind.NationalWarLite;
    private bool IsAmbitiousNationalMode => SelectedTableMode?.ModeId == "national:ambitious-6" ||
        _game.ModeId == "national:ambitious-6";
    public bool IsNationalSnapshot => _snapshot?.ModeKind == ContentModeKind.NationalWarLite;
    public string NationalSetupText => $"{NationalFactionDistributionText()}，势力由系统分配。依次选择两名同势力武将，暗置入场；出牌时可分别明置，明置后对应技能生效。消灭其他势力即可获胜。" + NationalHealthRuleText;
    public string NationalScopeText => IsAmbitiousNationalMode
        ? "六人魏蜀野心家独立势力试验；当前只验证三方人数、双将暗置/明置和势力胜负，不包含完整野心家规则、阵法、围攻、变更副将或国战专属牌堆。"
        : "四人魏蜀对抗，每势力两人，双将同势力；没有野心家、阵法、鏖战、明置奖励或完整国战专用牌堆。明置机会在自己的出牌阶段，沿用当前游戏的牌与技能规则。";
    public string NationalHealthRuleText => !IsNewGameSetupOpen && IsNationalSnapshot && _game.RulesVersion < 8
        ? "此存档沿用旧规则：体力上限固定为 4。"
        : "双将基础体力取平均、向下取整；选副将时可预览组合上限。开局满体力，明置不改变体力；当前国战切片没有半体力奖励。";
    public ObservableCollection<NationalRevealChoice> NationalRevealChoices { get; } = [];
    public ICommand RevealNationalGeneralCommand { get; private set; } = null!;
    private static string FactionName(string? faction) => faction switch
    {
        "wei" => "魏",
        "shu" => "蜀",
        "ambitious" => "野心家",
        "wu" => "吴",
        "qun" => "群",
        _ => "未明势力"
    };
    private string NationalSelectionSubtitle => _snapshot?.PendingDecision?.Prompt ?? "依次选择两名同势力武将";

    private string NationalFactionDistributionText()
    {
        var modeId = SelectedTableMode?.ModeId;
        if (modeId is null || !_contentRegistry.Modes.TryGetValue(modeId, out var mode) || mode.FactionCounts is null)
            return "国战势力人数待定";
        return string.Join("、", mode.FactionCounts
            .OrderBy(entry => FactionOrder(entry.Key))
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"{FactionName(entry.Key)} {entry.Value} 人"));
    }

    private static int FactionOrder(string factionId) => factionId switch
    {
        "wei" => 0,
        "shu" => 1,
        "wu" => 2,
        "qun" => 3,
        "ambitious" => 4,
        _ => 5
    };

    private void RefreshNationalPresentation()
    {
        NationalRevealChoices.Clear();
        if (IsNationalSnapshot && !IsTutorialActive)
            foreach (var action in _game.GetHumanLegalActions().Where(action => action.Kind == LegalActionKind.RevealGeneral))
                if (action.GeneralSlot is { } slot) NationalRevealChoices.Add(new(slot, action.Description));
        RaisePropertyChanged(nameof(IsNationalSnapshot));
        RaisePropertyChanged(nameof(NationalHealthRuleText));
        RaisePropertyChanged(nameof(NationalSetupText));
        RaisePropertyChanged(nameof(NationalScopeText));
    }

    private void RevealNationalGeneral(NationalRevealChoice choice)
    {
        if (!IsNationalSnapshot || IsTutorialActive ||
            _snapshot.PendingDecision is not
            {
                Kind: DecisionKind.PlayCard or DecisionKind.RespondDodge or DecisionKind.RespondSlash
            } prompt ||
            !_game.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.RevealGeneral && action.GeneralSlot == choice.Slot)) return;
        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new RevealGeneralCommand(_snapshot.HumanSeat, choice.Slot, _snapshot.Revision, prompt.PromptId));
            if (result.Accepted) ClearSelection();
            Refresh(result.State);
        });
    }

    private string DisplaySkillText(PlayerSnapshot player)
    {
        var primary = $"{player.SkillName}：{player.SkillDescription}";
        if (!IsNationalSnapshot) return primary;
        return GeneralSlotViewModel.FromPlayer(player, false, _game.RulesVersion).DetailText + "\n\n" +
            GeneralSlotViewModel.FromPlayer(player, true, _game.RulesVersion).DetailText;
    }

    private string NationalRelationship(PlayerSnapshot player)
    {
        if (player.IsHuman) return "自己";
        var ownFaction = _snapshot.Players.SingleOrDefault(seat => seat.IsHuman)?.FactionId;
        if (!player.IsFactionRevealed || player.FactionId is null || ownFaction is null) return "未明";
        return player.FactionId == ownFaction ? "同伴" : "对手";
    }
}

public sealed record NationalRevealChoice(GeneralSelectionSlot Slot, string Description);
