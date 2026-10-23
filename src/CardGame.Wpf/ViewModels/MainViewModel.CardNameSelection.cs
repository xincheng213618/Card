using System.Windows.Input;
using CardGame.Core;
using CardGame.Wpf.Controls;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private IReadOnlyList<PromptChoice> CardNameChoiceCandidates => IsHandResponsePending ? HandResponseExtraChoices
        : IsSkillSelectionPending ? CenterSkillChoices : EquipmentPlayChoices;

    // Resolve physical cards and opaque slots through the same region rules as Snatch first.
    // A name catalog keeps its original cancel choice and exact published command parameters.
    private bool CanUseExpandedSelection => CardNameChoiceCandidates is { Count: > 0 } choices &&
        choices.All(choice => choice.Cards.Count == 0 && choice.ContentIds.Count == 0 &&
            !HasOpaqueRegionSlot(choice) && TargetChoiceRegion(choice, publicPlay: false) is null);
    public bool HasCardNameSelection => CanUseExpandedSelection &&
        CardNameChoiceCandidates.Any(choice => CardNameChoicePresentation.From(choice) is not null);
    public bool HasExpandedSelection => CanUseExpandedSelection &&
        (CardNameChoiceCandidates.Count > 6 || HasCardNameSelection);
    private IReadOnlyList<PromptChoice> ExpandedSelectionChoices => HasExpandedSelection ? CardNameChoiceCandidates : [];

    public IReadOnlyList<PromptChoice> CardNameSelectionChoices => HasCardNameSelection ? CardNameChoiceCandidates : [];
    public ICommand CardNameSelectionCommand => IsHandResponsePending ? SelectHandResponseChoiceCommand
        : IsSkillSelectionPending ? SelectSkillChoiceCommand : SelectEquipmentPlayChoiceCommand;
    public string CardNameSelectionTitle => IsJinzhiSelectionPending ? "锦织 · 选择基本牌" : _snapshot.PendingDecision?.SkillPrompt is { } skill
        ? $"{skill.Name} · {(HasCardNameSelection ? "选择牌名" : "选择效果")}" : IsPlayConversionSelectionPending ? $"{PlayConversionSkillName} · 选择牌名" : "选择牌名";
    public IReadOnlyList<PromptChoice> CenterEquipmentPlayChoices => HasExpandedSelection ? [] : EquipmentPlayChoices;
}
