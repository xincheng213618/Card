using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>
/// Opt-in classic identity roster. The legacy standard and demo expansion
/// packages remain unchanged so their checkpoints keep the original content
/// hashes and v1-v9 behavior.
/// </summary>
public sealed class StandardClassicGeneralPackage : IGameContentPackage
{
    private const string SpZhaoYunRulesResource =
        "CardGame.Content.Standard.SkillPrograms.sp-zhao-yun.rules.json";
    private const string SpZhaoYunPresentationResource =
        "CardGame.Content.Standard.SkillPrograms.sp-zhao-yun.presentation.json";
    private const string ClassicZhangJiaoRulesResource =
        "CardGame.Content.Standard.SkillPrograms.classic-zhang-jiao.rules.json";
    private const string ClassicZhangJiaoPresentationResource =
        "CardGame.Content.Standard.SkillPrograms.classic-zhang-jiao.presentation.json";
    private const string BoundaryZhangJiaoRulesResource =
        "CardGame.Content.Standard.SkillPrograms.boundary-zhang-jiao.rules.json";
    private const string BoundaryZhangJiaoPresentationResource =
        "CardGame.Content.Standard.SkillPrograms.boundary-zhang-jiao.presentation.json";
    private const string ClassicShenGuanYuRulesResource =
        "CardGame.Content.Standard.SkillPrograms.classic-shen-guan-yu.rules.json";
    private const string ClassicShenGuanYuPresentationResource =
        "CardGame.Content.Standard.SkillPrograms.classic-shen-guan-yu.presentation.json";
    private static readonly Lazy<SkillProgramCatalog> SpZhaoYunCatalog = new(() =>
        SkillProgramCatalog.Load(
            ReadEmbeddedText(SpZhaoYunRulesResource),
            ReadEmbeddedText(SpZhaoYunPresentationResource)));
    private static readonly Lazy<SkillProgramCatalog> ClassicZhangJiaoCatalog = new(() =>
        SkillProgramCatalog.Load(
            ReadEmbeddedText(ClassicZhangJiaoRulesResource),
            ReadEmbeddedText(ClassicZhangJiaoPresentationResource)));
    private static readonly Lazy<SkillProgramCatalog> BoundaryZhangJiaoCatalog = new(() =>
        SkillProgramCatalog.Load(
            ReadEmbeddedText(BoundaryZhangJiaoRulesResource),
            ReadEmbeddedText(BoundaryZhangJiaoPresentationResource)));
    private static readonly Lazy<SkillProgramCatalog> ClassicShenGuanYuCatalog = new(() =>
        SkillProgramCatalog.Load(
            ReadEmbeddedText(ClassicShenGuanYuRulesResource),
            ReadEmbeddedText(ClassicShenGuanYuPresentationResource)));
    private readonly Version _version;

    public StandardClassicGeneralPackage(bool legacyRoster = false)
        : this(legacyRoster ? new Version(1, 0, 0) : new Version(1, 69, 0))
    {
    }

    public StandardClassicGeneralPackage(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version != new Version(1, 0, 0) &&
            version != new Version(1, 1, 0) &&
            version != new Version(1, 2, 0) &&
            version != new Version(1, 3, 0) &&
            version != new Version(1, 4, 0) &&
            version != new Version(1, 5, 0) &&
            version != new Version(1, 6, 0) &&
            version != new Version(1, 7, 0) &&
            version != new Version(1, 8, 0) &&
            version != new Version(1, 9, 0) &&
            version != new Version(1, 10, 0) &&
            version != new Version(1, 11, 0) &&
            version != new Version(1, 12, 0) &&
            version != new Version(1, 13, 0) &&
            version != new Version(1, 14, 0) &&
            version != new Version(1, 15, 0) &&
            version != new Version(1, 16, 0) &&
            version != new Version(1, 17, 0) &&
            version != new Version(1, 18, 0) &&
            version != new Version(1, 19, 0) &&
            version != new Version(1, 20, 0) &&
            version != new Version(1, 21, 0) &&
            version != new Version(1, 22, 0) &&
            version != new Version(1, 23, 0) &&
            version != new Version(1, 24, 0) &&
            version != new Version(1, 25, 0) &&
            version != new Version(1, 26, 0) &&
            version != new Version(1, 27, 0) &&
            version != new Version(1, 28, 0) &&
            version != new Version(1, 29, 0) &&
            version != new Version(1, 30, 0) &&
            version != new Version(1, 31, 0) &&
            version != new Version(1, 32, 0) &&
            version != new Version(1, 33, 0) &&
            version != new Version(1, 34, 0) &&
            version != new Version(1, 35, 0) &&
            version != new Version(1, 36, 0) &&
            version != new Version(1, 37, 0) &&
            version != new Version(1, 38, 0) &&
            version != new Version(1, 39, 0) &&
            version != new Version(1, 40, 0) &&
            version != new Version(1, 41, 0) &&
            version != new Version(1, 42, 0) &&
            version != new Version(1, 43, 0) &&
            version != new Version(1, 44, 0) &&
            version != new Version(1, 45, 0) &&
            version != new Version(1, 46, 0) &&
            version != new Version(1, 47, 0) &&
            version != new Version(1, 48, 0) &&
            version != new Version(1, 49, 0) &&
            version != new Version(1, 50, 0) &&
            version != new Version(1, 51, 0) &&
            version != new Version(1, 52, 0) &&
            version != new Version(1, 53, 0) &&
            version != new Version(1, 54, 0) &&
            version != new Version(1, 55, 0) &&
            version != new Version(1, 56, 0) &&
            version != new Version(1, 57, 0) &&
            version != new Version(1, 58, 0) &&
            version != new Version(1, 59, 0) &&
            version != new Version(1, 60, 0) &&
            version != new Version(1, 61, 0) &&
            version != new Version(1, 62, 0) &&
            version != new Version(1, 63, 0) &&
            version != new Version(1, 64, 0) &&
            version != new Version(1, 65, 0) &&
            version != new Version(1, 66, 0) &&
            version != new Version(1, 67, 0) &&
            version != new Version(1, 68, 0) &&
            version != new Version(1, 69, 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "Supported classic-general package versions are 1.0.0 through 1.69.0.");
        }

