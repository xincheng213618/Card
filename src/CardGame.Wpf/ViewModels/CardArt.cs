using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

/// <summary>Presentation assets keyed by CardKind; rank, suit and rules remain independent.</summary>
public static class CardArt
{
    private static readonly Dictionary<CardKind, ImageSource?> Cache = [];

    public static ImageSource? Get(CardKind? kind)
    {
        if (kind is not { } key || !Enum.IsDefined(key)) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Cards", $"{key}.png");
            ImageSource? image = null;
            try
            {
                using var stream = File.OpenRead(path);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                image = bitmap;
            }
            catch (Exception exception) when (exception is IOException or FileFormatException or UnauthorizedAccessException or NotSupportedException)
            {
                // A missing or unreadable optional face must not prevent playing the card.
            }
            Cache[key] = image;
            return image;
        }
    }
}
