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
            // Official hero artwork URL and hash: docs/content/sources/boundary-sima-yi-2026-09-26.json.
            ["boundary-sima-yi"] = "official-boundary-sima-yi.png",
            // Official hero artwork and SHA-256: docs/content/sources/boundary-diao-chan-2019-2026-09-26.json.
            ["boundary-diao-chan"] = "official-boundary-diao-chan.png",
            // Official hero artwork URL and hash: docs/content/sources/boundary-zhang-liao-2018-2026-09-26.json.
            ["boundary-zhang-liao"] = "official-boundary-zhang-liao.png",
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
            // Official artwork URL and hash: docs/content/sources/ma-dai-a55-2026-09-21.json.
            ["ma-dai"] = "official-ma-dai.png",
            // Official artwork URL and hash: docs/content/sources/gao-shun-a56-2026-09-21.json.
            ["gao-shun"] = "official-gao-shun.png",
            // Official artwork URL and hash: docs/content/sources/liu-biao-a57-2026-09-21.json.
            ["liu-biao"] = "official-liu-biao.png",
            // Official artwork URL and hash: docs/content/sources/wang-yi-a58-2026-09-21.json.
            ["wang-yi"] = "official-wang-yi.png",
            // Official artwork URL and hash: docs/content/sources/zhong-hui-a59-2026-09-21.json.
            ["zhong-hui"] = "official-zhong-hui.png",
            // Official artwork URL and hash: docs/content/sources/xun-you-a60-2026-09-21.json.
            ["xun-you"] = "official-xun-you.png",
            // Official artwork URL and hash: docs/content/sources/liao-hua-a61-2026-09-21.json.
            ["liao-hua"] = "official-liao-hua.png",
            // Official artwork URL and hash: docs/content/sources/guan-xing-zhang-bao-a62-2026-09-21.json.
            ["guan-xing-zhang-bao"] = "official-guan-xing-zhang-bao.png",
            // Official artwork URL and hash: docs/content/sources/bu-lian-shi-a63-2026-09-21.json.
            ["bu-lian-shi"] = "official-bu-lian-shi.png",
            // Official artwork URL and hash: docs/content/sources/cheng-pu-a64b-2026-09-21.json.
            ["cheng-pu"] = "official-cheng-pu.png",
            // Official artwork URL and hash: docs/content/sources/han-dang-a65-2026-09-21.json.
            ["han-dang"] = "official-han-dang.png",
            // Official artwork URL and hash: docs/content/sources/cao-chong-a66-2026-09-21.json.
            ["cao-chong"] = "official-cao-chong.png",
            // Official artwork URL and hash: docs/content/sources/guo-huai-a67-2026-09-21.json.
            ["guo-huai"] = "official-guo-huai.png",
            // Official artwork URL and hash: docs/content/sources/man-chong-a68-2026-09-21.json.
            ["man-chong"] = "official-man-chong.png",
            // Official artwork URL and hash: docs/content/sources/guan-ping-a69-2026-09-21.json.
            ["guan-ping"] = "official-guan-ping.png",
            // Official source and hash: docs/content/sources/gu-yong-2026-09-26.json.
            ["gu-yong"] = "official-gu-yong.png",
            // Official 2016 一将成名2014 portrait and SHA-256: docs/content/sources/zhu-huan-2014-2026-09-26.json.
            ["zhu-huan"] = "official-zhu-huan.png",
            // Official hero artwork URL and hash: docs/content/sources/li-dian-2026-09-26.json.
            ["li-dian"] = "official-li-dian.png",
            // Official hero artwork URL and hash: docs/content/sources/boundary-guo-jia-2026-09-26.json.
            ["boundary-guo-jia"] = "official-boundary-guo-jia.png",
            // Official hero artwork URL and hash: docs/content/sources/boundary-cao-cao-2026-09-26.json.
            ["boundary-cao-cao"] = "official-boundary-cao-cao.png",
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
