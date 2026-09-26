namespace CardGame.Core;

public sealed record PlayerSkillContext(
    int Seat,
    int Hp,
    int MaxHp,
    int HandCount,
    TurnPhase Phase,
    IReadOnlySet<SkillKind>? UsedActiveSkillKinds = null,
    bool IsOwnTurn = false,
    bool IsFaceDown = false,
    bool IsChained = false);

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
    RevealGiftAndDamage,
    RequestSlash,
    PayHpOrDiscardWeaponAndDamage,
    DiscardAndStartDuel,
    DiscardAndRecoverSelfAndTarget,
    StartArrowBarrage,
    ChooseOrdinaryTrick,
    UseTwoHandCardsAsSlash,
    DiscardForUnlimitedRange,
    AidByAttackRange,
    DiscardHandForCategoryChoice
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
    Suit? SourceCardSuit = null,
    int? TargetSeat = null,
    int? TargetHp = null,
    int? TargetMaxHp = null,
    int SourceCardCount = 0,
    int? SourceToTargetDistance = null);

public sealed record JudgmentSkillContext(
    PlayerSkillContext Owner,
    int TargetSeat,
    string Reason,
    int JudgmentCardId,
    CardKind JudgmentCardKind,
    Suit JudgmentSuit,
    int JudgmentRank);

public sealed record ResponseCountSkillContext(
    PlayerSkillContext Owner,
    int SourceSeat,
    int ResponderSeat,
    CardKind IncomingCard,
    CardKind RequiredCardKind);

/// <summary>
/// Identity for the absence of a printed or granted skill.
/// </summary>
public sealed class NoSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.None;
    public string Name => "无";
}

/// <summary>
/// Historical package identity only. Current Wuhun execution is a schema-38
/// program and does not dispatch through this passive object.
/// </summary>
public sealed class WuhunSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Wuhun;
    public string Name => "武魂";
}

public sealed class ShensuSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Shensu;
    public string Name => "神速";
}

public sealed class YaowuSkill : ISkillRuleIdentity, IDamageSkillRule
{
    public SkillKind Kind => SkillKind.Yaowu;
    public string Name => "耀武";

    public bool CanTriggerAfterDamage(DamageSkillContext context) =>
        context.Amount > 0 && context.SourceSeat is not null &&
        context.SourceCard is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash &&
        context.SourceCardSuit is Suit.Heart or Suit.Diamond &&
        context.TargetSeat == context.Owner.Seat;

    public DamageSkillEffectKind GetDamageSkillEffect(DamageSkillContext context) =>
        CanTriggerAfterDamage(context) ? DamageSkillEffectKind.BenefitDamageSource : DamageSkillEffectKind.None;
}

public sealed class JianxiongSkill : ISkillRuleIdentity, IDamageSkillRule
{
    public SkillKind Kind => SkillKind.Jianxiong;
    public string Name => "奸雄";
}

public sealed class HujiaSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Hujia;
    public string Name => "护驾";
}

public sealed class JijiangSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Jijiang;
    public string Name => "激将";




}

public sealed class JiuyuanSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Jiuyuan;
    public string Name => "救援";
}

public sealed class QixiSkill : ISkillRuleIdentity, ICardConversionSkillRule
{
    public SkillKind Kind => SkillKind.Qixi;
    public string Name => "奇袭";

    public bool CanUseAsDismantlement(PlayerSkillContext owner, Card card) =>
        card.Kind != CardKind.Dismantlement &&
        card.Suit is Suit.Spade or Suit.Club;
}

public sealed class DuanliangSkill : ISkillRuleIdentity, INumericSkillRule, ICardConversionSkillRule
{
    public SkillKind Kind => SkillKind.Duanliang;
    public string Name => "断粮";

    public bool CanUseAsSupplyShortage(PlayerSkillContext owner, Card card) =>
        card.Kind != CardKind.SupplyShortage &&
        card.Suit is Suit.Spade or Suit.Club &&
        (CardCatalog.Get(card.Kind).CategoryName == "基本牌" ||
         EquipmentCatalog.IsEquipment(card.Kind));

