using CardGame.Core;
using System.Windows.Media;

namespace CardGame.Wpf.ViewModels;

public sealed class GeneralChoiceViewModel
{
    public required string GeneralId { get; init; }

    public required ChoiceId ChoiceId { get; init; }

    public required string Text { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Kingdom { get; init; } = string.Empty;
    public string SkillName { get; init; } = string.Empty;
    public string SkillDescription { get; init; } = string.Empty;
    public string HealthText { get; init; } = string.Empty;
    public string HealthDescription { get; init; } = string.Empty;
    public bool HasHealthPreview => HealthText.Length > 0;
    public Brush PortraitBrush => GeneralArt.GetPortrait(GeneralId);
}
