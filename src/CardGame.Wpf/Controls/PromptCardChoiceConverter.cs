using System.Globalization;
using System.Windows.Data;
using CardGame.Core;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Controls;

/// <summary>Renders card choices solely from the viewer's authorized snapshot.</summary>
public sealed class PromptCardChoiceConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is PromptChoice choice && values[1] is GameSnapshot snapshot
            ? Present(choice, snapshot)
            : new PromptCardChoicePresentation(string.Empty, false, false, string.Empty, null);

    public static PromptCardChoicePresentation Present(PromptChoice choice, GameSnapshot snapshot)
    {
        int? slot = null;
        foreach (var key in new[] { "slot-index", "hand-slot", "slot" })
            if (int.TryParse(choice.Parameters.GetValueOrDefault(key), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var index) && index >= 0)
            {
                slot = index;
                break;
            }

        // An opaque slot remains a back even in a developer snapshot. Never infer its entity ID.
        if (choice.Cards.Count == 0 && slot is { } hiddenSlot)
            return new(choice.Description, true, true, $"暗牌 {hiddenSlot + 1}", null);
        if (choice.Cards.Count != 1)
            return new(choice.Description, false, false, string.Empty, null);

        var visible = snapshot.PublicRevealedCards.Concat(snapshot.PrivateRevealedCards ?? [])
            .Concat(snapshot.Players.SelectMany(player => player.Hand.Concat(player.Equipment)
                .Concat(player.Judgment).Concat(player.WoodenOxGrain ?? []).Concat(player.PrivateReserveCards ?? [])))
            .FirstOrDefault(card => card.Id == choice.Cards[0]);
        if (visible is null)
            return new(choice.Description, true, true, "暗牌", null);
        var face = new CardViewModel
        {
            Id = visible.Id,
            Kind = visible.Kind,
            Name = visible.DisplayName,
            KindLabel = CardCatalog.Get(visible.Kind).CategoryName,
            SuitGlyph = visible.Suit switch { Suit.Heart => "♥", Suit.Diamond => "♦", Suit.Spade => "♠", Suit.Club => "♣", _ => string.Empty },
            Rank = visible.RankText,
            Description = CardCatalog.Get(visible.Kind).Description,
            IsPlayable = false,
            IsSelected = false
        };
        return new(choice.Description, true, false, visible.DisplayName, face);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed record PromptCardChoicePresentation(
    string Description, bool IsCard, bool IsHidden, string Label, CardViewModel? Face);
