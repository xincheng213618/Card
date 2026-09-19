using System.Windows.Media;

namespace CardGame.Wpf.ViewModels;

public sealed class CardViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isPlayable;
    private string _availabilityText = string.Empty;

    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string KindLabel { get; init; }
    public required string SuitGlyph { get; init; }
    public required string Rank { get; init; }
    public required string Description { get; init; }
    public required bool IsPlayable { get => _isPlayable; set => SetProperty(ref _isPlayable, value); }
    public bool IsPublicChoice { get; init; }
    public string PublicCardLabel => IsPublicChoice ? "点击选取" : "公开牌";
    public string VerticalName => string.Join("\n", Name.ToCharArray());
    public double NameFontSize => Name.Length > 3 ? 23 : Name.Length == 1 ? 38 : 30;
    public double NameLineHeight => Name.Length > 3 ? 27 : 34;
    public string AvailabilityText
    {
        get => _availabilityText;
        set { if (SetProperty(ref _availabilityText, value)) RaisePropertyChanged(nameof(CardHint)); }
    }
    public string CardHint => $"{Name} · {SuitGlyph}{Rank}\n{KindLabel}\n\n{Description}" +
        (string.IsNullOrWhiteSpace(AvailabilityText) ? string.Empty : $"\n\n当前：{AvailabilityText}");

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public Brush SuitBrush => SuitGlyph is "♥" or "♦"
        ? new SolidColorBrush(Color.FromRgb(181, 55, 48))
        : new SolidColorBrush(Color.FromRgb(41, 40, 37));

    public Brush AccentBrush => Name switch
    {
        "杀" => new SolidColorBrush(Color.FromRgb(156, 49, 40)),
        "闪" => new SolidColorBrush(Color.FromRgb(45, 113, 89)),
        "桃" => new SolidColorBrush(Color.FromRgb(192, 91, 105)),
        "决斗" => new SolidColorBrush(Color.FromRgb(139, 79, 132)),
        "无中生有" => new SolidColorBrush(Color.FromRgb(60, 108, 145)),
        "南蛮入侵" => new SolidColorBrush(Color.FromRgb(168, 106, 45)),
        "万箭齐发" => new SolidColorBrush(Color.FromRgb(172, 77, 54)),
        "桃园结义" => new SolidColorBrush(Color.FromRgb(117, 104, 58)),
        "五谷丰登" => new SolidColorBrush(Color.FromRgb(72, 126, 87)),
        "过河拆桥" => new SolidColorBrush(Color.FromRgb(104, 84, 67)),
        "顺手牵羊" => new SolidColorBrush(Color.FromRgb(92, 107, 133)),
        "火杀" => new SolidColorBrush(Color.FromRgb(179, 74, 40)),
        "雷杀" => new SolidColorBrush(Color.FromRgb(81, 91, 151)),
        "酒" => new SolidColorBrush(Color.FromRgb(154, 107, 55)),
        "诸葛连弩" => new SolidColorBrush(Color.FromRgb(119, 87, 54)),
        "青釭剑" => new SolidColorBrush(Color.FromRgb(67, 104, 126)),
        "八卦阵" => new SolidColorBrush(Color.FromRgb(116, 91, 126)),
        "仁王盾" => new SolidColorBrush(Color.FromRgb(91, 106, 119)),
        "赤兔" => new SolidColorBrush(Color.FromRgb(157, 64, 46)),
        "绝影" => new SolidColorBrush(Color.FromRgb(64, 88, 112)),
        "大宛" => new SolidColorBrush(Color.FromRgb(126, 78, 52)),
        "紫骍" => new SolidColorBrush(Color.FromRgb(102, 70, 126)),
        "的卢" => new SolidColorBrush(Color.FromRgb(126, 118, 92)),
        "爪黄飞电" => new SolidColorBrush(Color.FromRgb(174, 128, 48)),
        "玉玺" => new SolidColorBrush(Color.FromRgb(166, 126, 45)),
        "无懈可击" => new SolidColorBrush(Color.FromRgb(77, 104, 116)),
        "铁索连环" => new SolidColorBrush(Color.FromRgb(122, 91, 66)),
        _ => new SolidColorBrush(Color.FromRgb(52, 49, 44))
    };
}
