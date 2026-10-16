using System.Collections.ObjectModel;

namespace CardGame.Content.Standard;

// Version-specific, bounded preferences for the implemented mixed eight-player
// identity roster. Rationale and dated public references: docs/content/IDENTITY_DRAFT.md.
internal static class IdentityGeneralSelectionWeights
{
    internal static IReadOnlyDictionary<string, int> EightPlayer { get; } = Create();

    private static IReadOnlyDictionary<string, int> Create()
    {
        var weights = new Dictionary<string, int>(StringComparer.Ordinal);
        Add(8, "mou:zhuge-liang", "ol:shen-sima-yi", "ol:du-yu", "ol:cao-chun",
            "ol:shen-lu-xun", "ol:shen-sun-quan");
        Add(5, "classic:shen-sima-yi", "classic:shen-cao-cao", "ol:xi-zhi-cai", "boundary:lu-su",
            "ol:wang-can", "ol:cao-ying", "classic:xu-you", "ol:shen-gan-ning", "ol:shen-zhao-yun",
            "ol:shen-zhang-jiao", "classic:zhang-rang", "mobile:liu-zan");
        Add(3, "classic:lu-su", "classic:xun-you", "classic:cao-rui", "ol:liu-xie", "classic:liu-shan",
            "ol:dong-yun", "ol:wang-yun", "ol:sima-hui", "boundary:li-ru", "boundary:ju-shou",
            "boundary:xun-yu", "boundary:huang-gai", "boundary:huang-yueying", "boundary:zhang-song",
            "boundary:gan-ning", "boundary:bu-lian-shi", "boundary:zhang-zhao-zhang-hong",
            "ol:bao-san-niang", "ol:guan-suo", "classic:wang-ji", "classic:qu-yi", "boundary:xu-sheng",
            "mou:guan-yu-ol", "friend:zhuge-liang", "ol:shen-zhuge-liang", "ol:shen-zhou-yu");
        return new ReadOnlyDictionary<string, int>(weights);

        void Add(int weight, params string[] ids)
        {
            foreach (var id in ids) weights.Add(id, weight);
        }
    }
}
