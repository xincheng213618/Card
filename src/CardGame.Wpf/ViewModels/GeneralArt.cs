using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CardGame.Wpf.ViewModels;

/// <summary>Local artwork and attributed external assets. Only the public general ID selects the image.</summary>
public static class GeneralArt
{
    private static readonly IReadOnlyDictionary<string, string> PortraitAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["demo-yuanhu"] = "huang-gai",
            ["demo-ganglie"] = "xiahou-dun",
            ["demo-guicai"] = "sima-yi",
            ["ganglie"] = "xiahou-dun",
            ["demo-kujin"] = "huang-gai",
            ["demo-zhiheng"] = "sun-quan",
            ["demo-rende"] = "liu-bei",
            ["demo-mashu"] = "ma-chao",
            ["demo-qicai"] = "huang-yueying",
            ["demo-qingnang"] = "hua-tuo",
            ["demo-huichun"] = "hua-tuo",
            ["demo-jijiu"] = "hua-tuo",
            ["ambitious-lu-bu"] = "lu-bu",
            ["ambitious-diao-chan"] = "diao-chan"
        };

    private static readonly IReadOnlyDictionary<string, string> StandalonePortraits =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["da-qiao"] = "official-da-qiao.png",
            ["diao-chan"] = "official-diao-chan.png",
            ["sun-shangxiang"] = "official-sun-shangxiang.png",
            ["lu-xun"] = "official-lu-xun.png",
            ["pang-tong"] = "official-pang-tong.png",
            ["taishi-ci"] = "official-taishi-ci.png",
            ["cao-ren"] = "official-cao-ren.png",
            ["xiao-qiao"] = "official-xiao-qiao.png",
            ["zhou-tai"] = "official-zhou-tai.png",
            ["yuan-shao"] = "official-yuan-shao.png",
            ["xiahou-yuan"] = "official-xiahou-yuan.png",
            ["hua-xiong"] = "official-hua-xiong.png",
            ["gongsun-zan"] = "official-gongsun-zan.png",
            // External source pages, image URLs and hashes: docs/content/bwiki-portraits.json.
            ["zhang-jiao"] = "wiki-zhang-jiao-classic.png",
            // Official replacement and skins: docs/content/general-art-catalog.json.
            ["boundary-zhang-jiao"] = "official-boundary-zhang-jiao.png",
            // Official hero artwork URL and hash: docs/content/sources/boundary-sima-yi-2026-09-26.json.
            ["boundary-sima-yi"] = "official-boundary-sima-yi.png",
            // Official hero artwork and SHA-256: docs/content/sources/boundary-diao-chan-2019-2026-09-26.json.
            ["boundary-diao-chan"] = "official-boundary-diao-chan.png",
            ["shen-guan-yu"] = "wiki-shen-guan-yu-classic.png",
            ["sp-zhao-yun"] = "wiki-sp-zhao-yun-classic.png",
            // Official artwork URL and hash: docs/content/sources/sp-guan-yu-c23-2026-09-21.json.
            ["sp-guan-yu"] = "official-sp-guan-yu.png",
            // Official hero artwork URL and hash: docs/content/sources/sp-le-jin-2015-2026-09-26.json.
            ["sp-le-jin"] = "official-sp-le-jin.png",
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
            // Official hero artwork URL and hash: docs/content/sources/li-dian-2026-09-26.json.
            ["li-dian"] = "official-li-dian.png",
            // Official hero artwork URL and hash: docs/content/sources/boundary-guo-jia-2026-09-26.json.
            ["boundary-guo-jia"] = "official-boundary-guo-jia.png",
            // Batch 3: source pages and hashes are recorded under docs/content/sources/.
            ["zhu-huan"] = "official-zhu-huan.png",
            ["boundary-cao-cao"] = "official-boundary-cao-cao.png",
            ["boundary-zhang-liao"] = "official-boundary-zhang-liao.png",
            ["sun-jian"] = "official-sun-jian.png",
            ["meng-huo"] = "official-meng-huo.png",
            ["zhu-rong"] = "official-zhu-rong.png",
            ["yu-jin"] = "official-yu-jin.png",
            ["xu-shu"] = "official-xu-shu.png"
        };

    private sealed record CatalogEntry(string Key, string DefaultSkinId, GeneralSkin[] Skins);
    private sealed record Catalog(CatalogEntry[] Entries);
    private static readonly Lazy<IReadOnlyDictionary<string, CatalogEntry>> ArtCatalog = new(() =>
    {
        using var stream = typeof(GeneralArt).Assembly.GetManifestResourceStream("CardGame.GeneralArtCatalog.json")
            ?? throw new InvalidOperationException("Missing general artwork catalog.");
        var catalog = System.Text.Json.JsonSerializer.Deserialize<Catalog>(stream,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return catalog.Entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
    });
    private static readonly Dictionary<string, WeakReference<BitmapImage>> Images = new(StringComparer.Ordinal);

    public static IReadOnlyList<GeneralSkin> GetSkins(string id)
    {
        var key = NormalizeKey(id);
        if (ArtCatalog.Value.TryGetValue(key, out var entry)) return entry.Skins;
        return StandalonePortraits.TryGetValue(key, out var file)
            ? [new GeneralSkin("classic", "经典形象", "src/CardGame.Wpf/Assets/" + file)] : [];
    }

    public static GeneralSkin? GetSkin(string id, string? skinId = null)
    {
        var skins = GetSkins(id);
        var defaultId = ArtCatalog.Value.GetValueOrDefault(NormalizeKey(id))?.DefaultSkinId;
        return skins.FirstOrDefault(skin => skin.Id == skinId)
            ?? skins.FirstOrDefault(skin => skin.Id == defaultId) ?? skins.FirstOrDefault();
    }

    public static bool HasPortrait(string id) => GetSkin(id) is not null;

    public static Brush GetPortrait(string id, string? skinId = null)
    {
        if (GetSkin(id, skinId) is not { } skin) return Brushes.Transparent;
        var brush = new ImageBrush(LoadImage(skin.LocalPath))
        {
            Stretch = Stretch.UniformToFill,
            AlignmentY = AlignmentY.Top
        };
        brush.Freeze();
        return brush;
    }

    internal static BitmapImage LoadImage(string localPath, int decodeWidth = 0)
    {
        const string prefix = "src/CardGame.Wpf/";
        var relative = localPath.StartsWith(prefix, StringComparison.Ordinal) ? localPath[prefix.Length..] : localPath;
        if (!relative.StartsWith("Assets/", StringComparison.Ordinal) || relative.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("Artwork must be a local asset.");
        var cacheKey = relative + "#" + decodeWidth;
        if (Images.TryGetValue(cacheKey, out var weak) && weak.TryGetTarget(out var cached)) return cached;
        var uri = relative.StartsWith("Assets/Skins/", StringComparison.Ordinal)
            ? new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, relative))
            : new Uri("pack://application:,,,/CardGame.Wpf;component/" + relative);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = uri;
        if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
        image.EndInit();
        image.Freeze();
        Images[cacheKey] = new(image);
        return image;
    }

    public static string NormalizeKey(string id)
    {
        var key = id;
        foreach (var prefix in new[] { "standard:", "classic:", "national:wei-", "national:shu-", "national:ambitious-", "national:" })
            if (key.StartsWith(prefix, StringComparison.Ordinal)) { key = key[prefix.Length..]; break; }
        foreach (var prefix in new[] { "mou:", "boundary:", "sp:" })
            if (key.StartsWith(prefix, StringComparison.Ordinal)) { key = prefix[..^1] + "-" + key[prefix.Length..]; break; }
        return PortraitAliases.GetValueOrDefault(key, key);
    }
}
