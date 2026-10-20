using CardGame.Core;
using CardGame.Wpf.Presentation;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    public DecisionContext? CurrentDecisionContext { get; private set; }
    public bool HasDecisionContext => CurrentDecisionContext is not null;
    public bool IsGeneralLibrarySelectionPending => _snapshot?.PendingDecision is { } prompt &&
        prompt.PlayerSeat == _snapshot.HumanSeat &&
        prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "private-general-library");
    public string TableDecisionTitle => CurrentDecisionContext?.Title ?? TurnHeadline;

    private static string ResponseStatusFor(GameSnapshot? snapshot, int seat)
    {
        if (snapshot?.PendingDecision is not { } prompt || prompt.PlayerSeat != snapshot.HumanSeat || prompt.PlayerSeat != seat)
            return string.Empty;
        if ((prompt.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash) && !DecisionContext.IsResponseCardPrompt(prompt))
            return string.Empty;
        if (prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") == "bagua-skip"))
            return "待发动八卦";
        return prompt.Kind switch
        {
            DecisionKind.RespondDodge or DecisionKind.RespondSlash =>
                (prompt.RequiredCardKind ?? (prompt.Kind == DecisionKind.RespondDodge ? CardKind.Dodge : CardKind.Slash)) switch
                {
                    CardKind.Dodge => "待打闪",
                    CardKind.Slash => "待打杀",
                    _ => string.Empty
                },
            DecisionKind.RescueDying => "待救援",
            DecisionKind.Nullification => "待无懈",
            DecisionKind.FireAttackReveal => "待展示",
            DecisionKind.FireAttackDiscard => "待弃牌",
            _ => string.Empty
        };
    }

    private void RefreshDecisionContext()
    {
        CurrentDecisionContext = DecisionContext.From(_snapshot);
        RaisePropertyChanged(nameof(CurrentDecisionContext));
        RaisePropertyChanged(nameof(HasDecisionContext));
        RaisePropertyChanged(nameof(IsGeneralLibrarySelectionPending));
        RaisePropertyChanged(nameof(TableDecisionTitle));
    }
}
