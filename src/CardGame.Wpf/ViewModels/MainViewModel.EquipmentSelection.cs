using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private bool IsActiveEquipmentMaterial(CardSnapshot? card) => card is not null &&
        IsActiveSkillCardSelectionPending && HumanActiveSkillAction!.SelectableCardIds.Contains(card.Id);

    private bool IsSelectedEquipmentActivation(CardSnapshot? card) => card is not null &&
        IsActiveSkillSelectionPending && HumanActiveSkillAction?.EquipmentKind == card.Kind;

    private LegalAction? EquipmentActivation(CardSnapshot? card) => card is null ? null :
        HumanActiveSkillActions.FirstOrDefault(action => action.EquipmentKind == card.Kind);

    private bool IsPlayConversionEquipmentMaterial(CardSnapshot? card) => card is not null &&
        IsPlayConversionSelectionPending && GetViewLegalActions().Any(action =>
            action.CardId == card.Id && action.PlayedCardKind is not null && MatchesSelectedPlayConversion(action));

    private bool CanSelectActiveEquipment(CardSnapshot? card) => IsActiveSkillSelectionPending
        ? IsActiveEquipmentMaterial(card) || IsSelectedEquipmentActivation(card)
        : IsPlayConversionSelectionPending ? IsPlayConversionEquipmentMaterial(card)
        : EquipmentActivation(card) is not null;

    private ICommand? _selectActiveEquipmentCommand;
    public ICommand SelectActiveEquipmentCommand => _selectActiveEquipmentCommand ??=
        new RelayCommand<EquipmentSlotViewModel>(slot =>
        {
            if (!HumanEquipmentSlots.Any(current => ReferenceEquals(current, slot)) || !CanSelectActiveEquipment(slot.Card)) return;
            if (IsActiveEquipmentMaterial(slot.Card)) ToggleActiveSkillCard(slot.Card!.Id);
            else if (IsSelectedEquipmentActivation(slot.Card)) ClearSelection();
            else if (IsPlayConversionEquipmentMaterial(slot.Card)) SelectRegionalPlayCard(slot.Card!);
            else if (!IsActiveSkillSelectionPending && EquipmentActivation(slot.Card) is { } action)
                SelectActiveSkillCommand.Execute(action);
        }, slot => HumanEquipmentSlots.Any(current => ReferenceEquals(current, slot)) && CanSelectActiveEquipment(slot.Card));
}
