namespace CardGame.Core;

public sealed record PlayerSkillContext(
    int Seat,
    int Hp,
    int MaxHp,
    int HandCount,
    TurnPhase Phase);

public sealed record DamageSkillContext(
    PlayerSkillContext Owner,
    int? SourceSeat,
    CardKind? SourceCard,
    bool SourceCardIsInProcessing,
    DamageNature Nature = DamageNature.Normal,
    int Amount = 1,
    int? SourceCardId = null,
    int? TargetSeat = null,
    int? TargetHp = null,
    int? TargetMaxHp = null);

/// <summary>
/// Skills answer small rule questions. State mutation remains in GameEngine, so
/// a skill cannot silently bypass card movement, logging, death, or victory checks.
/// </summary>
public interface IPassiveSkill
{
    SkillKind Kind { get; }

    string Name { get; }

    int ModifyDrawCount(PlayerSkillContext owner, int currentCount) => currentCount;

    int ModifySlashLimit(PlayerSkillContext owner, int currentLimit) => currentLimit;

    bool ProhibitsSlashTarget(PlayerSkillContext owner) => false;

    bool CanUseAsSlash(PlayerSkillContext owner, Card card) => false;

    bool CanUseAsResponse(
        PlayerSkillContext owner,
        Card card,
        CardKind requiredCardKind) => false;

    /// <summary>
    /// Declares whether this skill participates in the current after-damage
    /// window. The default keeps the historical rule that a skill belongs to
    /// the damaged character; cross-seat skills must opt in explicitly.
    /// </summary>
    bool CanTriggerAfterDamage(DamageSkillContext context) =>
        context.TargetSeat is null || context.TargetSeat == context.Owner.Seat;

    int DamageTriggerPriority => 0;

    string DamageTriggerId => Kind.ToString();

    bool OffersDamageCardChoice(DamageSkillContext context) => false;

    bool ClaimsDamageCard(DamageSkillContext context) => false;

    DamageSkillEffectKind GetDamageSkillEffect(DamageSkillContext context) =>
        ClaimsDamageCard(context)
            ? DamageSkillEffectKind.ClaimDamageCard
            : DamageSkillEffectKind.None;
}

public sealed class NoSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.None;
    public string Name => "无";
}

public sealed class JianxiongSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Jianxiong;
    public string Name => "奸雄";

    public bool ClaimsDamageCard(DamageSkillContext context) =>
        (context.SourceCard is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) &&
        context.SourceCardIsInProcessing;
}

public sealed class FeedbackSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Feedback;
    public string Name => "反馈";

    public bool OffersDamageCardChoice(DamageSkillContext context) =>
        context.SourceCard is not null && context.SourceCardIsInProcessing;

    public bool ClaimsDamageCard(DamageSkillContext context) =>
        context.SourceCard is not null && context.SourceCardIsInProcessing;
}

public sealed class YijiSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Yiji;
    public string Name => "遗计";

    /// <summary>
    /// The current bounded demo slice draws two cards and offers one private
    /// card-to-seat choice. The owner remains the damaged player, while the
    /// resulting card movement can cross seats.
    /// </summary>
    public bool OffersDamageCardChoice(DamageSkillContext context) => context.Amount > 0;

    public DamageSkillEffectKind GetDamageSkillEffect(DamageSkillContext context) =>
        context.Amount > 0
            ? DamageSkillEffectKind.GiftDrawnCard
            : DamageSkillEffectKind.None;
}

public sealed class JiemingSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Jieming;
    public string Name => "节命";

    /// <summary>
    /// The bounded slice keeps the real timing rule: the owner must be the
    /// character who just took positive damage. The effect target may be any
    /// living character whose hand is below max HP and is selected privately.
    /// </summary>
    public bool OffersDamageCardChoice(DamageSkillContext context) =>
        context.Amount > 0 && context.TargetSeat == context.Owner.Seat;

    public DamageSkillEffectKind GetDamageSkillEffect(DamageSkillContext context) =>
        OffersDamageCardChoice(context)
            ? DamageSkillEffectKind.DrawToMaxHand
            : DamageSkillEffectKind.None;
}

