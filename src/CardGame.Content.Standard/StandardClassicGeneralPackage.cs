using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>
/// Current opt-in classic identity roster and its skill definitions.
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
    private const string ClassicShenSimaYiRulesResource =
        "CardGame.Content.Standard.SkillPrograms.classic-shen-sima-yi.rules.json";
    private const string ClassicShenSimaYiPresentationResource =
        "CardGame.Content.Standard.SkillPrograms.classic-shen-sima-yi.presentation.json";
    private const string ClassicCaoPiRulesResource =
        "CardGame.Content.Standard.SkillPrograms.classic-cao-pi.rules.json";
    private const string ClassicCaoPiPresentationResource =
        "CardGame.Content.Standard.SkillPrograms.classic-cao-pi.presentation.json";
    private const string ClassicSunCeRulesResource =
        "CardGame.Content.Standard.SkillPrograms.classic-sun-ce.rules.json";
    private const string ClassicSunCePresentationResource =
        "CardGame.Content.Standard.SkillPrograms.classic-sun-ce.presentation.json";
    private const string ClassicShenGuanYuRulesResource =
        "CardGame.Content.Standard.SkillPrograms.classic-shen-guan-yu.rules.json";
    private const string ClassicShenGuanYuPresentationResource =
        "CardGame.Content.Standard.SkillPrograms.classic-shen-guan-yu.presentation.json";
    private const string ClassicGaoShunRulesResource =
        "CardGame.Content.Standard.SkillPrograms.classic-gao-shun.rules.json";
    private const string ClassicGaoShunPresentationResource =
        "CardGame.Content.Standard.SkillPrograms.classic-gao-shun.presentation.json";
    private const string ClassicLifecycleRulesResource =
        "CardGame.Content.Standard.SkillPrograms.classic-lifecycle-skills.rules.json";
    private const string ClassicLifecyclePresentationResource =
        "CardGame.Content.Standard.SkillPrograms.classic-lifecycle-skills.presentation.json";
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
    private static readonly Lazy<SkillProgramCatalog> ClassicShenSimaYiCatalog = new(() =>
        SkillProgramCatalog.Load(
            ReadEmbeddedText(ClassicShenSimaYiRulesResource),
            ReadEmbeddedText(ClassicShenSimaYiPresentationResource)));
    private static readonly Lazy<SkillProgramCatalog> ClassicCaoPiCatalog = new(() =>
        SkillProgramCatalog.Load(
            ReadEmbeddedText(ClassicCaoPiRulesResource),
            ReadEmbeddedText(ClassicCaoPiPresentationResource)));
    private static readonly Lazy<SkillProgramCatalog> ClassicSunCeCatalog = new(() =>
        SkillProgramCatalog.Load(
            ReadEmbeddedText(ClassicSunCeRulesResource),
            ReadEmbeddedText(ClassicSunCePresentationResource)));
    private static readonly Lazy<SkillProgramCatalog> ClassicGaoShunCatalog = new(() =>
        SkillProgramCatalog.Load(
            ReadEmbeddedText(ClassicGaoShunRulesResource),
            ReadEmbeddedText(ClassicGaoShunPresentationResource)));
    private static readonly Lazy<SkillProgramCatalog> ClassicLifecycleCatalog = new(() =>
        SkillProgramCatalog.Load(
            ReadEmbeddedText(ClassicLifecycleRulesResource),
            ReadEmbeddedText(ClassicLifecyclePresentationResource)));
    public static Version CurrentVersion { get; } = new(1, 152, 0);

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

        {
            builder.AddSkill(WithActiveActionMetadata(
                EmbeddedSkillProgramCatalog.Definition("classic-active-cutover", "classic:rende")));
            builder.AddSkill(WithActiveActionMetadata(EmbeddedSkillProgramCatalog.Definition("phase-owned-card-actions", "classic:zhiheng")));
            builder.AddSkill(WithActiveActionMetadata(EmbeddedSkillProgramCatalog.Definition("phase-owned-card-actions", "classic:qingnang")));
            builder.AddSkill(WithActiveActionMetadata(EmbeddedSkillProgramCatalog.Definition("classic-kujin-skills", "classic:kujin")));
        }

        {
            builder.AddSkill(WithOptionalTriggerMetadata(EmbeddedSkillProgramCatalog.Definition("judgment-cutover", "classic:guicai")));
            builder.AddSkill(WithOptionalTriggerMetadata(EmbeddedSkillProgramCatalog.Definition("passive-damage-cutover", "classic:ganglie")));
            builder.AddSkill(WithContinuousStateMetadata(EmbeddedSkillProgramCatalog.Definition(
                "conversion-cutover", "classic:jijiu")));
            builder.AddSkill(DamageSkillPrograms.Definition("classic:yiji"));
            builder.AddSkill(DrawPhaseSkillPrograms.Definition("classic:yingzi"));
            builder.AddSkill(DamageSkillPrograms.Definition("classic:jianxiong"));
            builder.AddSkill(DamageSkillPrograms.Definition("classic:jieming"));
        }

        {
            builder.AddSkill(new ContentSkillDefinition(
                "mou:hengye",
                "横野",
                "锁定技，你造成伤害后，你令本局游戏以下每个数值各+1（最多+3）：1.摸牌阶段摸牌数；2.出牌阶段使用【杀】次数；3.攻击范围；4.手牌上限。数值+3后，你每回合开始时回复1点体力。你杀死一名角色后重置此技能。")
            {
                Tags = SkillTag.Locked,
                ExecutionForms = SkillExecutionForm.State
            });
            builder.AddSkill(new ContentSkillDefinition(
                "mou:yingbo",
                "英博",
                "你使用的伤害牌若已有角色本轮使用过，则此牌造成的伤害改为火属性伤害且伤害+1；若本轮没有角色使用过，则此牌不能被响应且结算后你可将之交给一名其他角色。")
            {
                ExecutionForms = SkillExecutionForm.State | SkillExecutionForm.Trigger
            });
        }

        {
            builder.AddSkill(DrawPolicySkillPrograms.Definition("classic:jiangchi"));
        }

        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("hand-color-restriction-skills", "classic:qianxi") with
                {
                    ExecutionForms = SkillExecutionForm.State | SkillExecutionForm.Trigger
                });
        }

        {
            builder.AddSkill(ClassicCardActionSkillPrograms.Definition("classic:xianzhen"));

            var jinjiuProgram = ClassicGaoShunCatalog.Value.Programs["classic:jinjiu"];
            var jinjiuPresentation = ClassicGaoShunCatalog.Value.Presentations["classic:jinjiu"];
            builder.AddSkill(WithStructuredSkillMetadata(new ContentSkillDefinition(
                "classic:jinjiu",
                jinjiuPresentation.Name,
                jinjiuPresentation.Description)
            {
                Program = jinjiuProgram
            }, SkillTag.Locked, SkillExecutionForm.State));
        }

        {
            builder.AddSkill(DrawPolicySkillPrograms.Definition("classic:zishou"));
            builder.AddSkill(WithStructuredSkillMetadata(
                RuleQuerySkillPrograms.Definition("classic:zongshi"),
                SkillTag.Locked,
                SkillExecutionForm.State));
        }

        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition(
                    "classic-zhenlie-skills", "classic:zhenlie") with
                {
                    ExecutionForms = SkillExecutionForm.Trigger
                });
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition(
                    "owned-card-distribution-skills", "classic:miji") with
                {
                    ExecutionForms = SkillExecutionForm.Trigger
                });
        }

        {
            builder.AddSkill(PersistentZoneSkillPrograms.Definition("classic:quanji"));
            builder.AddSkill(AwakeningSkillPrograms.Definition("classic:zili"));
            builder.AddSkill(ActivePersistentZoneSkillPrograms.Definition("classic:paiyi"));
        }

        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("all-hand-trick-skills", "classic:qice") with
                {
                    ActionForms = SkillActionForm.Active
                });
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("all-hand-trick-skills", "classic:zhiyu") with
                {
                    ExecutionForms = SkillExecutionForm.Trigger
                });
        }

        {
            var dangxian = ClassicLifecycleCatalog.Value.Programs["classic:dangxian"];
            var dangxianPresentation = ClassicLifecycleCatalog.Value.Presentations["classic:dangxian"];
            builder.AddSkill(WithStructuredSkillMetadata(new ContentSkillDefinition(
                    dangxian.Id,
                    dangxianPresentation.Name,
                    dangxianPresentation.Description)
                {
                    Program = dangxian
                },
                SkillTag.Locked,
                SkillExecutionForm.State));
            var fuli = ClassicLifecycleCatalog.Value.Programs["classic:fuli"];
            var fuliPresentation = ClassicLifecycleCatalog.Value.Presentations["classic:fuli"];
            builder.AddSkill(WithStructuredSkillMetadata(new ContentSkillDefinition(
                    fuli.Id,
                    fuliPresentation.Name,
                    fuliPresentation.Description)
                {
                    Program = fuli
                },
                SkillTag.Limited,
                SkillExecutionForm.Trigger));
        }

        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition(
                    "multi-card-conversion-skills", "classic:fuhun") with
                {
                    ExecutionForms = SkillExecutionForm.State | SkillExecutionForm.Trigger,
                    ActionForms = SkillActionForm.Active
                });
        }

        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("unequal-hand-transfer-skills", "classic:anxu") with
                {
                    ActionForms = SkillActionForm.Active
                });
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("death-benefit-skills", "classic:zhuiyi") with
                {
                    ExecutionForms = SkillExecutionForm.Trigger
                });
        }

        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition(
                    "lihuo-program-skills", "classic:lihuo") with
                {
                    ExecutionForms = SkillExecutionForm.State | SkillExecutionForm.Trigger
                });
        }
        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("owned-zone-dying-rescue-skills", "classic:chunlao") with
                {
                    ExecutionForms = SkillExecutionForm.Trigger
                });
        }
        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-gongqi-skills", "classic:gongqi") with
                {
                    ActionForms = SkillActionForm.Active,
                    ExecutionForms = SkillExecutionForm.State
                });
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-jiefan-skills", "classic:jiefan") with
                {
                    ActionForms = SkillActionForm.Active,
                    Tags = SkillTag.Limited
                });
        }
        {
            var chengxiang = ClassicLifecycleCatalog.Value.Programs["classic:chengxiang"];
            var chengxiangPresentation = ClassicLifecycleCatalog.Value.Presentations["classic:chengxiang"];
            builder.AddSkill(WithOptionalTriggerMetadata(new ContentSkillDefinition(
                chengxiang.Id,
                chengxiangPresentation.Name,
                chengxiangPresentation.Description)
            {
                Program = chengxiang
            }));
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("damage-prevention-skills", "classic:renxin") with
                {
                    ExecutionForms = SkillExecutionForm.Trigger
                });
        }
        {
            builder.AddSkill(WithOptionalTriggerMetadata(
                ClassicPhaseWindowSkillPrograms.Definition("classic:jingce")));
        }
        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("category-challenge-skills", "classic:junxing") with
                {
                    ActionForms = SkillActionForm.Active
                });
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("category-challenge-skills", "classic:yuce") with
                {
                    ExecutionForms = SkillExecutionForm.Trigger
                });
        }
        {
            builder.AddSkill(ClassicCardActionSkillPrograms.Definition("classic:longyin"));
        }

        builder.AddSkill(DamageSkillPrograms.Definition("classic:feedback"));
        {
            builder.AddSkill(WithOptionalTriggerMetadata(EmbeddedSkillProgramCatalog.Definition("judgment-cutover", "classic:tiandu")));
        }
        {
            builder.AddSkill(WithActiveActionMetadata(
                EmbeddedSkillProgramCatalog.Definition("classic-active-cutover", "classic:fanjian")));
        }
        {
            builder.AddSkill(WithOptionalTriggerMetadata(EmbeddedSkillProgramCatalog.Definition("judgment-cutover", "classic:guanxing")));
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition("passive-response-rules", "classic:hujia"), SkillTag.Lord, SkillExecutionForm.Trigger));
        }
        {
            builder.AddSkill(WithActiveActionMetadata(
                WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition(
                    "classic-active-cutover", "classic:jijiang"),
                    SkillTag.Lord, SkillExecutionForm.Trigger)));
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition("passive-response-rules", "classic:jiuyuan"), SkillTag.Lord | SkillTag.Locked, SkillExecutionForm.State));
        }
        {
            builder.AddSkill(WithContinuousStateMetadata(EmbeddedSkillProgramCatalog.Definition(
                "conversion-cutover", "classic:qixi")));
        }
        {
            builder.AddSkill(WithOptionalTriggerMetadata(EmbeddedSkillProgramCatalog.Definition("passive-card-rules", "classic:keji")));
        }
        {
            builder.AddSkill(DrawReplacementSkillPrograms.Definition("classic:tuxi"));
        }
        {
            builder.AddSkill(DrawAdjustmentSkillPrograms.Definition("classic:luoyi"));
        }
        {
            builder.AddSkill(WithActiveActionMetadata(
                EmbeddedSkillProgramCatalog.Definition("classic-active-cutover", "classic:qiangxi")));
        }
        {
            builder.AddSkill(WithContinuousStateMetadata(EmbeddedSkillProgramCatalog.Definition(
                "conversion-cutover", "classic:duanliang")));
        }
        {
            builder.AddSkill(WithOptionalTriggerMetadata(EmbeddedSkillProgramCatalog.Definition("judgment-cutover", "classic:luoshen")));
            builder.AddSkill(WithContinuousStateMetadata(EmbeddedSkillProgramCatalog.Definition("classic-qingguo-skills", "classic:qingguo")));
        }
        {
            builder.AddSkill(WithOptionalTriggerMetadata(EmbeddedSkillProgramCatalog.Definition("passive-card-rules", "classic:jizhi")));
        }
        {
            builder.AddSkill(WithOptionalTriggerMetadata(EmbeddedSkillProgramCatalog.Definition("passive-response-rules", "classic:tieqi")));
        }
        {
            builder.AddSkill(WithOptionalTriggerMetadata(EmbeddedSkillProgramCatalog.Definition("passive-response-rules", "classic:liegong")));
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition("passive-damage-cutover", "classic:kuanggu"), SkillTag.Locked, SkillExecutionForm.State));
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition("passive-card-rules", "classic:wushuang"), SkillTag.Locked, SkillExecutionForm.State));
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(
                RuleQuerySkillPrograms.Definition("classic:paoxiao"),
                SkillTag.Locked, SkillExecutionForm.State));
        }
        {
            builder.AddSkill(WithContinuousStateMetadata(EmbeddedSkillProgramCatalog.Definition(
                    "classic-longdan-skills", "classic:longdan")));
        }
        {
            builder.AddSkill(WithContinuousStateMetadata(EmbeddedSkillProgramCatalog.Definition("classic-wusheng-skills", "classic:wusheng")));
        }
        {
            builder.AddSkill(WithContinuousStateMetadata(EmbeddedSkillProgramCatalog.Definition(
                "conversion-cutover", "classic:guose")));
            builder.AddSkill(WithOptionalTriggerMetadata(EmbeddedSkillProgramCatalog.Definition("passive-response-rules", "classic:liuli")));
        }
        {
            builder.AddSkill(WithActiveActionMetadata(
                EmbeddedSkillProgramCatalog.Definition("classic-active-cutover", "classic:lijian")));
            builder.AddSkill(WithOptionalTriggerMetadata(
                ClassicPhaseWindowSkillPrograms.Definition("classic:biyue")));
        }
        {
            builder.AddSkill(WithActiveActionMetadata(
                EmbeddedSkillProgramCatalog.Definition("classic-active-cutover", "classic:jieyin")));
            builder.AddSkill(WithOptionalTriggerMetadata(
                ClassicCardMovementSkillPrograms.Definition("classic:xiaoji")));
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition("passive-card-rules", "classic:qianxun"), SkillTag.Locked, SkillExecutionForm.State));
            builder.AddSkill(WithOptionalTriggerMetadata(
                ClassicCardMovementSkillPrograms.Definition("classic:lianying")));
        }
        {
            builder.AddSkill(WithOptionalTriggerMetadata(EmbeddedSkillProgramCatalog.Definition("passive-response-rules", "classic:mengjin")));
        }
        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("shared-pindian-skills", "classic:quhu"));
        }
        {
            builder.AddSkill(JudgmentDrawSkillPrograms.Definition("classic:shuangxiong"));
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition("passive-response-rules", "classic:bazhen"),
                SkillTag.Locked, SkillExecutionForm.State));
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("conversion-cutover", "classic:huoji"));
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("conversion-cutover", "classic:kanpo"));
        }
        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("conversion-cutover", "classic:lianhuan"));
            builder.AddSkill(SelfDyingSkillPrograms.Definition("classic:niepan"));
        }
        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("shared-pindian-skills", "classic:tianyi"));
        }
        {
            builder.AddSkill(WithOptionalTriggerMetadata(
                ClassicPhaseWindowSkillPrograms.Definition("classic:jushou")));
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition("passive-card-rules", "classic:hongyan"),
                SkillTag.Locked, SkillExecutionForm.State));
            builder.AddSkill(WithOptionalTriggerMetadata(EmbeddedSkillProgramCatalog.Definition("passive-damage-cutover", "classic:tianxiang")));
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition("passive-damage-cutover", "classic:buqu"),
                SkillTag.Locked, SkillExecutionForm.State));
        }
        {
            builder.AddSkill(WithActiveActionMetadata(
                WithContinuousStateMetadata(EmbeddedSkillProgramCatalog.Definition(
                    "classic-active-cutover", "classic:luanji"))));
            builder.AddSkill(WithStructuredSkillMetadata(
                EmbeddedSkillProgramCatalog.Definition("passive-card-rules", "classic:xueyi"),
                SkillTag.Lord | SkillTag.Locked,
                SkillExecutionForm.State));
        }
        {
            builder.AddSkill(WithOptionalTriggerMetadata(EmbeddedSkillProgramCatalog.Definition("judgment-cutover", "classic:shensu")));
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition("passive-damage-cutover", "classic:yaowu"),
                SkillTag.Locked, SkillExecutionForm.State));
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(
                RuleQuerySkillPrograms.Definition("classic:yicong"),
                SkillTag.Locked, SkillExecutionForm.State));
        }
        {
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
                    builder.AddSkill(id switch
                    {
                        "classic:huangtian" => WithStructuredSkillMetadata(
                            definition, SkillTag.Lord, SkillExecutionForm.Trigger),
                        "classic:guidao" or "classic:leiji" => WithOptionalTriggerMetadata(definition),
                        _ => definition
                    });
                }
            }
        }
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
                builder.AddSkill(id switch
                {
                    "boundary:huangtian" => WithStructuredSkillMetadata(
                        definition, SkillTag.Lord, SkillExecutionForm.Trigger),
                    "boundary:guidao" or "boundary:leiji" => WithOptionalTriggerMetadata(definition),
                    _ => definition
                });
            }
        }
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
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition(
                    "nightmare-death-skills-v53",
                    "classic:wuhun") with
                {
                    Tags = SkillTag.Locked,
                    ExecutionForms = SkillExecutionForm.State
                });
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition(
                "conversion-cutover", "sp:guan-yu-wusheng"), SkillTag.None, SkillExecutionForm.State));
            builder.AddSkill(AwakeningSkillPrograms.Definition("sp:danji"));
            builder.AddSkill(WithStructuredSkillMetadata(
                RuleQuerySkillPrograms.Definition("sp:guan-yu-mashu"),
                SkillTag.Locked, SkillExecutionForm.State));
            builder.AddSkill(WithStructuredSkillMetadata(new ContentSkillDefinition(
                "sp:nuzhan",
                "怒斩",
                "锁定技，你使用由锦囊牌转化的【杀】不计入出牌阶段使用次数；你使用由装备牌转化的【杀】伤害+1。"),
                SkillTag.Locked,
                SkillExecutionForm.State));
        }
        {
            builder.AddSkill(ClassicCardActionSkillPrograms.Definition("classic:juzhan"));
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition("passive-card-rules", "classic:kongcheng"), SkillTag.Locked, SkillExecutionForm.State));
            builder.AddSkill(WithStructuredSkillMetadata(
                RuleQuerySkillPrograms.Definition("classic:mashu"),
                SkillTag.Locked, SkillExecutionForm.State));
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition("passive-card-rules", "classic:qicai"), SkillTag.Locked, SkillExecutionForm.State));
        }
        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("owned-card-exchange-skills", "classic:yinghun"));
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition("passive-card-rules", "classic:huoshou"),
                SkillTag.Locked, SkillExecutionForm.State));
            builder.AddSkill(DrawRevealReplacementSkillPrograms.Definition("classic:zaiqi"));
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition("passive-card-rules", "classic:juxiang"),
                SkillTag.Locked, SkillExecutionForm.State));
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("shared-pindian-skills", "classic:lieren"));
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition("passive-card-rules", "classic:yizhong"),
                SkillTag.Locked, SkillExecutionForm.State));
        }
        {
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition("passive-card-rules", "classic:wuyan"),
                SkillTag.Locked, SkillExecutionForm.State));
        }
        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("support-choice-skills", "classic:jujian"));
        }
        {
            foreach (var (id, program) in SpZhaoYunCatalog.Value.Programs
                         .OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                var presentation = SpZhaoYunCatalog.Value.Presentations[id];
                var definition = new ContentSkillDefinition(
                    id,
                    presentation.Name,
                    presentation.Description)
                {
                    Program = program
                };
                builder.AddSkill(id switch
                {
                    "sp:chongzhen" => WithOptionalTriggerMetadata(definition),
                    "sp:longdan" => WithContinuousStateMetadata(definition),
                    _ => definition
                });
            }
        }

        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-zhu-huan", "classic:youdi"));
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-gu-yong", "classic:shenxing"));
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-gu-yong", "classic:bingyi"));
        }

        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-zhu-zhi", "classic:anguo"));

        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("sp-le-jin", "sp:xiaoguo"));

        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-li-dian", "classic:xunxun"));
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-li-dian", "classic:wangxi"));
            builder.AddSkill(WithOptionalTriggerMetadata(EmbeddedSkillProgramCatalog.Definition("judgment-cutover", "boundary:tiandu")));
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-guo-jia", "boundary:yiji"));
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-cao-cao", "boundary:jianxiong"));
            builder.AddSkill(WithStructuredSkillMetadata(EmbeddedSkillProgramCatalog.Definition("passive-response-rules", "boundary:hujia"), SkillTag.Lord, SkillExecutionForm.Trigger));
        }

        {
            builder.AddSkill(DamageSkillPrograms.Definition("boundary:feedback"));
            builder.AddSkill(WithOptionalTriggerMetadata(
                EmbeddedSkillProgramCatalog.Definition("boundary-sima-yi", "boundary:guicai")));
            builder.AddSkill(WithActiveActionMetadata(
                EmbeddedSkillProgramCatalog.Definition("classic-active-cutover", "boundary:lijian")));
            builder.AddSkill(WithOptionalTriggerMetadata(
                EmbeddedSkillProgramCatalog.Definition("boundary-diao-chan", "boundary:biyue")));
            builder.AddSkill(WithOptionalTriggerMetadata(
                EmbeddedSkillProgramCatalog.Definition("boundary-zhang-liao", "boundary:tuxi")));
        }
        {
            builder.AddSkill(WithContinuousStateMetadata(EmbeddedSkillProgramCatalog.Definition(
                "conversion-cutover", "boundary:qixi")));
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-gan-ning", "boundary:fenwei") with
            {
                Tags = SkillTag.Limited,
                ExecutionForms = SkillExecutionForm.Trigger
            });
        }

        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-xu-chu", "boundary:luoyi"));

        {
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-zhou-yu", "boundary:yingzi"));
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-zhou-yu", "boundary:fanjian"));
        }

        {
            builder.AddSkill(WithOptionalTriggerMetadata(
                EmbeddedSkillProgramCatalog.Definition("classic-pan-zhang-ma-zhong", "classic:duodao")));
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition(
                "classic-pan-zhang-ma-zhong", "classic:anjian") with
            {
                Tags = SkillTag.Locked,
                ExecutionForms = SkillExecutionForm.State
            });
        }

        builder.AddSkill(WithOptionalTriggerMetadata(
            EmbeddedSkillProgramCatalog.Definition("classic-xu-sheng", "classic:pojun")));

        builder.AddSkill(WithOptionalTriggerMetadata(
            EmbeddedSkillProgramCatalog.Definition("boundary-xu-sheng", "boundary:pojun")));

        builder.AddSkill(WithOptionalTriggerMetadata(
            EmbeddedSkillProgramCatalog.Definition("classic-zhang-song", "classic:qiangzhi")));

        builder.AddSkill(WithOptionalTriggerMetadata(
            EmbeddedSkillProgramCatalog.Definition("classic-zhang-song", "classic:xiantu")));

        builder.AddSkill(WithOptionalTriggerMetadata(
            EmbeddedSkillProgramCatalog.Definition("boundary-zhang-song", "boundary:qiangzhi")));

        builder.AddSkill(WithOptionalTriggerMetadata(
            EmbeddedSkillProgramCatalog.Definition("boundary-zhang-song", "boundary:xiantu")));

        builder.AddSkill(WithOptionalTriggerMetadata(
            EmbeddedSkillProgramCatalog.Definition("classic-cao-ang", "classic:kangkai")));
        builder.AddSkill(WithStructuredSkillMetadata(
            EmbeddedSkillProgramCatalog.Definition("classic-qu-yi", "classic:fuqi"),
            SkillTag.Locked, SkillExecutionForm.State));
        builder.AddSkill(WithStructuredSkillMetadata(
            EmbeddedSkillProgramCatalog.Definition("classic-qu-yi", "classic:jiaozi"),
            SkillTag.Locked, SkillExecutionForm.State));
        builder.AddSkill(WithStructuredSkillMetadata(
            EmbeddedSkillProgramCatalog.Definition("classic-zhang-xiu", "classic:xiongluan"),
            SkillTag.Limited, SkillExecutionForm.State) with { ActionForms = SkillActionForm.Active });
        builder.AddSkill(WithStructuredSkillMetadata(
            EmbeddedSkillProgramCatalog.Definition("ol-shen-guan-yu", "ol:wushen"),
            SkillTag.Locked, SkillExecutionForm.State));
        builder.AddSkill(WithOptionalTriggerMetadata(
            EmbeddedSkillProgramCatalog.Definition("special-xingtian-axe", "special:xingtian-axe-effect")));
        builder.AddSkill(WithStructuredSkillMetadata(
            EmbeddedSkillProgramCatalog.Definition("classic-cai-wen-ji", "classic:duanchang"),
            SkillTag.Locked, SkillExecutionForm.Trigger));
        builder.AddSkill(WithOptionalTriggerMetadata(
            EmbeddedSkillProgramCatalog.Definition("classic-cai-wen-ji", "classic:beige")));
        builder.AddSkill(new ContentSkillDefinition(
            "classic:chanyuan", "缠怨",
            "锁定技，你不能质疑蛊惑；体力值为 1 时，你的其他技能失效。")
        {
            Tags = SkillTag.Locked,
            ExecutionForms = SkillExecutionForm.State,
            SuppressionRule = new SkillSuppressionRule(1)
        });
        {
            builder.AddSkill(WithStructuredSkillMetadata(
                ShenSimaYiProgram("classic:renjie"), SkillTag.Locked, SkillExecutionForm.Trigger));
            builder.AddSkill(WithStructuredSkillMetadata(
                ShenSimaYiProgram("classic:baiyin"), SkillTag.Awakening, SkillExecutionForm.Trigger));
            builder.AddSkill(WithOptionalTriggerMetadata(ShenSimaYiProgram("classic:lianpo")));
        }

        {
            builder.AddSkill(WithOptionalTriggerMetadata(CaoPiProgram("classic:xingshang")));
            builder.AddSkill(WithOptionalTriggerMetadata(CaoPiProgram("classic:fangzhu")));
        }

        {
            builder.AddSkill(WithOptionalTriggerMetadata(SunCeProgram("classic:jiang")));
            builder.AddSkill(WithStructuredSkillMetadata(
                SunCeProgram("classic:hunzi"), SkillTag.Awakening, SkillExecutionForm.Trigger));
        }

        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:liu-bei",
            "刘备",
            "liu_bei",
            "classic:rende",
            "shu",
            BaseHp: 4,
            AdditionalSkillIds: ["classic:jijiang"]));
        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:sun-quan",
            "孙权",
            "sun_quan",
            "classic:zhiheng",
            "wu",
            BaseHp: 4,
            AdditionalSkillIds: ["classic:jiuyuan"]));
        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:sima-yi",
            "司马懿",
            "sima_yi",
            "classic:feedback",
            "wei",
            BaseHp: 3,
            AdditionalSkillIds:
            [
                "classic:guicai"
            ]));
        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:xiahou-dun",
            "夏侯惇",
            "xiahou_dun",
            "classic:ganglie",
            "wei",
            BaseHp: 4));
        builder.AddGeneral(new ContentGeneralDefinition(
            "classic:hua-tuo",
            "华佗",
            "hua_tuo",
            "classic:qingnang",
            "qun",
            BaseHp: 3,
            AdditionalSkillIds:
            [
                "classic:jijiu"
            ]));
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:guo-jia",
                "郭嘉",
                "guo_jia",
                "classic:tiandu",
                "wei",
                BaseHp: 3,
                AdditionalSkillIds:
                [
                    "classic:yiji"
                ]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhou-yu",
                "周瑜",
                "zhou_yu",
                "classic:yingzi",
                "wu",
                BaseHp: 3,
                AdditionalSkillIds: ["classic:fanjian"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhuge-liang",
                "诸葛亮",
                "zhuge_liang",
                "classic:guanxing",
                "shu",
                BaseHp: 3,
                AdditionalSkillIds:
                [
                    "classic:kongcheng"
                ]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:cao-cao",
                "曹操",
                "cao_cao",
                "classic:jianxiong",
                "wei",
                BaseHp: 4,
                AdditionalSkillIds: ["classic:hujia"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:huang-gai",
                "黄盖",
                "huang_gai",
                "classic:kujin",
                "wu",
                BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:gan-ning",
                "甘宁",
                "gan_ning",
                "classic:qixi",
                "wu",
                BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:lu-meng",
                "吕蒙",
                "lu_meng",
                "classic:keji",
                "wu",
                BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhang-liao",
                "张辽",
                "zhang_liao",
                "classic:tuxi",
                "wei",
                BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:xu-chu",
                "许褚",
                "xu_chu",
                "classic:luoyi",
                "wei",
                BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:dian-wei",
                "典韦",
                "dian_wei",
                "classic:qiangxi",
                "wei",
                BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:xu-huang",
                "徐晃",
                "xu_huang",
                "classic:duanliang",
                "wei",
                BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhen-ji",
                "甄姬",
                "zhen_ji",
                "classic:luoshen",
                "wei",
                BaseHp: 3,
                AdditionalSkillIds: ["classic:qingguo"],
                Gender: GeneralGender.Female));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:huang-yueying",
                "黄月英",
                "huang_yueying",
                "classic:jizhi",
                "shu",
                BaseHp: 3,
                AdditionalSkillIds:
                [
                    "classic:qicai"
                ],
                Gender: GeneralGender.Female));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:ma-chao",
                "马超",
                "ma_chao",
                "classic:tieqi",
                "shu",
                BaseHp: 4,
                AdditionalSkillIds:
                [
                    "classic:mashu"
                ]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:huang-zhong",
                "黄忠",
                "huang_zhong",
                "classic:liegong",
                "shu",
                BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:wei-yan",
                "魏延",
                "wei_yan",
                "classic:kuanggu",
                "shu",
                BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:lu-bu",
                "吕布",
                "lu_bu",
                "classic:wushuang",
                "qun",
                BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhang-fei",
                "张飞",
                "zhang_fei",
                "classic:paoxiao",
                "shu",
                BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhao-yun",
                "赵云",
                "zhao_yun",
                "classic:longdan",
                "shu",
                BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:guan-yu",
                "关羽",
                "guan_yu",
                "classic:wusheng",
                "shu",
                BaseHp: 4));
        }
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
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:pang-de",
                "庞德",
                "pang_de",
                "classic:mashu",
                "qun",
                BaseHp: 4,
                AdditionalSkillIds: ["classic:mengjin"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:xun-yu",
                "荀彧",
                "xun_yu",
                "classic:quhu",
                "wei",
                BaseHp: 3,
                AdditionalSkillIds:
                [
                    "classic:jieming"
                ]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:yan-liang-wen-chou", "颜良文丑", "yan_liang_wen_chou",
                "classic:shuangxiong", "qun", BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:wolong-zhuge-liang", "卧龙诸葛亮", "wolong_zhuge_liang",
                "classic:bazhen", "shu", BaseHp: 3,
                AdditionalSkillIds: ["classic:huoji", "classic:kanpo"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:pang-tong", "庞统", "pang_tong",
                "classic:lianhuan", "shu", BaseHp: 3,
                AdditionalSkillIds: ["classic:niepan"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:taishi-ci", "太史慈", "taishi_ci",
                "classic:tianyi", "wu", BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:cao-ren", "曹仁", "cao_ren",
                "classic:jushou", "wei", BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:xiao-qiao", "小乔", "xiao_qiao",
                "classic:hongyan", "wu", BaseHp: 3,
                AdditionalSkillIds: ["classic:tianxiang"], Gender: GeneralGender.Female));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhou-tai", "周泰", "zhou_tai",
                "classic:buqu", "wu", BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:yuan-shao", "袁绍", "yuan_shao",
                "classic:luanji", "qun", BaseHp: 4,
                AdditionalSkillIds: ["classic:xueyi"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:xiahou-yuan", "夏侯渊", "xiahou_yuan",
                "classic:shensu", "wei", BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:hua-xiong", "华雄", "hua_xiong",
                "classic:yaowu", "qun", BaseHp: 6));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:gongsun-zan", "公孙瓒", "gongsun_zan",
                "classic:yicong", "qun", BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhang-jiao", "张角", "zhang_jiao",
                "classic:guidao", "qun", BaseHp: 3,
                AdditionalSkillIds: ["classic:leiji", "classic:huangtian"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:sun-jian", "孙坚", "sun_jian",
                "classic:yinghun", "wu", BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:meng-huo", "孟获", "meng_huo",
                "classic:huoshou", "shu", BaseHp: 4,
                AdditionalSkillIds: ["classic:zaiqi"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhu-rong", "祝融", "zhu_rong",
                "classic:juxiang", "shu", BaseHp: 4,
                AdditionalSkillIds: ["classic:lieren"], Gender: GeneralGender.Female));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:yu-jin", "于禁", "yu_jin",
                "classic:yizhong", "wei", BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:xu-shu", "徐庶", "xu_shu",
                "classic:wuyan", "shu", BaseHp: 3,
                AdditionalSkillIds: ["classic:jujian"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "sp:zhao-yun", "SP赵云", "zhao_yun",
                "sp:longdan", "qun", BaseHp: 3,
                AdditionalSkillIds: ["sp:chongzhen"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "boundary:zhang-jiao", "界张角", "boundary_zhang_jiao",
                "boundary:leiji", "qun", BaseHp: 3,
                AdditionalSkillIds: ["boundary:guidao", "boundary:huangtian"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:shen-guan-yu", "神关羽", "shen_guan_yu",
                "classic:wushen", "god", BaseHp: 5,
                AdditionalSkillIds: ["classic:wuhun"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "sp:guan-yu", "SP关羽", "guan_yu",
                "sp:guan-yu-wusheng", "wei", BaseHp: 4,
                AdditionalSkillIds: ["sp:danji"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:yan-yan", "严颜", "yan_yan",
                "classic:juzhan", "shu", BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "mou:lu-meng", "谋吕蒙", "lu_meng",
                "mou:hengye", "wu", BaseHp: 4,
                AdditionalSkillIds: ["mou:yingbo"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:cao-zhang", "曹彰", "cao_zhang",
                "classic:jiangchi", "wei", BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:ma-dai", "马岱", "ma_dai",
                "classic:mashu", "shu", BaseHp: 4,
                AdditionalSkillIds: ["classic:qianxi"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:gao-shun", "高顺", "gao_shun",
                "classic:xianzhen", "qun", BaseHp: 4,
                AdditionalSkillIds: ["classic:jinjiu"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:liu-biao", "刘表", "liu_biao",
                "classic:zishou", "qun", BaseHp: 3,
                AdditionalSkillIds: ["classic:zongshi"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:wang-yi", "王异", "wang_yi",
                "classic:zhenlie", "wei", BaseHp: 3,
                AdditionalSkillIds: ["classic:miji"],
                Gender: GeneralGender.Female));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhong-hui", "钟会", "zhong_hui",
                "classic:quanji", "wei", BaseHp: 4,
                AdditionalSkillIds: ["classic:zili"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:xun-you", "荀攸", "xun_you",
                "classic:qice", "wei", BaseHp: 3,
                AdditionalSkillIds: ["classic:zhiyu"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:liao-hua", "廖化", "liao_hua",
                "classic:dangxian", "shu", BaseHp: 4,
                AdditionalSkillIds: ["classic:fuli"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:guan-xing-zhang-bao", "关兴张苞", "guan_xing_zhang_bao",
                "classic:fuhun", "shu", BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:bu-lian-shi", "步练师", "bu_lian_shi",
                "classic:anxu", "wu", BaseHp: 3,
                AdditionalSkillIds: ["classic:zhuiyi"],
                Gender: GeneralGender.Female));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:cheng-pu", "程普", "cheng_pu",
                "classic:lihuo", "wu", BaseHp: 4,
                AdditionalSkillIds: ["classic:chunlao"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:han-dang", "韩当", "han_dang",
                "classic:gongqi", "wu", BaseHp: 4,
                AdditionalSkillIds: ["classic:jiefan"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:cao-chong", "曹冲", "cao_chong",
                "classic:chengxiang", "wei", BaseHp: 3,
                AdditionalSkillIds: ["classic:renxin"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:guo-huai", "郭淮", "guo_huai",
                "classic:jingce", "wei", BaseHp: 4));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:man-chong", "满宠", "man_chong",
                "classic:junxing", "wei", BaseHp: 3,
                AdditionalSkillIds: ["classic:yuce"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:guan-ping", "关平", "guan_ping",
                "classic:longyin", "shu", BaseHp: 4));
        }

        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhu-huan", "朱桓", "zhu_huan",
                "classic:youdi", "wu", BaseHp: 4));
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:gu-yong", "顾雍", "gu_yong",
                "classic:shenxing", "wu", BaseHp: 3,
                AdditionalSkillIds: ["classic:bingyi"]));
        }

        builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhu-zhi", "朱治", "zhu_zhi",
                "classic:anguo", "wu", BaseHp: 4));

        builder.AddGeneral(new ContentGeneralDefinition(
                "sp:le-jin", "SP乐进", "sp_le_jin",
                "sp:xiaoguo", "wei", BaseHp: 4));

        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "classic:li-dian", "李典", "li_dian",
                "classic:xunxun", "wei", BaseHp: 3,
                AdditionalSkillIds: ["classic:wangxi"]));
            builder.AddGeneral(new ContentGeneralDefinition(
                "boundary:guo-jia", "界郭嘉", "boundary_guo_jia",
                "boundary:tiandu", "wei", BaseHp: 3,
                AdditionalSkillIds: ["boundary:yiji"]));
            builder.AddGeneral(new ContentGeneralDefinition(
                "boundary:cao-cao", "界曹操", "boundary_cao_cao",
                "boundary:jianxiong", "wei", BaseHp: 4,
                AdditionalSkillIds: ["boundary:hujia"]));
        }
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                "boundary:sima-yi", "界司马懿", "boundary_sima_yi",
                "boundary:feedback", "wei", BaseHp: 3,
                AdditionalSkillIds: ["boundary:guicai"]));
            builder.AddGeneral(new ContentGeneralDefinition(
                "boundary:diao-chan", "界貂蝉", "boundary_diao_chan",
                "boundary:lijian", "qun", BaseHp: 3,
                AdditionalSkillIds: ["boundary:biyue"], Gender: GeneralGender.Female));
            builder.AddGeneral(new ContentGeneralDefinition(
                "boundary:zhang-liao", "界张辽", "boundary_zhang_liao",
                "boundary:tuxi", "wei", BaseHp: 4));
        }
        builder.AddGeneral(new ContentGeneralDefinition(
                "boundary:xu-chu", "界许褚", "boundary_xu_chu",
                "boundary:luoyi", "wei", BaseHp: 4));
        builder.AddGeneral(new ContentGeneralDefinition(
                "boundary:gan-ning", "界甘宁", "boundary_gan_ning",
                "boundary:qixi", "wu", BaseHp: 4,
                AdditionalSkillIds: ["boundary:fenwei"]));
        builder.AddGeneral(new ContentGeneralDefinition(
                "boundary:zhou-yu", "界周瑜", "boundary_zhou_yu",
                "boundary:yingzi", "wu", BaseHp: 3,
                AdditionalSkillIds: ["boundary:fanjian"]));
        builder.AddGeneral(new ContentGeneralDefinition(
                "classic:pan-zhang-ma-zhong", "潘璋马忠", "pan_zhang_ma_zhong",
                "classic:duodao", "wu", BaseHp: 4,
                AdditionalSkillIds: ["classic:anjian"]));

        builder.AddGeneral(new ContentGeneralDefinition(
                "classic:xu-sheng", "徐盛", "xu_sheng",
                "classic:pojun", "wu", BaseHp: 4));

        builder.AddGeneral(new ContentGeneralDefinition(
                "boundary:xu-sheng", "界徐盛", "boundary_xu_sheng",
                "boundary:pojun", "wu", BaseHp: 4));

        builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhang-song", "张松", "zhang_song",
                "classic:qiangzhi", "shu", BaseHp: 3,
                AdditionalSkillIds: ["classic:xiantu"]));

        builder.AddGeneral(new ContentGeneralDefinition(
                "boundary:zhang-song", "界张松", "boundary_zhang_song",
                "boundary:qiangzhi", "shu", BaseHp: 3,
                AdditionalSkillIds: ["boundary:xiantu"]));

        builder.AddGeneral(new ContentGeneralDefinition(
                "classic:cao-ang", "曹昂", "cao_ang",
                "classic:kangkai", "wei", BaseHp: 4));

        builder.AddGeneral(new ContentGeneralDefinition(
                "classic:qu-yi", "麹义", "qu_yi",
                "classic:fuqi", "qun", BaseHp: 4,
                AdditionalSkillIds: ["classic:jiaozi"]));

        builder.AddGeneral(new ContentGeneralDefinition(
                "classic:zhang-xiu", "张绣", "zhang_xiu",
                "classic:xiongluan", "qun", BaseHp: 4));

        builder.AddGeneral(new ContentGeneralDefinition(
                "ol:shen-guan-yu", "神关羽·武神", "shen_guan_yu",
                "ol:wushen", "god", BaseHp: 5,
                AdditionalSkillIds: ["classic:wuhun"]));

        builder.AddGeneral(new ContentGeneralDefinition(
                "classic:shen-sima-yi", "神司马懿", "shen_sima_yi",
                "classic:renjie", "god", BaseHp: 4,
                AdditionalSkillIds: ["classic:baiyin"]));

        builder.AddGeneral(new ContentGeneralDefinition(
                "classic:cao-pi", "曹丕", "cao_pi",
                "classic:xingshang", "wei", BaseHp: 3,
                AdditionalSkillIds: ["classic:fangzhu"]));

        builder.AddGeneral(new ContentGeneralDefinition(
                "classic:sun-ce", "孙策", "sun_ce",
                "classic:jiang", "wu", BaseHp: 4,
                AdditionalSkillIds: ["classic:hunzi"]));

        builder.AddGeneral(new ContentGeneralDefinition(
                "classic:cai-wen-ji", "蔡文姬", "cai_wen_ji",
                "classic:beige", "qun", BaseHp: 3,
                AdditionalSkillIds: ["classic:duanchang"]));

        var generalPoolIds = CurrentGeneralIds;

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
            DeckId: "classic:standard-deck",
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
            DeckId: "classic:standard-deck",
            GeneralCandidateCount: 3,
            GeneralPoolIds: generalPoolIds));
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
        definition with { Tags = tags, ExecutionForms = executionForms };

    private static ContentSkillDefinition ShenSimaYiProgram(string skillId)
    {
        var presentation = ClassicShenSimaYiCatalog.Value.Presentations[skillId];
        return new ContentSkillDefinition(skillId, presentation.Name, presentation.Description)
        {
            Program = ClassicShenSimaYiCatalog.Value.Programs[skillId]
        };
    }

    private static ContentSkillDefinition CaoPiProgram(string skillId)
    {
        var presentation = ClassicCaoPiCatalog.Value.Presentations[skillId];
        return new ContentSkillDefinition(skillId, presentation.Name, presentation.Description)
        {
            Program = ClassicCaoPiCatalog.Value.Programs[skillId]
        };
    }

    private static ContentSkillDefinition SunCeProgram(string skillId)
    {
        var presentation = ClassicSunCeCatalog.Value.Presentations[skillId];
        return new ContentSkillDefinition(skillId, presentation.Name, presentation.Description)
        {
            Program = ClassicSunCeCatalog.Value.Programs[skillId]
        };
    }

    private ContentSkillDefinition WithOptionalTriggerMetadata(ContentSkillDefinition definition) =>
        WithStructuredSkillMetadata(
            definition,
            SkillTag.None,
            SkillExecutionForm.Trigger);

    private ContentSkillDefinition WithContinuousStateMetadata(ContentSkillDefinition definition) =>
        WithStructuredSkillMetadata(
            definition,
            SkillTag.None,
            SkillExecutionForm.State);

    private ContentSkillDefinition WithActiveActionMetadata(ContentSkillDefinition definition) =>
        definition with { ActionForms = SkillActionForm.Active };

    private static string ReadEmbeddedText(string resourceName)
    {
        using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded skill-program resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static IReadOnlyList<string> CurrentGeneralIds { get; } =
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
        "classic:zhang-jiao",
        "classic:sun-jian",
        "classic:meng-huo",
        "classic:zhu-rong",
        "classic:yu-jin",
        "classic:xu-shu",
        "sp:zhao-yun",
        "classic:shen-guan-yu",
        "sp:guan-yu",
        "classic:yan-yan",
        "mou:lu-meng",
        "classic:cao-zhang",
        "classic:ma-dai",
        "classic:gao-shun",
        "classic:liu-biao",
        "classic:wang-yi",
        "classic:zhong-hui",
        "classic:xun-you",
        "classic:liao-hua",
        "classic:guan-xing-zhang-bao",
        "classic:bu-lian-shi",
        "classic:cheng-pu",
        "classic:han-dang",
        "classic:cao-chong",
        "classic:guo-huai",
        "classic:man-chong",
        "classic:guan-ping",
        "classic:zhu-huan",
        "classic:gu-yong",
        "classic:li-dian",
        "boundary:sima-yi",
        "boundary:guo-jia",
        "boundary:cao-cao",
        "boundary:diao-chan",
        "boundary:zhang-liao",
        "classic:zhu-zhi",
        "boundary:xu-chu",
        "boundary:gan-ning",
        "boundary:zhou-yu",
        "classic:pan-zhang-ma-zhong",
        "sp:le-jin",
        "classic:xu-sheng",
        "boundary:xu-sheng",
        "classic:zhang-song",
        "boundary:zhang-song",
        "classic:cao-ang",
        "classic:qu-yi",
        "classic:zhang-xiu",
        "ol:shen-guan-yu",
        "classic:shen-sima-yi",
        "classic:cao-pi",
        "classic:sun-ce",
        "classic:cai-wen-ji",
    ];

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
