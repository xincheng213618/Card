using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum Role
{
    Lord,
    Loyalist,
    Rebel,
    Renegade,
    // Compatibility values used by the public-team adapter. They are not
    // identity roles and are never included in an identity RoleCounts map.
    TeamA,
    TeamB,
    Contestant
}

public enum ContentModeKind
{
    Identity,
    Team,
    /// <summary>
    /// A bounded national-war adapter: each seat owns two generals from one
    /// hidden faction, and the faction/general reveal is an explicit action.
    /// </summary>
    NationalWarLite,
    Survival
}

public enum GeneralSelectionSlot
{
    Primary,
    Secondary
}

public enum GeneralGender
{
    Male,
    Female
}

public enum CardKind
{
    Slash,
    Dodge,
    Peach,
    Duel,
    DrawTwo,
    BarbarianAssault,
    ArrowBarrage,
    PeachGarden,
    FiveGrains,
    Dismantlement,
    Snatch,
    FireSlash,
    ThunderSlash,
    Alcohol,
    FireAttack,
    Crossbow,
    BaguaFormation,
    RenwangShield,
    OffensiveHorse,
    DefensiveHorse,
    JadeSeal,
    QinggangSword,
    Nullification,
    IronChain,
    Indulgence,
    SupplyShortage,
    Lightning,
    BorrowedSword,
    StoneAxe,
    ZhangbaSerpentSpear,
    CixiongDoubleSwords,
    QinglongCrescentBlade,
    IceSword,
    QilinBow,
    FangtianHalberd,
    GudingBlade,
    ZhuqueFan,
    Tengjia,
    SilverLion,
    WoodenOx,
    Dawan,
    Zixing,
    Dilu,
    Zhaohuangfeidian,
    Hualiu,
    GhostDragonCrescentBlade,
    ScarletBloodSword,
    XingtianAxe,
    RedBloodBlade = 500,
    GeneralWeapon,
    UnexpectedAssault = 502,
    Xuanjian = 503,
    AllianceFeast = 504,
    WushuangHalberd,
    PurpleGoldCrown,
    RedCottonRobe,
    LionBelt,
    SowBeans = 520,
    TransplantWood,
    ReturnSoul = 540,
    StarShift = 550,
    BeamSwap = 551,
    HandRedistribution = 552,
    WenheChaos = 553,
    Poison = 554,
    NationalStyleRobe,
    CrimsonSoulZither,
    QimenEightTrigrams,
    DustlessGoldenSpear,
    ShuraInfernoHalberd,
    DelusionCrown,
    Zong = 600, RealgarWine = 601, SameBoat = 602, StriveUpstream = 603, AgainstCurrent = 604,
    Intoxication = 620, StoreGrain = 621, Reinforcements = 622, UnitedEffort = 623, ThunderChariot = 624,
    WumiaoFlood = 700,
    QizhengXiangsheng = 710,
    QiBaoDao = 33800,
    BaiBiDao = 33801,
    ZhanhunDao = 5754,
    ColdSlash = 5770,
    AnqiFeiren = 5780,
    YaodaoChitong = 5790,
    ShanheXingtianAxe = 59300,
    Leizhen = 59400,
    DagongChe = 43500,
    MantianGuohai = 47700
}

public enum Suit
{
    Spade,
    Heart,
    Club,
    Diamond,
    None = 500
}

public enum DamageNature
{
    Normal,
    Fire,
    Thunder,
    Cold
}

