using System.Windows.Media;

namespace CardGame.Wpf.ViewModels;

public sealed class SeatViewModel : ObservableObject
{
    private bool _isLegalTarget;
    private bool _isSelectedTarget;

    public required int Seat { get; init; }
    public required string Name { get; init; }
    public required string Kingdom { get; init; }
    public required string RoleLabel { get; init; }
    public required string HpText { get; init; }
    public required string HandText { get; init; }
    public required string EquipmentText { get; init; }
    public required string DistanceText { get; init; }
    public required string SkillText { get; init; }
    public required bool IsAlive { get; init; }
    public required bool IsCurrent { get; init; }
    public required bool IsHuman { get; init; }

    public string AvatarGlyph => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1];

    public Brush KingdomBrush => Kingdom switch
    {
        "魏" => new SolidColorBrush(Color.FromRgb(62, 103, 151)),
        "蜀" => new SolidColorBrush(Color.FromRgb(170, 67, 56)),
        "吴" => new SolidColorBrush(Color.FromRgb(50, 127, 102)),
        _ => new SolidColorBrush(Color.FromRgb(111, 91, 126))
    };

    public Brush RoleBrush => RoleLabel switch
    {
        "主公" => new SolidColorBrush(Color.FromRgb(181, 65, 52)),
        "忠臣" => new SolidColorBrush(Color.FromRgb(188, 129, 39)),
        "反贼" => new SolidColorBrush(Color.FromRgb(62, 113, 153)),
        "内奸" => new SolidColorBrush(Color.FromRgb(104, 83, 125)),
        _ => new SolidColorBrush(Color.FromRgb(91, 96, 92))
    };

    public bool IsLegalTarget
    {
        get => _isLegalTarget;
        set => SetProperty(ref _isLegalTarget, value);
    }

    public bool IsSelectedTarget
    {
        get => _isSelectedTarget;
        set => SetProperty(ref _isSelectedTarget, value);
    }
}