    public int ModifySupplyShortageDistanceLimit(
        PlayerSkillContext owner,
        int currentLimit) => Math.Max(currentLimit, 2);
}

public sealed class LuoshenSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Luoshen;
    public string Name => "洛神";
}

public sealed class QingguoSkill : ISkillRuleIdentity, ICardConversionSkillRule
{
    public SkillKind Kind => SkillKind.Qingguo;
    public string Name => "倾国";

    public bool CanUseAsResponse(
        PlayerSkillContext owner,
        Card card,
        CardKind requiredCardKind) =>
        requiredCardKind == CardKind.Dodge &&
        card.Kind != CardKind.Dodge &&
        card.Suit is Suit.Spade or Suit.Club;
}

public sealed class JizhiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Jizhi;
    public string Name => "集智";
}

public sealed class TieqiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Tieqi;
    public string Name => "铁骑";
}

public sealed class LiegongSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Liegong;
    public string Name => "烈弓";
}

public sealed class KuangguSkill : ISkillRuleIdentity, IDamageSkillRule
{
    public SkillKind Kind => SkillKind.Kuanggu;
    public string Name => "狂骨";
    public DamageTriggerScope AfterDamageTriggerScope => DamageTriggerScope.DamageSource;
    public int DamageTriggerPriority => 100;

    public bool CanTriggerAfterDamage(DamageSkillContext context) =>
        context.Amount > 0 &&
        context.SourceSeat == context.Owner.Seat &&
        context.SourceToTargetDistance is <= 1 &&
        context.Owner.Hp < context.Owner.MaxHp;

    public DamageSkillEffectKind GetDamageSkillEffect(DamageSkillContext context) =>
        CanTriggerAfterDamage(context)
            ? DamageSkillEffectKind.RecoverDamageSource
            : DamageSkillEffectKind.None;
}

public sealed class WushuangSkill : ISkillRuleIdentity, ICardUseSkillRule
{
    public SkillKind Kind => SkillKind.Wushuang;
    public string Name => "无双";

    public int ModifyRequiredResponseCount(
        ResponseCountSkillContext context,
        int currentCount) =>
        context.Owner.Seat == context.SourceSeat &&
        ((context.IncomingCard is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) &&
         context.RequiredCardKind == CardKind.Dodge ||
         context.IncomingCard == CardKind.Duel &&
         context.RequiredCardKind == CardKind.Slash)
            ? Math.Max(currentCount, 2)
            : currentCount;
}

public sealed class KejiSkill : ISkillRuleIdentity, ICardUseSkillRule
{
    public SkillKind Kind => SkillKind.Keji;
    public string Name => "克己";

    public bool CanSkipDiscardPhase(
        PlayerSkillContext owner,
        bool usedOrPlayedSlashDuringPlayPhase) =>
        owner.IsOwnTurn &&
        owner.Phase == TurnPhase.Discard &&
        !usedOrPlayedSlashDuringPlayPhase;
}

public sealed class TuxiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Tuxi;
    public string Name => "突袭";
}

public sealed class LuoyiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Luoyi;
    public string Name => "裸衣";
}

public sealed class QiangxiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Qiangxi;
    public string Name => "强袭";




}

public sealed class FeedbackSkill : ISkillRuleIdentity, IDamageSkillRule
{
    public SkillKind Kind => SkillKind.Feedback;
    public string Name => "反馈";
}

public sealed class YijiSkill : ISkillRuleIdentity, IDamageSkillRule
{
    public SkillKind Kind => SkillKind.Yiji;
    public string Name => "遗计";
}

public sealed class JiemingSkill : ISkillRuleIdentity, IDamageSkillRule
{
    public SkillKind Kind => SkillKind.Jieming;
    public string Name => "节命";
}

public sealed class YuanhuSkill : ISkillRuleIdentity, IDamageSkillRule
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
        ((IDamageSkillRule)this).CanTriggerAfterDamage(context) &&
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

public sealed class GanglieSkill : ISkillRuleIdentity, IDamageSkillRule
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