/// <summary>
/// Stable public counters attached to one player. The enum identifies rules
/// state; translated names remain presentation data in <see cref="PlayerMarkerCatalog"/>.
/// </summary>
public enum PlayerMarkerKind
{
    AnniversaryHuCheErZhong = 49500,
    AnniversaryLeiBoSilei = 49800,
    Pingding = 38100,
    ShenTaishiCiWei = 38200,
    Tianren = 38400,
    ShencaiChi = 38600,
    ShencaiZhang = 38601,
    ShencaiTu = 38602,
    ShencaiLiu = 38603,
    ShencaiSi = 38604,
    QingBanner = 38800,
    MobileWuling = 39100,
    MobileWulingTiger = 39101,
    MobileWulingCrane = 39102,
    MobileWulingBear = 39103,
    MobileWulingApe = 39104,
    MobileWulingDeer = 39105,
    LinCold = 39300,
    MobileShenMaChaoTing = 40625,
    HappyXue = 40940,
    HappyMedicine = 41780,
    HappyGuard = 41980,
    HappyCharmSpade = 42220,
    HappyCharmHeart = 42221,
    HappyCharmClub = 42222,
    HappyCharmDiamond = 42223,
    MouSunShangxiangZhu = 35100,
    MouDongZhuoGuanbian = 34700,
    MouKongRongXian = 34600,
    ShenwuPoison = 27720,
    Nightmare = 0,
    Ren,
    Rage = 400,
    Gale,
    Mist,
    Junlue = 450,
    Camp = 451,
    Huang = 500,
    Orange = 1280,
    Yi = 2600,
    Lie = 3000,
    LiuShi = 3100,
    MouShi = 3150,
    GengZhan = 3200,
    XiZhen = 3250,
    Bian = 3500,
    Zi = 3800,
    ShenJiao = 3900,
    Kangge = 4040,
    Jue = 4050,
    CategoryLei = 4060,
    CategoryFu = 4061,
    CategorySong = 4062,
    QihuiBasic = 20100, QihuiTrick = 20101, QihuiEquipment = 20102,
    LuanZhan = 21200,
    RaoShe = 21500,
    MeiYing = 22600,
    Kui = 23200,
    DragonSeal = 23500,
    PhoenixSeal = 23501,
    Baoli = 23600,
    Ziwei = 24400,
    Houtu = 24401,
    Yuqing = 24402,
    Gouchen = 24403,
    ShanjiaLoss = 24500,
    AdoptedChild = 26020,
    QuxiFeng = 26300,
    QuxiQian = 26301,
    XunyiYi = 26320,
    QingyuanPartner = 26820,
    RanshangRan = 27320,
    CaoPiSong = 28000,
    MoQuanyuChoice = 28910,
    OlMoRumo = 28940,
    MouHuangYueyingQi = 31500,
    MouDaQiaoLiuli = 31300,
    OlMoKuang = 30800,
    MouLixianAvailableNames = 28960,
    MouLixianUsedNames = 28961,
    MouHuaXiongWei = 29100,
    MouGuoHuaiJingce = 32000,
    MouGanNingWei = 31900,
    MouMengHuoCharge = 30900,
    MouJiangWeiCharge = 33200,
    MouJiangWeiBeifa = 33201,
    ShiZhHuiCharge = 33250,
    MouGongsunZanCharge = 33600,
    MouLiuChengChui = 29400,
    MouChenGongCe = 29600,
    MouFaZhengXuan = 31800,
    GuanduGrain = 44000,
    OriginalUseLightning = 44001,
    MuShunJin = 45300,
    XiahouJieLie = 45500
}

public static class PlayerMarkerCatalog
{
    public static string GetDisplayName(PlayerMarkerKind marker) => marker switch
    {
        PlayerMarkerKind.MuShunJin => "劲",
        PlayerMarkerKind.XiahouJieLie => "裂",
        PlayerMarkerKind.Pingding => "平定",
        PlayerMarkerKind.HappyMedicine => "药",
        PlayerMarkerKind.HappyGuard => "卫",
        PlayerMarkerKind.HappyCharmSpade => "魅惑♠",
        PlayerMarkerKind.HappyCharmHeart => "魅惑♥",
        PlayerMarkerKind.HappyCharmClub => "魅惑♣",
        PlayerMarkerKind.HappyCharmDiamond => "魅惑♦",
        PlayerMarkerKind.ShenTaishiCiWei => "围",
        PlayerMarkerKind.Tianren => "天任",
        PlayerMarkerKind.ShencaiChi => "笞",
        PlayerMarkerKind.ShencaiZhang => "杖",
        PlayerMarkerKind.ShencaiTu => "徒",
        PlayerMarkerKind.ShencaiLiu => "流",
        PlayerMarkerKind.ShencaiSi => "死",
        PlayerMarkerKind.QingBanner => "擎",
        PlayerMarkerKind.MobileWuling => "五灵",
        PlayerMarkerKind.MobileWulingTiger => "虎",
        PlayerMarkerKind.MobileWulingCrane => "鹤",
        PlayerMarkerKind.MobileWulingBear => "熊",
        PlayerMarkerKind.MobileWulingApe => "猿",
        PlayerMarkerKind.MobileWulingDeer => "鹿",
        PlayerMarkerKind.LinCold => "凛",
        PlayerMarkerKind.MobileShenMaChaoTing => "霆",
        PlayerMarkerKind.MouDongZhuoGuanbian => "观变",
        PlayerMarkerKind.MouSunShangxiangZhu => "助",
        PlayerMarkerKind.MouKongRongXian => "贤",
        PlayerMarkerKind.GuanduGrain => "粮",
        PlayerMarkerKind.OriginalUseLightning => "闪电",
        PlayerMarkerKind.CaoPiSong => "颂",
        PlayerMarkerKind.MoQuanyuChoice => "权御",
        PlayerMarkerKind.OlMoRumo => "入魔",
        PlayerMarkerKind.MouHuangYueyingQi => "奇",
        PlayerMarkerKind.MouDaQiaoLiuli => "流离",
        PlayerMarkerKind.OlMoKuang => "狂",
        PlayerMarkerKind.MouLixianAvailableNames => "理贤牌名",
        PlayerMarkerKind.MouLixianUsedNames => "理贤已用牌名",
        PlayerMarkerKind.MouHuaXiongWei => "威",
        PlayerMarkerKind.MouGuoHuaiJingce => "精策",
        PlayerMarkerKind.MouGanNingWei => "威",
        PlayerMarkerKind.MouMengHuoCharge => "蓄力",
        PlayerMarkerKind.MouJiangWeiCharge => "蓄力",
        PlayerMarkerKind.MouJiangWeiBeifa => "北伐",
        PlayerMarkerKind.ShiZhHuiCharge => "蓄力",
        PlayerMarkerKind.MouGongsunZanCharge => "蓄力",
        PlayerMarkerKind.MouLiuChengChui => "椎",
        PlayerMarkerKind.MouChenGongCe => "策",
        PlayerMarkerKind.AdoptedChild => "义子",
        PlayerMarkerKind.QuxiFeng => "丰",
        PlayerMarkerKind.QuxiQian => "歉",
        PlayerMarkerKind.XunyiYi => "义",
        PlayerMarkerKind.QingyuanPartner => "轻缘",
        PlayerMarkerKind.MouFaZhengXuan => "眩",
        PlayerMarkerKind.LuanZhan => "乱战",
        PlayerMarkerKind.RaoShe => "饶舌",
        PlayerMarkerKind.AnniversaryHuCheErZhong => "重",
        PlayerMarkerKind.AnniversaryLeiBoSilei => "私掠",
        PlayerMarkerKind.MeiYing => "梅影",
        PlayerMarkerKind.Kui => "傀",
        PlayerMarkerKind.DragonSeal => "龙印",
        PlayerMarkerKind.PhoenixSeal => "凤印",
        PlayerMarkerKind.Baoli => "暴戾",
        PlayerMarkerKind.Ziwei => "紫微",
        PlayerMarkerKind.Houtu => "后土",
        PlayerMarkerKind.Yuqing => "玉清",
        PlayerMarkerKind.Gouchen => "勾陈",
        PlayerMarkerKind.ShanjiaLoss => "损",
        PlayerMarkerKind.QihuiBasic => "启诲·基本",
        PlayerMarkerKind.QihuiTrick => "启诲·锦囊",
        PlayerMarkerKind.QihuiEquipment => "启诲·装备",
        PlayerMarkerKind.Nightmare => "梦魇",
        PlayerMarkerKind.HappyXue => "学",
        PlayerMarkerKind.Huang => "黄",
        PlayerMarkerKind.Orange => "橘",
        PlayerMarkerKind.Yi => "裔",
        PlayerMarkerKind.Lie => "烈",
        PlayerMarkerKind.LiuShi => "流矢",
        PlayerMarkerKind.MouShi => "谋识",
        PlayerMarkerKind.GengZhan => "更战",
        PlayerMarkerKind.XiZhen => "袭阵",
        PlayerMarkerKind.Bian => "变",
        PlayerMarkerKind.Zi => "辎",
        PlayerMarkerKind.ShenJiao => "省身",
        PlayerMarkerKind.Ren => "忍",
        PlayerMarkerKind.Rage => "暴怒",
        PlayerMarkerKind.ShenwuPoison => "蛇毒",
        PlayerMarkerKind.Gale => "狂风",
        PlayerMarkerKind.Mist => "大雾",
        PlayerMarkerKind.Junlue => "军略",
        PlayerMarkerKind.Camp => "营",
        PlayerMarkerKind.RanshangRan => "燃",
        PlayerMarkerKind.Kangge => "歌",
        PlayerMarkerKind.Jue => "爵",
        PlayerMarkerKind.CategoryLei => "诔",
        PlayerMarkerKind.CategoryFu => "赋",
        PlayerMarkerKind.CategorySong => "颂",
        _ => throw new InvalidOperationException($"Unknown public player marker '{marker}'.")
    };
}

