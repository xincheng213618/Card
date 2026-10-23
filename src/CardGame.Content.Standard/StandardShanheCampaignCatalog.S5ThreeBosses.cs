using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheCampaignDefinition WithS5ThreeBossEncounters(ShanheCampaignDefinition definition) => definition with
    {
        IntroText = definition.IntroText + "\n另有盟首袁绍、隐士张梁与虎痴许褚三个S5基础形态的单敌候选。" +
            "袁绍以两张同色手牌作万箭齐发，并可少选一人；张梁防止火焰伤害，每轮伤害所有其他角色，" +
            "敌方在自己回合获牌后手牌超过10张时受无影伤害；许褚可在摸牌前按当前体力亮牌，选择替代摸牌并固定本次加伤。" +
            "三者的本地地图位置、抽选和奖励均为本地编排，不代表原版章节路线或高难形态。",
        Nodes = definition.Nodes.Select(node => node.Id switch
        {
            "guangcheng" => AddS5BaseBossEncounter(node, "zhangliang", "末路残梁", StandardShanheS5ZhangliangPackage.ZhangliangId,
                "隐士张梁藏身关道。暴雨令其免受火焰伤害，并在每轮开始时伤害所有其他角色；" +
                "敌方在自己回合获牌后手牌超过10张，会受到无影伤害。采用组2656的3/4基础形态。"),
            "dagu" => AddS5BaseBossEncounter(node, "yuanshao", "盟首袁绍", StandardShanheS5YuanshaoPackage.YuanshaoId,
                "袁绍以盟首之名试你兵锋。乱击可将两张同色手牌当万箭齐发，并可少选一个目标。采用组2660的4/4基础形态。"),
            "huanyuan" => AddS5BaseBossEncounter(node, "xuchu", "虎痴许褚", StandardShanheS5XuchuPackage.XuchuId,
                "虎痴许褚横立阵前。裸衣在摸牌前按发动时体力亮牌；他可跳过摸牌并获得其中基本牌、武器及决斗，" +
                "直到下次本人回合开始，杀与决斗造成的伤害增加同一固定值。采用组2676的4/4基础形态。"),
            _ => node
        }).ToArray()
    };

    private static ShanheNodeDefinition AddS5BaseBossEncounter(ShanheNodeDefinition node, string id, string name, string generalId, string story) => node with
    {
        StoryText = node.StoryText + "\n" + story + "三档均不附加成长、固定牌或同行，遭遇位置、抽选和奖励为本地编排。",
        Description = node.Description + "另有S5基础单敌候选：" + name + "。",
        Encounters = node.Encounters!.Append(new ShanheEncounterDefinition(
            node.Id + ":" + id + "-s5", node.Name + " · " + name + "（基础形态）",
            [new(generalId)], [], VictoryGold: 80, HealAfterVictory: 2)).ToArray()
    };

    private static bool RequiresThreeBossRegistry(ShanheBattleSetup setup, string generalId, params string[] skillIds) =>
        new[] { setup.PlayerGeneralId }.Concat(setup.AllyRoster.Concat(setup.EnemyRoster).Select(member => member.GeneralId))
            .Contains(generalId, StringComparer.Ordinal) ||
        setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(member => member.ExtraSkillIds ?? []))
            .Any(id => skillIds.Contains(id, StringComparer.Ordinal));
}
