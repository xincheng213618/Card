using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed record FactionChoiceViewModel(PromptChoice Choice, string Name)
{
    public string? Artwork => FactionArtwork.ForName(Name);
}

internal static class FactionArtwork
{
    internal static string? ForName(string name) => name switch
    {
        "魏" => Path("wei"), "蜀" => Path("shu"), "吴" => Path("wu"),
        "群" => Path("qun"), "晋" => Path("jin"), "神" => Path("shen"), _ => null
    };

    private static string Path(string id) => $"pack://application:,,,/CardGame.Wpf;component/Assets/gallery-faction-{id}.png";
}

public sealed partial class MainViewModel
{
    public bool IsGodFactionSelectionPending => _snapshot.PendingDecision is
        { Kind: DecisionKind.SelectFaction, PlayerSeat: var seat } && seat == HumanPlayer?.Seat;

    public IReadOnlyList<FactionChoiceViewModel> GodFactionChoices => IsGodFactionSelectionPending
        ? SkillChoices.Select(choice => new FactionChoiceViewModel(choice,
            FactionName(choice.Parameters.GetValueOrDefault("faction-id")))).ToArray()
        : [];
}
