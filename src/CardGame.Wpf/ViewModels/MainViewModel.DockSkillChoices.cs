using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    public bool HasDockSkillChoices => _snapshot?.PendingDecision is { SkillPrompt: not null, Choices.Count: > 0 and <= 3 } prompt &&
        prompt.PlayerSeat == _snapshot.HumanSeat && !HasPublicRevealedCards && !HasPrivatelyViewedCards && !HasDeclaredCards &&
        prompt.Choices.All(choice => choice.Cards.Count == 0 && choice.Targets.Count == 0 && choice.ContentIds.Count == 0 &&
            choice.Parameters.GetValueOrDefault("program-action") is "activate" or "skip");

    public IReadOnlyList<PromptChoice> DockSkillChoices => HasDockSkillChoices ? SkillChoices : [];
}
