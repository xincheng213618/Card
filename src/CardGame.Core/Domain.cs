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
    TeamB
}

public enum ContentModeKind
{
    Identity,
    Team,
    /// <summary>
    /// A bounded national-war adapter: each seat owns two generals from one
    /// hidden faction, and the faction/general reveal is an explicit action.
    /// </summary>
    NationalWarLite
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
    Hualiu
}

public enum Suit
{
    Spade,
    Heart,
    Club,
    Diamond
}

public enum DamageNature
{
    Normal,
    Fire,
    Thunder
}

/// <summary>
/// Stable public counters attached to one player. The enum identifies rules
/// state; translated names remain presentation data in <see cref="PlayerMarkerCatalog"/>.
/// </summary>
public enum PlayerMarkerKind
{
    Nightmare
}

public static class PlayerMarkerCatalog
{
    public static string GetDisplayName(PlayerMarkerKind marker) => marker switch
    {
        PlayerMarkerKind.Nightmare => "梦魇",
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

public enum SkillKind
{
    None,
    Jianxiong,
    Paoxiao,
    Yingzi,
    Kongcheng,
    Feedback,
    Wusheng,
    Longdan,
    Yiji,
    Jieming,
    Yuanhu,
    Ganglie,
    Guicai,
    Guidao,
    Leiji,
    Huangtian,
    Tiandu,
    Fanjian,
    Guanxing,
    Kujin,
    Zhiheng,
    Rende,
    Qingnang,
    Huichun,
    Mashu,
    Qicai,
    Jijiu,
    Hujia,
    Jijiang,
    Jiuyuan,
    Qixi,
    Keji,
    Tuxi,
    Luoyi,
    Qiangxi,
    Duanliang,
    Luoshen,
    Qingguo,
    Jizhi,
    Tieqi,
    Liegong,
    Kuanggu,
    Wushuang,
    Guose,
    Liuli,
    Lijian,
    Biyue,
    Jieyin,
    Xiaoji,
    Qianxun,
    Lianying,
    Mengjin,
    Quhu,
    Shuangxiong,
    Bazhen,
    Huoji,
    Kanpo,
    Lianhuan,
    Niepan,
    Tianyi,
    Jushou,
    Hongyan,
    Tianxiang,
    Buqu,
    Luanji,
    Xueyi,
    Shensu,
    Yaowu,
    Yicong,
    Yinghun,
    Huoshou,
    Zaiqi,
    Juxiang,
    Lieren,
    Yizhong,
    Wuyan,
    Jujian,
    Wuhun,
    Xianzhen,
    Zishou,
    Zongshi,
    Zhenlie,
    Miji,
    Quanji,
    Zili,
    Paiyi,
    Qice,
    Zhiyu,
    Dangxian,
    Fuli,
    Fuhun,
    Anxu,
    Zhuiyi,
    Lihuo,
    Chunlao,
    Gongqi,
    Jiefan,
    Chengxiang,
    Renxin,
    Jingce,
    Junxing,
    Yuce,
    Longyin
}

public enum DecisionKind
{
    SelectGeneral,
    PlayCard,
    RespondDodge,
    RespondSlash,
    RescueDying,
    SelectHarvestCard,
    FireAttackReveal,
    FireAttackDiscard,
    Feedback,
    Yiji,
    Jieming,
    Yuanhu,
    Nullification,
    Ganglie,
    GangliePunish,
    Guicai,
    Guidao,
    Leiji,
    Yingzi,
    Tiandu,
    Fanjian,
    Guanxing,
    DiscardCards,
    SelectTargetCard,
    Keji,
    Liuli,
    Biyue,
    Tuxi,
    Luoyi,
    Luoshen,
    Jizhi,
    Tieqi,
    Liegong,
    Juzhan,
    Yingbo,
    StoneAxe,
    CixiongDoubleSwords,
    QinglongCrescentBlade,
    IceSword,
    QilinBow,
    Shuangxiong,
    ZhuqueFan,
    Mengjin,
    QuhuPindian,
    QuhuDamageTarget,
    TianyiPindian,
    Jushou,
    Shensu,
    Yaowu,
    Tianxiang,
    Yinghun,
    Zaiqi,
    Lieren,
    Jujian,
    ProgramCardTrigger,
    ProgramJudgmentTrigger,
    ProgramJudgmentReplacement,
    ProgramJudgmentTarget,
    SelectFaction,
    Jiangchi,
    Qianxi,
    XianzhenPindian,
    Zishou,
    Zhenlie,
    Miji,
    Quanji,
    Zili,
    Qice,
    Zhiyu,
    Chunlao,
    Gongqi,
    Jiefan,
    Chengxiang,
    Renxin,
    Jingce,
    Junxing,
    Yuce,
    Longyin,
    SkillModule,
    ProgramTrigger
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

public enum GangliePunishmentKind
{
    DiscardTwo,
    LoseHp
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
    AwaitingHumanFactionSelection
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
    NationalFactionC
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
    UseSkill,
    RevealGeneral,
    Recast,
    BorrowedSword,
    UseEquipmentEffect,
    UseProgramSkill
}

public sealed record GameOptions
{
    public int Seed { get; init; } = 20260907;

    /// <summary>Supported identity-table sizes for the current rules adapter.</summary>
    public int PlayerCount { get; init; } = 8;

    /// <summary>-1 means that every seat is controlled by the built-in AI.</summary>
    public int HumanSeat { get; init; } = 0;

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

    /// <summary>
    /// Runs the deterministic seat/identity/general-selection/deal pipeline.
    /// False retains the original constructor-assigned demo as a compatibility
    /// adapter while hosts migrate to the prompt-based setup flow.
    /// </summary>
    public bool UseInteractiveSetup { get; init; }

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
    /// hosts opt new games into v3. Never change a policy already used by a save.
    /// </summary>
    public int AiPolicyVersion { get; init; } = 1;

    public int MaxTurns { get; init; } = 400;
}

public sealed record Card(int Id, CardKind Kind, Suit Suit, int Rank)
{
    public string DisplayName => CardCatalog.Get(Kind).DisplayName;

    public string RankText => Rank switch
    {
        1 => "A",
        11 => "J",
        12 => "Q",
        13 => "K",
        _ => Rank.ToString()
    };
}

public sealed partial record GeneralSkillDefinition(
    SkillKind Kind,
    string Name,
    string Description)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SkillTag Tags { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SkillExecutionForm ExecutionForms { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SkillActionForm ActionForms { get; init; }
}

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
    IReadOnlyList<TurnCardActionProhibition>? ActionProhibitions = null);

public sealed record ProgramBooleanStateSnapshot(
    string SkillInstanceId, string StateId, bool Value, string Text);

public sealed partial record GeneralDefinition(
    string Id,
    string Name,
    string PortraitKey,
    SkillKind Skill,
    string SkillName,
    string SkillDescription,
    string? FactionId = null,
    int BaseHp = 4,
    IReadOnlyList<GeneralSkillDefinition>? AdditionalSkills = null,
    GeneralGender Gender = GeneralGender.Male)
{
    public string? SkillContentId { get; init; }
    public SkillTag SkillTags { get; init; }
    public SkillExecutionForm SkillExecutionForms { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public SkillActionForm SkillActionForms { get; init; }

    public IReadOnlyList<GeneralSkillDefinition> Skills => AdditionalSkills is { Count: > 0 }
        ? new[]
            {
                new GeneralSkillDefinition(Skill, SkillName, SkillDescription)
                {
                    ContentId = SkillContentId,
                    Tags = SkillTags,
                    ExecutionForms = SkillExecutionForms,
                    ActionForms = SkillActionForms
                }
            }
            .Concat(AdditionalSkills)
            .ToArray()
        :
        [
            new GeneralSkillDefinition(Skill, SkillName, SkillDescription)
            {
                ContentId = SkillContentId,
                Tags = SkillTags,
                ExecutionForms = SkillExecutionForms,
                ActionForms = SkillActionForms
            }
        ];

    public IReadOnlyList<SkillKind> SkillKinds => Skills.Select(skill => skill.Kind).ToArray();

    public string SkillSummary => string.Join(" / ", Skills.Select(skill => skill.Name));

    public string SkillDescriptionSummary => string.Join(
        Environment.NewLine,
        Skills.Select(skill => $"{skill.Name}：{skill.Description}"));

    public bool HasSkill(SkillKind kind) => Skills.Any(skill => skill.Kind == kind);
}

public static class GeneralCatalog
{
    /// <summary>
    /// Ten skills are implemented in the minimum legacy demo. The remaining generals
    /// deliberately have no skill so an eight-seat table does not require ten
    /// different mechanics on every seat.
    /// PortraitKey is resolved by the WPF project; the rules project never loads images.
    /// </summary>
    public static IReadOnlyList<GeneralDefinition> DemoGenerals { get; } =
    [
        new("cao-cao", "曹操", "cao_cao", SkillKind.Jianxiong, "奸雄", "受到杀造成的伤害后，获得这张杀。"),
        new("zhang-fei", "张飞", "zhang_fei", SkillKind.Paoxiao, "咆哮", "出牌阶段使用杀没有次数限制。"),
        new("zhou-yu", "周瑜", "zhou_yu", SkillKind.Yingzi, "英姿", "摸牌阶段额外摸一张牌。"),
        new("zhuge-liang", "诸葛亮", "zhuge_liang", SkillKind.Kongcheng, "空城", "没有手牌时不能成为杀的目标。"),
        new("liu-bei", "刘备", "liu_bei", SkillKind.None, "无", "演示版暂未启用技能。"),
        new("guan-yu", "关羽", "guan_yu", SkillKind.Wusheng, "武圣", "红色牌可当作杀使用。"),
        new("zhao-yun", "赵云", "zhao_yun", SkillKind.Longdan, "龙胆", "杀可当闪，闪可当杀使用。"),
        new("sun-quan", "孙权", "sun_quan", SkillKind.None, "无", "演示版暂未启用技能。"),
        new("hua-tuo", "华佗", "hua_tuo", SkillKind.Feedback, "反馈", "受到伤害且伤害牌仍在处理区时，可选择发动并获得造成伤害的牌。"),
        new("xun-yu", "荀彧", "xun_yu", SkillKind.Jieming, "节命", "受到伤害后，可令一名手牌数少于体力上限的角色摸牌至上限。"),
        new("demo-yuanhu", "援护者", "supporter", SkillKind.Yuanhu, "援护", "其他角色受到伤害后，可弃置一张牌令其回复 1 点体力。"),
        new("demo-ganglie", "刚烈者", "ganglie", SkillKind.Ganglie, "刚烈", "受到伤害后可进行判定；若为红色，伤害来源选择弃置两张手牌或受到 1 点伤害。")
    ];
}

public sealed record CardSnapshot(
    int Id,
    CardKind Kind,
    Suit Suit,
    int Rank,
    string DisplayName,
    string RankText);

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

    /// <summary>Public elemental-link state. Hidden cards and identities remain filtered separately.</summary>
    public bool IsChained { get; init; }

    /// <summary>
    /// Delayed cards are public while they wait in a player's judgment zone.
    /// The property is outside the positional constructor for compatibility
    /// with older hosts that construct snapshots directly.
    /// </summary>
    public IReadOnlyList<CardSnapshot> Judgment { get; init; } = Array.Empty<CardSnapshot>();

    /// <summary>Public wound cards kept on Zhou Tai by classic Buqu.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardSnapshot>? BuquWounds { get; init; }

    /// <summary>Public "权" cards placed on classic Zhong Hui's general card.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardSnapshot>? AuthorityCards { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AuthorityCount { get; init; }

    /// <summary>Public "醇" cards placed on classic Cheng Pu's general card.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardSnapshot>? ChunlaoCards { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ChunlaoCount { get; init; }

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

    /// <summary>Second general data for the national-war lite dual-general slice.</summary>
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

    /// <summary>
    /// For DiscardCards, choose exactly this many distinct cards from ValidCardIds.
    /// Submit DiscardCardsCommand; Choices is empty to avoid enumerating every subset.
    /// </summary>
    public int RequiredCardCount { get; init; }

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

    /// <summary>
    /// Optional card/target selection contract for a PlayCard prompt whose
    /// active-skill choice is not a finite card/target combination list.
    /// These values are copied only into the requesting player's snapshot.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillKind? ActiveSkillKind { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? ActiveSkillValidCardIds { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? ActiveSkillValidTargetSeats { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ActiveSkillMinCardCount { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ActiveSkillMaxCardCount { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ActiveSkillMinTargetCount { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ActiveSkillMaxTargetCount { get; init; }
}

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
        SkillKind? Skill = null,
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
        this.Skill = Skill;
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

    /// <summary>Identifies the active skill for a cardless skill action.</summary>
    public SkillKind? Skill { get; init; }

    /// <summary>Identifies the equipment whose active conversion creates this action.</summary>
    public CardKind? EquipmentKind { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProgramSkillId { get; init; }

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

    /// <summary>
    /// Ordered public conversions applied after <see cref="ConversionSource"/>.
    /// A chained view-as keeps every source visible to replay and trigger facts.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardConversionSource>? AdditionalConversionSources { get; init; }

    /// <summary>
    /// Identifies a rule modifier that changes the effective card kind after an
    /// existing card identity or view-as conversion. This stays separate from
    /// <see cref="ConversionSource"/> so compound uses such as Wusheng followed
    /// by Lihuo retain both sources.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillKind? CardKindModifierSkill { get; init; }

    /// <summary>Identifies a skill that enlarges this use's exact target set.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SkillKind? TargetCountModifierSkill { get; init; }

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
    /// <summary>Public winning team id for team modes; null for identity results.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WinnerTeamId { get; init; }

    /// <summary>
    /// Cards intentionally revealed by a public effect, such as FiveGrains.
    /// This is separate from private hands and is empty outside that effect.
    /// </summary>
    public IReadOnlyList<CardSnapshot> PublicRevealedCards { get; init; } = Array.Empty<CardSnapshot>();

    /// <summary>Mode metadata used by view-scoped AI and presentation adapters.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ContentModeKind ModeKind { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WinnerFactionId { get; init; }
}

public sealed record EngineRunResult(
    EngineStatus Status,
    Winner Winner,
    GameSnapshot State,
    PendingDecision? PendingDecision,
    long Revision = 0)
{
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
