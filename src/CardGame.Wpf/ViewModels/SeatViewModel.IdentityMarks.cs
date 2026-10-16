using System.Windows.Input;

namespace CardGame.Wpf.ViewModels;

public sealed partial class SeatViewModel
{
    private string? _identityMark;
    private int _identityMarkGeneration;
    private IReadOnlyList<IdentityMarkChoice>? _identityMarkChoices;

    // A personal guess uses only the ordinary viewer projection, never a trusted role.
    public bool CanMarkIdentity => !IsNationalSeat && TeamId is null && !IsHuman && IsAlive && RoleLabel == "?";
    public string? IdentityMark => _identityMark;
    public string IdentityMarkTooltip => IdentityMark is { } mark
        ? $"你的身份标记：{mark}（仅本机可见）\n点击修改或清除标记"
        : "身份未知 · 点击标记忠臣、反贼或内奸（仅本机可见）";

    public IReadOnlyList<IdentityMarkChoice> IdentityMarkChoices => _identityMarkChoices ??= CreateIdentityMarkChoices();

    private IReadOnlyList<IdentityMarkChoice> CreateIdentityMarkChoices()
    {
        var generation = _identityMarkGeneration;
        return Array.AsReadOnly(new[] { "忠臣", "反贼", "内奸", "清除标记" }.Select(label =>
        {
            var mark = label == "清除标记" ? null : label;
            return new IdentityMarkChoice(label, mark == IdentityMark, new RelayCommand(() =>
            {
                if (!CanMarkIdentity || generation != _identityMarkGeneration) return;
                SetIdentityMark(mark);
            }, () => CanMarkIdentity && generation == _identityMarkGeneration));
        }).ToArray());
    }

    private void SetIdentityMark(string? mark)
    {
        _identityMark = mark;
        _identityMarkChoices = null;
        _identityMarkGeneration++;
        RaisePropertyChanged(nameof(IdentityMark));
        RaisePropertyChanged(nameof(RoleBadgeText));
        RaisePropertyChanged(nameof(RoleBrush));
        RaisePropertyChanged(nameof(IdentityMarkTooltip));
        RaisePropertyChanged(nameof(IdentityMarkChoices));
    }

    internal void ResetIdentityMark()
    {
        // Invalidate an already-open menu even when it had no selected mark.
        if (_identityMark is not null || _identityMarkChoices is not null) SetIdentityMark(null);
    }
}

public sealed record IdentityMarkChoice(string Label, bool IsSelected, ICommand Command);
