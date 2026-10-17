using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed record JudgmentStatusViewModel(int CardId, CardKind Kind, string Name, string Tooltip)
{
    public CardKind? QinJudgmentSlot { get; init; }
    public bool IsQin => QinJudgmentSlot is not null;
    public string SlotGlyph => QinJudgmentSlot is { } slot ? QinSlotGlyph(slot) : string.Empty;
    public bool IsLightning => QinJudgmentSlot is null && Kind == CardKind.Lightning;

    internal static string QinSlotGlyph(CardKind slot) => slot switch
    {
        CardKind.Indulgence => "乐",
        CardKind.SupplyShortage => "兵",
        CardKind.Lightning => "闪",
        _ => "判"
    };

    public string Glyph => IsQin ? "琴" : Kind switch
    {
        CardKind.Indulgence => "乐",
        CardKind.SupplyShortage => "兵",
        CardKind.Lightning => "闪",
        _ => Name.Length > 0 ? Name[..1] : "判"
    };
}
