using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    public bool IsStoneAxeSelectionPending => _snapshot?.PendingDecision is { Kind: DecisionKind.StoneAxe } prompt &&
        prompt.PlayerSeat == _snapshot.HumanSeat;

    // Weapon costs use the same published-card draft as skill payments.
    public ICommand SelectStoneAxeEquipmentCommand => SelectPaymentEquipmentCommand;

}
