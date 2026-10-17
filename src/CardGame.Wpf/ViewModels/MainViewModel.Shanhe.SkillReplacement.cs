using System.Collections.ObjectModel;
using System.Windows.Input;
using CardGame.Content.Standard;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private bool _isShanheSkillReplacementOpen;
    private string _shanheReplacementIncomingName = string.Empty;
    private Action<ShanheCampaignChoice>? _shanheReplacementCommit;
    private Func<ShanheCampaignChoice, bool>? _shanheReplacementIsCurrent;
    private ShanheSkillReplacementOption? _selectedShanheReplacement;
    public ObservableCollection<ShanheSkillReplacementOption> ShanheSkillReplacementOptions { get; } = [];
    public bool IsShanheSkillReplacementOpen { get => _isShanheSkillReplacementOpen; private set => SetProperty(ref _isShanheSkillReplacementOpen, value); }
    public string ShanheReplacementIncomingName { get => _shanheReplacementIncomingName; private set => SetProperty(ref _shanheReplacementIncomingName, value); }
    public ICommand SelectShanheSkillReplacementCommand { get; private set; } = null!;
    public ICommand ConfirmShanheSkillReplacementCommand { get; private set; } = null!;
    public ICommand CancelShanheSkillReplacementCommand { get; private set; } = null!;
    public ICommand RequestShanheShopPurchaseCommand { get; private set; } = null!;

    private void InitializeShanheSkillReplacement()
    {
        SelectShanheSkillReplacementCommand = new RelayCommand<ShanheSkillReplacementOption>(option =>
        {
            if (!CanSelectShanheReplacement(option)) return;
            foreach (var item in ShanheSkillReplacementOptions) item.IsSelected = ReferenceEquals(item, option);
            _selectedShanheReplacement = option;
            (ConfirmShanheSkillReplacementCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }, CanSelectShanheReplacement);
        CancelShanheSkillReplacementCommand = new RelayCommand(CancelShanheSkillReplacement, () => IsShanheSkillReplacementOpen);
        ConfirmShanheSkillReplacementCommand = new RelayCommand(() =>
        {
            if (!CanConfirmShanheReplacement() || _selectedShanheReplacement?.Choice is not { } choice) return;
            var commit = _shanheReplacementCommit;
            CancelShanheSkillReplacement();
            commit?.Invoke(choice);
        }, CanConfirmShanheReplacement);
        RequestShanheShopPurchaseCommand = new RelayCommand<ShanheShopItem>(item =>
        {
            if (!CanRequestShanheShopPurchase(item) || item.Choice is not { } choice) return;
            if (choice.ReplaceSkillId is null) { ChooseShanheOption(choice); return; }
            OpenShanheSkillReplacement(item.Name, ShanheChoices.Where(option => option.Id == choice.Id &&
                option.ReplaceSkillId is not null && option.IsEnabled).ToArray(), ChooseShanheOption, IsCurrentShanheChoice);
        }, CanRequestShanheShopPurchase);
    }

    private bool CanRequestShanheShopPurchase(ShanheShopItem item) => !IsTutorialActive && !IsShanheSkillReplacementOpen &&
        ShanheShopItems.Contains(item) && item.CanBuy && item.Choice is { } choice && IsCurrentShanheChoice(choice);
    private bool CanSelectShanheReplacement(ShanheSkillReplacementOption option) => !IsTutorialActive && IsShanheSkillReplacementOpen &&
        ShanheSkillReplacementOptions.Contains(option) && option.Choice is { } choice && _shanheReplacementIsCurrent?.Invoke(choice) == true;
    private bool CanConfirmShanheReplacement() => _selectedShanheReplacement is { } selected && CanSelectShanheReplacement(selected);

    private void OpenShanheSkillReplacement(string incomingName, IReadOnlyList<ShanheCampaignChoice> choices,
        Action<ShanheCampaignChoice> commit, Func<ShanheCampaignChoice, bool> isCurrent)
    {
        if (_shanheSession?.Snapshot is not { } state || choices.Count == 0 || !choices.All(isCurrent)) return;
        ShanheSkillReplacementOptions.Clear();
        _selectedShanheReplacement = null;
        _shanheReplacementCommit = commit;
        _shanheReplacementIsCurrent = isCurrent;
        ShanheReplacementIncomingName = incomingName;
        var registry = CurrentPresentationRegistry.Generals.ContainsKey(state.PlayerGeneralId) ? CurrentPresentationRegistry :
            StandardShanheCampaignCatalog.PlayerRosterRegistry;
        var printed = registry.Generals.TryGetValue(state.PlayerGeneralId, out var general) ? general.SkillIds : [];
        foreach (var skillId in printed.Concat(state.ExtraSkillIds).Distinct(StringComparer.Ordinal))
        {
            var choice = printed.Contains(skillId) ? null : choices.FirstOrDefault(option => option.ReplaceSkillId == skillId);
            var skill = registry.Skills.GetValueOrDefault(skillId);
            ShanheSkillReplacementOptions.Add(new(skillId, skill?.Name ?? ShanheSkillName(skillId), skill?.Description ?? string.Empty, choice));
        }
        IsShanheSkillReplacementOpen = true;
        (ConfirmShanheSkillReplacementCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CancelShanheSkillReplacementCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ConfirmShanheRewardCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (RequestShanheShopPurchaseCommand as RelayCommand<ShanheShopItem>)?.NotifyCanExecuteChanged();
    }

    private void CancelShanheSkillReplacement()
    {
        IsShanheSkillReplacementOpen = false;
        _selectedShanheReplacement = null;
        _shanheReplacementCommit = null;
        _shanheReplacementIsCurrent = null;
        ShanheSkillReplacementOptions.Clear();
        (ConfirmShanheSkillReplacementCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CancelShanheSkillReplacementCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ConfirmShanheRewardCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (RequestShanheShopPurchaseCommand as RelayCommand<ShanheShopItem>)?.NotifyCanExecuteChanged();
    }

    private void ValidateShanheSkillReplacement()
    {
        if (IsShanheSkillReplacementOpen && ShanheSkillReplacementOptions.Where(option => option.Choice is not null)
            .Any(option => _shanheReplacementIsCurrent?.Invoke(option.Choice!) != true)) CancelShanheSkillReplacement();
    }
}

public sealed class ShanheSkillReplacementOption(string skillId, string name, string description, ShanheCampaignChoice? choice) : ObservableObject
{
    private bool _isSelected;
    public string SkillId { get; } = skillId;
    public string Name { get; } = name;
    public string Description { get; } = description;
    internal ShanheCampaignChoice? Choice { get; } = choice;
    public bool IsLocked => Choice is null;
    public bool IsEnabled => !IsLocked;
    public bool IsSelected { get => _isSelected; internal set => SetProperty(ref _isSelected, value); }
}
