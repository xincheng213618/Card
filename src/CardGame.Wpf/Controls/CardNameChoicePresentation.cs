using System.Windows.Media;
using CardGame.Core;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Controls;

/// <summary>A published card name, without a physical card's suit, rank or identity.</summary>
public sealed record CardNameChoicePresentation(CardKind Kind, string Name, string Category, string Action,
    string Description, bool IsRecorded)
{
    public ImageSource? Artwork => CardArt.Get(Kind);

    public static CardNameChoicePresentation? From(PromptChoice choice)
    {
        if (choice.Cards.Count != 0 || choice.ContentIds.Count != 0) return null;
        foreach (var key in new[] { "recorded-kind", "card-name", "played-kind", "card-kind", "kind" })
        {
            var value = choice.Parameters.GetValueOrDefault(key);
            if (!Enum.TryParse<CardKind>(value, out var kind) || !Enum.IsDefined(kind) || value != kind.ToString()) continue;
            var card = CardCatalog.Get(kind);
            var recorded = choice.Parameters.GetValueOrDefault("program-action") == "dinghan-edit" &&
                choice.Description.StartsWith("移除", StringComparison.Ordinal);
            var action = choice.Parameters.GetValueOrDefault("program-action") == "dinghan-edit"
                ? recorded ? "已记录 · 移除" : "未记录 · 增加"
                : key == "recorded-kind" ? "记录牌名"
                : key == "played-kind" && !choice.Description.Contains("联用", StringComparison.Ordinal) ? "视为使用"
                : choice.Description;
            return new(kind, card.DisplayName, card.CategoryName, action,
                $"{choice.Description}\n\n{card.Description}", recorded);
        }
        return null;
    }
}