public sealed class YuanhuSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Yuanhu;
    public string Name => "援护";

    /// <summary>
    /// Unlike the historical damaged-owner-only skills, Yuanhu is an explicit
    /// cross-seat trigger: a living owner may aid the character who just took
    /// damage. The effect still requires a real discardable card and missing HP.
    /// </summary>
    public bool CanTriggerAfterDamage(DamageSkillContext context) =>
        context.Amount > 0 &&
        context.TargetSeat is { } targetSeat &&
        targetSeat != context.Owner.Seat;

    public bool OffersDamageCardChoice(DamageSkillContext context) =>
        CanTriggerAfterDamage(context) &&
        context.Owner.HandCount > 0 &&
        context.TargetHp is { } targetHp &&
        context.TargetMaxHp is { } targetMaxHp &&
        targetHp > 0 &&
        targetHp < targetMaxHp;

    public DamageSkillEffectKind GetDamageSkillEffect(DamageSkillContext context) =>
        OffersDamageCardChoice(context)
            ? DamageSkillEffectKind.RecoverDamageTarget
            : DamageSkillEffectKind.None;
}

public sealed class WushengSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Wusheng;
    public string Name => "武圣";

    public bool CanUseAsSlash(PlayerSkillContext owner, Card card) =>
        card.Kind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) &&
        card.Suit is Suit.Heart or Suit.Diamond;
}

public sealed class LongdanSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Longdan;
    public string Name => "龙胆";

    public bool CanUseAsSlash(PlayerSkillContext owner, Card card) => card.Kind == CardKind.Dodge;

    public bool CanUseAsResponse(
        PlayerSkillContext owner,
        Card card,
        CardKind requiredCardKind) => requiredCardKind switch
        {
            CardKind.Dodge => card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash,
            CardKind.Slash => card.Kind == CardKind.Dodge,
            _ => false
        };
}

public sealed class PaoxiaoSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Paoxiao;
    public string Name => "咆哮";

    public int ModifySlashLimit(PlayerSkillContext owner, int currentLimit) => int.MaxValue;
}

public sealed class YingziSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Yingzi;
    public string Name => "英姿";

    public int ModifyDrawCount(PlayerSkillContext owner, int currentCount) => currentCount + 1;
}

public sealed class KongchengSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Kongcheng;
    public string Name => "空城";

    public bool ProhibitsSlashTarget(PlayerSkillContext owner) => owner.HandCount == 0;
}

public static class SkillRegistry
{
    private static readonly IReadOnlyDictionary<SkillKind, IPassiveSkill> Skills =
        new Dictionary<SkillKind, IPassiveSkill>
        {
            [SkillKind.None] = new NoSkill(),
            [SkillKind.Jianxiong] = new JianxiongSkill(),
            [SkillKind.Paoxiao] = new PaoxiaoSkill(),
            [SkillKind.Yingzi] = new YingziSkill(),
            [SkillKind.Kongcheng] = new KongchengSkill(),
            [SkillKind.Feedback] = new FeedbackSkill(),
            [SkillKind.Wusheng] = new WushengSkill(),
            [SkillKind.Longdan] = new LongdanSkill(),
            [SkillKind.Yiji] = new YijiSkill(),
            [SkillKind.Jieming] = new JiemingSkill(),
            [SkillKind.Yuanhu] = new YuanhuSkill()
        };

    public static IPassiveSkill Get(SkillKind kind) => Skills[kind];
}

public static class GameRules
{
    public static Winner EvaluateWinner(IEnumerable<PlayerLifeState> players)
    {
        var list = players.ToArray();
        if (list.Length == 0)
        {
            return Winner.None;
        }

        var lord = list.Single(player => player.Role == Role.Lord);
        if (!lord.IsAlive)
        {
            var survivors = list.Where(player => player.IsAlive).ToArray();
            return survivors.Length == 1 && survivors[0].Role == Role.Renegade
                ? Winner.Renegade
                : Winner.Rebels;
        }

        var enemiesRemain = list.Any(player =>
            player.IsAlive && player.Role is Role.Rebel or Role.Renegade);

        return enemiesRemain ? Winner.None : Winner.LordAndLoyalists;
    }
}
