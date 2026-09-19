using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private GeneralChoiceViewModel? _selectedGeneralChoice;
    private PromptId? _previewGeneralPromptId;

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
            ((RelayCommand)ConfirmGeneralChoiceCommand).NotifyCanExecuteChanged();
        }
    }

    public string SelectedGeneralChoiceText => SelectedGeneralChoice is null
        ? "先查看候选武将，再确认出征"
        : $"已选 {SelectedGeneralChoice.Name} · {SelectedGeneralChoice.Kingdom}势力 · {SelectedGeneralChoice.SkillName}";

    public bool CanConfirmGeneralChoice => IsGeneralSelectionPending && SelectedGeneralChoice is not null;
    public ICommand PreviewGeneralChoiceCommand { get; private set; } = null!;
    public ICommand ConfirmGeneralChoiceCommand { get; private set; } = null!;

    private void InitializeGeneralSelectionPresentation()
    {
        PreviewGeneralChoiceCommand = new RelayCommand<GeneralChoiceViewModel>(choice => SelectedGeneralChoice = choice);
        ConfirmGeneralChoiceCommand = new RelayCommand(() =>
        {
            if (SelectedGeneralChoice is { } choice) SelectGeneral(choice);
        }, () => CanConfirmGeneralChoice);
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
        SelectedGeneralChoice = GeneralChoices.FirstOrDefault(choice => choice.GeneralId == selectedId);
    }
}
