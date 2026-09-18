namespace CardGame.Core;

public sealed record PlayerSkillContext(
    int Seat,
    int Hp,
    int MaxHp,
    int HandCount,
    TurnPhase Phase,
    IReadOnlySet<SkillKind>? UsedActiveSkillKinds = null,
    bool IsOwnTurn = false);

public sealed record ActiveSkillContext(
    PlayerSkillContext Owner,
    int SelectedCardCount = 0,
    int SelectedTargetCount = 0,
    int AdditionalSelectableCardCount = 0,
    bool EnforceOncePerTurn = false);

public enum ActiveSkillEffectKind
{
    None,
    LoseHpAndDraw,
    DiscardAndDraw,
    GiveCardsAndRecover,
    DiscardAndRecover,
    DiscardAndRecoverTargets,
    RevealGiftAndDamage
}

public sealed record ActiveSkillEffect(
    ActiveSkillEffectKind Kind,
    int HpCost = 0,
    int DrawCount = 0,
    int MinCardCount = 0,
    int MaxCardCount = 0,
    int MinTargetCount = 0,
    int MaxTargetCount = 0,
    int RecoveryAmount = 0);

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
    int? TargetMaxHp = null,
    int SourceCardCount = 0);

public sealed record JudgmentSkillContext(
    PlayerSkillContext Owner,
    int TargetSeat,
    string Reason,
    int JudgmentCardId,
    CardKind JudgmentCardKind,
    Suit JudgmentSuit,
    int JudgmentRank);

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

    int ModifyOutgoingDistance(PlayerSkillContext owner, int currentDistance) => currentDistance;

    bool IgnoresTrickDistance(PlayerSkillContext owner, CardKind trickKind) => false;

    bool ProhibitsSlashTarget(PlayerSkillContext owner) => false;

    /// <summary>
    /// Returns whether the owner cannot be selected by this effective card
    /// kind. The default projects the historical Slash-only hook so existing
    /// skills and old rules paths keep their original behavior.
    /// </summary>
    bool ProhibitsCardTarget(PlayerSkillContext owner, CardKind cardKind) =>
        (cardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) &&
        ProhibitsSlashTarget(owner);

    bool CanUseAsSlash(PlayerSkillContext owner, Card card) => false;

    bool CanUseAsResponse(
        PlayerSkillContext owner,
        Card card,
        CardKind requiredCardKind) => false;

    /// <summary>
    /// Returns whether this skill can treat a physical hand card as Peach in
    /// a dying response. The physical card remains unchanged in the movement
    /// ledger; the engine owns the effective Peach resolution.
    /// </summary>
    bool CanUseAsDyingRescue(PlayerSkillContext owner, Card card) => false;

    /// <summary>
    /// Declares which seat relationship can enter the after-damage window.
    /// The default keeps the historical rule that the skill belongs to the
    /// damaged character; cross-seat skills opt in with an explicit scope.
    /// </summary>
    DamageTriggerScope AfterDamageTriggerScope => DamageTriggerScope.DamagedPlayer;

    bool CanTriggerAfterDamage(DamageSkillContext context)
    {
        if (context.Amount <= 0)
        {
            return false;
        }

        if (context.TargetSeat is not { } targetSeat)
        {
            return AfterDamageTriggerScope == DamageTriggerScope.DamagedPlayer;
        }

        return AfterDamageTriggerScope switch
        {
            DamageTriggerScope.DamagedPlayer => targetSeat == context.Owner.Seat,
            DamageTriggerScope.OtherLivingPlayer => targetSeat != context.Owner.Seat,
            DamageTriggerScope.AnyLivingPlayer => true,
            _ => false
        };
    }

    int DamageTriggerPriority => 0;

    string DamageTriggerId => Kind.ToString();

    bool OffersDamageCardChoice(DamageSkillContext context) => false;

    bool ClaimsDamageCard(DamageSkillContext context) => false;

    DamageSkillEffectKind GetDamageSkillEffect(DamageSkillContext context) =>
        ClaimsDamageCard(context)
            ? DamageSkillEffectKind.ClaimDamageCard
            : DamageSkillEffectKind.None;

    bool CanTriggerBeforeJudgment(JudgmentSkillContext context) => false;

    bool OffersJudgmentCardChoice(JudgmentSkillContext context) =>
        CanTriggerBeforeJudgment(context);

    bool CanClaimResolvedJudgment(JudgmentSkillContext context) => false;

    int JudgmentTriggerPriority => 0;

    string JudgmentTriggerId => Kind.ToString();
}

