using System.Collections.ObjectModel;
using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private string _customGeneralSearch = string.Empty;
    public ObservableCollection<GeneralChoiceViewModel> CustomGeneralChoices { get; } = [];
    public string CustomGeneralSearch
    {
        get => _customGeneralSearch;
        set { if (SetProperty(ref _customGeneralSearch, value ?? string.Empty)) RefreshCustomGeneralChoices(); }
    }
    public bool IsCustomGeneralSelectionOpen => IsGeneralSelectionPending &&
        _snapshot.PendingDecision!.Choices.Any(choice => choice.Parameters.ContainsKey("full-roster"));
    public string GeneralSelectionSourceText => IsCustomGeneralSelectionOpen ? "返回候选" : "自选武将";
    public string CustomGeneralCountText => $"可选 {CustomGeneralChoices.Count} 名武将 · 搜索姓名、版本或势力";
    public bool CanCustomizeStartingIdentity => !IsTutorialActive && _snapshot?.ModeKind == ContentModeKind.Identity &&
        _snapshot.TurnNumber == 0 && _snapshot.PendingDecision?.Kind == DecisionKind.SelectGeneral;
    public ICommand ToggleGeneralRosterCommand { get; private set; } = null!;
    public ICommand ChangeStartingIdentityCommand { get; private set; } = null!;

    private void InitializeOpeningCustomization()
    {
        ToggleGeneralRosterCommand = new RelayCommand(ToggleGeneralRoster,
            () => !IsTutorialActive && IsGeneralSelectionPending);
        ChangeStartingIdentityCommand = new RelayCommand<StartingRoleOption>(ChangeStartingIdentity,
            option => CanCustomizeStartingIdentity && StartingRoles.Contains(option));
    }

    private void ToggleGeneralRoster()
    {
        if (IsTutorialActive || _snapshot.PendingDecision is not { Kind: DecisionKind.SelectGeneral } prompt) return;
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
        RefreshCustomGeneralChoices();
        RaisePropertyChanged(nameof(IsCustomGeneralSelectionOpen));
        RaisePropertyChanged(nameof(GeneralSelectionSourceText));
        RaisePropertyChanged(nameof(CanCustomizeStartingIdentity));
        (ToggleGeneralRosterCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ChangeStartingIdentityCommand as RelayCommand<StartingRoleOption>)?.NotifyCanExecuteChanged();
    }

    private void RefreshCustomGeneralChoices()
    {
        CustomGeneralChoices.Clear();
        if (IsCustomGeneralSelectionOpen)
        {
            var search = CustomGeneralSearch.Trim();
            foreach (var choice in _generalSelectionChoices.Values.OrderBy(choice => choice.Kingdom).ThenBy(choice => choice.Name))
                if (search.Length == 0 || choice.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    choice.GeneralId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    choice.Kingdom.Contains(search, StringComparison.OrdinalIgnoreCase))
                    CustomGeneralChoices.Add(choice);
        }
        RaisePropertyChanged(nameof(CustomGeneralCountText));
    }
}
