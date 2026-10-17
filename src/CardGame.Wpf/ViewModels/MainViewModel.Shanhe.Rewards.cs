using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using CardGame.Core.Campaigns;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private ShanheRewardCard? _selectedShanheRewardCard;
    private ShanheCampaignSession? _shanheRewardOwner;
    private readonly List<ShanheCampaignChoice> _shanheOpeningChoices = [];
    private bool IsShanheOpeningSkillDraft => _shanheSession?.Snapshot.Phase == ShanheCampaignPhase.OpeningSkillDraft;

    public ObservableCollection<ShanheRewardCard> ShanheRewardCards { get; } = [];
    public ObservableCollection<ShanheCampaignChoice> SelectedShanheRewardReplacements { get; } = [];
    public bool IsShanheRewardOpen => IsShanheOpeningSkillDraft || _shanheSession?.Snapshot.Phase == ShanheCampaignPhase.Reward &&
        (_shanheSession.Snapshot.BossRewardSummary is null || HasShanheBossRewardGroup);
    public string ShanheRewardHeading => IsShanheOpeningSkillDraft ? "传 承 · 技 能 选 择" :
        ActiveShanheBossRewardGroup is { } group ? group.Id switch
        { "tactics" => "战 法 选 择", "cards" => "卡 牌 选 择", _ => group.Name + " · 选择" } :
        _shanheSession?.Snapshot.RewardOffers is { Count: > 0 } offers && offers.All(reward => reward.SkillId is not null)
            ? "技 能 选 择" : "战 利 品 选 择";
    public string ShanheRewardNotice => IsShanheOpeningSkillDraft ? "传承已触发。从三个随机稀有技能中选择一项，获得后开始征程。" :
        HasShanheBossRewardGroup ? "从本组候选中选择一项，确认后回到首领奖励。每组奖励分别领取。" :
        _shanheSession?.Snapshot is { CanSkipReward: true } state
        ? $"通关奖励已结算。选择一项收获，或放弃本次战利品，额外获得 {state.RewardSkipGold} 铜币。"
        : "通关奖励已结算。选择一项收获，带着新的力量继续前行。";
    public ShanheRewardCard? SelectedShanheRewardCard => _selectedShanheRewardCard;
    public bool ShanheRewardNeedsReplacement => SelectedShanheRewardReplacements.Count > 0;

    public ICommand SelectShanheRewardCardCommand { get; private set; } = null!;
    public ICommand ConfirmShanheRewardCommand { get; private set; } = null!;

    private void InitializeShanheRewards()
    {
        SelectShanheRewardCardCommand = new RelayCommand<ShanheRewardCard>(SelectShanheRewardCard, IsCurrentShanheRewardCard);
        ConfirmShanheRewardCommand = new RelayCommand(ConfirmShanheReward, CanConfirmShanheReward);
        InitializeShanheBossRewards();
        InitializeShanheSkillReplacement();
    }

    private bool IsCurrentShanheRewardCard(ShanheRewardCard card) => !IsTutorialActive && !IsShanheSkillReplacementOpen && IsShanheRewardOpen &&
        card.IsEnabled && _shanheSession?.Revision == card.Revision && ShanheRewardCards.Contains(card);

    private void SelectShanheRewardCard(ShanheRewardCard card)
    {
        if (!IsCurrentShanheRewardCard(card)) return;
        foreach (var item in ShanheRewardCards) item.IsSelected = ReferenceEquals(item, card);
        _selectedShanheRewardCard = card;
        SelectedShanheRewardReplacements.Clear();
        foreach (var choice in CurrentShanheRewardChoices.Where(choice => choice.IsReward && choice.Id == card.Id &&
                     choice.ReplaceSkillId is not null && choice.IsEnabled))
            SelectedShanheRewardReplacements.Add(choice);
        RaisePropertyChanged(nameof(SelectedShanheRewardCard));
        RaisePropertyChanged(nameof(ShanheRewardNeedsReplacement));
        (ConfirmShanheRewardCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private bool CanConfirmShanheReward()
    {
        if (_selectedShanheRewardCard is not { } card || !IsCurrentShanheRewardCard(card)) return false;
        if (card.IsSkip) return _shanheSession!.Snapshot.CanSkipReward;
        return ShanheRewardNeedsReplacement
            ? SelectedShanheRewardReplacements.Any(IsCurrentShanheRewardChoice)
            : CurrentShanheRewardChoices.Any(choice => choice.IsReward && choice.Id == card.Id &&
                choice.ReplaceSkillId is null && IsCurrentShanheRewardChoice(choice));
    }

    private void ConfirmShanheReward()
    {
        if (!CanConfirmShanheReward() || _selectedShanheRewardCard is not { } card) return;
        if (ShanheRewardNeedsReplacement)
        {
            OpenShanheSkillReplacement(card.Name, SelectedShanheRewardReplacements.ToArray(),
                choice => CompleteShanheRewardSelection(card, choice), IsCurrentShanheRewardChoice);
            return;
        }
        CompleteShanheRewardSelection(card, null);
    }

    private void CompleteShanheRewardSelection(ShanheRewardCard card, ShanheCampaignChoice? replacement)
    {
        if (!IsCurrentShanheRewardCard(card) || replacement is not null && !IsCurrentShanheRewardChoice(replacement)) return;
        if (IsShanheOpeningSkillDraft)
            ApplyShanheCommand(new ChooseShanheOpeningSkillCommand(card.Id, card.Revision));
        else if (ActiveShanheBossRewardGroup is { } group)
            ApplyShanheCommand(new ChooseShanheBossRewardCommand(group.Id, card.Id,
                replacement?.ReplaceSkillId, card.Revision));
        else if (card.IsSkip) ApplyShanheCommand(new SkipShanheRewardCommand(card.Revision));
        else ChooseShanheOption(replacement ??
            ShanheChoices.Single(choice => choice.IsReward && choice.Id == card.Id && choice.ReplaceSkillId is null));
    }

    private void RefreshShanheRewards()
    {
        if (!ReferenceEquals(_shanheRewardOwner, _shanheSession))
        {
            _shanheRewardOwner = _shanheSession;
            _selectedShanheRewardCard = null;
            _shanheBossRewardGroupId = null;
        }
        var previous = _selectedShanheRewardCard;
        RefreshShanheBossRewards();
        _shanheOpeningChoices.Clear();
        ShanheRewardCards.Clear();
        SelectedShanheRewardReplacements.Clear();
        _selectedShanheRewardCard = null;
        if (_shanheSession?.Snapshot is { } state && (state.Phase == ShanheCampaignPhase.Reward || IsShanheOpeningSkillDraft))
        {
            if (IsShanheOpeningSkillDraft)
                foreach (var reward in state.OpeningSkillOffers)
                    _shanheOpeningChoices.Add(new(reward.Id, reward.Name, reward.Description, true, null, true, state.Revision));
            foreach (var reward in IsShanheOpeningSkillDraft ? state.OpeningSkillOffers : ActiveShanheBossRewardGroup?.Offers ?? state.RewardOffers)
            {
                var rule = RewardRule(reward);
                var showSkillRule = reward.SkillId is not null && !string.IsNullOrWhiteSpace(rule);
                ShanheRewardCards.Add(new(reward.Id, reward.Name.Replace("传习·", ""), showSkillRule ? rule : reward.Description,
                    showSkillRule ? string.Empty : rule, RewardCategory(reward), false, "", state.Revision,
                    CurrentShanheRewardChoices.Any(choice => choice.IsReward && choice.Id == reward.Id && choice.IsEnabled))
                { CardImage = (reward.StartingCards ?? []).Concat(reward.StartingEquipment ?? []).FirstOrDefault() is { } card ? CardArt.Get(card.Kind) : null });
            }
            if (state.CanSkipReward && state.BossRewardSummary is null)
                ShanheRewardCards.Add(new("reward:skip", "放弃", "放弃本次战利品，将这份收获折为行军盘缠。",
                    "已获得的通关铜币照常保留。", "额外铜币", true, $"+ {state.RewardSkipGold} 铜币", state.Revision, true));
            if (previous?.Revision == state.Revision && ShanheRewardCards.FirstOrDefault(card => card.Id == previous.Id) is { } restored)
            {
                SelectShanheRewardCard(restored);
            }
        }
        foreach (var property in new[] { nameof(IsShanheRewardOpen), nameof(ShanheRewardHeading), nameof(ShanheRewardNotice),
                     nameof(SelectedShanheRewardCard), nameof(ShanheRewardNeedsReplacement) }) RaisePropertyChanged(property);
        (ConfirmShanheRewardCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }
}

public sealed class ShanheRewardCard(string id, string name, string description, string ruleDescription,
    string category, bool isSkip, string bonusText, long revision, bool isEnabled) : ObservableObject
{
    private bool _isSelected;
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public string RuleDescription { get; } = ruleDescription;
    public bool HasRuleDescription => !string.IsNullOrWhiteSpace(RuleDescription) && RuleDescription != Description;
    public string Category { get; } = category;
    public string CategoryText => Category;
    public ImageSource? CardImage { get; init; }
    public bool HasCardImage => CardImage is not null;
    public bool IsSkip { get; } = isSkip;
    public string BonusText { get; } = bonusText;
    public long Revision { get; } = revision;
    public bool IsEnabled { get; } = isEnabled;
    public bool IsSelected { get => _isSelected; internal set => SetProperty(ref _isSelected, value); }
    public string AccessibleText => $"{Name}，{Category}，{Description}，{BonusText}";
}