public sealed class GuicaiSkill : ISkillRuleIdentity, IJudgmentSkillRule
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

public sealed class GuidaoSkill : ISkillRuleIdentity, IJudgmentSkillRule
{
    public SkillKind Kind => SkillKind.Guidao;
    public string Name => "鬼道";

    public bool CanTriggerBeforeJudgment(JudgmentSkillContext context) => true;

    public bool OffersJudgmentCardChoice(JudgmentSkillContext context) => true;
}

public sealed class LeijiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Leiji;
    public string Name => "雷击";
}

public sealed class HuangtianSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Huangtian;
    public string Name => "黄天";
}

public sealed class YinghunSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Yinghun;
    public string Name => "英魂";
}

public sealed class HuoshouSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Huoshou;
    public string Name => "祸首";
}

public sealed class ZaiqiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Zaiqi;
    public string Name => "再起";
}

public sealed class JuxiangSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Juxiang;
    public string Name => "巨象";
}

public sealed class LierenSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Lieren;
    public string Name => "烈刃";
}

public sealed class YizhongSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Yizhong;
    public string Name => "毅重";
}

public sealed class WuyanSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Wuyan;
    public string Name => "无言";
}

public sealed class JujianSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Jujian;
    public string Name => "举荐";
}

public sealed class TianduSkill : ISkillRuleIdentity, IJudgmentSkillRule
{
    public SkillKind Kind => SkillKind.Tiandu;
    public string Name => "天妒";

    public bool CanClaimResolvedJudgment(JudgmentSkillContext context) =>
        context.Owner.Seat == context.TargetSeat;
}

public sealed class WushengSkill : ISkillRuleIdentity, ICardConversionSkillRule
{
    public SkillKind Kind => SkillKind.Wusheng;
    public string Name => "武圣";

    public bool CanUseAsSlash(PlayerSkillContext owner, Card card) =>
        card.Kind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) &&
        card.Suit is Suit.Heart or Suit.Diamond;

    public bool CanUseAsResponse(PlayerSkillContext owner, Card card, CardKind requiredCardKind) =>
        requiredCardKind == CardKind.Slash && CanUseAsSlash(owner, card);
}

public sealed class LongdanSkill : ISkillRuleIdentity, ICardConversionSkillRule
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

public sealed class GuoseSkill : ISkillRuleIdentity, ICardConversionSkillRule
{
    public SkillKind Kind => SkillKind.Guose;

    public string Name => "国色";

    public bool CanUseAsIndulgence(PlayerSkillContext owner, Card card) =>
        owner.Phase == TurnPhase.Play && card.Suit == Suit.Diamond;
}

public sealed class ShuangxiongSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Shuangxiong;
    public string Name => "双雄";
}

public sealed class BazhenSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Bazhen;
    public string Name => "八阵";
}

public sealed class HuojiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Huoji;
    public string Name => "火计";
}

public sealed class KanpoSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Kanpo;
    public string Name => "看破";
}

public sealed class LianhuanSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Lianhuan;
    public string Name => "连环";
}

public sealed class NiepanSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Niepan;
    public string Name => "涅槃";
}

public sealed class LiuliSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Liuli;

    public string Name => "流离";
}

public sealed class LijianSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Lijian;
    public string Name => "离间";




}

public sealed class BiyueSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Biyue;
    public string Name => "闭月";
}

public sealed class JushouSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Jushou;
    public string Name => "据守";
}

public sealed class HongyanSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Hongyan;
    public string Name => "红颜";
}

public sealed class TianxiangSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Tianxiang;
    public string Name => "天香";
}

public sealed class BuquSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Buqu;
    public string Name => "不屈";
}

public sealed class LuanjiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Luanji;
    public string Name => "乱击";




}

public sealed class XueyiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Xueyi;
    public string Name => "血裔";
}

public sealed class JieyinSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Jieyin;
    public string Name => "结姻";




}

public sealed class XiaojiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Xiaoji;
    public string Name => "枭姬";
}

public sealed class PaoxiaoSkill : ISkillRuleIdentity, INumericSkillRule
{
    public SkillKind Kind => SkillKind.Paoxiao;
    public string Name => "咆哮";

