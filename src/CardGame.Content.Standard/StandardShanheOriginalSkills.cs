using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

/// <summary>Acquirable variants documented in the original 2024 Zhuhou campaign guide.</summary>
public static class StandardShanheOriginalSkills
{
    private static readonly IReadOnlyList<ShanheRewardDefinition> Rewards = Array.AsReadOnly(new ShanheRewardDefinition[]
    {
        new("original-qiangxi", "传习·强袭", SkillId: "boundary:qiangxi")
        { Description = "出牌阶段限两次：受到1点伤害或弃置一张武器，对本回合未以此法指定的其他角色造成1点伤害。占用额外技能槽。" },
        new("original-shizhan", "传习·势斩", SkillId: "boundary:shizhan-current")
        { Description = "出牌阶段限两次，令一名其他角色视为对你使用决斗。受到的伤害可以触发反刺；占用额外技能槽。" },
        new("original-shenxing", "传习·慎行", SkillId: "classic:shenxing")
        { Description = "出牌阶段可弃置两张牌（含装备区牌），然后摸一张牌。可多次发动；占用额外技能槽。" },
        new("original-lianying", "传习·连营", SkillId: "boundary:lianying")
        { Description = "失去所有手牌后，可令至多X名角色各摸一张牌，X为本次失去的手牌数。占用额外技能槽。" },
        new("original-kuanggu", "传习·狂骨", SkillId: "classic:kuanggu")
        { Description = "锁定技，对距离1以内的角色每造成1点伤害后，回复1点体力。占用额外技能槽。" },
        new("original-qingxian", "传习·清弦", SkillId: "classic:qingxian")
        { Description = "受伤或回复后且无人濒死，可令对应角色失去1体力并随机用装备，或回复1体力并弃装备；相应装备为梅花时你摸1张。占用额外技能槽。" },
        new("original-huituo", "传习·恢拓", SkillId: "classic:huituo")
        { Description = "受伤后可令一名角色判定：红色回复1点体力；黑色摸与此次伤害值等量的牌。占用额外技能槽。" },
        new("original-sanyao", "传习·散谣", SkillId: "classic:sanyao")
        { Description = "出牌阶段对体力最多、手牌最多两种目标各限一次，弃置一张牌对其造成1点伤害。占用额外技能槽。" },
        new("original-jueqing", "传习·绝情", SkillId: "classic:jueqing")
        { Description = "锁定技，你即将造成的伤害视为失去体力。不会再触发伤害后技能；占用额外技能槽。" },
        new("original-kongcheng", "传习·空城", SkillId: "classic:kongcheng")
        { Description = "锁定技，没有手牌时，不能成为杀或决斗的目标。占用额外技能槽。" },
        new("original-yeyan", "传习·业炎", SkillId: "classic:yeyan")
        { Description = "限定技，出牌阶段对至多三名角色分配共计至多3点火焰伤害；若对一人分配至少2点，先弃置四张不同花色手牌并失去3体力。占用额外技能槽。" },
        new("original-jijiu", "传习·急救", SkillId: "classic:jijiu")
        { Description = "你的回合外，可将一张红色牌当桃使用。占用额外技能槽。" },
        new("original-bazhen", "传习·八阵", SkillId: "classic:bazhen")
        { Description = "锁定技，装备区没有防具时，视为装备着八卦阵。占用额外技能槽。" },
        new("original-jishe", "传习·极奢", SkillId: "classic:jishe")
        { Description = "出牌阶段，手牌上限大于0时可摸1张，再令本回合手牌上限减1；结束阶段没有手牌时，可令至多等于体力值的角色进入连环。占用额外技能槽。" },
        new("original-danshou", "传习·胆守", SkillId: "classic:danshou")
        { Description = "出牌阶段第X次发动时，弃X张牌并选攻击范围内一名其他角色：X为1时弃其1牌，2时其交给你1牌，3时对其造成1伤害，至少4时你与其各摸2张。占用额外技能槽。" },
        new("original-beizhan", "传习·备战", SkillId: "ol:beizhan")
        { Description = "回合结束后，可令一人手牌摸至体力上限且至多5张；其下回合开始时若手牌全场最多，本回合不能以其他角色为用牌目标。占用额外技能槽。" },
        new("original-wusheng", "传习·界武圣", SkillId: "boundary:wusheng")
        { Description = "可将一张红色牌当杀使用或打出；你使用的方块杀无距离限制。此为原作所列变体，占用额外技能槽。" }
    });

    public static IReadOnlyList<string> RewardIds { get; } = Array.AsReadOnly(Rewards.Select(reward => reward.Id).ToArray());

    public static IReadOnlyList<ShanheRewardDefinition> CreateRewards() => Rewards;
}
