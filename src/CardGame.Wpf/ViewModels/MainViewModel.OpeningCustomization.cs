using System.Windows.Input;
using CardGame.Core;
using CardGame.Wpf.Presentation;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private string _customGeneralSearch = string.Empty;
    private string _selectedCustomGeneralCategory = "all";
    private GeneralChoiceViewModel[] _orderedCustomGeneralChoices = [];
    public ObservableRangeCollection<GeneralChoiceViewModel> CustomGeneralChoices { get; } = [];
    public ObservableRangeCollection<GeneralRosterCategoryOption> CustomGeneralCategories { get; } = [];
    public string SelectedCustomGeneralCategory => _selectedCustomGeneralCategory;
    public string CustomGeneralCategoryText => SelectedCustomGeneralCategory == "all" ? "全部可选武将" :
        $"{GeneralRosterCatalog.Categories.First(option => option.Id == SelectedCustomGeneralCategory).Name} · 可选武将";
    public string CustomGeneralSearch
    {
        get => _customGeneralSearch;
        set { if (SetProperty(ref _customGeneralSearch, value ?? string.Empty)) RefreshCustomGeneralChoices(); }
    }
    public bool IsCustomGeneralSelectionOpen => IsGeneralSelectionPending &&
        _snapshot.PendingDecision!.Choices.Any(choice => choice.Parameters.ContainsKey("full-roster"));
    public string GeneralSelectionSourceText => IsCustomGeneralSelectionOpen ? "返回候选" : "自选武将";
    public string CustomGeneralCountText => $"{CustomGeneralChoices.Count} / {_orderedCustomGeneralChoices.Length} 名武将";
    public bool HasNoCustomGeneralChoices => IsCustomGeneralSelectionOpen && CustomGeneralChoices.Count == 0;
    public bool CanCustomizeStartingIdentity => !IsTutorialActive && _snapshot?.ModeKind == ContentModeKind.Identity &&
        _snapshot.TurnNumber == 0 && _snapshot.PendingDecision?.Kind == DecisionKind.SelectGeneral;
    public ICommand ToggleGeneralRosterCommand { get; private set; } = null!;
    public ICommand ChangeStartingIdentityCommand { get; private set; } = null!;
    public ICommand SelectCustomGeneralCategoryCommand { get; private set; } = null!;

    private void InitializeOpeningCustomization()
    {
        ToggleGeneralRosterCommand = new RelayCommand(ToggleGeneralRoster,
            () => !IsTutorialActive && IsGeneralSelectionPending && CanExpandGeneralRoster);
        ChangeStartingIdentityCommand = new RelayCommand<StartingRoleOption>(ChangeStartingIdentity,
            option => CanCustomizeStartingIdentity && StartingRoles.Contains(option));
        SelectCustomGeneralCategoryCommand = new RelayCommand<string>(category =>
        {
            if (!IsCustomGeneralSelectionOpen || !CustomGeneralCategories.Any(option => option.Id == category) ||
                _selectedCustomGeneralCategory == category) return;
            _selectedCustomGeneralCategory = category;
            RaisePropertyChanged(nameof(SelectedCustomGeneralCategory));
            RaisePropertyChanged(nameof(CustomGeneralCategoryText));
            RefreshCustomGeneralChoices();
            PreviewVisibleCustomGeneral();
        });
    }

    private void ToggleGeneralRoster()
    {
        if (IsTutorialActive || !CanExpandGeneralRoster || _snapshot.PendingDecision is not { Kind: DecisionKind.SelectGeneral } prompt) return;
        ExecuteSafely(() =>
        {
            var result = SubmitCommand(new RequestGeneralRosterCommand(_snapshot.HumanSeat,
                !IsCustomGeneralSelectionOpen, _snapshot.Revision, prompt.PromptId));
            RefreshCommandResult(result.State);
        });
    }

    private void ChangeStartingIdentity(StartingRoleOption option)
    {
        if (!CanCustomizeStartingIdentity || !StartingRoles.Contains(option)) return;
        var reveal = IsIdentityRevealOpen;
        SelectedStartingRole = option;
        var options = _game.CreateCheckpoint().Options with { HumanRole = option.Role };
        ExecuteSafely(() =>
        {
            ReplaceEngine(GameEngine.CreateStandard(options, _contentRegistry));
            var result = SubmitCommand(new StartGameCommand());
            RefreshCommandResult(result.State);
            IsIdentityRevealOpen = reveal;
        });
    }

    private void RefreshOpeningCustomization()
    {
        _orderedCustomGeneralChoices = IsCustomGeneralSelectionOpen
            ? _generalSelectionChoices.Values.OrderBy(choice => choice.Kingdom).ThenBy(choice => choice.Name).ToArray()
            : [];
        var counts = _orderedCustomGeneralChoices.GroupBy(choice => choice.RosterCategoryId)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        CustomGeneralCategories.ReplaceAll(!IsCustomGeneralSelectionOpen ? [] : GeneralRosterCatalog.Categories
            .Where(category => category.Id == "all" || counts.ContainsKey(category.Id))
            .Select(category => new GeneralRosterCategoryOption(category.Id, category.Name,
                category.Id == "all" ? _orderedCustomGeneralChoices.Length : counts[category.Id])));
        if (IsCustomGeneralSelectionOpen && !CustomGeneralCategories.Any(option => option.Id == SelectedCustomGeneralCategory))
        {
            _selectedCustomGeneralCategory = "all";
            RaisePropertyChanged(nameof(SelectedCustomGeneralCategory));
        }
        RefreshCustomGeneralChoices();
        if (SelectedCustomGeneralCategory != "all") PreviewVisibleCustomGeneral();
        RaisePropertyChanged(nameof(CustomGeneralCategoryText));
        RaisePropertyChanged(nameof(IsCustomGeneralSelectionOpen));
        RaisePropertyChanged(nameof(GeneralSelectionSourceText));
        RaisePropertyChanged(nameof(CanCustomizeStartingIdentity));
        (ToggleGeneralRosterCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ChangeStartingIdentityCommand as RelayCommand<StartingRoleOption>)?.NotifyCanExecuteChanged();
    }

    private void PreviewVisibleCustomGeneral()
    {
        if (CustomGeneralChoices.Count > 0 && !CustomGeneralChoices.Contains(SelectedGeneralChoice!))
            PreviewGeneralChoice(CustomGeneralChoices[0]);
    }

    private void RefreshCustomGeneralChoices()
    {
        var search = CustomGeneralSearch.Trim();
        CustomGeneralChoices.ReplaceAll(!IsCustomGeneralSelectionOpen ? [] :
            _orderedCustomGeneralChoices
            .Where(choice => SelectedCustomGeneralCategory == "all" || choice.RosterCategoryId == SelectedCustomGeneralCategory)
            .Where(choice => search.Length == 0 ||
                choice.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                choice.GeneralId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                choice.Kingdom.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                choice.EditionText.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                choice.SkillName.Contains(search, StringComparison.OrdinalIgnoreCase)));
        RaisePropertyChanged(nameof(CustomGeneralCountText));
        RaisePropertyChanged(nameof(HasNoCustomGeneralChoices));
    }
}
