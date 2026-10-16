using System.Windows.Media;

namespace CardGame.Wpf.ViewModels;

/// <summary>One local appearance shared by the gallery, choices and visible battle seats.</summary>
public sealed class GeneralPortraitViewModel(string generalId, string? skinId = null) : ObservableObject
{
    private string? _skinId = skinId;
    private WeakReference<Brush>? _brush;
    private WeakReference<Brush>? _thumbnail;
    public string GeneralId { get; } = generalId;
    public string SkinId => GeneralArt.GetSkin(GeneralId, _skinId)?.Id ?? string.Empty;
    public string SkinName => GeneralArt.GetSkin(GeneralId, _skinId)?.Name ?? "经典形象";
    public Brush Brush
    {
        get
        {
            if (_brush is not null && _brush.TryGetTarget(out var cached)) return cached;
            var brush = GeneralArt.GetPortrait(GeneralId, _skinId);
            // Reuse visible portraits without pinning the entire browsed gallery.
            _brush = new(brush);
            return brush;
        }
    }
    public ImageSource? Image => (Brush as ImageBrush)?.ImageSource;
    public Brush ThumbnailBrush
    {
        get
        {
            if (_thumbnail is not null && _thumbnail.TryGetTarget(out var cached)) return cached;
            // The 126-DIP portrait stays sharp at 200% scale without decoding its full artwork.
            var brush = GeneralArt.GetPortrait(GeneralId, _skinId, 256);
            _thumbnail = new(brush);
            return brush;
        }
    }

    internal void SelectSkin(string? id)
    {
        _skinId = GeneralArt.GetSkin(GeneralId, id)?.Id;
        _brush = null;
        _thumbnail = null;
        foreach (var name in new[] { nameof(SkinId), nameof(SkinName), nameof(Brush), nameof(Image), nameof(ThumbnailBrush) })
            RaisePropertyChanged(name);
    }
}

public sealed record GeneralSkin(string Id, string Name, string LocalPath)
{
    public ImageSource Thumbnail => GeneralArt.LoadImage(LocalPath, 180);
}

public sealed record GeneralGallerySkill(string Name, string Description, string GeneralId = "")
{
    public bool HasVoice => Audio.GameAudioCatalog.ForGeneral(GeneralId).Any(voice => voice.Binding.SkillName == Name);
}
