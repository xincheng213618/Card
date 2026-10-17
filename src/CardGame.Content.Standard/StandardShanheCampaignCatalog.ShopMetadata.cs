using System.Collections.ObjectModel;
using CardGame.Core;

namespace CardGame.Content.Standard;

public sealed record ShanheShopMetadata(
    int SourceRewardId, int Price, int Rarity, string Kind,
    CardKind? CardKind = null, Suit? Suit = null, int? Rank = null);

public static partial class StandardShanheCampaignCatalog
{
    /// <summary>Exact public catalogue rows, keyed by runtime reward id. No quality-to-price inference.</summary>
    public static IReadOnlyDictionary<string, ShanheShopMetadata> ShopMetadataByRewardId => CodexShopMetadataCache.Values;

    /// <summary>Ordinary equipment stock available without a skill-tree unlock in a new codex expedition.</summary>
    public static IReadOnlyList<string> DefaultCodexUnlockedRewardIds { get; } = Array.AsReadOnly(new[]
    {
        "codex-card-4004", "codex-card-4008"
    });

    private static class CodexShopMetadataCache
    {
        // https://s.xiaochao.org/data/skills.json
        // https://s.xiaochao.org/data/tactics.json
        // https://s.xiaochao.org/data/cards.json
        // Tree bindings use the rewardId in shanhe-codex-2026.json. Basic card
        // rows are the exact 4004/4008/4076/4092/4107 faces verified in the client
        // screenshots, not the differently priced 4183+ duplicate card catalogue.
        internal static readonly IReadOnlyDictionary<string, ShanheShopMetadata> Values =
            new ReadOnlyDictionary<string, ShanheShopMetadata>(new Dictionary<string, ShanheShopMetadata>(StringComparer.Ordinal)
            {
                ["codex-3"] = new(3225, 100, 1, "技能"),
                ["codex-4"] = new(3057, 100, 1, "技能"),
                ["codex-5"] = new(3268, 100, 1, "技能"),
                ["codex-6"] = new(3218, 100, 1, "技能"),
                ["codex-7"] = new(3177, 100, 1, "技能"),
                ["codex-9"] = new(3047, 100, 1, "技能"),
                ["codex-10"] = new(3024, 100, 1, "技能"),
                ["codex-13"] = new(4016, 200, 2, "装备", CardKind.Tengjia, Suit.Spade, 2),
                ["codex-14"] = new(3003, 100, 1, "技能"),
                ["codex-15"] = new(4012, 100, 1, "装备", CardKind.IceSword, Suit.Spade, 2),
                ["codex-18"] = new(3178, 100, 1, "技能"),
                ["codex-19"] = new(4009, 200, 2, "装备", CardKind.GudingBlade, Suit.Spade, 1),
                ["codex-21"] = new(4006, 200, 2, "装备", CardKind.ZhangbaSerpentSpear, Suit.Spade, 12),
                ["codex-52"] = new(4010, 200, 2, "装备", CardKind.ZhuqueFan, Suit.Diamond, 1),
                ["codex-54"] = new(4033, 200, 2, "装备", CardKind.CrimsonSoulZither, Suit.Diamond, 1),
                ["codex-57"] = new(4029, 200, 2, "装备", CardKind.LionBelt, Suit.Spade, 2),
                ["codex-66"] = new(4015, 400, 3, "装备", CardKind.RenwangShield, Suit.Club, 2),
                ["codex-88"] = new(4038, 400, 3, "装备", CardKind.DelusionCrown, Suit.Club, 4),
                ["codex-101"] = new(4031, 600, 4, "装备", CardKind.GhostDragonCrescentBlade, Suit.Spade, 5),
                ["codex-115"] = new(4032, 600, 4, "装备", CardKind.NationalStyleRobe, Suit.Spade, 9),
                ["tactic-jinshen"] = new(2302, 200, 1, "战法"),
                ["tactic-ruoxi"] = new(2303, 200, 1, "战法"),
                ["tactic-lezhuan"] = new(2304, 200, 1, "战法"),
                ["tactic-bingfan"] = new(2305, 200, 1, "战法"),
                ["tactic-neihong"] = new(2306, 200, 1, "战法"),
                ["tactic-dushu"] = new(2307, 200, 1, "战法"),
                ["tactic-zengshou"] = new(2310, 300, 2, "战法"),
                ["tactic-zengshou-2"] = new(2311, 500, 3, "战法"),
                ["tactic-miaoji-1"] = new(2312, 200, 1, "战法"),
                ["tactic-miaoji-2"] = new(2313, 200, 1, "战法"),
                ["tactic-xueren"] = new(2314, 700, 4, "战法"),
                ["tactic-shixue"] = new(2315, 500, 3, "战法"),
                ["tactic-fanci"] = new(2317, 300, 2, "战法"),
                ["tactic-mouming"] = new(2301, 300, 2, "战法"),
                ["tactic-xiaoming"] = new(2300, 500, 3, "战法"),
                ["tactic-duichu-2026"] = new(2202, 200, 1, "战法"),
                ["tactic-jiban-2026"] = new(2204, 300, 2, "战法"),
                ["original-qiangxi"] = new(3011, 600, 3, "技能"),
                ["original-shizhan"] = new(3057, 100, 1, "技能"),
                ["original-shenxing"] = new(3192, 100, 1, "技能"),
                ["original-lianying"] = new(3016, 100, 1, "技能"),
                // 3259 is locked classic Kuanggu. Tree row 3048 is the other
                // version (recover OR draw) and must not supply this price.
                ["original-kuanggu"] = new(3259, 600, 3, "技能"),
                ["original-qingxian"] = new(3067, 600, 3, "技能"),
                ["original-huituo"] = new(3085, 600, 3, "技能"),
                ["original-sanyao"] = new(3063, 300, 2, "技能"),
                ["original-jueqing"] = new(3177, 100, 1, "技能"),
                ["original-kongcheng"] = new(3134, 100, 1, "技能"),
                ["original-yeyan"] = new(3065, 300, 2, "技能"),
                ["original-jijiu"] = new(3042, 300, 2, "技能"),
                ["original-bazhen"] = new(3107, 300, 2, "技能"),
                ["original-jishe"] = new(3207, 300, 2, "技能"),
                ["original-danshou"] = new(3015, 300, 2, "技能"),
                ["original-beizhan"] = new(3273, 300, 2, "技能"),
                ["original-wusheng"] = new(3025, 300, 2, "技能"),
                ["wusheng"] = new(3145, 600, 3, "技能"),
                ["mashu"] = new(3097, 100, 1, "技能"),
                ["tianyi"] = new(3272, 300, 2, "技能"),
                ["armor"] = new(4014, 400, 3, "装备", CardKind.BaguaFormation, Suit.Spade, 2),
                ["blade"] = new(4004, 200, 2, "装备", CardKind.QinggangSword, Suit.Spade, 6),
                ["codex-card-4004"] = new(4004, 200, 2, "装备", CardKind.QinggangSword, Suit.Spade, 6),
                ["codex-card-4008"] = new(4008, 100, 1, "装备", CardKind.QilinBow, Suit.Heart, 5),
                ["codex-card-4076"] = new(4076, 250, 3, "牌", CardKind.Peach, Suit.Club, 8),
                ["codex-card-4092"] = new(4092, 150, 1, "牌", CardKind.FiveGrains, Suit.Club, 5),
                ["codex-card-4107"] = new(4107, 200, 2, "牌", CardKind.Lightning, Suit.Heart, 1)
            });
    }
}
