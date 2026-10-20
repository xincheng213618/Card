using System.Globalization;
using System.Windows.Data;
using CardGame.Core;

namespace CardGame.Wpf.Controls;

public sealed class PublicTargetChoiceLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is PromptChoice choice
            ? choice.Parameters.ContainsKey("target-card-id") ? choice.Description
                : $"{choice.Description}，选择其手牌（暗牌）"
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
