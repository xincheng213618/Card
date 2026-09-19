using CardGame.Core;
using System.Windows.Media;

namespace CardGame.Wpf.ViewModels;

public sealed class GeneralChoiceViewModel : ObservableObject
{
    private bool _isPreviewSelected;
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
    public bool IsPreviewSelected { get => _isPreviewSelected; set => SetProperty(ref _isPreviewSelected, value); }
}

public sealed class GeneralGalleryEntryViewModel
{
    public required string GeneralId { get; init; }
    public required string SeriesId { get; init; }
    public required string SeriesName { get; init; }
    public required string Name { get; init; }
    public required string FactionId { get; init; }
    public required string Kingdom { get; init; }
    public required string HealthText { get; init; }
    public required string SkillName { get; init; }
    public required string SkillDescription { get; init; }
    public Brush PortraitBrush => GeneralArt.GetPortrait(GeneralId);
}

public sealed record GeneralGalleryFactionOption(string Id, string Name);

public sealed record GeneralGallerySeriesOption(string Id, string Name, string Description);