/// <summary>
/// Optional active-skill surface. Selection and state mutation remain owned by
/// GameEngine; implementations only expose deterministic legality and effect
/// data for the current context.
/// </summary>
public interface IActiveSkill
{
    SkillKind Kind { get; }

    string Name { get; }

    bool CanUse(ActiveSkillContext context) => false;

    ActiveSkillEffect GetEffect(ActiveSkillContext context) =>
        new(ActiveSkillEffectKind.None);
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

public sealed class HujiaSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Hujia;
    public string Name => "护驾";
}

public sealed class FeedbackSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Feedback;
    public string Name => "反馈";

    // Rules v1-v9 and the legacy demo modes keep the original bounded rule
    // where Feedback claims the damage card from Processing. Classic identity
    // modes select the formal source-card effect in GameEngine instead.
    public bool ClaimsDamageCard(DamageSkillContext context) =>
        context.SourceCard is not null && context.SourceCardIsInProcessing;

    public bool OffersDamageCardChoice(DamageSkillContext context) =>
        context.Amount > 0 &&
        context.SourceSeat is not null &&
        context.SourceSeat != context.Owner.Seat &&
        (context.SourceCardIsInProcessing || context.SourceCardCount > 0);

    public DamageSkillEffectKind GetDamageSkillEffect(DamageSkillContext context) =>
        OffersDamageCardChoice(context)
            ? DamageSkillEffectKind.TakeSourceCard
            : DamageSkillEffectKind.None;
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

    public DamageTriggerScope AfterDamageTriggerScope => DamageTriggerScope.OtherLivingPlayer;

    /// <summary>
    /// Yuanhu is an explicit cross-seat trigger: a living owner may aid the
    /// character who just took damage. The shared scope query handles the seat
    /// relationship; this skill adds its discardable-card and missing-HP rules.
    /// </summary>
    public bool OffersDamageCardChoice(DamageSkillContext context) =>
        ((IPassiveSkill)this).CanTriggerAfterDamage(context) &&
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

public sealed class GanglieSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Ganglie;
    public string Name => "刚烈";

    /// <summary>
    /// The bounded slice keeps the real timing and privacy boundary: only the
    /// character who took positive damage may opt in. The public judgment and
    /// the source's private punishment choice are resolved by GameEngine.
    /// </summary>
    public bool OffersDamageCardChoice(DamageSkillContext context) =>
        context.Amount > 0 &&
        context.TargetSeat == context.Owner.Seat &&
        context.SourceSeat is not null;

    public DamageSkillEffectKind GetDamageSkillEffect(DamageSkillContext context) =>
        OffersDamageCardChoice(context)
            ? DamageSkillEffectKind.GanglieJudgment
            : DamageSkillEffectKind.None;
}

public sealed class GuicaiSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Guicai;
    public string Name => "鬼才";

    /// <summary>
    /// The bounded slice lets a living owner with a real hand card replace the
    /// current public judgment card. The engine owns the discard/movement and
    /// keeps the replacement prompt private to this owner.
    /// </summary>
    public bool CanTriggerBeforeJudgment(JudgmentSkillContext context) =>
        context.Owner.HandCount > 0;

    public bool OffersJudgmentCardChoice(JudgmentSkillContext context) =>
        CanTriggerBeforeJudgment(context);
}

public sealed class TianduSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Tiandu;
    public string Name => "天妒";

    public bool CanClaimResolvedJudgment(JudgmentSkillContext context) =>
        context.Owner.Seat == context.TargetSeat;
}

public sealed class WushengSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Wusheng;
    public string Name => "武圣";

    public bool CanUseAsSlash(PlayerSkillContext owner, Card card) =>
        card.Kind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) &&
        card.Suit is Suit.Heart or Suit.Diamond;

    public bool CanUseAsResponse(PlayerSkillContext owner, Card card, CardKind requiredCardKind) =>
        requiredCardKind == CardKind.Slash && CanUseAsSlash(owner, card);
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

public sealed class MashuSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Mashu;
    public string Name => "马术";

    /// <summary>
    /// Mashu is a public outgoing-distance modifier. The engine clamps the
    /// final combat distance to one, so a source can never reach zero-distance
    /// opponents through this passive hook.
    /// </summary>
    public int ModifyOutgoingDistance(PlayerSkillContext owner, int currentDistance) =>
        currentDistance - 1;
}

