using CardGame.Core;

namespace CardGame.Wpf.Presentation;

public enum BattleCueKind { Card, ResponseWindow, Response, Judgment, Damage, Recovery, Turn, Dying, Death }

/// <summary>Presentation data containing only actions and values that are already public.</summary>
public sealed record BattleCue(long Sequence, BattleCueKind Kind, int SourceSeat,
    IReadOnlyList<int> TargetSeats, string Label, string ActorName, DamageNature Nature = DamageNature.Normal,
    string? Detail = null);

public static class BattleCueProjector
{
    public static IReadOnlyList<BattleCue> Project(IEnumerable<EventEnvelope> source, GameSnapshot playerView,
        int rulesVersion = GameCheckpoint.CurrentRulesVersion)
    {
        var events = source.ToArray();
        var targets = events.Select(item => item.Payload).OfType<TargetsConfirmedEvent>()
            .GroupBy(item => item.ResolutionId).ToDictionary(group => group.Key, group => group.Last().TargetSeats);
        var legacyJingceOwners = events.Select(item => item.Payload).OfType<JingceResolvedEvent>()
            .Where(item => item.Used).Select(item => item.OwnerSeat).ToHashSet();
        var cues = new List<BattleCue>();
        var responseCards = new HashSet<int>();
        string Name(int seat) => playerView.Players.FirstOrDefault(player => player.Seat == seat)?.GeneralName ?? "武将";
        string SkillName(SkillModuleResolvedEvent resolved)
        {
            var player = playerView.Players.FirstOrDefault(candidate => candidate.Seat == resolved.OwnerSeat);
            var skills = (player?.Skills ?? []).Concat(player?.SecondarySkills ?? []);
            return skills.FirstOrDefault(skill => skill.ContentId == resolved.SkillId)?.Name ?? resolved.SkillId;
        }
        IReadOnlyList<int> Seats(IEnumerable<int> seats) => Array.AsReadOnly(seats.Where(seat => seat >= 0 && seat < playerView.Players.Count).Distinct().ToArray());

        foreach (var envelope in events)
        {
            BattleCue? cue = envelope.Payload switch
            {
                CardRecastEvent recast => new(envelope.Sequence, BattleCueKind.Card, recast.ActorSeat, [],
                    $"重铸{CardCatalog.Get(recast.CardKind).DisplayName}", Name(recast.ActorSeat)),
                CardUseDeclaredEvent card => new(envelope.Sequence, BattleCueKind.Card, card.SourceSeat,
                    Seats(targets.GetValueOrDefault(card.ResolutionId) ?? []), CardCatalog.Get(card.CardKind).DisplayName, Name(card.SourceSeat)),
                ResponseRequestedEvent request => new(envelope.Sequence, BattleCueKind.ResponseWindow, request.TargetSeat,
                    Seats([request.SourceSeat]), $"等待{CardCatalog.Get(request.RequiredCardKind ?? CardKind.Dodge).DisplayName}响应",
                    Name(request.TargetSeat), Detail: $"响应【{CardCatalog.Get(request.IncomingCard).DisplayName}】"),
                RequiredResponseProgressEvent progress => new(envelope.Sequence, BattleCueKind.ResponseWindow, progress.ResponderSeat,
                    Seats([progress.SkillOwnerSeat]),
                    $"连续响应 {progress.ResponseCount} / {progress.RequiredResponseCount}", Name(progress.ResponderSeat),
                    Detail: $"已打出【{CardCatalog.Get(progress.RequiredCardKind).DisplayName}】"),
                NullificationRequestedEvent nullification => new(envelope.Sequence, BattleCueKind.ResponseWindow,
                    nullification.ResponderSeat, Seats([nullification.SourceSeat]),
                    $"无懈可击询问中 · 第 {nullification.ChainDepth + 1} 层", Name(nullification.ResponderSeat),
                    Detail: nullification.EffectCurrentlyNullified ? "当前锦囊已失效 · 可反制恢复" : "当前锦囊生效中 · 可令其失效"),
                NullificationRespondedEvent nullification => new(envelope.Sequence, BattleCueKind.Response,
                    nullification.ResponderSeat, [], $"打出无懈可击 · 第 {nullification.ChainDepth} 层",
                    Name(nullification.ResponderSeat), Detail: nullification.EffectNullified ? "锦囊暂时失效" : "锦囊恢复生效"),
                NullificationResolvedEvent nullification => new(envelope.Sequence, BattleCueKind.ResponseWindow, -1, [],
                    $"{CardCatalog.Get(nullification.EffectCardKind).DisplayName} · {(nullification.EffectNullified ? "已失效" : "继续生效")}",
                    "响应链结束", Detail: $"无懈链共 {nullification.ChainDepth} 次响应"),
                ArmorEffectAppliedEvent armor => new(envelope.Sequence, BattleCueKind.Response, armor.TargetSeat,
                    Seats([armor.SourceSeat]), $"{CardCatalog.Get(armor.ArmorCard).DisplayName} · {CardCatalog.Get(armor.IncomingCard).DisplayName}无效", Name(armor.TargetSeat)),
                YizhongNullifiedEvent yizhong => new(envelope.Sequence, BattleCueKind.Response, yizhong.TargetSeat,
                    Seats([yizhong.SourceSeat]), "毅重 · 黑色杀无效", Name(yizhong.TargetSeat)),
                WuyanDamagePreventedEvent wuyan => new(envelope.Sequence, BattleCueKind.Response, wuyan.SkillOwnerSeat,
                    Seats([wuyan.SourceSeat, wuyan.TargetSeat]), "无言 · 锦囊伤害已防止", Name(wuyan.SkillOwnerSeat)),
                JujianResolvedEvent { Used: true } jujian => new(envelope.Sequence,
                    jujian.Benefit == JujianBenefitKind.RecoverOne ? BattleCueKind.Recovery : BattleCueKind.Response,
                    jujian.OwnerSeat, Seats(jujian.TargetSeat is { } target ? [target] : []),
                    $"举荐 · {JujianBenefitLabel(jujian.Benefit)}", Name(jujian.OwnerSeat)),
                GudingBladeDamageIncreasedEvent guding => new(envelope.Sequence, BattleCueKind.Response, guding.SourceSeat,
                    Seats([guding.TargetSeat]), "古锭刀 · 伤害+1", Name(guding.SourceSeat)),
                TengjiaFireDamageIncreasedEvent tengjia => new(envelope.Sequence, BattleCueKind.Response, tengjia.TargetSeat,
                    Seats([tengjia.SourceSeat]), "藤甲 · 火焰伤害+1", Name(tengjia.TargetSeat), DamageNature.Fire),
                SilverLionDamageCappedEvent silverLion => new(envelope.Sequence, BattleCueKind.Response, silverLion.TargetSeat,
                    Seats([silverLion.SourceSeat]), "白银狮子 · 伤害改为1", Name(silverLion.TargetSeat)),
                SilverLionRemovedRecoveryEvent silverLion => new(envelope.Sequence, BattleCueKind.Recovery, silverLion.PlayerSeat,
                    Seats([silverLion.PlayerSeat]), "白银狮子 · +1", Name(silverLion.PlayerSeat)),
                QuanjiResolvedEvent { Used: true } quanji => new(envelope.Sequence, BattleCueKind.Response, quanji.OwnerSeat,
                    Seats([quanji.OwnerSeat]), $"权计 · 权 {quanji.AuthorityCount}", Name(quanji.OwnerSeat)),
                ZiliResolvedEvent zili => new(envelope.Sequence,
                    zili.Recovered ? BattleCueKind.Recovery : BattleCueKind.Response, zili.OwnerSeat,
                    Seats([zili.OwnerSeat]), "自立 · 觉醒", Name(zili.OwnerSeat),
                    Detail: zili.Recovered ? "体力上限−1 · 回复1 · 获得排异" : "体力上限−1 · 摸两张 · 获得排异"),
                PaiyiResolvedEvent paiyi => new(envelope.Sequence, BattleCueKind.Response, paiyi.SourceSeat,
                    Seats([paiyi.TargetSeat]), "排异 · 摸两张", Name(paiyi.SourceSeat),
                    Detail: paiyi.DamageTriggered ? "目标手牌较多 · 继续造成1点伤害" : "手牌比较后不造成伤害"),
                QiceConvertedEvent qice => new(envelope.Sequence, BattleCueKind.Response, qice.SourceSeat,
                    Seats(qice.TargetSeats), $"奇策 · {CardCatalog.Get(qice.EffectiveCardKind).DisplayName}", Name(qice.SourceSeat),
                    Detail: $"全部 {qice.PhysicalCardIds.Count} 张手牌转化"),
                ZhiyuResolvedEvent { Used: true } zhiyu => new(envelope.Sequence, BattleCueKind.Response, zhiyu.OwnerSeat,
                    Seats(zhiyu.DiscardedCardId is null ? [zhiyu.OwnerSeat] : [zhiyu.SourceSeat]),
                    $"智愚 · 展示{zhiyu.RevealedCards.Count}张", Name(zhiyu.OwnerSeat),
                    Detail: zhiyu.DiscardedCardId is null ? "手牌颜色不同或来源无手牌" : "手牌同色 · 来源弃置一张"),
                AnxuResolvedEvent anxu => new(envelope.Sequence, BattleCueKind.Response, anxu.OwnerSeat,
                    Seats([anxu.ReceiverSeat, anxu.DonorSeat]), "安恤 · 获得并展示一张", Name(anxu.OwnerSeat),
                    Detail: anxu.OwnerDrewCard ? "有效花色非黑桃 · 摸一张" : "有效花色为黑桃 · 不摸牌"),
                ZhuiyiResolvedEvent zhuiyi => new(envelope.Sequence,
                    zhuiyi.RecoveredHp > 0 ? BattleCueKind.Recovery : BattleCueKind.Response,
                    zhuiyi.OwnerSeat, Seats([zhuiyi.TargetSeat]), "追忆 · 摸三张", Name(zhuiyi.OwnerSeat),
                    Detail: zhuiyi.RecoveredHp > 0 ? "并回复1点体力" : "目标体力已满"),
                ChunlaoStoredEvent chunlao => new(envelope.Sequence, BattleCueKind.Response,
                    chunlao.OwnerSeat, Seats([chunlao.OwnerSeat]), $"醇醪 · 醇 {chunlao.CardIds.Count}",
                    Name(chunlao.OwnerSeat), Detail: "公开置于武将牌上"),
                ChunlaoRescueEvent chunlao => new(envelope.Sequence, BattleCueKind.Recovery,
                    chunlao.OwnerSeat, Seats([chunlao.VictimSeat]), "醇醪 · 酒救援",
                    Name(chunlao.OwnerSeat), Detail: $"回复{chunlao.RecoveredHp}点体力"),
                GongqiResolvedEvent gongqi => new(envelope.Sequence, BattleCueKind.Response,
                    gongqi.OwnerSeat, Seats(gongqi.TargetSeat is { } target ? [target] : [gongqi.OwnerSeat]),
                    "弓骑 · 攻击范围无限", Name(gongqi.OwnerSeat),
                    Detail: gongqi.DiscardedCardId is null ? "未弃置其他角色的牌" : "弃置其他角色一张牌"),
                JiefanChoiceResolvedEvent jiefan => new(envelope.Sequence,
                    jiefan.DiscardedWeaponCardId is null ? BattleCueKind.Recovery : BattleCueKind.Response,
                    jiefan.ResponderSeat, Seats([jiefan.TargetSeat]), "解烦 · 响应",
                    Name(jiefan.ResponderSeat),
                    Detail: jiefan.DiscardedWeaponCardId is null ? "令目标摸一张牌" : "弃置一张武器"),
                ChengxiangResolvedEvent { Used: true } chengxiang => new(envelope.Sequence,
                    BattleCueKind.Response, chengxiang.OwnerSeat, Seats([chengxiang.OwnerSeat]),
                    $"称象 · 获得{chengxiang.ObtainedCardIds.Count}张", Name(chengxiang.OwnerSeat),
                    Detail: $"点数和 {chengxiang.ObtainedRankSum} · 其余{chengxiang.DiscardedCardIds.Count}张弃置"),
                RenxinResolvedEvent { Used: true } renxin => new(envelope.Sequence,
                    BattleCueKind.Response, renxin.OwnerSeat, Seats([renxin.TargetSeat]),
                    $"仁心 · 防止{renxin.PreventedAmount}点伤害", Name(renxin.OwnerSeat),
                    Detail: renxin.IsFaceDown ? "弃置装备 · 翻至背面" : "弃置装备 · 翻至正面"),
                JingceResolvedEvent { Used: true } jingce => new(envelope.Sequence,
                    BattleCueKind.Response, jingce.OwnerSeat, Seats([jingce.OwnerSeat]),
                    $"精策 · 摸{jingce.DrawnCardIds.Count}张", Name(jingce.OwnerSeat),
                    Detail: $"本回合用牌 {jingce.UsedCardCount} · 当前体力 {jingce.CurrentHp}"),
                SkillModuleResolvedEvent { Used: true } skillModule
                    when skillModule.SkillId != "classic:jingce" || !legacyJingceOwners.Contains(skillModule.OwnerSeat) => new(envelope.Sequence,
                        BattleCueKind.Response, skillModule.OwnerSeat, Seats([skillModule.OwnerSeat]),
                        $"{SkillName(skillModule)} · 已发动", Name(skillModule.OwnerSeat)),
                JunxingResolvedEvent junxing => new(envelope.Sequence,
                    BattleCueKind.Response, junxing.OwnerSeat, Seats([junxing.TargetSeat]),
                    junxing.DiscardedCardId is not null ? "峻刑 · 弃置异类手牌" : $"峻刑 · 翻面摸{junxing.DrawnCardIds.Count}张",
                    Name(junxing.OwnerSeat),
                    Detail: $"弃置代价 {junxing.CostCardIds.Count} 张 · {string.Join("/", junxing.CostCategories)}"),
                YuceResolvedEvent { Used: true } yuce => new(envelope.Sequence,
                    BattleCueKind.Response, yuce.OwnerSeat, Seats([yuce.SourceSeat]),
                    yuce.DiscardedCardId is not null ? "御策 · 来源弃置异类手牌" : $"御策 · 回复{yuce.RecoveredHp}点",
                    Name(yuce.OwnerSeat),
                    Detail: yuce.RevealedCategory is null ? null : $"展示{yuce.RevealedCategory}"),
                LongyinResolvedEvent { Used: true } longyin => new(envelope.Sequence,
                    BattleCueKind.Response, longyin.OwnerSeat, Seats([longyin.SlashSourceSeat]),
                    "龙吟 · 杀不计次数", Name(longyin.OwnerSeat),
                    Detail: longyin.SlashWasRed ? "弃置一张牌 · 红色杀摸一张" : "弃置一张牌 · 黑色杀不摸牌"),
                DangxianExtraPlayPhaseEvent { Started: true } dangxian => new(envelope.Sequence,
                    BattleCueKind.Turn, dangxian.OwnerSeat, Seats([dangxian.OwnerSeat]),
                    "当先 · 额外出牌阶段", Name(dangxian.OwnerSeat), Detail: "正常准备与摸牌前"),
                FuliResolvedEvent fuli => new(envelope.Sequence, BattleCueKind.Recovery, fuli.OwnerSeat,
                    Seats([fuli.OwnerSeat]), $"伏枥 · 回复至{fuli.RemainingHp}", Name(fuli.OwnerSeat),
                    Detail: $"现存势力 {fuli.LivingFactionCount} · {(fuli.IsFaceDown ? "翻至背面" : "翻至正面")}"),
                FuhunConvertedEvent fuhun => new(envelope.Sequence, BattleCueKind.Response, fuhun.SourceSeat,
                    Seats([fuhun.ResponseTargetSeat]), "父魂 · 两牌化杀", Name(fuhun.SourceSeat),
                    Detail: fuhun.IsUse ? "作为【杀】使用" : "作为【杀】打出"),
                FuhunSkillsGrantedEvent fuhun => new(envelope.Sequence, BattleCueKind.Response, fuhun.OwnerSeat,
                    Seats([fuhun.OwnerSeat]), "父魂 · 获得武圣／咆哮", Name(fuhun.OwnerSeat),
                    Detail: "持续至本回合结束"),
                ZhuqueFanConvertedEvent zhuque => new(envelope.Sequence, BattleCueKind.Response, zhuque.SourceSeat,
                    Seats(zhuque.TargetSeats), "朱雀羽扇 · 火杀", Name(zhuque.SourceSeat)),
                JudgmentRequestedEvent judgment => new(envelope.Sequence, BattleCueKind.Judgment, judgment.TargetSeat,
                    Seats([judgment.TargetSeat]), $"{JudgmentName(judgment.Reason)} · 判定中", Name(judgment.TargetSeat)),
                JudgmentReplacementResolvedEvent { Used: true } replacement => new(envelope.Sequence, BattleCueKind.Judgment,
                    replacement.OwnerSeat, Seats([replacement.TargetSeat]),
                    $"鬼才改判 · {JudgmentCard(replacement.NewSuit, replacement.NewRank)}", Name(replacement.OwnerSeat),
                    Detail: "最终判定牌已替换"),
                JudgmentResolvedEvent judgment => new(envelope.Sequence, BattleCueKind.Judgment, judgment.TargetSeat,
                    Seats([judgment.TargetSeat]),
                    $"{JudgmentName(judgment.Reason)} · {JudgmentCard(judgment.Suit, judgment.Rank)}", Name(judgment.TargetSeat),
                    Detail: JudgmentOutcome(judgment, rulesVersion)),
                DamageAppliedEvent damage when damage.Amount > 0 => new(envelope.Sequence, BattleCueKind.Damage, damage.SourceSeat,
                    Seats([damage.TargetSeat]), $"−{damage.Amount}", Name(damage.TargetSeat), damage.Nature),
                RecoveryAppliedEvent recovery when recovery.Amount > 0 => new(envelope.Sequence, BattleCueKind.Recovery, recovery.SourceSeat,
                    Seats([recovery.TargetSeat]), $"+{recovery.Amount}", Name(recovery.TargetSeat)),
                TurnStartedEvent turn => new(envelope.Sequence, BattleCueKind.Turn, turn.ActorSeat, [],
                    turn.ActorSeat == playerView.HumanSeat ? "轮 到 你 了" : $"{Name(turn.ActorSeat)}的回合", $"回合 {turn.TurnNumber}"),
                PlayerDyingEvent dying => new(envelope.Sequence, BattleCueKind.Dying, dying.VictimSeat, Seats([dying.VictimSeat]), "濒死 · 等待救援", Name(dying.VictimSeat)),
                PlayerDiedEvent died => new(envelope.Sequence, BattleCueKind.Death, died.VictimSeat, Seats([died.VictimSeat]), "阵 亡", Name(died.VictimSeat)),
                _ => null
            };

            // Do not project hand movements, private skill candidates, draws, or setup choices.
            // A response card is included only after the corresponding public response was committed.
            (int? Card, int Seat, CardKind? Kind) response = envelope.Payload switch
            {
                CardRespondedEvent card => (card.CardId, card.ResponderSeat, card.EffectiveCardKind),
                DuelResponseEvent { UsedSlash: true } duel => (duel.SlashCardId, duel.ResponderSeat, duel.ResponseCardKind ?? CardKind.Slash),
                GroupResponseEvent { UsedResponse: true } group => (group.ResponseCardId, group.ResponderSeat, group.ResponseCardKind ?? group.RequiredCardKind),
                DyingResponseEvent { UsedPeach: true } dying =>
                    (dying.PeachCardId, dying.ResponderSeat, dying.UsedPeachPhysicalCardKind ?? CardKind.Peach),
                DyingResponseEvent { UsedAlcohol: true } dying => (dying.AlcoholCardId, dying.ResponderSeat, CardKind.Alcohol),
                _ => (null, -1, null)
            };
            if (response.Card is { } cardId && responseCards.Add(cardId))
                cue = new(envelope.Sequence, BattleCueKind.Response, response.Seat, [],
                    response.Kind is { } kind ? $"打出{CardCatalog.Get(kind).DisplayName}" : "打出响应牌", Name(response.Seat));
            if (cue is not null) cues.Add(cue);
        }

        // A bulk run can commit many turns. Show the latest useful actions, keeping playback bounded.
        return cues.TakeLast(12).ToArray();
    }

