using System.Collections.ObjectModel;
using System.Windows.Input;
using CardGame.Core.Campaigns;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private string? _shanheBossRewardGroupId;
    private readonly List<ShanheCampaignChoice> _shanheBossRewardChoices = [];
    public ObservableCollection<ShanheBossRewardGroup> ShanheBossRewardGroups { get; } = [];
    private ShanheBossRewardGroupSnapshot? ActiveShanheBossRewardGroup => _shanheSession?.Snapshot.BossRewardSummary?.Groups
        .FirstOrDefault(group => group.Id == _shanheBossRewardGroupId && !group.IsResolved);
    public bool HasShanheBossRewardGroup => ActiveShanheBossRewardGroup is not null;
    public bool IsShanheBossRewardOpen => _shanheSession?.Snapshot.BossRewardSummary is not null && !HasShanheBossRewardGroup;
    public string ShanheBossRewardTitle => (_shanheSession?.Snapshot.BossRewardSummary?.Title ?? "章节首领") + " · 奖 励";
    public string ShanheBossFixedGoldText => $"铜币 +{_shanheSession?.Snapshot.BossRewardSummary?.FixedGold ?? 0}";
    public string ShanheBossSkillSlotsText => $"技能槽 +{_shanheSession?.Snapshot.BossRewardSummary?.SkillSlots ?? 0}";
    public string ShanheBossRewardContinueText => _shanheSession?.Snapshot.BossRewardSummary?.ChapterNumber is >= 3
        ? "完 成 征 程" : "进 入 下 一 章";
    public ICommand OpenShanheBossRewardGroupCommand { get; private set; } = null!;
    public ICommand CloseShanheBossRewardGroupCommand { get; private set; } = null!;
    public ICommand ConfirmShanheBossRewardCommand { get; private set; } = null!;
    private IEnumerable<ShanheCampaignChoice> CurrentShanheRewardChoices => IsShanheOpeningSkillDraft ? _shanheOpeningChoices :
        HasShanheBossRewardGroup ? _shanheBossRewardChoices : ShanheChoices;
    private bool IsCurrentShanheRewardChoice(ShanheCampaignChoice choice) => choice.IsEnabled &&
        _shanheSession?.Revision == choice.Revision && CurrentShanheRewardChoices.Any(current => ReferenceEquals(current, choice));

    private void InitializeShanheBossRewards()
    {
        OpenShanheBossRewardGroupCommand = new RelayCommand<ShanheBossRewardGroup>(group =>
        {
            if (!CanOpenShanheBossRewardGroup(group)) return;
            _shanheBossRewardGroupId = group.Id;
            RefreshShanheRewards();
        }, CanOpenShanheBossRewardGroup);
        CloseShanheBossRewardGroupCommand = new RelayCommand(() =>
        {
            _shanheBossRewardGroupId = null;
            RefreshShanheRewards();
        }, () => HasShanheBossRewardGroup);
        ConfirmShanheBossRewardCommand = new RelayCommand(() =>
        {
            if (!CanConfirmShanheBossRewards()) return;
            ApplyShanheCommand(new ConfirmShanheBossRewardCommand(_shanheSession!.Revision));
        }, CanConfirmShanheBossRewards);
    }

    private bool CanOpenShanheBossRewardGroup(ShanheBossRewardGroup group) => !IsTutorialActive && !IsShanheSkillReplacementOpen && !group.IsResolved &&
        _shanheSession?.Revision == group.Revision && ShanheBossRewardGroups.Contains(group);
    private bool CanConfirmShanheBossRewards() => !IsTutorialActive && IsShanheBossRewardOpen &&
        _shanheSession?.Snapshot.BossRewardSummary?.CanConfirm == true;

    private void RefreshShanheBossRewards()
    {
        ShanheBossRewardGroups.Clear();
        _shanheBossRewardChoices.Clear();
        var state = _shanheSession?.Snapshot;
        if (state?.BossRewardSummary is { } summary)
        {
            if (ActiveShanheBossRewardGroup is null) _shanheBossRewardGroupId = null;
            foreach (var group in summary.Groups)
                ShanheBossRewardGroups.Add(new(group.Id, group.Name, group.SelectedReward?.Name ?? (group.IsExhausted ? "本组已集齐" : "多 选 一"),
                    group.IsResolved, group.Id == _shanheBossRewardGroupId, state.Revision));
            if (ActiveShanheBossRewardGroup is { } active)
                foreach (var reward in active.Offers)
                {
                    var alreadyKnown = reward.SkillId is { } skillId && state.ExtraSkillIds.Contains(skillId);
                    if (reward.SkillId is not null && !alreadyKnown && state.ExtraSkillIds.Count >= state.ExtraSkillSlots + reward.AddSkillSlots)
                        foreach (var replaced in state.ExtraSkillIds)
                            _shanheBossRewardChoices.Add(new(reward.Id, $"{reward.Name} · 替换{ShanheSkillName(replaced)}",
                                reward.Description, true, replaced, true, state.Revision));
                    else _shanheBossRewardChoices.Add(new(reward.Id, reward.Name, reward.Description, true, null, !alreadyKnown, state.Revision));
                }
        }
        else _shanheBossRewardGroupId = null;
        foreach (var property in new[] { nameof(HasShanheBossRewardGroup), nameof(IsShanheBossRewardOpen), nameof(ShanheBossRewardTitle),
                     nameof(ShanheBossFixedGoldText), nameof(ShanheBossSkillSlotsText), nameof(ShanheBossRewardContinueText) }) RaisePropertyChanged(property);
        (CloseShanheBossRewardGroupCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ConfirmShanheBossRewardCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }
}

public sealed record ShanheBossRewardGroup(string Id, string Name, string SelectedName, bool IsResolved, bool IsSelected, long Revision);