    public int ModifySlashLimit(PlayerSkillContext owner, int currentLimit) => int.MaxValue;
}

public sealed class YingziSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Yingzi;
    public string Name => "英姿";
}

public sealed class MashuSkill : ISkillRuleIdentity, INumericSkillRule
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

public sealed class YicongSkill : ISkillRuleIdentity, INumericSkillRule
{
    public SkillKind Kind => SkillKind.Yicong;
    public string Name => "义从";

    public int ModifyOutgoingDistance(PlayerSkillContext owner, int currentDistance) =>
        owner.Hp > 2 ? currentDistance - 1 : currentDistance;

    public int ModifyIncomingDistance(PlayerSkillContext owner, int currentDistance) =>
        owner.Hp <= 2 ? currentDistance + 1 : currentDistance;
}

public sealed class QicaiSkill : ISkillRuleIdentity, ICardUseSkillRule
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

public sealed class JijiuSkill : ISkillRuleIdentity, ICardConversionSkillRule
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

public sealed class KongchengSkill : ISkillRuleIdentity, ICardUseSkillRule
{
    public SkillKind Kind => SkillKind.Kongcheng;
    public string Name => "空城";

    public bool ProhibitsSlashTarget(PlayerSkillContext owner) => owner.HandCount == 0;

    public bool ProhibitsCardTarget(PlayerSkillContext owner, CardKind cardKind) =>
        owner.HandCount == 0 &&
        cardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Duel;
}

public sealed class QianxunSkill : ISkillRuleIdentity, ICardUseSkillRule
{
    public SkillKind Kind => SkillKind.Qianxun;
    public string Name => "谦逊";

    public bool ProhibitsCardTarget(PlayerSkillContext owner, CardKind cardKind) =>
        cardKind is CardKind.Snatch or CardKind.Indulgence;
}

public sealed class LianyingSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Lianying;
    public string Name => "连营";
}

public sealed class MengjinSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Mengjin;
    public string Name => "猛进";
}

public sealed class QuhuSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Quhu;
    public string Name => "驱虎";




}

public sealed class TianyiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Tianyi;
    public string Name => "天义";




}

public sealed class ZishouSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Zishou;
    public string Name => "自守";
}

public sealed class ZongshiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Zongshi;
    public string Name => "宗室";
}

public sealed class ZhenlieSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Zhenlie;
    public string Name => "贞烈";
}

public sealed class MijiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Miji;
    public string Name => "秘计";
}

// Historical name/kind metadata only. Current behavior is defined by skill programs.
public sealed class QuanjiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Quanji;
    public string Name => "权计";
}

public sealed class ZiliSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Zili;
    public string Name => "自立";
}

public sealed class PaiyiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Paiyi;
    public string Name => "排异";
}

public sealed class QiceSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Qice;
    public string Name => "奇策";




}

public sealed class ZhiyuSkill : ISkillRuleIdentity, IDamageSkillRule
{
    public SkillKind Kind => SkillKind.Zhiyu;
    public string Name => "智愚";

    public bool CanTriggerAfterDamage(DamageSkillContext context) =>
        context.Amount > 0 && context.TargetSeat == context.Owner.Seat;

    public bool OffersDamageCardChoice(DamageSkillContext context) =>
        CanTriggerAfterDamage(context);

    public DamageSkillEffectKind GetDamageSkillEffect(DamageSkillContext context) =>
        CanTriggerAfterDamage(context)
            ? DamageSkillEffectKind.RevealHandAndPunishSource
            : DamageSkillEffectKind.None;
}

public sealed class RenxinSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Renxin;
    public string Name => "仁心";
}

public sealed class JingceSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Jingce;
    public string Name => "精策";
}

public sealed class JunxingSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Junxing;
    public string Name => "峻刑";
}

public sealed class YuceSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Yuce;
    public string Name => "御策";
}

public sealed class FuhunSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Fuhun;
    public string Name => "父魂";




}

/// <summary>Historical package identity; current Anxu is a composed program.</summary>
public sealed class AnxuSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Anxu;
    public string Name => "安恤";
}

