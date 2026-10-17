namespace CardGame.Content.Standard;

// Edition identities, never character-family identities: upgraded editions of
// these people remain eligible for eight-player identity games.
internal static class IdentityGeneralDraftPools
{
    // Standard and the four original ordinary myth-expansion rosters. God
    // editions belong to their separate series and retain their direct-draw rule.
    internal static IReadOnlyList<string> ClassicFive { get; } = Array.AsReadOnly(new[]
    {
        "cao-cao", "sima-yi", "xiahou-dun", "zhang-liao", "xu-chu", "guo-jia", "zhen-ji",
        "liu-bei", "guan-yu", "zhang-fei", "zhuge-liang", "zhao-yun", "ma-chao", "huang-yueying",
        "sun-quan", "gan-ning", "lu-meng", "huang-gai", "zhou-yu", "da-qiao", "lu-xun", "sun-shangxiang",
        "hua-tuo", "lu-bu", "diao-chan",
        "xiahou-yuan", "cao-ren", "huang-zhong", "wei-yan", "xiao-qiao", "zhou-tai", "zhang-jiao", "yu-ji",
        "dian-wei", "xun-yu", "pang-tong", "wolong-zhuge-liang", "taishi-ci", "pang-de", "yan-liang-wen-chou", "yuan-shao",
        "xu-huang", "cao-pi", "sun-jian", "dong-zhuo", "zhu-rong", "meng-huo", "jia-xu", "lu-su",
        "zhang-he", "deng-ai", "jiang-wei", "liu-shan", "sun-ce", "zhang-zhao-zhang-hong", "cai-wen-ji", "zuo-ci"
    }.Select(name => "classic:" + name).ToArray());

    internal static IReadOnlyList<string> EightPlayer(IReadOnlyList<string> completePool)
    {
        var missing = ClassicFive.Except(completePool, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("Classic five-player draft references missing editions: " + string.Join(", ", missing));
        var classic = ClassicFive.ToHashSet(StringComparer.Ordinal);
        return Array.AsReadOnly(completePool.Where(id => !classic.Contains(id) &&
            !id.StartsWith("standard:", StringComparison.Ordinal)).ToArray());
    }
}
