using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CardGame.Wpf.ViewModels;

/// <summary>Original local artwork. No hidden game state is used to pick a portrait.</summary>
public static class GeneralArt
{
    private static readonly string[] PortraitIds =
    [
        "cao-cao",
        "zhang-fei",
        "zhou-yu",
        "zhuge-liang",
        "liu-bei",
        "guan-yu",
        "zhao-yun",
        "sun-quan",
        "hua-tuo",
        "guo-jia",
        "xun-yu",
        "demo-yuanhu",
        "demo-ganglie",
        "demo-guicai"
    ];

    private static readonly IReadOnlyDictionary<string, string> PortraitAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["huang-gai"] = "demo-yuanhu",
            ["gan-ning"] = "zhao-yun",
            ["lu-meng"] = "zhuge-liang",
            ["zhang-liao"] = "demo-ganglie",
            ["xu-chu"] = "zhang-fei",
            ["dian-wei"] = "zhang-fei",
            ["xu-huang"] = "demo-ganglie",
            ["cao-ren"] = "demo-ganglie",
            ["zhen-ji"] = "zhou-yu",
            ["huang-yueying"] = "zhou-yu",
            ["ma-chao"] = "zhao-yun",
            ["huang-zhong"] = "liu-bei",
            ["wei-yan"] = "zhao-yun",
            ["lu-bu"] = "zhang-fei",
            ["demo-kujin"] = "sun-quan",
            ["demo-zhiheng"] = "sun-quan",
            ["demo-rende"] = "liu-bei",
            ["demo-mashu"] = "zhao-yun",
            ["demo-qicai"] = "zhuge-liang",
            ["xiahou-dun"] = "zhang-fei",
            ["sima-yi"] = "guo-jia",
            ["ambitious-lu-bu"] = "zhang-fei",
            ["ambitious-diao-chan"] = "liu-bei"
        };

    private static readonly IReadOnlyDictionary<string, string> StandalonePortraits =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["da-qiao"] = "general-da-qiao.png",
            ["diao-chan"] = "general-diao-chan.png",
            ["sun-shangxiang"] = "general-sun-shangxiang.png",
            ["lu-xun"] = "general-lu-xun.png",
            ["pang-tong"] = "general-pang-tong.png",
            ["taishi-ci"] = "general-taishi-ci.png"
        };

    private static readonly Lazy<BitmapImage> Atlas = new(() =>
    {
        var image = new BitmapImage(new Uri("pack://application:,,,/CardGame.Wpf;component/Assets/generals-atlas.png"));
        image.Freeze();
        return image;
    });
    private static readonly Dictionary<string, Brush> Cache = new();

    public static bool HasPortrait(string id)
    {
        var key = NormalizeKey(id);
        return StandalonePortraits.ContainsKey(key) || Array.IndexOf(PortraitIds, key) >= 0;
    }

    public static Brush GetPortrait(string id)
    {
        var key = NormalizeKey(id);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        if (StandalonePortraits.TryGetValue(key, out var fileName))
        {
            var image = new BitmapImage(new Uri(
                $"pack://application:,,,/CardGame.Wpf;component/Assets/{fileName}"));
            image.Freeze();
            var standalone = new ImageBrush(image)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentY = AlignmentY.Top
            };
            standalone.Freeze();
            Cache[key] = standalone;
            return standalone;
        }

        var index = Array.IndexOf(PortraitIds, key);
        if (index < 0) return Brushes.Transparent;
        var brush = new ImageBrush(Atlas.Value)
        {
            Viewbox = new Rect(index % 4 / 4.0, index / 4 / 4.0, .25, .25),
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Stretch = Stretch.UniformToFill,
            AlignmentY = AlignmentY.Top
        };
        brush.Freeze();
        Cache[key] = brush;
        return brush;
    }

    private static string NormalizeKey(string id)
    {
        var key = id.Replace("standard:", string.Empty, StringComparison.Ordinal)
            .Replace("classic:", string.Empty, StringComparison.Ordinal)
            .Replace("national:wei-", string.Empty, StringComparison.Ordinal)
            .Replace("national:shu-", string.Empty, StringComparison.Ordinal)
            .Replace("national:ambitious-", string.Empty, StringComparison.Ordinal);
        if (key == "ganglie") key = "demo-ganglie";
        return PortraitAliases.GetValueOrDefault(key, key);
    }
}
