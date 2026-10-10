using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    public GameSnapshot ChoiceSnapshot => _snapshot;
}