public sealed class QicaiSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Qicai;
    public string Name => "奇才";

    /// <summary>
    /// Qicai removes distance restrictions from trick cards. The engine still
    /// owns target filtering and only asks this small query for the current
    /// trick kind, so future distance-gated tricks can reuse the same hook.
    /// </summary>
    public bool IgnoresTrickDistance(PlayerSkillContext owner, CardKind trickKind) =>
        trickKind is
            CardKind.Duel or
            CardKind.DrawTwo or
            CardKind.BarbarianAssault or
            CardKind.ArrowBarrage or
            CardKind.PeachGarden or
            CardKind.FiveGrains or
            CardKind.Dismantlement or
            CardKind.Snatch or
            CardKind.FireAttack or
            CardKind.Nullification or
            CardKind.IronChain or
            CardKind.Indulgence or
            CardKind.SupplyShortage or
            CardKind.Lightning;
}

public sealed class JijiuSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Jijiu;
    public string Name => "急救";

    /// <summary>
    /// A red physical card may be used as Peach only while answering a dying
    /// window. Native Peach remains a normal card and is matched separately by
    /// the engine, so this hook never creates a duplicate candidate.
    /// </summary>
    public bool CanUseAsDyingRescue(PlayerSkillContext owner, Card card) =>
        !owner.IsOwnTurn &&
        card.Kind != CardKind.Peach &&
        card.Suit is Suit.Heart or Suit.Diamond;
}

public sealed class KongchengSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Kongcheng;
    public string Name => "空城";

    public bool ProhibitsSlashTarget(PlayerSkillContext owner) => owner.HandCount == 0;

    public bool ProhibitsCardTarget(PlayerSkillContext owner, CardKind cardKind) =>
        owner.HandCount == 0 &&
        cardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Duel;
}

public sealed class KujinSkill : IPassiveSkill, IActiveSkill
{
    public SkillKind Kind => SkillKind.Kujin;
    public string Name => "苦肉";

    /// <summary>
    /// The owner may pay the one-point cost even at one HP. In that case the
    /// engine pauses this active-skill frame in the shared dying window before
    /// completing the draw effect.
    /// </summary>
    public bool CanUse(ActiveSkillContext context) =>
        context.Owner.Phase == TurnPhase.Play && context.Owner.Hp > 0;

    public ActiveSkillEffect GetEffect(ActiveSkillContext context) =>
        new(ActiveSkillEffectKind.LoseHpAndDraw, HpCost: 1, DrawCount: 2);
}

public sealed class ZhihengSkill : IPassiveSkill, IActiveSkill
{
    public SkillKind Kind => SkillKind.Zhiheng;
    public string Name => "制衡";

    /// <summary>
    /// The engine supplies equipment as additional selectable cards only for
    /// the versioned classic rules. Historical/demo contexts remain hand-only.
    /// The engine owns the two-step movement through Processing.
    /// </summary>
    public bool CanUse(ActiveSkillContext context) =>
        context.Owner.Phase == TurnPhase.Play &&
        context.Owner.HandCount + context.AdditionalSelectableCardCount > 0 &&
        (!context.EnforceOncePerTurn ||
         context.Owner.UsedActiveSkillKinds?.Contains(Kind) != true);

    public ActiveSkillEffect GetEffect(ActiveSkillContext context) =>
        new(
            ActiveSkillEffectKind.DiscardAndDraw,
            MinCardCount: 1,
            MaxCardCount: Math.Max(
                0,
                context.Owner.HandCount + context.AdditionalSelectableCardCount));
}

public sealed class RendeSkill : IPassiveSkill, IActiveSkill
{
    public SkillKind Kind => SkillKind.Rende;
    public string Name => "仁德";

    /// <summary>
    /// The first target-selection slice gives one or more hand cards to one
    /// other living character. It can be used once per turn; giving at least
    /// two cards in that use also grants one point of recovery when needed.
    /// </summary>
    public bool CanUse(ActiveSkillContext context) =>
        context.Owner.Phase == TurnPhase.Play &&
        context.Owner.HandCount > 0 &&
        context.Owner.UsedActiveSkillKinds?.Contains(Kind) != true;

    public ActiveSkillEffect GetEffect(ActiveSkillContext context) =>
        new(
            ActiveSkillEffectKind.GiveCardsAndRecover,
            MinCardCount: 1,
            MaxCardCount: Math.Max(0, context.Owner.HandCount),
            MinTargetCount: 1,
            MaxTargetCount: 1,
            RecoveryAmount: context.SelectedCardCount >= 2 ? 1 : 0);
}

public sealed class FanjianSkill : IPassiveSkill, IActiveSkill
{
    public SkillKind Kind => SkillKind.Fanjian;
    public string Name => "反间";

    public bool CanUse(ActiveSkillContext context) =>
        context.Owner.Phase == TurnPhase.Play &&
        context.Owner.HandCount > 0 &&
        context.Owner.UsedActiveSkillKinds?.Contains(Kind) != true;

