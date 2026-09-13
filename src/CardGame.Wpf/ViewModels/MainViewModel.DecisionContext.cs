using CardGame.Wpf.Presentation;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    public DecisionContext? CurrentDecisionContext { get; private set; }
    public bool HasDecisionContext => CurrentDecisionContext is not null;
    public string TableDecisionTitle => CurrentDecisionContext?.Title ?? TurnHeadline;

    private void RefreshDecisionContext()
    {
        CurrentDecisionContext = DecisionContext.From(_snapshot);
        RaisePropertyChanged(nameof(CurrentDecisionContext));
        RaisePropertyChanged(nameof(HasDecisionContext));
        RaisePropertyChanged(nameof(TableDecisionTitle));
    }
}