public enum PublicAttackKind
{
    Slash,
    Duel,
    GroupAttack,
    FireAttack
}

/// <summary>
/// The complete public input an AI may receive for an identity-mode attack.
/// Hidden roles, hands, deck order and resolution state are intentionally not
/// representable by this contract.
/// </summary>
public sealed record PublicAttackEvidence(
    PublicAttackKind Kind,
    int SourceSeat,
    int TargetSeat,
    int LordSeat,
    Role? RevealedTargetRole);

public enum DamageTriggerScope
{
    DamagedPlayer,
    OtherLivingPlayer,
    AnyLivingPlayer,
    DamageSource
}

public enum TurnPhase
{
    NotStarted,
    Draw,
    Play,
    Discard,
    Finished
}

public enum DecisionKind
{
    RecoveryReplacement = 2700,
    SelectGeneral = 0,
    PlayCard,
    RespondDodge,
    RespondSlash,
    RescueDying,
    SelectHarvestCard,
    FireAttackReveal,
    FireAttackDiscard,
    Nullification,
    DiscardCards,
    SelectTargetCard,
    SkipDiscardPolicy,
    Yingbo,
    StoneAxe,
    CixiongDoubleSwords,
    QinglongCrescentBlade,
    IceSword,
    QilinBow,
    ZhuqueFan,
    ProgramJudgmentTrigger,
    ProgramJudgmentReplacement,
    ProgramTopReorder,
    ProgramRepeatJudgment,
    SelectFaction,
    SkillModule,
    ProgramTrigger,
    OpeningHand
}

public enum JiangchiMode
{
    Skipped,
    DrawMore,
    Assault
}

public enum CardColor
{
    Red,
    Black
}


public enum EngineStatus
{
    NotStarted,
    Running,
    AwaitingHumanGeneralSelection,
    AwaitingHumanPlay,
    AwaitingHumanResponse,
    AwaitingHumanDying,
    AwaitingHumanCardSelection,
    Completed,
    AwaitingHumanDiscard,
    AwaitingHumanFactionSelection,
    Faulted,
    AwaitingHumanOpeningHand
}

