using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private GeneralChoiceViewModel? _selectedGeneralChoice;
    private PromptId? _previewGeneralPromptId;
    private readonly Dictionary<string, GeneralChoiceViewModel> _generalSelectionChoices = new(StringComparer.Ordinal);

    public GeneralChoiceViewModel? SelectedGeneralChoice
    {
        get => _selectedGeneralChoice;
        private set
        {
            if (ReferenceEquals(_selectedGeneralChoice, value)) return;
            if (_selectedGeneralChoice is not null) _selectedGeneralChoice.IsPreviewSelected = false;
            if (!SetProperty(ref _selectedGeneralChoice, value)) return;
            if (value is not null) value.IsPreviewSelected = true;
            RaisePropertyChanged(nameof(SelectedGeneralChoiceText));
            RaisePropertyChanged(nameof(CanConfirmGeneralChoice));
            RaisePropertyChanged(nameof(SelectedGeneralVariants));
            RaisePropertyChanged(nameof(HasGeneralVariants));
            ((RelayCommand)ConfirmGeneralChoiceCommand).NotifyCanExecuteChanged();
        }
    }

    public string SelectedGeneralChoiceText => SelectedGeneralChoice is null
        ? "先查看候选武将，再确认出征"
        : $"已选 {SelectedGeneralChoice.Name} · {SelectedGeneralChoice.Kingdom}势力 · {SelectedGeneralChoice.SkillName}";

    public bool CanConfirmGeneralChoice => IsGeneralSelectionPending && SelectedGeneralChoice is not null;
    public int GeneralChoiceColumns => GeneralChoices.Count > 3 ? (GeneralChoices.Count + 1) / 2 : Math.Max(1, GeneralChoices.Count);
    public IReadOnlyList<GeneralChoiceViewModel> SelectedGeneralVariants => SelectedGeneralChoice is not { } selected ? [] :
        _generalSelectionChoices.Values.Where(choice => choice.CandidateGeneralId == selected.CandidateGeneralId).ToArray();
    public bool HasGeneralVariants => SelectedGeneralVariants.Count > 1;
    public ICommand PreviewGeneralChoiceCommand { get; private set; } = null!;
    public ICommand ConfirmGeneralChoiceCommand { get; private set; } = null!;

    private void InitializeGeneralSelectionPresentation()
    {
        PreviewGeneralChoiceCommand = new RelayCommand<GeneralChoiceViewModel>(PreviewGeneralChoice);
        ConfirmGeneralChoiceCommand = new RelayCommand(() =>
        {
            if (SelectedGeneralChoice is { } choice) SelectGeneral(choice);
        }, () => CanConfirmGeneralChoice);
    }

    private void PreviewGeneralChoice(GeneralChoiceViewModel choice)
    {
        if (!IsGeneralSelectionPending || !_generalSelectionChoices.TryGetValue(choice.GeneralId, out var current) ||
            !ReferenceEquals(current, choice)) return;
        var index = GeneralChoices.ToList().FindIndex(candidate => candidate.CandidateGeneralId == choice.CandidateGeneralId);
        if (index < 0) return;
        if (!ReferenceEquals(GeneralChoices[index], choice)) GeneralChoices[index] = choice;
        SelectedGeneralChoice = choice;
    }

    private void SyncGeneralChoicePreview(PromptId? promptId)
    {
        if (promptId is null)
        {
            _previewGeneralPromptId = null;
            SelectedGeneralChoice = null;
            return;
        }

        if (_previewGeneralPromptId != promptId)
        {
            _previewGeneralPromptId = promptId;
            SelectedGeneralChoice = GeneralChoices.FirstOrDefault();
            return;
        }

        var selectedId = SelectedGeneralChoice?.GeneralId;
        if (selectedId is not null && _generalSelectionChoices.TryGetValue(selectedId, out var selected)) PreviewGeneralChoice(selected);
        else SelectedGeneralChoice = GeneralChoices.FirstOrDefault();
    }
}
