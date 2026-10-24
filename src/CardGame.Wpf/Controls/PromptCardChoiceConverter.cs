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
            ? parameter is "names-only"
                ? new PromptCardChoicePresentation(choice.Description, false, false, string.Empty, null)
                    { NamedCard = CardNameChoicePresentation.From(choice) }
                : Present(choice, snapshot)
            : new PromptCardChoicePresentation(string.Empty, false, false, string.Empty, null);

    public static PromptCardChoicePresentation Present(PromptChoice choice, GameSnapshot snapshot)
    {
        var slot = OpaqueHandSlot(choice);

        // An opaque slot remains a back even in a developer snapshot. Never infer its entity ID.
        if (choice.Cards.Count == 0 && slot is { } hiddenSlot)
            return new(choice.Description, true, true, $"暗牌 {hiddenSlot + 1}", null);
        if (choice.Cards.Count != 1)
            return new(choice.Description, false, false, string.Empty, null)
            { NamedCard = choice.Cards.Count == 0 && choice.ContentIds.Count == 0 ? CardNameChoicePresentation.From(choice) : null };

        var visible = snapshot.PublicRevealedCards.Concat(snapshot.PrivateRevealedCards ?? [])
            .Concat(snapshot.Players.SelectMany(player => player.Hand.Concat(player.Equipment)
                .Concat(player.Judgment).Concat(player.WoodenOxGrain ?? []).Concat(player.PrivateReserveCards ?? [])))
            .Concat(snapshot.Players.Where(player => player.Seat == snapshot.HumanSeat)
                .SelectMany(player => (player.PrivateWhirlPiles ?? []).SelectMany(pile => pile.Cards ?? [])))
            .Concat((choice.Parameters.GetValueOrDefault("native-original-public-pile") == "true" || choice.Parameters.ContainsKey("distribution-target"))
                ? snapshot.Players.SelectMany(player => player.PublicPersistentPiles is { Count: > 0 } piles
                    ? piles.SelectMany(pile => pile.Cards) : player.PublicPersistentPileCards ?? [])
                : [])
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
            OriginalHandTag = visible.SkillMarkLabel ?? string.Empty,
            IsPlayable = false,
            IsSelected = false
        };
        return new(choice.Description, true, false, visible.DisplayName, face);
    }

    public static int? OpaqueHandSlot(PromptChoice choice)
    {
        IEnumerable<string> keys = new[] { "slot-index", "hand-slot", "slot" };
        if (choice.Parameters.GetValueOrDefault("program-action") == "qizheng" &&
            choice.Parameters.GetValueOrDefault("branch") == "hand") keys = keys.Append("value");
        foreach (var key in keys)
            if (int.TryParse(choice.Parameters.GetValueOrDefault(key), NumberStyles.None,
                CultureInfo.InvariantCulture, out var slot) && slot >= 0) return slot;
        return null;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed record PromptCardChoicePresentation(
    string Description, bool IsCard, bool IsHidden, string Label, CardViewModel? Face)
{
    public CardNameChoicePresentation? NamedCard { get; init; }
    public bool IsCardName => NamedCard is not null;
}
