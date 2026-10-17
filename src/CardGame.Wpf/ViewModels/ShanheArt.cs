using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CardGame.Wpf.ViewModels;

/// <summary>Optional local artwork, keyed by the public catalogue's numeric reward identity.</summary>
public static class ShanheArt
{
    private static readonly Dictionary<int, ImageSource?> Cache = [];
    public static ImageSource? Get(int sourceRewardId)
    {
        if (sourceRewardId <= 0) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(sourceRewardId, out var cached)) return cached;
            ImageSource? image = null;
            try
            {
                using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", "Shanhe", sourceRewardId + ".png"));
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); image = bitmap;
            }
            catch (Exception error) when (error is IOException or FileFormatException or UnauthorizedAccessException or NotSupportedException)
            {
                // The native text/card fallback remains usable when an optional image is absent.
            }
            Cache[sourceRewardId] = image;
            return image;
        }
    }
}