    public ActiveSkillEffect GetEffect(ActiveSkillContext context) =>
        new(
            ActiveSkillEffectKind.RevealGiftAndDamage,
            MinTargetCount: 1,
            MaxTargetCount: 1);
}

public sealed class GuanxingSkill : IPassiveSkill
{
    public SkillKind Kind => SkillKind.Guanxing;
    public string Name => "观星";
}

public sealed class QingnangSkill : IPassiveSkill, IActiveSkill
{
    public SkillKind Kind => SkillKind.Qingnang;
    public string Name => "青囊";

    /// <summary>
    /// Qingnang is a once-per-turn discard-and-recover effect. Target legality
    /// is supplied by the engine from public living HP, while this rule object
    /// only exposes the phase, cost and effect contract.
    /// </summary>
    public bool CanUse(ActiveSkillContext context) =>
        context.Owner.Phase == TurnPhase.Play &&
        context.Owner.HandCount > 0 &&
        context.Owner.UsedActiveSkillKinds?.Contains(Kind) != true;

    public ActiveSkillEffect GetEffect(ActiveSkillContext context) =>
        new(
            ActiveSkillEffectKind.DiscardAndRecover,
            MinCardCount: 1,
            MaxCardCount: 1,
            MinTargetCount: 1,
            MaxTargetCount: 1,
            RecoveryAmount: 1);
}

public sealed class HuichunSkill : IPassiveSkill, IActiveSkill
{
    public SkillKind Kind => SkillKind.Huichun;
    public string Name => "回春";

    /// <summary>
    /// The bounded multi-target slice discards exactly two private hand cards
    /// and recovers two or three living wounded characters once per turn. The
    /// engine supplies the public target candidates and owns each recovery
    /// frame, so the rule object remains a deterministic contract only.
    /// </summary>
    public bool CanUse(ActiveSkillContext context) =>
        context.Owner.Phase == TurnPhase.Play &&
        context.Owner.HandCount >= 2 &&
        context.Owner.UsedActiveSkillKinds?.Contains(Kind) != true;

    public ActiveSkillEffect GetEffect(ActiveSkillContext context) =>
        new(
            ActiveSkillEffectKind.DiscardAndRecoverTargets,
            MinCardCount: 2,
            MaxCardCount: 2,
            MinTargetCount: 2,
            MaxTargetCount: 3,
            RecoveryAmount: 1);
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
            [SkillKind.Yuanhu] = new YuanhuSkill(),
            [SkillKind.Ganglie] = new GanglieSkill(),
            [SkillKind.Guicai] = new GuicaiSkill(),
            [SkillKind.Tiandu] = new TianduSkill(),
            [SkillKind.Fanjian] = new FanjianSkill(),
            [SkillKind.Guanxing] = new GuanxingSkill(),
            [SkillKind.Kujin] = new KujinSkill(),
            [SkillKind.Zhiheng] = new ZhihengSkill(),
            [SkillKind.Rende] = new RendeSkill(),
            [SkillKind.Qingnang] = new QingnangSkill(),
            [SkillKind.Huichun] = new HuichunSkill(),
            [SkillKind.Mashu] = new MashuSkill(),
            [SkillKind.Qicai] = new QicaiSkill(),
            [SkillKind.Jijiu] = new JijiuSkill(),
            [SkillKind.Hujia] = new HujiaSkill()
        };

    public static IPassiveSkill Get(SkillKind kind) => Skills[kind];

    public static IActiveSkill? GetActive(SkillKind kind) =>
        Skills.TryGetValue(kind, out var skill) ? skill as IActiveSkill : null;
}

public static class GameRules
{
    /// <summary>
    /// Evaluates a public-team mode independently of identity roles. A team
    /// wins as soon as it is the only team with a living member; returning
    /// null keeps the in-progress and empty-table cases explicit.
    /// </summary>
    public static string? EvaluateWinningTeam(IEnumerable<TeamLifeState> players)
    {
        var survivingTeams = players
            .Where(player => player.IsAlive)
            .Select(player => player.TeamId)
            .Where(teamId => !string.IsNullOrWhiteSpace(teamId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return survivingTeams.Length == 1 ? survivingTeams[0] : null;
    }

    /// <summary>
    /// Evaluates a national-war mode independently of identity roles and
    /// public-team compatibility values.
    /// </summary>
    public static string? EvaluateWinningFaction(IEnumerable<FactionLifeState> players)
    {
        var survivingFactions = players
            .Where(player => player.IsAlive)
            .Select(player => player.FactionId)
            .Where(factionId => !string.IsNullOrWhiteSpace(factionId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return survivingFactions.Length == 1 ? survivingFactions[0] : null;
    }

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
