using System.Globalization;
using System.Windows.Data;
using CardGame.Core;

namespace CardGame.Wpf.Controls;

/// <summary>Uses the target's public zones, never the played card or a foreign hand face.</summary>
public sealed class TargetRegionCardChoiceConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is PromptChoice choice && values[1] is GameSnapshot snapshot
            ? values.Length >= 3 && values[2] is false
                ? PromptCardChoiceConverter.Present(choice, snapshot) : Present(choice, snapshot)
            : new PromptCardChoicePresentation(string.Empty, false, false, string.Empty, null);

    public static PromptCardChoicePresentation Present(PromptChoice choice, GameSnapshot snapshot)
    {
        if (!choice.Parameters.ContainsKey("target-card-id"))
            return new(choice.Description, true, true, "手牌（暗牌）", null);

        if (int.TryParse(choice.Parameters["target-card-id"], NumberStyles.None,
                CultureInfo.InvariantCulture, out var cardId) &&
            snapshot.Players.Where(player => choice.Targets.Contains(player.Seat))
                .Any(player => player.Equipment.Concat(player.Judgment).Any(card => card.Id == cardId)))
            return PromptCardChoiceConverter.Present(choice with { Cards = [cardId] }, snapshot);

        return new(choice.Description, false, false, string.Empty, null);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class TargetRegionCardSelectionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is PromptChoice { Cards: [var id] } &&
        values[1] is IReadOnlyList<int> selected && selected.Contains(id);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
