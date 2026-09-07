using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum Role
{
    Lord,
    Loyalist,
    Rebel,
    Renegade
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
    OffensiveHorse,
    DefensiveHorse,
    JadeSeal
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
    Yuanhu
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
    Yuanhu
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
    Completed
}

public enum Winner
{
    None,
    LordAndLoyalists,
    Rebels,
    Renegade,
    Draw
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
    Feedback,
    SkipFeedback,
    YijiGift,
    SkipYiji,
    Jieming,
    SkipJieming,
    Yuanhu,
    SkipYuanhu,
    Equip
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
    /// Runs the deterministic seat/identity/general-selection/deal pipeline.
    /// False retains the original constructor-assigned demo as a compatibility
    /// adapter while hosts migrate to the prompt-based setup flow.
    /// </summary>
    public bool UseInteractiveSetup { get; init; }

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

public sealed record GeneralDefinition(
    string Id,
    string Name,
    string PortraitKey,
    SkillKind Skill,
    string SkillName,
    string SkillDescription);

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
        new("demo-yuanhu", "援护者", "supporter", SkillKind.Yuanhu, "援护", "其他角色受到伤害后，可弃置一张牌令其回复 1 点体力。")
    ];
}

public sealed record CardSnapshot(
    int Id,
    CardKind Kind,
    Suit Suit,
    int Rank,
    string DisplayName,
    string RankText);

public sealed partial record PlayerSnapshot(
    int Seat,
    string Name,
    bool IsHuman,
    Role? Role,
    bool IsRoleRevealed,
    string GeneralId,
    string GeneralName,
    string PortraitKey,
    SkillKind Skill,
    string SkillName,
    string SkillDescription,
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
    /// Equipment is public state. It is kept outside the positional constructor
    /// so older hosts can continue constructing snapshots unchanged.
    /// </summary>
    public IReadOnlyList<CardSnapshot> Equipment { get; init; } = Array.Empty<CardSnapshot>();
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
    /// <summary>Stable id of this exact prompt; zero means legacy construction.</summary>
    public PromptId PromptId { get; init; }

    /// <summary>Engine revision at which this prompt was published.</summary>
    public long Revision { get; init; }

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

public sealed record LegalAction(
    LegalActionKind Kind,
    int? CardId,
    int? TargetSeat,
    string Description,
    CardKind? PlayedCardKind = null);

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
    /// <summary>
    /// Cards intentionally revealed by a public effect, such as FiveGrains.
    /// This is separate from private hands and is empty outside that effect.
    /// </summary>
    public IReadOnlyList<CardSnapshot> PublicRevealedCards { get; init; } = Array.Empty<CardSnapshot>();
}

public sealed record EngineRunResult(
    EngineStatus Status,
    Winner Winner,
    GameSnapshot State,
    PendingDecision? PendingDecision,
    long Revision = 0);

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
