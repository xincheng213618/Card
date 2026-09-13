using System.Windows.Media;

namespace CardGame.Wpf.ViewModels;

public sealed class SeatViewModel : ObservableObject
{
    private bool _isLegalTarget;
    private bool _isSelectedTarget;

    public required int Seat { get; init; }
    public string GeneralId { get; init; } = string.Empty;
    public string GeneralName { get; init; } = string.Empty;
    public string SecondaryGeneralText { get; init; } = string.Empty;
    public bool HasSecondaryGeneral => SecondaryGeneralText.Length > 0;
    public bool IsNationalSeat { get; init; }
    public GeneralSlotViewModel? PrimaryGeneral { get; init; }
    public GeneralSlotViewModel? SecondaryGeneral { get; init; }
    public string RelationshipLabel { get; init; } = string.Empty;
    public string RoleBadgeText => IsNationalSeat ? RelationshipLabel : RoleLabel;
    public string SkillName { get; init; } = string.Empty;
    public int Hp { get; init; }
    public int MaxHp { get; init; }
    public int HandCount { get; init; }
    public bool IsChained { get; init; }
    public bool HasAlcoholEffect { get; init; }
    public bool HasPortrait => GeneralArt.HasPortrait(GeneralId);
    public Brush PortraitBrush => GeneralArt.GetPortrait(GeneralId);
    public string? TeamId { get; init; }
    public bool IsTeammate { get; init; }
    public string DecisionRoleLabel { get; init; } = string.Empty;
    public bool HasDecisionRole => DecisionRoleLabel.Length > 0;
    public string SeatLabel => IsHuman ? "你 · 一号位"
        : IsNationalSeat ? $"{Seat + 1:00} 号位 · {RelationshipLabel}"
        : TeamId is not null ? $"{Seat + 1:00} 号位 · {(IsTeammate ? "队友" : "对手")}"
        : $"{Seat + 1:00} 号位";
    public string HealthPips => new('●', Math.Max(0, Hp));
    public string EmptyHealthPips => new('○', Math.Max(0, MaxHp - Math.Max(0, Hp)));
    public string HealthValue => $"{Math.Max(0, Hp)}/{MaxHp}";
    public string ShortEquipment => EquipmentText == "装备 —" ? "装备栏空闲" : EquipmentText.Replace("装备 ", string.Empty);
    public bool HasJudgment => JudgmentText != "判定区 —";
    public string ShortJudgment => JudgmentText.Replace("判定区 ", string.Empty);
    public required string Name { get; init; }
    public required string Kingdom { get; init; }
    public required string RoleLabel { get; init; }
    public required string HpText { get; init; }
    public required string HandText { get; init; }
    public required string EquipmentText { get; init; }
    public required string JudgmentText { get; init; }
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

    public Brush RoleBrush => IsNationalSeat ? RelationshipLabel switch
    {
        "自己" => new SolidColorBrush(Color.FromRgb(121, 104, 66)),
        "同伴" => new SolidColorBrush(Color.FromRgb(48, 125, 115)),
        "对手" => new SolidColorBrush(Color.FromRgb(155, 67, 58)),
        _ => new SolidColorBrush(Color.FromRgb(91, 96, 92))
    } : RoleLabel switch
    {
        "主公" => new SolidColorBrush(Color.FromRgb(181, 65, 52)),
        "忠臣" => new SolidColorBrush(Color.FromRgb(188, 129, 39)),
        "反贼" => new SolidColorBrush(Color.FromRgb(62, 113, 153)),
        "内奸" => new SolidColorBrush(Color.FromRgb(104, 83, 125)),
        "青队" => new SolidColorBrush(Color.FromRgb(48, 125, 115)),
        "赤队" => new SolidColorBrush(Color.FromRgb(155, 67, 58)),
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
