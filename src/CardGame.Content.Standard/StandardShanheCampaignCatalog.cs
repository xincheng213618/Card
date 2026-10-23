using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Content.Standard;

/// <summary>Original local scenario inspired by the documented 2024 Shanhe campaign structure.</summary>
public static partial class StandardShanheCampaignCatalog
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<ContentRegistry>> BattleRegistries = new(StringComparer.Ordinal);

    public static ShanheCampaignDefinition CreateDefinition(int difficulty = 1) =>
        WithStartingResourcesContract(CreateLegacyLocalDefinition(difficulty));

    private static ShanheCampaignDefinition CreateLegacyLocalDefinition(int difficulty)
    {
        if (difficulty is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(difficulty));
        var growth = difficulty - 1;
        var rewards = new ShanheRewardDefinition[]
        {
            new("supplies", "军资", Gold: 80) { Description = "获得80铜钱，可在沿途集市购买构筑。" },
            new("medicine", "军医包", Heal: 3) { Description = "恢复3点体力，至多回复至当前上限。" },
            new("vitality", "整军", Heal: 1, Bonuses: new(MaxHp: 1)) { Description = "体力上限+1，并回复1点体力。" },
            new("opening", "筹备", Bonuses: new(InitialHand: 1)) { Description = "今后每场战斗的初始手牌+1。本地战法。" },
            new("draw", "粮道", Bonuses: new(Draw: 1)) { Description = "今后每场战斗的摸牌阶段额外摸1张。本地战法。" },
            new("slash", "连击", Bonuses: new(Slash: 1)) { Description = "今后每场战斗正常出杀次数+1。本地战法。" },
            new("wusheng", "传习·武圣", SkillId: "classic:wusheng") { Description = "获得当前经典版武圣，占用一个额外技能槽。" },
            new("longdan", "传习·龙胆", SkillId: "classic:longdan") { Description = "获得当前经典版龙胆，占用一个额外技能槽。" },
            new("mashu", "传习·马术", SkillId: "classic:mashu") { Description = "获得当前经典版马术，占用一个额外技能槽。" },
            new("qicai", "传习·奇才", SkillId: "classic:qicai") { Description = "获得当前经典版奇才，占用一个额外技能槽。" },
            new("yingzi", "传习·英姿", SkillId: "classic:yingzi") { Description = "获得当前经典版英姿，摸牌阶段多摸一张；占用额外技能槽。" },
            new("jizhi", "传习·集智", SkillId: "classic:jizhi") { Description = "获得当前经典版集智，使用普通锦囊后获得补牌；占用额外技能槽。" },
            new("zhiheng", "传习·制衡", SkillId: "classic:zhiheng") { Description = "获得当前经典版制衡，通过原生技能按钮发动；占用额外技能槽。" },
            new("tianyi", "传习·天义", SkillId: "classic:tianyi") { Description = "获得当前经典版天义，与一名角色拼点；可与赌术构筑配合。" },
            new("tactic-ruoxi", "战法·弱袭") { TacticSkillId = "shanhe:ruoxi", Description = "手牌少于当前体力时，你造成的伤害+1，含技能伤害和铁索传导。不占额外技能槽。" },
            new("tactic-jinshen", "战法·近身术") { TacticSkillId = "shanhe:jinshen", Description = "你到其他角色的最终距离固定为1。不提供额外出杀次数，不占技能槽。" },
            new("tactic-bingfan", "战法·兵反") { TacticSkillId = "shanhe:bingfan", Description = "兵粮寸断判定效果反转；相同效果只反转一次。不占技能槽。" },
            new("tactic-lezhuan", "战法·乐转") { TacticSkillId = "shanhe:lezhuan", Description = "乐不思蜀判定效果反转；相同效果只反转一次。不占技能槽。" },
            new("tactic-dushu", "战法·赌术 I") { TacticSkillId = "shanhe:dushu-1", Description = "有效拼点点数+3，至多K；可搭配天义等原生拼点技能。不占技能槽。" },
            new("tactic-fanci", "战法·反刺") { TacticSkillId = "shanhe:fanci", Description = "每回合首次受到伤害后，对所有敌人各造成1点伤害。不占技能槽。" },
            new("tactic-shixue", "战法·噬血 I") { TacticSkillId = "shanhe:shixue-1", Description = "回复体力后摸1张牌。不占技能槽。" },
            new("tactic-zengshou", "战法·增寿 I") { TacticSkillId = "shanhe:zengshou-1", Description = "每场战斗开始时增加1点空体力上限，不回复体力；该场增加的上限不跨战累加。不占技能槽。" },
            new("tactic-mouming", "战法·谋命") { TacticSkillId = "shanhe:mouming", Description = "每轮你的锦囊首次命中的牌伤改为扣减目标等量体力上限；附加伤不计入。上限归零直接死亡，不进入濒死。不占技能槽。" },
            new("tactic-xiaoming", "战法·削命") { TacticSkillId = "shanhe:xiaoming", Description = "每轮你的杀首次命中的牌伤改为扣减目标等量体力上限；酒及牌伤加成计入，弱袭等附加伤不计入。不占技能槽。" },
            new("tactic-xueren", "战法·血刃") { TacticSkillId = "shanhe:xueren", Description = "实际回复体力后，随机对一名存活敌人造成1点伤害；可与狂骨、噬血等联动。不占技能槽。" },
            new("tactic-zengshou-2", "战法·增寿 II") { TacticSkillId = "shanhe:zengshou-2", Description = "每场战斗开始时增加2点空体力上限，不回复体力，不跨战累加。不占技能槽。" },
            new("tactic-neihong", "战法·内讧") { TacticSkillId = "shanhe:neihong", Description = "敌人对另一名敌人造成伤害时，伤害+1；你不必是伤害双方之一。不占技能槽。" },
            new("tactic-miaoji-1", "战法·妙技 I") { TacticSkillId = "shanhe:miaoji-1", Description = "每个真实回合，你使用的第一张锦囊造成的伤害+1。无伤害锦囊也占次数，附加伤不计入谋命或绝情的转换金额。不占技能槽。" },
            new("tactic-miaoji-2", "战法·妙技 II") { TacticSkillId = "shanhe:miaoji-2", Description = "每个真实回合，你使用的前两张锦囊造成的伤害+1。无懈与非伤害锦囊也占次数，同一张锦囊的多个目标共用序号。不占技能槽。" },
            new("peach", "随身药囊", StartingCards: [new(CardKind.Peach, Suit.Heart, 3)])
                { Description = "每场初始手牌外再获得一张红桃3桃；本地版按普通实体牌流转。" },
            new("duel", "决战手札", StartingCards: [new(CardKind.Duel, Suit.Diamond, 1)])
                { Description = "每场初始手牌外再获得一张方块A决斗。" },
            new("armor", "八卦护身", StartingEquipment: [new(CardKind.BaguaFormation, Suit.Spade, 2)])
                { Description = "每场开始装备八卦阵；同槽新装备替换已有开场装备。" },
            new("blade", "青釭利刃", StartingEquipment: [new(CardKind.QinggangSword, Suit.Spade, 6)])
                { Description = "每场开始装备青釭剑；同槽新装备替换已有开场装备。" },
        }.Concat(StandardShanheOriginalSkills.CreateRewards()).ToArray();
        string[] spoils = ["medicine", "vitality", "opening", "draw", "slash", "wusheng", "longdan", "mashu", "qicai", "yingzi", "jizhi", "zhiheng", "tianyi", "peach", "duel", "tactic-ruoxi", "tactic-jinshen", "tactic-bingfan", "tactic-lezhuan", "tactic-dushu", "tactic-fanci", "tactic-shixue", "tactic-zengshou"];
        spoils = spoils.Concat(StandardShanheOriginalSkills.RewardIds)
            .Concat(new[] { "tactic-neihong", "tactic-miaoji-1", "tactic-miaoji-2" }).ToArray();
        var advancedSpoils = spoils.Concat(new[] { "tactic-mouming", "tactic-xiaoming", "tactic-xueren", "tactic-zengshou-2" }).ToArray();
        var nodes = new List<ShanheNodeDefinition>
        {
            Departure("hulao-start", "北路出征 · 虎牢", "hulao"),
            Departure("guangcheng-start", "南路出征 · 广成", "guangcheng"),
            Battle("hulao", "第一章 · 虎牢关", "关前旌旗密布。你沿北路接近虎牢，须先击退巡关将领，才能与联军会合。",
                ["village"], "classic:zhang-liao"),
            Battle("guangcheng", "第一章 · 广成关", "山道上散落着逃难者的车辙。你从广成关出发，截住一支离营的西凉军。",
                ["village"], "classic:gao-shun"),
            new("village", "荒村炊烟", ShanheNodeKind.Event, ["market1", "camp1"], Choices:
            [
                new("relief", "分粮救民", GoldCost: 30, RewardId: "medicine") { Description = "花30铜钱换得乡民的药材，恢复3点体力。" },
                new("training", "与乡勇切磋", HpCost: 1, RewardId: "wusheng") { Description = "花1点体力，学习武圣。" },
                new("gather", "协助收集军资", RewardId: "supplies") { Description = "获得80铜钱，继续行军。" }
            ]) { StoryText = "乡老将最后一袋粮食递给你：‘若诸侯真为救民而来，请莫让这炊烟再熄。’你决定如何回应？" },
            Market("market1", "关前集市", ["huaxiong"]),
            Camp("camp1", "联军营地", ["huaxiong"]),
            Battle("huaxiong", "第一章首领 · 汜水扬威", "孙坚的前锋被阻于关外。华雄领兵来战，你与孙坚并肩迎敌。",
                ["dagu", "huanyuan"], "classic:hua-xiong", boss: true, ally: "classic:sun-jian"),
            Battle("dagu", "第二章 · 大谷关", "董军的辎重正从南路撤走。你决定穿过大谷关，截断运粮道路。",
                ["caravan"], "ol:guo-si", rewards: advancedSpoils),
            Battle("huanyuan", "第二章 · 轘辕关", "关道狭窄，敌军借险立阵。你选择东线，与据守关隘的部队交锋。",
                ["rumor"], "classic:gao-shun", rewards: advancedSpoils),
            new("caravan", "失散的商旅", ShanheNodeKind.Event, ["market2", "camp2"], Choices:
            [
                new("escort", "护送商旅", HpCost: 1, RewardId: "supplies") { Description = "花1点体力，获得80铜钱。" },
                new("learn", "询问通关之法", RewardId: "mashu") { Description = "商旅赠你骑术心得，获得马术。" },
                new("leave", "告别继续赶路")
            ]) { StoryText = "一队商旅被困在烧毁的桥边。护送他们会拖慢行军，却也可能带来急需的消息和物资。" },
            new("rumor", "夜闻密议", ShanheNodeKind.Event, ["market2", "camp2"], Choices:
            [
                new("study", "研读遗下的图纸", RewardId: "qicai") { Description = "获得奇才。" },
                new("prepare", "收拢散落军械", RewardId: "opening") { Description = "今后每战初始手牌+1。" },
                new("rest", "不追疑兵，原地休整", RewardId: "medicine")
            ]) { StoryText = "破庙里传来密议，火光却转眼熄灭。你找到一卷残图，也看见通向两支董军的足迹。" },
            Market("market2", "行军集市", ["lijue", "guosi"]),
            Camp("camp2", "河畔营火", ["lijue", "guosi"]),
            Battle("lijue", "第二章首领 · 西凉铁骑", "李傕率军迎面压来。诸侯各有盘算，你只能先破眼前的铁骑，再作决定。",
                ["finalcamp"], "ol:li-jue", boss: true, ally: "classic:sun-jian", rewards: advancedSpoils),
            Battle("guosi", "第二章首领 · 乱军追击", "郭汜借撤军设下埋伏。你护住粮道，与回援的联军夹击乱军。",
                ["finalcamp"], "ol:guo-si", boss: true, ally: "classic:liu-bei", rewards: advancedSpoils),
            Camp("finalcamp", "第三章 · 最后的营火", ["luoyang", "suanzao", "henei"]),
            Battle("luoyang", "洛阳 · 直取董卓", "洛阳火光冲天。你选择直入京畿，与孙坚攻向董卓和李儒的军阵。",
                [], "classic:dong-zhuo", boss: true, ally: "classic:sun-jian", secondEnemy: "classic:li-ru", rewards: advancedSpoils),
            Battle("suanzao", "酸枣 · 重整诸侯", "联军因粮草和指挥争执不休。你折返酸枣，要在军阵演武中说服盟军重新合力。",
                [], "classic:yuan-shao", boss: true, ally: "classic:liu-bei", secondEnemy: "classic:yuan-shu", rewards: advancedSpoils),
            Battle("henei", "河内 · 护民北渡", "你放下追击之名，转向护送百姓渡河。西凉追兵将至，这是通往北岸的最后一战。",
                [], "ol:zhang-ji", boss: true, ally: "classic:cao-cao", secondEnemy: "classic:zhang-liao", rewards: advancedSpoils)
        };
        return WithChapterRewards(new ShanheCampaignDefinition($"shanhe:zhuhou-local-{difficulty}", "山河图 · 诸侯伐董（本地起步版）",
            GetPlayerGeneralIds(),
            ["hulao-start", "guangcheng-start"], nodes, rewards, StartingGold: 90, ExtraSkillSlots: 1,
            StartingBonuses: new(MaxHp: 2, InitialHand: 2, Draw: 1, Slash: 1))
        {
            RandomStartingNode = true,
            Description = $"剧情单机征程 · 本地难度 {difficulty}/3 · 三章分路、奇遇、商店、营地与真实战斗。阵容和数值为本地编排。",
            IntroText = "初平元年，董卓擅权，诸侯举兵。你带着一队义士踏上征程。\n关隘之外有敌军，也有求援者；洛阳、酸枣与河内，将因你的选择留下不同的故事。\n每战保留剩余体力；沿途可恢复、购买战法和习得技能。任一战败则本次征程结束，可以重新出征。",
            VictoryText = "此段征程已结束。洛阳的烽火、酸枣的盟誓、河内的归舟，都曾因你的选择而改变。你可以换一位武将、另一条路线，再走一段不同的山河。",
            DefeatText = "军旗暂歇，义军退守。此番征程虽止，曾经的选择仍可成为下次出征的经验。重新整备，再赴山河。"
        });

        ShanheNodeDefinition Battle(string id, string name, string story, string[] next, string enemy,
            bool boss = false, string? ally = null, string? secondEnemy = null, string[]? rewards = null)
        {
            var enemies = new List<ShanheCombatantDefinition> { new(enemy, MaxHpBonus: growth + (boss ? 1 : 0),
                InitialHandBonus: growth, DrawBonus: difficulty == 3 ? 1 : 0) };
            if (secondEnemy is not null) enemies.Add(new(secondEnemy, MaxHpBonus: growth));
            return new(id, name, boss ? ShanheNodeKind.BossBattle : ShanheNodeKind.Battle, next,
                [new(id + ":encounter", name, enemies, ally is null ? [] : [new(ally, MaxHpBonus: 1)],
                    VictoryGold: boss ? 100 : 55, HealAfterVictory: boss ? 2 : 1)], rewards ?? spoils)
                {
                    StoryText = story,
                    AddSkillSlotsOnVictory = boss && next.Length != 0 ? 1 : 0,
                    RewardSkipGold = 100,
                    VictoryText = id switch
                    {
                        "luoyang" => "【结局 · 洛阳破晓】\n董军败退，城门终于打开。你和孙坚立在余烬之间，扶起一面尚未烧尽的汉旗。天下未定，但京畿百姓等来了这一日的曙光。",
                        "suanzao" => "【结局 · 重续盟誓】\n酸枣军阵收兵。你用这一战赢得诸侯的认可，分散的军旗再次转向西方。有人为名，有人为利，而你终于让他们共同向前一步。",
                        "henei" => "【结局 · 北渡归舟】\n追兵退去，最后一艘渡船驶向北岸。你没有把名字留在洛阳城头，却让一村人的灯火在河内重新亮起。山河之大，也容得下这样的胜利。",
                        _ => ""
                    },
                    Description = $"{(boss ? "首领战" : "遭遇战")} · 胜利获得铜钱与战利品；剩余体力带入下一战。"
                };
        }

        ShanheNodeDefinition Departure(string id, string name, string next) => new(id, name, ShanheNodeKind.Event, [next], Choices:
        [
            new("supplies", "向盟军领取军资", RewardId: "supplies") { Description = "获得80铜钱，为沿途集市作准备。" },
            new("prepare", "与义士整备行装", RewardId: "opening") { Description = "每场战斗的初始手牌+1。" },
            new("train", "操练刀阵", RewardId: "slash") { Description = "每场战斗正常出杀次数+1。" },
            new("tactic", "研习战法·近身术", RewardId: "tactic-jinshen") { Description = "掌握独立战法：你到其他角色的距离固定为1，不占额外技能槽。" }
        ]) { StoryText = "诸侯于酸枣会盟。你接过出征的军令，先为同行的义士作最后一次准备。营门之外，一段属于你的征程正要开始。", Description = "选择一项出征准备，随后进入关隘。" };

        ShanheNodeDefinition Market(string id, string name, string[] next) => new(id, name, ShanheNodeKind.Shop, next, Choices:
        [
            new("buy-medicine", "军医包 · 35钱", 35, RewardId: "medicine", LeavesNode: false),
            new("buy-draw", "粮道 · 100钱", 100, RewardId: "draw", LeavesNode: false),
            new("buy-slash", "连击 · 90钱", 90, RewardId: "slash", LeavesNode: false),
            new("buy-ruoxi", "战法·弱袭 · 100钱", 100, RewardId: "tactic-ruoxi", LeavesNode: false),
            new("buy-fanci", "战法·反刺 · 100钱", 100, RewardId: "tactic-fanci", LeavesNode: false),
            new("buy-dushu", "战法·赌术 I · 80钱", 80, RewardId: "tactic-dushu", LeavesNode: false),
            new("buy-tianyi", "技能·天义 · 80钱", 80, RewardId: "tianyi", LeavesNode: false),
            new("buy-neihong", "战法·内讧 · 60钱", 60, RewardId: "tactic-neihong", LeavesNode: false),
            new("buy-miaoji", "战法·妙技 I · 60钱", 60, RewardId: "tactic-miaoji-1", LeavesNode: false),
            new("buy-armor", "八卦护身 · 100钱", 100, RewardId: "armor", LeavesNode: false),
            new("buy-blade", "青釭利刃 · 80钱", 80, RewardId: "blade", LeavesNode: false),
            .. (id == "market2" ? new ShanheNodeChoiceDefinition[]
            {
                new("buy-mouming", "战法·谋命 · 160钱", 160, RewardId: "tactic-mouming", LeavesNode: false),
                new("buy-xiaoming", "战法·削命 · 180钱", 180, RewardId: "tactic-xiaoming", LeavesNode: false),
                new("buy-xueren", "战法·血刃 · 220钱", 220, RewardId: "tactic-xueren", LeavesNode: false),
                new("buy-miaoji-2", "战法·妙技 II · 100钱", 100, RewardId: "tactic-miaoji-2", LeavesNode: false)
            } : [])
        ]) { StoryText = "商人支起灯笼：‘行军可以缺酒，不能缺粮。将军要什么？’每件货物仅可购买一次。", Description = "花费本次征程的铜钱购买成长，购买后仍可继续挑选。" };

        ShanheNodeDefinition Camp(string id, string name, string[] next) => new(id, name, ShanheNodeKind.Camp, next, Choices:
        [
            new("rest", "疗伤休整", RewardId: "medicine") { Description = "恢复3点体力。" },
            new("train", "操练军阵", HpCost: 1, RewardId: "vitality") { Description = "先花1点体力，体力上限+1并回复1点。" },
            new("prepare", "准备行装", RewardId: "opening") { Description = "今后每场战斗初始手牌+1。" }
        ]) { StoryText = "夜色落在营帐之间。你听见士卒的乡音，思量着明日的道路。趁出发前，再作一次整备。", Description = "选择一种整备后继续前进。" };
    }

    public static ContentRegistry CreateBattleRegistry(ShanheBattleSetup setup)
    {
        setup = ShanheBattleSetup.Freeze(setup);
        if (!setup.RebirthAvailable && setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(member => member.ExtraSkillIds ?? []))
            .Contains(ShanheGeneralRanks.RebirthSkillId, StringComparer.Ordinal))
            throw new InvalidOperationException("The rank-five rebirth skill requires this campaign's unspent rebirth privilege.");
        var ids = new[] { setup.PlayerGeneralId }.Concat(setup.AllyRoster.Select(m => m.GeneralId))
            .Concat(setup.EnemyRoster.Select(m => m.GeneralId)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var codexEquipment = RequiresCodexEquipmentRegistry(setup);
        var currentFarStrike = RequiresCurrentFarStrikeRegistry(setup);
        var currentJinnangJi = RequiresCurrentJinnangJiRegistry(setup);
        var currentDuoduoYishan = RequiresCurrentDuoduoYishanRegistry(setup);
        var currentYaoli = RequiresCurrentYaoliRegistry(setup);
        var currentSlashTactics = RequiresCurrentSlashTacticsRegistry(setup);
        var s5Chengxiong = RequiresChengxiongBossRegistry(setup);
        var s5CaoxingHigh = RequiresCaoxingHighRegistry(setup);
        var s5Shidu = RequiresShiduBossRegistry(setup) || s5CaoxingHigh;
        var s5Huaxiong = RequiresHuaxiongBossRegistry(setup);
        var s5Taishici = RequiresTaishiciBossRegistry(setup);
        var s5Caocao = RequiresCaocaoBossRegistry(setup);
        var s5Yujin = RequiresYujinBossRegistry(setup);
        var s5Yudu = RequiresYuduBossRegistry(setup);
        var s5Liru = RequiresLiruBossRegistry(setup);
        var s5Yuanshao = RequiresThreeBossRegistry(setup, StandardShanheS5YuanshaoPackage.YuanshaoId,
            StandardShanheS5YuanshaoPackage.LuanjiId);
        var s5Zhangliang = RequiresThreeBossRegistry(setup, StandardShanheS5ZhangliangPackage.ZhangliangId,
            StandardShanheS5ZhangliangPackage.BaoyuId, StandardShanheS5ZhangliangPackage.WuyingId);
        var s5Xuchu = RequiresThreeBossRegistry(setup, StandardShanheS5XuchuPackage.XuchuId,
            StandardShanheS5XuchuPackage.LuoyiId);
        var s5Zhuling = RequiresThreeBossRegistry(setup, StandardShanheS5ZhulingPackage.ZhulingId,
            StandardShanheS5ZhulingPackage.JixianId, StandardShanheS5ZhulingPackage.BaoliId);
        var s5YanliangWenchou = RequiresThreeBossRegistry(setup, StandardShanheS5YanliangWenchouPackage.YanliangWenchouId,
            StandardShanheS5YanliangWenchouPackage.ShuangxiongId);
        var s5Quyi = RequiresThreeBossRegistry(setup, StandardShanheS5QuyiPackage.QuyiId,
            StandardShanheS5QuyiPackage.FuqiId, StandardShanheS5QuyiPackage.JiaoziId);
        var s5Zhanghe = RequiresThreeBossRegistry(setup, StandardShanheS5ZhanghePackage.ZhangheId,
            StandardShanheS5ZhanghePackage.ZhouxuanId);
        var s5Yuanshu = RequiresThreeBossRegistry(setup, StandardShanheS5YuanshuPackage.YuanshuId,
            StandardShanheS5YuanshuPackage.TongjiId, StandardShanheS5YuanshuPackage.WuzangId) ||
            ids.Contains(StandardShanheS5YuanshuPackage.YuanyaoId, StringComparer.Ordinal) ||
            ids.Contains(StandardShanheS5YuanshuPackage.YuanyinId, StringComparer.Ordinal);
        var s5Gongsun = RequiresThreeBossRegistry(setup, StandardShanheS5GongsunZanPackage.GongsunZanId,
            StandardShanheS5GongsunZanPackage.QiaomengId, StandardShanheS5GongsunZanPackage.JiuzhuId) ||
            ids.Contains(StandardShanheS5GongsunZanPackage.GongsunYueId, StringComparer.Ordinal);
        var s5Taoyuan = RequiresThreeBossRegistry(setup, StandardShanheS5TaoyuanPackage.ZhangfeiId,
            StandardShanheS5TaoyuanPackage.TishenId) ||
            ids.Contains(StandardShanheS5TaoyuanPackage.LiubeiId, StringComparer.Ordinal) ||
            ids.Contains(StandardShanheS5TaoyuanPackage.GuanyuId, StringComparer.Ordinal);
        var s5Lijue = RequiresThreeBossRegistry(setup, StandardShanheS5LiJuePackage.LijueId,
            StandardShanheS5LiJuePackage.LangxiId);
        var s5Guosi = RequiresThreeBossRegistry(setup, StandardShanheS5GuosiPackage.GuosiId,
            StandardShanheS5GuosiPackage.ZhanhunDaoSkillId) || ShanheRecipeCards(setup).Any(card => card.Kind == CardKind.ZhanhunDao);
        var s5MaSiblings = RequiresThreeBossRegistry(setup, StandardShanheS5MaSiblingsPackage.MachaoId,
            StandardShanheS5MaSiblingsPackage.MashuId, StandardShanheS5MaSiblingsPackage.TieqiId) ||
            ids.Contains(StandardShanheS5MaSiblingsPackage.MayunluId, StringComparer.Ordinal);
        var s5Dongwu = RequiresThreeBossRegistry(setup, StandardShanheS5DongwuVeteransPackage.ChengpuId,
            StandardShanheS5DongwuVeteransPackage.ChunlaoId) || ids.Contains(StandardShanheS5DongwuVeteransPackage.HandangId, StringComparer.Ordinal);
        var s5Moushi = RequiresThreeBossRegistry(setup, StandardShanheS5MoushiPackage.XunchenId,
            StandardShanheS5MoushiPackage.MoushiId, StandardShanheS5MoushiPackage.JiemingId) ||
            ids.Contains(StandardShanheS5MoushiPackage.GuotuId, StringComparer.Ordinal) ||
            ids.Contains(StandardShanheS5MoushiPackage.ShenpeiId, StringComparer.Ordinal);
        var s5Zaiqi = RequiresS5ZaiqiRegistry(setup);
        var key = $"{setup.AllyRoster.Count}/{setup.EnemyRoster.Count}/" + string.Join("|", ids) +
            (codexEquipment ? "/codex-equipment" : "") + (currentFarStrike ? "/current-yuanji-2129" : "") +
            (currentJinnangJi ? "/current-jinnangji-2011" : "") +
            (currentDuoduoYishan ? "/current-duoduo-yishan" : "") +
            (currentYaoli ? "/current-yaoli" : "") +
            (currentSlashTactics ? "/current-slash-tactics" : "") +
            (s5Chengxiong ? "/s5-chengxiong-14055" : "") +
            (s5Shidu ? "/s5-shidu-7003" : "") +
            (s5CaoxingHigh ? "/s5-shihun-14071" : "") +
            (s5Huaxiong ? "/s5-huaxiong-22647" : "") +
            (s5Taishici ? "/s5-taishici-2632" : "") +
            (s5Caocao ? "/s5-caocao-2649" : "") +
            (s5Yujin ? "/s5-yujin-2648" : "") +
            (s5Yudu ? "/s5-yudu-2633" : "") +
            (s5Liru ? "/s5-liru-2668" : "") +
            (s5Yuanshao ? "/s5-yuanshao-2660" : "") +
            (s5Zhangliang ? "/s5-zhangliang-2656" : "") +
            (s5Xuchu ? "/s5-xuchu-2676" : "") +
            (s5Zhuling ? "/s5-zhuling-2664" : "") +
            (s5YanliangWenchou ? "/s5-yanliang-wenchou-2665" : "") +
            (s5Quyi ? "/s5-quyi-2666" : "") +
            (s5Zhanghe ? "/s5-zhanghe-2673" : "") +
            (s5Yuanshu ? "/s5-yuanshu-2654" : "") +
            (s5Gongsun ? "/s5-gongsun-2662" : "") +
            (s5Taoyuan ? "/s5-taoyuan-2683" : "") +
            (s5Lijue ? "/s5-lijue-2636" : "") +
            (s5Guosi ? "/s5-guosi-2637" : "") +
            (s5MaSiblings ? "/s5-ma-siblings-2642" : "") +
            (s5Dongwu ? "/s5-dongwu-2659" : "") +
            (s5Moushi ? "/s5-moushi-2674" : "") +
            (s5Zaiqi ? "/s5-zaiqi-887" : "") +
            (setup.RebirthAvailable ? "/rank-five-rebirth" : "");
        return BattleRegistries.GetOrAdd(key, _ => new Lazy<ContentRegistry>(() =>
        {
            var packages = new List<IGameContentPackage> { new StandardShanheBossPackage() };
            if (ids.Any(id => id == StandardShanheS5BossPackage.ZhangliaoBaseId ||
                id == StandardShanheS5BossPackage.ZhangliaoLingdongId || id == StandardShanheS5BossPackage.ZhangliaoHuangkongId ||
                id == StandardShanheS5BossPackage.DongZhuoLuoyangId || id == StandardShanheS5BossPackage.DongMinBaseId ||
                id == StandardShanheS5BossPackage.DongMinCuanchaoId || id == StandardShanheS5BossPackage.LvbuBajiId ||
                id == StandardShanheS5BossPackage.DiaoChanBiyueId || id == StandardShanheS5BossPackage.DongZhuoFengyiId ||
                id == StandardShanheS5GaichaoPackage.DongZhuoGaichaoId ||
                id == StandardShanheS5DongshiPackage.DongZhuoBaxinId || id == StandardShanheS5DongshiPackage.DongJiQiaoxiaoId))
                packages.Add(new StandardShanheS5BossPackage());
            if (RequiresFengyiBossRegistry(setup)) packages.Add(new StandardShanheS5FengyiPackage());
            if (RequiresGaichaoBossRegistry(setup)) packages.Add(new StandardShanheS5GaichaoPackage());
            if (RequiresDongshiBossRegistry(setup)) packages.Add(new StandardShanheS5DongshiPackage());
            if (RequiresGaoshunBossRegistry(setup)) packages.Add(new StandardShanheS5GaoshunPackage());
            if (RequiresCaoxingBossRegistry(setup)) packages.Add(new StandardShanheS5CaoxingPackage());
            if (s5Shidu) packages.Add(new StandardShanheS5ShiduPackage());
            if (s5CaoxingHigh) packages.Add(new StandardShanheS5CaoxingHighPackage());
            if (s5Huaxiong) packages.Add(new StandardShanheS5HuaxiongPackage());
            if (s5Taishici) packages.Add(new StandardShanheS5TaishiciPackage());
            if (s5Caocao) packages.Add(new StandardShanheS5CaocaoPackage());
            if (s5Yujin) packages.Add(new StandardShanheS5YujinPackage());
            if (s5Yudu) packages.Add(new StandardShanheS5YuduPackage());
            if (s5Liru) packages.Add(new StandardShanheS5LiruPackage());
            if (s5Yuanshao) packages.Add(new StandardShanheS5YuanshaoPackage());
            if (s5Zhangliang) packages.Add(new StandardShanheS5ZhangliangPackage());
            if (s5Xuchu) packages.Add(new StandardShanheS5XuchuPackage());
            if (s5Zhuling) packages.Add(new StandardShanheS5ZhulingPackage());
            if (s5YanliangWenchou) packages.Add(new StandardShanheS5YanliangWenchouPackage());
            if (s5Quyi) packages.Add(new StandardShanheS5QuyiPackage());
            if (s5Zhanghe) packages.Add(new StandardShanheS5ZhanghePackage());
            if (s5Yuanshu) packages.Add(new StandardShanheS5YuanshuPackage());
            if (s5Gongsun) packages.Add(new StandardShanheS5GongsunZanPackage());
            if (s5Taoyuan) packages.Add(new StandardShanheS5TaoyuanPackage());
            if (s5Lijue) packages.Add(new StandardShanheS5LiJuePackage());
            if (s5Guosi) packages.Add(new StandardShanheS5GuosiPackage());
            if (s5MaSiblings) packages.Add(new StandardShanheS5MaSiblingsPackage());
            if (s5Dongwu) packages.Add(new StandardShanheS5DongwuVeteransPackage());
            if (s5Moushi) packages.Add(new StandardShanheS5MoushiPackage());
            // These forms reuse existing skills. The roster, already present
            // in the cache key, is their only additional package requirement.
            if (ids.Contains(StandardShanheS5HuaxiongBasePackage.HuaxiongId, StringComparer.Ordinal))
                packages.Add(new StandardShanheS5HuaxiongBasePackage());
            if (ids.Contains(StandardShanheS5LukuangLuxiangPackage.LukuangLuxiangId, StringComparer.Ordinal))
                packages.Add(new StandardShanheS5LukuangLuxiangPackage());
            if (ids.Contains(StandardShanheS5ChenlinPackage.ChenlinId, StringComparer.Ordinal))
                packages.Add(new StandardShanheS5ChenlinPackage());
            if (ids.Contains(StandardShanheS5ZhouyuPackage.ZhouyuId, StringComparer.Ordinal))
                packages.Add(new StandardShanheS5ZhouyuPackage());
            if (ids.Contains(StandardShanheS5XuyouJushouPackage.XuyouId, StringComparer.Ordinal) ||
                ids.Contains(StandardShanheS5XuyouJushouPackage.JushouId, StringComparer.Ordinal))
                packages.Add(new StandardShanheS5XuyouJushouPackage());
            if (ids.Contains(StandardShanheS5CaorenZhaoyunPackage.CaorenId, StringComparer.Ordinal) ||
                ids.Contains(StandardShanheS5CaorenZhaoyunPackage.ZhaoyunId, StringComparer.Ordinal))
                packages.Add(new StandardShanheS5CaorenZhaoyunPackage());
            if (ids.Contains(StandardShanheS5XujingXuqiuPackage.XujingId, StringComparer.Ordinal) ||
                ids.Contains(StandardShanheS5XujingXuqiuPackage.XuqiuId, StringComparer.Ordinal))
                packages.Add(new StandardShanheS5XujingXuqiuPackage());
            if (s5Chengxiong) packages.Add(new StandardShanheS5ChengxiongPackage());
            if (setup.RebirthAvailable) packages.Add(new StandardShanheRankPrivilegePackage());
            packages.Add(new ShanheEncounterPackage(setup));
            if (codexEquipment) packages.Add(new ShanheCodexEquipmentPackage());
            return ComposedSkillContentRegistry.CreateShowcase(additionalPackages: packages);
        })).Value;
    }

    private static bool RequiresCurrentFarStrikeRegistry(ShanheBattleSetup setup) =>
        setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(member => member.ExtraSkillIds ?? []))
            .Contains(CurrentFarStrikeSkillId, StringComparer.Ordinal);

    private static bool RequiresS5ZaiqiRegistry(ShanheBattleSetup setup) =>
        RequiresFengyiBossRegistry(setup) || setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(member => member.ExtraSkillIds ?? []))
            .Contains(S5ZaiqiSkillId, StringComparer.Ordinal);

    private static bool RequiresCurrentJinnangJiRegistry(ShanheBattleSetup setup) =>
        setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(member => member.ExtraSkillIds ?? []))
            .Contains(CurrentJinnangJiSkillId, StringComparer.Ordinal);

    private static bool RequiresCurrentDuoduoYishanRegistry(ShanheBattleSetup setup) =>
        setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(member => member.ExtraSkillIds ?? []))
            .Any(id => id is CurrentDuoduoYishanSkillId or CurrentDuoduoYishan2SkillId or CurrentDuoduoYishan3SkillId);

    private static bool RequiresCurrentYaoliRegistry(ShanheBattleSetup setup) =>
        setup.TacticSkillIds.Concat(setup.ExtraSkillIds)
            .Concat(setup.AllyRoster.Concat(setup.EnemyRoster).SelectMany(member => member.ExtraSkillIds ?? []))
            .Any(id => id is CurrentYaoliSkillId or CurrentYaoli2SkillId or CurrentYaoli3SkillId or
                CurrentYaoli4SkillId or CurrentYaoli5SkillId or CurrentYaoli6SkillId);

    private static bool RequiresFengyiBossRegistry(ShanheBattleSetup setup) =>
        setup.PlayerGeneralId == StandardShanheS5BossPackage.DongZhuoFengyiId ||
        setup.AllyRoster.Concat(setup.EnemyRoster).Any(member => member.GeneralId == StandardShanheS5BossPackage.DongZhuoFengyiId) ||
        RequiresGaichaoBossRegistry(setup) || RequiresDongshiBossRegistry(setup);

    private static bool RequiresGaichaoBossRegistry(ShanheBattleSetup setup) =>
        setup.PlayerGeneralId == StandardShanheS5GaichaoPackage.DongZhuoGaichaoId ||
        setup.AllyRoster.Concat(setup.EnemyRoster).Any(member => member.GeneralId == StandardShanheS5GaichaoPackage.DongZhuoGaichaoId);

    public static GameEngine CreateBattle(ShanheBattleSetup setup) => GameEngine.CreateStandard(new GameOptions
    {
        Seed = setup.BattleSeed, PlayerCount = 1 + setup.AllyRoster.Count + setup.EnemyRoster.Count,
        HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
        ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
        UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 150,
        AiPolicyVersion = AiDecisionPolicy.CurrentVersion
    }, CreateBattleRegistry(setup));

    private sealed class ShanheEncounterPackage(ShanheBattleSetup setup) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("shanhe-campaign", new Version(1, 0, 0),
            [new("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder)
        {
            EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-passives", skill => skill with { Tags = SkillTag.Locked });
            EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-reactions", skill => skill with { Tags = SkillTag.Locked });
            EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-replacements", skill => skill with { Tags = SkillTag.Locked });
            // Import current source content only when this exact recipe owns it;
            // historical expeditions without this tactic retain their registry.
            if (RequiresCurrentFarStrikeRegistry(setup))
                EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-current-yuanji", skill => skill with
                {
                    Tags = SkillTag.Locked,
                    CampaignTacticAcquisition = new(skill.Id, Repeatable: false)
                });
            if (RequiresS5ZaiqiRegistry(setup) && !RequiresFengyiBossRegistry(setup))
                EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-s5-zaiqi");
            if (RequiresCurrentJinnangJiRegistry(setup))
                EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-current-jinnangji", skill => skill with
                {
                    Tags = SkillTag.Locked,
                    CampaignTacticAcquisition = new(skill.Id, Repeatable: false)
                });
            if (RequiresCurrentDuoduoYishanRegistry(setup))
                EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-current-duoduo-yishan", skill => skill with
                {
                    Tags = SkillTag.Locked,
                    CampaignTacticAcquisition = new(skill.Id, Repeatable: false)
                });
            if (RequiresCurrentYaoliRegistry(setup))
                EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-current-yaoli", skill => skill with
                {
                    Tags = SkillTag.Locked,
                    CampaignTacticAcquisition = new(skill.Id, Repeatable: false)
                });
            if (RequiresCurrentSlashTacticsRegistry(setup))
                EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-current-slash-tactics", skill => skill with
                {
                    Tags = SkillTag.Locked,
                    CampaignTacticAcquisition = new(skill.Id, Repeatable: false)
                });
            EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-team-2026", skill => skill with { Tags = SkillTag.Locked });
            EmbeddedSkillProgramCatalog.RegisterBundle(builder, "shanhe-tactics-2026", skill => skill with
            {
                Tags = SkillTag.Locked,
                CampaignTacticAcquisition = skill.Id is "shanhe:jingyu-moulue-2-2026" or "shanhe:duipai-2026" or "shanhe:duijia-2026"
                    ? new(skill.Id, Repeatable: true)
                    // The source confirms the rule, but not repeated acquisition.
                    // The one-copy expedition limit is an explicit local choice.
                    : skill.Id == "shanhe:duisha-2026" ? new(skill.Id, Repeatable: false) : null
            });
            var ids = new[] { setup.PlayerGeneralId }.Concat(setup.AllyRoster.Select(m => m.GeneralId))
                .Concat(setup.EnemyRoster.Select(m => m.GeneralId)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var count = 1 + setup.AllyRoster.Count + setup.EnemyRoster.Count;
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "山河图 · 剧情战斗", count, count,
                new Dictionary<string, int>(), "classic:standard-deck", GeneralPoolIds: ids,
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int>
                {
                    [ShanheBattleSetup.PlayerTeamId] = 1 + setup.AllyRoster.Count,
                    [ShanheBattleSetup.EnemyTeamId] = setup.EnemyRoster.Count
                }));
        }
    }
}