public enum Winner
{
    None,
    LordAndLoyalists,
    Rebels,
    Renegade,
    Draw,
    TeamA,
    TeamB,
    NationalFactionA,
    NationalFactionB,
    NationalFactionC,
    SoleSurvivor,
    RankedTeams
}

public enum LegalActionKind
{
    Slash,
    Peach,
    Duel,
    DrawTwo,
    BarbarianAssault,
    ArrowBarrage,
    PeachGarden,
    FiveGrains,
    Dismantlement,
    Snatch,
    FireAttack,
    FireAttackReveal,
    FireAttackDiscard,
    SkipFireAttack,
    SelectHarvestCard,
    EndPlay,
    Alcohol,
    Yuanhu,
    SkipYuanhu,
    Equip,
    Nullification,
    SkipNullification,
    IronChain,
    Indulgence,
    SupplyShortage,
    Lightning,
    Ganglie,
    SkipGanglie,
    GanglieDiscardTwo,
    GanglieLoseHp,
    Guicai,
    SkipGuicai,
    Tuxi,
    SkipTuxi,
    Luoyi,
    SkipLuoyi,
    SkillChoice,
    RevealGeneral,
    Recast,
    BorrowedSword,
    UseEquipmentEffect,
    UseProgramSkill,
    UnexpectedAssault = 502,
    AllianceFeast = 504,
    SowBeans = 520,
    TransplantWood,
    ReturnSoul = 540,
    StarShift = 550,
    BeamSwap = 551,
    HandRedistribution = 552,
    WenheChaos = 553,
    Zong = 600, RealgarWine = 601, SameBoat = 602, StriveUpstream = 603, AgainstCurrent = 604,
    Intoxication = 620, StoreGrain = 621, Reinforcements = 622, UnitedEffort = 623,
    WumiaoFlood = 700,
    QizhengXiangsheng = 710,
    MantianGuohai = 47700
}

public sealed record GameOptions
{
    /// <summary>Validated, immutable opening recipe for one local story-campaign encounter.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Campaigns.ShanheBattleSetup? ShanheBattle { get; init; }

    public int Seed { get; init; } = 20260907;

    /// <summary>Supported identity-table sizes for the current rules adapter.</summary>
    public int PlayerCount { get; init; } = 8;

    /// <summary>-1 means that every seat is controlled by the built-in AI.</summary>
    public int HumanSeat { get; init; } = 0;

    /// <summary>
    /// Explicit externally controlled seats for a multiplayer identity match.
    /// Null preserves the legacy single-player HumanSeat contract.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? HumanSeats { get; init; }

    /// <summary>
    /// The demo defaults to a human Lord so the first interaction is immediate.
    /// Set this to null to shuffle the human role as well.
    /// </summary>
    public Role? HumanRole { get; init; } = Role.Lord;

    /// <summary>
    /// Optional namespaced mode id. When omitted, the identity mode matching
    /// <see cref="PlayerCount"/> is selected from the supplied content registry.
    /// </summary>
    public string? ModeId { get; init; }

    /// <summary>The optional namespaced deck id; the selected mode supplies the default.</summary>
    public string? DeckId { get; init; }

    /// <summary>
    /// Optional public team override for a team mode. Identity modes ignore this
    /// value; a null value lets the mode assign a deterministic random team.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HumanTeamId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GuanduSeatRole? HumanGuanduRole { get; init; }

    /// <summary>Free local Wenhe aspiration: top six, four, two, or champion. No account currency is involved.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WenheAspirationRank { get; init; }

    /// <summary>
    /// Runs the deterministic seat/identity/general-selection/deal pipeline.
    /// False retains the original constructor-assigned demo as a compatibility
    /// adapter while hosts migrate to the prompt-based setup flow.
    /// </summary>
    public bool UseInteractiveSetup { get; init; }

    /// <summary>Let the human confirm or reroll their private initial hand before game-start rules and the first turn.</summary>
    public bool ConfirmOpeningHand { get; init; }

    /// <summary>Human players choose their hand-limit discards. Disable for unattended demos.</summary>
    public bool UseInteractiveDiscard { get; init; } = true;

    /// <summary>
    /// Continue selection, play and response commands to the next human boundary.
    /// A paced host sets this to false and submits AdvanceOneStepCommand between
    /// decisions. The checkpoint stores this policy so replay uses identical boundaries.
    /// </summary>
    public bool AdvanceAfterHumanCommands { get; init; } = true;

    /// <summary>
    /// Recorded decision policy. Missing values in older checkpoints retain v1;
    /// hosts opt new games into AiDecisionPolicy.CurrentVersion. Never change a policy already used by a save.
    /// </summary>
    public int AiPolicyVersion { get; init; } = 1;

    public int MaxTurns { get; init; } = 400;
}

public sealed record Card(int Id, CardKind Kind, Suit Suit, int Rank)
{
    public string? PrintedName { get; init; }
    public int? PrintedAttackRange { get; init; }
    public IReadOnlyList<string> GrantedSkillIds { get; init; } = [];
    public bool IsGeneralWeapon { get; init; }
    public string DisplayName => PrintedName ?? CardCatalog.Get(Kind).DisplayName;

