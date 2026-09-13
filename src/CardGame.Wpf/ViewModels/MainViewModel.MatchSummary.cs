using CardGame.Wpf.Presentation;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    public MatchSummary? CompletedMatch { get; private set; }

    private void RefreshMatchSummary()
    {
        CompletedMatch = MatchSummary.Create(_snapshot, _game.Events);
        RaisePropertyChanged(nameof(CompletedMatch));
    }
}
