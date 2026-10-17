using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>
/// Current opt-in classic identity roster and its skill definitions.
/// </summary>
public sealed class StandardClassicGeneralPackage : IGameContentPackage
{
    public static Version CurrentVersion { get; } = new(1, 164, 0);

    public StandardClassicGeneralPackage()
    {
        Manifest = new PackageManifest(
            Id: "standard-classic-generals",
            Version: CurrentVersion,
            Dependencies: [new PackageDependency("standard-rescue-skills", new Version(1, 0, 0))]);
    }
    public PackageManifest Manifest { get; }

    public void Register(IContentRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var variantBuilder = new GeneralVariantContentBuilder(builder);
        builder = variantBuilder;

        foreach (var weapon in new[]
        {
            ("special:ghost-dragon-crescent-blade", CardKind.GhostDragonCrescentBlade),
            ("special:scarlet-blood-sword", CardKind.ScarletBloodSword),
            ("special:xingtian-axe", CardKind.XingtianAxe)
        })
        {
            var definition = EquipmentCatalog.Get(weapon.Item2);
            builder.AddCard(new ContentCardDefinition(
                weapon.Item1, definition.DisplayName, "装备牌", definition.Description,
                LegacyKind: definition.Kind));
        }

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
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:stone-axe", 1));
            }
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:zhangba-serpent-spear", 1));
            }
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:cixiong-double-swords", 2));
            }
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:qinglong-crescent-blade", 1));
            }
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:ice-sword", 1));
            }
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:qilin-bow", 1));
            }
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:fangtian-halberd", 1));
            }
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:guding-blade", 1));
            }
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:zhuque-fan", 1));
            }
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:tengjia", 1));
            }
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:silver-lion", 1));
            }
            {
                classicDeckCards.Add(new ContentDeckCardCount("classic:wooden-ox", 1));
            }

            var physicalCards = CreateMilitaryPhysicalDeck();
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
                Name: "经典军争 160 张逐张牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: physicalCards is null ? classicDeckCards : [])
            {
                PhysicalCards = physicalCards
            });
        }

        CharacterContentCatalog.Default.Register(builder, Manifest.Id);
        var generalPoolIds = CurrentGeneralIds.Concat(FanGeneralIds).ToArray();
        var generalVariantGroups = variantBuilder.CreateGroups(generalPoolIds);
        var aiGeneralSwapExclusions = variantBuilder.CreateAiSwapExclusions(generalPoolIds);
        var roleGeneralCandidateCounts = new Dictionary<string, int>
        {
            [nameof(Role.Lord)] = 13,
            [nameof(Role.Loyalist)] = 8,
            [nameof(Role.Rebel)] = 8,
            [nameof(Role.Renegade)] = 13
        };

        var eightPlayerIdentity = new ContentModeDefinition(
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
            DeckId: "classic:standard-deck",
            GeneralCandidateCount: 3,
            GeneralPoolIds: generalPoolIds)
        {
            RoleGeneralCandidateCounts = roleGeneralCandidateCounts,
            GeneralVariantGroups = generalVariantGroups,
            AiGeneralSwapExcludedIds = aiGeneralSwapExclusions,
            GeneralSelectionWeights = IdentityGeneralSelectionWeights.EightPlayer
        };
        builder.AddMode(eightPlayerIdentity);
        builder.AddMode(eightPlayerIdentity with
        {
            Id = "identity:classic-dual-8",
            Name = "八人经典身份·双将",
            DualGenerals = true
        });
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
            DeckId: "classic:standard-deck",
            GeneralCandidateCount: 3,
            GeneralPoolIds: generalPoolIds) { RoleGeneralCandidateCounts = roleGeneralCandidateCounts,
                GeneralVariantGroups = generalVariantGroups, AiGeneralSwapExcludedIds = aiGeneralSwapExclusions });
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

    internal static IReadOnlyList<string> CurrentGeneralIds { get; } =
        CharacterContentCatalog.Default.GetPoolIds("standard-classic-generals", "current");

    internal static IReadOnlyList<string> FanGeneralIds { get; } =
        CharacterContentCatalog.Default.GetPoolIds("standard-classic-generals", "fan");

    internal static IReadOnlyList<string> BoundaryGeneralIds { get; } =
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
        "classic:guo-jia",
        "classic:da-qiao",
        "classic:diao-chan",
        "classic:sun-shangxiang",
        "classic:lu-xun",
        "classic:pang-de",
        "classic:xun-yu",
        "classic:yan-liang-wen-chou",
        "classic:wolong-zhuge-liang",
        "classic:pang-tong",
        "classic:taishi-ci",
        "classic:cao-ren",
        "classic:xiao-qiao",
        "classic:zhou-tai",
        "classic:yuan-shao",
        "classic:xiahou-yuan",
        "classic:hua-xiong",
        "classic:gongsun-zan",
        "boundary:zhang-jiao",
        "classic:sun-jian",
        "classic:meng-huo",
        "classic:zhu-rong",
        "classic:yu-jin",
        "classic:xu-shu",
        "sp:zhao-yun",
    ];
}
