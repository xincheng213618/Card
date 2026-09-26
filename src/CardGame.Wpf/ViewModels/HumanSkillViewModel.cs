namespace CardGame.Wpf.ViewModels;

public sealed record HumanSkillViewModel(
    string Name,
    string Description,
    string TypeText,
    string StateText,
    string SourceText,
    bool IsAvailable,
    bool IsDisabled)
{
    public string? ContentId { get; init; }
    public CardGame.Core.SkillKind? LegacyKind { get; init; }
    public bool IsLocked => TypeText.Contains("锁定", StringComparison.Ordinal);
    public string ButtonArtwork => GetButtonArtwork(IsAvailable && !IsDisabled);
    public string DisabledButtonArtwork => GetButtonArtwork(false);
    private string GetButtonArtwork(bool available) => "pack://application:,,,/CardGame.Wpf;component/Assets/Table/selfseat__skill" +
        (TypeText.Contains("觉醒", StringComparison.Ordinal) ? "JueXing" : TypeText.Contains("限定", StringComparison.Ordinal) ? "XianDing" : IsLocked ? "SuoDing" : "PuTong") +
        (available ? "_up.png" : "_disabled.png");
    public string Tooltip => $"{Name} · {TypeText}\n{Description}\n\n{StateText}\n{SourceText}";
}
