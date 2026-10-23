using CardGame.Core;
using CardGame.Wpf.Controls;
using System.Windows.Input;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private IReadOnlyList<PromptChoice> DockChoiceCandidates => IsHandResponsePending ? HandResponseExtraChoices
        : IsSkillSelectionPending ? CenterSkillChoices : [];

    public bool HasDockSkillChoices => !IsOwnedCardSelectionPending && !IsGodFactionSelectionPending &&
        // Larger catalogs use the lower wrap/scroll area instead of shrinking the button row.
        _snapshot?.PendingDecision is { } prompt && prompt.PlayerSeat == _snapshot.HumanSeat &&
        DockChoiceCandidates is { Count: > 0 and <= 6 } choices &&
        choices.All(choice => choice.Cards.Count == 0 && choice.ContentIds.Count == 0 &&
            !HasOpaqueRegionSlot(choice) && CardNameChoicePresentation.From(choice) is null);

    public IReadOnlyList<PromptChoice> DockSkillChoices => HasDockSkillChoices ? DockChoiceCandidates : [];
    public ICommand DockSkillChoiceCommand => IsHandResponsePending ? SelectHandResponseChoiceCommand : SelectSkillChoiceCommand;
    public bool ShowPrimaryActionButtons => !HasDockSkillChoices || IsSkillTargetSelectionPending || IsHandResponsePending;
    public IReadOnlyList<PromptChoice> CenterHandResponseExtraChoices => HasDockSkillChoices || HasExpandedSelection ? [] : HandResponseExtraChoices;
}
