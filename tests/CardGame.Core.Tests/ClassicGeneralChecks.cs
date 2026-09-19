using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ClassicGeneralChecks
{
    public static void ContentContract()
    {
        var legacy = StandardContentRegistry.CreateWithRescueSkills();
        var classic = StandardContentRegistry.CreateWithClassicGenerals();
        var legacyClassic = StandardContentRegistry.CreateWithClassicGenerals(legacyRoster: true);
        var tianduClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 1, 0));
        var fanjianClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 2, 0));
        var guanxingClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 3, 0));
        var hujiaClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 4, 0));
        var jijiangClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 5, 0));
        var jiuyuanClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 6, 0));
        var kujinClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 7, 0));
        var qixiClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 8, 0));
        var kejiClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 9, 0));
        var tuxiClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 10, 0));
        var luoyiClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 11, 0));
        var qiangxiClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 12, 0));
        var duanliangClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 13, 0));
        var luoshenClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 14, 0));
        var jizhiClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 15, 0));
        var tieqiClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 16, 0));
        var liegongClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 17, 0));
        var kuangguClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 18, 0));
        var wushuangClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 19, 0));
        var paoxiaoClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 20, 0));
        var longdanClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 21, 0));
        var wushengClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 22, 0));
        var borrowedSwordClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 23, 0));
        var stoneAxeClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 24, 0));
        var zhangbaClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 25, 0));
        var cixiongClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 26, 0));
        var qinglongClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 27, 0));
        var iceSwordClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 28, 0));
        var qilinBowClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 29, 0));
        var fangtianClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 30, 0));
        var gudingClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 31, 0));
        var zhuqueClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 32, 0));
        var tengjiaClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 33, 0));
        var woodenOxClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 35, 0));
        var daQiaoClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 40, 0));
        var diaoChanClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 41, 0));
        var sunShangxiangClassic = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 42, 0));

        Require(!legacy.Packages.Any(package => package.Id == "standard-classic-generals"),
            "The legacy rescue registry must not silently gain the classic roster.");
        Require(classic.Packages.Select(package => $"{package.Id}@{package.Version}")
            .SequenceEqual([
                "standard@1.11.0",
                "standard-active-skills@1.0.0",
                "standard-rescue-skills@1.0.0",
                "standard-classic-generals@1.43.0"]),
            "The classic package signature must be explicit and dependency ordered.");
        var expectedCurrentRoster = new[]
        {
            // Original standard 25.
            "classic:cao-cao", "classic:sima-yi", "classic:xiahou-dun", "classic:zhang-liao",
            "classic:xu-chu", "classic:guo-jia", "classic:zhen-ji", "classic:liu-bei",
            "classic:guan-yu", "classic:zhang-fei", "classic:zhuge-liang", "classic:zhao-yun",
            "classic:ma-chao", "classic:huang-yueying", "classic:sun-quan", "classic:gan-ning",
            "classic:lu-meng", "classic:huang-gai", "classic:zhou-yu", "classic:da-qiao",
            "classic:lu-xun", "classic:sun-shangxiang", "classic:hua-tuo", "classic:lu-bu",
            "classic:diao-chan",
            // Current expansion representatives already shipped by this package.
            "classic:dian-wei", "classic:xu-huang", "classic:huang-zhong", "classic:wei-yan"
        };
        Require(classic.Modes["identity:classic-5"].GeneralPoolIds!
                .Order(StringComparer.Ordinal)
                .SequenceEqual(expectedCurrentRoster.Order(StringComparer.Ordinal)),
            "Classic 1.43 must contain the complete original standard roster plus its four explicit expansion representatives.");
        Require(classic.Generals["classic:da-qiao"] is
                { BaseHp: 3, Gender: GeneralGender.Female } daQiao &&
                daQiao.SkillIds.SequenceEqual(["classic:guose", "classic:liuli"]) &&
                classic.Modes["identity:classic-5"].GeneralPoolIds!.Contains("classic:da-qiao") &&
                !woodenOxClassic.Generals.ContainsKey("classic:da-qiao") &&
                !woodenOxClassic.Skills.ContainsKey("classic:guose") &&
                classic.Generals["classic:diao-chan"] is
                    { BaseHp: 3, Gender: GeneralGender.Female } diaoChan &&
                diaoChan.SkillIds.SequenceEqual(["classic:biyue", "classic:lijian"]) &&
                classic.Modes["identity:classic-5"].GeneralPoolIds!.Contains("classic:diao-chan") &&
                !daQiaoClassic.Generals.ContainsKey("classic:diao-chan") &&
                !daQiaoClassic.Skills.ContainsKey("classic:lijian") &&
                classic.Generals["classic:sun-shangxiang"] is
                    { BaseHp: 3, Gender: GeneralGender.Female } sunShangxiang &&
                sunShangxiang.SkillIds.SequenceEqual(["classic:jieyin", "classic:xiaoji"]) &&
                !diaoChanClassic.Generals.ContainsKey("classic:sun-shangxiang") &&
                !diaoChanClassic.Skills.ContainsKey("classic:jieyin") &&
                classic.Generals["classic:lu-xun"] is { BaseHp: 3 } luXun &&
                luXun.SkillIds.SequenceEqual(["classic:qianxun", "classic:lianying"]) &&
                classic.Modes["identity:classic-5"].GeneralPoolIds!.Contains("classic:lu-xun") &&
                !sunShangxiangClassic.Generals.ContainsKey("classic:lu-xun") &&
                !sunShangxiangClassic.Skills.ContainsKey("classic:qianxun"),
            "Classic 1.40-1.43 must add Da Qiao, Diao Chan, Sun Shangxiang and Lu Xun without changing historical rosters.");
        Require(legacyClassic.Packages.Last().Version == new Version(1, 0, 0) &&
                legacyClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "standard:guo-jia",
                    StringComparer.Ordinal) &&
                !legacyClassic.Generals.ContainsKey("classic:guo-jia"),
            "The legacy classic registry must remain reproducible for 1.0 checkpoints.");
        Require(tianduClassic.Packages.Last().Version == new Version(1, 1, 0) &&
                tianduClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "standard:zhou-yu",
                    StringComparer.Ordinal) &&
                !tianduClassic.Generals.ContainsKey("classic:zhou-yu") &&
                !tianduClassic.Skills.ContainsKey("classic:fanjian"),
            "The Tiandu-era classic registry must remain reproducible for 1.1 checkpoints.");
        Require(fanjianClassic.Packages.Last().Version == new Version(1, 2, 0) &&
                fanjianClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "standard:zhuge-liang",
                    StringComparer.Ordinal) &&
                !fanjianClassic.Generals.ContainsKey("classic:zhuge-liang") &&
                !fanjianClassic.Skills.ContainsKey("classic:guanxing"),
            "The Fanjian-era classic registry must remain reproducible for 1.2 checkpoints.");
        Require(guanxingClassic.Packages.Last().Version == new Version(1, 3, 0) &&
                guanxingClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "standard:cao-cao",
                    StringComparer.Ordinal) &&
                !guanxingClassic.Generals.ContainsKey("classic:cao-cao") &&
                !guanxingClassic.Skills.ContainsKey("classic:hujia"),
            "The Guanxing-era classic registry must remain reproducible for 1.3 checkpoints.");
        Require(hujiaClassic.Packages.Last().Version == new Version(1, 4, 0) &&
                hujiaClassic.Generals["classic:liu-bei"].SkillIds.SequenceEqual(["standard:rende"]) &&
                !hujiaClassic.Skills.ContainsKey("classic:jijiang"),
            "The Hujia-era classic registry must retain Liu Bei without Jijiang for 1.4 checkpoints.");
        Require(jijiangClassic.Packages.Last().Version == new Version(1, 5, 0) &&
                jijiangClassic.Generals["classic:sun-quan"].SkillIds.SequenceEqual(["standard:zhiheng"]) &&
                !jijiangClassic.Skills.ContainsKey("classic:jiuyuan"),
            "The Jijiang-era classic registry must retain Sun Quan without Jiuyuan for 1.5 checkpoints.");
        Require(jiuyuanClassic.Packages.Last().Version == new Version(1, 6, 0) &&
                !jiuyuanClassic.Generals.ContainsKey("classic:huang-gai") &&
                !jiuyuanClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "classic:huang-gai",
                    StringComparer.Ordinal),
            "The Jiuyuan-era classic registry must retain the 1.6 roster without Huang Gai.");
        Require(kujinClassic.Packages.Last().Version == new Version(1, 7, 0) &&
                !kujinClassic.Generals.ContainsKey("classic:gan-ning") &&
                !kujinClassic.Skills.ContainsKey("classic:qixi") &&
                !kujinClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "classic:gan-ning",
                    StringComparer.Ordinal),
            "The Kujin-era classic registry must retain the 1.7 roster without Gan Ning or Qixi.");
        Require(qixiClassic.Packages.Last().Version == new Version(1, 8, 0) &&
                !qixiClassic.Generals.ContainsKey("classic:lu-meng") &&
                !qixiClassic.Skills.ContainsKey("classic:keji") &&
                !qixiClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "classic:lu-meng",
                    StringComparer.Ordinal),
            "The Qixi-era classic registry must retain the 1.8 roster without Lu Meng or Keji.");
        Require(kejiClassic.Packages.Last().Version == new Version(1, 9, 0) &&
                !kejiClassic.Generals.ContainsKey("classic:zhang-liao") &&
                !kejiClassic.Skills.ContainsKey("classic:tuxi") &&
                !kejiClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "classic:zhang-liao",
                    StringComparer.Ordinal),
            "The Keji-era classic registry must retain the 1.9 roster without Zhang Liao or Tuxi.");
        Require(tuxiClassic.Packages.Last().Version == new Version(1, 10, 0) &&
                !tuxiClassic.Generals.ContainsKey("classic:xu-chu") &&
                !tuxiClassic.Skills.ContainsKey("classic:luoyi") &&
                !tuxiClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "classic:xu-chu",
                    StringComparer.Ordinal),
            "The Tuxi-era classic registry must retain the 1.10 roster without Xu Chu or Luoyi.");
        Require(luoyiClassic.Packages.Last().Version == new Version(1, 11, 0) &&
                !luoyiClassic.Generals.ContainsKey("classic:dian-wei") &&
                !luoyiClassic.Skills.ContainsKey("classic:qiangxi") &&
                !luoyiClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "classic:dian-wei",
                    StringComparer.Ordinal),
            "The Luoyi-era classic registry must retain the 1.11 roster without Dian Wei or Qiangxi.");
        Require(qiangxiClassic.Packages.Last().Version == new Version(1, 12, 0) &&
                !qiangxiClassic.Generals.ContainsKey("classic:xu-huang") &&
                !qiangxiClassic.Skills.ContainsKey("classic:duanliang") &&
                !qiangxiClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "classic:xu-huang",
                    StringComparer.Ordinal),
            "The Qiangxi-era classic registry must retain the 1.12 roster without Xu Huang or Duanliang.");
        Require(duanliangClassic.Packages.Last().Version == new Version(1, 13, 0) &&
                !duanliangClassic.Generals.ContainsKey("classic:zhen-ji") &&
                !duanliangClassic.Skills.ContainsKey("classic:luoshen") &&
                !duanliangClassic.Skills.ContainsKey("classic:qingguo") &&
                !duanliangClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "classic:zhen-ji",
                    StringComparer.Ordinal),
            "The Duanliang-era classic registry must retain the 1.13 roster without Zhen Ji, Luoshen or Qingguo.");
        Require(luoshenClassic.Packages.Last().Version == new Version(1, 14, 0) &&
                !luoshenClassic.Generals.ContainsKey("classic:huang-yueying") &&
                !luoshenClassic.Skills.ContainsKey("classic:jizhi") &&
                !luoshenClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "classic:huang-yueying",
                    StringComparer.Ordinal),
            "The Luoshen-era classic registry must retain the 1.14 roster without Huang Yueying or Jizhi.");
        Require(jizhiClassic.Packages.Last().Version == new Version(1, 15, 0) &&
                !jizhiClassic.Generals.ContainsKey("classic:ma-chao") &&
                !jizhiClassic.Skills.ContainsKey("classic:tieqi") &&
                !jizhiClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "classic:ma-chao",
                    StringComparer.Ordinal),
            "The Jizhi-era classic registry must retain the 1.15 roster without Ma Chao or Tieqi.");
        Require(tieqiClassic.Packages.Last().Version == new Version(1, 16, 0) &&
                !tieqiClassic.Generals.ContainsKey("classic:huang-zhong") &&
                !tieqiClassic.Skills.ContainsKey("classic:liegong") &&
                !tieqiClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "classic:huang-zhong",
                    StringComparer.Ordinal),
            "The Tieqi-era classic registry must retain the 1.16 roster without Huang Zhong or Liegong.");
        Require(liegongClassic.Packages.Last().Version == new Version(1, 17, 0) &&
                !liegongClassic.Generals.ContainsKey("classic:wei-yan") &&
                !liegongClassic.Skills.ContainsKey("classic:kuanggu") &&
                !liegongClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "classic:wei-yan",
                    StringComparer.Ordinal),
            "The Liegong-era classic registry must retain the 1.17 roster without Wei Yan or Kuanggu.");
        Require(kuangguClassic.Packages.Last().Version == new Version(1, 18, 0) &&
                !kuangguClassic.Generals.ContainsKey("classic:lu-bu") &&
                !kuangguClassic.Skills.ContainsKey("classic:wushuang") &&
                !kuangguClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "classic:lu-bu",
                    StringComparer.Ordinal),
            "The Kuanggu-era classic registry must retain the 1.18 roster without Lu Bu or Wushuang.");
        Require(wushuangClassic.Packages.Last().Version == new Version(1, 19, 0) &&
                !wushuangClassic.Generals.ContainsKey("classic:zhang-fei") &&
                !wushuangClassic.Skills.ContainsKey("classic:paoxiao") &&
                wushuangClassic.Generals.ContainsKey("standard:zhang-fei") &&
                wushuangClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "standard:zhang-fei",
                    StringComparer.Ordinal) &&
                !wushuangClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "classic:zhang-fei",
                    StringComparer.Ordinal),
            "The Wushuang-era classic registry must retain the 1.19 standard Zhang Fei identity.");
        Require(paoxiaoClassic.Packages.Last().Version == new Version(1, 20, 0) &&
                !paoxiaoClassic.Generals.ContainsKey("classic:zhao-yun") &&
                !paoxiaoClassic.Skills.ContainsKey("classic:longdan") &&
                paoxiaoClassic.Generals.ContainsKey("standard:zhao-yun") &&
                paoxiaoClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "standard:zhao-yun",
                    StringComparer.Ordinal) &&
                !paoxiaoClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "classic:zhao-yun",
                    StringComparer.Ordinal),
            "The Paoxiao-era classic registry must retain the 1.20 standard Zhao Yun identity.");
        Require(longdanClassic.Packages.Last().Version == new Version(1, 21, 0) &&
                !longdanClassic.Generals.ContainsKey("classic:guan-yu") &&
                !longdanClassic.Skills.ContainsKey("classic:wusheng") &&
                longdanClassic.Generals.ContainsKey("standard:guan-yu") &&
                longdanClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "standard:guan-yu",
                    StringComparer.Ordinal) &&
                !longdanClassic.Modes["identity:classic-5"].GeneralPoolIds!.Contains(
                    "classic:guan-yu",
                    StringComparer.Ordinal),
            "The Longdan-era classic registry must retain the 1.21 standard Guan Yu identity.");
        Require(wushengClassic.Packages.Last().Version == new Version(1, 22, 0) &&
                !wushengClassic.Cards.ContainsKey("classic:borrowed-sword") &&
                !wushengClassic.Decks.ContainsKey("classic:standard-deck") &&
                wushengClassic.Modes["identity:classic-5"].DeckId == "standard:basic-demo",
            "The Wusheng-era classic registry must retain the 1.22 deck without Borrowed Sword.");
        Require(borrowedSwordClassic.Cards["classic:borrowed-sword"].LegacyKind == CardKind.BorrowedSword &&
                !borrowedSwordClassic.Cards.ContainsKey("classic:stone-axe") &&
                borrowedSwordClassic.Decks["classic:standard-deck"].Cards.Sum(card => card.Count) == 92,
            "The 1.23 classic registry must retain its 92-card Borrowed Sword deck without Stone Axe.");
        Require(stoneAxeClassic.Cards["classic:stone-axe"].LegacyKind == CardKind.StoneAxe &&
                !stoneAxeClassic.Cards.ContainsKey("classic:zhangba-serpent-spear") &&
                stoneAxeClassic.Decks["classic:standard-deck"].Cards.Sum(card => card.Count) == 93,
            "The 1.24 classic registry must retain its 93-card Stone Axe deck without Zhangba.");
        Require(zhangbaClassic.Cards["classic:zhangba-serpent-spear"].LegacyKind == CardKind.ZhangbaSerpentSpear &&
                !zhangbaClassic.Cards.ContainsKey("classic:cixiong-double-swords") &&
                zhangbaClassic.Decks["classic:standard-deck"].Cards.Sum(card => card.Count) == 94 &&
                zhangbaClassic.Generals["classic:zhen-ji"].Gender == GeneralGender.Male &&
                zhangbaClassic.Generals["classic:huang-yueying"].Gender == GeneralGender.Male,
            "The 1.25 classic registry must retain its 94-card Zhangba deck and legacy gender-neutral projection.");
        Require(cixiongClassic.Cards["classic:cixiong-double-swords"].LegacyKind == CardKind.CixiongDoubleSwords &&
                !cixiongClassic.Cards.ContainsKey("classic:qinglong-crescent-blade") &&
                cixiongClassic.Decks["classic:standard-deck"].Cards.Sum(card => card.Count) == 96 &&
                cixiongClassic.Generals["classic:zhen-ji"].Gender == GeneralGender.Female &&
                cixiongClassic.Generals["classic:huang-yueying"].Gender == GeneralGender.Female,
            "The 1.26 classic registry must retain its 96-card Cixiong deck and typed gender.");
        Require(qinglongClassic.Cards["classic:qinglong-crescent-blade"].LegacyKind == CardKind.QinglongCrescentBlade &&
                !qinglongClassic.Cards.ContainsKey("classic:ice-sword") &&
                qinglongClassic.Decks["classic:standard-deck"].Cards.Sum(card => card.Count) == 97,
            "The 1.27 classic registry must retain its 97-card Qinglong deck without Ice Sword.");
        Require(iceSwordClassic.Cards["classic:ice-sword"].LegacyKind == CardKind.IceSword &&
                !iceSwordClassic.Cards.ContainsKey("classic:qilin-bow") &&
                iceSwordClassic.Decks["classic:standard-deck"].Cards.Sum(card => card.Count) == 98,
            "The 1.28 classic registry must retain its 98-card Ice Sword deck without Qilin Bow.");
        Require(qilinBowClassic.Cards["classic:qilin-bow"].LegacyKind == CardKind.QilinBow &&
                !qilinBowClassic.Cards.ContainsKey("classic:fangtian-halberd") &&
                qilinBowClassic.Decks["classic:standard-deck"].Cards.Sum(card => card.Count) == 99,
            "The 1.29 classic registry must retain its 99-card Qilin Bow deck without Fangtian Halberd.");
        Require(fangtianClassic.Cards["classic:fangtian-halberd"].LegacyKind == CardKind.FangtianHalberd &&
                !fangtianClassic.Cards.ContainsKey("classic:guding-blade") &&
                fangtianClassic.Decks["classic:standard-deck"].Cards.Sum(card => card.Count) == 100,
            "The 1.30 classic registry must retain its 100-card Fangtian deck without Guding Blade.");
        Require(gudingClassic.Cards["classic:guding-blade"].LegacyKind == CardKind.GudingBlade &&
                !gudingClassic.Cards.ContainsKey("classic:zhuque-fan") &&
                gudingClassic.Decks["classic:standard-deck"].Cards.Sum(card => card.Count) == 101,
            "The 1.31 classic registry must retain its 101-card Guding deck without Zhuque Fan.");
        Require(zhuqueClassic.Cards["classic:zhuque-fan"].LegacyKind == CardKind.ZhuqueFan &&
                !zhuqueClassic.Cards.ContainsKey("classic:tengjia") &&
                zhuqueClassic.Decks["classic:standard-deck"].Cards.Sum(card => card.Count) == 102,
            "The 1.32 classic registry must retain its 102-card Zhuque deck without Tengjia.");
        Require(tengjiaClassic.Cards["classic:tengjia"].LegacyKind == CardKind.Tengjia &&
                !tengjiaClassic.Cards.ContainsKey("classic:silver-lion") &&
                tengjiaClassic.Decks["classic:standard-deck"].Cards.Sum(card => card.Count) == 103,
            "The 1.33 classic registry must retain its 103-card Tengjia deck without Silver Lion.");
        Require(classic.Cards["classic:borrowed-sword"].LegacyKind == CardKind.BorrowedSword &&
                classic.Cards["classic:stone-axe"].LegacyKind == CardKind.StoneAxe &&
                classic.Cards["classic:zhangba-serpent-spear"].LegacyKind == CardKind.ZhangbaSerpentSpear &&
                classic.Cards["classic:cixiong-double-swords"].LegacyKind == CardKind.CixiongDoubleSwords &&
                classic.Cards["classic:qinglong-crescent-blade"].LegacyKind == CardKind.QinglongCrescentBlade &&
                classic.Cards["classic:ice-sword"].LegacyKind == CardKind.IceSword &&
                classic.Cards["classic:qilin-bow"].LegacyKind == CardKind.QilinBow &&
                classic.Cards["classic:fangtian-halberd"].LegacyKind == CardKind.FangtianHalberd &&
                classic.Cards["classic:guding-blade"].LegacyKind == CardKind.GudingBlade &&
                classic.Cards["classic:zhuque-fan"].LegacyKind == CardKind.ZhuqueFan &&
                classic.Cards["classic:tengjia"].LegacyKind == CardKind.Tengjia &&
                classic.Cards["classic:silver-lion"].LegacyKind == CardKind.SilverLion &&
                woodenOxClassic.Decks["classic:standard-deck"].Cards.Sum(card => card.Count) == 105 &&
                woodenOxClassic.Decks["classic:standard-deck"].PhysicalCards is null &&
                classic.Decks["classic:standard-deck"].Cards.Count == 0 &&
                classic.Decks["classic:standard-deck"].PhysicalCards?.Count == 160 &&
                classic.Generals["classic:zhen-ji"].Gender == GeneralGender.Female &&
                classic.Generals["classic:huang-yueying"].Gender == GeneralGender.Female &&
                classic.Modes["identity:classic-5"].DeckId == "classic:standard-deck" &&
                classic.Modes["identity:classic-8"].DeckId == "classic:standard-deck",
            "The current classic registry must use the exact 160-card military deck while 1.35 retains its 105-card hybrid recipe.");
        Require(classic.ContentHash != legacy.ContentHash,
            "The opt-in classic roster must have its own content fingerprint.");

        var simaYi = classic.Generals["classic:sima-yi"];
        Require(simaYi.Name == "司马懿" && simaYi.BaseHp == 3 &&
                simaYi.SkillIds.SequenceEqual(["classic:feedback", "standard:guicai"]),
            "Sima Yi must expose Feedback and Guicai in a stable order.");
        var huaTuo = classic.Generals["classic:hua-tuo"];
        Require(huaTuo.Name == "华佗" && huaTuo.BaseHp == 3 &&
                huaTuo.SkillIds.SequenceEqual(["standard:qingnang", "standard:jijiu"]),
            "Hua Tuo must expose Qingnang and Jijiu in a stable order.");
        Require(classic.Generals["classic:liu-bei"].SkillIds.SequenceEqual(["standard:rende", "classic:jijiang"]) &&
                classic.Generals["classic:sun-quan"].SkillIds.SequenceEqual(["standard:zhiheng", "classic:jiuyuan"]) &&
                classic.Generals["classic:xiahou-dun"].SkillIds.SequenceEqual(["standard:ganglie"]),
            "The current classic roster must point at the implemented formal skills.");
        var guoJia = classic.Generals["classic:guo-jia"];
        Require(guoJia.BaseHp == 3 &&
                guoJia.SkillIds.SequenceEqual(["classic:tiandu", "standard:yiji"]),
            "The current classic Guo Jia must expose Tiandu and Yiji in a stable order.");
        var zhouYu = classic.Generals["classic:zhou-yu"];
        Require(zhouYu.BaseHp == 3 &&
                zhouYu.SkillIds.SequenceEqual(["standard:yingzi", "classic:fanjian"]),
            "The current classic Zhou Yu must expose Yingzi and Fanjian in a stable order.");
        var zhugeLiang = classic.Generals["classic:zhuge-liang"];
        Require(zhugeLiang.BaseHp == 3 &&
                zhugeLiang.SkillIds.SequenceEqual(["classic:guanxing", "standard:kongcheng"]),
            "The current classic Zhuge Liang must expose Guanxing and Kongcheng in a stable order.");
        var caoCao = classic.Generals["classic:cao-cao"];
        Require(caoCao.BaseHp == 4 &&
                caoCao.SkillIds.SequenceEqual(["standard:jianxiong", "classic:hujia"]),
            "The current classic Cao Cao must expose Jianxiong and Hujia in a stable order.");
        var huangGai = classic.Generals["classic:huang-gai"];
        Require(huangGai.Name == "黄盖" &&
                huangGai.FactionId == "wu" &&
                huangGai.BaseHp == 4 &&
                huangGai.SkillIds.SequenceEqual(["standard:kujin"]),
            "The current classic Huang Gai must expose the formal Wu, 4-HP Kujin definition.");
        var ganNing = classic.Generals["classic:gan-ning"];
        Require(ganNing.Name == "甘宁" &&
                ganNing.FactionId == "wu" &&
                ganNing.BaseHp == 4 &&
                ganNing.SkillIds.SequenceEqual(["classic:qixi"]),
            "The current classic Gan Ning must expose the formal Wu, 4-HP Qixi definition.");
        var luMeng = classic.Generals["classic:lu-meng"];
        Require(luMeng.Name == "吕蒙" &&
                luMeng.FactionId == "wu" &&
                luMeng.BaseHp == 4 &&
                luMeng.SkillIds.SequenceEqual(["classic:keji"]),
            "The current classic Lu Meng must expose the formal Wu, 4-HP Keji definition.");
        var zhangLiao = classic.Generals["classic:zhang-liao"];
        Require(zhangLiao.Name == "张辽" &&
                zhangLiao.FactionId == "wei" &&
                zhangLiao.BaseHp == 4 &&
                zhangLiao.SkillIds.SequenceEqual(["classic:tuxi"]),
            "The current classic Zhang Liao must expose the formal Wei, 4-HP Tuxi definition.");
        var xuChu = classic.Generals["classic:xu-chu"];
        Require(xuChu.Name == "许褚" &&
                xuChu.FactionId == "wei" &&
                xuChu.BaseHp == 4 &&
                xuChu.SkillIds.SequenceEqual(["classic:luoyi"]),
            "The current classic Xu Chu must expose the formal Wei, 4-HP Luoyi definition.");
        var dianWei = classic.Generals["classic:dian-wei"];
        Require(dianWei.Name == "典韦" &&
                dianWei.FactionId == "wei" &&
                dianWei.BaseHp == 4 &&
                dianWei.SkillIds.SequenceEqual(["classic:qiangxi"]),
            "The current classic Dian Wei must expose the formal Wei, 4-HP Qiangxi definition.");
        var xuHuang = classic.Generals["classic:xu-huang"];
        Require(xuHuang.Name == "徐晃" &&
                xuHuang.FactionId == "wei" &&
                xuHuang.BaseHp == 4 &&
                xuHuang.SkillIds.SequenceEqual(["classic:duanliang"]),
            "The current classic Xu Huang must expose the formal Wei, 4-HP Duanliang definition.");
        var zhenJi = classic.Generals["classic:zhen-ji"];
        Require(zhenJi.Name == "甄姬" &&
                zhenJi.FactionId == "wei" &&
                zhenJi.BaseHp == 3 &&
                zhenJi.SkillIds.SequenceEqual(["classic:luoshen", "classic:qingguo"]),
            "The current classic Zhen Ji must expose formal Wei, 3-HP Luoshen and Qingguo in a stable order.");
        var huangYueying = classic.Generals["classic:huang-yueying"];
        Require(huangYueying.Name == "黄月英" &&
                huangYueying.FactionId == "shu" &&
                huangYueying.BaseHp == 3 &&
                huangYueying.SkillIds.SequenceEqual(["classic:jizhi", "standard:qicai"]),
            "The current classic Huang Yueying must expose formal Shu, 3-HP Jizhi and Qicai in a stable order.");
        var maChao = classic.Generals["classic:ma-chao"];
        Require(maChao.Name == "马超" &&
                maChao.FactionId == "shu" &&
                maChao.BaseHp == 4 &&
                maChao.SkillIds.SequenceEqual(["classic:tieqi", "standard:mashu"]),
            "The current classic Ma Chao must expose formal Shu, 4-HP Tieqi and Mashu in a stable order.");
        var huangZhong = classic.Generals["classic:huang-zhong"];
        Require(huangZhong.Name == "黄忠" &&
                huangZhong.FactionId == "shu" &&
                huangZhong.BaseHp == 4 &&
                huangZhong.SkillIds.SequenceEqual(["classic:liegong"]),
            "The current classic Huang Zhong must expose formal Shu, 4-HP Liegong.");
        var weiYan = classic.Generals["classic:wei-yan"];
        Require(weiYan.Name == "魏延" &&
                weiYan.FactionId == "shu" &&
                weiYan.BaseHp == 4 &&
                weiYan.SkillIds.SequenceEqual(["classic:kuanggu"]),
            "The current classic Wei Yan must expose formal Shu, 4-HP Kuanggu.");
        var luBu = classic.Generals["classic:lu-bu"];
        Require(luBu.Name == "吕布" &&
                luBu.FactionId == "qun" &&
                luBu.BaseHp == 4 &&
                luBu.SkillIds.SequenceEqual(["classic:wushuang"]),
            "The current classic Lu Bu must expose formal Qun, 4-HP Wushuang.");
        var zhangFei = classic.Generals["classic:zhang-fei"];
        Require(zhangFei.Name == "张飞" &&
                zhangFei.FactionId == "shu" &&
                zhangFei.BaseHp == 4 &&
                zhangFei.SkillIds.SequenceEqual(["classic:paoxiao"]),
            "The current classic Zhang Fei must expose formal Shu, 4-HP Paoxiao.");
        var zhaoYun = classic.Generals["classic:zhao-yun"];
        Require(zhaoYun.Name == "赵云" &&
                zhaoYun.FactionId == "shu" &&
                zhaoYun.BaseHp == 4 &&
                zhaoYun.SkillIds.SequenceEqual(["classic:longdan"]),
            "The current classic Zhao Yun must expose formal Shu, 4-HP Longdan.");
        var guanYu = classic.Generals["classic:guan-yu"];
        Require(guanYu.Name == "关羽" &&
                guanYu.FactionId == "shu" &&
                guanYu.BaseHp == 4 &&
                guanYu.SkillIds.SequenceEqual(["classic:wusheng"]) &&
                classic.Skills["classic:wusheng"].Description ==
                    "你可以将一张红色牌当【杀】使用或打出。",
            "The current classic Guan Yu must expose formal Shu, 4-HP Wusheng.");

        foreach (var modeId in new[] { "identity:classic-5", "identity:classic-8" })
        {
            var mode = classic.Modes[modeId];
            var pool = mode.GeneralPoolIds ?? [];
            Require(pool.Contains("classic:sima-yi", StringComparer.Ordinal) &&
                    pool.Contains("classic:hua-tuo", StringComparer.Ordinal) &&
                    pool.Contains("classic:zhuge-liang", StringComparer.Ordinal) &&
                    pool.Contains("classic:cao-cao", StringComparer.Ordinal) &&
                    pool.Contains("classic:huang-gai", StringComparer.Ordinal) &&
                    pool.Contains("classic:gan-ning", StringComparer.Ordinal) &&
                    pool.Contains("classic:lu-meng", StringComparer.Ordinal) &&
                    pool.Contains("classic:zhang-liao", StringComparer.Ordinal) &&
                    pool.Contains("classic:xu-chu", StringComparer.Ordinal) &&
                    pool.Contains("classic:dian-wei", StringComparer.Ordinal) &&
                    pool.Contains("classic:xu-huang", StringComparer.Ordinal) &&
                    pool.Contains("classic:zhen-ji", StringComparer.Ordinal) &&
                    pool.Contains("classic:huang-yueying", StringComparer.Ordinal) &&
                    pool.Contains("classic:ma-chao", StringComparer.Ordinal) &&
                    pool.Contains("classic:huang-zhong", StringComparer.Ordinal) &&
                    pool.Contains("classic:wei-yan", StringComparer.Ordinal) &&
                    pool.Contains("classic:lu-bu", StringComparer.Ordinal) &&
                    pool.Contains("classic:zhang-fei", StringComparer.Ordinal) &&
                    pool.Contains("classic:zhao-yun", StringComparer.Ordinal) &&
                    pool.Contains("classic:guan-yu", StringComparer.Ordinal) &&
                    !pool.Contains("standard:zhang-fei", StringComparer.Ordinal) &&
                    !pool.Contains("standard:zhao-yun", StringComparer.Ordinal) &&
                    !pool.Contains("standard:guan-yu", StringComparer.Ordinal) &&
                    !pool.Any(id => id.StartsWith("standard:demo-", StringComparison.Ordinal)),
                $"{modeId} must publish formal generals instead of demo placeholders.");
        }

        Require(GameCheckpoint.CurrentRulesVersion >= 40,
            "Classic Wusheng equipment conversion must have an explicit replay-versioned rules boundary.");
        var feedback = SkillRegistry.Get(SkillKind.Feedback);
        var damaged = new PlayerSkillContext(0, 2, 3, 2, TurnPhase.Play);
        var feedbackContext = new DamageSkillContext(
            damaged,
            SourceSeat: 1,
            SourceCard: CardKind.Slash,
            SourceCardIsInProcessing: true,
            Amount: 1,
            TargetSeat: 0,
            SourceCardCount: 2);
        Require(feedback.ClaimsDamageCard(feedbackContext) &&
                feedback.GetDamageSkillEffect(feedbackContext) == DamageSkillEffectKind.TakeSourceCard,
            "Feedback must retain the legacy hook while declaring its formal source-card effect.");

        var jijiu = SkillRegistry.Get(SkillKind.Jijiu);
        var red = new Card(9001, CardKind.Slash, Suit.Heart, 7);
        Require(!jijiu.CanUseAsDyingRescue(damaged with { IsOwnTurn = true }, red) &&
                jijiu.CanUseAsDyingRescue(damaged with { IsOwnTurn = false }, red),
            "Formal Jijiu must only convert red cards outside the owner's turn.");

        var keji = SkillRegistry.Get(SkillKind.Keji);
        var discard = damaged with { Phase = TurnPhase.Discard, IsOwnTurn = true };
        Require(keji.CanSkipDiscardPhase(discard, usedOrPlayedSlashDuringPlayPhase: false) &&
                !keji.CanSkipDiscardPhase(discard, usedOrPlayedSlashDuringPlayPhase: true),
            "Formal Keji must allow only a Slash-free own discard phase to be skipped.");

        var wushuang = SkillRegistry.Get(SkillKind.Wushuang);
        var wushuangOwner = damaged with { Seat = 1 };
        Require(wushuang.ModifyRequiredResponseCount(
                    new ResponseCountSkillContext(
                        wushuangOwner,
                        SourceSeat: 1,
                        ResponderSeat: 0,
                        IncomingCard: CardKind.Slash,
                        RequiredCardKind: CardKind.Dodge),
                    currentCount: 1) == 2 &&
                wushuang.ModifyRequiredResponseCount(
                    new ResponseCountSkillContext(
                        wushuangOwner,
                        SourceSeat: 1,
                        ResponderSeat: 0,
                        IncomingCard: CardKind.Duel,
                        RequiredCardKind: CardKind.Slash),
                    currentCount: 1) == 2 &&
                wushuang.ModifyRequiredResponseCount(
                    new ResponseCountSkillContext(
                        wushuangOwner,
                        SourceSeat: 0,
                        ResponderSeat: 1,
                        IncomingCard: CardKind.Duel,
                        RequiredCardKind: CardKind.Slash),
                    currentCount: 1) == 1,
            "Formal Wushuang must require two sequential responses only from the skill owner's opponent.");

        var tuxi = SkillRegistry.Get(SkillKind.Tuxi);
        var draw = damaged with { Phase = TurnPhase.Draw, IsOwnTurn = true };
        Require(tuxi.CanReplaceDrawPhase(draw) &&
                !tuxi.CanReplaceDrawPhase(draw with { IsOwnTurn = false }),
            "Formal Tuxi must replace only its owner's draw phase.");

        var luoyi = SkillRegistry.Get(SkillKind.Luoyi);
        Require(luoyi.CanReduceDrawPhase(draw) &&
                !luoyi.CanReduceDrawPhase(draw with { IsOwnTurn = false }),
            "Formal Luoyi must reduce only its owner's draw phase.");

        var qiangxi = SkillRegistry.GetActive(SkillKind.Qiangxi) ??
            throw new InvalidOperationException("Formal Qiangxi must expose an active-skill contract.");
        var qiangxiOwner = damaged with
        {
            Phase = TurnPhase.Play,
            IsOwnTurn = true,
            UsedActiveSkillKinds = new HashSet<SkillKind>()
        };
        var hpCost = qiangxi.GetEffect(new ActiveSkillContext(qiangxiOwner));
        var weaponCost = qiangxi.GetEffect(new ActiveSkillContext(qiangxiOwner, SelectedCardCount: 1));
        Require(qiangxi.CanUse(new ActiveSkillContext(qiangxiOwner)) &&
                hpCost.Kind == ActiveSkillEffectKind.PayHpOrDiscardWeaponAndDamage &&
                hpCost.HpCost == 1 &&
                weaponCost.HpCost == 0 &&
                hpCost.MinCardCount == 0 && hpCost.MaxCardCount == 1 &&
                hpCost.MinTargetCount == 1 && hpCost.MaxTargetCount == 1,
            "Formal Qiangxi must expose the one-HP-or-one-weapon, one-target damage contract.");

        var duanliang = SkillRegistry.Get(SkillKind.Duanliang);
        Require(duanliang.CanUseAsSupplyShortage(damaged, new Card(9101, CardKind.Slash, Suit.Spade, 7)) &&
                duanliang.CanUseAsSupplyShortage(damaged, new Card(9102, CardKind.Crossbow, Suit.Club, 6)) &&
                !duanliang.CanUseAsSupplyShortage(damaged, new Card(9103, CardKind.Duel, Suit.Spade, 1)) &&
                !duanliang.CanUseAsSupplyShortage(damaged, new Card(9104, CardKind.Slash, Suit.Heart, 8)) &&
                duanliang.ModifySupplyShortageDistanceLimit(damaged, 1) == 2,
            "Formal Duanliang must accept only black basic/equipment cards and extend Supply Shortage to distance two.");

        var qingguo = SkillRegistry.Get(SkillKind.Qingguo);
        Require(qingguo.CanUseAsResponse(damaged, new Card(9201, CardKind.Slash, Suit.Spade, 7), CardKind.Dodge) &&
                qingguo.CanUseAsResponse(damaged, new Card(9202, CardKind.Duel, Suit.Club, 1), CardKind.Dodge) &&
                !qingguo.CanUseAsResponse(damaged, new Card(9203, CardKind.Slash, Suit.Heart, 8), CardKind.Dodge) &&
                !qingguo.CanUseAsResponse(damaged, new Card(9204, CardKind.Slash, Suit.Spade, 9), CardKind.Slash),
            "Formal Qingguo must convert only black cards into Dodge responses.");

        var kuanggu = SkillRegistry.Get(SkillKind.Kuanggu);
        var kuangguContext = new DamageSkillContext(
            new PlayerSkillContext(0, 1, 4, 2, TurnPhase.Play),
            SourceSeat: 0,
            SourceCard: CardKind.Slash,
            SourceCardIsInProcessing: true,
            Amount: 2,
            TargetSeat: 1,
            SourceToTargetDistance: 1);
        Require(kuanggu.AfterDamageTriggerScope == DamageTriggerScope.DamageSource &&
                kuanggu.DamageTriggerPriority == 100 &&
                kuanggu.CanTriggerAfterDamage(kuangguContext) &&
                kuanggu.GetDamageSkillEffect(kuangguContext) == DamageSkillEffectKind.RecoverDamageSource &&
                !kuanggu.CanTriggerAfterDamage(kuangguContext with { SourceToTargetDistance = 2 }) &&
                !kuanggu.CanTriggerAfterDamage(kuangguContext with
                {
                    Owner = kuangguContext.Owner with { Hp = 4 }
                }),
            "Formal Kuanggu must be a high-priority locked damage-source recovery within distance one.");
    }

    public static void FormalJiuyuanRecoveryBonus()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var (current, providerSeat, peachCardId, selfPeachCardId, nonWuProviderSeat, nonWuPeachCardId) =
            FindJiuyuanFixture(registry);
        var checkpoint = current.CreateCheckpoint();
        var legacy = GameReplay.Restore(
            checkpoint with { RulesVersion = 26 },
            registry);
        var selfRescue = GameReplay.Restore(checkpoint, registry);
        var nonWuRescue = GameReplay.Restore(checkpoint, registry);

        ApplySyntheticDyingPeach(current, providerSeat, peachCardId);
        var currentSun = current.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        var applied = current.Events.Select(item => item.Payload).OfType<JiuyuanAppliedEvent>().Single();
        Require(currentSun.Hp == 2 &&
                applied.OwnerSeat == 0 &&
                applied.ProviderSeat == providerSeat &&
                applied.PeachCardId == peachCardId &&
                applied.RecoveryAmount == 2 &&
                current.Events.Select(item => item.Payload).OfType<RecoveryAppliedEvent>().Last().Amount == 2,
            "A different Wu provider's Peach must recover the dying Lord Sun Quan for two through Jiuyuan.");

        ApplySyntheticDyingPeach(legacy, providerSeat, peachCardId);
        var legacySun = legacy.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(legacySun.Hp == 1 &&
                legacy.Events.Select(item => item.Payload).OfType<JiuyuanAppliedEvent>().Count() == 0 &&
                legacy.Events.Select(item => item.Payload).OfType<RecoveryAppliedEvent>().Last().Amount == 1,
            "Rules v26 must keep the same physical Peach at the historical one-point recovery amount.");

        ApplySyntheticDyingPeach(selfRescue, 0, selfPeachCardId);
        Require(selfRescue.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hp == 1 &&
                selfRescue.Events.Select(item => item.Payload).OfType<JiuyuanAppliedEvent>().Count() == 0,
            "Sun Quan's own Peach must not receive Jiuyuan's recovery bonus.");

        ApplySyntheticDyingPeach(nonWuRescue, nonWuProviderSeat, nonWuPeachCardId);
        Require(nonWuRescue.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hp == 1 &&
                nonWuRescue.Events.Select(item => item.Payload).OfType<JiuyuanAppliedEvent>().Count() == 0,
            "A non-Wu provider's Peach must not receive Jiuyuan's recovery bonus.");
    }

    public static void SetupHealthAndReplay()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var currentSun = SelectGeneral(registry, "classic:sun-quan", GameCheckpoint.CurrentRulesVersion);
        var currentSunPlayer = currentSun.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(currentSunPlayer.MaxHp == 5 && currentSunPlayer.Hp == 5,
            "A classic 4-HP Lord must receive the identity-mode +1 maximum HP.");
        Require(currentSunPlayer.Skills is { Count: 2 } &&
                currentSunPlayer.Skills.Select(skill => skill.Kind).SequenceEqual([SkillKind.Zhiheng, SkillKind.Jiuyuan]),
            "The current snapshot must publish the selected general's ordered skill list.");

        var legacySun = SelectGeneral(registry, "classic:sun-quan", rulesVersion: 9);
        var legacySunPlayer = legacySun.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(legacySunPlayer.MaxHp == 5 && legacySunPlayer.Hp == 5 && legacySunPlayer.Skills is null,
            $"A rules-v9 replay must retain the old fixed 5-HP Lord rule and singular skill projection " +
            $"(rules={legacySun.RulesVersion}, hp={legacySunPlayer.Hp}/{legacySunPlayer.MaxHp}, skills={legacySunPlayer.Skills?.Count.ToString() ?? "null"}).");

        var simaYi = SelectGeneral(registry, "classic:sima-yi", GameCheckpoint.CurrentRulesVersion);
        var simaYiPlayer = simaYi.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(simaYiPlayer.MaxHp == 4 &&
                simaYiPlayer.Skills!.Select(skill => skill.Kind).SequenceEqual([SkillKind.Feedback, SkillKind.Guicai]),
            "Sima Yi must combine base 3 HP, the Lord bonus, Feedback and Guicai.");
        var legacySimaYi = SelectGeneral(registry, "classic:sima-yi", rulesVersion: 9)
            .CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(legacySimaYi.MaxHp == 5 && legacySimaYi.Skills is null,
            "Rules v9 must ignore the new base HP and additional-skill fields.");

        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(simaYi.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(simaYi.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(simaYi)),
            "A selected multi-skill classic general must replay exactly.");
    }

    public static void FormalKujinFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = SelectGeneral(registry, "classic:huang-gai", GameCheckpoint.CurrentRulesVersion);
        var selected = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(selected.GeneralId == "classic:huang-gai" &&
                selected.MaxHp == 5 &&
                selected.Hp == 5 &&
                selected.Skills!.Select(skill => skill.Kind).SequenceEqual([SkillKind.Kujin]),
            "Classic Huang Gai must combine base 4 HP, the Lord bonus and the formal Kujin skill.");

        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted, advanced.Error?.Message ?? "Classic Huang Gai setup did not advance.");
        var before = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        for (var use = 0; use < 2; use++)
        {
            var prompt = game.PendingDecision ??
                throw new InvalidOperationException("Classic Huang Gai did not remain at the human play boundary.");
            Require(prompt.Kind == DecisionKind.PlayCard &&
                    game.GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.UseSkill && action.Skill == SkillKind.Kujin),
                "Classic Huang Gai must publish Kujin as a legal play action.");
            var used = game.Submit(new UseSkillCommand(
                0,
                SkillKind.Kujin,
                [],
                [],
                game.Revision,
                prompt.PromptId));
            Require(used.Accepted, used.Error?.Message ?? "Classic Huang Gai's Kujin command was rejected.");
            for (var step = 0; step < 16 && game.PendingDecision is null; step++)
            {
                var resumed = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Require(resumed.Accepted, resumed.Error?.Message ??
                    "Classic Huang Gai's Kujin frame did not return to the play boundary.");
            }
        }

        var after = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(after.Hp == before.Hp - 2 &&
                after.HandCount == before.HandCount + 4 &&
                game.Events.Select(item => item.Payload).OfType<ActiveSkillResolvedEvent>()
                    .Count(item => item.Skill == SkillKind.Kujin) == 2,
            "Classic Kujin must remain repeatable in one play phase and resolve one HP for two cards each time.");

        var restored = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(game)),
            "Repeated formal Kujin commands must restore with identical state and events.");
    }

    public static void FormalQixiFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(GameCheckpoint.CurrentRulesVersion >= 28,
            "Formal Qixi must have an explicit rules-version boundary.");

        var qixi = SkillRegistry.Get(SkillKind.Qixi);
        var context = new PlayerSkillContext(0, 4, 5, 4, TurnPhase.Play);
        Require(qixi.CanUseAsDismantlement(context, new Card(9101, CardKind.Crossbow, Suit.Club, 1)) &&
                qixi.CanUseAsDismantlement(context, new Card(9102, CardKind.Peach, Suit.Spade, 6)) &&
                !qixi.CanUseAsDismantlement(context, new Card(9103, CardKind.Peach, Suit.Heart, 6)) &&
                !qixi.CanUseAsDismantlement(context, new Card(9104, CardKind.Dismantlement, Suit.Spade, 3)),
            "Qixi must accept black physical cards, reject red cards and avoid duplicating native Dismantlement actions.");

        GameEngine? current = null;
        GameEngine? legacy = null;
        CardSnapshot? blackEquipment = null;
        CardSnapshot? redCard = null;
        LegalAction? handConversion = null;
        for (var seed = 1; seed <= 16_384 && current is null; seed++)
        {
            var candidate = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:gan-ning",
                GameCheckpoint.CurrentRulesVersion);
            if (candidate is null)
            {
                continue;
            }

            var snapshot = candidate.CreateSnapshot(0, revealAll: true);
            var self = snapshot.Players.Single(player => player.Seat == 0);
            var candidateEquipment = self.Hand.FirstOrDefault(card =>
                EquipmentCatalog.IsEquipment(card.Kind) &&
                card.Suit is Suit.Spade or Suit.Club);
            var candidateRed = self.Hand.FirstOrDefault(card =>
                card.Kind != CardKind.Dismantlement &&
                card.Suit is Suit.Heart or Suit.Diamond);
            var conversion = candidateEquipment is null
                ? null
                : candidate.GetHumanLegalActions().FirstOrDefault(action =>
                    action.Kind == LegalActionKind.Dismantlement &&
                    action.CardId == candidateEquipment.Id &&
                    action.PlayedCardKind == CardKind.Dismantlement &&
                    action.TargetCardId is null);
            var hasNullificationResponder = snapshot.Players
                .Where(player => player.Seat != 0)
                .Any(player => player.Hand.Any(card => card.Kind == CardKind.Nullification));
            if (candidateEquipment is null || candidateRed is null || conversion is null || !hasNullificationResponder)
            {
                continue;
            }

            var legacyCandidate = StartClassicGeneralAtPlay(registry, seed, "classic:gan-ning", rulesVersion: 27);
            if (legacyCandidate is null)
            {
                continue;
            }

            current = candidate;
            legacy = legacyCandidate;
            blackEquipment = candidateEquipment;
            redCard = candidateRed;
            handConversion = conversion;
        }

        if (current is null || legacy is null || blackEquipment is null || redCard is null || handConversion is null)
        {
            throw new InvalidOperationException("No deterministic Gan Ning fixture exposed black equipment and a Nullification responder.");
        }

        var selected = current.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(selected.GeneralId == "classic:gan-ning" &&
                selected.MaxHp == 5 &&
                selected.Skills!.Select(skill => skill.Kind).SequenceEqual([SkillKind.Qixi]),
            "Classic Gan Ning must combine base 4 HP, the Lord bonus and formal Qixi.");
        Require(current.GetHumanLegalActions().Any(action =>
                    action.Kind == LegalActionKind.Dismantlement &&
                    action.CardId == blackEquipment.Id &&
                    action.PlayedCardKind == CardKind.Dismantlement) &&
                !current.GetHumanLegalActions().Any(action =>
                    action.Kind == LegalActionKind.Dismantlement &&
                    action.CardId == redCard.Id &&
                    action.PlayedCardKind == CardKind.Dismantlement),
            "Formal Qixi must publish black hand-card conversions without converting red hand cards.");
        Require(!legacy.GetHumanLegalActions().Any(action =>
                action.CardId == blackEquipment.Id &&
                action.PlayedCardKind == CardKind.Dismantlement),
            "Rules v27 must not gain Qixi conversion actions from current content.");

        var prompt = current.PendingDecision ??
            throw new InvalidOperationException("Gan Ning fixture lost its play prompt.");
        var stateBeforeInvalid = SnapshotJson.Serialize(current.CreateSnapshot(0, revealAll: true));
        var invalid = current.Submit(new PlayCardCommand(
            0,
            blackEquipment.Id,
            handConversion.TargetSeats,
            current.Revision,
            prompt.PromptId,
            CardKind.Snatch,
            handConversion.TargetCardId));
        Require(!invalid.Accepted &&
                SnapshotJson.Serialize(current.CreateSnapshot(0, revealAll: true)) == stateBeforeInvalid,
            "A mismatched Qixi effective kind must reject atomically.");

        Equip(current, blackEquipment.Id);
        Equip(legacy, blackEquipment.Id);
        var equippedConversion = current.GetHumanLegalActions().FirstOrDefault(action =>
            action.Kind == LegalActionKind.Dismantlement &&
            action.CardId == blackEquipment.Id &&
            action.PlayedCardKind == CardKind.Dismantlement &&
            action.TargetCardId is null);
        Require(equippedConversion is not null &&
                !legacy.GetHumanLegalActions().Any(action =>
                    action.CardId == blackEquipment.Id &&
                    action.PlayedCardKind == CardKind.Dismantlement),
            "Formal Qixi must convert a black card from the equipment zone while rules v27 remains unchanged.");
        var selectedEquippedConversion = equippedConversion ??
            throw new InvalidOperationException("The equipped Qixi action disappeared before submission.");

        var qixiPrompt = current.PendingDecision ??
            throw new InvalidOperationException("Equipped Qixi fixture lost its play prompt.");
        var used = current.Submit(new PlayCardCommand(
            0,
            blackEquipment.Id,
            selectedEquippedConversion.TargetSeats,
            current.Revision,
            qixiPrompt.PromptId,
            CardKind.Dismantlement,
            selectedEquippedConversion.TargetCardId));
        Require(used.Accepted, used.Error?.Message ?? "Equipped Qixi conversion was rejected.");

        var nullificationFrame = current.ResolutionStack.OfType<NullificationWindowFrame>().SingleOrDefault();
        Require(nullificationFrame is not null &&
                nullificationFrame.EffectCardId == blackEquipment.Id &&
                nullificationFrame.EffectCardKind == CardKind.Dismantlement &&
                current.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>().Any(item =>
                    item.CardId == blackEquipment.Id && item.CardKind == CardKind.Dismantlement) &&
                current.Events.Select(item => item.Payload).OfType<NullificationRequestedEvent>().Any(item =>
                    item.EffectCardId == blackEquipment.Id && item.EffectCardKind == CardKind.Dismantlement),
            "Qixi must expose Dismantlement as the effective kind in the card-use and Nullification contracts.");
        Require(current.CardMovements.Any(movement =>
                movement.CardId == blackEquipment.Id &&
                movement.From == CardLocation.Equipment(0) &&
                movement.To == CardLocation.Processing &&
                movement.Reason == CardMoveReasons.Use),
            "Qixi must preserve the exact equipment source zone for its physical card cost.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(current.CreateCheckpoint()));
        var pausedRestored = GameReplay.Restore(pausedCheckpoint, registry);
        Require(SnapshotJson.Serialize(pausedRestored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(current.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(pausedRestored).SequenceEqual(EventSignatures(current)),
            "A paused Qixi Nullification window must restore with identical effective-card state.");

        for (var step = 0; step < 128; step++)
        {
            if (current.PendingDecision is { Kind: DecisionKind.PlayCard } && current.ResolutionStack.Count == 0)
            {
                break;
            }

            CommandResult next;
            if (current.PendingDecision is { Kind: DecisionKind.Nullification } nullification &&
                nullification.PlayerSeat == 0)
            {
                var pass = nullification.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "pass");
                next = current.Submit(new AnswerPromptCommand(0, nullification.PromptId, pass.Id, current.Revision));
            }
            else if (current.PendingDecision is { Kind: DecisionKind.SelectTargetCard } selection &&
                     selection.PlayerSeat == 0)
            {
                next = current.Submit(new AnswerPromptCommand(
                    0,
                    selection.PromptId,
                    selection.Choices[0].Id,
                    current.Revision));
            }
            else
            {
                next = current.Submit(new AdvanceOneStepCommand(current.Revision));
            }

            Require(next.Accepted, next.Error?.Message ?? "Qixi resolution did not advance.");
        }

        Require(current.PendingDecision?.Kind == DecisionKind.PlayCard &&
                current.CardMovements.Any(movement =>
                    movement.CardId == blackEquipment.Id &&
                    movement.From == CardLocation.Processing &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.UseFinished) &&
                current.Events.Select(item => item.Payload).OfType<CardUseFinishedEvent>().Any(item =>
                    item.CardId == blackEquipment.Id && item.CardKind == CardKind.Dismantlement),
            "Qixi must finish by discarding the physical equipment while publishing Dismantlement as the effective kind.");

        var restored = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(current.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(current.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(current)),
            "The completed equipped Qixi command must restore with identical state and events.");
    }

    public static void FormalKejiFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = SelectGeneral(registry, "classic:lu-meng", GameCheckpoint.CurrentRulesVersion);
        var selected = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(selected.GeneralId == "classic:lu-meng" &&
                selected.MaxHp == 5 &&
                selected.Hp == 5 &&
                selected.Skills!.Select(skill => skill.Kind).SequenceEqual([SkillKind.Keji]),
            "Classic Lu Meng must combine base 4 HP, the Lord bonus and formal Keji.");

        var reachedPlay = game.Submit(new AdvanceCommand(game.Revision));
        Require(reachedPlay.Accepted && game.PendingDecision?.Kind == DecisionKind.PlayCard,
            reachedPlay.Error?.Message ?? "Classic Lu Meng did not reach the play phase.");
        var handBeforeDiscard = game.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0);
        Require(handBeforeDiscard.HandCount > handBeforeDiscard.Hp,
            "The Keji fixture must have at least one excess hand card to prove the skipped discard.");

        var ended = game.Submit(new EndPlayPhaseCommand(
            0,
            game.Revision,
            game.PendingDecision!.PromptId));
        Require(ended.Accepted &&
                game.State.Phase == TurnPhase.Discard &&
                game.PendingDecision is
                {
                    Kind: DecisionKind.Keji,
                    PlayerSeat: 0,
                    Choices.Count: 2
                } kejiPrompt &&
                kejiPrompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "keji-use") &&
                kejiPrompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "keji-skip"),
            ended.Error?.Message ?? "A Slash-free Lu Meng play phase must publish both Keji choices.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var skippedBranch = GameReplay.Restore(pausedCheckpoint, registry);
        Require(skippedBranch.PendingDecision?.Kind == DecisionKind.Keji &&
                SnapshotJson.Serialize(skippedBranch.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "A paused Keji choice must restore exactly from its command checkpoint.");

        var beforeForged = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var forged = game.Submit(new AnswerPromptCommand(
            0,
            game.PendingDecision!.PromptId,
            new ChoiceId("keji.forged"),
            game.Revision));
        Require(!forged.Accepted &&
                forged.Error?.Code == CommandErrorCode.InvalidChoice &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == beforeForged,
            "A forged Keji choice must be rejected atomically.");

        var usePrompt = game.PendingDecision!;
        var used = game.Submit(new AnswerPromptCommand(
            0,
            usePrompt.PromptId,
            usePrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "keji-use").Id,
            game.Revision));
        var handAfterUse = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(used.Accepted &&
                game.PendingDecision is null &&
                game.State.Phase == TurnPhase.NotStarted &&
                handAfterUse.HandCount == handBeforeDiscard.HandCount &&
                game.Events.Select(item => item.Payload).OfType<PhaseSkillResolvedEvent>().Any(resolved =>
                    resolved.SourceSeat == 0 &&
                    resolved.Skill == SkillKind.Keji &&
                    resolved.Phase == TurnPhase.Discard &&
                    resolved.Used),
            used.Error?.Message ?? "Using Keji must skip discard, retain the full hand and end the turn.");
        var usedReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(usedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(usedReplay).SequenceEqual(EventSignatures(game)),
            "The completed Keji branch must replay exactly.");

        var skipPrompt = skippedBranch.PendingDecision!;
        var skipped = skippedBranch.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "keji-skip").Id,
            skippedBranch.Revision));
        Require(skipped.Accepted &&
                skippedBranch.State.Phase == TurnPhase.Discard &&
                skippedBranch.PendingDecision is null,
            skipped.Error?.Message ?? "Skipping Keji must continue the ordinary discard phase.");
        var discarded = skippedBranch.Submit(new AdvanceOneStepCommand(skippedBranch.Revision));
        var handAfterSkip = skippedBranch.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0);
        Require(discarded.Accepted &&
                handAfterSkip.HandCount == handAfterSkip.Hp &&
                skippedBranch.Events.Select(item => item.Payload).OfType<PhaseSkillResolvedEvent>().Any(resolved =>
                    resolved.Skill == SkillKind.Keji && !resolved.Used),
            discarded.Error?.Message ?? "Skipping Keji must retain the ordinary hand-limit discard.");

        var legacy = SelectGeneral(registry, "classic:lu-meng", rulesVersion: 28);
        Require(legacy.Submit(new AdvanceCommand(legacy.Revision)).Accepted &&
                legacy.PendingDecision?.Kind == DecisionKind.PlayCard,
            "The Keji legacy fixture did not reach play.");
        var legacyEnd = legacy.Submit(new EndPlayPhaseCommand(
            0,
            legacy.Revision,
            legacy.PendingDecision!.PromptId));
        Require(legacyEnd.Accepted &&
                legacy.State.Phase == TurnPhase.Discard &&
                legacy.PendingDecision?.Kind != DecisionKind.Keji &&
                legacy.Events.Select(item => item.Payload).OfType<PhaseSkillResolvedEvent>().Count() == 0,
            "Rules v28 must keep the historical discard path without a Keji choice.");

        var slashGame = FindLuMengSlashFixture(registry);
        var slashAction = slashGame.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash);
        var slashPlayed = slashGame.Submit(new PlayCardCommand(
            0,
            slashAction.CardId!.Value,
            slashAction.TargetSeats,
            slashGame.Revision,
            slashGame.PendingDecision!.PromptId,
            slashAction.PlayedCardKind));
        Require(slashPlayed.Accepted, slashPlayed.Error?.Message ?? "Lu Meng's direct Slash was rejected.");
        var slashResolved = slashGame.Submit(new AdvanceCommand(slashGame.Revision));
        Require(slashResolved.Accepted && slashGame.PendingDecision?.Kind == DecisionKind.PlayCard,
            slashResolved.Error?.Message ?? "Lu Meng's direct Slash did not return to play.");
        var afterSlash = slashGame.Submit(new EndPlayPhaseCommand(
            0,
            slashGame.Revision,
            slashGame.PendingDecision!.PromptId));
        Require(afterSlash.Accepted &&
                slashGame.State.Phase == TurnPhase.Discard &&
                slashGame.PendingDecision?.Kind != DecisionKind.Keji,
            "Using a Slash during the play phase must suppress Keji.");

    }

    public static void FormalTuxiFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = SelectGeneral(registry, "classic:zhang-liao", GameCheckpoint.CurrentRulesVersion);
        var selected = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(selected.GeneralId == "classic:zhang-liao" &&
                selected.MaxHp == 5 &&
                selected.Hp == 5 &&
                selected.Skills!.Select(skill => skill.Kind).SequenceEqual([SkillKind.Tuxi]),
            "Classic Zhang Liao must combine base 4 HP, the Lord bonus and formal Tuxi.");

        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        var eligibleTargetCount = game.CreateSnapshot(0, revealAll: true).Players.Count(player =>
            player.Seat != 0 && player.IsAlive && player.HandCount > 0);
        var expectedChoiceCount = eligibleTargetCount + eligibleTargetCount * (eligibleTargetCount - 1) / 2 + 1;
        Require(advanced.Accepted &&
                game.State.Phase == TurnPhase.Draw &&
                game.PendingDecision is
                {
                    Kind: DecisionKind.Tuxi,
                    PlayerSeat: 0,
                    IsPrivate: true
                } prompt &&
                prompt.ValidTargetSeats.Count == eligibleTargetCount &&
                prompt.Choices.Count == expectedChoiceCount &&
                prompt.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "tuxi-use" &&
                    choice.Targets.Count == 2) == eligibleTargetCount * (eligibleTargetCount - 1) / 2 &&
                prompt.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "tuxi-skip") == 1,
            advanced.Error?.Message ?? "Classic Zhang Liao must publish every legal one- or two-target Tuxi choice.");
        Require(game.CreateSnapshot(1).PendingDecision is null,
            "Another seat must not receive Zhang Liao's private Tuxi choice surface.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var skippedBranch = GameReplay.Restore(pausedCheckpoint, registry);
        Require(skippedBranch.PendingDecision?.Kind == DecisionKind.Tuxi &&
                SnapshotJson.Serialize(skippedBranch.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "A paused Tuxi target choice must restore exactly from its command checkpoint.");

        var beforeForged = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var forged = game.Submit(new AnswerPromptCommand(
            0,
            game.PendingDecision!.PromptId,
            new ChoiceId("tuxi.forged"),
            game.Revision));
        Require(!forged.Accepted &&
                forged.Error?.Code == CommandErrorCode.InvalidChoice &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == beforeForged,
            "A forged Tuxi choice must be rejected atomically.");

        var usePrompt = game.PendingDecision!;
        var useChoice = usePrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "tuxi-use" &&
            choice.Targets.Count == 2);
        var beforeUse = game.CreateSnapshot(0, revealAll: true);
        var sourceHandBefore = beforeUse.Players.Single(player => player.Seat == 0).Hand
            .Select(card => card.Id)
            .ToHashSet();
        var targetHandsBefore = useChoice.Targets.ToDictionary(
            seat => seat,
            seat => beforeUse.Players.Single(player => player.Seat == seat).Hand
                .Select(card => card.Id)
                .ToHashSet());
        var used = game.Submit(new AnswerPromptCommand(
            0,
            usePrompt.PromptId,
            useChoice.Id,
            game.Revision));
        var afterUse = game.CreateSnapshot(0, revealAll: true);
        var sourceAfter = afterUse.Players.Single(player => player.Seat == 0);
        var gainedIds = sourceAfter.Hand.Select(card => card.Id).Where(id => !sourceHandBefore.Contains(id)).ToArray();
        Require(used.Accepted &&
                game.PendingDecision is null &&
                game.State.Phase == TurnPhase.Play &&
                gainedIds.Length == 2 &&
                useChoice.Targets.All(seat =>
                    afterUse.Players.Single(player => player.Seat == seat).HandCount ==
                    targetHandsBefore[seat].Count - 1) &&
                useChoice.Targets.All(seat =>
                    targetHandsBefore[seat].Except(
                        afterUse.Players.Single(player => player.Seat == seat).Hand.Select(card => card.Id)).Count() == 1) &&
                game.Events.Select(item => item.Payload).OfType<HandCardsGainedBySkillEvent>().Any(resolved =>
                    resolved.SourceSeat == 0 &&
                    resolved.Skill == SkillKind.Tuxi &&
                    resolved.Used &&
                    resolved.CardCount == 2 &&
                    resolved.TargetSeats.SequenceEqual(useChoice.Targets)) &&
                gainedIds.All(cardId => game.CardMovements.Count(movement =>
                    movement.CardId == cardId &&
                    movement.Reason == CardMoveReasons.TuxiGain) == 2),
            used.Error?.Message ?? "Using Tuxi must replace drawing with one hidden hand card from each selected target.");
        var usedReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(usedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(usedReplay).SequenceEqual(EventSignatures(game)),
            "The completed Tuxi branch must replay exactly.");

        var skipPrompt = skippedBranch.PendingDecision!;
        var sourceBeforeSkip = skippedBranch.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0).HandCount;
        var skipped = skippedBranch.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "tuxi-skip").Id,
            skippedBranch.Revision));
        Require(skipped.Accepted &&
                skippedBranch.PendingDecision is null &&
                skippedBranch.State.Phase == TurnPhase.Play &&
                skippedBranch.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount ==
                sourceBeforeSkip + 2 &&
                skippedBranch.Events.Select(item => item.Payload).OfType<HandCardsGainedBySkillEvent>().Any(resolved =>
                    resolved.Skill == SkillKind.Tuxi && !resolved.Used && resolved.CardCount == 0),
            skipped.Error?.Message ?? "Skipping Tuxi must preserve the ordinary two-card draw.");

        var legacy = SelectGeneral(registry, "classic:zhang-liao", rulesVersion: 29);
        var legacyAdvance = legacy.Submit(new AdvanceCommand(legacy.Revision));
        Require(legacyAdvance.Accepted &&
                legacy.PendingDecision?.Kind == DecisionKind.PlayCard &&
                legacy.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount ==
                6 &&
                legacy.Events.Select(item => item.Payload).OfType<HandCardsGainedBySkillEvent>().Count() == 0,
            legacyAdvance.Error?.Message ?? "Rules v29 must retain the historical ordinary draw without Tuxi.");
    }

    public static void FormalLuoyiFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var game = SelectGeneral(registry, "classic:xu-chu", GameCheckpoint.CurrentRulesVersion);
        var selected = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(selected.GeneralId == "classic:xu-chu" &&
                selected.MaxHp == 5 &&
                selected.Hp == 5 &&
                selected.Skills!.Select(skill => skill.Kind).SequenceEqual([SkillKind.Luoyi]),
            "Classic Xu Chu must combine base 4 HP, the Lord bonus and formal Luoyi.");

        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted &&
                game.State.Phase == TurnPhase.Draw &&
                game.PendingDecision is
                {
                    Kind: DecisionKind.Luoyi,
                    PlayerSeat: 0,
                    IsPrivate: true,
                    Choices.Count: 2
                } prompt &&
                prompt.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "luoyi-use") == 1 &&
                prompt.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "luoyi-skip") == 1 &&
                game.CreateSnapshot(1).PendingDecision is null,
            advanced.Error?.Message ?? "Classic Xu Chu must publish a private use-or-skip Luoyi choice.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var skippedBranch = GameReplay.Restore(pausedCheckpoint, registry);
        Require(skippedBranch.PendingDecision?.Kind == DecisionKind.Luoyi &&
                SnapshotJson.Serialize(skippedBranch.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "A paused Luoyi choice must restore exactly from its command checkpoint.");

        var beforeForged = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var forged = game.Submit(new AnswerPromptCommand(
            0,
            game.PendingDecision!.PromptId,
            new ChoiceId("luoyi.forged"),
            game.Revision));
        Require(!forged.Accepted &&
                forged.Error?.Code == CommandErrorCode.InvalidChoice &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == beforeForged,
            "A forged Luoyi choice must be rejected atomically.");

        var sourceBeforeUse = game.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0).HandCount;
        var usePrompt = game.PendingDecision!;
        var used = game.Submit(new AnswerPromptCommand(
            0,
            usePrompt.PromptId,
            usePrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "luoyi-use").Id,
            game.Revision));
        Require(used.Accepted &&
                game.PendingDecision is null &&
                game.State.Phase == TurnPhase.Play &&
                game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount ==
                sourceBeforeUse + 1 &&
                game.Events.Select(item => item.Payload).OfType<DrawSkillResolvedEvent>().Any(resolved =>
                    resolved.SourceSeat == 0 &&
                    resolved.Skill == SkillKind.Luoyi &&
                    resolved.Used &&
                    resolved.DrawCount == 1),
            used.Error?.Message ?? "Using Luoyi must draw one fewer card and enter the play phase.");

        var sourceBeforeSkip = skippedBranch.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0).HandCount;
        var skipPrompt = skippedBranch.PendingDecision!;
        var skipped = skippedBranch.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "luoyi-skip").Id,
            skippedBranch.Revision));
        Require(skipped.Accepted &&
                skippedBranch.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount ==
                sourceBeforeSkip + 2 &&
                skippedBranch.Events.Select(item => item.Payload).OfType<DrawSkillResolvedEvent>().Any(resolved =>
                    resolved.Skill == SkillKind.Luoyi && !resolved.Used && resolved.DrawCount == 2),
            skipped.Error?.Message ?? "Skipping Luoyi must preserve the ordinary two-card draw.");

        var (slashGame, slashAction, slashTargetHp) = FindXuChuDirectAttackFixture(
            registry,
            LegalActionKind.Slash,
            target => target.Hand.All(card => card.Kind != CardKind.Dodge) && target.Equipment.Count == 0);
        var slashTargetSeat = slashAction.TargetSeat!.Value;
        var slashPlayed = slashGame.Submit(new PlayCardCommand(
            0,
            slashAction.CardId!.Value,
            slashAction.TargetSeats,
            slashGame.Revision,
            slashGame.PendingDecision!.PromptId,
            slashAction.PlayedCardKind));
        var slashDamage = slashGame.Events.Select(item => item.Payload)
            .OfType<DamageRequestedEvent>()
            .LastOrDefault(item => item.SourceSeat == 0 && item.TargetSeat == slashTargetSeat);
        Require(slashPlayed.Accepted &&
                slashDamage is { Amount: 2 } &&
                slashGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == slashTargetSeat).Hp ==
                slashTargetHp - 2 &&
                slashGame.Events.Select(item => item.Payload).OfType<DamageModifiedBySkillEvent>().Any(modified =>
                    modified.ResolutionId == slashDamage.ResolutionId - 1 &&
                    modified.Skill == SkillKind.Luoyi &&
                    modified.BaseAmount == 1 &&
                    modified.ModifiedAmount == 2),
            slashPlayed.Error?.Message ?? "Luoyi must add one damage to a Slash used by Xu Chu this turn.");

        var (duelGame, duelAction, duelTargetHp) = FindXuChuDirectAttackFixture(
            registry,
            LegalActionKind.Duel,
            target =>
                target.Hand.All(card => card.Kind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)) &&
                target.Skills?.All(skill => skill.Kind is not (SkillKind.Wusheng or SkillKind.Longdan or SkillKind.Jijiang)) != false,
            requireNoNullification: true);
        var duelTargetSeat = duelAction.TargetSeat!.Value;
        var duelPlayed = duelGame.Submit(new PlayCardCommand(
            0,
            duelAction.CardId!.Value,
            duelAction.TargetSeats,
            duelGame.Revision,
            duelGame.PendingDecision!.PromptId,
            duelAction.PlayedCardKind));
        var duelDamage = duelGame.Events.Select(item => item.Payload)
            .OfType<DamageRequestedEvent>()
            .LastOrDefault(item => item.SourceCard == CardKind.Duel && item.TargetSeat == duelTargetSeat);
        Require(duelPlayed.Accepted &&
                duelDamage is { SourceSeat: 0, Amount: 2 } &&
                duelGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == duelTargetSeat).Hp ==
                duelTargetHp - 2,
            duelPlayed.Error?.Message ?? "Luoyi must add one damage when Xu Chu's Duel target fails first.");

        var (reverseGame, reverseTargetSeat, reverseResolutionId, xuChuHpBefore) =
            FindXuChuReverseDuelFixture(registry);
        var reversePrompt = reverseGame.PendingDecision!;
        var declined = reverseGame.Submit(new AnswerPromptCommand(
            0,
            reversePrompt.PromptId,
            reversePrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("response") == "take-damage").Id,
            reverseGame.Revision));
        var reverseDamage = reverseGame.Events.Select(item => item.Payload)
            .OfType<DamageRequestedEvent>()
            .Last(item => item.SourceCard == CardKind.Duel && item.TargetSeat == 0);
        Require(declined.Accepted &&
                reverseDamage.SourceSeat == reverseTargetSeat &&
                reverseDamage.Amount == 1 &&
                reverseGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hp ==
                xuChuHpBefore - 1 &&
                reverseGame.Events.Select(item => item.Payload).OfType<DamageModifiedBySkillEvent>()
                    .All(modified => modified.ResolutionId != reverseResolutionId),
            declined.Error?.Message ??
            "A Duel opponent must deal unmodified damage when Xu Chu used the Duel but then failed to respond.");

        var legacy = SelectGeneral(registry, "classic:xu-chu", rulesVersion: 30);
        var legacyAdvance = legacy.Submit(new AdvanceCommand(legacy.Revision));
        Require(legacyAdvance.Accepted &&
                legacy.PendingDecision?.Kind == DecisionKind.PlayCard &&
                legacy.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount == 6 &&
                legacy.Events.Select(item => item.Payload).OfType<DrawSkillResolvedEvent>()
                    .All(resolved => resolved.Skill != SkillKind.Luoyi),
            legacyAdvance.Error?.Message ?? "Rules v30 must retain ordinary drawing without Luoyi.");
    }

    public static void FormalQiangxiFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var (fixture, action, targetSeat, weaponCardId) = FindDianWeiQiangxiFixture(
            registry,
            requireWeapon: true);
        var before = fixture.CreateSnapshot(0, revealAll: true);
        var sourceBefore = before.Players.Single(player => player.Seat == 0);
        var targetBefore = before.Players.Single(player => player.Seat == targetSeat);
        Require(action.MinCardCount == 0 && action.MaxCardCount == 1 &&
                action.MinTargetCount == 1 && action.MaxTargetCount == 1 &&
                action.SelectableCardIds.Contains(weaponCardId) &&
                action.SelectableTargetSeats.Contains(targetSeat) &&
                action.SelectableTargetSeats.All(seat =>
                    fixture.GetCombatDistance(0, seat) <= fixture.GetAttackRange(0)),
            "Qiangxi must publish an optional weapon cost and only in-range living targets.");

        var checkpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(fixture.CreateCheckpoint()));
        var outOfRange = before.Players.FirstOrDefault(player =>
            player.IsAlive &&
            player.Seat != 0 &&
            !action.SelectableTargetSeats.Contains(player.Seat));
        if (outOfRange is not null)
        {
            var stateBeforeForged = SnapshotJson.Serialize(fixture.CreateSnapshot(0, revealAll: true));
            var forged = fixture.Submit(new UseSkillCommand(
                0,
                SkillKind.Qiangxi,
                [],
                [outOfRange.Seat],
                fixture.Revision,
                fixture.PendingDecision!.PromptId));
            Require(!forged.Accepted &&
                    forged.Error?.Code == CommandErrorCode.InvalidTarget &&
                    SnapshotJson.Serialize(fixture.CreateSnapshot(0, revealAll: true)) == stateBeforeForged,
                "Qiangxi must reject an out-of-range target atomically.");
        }

        var nonWeapon = sourceBefore.Hand.FirstOrDefault(card =>
            !action.SelectableCardIds.Contains(card.Id));
        if (nonWeapon is not null)
        {
            var stateBeforeForged = SnapshotJson.Serialize(fixture.CreateSnapshot(0, revealAll: true));
            var forged = fixture.Submit(new UseSkillCommand(
                0,
                SkillKind.Qiangxi,
                [nonWeapon.Id],
                [targetSeat],
                fixture.Revision,
                fixture.PendingDecision!.PromptId));
            Require(!forged.Accepted &&
                    forged.Error?.Code == CommandErrorCode.InvalidCard &&
                    SnapshotJson.Serialize(fixture.CreateSnapshot(0, revealAll: true)) == stateBeforeForged,
                "Qiangxi must reject a non-weapon cost atomically.");
        }

        var hpBranch = GameReplay.Restore(checkpoint, registry);
        var hpUsed = hpBranch.Submit(new UseSkillCommand(
            0,
            SkillKind.Qiangxi,
            [],
            [targetSeat],
            hpBranch.Revision,
            hpBranch.PendingDecision!.PromptId));
        var hpAfter = hpBranch.CreateSnapshot(0, revealAll: true);
        Require(hpUsed.Accepted &&
                hpAfter.Players.Single(player => player.Seat == 0).Hp == sourceBefore.Hp - 1 &&
                hpAfter.Players.Single(player => player.Seat == targetSeat).Hp == targetBefore.Hp - 1 &&
                hpBranch.Events.Select(item => item.Payload).OfType<DamageRequestedEvent>().Any(damage =>
                    damage.SourceSeat == 0 &&
                    damage.TargetSeat == targetSeat &&
                    damage.Amount == 1 &&
                    damage.SourceCard is null) &&
                hpBranch.Events.Select(item => item.Payload).OfType<ActiveSkillResolvedEvent>().Any(resolved =>
                    resolved.SourceSeat == 0 &&
                    resolved.Skill == SkillKind.Qiangxi &&
                    resolved.Effect == ActiveSkillEffectKind.PayHpOrDiscardWeaponAndDamage) &&
                hpBranch.GetHumanLegalActions().All(candidate => candidate.Skill != SkillKind.Qiangxi),
            hpUsed.Error?.Message ??
            "The HP-cost Qiangxi branch must deal cardless skill damage and enforce once per play phase.");

        var replayedHp = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(hpBranch.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(replayedHp.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(hpBranch.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replayedHp).SequenceEqual(EventSignatures(hpBranch)),
            "The resolved HP-cost Qiangxi branch must replay exactly.");

        var (dyingBranch, _, dyingTargetSeat, _) = FindDianWeiQiangxiFixture(
            registry,
            requireWeapon: false,
            requirePeach: true);
        SetPlayerHp(dyingBranch, seat: 0, hp: 1);
        var dyingTargetHp = dyingBranch.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == dyingTargetSeat).Hp;
        var enteredDying = dyingBranch.Submit(new UseSkillCommand(
            0,
            SkillKind.Qiangxi,
            [],
            [dyingTargetSeat],
            dyingBranch.Revision,
            dyingBranch.PendingDecision!.PromptId));
        var dyingPrompt = dyingBranch.PendingDecision ??
            throw new InvalidOperationException("Lethal Qiangxi did not publish a dying prompt.");
        Require(enteredDying.Accepted &&
                dyingPrompt.Kind == DecisionKind.RescueDying &&
                dyingPrompt.PlayerSeat == 0 &&
                dyingBranch.CreateSnapshot(0, revealAll: true)
                    .Players.Single(player => player.Seat == dyingTargetSeat).Hp == dyingTargetHp &&
                dyingBranch.ResolutionStack.Count == 2 &&
                dyingBranch.ResolutionStack[0] is ActiveSkillFrame dyingSkill &&
                dyingSkill.Skill == SkillKind.Qiangxi &&
                dyingBranch.ResolutionStack[1] is DyingFrame dyingFrame &&
                dyingFrame.ParentFrameId == dyingSkill.Id,
            enteredDying.Error?.Message ??
            "Lethal Qiangxi must pause before target damage on the shared dying continuation.");

        var peachChoice = dyingPrompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("response") == "peach");
        var rescued = dyingBranch.Submit(new AnswerPromptCommand(
            0,
            dyingPrompt.PromptId,
            peachChoice.Id,
            dyingBranch.Revision));
        var rescuedAfter = dyingBranch.CreateSnapshot(0, revealAll: true);
        Require(rescued.Accepted &&
                rescuedAfter.Players.Single(player => player.Seat == 0).Hp == 1 &&
                rescuedAfter.Players.Single(player => player.Seat == dyingTargetSeat).Hp == dyingTargetHp - 1 &&
                dyingBranch.Events.Select(item => item.Payload).OfType<DyingResolvedEvent>().Any(resolved =>
                    resolved.Survived) &&
                dyingBranch.Events.Select(item => item.Payload).OfType<ActiveSkillResolvedEvent>().Any(resolved =>
                    resolved.SourceSeat == 0 && resolved.Skill == SkillKind.Qiangxi),
            rescued.Error?.Message ??
            "Rescued Qiangxi must resume and deal its pending cardless damage exactly once.");

        var weaponBranch = GameReplay.Restore(checkpoint, registry);
        Equip(weaponBranch, weaponCardId);
        var weaponAction = weaponBranch.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Qiangxi);
        var weaponTargetSeat = weaponAction.SelectableTargetSeats.Contains(targetSeat)
            ? targetSeat
            : weaponAction.SelectableTargetSeats.First();
        var weaponBefore = weaponBranch.CreateSnapshot(0, revealAll: true);
        var weaponUsed = weaponBranch.Submit(new UseSkillCommand(
            0,
            SkillKind.Qiangxi,
            [weaponCardId],
            [weaponTargetSeat],
            weaponBranch.Revision,
            weaponBranch.PendingDecision!.PromptId));
        var weaponAfter = weaponBranch.CreateSnapshot(0, revealAll: true);
        Require(weaponUsed.Accepted &&
                weaponAfter.Players.Single(player => player.Seat == 0).Hp ==
                weaponBefore.Players.Single(player => player.Seat == 0).Hp &&
                weaponAfter.Players.Single(player => player.Seat == weaponTargetSeat).Hp ==
                weaponBefore.Players.Single(player => player.Seat == weaponTargetSeat).Hp - 1 &&
                weaponBranch.CardMovements.Any(movement =>
                    movement.CardId == weaponCardId &&
                    movement.From == CardLocation.Equipment(0) &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.QiangxiDiscard) &&
                weaponBranch.CardMovements.Any(movement =>
                    movement.CardId == weaponCardId &&
                    movement.From == CardLocation.Processing &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.QiangxiDiscard) &&
                weaponBranch.Events.Select(item => item.Payload).OfType<SkillCardsDiscardedEvent>().Any(discarded =>
                    discarded.SourceSeat == 0 &&
                    discarded.Skill == SkillKind.Qiangxi &&
                    discarded.CardIds.SequenceEqual([weaponCardId])),
            weaponUsed.Error?.Message ??
            "The equipped-weapon Qiangxi branch must discard the exact weapon without losing HP.");

        var aiWeapon = new SimpleAiBrain(0, seed: 32)
            .ChooseActiveSkillCards(before, action);
        Require(aiWeapon.Count == 1 && action.SelectableCardIds.Contains(aiWeapon[0]),
            "Qiangxi AI must choose only one of its own published weapon candidates when available.");

        var (triggerGame, _, triggerTargetSeat, _) = FindDianWeiQiangxiFixture(
            registry,
            requireWeapon: false,
            targetSkill: SkillKind.Ganglie);
        var triggerAction = triggerGame.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Qiangxi);
        var triggered = triggerGame.Submit(new UseSkillCommand(
            0,
            SkillKind.Qiangxi,
            [],
            [triggerTargetSeat],
            triggerGame.Revision,
            triggerGame.PendingDecision!.PromptId));
        Require(triggered.Accepted &&
                triggerGame.Events.Select(item => item.Payload).OfType<DamageTriggerWindowOpenedEvent>().Any(opened =>
                    opened.SourceSeat == 0 &&
                    opened.TargetSeat == triggerTargetSeat &&
                    opened.CardId is null &&
                    opened.CardKind is null &&
                    opened.Candidates.Any(candidate => candidate.Skill == SkillKind.Ganglie)),
            triggered.Error?.Message ??
            "Cardless Qiangxi damage must still enter the shared after-damage trigger window.");

        var legacy = SelectGeneral(registry, "classic:dian-wei", rulesVersion: 31);
        var legacyAdvance = legacy.Submit(new AdvanceCommand(legacy.Revision));
        Require(legacyAdvance.Accepted &&
                legacy.GetHumanLegalActions().All(candidate => candidate.Skill != SkillKind.Qiangxi),
            legacyAdvance.Error?.Message ?? "Rules v31 must not expose formal Qiangxi actions.");
    }

    public static void FormalDuanliangFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var (game, physicalCardId, targetSeat, distanceTwoSeat) =
            FindXuHuangDuanliangFixture(registry);
        var before = game.CreateSnapshot(0, revealAll: true);
        var source = before.Players.Single(player => player.Seat == 0);
        var physicalCard = source.Equipment.Single(card => card.Id == physicalCardId);
        var convertedActions = game.GetHumanLegalActions()
            .Where(action =>
                action.Kind == LegalActionKind.SupplyShortage &&
                action.CardId == physicalCardId &&
                action.PlayedCardKind == CardKind.SupplyShortage)
            .ToArray();
        var targetAction = convertedActions.Single(action => action.TargetSeat == targetSeat);
        Require(physicalCard.Suit is Suit.Spade or Suit.Club &&
                EquipmentCatalog.IsEquipment(physicalCard.Kind) &&
                game.GetCombatDistance(0, distanceTwoSeat) == 2 &&
                convertedActions.Any(action => action.TargetSeat == distanceTwoSeat),
            "Duanliang must publish an equipped black card as Supply Shortage against distance-two targets.");

        var stateBeforeForged = SnapshotJson.Serialize(before);
        var forged = game.Submit(new PlayCardCommand(
            0,
            physicalCardId,
            targetAction.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            physicalCard.Kind));
        Require(!forged.Accepted &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == stateBeforeForged,
            "Duanliang must reject a forged physical effective kind atomically.");

        var aiActions = convertedActions
            .Where(action => action.TargetSeat == distanceTwoSeat)
            .ToArray();
        var aiChoice = new SimpleAiBrain(0, seed: 33).ChoosePlay(before, aiActions, thoughtSequence: 1);
        Require(aiChoice.Action.CardId == physicalCardId &&
                aiChoice.Action.PlayedCardKind == CardKind.SupplyShortage &&
                aiChoice.Thought.Candidates.All(candidate =>
                    candidate.Action.CardId == physicalCardId),
            "Duanliang AI must choose only from its published physical-card conversions.");

        var used = game.Submit(new PlayCardCommand(
            0,
            physicalCardId,
            targetAction.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            CardKind.SupplyShortage));
        var returnedToPlay = game.Submit(new AdvanceCommand(game.Revision));
        var placed = game.CreateSnapshot(0, revealAll: true);
        Require(used.Accepted &&
                returnedToPlay.Accepted &&
                game.PendingDecision?.Kind == DecisionKind.PlayCard &&
                placed.Players.Single(player => player.Seat == targetSeat).Judgment.Any(card =>
                    card.Id == physicalCardId && card.Kind == CardKind.SupplyShortage) &&
                game.CreateCardZoneDiagnostics().Any(card =>
                    card.CardId == physicalCardId &&
                    card.CardKind == physicalCard.Kind &&
                    card.Location == CardLocation.Judgment(targetSeat)) &&
                game.CardMovements.Any(movement =>
                    movement.CardId == physicalCardId &&
                    movement.From == CardLocation.Equipment(0) &&
                    movement.To == CardLocation.Processing) &&
                game.CardMovements.Any(movement =>
                    movement.CardId == physicalCardId &&
                    movement.From == CardLocation.Processing &&
                    movement.To == CardLocation.Judgment(targetSeat)) &&
                game.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>().Any(declared =>
                    declared.CardId == physicalCardId &&
                    declared.CardKind == CardKind.SupplyShortage) &&
                game.Events.Select(item => item.Payload).OfType<DelayedCardPlacedEvent>().Any(delayed =>
                    delayed.CardId == physicalCardId &&
                    delayed.CardKind == CardKind.SupplyShortage &&
                    delayed.TargetSeat == targetSeat),
            used.Error?.Message ?? returnedToPlay.Error?.Message ??
            "Duanliang must retain the physical equipment identity while publishing persistent Supply Shortage semantics.");

        var placedReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(placedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(placed) &&
                EventSignatures(placedReplay).SequenceEqual(EventSignatures(game)),
            "A placed Duanliang conversion must restore with the same effective judgment-card identity.");

        var ended = game.Submit(new EndPlayPhaseCommand(
            0,
            game.Revision,
            game.PendingDecision!.PromptId));
        var resolved = ended.Accepted
            ? AdvanceUntilDelayedCardResolves(game, physicalCardId)
            : null;
        Require(ended.Accepted &&
                resolved is { CardKind: CardKind.SupplyShortage, SkippedDrawPhase: true } &&
                game.CreateCardZoneDiagnostics().Any(card =>
                    card.CardId == physicalCardId &&
                    card.CardKind == physicalCard.Kind &&
                    card.Location == CardLocation.DiscardPile) &&
                game.CreateSnapshot(0, revealAll: true).Players
                    .Single(player => player.Seat == targetSeat).Judgment
                    .All(card => card.Id != physicalCardId),
            ended.Error?.Message ??
            "The converted Supply Shortage must resolve as a draw-skip judgment and discard its original physical card.");

        var resolvedReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(resolvedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(resolvedReplay).SequenceEqual(EventSignatures(game)),
            "A resolved Duanliang delayed card must replay exactly.");

        var legacy = SelectGeneral(registry, "classic:xu-huang", rulesVersion: 32);
        var legacyAdvance = legacy.Submit(new AdvanceCommand(legacy.Revision));
        Require(legacyAdvance.Accepted &&
                legacy.GetHumanLegalActions().All(action =>
                    action.PlayedCardKind != CardKind.SupplyShortage ||
                    action.CardId is null ||
                    legacy.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0)
                        .Hand.Single(card => card.Id == action.CardId).Kind == CardKind.SupplyShortage),
            legacyAdvance.Error?.Message ??
            "Rules v32 must not expose Duanliang card conversions.");
    }

    public static void FormalLuoshenAndQingguoFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 35, 0));
        var game = FindZhenJiFirstBlackLuoshenFixture(registry);
        var owner = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(owner.GeneralId == "classic:zhen-ji" &&
                owner.MaxHp == 4 &&
                owner.Hp == 4 &&
                owner.Skills!.Select(skill => skill.Kind).SequenceEqual([SkillKind.Luoshen, SkillKind.Qingguo]) &&
                game.PendingDecision is
                {
                    Kind: DecisionKind.Luoshen,
                    PlayerSeat: 0,
                    IsPrivate: true,
                    Choices.Count: 2
                } repeatPrompt &&
                repeatPrompt.Prompt.Contains("再次", StringComparison.Ordinal) &&
                game.CreateSnapshot(1).PendingDecision is null,
            "Classic Zhen Ji must publish a private repeated Luoshen choice after claiming a black judgment.");

        var blackJudgment = game.Events.Select(item => item.Payload)
            .OfType<JudgmentResolvedEvent>()
            .Last(item => item.Reason == JudgmentReasons.Luoshen);
        Require(blackJudgment is { CardId: not null, Suit: Suit.Spade or Suit.Club, Succeeded: true } &&
                game.CardMovements.Any(movement =>
                    movement.CardId == blackJudgment.CardId &&
                    movement.From == CardLocation.Judgment(0) &&
                    movement.To == CardLocation.Hand(0) &&
                    movement.Reason == CardMoveReasons.LuoshenClaim) &&
                game.Events.Select(item => item.Payload).OfType<JudgmentCardClaimedEvent>().Any(claimed =>
                    claimed.JudgmentFrameId == blackJudgment.ResolutionId &&
                    claimed.Skill == SkillKind.Luoshen &&
                    claimed.Used &&
                    claimed.CardId == blackJudgment.CardId),
            "A black Luoshen judgment must enter Zhen Ji's hand through an auditable claim movement and event.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restored = GameReplay.Restore(pausedCheckpoint, registry);
        Require(restored.PendingDecision?.Kind == DecisionKind.Luoshen &&
                SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(game)),
            "A repeated Luoshen prompt must restore with the same private choice and claimed black card.");

        ContinueLuoshenUntilPlay(game);
        var finalJudgment = game.Events.Select(item => item.Payload)
            .OfType<JudgmentResolvedEvent>()
            .Last(item => item.Reason == JudgmentReasons.Luoshen);
        Require(finalJudgment is { CardId: not null, Suit: Suit.Heart or Suit.Diamond, Succeeded: false } &&
                game.State.Phase == TurnPhase.Play &&
                game.PendingDecision?.Kind == DecisionKind.PlayCard &&
                game.CardMovements.Any(movement =>
                    movement.CardId == finalJudgment.CardId &&
                    movement.From == CardLocation.Judgment(0) &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.JudgmentFinish),
            "A red Luoshen judgment must stop the chain, discard the red card and continue to the play phase.");
        var completedReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(completedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(completedReplay).SequenceEqual(EventSignatures(game)),
            "The completed black-to-red Luoshen chain must replay exactly.");

        var skipped = GameReplay.Restore(pausedCheckpoint, registry);
        var skippedPrompt = skipped.PendingDecision!;
        var skippedResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skippedPrompt.PromptId,
            skippedPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "luoshen-skip").Id,
            skipped.Revision));
        Require(skippedResult.Accepted &&
                skipped.State.Phase == TurnPhase.Play &&
                skipped.Events.Select(item => item.Payload).OfType<LuoshenChoiceResolvedEvent>().Any(resolved =>
                    resolved.SourceSeat == 0 && resolved.IsRepeat && !resolved.Used),
            skippedResult.Error?.Message ?? "Stopping after a black Luoshen result must continue the turn without another judgment.");

        foreach (var incoming in new[] { CardKind.Slash, CardKind.ArrowBarrage })
        {
            var responseGame = WushengResponseScenario.FindQingguoDodge(
                incoming,
                new Version(1, 35, 0));
            var responsePrompt = responseGame.PendingDecision!;
            var hand = responseGame.CreateSnapshot(0).Players[0].Hand;
            var choice = responsePrompt.Choices.First(candidate =>
                candidate.Cards.Count == 1 &&
                hand.Single(card => card.Id == candidate.Cards[0]).Kind != CardKind.Dodge);
            var physical = hand.Single(card => card.Id == choice.Cards[0]);
            Require(physical.Suit is Suit.Spade or Suit.Club &&
                    choice.Parameters.GetValueOrDefault("response-card-kind") == nameof(CardKind.Dodge) &&
                    choice.Description.Contains("当作【闪】", StringComparison.Ordinal) &&
                    responseGame.CreateSnapshot(1).PendingDecision is null,
                $"Qingguo must publish one private black-card Dodge conversion against {incoming}.");
            var answered = responseGame.Submit(new AnswerPromptCommand(
                0,
                responsePrompt.PromptId,
                choice.Id,
                responseGame.Revision));
            Require(answered.Accepted &&
                    responseGame.Events.Select(item => item.Payload).OfType<CardRespondedEvent>().Any(responded =>
                        responded.CardId == physical.Id &&
                        responded.ResponderSeat == 0 &&
                        responded.EffectiveCardKind == CardKind.Dodge) &&
                    responseGame.CardMovements.Any(movement =>
                        movement.CardId == physical.Id &&
                        movement.CardKind == physical.Kind &&
                        movement.From == CardLocation.Hand(0) &&
                        movement.To == CardLocation.Processing &&
                        movement.Reason == CardMoveReasons.Respond) &&
                    responseGame.CardMovements.Any(movement =>
                        movement.CardId == physical.Id &&
                        movement.From == CardLocation.Processing &&
                        movement.To == CardLocation.DiscardPile &&
                        movement.Reason == CardMoveReasons.ResponseFinished),
                answered.Error?.Message ?? $"Qingguo must keep the physical black card while responding as Dodge to {incoming}.");
            var responseReplay = GameReplay.Restore(responseGame.CreateCheckpoint(), registry);
            Require(SnapshotJson.Serialize(responseReplay.CreateSnapshot(0, revealAll: true)) ==
                    SnapshotJson.Serialize(responseGame.CreateSnapshot(0, revealAll: true)) &&
                    EventSignatures(responseReplay).SequenceEqual(EventSignatures(responseGame)),
                $"The Qingguo {incoming} response must replay exactly.");
        }

        var legacy = SelectGeneral(registry, "classic:zhen-ji", rulesVersion: 33);
        var legacyAdvance = legacy.Submit(new AdvanceCommand(legacy.Revision));
        var legacyPlayers = ((System.Collections.IEnumerable)typeof(GameEngine)
                .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(legacy)!)
            .Cast<object>()
            .ToArray();
        var legacyResponseCards = (IReadOnlyList<Card>)typeof(GameEngine)
            .GetMethod("GetResponseCards", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(legacy, [legacyPlayers[0], CardKind.Dodge])!;
        Require(legacyAdvance.Accepted &&
                legacy.PendingDecision?.Kind == DecisionKind.PlayCard &&
                legacy.Events.Select(item => item.Payload).OfType<JudgmentResolvedEvent>()
                    .All(item => item.Reason != JudgmentReasons.Luoshen) &&
                legacyResponseCards.All(card => card.Kind == CardKind.Dodge),
            legacyAdvance.Error?.Message ??
            "Rules v33 must retain the historical turn start and native-only Dodge response set.");
    }

    public static void FormalJizhiAndQicaiFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var (game, action) = FindHuangYueyingOrdinaryTrickFixture(
            registry,
            GameCheckpoint.CurrentRulesVersion,
            requireNullification: false);
        var owner = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(owner.GeneralId == "classic:huang-yueying" &&
                owner.MaxHp == 4 &&
                owner.Skills!.Select(skill => skill.Kind).SequenceEqual([SkillKind.Jizhi, SkillKind.Qicai]),
            "Classic Huang Yueying must expose formal Shu, Lord-adjusted 4 HP, Jizhi and Qicai.");

        var played = game.Submit(new PlayCardCommand(
            0,
            action.CardId!.Value,
            action.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            action.PlayedCardKind,
            action.TargetCardId));
        var prompt = game.PendingDecision;
        Require(played.Accepted &&
                prompt is
                {
                    Kind: DecisionKind.Jizhi,
                    PlayerSeat: 0,
                    IsPrivate: true,
                    Choices.Count: 2
                } &&
                prompt.IncomingCard == (action.PlayedCardKind ?? owner.Hand.Single(card => card.Id == action.CardId).Kind) &&
                game.CreateSnapshot(1).PendingDecision is null &&
                game.ResolutionStack.LastOrDefault() is CardUseFrame { Step: ResolutionFrameStep.Declared },
            played.Error?.Message ??
            "Using an ordinary trick must pause at a private Jizhi choice before the Nullification window.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restored = GameReplay.Restore(pausedCheckpoint, registry);
        Require(restored.PendingDecision?.Kind == DecisionKind.Jizhi &&
                SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(game)),
            "A paused Jizhi choice must restore before the ordinary trick enters its Nullification window.");

        var useChoice = prompt!.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "jizhi-use");
        var used = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            useChoice.Id,
            game.Revision));
        Require(used.Accepted &&
                game.Events.Select(item => item.Payload).OfType<DrawSkillResolvedEvent>().Any(resolved =>
                    resolved.SourceSeat == 0 &&
                    resolved.Skill == SkillKind.Jizhi &&
                    resolved.Used &&
                    resolved.DrawCount == 1) &&
                game.CardMovements.Count(movement =>
                    movement.To == CardLocation.Hand(0) &&
                    movement.Reason == CardMoveReasons.JizhiDraw) == 1 &&
                game.PendingDecision?.Kind != DecisionKind.Jizhi,
            used.Error?.Message ?? "Accepting Jizhi must draw exactly one card and resume the original trick.");
        var completedReplay = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(completedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(completedReplay).SequenceEqual(EventSignatures(game)),
            "The resumed ordinary-trick/Jizhi flow must replay exactly.");

        var skipped = GameReplay.Restore(pausedCheckpoint, registry);
        var skippedPrompt = skipped.PendingDecision!;
        var skippedResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skippedPrompt.PromptId,
            skippedPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "jizhi-skip").Id,
            skipped.Revision));
        Require(skippedResult.Accepted &&
                skipped.CardMovements.All(movement => movement.Reason != CardMoveReasons.JizhiDraw) &&
                skipped.Events.Select(item => item.Payload).OfType<DrawSkillResolvedEvent>().Any(resolved =>
                    resolved.SourceSeat == 0 &&
                    resolved.Skill == SkillKind.Jizhi &&
                    !resolved.Used &&
                    resolved.DrawCount == 0),
            skippedResult.Error?.Message ?? "Skipping Jizhi must resume the trick without drawing a card.");

        var nullificationGame = FindHuangYueyingNullificationJizhiFixture(registry);
        var nullificationPrompt = nullificationGame.PendingDecision!;
        Require(nullificationPrompt.Kind == DecisionKind.Jizhi &&
                nullificationPrompt.IncomingCard == CardKind.Nullification &&
                nullificationGame.CardMovements.Any(movement =>
                    movement.CardKind == CardKind.Nullification &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.NullificationFinished) &&
                nullificationGame.ResolutionStack.LastOrDefault() is NullificationWindowFrame,
            "Using Nullification must open Jizhi while retaining the parent Nullification cursor.");
        var nullificationCheckpoint = nullificationGame.CreateCheckpoint();
        var nullificationUsed = nullificationGame.Submit(new AnswerPromptCommand(
            0,
            nullificationPrompt.PromptId,
            nullificationPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "jizhi-use").Id,
            nullificationGame.Revision));
        Require(nullificationUsed.Accepted &&
                nullificationGame.Events.Select(item => item.Payload).OfType<DrawSkillResolvedEvent>().Any(resolved =>
                    resolved.SourceSeat == 0 &&
                    resolved.Skill == SkillKind.Jizhi &&
                    resolved.Used),
            nullificationUsed.Error?.Message ??
            "Jizhi after Nullification must draw and resume the exact counter-chain cursor.");
        var nullificationReplay = GameReplay.Restore(nullificationGame.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(nullificationReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(nullificationGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(nullificationReplay).SequenceEqual(EventSignatures(nullificationGame)) &&
                GameReplay.Restore(nullificationCheckpoint, registry).PendingDecision?.Kind == DecisionKind.Jizhi,
            "Paused and resumed Nullification-triggered Jizhi must replay exactly.");

        var (legacy, legacyAction) = FindHuangYueyingOrdinaryTrickFixture(
            registry,
            rulesVersion: 34,
            requireNullification: false);
        var legacyPlayed = legacy.Submit(new PlayCardCommand(
            0,
            legacyAction.CardId!.Value,
            legacyAction.TargetSeats,
            legacy.Revision,
            legacy.PendingDecision!.PromptId,
            legacyAction.PlayedCardKind,
            legacyAction.TargetCardId));
        Require(legacyPlayed.Accepted &&
                legacy.PendingDecision?.Kind != DecisionKind.Jizhi &&
                legacy.Events.Select(item => item.Payload).OfType<DrawSkillResolvedEvent>()
                    .All(resolved => resolved.Skill != SkillKind.Jizhi) &&
                legacy.CardMovements.All(movement => movement.Reason != CardMoveReasons.JizhiDraw),
            legacyPlayed.Error?.Message ?? "Rules v34 must not publish or resolve Jizhi.");
    }

    public static void FormalTieqiAndMashuFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var red = FindMaChaoTieqiFixture(registry, requireRedJudgment: true);
        var owner = red.Game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(owner.GeneralId == "classic:ma-chao" &&
                owner.MaxHp == 5 &&
                owner.Skills!.Select(skill => skill.Kind).SequenceEqual([SkillKind.Tieqi, SkillKind.Mashu]),
            "Classic Ma Chao must expose formal Shu, Lord-adjusted 5 HP, Tieqi and Mashu.");
        Require(red.Prompt is
        {
            Kind: DecisionKind.Tieqi,
            PlayerSeat: 0,
            IsPrivate: true,
            Choices.Count: 2
        } &&
                red.Prompt.TargetSeat == red.TargetSeat &&
                red.Game.CreateSnapshot(red.TargetSeat).PendingDecision is null &&
                red.Game.ResolutionStack.LastOrDefault() is CardUseFrame
                {
                    Step: ResolutionFrameStep.Declared
                },
            "Using Slash must pause at a private Tieqi choice before any Dodge response.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(red.Game.CreateCheckpoint()));
        var pausedReplay = GameReplay.Restore(pausedCheckpoint, registry);
        Require(pausedReplay.PendingDecision?.Kind == DecisionKind.Tieqi &&
                SnapshotJson.Serialize(pausedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(red.Game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(pausedReplay).SequenceEqual(EventSignatures(red.Game)),
            "A paused Tieqi choice must restore before its judgment and Slash response.");

        var redEventCount = red.Game.Events.Count;
        var used = red.Game.Submit(new AnswerPromptCommand(
            0,
            red.Prompt.PromptId,
            red.Prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "tieqi-use").Id,
            red.Game.Revision));
        var redEvents = red.Game.Events.Skip(redEventCount).Select(item => item.Payload).ToArray();
        Require(used.Accepted &&
                redEvents.OfType<TieqiChoiceResolvedEvent>().Any(resolved =>
                    resolved.SourceSeat == 0 && resolved.TargetSeat == red.TargetSeat && resolved.Used) &&
                redEvents.OfType<JudgmentResolvedEvent>().Any(resolved =>
                    resolved.Reason == JudgmentReasons.Tieqi &&
                    resolved.TargetSeat == 0 &&
                    resolved.Suit is Suit.Heart or Suit.Diamond &&
                    resolved.Succeeded) &&
                redEvents.OfType<ResponseRequestedEvent>().All(requested =>
                    requested.TargetSeat != red.TargetSeat || requested.RequiredCardKind != CardKind.Dodge) &&
                redEvents.OfType<DamageAppliedEvent>().Any(damage => damage.TargetSeat == red.TargetSeat),
            used.Error?.Message ??
            "A red Tieqi judgment must prohibit the target's published Dodge and continue to Slash damage.");
        var completedReplay = GameReplay.Restore(red.Game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(completedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(red.Game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(completedReplay).SequenceEqual(EventSignatures(red.Game)),
            "The completed red Tieqi Slash must replay exactly.");

        var skipped = GameReplay.Restore(pausedCheckpoint, registry);
        var skipPrompt = skipped.PendingDecision!;
        var judgmentCountBeforeSkip = skipped.Events.Select(item => item.Payload)
            .OfType<JudgmentRequestedEvent>().Count(item => item.Reason == JudgmentReasons.Tieqi);
        var skippedResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "tieqi-skip").Id,
            skipped.Revision));
        Require(skippedResult.Accepted &&
                skipped.Events.Select(item => item.Payload).OfType<ResponseRequestedEvent>().Any(requested =>
                    requested.TargetSeat == red.TargetSeat &&
                    requested.RequiredCardKind == CardKind.Dodge) &&
                skipped.Events.Select(item => item.Payload)
                    .OfType<JudgmentRequestedEvent>().Count(item => item.Reason == JudgmentReasons.Tieqi) ==
                judgmentCountBeforeSkip &&
                skipped.Events.Select(item => item.Payload).OfType<TieqiChoiceResolvedEvent>().Any(resolved =>
                    resolved.SourceSeat == 0 && resolved.TargetSeat == red.TargetSeat && !resolved.Used),
            skippedResult.Error?.Message ??
            $"Skipping Tieqi must open the ordinary Dodge response without creating a judgment " +
            $"(pending={skipped.PendingDecision?.Kind}/{skipped.PendingDecision?.PlayerSeat}, " +
            $"expected={red.TargetSeat}, judgments={skipped.Events.Select(item => item.Payload).OfType<JudgmentRequestedEvent>().Count(item => item.Reason == JudgmentReasons.Tieqi)}, " +
            $"choices={skipped.Events.Select(item => item.Payload).OfType<TieqiChoiceResolvedEvent>().Count()}).");

        var black = FindMaChaoTieqiFixture(registry, requireRedJudgment: false);
        var blackEventCount = black.Game.Events.Count;
        var blackUsed = black.Game.Submit(new AnswerPromptCommand(
            0,
            black.Prompt.PromptId,
            black.Prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "tieqi-use").Id,
            black.Game.Revision));
        var blackEvents = black.Game.Events.Skip(blackEventCount).Select(item => item.Payload).ToArray();
        Require(blackUsed.Accepted &&
                blackEvents.OfType<JudgmentResolvedEvent>().Any(resolved =>
                    resolved.Reason == JudgmentReasons.Tieqi &&
                    resolved.Suit is Suit.Spade or Suit.Club &&
                    !resolved.Succeeded) &&
                blackEvents.OfType<ResponseRequestedEvent>().Any(requested =>
                    requested.TargetSeat == black.TargetSeat &&
                    requested.RequiredCardKind == CardKind.Dodge),
            blackUsed.Error?.Message ??
            $"A black Tieqi judgment must retain the target's ordinary Dodge response " +
            $"(pending={black.Game.PendingDecision?.Kind}/{black.Game.PendingDecision?.PlayerSeat}, " +
            $"expected={black.TargetSeat}, valid={black.Game.PendingDecision?.ValidCardIds.Count}, " +
            $"judgment={string.Join(',', blackEvents.OfType<JudgmentResolvedEvent>().Where(item => item.Reason == JudgmentReasons.Tieqi).Select(item => $"{item.Suit}/{item.Succeeded}"))}, " +
            $"responses={blackEvents.OfType<ResponseRequestedEvent>().Count()}).");

        var legacy = FindMaChaoSlashFixture(registry, red.Seed, rulesVersion: 35);
        Require(legacy.Game.Events.Select(item => item.Payload).OfType<ResponseRequestedEvent>().Any(requested =>
                    requested.TargetSeat == legacy.TargetSeat &&
                    requested.RequiredCardKind == CardKind.Dodge) &&
                legacy.Game.Events.Select(item => item.Payload).OfType<TieqiChoiceResolvedEvent>().Count() == 0 &&
                legacy.Game.Events.Select(item => item.Payload).OfType<JudgmentRequestedEvent>()
                    .All(item => item.Reason != JudgmentReasons.Tieqi),
            "Rules v35 must not publish or resolve Tieqi.");
    }

    public static void FormalLiegongFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var eligible = FindHuangZhongLiegongFixture(
            registry,
            GameCheckpoint.CurrentRulesVersion,
            LiegongFixtureKind.EligibleByHp);
        var eligiblePrompt = eligible.Prompt ??
            throw new InvalidOperationException("Eligible Liegong did not publish its private choice.");
        var owner = eligible.Game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(owner.GeneralId == "classic:huang-zhong" &&
                owner.MaxHp == 4 &&
                owner.Skills!.Select(skill => skill.Kind).SequenceEqual([SkillKind.Liegong]),
            "Classic rebel Huang Zhong must expose formal Shu, 4 HP and Liegong.");
        Require(eligiblePrompt is
        {
            Kind: DecisionKind.Liegong,
            PlayerSeat: 0,
            IsPrivate: true,
            Choices.Count: 2
        } &&
                eligiblePrompt.TargetSeat == eligible.TargetSeat &&
                eligible.TargetHandCount >= eligible.SourceHp &&
                eligible.TargetHandCount > eligible.AttackRange &&
                eligible.Game.CreateSnapshot(eligible.TargetSeat).PendingDecision is null &&
                eligible.Game.ResolutionStack.LastOrDefault() is CardUseFrame
                {
                    Step: ResolutionFrameStep.Declared
                },
            "An HP-eligible Slash must pause at a private Liegong choice before any Dodge response.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(eligible.Game.CreateCheckpoint()));
        var pausedReplay = GameReplay.Restore(pausedCheckpoint, registry);
        Require(pausedReplay.PendingDecision?.Kind == DecisionKind.Liegong &&
                SnapshotJson.Serialize(pausedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(eligible.Game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(pausedReplay).SequenceEqual(EventSignatures(eligible.Game)),
            "A paused Liegong choice must restore before its Slash response.");

        var eventCount = eligible.Game.Events.Count;
        var used = eligible.Game.Submit(new AnswerPromptCommand(
            0,
            eligiblePrompt.PromptId,
            eligiblePrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "liegong-use").Id,
            eligible.Game.Revision));
        var usedEvents = eligible.Game.Events.Skip(eventCount).Select(item => item.Payload).ToArray();
        Require(used.Accepted &&
                usedEvents.OfType<LiegongChoiceResolvedEvent>().Any(resolved =>
                    resolved.SourceSeat == 0 &&
                    resolved.TargetSeat == eligible.TargetSeat &&
                    resolved.Used) &&
                usedEvents.OfType<ResponseRequestedEvent>().All(requested =>
                    requested.TargetSeat != eligible.TargetSeat ||
                    requested.RequiredCardKind != CardKind.Dodge) &&
                usedEvents.OfType<DamageAppliedEvent>().Any(damage =>
                    damage.TargetSeat == eligible.TargetSeat),
            used.Error?.Message ??
            $"Using eligible Liegong must prohibit the target's Dodge and continue to Slash damage " +
            $"(target={eligible.Game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == eligible.TargetSeat).GeneralId}, " +
            $"pending={eligible.Game.PendingDecision?.Kind}, events={string.Join(',', usedEvents.Select(item => item.GetType().Name))}).");
        var completedReplay = GameReplay.Restore(eligible.Game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(completedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(eligible.Game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(completedReplay).SequenceEqual(EventSignatures(eligible.Game)),
            "The completed Liegong Slash must replay exactly.");

        var skipped = GameReplay.Restore(pausedCheckpoint, registry);
        var skipPrompt = skipped.PendingDecision!;
        var skippedResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "liegong-skip").Id,
            skipped.Revision));
        Require(skippedResult.Accepted &&
                skipped.Events.Select(item => item.Payload).OfType<ResponseRequestedEvent>().Any(requested =>
                    requested.TargetSeat == eligible.TargetSeat &&
                    requested.RequiredCardKind == CardKind.Dodge) &&
                skipped.Events.Select(item => item.Payload).OfType<LiegongChoiceResolvedEvent>().Any(resolved =>
                    resolved.SourceSeat == 0 &&
                    resolved.TargetSeat == eligible.TargetSeat &&
                    !resolved.Used),
            skippedResult.Error?.Message ??
            "Skipping eligible Liegong must open the ordinary Dodge response.");

        var rangeEligible = FindHuangZhongLiegongFixture(
            registry,
            GameCheckpoint.CurrentRulesVersion,
            LiegongFixtureKind.EligibleByRange);
        Require(rangeEligible.Prompt?.Kind == DecisionKind.Liegong &&
                rangeEligible.TargetHandCount <= rangeEligible.AttackRange &&
                rangeEligible.TargetHandCount < rangeEligible.SourceHp,
            "A target whose hand count is within Huang Zhong's attack range must be Liegong-eligible independently of HP.");

        var ineligible = FindHuangZhongLiegongFixture(
            registry,
            GameCheckpoint.CurrentRulesVersion,
            LiegongFixtureKind.Ineligible);
        Require(ineligible.TargetHandCount < ineligible.SourceHp &&
                ineligible.TargetHandCount > ineligible.AttackRange &&
                ineligible.Game.Events.Select(item => item.Payload).OfType<ResponseRequestedEvent>().Any(requested =>
                    requested.TargetSeat == ineligible.TargetSeat &&
                    requested.RequiredCardKind == CardKind.Dodge) &&
                ineligible.Game.Events.Select(item => item.Payload).OfType<LiegongChoiceResolvedEvent>().Count() == 0,
            "A Slash target outside both Liegong hand-count conditions must receive the ordinary Dodge response.");

        var legacy = FindHuangZhongLiegongFixture(
            registry,
            rulesVersion: 36,
            LiegongFixtureKind.EligibleByHp);
        Require(legacy.Game.Events.Select(item => item.Payload).OfType<ResponseRequestedEvent>().Any(requested =>
                    requested.TargetSeat == legacy.TargetSeat &&
                    requested.RequiredCardKind == CardKind.Dodge) &&
                legacy.Game.Events.Select(item => item.Payload).OfType<LiegongChoiceResolvedEvent>().Count() == 0,
            "Rules v36 must not publish or resolve Liegong.");
    }

    public static void FormalKuangguFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var fixture = FindWeiYanKuangguFixture(registry);
        var game = fixture.Game;
        var events = game.Events.Skip(fixture.EventCount).Select(item => item.Payload).ToArray();
        var owner = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        var recovered = events.OfType<KuangguRecoveredEvent>().Single();
        Require(owner.GeneralId == "classic:wei-yan" &&
                owner.MaxHp == 4 &&
                owner.Hp == fixture.SourceHpBefore + 1 &&
                owner.Skills!.Select(skill => skill.Kind).SequenceEqual([SkillKind.Kuanggu]) &&
                fixture.Distance == 1 &&
                recovered.SourceSeat == 0 &&
                recovered.TargetSeat == fixture.TargetSeat &&
                recovered.DamageAmount == 1 &&
                recovered.RecoveredAmount == 1 &&
                recovered.RemainingHp == fixture.SourceHpBefore + 1 &&
                events.OfType<DamageAppliedEvent>().Any(damage =>
                    damage.SourceSeat == 0 &&
                    damage.TargetSeat == fixture.TargetSeat &&
                    damage.Amount == 1) &&
                events.OfType<RecoveryAppliedEvent>().Any(recovery =>
                    recovery.SourceSeat == 0 &&
                    recovery.TargetSeat == 0 &&
                    recovery.Amount == 1 &&
                    recovery.RemainingHp == fixture.SourceHpBefore + 1),
            "A wounded Wei Yan must recover after dealing damage to a distance-one target.");

        var damageIndex = Array.FindIndex(events, item => item is DamageAppliedEvent applied &&
            applied.TargetSeat == fixture.TargetSeat);
        var recoveryIndex = Array.FindIndex(events, item => item is KuangguRecoveredEvent);
        Require(damageIndex >= 0 && recoveryIndex > damageIndex,
            "Kuanggu recovery must resolve after its damage is applied.");

        var replayed = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(replayed.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replayed).SequenceEqual(EventSignatures(game)),
            "A completed Kuanggu recovery must replay exactly.");

        var legacyFixture = FindWeiYanKuangguFixture(registry, rulesVersion: 37);
        var legacy = legacyFixture.Game;
        var legacyEvents = legacy.Events.Skip(legacyFixture.EventCount).Select(item => item.Payload).ToArray();
        Require(legacy.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hp ==
                legacyFixture.SourceHpBefore &&
                legacyEvents.OfType<DamageAppliedEvent>().Any(damage =>
                    damage.TargetSeat == legacyFixture.TargetSeat && damage.Amount == 1) &&
                legacyEvents.OfType<KuangguRecoveredEvent>().Count() == 0 &&
                legacyEvents.OfType<RecoveryAppliedEvent>().All(recovery =>
                    recovery.TargetSeat != 0),
            "A match created under rules v37 must not resolve Kuanggu.");

        var fullHealth = GameReplay.Restore(fixture.BeforeDamage, registry);
        SetPlayerHp(fullHealth, seat: 0, hp: 4);
        var fullHealthEventCount = fullHealth.Events.Count;
        var fullHealthResult = SubmitPlayAction(fullHealth, fixture.Action);
        var fullHealthEvents = fullHealth.Events.Skip(fullHealthEventCount).Select(item => item.Payload).ToArray();
        Require(fullHealthResult.Accepted &&
                fullHealth.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hp == 4 &&
                fullHealthEvents.OfType<KuangguRecoveredEvent>().Count() == 0,
            fullHealthResult.Error?.Message ?? "Full-health Wei Yan must not create a no-op Kuanggu recovery.");
    }

    public static void FormalWushuangFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();

        var slashFixture = FindLuBuWushuangSlashFixture(registry);
        var slashEventCount = slashFixture.Game.Events.Count;
        var slashResult = SubmitPlayAction(slashFixture.Game, slashFixture.Action);
        Require(slashResult.Accepted, slashResult.Error?.Message ?? "Lu Bu could not use the Wushuang Slash.");
        DriveAiUntil(slashFixture.Game, () => slashFixture.Game.Events.Skip(slashEventCount)
            .Select(item => item.Payload)
            .OfType<RequiredResponseProgressEvent>()
            .Count(progress => progress.RequiredCardKind == CardKind.Dodge) >= 1);
        var secondDodgePrompt = slashFixture.Game.CreateSnapshot(slashFixture.TargetSeat).PendingDecision;
        Require(secondDodgePrompt is
        {
            Kind: DecisionKind.RespondDodge,
            PlayerSeat: var responderSeat,
            RequiredCardKind: CardKind.Dodge
        } &&
                responderSeat == slashFixture.TargetSeat &&
                secondDodgePrompt.Prompt.Contains("第 2 张", StringComparison.Ordinal),
            "The first Wushuang Dodge must open a distinct second-Dodge response window.");

        var midSlashReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(slashFixture.Game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(midSlashReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(slashFixture.Game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(midSlashReplay).SequenceEqual(EventSignatures(slashFixture.Game)),
            "A Wushuang Slash must restore exactly between its first and second Dodge windows.");

        DriveAiUntil(slashFixture.Game, () => slashFixture.Game.Events.Skip(slashEventCount)
            .Select(item => item.Payload)
            .OfType<RequiredResponseProgressEvent>()
            .Count(progress => progress.RequiredCardKind == CardKind.Dodge) >= 2);
        DriveAiUntil(midSlashReplay, () => midSlashReplay.Events.Skip(slashEventCount)
            .Select(item => item.Payload)
            .OfType<RequiredResponseProgressEvent>()
            .Count(progress => progress.RequiredCardKind == CardKind.Dodge) >= 2);
        var slashEvents = slashFixture.Game.Events.Skip(slashEventCount).Select(item => item.Payload).ToArray();
        var slashProgress = slashEvents.OfType<RequiredResponseProgressEvent>()
            .Where(progress => progress.RequiredCardKind == CardKind.Dodge)
            .ToArray();
        Require(slashProgress.Select(progress => progress.ResponseCount).SequenceEqual([1, 2]) &&
                slashProgress.All(progress =>
                    progress.SkillOwnerSeat == 0 &&
                    progress.ResponderSeat == slashFixture.TargetSeat &&
                    progress.RequiredResponseCount == 2) &&
                slashEvents.OfType<CardRespondedEvent>().Count(response =>
                    response.ResponderSeat == slashFixture.TargetSeat) == 2 &&
                slashEvents.OfType<DamageAppliedEvent>().All(damage =>
                    damage.TargetSeat != slashFixture.TargetSeat),
            "A Wushuang Slash must consume two sequential Dodge responses before it is canceled.");

        Require(SnapshotJson.Serialize(midSlashReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(slashFixture.Game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(midSlashReplay).SequenceEqual(EventSignatures(slashFixture.Game)),
            "A resumed Wushuang Slash must finish identically after its second Dodge.");

        var slashReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(slashFixture.Game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(slashReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(slashFixture.Game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(slashReplay).SequenceEqual(EventSignatures(slashFixture.Game)),
            "A completed two-Dodge Wushuang Slash must replay exactly.");

        var legacySlash = GameReplay.Restore(
            slashFixture.BeforeAction with { RulesVersion = 38 },
            registry);
        var legacySlashEventCount = legacySlash.Events.Count;
        var legacySlashResult = SubmitPlayAction(legacySlash, slashFixture.Action);
        Require(legacySlashResult.Accepted, legacySlashResult.Error?.Message ??
            "Rules v38 could not reproduce the Slash fixture.");
        DriveAiUntil(legacySlash, () => legacySlash.Events.Skip(legacySlashEventCount)
            .Select(item => item.Payload)
            .OfType<CardRespondedEvent>()
            .Any(response => response.ResponderSeat == slashFixture.TargetSeat));
        var legacySlashEvents = legacySlash.Events.Skip(legacySlashEventCount).Select(item => item.Payload).ToArray();
        Require(legacySlashEvents.OfType<CardRespondedEvent>().Count(response =>
                    response.ResponderSeat == slashFixture.TargetSeat) == 1 &&
                legacySlashEvents.OfType<RequiredResponseProgressEvent>().Count() == 0 &&
                legacySlashEvents.OfType<DamageAppliedEvent>().All(damage =>
                    damage.TargetSeat != slashFixture.TargetSeat),
            "Rules v38 must retain the historical one-Dodge Slash response.");

        var duelFixture = FindLuBuWushuangDuelFixture(registry);
        SetPlayerHp(duelFixture.Game, duelFixture.TargetSeat, hp: 1);
        var duelEventCount = duelFixture.Game.Events.Count;
        var duelResult = SubmitPlayAction(duelFixture.Game, duelFixture.Action);
        Require(duelResult.Accepted, duelResult.Error?.Message ?? "Lu Bu could not use the Wushuang Duel.");
        DriveAiUntil(duelFixture.Game, () => duelFixture.Game.Events.Skip(duelEventCount)
            .Select(item => item.Payload)
            .OfType<DamageAppliedEvent>()
            .Any(damage => damage.TargetSeat == duelFixture.TargetSeat));
        var duelEvents = duelFixture.Game.Events.Skip(duelEventCount).Select(item => item.Payload).ToArray();
        var duelDiagnostics = string.Join(", ", duelEvents.Select(item => item switch
        {
            DuelResponseEvent response => $"duel:{response.ResponderSeat}:{response.UsedSlash}",
            RequiredResponseProgressEvent progress =>
                $"progress:{progress.ResponderSeat}:{progress.ResponseCount}/{progress.RequiredResponseCount}",
            DamageAppliedEvent damage => $"damage:{damage.SourceSeat}->{damage.TargetSeat}:{damage.Amount}",
            _ => item.GetType().Name
        }));
        Require(duelEvents.OfType<DuelResponseEvent>().Count(response =>
                    response.ResponderSeat == duelFixture.TargetSeat && response.UsedSlash) == 1 &&
                duelEvents.OfType<DuelResponseEvent>().Any(response =>
                    response.ResponderSeat == duelFixture.TargetSeat && !response.UsedSlash) &&
                duelEvents.OfType<RequiredResponseProgressEvent>().Any(progress =>
                    progress.SkillOwnerSeat == 0 &&
                    progress.ResponderSeat == duelFixture.TargetSeat &&
                    progress.IncomingCard == CardKind.Duel &&
                    progress.RequiredCardKind == CardKind.Slash &&
                    progress.ResponseCount == 1 &&
                    progress.RequiredResponseCount == 2),
            $"A Wushuang Duel opponent with one Slash must pay it and then fail the second response. {duelDiagnostics}");

        var legacyDuel = GameReplay.Restore(
            duelFixture.BeforeAction with { RulesVersion = 38 },
            registry);
        SetPlayerHp(legacyDuel, duelFixture.TargetSeat, hp: 1);
        var legacyDuelEventCount = legacyDuel.Events.Count;
        var legacyDuelResult = SubmitPlayAction(legacyDuel, duelFixture.Action);
        Require(legacyDuelResult.Accepted, legacyDuelResult.Error?.Message ??
            "Rules v38 could not reproduce the Duel fixture.");
        DriveAiUntil(legacyDuel, () => legacyDuel.Events.Skip(legacyDuelEventCount)
            .Select(item => item.Payload)
            .OfType<DuelResponseEvent>()
            .Any(response => response.ResponderSeat == duelFixture.TargetSeat && response.UsedSlash));
        var legacyDuelEvents = legacyDuel.Events.Skip(legacyDuelEventCount).Select(item => item.Payload).ToArray();
        Require(legacyDuelEvents.OfType<RequiredResponseProgressEvent>().Count() == 0 &&
                legacyDuelEvents.OfType<DamageAppliedEvent>().All(damage =>
                    damage.TargetSeat != duelFixture.TargetSeat),
            "Rules v38 must let one Slash complete the opponent's Duel response set.");
    }

    public static void FormalPaoxiaoFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var fixture = FindZhangFeiPaoxiaoFixture(registry);
        var game = fixture.Game;
        var before = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        Require(before.GeneralId == "classic:zhang-fei" &&
                before.MaxHp == 5 &&
                before.Skills!.Select(skill => skill.Kind).SequenceEqual([SkillKind.Paoxiao]) &&
                before.Equipment.All(card => card.Kind != CardKind.Crossbow),
            "Classic Zhang Fei must enter the Lord fixture with formal Paoxiao and no Crossbow fallback.");

        var eventCount = game.Events.Count;
        var first = SubmitPlayAction(game, fixture.FirstAction);
        Require(first.Accepted, first.Error?.Message ?? "Classic Zhang Fei could not use his first Slash.");
        Require(TryReturnToHumanPlay(game),
            "Classic Zhang Fei did not return to the same play phase after his first Slash.");

        var secondAction = game.GetHumanLegalActions()
            .Where(action => action.Kind == LegalActionKind.Slash && action.CardId is not null)
            .OrderBy(action => action.CardId)
            .ThenBy(action => action.TargetSeat)
            .FirstOrDefault();
        Require(secondAction is not null,
            "Paoxiao must leave a second physical Slash legal in the same play phase.");
        var second = SubmitPlayAction(game, secondAction!);
        Require(second.Accepted, second.Error?.Message ?? "Paoxiao rejected Zhang Fei's second Slash.");

        var slashUses = game.Events.Skip(eventCount)
            .Select(item => item.Payload)
            .OfType<CardUsedEvent>()
            .Where(item => item.SourceSeat == 0 &&
                           item.CardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)
            .ToArray();
        Require(slashUses.Length == 2 &&
                slashUses.Select(item => item.CardId).Distinct().Count() == 2,
            "Formal Paoxiao must publish two distinct Slash uses in one play phase.");

        var replayed = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(replayed.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replayed).SequenceEqual(EventSignatures(game)),
            "A second in-flight Paoxiao Slash must replay exactly.");
    }

    public static void FormalLongdanFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var fixture = FindZhaoYunLongdanFixture(registry);
        var game = fixture.Game;
        var before = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        var physicalDodge = before.Hand.Single(card => card.Id == fixture.Action.CardId);
        Require(before.GeneralId == "classic:zhao-yun" &&
                before.MaxHp == 5 &&
                before.Skills!.Select(skill => skill.Kind).SequenceEqual([SkillKind.Longdan]) &&
                physicalDodge.Kind == CardKind.Dodge &&
                fixture.Action.PlayedCardKind == CardKind.Slash,
            "Classic Zhao Yun must publish a physical Dodge as a typed Slash through formal Longdan.");

        var eventCount = game.Events.Count;
        var used = SubmitPlayAction(game, fixture.Action);
        Require(used.Accepted, used.Error?.Message ?? "Classic Zhao Yun could not use Dodge as Slash.");
        Require(TryReturnToHumanPlay(game),
            "Classic Zhao Yun did not finish the converted Slash and return to play.");
        var playEvents = game.Events.Skip(eventCount).Select(item => item.Payload).ToArray();
        Require(playEvents.OfType<CardUseDeclaredEvent>().Any(item =>
                    item.CardId == physicalDodge.Id && item.CardKind == CardKind.Slash) &&
                playEvents.OfType<CardUsedEvent>().Any(item =>
                    item.CardId == physicalDodge.Id && item.CardKind == CardKind.Slash) &&
                game.CardMovements.Any(move =>
                    move.CardId == physicalDodge.Id &&
                    move.CardKind == CardKind.Dodge &&
                    move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Processing &&
                    move.Reason == CardMoveReasons.Use),
            "Formal Longdan must preserve the physical Dodge while publishing an effective Slash use.");

        var activeReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(activeReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(activeReplay).SequenceEqual(EventSignatures(game)),
            "A completed formal Longdan Dodge-to-Slash use must replay exactly.");

        var responseGame = WushengResponseScenario.FindLongdanDodge(
            registry,
            "identity:classic-8");
        var responseOwner = responseGame.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == 0);
        var prompt = responseGame.PendingDecision ??
            throw new InvalidOperationException("The formal Longdan response fixture lost its prompt.");
        var choice = prompt.Choices.First(candidate =>
            candidate.Parameters.GetValueOrDefault("response") == "dodge" &&
            candidate.Parameters.GetValueOrDefault("response-card-kind") == nameof(CardKind.Dodge) &&
            candidate.Cards.Count == 1 &&
            responseOwner.Hand.Single(card => card.Id == candidate.Cards[0]).Kind is
                CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash);
        var physicalSlash = responseOwner.Hand.Single(card => card.Id == choice.Cards[0]);
        Require(responseOwner.GeneralId == "classic:zhao-yun" &&
                prompt.Kind == DecisionKind.RespondDodge &&
                choice.Description.Contains("当作【闪】", StringComparison.Ordinal),
            "Classic Zhao Yun must publish a Slash-to-Dodge response without leaking the physical identity.");

        var response = responseGame.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            choice.Id,
            responseGame.Revision));
        Require(response.Accepted &&
                responseGame.Events.Any(item =>
                    item.Payload is CardRespondedEvent responded &&
                    responded.CardId == physicalSlash.Id &&
                    responded.ResponderSeat == 0 &&
                    responded.EffectiveCardKind == CardKind.Dodge) &&
                responseGame.CardMovements.Any(move =>
                    move.CardId == physicalSlash.Id &&
                    move.CardKind == physicalSlash.Kind &&
                    move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Processing &&
                    move.Reason == CardMoveReasons.Respond),
            response.Error?.Message ??
            "Formal Longdan must preserve the physical Slash while publishing an effective Dodge response.");

        var responseReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(responseGame.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(responseReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(responseGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(responseReplay).SequenceEqual(EventSignatures(responseGame)),
            "An in-flight formal Longdan Slash-to-Dodge response must replay exactly.");
    }

    public static void FormalWushengEquipmentFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(GameCheckpoint.CurrentRulesVersion >= 40,
            "Formal Wusheng equipment conversion must have an explicit rules-version boundary.");

        var wusheng = SkillRegistry.Get(SkillKind.Wusheng);
        var context = new PlayerSkillContext(0, 4, 5, 4, TurnPhase.Play);
        Require(wusheng.CanUseAsSlash(context, new Card(9201, CardKind.Crossbow, Suit.Diamond, 1)) &&
                !wusheng.CanUseAsSlash(context, new Card(9202, CardKind.Crossbow, Suit.Spade, 1)),
            "Wusheng must classify red and black equipment by the same physical-card rule as hand cards.");

        var fixture = FindGuanYuWushengEquipmentFixture(registry);
        var activeGame = fixture.ActiveGame;
        var owner = activeGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
        var equipment = owner.Equipment.Single(card => card.Id == fixture.EquipmentCardId);
        Require(owner.GeneralId == "classic:guan-yu" &&
                owner.MaxHp == 5 &&
                owner.Skills!.Select(skill => skill.Kind).SequenceEqual([SkillKind.Wusheng]) &&
                equipment.Suit is Suit.Heart or Suit.Diamond &&
                fixture.ActiveAction.PlayedCardKind == CardKind.Slash,
            "Classic Guan Yu must publish a red equipment card as a typed Slash through formal Wusheng.");
        Require(!fixture.LegacyGame.GetHumanLegalActions().Any(action =>
                    action.Kind == LegalActionKind.Slash &&
                    action.CardId == equipment.Id &&
                    action.PlayedCardKind == CardKind.Slash),
            "Rules v39 must not publish Wusheng conversion actions from the equipment zone.");

        var legacyPlayers = ((System.Collections.IEnumerable)typeof(GameEngine)
                .GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(fixture.LegacyGame)!)
            .Cast<object>()
            .ToArray();
        var legacyResponseCards = (IReadOnlyList<Card>)typeof(GameEngine)
            .GetMethod("GetResponseCards", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(fixture.LegacyGame, [legacyPlayers[0], CardKind.Slash])!;
        Require(legacyResponseCards.All(card => card.Id != equipment.Id),
            "Rules v39 must not publish an equipped red card as a Slash response.");

        var eventCount = activeGame.Events.Count;
        var used = SubmitPlayAction(activeGame, fixture.ActiveAction);
        Require(used.Accepted, used.Error?.Message ??
            "Classic Guan Yu could not use red equipment as Slash.");
        Require(activeGame.CardMovements.Any(move =>
                    move.CardId == equipment.Id &&
                    move.CardKind == equipment.Kind &&
                    move.From == CardLocation.Equipment(0) &&
                    move.To == CardLocation.Processing &&
                    move.Reason == CardMoveReasons.Use) &&
                activeGame.Events.Skip(eventCount).Select(item => item.Payload)
                    .OfType<CardUsedEvent>().Any(item =>
                        item.CardId == equipment.Id && item.CardKind == CardKind.Slash),
            "Formal Wusheng must retain the physical equipment and publish an effective Slash use.");
        Require(TryReturnToHumanPlay(activeGame) &&
                activeGame.CardMovements.Any(move =>
                    move.CardId == equipment.Id &&
                    move.From == CardLocation.Processing &&
                    move.To == CardLocation.DiscardPile &&
                    move.Reason == CardMoveReasons.UseFinished),
            "The equipped Wusheng Slash did not finish through the ordinary Slash movement chain.");

        var activeReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(activeGame.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(activeReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(activeGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(activeReplay).SequenceEqual(EventSignatures(activeGame)),
            "A completed equipped Wusheng Slash must replay exactly.");

        var responseGame = fixture.ResponseGame;
        var responsePrompt = responseGame.PendingDecision ??
            throw new InvalidOperationException("The equipped Wusheng response fixture lost its prompt.");
        var responseOwner = responseGame.CreateSnapshot(0, revealAll: true).Players
            .Single(player => player.Seat == 0);
        var responseChoice = responsePrompt.Choices.Single(choice =>
            choice.Cards.SequenceEqual([equipment.Id]) &&
            choice.Parameters.GetValueOrDefault("response-card-kind") == nameof(CardKind.Slash));
        Require(responsePrompt.Kind == DecisionKind.RespondSlash &&
                responseOwner.Equipment.Any(card => card.Id == equipment.Id) &&
                responseChoice.Description.Contains("当作【杀】", StringComparison.Ordinal) &&
                responseGame.CreateSnapshot(1).PendingDecision is null,
            "Formal Wusheng must publish the equipped red card only in Guan Yu's private Slash response.");

        var responseCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(responseGame.CreateCheckpoint()));
        var pausedReplay = GameReplay.Restore(responseCheckpoint, registry);
        Require(SnapshotJson.Serialize(pausedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(responseGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(pausedReplay).SequenceEqual(EventSignatures(responseGame)),
            "An in-flight equipped Wusheng response must replay exactly.");

        var answered = responseGame.Submit(new AnswerPromptCommand(
            0,
            responsePrompt.PromptId,
            responseChoice.Id,
            responseGame.Revision));
        Require(answered.Accepted &&
                responseGame.Events.Any(item =>
                    item.Payload is CardRespondedEvent responded &&
                    responded.CardId == equipment.Id &&
                    responded.ResponderSeat == 0 &&
                    responded.EffectiveCardKind == CardKind.Slash) &&
                responseGame.CardMovements.Any(move =>
                    move.CardId == equipment.Id &&
                    move.CardKind == equipment.Kind &&
                    move.From == CardLocation.Equipment(0) &&
                    move.To == CardLocation.Processing &&
                    move.Reason == CardMoveReasons.Respond) &&
                responseGame.CardMovements.Any(move =>
                    move.CardId == equipment.Id &&
                    move.From == CardLocation.Processing &&
                    move.To == CardLocation.DiscardPile &&
                    move.Reason == CardMoveReasons.ResponseFinished),
            answered.Error?.Message ??
            "Formal Wusheng must pay the equipped physical card through the Slash response chain.");
    }

    public static void FormalFeedbackFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        GameEngine? selectedGame = null;
        PendingDecision? selectedPrompt = null;

        for (var seed = 1; seed <= 8_192 && selectedGame is null; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Classic Feedback fixture failed to start.");
            var simaYiChoice = started.Result.PendingDecision?.Choices.FirstOrDefault(choice =>
                choice.ContentIds.SequenceEqual(["classic:sima-yi"]));
            if (simaYiChoice is null) continue;

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:sima-yi",
                game.Revision,
                game.PendingDecision!.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Sima Yi selection was rejected.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Classic setup did not advance.");
            var result = advanced.Result;
            for (var step = 0; result.Status != EngineStatus.Completed && step < 4_000; step++)
            {
                if (result.Status == EngineStatus.AwaitingHumanResponse &&
                    result.PendingDecision is { Kind: DecisionKind.Feedback } feedbackPrompt)
                {
                    selectedGame = game;
                    selectedPrompt = feedbackPrompt;
                    break;
                }

                result = DeclineOrAdvance(game, result);
            }
        }

        if (selectedGame is null || selectedPrompt is null)
            throw new InvalidOperationException("No deterministic classic Feedback source-card boundary was found.");

        var gameWithFeedback = selectedGame;
        var prompt = selectedPrompt;
        var frame = gameWithFeedback.ResolutionStack.OfType<DamageSkillFrame>().Single();
        Require(frame.Skill == SkillKind.Feedback && frame.Effect == DamageSkillEffectKind.TakeSourceCard,
            "Classic Feedback must pause with the source-card effect.");
        Require(prompt.IsPrivate && prompt.SourceSeat is not null && prompt.TargetSeat == prompt.SourceSeat,
            "The source-card choice must be visible only to the skill owner.");
        var sourceSeat = prompt.SourceSeat!.Value;
        Require(gameWithFeedback.CreateSnapshot(sourceSeat).PendingDecision is null,
            "The damage source must not receive the private Feedback choice.");

        var sourceBefore = gameWithFeedback.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == sourceSeat);
        var takeChoice = prompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("response") == "feedback-source-card");
        var sourceZone = takeChoice.Parameters["source-zone"];
        int cardId;
        CardLocation from;
        if (sourceZone == "hand")
        {
            var slot = int.Parse(takeChoice.Parameters["slot-index"], System.Globalization.CultureInfo.InvariantCulture);
            cardId = sourceBefore.Hand[slot].Id;
            from = CardLocation.Hand(sourceSeat);
            Require(takeChoice.Cards.Count == 0,
                "A hidden source hand choice must expose an opaque slot, not a card id.");
        }
        else
        {
            cardId = takeChoice.Cards.Single();
            from = CardLocation.Equipment(sourceSeat);
        }

        var accepted = gameWithFeedback.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            takeChoice.Id,
            gameWithFeedback.Revision));
        Require(accepted.Accepted, accepted.Error?.Message ?? "Classic Feedback choice was rejected.");
        Require(gameWithFeedback.CardMovements.Any(movement =>
                movement.CardId == cardId &&
                movement.From == from &&
                movement.To == CardLocation.Hand(0) &&
                movement.Reason == CardMoveReasons.FeedbackTakeSourceCard),
            "Classic Feedback must transfer the exact selected source card into the owner's hand.");
        Require(gameWithFeedback.Events.Any(item =>
                item.Payload is DamageSkillCardTakenEvent taken &&
                taken.OwnerSeat == 0 &&
                taken.SourceSeat == sourceSeat &&
                taken.CardId == cardId &&
                taken.From == from),
            "Classic Feedback must publish a typed trusted-host card-taken event.");
        Require(gameWithFeedback.CreateSnapshot(0).Players.Single(player => player.Seat == 0)
                .Hand.Any(card => card.Id == cardId),
            "The Feedback owner must see the acquired physical card.");

        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(gameWithFeedback.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(gameWithFeedback.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(gameWithFeedback)),
            "The formal Feedback choice must restore with identical state and events.");
    }

    public static void FormalJianxiongFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(GameCheckpoint.CurrentRulesVersion >= 16,
            "Formal Jianxiong must have an explicit rules version.");

        var duelContext = new DamageSkillContext(
            new PlayerSkillContext(0, 3, 4, 2, TurnPhase.Play),
            SourceSeat: 1,
            SourceCard: CardKind.Duel,
            SourceCardIsInProcessing: true,
            Amount: 1,
            SourceCardId: 9001,
            TargetSeat: 0);
        var effectMethod = typeof(GameEngine).GetMethod(
            "ResolveDamageSkillEffect",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("Damage-skill effect resolver not found.");
        var currentRules = CreateInteractive(registry, seed: 1);
        var legacyRules = GameReplay.Restore(
            CreateInteractive(registry, seed: 1).CreateCheckpoint() with { RulesVersion = 15 },
            registry);
        var jianxiong = SkillRegistry.Get(SkillKind.Jianxiong);
        Require((DamageSkillEffectKind)effectMethod.Invoke(currentRules, [jianxiong, duelContext])! ==
                DamageSkillEffectKind.ClaimDamageCard &&
                (DamageSkillEffectKind)effectMethod.Invoke(legacyRules, [jianxiong, duelContext])! ==
                DamageSkillEffectKind.None,
            "Rules v16 must accept a Duel damage card while rules v15 retains Slash-only Jianxiong.");

        GameEngine? selectedGame = null;
        PendingDecision? selectedPrompt = null;
        DamageSkillFrame? selectedFrame = null;
        for (var seed = 1; seed <= 8_192 && selectedGame is null; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Classic Jianxiong fixture failed to start.");
            var caoCaoChoice = started.Result.PendingDecision?.Choices.FirstOrDefault(choice =>
                choice.ContentIds.SequenceEqual(["classic:cao-cao"]));
            if (caoCaoChoice is null) continue;

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:cao-cao",
                game.Revision,
                game.PendingDecision!.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Cao Cao selection was rejected.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Classic Jianxiong setup did not advance.");
            var result = advanced.Result;
            for (var step = 0; result.Status != EngineStatus.Completed && step < 4_000; step++)
            {
                var frame = game.ResolutionStack.OfType<DamageSkillFrame>().SingleOrDefault(candidate =>
                    candidate.OwnerSeat == 0 &&
                    candidate.Skill == SkillKind.Jianxiong &&
                    candidate.Effect == DamageSkillEffectKind.ClaimDamageCard);
                if (result.Status == EngineStatus.AwaitingHumanResponse &&
                    result.PendingDecision is { Kind: DecisionKind.Feedback } prompt &&
                    frame is not null &&
                    frame.CardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))
                {
                    selectedGame = game;
                    selectedPrompt = prompt;
                    selectedFrame = frame;
                    break;
                }

                result = DeclineOrAdvance(game, result);
            }
        }

        if (selectedGame is null || selectedPrompt is null || selectedFrame is null)
            throw new InvalidOperationException("No deterministic non-Slash Jianxiong boundary was found.");

        var gameWithJianxiong = selectedGame;
        var promptAtBoundary = selectedPrompt;
        var frameAtBoundary = selectedFrame;
        Require(promptAtBoundary.IsPrivate && promptAtBoundary.PlayerSeat == 0 &&
                promptAtBoundary.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "feedback") &&
                promptAtBoundary.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "take-damage"),
            "Formal Jianxiong must publish private take/skip choices.");
        Require(gameWithJianxiong.CreateSnapshot(1).PendingDecision is null,
            "Other viewers must not receive the private Jianxiong choice.");

        var boundaryCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(gameWithJianxiong.CreateCheckpoint()));
        var claimBranch = GameReplay.Restore(boundaryCheckpoint, registry);
        Require(SnapshotJson.Serialize(claimBranch.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(gameWithJianxiong.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(claimBranch).SequenceEqual(EventSignatures(gameWithJianxiong)),
            "The pending formal Jianxiong choice must restore exactly.");

        var skipChoice = promptAtBoundary.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("response") == "take-damage");
        var skipped = gameWithJianxiong.Submit(new AnswerPromptCommand(
            0,
            promptAtBoundary.PromptId,
            skipChoice.Id,
            gameWithJianxiong.Revision));
        Require(skipped.Accepted, skipped.Error?.Message ?? "Formal Jianxiong skip was rejected.");
        Require(gameWithJianxiong.Events.Select(item => item.Payload)
                .OfType<DamageSkillResolvedEvent>()
                .Any(resolved => resolved.Skill == SkillKind.Jianxiong && !resolved.Used) &&
                !gameWithJianxiong.Events.Select(item => item.Payload)
                    .OfType<DamageCardClaimedEvent>()
                    .Any(claimed => claimed.Skill == SkillKind.Jianxiong &&
                                    claimed.CardId == frameAtBoundary.CardId),
            "Skipping formal Jianxiong must leave the damage card unclaimed.");

        var claimPrompt = claimBranch.PendingDecision ??
            throw new InvalidOperationException("Restored Jianxiong branch lost its prompt.");
        var claimChoice = claimPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("response") == "feedback");
        var claimed = claimBranch.Submit(new AnswerPromptCommand(
            0,
            claimPrompt.PromptId,
            claimChoice.Id,
            claimBranch.Revision));
        Require(claimed.Accepted, claimed.Error?.Message ?? "Formal Jianxiong claim was rejected.");
        Require(claimBranch.CardMovements.Any(movement =>
                movement.CardId == frameAtBoundary.CardId &&
                movement.From == CardLocation.Processing &&
                movement.To == CardLocation.Hand(0) &&
                movement.Reason == CardMoveReasons.JianxiongClaim) &&
                claimBranch.Events.Select(item => item.Payload)
                    .OfType<DamageCardClaimedEvent>()
                    .Any(claim => claim.Skill == SkillKind.Jianxiong &&
                                  claim.CardId == frameAtBoundary.CardId) &&
                claimBranch.CreateSnapshot(0).Players[0].Hand.Any(card =>
                    card.Id == frameAtBoundary.CardId),
            "Formal Jianxiong must move the exact non-Slash damage card into Cao Cao's hand.");

        var claimedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(claimBranch.CreateCheckpoint()));
        var replayedClaim = GameReplay.Restore(claimedCheckpoint, registry);
        Require(SnapshotJson.Serialize(replayedClaim.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(claimBranch.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replayedClaim).SequenceEqual(EventSignatures(claimBranch)),
            "The claimed non-Slash Jianxiong branch must replay exactly.");
    }

    public static void FormalZhihengEquipmentFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(GameCheckpoint.CurrentRulesVersion >= 17,
            "Formal Zhiheng must have an explicit rules version.");

        GameEngine? current = null;
        GameEngine? legacy = null;
        LegalAction? equipmentAction = null;
        for (var seed = 1; seed <= 8_192 && current is null; seed++)
        {
            var candidate = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:sun-quan",
                GameCheckpoint.CurrentRulesVersion);
            var candidateEquipment = candidate?.GetHumanLegalActions()
                .FirstOrDefault(action => action.Kind == LegalActionKind.Equip && action.CardId is not null);
            if (candidate is null || candidateEquipment is null)
            {
                continue;
            }

            current = candidate;
            legacy = StartClassicGeneralAtPlay(registry, seed, "classic:sun-quan", rulesVersion: 16) ??
                throw new InvalidOperationException("The rules-v16 Zhiheng fixture did not reproduce.");
            equipmentAction = candidateEquipment;
        }

        if (current is null || legacy is null || equipmentAction?.CardId is not { } equipmentCardId)
            throw new InvalidOperationException("No deterministic classic Sun Quan equipment fixture was found.");

        Equip(current, equipmentCardId);
        Equip(legacy, equipmentCardId);

        var currentBefore = current.CreateSnapshot(0, revealAll: true);
        var currentPlayerBefore = currentBefore.Players.Single(player => player.Seat == 0);
        var currentPrompt = current.PendingDecision ??
            throw new InvalidOperationException("Current Zhiheng fixture lost its play prompt.");
        var currentAction = current.GetHumanLegalActions().Single(action =>
            action.Kind == LegalActionKind.UseSkill && action.Skill == SkillKind.Zhiheng);
        Require(currentPrompt.ActiveSkillValidCardIds?.Contains(equipmentCardId) == true &&
                currentAction.MaxCardCount == currentPlayerBefore.Hand.Count + currentPlayerBefore.Equipment.Count,
            "Rules v17 Zhiheng must publish hand and owned equipment cards in one private selection contract.");
        var equipmentOnlyView = currentBefore with
        {
            Players = currentBefore.Players.Select(player => player.Seat == 0
                ? player with { HandCount = 0, Hand = Array.Empty<CardSnapshot>() }
                : player).ToArray()
        };
        Require(new SimpleAiBrain(0, seed: 17).ChooseActiveSkillCards(equipmentOnlyView, currentAction)
                .SequenceEqual([equipmentCardId]),
            "Formal Zhiheng AI must be able to select its own equipment when no hand card is available.");

        var currentUsed = current.Submit(new UseSkillCommand(
            0,
            SkillKind.Zhiheng,
            [equipmentCardId],
            [],
            current.Revision,
            currentPrompt.PromptId));
        Require(currentUsed.Accepted, currentUsed.Error?.Message ?? "Equipment Zhiheng was rejected.");
        var currentAfter = current.CreateSnapshot(0, revealAll: true);
        var currentPlayerAfter = currentAfter.Players.Single(player => player.Seat == 0);
        Require(currentPlayerAfter.Equipment.All(card => card.Id != equipmentCardId) &&
                currentPlayerAfter.Hand.Count == currentPlayerBefore.Hand.Count + 1 &&
                !current.GetHumanLegalActions().Any(action =>
                    action.Kind == LegalActionKind.UseSkill && action.Skill == SkillKind.Zhiheng),
            "Rules v17 Zhiheng must discard the equipment, draw one card and enforce once per play phase.");
        Require(current.CardMovements.Count(movement =>
                    movement.CardId == equipmentCardId &&
                    movement.Reason == CardMoveReasons.ZhihengDiscard) == 2 &&
                current.CardMovements.Any(movement =>
                    movement.CardId == equipmentCardId &&
                    movement.From == CardLocation.Equipment(0) &&
                    movement.To == CardLocation.Processing) &&
                current.Events.Select(item => item.Payload)
                    .OfType<SkillCardsDiscardedEvent>()
                    .Any(discarded => discarded.Skill == SkillKind.Zhiheng &&
                                      discarded.CardIds.SequenceEqual([equipmentCardId])),
            "Equipment Zhiheng must retain the exact public source zone, processing move and typed event.");

        var restored = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(current.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(currentAfter) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(current)),
            "Equipment Zhiheng must restore with identical state and events.");

        var legacyBefore = legacy.CreateSnapshot(0, revealAll: true);
        var legacyPlayerBefore = legacyBefore.Players.Single(player => player.Seat == 0);
        var legacyPrompt = legacy.PendingDecision ??
            throw new InvalidOperationException("Legacy Zhiheng fixture lost its play prompt.");
        var legacyAction = legacy.GetHumanLegalActions().Single(action =>
            action.Kind == LegalActionKind.UseSkill && action.Skill == SkillKind.Zhiheng);
        Require(legacyPrompt.ActiveSkillValidCardIds?.Contains(equipmentCardId) == false &&
                legacyAction.MaxCardCount == legacyPlayerBefore.Hand.Count,
            "Rules v16 must retain the hand-only Zhiheng candidate set.");
        var legacyState = SnapshotJson.Serialize(legacyBefore);
        var legacyRejected = legacy.Submit(new UseSkillCommand(
            0,
            SkillKind.Zhiheng,
            [equipmentCardId],
            [],
            legacy.Revision,
            legacyPrompt.PromptId));
        Require(!legacyRejected.Accepted && legacyRejected.Error?.Code == CommandErrorCode.InvalidCard &&
                SnapshotJson.Serialize(legacy.CreateSnapshot(0, revealAll: true)) == legacyState,
            "Rules v16 must reject an equipment Zhiheng selection atomically.");

        var legacyHandCardId = legacyPlayerBefore.Hand.First().Id;
        var legacyUsed = legacy.Submit(new UseSkillCommand(
            0,
            SkillKind.Zhiheng,
            [legacyHandCardId],
            [],
            legacy.Revision,
            legacyPrompt.PromptId));
        Require(legacyUsed.Accepted, legacyUsed.Error?.Message ??
            "Rules-v16 hand-only Zhiheng was rejected.");
        if (legacy.PendingDecision?.Kind != DecisionKind.PlayCard)
        {
            var advanced = legacy.Submit(new AdvanceCommand(legacy.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ??
                "Rules-v16 Zhiheng did not return to the play boundary.");
        }
        Require(legacy.GetHumanLegalActions().Any(action =>
                action.Kind == LegalActionKind.UseSkill && action.Skill == SkillKind.Zhiheng),
            "Rules v16 must retain the historical repeatable hand-only Zhiheng behavior.");
    }

    public static void FormalYingziChoice()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(GameCheckpoint.CurrentRulesVersion >= 21,
            "Formal Yingzi must have an explicit rules version.");

        var skipped = SelectGeneral(registry, "classic:zhou-yu", GameCheckpoint.CurrentRulesVersion);
        var reachedChoice = skipped.Submit(new AdvanceCommand(skipped.Revision));
        Require(reachedChoice.Accepted, reachedChoice.Error?.Message ?? "Could not reach the Yingzi choice.");
        var skipPrompt = skipped.PendingDecision;
        Require(skipPrompt is { Kind: DecisionKind.Yingzi } &&
                skipPrompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                    .OrderBy(action => action, StringComparer.Ordinal)
                    .SequenceEqual(["yingzi-skip", "yingzi-use"]),
            "Rules v21 must publish complete use and skip choices for Yingzi.");
        var initialHandCount = skipped.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0).HandCount;

        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(skipped.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(restored.PendingDecision?.Kind == DecisionKind.Yingzi,
            "A paused Yingzi choice must restore from the command checkpoint.");

        var staleSnapshot = SnapshotJson.Serialize(skipped.CreateSnapshot(0, revealAll: true));
        var rejected = skipped.Submit(new AnswerPromptCommand(
            0,
            skipPrompt!.PromptId,
            new ChoiceId("yingzi.unknown"),
            skipped.Revision));
        Require(!rejected.Accepted && rejected.Error?.Code == CommandErrorCode.InvalidChoice &&
                SnapshotJson.Serialize(skipped.CreateSnapshot(0, revealAll: true)) == staleSnapshot,
            "A forged Yingzi choice must be rejected atomically.");

        var skipChoice = skipPrompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "yingzi-skip");
        var skippedResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipChoice.Id,
            skipped.Revision));
        Require(skippedResult.Accepted && skipped.State.Phase == TurnPhase.Play && skipped.PendingDecision is null,
            skippedResult.Error?.Message ??
            $"Skipping Yingzi did not continue to the play phase (status={skipped.State.Status}, prompt={skipped.PendingDecision?.Kind.ToString() ?? "none"}, phase={skipped.State.Phase}).");
        Require(skipped.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount ==
                initialHandCount + 2,
            "Skipping Yingzi must draw only the normal two cards.");

        var restoredSkip = restored.Submit(new AnswerPromptCommand(
            0,
            restored.PendingDecision!.PromptId,
            restored.PendingDecision.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "yingzi-skip").Id,
            restored.Revision));
        Require(restoredSkip.Accepted &&
                SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(skipped.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(skipped)),
            "A restored Yingzi choice must resolve deterministically.");

        var used = SelectGeneral(registry, "classic:zhou-yu", GameCheckpoint.CurrentRulesVersion);
        Require(used.Submit(new AdvanceCommand(used.Revision)).Accepted,
            "Could not reach the second Yingzi choice.");
        var usePrompt = used.PendingDecision!;
        var beforeUse = used.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount;
        var usedResult = used.Submit(new AnswerPromptCommand(
            0,
            usePrompt.PromptId,
            usePrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "yingzi-use").Id,
            used.Revision));
        Require(usedResult.Accepted &&
                used.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount ==
                beforeUse + 3 &&
                used.Events.Any(envelope => envelope.Payload is DrawSkillResolvedEvent
                {
                    SourceSeat: 0,
                    Skill: SkillKind.Yingzi,
                    Used: true,
                    DrawCount: 3
                }),
            usedResult.Error?.Message ?? "Using Yingzi must draw one extra card and publish its result.");

        var legacy = SelectGeneral(registry, "classic:zhou-yu", rulesVersion: 20);
        var legacyResult = legacy.Submit(new AdvanceCommand(legacy.Revision));
        Require(legacyResult.Accepted && legacy.PendingDecision?.Kind == DecisionKind.PlayCard &&
                legacy.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount == 7 &&
                legacy.Events.All(envelope => envelope.Payload is not DrawSkillResolvedEvent),
            legacyResult.Error?.Message ?? "Rules v20 must retain automatic Yingzi drawing.");
    }

    public static void FormalTianduJudgment()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(GameCheckpoint.CurrentRulesVersion >= 22,
            "Formal Tiandu must have an explicit rules version.");
        var tiandu = SkillRegistry.Get(SkillKind.Tiandu);
        var context = new JudgmentSkillContext(
            new PlayerSkillContext(0, 3, 4, 2, TurnPhase.Draw),
            TargetSeat: 0,
            JudgmentReasons.Lightning,
            JudgmentCardId: 9001,
            JudgmentCardKind: CardKind.Dodge,
            JudgmentSuit: Suit.Heart,
            JudgmentRank: 8);
        Require(tiandu.CanClaimResolvedJudgment(context) &&
                !tiandu.CanClaimResolvedJudgment(context with { TargetSeat = 1 }),
            "Tiandu must only claim its owner's resolved judgment card.");

        GameEngine? current = null;
        JudgmentResolvedEvent? resolvedJudgment = null;
        for (var seed = 1; seed <= 8_192 && current is null; seed++)
        {
            var candidate = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:guo-jia",
                GameCheckpoint.CurrentRulesVersion);
            var lightningAction = candidate?.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Lightning && action.CardId is not null);
            if (candidate is null || lightningAction is null)
            {
                continue;
            }

            var usedLightning = candidate.Submit(new PlayCardCommand(
                0,
                lightningAction.CardId!.Value,
                lightningAction.TargetSeats,
                candidate.Revision,
                candidate.PendingDecision!.PromptId));
            if (!usedLightning.Accepted ||
                !DriveUntilOwnLightningJudgment(candidate, expectTiandu: true, out var candidateJudgment) ||
                candidateJudgment.Succeeded)
            {
                continue;
            }

            current = candidate;
            resolvedJudgment = candidateJudgment;
        }

        var game = current ??
            throw new InvalidOperationException("No deterministic non-lethal Tiandu Lightning fixture was found.");
        var judgment = resolvedJudgment!;
        var prompt = game.PendingDecision;
        Require(prompt is { Kind: DecisionKind.Tiandu, PlayerSeat: 0 } &&
                prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                    .OrderBy(action => action, StringComparer.Ordinal)
                    .SequenceEqual(["tiandu-claim", "tiandu-skip"]),
            "Rules v22 must pause after the judgment result with complete Tiandu choices.");
        Require(game.CardMovements.Any(movement =>
                movement.CardId == judgment.CardId &&
                movement.To == CardLocation.Judgment(0)) &&
                game.CardMovements.All(movement =>
                    movement.CardId != judgment.CardId || movement.To != CardLocation.DiscardPile),
            "The resolved judgment card must remain in the public judgment zone while Tiandu is pending.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var pausedRestore = GameReplay.Restore(pausedCheckpoint, registry);
        Require(pausedRestore.PendingDecision?.Kind == DecisionKind.Tiandu,
            "A paused Tiandu choice must restore from its command checkpoint.");

        var unchanged = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var forged = game.Submit(new AnswerPromptCommand(
            0,
            prompt!.PromptId,
            new ChoiceId("tiandu.unknown"),
            game.Revision));
        Require(!forged.Accepted && forged.Error?.Code == CommandErrorCode.InvalidChoice &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == unchanged,
            "A forged Tiandu choice must be rejected atomically.");

        var claimChoice = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "tiandu-claim");
        var claimed = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            claimChoice.Id,
            game.Revision));
        Require(claimed.Accepted,
            claimed.Error?.Message ?? "Tiandu did not accept the claim choice.");
        Require(game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0)
                .Hand.Any(card => card.Id == judgment.CardId),
            "Tiandu must add the exact resolved judgment card to its owner's hand.");
        Require(game.CardMovements.Any(movement =>
                movement.CardId == judgment.CardId &&
                movement.From == CardLocation.Judgment(0) &&
                movement.To == CardLocation.Hand(0) &&
                movement.Reason == CardMoveReasons.TianduClaim),
            "Tiandu must publish the exact judgment-to-hand card movement.");
        Require(game.Events.Any(envelope => envelope.Payload is JudgmentCardClaimedEvent
        {
            OwnerSeat: 0,
            Skill: SkillKind.Tiandu,
            Used: true
        }),
            "Tiandu must publish its typed claim result event.");
        var claimedReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(claimedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(claimedReplay).SequenceEqual(EventSignatures(game)),
            "The claimed Tiandu branch must replay exactly.");

        var skipPrompt = pausedRestore.PendingDecision!;
        var skipped = pausedRestore.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "tiandu-skip").Id,
            pausedRestore.Revision));
        Require(skipped.Accepted && pausedRestore.CardMovements.Any(movement =>
                movement.CardId == judgment.CardId &&
                movement.From == CardLocation.Judgment(0) &&
                movement.To == CardLocation.DiscardPile &&
                movement.Reason == CardMoveReasons.JudgmentFinish),
            skipped.Error?.Message ?? "Skipping Tiandu did not discard the judgment card normally.");

        var legacyRegistry = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 1, 0));
        GameEngine? legacy = null;
        for (var seed = 1; seed <= 8_192 && legacy is null; seed++)
        {
            var candidate = StartClassicGeneralAtPlay(
                legacyRegistry,
                seed,
                "classic:guo-jia",
                rulesVersion: 21);
            var legacyLightning = candidate?.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Lightning && action.CardId is not null);
            if (candidate is null || legacyLightning is null ||
                !candidate.Submit(new PlayCardCommand(
                    0,
                    legacyLightning.CardId!.Value,
                    legacyLightning.TargetSeats,
                    candidate.Revision,
                    candidate.PendingDecision!.PromptId)).Accepted ||
                !DriveUntilOwnLightningJudgment(candidate, expectTiandu: false, out var legacyJudgment) ||
                candidate.PendingDecision?.Kind == DecisionKind.Tiandu ||
                !candidate.CardMovements.Any(movement =>
                    movement.CardId == legacyJudgment.CardId &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.JudgmentFinish))
            {
                continue;
            }

            legacy = candidate;
        }
        Require(legacy is not null,
            "Rules v21 must retain the historical automatic judgment discard path.");
    }

    public static void FormalFanjianFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(GameCheckpoint.CurrentRulesVersion >= 23,
            "Formal Fanjian must have an explicit rules version.");

        var game = ReachZhouYuPlayPhase(registry, GameCheckpoint.CurrentRulesVersion);
        var playPrompt = game.PendingDecision!;
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Fanjian);
        Require(action.MinCardCount == 0 && action.MaxCardCount == 0 &&
                action.MinTargetCount == 1 && action.MaxTargetCount == 1,
            "Fanjian must publish a target-only active-skill contract.");

        var sourceBefore = game.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 0).HandCount;
        var targetBefore = game.CreateSnapshot(0, revealAll: true)
            .Players.Single(player => player.Seat == 1);
        var used = game.Submit(new UseSkillCommand(
            0,
            SkillKind.Fanjian,
            [],
            [1],
            game.Revision,
            playPrompt.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "Fanjian use was rejected.");

        var suitPrompt = game.CreateSnapshot(1).PendingDecision;
        Require(suitPrompt is { Kind: DecisionKind.Fanjian, PlayerSeat: 1 } &&
                suitPrompt.ValidCardIds.Count == 0 &&
                suitPrompt.Choices.Count == 4 &&
                suitPrompt.Choices.All(choice =>
                    choice.Cards.Count == 0 &&
                    choice.Targets.Count == 0 &&
                    choice.Parameters.GetValueOrDefault("action") == "fanjian-choose-suit") &&
                suitPrompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("suit"))
                    .OrderBy(suit => suit, StringComparer.Ordinal)
                    .SequenceEqual(["Club", "Diamond", "Heart", "Spade"]),
            $"Fanjian must ask the target for four complete suit choices without exposing a source card " +
            $"(kind={suitPrompt?.Kind}, seat={suitPrompt?.PlayerSeat}, valid={suitPrompt?.ValidCardIds.Count}, " +
            $"choices={suitPrompt?.Choices.Count}, suits={string.Join(',', suitPrompt?.Choices.Select(choice => choice.Parameters.GetValueOrDefault("suit")) ?? [])}).");
        Require(game.Events.All(envelope => envelope.Payload is not FanjianCardRevealedEvent) &&
                game.CreateSnapshot(1).Players.Single(player => player.Seat == 0).Hand.Count == 0,
            "The target must not see Zhou Yu's private hand before choosing a suit.");

        var pausedCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restoredPrompt = GameReplay.Restore(pausedCheckpoint, registry);
        Require(restoredPrompt.CreateSnapshot(1).PendingDecision?.Kind == DecisionKind.Fanjian,
            "A paused Fanjian suit choice must restore from its command checkpoint.");

        var unchanged = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var forged = game.Submit(new AnswerPromptCommand(
            1,
            suitPrompt!.PromptId,
            new ChoiceId("fanjian-suit-forged"),
            game.Revision));
        Require(!forged.Accepted && forged.Error?.Code == CommandErrorCode.InvalidChoice &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == unchanged,
            "A forged Fanjian suit choice must be rejected atomically.");

        GameEngine? matching = null;
        GameEngine? mismatching = null;
        FanjianCardRevealedEvent? matchingEvent = null;
        FanjianCardRevealedEvent? mismatchingEvent = null;
        foreach (var suitName in new[] { "Spade", "Heart", "Club", "Diamond" })
        {
            var branch = GameReplay.Restore(pausedCheckpoint, registry);
            var branchPrompt = branch.CreateSnapshot(1).PendingDecision!;
            var choice = branchPrompt.Choices.Single(candidate =>
                candidate.Parameters.GetValueOrDefault("suit") == suitName);
            var answered = branch.Submit(new AnswerPromptCommand(
                1,
                branchPrompt.PromptId,
                choice.Id,
                branch.Revision));
            Require(answered.Accepted, answered.Error?.Message ?? $"Fanjian rejected {suitName}.");
            var revealed = branch.Events.Select(envelope => envelope.Payload)
                .OfType<FanjianCardRevealedEvent>()
                .Last();
            if (revealed.DamageTriggered)
            {
                mismatching ??= branch;
                mismatchingEvent ??= revealed;
            }
            else
            {
                matching ??= branch;
                matchingEvent ??= revealed;
            }
        }

        Require(matching is not null && matchingEvent is not null &&
                mismatching is not null && mismatchingEvent is not null,
            "The four suit branches must contain one matching and three mismatching outcomes for the same random card.");
        var matchingGame = matching!;
        var mismatchingGame = mismatching!;
        var matchedCard = matchingEvent!;
        var mismatchedCard = mismatchingEvent!;
        Require(matchedCard.CardId == mismatchedCard.CardId &&
                matchedCard.CardSuit == mismatchedCard.CardSuit &&
                matchedCard.ChosenSuit == matchedCard.CardSuit &&
                mismatchedCard.ChosenSuit != mismatchedCard.CardSuit,
            "Fanjian must select the same deterministic random card after, not before, the target's suit choice.");
        Require(matchingGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).HandCount ==
                sourceBefore - 1 &&
                matchingGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 1).HandCount ==
                targetBefore.HandCount + 1 &&
                matchingGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 1).Hp ==
                targetBefore.Hp,
            "A matching Fanjian card must transfer to the target without damage.");
        Require(mismatchingGame.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 1).Hp ==
                targetBefore.Hp - 1 &&
                mismatchingGame.Events.Any(envelope => envelope.Payload is DamageAppliedEvent
                {
                    SourceSeat: 0,
                    TargetSeat: 1,
                    Amount: 1,
                    Nature: DamageNature.Normal
                }),
            "A mismatching Fanjian card must cause one point of ordinary damage through the shared damage pipeline.");
        Require(matchingGame.CardMovements.Count(movement =>
                    movement.CardId == matchedCard.CardId &&
                    movement.Reason == CardMoveReasons.FanjianGive) == 2,
            "Fanjian must record the exact hand-to-processing-to-hand transfer.");
        var matchingReturnedToPlay = matchingGame.Submit(new AdvanceCommand(matchingGame.Revision));
        Require(matchingGame.Events.Any(envelope =>
                envelope.Payload is ActiveSkillResolvedEvent
                {
                    Skill: SkillKind.Fanjian
                }) &&
                matchingReturnedToPlay.Accepted &&
                matchingGame.PendingDecision?.Kind == DecisionKind.PlayCard &&
                matchingGame.GetHumanLegalActions().All(candidate => candidate.Skill != SkillKind.Fanjian),
            "Fanjian must complete its active-skill frame and remain limited to once per play phase.");

        for (var step = 0; mismatchingGame.ResolutionStack.Count > 0 && step < 100; step++)
        {
            CommandResult advanced;
            if (mismatchingGame.PendingDecision is
                {
                    Kind: DecisionKind.GangliePunish,
                    PlayerSeat: 0
                } gangliePunishment)
            {
                var loseHp = gangliePunishment.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("response") == "ganglie-lose-hp");
                advanced = mismatchingGame.Submit(new AnswerPromptCommand(
                    0,
                    gangliePunishment.PromptId,
                    loseHp.Id,
                    mismatchingGame.Revision));
            }
            else
            {
                advanced = mismatchingGame.Submit(new AdvanceOneStepCommand(mismatchingGame.Revision));
            }

            Require(advanced.Accepted, advanced.Error?.Message ?? "Fanjian damage continuation did not advance.");
        }
        Require(mismatchingGame.ResolutionStack.Count == 0 &&
                mismatchingGame.Events.Any(envelope =>
                    envelope.Payload is ActiveSkillResolvedEvent
                    {
                        Skill: SkillKind.Fanjian
                    }),
            $"Fanjian damage must close its ordinary damage triggers and active-skill frame " +
            $"(status={mismatchingGame.State.Status}, prompt={mismatchingGame.PendingDecision?.Kind}, " +
            $"seat={mismatchingGame.PendingDecision?.PlayerSeat}, stack={string.Join(',', mismatchingGame.ResolutionStack.Select(frame => frame.Kind))}).");
        var replay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(mismatchingGame.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(mismatchingGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replay).SequenceEqual(EventSignatures(mismatchingGame)),
            "The chosen Fanjian suit, random transfer and damage branch must replay exactly.");

        var legacy = ReachZhouYuPlayPhase(registry, rulesVersion: 22);
        Require(legacy.GetHumanLegalActions().All(candidate => candidate.Skill != SkillKind.Fanjian),
            "Rules v22 must not expose the formal Fanjian active action.");
    }

    public static void FormalGuanxingFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(GameCheckpoint.CurrentRulesVersion >= 24,
            "Formal Guanxing must have an explicit rules version.");

        var game = SelectGeneral(registry, "classic:zhuge-liang", GameCheckpoint.CurrentRulesVersion);
        var reachedOffer = game.Submit(new AdvanceCommand(game.Revision));
        Require(reachedOffer.Accepted, reachedOffer.Error?.Message ?? "Could not reach the Guanxing offer.");
        var offer = game.PendingDecision;
        Require(offer is
        {
            Kind: DecisionKind.Guanxing,
            PlayerSeat: 0,
            IsPrivate: true,
            Choices.Count: 2
        } &&
                offer.Choices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                    .OrderBy(action => action, StringComparer.Ordinal)
                    .SequenceEqual(["guanxing-skip", "guanxing-use"]) &&
                game.CreateSnapshot(1).PendingDecision is null,
            "Guanxing must first publish a private use/skip offer only to its owner.");

        var offerCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restoredOffer = GameReplay.Restore(offerCheckpoint, registry);
        Require(SnapshotJson.Serialize(restoredOffer.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restoredOffer).SequenceEqual(EventSignatures(game)),
            "A paused Guanxing offer must restore exactly.");

        var originalTopTwo = game.CreateCardZoneDiagnostics()
            .Where(card => card.Location == CardLocation.DrawPile)
            .OrderByDescending(card => card.ZoneIndex)
            .Take(2)
            .Select(card => card.CardId)
            .ToArray();
        var skipped = GameReplay.Restore(offerCheckpoint, registry);
        var skipPrompt = skipped.PendingDecision!;
        var skipResult = skipped.Submit(new AnswerPromptCommand(
            0,
            skipPrompt.PromptId,
            skipPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "guanxing-skip").Id,
            skipped.Revision));
        Require(skipResult.Accepted &&
                skipped.Events.Select(envelope => envelope.Payload).OfType<GuanxingResolvedEvent>()
                    .Any(resolved => !resolved.Used && resolved.ViewedCount == 0) &&
                originalTopTwo.All(cardId => skipped.CreateSnapshot(0).Players[0].Hand.Any(card => card.Id == cardId)),
            $"Skipping Guanxing must retain the original top order and continue through the ordinary draw phase. " +
            $"Top={string.Join(',', originalTopTwo)}; hand={string.Join(',', skipped.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id))}; " +
            $"accepted={skipResult.Accepted}.");

        var unchangedSnapshot = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var unchangedDiagnostics = game.CreateCardZoneDiagnostics().ToArray();
        var unchangedCommands = game.AcceptedCommands.Count;
        var forged = game.Submit(new AnswerPromptCommand(
            0,
            offer!.PromptId,
            new ChoiceId("guanxing-forged"),
            game.Revision));
        Require(!forged.Accepted && forged.Error?.Code == CommandErrorCode.InvalidChoice &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == unchangedSnapshot &&
                game.CreateCardZoneDiagnostics().SequenceEqual(unchangedDiagnostics) &&
                game.AcceptedCommands.Count == unchangedCommands,
            "A forged Guanxing offer answer must be rejected without changing state, deck order or journal.");

        var used = game.Submit(new AnswerPromptCommand(
            0,
            offer.PromptId,
            offer.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "guanxing-use").Id,
            game.Revision));
        Require(used.Accepted, used.Error?.Message ?? "Guanxing use was rejected.");
        var topPrompt = game.PendingDecision;
        Require(topPrompt is
        {
            Kind: DecisionKind.Guanxing,
            PlayerSeat: 0,
            IsPrivate: true,
            ValidCardIds.Count: 5,
            Choices.Count: 6
        } &&
                topPrompt.Choices.Count(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "guanxing-finish-top") == 1 &&
                topPrompt.Choices.Where(choice => choice.Cards.Count == 1).All(choice =>
                    choice.Parameters.GetValueOrDefault("stage") == "top" &&
                    choice.Parameters.ContainsKey("card-kind") &&
                    choice.Parameters.ContainsKey("suit") &&
                    choice.Parameters.ContainsKey("rank")) &&
                game.CreateSnapshot(1).PendingDecision is null,
            "Guanxing must privately reveal five exact top cards plus one finish-top action to the owner only.");
        var actualViewedTop = game.CreateCardZoneDiagnostics()
            .Where(card => card.Location == CardLocation.DrawPile)
            .OrderByDescending(card => card.ZoneIndex)
            .Take(5)
            .Select(card => card.CardId)
            .ToArray();
        Require(topPrompt!.ValidCardIds.SequenceEqual(actualViewedTop),
            "The Guanxing prompt must preserve the actual draw-pile top-first order.");

        var pausedOrderingCheckpoint = GameCheckpointJson.Deserialize(
            GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restoredOrdering = GameReplay.Restore(pausedOrderingCheckpoint, registry);
        Require(SnapshotJson.Serialize(restoredOrdering.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                restoredOrdering.CreateCardZoneDiagnostics().SequenceEqual(game.CreateCardZoneDiagnostics()) &&
                EventSignatures(restoredOrdering).SequenceEqual(EventSignatures(game)),
            "A paused private Guanxing card view must restore with identical deck order and events.");

        var aiChoice = new SimpleAiBrain(0, seed: 24).ChooseGuanxing(
            game.CreateSnapshot(0),
            topPrompt.Choices,
            thoughtSequence: 1);
        Require(topPrompt.Choices.Any(choice => choice.Id == aiChoice.Choice) &&
                topPrompt.Choices.Single(choice => choice.Id == aiChoice.Choice).Cards.Count == 1 &&
                aiChoice.Thought.Summary.Contains("观星", StringComparison.Ordinal),
            "Guanxing AI must choose only from its private published card candidates.");

        var chosenTopId = actualViewedTop[^1];
        var selectTop = topPrompt.Choices.Single(choice =>
            choice.Cards.SequenceEqual([chosenTopId]) &&
            choice.Parameters.GetValueOrDefault("action") == "guanxing-top");
        var selectedTop = game.Submit(new AnswerPromptCommand(
            0,
            topPrompt.PromptId,
            selectTop.Id,
            game.Revision));
        Require(selectedTop.Accepted, selectedTop.Error?.Message ?? "Guanxing top-card selection was rejected.");

        var finishPrompt = game.PendingDecision!;
        var finishedTop = game.Submit(new AnswerPromptCommand(
            0,
            finishPrompt.PromptId,
            finishPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "guanxing-finish-top").Id,
            game.Revision));
        Require(finishedTop.Accepted && game.PendingDecision is
        {
            Kind: DecisionKind.Guanxing,
            ValidCardIds.Count: 4
        },
            finishedTop.Error?.Message ?? "Guanxing did not enter bottom ordering.");

        var bottomOrder = actualViewedTop.Where(cardId => cardId != chosenTopId).Reverse().ToArray();
        foreach (var cardId in bottomOrder)
        {
            var bottomPrompt = game.PendingDecision ??
                throw new InvalidOperationException("Guanxing bottom ordering ended early.");
            var bottomChoice = bottomPrompt.Choices.Single(choice =>
                choice.Cards.SequenceEqual([cardId]) &&
                choice.Parameters.GetValueOrDefault("action") == "guanxing-bottom");
            var selectedBottom = game.Submit(new AnswerPromptCommand(
                0,
                bottomPrompt.PromptId,
                bottomChoice.Id,
                game.Revision));
            Require(selectedBottom.Accepted, selectedBottom.Error?.Message ??
                $"Guanxing bottom-card selection {cardId} was rejected.");
        }

        var humanAfter = game.CreateSnapshot(0, revealAll: true).Players[0];
        var bottomDiagnostics = game.CreateCardZoneDiagnostics()
            .Where(card => bottomOrder.Contains(card.CardId))
            .OrderBy(card => card.ZoneIndex)
            .Select(card => card.CardId)
            .ToArray();
        var resolvedEvent = game.Events.Select(envelope => envelope.Payload)
            .OfType<GuanxingResolvedEvent>()
            .Last();
        Require(humanAfter.Hand.Any(card => card.Id == chosenTopId) &&
                bottomDiagnostics.SequenceEqual(bottomOrder) &&
                resolvedEvent is { SourceSeat: 0, Used: true, ViewedCount: 5, TopCount: 1, BottomCount: 4 } &&
                bottomOrder.All(cardId => game.CardMovements.All(movement => movement.CardId != cardId)),
            "Guanxing must make the first top card the next draw, preserve bottom-first order, and expose only public counts.");

        var resolvedReplay = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(resolvedReplay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                resolvedReplay.CreateCardZoneDiagnostics().SequenceEqual(game.CreateCardZoneDiagnostics()) &&
                EventSignatures(resolvedReplay).SequenceEqual(EventSignatures(game)),
            "Guanxing top/bottom ordering and the following draw must replay exactly.");

        var legacy = SelectGeneral(registry, "classic:zhuge-liang", rulesVersion: 23);
        var legacyAdvanced = legacy.Submit(new AdvanceCommand(legacy.Revision));
        Require(legacyAdvanced.Accepted && legacy.PendingDecision?.Kind != DecisionKind.Guanxing &&
                legacy.Events.All(envelope => envelope.Payload is not GuanxingResolvedEvent),
            "Rules v23 must retain the historical turn start without a Guanxing prompt.");
    }

    public static void FormalHujiaFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(GameCheckpoint.CurrentRulesVersion >= 25,
            "Formal Hujia must have an explicit rules version.");

        GameEngine? completed = null;
        HujiaResolvedEvent? completedEvent = null;
        int ownerHpBefore = 0;
        for (var seed = 1; seed <= 8_192 && completed is null; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Classic Hujia fixture failed to start.");
            if (started.Result.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:cao-cao"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:cao-cao",
                game.Revision,
                game.PendingDecision!.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Classic Cao Cao selection was rejected.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Classic Hujia setup did not advance.");

            PendingDecision? ownerPrompt = null;
            for (var step = 0; game.State.Status != EngineStatus.Completed && step < 4_000; step++)
            {
                var prompt = game.PendingDecision;
                if (prompt is { Kind: DecisionKind.RespondDodge } &&
                    prompt.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "hujia-request"))
                {
                    ownerPrompt = prompt;
                    break;
                }

                DeclineOrAdvance(game);
            }

            if (ownerPrompt is null)
            {
                continue;
            }

            var boundaryState = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
            var boundaryRevision = game.Revision;
            var forged = game.Submit(new AnswerPromptCommand(
                0,
                ownerPrompt.PromptId,
                new ChoiceId("hujia.forged"),
                game.Revision));
            Require(!forged.Accepted && game.Revision == boundaryRevision &&
                    SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == boundaryState,
                "A forged Hujia choice must be rejected atomically.");

            ownerHpBefore = game.CreateSnapshot(0, revealAll: true).Players[0].Hp;
            var hujiaChoice = ownerPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("response") == "hujia-request");
            var requested = game.Submit(new AnswerPromptCommand(
                0,
                ownerPrompt.PromptId,
                hujiaChoice.Id,
                game.Revision));
            Require(requested.Accepted, requested.Error?.Message ?? "Hujia request was rejected.");

            var providerSeats = Enumerable.Range(1, game.PlayerCount)
                .Select(seat => seat % game.PlayerCount)
                .Where(seat => game.CreateSnapshot(seat).PendingDecision?.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") is "hujia-dodge" or "hujia-bagua") == true)
                .ToArray();
            if (providerSeats.Length != 1)
            {
                continue;
            }

            var providerSeat = providerSeats[0];
            Require(game.CreateSnapshot(0).PendingDecision is null &&
                    Enumerable.Range(0, game.PlayerCount)
                        .Where(seat => seat != providerSeat)
                        .All(seat => game.CreateSnapshot(seat).PendingDecision is null),
                "The Hujia provider prompt must remain private to exactly one Wei responder.");
            var pausedCheckpoint = GameCheckpointJson.Deserialize(
                GameCheckpointJson.Serialize(game.CreateCheckpoint()));
            var restoredPaused = GameReplay.Restore(pausedCheckpoint, registry);
            Require(SnapshotJson.Serialize(restoredPaused.CreateSnapshot(providerSeat, revealAll: true)) ==
                    SnapshotJson.Serialize(game.CreateSnapshot(providerSeat, revealAll: true)) &&
                    EventSignatures(restoredPaused).SequenceEqual(EventSignatures(game)),
                "The paused private Hujia provider prompt must replay exactly.");

            var eventCount = game.Events.Count;
            for (var step = 0; step < 32 && game.State.Status != EngineStatus.Completed; step++)
            {
                var resolved = game.Events.Skip(eventCount).Select(envelope => envelope.Payload)
                    .OfType<HujiaResolvedEvent>()
                    .LastOrDefault();
                if (resolved is not null)
                {
                    if (resolved is { Succeeded: true, ResponseCardId: not null })
                    {
                        completed = game;
                        completedEvent = resolved;
                    }
                    break;
                }

                if (game.PendingDecision is not null)
                {
                    break;
                }

                var stepResult = game.Submit(new AdvanceOneStepCommand(game.Revision));
                Require(stepResult.Accepted, stepResult.Error?.Message ?? "Hujia AI responder did not advance.");
            }
        }

        if (completed is null || completedEvent is null || completedEvent.ResponseCardId is not { } responseCardId)
        {
            throw new InvalidOperationException("No deterministic physical-Dodge Hujia boundary was found.");
        }

        Require(completedEvent.OwnerSeat == 0 &&
                completedEvent.ProviderSeat is { } provider &&
                completed.CreateSnapshot(0, revealAll: true).Players[0].Hp == ownerHpBefore &&
                completed.CardMovements.Any(movement =>
                    movement.CardId == responseCardId &&
                    movement.From == CardLocation.Hand(provider) &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.Respond) &&
                completed.CardMovements.Any(movement =>
                    movement.CardId == responseCardId &&
                    movement.From == CardLocation.Processing &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.ResponseFinished) &&
                completed.Events.Select(envelope => envelope.Payload)
                    .OfType<CardRespondedEvent>()
                    .Any(response => response.CardId == responseCardId && response.ResponderSeat == 0),
            "Hujia must spend the provider's exact physical Dodge while publishing the effective response as Cao Cao's.");

        var replayed = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(completed.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(replayed.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(completed.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(replayed).SequenceEqual(EventSignatures(completed)),
            "The resolved physical-Dodge Hujia branch must replay exactly.");
    }

    public static void FormalHujiaBaguaFallback()
    {
        const string modeId = "identity:classic-hujia-bagua-test";
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new SyntheticPackage(
                "hujia-bagua-test",
                builder =>
                {
                    builder.AddDeck(new ContentDeckRecipe(
                        "test:hujia-bagua-deck",
                        "护驾八卦测试牌堆",
                        InitialHandSize: 4,
                        DrawPerTurn: 2,
                        Cards:
                        [
                            new ContentDeckCardCount("standard:bagua", 20),
                            new ContentDeckCardCount("standard:slash", 50),
                            new ContentDeckCardCount("standard:peach", 20)
                        ]));
                    builder.AddMode(new ContentModeDefinition(
                        modeId,
                        "护驾八卦测试身份局",
                        MinPlayers: 5,
                        MaxPlayers: 5,
                        RoleCounts: new Dictionary<string, int>
                        {
                            [nameof(Role.Lord)] = 1,
                            [nameof(Role.Loyalist)] = 1,
                            [nameof(Role.Rebel)] = 2,
                            [nameof(Role.Renegade)] = 1
                        },
                        DeckId: "test:hujia-bagua-deck",
                        GeneralCandidateCount: 1,
                        GeneralPoolIds:
                        [
                            "classic:cao-cao",
                            "classic:xiahou-dun",
                            "standard:cao-cao",
                            "standard:guo-jia",
                            "standard:xun-yu"
                        ]));
                },
                new PackageDependency("standard-classic-generals", new Version(1, 4, 0))));

        GameEngine? failedBagua = null;
        JudgmentResolvedEvent? failedJudgment = null;
        int ownerSeat = -1;
        int ownerHpBefore = -1;
        for (var seed = 1; seed <= 4_096 && failedBagua is null; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = 1,
                HumanRole = Role.Loyalist,
                ModeId = modeId,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 120
            }, registry);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Hujia Bagua fixture failed to start.");
            var full = game.CreateSnapshot(1, revealAll: true);
            var lord = full.Players.Single(player => player.Role == Role.Lord);
            if (lord.GeneralId != "classic:cao-cao" || full.Players[1].GeneralId == "classic:cao-cao")
            {
                continue;
            }

            var equippedBagua = false;
            for (var step = 0; step < 2_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var prompt = game.PendingDecision;
                if (prompt is { Kind: DecisionKind.RespondDodge } &&
                    prompt.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "hujia-bagua"))
                {
                    var beforeEvents = game.Events.Count;
                    ownerSeat = prompt.TargetSeat ?? lord.Seat;
                    ownerHpBefore = game.CreateSnapshot(1, revealAll: true).Players[ownerSeat].Hp;
                    var bagua = prompt.Choices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "hujia-bagua");
                    var answered = game.Submit(new AnswerPromptCommand(
                        1,
                        prompt.PromptId,
                        bagua.Id,
                        game.Revision));
                    Require(answered.Accepted, answered.Error?.Message ?? "Hujia Bagua response was rejected.");
                    var judgment = game.Events.Skip(beforeEvents).Select(envelope => envelope.Payload)
                        .OfType<JudgmentResolvedEvent>()
                        .LastOrDefault(item => item.TargetSeat == 1 && item.Reason == JudgmentReasons.BaguaDefense);
                    if (judgment is { Succeeded: false })
                    {
                        failedBagua = game;
                        failedJudgment = judgment;
                    }
                    break;
                }

                GameCommand command;
                if (prompt is null)
                {
                    command = new AdvanceOneStepCommand(game.Revision);
                }
                else if (prompt.Kind == DecisionKind.PlayCard)
                {
                    var baguaAction = game.GetHumanLegalActions().FirstOrDefault(action =>
                        action.Kind == LegalActionKind.Equip &&
                        action.CardId is { } cardId &&
                        game.CreateSnapshot(1).Players[1].Hand.Single(card => card.Id == cardId).Kind ==
                        CardKind.BaguaFormation);
                    if (!equippedBagua && baguaAction is not null)
                    {
                        command = new PlayCardCommand(
                            1,
                            baguaAction.CardId!.Value,
                            baguaAction.TargetSeats,
                            game.Revision,
                            prompt.PromptId);
                        equippedBagua = true;
                    }
                    else
                    {
                        command = new EndPlayPhaseCommand(1, game.Revision, prompt.PromptId);
                    }
                }
                else if (prompt.Kind == DecisionKind.DiscardCards)
                {
                    command = new DiscardCardsCommand(
                        1,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId,
                        game.Revision);
                }
                else
                {
                    var decline = prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.Values.Any(value =>
                            value.StartsWith("skip", StringComparison.Ordinal) ||
                            value is "take-damage" or "no-nullification" or "ganglie-lose-hp")) ??
                        prompt.Choices.First();
                    command = new AnswerPromptCommand(1, prompt.PromptId, decline.Id, game.Revision);
                }

                var accepted = game.Submit(command);
                if (!accepted.Accepted)
                {
                    break;
                }
            }
        }

        if (failedBagua is null || failedJudgment is null)
        {
            throw new InvalidOperationException("No deterministic failed Hujia Bagua judgment was found.");
        }

        Require(failedJudgment.Succeeded == false &&
                failedBagua.CreateSnapshot(1, revealAll: true).Players[ownerSeat].Hp == ownerHpBefore &&
                failedBagua.ResolutionStack.OfType<ResponseWindowFrame>().Any(frame =>
                    frame.ResponderSeat == ownerSeat && frame.RequiredCardKind == CardKind.Dodge) &&
                failedBagua.Events.Select(envelope => envelope.Payload)
                    .OfType<HujiaResolvedEvent>()
                    .All(resolved => !resolved.Succeeded),
            "A failed allied Bagua judgment must keep Cao Cao unharmed and continue the original Dodge response window.");

        var replayed = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(failedBagua.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(replayed.CreateSnapshot(1, revealAll: true)) ==
                SnapshotJson.Serialize(failedBagua.CreateSnapshot(1, revealAll: true)) &&
                EventSignatures(replayed).SequenceEqual(EventSignatures(failedBagua)),
            "The failed Hujia Bagua continuation must replay exactly.");
    }

    public static void FormalJijiangActiveFlow()
    {
        var (registry, modeId) = CreateJijiangFixtureRegistry(
            "active",
            [new ContentDeckCardCount("standard:slash", 100)]);
        GameEngine? game = null;
        LegalAction? jijiang = null;
        int targetSeat = -1;
        for (var seed = 1; seed <= 256 && game is null; seed++)
        {
            var candidate = StartJijiangLordAtPlay(registry, modeId, seed);
            var full = candidate.CreateSnapshot(0, revealAll: true);
            var action = candidate.GetHumanLegalActions().Single(item =>
                item.Kind == LegalActionKind.UseSkill && item.Skill == SkillKind.Jijiang);
            var rebelTarget = action.SelectableTargetSeats.FirstOrDefault(seat =>
                full.Players[seat].Role == Role.Rebel, -1);
            if (rebelTarget < 0)
            {
                continue;
            }

            game = candidate;
            jijiang = action;
            targetSeat = rebelTarget;
        }

        if (game is null || jijiang is null)
        {
            throw new InvalidOperationException("No deterministic active Jijiang fixture exposed an in-range Rebel.");
        }

        var lord = game.CreateSnapshot(0, revealAll: true).Players[0];
        Require(lord.MaxHp == 5 &&
                lord.Skills!.Select(skill => skill.Kind).SequenceEqual([SkillKind.Rende, SkillKind.Jijiang]),
            "Classic Liu Bei must combine the Lord HP bonus with Rende and Jijiang in stable order.");
        Require(game.GetHumanLegalActions().Where(action => action.Kind == LegalActionKind.UseSkill)
                .Select(action => action.Skill)
                .SequenceEqual([SkillKind.Rende, SkillKind.Jijiang]) &&
                jijiang.MinCardCount == 0 && jijiang.MaxCardCount == 0 &&
                jijiang.MinTargetCount == 1 && jijiang.MaxTargetCount == 1 &&
                jijiang.SelectableTargetSeats.Contains(targetSeat),
            "The play boundary must publish Rende and Jijiang as distinct typed active actions.");

        var prompt = game.PendingDecision!;
        var beforeForgery = SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
        var beforeForgeryRevision = game.Revision;
        var forged = game.Submit(new UseSkillCommand(
            0,
            SkillKind.Jijiang,
            [],
            [0],
            game.Revision,
            prompt.PromptId));
        Require(!forged.Accepted && game.Revision == beforeForgeryRevision &&
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == beforeForgery,
            "A forged active Jijiang target must be rejected atomically.");

        var requested = game.Submit(new UseSkillCommand(
            0,
            SkillKind.Jijiang,
            [],
            [targetSeat],
            game.Revision,
            prompt.PromptId));
        Require(requested.Accepted, requested.Error?.Message ?? "Active Jijiang was rejected.");
        var providerPrompts = Enumerable.Range(0, game.PlayerCount)
            .Select(seat => game.CreateSnapshot(seat).PendingDecision)
            .Where(decision => decision?.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("response") == "jijiang-slash") == true)
            .Cast<PendingDecision>()
            .ToArray();
        Require(providerPrompts is [{ Kind: DecisionKind.RespondSlash }],
            "Active Jijiang must pause at one private Shu provider prompt.");
        var providerPrompt = providerPrompts[0];
        var providerSeat = providerPrompt.PlayerSeat;
        Require(game.CreateSnapshot(providerSeat).PendingDecision is not null &&
                Enumerable.Range(0, game.PlayerCount)
                    .Where(seat => seat != providerSeat)
                    .All(seat => game.CreateSnapshot(seat).PendingDecision is null),
            "The active Jijiang provider prompt must be private to its current Shu candidate.");

        var paused = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(paused.CreateSnapshot(providerSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(providerSeat, revealAll: true)) &&
                EventSignatures(paused).SequenceEqual(EventSignatures(game)),
            "A paused active Jijiang provider prompt must replay exactly.");

        JijiangResolvedEvent? resolved = null;
        for (var step = 0; step < 16 && resolved is null; step++)
        {
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The Jijiang provider cursor did not advance.");
            resolved = game.Events.Select(envelope => envelope.Payload)
                .OfType<JijiangResolvedEvent>()
                .LastOrDefault(item => item is { IsActiveUse: true, Succeeded: true });
        }

        if (resolved is not { ProviderSeat: { } successfulProvider, SlashCardId: { } slashCardId })
        {
            throw new InvalidOperationException("No allied Shu provider completed active Jijiang.");
        }

        Require(resolved.OwnerSeat == 0 && resolved.TargetSeat == targetSeat &&
                resolved.EffectiveSlashKind == CardKind.Slash &&
                game.CardMovements.Any(movement =>
                    movement.CardId == slashCardId &&
                    movement.From == CardLocation.Hand(successfulProvider) &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.Use) &&
                game.CardMovements.Any(movement =>
                    movement.CardId == slashCardId &&
                    movement.From == CardLocation.Processing &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.UseFinished) &&
                game.Events.Select(envelope => envelope.Payload).OfType<CardUsedEvent>().Any(cardUse =>
                    cardUse.CardId == slashCardId && cardUse.SourceSeat == 0 && cardUse.TargetSeat == targetSeat),
            "Active Jijiang must spend the provider's exact Slash while making Liu Bei the effective user.");

        var returned = game.Submit(new AdvanceCommand(game.Revision));
        Require(returned.Accepted && game.PendingDecision?.Kind == DecisionKind.PlayCard &&
                game.GetHumanLegalActions().All(action => action.Skill != SkillKind.Jijiang),
            "A successful active Jijiang Slash must consume Liu Bei's Slash allowance for the turn.");

        var (failureRegistry, failureModeId) = CreateJijiangFixtureRegistry(
            "active-failure",
            [new ContentDeckCardCount("standard:peach", 100)]);
        var failed = StartJijiangLordAtPlay(failureRegistry, failureModeId, seed: 1);
        var failedAction = failed.GetHumanLegalActions().Single(action =>
            action.Kind == LegalActionKind.UseSkill && action.Skill == SkillKind.Jijiang);
        var failedTarget = failedAction.SelectableTargetSeats[0];
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var failedPrompt = failed.PendingDecision!;
            var result = failed.Submit(new UseSkillCommand(
                0,
                SkillKind.Jijiang,
                [],
                [failedTarget],
                failed.Revision,
                failedPrompt.PromptId));
            Require(result.Accepted, result.Error?.Message ?? "A failed Jijiang attempt was rejected before resolution.");
            Require(failed.Events.Select(envelope => envelope.Payload).OfType<JijiangResolvedEvent>()
                    .Count(item => item is { IsActiveUse: true, Succeeded: false }) == attempt,
                "An all-decline active Jijiang attempt must publish one typed failure result.");
            var resumed = failed.Submit(new AdvanceCommand(failed.Revision));
            Require(resumed.Accepted && failed.PendingDecision?.Kind == DecisionKind.PlayCard &&
                    failed.GetHumanLegalActions().Any(action => action.Skill == SkillKind.Jijiang),
                "A failed human Jijiang attempt must not consume the Slash limit and must remain retryable.");
        }
    }

    public static void FormalJijiangResponseFlow()
    {
        var (registry, modeId) = CreateJijiangFixtureRegistry(
            "response",
            [
                new ContentDeckCardCount("standard:slash", 60),
                new ContentDeckCardCount("standard:barbarian_assault", 40)
            ]);
        GameEngine? selectedGame = null;
        PendingDecision? selectedPrompt = null;
        JijiangRequestedEvent? selectedRequest = null;
        for (var seed = 1; seed <= 512 && selectedGame is null; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                HumanSeat = 1,
                HumanRole = Role.Loyalist,
                ModeId = modeId,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 80
            }, registry);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Jijiang response fixture failed to start.");
            var full = game.CreateSnapshot(1, revealAll: true);
            if (full.Players.Single(player => player.Role == Role.Lord).GeneralId != "classic:liu-bei")
            {
                continue;
            }

            for (var step = 0; step < 2_000 && game.State.Status != EngineStatus.Completed; step++)
            {
                var decision = game.PendingDecision;
                if (decision?.PlayerSeat == 1 &&
                    decision.Kind == DecisionKind.RespondSlash &&
                    decision.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "jijiang-slash"))
                {
                    var request = game.Events.Select(envelope => envelope.Payload)
                        .OfType<JijiangRequestedEvent>()
                        .Last();
                    if (!request.IsActiveUse)
                    {
                        selectedGame = game;
                        selectedPrompt = decision;
                        selectedRequest = request;
                        break;
                    }
                }

                GameCommand command;
                if (decision is null || decision.PlayerSeat != 1)
                {
                    command = new AdvanceOneStepCommand(game.Revision);
                }
                else if (decision.Kind == DecisionKind.PlayCard)
                {
                    command = new EndPlayPhaseCommand(1, game.Revision, decision.PromptId);
                }
                else if (decision.Kind == DecisionKind.DiscardCards)
                {
                    command = new DiscardCardsCommand(
                        1,
                        decision.ValidCardIds.Take(decision.RequiredCardCount).ToArray(),
                        decision.PromptId,
                        game.Revision);
                }
                else
                {
                    command = new AnswerPromptCommand(
                        1,
                        decision.PromptId,
                        DeclineChoice(decision).Id,
                        game.Revision);
                }

                var advanced = game.Submit(command);
                if (!advanced.Accepted)
                {
                    break;
                }
            }
        }

        if (selectedGame is null || selectedPrompt is null || selectedRequest is null)
        {
            throw new InvalidOperationException("No deterministic response Jijiang provider boundary was found.");
        }

        var gameWithResponse = selectedGame;
        var prompt = selectedPrompt;
        var requestEvent = selectedRequest;
        var ownerSeat = requestEvent.OwnerSeat;
        Require(prompt.IsPrivate && prompt.TargetSeat == ownerSeat && prompt.SourceSeat == ownerSeat &&
                gameWithResponse.CreateSnapshot(ownerSeat).PendingDecision is null &&
                Enumerable.Range(0, gameWithResponse.PlayerCount)
                    .Where(seat => seat != 1)
                    .All(seat => gameWithResponse.CreateSnapshot(seat).PendingDecision is null),
            "A response Jijiang prompt must be private to exactly one Shu provider.");

        var beforeForgery = SnapshotJson.Serialize(gameWithResponse.CreateSnapshot(1, revealAll: true));
        var beforeForgeryRevision = gameWithResponse.Revision;
        var forged = gameWithResponse.Submit(new AnswerPromptCommand(
            1,
            prompt.PromptId,
            new ChoiceId("jijiang.forged"),
            gameWithResponse.Revision));
        Require(!forged.Accepted && gameWithResponse.Revision == beforeForgeryRevision &&
                SnapshotJson.Serialize(gameWithResponse.CreateSnapshot(1, revealAll: true)) == beforeForgery,
            "A forged Jijiang provider choice must be rejected atomically.");

        var paused = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(gameWithResponse.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(paused.CreateSnapshot(1, revealAll: true)) ==
                SnapshotJson.Serialize(gameWithResponse.CreateSnapshot(1, revealAll: true)) &&
                EventSignatures(paused).SequenceEqual(EventSignatures(gameWithResponse)),
            "A paused response Jijiang provider prompt must replay exactly.");

        var slashChoice = prompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("response") == "jijiang-slash");
        var slashCardId = slashChoice.Cards.Single();
        var answered = gameWithResponse.Submit(new AnswerPromptCommand(
            1,
            prompt.PromptId,
            slashChoice.Id,
            gameWithResponse.Revision));
        Require(answered.Accepted, answered.Error?.Message ?? "The Jijiang Slash response was rejected.");
        var resolved = gameWithResponse.Events.Select(envelope => envelope.Payload)
            .OfType<JijiangResolvedEvent>()
            .Last(item => item.ResolutionId == requestEvent.ResolutionId);
        Require(resolved is { Succeeded: true, IsActiveUse: false, ProviderSeat: 1 } &&
                resolved.OwnerSeat == ownerSeat && resolved.SlashCardId == slashCardId &&
                resolved.EffectiveSlashKind == CardKind.Slash &&
                gameWithResponse.CardMovements.Any(movement =>
                    movement.CardId == slashCardId &&
                    movement.From == CardLocation.Hand(1) &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.Respond) &&
                gameWithResponse.CardMovements.Any(movement =>
                    movement.CardId == slashCardId &&
                    movement.From == CardLocation.Processing &&
                    movement.To == CardLocation.DiscardPile &&
                    movement.Reason == CardMoveReasons.ResponseFinished) &&
                gameWithResponse.Events.Select(envelope => envelope.Payload).OfType<CardRespondedEvent>().Any(response =>
                    response.CardId == slashCardId && response.ResponderSeat == ownerSeat &&
                    response.EffectiveCardKind == CardKind.Slash),
            "Response Jijiang must spend the provider's exact Slash while publishing Liu Bei as the responder.");

        var replayed = GameReplay.Restore(
            GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(gameWithResponse.CreateCheckpoint())),
            registry);
        Require(SnapshotJson.Serialize(replayed.CreateSnapshot(1, revealAll: true)) ==
                SnapshotJson.Serialize(gameWithResponse.CreateSnapshot(1, revealAll: true)) &&
                EventSignatures(replayed).SequenceEqual(EventSignatures(gameWithResponse)),
            "A completed response Jijiang branch must replay exactly.");
    }

    private static (ContentRegistry Registry, string ModeId) CreateJijiangFixtureRegistry(
        string suffix,
        IReadOnlyList<ContentDeckCardCount> cards)
    {
        var modeId = $"identity:classic-jijiang-{suffix}-test";
        var deckId = $"test:jijiang-{suffix}-deck";
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new SyntheticPackage(
                $"jijiang-{suffix}-test",
                builder =>
                {
                    builder.AddDeck(new ContentDeckRecipe(
                        deckId,
                        $"激将{suffix}测试牌堆",
                        InitialHandSize: 4,
                        DrawPerTurn: 2,
                        Cards: cards));
                    builder.AddMode(new ContentModeDefinition(
                        modeId,
                        $"激将{suffix}测试身份局",
                        MinPlayers: 5,
                        MaxPlayers: 5,
                        RoleCounts: new Dictionary<string, int>
                        {
                            [nameof(Role.Lord)] = 1,
                            [nameof(Role.Loyalist)] = 1,
                            [nameof(Role.Rebel)] = 2,
                            [nameof(Role.Renegade)] = 1
                        },
                        DeckId: deckId,
                        GeneralCandidateCount: 5,
                        GeneralPoolIds:
                        [
                            "classic:liu-bei",
                            "standard:zhang-fei",
                            "standard:liu-bei",
                            "standard:zhuge-liang",
                            "classic:zhuge-liang"
                        ]));
                },
                new PackageDependency("standard-classic-generals", new Version(1, 5, 0))));
        return (registry, modeId);
    }

    private static GameEngine StartJijiangLordAtPlay(
        ContentRegistry registry,
        string modeId,
        int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = modeId,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 80
        }, registry);
        var started = game.Submit(new StartGameCommand());
        Require(started.Accepted && game.PendingDecision?.Choices.Any(choice =>
                choice.ContentIds.SequenceEqual(["classic:liu-bei"])) == true,
            started.Error?.Message ?? "The Jijiang fixture did not offer classic Liu Bei.");
        var selected = game.Submit(new SelectGeneralCommand(
            0,
            "classic:liu-bei",
            game.Revision,
            game.PendingDecision!.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Classic Liu Bei selection was rejected.");
        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted && game.PendingDecision?.Kind == DecisionKind.PlayCard,
            advanced.Error?.Message ?? "The Jijiang fixture did not reach Liu Bei's play phase.");
        return game;
    }

    public static void FormalGuoseAndLiuliFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        GameEngine? guoseGame = null;
        LegalAction? guoseAction = null;
        for (var seed = 1; seed <= 8_192 && guoseAction is null; seed++)
        {
            var candidate = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:da-qiao",
                GameCheckpoint.CurrentRulesVersion);
            guoseAction = candidate?.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Indulgence &&
                action.PlayedCardKind == CardKind.Indulgence &&
                action.CardId is { } cardId &&
                candidate.CreateSnapshot(0, revealAll: true).Players[0].Hand
                    .Concat(candidate.CreateSnapshot(0, revealAll: true).Players[0].Equipment)
                    .Any(card => card.Id == cardId && card.Suit == Suit.Diamond && card.Kind != CardKind.Indulgence));
            if (guoseAction is not null)
            {
                guoseGame = candidate;
            }
        }

        Require(guoseGame is not null && guoseAction is not null,
            "Could not find a deterministic Da Qiao Guose fixture.");
        var activeGuoseGame = guoseGame ?? throw new InvalidOperationException("Guose game missing.");
        var activeGuoseAction = guoseAction ?? throw new InvalidOperationException("Guose action missing.");
        var beforeGuose = activeGuoseGame.CreateCheckpoint();
        var physicalCard = activeGuoseGame.CreateSnapshot(0, revealAll: true).Players[0].Hand
            .Concat(activeGuoseGame.CreateSnapshot(0, revealAll: true).Players[0].Equipment)
            .Single(card => card.Id == activeGuoseAction.CardId);
        var used = activeGuoseGame.Submit(new PlayCardCommand(
            0,
            activeGuoseAction.CardId!.Value,
            activeGuoseAction.TargetSeats,
            activeGuoseGame.Revision,
            activeGuoseGame.PendingDecision!.PromptId,
            activeGuoseAction.PlayedCardKind));
        Require(used.Accepted &&
                physicalCard.Suit == Suit.Diamond &&
                activeGuoseGame.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>().Any(item =>
                    item.CardId == physicalCard.Id && item.CardKind == CardKind.Indulgence) &&
                activeGuoseGame.CardMovements.Any(item =>
                    item.CardId == physicalCard.Id && item.To == CardLocation.Processing),
            used.Error?.Message ?? "Guose must retain the diamond physical card while declaring Indulgence.");
        var legacy = GameReplay.Restore(beforeGuose with { RulesVersion = 54 }, registry);
        Require(legacy.GetHumanLegalActions().All(action =>
                action.PlayedCardKind != CardKind.Indulgence || action.CardId != physicalCard.Id),
            "Rules v54 must not expose Guose conversions from the same checkpoint.");

        var liuliGame = FindDaQiaoLiuliFixture(registry);
        var prompt = liuliGame.PendingDecision!;
        var redirect = prompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "liuli-use");
        var oldTarget = prompt.PlayerSeat;
        var answered = liuliGame.Submit(new AnswerPromptCommand(
            oldTarget,
            prompt.PromptId,
            redirect.Id,
            liuliGame.Revision));
        Require(answered.Accepted &&
                liuliGame.Events.Select(item => item.Payload).OfType<LiuliRedirectedEvent>().Any(item =>
                    item.OriginalTargetSeat == oldTarget &&
                    item.NewTargetSeat == redirect.Targets.Single() &&
                    item.DiscardedCardId == redirect.Cards.Single()) &&
                liuliGame.CardMovements.Any(item =>
                    item.CardId == redirect.Cards.Single() &&
                    item.Reason == CardMoveReasons.LiuliDiscard &&
                    item.To == CardLocation.DiscardPile),
            answered.Error?.Message ?? "Liuli must discard the exact published card and redirect the same Slash.");
        var restored = GameReplay.Restore(liuliGame.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(liuliGame.CreateSnapshot(0, revealAll: true)) &&
                EventSignatures(restored).SequenceEqual(EventSignatures(liuliGame)),
            "The resolved Liuli branch must replay exactly.");
    }

    public static void FormalLijianAndBiyueFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        GameEngine? game = null;
        LegalAction? action = null;
        for (var seed = 1; seed <= 4_096 && action is null; seed++)
        {
            var candidate = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:diao-chan",
                GameCheckpoint.CurrentRulesVersion);
            action = candidate?.GetHumanLegalActions().SingleOrDefault(item =>
                item.Kind == LegalActionKind.UseSkill && item.Skill == SkillKind.Lijian);
            if (candidate is not null && action is not null &&
                !action.SelectableTargetSeats.Any(seat =>
                    candidate.CreateSnapshot(0, revealAll: true).Players
                        .Single(player => player.Seat == seat).Hand.Any(card => card.Kind == CardKind.Slash)))
            {
                action = null;
            }
            if (action is not null) game = candidate;
        }

        Require(game is not null && action is not null,
            "Could not find a deterministic Diao Chan Lijian fixture.");
        var active = game!;
        var lijian = action!;
        var cost = lijian.SelectableCardIds.First();
        var full = active.CreateSnapshot(0, revealAll: true);
        var responder = lijian.SelectableTargetSeats.First(seat =>
            full.Players.Single(player => player.Seat == seat).Hand.Any(card => card.Kind == CardKind.Slash));
        var source = lijian.SelectableTargetSeats.First(seat => seat != responder);
        var targets = new[] { source, responder };
        Require(targets.Length == 2 && targets.All(seat =>
                registry.Generals[active.CreateSnapshot(0, revealAll: true).Players
                    .Single(player => player.Seat == seat).GeneralId!].Gender == GeneralGender.Male),
            "Lijian must publish exactly male target candidates.");
        var used = active.Submit(new UseSkillCommand(
            0,
            SkillKind.Lijian,
            [cost],
            targets,
            active.Revision,
            active.PendingDecision!.PromptId));
        Require(used.Accepted &&
                active.CardMovements.Any(move =>
                    move.CardId == cost && move.Reason == CardMoveReasons.LijianDiscard &&
                    move.To == CardLocation.DiscardPile) &&
                active.ResolutionStack.OfType<ActiveSkillFrame>().Any(frame =>
                    frame.Skill == SkillKind.Lijian &&
                    frame.Effect == ActiveSkillEffectKind.DiscardAndStartDuel) &&
                active.ResolutionStack.LastOrDefault() is ResponseWindowFrame
                {
                    IncomingCard: CardKind.Duel,
                    RequiredCardKind: CardKind.Slash
                },
            used.Error?.Message ?? $"Lijian must discard its exact cost and open a virtual Duel without Nullification " +
            $"(pending={active.PendingDecision?.Kind}, incoming={active.PendingDecision?.IncomingCard}, " +
            $"frames={string.Join(',', active.ResolutionStack.Select(frame => $"{frame.Kind}/{frame.Step}"))}, " +
            $"moves={string.Join(',', active.CardMovements.Where(move => move.CardId == cost).Select(move => move.Reason.Value))}).");
        for (var step = 0; step < 32 && active.ResolutionStack.Count > 0; step++)
        {
            var continued = active.Submit(new AdvanceOneStepCommand(active.Revision));
            Require(continued.Accepted, continued.Error?.Message ?? "The Lijian Duel could not continue.");
        }
        Require(active.ResolutionStack.Count == 0,
            "The Lijian Duel must close its active-skill and response frames.");
        var restored = GameReplay.Restore(active.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(active.CreateSnapshot(0, revealAll: true)),
            "A completed Lijian Duel must replay exactly.");

        var biyue = StartClassicGeneralAtPlay(
            registry,
            1,
            "classic:diao-chan",
            GameCheckpoint.CurrentRulesVersion) ?? SelectGeneral(registry, "classic:diao-chan", GameCheckpoint.CurrentRulesVersion);
        if (biyue.PendingDecision?.Kind != DecisionKind.PlayCard)
        {
            var reached = biyue.Submit(new AdvanceCommand(biyue.Revision));
            Require(reached.Accepted, reached.Error?.Message ?? "Diao Chan did not reach play.");
        }
        var ended = biyue.Submit(new EndPlayPhaseCommand(0, biyue.Revision, biyue.PendingDecision!.PromptId));
        Require(ended.Accepted, ended.Error?.Message ?? "Diao Chan could not end play.");
        var advanced = biyue.Submit(new AdvanceCommand(biyue.Revision));
        Require(advanced.Accepted && biyue.PendingDecision is { Kind: DecisionKind.Biyue, PlayerSeat: 0 },
            advanced.Error?.Message ?? "Diao Chan must receive the optional Biyue end-phase prompt.");
        var prompt = biyue.PendingDecision!;
        var handBefore = biyue.CreateSnapshot(0, revealAll: true).Players[0].Hand.Count;
        var drew = biyue.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("action") == "biyue-use").Id,
            biyue.Revision));
        Require(drew.Accepted && biyue.CardMovements.Any(move => move.Reason == CardMoveReasons.BiyueDraw) &&
                biyue.CreateSnapshot(0, revealAll: true).Players[0].Hand.Count == handBefore + 1,
            drew.Error?.Message ?? "Biyue must draw exactly one card before ending the turn.");
    }

    public static void FormalJieyinAndXiaojiFlow()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        GameEngine? xiaojiGame = null;
        LegalAction[]? equipmentActions = null;
        for (var seed = 1; seed <= 8_192 && equipmentActions is null; seed++)
        {
            var candidate = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:sun-shangxiang",
                GameCheckpoint.CurrentRulesVersion);
            if (candidate is null) continue;
            var equipments = candidate.GetHumanLegalActions()
                .Where(action => action.Kind == LegalActionKind.Equip && action.CardId is not null)
                .Select(action => new
                {
                    Action = action,
                    Slot = EquipmentCatalog.Get(candidate.CreateSnapshot(0, revealAll: true).Players[0].Hand
                        .Single(card => card.Id == action.CardId).Kind).Slot
                })
                .GroupBy(item => item.Slot)
                .Select(group => group.Select(item => item.Action).Take(2).ToArray())
                .FirstOrDefault(group => group.Length == 2);
            if (equipments is not null)
            {
                xiaojiGame = candidate;
                equipmentActions = equipments;
            }
        }

        Require(xiaojiGame is not null && equipmentActions is { Length: 2 },
            "Could not find a deterministic Sun Shangxiang equipment-replacement fixture.");
        var xiaoji = xiaojiGame!;
        var legacyXiaoji = StartClassicGeneralAtPlay(
            registry,
            xiaoji.Seed,
            "classic:sun-shangxiang",
            rulesVersion: 56) ?? throw new InvalidOperationException("The rules v56 Xiaoji fixture was not reproducible.");
        foreach (var equipment in equipmentActions!)
        {
            if (legacyXiaoji.PendingDecision is null)
            {
                var continued = legacyXiaoji.Submit(new AdvanceCommand(legacyXiaoji.Revision));
                Require(continued.Accepted && legacyXiaoji.PendingDecision?.Kind == DecisionKind.PlayCard,
                    continued.Error?.Message ?? "The rules v56 fixture did not return to play.");
            }
            var equipped = legacyXiaoji.Submit(new PlayCardCommand(
                0,
                equipment.CardId!.Value,
                [],
                legacyXiaoji.Revision,
                legacyXiaoji.PendingDecision!.PromptId));
            Require(equipped.Accepted, equipped.Error?.Message ?? "The rules v56 fixture could not equip.");
        }
        Require(legacyXiaoji.PendingDecision?.Kind != DecisionKind.Xiaoji &&
                legacyXiaoji.CardMovements.All(move => move.Reason != CardMoveReasons.XiaojiDraw),
            "Rules v56 must preserve equipment replacement without Xiaoji.");

        foreach (var equipment in equipmentActions!)
        {
            if (xiaoji.PendingDecision is null)
            {
                var continued = xiaoji.Submit(new AdvanceCommand(xiaoji.Revision));
                Require(continued.Accepted && xiaoji.PendingDecision?.Kind == DecisionKind.PlayCard,
                    continued.Error?.Message ?? "Sun Shangxiang did not return to play after equipping.");
            }
            var prompt = xiaoji.PendingDecision!;
            var equipped = xiaoji.Submit(new PlayCardCommand(
                0,
                equipment.CardId!.Value,
                [],
                xiaoji.Revision,
                prompt.PromptId));
            Require(equipped.Accepted, equipped.Error?.Message ?? "Sun Shangxiang could not equip the fixture card.");
        }

        Require(xiaoji.PendingDecision is { Kind: DecisionKind.Xiaoji, PlayerSeat: 0 } &&
                xiaoji.PendingDecision.Choices.Count == 2,
            "Replacing Sun Shangxiang's equipment must publish an optional Xiaoji prompt.");
        var xiaojiPrompt = xiaoji.PendingDecision!;
        var paused = xiaoji.CreateCheckpoint();
        var pausedRestore = GameReplay.Restore(paused, registry);
        Require(pausedRestore.PendingDecision?.Kind == DecisionKind.Xiaoji,
            "A paused Xiaoji prompt must replay exactly.");
        var handBeforeXiaoji = xiaoji.CreateSnapshot(0, revealAll: true).Players[0].Hand.Count;
        var drew = xiaoji.Submit(new AnswerPromptCommand(
            0,
            xiaojiPrompt.PromptId,
            xiaojiPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "xiaoji-use").Id,
            xiaoji.Revision));
        Require(drew.Accepted &&
                xiaoji.CardMovements.Count(move => move.Reason == CardMoveReasons.XiaojiDraw) == 2 &&
                xiaoji.Events.Select(item => item.Payload).OfType<EquipmentLossSkillResolvedEvent>().Any(item =>
                    item.SourceSeat == 0 && item.Skill == SkillKind.Xiaoji && item.Used && item.DrawCount == 2) &&
                xiaoji.CreateSnapshot(0, revealAll: true).Players[0].Hand.Count == handBeforeXiaoji + 2,
            drew.Error?.Message ?? "Xiaoji must draw exactly two cards after one equipment leaves.");

        var jieyin = SelectGeneral(registry, "classic:sun-shangxiang", GameCheckpoint.CurrentRulesVersion);
        var reached = jieyin.Submit(new AdvanceCommand(jieyin.Revision));
        Require(reached.Accepted && jieyin.PendingDecision?.Kind == DecisionKind.PlayCard,
            reached.Error?.Message ?? "Sun Shangxiang did not reach her play phase.");
        var full = jieyin.CreateSnapshot(0, revealAll: true);
        var maleTarget = full.Players.First(player =>
            player.Seat != 0 &&
            registry.Generals[player.GeneralId!].Gender == GeneralGender.Male);
        SetRuntimeHp(jieyin, 0, full.Players[0].MaxHp - 1);
        SetRuntimeHp(jieyin, maleTarget.Seat, maleTarget.MaxHp - 1);
        var jieyinAction = jieyin.GetHumanLegalActions().Single(action =>
            action.Kind == LegalActionKind.UseSkill && action.Skill == SkillKind.Jieyin);
        var cost = jieyinAction.SelectableCardIds.Take(2).ToArray();
        var used = jieyin.Submit(new UseSkillCommand(
            0,
            SkillKind.Jieyin,
            cost,
            [maleTarget.Seat],
            jieyin.Revision,
            jieyin.PendingDecision!.PromptId));
        var after = jieyin.CreateSnapshot(0, revealAll: true);
        Require(used.Accepted &&
                after.Players[0].Hp == after.Players[0].MaxHp &&
                after.Players[maleTarget.Seat].Hp == after.Players[maleTarget.Seat].MaxHp &&
                jieyin.CardMovements.Count(move =>
                    cost.Contains(move.CardId) &&
                    move.Reason == CardMoveReasons.JieyinDiscard &&
                    move.To == CardLocation.DiscardPile) == 2,
            used.Error?.Message ?? "Jieyin must discard exactly two hand cards and recover both characters.");
    }

    public static void FormalQianxunAndLianyingFlow()
    {
        var qianxun = SkillRegistry.Get(SkillKind.Qianxun);
        var context = new PlayerSkillContext(0, 3, 3, 2, TurnPhase.Play);
        Require(qianxun.ProhibitsCardTarget(context, CardKind.Snatch) &&
                qianxun.ProhibitsCardTarget(context, CardKind.Indulgence) &&
                !qianxun.ProhibitsCardTarget(context, CardKind.Dismantlement),
            "Qianxun must prohibit Snatch and Indulgence without blocking other tricks.");

        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        GameEngine? game = null;
        LegalAction? equipment = null;
        for (var seed = 1; seed <= 8_192 && equipment is null; seed++)
        {
            var candidate = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:lu-xun",
                GameCheckpoint.CurrentRulesVersion);
            var action = candidate?.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.Equip && item.CardId is not null);
            if (candidate is not null && action is not null)
            {
                game = candidate;
                equipment = action;
            }
        }

        Require(game is not null && equipment?.CardId is not null,
            "Could not find a deterministic Lu Xun last-hand equipment fixture.");
        var current = game!;
        var lastCardId = equipment!.CardId!.Value;
        KeepOnlyHandCard(current, 0, lastCardId);
        var handBefore = current.CreateSnapshot(0, revealAll: true).Players[0].Hand.Count;
        var equipped = current.Submit(new PlayCardCommand(
            0,
            lastCardId,
            [],
            current.Revision,
            current.PendingDecision!.PromptId));
        Require(equipped.Accepted &&
                current.PendingDecision is { Kind: DecisionKind.Lianying, PlayerSeat: 0 } &&
                current.PendingDecision.Choices.Count == 2,
            equipped.Error?.Message ?? "Losing Lu Xun's last hand card must publish an optional Lianying prompt.");
        var prompt = current.PendingDecision!;
        var drew = current.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "lianying-use").Id,
            current.Revision));
        Require(drew.Accepted && handBefore == 1 &&
                current.CardMovements.Count(move => move.Reason == CardMoveReasons.LianyingDraw) == 1 &&
                current.Events.Select(item => item.Payload).OfType<DrawSkillResolvedEvent>().Any(item =>
                    item.SourceSeat == 0 && item.Skill == SkillKind.Lianying && item.Used && item.DrawCount == 1) &&
                current.CreateSnapshot(0, revealAll: true).Players[0].Hand.Count == 1,
            drew.Error?.Message ?? "Lianying must draw exactly one card after the last hand card is lost.");

        var legacy = StartClassicGeneralAtPlay(registry, current.Seed, "classic:lu-xun", rulesVersion: 57) ??
            throw new InvalidOperationException("The rules v57 Lu Xun fixture was not reproducible.");
        KeepOnlyHandCard(legacy, 0, lastCardId);
        var legacyEquip = legacy.Submit(new PlayCardCommand(
            0,
            lastCardId,
            [],
            legacy.Revision,
            legacy.PendingDecision!.PromptId));
        Require(legacyEquip.Accepted && legacy.PendingDecision?.Kind != DecisionKind.Lianying &&
                legacy.CardMovements.All(move => move.Reason != CardMoveReasons.LianyingDraw),
            legacyEquip.Error?.Message ?? "Rules v57 must preserve last-hand-card loss without Lianying.");
    }

    private static void KeepOnlyHandCard(GameEngine game, int seat, int keptCardId)
    {
        var cardZonesField = typeof(GameEngine).GetField("_cardZones", BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The runtime card-zone store was not found.");
        var cardZones = cardZonesField.GetValue(game) ??
            throw new InvalidOperationException("The runtime card-zone store is unavailable.");
        var move = cardZones.GetType().GetMethod("Move", BindingFlags.Public | BindingFlags.Instance) ??
            throw new InvalidOperationException("The runtime card-zone move method was not found.");
        var handIds = game.CreateSnapshot(seat, revealAll: true).Players[seat].Hand
            .Select(card => card.Id)
            .Where(cardId => cardId != keptCardId)
            .ToArray();
        foreach (var cardId in handIds)
        {
            _ = move.Invoke(cardZones, [cardId, CardLocation.Hand(seat), CardLocation.DiscardPile]);
        }
    }

    private static void SetRuntimeHp(GameEngine game, int seat, int hp)
    {
        var playersField = typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The runtime player list was not found.");
        var players = (System.Collections.IList)(playersField.GetValue(game) ??
            throw new InvalidOperationException("The runtime player list is unavailable."));
        var player = players[seat] ?? throw new InvalidOperationException("The runtime player is unavailable.");
        var hpProperty = player.GetType().GetProperty("Hp") ??
            throw new InvalidOperationException("The runtime HP property was not found.");
        hpProperty.SetValue(player, hp);
    }

    private static GameEngine FindDaQiaoLiuliFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 8_192; seed++)
        {
            var game = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:da-qiao",
                GameCheckpoint.CurrentRulesVersion);
            if (game is null)
            {
                continue;
            }

            for (var boundary = 0; boundary < 80 && game.State.Status != EngineStatus.Completed; boundary++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.Liuli, PlayerSeat: 0 } liuli &&
                    liuli.Choices.Any(choice => choice.Parameters.GetValueOrDefault("action") == "liuli-use"))
                {
                    return game;
                }

                CommandResult result;
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } play)
                {
                    result = game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId));
                }
                else
                {
                    result = game.Submit(new AdvanceCommand(game.Revision));
                }
                Require(result.Accepted, result.Error?.Message ?? "Could not advance the Da Qiao Liuli fixture.");
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Da Qiao Liuli fixture.");
    }

    private static GameEngine ReachZhouYuPlayPhase(ContentRegistry registry, int rulesVersion)
    {
        var game = SelectGeneral(registry, "classic:zhou-yu", rulesVersion);
        var reachedYingzi = game.Submit(new AdvanceCommand(game.Revision));
        Require(reachedYingzi.Accepted && game.PendingDecision?.Kind == DecisionKind.Yingzi,
            reachedYingzi.Error?.Message ?? "Could not reach Zhou Yu's Yingzi choice.");
        var prompt = game.PendingDecision!;
        var skipped = game.Submit(new AnswerPromptCommand(
            0,
            prompt.PromptId,
            prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "yingzi-skip").Id,
            game.Revision));
        Require(skipped.Accepted, skipped.Error?.Message ?? "Could not skip Yingzi for the Fanjian fixture.");
        var reachedPlay = game.Submit(new AdvanceCommand(game.Revision));
        Require(reachedPlay.Accepted && game.PendingDecision?.Kind == DecisionKind.PlayCard,
            reachedPlay.Error?.Message ?? "Could not reach Zhou Yu's play phase.");
        return game;
    }

    private static bool DriveUntilOwnLightningJudgment(
        GameEngine game,
        bool expectTiandu,
        out JudgmentResolvedEvent judgment)
    {
        var seenEventCount = game.Events.Count;
        for (var step = 0; step < 2_000 && game.State.Status != EngineStatus.Completed; step++)
        {
            var resolved = game.Events
                .Skip(seenEventCount)
                .Select(envelope => envelope.Payload)
                .OfType<JudgmentResolvedEvent>()
                .LastOrDefault(candidate =>
                    candidate.TargetSeat == 0 && candidate.Reason == JudgmentReasons.Lightning);
            if (resolved is not null)
            {
                if (!expectTiandu || game.PendingDecision?.Kind == DecisionKind.Tiandu)
                {
                    judgment = resolved;
                    return true;
                }
            }

            DeclineOrAdvance(game);
        }

        judgment = null!;
        return false;
    }

    private static GameEngine? StartClassicGeneralAtPlay(
        ContentRegistry registry,
        int seed,
        string generalId,
        int rulesVersion)
    {
        var game = CreateInteractive(registry, seed);
        if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
        {
            game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
        }

        var started = game.Submit(new StartGameCommand());
        Require(started.Accepted, started.Error?.Message ?? "Classic active-skill fixture failed to start.");
        if (started.Result.PendingDecision?.Choices.Any(choice =>
                choice.ContentIds.SequenceEqual([generalId])) != true)
        {
            return null;
        }

        var selected = game.Submit(new SelectGeneralCommand(
            0,
            generalId,
            game.Revision,
            game.PendingDecision!.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? $"Could not select {generalId}.");
        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted, advanced.Error?.Message ?? "Classic active-skill setup did not advance.");
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Classic active-skill fixture did not stop at the human play phase.");
        return game;
    }

    private static GameEngine FindLuMengSlashFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:lu-meng",
                GameCheckpoint.CurrentRulesVersion);
            if (game?.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Slash) == true)
            {
                return game;
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Lu Meng Slash fixture.");
    }

    private static (GameEngine Game, LegalAction Action, int TargetHp) FindXuChuDirectAttackFixture(
        ContentRegistry registry,
        LegalActionKind actionKind,
        Func<PlayerSnapshot, bool> targetPredicate,
        bool requireNoNullification = false)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Xu Chu attack fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:xu-chu"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:xu-chu",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Xu Chu.");
            var reachedLuoyi = game.Submit(new AdvanceCommand(game.Revision));
            Require(reachedLuoyi.Accepted, reachedLuoyi.Error?.Message ?? "Xu Chu did not reach Luoyi.");
            var luoyi = game.PendingDecision;
            if (luoyi?.Kind != DecisionKind.Luoyi)
            {
                continue;
            }

            var used = game.Submit(new AnswerPromptCommand(
                0,
                luoyi.PromptId,
                luoyi.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "luoyi-use").Id,
                game.Revision));
            Require(used.Accepted, used.Error?.Message ?? "Could not enable Luoyi for the attack fixture.");
            var reachedPlay = game.Submit(new AdvanceCommand(game.Revision));
            Require(reachedPlay.Accepted, reachedPlay.Error?.Message ?? "Xu Chu did not reach the play phase.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            if (requireNoNullification && full.Players.Any(player =>
                    player.Hand.Any(card => card.Kind == CardKind.Nullification)))
            {
                continue;
            }

            var action = game.GetHumanLegalActions()
                .Where(candidate => candidate.Kind == actionKind && candidate.TargetSeat is not null)
                .FirstOrDefault(candidate => targetPredicate(
                    full.Players.Single(player => player.Seat == candidate.TargetSeat)));
            if (action is null)
            {
                continue;
            }

            var target = full.Players.Single(player => player.Seat == action.TargetSeat);
            return (game, action, target.Hp);
        }

        throw new InvalidOperationException($"Could not find a deterministic Xu Chu {actionKind} fixture.");
    }

    private static (GameEngine Game, int PhysicalCardId, int TargetSeat, int DistanceTwoSeat)
        FindXuHuangDuanliangFixture(ContentRegistry registry)
    {
        var offered = 0;
        var equippedCandidates = 0;
        var convertedCandidates = 0;
        var placedCandidates = 0;
        var resolvedCandidates = 0;
        string? lastError = null;
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Xu Huang fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:xu-huang"])) != true)
            {
                continue;
            }
            offered++;

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:xu-huang",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Xu Huang.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Xu Huang did not reach the play phase.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            if (full.Players.SelectMany(player => player.Hand).Any(card => card.Kind == CardKind.Nullification))
            {
                continue;
            }

            var equipment = full.Players.Single(player => player.Seat == 0).Hand
                .Where(card =>
                    EquipmentCatalog.IsEquipment(card.Kind) &&
                    card.Suit is Suit.Spade or Suit.Club &&
                    EquipmentCatalog.Get(card.Kind).Slot != EquipmentSlot.OffensiveHorse)
                .OrderBy(card => card.Id)
                .FirstOrDefault();
            if (equipment is null)
            {
                continue;
            }
            equippedCandidates++;

            Equip(game, equipment.Id);
            var converted = game.GetHumanLegalActions()
                .Where(action =>
                    action.Kind == LegalActionKind.SupplyShortage &&
                    action.CardId == equipment.Id &&
                    action.PlayedCardKind == CardKind.SupplyShortage)
                .ToArray();
            var targetAction = converted.FirstOrDefault(action => action.TargetSeat == 1);
            var distanceTwoAction = converted.FirstOrDefault(action =>
                action.TargetSeat is { } seat && game.GetCombatDistance(0, seat) == 2);
            if (targetAction is null || distanceTwoAction?.TargetSeat is not { } distanceTwoSeat)
            {
                continue;
            }
            convertedCandidates++;

            var simulated = GameReplay.Restore(game.CreateCheckpoint(), registry);
            var simulatedAction = simulated.GetHumanLegalActions().Single(action =>
                action.Kind == LegalActionKind.SupplyShortage &&
                action.CardId == equipment.Id &&
                action.TargetSeat == 1 &&
                action.PlayedCardKind == CardKind.SupplyShortage);
            var used = simulated.Submit(new PlayCardCommand(
                0,
                equipment.Id,
                simulatedAction.TargetSeats,
                simulated.Revision,
                simulated.PendingDecision!.PromptId,
                CardKind.SupplyShortage));
            if (!used.Accepted ||
                simulated.Events.Select(item => item.Payload).OfType<DelayedCardPlacedEvent>()
                    .All(placed => placed.CardId != equipment.Id))
            {
                lastError = used.Error?.Message ??
                    $"placement accepted={used.Accepted}, status={used.Status}, " +
                    $"pending={simulated.PendingDecision?.Kind}, " +
                    $"events={string.Join(',', simulated.Events.TakeLast(4).Select(item => item.Payload.GetType().Name))}";
                continue;
            }
            var returnedToPlay = simulated.Submit(new AdvanceCommand(simulated.Revision));
            if (!returnedToPlay.Accepted || simulated.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                lastError = returnedToPlay.Error?.Message ??
                    $"return-to-play accepted={returnedToPlay.Accepted}, status={returnedToPlay.Status}, " +
                    $"pending={simulated.PendingDecision?.Kind}";
                continue;
            }
            placedCandidates++;

            var ended = simulated.Submit(new EndPlayPhaseCommand(
                0,
                simulated.Revision,
                simulated.PendingDecision.PromptId));
            var resolved = ended.Accepted
                ? AdvanceUntilDelayedCardResolves(simulated, equipment.Id)
                : null;
            if (!ended.Accepted || resolved?.SkippedDrawPhase != true)
            {
                lastError = ended.Error?.Message ?? $"resolution pending={simulated.PendingDecision?.Kind}";
                continue;
            }
            resolvedCandidates++;

            return (game, equipment.Id, 1, distanceTwoSeat);
        }

        throw new InvalidOperationException(
            $"Could not find a deterministic Xu Huang Duanliang equipment fixture " +
            $"(offered={offered}, equipment={equippedCandidates}, converted={convertedCandidates}, " +
            $"placed={placedCandidates}, resolved={resolvedCandidates}, last={lastError ?? "none"}).");
    }

    private static DelayedCardResolvedEvent? AdvanceUntilDelayedCardResolves(
        GameEngine game,
        int cardId)
    {
        for (var step = 0; step < 2_000 && game.State.Status != EngineStatus.Completed; step++)
        {
            var resolved = game.Events.Select(item => item.Payload)
                .OfType<DelayedCardResolvedEvent>()
                .LastOrDefault(item => item.CardId == cardId);
            if (resolved is not null)
            {
                return resolved;
            }

            if (game.PendingDecision?.PlayerSeat == 0)
            {
                return null;
            }

            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            if (!advanced.Accepted)
            {
                return null;
            }
        }

        return game.Events.Select(item => item.Payload)
            .OfType<DelayedCardResolvedEvent>()
            .LastOrDefault(item => item.CardId == cardId);
    }

    private static (GameEngine Game, LegalAction Action, int TargetSeat, int WeaponCardId)
        FindDianWeiQiangxiFixture(
            ContentRegistry registry,
            bool requireWeapon,
            SkillKind? targetSkill = null,
            bool requirePeach = false)
    {
        var damageTriggerSkills = new HashSet<SkillKind>
        {
            SkillKind.Feedback,
            SkillKind.Yiji,
            SkillKind.Jieming,
            SkillKind.Yuanhu,
            SkillKind.Ganglie
        };
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Dian Wei fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:dian-wei"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:dian-wei",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Dian Wei.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Dian Wei did not reach the play phase.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var action = game.GetHumanLegalActions().SingleOrDefault(candidate =>
                candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Qiangxi);
            if (action is null)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            var source = full.Players.Single(player => player.Seat == 0);
            var weaponCardId = source.Hand
                .Where(card => action.SelectableCardIds.Contains(card.Id))
                .Select(card => card.Id)
                .FirstOrDefault();
            if (requireWeapon && weaponCardId == 0)
            {
                continue;
            }
            if (requirePeach && source.Hand.All(card => card.Kind != CardKind.Peach))
            {
                continue;
            }

            var target = full.Players
                .Where(player => action.SelectableTargetSeats.Contains(player.Seat))
                .FirstOrDefault(player => targetSkill is { } required
                    ? player.Skills?.Any(skill => skill.Kind == required) == true
                    : player.Skills?.All(skill => !damageTriggerSkills.Contains(skill.Kind)) != false);
            if (target is null)
            {
                continue;
            }

            return (game, action, target.Seat, weaponCardId);
        }

        throw new InvalidOperationException(
            $"Could not find a deterministic Dian Wei Qiangxi fixture " +
            $"(weapon={requireWeapon}, peach={requirePeach}, " +
            $"target-skill={targetSkill?.ToString() ?? "none"}).");
    }

    private static void SetPlayerHp(GameEngine game, int seat, int hp)
    {
        var playersField = typeof(GameEngine).GetField(
            "_players",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine player store was not found.");
        var players = (System.Collections.IList)playersField.GetValue(game)!;
        players[seat]!.GetType().GetProperty("Hp")!.SetValue(players[seat], hp);
    }

    private static (GameEngine Game, int TargetSeat, long ResolutionId, int XuChuHp) FindXuChuReverseDuelFixture(
        ContentRegistry registry)
    {
        var offered = 0;
        var noNullification = 0;
        var duelFound = 0;
        var responsePromptFound = 0;
        var slashChoiceFound = 0;
        var lastPending = "none";
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Reverse Duel fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:xu-chu"])) != true)
            {
                continue;
            }
            offered++;

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:xu-chu",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Xu Chu.");
            var reachedLuoyi = game.Submit(new AdvanceCommand(game.Revision));
            Require(reachedLuoyi.Accepted && game.PendingDecision?.Kind == DecisionKind.Luoyi,
                reachedLuoyi.Error?.Message ?? "Reverse Duel fixture did not reach Luoyi.");
            var luoyi = game.PendingDecision!;
            var used = game.Submit(new AnswerPromptCommand(
                0,
                luoyi.PromptId,
                luoyi.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "luoyi-use").Id,
                game.Revision));
            Require(used.Accepted, used.Error?.Message ?? "Could not enable Luoyi for reverse Duel.");
            var reachedPlay = game.Submit(new AdvanceCommand(game.Revision));
            Require(reachedPlay.Accepted, reachedPlay.Error?.Message ?? "Reverse Duel fixture did not reach play.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            if (full.Players.Any(player => player.Hand.Any(card => card.Kind == CardKind.Nullification)))
            {
                continue;
            }
            noNullification++;

            var duelAction = game.GetHumanLegalActions()
                .Where(action => action.Kind == LegalActionKind.Duel && action.TargetSeat is not null)
                .FirstOrDefault(action => full.Players.Single(player => player.Seat == action.TargetSeat)
                    .Hand.Any(card => card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash));
            if (duelAction is null)
            {
                continue;
            }
            duelFound++;

            var targetSeat = duelAction.TargetSeat!.Value;
            var played = game.Submit(new PlayCardCommand(
                0,
                duelAction.CardId!.Value,
                duelAction.TargetSeats,
                game.Revision,
                game.PendingDecision.PromptId,
                duelAction.PlayedCardKind));
            if (!played.Accepted)
            {
                continue;
            }

            var hostPending = GetHostPendingDecision(game);
            lastPending = $"{hostPending?.Kind.ToString() ?? "none"}/" +
                $"{hostPending?.PlayerSeat.ToString() ?? "none"}/target-{targetSeat}/" +
                $"status-{game.State.Status}/stack-{string.Join(',', game.ResolutionStack.Select(frame => frame.Kind))}";

            var resolutionId = game.Events.Select(item => item.Payload)
                .OfType<CardUseDeclaredEvent>()
                .Last(item => item.CardId == duelAction.CardId).ResolutionId;
            if (hostPending is not { Kind: DecisionKind.RespondSlash } targetPrompt ||
                targetPrompt.PlayerSeat != targetSeat)
            {
                continue;
            }
            responsePromptFound++;

            var slashChoice = targetPrompt.Choices.FirstOrDefault(choice =>
                choice.Parameters.GetValueOrDefault("response") == "slash");
            if (slashChoice?.Cards.Count != 1)
            {
                continue;
            }
            slashChoiceFound++;

            ResolveSyntheticDuelSlash(game, targetSeat, slashChoice.Cards[0]);
            if (game.PendingDecision is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 } prompt &&
                prompt.IncomingCard == CardKind.Duel)
            {
                var xuChuHp = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hp;
                return (game, targetSeat, resolutionId, xuChuHp);
            }
        }

        throw new InvalidOperationException(
            $"Could not find a deterministic Luoyi reverse-Duel fixture " +
            $"(offered={offered}, no-null={noNullification}, duel={duelFound}, prompt={responsePromptFound}, slash={slashChoiceFound}, last={lastPending}).");
    }

    private static void ResolveSyntheticDuelSlash(GameEngine game, int responderSeat, int slashCardId)
    {
        var duelField = typeof(GameEngine).GetField(
            "_pendingDuel",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine Duel continuation was not found.");
        var duel = duelField.GetValue(game) ??
            throw new InvalidOperationException("The reverse-Duel fixture lost its continuation.");
        var playersField = typeof(GameEngine).GetField(
            "_players",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine player store was not found.");
        var players = (System.Collections.IList)playersField.GetValue(game)!;
        var responder = players[responderSeat]!;
        var getHand = typeof(GameEngine).GetMethod(
            "GetHand",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine hand accessor was not found.");
        var slash = ((System.Collections.IEnumerable)getHand.Invoke(game, [responder])!)
            .Cast<Card>()
            .Single(card => card.Id == slashCardId);
        var resolutionId = game.ResolutionStack.OfType<CardUseFrame>()
            .Single(frame => frame.CardKind == CardKind.Duel).Id;
        var popResponse = typeof(GameEngine).GetMethod(
            "PopResponseWindow",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine response-window popper was not found.");
        popResponse.Invoke(game, [resolutionId]);
        var setCardUseStep = typeof(GameEngine).GetMethod(
            "SetCardUseStep",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine card-use cursor updater was not found.");
        setCardUseStep.Invoke(game, [resolutionId, ResolutionFrameStep.ResolvingEffect]);
        var clearPending = typeof(GameEngine).GetMethod(
            "ClearPendingDecision",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine pending-decision clearer was not found.");
        clearPending.Invoke(game, null);
        var resolve = typeof(GameEngine).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == "ResolveDuelResponse" && method.GetParameters().Length == 3);
        resolve.Invoke(game, [duel, responder, slash]);
    }

    private static PendingDecision? GetHostPendingDecision(GameEngine game)
    {
        var decisionField = typeof(GameEngine).GetField(
            "_pendingDecision",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine pending-decision store was not found.");
        return (PendingDecision?)decisionField.GetValue(game);
    }

    private static GameEngine FindZhenJiFirstBlackLuoshenFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Zhen Ji fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:zhen-ji"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:zhen-ji",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Zhen Ji.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Zhen Ji did not reach Luoshen.");
            if (game.PendingDecision is not { Kind: DecisionKind.Luoshen } prompt)
            {
                continue;
            }

            var used = game.Submit(new AnswerPromptCommand(
                0,
                prompt.PromptId,
                prompt.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "luoshen-use").Id,
                game.Revision));
            Require(used.Accepted, used.Error?.Message ?? "Could not use Luoshen.");
            if (game.PendingDecision is null && game.State.Phase != TurnPhase.Play)
            {
                var resolvedAi = game.Submit(new AdvanceCommand(game.Revision));
                Require(resolvedAi.Accepted, resolvedAi.Error?.Message ?? "Could not resolve Luoshen replacement choices.");
            }

            var judgment = game.Events.Select(item => item.Payload)
                .OfType<JudgmentResolvedEvent>()
                .LastOrDefault(item => item.Reason == JudgmentReasons.Luoshen);
            if (game.PendingDecision is { Kind: DecisionKind.Luoshen } &&
                judgment is { Suit: Suit.Spade or Suit.Club, Succeeded: true })
            {
                return game;
            }
        }

        throw new InvalidOperationException("Could not find a deterministic first-black Luoshen fixture.");
    }

    private static (
        GameEngine Game,
        GameCheckpoint BeforeDamage,
        LegalAction Action,
        int EventCount,
        int TargetSeat,
        int SourceHpBefore,
        int Distance) FindWeiYanKuangguFixture(
            ContentRegistry registry,
            int rulesVersion = GameCheckpoint.CurrentRulesVersion)
    {
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = CreateInteractive(registry, seed, Role.Rebel);
            if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
            {
                game = GameReplay.Restore(
                    game.CreateCheckpoint() with { RulesVersion = rulesVersion },
                    registry);
            }
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Wei Yan fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:wei-yan"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:wei-yan",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Wei Yan.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Wei Yan setup did not advance.");
            var result = advanced.Result;
            for (var step = 0; result.Status != EngineStatus.Completed && step < 1_200; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } prompt)
                {
                    if (game.Events.Any(item => item.Payload is StoneAxeResolvedEvent))
                    {
                        break;
                    }

                    var full = game.CreateSnapshot(0, revealAll: true);
                    var source = full.Players.Single(player => player.Seat == 0);
                    if (source.Hp > 0 && source.Hp < source.MaxHp)
                    {
                        var candidate = game.GetHumanLegalActions()
                            .Where(action => action.Kind == LegalActionKind.Slash &&
                                             action.CardId is not null &&
                                             action.TargetSeat is not null)
                            .Select(action => new
                            {
                                Action = action,
                                Target = full.Players.Single(player => player.Seat == action.TargetSeat),
                                Distance = game.GetCombatDistance(0, action.TargetSeat!.Value)
                            })
                            .Where(item => item.Distance == 1 &&
                                           item.Target.Hand.All(card => card.Kind != CardKind.Dodge) &&
                                           item.Target.Equipment.All(card =>
                                               card.Kind is not (CardKind.BaguaFormation or CardKind.RenwangShield)) &&
                                           item.Target.Skills?.All(skill =>
                                               skill.Kind is not (SkillKind.Qingguo or SkillKind.Longdan or SkillKind.Hujia)) != false)
                            .OrderBy(item => item.Action.CardId)
                            .ThenBy(item => item.Action.TargetSeat)
                            .FirstOrDefault();
                        if (candidate is not null)
                        {
                            var beforeDamage = GameCheckpointJson.Deserialize(
                                GameCheckpointJson.Serialize(game.CreateCheckpoint()));
                            var eventCount = game.Events.Count;
                            var played = SubmitPlayAction(game, candidate.Action);
                            if (!played.Accepted)
                            {
                                continue;
                            }

                            if (rulesVersion >= 38 &&
                                !game.Events.Skip(eventCount).Any(item =>
                                    item.Payload is KuangguRecoveredEvent))
                            {
                                var resolved = game.Submit(new AdvanceOneStepCommand(game.Revision));
                                if (!resolved.Accepted)
                                {
                                    continue;
                                }
                            }

                            var newEvents = game.Events.Skip(eventCount)
                                .Select(item => item.Payload)
                                .ToArray();
                            if ((rulesVersion >= 38 && newEvents.Any(item =>
                                    item is KuangguRecoveredEvent)) ||
                                (rulesVersion < 38 && newEvents.Any(item =>
                                    item is DamageAppliedEvent damage &&
                                    damage.SourceSeat == 0 &&
                                    damage.TargetSeat == candidate.Target.Seat)))
                            {
                                return (
                                    game,
                                    beforeDamage,
                                    candidate.Action,
                                    eventCount,
                                    candidate.Target.Seat,
                                    source.Hp,
                                    candidate.Distance);
                            }
                        }
                    }

                    result = DeclineOrAdvance(game, result);
                    continue;
                }

                result = DeclineOrAdvance(game, result);
            }
        }

        throw new InvalidOperationException("Could not find a deterministic classic Wei Yan Kuanggu fixture.");
    }

    private static (
        GameEngine Game,
        LegalAction FirstAction) FindZhangFeiPaoxiaoFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:zhang-fei",
                GameCheckpoint.CurrentRulesVersion);
            if (game is null)
            {
                continue;
            }

            var self = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
            if (self.Hand.Count(card =>
                    card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) < 2)
            {
                continue;
            }

            var beforeFirstSlash = GameCheckpointJson.Deserialize(
                GameCheckpointJson.Serialize(game.CreateCheckpoint()));
            foreach (var firstAction in game.GetHumanLegalActions()
                         .Where(action => action.Kind == LegalActionKind.Slash && action.CardId is not null)
                         .OrderBy(action => action.CardId)
                         .ThenBy(action => action.TargetSeat))
            {
                var probe = GameReplay.Restore(beforeFirstSlash, registry);
                var matchingAction = probe.GetHumanLegalActions().Single(action =>
                    action.Kind == firstAction.Kind &&
                    action.CardId == firstAction.CardId &&
                    action.TargetSeats.SequenceEqual(firstAction.TargetSeats) &&
                    action.PlayedCardKind == firstAction.PlayedCardKind);
                var used = SubmitPlayAction(probe, matchingAction);
                if (!used.Accepted ||
                    !TryReturnToHumanPlay(probe) ||
                    !probe.GetHumanLegalActions().Any(action =>
                        action.Kind == LegalActionKind.Slash && action.CardId is not null))
                {
                    continue;
                }

                return (game, firstAction);
            }
        }

        throw new InvalidOperationException(
            "Could not find a deterministic classic Zhang Fei two-Slash Paoxiao fixture.");
    }

    private static (
        GameEngine Game,
        LegalAction Action) FindZhaoYunLongdanFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:zhao-yun",
                GameCheckpoint.CurrentRulesVersion);
            if (game is null)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            var self = full.Players.Single(player => player.Seat == 0);
            var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            {
                if (candidate.Kind != LegalActionKind.Slash ||
                    candidate.PlayedCardKind != CardKind.Slash ||
                    candidate.CardId is not { } cardId ||
                    candidate.TargetSeat is not { } targetSeat ||
                    self.Hand.Single(card => card.Id == cardId).Kind != CardKind.Dodge)
                {
                    return false;
                }

                var target = full.Players.Single(player => player.Seat == targetSeat);
                return target.Hp > 1 &&
                       target.Hand.All(card => card.Kind != CardKind.Dodge) &&
                       target.Equipment.All(card => card.Kind != CardKind.BaguaFormation) &&
                       target.Skills?.All(skill =>
                           skill.Kind is not (SkillKind.Qingguo or SkillKind.Longdan or SkillKind.Hujia)) != false;
            });
            if (action is not null)
            {
                return (game, action);
            }
        }

        throw new InvalidOperationException(
            "Could not find a deterministic classic Zhao Yun Longdan conversion fixture.");
    }

    private static (
        GameEngine ActiveGame,
        GameEngine LegacyGame,
        GameEngine ResponseGame,
        int EquipmentCardId,
        LegalAction ActiveAction) FindGuanYuWushengEquipmentFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var current = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:guan-yu",
                GameCheckpoint.CurrentRulesVersion);
            if (current is null)
            {
                continue;
            }

            var self = current.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0);
            var redEquipment = self.Hand.FirstOrDefault(card =>
                EquipmentCatalog.IsEquipment(card.Kind) &&
                card.Suit is Suit.Heart or Suit.Diamond);
            if (redEquipment is null)
            {
                continue;
            }

            var legacy = StartClassicGeneralAtPlay(registry, seed, "classic:guan-yu", rulesVersion: 39);
            if (legacy is null)
            {
                continue;
            }

            Equip(current, redEquipment.Id);
            Equip(legacy, redEquipment.Id);
            var activeAction = current.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash &&
                action.CardId == redEquipment.Id &&
                action.PlayedCardKind == CardKind.Slash);
            if (activeAction is null)
            {
                continue;
            }

            var response = GameReplay.Restore(
                GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(current.CreateCheckpoint())),
                registry);
            var playPrompt = response.PendingDecision ??
                throw new InvalidOperationException("The equipped Wusheng fixture lost its play prompt.");
            var ended = response.Submit(new EndPlayPhaseCommand(
                0,
                response.Revision,
                playPrompt.PromptId));
            Require(ended.Accepted, ended.Error?.Message ??
                "The equipped Wusheng response fixture could not end the play phase.");

            for (var step = 0; step < 4_000 && response.State.Status != EngineStatus.Completed; step++)
            {
                if (response.PendingDecision is
                    {
                        Kind: DecisionKind.RespondSlash,
                        PlayerSeat: 0
                    } responsePrompt &&
                    responsePrompt.IncomingCard is CardKind.Duel or CardKind.BarbarianAssault &&
                    responsePrompt.Choices.Any(choice =>
                        choice.Cards.SequenceEqual([redEquipment.Id]) &&
                        choice.Parameters.GetValueOrDefault("response-card-kind") == nameof(CardKind.Slash)))
                {
                    return (current, legacy, response, redEquipment.Id, activeAction);
                }

                var responseOwner = response.CreateSnapshot(0, revealAll: true).Players
                    .Single(player => player.Seat == 0);
                if (!responseOwner.IsAlive ||
                    responseOwner.Equipment.All(card => card.Id != redEquipment.Id))
                {
                    break;
                }

                DeclineOrAdvance(response);
            }
        }

        throw new InvalidOperationException(
            "Could not find a deterministic classic Guan Yu equipped Wusheng use-and-response fixture.");
    }

    private static bool TryReturnToHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 64 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard })
            {
                return true;
            }

            DeclineOrAdvance(game);
        }

        return false;
    }

    private static (
        GameEngine Game,
        GameCheckpoint BeforeAction,
        LegalAction Action,
        int TargetSeat) FindLuBuWushuangSlashFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:lu-bu",
                GameCheckpoint.CurrentRulesVersion);
            if (game is null)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            var candidate = game.GetHumanLegalActions()
                .Where(action => action.Kind == LegalActionKind.Slash && action.TargetSeat is not null)
                .Select(action => new
                {
                    Action = action,
                    Target = full.Players.Single(player => player.Seat == action.TargetSeat)
                })
                .Where(item => item.Target.Hand.Count(card => card.Kind == CardKind.Dodge) >= 2 &&
                               item.Target.Equipment.All(card =>
                                   card.Kind is not (CardKind.BaguaFormation or CardKind.RenwangShield)) &&
                               item.Target.Skills?.All(skill =>
                                   skill.Kind is not (SkillKind.Qingguo or SkillKind.Longdan or SkillKind.Hujia)) != false)
                .OrderBy(item => item.Action.CardId)
                .ThenBy(item => item.Action.TargetSeat)
                .FirstOrDefault();
            if (candidate is null)
            {
                continue;
            }

            return (
                game,
                GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
                candidate.Action,
                candidate.Target.Seat);
        }

        throw new InvalidOperationException("Could not find a deterministic classic Lu Bu two-Dodge fixture.");
    }

    private static (
        GameEngine Game,
        GameCheckpoint BeforeAction,
        LegalAction Action,
        int TargetSeat) FindLuBuWushuangDuelFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = StartClassicGeneralAtPlay(
                registry,
                seed,
                "classic:lu-bu",
                GameCheckpoint.CurrentRulesVersion);
            if (game is null)
            {
                continue;
            }

            var full = game.CreateSnapshot(0, revealAll: true);
            if (full.Players.SelectMany(player => player.Hand)
                .Any(card => card.Kind == CardKind.Nullification))
            {
                continue;
            }

            var candidate = game.GetHumanLegalActions()
                .Where(action => action.Kind == LegalActionKind.Duel && action.TargetSeat is not null)
                .Select(action => new
                {
                    Action = action,
                    Target = full.Players.Single(player => player.Seat == action.TargetSeat)
                })
                .Where(item => item.Target.Hand.Count(card =>
                                   card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) == 1 &&
                               item.Target.Skills?.All(skill =>
                                   skill.Kind is not (SkillKind.Wusheng or SkillKind.Longdan or SkillKind.Jijiang)) != false)
                .OrderBy(item => item.Action.CardId)
                .ThenBy(item => item.Action.TargetSeat)
                .FirstOrDefault();
            if (candidate is null)
            {
                continue;
            }

            return (
                game,
                GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),
                candidate.Action,
                candidate.Target.Seat);
        }

        throw new InvalidOperationException("Could not find a deterministic classic Lu Bu one-Slash Duel fixture.");
    }

    private static void DriveAiUntil(GameEngine game, Func<bool> completed)
    {
        for (var step = 0; step < 32 && !completed(); step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 })
            {
                throw new InvalidOperationException(
                    $"Wushuang fixture reached an unexpected human {game.PendingDecision.Kind} prompt.");
            }

            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Could not advance the Wushuang AI response.");
        }

        Require(completed(), "Wushuang fixture did not reach the expected response boundary.");
    }

    private static CommandResult SubmitPlayAction(GameEngine game, LegalAction action) =>
        game.Submit(new PlayCardCommand(
            0,
            action.CardId ?? throw new InvalidOperationException("The play action has no physical card."),
            action.TargetSeats,
            game.Revision,
            game.PendingDecision?.PromptId ??
            throw new InvalidOperationException("The play action has no current prompt."),
            action.PlayedCardKind,
            action.TargetCardId));

    private static (
        GameEngine Game,
        PendingDecision? Prompt,
        int TargetSeat,
        int SourceHp,
        int TargetHandCount,
        int AttackRange) FindHuangZhongLiegongFixture(
        ContentRegistry registry,
        int rulesVersion,
        LiegongFixtureKind kind)
    {
        var humanRole = kind == LiegongFixtureKind.Ineligible ? Role.Lord : Role.Rebel;
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = CreateInteractive(registry, seed, humanRole);
            if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
            {
                game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
            }

            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Huang Zhong fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:huang-zhong"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:huang-zhong",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Huang Zhong.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Huang Zhong setup did not advance.");
            var result = advanced.Result;
            for (var step = 0; result.Status != EngineStatus.Completed && step < 1_200; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
                {
                    var full = game.CreateSnapshot(0, revealAll: true);
                    var source = full.Players.Single(player => player.Seat == 0);
                    var attackRange = game.GetAttackRange(0);
                    var candidate = game.GetHumanLegalActions()
                        .Where(action => action.Kind == LegalActionKind.Slash &&
                                         action.CardId is not null &&
                                         action.TargetSeat is not null)
                        .Select(action =>
                        {
                            var target = full.Players.Single(player => player.Seat == action.TargetSeat);
                            var targetHandCount = target.Hand.Count;
                            var eligibleByHp = targetHandCount >= source.Hp;
                            var eligibleByRange = targetHandCount <= attackRange;
                            return new
                            {
                                Action = action,
                                Target = target,
                                TargetHandCount = targetHandCount,
                                EligibleByHp = eligibleByHp,
                                EligibleByRange = eligibleByRange
                            };
                        })
                        .Where(item => item.Target.Hand.Any(card => card.Kind == CardKind.Dodge))
                        .Where(item => item.Target.GeneralId != "classic:da-qiao")
                        .Where(item => item.Target.Equipment.All(card =>
                            !EquipmentCatalog.IsEquipment(card.Kind) ||
                            EquipmentCatalog.Get(card.Kind).Slot != EquipmentSlot.Armor))
                        .Where(item => kind switch
                        {
                            LiegongFixtureKind.EligibleByHp =>
                                item.EligibleByHp && !item.EligibleByRange,
                            LiegongFixtureKind.EligibleByRange =>
                                item.EligibleByRange && !item.EligibleByHp,
                            LiegongFixtureKind.Ineligible =>
                                !item.EligibleByHp && !item.EligibleByRange,
                            _ => false
                        })
                        .OrderBy(item => item.Action.CardId)
                        .ThenBy(item => item.Action.TargetSeat)
                        .FirstOrDefault();
                    if (candidate is not null)
                    {
                        var played = game.Submit(new PlayCardCommand(
                            0,
                            candidate.Action.CardId!.Value,
                            candidate.Action.TargetSeats,
                            game.Revision,
                            game.PendingDecision.PromptId,
                            candidate.Action.PlayedCardKind,
                            candidate.Action.TargetCardId));
                        Require(played.Accepted, played.Error?.Message ?? "Huang Zhong could not use Slash.");
                        var prompt = game.PendingDecision;
                        if (prompt is null &&
                            rulesVersion >= 37 &&
                            kind != LiegongFixtureKind.Ineligible)
                        {
                            break;
                        }
                        return (
                            game,
                            prompt,
                            candidate.Target.Seat,
                            source.Hp,
                            candidate.TargetHandCount,
                            attackRange);
                    }
                }

                result = DeclineOrAdvance(game, result);
            }
        }

        throw new InvalidOperationException($"Could not find a deterministic Huang Zhong {kind} fixture.");
    }

    private static (
        GameEngine Game,
        PendingDecision Prompt,
        int TargetSeat,
        int Seed) FindMaChaoTieqiFixture(
        ContentRegistry registry,
        bool requireRedJudgment)
    {
        var attempted = 0;
        var tieqiPrompts = 0;
        var resolvedJudgments = 0;
        var offeredWithoutFixture = 0;
        string? lastFailure = null;
        string? lastOfferedFailure = null;
        for (var seed = 1; seed <= 8_192; seed++)
        {
            GameEngine game;
            int targetSeat;
            try
            {
                (game, targetSeat) = FindMaChaoSlashFixture(
                    registry,
                    seed,
                    GameCheckpoint.CurrentRulesVersion);
            }
            catch (InvalidOperationException error)
            {
                lastFailure = error.Message;
                if (error.Message != "Ma Chao was not offered.")
                {
                    offeredWithoutFixture++;
                    lastOfferedFailure = error.Message;
                }
                continue;
            }
            attempted++;

            if (game.PendingDecision is not { Kind: DecisionKind.Tieqi } prompt)
            {
                continue;
            }
            tieqiPrompts++;

            var probe = GameReplay.Restore(game.CreateCheckpoint(), registry);
            var probePrompt = probe.PendingDecision!;
            var used = probe.Submit(new AnswerPromptCommand(
                0,
                probePrompt.PromptId,
                probePrompt.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "tieqi-use").Id,
                probe.Revision));
            if (!used.Accepted || probe.PendingDecision?.Kind == DecisionKind.Guicai)
            {
                continue;
            }

            var judgment = probe.Events.Select(item => item.Payload)
                .OfType<JudgmentResolvedEvent>()
                .LastOrDefault(item => item.Reason == JudgmentReasons.Tieqi);
            if (judgment is not null) resolvedJudgments++;
            if (judgment is not null && judgment.Succeeded == requireRedJudgment)
            {
                return (game, prompt, targetSeat, seed);
            }
        }

        throw new InvalidOperationException(
            $"Could not find a deterministic Ma Chao Tieqi fixture for a " +
            $"{(requireRedJudgment ? "red" : "black")} judgment " +
            $"(attempted={attempted}, offered-failures={offeredWithoutFixture}, prompts={tieqiPrompts}, " +
            $"judgments={resolvedJudgments}, last={lastFailure}, offered-last={lastOfferedFailure}).");
    }

    private static (GameEngine Game, int TargetSeat) FindMaChaoSlashFixture(
        ContentRegistry registry,
        int seed,
        int rulesVersion)
    {
        var game = CreateInteractive(registry, seed);
        if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
        {
            game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
        }

        var started = game.Submit(new StartGameCommand());
        Require(started.Accepted, started.Error?.Message ?? "Ma Chao fixture failed to start.");
        if (game.PendingDecision?.Choices.Any(choice =>
                choice.ContentIds.SequenceEqual(["classic:ma-chao"])) != true)
        {
            throw new InvalidOperationException("Ma Chao was not offered.");
        }

        var selected = game.Submit(new SelectGeneralCommand(
            0,
            "classic:ma-chao",
            game.Revision,
            game.PendingDecision.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Ma Chao.");
        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted, advanced.Error?.Message ?? "Ma Chao did not reach the play phase.");
        if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
        {
            throw new InvalidOperationException("Ma Chao did not stop at a PlayCard decision.");
        }

        var full = game.CreateSnapshot(0, revealAll: true);
        Require(game.GetCombatDistance(0, 2) == 1,
            "Classic Ma Chao must reduce the public distance-two seat to distance one through Mashu.");
        var action = game.GetHumanLegalActions()
            .Where(candidate => candidate.Kind == LegalActionKind.Slash &&
                                candidate.CardId is not null &&
                                candidate.TargetSeat is { } targetSeat &&
                                full.Players.Single(player => player.Seat == targetSeat).Hand.Any(card =>
                                    card.Kind == CardKind.Dodge))
            .OrderBy(candidate => candidate.CardId)
            .ThenBy(candidate => candidate.TargetSeat)
            .FirstOrDefault() ??
            throw new InvalidOperationException("Ma Chao has no Slash target holding Dodge.");
        var target = action.TargetSeat!.Value;
        var played = game.Submit(new PlayCardCommand(
            0,
            action.CardId!.Value,
            action.TargetSeats,
            game.Revision,
            game.PendingDecision.PromptId,
            action.PlayedCardKind,
            action.TargetCardId));
        Require(played.Accepted, played.Error?.Message ?? "Ma Chao could not use Slash.");
        return (game, target);
    }

    private static (GameEngine Game, LegalAction Action) FindHuangYueyingOrdinaryTrickFixture(
        ContentRegistry registry,
        int rulesVersion,
        bool requireNullification)
    {
        for (var seed = 1; seed <= 8_192; seed++)
        {
            var game = CreateInteractive(registry, seed);
            if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
            {
                game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
            }

            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Huang Yueying fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:huang-yueying"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:huang-yueying",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Huang Yueying.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "Huang Yueying did not reach the play phase.");
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
            {
                continue;
            }

            var hand = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hand;
            if (requireNullification && hand.All(card => card.Kind != CardKind.Nullification))
            {
                continue;
            }

            var action = game.GetHumanLegalActions()
                .Where(candidate => candidate.CardId is not null && candidate.Kind is
                    LegalActionKind.DrawTwo or
                    LegalActionKind.BarbarianAssault or
                    LegalActionKind.ArrowBarrage or
                    LegalActionKind.PeachGarden or
                    LegalActionKind.FiveGrains or
                    LegalActionKind.IronChain or
                    LegalActionKind.Dismantlement or
                    LegalActionKind.Snatch or
                    LegalActionKind.FireAttack or
                    LegalActionKind.Duel)
                .OrderBy(candidate => candidate.Kind == LegalActionKind.DrawTwo ? 0 : 1)
                .ThenBy(candidate => candidate.CardId)
                .FirstOrDefault();
            if (action is not null)
            {
                return (game, action);
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Huang Yueying ordinary-trick fixture.");
    }

    private static GameEngine FindHuangYueyingNullificationJizhiFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 8_192; seed++)
        {
            GameEngine game;
            LegalAction action;
            try
            {
                (game, action) = FindHuangYueyingOrdinaryTrickFixtureForSeed(registry, seed);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            var played = game.Submit(new PlayCardCommand(
                0,
                action.CardId!.Value,
                action.TargetSeats,
                game.Revision,
                game.PendingDecision!.PromptId,
                action.PlayedCardKind,
                action.TargetCardId));
            if (!played.Accepted || game.PendingDecision is not { Kind: DecisionKind.Jizhi } initialJizhi)
            {
                continue;
            }

            var skipped = game.Submit(new AnswerPromptCommand(
                0,
                initialJizhi.PromptId,
                initialJizhi.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "jizhi-skip").Id,
                game.Revision));
            if (!skipped.Accepted)
            {
                continue;
            }

            for (var step = 0; step < 128; step++)
            {
                if (game.PendingDecision is
                    {
                        Kind: DecisionKind.Nullification,
                        PlayerSeat: 0
                    } nullificationPrompt)
                {
                    var choice = nullificationPrompt.Choices.FirstOrDefault(candidate =>
                        candidate.Parameters.GetValueOrDefault("response") == "nullification");
                    if (choice is null)
                    {
                        break;
                    }

                    var used = game.Submit(new AnswerPromptCommand(
                        0,
                        nullificationPrompt.PromptId,
                        choice.Id,
                        game.Revision));
                    if (used.Accepted && game.PendingDecision?.Kind == DecisionKind.Jizhi)
                    {
                        return game;
                    }
                    break;
                }

                if (!game.ResolutionStack.Any(frame => frame is NullificationWindowFrame))
                {
                    break;
                }

                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                if (!advanced.Accepted)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException("Could not find a deterministic Nullification-triggered Jizhi fixture.");
    }

    private static (GameEngine Game, LegalAction Action) FindHuangYueyingOrdinaryTrickFixtureForSeed(
        ContentRegistry registry,
        int seed)
    {
        var game = CreateInteractive(registry, seed);
        var started = game.Submit(new StartGameCommand());
        Require(started.Accepted, started.Error?.Message ?? "Huang Yueying Nullification fixture failed to start.");
        if (game.PendingDecision?.Choices.Any(choice =>
                choice.ContentIds.SequenceEqual(["classic:huang-yueying"])) != true)
        {
            throw new InvalidOperationException("Huang Yueying was not offered.");
        }

        var selected = game.Submit(new SelectGeneralCommand(
            0,
            "classic:huang-yueying",
            game.Revision,
            game.PendingDecision.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Huang Yueying.");
        var advanced = game.Submit(new AdvanceCommand(game.Revision));
        Require(advanced.Accepted && game.PendingDecision?.Kind == DecisionKind.PlayCard,
            advanced.Error?.Message ?? "Huang Yueying did not reach play for Nullification.");
        var hand = game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0).Hand;
        if (hand.All(card => card.Kind != CardKind.Nullification))
        {
            throw new InvalidOperationException("Huang Yueying has no Nullification.");
        }

        var action = game.GetHumanLegalActions().FirstOrDefault(candidate =>
            candidate.CardId is not null && candidate.Kind is
                LegalActionKind.DrawTwo or
                LegalActionKind.BarbarianAssault or
                LegalActionKind.ArrowBarrage or
                LegalActionKind.PeachGarden or
                LegalActionKind.FiveGrains or
                LegalActionKind.IronChain or
                LegalActionKind.Dismantlement or
                LegalActionKind.Snatch or
                LegalActionKind.FireAttack or
                LegalActionKind.Duel) ??
            throw new InvalidOperationException("Huang Yueying has no ordinary trick action.");
        return (game, action);
    }

    private static void ContinueLuoshenUntilPlay(GameEngine game)
    {
        for (var step = 0; step < 256 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
        {
            CommandResult result;
            if (game.PendingDecision is { Kind: DecisionKind.Luoshen } prompt)
            {
                result = game.Submit(new AnswerPromptCommand(
                    0,
                    prompt.PromptId,
                    prompt.Choices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("action") == "luoshen-use").Id,
                    game.Revision));
            }
            else
            {
                result = game.Submit(new AdvanceCommand(game.Revision));
            }

            Require(result.Accepted, result.Error?.Message ?? "Could not continue the Luoshen chain.");
        }

        Require(game.State.Phase == TurnPhase.Play &&
                game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "The bounded Luoshen chain did not reach the play phase.");
    }

    private static void Equip(GameEngine game, int cardId)
    {
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Equipment fixture lost its play prompt.");
        var equipped = game.Submit(new PlayCardCommand(
            0,
            cardId,
            [],
            game.Revision,
            prompt.PromptId));
        Require(equipped.Accepted, equipped.Error?.Message ?? "Could not equip the Zhiheng fixture card.");
        if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
        {
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ??
                "Could not return the Zhiheng fixture to the human play boundary.");
        }
        Require(game.CreateSnapshot(0, revealAll: true).Players.Single(player => player.Seat == 0)
                .Equipment.Any(card => card.Id == cardId),
            "The Zhiheng fixture card did not enter the equipment zone.");
    }

    private static GameEngine SelectGeneral(ContentRegistry registry, string generalId, int rulesVersion)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateInteractive(registry, seed);
            if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
                game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Classic selection fixture failed to start.");
            if (started.Result.PendingDecision?.Choices.Any(choice => choice.ContentIds.SequenceEqual([generalId])) != true)
                continue;
            var selected = game.Submit(new SelectGeneralCommand(
                0,
                generalId,
                game.Revision,
                game.PendingDecision!.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? $"Could not select {generalId}.");
            return game;
        }

        throw new InvalidOperationException($"No deterministic selection fixture exposed {generalId}.");
    }

    private static (
        GameEngine Game,
        int ProviderSeat,
        int PeachCardId,
        int SelfPeachCardId,
        int NonWuProviderSeat,
        int NonWuPeachCardId) FindJiuyuanFixture(
        ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateInteractive(registry, seed);
            var started = game.Submit(new StartGameCommand());
            Require(started.Accepted, started.Error?.Message ?? "Jiuyuan fixture failed to start.");
            if (game.PendingDecision?.Choices.Any(choice =>
                    choice.ContentIds.SequenceEqual(["classic:sun-quan"])) != true)
            {
                continue;
            }

            var selected = game.Submit(new SelectGeneralCommand(
                0,
                "classic:sun-quan",
                game.Revision,
                game.PendingDecision.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Could not select classic Sun Quan.");
            var advanced = game.Submit(new AdvanceCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ??
                "The Jiuyuan fixture could not finish AI general selection.");
            var players = game.CreateSnapshot(0, revealAll: true).Players;
            var selfPeach = players.Single(player => player.Seat == 0).Hand
                .FirstOrDefault(card => card.Kind == CardKind.Peach);
            var provider = players
                .Where(player => player.Seat != 0 &&
                                 string.Equals(
                                     registry.Generals[player.GeneralId].FactionId,
                                     "wu",
                                     StringComparison.Ordinal))
                .Select(player => new
                {
                    player.Seat,
                    Peach = player.Hand.FirstOrDefault(card => card.Kind == CardKind.Peach)
                })
                .FirstOrDefault(candidate => candidate.Peach is not null);
            var nonWuProvider = players
                .Where(player => player.Seat != 0 &&
                                 !string.Equals(
                                     registry.Generals[player.GeneralId].FactionId,
                                     "wu",
                                     StringComparison.Ordinal))
                .Select(player => new
                {
                    player.Seat,
                    Peach = player.Hand.FirstOrDefault(card => card.Kind == CardKind.Peach)
                })
                .FirstOrDefault(candidate => candidate.Peach is not null);
            if (provider is not null && selfPeach is not null && nonWuProvider is not null)
            {
                return (
                    game,
                    provider.Seat,
                    provider.Peach!.Id,
                    selfPeach.Id,
                    nonWuProvider.Seat,
                    nonWuProvider.Peach!.Id);
            }
        }

        throw new InvalidOperationException("No deterministic Jiuyuan fixture exposed a Wu provider with Peach.");
    }

    private static void ApplySyntheticDyingPeach(
        GameEngine game,
        int providerSeat,
        int peachCardId)
    {
        var playersField = typeof(GameEngine).GetField(
            "_players",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine player store was not found.");
        var players = (System.Collections.IList)playersField.GetValue(game)!;
        var target = players[0]!;
        var provider = players[providerSeat]!;
        target.GetType().GetProperty("Hp")!.SetValue(target, 0);

        var getHand = typeof(GameEngine).GetMethod(
            "GetHand",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine hand accessor was not found.");
        var providerHand = ((System.Collections.IEnumerable)getHand.Invoke(game, [provider])!)
            .Cast<Card>();
        var peach = providerHand.Single(card => card.Id == peachCardId);
        var resolvePeach = typeof(GameEngine).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == "ResolvePeach" && method.GetParameters().Length == 4);
        resolvePeach.Invoke(game, [provider, target, peach, true]);

        var commitEvents = typeof(GameEngine).GetMethod(
            "CommitPendingEvents",
            BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine event commit method was not found.");
        commitEvents.Invoke(game, null);
    }

    private static GameEngine CreateInteractive(
        ContentRegistry registry,
        int seed,
        Role humanRole = Role.Lord) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = humanRole,
            ModeId = "identity:classic-5",
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 220
        }, registry);

    private enum LiegongFixtureKind
    {
        EligibleByHp,
        EligibleByRange,
        Ineligible
    }

    private static EngineRunResult DeclineOrAdvance(GameEngine game, EngineRunResult? result = null)
    {
        var prompt = game.PendingDecision;
        GameCommand command = prompt?.Kind switch
        {
            null => new AdvanceOneStepCommand(game.Revision),
            DecisionKind.PlayCard => new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId),
            DecisionKind.DiscardCards => new DiscardCardsCommand(
                0,
                prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                prompt.PromptId,
                game.Revision),
            DecisionKind.SelectGeneral => new SelectGeneralCommand(
                0,
                prompt.ValidContentIds[0],
                game.Revision,
                prompt.PromptId),
            _ => new AnswerPromptCommand(
                0,
                prompt.PromptId,
                DeclineChoice(prompt).Id,
                game.Revision)
        };
        var accepted = game.Submit(command);
        if (!accepted.Accepted)
            throw new InvalidOperationException(accepted.Error?.Message ?? $"Could not advance from {result?.Status} / {prompt?.Kind}.");
        return accepted.Result;
    }

    private static PromptChoice DeclineChoice(PendingDecision prompt) =>
        prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.Values.Any(value =>
                value.StartsWith("skip", StringComparison.Ordinal) ||
                value is "take-damage" or "no-nullification" or "ganglie-lose-hp"))
        ?? prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0)
        ?? prompt.Choices.First();

    private static IReadOnlyList<string> EventSignatures(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
