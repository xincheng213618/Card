using System.Windows.Media;

namespace CardGame.Wpf.ViewModels;

public sealed partial class SeatViewModel : ObservableObject
{
    private bool _isLegalTarget;
    private bool _isSelectedTarget;
    private IReadOnlyList<string>? _healthImages;
    private int _healthIconCount;
    private int _filledHealthIcons;

    public required int Seat { get; init; }
    public int DisplaySeatNumber { get; set; }
    public string GeneralId { get; set; } = string.Empty;
    public string GeneralName { get; set; } = string.Empty;
    public string SecondaryGeneralText { get; set; } = string.Empty;
    public bool HasSecondaryGeneral => SecondaryGeneralText.Length > 0;
    public bool IsNationalSeat { get; set; }
    public GeneralSlotViewModel? PrimaryGeneral { get; set; }
    public GeneralSlotViewModel? SecondaryGeneral { get; set; }
    public string RelationshipLabel { get; set; } = string.Empty;
    public string RoleBadgeText => IsNationalSeat ? RelationshipLabel : CanMarkIdentity && IdentityMark is { } mark ? mark : RoleLabel;
    public string SkillName { get; set; } = string.Empty;
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int HandCount { get; set; }
    public int? HandLimit { get; set; }
    public string HandBadgeText => IsCurrent ? $"{HandCount}/{HandLimitValue}" : HandCount.ToString();
    private string HandLimitValue => HandLimit == int.MaxValue ? "∞" : HandLimit?.ToString() ?? "—";
    public string HandBadgeTooltip => IsCurrent ? $"手牌 {HandCount} 张 · 手牌上限 {HandLimitValue}" : $"手牌 {HandCount} 张";
    public bool IsChained { get; set; }
    public bool IsFaceDown { get; set; }
    public string BuquWoundText { get; set; } = string.Empty;
    public bool HasBuquWounds => BuquWoundText.Length > 0;
    public string AuthorityText { get; set; } = string.Empty;
    public bool HasAuthority => AuthorityText.Length > 0;
    public string ChunlaoText { get; set; } = string.Empty;
    public string ChunlaoTooltip { get; set; } = string.Empty;
    public bool HasChunlao => ChunlaoText.Length > 0;
    public string PojunHoldText { get; set; } = string.Empty;
    public string PojunHoldTooltip { get; set; } = string.Empty;
    public bool HasPojunHold => PojunHoldText.Length > 0;
    public string DeferredPileText { get; set; } = string.Empty;
    public string DeferredPileTooltip { get; set; } = string.Empty;
    public bool HasDeferredPile => DeferredPileText.Length > 0;
    public int PrivateReserveCount { get; set; }
    public bool HasPrivateReserve => PrivateReserveCount > 0;
    public string PrivateReserveText => $"七星[{PrivateReserveCount}]";
    public int TurnHandHoldCount { get; set; }
    public string TurnHandHoldName { get; set; } = "暂存";
    public bool HasTurnHandHold => TurnHandHoldCount > 0;
    public string TurnHandHoldText => $"{TurnHandHoldName}[{TurnHandHoldCount}]";
    public bool HasGale { get; set; }
    public bool HasMist { get; set; }
    public bool HasAlcoholEffect { get; set; }
    private IReadOnlyList<SeatStatusViewModel> _statusBadges = [];
    public IReadOnlyList<SeatStatusViewModel> StatusBadges
    {
        get => _statusBadges;
        set
        {
            _statusBadges = value;
            VisibleStatusBadges = value.Count <= 5 ? value : Array.AsReadOnly(value.Take(4)
                .Append(new SeatStatusViewModel($"+{value.Count - 4}",
                    string.Join("\n\n", value.Skip(4).Select(status => status.Tooltip)))).ToArray());
        }
    }
    public IReadOnlyList<SeatStatusViewModel> VisibleStatusBadges { get; private set; } = [];
    public bool HasStatusBadges => StatusBadges.Count > 0;
    public bool ShowPortraitStatuses => HasStatusBadges && !IsHuman;
    private bool _statusBadgesOnLeft;
    public bool StatusBadgesOnLeft { get => _statusBadgesOnLeft; set => SetProperty(ref _statusBadgesOnLeft, value); }
    public bool HasPortrait => GeneralArt.HasPortrait(GeneralId);
    private GeneralPortraitViewModel? _portrait;
    public GeneralPortraitViewModel Portrait { get => _portrait ??= new(GeneralId); set => _portrait = value; }
    public Brush PortraitBrush => Portrait.Brush;
    public string? TeamId { get; set; }
    public bool IsTeammate { get; set; }
    public string DecisionRoleLabel { get; set; } = string.Empty;
    public bool HasDecisionRole => DecisionRoleLabel.Length > 0;
    public string ResponseStatusText { get; set; } = string.Empty;
    public bool HasResponseStatus => ResponseStatusText.Length > 0;
    public string SeatLabel => IsHuman ? $"你 · {DisplaySeatNumber:00} 号位"
        : IsNationalSeat ? $"{DisplaySeatNumber:00} 号位 · {RelationshipLabel}"
        : TeamId is not null ? $"{DisplaySeatNumber:00} 号位 · {(IsTeammate ? "队友" : "对手")}"
        : $"{DisplaySeatNumber:00} 号位";
    public string VerticalName => string.Join("\n", GeneralName.ToCharArray());
    public bool IdentityConcealed { get; set; }
    public bool HpUndefined { get; set; }
    public string HealthPips => HpUndefined ? string.Empty : new('●', Math.Max(0, Hp));
    public string EmptyHealthPips => HpUndefined ? string.Empty : new('○', Math.Max(0, MaxHp - Math.Max(0, Hp)));
    public string HealthValue => HpUndefined ? "体力未定义" : $"{Math.Max(0, Hp)}/{MaxHp}";
    public bool ShowNumericHealth => !HpUndefined && MaxHp > 5;
    public string NumericHealthText => $"{Math.Max(0, Hp)}\n/\n{MaxHp}";
    private static readonly Brush HealthyHealthBrush = FrozenHealthBrush("#6FBD48");
    private static readonly Brush WarningHealthBrush = FrozenHealthBrush("#EFC64E");
    private static readonly Brush CriticalHealthBrush = FrozenHealthBrush("#EF7264");
    public Brush HealthBrush => Hp <= 1 ? CriticalHealthBrush : Hp == 2 ? WarningHealthBrush : HealthyHealthBrush;
    private static Brush FrozenHealthBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
    public IEnumerable<string> HealthImages
    {
        get
        {
            if (HpUndefined) return Array.Empty<string>();
            var count = ShowNumericHealth ? 1 : Math.Clamp(MaxHp, 0, 5);
            var filled = ShowNumericHealth ? Hp > 0 ? 1 : 0 : Math.Clamp(Hp, 0, count);
            if (_healthImages is null || count != _healthIconCount || filled != _filledHealthIcons)
            {
                _healthIconCount = count;
                _filledHealthIcons = filled;
                _healthImages = Array.AsReadOnly(Enumerable.Range(0, count).Select(index => index >= count - filled
                    ? "pack://application:,,,/CardGame.Wpf;component/Assets/gallery-hp-wu.png"
                    : "pack://application:,,,/CardGame.Wpf;component/Assets/gallery-hp-empty.png").ToArray());
            }
            return _healthImages;
        }
    }
    public string ShortEquipment => EquipmentText == "装备 —" ? "装备栏空闲" : EquipmentText.Replace("装备 ", string.Empty);
    public IReadOnlyList<EquipmentSlotViewModel> EquipmentSlots { get; set; } = [];
    public bool ShowPortraitEquipment => EquipmentSlots.Count > 0 && !IsHuman;
    public bool HasJudgment => JudgmentText != "判定区 —";
    public bool ShowPortraitJudgment => HasJudgment && !IsHuman;
    public IReadOnlyList<JudgmentStatusViewModel> JudgmentStatuses { get; set; } = [];
    public string ShortJudgment => JudgmentText.Replace("判定区 ", string.Empty);
    public required string Name { get; set; }
    public required string Kingdom { get; set; }
    public string? KingdomArtwork => FactionArtwork.ForName(Kingdom);
    public bool HasKingdomArtwork => KingdomArtwork is not null;
    public required string RoleLabel { get; set; }
    public required string HpText { get; set; }
    public required string HandText { get; set; }
    public required string EquipmentText { get; set; }
    public required string JudgmentText { get; set; }
    public required string DistanceText { get; set; }
    public required string SkillText { get; set; }
    public string PortraitTooltip => $"{SkillText}\n\n{HpText}\n{HandBadgeTooltip}\n{EquipmentText}\n{DistanceText}";
    public required bool IsAlive { get; set; }
    public required bool IsCurrent { get; set; }
    public required bool IsHuman { get; set; }

