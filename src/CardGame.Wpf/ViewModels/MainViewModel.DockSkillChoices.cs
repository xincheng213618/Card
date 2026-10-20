using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    public bool HasDockSkillChoices => IsSkillSelectionPending && !IsHandResponsePending && !IsOwnedCardSelectionPending &&
        _snapshot?.PendingDecision is { Choices.Count: > 0 } prompt &&
        (prompt.SkillPrompt is not null || prompt.Kind == DecisionKind.IceSword) &&
        prompt.PlayerSeat == _snapshot.HumanSeat && !HasPublicRevealedCards && !HasPrivatelyViewedCards && !HasDeclaredCards &&
        prompt.Choices.All(choice => choice.Cards.Count == 0 && choice.Targets.Count == 0 && choice.ContentIds.Count == 0 &&
            !HasOpaqueRegionSlot(choice));

    public IReadOnlyList<PromptChoice> DockSkillChoices => HasDockSkillChoices ? SkillChoices : [];
}
