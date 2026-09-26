using CardGame.Content.Standard;
using CardGame.Core;

internal static class CurrentClassicContentChecks
{
    public static void CatalogueAndModes()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var classicPackage = registry.Packages.Single(package => package.Id == "standard-classic-generals");
        Require(classicPackage.Version == StandardClassicGeneralPackage.CurrentVersion &&
                registry.Packages.Any(package => package.Id == "standard-rescue-skills"),
            "The current classic roster must declare its current package and rescue dependency.");

        var five = registry.Modes["identity:classic-5"];
        var eight = registry.Modes["identity:classic-8"];
        var fivePool = five.GeneralPoolIds!;
        var eightPool = eight.GeneralPoolIds!;
        Require(five.DeckId == "classic:standard-deck" &&
                eight.DeckId == "classic:standard-deck" &&
                fivePool.Order(StringComparer.Ordinal).SequenceEqual(eightPool.Order(StringComparer.Ordinal)) &&
                fivePool.Distinct(StringComparer.Ordinal).Count() == fivePool.Count &&
                fivePool.All(registry.Generals.ContainsKey),
            "Both current identity modes must use the physical classic deck and a valid, unique general pool.");

        foreach (var generalId in new[]
                 {
                     "classic:liu-bei", "classic:sima-yi", "classic:guan-yu", "classic:zhang-jiao",
                     "classic:gao-shun", "classic:cheng-pu", "classic:han-dang", "classic:cao-chong",
                     "classic:guo-huai", "classic:man-chong", "classic:guan-ping", "classic:gu-yong",
                     "classic:li-dian", "classic:zhu-huan", "classic:zhu-zhi", "boundary:sima-yi",
                     "boundary:cao-cao", "boundary:xu-chu", "boundary:gan-ning", "boundary:zhou-yu",
                     "sp:le-jin"
                 })
            Require(fivePool.Contains(generalId), $"Current identity pool is missing {generalId}.");

        foreach (var general in registry.Generals.Values)
            Require(!string.IsNullOrWhiteSpace(general.Name) && general.BaseHp > 0 &&
                    general.SkillIds.Count > 0 && general.SkillIds.All(registry.Skills.ContainsKey),
                $"{general.Id} must have a playable identity and valid skill definitions.");

        foreach (var (generalId, faction, hp, skillIds) in new[]
                 {
                     ("classic:cao-cao", "wei", 4, new[] { "classic:jianxiong", "classic:hujia" }),
                     ("classic:liu-bei", "shu", 4, new[] { "classic:rende", "classic:jijiang" }),
                     ("classic:sun-quan", "wu", 4, new[] { "classic:zhiheng", "classic:jiuyuan" }),
                     ("classic:sima-yi", "wei", 3, new[] { "classic:feedback", "classic:guicai" }),
                     ("classic:hua-tuo", "qun", 3, new[] { "classic:qingnang", "classic:jijiu" }),
                     ("classic:gu-yong", "wu", 3, new[] { "classic:shenxing", "classic:bingyi" }),
                     ("classic:li-dian", "wei", 3, new[] { "classic:xunxun", "classic:wangxi" })
                 })
        {
            var general = registry.Generals[generalId];
            Require(general.FactionId == faction && general.BaseHp == hp &&
                    general.SkillIds.SequenceEqual(skillIds),
                $"Current {generalId} metadata or skill order drifted.");
        }

        var boundaryFive = registry.Modes["identity:classic-boundary-5"].GeneralPoolIds!;
        var boundaryEight = registry.Modes["identity:classic-boundary-8"].GeneralPoolIds!;
        Require(boundaryFive.Order(StringComparer.Ordinal).SequenceEqual(boundaryEight.Order(StringComparer.Ordinal)) &&
                boundaryFive.Distinct(StringComparer.Ordinal).Count() == boundaryFive.Count &&
                boundaryFive.All(registry.Generals.ContainsKey) &&
                boundaryFive.Contains("boundary:zhang-jiao"),
            "Both boundary identity modes must expose the same valid distinct current roster.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
