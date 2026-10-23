using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheCampaignDefinition WithS5RegionalGroupEncounters(ShanheCampaignDefinition definition) => definition with
    {
        IntroText = definition.IntroText + "\n另有西凉兄妹、东吴宿将与谋士争衡的完整S5基础阵容，以及主组2647的5/5单技华雄。" +
            "马超、马云騄向其他角色距离固定为一，铁骑按判定颜色要求弃尽同色牌；程普的醇至多九张，韩当沿基础弓骑、解烦；" +
            "荀谌、郭图、审配以各自基础技能协作。高难技能与固定牌不加入，位置、抽选及奖励为本地编排。",
        Nodes = definition.Nodes.Select(node => node.Id switch
        {
            "hulao" => AddS5BaseLordGroup(node, "huaxiong-base", "华雄耀武·主组",
                "采用主组2647的5/5华雄，仅有基础耀武：受到红色杀伤害后，来源选择回复一点体力或摸一张牌；与22647的3/3双技形态分别投放。",
                [new(StandardShanheS5HuaxiongBasePackage.HuaxiongId)]),
            "dagu" => AddS5BaseLordGroup(node, "ma-siblings", "西凉兄妹",
                "采用主组2642完整马超、马云騄双敌，各4/4。马术令到其他角色的距离为一；马超的铁骑令目标本回合非锁定技失效，" +
                "判定后需实际弃尽相同颜色牌才能抵消该杀；马云騄保留凤魄。",
                [new(StandardShanheS5MaSiblingsPackage.MachaoId), new(StandardShanheS5MaSiblingsPackage.MayunluId)]),
            "huanyuan" => AddS5BaseLordGroup(AddS5BaseLordGroup(node, "dongwu-veterans", "东吴宿将",
                "采用主组2659完整程普、韩当双敌，各4/4。程普以疠火与至多九张的醇醪应战，韩当保留基础弓骑、解烦。",
                [new(StandardShanheS5DongwuVeteransPackage.ChengpuId), new(StandardShanheS5DongwuVeteransPackage.HandangId)]),
                "moushi", "谋士争衡",
                "采用主组2674完整荀谌、郭图、审配三敌，各3/3。荀谌以锋略、谋识协助下一出牌阶段，郭图以趋势、诿解应对，审配保留刚直、每点伤害可令一人补至上限且至多五张的节命。",
                [new(StandardShanheS5MoushiPackage.XunchenId), new(StandardShanheS5MoushiPackage.GuotuId), new(StandardShanheS5MoushiPackage.ShenpeiId)]),
            _ => node
        }).ToArray()
    };
}
