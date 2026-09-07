using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed class GeneralChoiceViewModel
{
    public required string GeneralId { get; init; }

    public required ChoiceId ChoiceId { get; init; }

    public required string Text { get; init; }
}
