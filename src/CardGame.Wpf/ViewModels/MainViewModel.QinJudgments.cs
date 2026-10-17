using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private static string JudgmentDisplayName(CardSnapshot card) => card.QinJudgmentSlot is { } slot
        ? $"琴（{JudgmentStatusViewModel.QinSlotGlyph(slot)}）"
        : card.DisplayName;

    private JudgmentStatusViewModel BuildJudgmentStatus(CardSnapshot card)
    {
        var tooltip = card.QinJudgmentSlot is { } slot
            ? $"{JudgmentDisplayName(card)}\n原牌：{card.DisplayName} · {GetSuitGlyph(card.Suit)}{card.RankText}\n占据{CardCatalog.Get(slot).DisplayName}副区域；此“琴”不执行判定。"
            : $"{card.DisplayName} · {GetSuitGlyph(card.Suit)}{card.RankText}\n判定区\n\n{GetCardDescription(card.Kind)}";
        return new(card.Id, card.Kind, JudgmentDisplayName(card), tooltip)
            { QinJudgmentSlot = card.QinJudgmentSlot };
    }
}
