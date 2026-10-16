using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private GeneralChoiceViewModel? _selectedGeneralChoice;
    private IReadOnlyList<GeneralChoiceViewModel> _selectedGeneralVariants = [];
    private PromptId? _previewGeneralPromptId;
    private readonly Dictionary<string, GeneralChoiceViewModel> _generalSelectionChoices = new(StringComparer.Ordinal);

    public GeneralChoiceViewModel? SelectedGeneralChoice
    {
        get => _selectedGeneralChoice;
        private set
        {
            if (ReferenceEquals(_selectedGeneralChoice, value)) return;
            if (_selectedGeneralChoice is not null) _selectedGeneralChoice.IsPreviewSelected = false;
            var variants = value is null ? Array.Empty<GeneralChoiceViewModel>() :
                _generalSelectionChoices.Values.Where(choice => choice.CandidateGeneralId == value.CandidateGeneralId).ToArray();
            var variantsChanged = !_selectedGeneralVariants.SequenceEqual(variants);
            // Keep version buttons attached while previewing the same family, but
            // replace their current choice objects after a real refresh or load.
            if (variantsChanged) _selectedGeneralVariants = Array.AsReadOnly(variants);
            if (!SetProperty(ref _selectedGeneralChoice, value)) return;
            if (value is not null) value.IsPreviewSelected = true;
            RaisePropertyChanged(nameof(SelectedGeneralChoiceText));
            RaisePropertyChanged(nameof(CanConfirmGeneralChoice));
            if (variantsChanged)
            {
                RaisePropertyChanged(nameof(SelectedGeneralVariants));
                RaisePropertyChanged(nameof(HasGeneralVariants));
            }
            ((RelayCommand)ConfirmGeneralChoiceCommand).NotifyCanExecuteChanged();
        }
    }

    public string SelectedGeneralChoiceText => SelectedGeneralChoice is null
        ? "先查看候选武将，再确认出征"
        : $"已选 {SelectedGeneralChoice.Name} · {SelectedGeneralChoice.Kingdom}势力 · {SelectedGeneralChoice.SkillName}";

    public bool CanConfirmGeneralChoice => IsGeneralSelectionPending && SelectedGeneralChoice is not null;
    public int GeneralChoiceColumns => GeneralChoices.Count > 3 ? (GeneralChoices.Count + 1) / 2 : Math.Max(1, GeneralChoices.Count);
    public IReadOnlyList<GeneralChoiceViewModel> CandidateGeneralChoices => IsCustomGeneralSelectionOpen ? [] : GeneralChoices;
    public IReadOnlyList<GeneralChoiceViewModel> SelectedGeneralVariants => _selectedGeneralVariants;
    public bool HasGeneralVariants => SelectedGeneralVariants.Count > 1;
    public ICommand PreviewGeneralChoiceCommand { get; private set; } = null!;
    public ICommand ConfirmGeneralChoiceCommand { get; private set; } = null!;

    private void InitializeGeneralSelectionPresentation()
    {
        InitializeOpeningCustomization();
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

    private void SyncGeneralChoicePreview(PromptId? promptId,
        IReadOnlyList<GeneralChoiceViewModel> previousChoices, IReadOnlyList<GeneralChoiceViewModel> previousVariants)
    {
        if (promptId is null)
        {
            _previewGeneralPromptId = null;
            SelectedGeneralChoice = null;
            return;
        }

        if (_previewGeneralPromptId != promptId || previousChoices.Count != GeneralChoices.Count ||
            previousVariants.Count != _generalSelectionChoices.Count || previousVariants.Any(previous =>
                !_generalSelectionChoices.TryGetValue(previous.GeneralId, out var current) ||
                current.CandidateGeneralId != previous.CandidateGeneralId || current.ChoiceId != previous.ChoiceId))
        {
            _previewGeneralPromptId = promptId;
            SelectedGeneralChoice = GeneralChoices.FirstOrDefault();
            return;
        }

        var selectedId = SelectedGeneralChoice?.GeneralId;
        // Only an unchanged published candidate contract can retain its local
        // edition previews. Rebind each row before restoring the selected family.
        foreach (var previous in previousChoices)
            if (_generalSelectionChoices.TryGetValue(previous.GeneralId, out var choice)) PreviewGeneralChoice(choice);
        if (selectedId is not null && _generalSelectionChoices.TryGetValue(selectedId, out var selected)) PreviewGeneralChoice(selected);
        else SelectedGeneralChoice = GeneralChoices.FirstOrDefault();
    }
}