    public string RankText => Rank switch
    {
        0 => "",
        1 => "A",
        11 => "J",
        12 => "Q",
        13 => "K",
        _ => Rank.ToString()
    };
}

public sealed partial record GeneralSkillDefinition(
    string Name,
    string Description)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentId { get; init; }

    public string? Id => ContentId;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SkillTag Tags { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SkillExecutionForm ExecutionForms { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SkillActionForm ActionForms { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SkillViewAsOpportunity>? ViewAsOpportunities { get; init; }
    public IReadOnlyDictionary<Role, double>? SelectionWeights { get; init; }
    public SkillRevealWeights? RevealWeights { get; init; }
}

public sealed record SkillRevealWeights(double Base = 4, double Wounded = 0,
    double EmptyHand = 0, double RepeatedSlash = 0);

public sealed record SkillViewAsOpportunity(
    CardKind OutputKind,
    IReadOnlyList<CardKind> InputKinds,
    IReadOnlyList<Suit> InputSuits,
    bool ForPlay,
    bool ForResponse);

public sealed record SkillUsageStateSnapshot(
    string UsageId,
    SkillUsageScope Scope,
    int Count);

/// <summary>
/// Public runtime state for one currently owned skill. The content definition
/// remains immutable; acquisitions, use records and conversion polarity belong
/// to one player in one match.
/// </summary>
public sealed record SkillRuntimeStateSnapshot(
    string SkillId,
    bool IsAcquired,
    IReadOnlyList<SkillUsageStateSnapshot> Usages,
    SkillPolarity? Polarity = null,
    IReadOnlyList<ProgramBooleanStateSnapshot>? BooleanStates = null,
    IReadOnlyList<DirectedTurnCardPolicy>? DirectedPolicies = null,
    IReadOnlyList<TurnCardActionProhibition>? ActionProhibitions = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ProgramPublicRuleStateSnapshot>? PublicRuleStates { get; init; }
}
public enum ProgramPublicRuleStateKind { ActivationLimit = 1160, PersistentHandLimit = 1161, SelfTargetProhibition = 1162, GameActivationProgress = 1163 }
public sealed record ProgramPublicRuleStateSnapshot(string SkillInstanceId,string StateId,ProgramPublicRuleStateKind Kind,int Value,int? Used = null,SkillUsageScope? Scope = null);

public sealed record ProgramAlternatingChoiceStateSnapshot(string SkillId, string StateId, string SkillInstanceId,
    string? PendingOptionId, int NextDrawBonus, int NextTargetBonus);

public sealed record ProgramBooleanStateSnapshot(
    string SkillInstanceId, string StateId, bool Value, string Text);

public sealed partial record GeneralDefinition(
    string Id,
    string Name,
    string PortraitKey,
    IReadOnlyList<GeneralSkillDefinition> Skills,
    string? FactionId = null,
    int BaseHp = 4,
    GeneralGender Gender = GeneralGender.Male)
{
    public string SkillSummary => string.Join(" / ", Skills.Select(skill => skill.Name));
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? InitialHp { get; init; }

    public string SkillDescriptionSummary => string.Join(
        Environment.NewLine,
        Skills.Select(skill => $"{skill.Name}：{skill.Description}"));

}

public sealed record CardSnapshot(
    int Id,
    CardKind Kind,
    Suit Suit,
    int Rank,
    string DisplayName,
    string RankText)
{
    // Viewer-only sentinel. It is deliberately not a CardKind enum member or
    // an implemented physical card and cannot be used as a command material.
    public const CardKind UndisclosedKind = (CardKind)(-1);
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EquipmentSlot? OccupiedEquipmentSlot { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsBone { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? PublicHandOwnerSeat { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SkillMarkLabel { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CardKind? QinJudgmentSlot { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CardKind? IdentityKind { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? IdentitySkillName { get; init; }
}

public sealed record PlayerMarkerSnapshot(
    PlayerMarkerKind Kind,
    string Name,
    int Count);

public sealed partial record PlayerSnapshot(
    int Seat,
    string Name,
    bool IsHuman,
    Role? Role,
    bool IsRoleRevealed,
    string GeneralId,
    string GeneralName,
    string PortraitKey,
    int Hp,
    int MaxHp,
    bool IsAlive,
    int HandCount,
    IReadOnlyList<CardSnapshot> Hand,
    bool IsGeneralPublic = true,
    bool HasAlcoholEffect = false);

public sealed partial record PlayerSnapshot
{
    /// <summary>The viewer may use or play this player's real hand through an issued grant.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ViewerCanUseHand { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HappyQiaoDualHealth? HappyQiaoHealth { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IdentityConcealed { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HpUndefined { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? HandLimit { get; init; }
    /// <summary>Current unconditional damage cap from enabled skills visible to this viewer.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? IncomingDamageCap { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? AlcoholDamageBonus { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<EquipmentSlot, int>? EquipmentSlotCapacities { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PrivateReserveCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardSnapshot>? PrivateReserveCards { get; init; }
    /// <summary>
    /// Public typed counters. Null preserves the serialized shape of old rules
    /// and of players that currently have no marks.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PlayerMarkerSnapshot>? Markers { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsFaceDown { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int WoodenOxGrainCount { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardSnapshot>? WoodenOxGrain { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, int>? ConfiguredConversionTiers { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ProgramAlternatingChoiceStateSnapshot>? AlternatingChoiceStates { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BeneficiarySuitShield>? BeneficiarySuitShields { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ActualPlayPhaseCardUseState? ActualPlayPhaseCardUseState { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<IssuedPlayPhaseUseProhibition>? IssuedPlayPhaseUseProhibitions { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<DeferredHandAlignment>? DeferredHandAlignments { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<IssuedPlayPhaseSuitUseAllowance>? IssuedPlayPhaseSuitUseAllowances { get; init; }

    /// <summary>
    /// Ordered skills visible with the primary general. Null means this viewer
    /// cannot see the general; an empty collection means no visible skills.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<GeneralSkillDefinition>? Skills { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SkillRuntimeStateSnapshot>? SkillRuntimeStates { get; init; }

    /// <summary>
    /// Public team membership for team modes. Identity modes keep this null, and
    /// hidden-information modes never use it to expose an identity.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TeamId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsTeamRevealed { get; init; }

    /// <summary>
    /// Equipment is public state. It is kept outside the positional constructor
    /// so older hosts can continue constructing snapshots unchanged.
    /// </summary>
    public IReadOnlyList<CardSnapshot> Equipment { get; init; } = Array.Empty<CardSnapshot>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsEquipmentAreaAbolished { get; init; }

    /// <summary>Public elemental-link state. Hidden cards and identities remain filtered separately.</summary>
    public bool IsChained { get; init; }

    /// <summary>
    /// Delayed cards are public while they wait in a player's judgment zone.
    /// The property is outside the positional constructor for compatibility
    /// with older hosts that construct snapshots directly.
    /// </summary>
    public IReadOnlyList<CardSnapshot> Judgment { get; init; } = Array.Empty<CardSnapshot>();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsJudgmentAreaAbolished { get; init; }

    /// <summary>Public wound cards kept on Zhou Tai by classic Buqu.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardSnapshot>? BuquWounds { get; init; }

    /// <summary>Public cards placed in the authority pile on a general card.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardSnapshot>? AuthorityCards { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuthorityName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AuthorityCount { get; init; }

    /// <summary>Public "醇" cards placed on classic Cheng Pu's general card.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardSnapshot>? ChunlaoCards { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ChunlaoCount { get; init; }

    /// <summary>Public cards deposited on this character until its next turn.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardSnapshot>? PublicDeferredPileCards { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PublicDeferredPileName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PublicDeferredPileCount { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardSnapshot>? PublicPersistentPileCards { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PublicPersistentPileName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PublicPersistentPileSkillId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PublicPersistentPileCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PublicPersistentPileSnapshot>? PublicPersistentPiles { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PrivateGeneralLibrarySnapshot>? PrivateGeneralLibraries {get;init;}
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PrivateTurnHoldSnapshot>? PrivateTurnHolds { get; init; }

    /// <summary>
    /// Cards held face-down on this character's general card by Xu Sheng's Pojun.
    /// The movement is public, so every viewer sees the held identities.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardSnapshot>? PojunHoldCards { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int PojunHoldCount { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int RoundHoldCount { get; init; }

    /// <summary>
    /// Private/public effective faction metadata. In national war, a viewer may
    /// receive its own hidden faction before it is revealed. In identity mode,
    /// a god general receives the selected Wei/Shu/Wu/Qun faction while its
    /// immutable content definition keeps the printed "god" faction.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FactionId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsFactionRevealed { get; init; }

    /// <summary>Viewer-visible second general data for dual-general modes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SecondaryGeneralId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SecondaryGeneralName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SecondaryPortraitKey { get; init; }

    /// <summary>
    /// Ordered skills visible with the secondary general. Null means this
    /// viewer cannot see the slot; an empty collection means no visible skills.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<GeneralSkillDefinition>? SecondarySkills { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsSecondaryGeneralPublic { get; init; }
}

public sealed partial record PendingDecision(
    DecisionKind Kind,
    int PlayerSeat,
    string Prompt,
    IReadOnlyList<int> ValidCardIds,
    IReadOnlyList<int> ValidTargetSeats,
    int? SourceSeat = null,
    CardKind? IncomingCard = null);

public sealed partial record PendingDecision
{
    /// <summary>Optional content-owned presentation for a generic skill choice.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillPromptPresentation? SkillPrompt { get; init; }

    /// <summary>Stable id of this exact prompt; zero means legacy construction.</summary>
    public PromptId PromptId { get; init; }

    /// <summary>Engine revision at which this prompt was published.</summary>
    public long Revision { get; init; }

    /// <summary>Owning frame of an opt-in participant prompt; each actor receives only its private choices.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ParticipantFrameId { get; init; }

    /// <summary>
    /// For DiscardCards, choose exactly this many distinct cards from ValidCardIds.
    /// Submit DiscardCardsCommand; Choices is empty to avoid enumerating every subset.
    /// For a projected exact owned-card draft, this is the remaining count;
    /// answer its current published choices in order through AnswerPromptCommand.
    /// </summary>
    public int RequiredCardCount { get; init; }

    /// <summary>
    /// Viewer-only lower bound for a program's local owned-card selection.
    /// RequiredCardCount is its remaining upper bound. Null retains exact-count semantics.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MinimumCardCount { get; init; }

    /// <summary>
    /// Complete choices for K2 callers. The old card/target collections remain
    /// as compatibility projections for the current WPF adapter.
    /// </summary>
    public IReadOnlyList<PromptChoice> Choices { get; init; } = [];

    /// <summary>All current prompts are private to their responder.</summary>
    public bool IsPrivate { get; init; } = true;

    /// <summary>Content ids accepted by a setup/content selection prompt.</summary>
    public IReadOnlyList<string> ValidContentIds { get; init; } = [];

    /// <summary>Optional target of a response prompt, such as the dying player.</summary>
    public int? TargetSeat { get; init; }

    /// <summary>
    /// The exact card kind required by a generic response window. IncomingCard
    /// remains the card/effect being answered for compatibility with the first
    /// Slash/Dodge slice.
    /// </summary>
    public CardKind? RequiredCardKind { get; init; }

}

/// <summary>A currently legal single-card active-skill cost and recipient.</summary>
public sealed record ActiveSkillCardTarget(int CardId, int TargetSeat);

public sealed record LegalAction
{
    public LegalAction(
        LegalActionKind Kind,
        int? CardId,
        int? TargetSeat,
        string Description,
        CardKind? PlayedCardKind = null,
        int? TargetCardId = null,
        IReadOnlyList<int>? TargetSeats = null,
        int MinCardCount = 0,
        int MaxCardCount = 0,
        int MinTargetCount = 0,
        int MaxTargetCount = 0,
        CardKind? EquipmentKind = null)
    {
        this.Kind = Kind;
        this.CardId = CardId;
        this.TargetSeat = TargetSeat;
        this.Description = Description;
        this.PlayedCardKind = PlayedCardKind;
        this.TargetCardId = TargetCardId;
        this.MinCardCount = MinCardCount;
        this.MaxCardCount = MaxCardCount;
        this.MinTargetCount = MinTargetCount;
        this.MaxTargetCount = MaxTargetCount;
        this.EquipmentKind = EquipmentKind;
        this.TargetSeats = TargetSeats is { } explicitTargets
            ? Array.AsReadOnly(explicitTargets.ToArray())
            : TargetSeat is { } singleTarget
                ? [singleTarget]
                : [];
    }

    public LegalActionKind Kind { get; init; }
    public int? CardId { get; init; }
    public int? TargetSeat { get; init; }
    public string Description { get; init; }
    public CardKind? PlayedCardKind { get; init; }
    public int? TargetCardId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TargetHandCardPreselection? TargetHandPreselection { get; init; }

    /// <summary>Selected activation cards must all share one printed suit.</summary>
    public bool SelectedCardsSameSuit { get; init; }
    public bool SelectedCardsDistinctSuits { get; init; }

    /// <summary>
    /// At least two selected activation cards must have one printed name (all Slash variants
    /// share a name), or every selected card must be equipment.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SelectedCardsSameNameOrAllEquipment { get; init; }

    /// <summary>Identifies the equipment whose active conversion creates this action.</summary>
    public CardKind? EquipmentKind { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProgramSkillId { get; init; }

    public string? SkillId => ProgramSkillId;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProgramActivationId { get; init; }

    /// <summary>
    /// The character who owns a configured skill when its play-phase entry is
    /// granted to another actor. Null retains the ordinary self-owned action.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ProgramSkillOwnerSeat { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardConversionSource? ConversionSource { get; init; }

    /// <summary>Authorizes one public-pile original entity without converting it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardConversionSource? NativePublicPileSource { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramTieredRoundConversionPolicy? TieredRoundZeroUse { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ProgramRoundOnceCardlessBasicPolicy? RoundOnceCardlessBasicUse { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DrawFundedDistinctBasicPolicy? DrawFundedDistinctBasicUse { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ChainedStateBasicUse { get; init; }

    /// <summary>
    /// Ordered public conversions applied after <see cref="ConversionSource"/>.
    /// A chained view-as keeps every source visible to replay and trigger facts.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardConversionSource>? AdditionalConversionSources { get; init; }


    /// <summary>
    /// Selection bounds for a cardless active-skill action. The prompt carries
    /// the corresponding private candidate ids; legal action data carries the
    /// stable rule contract without enumerating every subset.
    /// </summary>
    public int MinCardCount { get; init; }

    public int MaxCardCount { get; init; }

    public int MinTargetCount { get; init; }

    public int MaxTargetCount { get; init; }

    /// <summary>
    /// Candidate cards for an active-skill draft. This lets presentation layers
    /// distinguish multiple simultaneously available active skills.
    /// </summary>
    public IReadOnlyList<int> SelectableCardIds { get; init; } = [];

    /// <summary>
    /// Candidate targets for an active-skill draft. TargetSeats remains the
    /// exact committed target set for ordinary card actions.
    /// </summary>
    public IReadOnlyList<int> SelectableTargetSeats { get; init; } = [];

    /// <summary>
    /// Exact single-card/single-target associations for correlated active-skill
    /// inputs. Null retains independently selectable cards and targets.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ActiveSkillCardTarget>? SelectableCardTargets { get; init; }

    /// <summary>
    /// Complete ordered target selections for correlated active-skill inputs.
    /// Null retains independently selectable targets.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<IReadOnlyList<int>>? SelectableTargetSequences { get; init; }

    /// <summary>
    /// The complete ordered target selection. Single-target legacy actions are
    /// projected from TargetSeat; multi-target actions publish their exact
    /// combination here so callers never have to reconstruct legal pairs.
    /// </summary>
    public IReadOnlyList<int> TargetSeats { get; init; }

    /// <summary>Identifies the general slot for a national-war reveal action.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeneralSelectionSlot? GeneralSlot { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillProgramAiHint? ProgramAiHint { get; init; }
}

public sealed record GameSnapshot(
    int? Seed,
    int HumanSeat,
    EngineStatus Status,
    Winner Winner,
    int TurnNumber,
    int CurrentSeat,
    TurnPhase Phase,
    int DrawPileCount,
    int DiscardPileCount,
    IReadOnlyList<PlayerSnapshot> Players,
    PendingDecision? PendingDecision,
    int ProcessingCardCount = 0,
    long Revision = 0)
{
    /// <summary>Public clockwise participant identities; null in older externally constructed views.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? TableOrder { get; init; }
    /// <summary>Shared public benevolence pile, in arrival order from oldest to newest.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardSnapshot>? BenevolenceCards { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardDeclarationSnapshot>? CardDeclarations { get; init; }
    /// <summary>Public winning team id for team modes; null for identity results.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WinnerTeamId { get; init; }

    /// <summary>
    /// Cards intentionally revealed by a public effect, such as FiveGrains.
    /// This is separate from private hands and is empty outside that effect.
    /// </summary>
    public IReadOnlyList<CardSnapshot> PublicRevealedCards { get; init; } = Array.Empty<CardSnapshot>();

    /// <summary>The current public FiveGrains recipient, including its counterspell window.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? HarvestPickerSeat { get; init; }

    /// <summary>Cards privately viewed by this snapshot's viewer; absent for every other viewer.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardSnapshot>? PrivateRevealedCards { get; init; }
    /// <summary>Issued forecasts visible to this viewer: private to their owner until deliberately revealed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<FriendPredictionView>? FriendPredictions { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ProgramResponseExchangeStateSnapshot>? ProgramResponseExchangeStates { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TurnProhibitedPhysicalCardsSnapshot>? TurnProhibitedPhysicalCards { get; init; }

    /// <summary>Mode metadata used by view-scoped AI and presentation adapters.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ContentModeKind ModeKind { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WinnerFactionId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WinnerSeat { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WenheRoundRuleSnapshot? WenheRule { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WenheAspirationSnapshot? WenheAspiration { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DragonBoatTeamSnapshot>? DragonBoatStandings { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DragonBoatDraftSnapshot? DragonBoatDraft { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GuanduActivitySnapshot? GuanduActivity { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WushuangSnapshot? Wushuang { get; init; }
}

public sealed record EngineRunResult(
    EngineStatus Status,
    Winner Winner,
    GameSnapshot State,
    PendingDecision? PendingDecision,
    long Revision = 0)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WinnerSeat { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WinnerTeamId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WinnerFactionId { get; init; }
}

public sealed record GameLogEntry(
    int Sequence,
    int TurnNumber,
    string Type,
    string Message,
    int? ActorSeat = null,
    int? TargetSeat = null);

/// <summary>
/// A diagnostic captured when a UI/host observer throws while receiving an
/// already-committed engine notification. Observer failures never roll back or
/// interrupt game state.
/// </summary>
public sealed record ObserverFailure(
    int Sequence,
    string NotificationType,
    string ExceptionType,
    string Message);

public sealed record AiCandidateScore(
    LegalAction Action,
    double Score,
    string Reason);

public sealed record AiThoughtRecord(
    int Sequence,
    int TurnNumber,
    int ActorSeat,
    string Decision,
    IReadOnlyList<AiCandidateScore> Candidates,
    string Summary);

public sealed record AiGeneralCandidateScore(
    string GeneralId,
    string GeneralName,
    string SkillName,
    double Score,
    string Reason);

/// <summary>
/// Trusted-host diagnostic for a private AI general selection. It is not part
/// of any player snapshot or network payload.
/// </summary>
public sealed record AiGeneralThought(
    int Sequence,
    int TurnNumber,
    int ActorSeat,
    string SelectedGeneralId,
    string SelectedGeneralName,
    IReadOnlyList<AiGeneralCandidateScore> Candidates,
    string Summary);

public sealed record PlayerLifeState(Role Role, bool IsAlive);

public sealed record TeamLifeState(string TeamId, bool IsAlive);

public sealed record FactionLifeState(string FactionId, bool IsAlive);

/// <summary>
/// Public inputs for selecting the living players tied at the greatest
/// positive count of one marker source. The source attribution itself stays
/// trusted engine state so hidden skill ownership cannot leak through views.
/// </summary>
public sealed record PlayerMarkerCandidateState(int Seat, bool IsAlive, int Count);

public static class SnapshotJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(GameSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Options);
}