public sealed class ZhuiyiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Zhuiyi;
    public string Name => "追忆";
}

public sealed class LihuoSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Lihuo;
    public string Name => "疠火";
}

public sealed class ChunlaoSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Chunlao;
    public string Name => "醇醪";
}

public sealed class GongqiSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Gongqi;
    public string Name => "弓骑";
}

public sealed class JiefanSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Jiefan;
    public string Name => "解烦";




}

public sealed class KujinSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Kujin;
    public string Name => "苦肉";

    /// <summary>
    /// The owner may pay the one-point cost even at one HP. In that case the
    /// engine pauses this active-skill frame in the shared dying window before
    /// completing the draw effect.
    /// </summary>



}

public sealed class ZhihengSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Zhiheng;
    public string Name => "制衡";

    /// <summary>
    /// The engine supplies equipment as additional selectable cards only for
    /// the versioned classic rules. Historical/demo contexts remain hand-only.
    /// The engine owns the two-step movement through Processing.
    /// </summary>



}

public sealed class RendeSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Rende;
    public string Name => "仁德";

    /// <summary>
    /// Gives one or more hand cards to one other living character. The engine
    /// supplies the versioned phase ledger and cumulative self-recovery rule;
    /// EnforceOncePerTurn retains the earlier showcase and replay behavior.
    /// </summary>



}

public sealed class FanjianSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Fanjian;
    public string Name => "反间";




}

public sealed class GuanxingSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Guanxing;
    public string Name => "观星";
}

public sealed class QingnangSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Qingnang;
    public string Name => "青囊";

    /// <summary>
    /// Qingnang is a once-per-turn discard-and-recover effect. Target legality
    /// is supplied by the engine from public living HP, while this rule object
    /// only exposes the phase, cost and effect contract.
    /// </summary>



}

public sealed class HuichunSkill : ISkillRuleIdentity
{
    public SkillKind Kind => SkillKind.Huichun;
    public string Name => "回春";

    /// <summary>
    /// The bounded multi-target slice discards exactly two private hand cards
    /// and recovers two or three living wounded characters once per turn. The
    /// engine supplies the public target candidates and owns each recovery
    /// frame, so the rule object remains a deterministic contract only.
    /// </summary>



}