    public string AvatarGlyph => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1];

    public Brush KingdomBrush => Kingdom switch
    {
        "魏" => new SolidColorBrush(Color.FromRgb(62, 103, 151)),
        "蜀" => new SolidColorBrush(Color.FromRgb(170, 67, 56)),
        "吴" => new SolidColorBrush(Color.FromRgb(50, 127, 102)),
        "群" => new SolidColorBrush(Color.FromRgb(139, 126, 60)),
        "晋" => new SolidColorBrush(Color.FromRgb(111, 69, 145)),
        _ => new SolidColorBrush(Color.FromRgb(111, 91, 126))
    };

    public Brush RoleBrush => IsNationalSeat ? RelationshipLabel switch
    {
        "自己" => new SolidColorBrush(Color.FromRgb(121, 104, 66)),
        "同伴" => new SolidColorBrush(Color.FromRgb(48, 125, 115)),
        "对手" => new SolidColorBrush(Color.FromRgb(155, 67, 58)),
        _ => new SolidColorBrush(Color.FromRgb(91, 96, 92))
    } : RoleBadgeText switch
    {
        "主公" => new SolidColorBrush(Color.FromRgb(181, 65, 52)),
        "忠臣" => new SolidColorBrush(Color.FromRgb(188, 129, 39)),
        "反贼" => new SolidColorBrush(Color.FromRgb(62, 113, 153)),
        "内奸" => new SolidColorBrush(Color.FromRgb(104, 83, 125)),
        "青队" => new SolidColorBrush(Color.FromRgb(48, 125, 115)),
        "赤队" => new SolidColorBrush(Color.FromRgb(155, 67, 58)),
        _ => new SolidColorBrush(Color.FromRgb(91, 96, 92))
    };

    internal void UpdateFrom(SeatViewModel source)
    {
        var changed = false;
        if (PrivateReserveCount != source.PrivateReserveCount) { PrivateReserveCount = source.PrivateReserveCount; changed = true; }
        if (TurnHandHoldCount != source.TurnHandHoldCount) { TurnHandHoldCount = source.TurnHandHoldCount; changed = true; }
        if (TurnHandHoldName != source.TurnHandHoldName) { TurnHandHoldName = source.TurnHandHoldName; changed = true; }
        if (HasGale != source.HasGale) { HasGale = source.HasGale; changed = true; }
        if (HasMist != source.HasMist) { HasMist = source.HasMist; changed = true; }
        if (DisplaySeatNumber != source.DisplaySeatNumber) { DisplaySeatNumber = source.DisplaySeatNumber; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(GeneralId, source.GeneralId)) { GeneralId = source.GeneralId; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(GeneralName, source.GeneralName)) { GeneralName = source.GeneralName; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(SecondaryGeneralText, source.SecondaryGeneralText)) { SecondaryGeneralText = source.SecondaryGeneralText; changed = true; }
        if (!EqualityComparer<bool>.Default.Equals(IsNationalSeat, source.IsNationalSeat)) { IsNationalSeat = source.IsNationalSeat; changed = true; }
        if (!EqualityComparer<GeneralSlotViewModel?>.Default.Equals(PrimaryGeneral, source.PrimaryGeneral)) { PrimaryGeneral = source.PrimaryGeneral; changed = true; }
        if (!EqualityComparer<GeneralSlotViewModel?>.Default.Equals(SecondaryGeneral, source.SecondaryGeneral)) { SecondaryGeneral = source.SecondaryGeneral; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(RelationshipLabel, source.RelationshipLabel)) { RelationshipLabel = source.RelationshipLabel; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(SkillName, source.SkillName)) { SkillName = source.SkillName; changed = true; }
        if (!EqualityComparer<bool>.Default.Equals(IdentityConcealed, source.IdentityConcealed)) { IdentityConcealed = source.IdentityConcealed; changed = true; }
        if (!EqualityComparer<bool>.Default.Equals(HpUndefined, source.HpUndefined)) { HpUndefined = source.HpUndefined; changed = true; }
        if (!EqualityComparer<int>.Default.Equals(Hp, source.Hp)) { Hp = source.Hp; changed = true; }
        if (!EqualityComparer<int>.Default.Equals(MaxHp, source.MaxHp)) { MaxHp = source.MaxHp; changed = true; }
        if (!EqualityComparer<int>.Default.Equals(HandCount, source.HandCount)) { HandCount = source.HandCount; changed = true; }
        if (HandLimit != source.HandLimit) { HandLimit = source.HandLimit; changed = true; }
        if (!EqualityComparer<bool>.Default.Equals(IsChained, source.IsChained)) { IsChained = source.IsChained; changed = true; }
        if (!EqualityComparer<bool>.Default.Equals(IsFaceDown, source.IsFaceDown)) { IsFaceDown = source.IsFaceDown; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(BuquWoundText, source.BuquWoundText)) { BuquWoundText = source.BuquWoundText; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(AuthorityText, source.AuthorityText)) { AuthorityText = source.AuthorityText; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(ChunlaoText, source.ChunlaoText)) { ChunlaoText = source.ChunlaoText; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(ChunlaoTooltip, source.ChunlaoTooltip)) { ChunlaoTooltip = source.ChunlaoTooltip; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(PojunHoldText, source.PojunHoldText)) { PojunHoldText = source.PojunHoldText; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(PojunHoldTooltip, source.PojunHoldTooltip)) { PojunHoldTooltip = source.PojunHoldTooltip; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(DeferredPileText, source.DeferredPileText)) { DeferredPileText = source.DeferredPileText; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(DeferredPileTooltip, source.DeferredPileTooltip)) { DeferredPileTooltip = source.DeferredPileTooltip; changed = true; }
        if (!EqualityComparer<bool>.Default.Equals(HasAlcoholEffect, source.HasAlcoholEffect)) { HasAlcoholEffect = source.HasAlcoholEffect; changed = true; }
        if (!StatusBadges.SequenceEqual(source.StatusBadges)) { StatusBadges = source.StatusBadges; changed = true; }
        if (!EqualityComparer<string?>.Default.Equals(TeamId, source.TeamId)) { TeamId = source.TeamId; changed = true; }
        if (!EqualityComparer<bool>.Default.Equals(IsTeammate, source.IsTeammate)) { IsTeammate = source.IsTeammate; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(DecisionRoleLabel, source.DecisionRoleLabel)) { DecisionRoleLabel = source.DecisionRoleLabel; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(ResponseStatusText, source.ResponseStatusText)) { ResponseStatusText = source.ResponseStatusText; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(Name, source.Name)) { Name = source.Name; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(Kingdom, source.Kingdom)) { Kingdom = source.Kingdom; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(RoleLabel, source.RoleLabel)) { RoleLabel = source.RoleLabel; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(HpText, source.HpText)) { HpText = source.HpText; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(HandText, source.HandText)) { HandText = source.HandText; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(EquipmentText, source.EquipmentText)) { EquipmentText = source.EquipmentText; changed = true; }
        if (!EquipmentSlots.SequenceEqual(source.EquipmentSlots)) { EquipmentSlots = source.EquipmentSlots; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(JudgmentText, source.JudgmentText)) { JudgmentText = source.JudgmentText; changed = true; }
        if (!JudgmentStatuses.SequenceEqual(source.JudgmentStatuses)) { JudgmentStatuses = source.JudgmentStatuses; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(DistanceText, source.DistanceText)) { DistanceText = source.DistanceText; changed = true; }
        if (!EqualityComparer<string>.Default.Equals(SkillText, source.SkillText)) { SkillText = source.SkillText; changed = true; }
        if (!EqualityComparer<bool>.Default.Equals(IsAlive, source.IsAlive)) { IsAlive = source.IsAlive; changed = true; }
        if (!EqualityComparer<bool>.Default.Equals(IsCurrent, source.IsCurrent)) { IsCurrent = source.IsCurrent; changed = true; }
        if (!EqualityComparer<bool>.Default.Equals(IsHuman, source.IsHuman)) { IsHuman = source.IsHuman; changed = true; }
        if (!EqualityComparer<GeneralPortraitViewModel>.Default.Equals(Portrait, source.Portrait)) { Portrait = source.Portrait; changed = true; }
        if (!CanMarkIdentity) ResetIdentityMark();
        if (changed) RaisePropertyChanged(string.Empty);
    }

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