    private static string JujianBenefitLabel(JujianBenefitKind? benefit) => benefit switch
    {
        JujianBenefitKind.DrawTwo => "摸两张牌",
        JujianBenefitKind.RecoverOne => "回复1点体力",
        JujianBenefitKind.RestoreGeneral => "复原武将牌",
        _ => "未发动"
    };

    private static string JudgmentName(string reason) => reason switch
    {
        JudgmentReasons.BaguaDefense => "八卦阵",
        JudgmentReasons.Ganglie => "刚烈",
        JudgmentReasons.Leiji => "雷击",
        JudgmentReasons.Luoshen => "洛神",
        JudgmentReasons.Tieqi => "铁骑",
        JudgmentReasons.Indulgence => "乐不思蜀",
        JudgmentReasons.SupplyShortage => "兵粮寸断",
        JudgmentReasons.Lightning => "闪电",
        _ => "判定"
    };

    private static string JudgmentCard(Suit? suit, int? rank)
    {
        if (suit is null || rank is null) return "牌堆耗尽";
        var glyph = suit switch { Suit.Heart => "♥", Suit.Diamond => "♦", Suit.Club => "♣", Suit.Spade => "♠", _ => "?" };
        var rankText = rank switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", _ => rank.Value.ToString() };
        return $"{glyph}{rankText}";
    }