public static class SkillRegistry
{
    private static readonly IReadOnlyDictionary<SkillKind, ISkillRuleIdentity> Skills =
        new Dictionary<SkillKind, ISkillRuleIdentity>
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
            [SkillKind.Guidao] = new GuidaoSkill(),
            [SkillKind.Leiji] = new LeijiSkill(),
            [SkillKind.Huangtian] = new HuangtianSkill(),
            [SkillKind.Yinghun] = new YinghunSkill(),
            [SkillKind.Huoshou] = new HuoshouSkill(),
            [SkillKind.Zaiqi] = new ZaiqiSkill(),
            [SkillKind.Juxiang] = new JuxiangSkill(),
            [SkillKind.Lieren] = new LierenSkill(),
            [SkillKind.Yizhong] = new YizhongSkill(),
            [SkillKind.Wuyan] = new WuyanSkill(),
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
            [SkillKind.Hujia] = new HujiaSkill(),
            [SkillKind.Jijiang] = new JijiangSkill(),
            [SkillKind.Jiuyuan] = new JiuyuanSkill(),
            [SkillKind.Qixi] = new QixiSkill(),
            [SkillKind.Keji] = new KejiSkill(),
            [SkillKind.Tuxi] = new TuxiSkill(),
            [SkillKind.Luoyi] = new LuoyiSkill(),
            [SkillKind.Qiangxi] = new QiangxiSkill(),
            [SkillKind.Duanliang] = new DuanliangSkill(),
            [SkillKind.Luoshen] = new LuoshenSkill(),
            [SkillKind.Qingguo] = new QingguoSkill(),
            [SkillKind.Guose] = new GuoseSkill(),
            [SkillKind.Liuli] = new LiuliSkill(),
            [SkillKind.Lijian] = new LijianSkill(),
            [SkillKind.Biyue] = new BiyueSkill(),
            [SkillKind.Jieyin] = new JieyinSkill(),
            [SkillKind.Xiaoji] = new XiaojiSkill(),
            [SkillKind.Qianxun] = new QianxunSkill(),
            [SkillKind.Lianying] = new LianyingSkill(),
            [SkillKind.Mengjin] = new MengjinSkill(),
            [SkillKind.Quhu] = new QuhuSkill(),
            [SkillKind.Tianyi] = new TianyiSkill(),
            [SkillKind.Zishou] = new ZishouSkill(),
            [SkillKind.Zongshi] = new ZongshiSkill(),
            [SkillKind.Zhenlie] = new ZhenlieSkill(),
            [SkillKind.Miji] = new MijiSkill(),
            [SkillKind.Quanji] = new QuanjiSkill(),
            [SkillKind.Zili] = new ZiliSkill(),
            [SkillKind.Paiyi] = new PaiyiSkill(),
            [SkillKind.Qice] = new QiceSkill(),
            [SkillKind.Zhiyu] = new ZhiyuSkill(),
            [SkillKind.Fuhun] = new FuhunSkill(),
            [SkillKind.Anxu] = new AnxuSkill(),
            [SkillKind.Zhuiyi] = new ZhuiyiSkill(),
            [SkillKind.Lihuo] = new LihuoSkill(),
            [SkillKind.Chunlao] = new ChunlaoSkill(),
            [SkillKind.Gongqi] = new GongqiSkill(),
            [SkillKind.Jiefan] = new JiefanSkill(),
            [SkillKind.Renxin] = new RenxinSkill(),
            [SkillKind.Jingce] = new JingceSkill(),
            [SkillKind.Junxing] = new JunxingSkill(),
            [SkillKind.Yuce] = new YuceSkill(),
            [SkillKind.Jushou] = new JushouSkill(),
            [SkillKind.Jujian] = new JujianSkill(),
            [SkillKind.Hongyan] = new HongyanSkill(),
            [SkillKind.Tianxiang] = new TianxiangSkill(),
            [SkillKind.Buqu] = new BuquSkill(),
            [SkillKind.Luanji] = new LuanjiSkill(),
            [SkillKind.Xueyi] = new XueyiSkill(),
            [SkillKind.Shensu] = new ShensuSkill(),
            [SkillKind.Yaowu] = new YaowuSkill(),
            [SkillKind.Yicong] = new YicongSkill(),
            [SkillKind.Shuangxiong] = new ShuangxiongSkill(),
            [SkillKind.Bazhen] = new BazhenSkill(),
            [SkillKind.Huoji] = new HuojiSkill(),
            [SkillKind.Kanpo] = new KanpoSkill(),
            [SkillKind.Lianhuan] = new LianhuanSkill(),
            [SkillKind.Niepan] = new NiepanSkill(),
            [SkillKind.Jizhi] = new JizhiSkill(),
            [SkillKind.Tieqi] = new TieqiSkill(),
            [SkillKind.Liegong] = new LiegongSkill(),
            [SkillKind.Kuanggu] = new KuangguSkill(),
            [SkillKind.Wushuang] = new WushuangSkill(),
            [SkillKind.Wuhun] = new WuhunSkill()
        };

    public static SkillRuleDefinition Get(SkillKind kind) => SkillRuleDefinition.From(Skills[kind]);
}

public static class GameRules
{
    /// <summary>
    /// Returns every living player tied at the greatest positive marker count.
    /// Zero-only tables deliberately produce no candidate.
    /// </summary>
    public static IReadOnlyList<int> GetMaximumMarkerCandidates(
        IEnumerable<PlayerMarkerCandidateState> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        var eligible = players
            .Where(player => player.IsAlive && player.Count > 0)
            .OrderBy(player => player.Seat)
            .ToArray();
        if (eligible.Length == 0)
        {
            return [];
        }

        var maximum = eligible.Max(player => player.Count);
        return Array.AsReadOnly(eligible
            .Where(player => player.Count == maximum)
            .Select(player => player.Seat)
            .ToArray());
    }

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
