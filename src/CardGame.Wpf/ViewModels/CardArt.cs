using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

/// <summary>Presentation assets keyed by CardKind; rank, suit and rules remain independent.</summary>
public static class CardArt
{
    private static readonly Dictionary<string, ImageSource?> Cache = [];

    public static ImageSource? Get(CardKind? kind)
    {
        if (kind is not { } key || !Enum.IsDefined(key)) return null;
        return Load($"{key}.png");
    }

    public static ImageSource? GetRank(string rank, string suit) =>
        rank is "A" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9" or "10" or "J" or "Q" or "K"
            ? Load($"rank-{(suit is "♥" or "♦" ? "red" : "black")}_{rank}.png") : null;

    public static ImageSource? GetSuit(string suit) => suit switch
    {
        "♥" => Load("suit-heart.png"), "♦" => Load("suit-diamond.png"),
        "♠" => Load("suit-spade.png"), "♣" => Load("suit-club.png"), _ => null
    };

    public static ImageSource? GetEquipment(CardKind kind) =>
        EquipmentCatalog.IsEquipment(kind) ? Load($"equip-{kind}.png") : null;

    private static ImageSource? Load(string key)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Cards", key);
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