    private static string JudgmentOutcome(JudgmentResolvedEvent judgment, int rulesVersion)
    {
        if (judgment.CardId is null) return "没有可用的判定牌";
        return judgment.Reason switch
        {
            JudgmentReasons.BaguaDefense => judgment.Succeeded ? "红色 · 视为打出闪" : "黑色 · 未提供闪",
            JudgmentReasons.Indulgence when rulesVersion < 11 => judgment.Succeeded ? "红色 · 不跳过出牌阶段" : "黑色 · 跳过出牌阶段",
            JudgmentReasons.SupplyShortage when rulesVersion < 11 => judgment.Succeeded ? "红色 · 不跳过摸牌阶段" : "黑色 · 跳过摸牌阶段",
            JudgmentReasons.Indulgence => judgment.Succeeded ? "红桃 · 不跳过出牌阶段" : "非红桃 · 跳过出牌阶段",
            JudgmentReasons.SupplyShortage => judgment.Succeeded ? "梅花 · 不跳过摸牌阶段" : "非梅花 · 跳过摸牌阶段",
            JudgmentReasons.Lightning => judgment.Succeeded ? "黑桃 2–9 · 命中" : "未命中 · 移至下家",
            JudgmentReasons.Luoshen => judgment.Succeeded ? "黑色 · 继续判定" : "红色 · 结束",
            JudgmentReasons.Tieqi => judgment.Succeeded ? "红色 · 目标不能使用闪" : "黑色 · 可正常响应",
            JudgmentReasons.Leiji => judgment.Suit switch
            {
                Suit.Spade => "黑桃 · 受到2点雷电伤害",
                Suit.Club => "梅花 · 张角回复1点并造成1点雷电伤害",
                _ => "非黑桃/梅花 · 无事发生"
            },
            _ => judgment.Succeeded ? "判定成功" : "判定失败"
        };
    }
}
