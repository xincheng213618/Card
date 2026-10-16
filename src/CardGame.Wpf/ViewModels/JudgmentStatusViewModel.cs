using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed record JudgmentStatusViewModel(int CardId, CardKind Kind, string Name, string Tooltip)
{
    public bool IsLightning => Kind == CardKind.Lightning;

    public string Glyph => Kind switch
    {
        CardKind.Indulgence => "乐",
        CardKind.SupplyShortage => "兵",
        CardKind.Lightning => "闪",
        _ => Name.Length > 0 ? Name[..1] : "判"
    };
}