        _version = version;
        Manifest = new PackageManifest(
            Id: "standard-classic-generals",
            Version: version,
            Dependencies: [new PackageDependency("standard-rescue-skills", new Version(1, 0, 0))]);
    }

    public PackageManifest Manifest { get; }

    public void Register(IContentRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (_version >= new Version(1, 23, 0))
        {
            builder.AddCard(new ContentCardDefinition(
                Id: "classic:borrowed-sword",
                DisplayName: "借刀杀人",
                CategoryName: "锦囊牌",
                Description: "选择一名装备武器的其他角色及其攻击范围内的另一名角色；前者需对后者使用一张杀，否则将武器交给你。",
                LegacyKind: CardKind.BorrowedSword,
                AiTags: new Dictionary<string, string>
                {
                    ["action"] = "force-slash-or-take-weapon",
                    ["targets"] = "ordered-two"
                }));
            if (_version >= new Version(1, 24, 0))
            {
                builder.AddCard(new ContentCardDefinition(
                    Id: "classic:stone-axe",
                    DisplayName: "贯石斧",
                    CategoryName: "装备牌",
                    Description: "装备至武器槽；攻击范围 3，当你的杀被闪抵消后，你可以弃置两张牌，令此杀仍造成伤害。",
                    LegacyKind: CardKind.StoneAxe,
                    AiTags: new Dictionary<string, string>
                    {
                        ["slot"] = "weapon",
                        ["trigger"] = "slash-canceled-by-dodge",
                        ["cost"] = "discard-two"
                    }));
            }
            if (_version >= new Version(1, 25, 0))
            {
                builder.AddCard(new ContentCardDefinition(
                    Id: "classic:zhangba-serpent-spear",
                    DisplayName: "丈八蛇矛",
                    CategoryName: "装备牌",
                    Description: "装备至武器槽；攻击范围 3，你可以将两张手牌当一张杀使用或打出。",
                    LegacyKind: CardKind.ZhangbaSerpentSpear,
                    AiTags: new Dictionary<string, string>
                    {
                        ["slot"] = "weapon",
                        ["conversion"] = "two-hand-cards-as-slash"
                    }));
            }
            if (_version >= new Version(1, 26, 0))
            {
                builder.AddCard(new ContentCardDefinition(
                    Id: "classic:cixiong-double-swords",
                    DisplayName: "雌雄双股剑",
                    CategoryName: "装备牌",
                    Description: "装备至武器槽；攻击范围 2，使用杀指定异性目标后，可令其弃一张手牌或令你摸一张牌。",
                    LegacyKind: CardKind.CixiongDoubleSwords,
                    AiTags: new Dictionary<string, string>
                    {
                        ["slot"] = "weapon",
                        ["trigger"] = "opposite-gender-slash-target"
                    }));
            }
            if (_version >= new Version(1, 27, 0))
            {
                builder.AddCard(new ContentCardDefinition(
                    Id: "classic:qinglong-crescent-blade",
                    DisplayName: "青龙偃月刀",
                    CategoryName: "装备牌",
                    Description: "装备至武器槽；攻击范围 3，当你的杀被闪抵消后，可对同一目标再使用一张杀（无距离限制）。",
                    LegacyKind: CardKind.QinglongCrescentBlade,
                    AiTags: new Dictionary<string, string>
                    {
                        ["slot"] = "weapon",
                        ["trigger"] = "slash-canceled-by-dodge",
                        ["continuation"] = "same-target-slash"
                    }));
            }
            if (_version >= new Version(1, 28, 0))
            {
                builder.AddCard(new ContentCardDefinition(
                    Id: "classic:ice-sword",
                    DisplayName: "寒冰剑",
                    CategoryName: "装备牌",
                    Description: "装备至武器槽；攻击范围 2，当你使用杀即将造成伤害且目标有手牌或装备时，可防止此伤害并依次弃置其至多两张牌。",
                    LegacyKind: CardKind.IceSword,
                    AiTags: new Dictionary<string, string>
                    {
                        ["slot"] = "weapon",
                        ["trigger"] = "slash-would-deal-damage",
                        ["replacement"] = "discard-up-to-two-target-cards"
                    }));
            }
            if (_version >= new Version(1, 29, 0))
            {
                builder.AddCard(new ContentCardDefinition(
                    Id: "classic:qilin-bow",
                    DisplayName: "麒麟弓",
                    CategoryName: "装备牌",
                    Description: "装备至武器槽；攻击范围 5，当你使用杀对目标角色造成伤害时，可弃置其装备区的一张坐骑牌。",
                    LegacyKind: CardKind.QilinBow,
                    AiTags: new Dictionary<string, string>
                    {
                        ["slot"] = "weapon",
                        ["trigger"] = "slash-causes-damage",
                        ["effect"] = "discard-target-mount"
                    }));
            }
            if (_version >= new Version(1, 30, 0))
            {
                builder.AddCard(new ContentCardDefinition(
                    Id: "classic:fangtian-halberd",
                    DisplayName: "方天画戟",
                    CategoryName: "装备牌",
                    Description: "装备至武器槽；攻击范围 4，当你使用最后的手牌杀时，可额外指定至多两个合法目标。",
                    LegacyKind: CardKind.FangtianHalberd,
                    AiTags: new Dictionary<string, string>
                    {
                        ["slot"] = "weapon",
                        ["condition"] = "last-hand-slash",
                        ["effect"] = "up-to-two-extra-targets"
                    }));
            }
            if (_version >= new Version(1, 31, 0))
            {
                builder.AddCard(new ContentCardDefinition(
                    Id: "classic:guding-blade",
                    DisplayName: "古锭刀",
                    CategoryName: "装备牌",
                    Description: "装备至武器槽；攻击范围 2，锁定技，当你使用杀对没有手牌的目标造成伤害时，此伤害 +1。",
                    LegacyKind: CardKind.GudingBlade,
                    AiTags: new Dictionary<string, string>
                    {
                        ["slot"] = "weapon",
                        ["trigger"] = "slash-causes-damage-to-empty-hand",
                        ["effect"] = "damage-plus-one"
                    }));
            }
            if (_version >= new Version(1, 32, 0))
            {
                builder.AddCard(new ContentCardDefinition(
                    Id: "classic:zhuque-fan",
                    DisplayName: "朱雀羽扇",
                    CategoryName: "装备牌",
                    Description: "装备至武器槽；攻击范围 4，你可以将使用的普通杀改为火杀。",
                    LegacyKind: CardKind.ZhuqueFan,
                    AiTags: new Dictionary<string, string>
                    {
                        ["slot"] = "weapon",
                        ["trigger"] = "ordinary-slash-use",
                        ["effect"] = "convert-to-fire-slash"
                    }));
            }
            if (_version >= new Version(1, 33, 0))
            {
                builder.AddCard(new ContentCardDefinition(
                    Id: "classic:tengjia",
                    DisplayName: "藤甲",
                    CategoryName: "装备牌",
                    Description: "装备至防具槽；锁定技，南蛮入侵、万箭齐发和普通杀对你无效；你受到的火焰伤害 +1。",
                    LegacyKind: CardKind.Tengjia,
                    AiTags: new Dictionary<string, string>
                    {
                        ["slot"] = "armor",
                        ["immunity"] = "barbarian-arrow-ordinary-slash",
                        ["fire-damage"] = "plus-one"
                    }));
            }
            if (_version >= new Version(1, 34, 0))
            {
                builder.AddCard(new ContentCardDefinition(
                    Id: "classic:silver-lion",
                    DisplayName: "白银狮子",
                    CategoryName: "装备牌",
                    Description: "装备至防具槽；锁定技，受到大于1点的伤害时将伤害值改为1；失去装备区里的白银狮子后回复1点体力。",
                    LegacyKind: CardKind.SilverLion,
                    AiTags: new Dictionary<string, string>
                    {
                        ["slot"] = "armor",
                        ["damage-cap"] = "one",
                        ["on-loss"] = "recover-one"
                    }));
            }
            if (_version >= new Version(1, 35, 0))
            {
                builder.AddCard(new ContentCardDefinition(
                    Id: "classic:wooden-ox",
                    DisplayName: "木牛流马",
                    CategoryName: "装备牌",
                    Description: "出牌阶段限一次，将一张手牌扣置于此牌下，然后可将此牌移动至其他角色的装备区；持有者可使用或打出其中的牌。",
                    LegacyKind: CardKind.WoodenOx,
                    AiTags: new Dictionary<string, string>
                    {
                        ["slot"] = "treasure",
                        ["storage"] = "private-playable-cards",
                        ["transfer"] = "equipment-to-equipment"
                    }));
            }
            if (_version >= new Version(1, 37, 0))
            {
                builder.AddCard(new ContentCardDefinition(
                    "classic:dawan", "大宛", "装备牌",
                    "装备至进攻坐骑槽；你到其他角色的战斗距离 -1。", CardKind.Dawan));
                builder.AddCard(new ContentCardDefinition(
                    "classic:zixing", "紫骍", "装备牌",
                    "装备至进攻坐骑槽；你到其他角色的战斗距离 -1。", CardKind.Zixing));
                builder.AddCard(new ContentCardDefinition(
                    "classic:dilu", "的卢", "装备牌",
                    "装备至防御坐骑槽；其他角色到你的战斗距离 +1。", CardKind.Dilu));
                builder.AddCard(new ContentCardDefinition(
                    "classic:zhaohuangfeidian", "爪黄飞电", "装备牌",
                    "装备至防御坐骑槽；其他角色到你的战斗距离 +1。", CardKind.Zhaohuangfeidian));
            }
            if (_version >= new Version(1, 38, 0))
            {
                builder.AddCard(new ContentCardDefinition(
                    "classic:hualiu", "骅骝", "装备牌",
                    "装备至防御坐骑槽；其他角色到你的战斗距离 +1。", CardKind.Hualiu));
            }

            var classicDeckCards = new List<ContentDeckCardCount>
            {
                new("standard:slash", 18),
                new("standard:dodge", 18),
                new("standard:peach", 10),
                new("standard:duel", 4),
                new("standard:draw_two", 2),
                new("standard:barbarian_assault", 2),
                new("standard:arrow_barrage", 2),
                new("standard:peach_garden", 2),
                new("standard:five_grains", 2),
                new("standard:dismantlement", 2),
                new("standard:snatch", 2),
                new("standard:fire_slash", 2),
                new("standard:thunder_slash", 2),
                new("standard:alcohol", 2),
                new("standard:fire_attack", 2),
                new("standard:crossbow", 2),
                new("standard:bagua", 1),
                new("standard:offensive_horse", 1),
                new("standard:defensive_horse", 1),
                new("standard:jade_seal", 1),
                new("standard:qinggang_sword", 1),
                new("standard:nullification", 2),
                new("standard:iron_chain", 2),
                new("standard:indulgence", 2),
                new("standard:supply_shortage", 2),
                new("standard:lightning", 2),
                new("standard:renwang_shield", 1),
                new("classic:borrowed-sword", 2)
            };
            if (_version >= new Version(1, 24, 0))
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:stone-axe", 1));
            }
            if (_version >= new Version(1, 25, 0))
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:zhangba-serpent-spear", 1));
            }
            if (_version >= new Version(1, 26, 0))
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:cixiong-double-swords", 2));
            }
            if (_version >= new Version(1, 27, 0))
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:qinglong-crescent-blade", 1));
            }
            if (_version >= new Version(1, 28, 0))
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:ice-sword", 1));
            }
            if (_version >= new Version(1, 29, 0))
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:qilin-bow", 1));
            }
            if (_version >= new Version(1, 30, 0))
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:fangtian-halberd", 1));
            }
            if (_version >= new Version(1, 31, 0))
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:guding-blade", 1));
            }
            if (_version >= new Version(1, 32, 0))
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:zhuque-fan", 1));
            }
            if (_version >= new Version(1, 33, 0))
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:tengjia", 1));
            }
            if (_version >= new Version(1, 34, 0))
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:silver-lion", 1));
            }
            if (_version >= new Version(1, 35, 0))
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:wooden-ox", 1));
            }

            var physicalCards = _version >= new Version(1, 38, 0)
                ? CreateMilitaryPhysicalDeck()
                : _version >= new Version(1, 36, 0)
                ? CreateStandardPhysicalDeck(_version >= new Version(1, 37, 0))
                : null;
            if (_version >= new Version(1, 39, 0))
            {
                builder.AddDeck(new ContentDeckRecipe(
                    Id: "classic:standard-108",
                    Name: "经典标准 108 张逐张牌堆",
                    InitialHandSize: 4,
                    DrawPerTurn: 2,
                    Cards: [])
                {
                    PhysicalCards = CreateStandardPhysicalDeck(distinctHorseNames: true)
                });
            }
            builder.AddDeck(new ContentDeckRecipe(
                Id: "classic:standard-deck",
                Name: _version >= new Version(1, 38, 0)
                    ? "经典军争 160 张逐张牌堆"
                    : _version >= new Version(1, 37, 0)
                    ? "经典标准 108 张逐张牌堆（六匹实名坐骑）"
                    : _version >= new Version(1, 36, 0)
                    ? "经典标准 108 张逐张牌堆（含 4 张 EX）"
                    : _version >= new Version(1, 35, 0)
                    ? "经典标准牌堆（含木牛流马等扩展装备）"
                    : _version >= new Version(1, 34, 0)
                    ? "经典标准牌堆（借刀杀人·贯石斧·丈八蛇矛·雌雄双股剑·青龙偃月刀·寒冰剑·麒麟弓·方天画戟·古锭刀·朱雀羽扇·藤甲·白银狮子）"
                    : _version >= new Version(1, 33, 0)
                    ? "经典标准牌堆（借刀杀人·贯石斧·丈八蛇矛·雌雄双股剑·青龙偃月刀·寒冰剑·麒麟弓·方天画戟·古锭刀·朱雀羽扇·藤甲）"
                    : _version >= new Version(1, 32, 0)
                    ? "经典标准牌堆（借刀杀人·贯石斧·丈八蛇矛·雌雄双股剑·青龙偃月刀·寒冰剑·麒麟弓·方天画戟·古锭刀·朱雀羽扇）"
                    : _version >= new Version(1, 31, 0)
                    ? "经典标准牌堆（借刀杀人·贯石斧·丈八蛇矛·雌雄双股剑·青龙偃月刀·寒冰剑·麒麟弓·方天画戟·古锭刀）"
                    : _version >= new Version(1, 30, 0)
                    ? "经典标准牌堆（借刀杀人·贯石斧·丈八蛇矛·雌雄双股剑·青龙偃月刀·寒冰剑·麒麟弓·方天画戟）"
                    : _version >= new Version(1, 29, 0)
                    ? "经典标准牌堆（借刀杀人·贯石斧·丈八蛇矛·雌雄双股剑·青龙偃月刀·寒冰剑·麒麟弓）"
                    : _version >= new Version(1, 28, 0)
                    ? "经典标准牌堆（借刀杀人·贯石斧·丈八蛇矛·雌雄双股剑·青龙偃月刀·寒冰剑）"
                    : _version >= new Version(1, 27, 0)
                    ? "经典标准牌堆（借刀杀人·贯石斧·丈八蛇矛·雌雄双股剑·青龙偃月刀）"
                    : _version >= new Version(1, 26, 0)
                    ? "经典标准牌堆（借刀杀人·贯石斧·丈八蛇矛·雌雄双股剑）"
                    : _version >= new Version(1, 25, 0)
                    ? "经典标准牌堆（借刀杀人·贯石斧·丈八蛇矛）"
                    : _version >= new Version(1, 24, 0)
                    ? "经典标准牌堆（借刀杀人·贯石斧）"
                    : "经典标准牌堆（借刀杀人）",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: physicalCards is null ? classicDeckCards : [])
            {
                PhysicalCards = physicalCards
            });
        }

        builder.AddSkill(new ContentSkillDefinition(
            "classic:feedback",
            "反馈",
            "受到伤害后，你可以获得伤害来源的一张牌。",
            SkillKind.Feedback));
        if (_version >= new Version(1, 1, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:tiandu",
                "天妒",
                "当你的判定牌生效后，你可以获得此牌。",
                SkillKind.Tiandu));
        }
        if (_version >= new Version(1, 2, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:fanjian",
                "反间",
                "出牌阶段限一次，你可以令一名其他角色选择一种花色，令其获得并展示你的一张随机手牌；若花色不同，你对其造成1点伤害。",
                SkillKind.Fanjian));
        }
        if (_version >= new Version(1, 3, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:guanxing",
                "观星",
                "准备阶段，你可以观看牌堆顶的X张牌（X为存活角色数且至多为5），然后以任意顺序置于牌堆顶或牌堆底。",
                SkillKind.Guanxing));
        }
        if (_version >= new Version(1, 4, 0))
        {
            builder.AddSkill(WithStructuredSkillMetadata(new ContentSkillDefinition(
                "classic:hujia",
                "护驾",
                "主公技，当你需要使用或打出【闪】时，你可以令其他魏势力角色依次选择是否打出一张【闪】；视为由你使用或打出。",
                SkillKind.Hujia), SkillTag.Lord, SkillExecutionForm.Trigger));
        }
        if (_version >= new Version(1, 5, 0))
        {
            builder.AddSkill(WithStructuredSkillMetadata(new ContentSkillDefinition(
                "classic:jijiang",
                "激将",
                "主公技，当你需要使用或打出【杀】时，你可以令其他蜀势力角色依次选择是否打出一张【杀】；视为由你使用或打出。",
                SkillKind.Jijiang), SkillTag.Lord, SkillExecutionForm.Trigger));
        }
        if (_version >= new Version(1, 6, 0))
        {
            builder.AddSkill(WithStructuredSkillMetadata(new ContentSkillDefinition(
                "classic:jiuyuan",
                "救援",
                "主公技，锁定技，其他吴势力角色对处于濒死状态的你使用的【桃】回复的体力+1。",
                SkillKind.Jiuyuan), SkillTag.Lord | SkillTag.Locked, SkillExecutionForm.State));
        }
        if (_version >= new Version(1, 8, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:qixi",
                "奇袭",
                "你可以将一张黑色牌当【过河拆桥】使用。",
                SkillKind.Qixi));
        }
        if (_version >= new Version(1, 9, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:keji",
                "克己",
                "若你未于本回合出牌阶段使用或打出过【杀】，你可以跳过弃牌阶段。",
                SkillKind.Keji));
        }
        if (_version >= new Version(1, 10, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:tuxi",
                "突袭",
                "摸牌阶段，你可以改为获得至多两名其他角色的各一张手牌。",
                SkillKind.Tuxi));
        }
        if (_version >= new Version(1, 11, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:luoyi",
                "裸衣",
                "摸牌阶段，你可以少摸一张牌，若如此做，每当你于此回合内使用【杀】或【决斗】对目标角色造成伤害时，此伤害+1。",
                SkillKind.Luoyi));
        }
        if (_version >= new Version(1, 12, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:qiangxi",
                "强袭",
                "出牌阶段限一次，你可以失去1点体力或弃置一张武器牌，并选择你攻击范围内的一名其他角色，对其造成1点伤害。",
                SkillKind.Qiangxi));
        }
        if (_version >= new Version(1, 13, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:duanliang",
                "断粮",
                "你可以将一张黑色基本牌或黑色装备牌当【兵粮寸断】使用；你可以对距离为2的角色使用【兵粮寸断】。",
                SkillKind.Duanliang));
        }
        if (_version >= new Version(1, 14, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:luoshen",
                "洛神",
                "准备阶段开始时，你可以进行判定，若结果为黑色，你可以再次进行判定，直到出现红色的结果，然后你获得所有生效后的黑色判定牌。",
                SkillKind.Luoshen));
            builder.AddSkill(new ContentSkillDefinition(
                "classic:qingguo",
                "倾国",
                "你可以将1张黑色手牌当【闪】使用或打出。",
                SkillKind.Qingguo));
        }
        if (_version >= new Version(1, 15, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:jizhi",
                "集智",
                "每当你使用普通锦囊牌时，你可以摸一张牌。",
                SkillKind.Jizhi));
        }
        if (_version >= new Version(1, 16, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:tieqi",
                "铁骑",
                "每当你使用【杀】指定一名目标角色后，你可以进行判定，若结果为红色，该角色不能使用【闪】响应此【杀】。",
                SkillKind.Tieqi));
        }
        if (_version >= new Version(1, 17, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:liegong",
                "烈弓",
                "当你于出牌阶段内使用【杀】指定一个目标后，若该角色的手牌数不小于你的体力值或不大于你的攻击范围，则你可以令其不能使用【闪】响应此【杀】。",
                SkillKind.Liegong));
        }
        if (_version >= new Version(1, 18, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:kuanggu",
                "狂骨",
                "锁定技，当你对距离1以内的一名角色造成1点伤害后，你回复1点体力。",
                SkillKind.Kuanggu));
        }
        if (_version >= new Version(1, 19, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:wushuang",
                "无双",
                "锁定技，你使用的【杀】需两张【闪】才能抵消；与你【决斗】的角色每次需打出两张【杀】。",
                SkillKind.Wushuang));
        }
        if (_version >= new Version(1, 20, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:paoxiao",
                "咆哮",
                "锁定技，你使用【杀】无次数限制。",
                SkillKind.Paoxiao));
        }
        if (_version >= new Version(1, 21, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:longdan",
                "龙胆",
                "你可以将一张【杀】当【闪】、【闪】当【杀】使用或打出。",
                SkillKind.Longdan));
        }
        if (_version >= new Version(1, 22, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:wusheng",
                "武圣",
                "你可以将一张红色牌当【杀】使用或打出。",
                SkillKind.Wusheng));
        }
        if (_version >= new Version(1, 40, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:guose",
                "国色",
                "你可以将一张方块牌当【乐不思蜀】使用。",
                SkillKind.Guose));
            builder.AddSkill(new ContentSkillDefinition(
                "classic:liuli",
                "流离",
                "当你成为【杀】的目标时，你可以弃置一张牌，将此【杀】转移给你攻击范围内且不是此【杀】使用者的一名其他角色。",
                SkillKind.Liuli));
        }
        if (_version >= new Version(1, 41, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:lijian",
                "离间",
                "出牌阶段限一次，你可以弃置一张牌并选择两名男性角色，视为其中一名角色对另一名角色使用一张不能被无懈可击响应的【决斗】。",
                SkillKind.Lijian));
            builder.AddSkill(new ContentSkillDefinition(
                "classic:biyue",
                "闭月",
                "结束阶段，你可以摸一张牌。",
                SkillKind.Biyue));
        }
        if (_version >= new Version(1, 42, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:jieyin",
                "结姻",
                "出牌阶段限一次，你可以弃置两张手牌并选择一名已受伤的男性角色，令你与其各回复1点体力。",
                SkillKind.Jieyin));
            builder.AddSkill(new ContentSkillDefinition(
                "classic:xiaoji",
                "枭姬",
                "当你失去装备区里的一张牌后，你可以摸两张牌。",
                SkillKind.Xiaoji));
        }
        if (_version >= new Version(1, 43, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:qianxun",
                "谦逊",
                "锁定技，你不能被选择为【顺手牵羊】和【乐不思蜀】的目标。",
                SkillKind.Qianxun));
            builder.AddSkill(new ContentSkillDefinition(
                "classic:lianying",
                "连营",
                "当你失去最后的手牌时，你可以摸一张牌。",
                SkillKind.Lianying));
        }
        if (_version >= new Version(1, 44, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:mengjin",
                "猛进",
                "当你使用的【杀】被目标角色使用的【闪】抵消后，你可以弃置其一张手牌或装备牌。",
                SkillKind.Mengjin));
        }
        if (_version >= new Version(1, 45, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:quhu",
                "驱虎",
                "出牌阶段限一次，你可以与一名体力值大于你的角色拼点：若你赢，其对其攻击范围内由你选择的另一名角色造成1点伤害；若你没赢，其对你造成1点伤害。",
                SkillKind.Quhu));
        }
        if (_version >= new Version(1, 46, 0))
        {
            builder.AddSkill(new ContentSkillDefinition(
                "classic:shuangxiong", "双雄",
                "摸牌阶段，你可以改为判定并获得判定牌；本回合你可以将与判定牌颜色不同的一张手牌当【决斗】使用。",
                SkillKind.Shuangxiong));
        }
        if (_version >= new Version(1, 47, 0))
        {
            builder.AddSkill(new ContentSkillDefinition("classic:bazhen", "八阵",
                "锁定技，若你的装备区里没有防具牌，你视为装备着【八卦阵】。", SkillKind.Bazhen));
            builder.AddSkill(new ContentSkillDefinition("classic:huoji", "火计",
                "你可以将一张红色手牌当【火攻】使用。", SkillKind.Huoji));
            builder.AddSkill(new ContentSkillDefinition("classic:kanpo", "看破",
                "你可以将一张黑色手牌当【无懈可击】使用。", SkillKind.Kanpo));
        }
        if (_version >= new Version(1, 48, 0))
        {
            builder.AddSkill(new ContentSkillDefinition("classic:lianhuan", "连环",
                "出牌阶段，你可以将一张梅花手牌当【铁索连环】使用或重铸。", SkillKind.Lianhuan));
            builder.AddSkill(WithStructuredSkillMetadata(
                new ContentSkillDefinition("classic:niepan", "涅槃",
                    "限定技，当你处于濒死状态时，你可以弃置区域内所有牌，解除连环状态，摸三张牌并将体力回复至3点。", SkillKind.Niepan),
                SkillTag.Limited,
                SkillExecutionForm.Trigger));
        }
        if (_version >= new Version(1, 49, 0))
        {
            builder.AddSkill(new ContentSkillDefinition("classic:tianyi", "天义",
                "出牌阶段限一次，你可以与一名其他角色拼点。若你赢，本回合可额外使用一张【杀】、使用【杀】无距离限制且目标上限+1；若你没赢，本回合不能使用【杀】。", SkillKind.Tianyi));
        }
        if (_version >= new Version(1, 50, 0))
        {
            builder.AddSkill(new ContentSkillDefinition("classic:jushou", "据守",
                "结束阶段，你可以摸三张牌，然后将武将牌翻面。", SkillKind.Jushou));
        }
        if (_version >= new Version(1, 51, 0))
        {
            builder.AddSkill(new ContentSkillDefinition("classic:hongyan", "红颜",
                "锁定技，你的黑桃牌均视为红桃牌。", SkillKind.Hongyan));
            builder.AddSkill(new ContentSkillDefinition("classic:tianxiang", "天香",
                "当你受到伤害时，你可以弃置一张红桃手牌并选择一名其他角色，防止此伤害并令其受到等量伤害，然后其摸等同于其已损失体力值的牌。", SkillKind.Tianxiang));
        }
        if (_version >= new Version(1, 52, 0))
        {
            builder.AddSkill(new ContentSkillDefinition("classic:buqu", "不屈",
                "锁定技，当你处于濒死状态时，将牌堆顶一张牌置于武将牌上，称为“创”；若其点数与已有“创”均不同，你回复至1点体力，否则弃置之。若你有“创”，手牌上限等于“创”的数量。", SkillKind.Buqu));
        }
        if (_version >= new Version(1, 53, 0))
        {
            builder.AddSkill(new ContentSkillDefinition("classic:luanji", "乱击",
                "你可以将两张花色相同的手牌当【万箭齐发】使用。", SkillKind.Luanji));
            builder.AddSkill(WithStructuredSkillMetadata(
                new ContentSkillDefinition("classic:xueyi", "血裔",
                    "主公技，锁定技，你的手牌上限+X（X为其他群势力角色数的两倍）。", SkillKind.Xueyi),
                SkillTag.Lord | SkillTag.Locked,
                SkillExecutionForm.State));
        }
        if (_version >= new Version(1, 54, 0))
        {
            builder.AddSkill(new ContentSkillDefinition("classic:shensu", "神速",
                "你可以选择一项：跳过判定阶段和摸牌阶段，或跳过出牌阶段并弃置一张装备牌；每如此做一次，视为你使用一张无距离限制的【杀】。", SkillKind.Shensu));
        }
        if (_version >= new Version(1, 55, 0))
        {
            builder.AddSkill(new ContentSkillDefinition("classic:yaowu", "耀武",
                "锁定技，当一名角色使用红色【杀】对你造成伤害后，其选择回复1点体力或摸一张牌。", SkillKind.Yaowu));
        }
        if (_version >= new Version(1, 56, 0))
        {
            builder.AddSkill(new ContentSkillDefinition("classic:yicong", "义从",
                "锁定技，若你的体力值大于2，你计算与其他角色的距离-1；若你的体力值不大于2，其他角色计算与你的距离+1。", SkillKind.Yicong));
        }
        if (_version >= new Version(1, 57, 0))
        {
            if (_version >= new Version(1, 65, 0))
            {
                foreach (var (id, program) in ClassicZhangJiaoCatalog.Value.Programs
                             .OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    var presentation = ClassicZhangJiaoCatalog.Value.Presentations[id];
                    var definition = new ContentSkillDefinition(
                        id,
                        presentation.Name,
                        presentation.Description)
                    {
                        Program = program
                    };
                    builder.AddSkill(id == "classic:huangtian"
                        ? WithStructuredSkillMetadata(
                            definition, SkillTag.Lord, SkillExecutionForm.Trigger)
                        : definition);
                }
            }
            else
            {
                builder.AddSkill(new ContentSkillDefinition("classic:guidao", "鬼道",
                    "一名角色的判定牌生效前，你可以打出一张黑色牌替换之。", SkillKind.Guidao));
                builder.AddSkill(new ContentSkillDefinition("classic:leiji", "雷击",
                    "当你使用或打出闪时，你可以令一名其他角色判定：黑桃则你对其造成2点雷电伤害；梅花则你回复1点体力，然后对其造成1点雷电伤害。", SkillKind.Leiji));
                builder.AddSkill(WithStructuredSkillMetadata(
                    new ContentSkillDefinition("classic:huangtian", "黄天",
                        "主公技，其他群势力角色的出牌阶段限一次，其可以将一张闪或闪电交给你。", SkillKind.Huangtian),
                    SkillTag.Lord,
                    SkillExecutionForm.Trigger));
            }
        }
        if (_version >= new Version(1, 66, 0))
        {
            foreach (var (id, program) in BoundaryZhangJiaoCatalog.Value.Programs
                         .OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                var presentation = BoundaryZhangJiaoCatalog.Value.Presentations[id];
                var definition = new ContentSkillDefinition(
                    id,
                    presentation.Name,
                    presentation.Description)
                {
                    Program = program
                };
                builder.AddSkill(id == "boundary:huangtian"
                    ? WithStructuredSkillMetadata(
                        definition, SkillTag.Lord, SkillExecutionForm.Trigger)
                    : definition);
            }
        }
        if (_version >= new Version(1, 67, 0))
        {
            var wushenProgram = ClassicShenGuanYuCatalog.Value.Programs["classic:wushen"];
            var wushenPresentation = ClassicShenGuanYuCatalog.Value.Presentations["classic:wushen"];
            builder.AddSkill(WithStructuredSkillMetadata(new ContentSkillDefinition(
                "classic:wushen",
                wushenPresentation.Name,
                wushenPresentation.Description)
            {
                Program = wushenProgram
            }, SkillTag.Locked, SkillExecutionForm.State));
            builder.AddSkill(WithStructuredSkillMetadata(new ContentSkillDefinition(
                "classic:wuhun",
                "武魂",
                "锁定技，当你受到1点伤害后，你令伤害来源获得1枚“梦魇”标记；当你死亡时，你令“梦魇”标记最多的一名角色进行判定，若结果不为【桃】或【桃园结义】，该角色死亡。",
                SkillKind.Wuhun), SkillTag.Locked, SkillExecutionForm.State));
        }
        if (_version >= new Version(1, 69, 0))
        {
            builder.AddSkill(WithStructuredSkillMetadata(new ContentSkillDefinition(
                "sp:guan-yu-wusheng",
                "武圣",
                "你可以将一张红色牌当【杀】使用或打出；你使用或打出的方块【杀】无距离限制。",
                SkillKind.Wusheng), SkillTag.None, SkillExecutionForm.State));
            builder.AddSkill(WithStructuredSkillMetadata(new ContentSkillDefinition(
                "sp:danji",
                "单骑",
                "觉醒技，准备阶段，若你的手牌数大于体力值且本局主公不为刘备，你减1点体力上限，然后获得【马术】和【怒斩】。"),
                SkillTag.Awakening,
                SkillExecutionForm.Trigger));
            builder.AddSkill(WithStructuredSkillMetadata(new ContentSkillDefinition(
                "sp:guan-yu-mashu",
                "马术",
                "锁定技，你计算与其他角色的距离-1。",
                SkillKind.Mashu), SkillTag.Locked, SkillExecutionForm.State));
            builder.AddSkill(WithStructuredSkillMetadata(new ContentSkillDefinition(
                "sp:nuzhan",
                "怒斩",
                "锁定技，你使用由锦囊牌转化的【杀】不计入出牌阶段使用次数；你使用由装备牌转化的【杀】伤害+1。"),
                SkillTag.Locked,
                SkillExecutionForm.State));
        }
        if (_version >= new Version(1, 58, 0))
        {
            builder.AddSkill(new ContentSkillDefinition("classic:yinghun", "英魂",
                "准备阶段开始时，若你已受伤，你可以令一名其他角色摸X张牌并弃置一张牌，或摸一张牌并弃置X张牌（X为你已损失的体力值）。", SkillKind.Yinghun));
        }
        if (_version >= new Version(1, 59, 0))
        {
            builder.AddSkill(new ContentSkillDefinition("classic:huoshou", "祸首",
                "锁定技，南蛮入侵对你无效；其他角色使用南蛮入侵造成的伤害来源改为你。", SkillKind.Huoshou));
            builder.AddSkill(new ContentSkillDefinition("classic:zaiqi", "再起",
                "摸牌阶段开始时，若你已受伤，你可以放弃摸牌并展示牌堆顶X张牌（X为你已损失的体力值）：每有一张红桃牌，你回复1点体力，然后弃置这些红桃牌并获得其余牌。", SkillKind.Zaiqi));
        }
        if (_version >= new Version(1, 60, 0))
        {
            builder.AddSkill(new ContentSkillDefinition("classic:juxiang", "巨象",
                "锁定技，南蛮入侵对你无效；其他角色使用的南蛮入侵结算完毕置入弃牌堆后，你获得之。", SkillKind.Juxiang));
            builder.AddSkill(new ContentSkillDefinition("classic:lieren", "烈刃",
                "当你使用杀对目标角色造成伤害后，你可以与其拼点；若你赢，你获得其一张牌。", SkillKind.Lieren));
        }
        if (_version >= new Version(1, 61, 0))
        {
            builder.AddSkill(new ContentSkillDefinition("classic:yizhong", "毅重",
                "锁定技，若你的装备区里没有防具牌，黑色的杀对你无效。", SkillKind.Yizhong));
        }
        if (_version >= new Version(1, 62, 0))
        {
            builder.AddSkill(new ContentSkillDefinition("classic:wuyan", "无言",
                "锁定技，当锦囊牌造成伤害时，若你为伤害来源或受伤角色，防止此伤害。", SkillKind.Wuyan));
        }
        if (_version >= new Version(1, 63, 0))
        {
            builder.AddSkill(new ContentSkillDefinition("classic:jujian", "举荐",
                "结束阶段开始时，你可以弃置一张非基本牌并选择一名其他角色，令其选择摸两张牌、回复1点体力或复原武将牌。", SkillKind.Jujian));
        }
        if (_version >= new Version(1, 64, 0))
        {
            foreach (var (id, program) in SpZhaoYunCatalog.Value.Programs
                         .OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                var presentation = SpZhaoYunCatalog.Value.Presentations[id];
                builder.AddSkill(new ContentSkillDefinition(
                    id,
                    presentation.Name,
                    presentation.Description)
                {
                    Program = program
                });
            }
        }

        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:liu-bei",
            "刘备",
            "liu_bei",
            "standard:rende",
            "shu",
            BaseHp: 4,
            AdditionalSkillIds: _version >= new Version(1, 5, 0)
                ? ["classic:jijiang"]
                : null));
        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:sun-quan",
            "孙权",
            "sun_quan",
            "standard:zhiheng",
            "wu",
            BaseHp: 4,
            AdditionalSkillIds: _version >= new Version(1, 6, 0)
                ? ["classic:jiuyuan"]
                : null));
        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:sima-yi",
            "司马懿",
            "sima_yi",
            "classic:feedback",
            "wei",
            BaseHp: 3,
            AdditionalSkillIds: ["standard:guicai"]));
        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:xiahou-dun",
            "夏侯惇",
            "xiahou_dun",
            "standard:ganglie",
            "wei",
            BaseHp: 4));
        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:hua-tuo",
            "华佗",
            "hua_tuo",
            "standard:qingnang",
            "qun",
            BaseHp: 3,
            AdditionalSkillIds: ["standard:jijiu"]));
        if (_version >= new Version(1, 1, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:guo-jia",
                "郭嘉",
                "guo_jia",
                "classic:tiandu",
                "wei",
                BaseHp: 3,
                AdditionalSkillIds: ["standard:yiji"]));
        }
        if (_version >= new Version(1, 2, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhou-yu",
                "周瑜",
                "zhou_yu",
                "standard:yingzi",
                "wu",
                BaseHp: 3,
                AdditionalSkillIds: ["classic:fanjian"]));
        }
        if (_version >= new Version(1, 3, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhuge-liang",
                "诸葛亮",
                "zhuge_liang",
                "classic:guanxing",
                "shu",
                BaseHp: 3,
                AdditionalSkillIds: ["standard:kongcheng"]));
        }
        if (_version >= new Version(1, 4, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:cao-cao",
                "曹操",
                "cao_cao",
                "standard:jianxiong",
                "wei",
                BaseHp: 4,
                AdditionalSkillIds: ["classic:hujia"]));
        }
        if (_version >= new Version(1, 7, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:huang-gai",
                "黄盖",
                "huang_gai",
                "standard:kujin",
                "wu",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 8, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:gan-ning",
                "甘宁",
                "gan_ning",
                "classic:qixi",
                "wu",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 9, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:lu-meng",
                "吕蒙",
                "lu_meng",
                "classic:keji",
                "wu",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 10, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhang-liao",
                "张辽",
                "zhang_liao",
                "classic:tuxi",
                "wei",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 11, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:xu-chu",
                "许褚",
                "xu_chu",
                "classic:luoyi",
                "wei",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 12, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:dian-wei",
                "典韦",
                "dian_wei",
                "classic:qiangxi",
                "wei",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 13, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:xu-huang",
                "徐晃",
                "xu_huang",
                "classic:duanliang",
                "wei",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 14, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhen-ji",
                "甄姬",
                "zhen_ji",
                "classic:luoshen",
                "wei",
                BaseHp: 3,
                AdditionalSkillIds: ["classic:qingguo"],
                Gender: _version >= new Version(1, 26, 0)
                    ? GeneralGender.Female
                    : GeneralGender.Male));
        }
        if (_version >= new Version(1, 15, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:huang-yueying",
                "黄月英",
                "huang_yueying",
                "classic:jizhi",
                "shu",
                BaseHp: 3,
                AdditionalSkillIds: ["standard:qicai"],
                Gender: _version >= new Version(1, 26, 0)
                    ? GeneralGender.Female
                    : GeneralGender.Male));
        }
        if (_version >= new Version(1, 16, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:ma-chao",
                "马超",
                "ma_chao",
                "classic:tieqi",
                "shu",
                BaseHp: 4,
                AdditionalSkillIds: ["standard:mashu"]));
        }
        if (_version >= new Version(1, 17, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:huang-zhong",
                "黄忠",
                "huang_zhong",
                "classic:liegong",
                "shu",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 18, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:wei-yan",
                "魏延",
                "wei_yan",
                "classic:kuanggu",
                "shu",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 19, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:lu-bu",
                "吕布",
                "lu_bu",
                "classic:wushuang",
                "qun",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 20, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhang-fei",
                "张飞",
                "zhang_fei",
                "classic:paoxiao",
                "shu",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 21, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhao-yun",
                "赵云",
                "zhao_yun",
                "classic:longdan",
                "shu",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 22, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:guan-yu",
                "关羽",
                "guan_yu",
                "classic:wusheng",
                "shu",
                BaseHp: 4));
        }
        if (_version >= new Version(1, 40, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:da-qiao",
                "大乔",
                "da_qiao",
                "classic:guose",
                "wu",
                BaseHp: 3,
                AdditionalSkillIds: ["classic:liuli"],
                Gender: GeneralGender.Female));
        }
        if (_version >= new Version(1, 41, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:diao-chan",
                "貂蝉",
                "diao_chan",
                "classic:biyue",
                "qun",
                BaseHp: 3,
                AdditionalSkillIds: ["classic:lijian"],
                Gender: GeneralGender.Female));
        }
        if (_version >= new Version(1, 42, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:sun-shangxiang",
                "孙尚香",
                "sun_shangxiang",
                "classic:jieyin",
                "wu",
                BaseHp: 3,
                AdditionalSkillIds: ["classic:xiaoji"],
                Gender: GeneralGender.Female));
        }
        if (_version >= new Version(1, 43, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:lu-xun",
                "陆逊",
                "lu_xun",
                "classic:qianxun",
                "wu",
                BaseHp: 3,
                AdditionalSkillIds: ["classic:lianying"]));
        }
        if (_version >= new Version(1, 44, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:pang-de",
                "庞德",
                "pang_de",
                "standard:mashu",
                "qun",
                BaseHp: 4,
                AdditionalSkillIds: ["classic:mengjin"]));
        }
        if (_version >= new Version(1, 45, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:xun-yu",
                "荀彧",
                "xun_yu",
                "classic:quhu",
                "wei",
                BaseHp: 3,
                AdditionalSkillIds: ["standard:jieming"]));
        }
        if (_version >= new Version(1, 46, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:yan-liang-wen-chou", "颜良文丑", "yan_liang_wen_chou",
                "classic:shuangxiong", "qun", BaseHp: 4));
        }
        if (_version >= new Version(1, 47, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:wolong-zhuge-liang", "卧龙诸葛亮", "wolong_zhuge_liang",
                "classic:bazhen", "shu", BaseHp: 3,
                AdditionalSkillIds: ["classic:huoji", "classic:kanpo"]));
        }
        if (_version >= new Version(1, 48, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:pang-tong", "庞统", "pang_tong",
                "classic:lianhuan", "shu", BaseHp: 3,
                AdditionalSkillIds: ["classic:niepan"]));
        }
        if (_version >= new Version(1, 49, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:taishi-ci", "太史慈", "taishi_ci",
                "classic:tianyi", "wu", BaseHp: 4));
        }
        if (_version >= new Version(1, 50, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:cao-ren", "曹仁", "cao_ren",
                "classic:jushou", "wei", BaseHp: 4));
        }
        if (_version >= new Version(1, 51, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:xiao-qiao", "小乔", "xiao_qiao",
                "classic:hongyan", "wu", BaseHp: 3,
                AdditionalSkillIds: ["classic:tianxiang"], Gender: GeneralGender.Female));
        }
        if (_version >= new Version(1, 52, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhou-tai", "周泰", "zhou_tai",
                "classic:buqu", "wu", BaseHp: 4));
        }
        if (_version >= new Version(1, 53, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:yuan-shao", "袁绍", "yuan_shao",
                "classic:luanji", "qun", BaseHp: 4,
                AdditionalSkillIds: ["classic:xueyi"]));
        }
        if (_version >= new Version(1, 54, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:xiahou-yuan", "夏侯渊", "xiahou_yuan",
                "classic:shensu", "wei", BaseHp: 4));
        }
        if (_version >= new Version(1, 55, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:hua-xiong", "华雄", "hua_xiong",
                "classic:yaowu", "qun", BaseHp: 6));
        }
        if (_version >= new Version(1, 56, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:gongsun-zan", "公孙瓒", "gongsun_zan",
                "classic:yicong", "qun", BaseHp: 4));
        }
        if (_version >= new Version(1, 57, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhang-jiao", "张角", "zhang_jiao",
                "classic:guidao", "qun", BaseHp: 3,
                AdditionalSkillIds: ["classic:leiji", "classic:huangtian"]));
        }
        if (_version >= new Version(1, 58, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:sun-jian", "孙坚", "sun_jian",
                "classic:yinghun", "wu", BaseHp: 4));
        }
        if (_version >= new Version(1, 59, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:meng-huo", "孟获", "meng_huo",
                "classic:huoshou", "shu", BaseHp: 4,
                AdditionalSkillIds: ["classic:zaiqi"]));
        }
        if (_version >= new Version(1, 60, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhu-rong", "祝融", "zhu_rong",
                "classic:juxiang", "shu", BaseHp: 4,
                AdditionalSkillIds: ["classic:lieren"], Gender: GeneralGender.Female));
        }
        if (_version >= new Version(1, 61, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:yu-jin", "于禁", "yu_jin",
                "classic:yizhong", "wei", BaseHp: 4));
        }
        if (_version >= new Version(1, 63, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:xu-shu", "徐庶", "xu_shu",
                "classic:wuyan", "shu", BaseHp: 3,
                AdditionalSkillIds: ["classic:jujian"]));
        }
        if (_version >= new Version(1, 64, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "sp:zhao-yun", "SP赵云", "zhao_yun",
                "sp:longdan", "qun", BaseHp: 3,
                AdditionalSkillIds: ["sp:chongzhen"]));
        }
        if (_version >= new Version(1, 66, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "boundary:zhang-jiao", "界张角", "boundary_zhang_jiao",
                "boundary:leiji", "qun", BaseHp: 3,
                AdditionalSkillIds: ["boundary:guidao", "boundary:huangtian"]));
        }
        if (_version >= new Version(1, 67, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:shen-guan-yu", "神关羽", "shen_guan_yu",
                "classic:wushen", "god", BaseHp: 5,
                AdditionalSkillIds: ["classic:wuhun"]));
        }
        if (_version >= new Version(1, 69, 0))
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "sp:guan-yu", "SP关羽", "guan_yu",
                "sp:guan-yu-wusheng", "wei", BaseHp: 4,
                AdditionalSkillIds: ["sp:danji"]));
        }

        var generalPoolIds = _version switch
        {
            { Major: 1, Minor: >= 69 } => SpGuanYuClassicGeneralIds,
            { Major: 1, Minor: >= 67 } => ShenGuanYuClassicGeneralIds,
            { Major: 1, Minor: >= 64 } => SpZhaoYunClassicGeneralIds,
            { Major: 1, Minor: >= 63 } => XuShuClassicGeneralIds,
            { Major: 1, Minor: >= 61 } => YuJinClassicGeneralIds,
            { Major: 1, Minor: 60 } => ZhuRongClassicGeneralIds,
            { Major: 1, Minor: 59 } => MengHuoClassicGeneralIds,
            { Major: 1, Minor: 58 } => SunJianClassicGeneralIds,
            { Major: 1, Minor: 57 } => ZhangJiaoClassicGeneralIds,
            { Major: 1, Minor: 56 } => GongsunZanClassicGeneralIds,
            { Major: 1, Minor: 55 } => HuaXiongClassicGeneralIds,
            { Major: 1, Minor: 54 } => XiahouYuanClassicGeneralIds,
            { Major: 1, Minor: 53 } => YuanShaoClassicGeneralIds,
            { Major: 1, Minor: 52 } => ZhouTaiClassicGeneralIds,
            { Major: 1, Minor: 51 } => XiaoQiaoClassicGeneralIds,
            { Major: 1, Minor: 50 } => CaoRenClassicGeneralIds,
            { Major: 1, Minor: 49 } => TaishiCiClassicGeneralIds,
            { Major: 1, Minor: 48 } => PangTongClassicGeneralIds,
            { Major: 1, Minor: 47 } => WolongClassicGeneralIds,
            { Major: 1, Minor: 46 } => YanLiangWenChouClassicGeneralIds,
            { Major: 1, Minor: 45 } => XunYuClassicGeneralIds,
            { Major: 1, Minor: 44 } => PangDeClassicGeneralIds,
            { Major: 1, Minor: 43 } => LuXunClassicGeneralIds,
            { Major: 1, Minor: 42 } => SunShangxiangClassicGeneralIds,
            { Major: 1, Minor: 41 } => DiaoChanClassicGeneralIds,
            { Major: 1, Minor: 40 } => DaQiaoClassicGeneralIds,
            { Major: 1, Minor: 0 } => LegacyClassicGeneralIds,
            { Major: 1, Minor: 1 } => TianduClassicGeneralIds,
            { Major: 1, Minor: 2 } => FanjianClassicGeneralIds,
            { Major: 1, Minor: 3 } => GuanxingClassicGeneralIds,
            { Major: 1, Minor: 4 } => PreHuangGaiClassicGeneralIds,
            { Major: 1, Minor: 5 } => PreHuangGaiClassicGeneralIds,
            { Major: 1, Minor: 6 } => PreHuangGaiClassicGeneralIds,
            { Major: 1, Minor: 7 } => PreGanNingClassicGeneralIds,
            { Major: 1, Minor: 8 } => PreLuMengClassicGeneralIds,
            { Major: 1, Minor: 9 } => PreZhangLiaoClassicGeneralIds,
            { Major: 1, Minor: 10 } => PreXuChuClassicGeneralIds,
            { Major: 1, Minor: 11 } => PreDianWeiClassicGeneralIds,
            { Major: 1, Minor: 12 } => PreXuHuangClassicGeneralIds,
            { Major: 1, Minor: 13 } => PreZhenJiClassicGeneralIds,
            { Major: 1, Minor: 14 } => PreHuangYueyingClassicGeneralIds,
            { Major: 1, Minor: 15 } => PreMaChaoClassicGeneralIds,
            { Major: 1, Minor: 16 } => PreHuangZhongClassicGeneralIds,
            { Major: 1, Minor: 17 } => PreWeiYanClassicGeneralIds,
            { Major: 1, Minor: 18 } => PreLuBuClassicGeneralIds,
            { Major: 1, Minor: 19 } => PreZhangFeiClassicGeneralIds,
            { Major: 1, Minor: 20 } => PreZhaoYunClassicGeneralIds,
            { Major: 1, Minor: 21 } => PreGuanYuClassicGeneralIds,
            _ => ClassicGeneralIds
        };

        builder.AddMode(new ContentModeDefinition(
            Id: "identity:classic-8",
            Name: "八人经典身份（正式武将首批）",
            MinPlayers: 8,
            MaxPlayers: 8,
            RoleCounts: new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 2,
                [nameof(Role.Rebel)] = 4,
                [nameof(Role.Renegade)] = 1
            },
            DeckId: _version >= new Version(1, 23, 0)
                ? "classic:standard-deck"
                : "standard:basic-demo",
            GeneralCandidateCount: 3,
            GeneralPoolIds: generalPoolIds));
        builder.AddMode(new ContentModeDefinition(
            Id: "identity:classic-5",
            Name: "五人经典身份（正式武将首批）",
            MinPlayers: 5,
            MaxPlayers: 5,
            RoleCounts: new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 1,
                [nameof(Role.Rebel)] = 2,
                [nameof(Role.Renegade)] = 1
            },
            DeckId: _version >= new Version(1, 23, 0)
                ? "classic:standard-deck"
                : "standard:basic-demo",
            GeneralCandidateCount: 3,
            GeneralPoolIds: generalPoolIds));
        if (_version >= new Version(1, 66, 0))
        {
            builder.AddMode(new ContentModeDefinition(
                Id: "identity:classic-boundary-8",
                Name: "八人界限突破身份（界张角试验）",
                MinPlayers: 8,
                MaxPlayers: 8,
                RoleCounts: new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 2,
                    [nameof(Role.Rebel)] = 4,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "classic:standard-deck",
                GeneralCandidateCount: 3,
                GeneralPoolIds: BoundaryGeneralIds));
            builder.AddMode(new ContentModeDefinition(
                Id: "identity:classic-boundary-5",
                Name: "五人界限突破身份（界张角试验）",
                MinPlayers: 5,
                MaxPlayers: 5,
                RoleCounts: new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "classic:standard-deck",
                GeneralCandidateCount: 3,
                GeneralPoolIds: BoundaryGeneralIds));
        }
    }

    private static IReadOnlyList<ContentDeckPhysicalCard> CreateStandardPhysicalDeck(bool distinctHorseNames)
    {
        var dawan = distinctHorseNames ? "classic:dawan" : "standard:offensive_horse";
        var zixing = distinctHorseNames ? "classic:zixing" : "standard:offensive_horse";
        var dilu = distinctHorseNames ? "classic:dilu" : "standard:defensive_horse";
        var zhaohuangfeidian = distinctHorseNames
            ? "classic:zhaohuangfeidian"
            : "standard:defensive_horse";
        var cards = new List<ContentDeckPhysicalCard>(108);
        Add(Suit.Heart,
            ("standard:peach_garden", 1), ("standard:arrow_barrage", 1),
            ("standard:dodge", 2), ("standard:dodge", 2),
            ("standard:peach", 3), ("standard:five_grains", 3),
            ("standard:peach", 4), ("standard:five_grains", 4),
            ("classic:qilin-bow", 5), ("standard:offensive_horse", 5),
            ("standard:peach", 6), ("standard:indulgence", 6),
            ("standard:peach", 7), ("standard:draw_two", 7),
            ("standard:peach", 8), ("standard:draw_two", 8),
            ("standard:peach", 9), ("standard:draw_two", 9),
            ("standard:slash", 10), ("standard:slash", 10),
            ("standard:slash", 11), ("standard:draw_two", 11),
            ("standard:peach", 12), ("standard:dismantlement", 12),
            ("standard:lightning", 12),
            ("standard:dodge", 13), (zhaohuangfeidian, 13));
        Add(Suit.Spade,
            ("standard:duel", 1), ("standard:lightning", 1),
            ("classic:cixiong-double-swords", 2), ("standard:bagua", 2),
            ("classic:ice-sword", 2),
            ("standard:dismantlement", 3), ("standard:snatch", 3),
            ("standard:dismantlement", 4), ("standard:snatch", 4),
            ("classic:qinglong-crescent-blade", 5), ("standard:defensive_horse", 5),
            ("standard:indulgence", 6), ("standard:qinggang_sword", 6),
            ("standard:slash", 7), ("standard:barbarian_assault", 7),
            ("standard:slash", 8), ("standard:slash", 8),
            ("standard:slash", 9), ("standard:slash", 9),
            ("standard:slash", 10), ("standard:slash", 10),
            ("standard:snatch", 11), ("standard:nullification", 11),
            ("standard:dismantlement", 12), ("classic:zhangba-serpent-spear", 12),
            ("standard:barbarian_assault", 13), (dawan, 13));
        Add(Suit.Diamond,
            ("standard:crossbow", 1), ("standard:duel", 1),
            ("standard:dodge", 2), ("standard:dodge", 2),
            ("standard:dodge", 3), ("standard:snatch", 3),
            ("standard:dodge", 4), ("standard:snatch", 4),
            ("standard:dodge", 5), ("classic:stone-axe", 5),
            ("standard:slash", 6), ("standard:dodge", 6),
            ("standard:slash", 7), ("standard:dodge", 7),
            ("standard:slash", 8), ("standard:dodge", 8),
            ("standard:slash", 9), ("standard:dodge", 9),
            ("standard:slash", 10), ("standard:dodge", 10),
            ("standard:dodge", 11), ("standard:dodge", 11),
            ("standard:peach", 12), ("classic:fangtian-halberd", 12),
            ("standard:nullification", 12),
            ("standard:slash", 13), (zixing, 13));
        Add(Suit.Club,
            ("standard:duel", 1), ("standard:crossbow", 1),
            ("standard:slash", 2), ("standard:bagua", 2),
            ("standard:renwang_shield", 2),
            ("standard:slash", 3), ("standard:dismantlement", 3),
            ("standard:slash", 4), ("standard:dismantlement", 4),
            ("standard:slash", 5), (dilu, 5),
            ("standard:slash", 6), ("standard:indulgence", 6),
            ("standard:slash", 7), ("standard:barbarian_assault", 7),
            ("standard:slash", 8), ("standard:slash", 8),
            ("standard:slash", 9), ("standard:slash", 9),
            ("standard:slash", 10), ("standard:slash", 10),
            ("standard:slash", 11), ("standard:slash", 11),
            ("classic:borrowed-sword", 12), ("standard:nullification", 12),
            ("classic:borrowed-sword", 13), ("standard:nullification", 13));
        return cards;

        void Add(Suit suit, params (string Id, int Rank)[] entries)
        {
            cards.AddRange(entries.Select(entry => new ContentDeckPhysicalCard(entry.Id, suit, entry.Rank)));
        }
    }

    private static IReadOnlyList<ContentDeckPhysicalCard> CreateMilitaryPhysicalDeck()
    {
        var cards = CreateStandardPhysicalDeck(distinctHorseNames: true).ToList();
        Add(Suit.Heart,
            ("standard:nullification", 1), ("standard:fire_attack", 2),
            ("standard:fire_attack", 3), ("standard:fire_slash", 4),
            ("standard:peach", 5), ("standard:peach", 6),
            ("standard:fire_slash", 7), ("standard:dodge", 8),
            ("standard:dodge", 9), ("standard:fire_slash", 10),
            ("standard:dodge", 11), ("standard:dodge", 12),
            ("standard:nullification", 13));
        Add(Suit.Club,
            ("classic:silver-lion", 1), ("classic:tengjia", 2),
            ("standard:alcohol", 3), ("standard:supply_shortage", 4),
            ("standard:thunder_slash", 5), ("standard:thunder_slash", 6),
            ("standard:thunder_slash", 7), ("standard:thunder_slash", 8),
            ("standard:alcohol", 9), ("standard:iron_chain", 10),
            ("standard:iron_chain", 11), ("standard:iron_chain", 12),
            ("standard:iron_chain", 13));
        Add(Suit.Spade,
            ("classic:guding-blade", 1), ("classic:tengjia", 2),
            ("standard:alcohol", 3), ("standard:thunder_slash", 4),
            ("standard:thunder_slash", 5), ("standard:thunder_slash", 6),
            ("standard:thunder_slash", 7), ("standard:thunder_slash", 8),
            ("standard:alcohol", 9), ("standard:supply_shortage", 10),
            ("standard:iron_chain", 11), ("standard:iron_chain", 12),
            ("standard:nullification", 13));
        Add(Suit.Diamond,
            ("classic:zhuque-fan", 1), ("standard:peach", 2),
            ("standard:peach", 3), ("standard:fire_slash", 4),
            ("standard:fire_slash", 5), ("standard:dodge", 6),
            ("standard:dodge", 7), ("standard:dodge", 8),
            ("standard:alcohol", 9), ("standard:dodge", 10),
            ("standard:dodge", 11), ("standard:fire_attack", 12),
            ("classic:hualiu", 13));
        return cards;

        void Add(Suit suit, params (string Id, int Rank)[] entries)
        {
            cards.AddRange(entries.Select(entry => new ContentDeckPhysicalCard(entry.Id, suit, entry.Rank)));
        }
    }

    private ContentSkillDefinition WithStructuredSkillMetadata(
        ContentSkillDefinition definition,
        SkillTag tags,
        SkillExecutionForm executionForms) =>
        _version >= new Version(1, 68, 0)
            ? definition with { Tags = tags, ExecutionForms = executionForms }
            : definition;

    private static string ReadEmbeddedText(string resourceName)
    {
        using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded skill-program resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static IReadOnlyList<string> ClassicGeneralIds { get; } =
    [
        "classic:liu-bei",
        "classic:sun-quan",
        "classic:sima-yi",
        "classic:xiahou-dun",
        "classic:hua-tuo",
        "classic:cao-cao",
        "classic:zhang-liao",
        "classic:xu-chu",
        "classic:dian-wei",
        "classic:xu-huang",
        "classic:zhen-ji",
        "classic:huang-yueying",
        "classic:ma-chao",
        "classic:huang-zhong",
        "classic:wei-yan",
        "classic:lu-bu",
        "classic:huang-gai",
        "classic:gan-ning",
        "classic:lu-meng",
        "classic:zhang-fei",
        "classic:zhou-yu",
        "classic:zhuge-liang",
        "classic:guan-yu",
        "classic:zhao-yun",
        "classic:guo-jia"
    ];

    internal static IReadOnlyList<string> DaQiaoClassicGeneralIds { get; } =
    [
        .. ClassicGeneralIds,
        "classic:da-qiao"
    ];

    internal static IReadOnlyList<string> DiaoChanClassicGeneralIds { get; } =
    [
        .. DaQiaoClassicGeneralIds,
        "classic:diao-chan"
    ];

    internal static IReadOnlyList<string> SunShangxiangClassicGeneralIds { get; } =
    [
        .. DiaoChanClassicGeneralIds,
        "classic:sun-shangxiang"
    ];

    internal static IReadOnlyList<string> LuXunClassicGeneralIds { get; } =
    [
        .. SunShangxiangClassicGeneralIds,
        "classic:lu-xun"
    ];

    internal static IReadOnlyList<string> PangDeClassicGeneralIds { get; } =
    [
        .. LuXunClassicGeneralIds,
        "classic:pang-de"
    ];

    internal static IReadOnlyList<string> XunYuClassicGeneralIds { get; } =
    [
        .. PangDeClassicGeneralIds,
        "classic:xun-yu"
    ];

    internal static IReadOnlyList<string> YanLiangWenChouClassicGeneralIds { get; } =
    [
        .. XunYuClassicGeneralIds,
        "classic:yan-liang-wen-chou"
    ];

    internal static IReadOnlyList<string> WolongClassicGeneralIds { get; } =
    [
        .. YanLiangWenChouClassicGeneralIds,
        "classic:wolong-zhuge-liang"
    ];

    internal static IReadOnlyList<string> PangTongClassicGeneralIds { get; } =
    [
        .. WolongClassicGeneralIds,
        "classic:pang-tong"
    ];

    internal static IReadOnlyList<string> TaishiCiClassicGeneralIds { get; } =
    [
        .. PangTongClassicGeneralIds,
        "classic:taishi-ci"
    ];

    internal static IReadOnlyList<string> CaoRenClassicGeneralIds { get; } =
    [
        .. TaishiCiClassicGeneralIds,
        "classic:cao-ren"
    ];

    internal static IReadOnlyList<string> XiaoQiaoClassicGeneralIds { get; } =
    [
        .. CaoRenClassicGeneralIds,
        "classic:xiao-qiao"
    ];

    internal static IReadOnlyList<string> ZhouTaiClassicGeneralIds { get; } =
    [
        .. XiaoQiaoClassicGeneralIds,
        "classic:zhou-tai"
    ];

    internal static IReadOnlyList<string> YuanShaoClassicGeneralIds { get; } =
    [
        .. ZhouTaiClassicGeneralIds,
        "classic:yuan-shao"
    ];

    internal static IReadOnlyList<string> XiahouYuanClassicGeneralIds { get; } =
    [
        .. YuanShaoClassicGeneralIds,
        "classic:xiahou-yuan"
    ];

    internal static IReadOnlyList<string> HuaXiongClassicGeneralIds { get; } =
    [
        .. XiahouYuanClassicGeneralIds,
        "classic:hua-xiong"
    ];

    internal static IReadOnlyList<string> GongsunZanClassicGeneralIds { get; } =
    [
        .. HuaXiongClassicGeneralIds,
        "classic:gongsun-zan"
    ];

    internal static IReadOnlyList<string> ZhangJiaoClassicGeneralIds { get; } =
    [
        .. GongsunZanClassicGeneralIds,
        "classic:zhang-jiao"
    ];

    internal static IReadOnlyList<string> SunJianClassicGeneralIds { get; } =
    [
        .. ZhangJiaoClassicGeneralIds,
        "classic:sun-jian"
    ];

    internal static IReadOnlyList<string> MengHuoClassicGeneralIds { get; } =
    [
        .. SunJianClassicGeneralIds,
        "classic:meng-huo"
    ];

    internal static IReadOnlyList<string> ZhuRongClassicGeneralIds { get; } =
    [
        .. MengHuoClassicGeneralIds,
        "classic:zhu-rong"
    ];

    internal static IReadOnlyList<string> YuJinClassicGeneralIds { get; } =
    [
        .. ZhuRongClassicGeneralIds,
        "classic:yu-jin"
    ];

    internal static IReadOnlyList<string> XuShuClassicGeneralIds { get; } =
    [
        .. YuJinClassicGeneralIds,
        "classic:xu-shu"
    ];

    internal static IReadOnlyList<string> SpZhaoYunClassicGeneralIds { get; } =
    [
        .. XuShuClassicGeneralIds,
        "sp:zhao-yun"
    ];

    internal static IReadOnlyList<string> ShenGuanYuClassicGeneralIds { get; } =
    [
        .. SpZhaoYunClassicGeneralIds,
        "classic:shen-guan-yu"
    ];

    internal static IReadOnlyList<string> SpGuanYuClassicGeneralIds { get; } =
    [
        .. ShenGuanYuClassicGeneralIds,
        "sp:guan-yu"
    ];

    internal static IReadOnlyList<string> BoundaryGeneralIds { get; } =
    [
        .. SpZhaoYunClassicGeneralIds.Select(id =>
            id == "classic:zhang-jiao" ? "boundary:zhang-jiao" : id)
    ];

    internal static IReadOnlyList<string> PreGuanYuClassicGeneralIds { get; } =
    [
        .. ClassicGeneralIds.Select(id => id == "classic:guan-yu" ? "standard:guan-yu" : id)
    ];

    internal static IReadOnlyList<string> PreZhaoYunClassicGeneralIds { get; } =
    [
        .. PreGuanYuClassicGeneralIds.Select(id => id == "classic:zhao-yun" ? "standard:zhao-yun" : id)
    ];

    internal static IReadOnlyList<string> PreZhangFeiClassicGeneralIds { get; } =
    [
        .. PreZhaoYunClassicGeneralIds.Select(id => id == "classic:zhang-fei" ? "standard:zhang-fei" : id)
    ];

    internal static IReadOnlyList<string> PreLuBuClassicGeneralIds { get; } =
    [
        .. PreZhangFeiClassicGeneralIds.Where(id => id != "classic:lu-bu")
    ];

    internal static IReadOnlyList<string> PreWeiYanClassicGeneralIds { get; } =
    [
        .. PreLuBuClassicGeneralIds.Where(id => id != "classic:wei-yan")
    ];

    internal static IReadOnlyList<string> PreHuangZhongClassicGeneralIds { get; } =
    [
        .. PreWeiYanClassicGeneralIds.Where(id => id != "classic:huang-zhong")
    ];

    internal static IReadOnlyList<string> PreMaChaoClassicGeneralIds { get; } =
    [
        .. PreHuangZhongClassicGeneralIds.Where(id => id != "classic:ma-chao")
    ];

    internal static IReadOnlyList<string> PreHuangYueyingClassicGeneralIds { get; } =
    [
        .. PreMaChaoClassicGeneralIds.Where(id => id != "classic:huang-yueying")
    ];

    internal static IReadOnlyList<string> PreZhenJiClassicGeneralIds { get; } =
    [
        .. PreHuangYueyingClassicGeneralIds.Where(id => id != "classic:zhen-ji")
    ];

    internal static IReadOnlyList<string> PreXuHuangClassicGeneralIds { get; } =
    [
        .. PreZhenJiClassicGeneralIds.Where(id => id != "classic:xu-huang")
    ];

    internal static IReadOnlyList<string> PreDianWeiClassicGeneralIds { get; } =
    [
        .. PreXuHuangClassicGeneralIds.Where(id => id != "classic:dian-wei")
    ];

    internal static IReadOnlyList<string> PreXuChuClassicGeneralIds { get; } =
    [
        .. PreDianWeiClassicGeneralIds.Where(id => id != "classic:xu-chu")
    ];

    internal static IReadOnlyList<string> PreZhangLiaoClassicGeneralIds { get; } =
    [
        .. PreXuChuClassicGeneralIds.Where(id => id != "classic:zhang-liao")
    ];

    internal static IReadOnlyList<string> PreLuMengClassicGeneralIds { get; } =
    [
        .. PreZhangLiaoClassicGeneralIds.Where(id => id != "classic:lu-meng")
    ];

    internal static IReadOnlyList<string> PreGanNingClassicGeneralIds { get; } =
    [
        .. PreLuMengClassicGeneralIds.Where(id => id != "classic:gan-ning")
    ];

    internal static IReadOnlyList<string> PreHuangGaiClassicGeneralIds { get; } =
    [
        .. PreGanNingClassicGeneralIds.Where(id => id != "classic:huang-gai")
    ];

    internal static IReadOnlyList<string> GuanxingClassicGeneralIds { get; } =
    [
        .. PreHuangGaiClassicGeneralIds.Select(id => id == "classic:cao-cao" ? "standard:cao-cao" : id)
    ];

    internal static IReadOnlyList<string> FanjianClassicGeneralIds { get; } =
    [
        .. GuanxingClassicGeneralIds.Select(id => id == "classic:zhuge-liang" ? "standard:zhuge-liang" : id)
    ];

    internal static IReadOnlyList<string> TianduClassicGeneralIds { get; } =
    [
        .. FanjianClassicGeneralIds.Select(id => id == "classic:zhou-yu" ? "standard:zhou-yu" : id)
    ];

    internal static IReadOnlyList<string> LegacyClassicGeneralIds { get; } =
    [
        .. TianduClassicGeneralIds.Select(id => id == "classic:guo-jia" ? "standard:guo-jia" : id)
    ];
}
