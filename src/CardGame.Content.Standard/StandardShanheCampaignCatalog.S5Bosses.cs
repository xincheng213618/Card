using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

public static partial class StandardShanheCampaignCatalog
{
    private static ShanheNodeDefinition WithS5LuoyangEncounter(ShanheNodeDefinition node, int difficulty) => WithS5DongshiEncounter(node with
    {
        Name = "洛阳 · 董氏军阵、吕布与貂蝉",
        StoryText = "洛阳火光冲天，你与孙坚突入董氏军阵。本地遭遇可能为四技董卓，也可能为盾弓董旻。董卓以酒池转酒，肉林使双向女性杀需要两闪；酒杀造成伤害后，本回合崩坏失效。其他群角色每造成1点伤害，都可能引发董卓的暴虐判定，黑桃令其回复并取得判定牌。董旻以盾弓防止每次伤害超过3点的部分，再由护甲支付余下伤害；本地第2档起再加入篡朝，阳态可令基本牌无效，阴态可令锦囊牌无效。\n董旻采用当前全站配置的独立技能形态，群势力、本地初始阳、三档技能选择和成长均为本地约定；其原版所属关卡与阶段入口尚未闭合。本地直接投放与同行孙坚不代表董卓变身流程。\n你也可能遭遇霸戟雄英吕布：无双按本场累计使用杀数要求响应，利驭可取得你一个区域的全部牌，若获得装备则由你指定另一角色，吕布对其使用伤害为实际获牌数的决斗。此双技形态原版属于崤函道；本地单独投放并安排孙坚同行，不代表完整的凤仪情仇三敌阵容，也不附加神躯、极武或未经核实的开局装备。\n另一支本地双敌阵容由霸戟雄英与闭月仙伶同行。貂蝉可弃牌令两名男性角色依次决斗，第一人对第二人使用，此决斗不能被无懈但仍可用杀应战；结束阶段空手则摸至手牌上限，否则获得流离直到自己的下一回合开始。双敌与孙坚同行属于本地编排。另有凤仪情仇三敌阵容：覆銮凶影董卓以10/15体力登场，黑色手牌可作酒且不限次数；酒杀造成伤害后永久失去崩坏并获得再起，受伤时可亮已损失体力值加一张牌代替摸牌，红桃回复，其余获得。肉林使所有其他角色的双向杀均需两闪，盗书可猜花色取牌。三敌保持原始基础体力，原版关卡归属崤函道；洛阳位置与孙坚同行为本地编排。\n另一支独立单首领阵容为西凉霸主·改朝罪魁，采用崤函道战斗2687的四技与10/15初始体力，摸牌2、起手4、出杀1、护甲0；所有本地难度均保留这些基础值，该配方未添加固定牌或变身流程。洛阳位置与孙坚同行仍为本地编排，不采用并列22687的5/10。",
        Description = "四技董卓、盾弓董旻、双技霸戟雄英吕布或吕布与闭月仙伶双敌。吕布按累计用杀数量要求响应，利驭取得一个区域全部牌，装备分支按实际获牌数结算决斗。另有包含覆銮凶影董卓的凤仪情仇三敌阵容，董卓黑手牌作酒，酒杀后永久换崩坏为再起，肉林双向两闪。三敌使用原始基础体力，地图位置和孙坚同行仍为本地编排。另有西凉霸主·改朝罪魁单首领，采用2687基础四技与10/15、摸2起手4杀1甲0；所有本地档保留基础值，不混22687的5/10，不添加变身。",
        Encounters =
        [
            new("luoyang:dongzhuo-s5", "洛阳 · 四技董卓",
                [new(StandardShanheS5BossPackage.DongZhuoLuoyangId,
                    MaxHpBonus: difficulty - 1, InitialHandBonus: difficulty - 1, DrawBonus: difficulty == 3 ? 1 : 0)
                { QualifiedPrintedLordSkillIds = [StandardShanheS5BossPackage.DongZhuoBaonueId] }],
                node.Encounters!.Single().Allies, VictoryGold: 100, HealAfterVictory: 2),
            new(difficulty == 1 ? "luoyang:dongmin-current-base" : "luoyang:dongmin-current-cuanchao",
                difficulty == 1 ? "董旻 · 盾弓基础形态（本地投放）" : "董旻 · 盾弓与篡朝技能形态（本地投放）",
                [new(difficulty == 1 ? StandardShanheS5BossPackage.DongMinBaseId : StandardShanheS5BossPackage.DongMinCuanchaoId,
                    MaxHpBonus: difficulty - 1, InitialHandBonus: difficulty - 1, DrawBonus: difficulty == 3 ? 1 : 0)],
                node.Encounters!.Single().Allies, VictoryGold: 100, HealAfterVictory: 2),
            new("luoyang:lvbu-baji-s5", "霸戟雄英 · 双技吕布（本地独立遭遇）",
                [new(StandardShanheS5BossPackage.LvbuBajiId,
                    MaxHpBonus: difficulty - 1, InitialHandBonus: difficulty - 1, DrawBonus: difficulty == 3 ? 1 : 0)],
                node.Encounters!.Single().Allies, VictoryGold: 100, HealAfterVictory: 2),
            new("luoyang:lvbu-diaochan-s5", "霸戟雄英与闭月仙伶（本地双敌遭遇）",
                [new(StandardShanheS5BossPackage.LvbuBajiId,
                    MaxHpBonus: difficulty - 1, InitialHandBonus: difficulty - 1, DrawBonus: difficulty == 3 ? 1 : 0),
                 new(StandardShanheS5BossPackage.DiaoChanBiyueId,
                    MaxHpBonus: difficulty - 1, InitialHandBonus: difficulty - 1, DrawBonus: difficulty == 3 ? 1 : 0)],
                node.Encounters!.Single().Allies, VictoryGold: 100, HealAfterVictory: 2),
            // The complete published enemy roster uses raw base quantities at
            // every local tier. Location and the accompanying Sun Jian are local;
            // no unpublished difficulty formula, phase or equipment zone is inferred.
            new("luoyang:fengyi-s5", "凤仪情仇 · 覆銮凶影、霸戟雄英与闭月仙伶（本地投放）",
                [new(StandardShanheS5BossPackage.DongZhuoFengyiId),
                 new(StandardShanheS5BossPackage.LvbuBajiId),
                 new(StandardShanheS5BossPackage.DiaoChanBiyueId)],
                node.Encounters!.Single().Allies, VictoryGold: 100, HealAfterVictory: 2),
            // The original group 2687 is a separate one-enemy encounter with
            // no fixed cards. All local tiers retain its raw 10/15 quantities;
            // this does not use parallel group 22687 or imply a transformation.
            new("luoyang:gaichao-s5", "西凉霸主 · 改朝罪魁（单首领）",
                [new(StandardShanheS5GaichaoPackage.DongZhuoGaichaoId)],
                node.Encounters!.Single().Allies, VictoryGold: 100, HealAfterVictory: 2)
        ]
    });

    private static IReadOnlyList<ShanheEncounterDefinition> WithS5ZhangliaoEncounter(
        IEnumerable<ShanheEncounterDefinition> encounters, int difficulty)
    {
        // Local three-tier encounters choose a verified skill form; this is not
        // the source's five-band difficulty/rank/HP formula.
        var (encounterId, name, generalId) = difficulty switch
        {
            1 => ("hulao:zhangliao-s5-base", "文远之威 · 基础技能形态", StandardShanheS5BossPackage.ZhangliaoBaseId),
            2 => ("hulao:zhangliao-s5-lingdong", "文远之威 · 灵动技能形态", StandardShanheS5BossPackage.ZhangliaoLingdongId),
            _ => ("hulao:zhangliao-s5-huangkong", "文远之威 · 灵动与惶恐技能形态", StandardShanheS5BossPackage.ZhangliaoHuangkongId)
        };
        return encounters.Append(
            new ShanheEncounterDefinition(encounterId, name,
                [new(generalId,
                    MaxHpBonus: difficulty - 1,
                    InitialHandBonus: difficulty - 1, DrawBonus: difficulty == 3 ? 1 : 0)], [],
                VictoryGold: 100, HealAfterVictory: 2)).ToArray();
    }
}
