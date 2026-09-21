using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CardGame.Wpf.ViewModels;

/// <summary>Local artwork and attributed external assets. Only the public general ID selects the image.</summary>
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
            ["taishi-ci"] = "general-taishi-ci.png",
            ["cao-ren"] = "general-cao-ren.png",
            ["xiao-qiao"] = "general-xiao-qiao.png",
            ["zhou-tai"] = "general-zhou-tai.png",
            ["yuan-shao"] = "general-yuan-shao.png",
            ["xiahou-yuan"] = "general-xiahou-yuan.png",
            ["hua-xiong"] = "general-hua-xiong.png",
            ["gongsun-zan"] = "general-gongsun-zan.png",
            // External source pages, image URLs and hashes: docs/content/bwiki-portraits.json.
            ["zhang-jiao"] = "wiki-zhang-jiao-classic.png",
            // Project-original asset history: docs/content/sources/boundary-zhang-jiao-2026-09-20.json.
            ["boundary-zhang-jiao"] = "general-zhang-jiao.png",
            ["shen-guan-yu"] = "wiki-shen-guan-yu-classic.png",
            ["sp-zhao-yun"] = "wiki-sp-zhao-yun-classic.png",
            // Official artwork URL and hash: docs/content/sources/sp-guan-yu-c23-2026-09-21.json.
            ["sp-guan-yu"] = "official-sp-guan-yu.png",
            // Official artwork URL and hash: docs/content/sources/yan-yan-c24-2026-09-21.json.
            ["yan-yan"] = "official-yan-yan.png",
            // Official artwork URL and hash: docs/content/sources/mou-lu-meng-c22-2026-09-21.json.
            ["mou-lu-meng"] = "official-mou-lu-meng.png",
            // Official artwork URL and hash: docs/content/sources/cao-zhang-a54-2026-09-21.json.
            ["cao-zhang"] = "official-cao-zhang.png",
            ["sun-jian"] = "general-sun-jian.png",
            ["meng-huo"] = "general-meng-huo.png",
            ["zhu-rong"] = "general-zhu-rong.png",
            ["yu-jin"] = "general-yu-jin.png",
            ["xu-shu"] = "general-xu-shu.png"
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
            .Replace("mou:", "mou-", StringComparison.Ordinal)
            .Replace("boundary:", "boundary-", StringComparison.Ordinal)
            .Replace("sp:", "sp-", StringComparison.Ordinal)
            .Replace("national:wei-", string.Empty, StringComparison.Ordinal)
            .Replace("national:shu-", string.Empty, StringComparison.Ordinal)
            .Replace("national:ambitious-", string.Empty, StringComparison.Ordinal)
            .Replace("national:", string.Empty, StringComparison.Ordinal);
        if (key == "ganglie") key = "demo-ganglie";
        return PortraitAliases.GetValueOrDefault(key, key);
    }
}
