namespace CardGame.Wpf.ViewModels;

public sealed record SeatStatusViewModel(string Label, string Tooltip, int Count = 0)
{
    public bool ShowSingleCount { get; init; }
    public bool IsDrawPhaseBonus { get; init; }
    public bool IsTitle { get; init; }
    public int? PublicPileOwnerSeat { get; init; }
    public int PublicPileIndex { get; init; }
    public bool CanInspectPile => PublicPileOwnerSeat is not null;
    public bool HasCount => !IsDrawPhaseBonus && (Count > 1 || ShowSingleCount && Count > 0);
    public string CountText => Count.ToString();
    public double FontSize => Label.Length == 1 ? 18 : Label.Length == 2 ? 10 : 8;
}
