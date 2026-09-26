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
        var cues = new List<BattleCue>();
        var responseCards = new HashSet<int>();
        string Name(int seat) => playerView.Players.FirstOrDefault(player => player.Seat == seat)?.GeneralName ?? "武将";
        string SkillName(int ownerSeat, string skillId)
        {
            var player = playerView.Players.FirstOrDefault(candidate => candidate.Seat == ownerSeat);
            var skills = (player?.Skills ?? []).Concat(player?.SecondarySkills ?? []);
            return skills.FirstOrDefault(skill => skill.ContentId == skillId)?.Name ?? skillId;
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
                ProgramOptionChosenEvent option => new(envelope.Sequence, BattleCueKind.Response,
                    option.ChooserSeat, [],
                    $"{SkillName(option.OwnerSeat, option.SkillId)} · {option.OptionLabel}", Name(option.ChooserSeat)),
                ProgramCardsRevealedEvent revealed => new(envelope.Sequence, BattleCueKind.Response,
                    revealed.OwnerSeat, Seats([revealed.OwnerSeat]),
                    $"{SkillName(revealed.OwnerSeat, revealed.SkillId)} · 展示{revealed.Cards.Count}张",
                    Name(revealed.OwnerSeat)),
                GudingBladeDamageIncreasedEvent guding => new(envelope.Sequence, BattleCueKind.Response, guding.SourceSeat,
                    Seats([guding.TargetSeat]), "古锭刀 · 伤害+1", Name(guding.SourceSeat)),
                TengjiaFireDamageIncreasedEvent tengjia => new(envelope.Sequence, BattleCueKind.Response, tengjia.TargetSeat,
                    Seats([tengjia.SourceSeat]), "藤甲 · 火焰伤害+1", Name(tengjia.TargetSeat), DamageNature.Fire),
                SilverLionDamageCappedEvent silverLion => new(envelope.Sequence, BattleCueKind.Response, silverLion.TargetSeat,
                    Seats([silverLion.SourceSeat]), "白银狮子 · 伤害改为1", Name(silverLion.TargetSeat)),
                SilverLionRemovedRecoveryEvent silverLion => new(envelope.Sequence, BattleCueKind.Recovery, silverLion.PlayerSeat,
                    Seats([silverLion.PlayerSeat]), "白银狮子 · +1", Name(silverLion.PlayerSeat)),
                ChunlaoStoredEvent chunlao => new(envelope.Sequence, BattleCueKind.Response,
                    chunlao.OwnerSeat, Seats([chunlao.OwnerSeat]), $"醇醪 · 醇 {chunlao.CardIds.Count}",
                    Name(chunlao.OwnerSeat), Detail: "公开置于武将牌上"),
                ChunlaoRescueEvent chunlao => new(envelope.Sequence, BattleCueKind.Recovery,
                    chunlao.OwnerSeat, Seats([chunlao.VictimSeat]), "醇醪 · 酒救援",
                    Name(chunlao.OwnerSeat), Detail: $"回复{chunlao.RecoveredHp}点体力"),
                ProgramDyingRescueEvent rescue => new(envelope.Sequence, BattleCueKind.Recovery,
                    rescue.OwnerSeat, Seats([rescue.VictimSeat]),
                    $"{SkillName(rescue.OwnerSeat, rescue.SkillId)} · 酒救援",
                    Name(rescue.OwnerSeat), Detail: $"回复{rescue.RecoveredHp}点体力"),
                GongqiResolvedEvent gongqi => new(envelope.Sequence, BattleCueKind.Response,
                    gongqi.OwnerSeat, Seats(gongqi.TargetSeat is { } target ? [target] : [gongqi.OwnerSeat]),
                    "弓骑 · 攻击范围无限", Name(gongqi.OwnerSeat),
                    Detail: gongqi.DiscardedCardId is null ? "未弃置其他角色的牌" : "弃置其他角色一张牌"),
                ProgramAttackRangeAidChoiceResolvedEvent aid => new(envelope.Sequence,
                    aid.DiscardedWeaponCardId is null ? BattleCueKind.Recovery : BattleCueKind.Response,
                    aid.ResponderSeat, Seats([aid.TargetSeat]),
                    $"{SkillName(aid.OwnerSeat, aid.SkillId)} · 响应",
                    Name(aid.ResponderSeat),
                    Detail: aid.DiscardedWeaponCardId is null ? "令目标摸一张牌" : "弃置一张武器"),
                ProgramDamagePreventedEvent prevented => new(envelope.Sequence,
                    BattleCueKind.Response, prevented.OwnerSeat, Seats([prevented.TargetSeat]),
                    $"{SkillName(prevented.OwnerSeat, prevented.SkillId)} · 防止{prevented.Amount}点伤害",
                    Name(prevented.OwnerSeat)),
                SkillModuleResolvedEvent { Used: true } skillModule => new(envelope.Sequence,
                        BattleCueKind.Response, skillModule.OwnerSeat, Seats([skillModule.OwnerSeat]),
                        $"{SkillName(skillModule.OwnerSeat, skillModule.SkillId)} · 已发动", Name(skillModule.OwnerSeat)),
                ProgramPhaseScheduledEvent { Started: true } phase => new(envelope.Sequence,
                    BattleCueKind.Turn, phase.OwnerSeat, Seats([phase.OwnerSeat]),
                    $"{SkillName(phase.OwnerSeat, phase.SkillId)} · 额外{PhaseName(phase.Phase)}阶段",
                    Name(phase.OwnerSeat), Detail: "正常回合流程前"),
                ProgramBindingResolvedEvent { Activated: true, Completed: true } program => new(envelope.Sequence,
                    BattleCueKind.Response, program.OwnerSeat, Seats([program.OwnerSeat]),
                    $"{SkillName(program.OwnerSeat, program.SkillId)} · 已发动", Name(program.OwnerSeat)),
                ProgramSkillResolvedEvent { Completed: true } program => new(envelope.Sequence,
                    BattleCueKind.Response, program.OwnerSeat, Seats([program.OwnerSeat]),
                    $"{SkillName(program.OwnerSeat, program.SkillId)} · 已发动", Name(program.OwnerSeat)),
                ProgramViewAsConvertedEvent converted => new(envelope.Sequence, BattleCueKind.Response,
                    converted.OwnerSeat, Seats(converted.TargetSeats),
                    $"{SkillName(converted.OwnerSeat, converted.SkillId)} · {converted.PhysicalCardIds.Count}牌化" +
                    CardCatalog.Get(converted.OutputKind).DisplayName,
                    Name(converted.OwnerSeat),
                    Detail: converted.IsUse ? $"作为【{CardCatalog.Get(converted.OutputKind).DisplayName}】使用" :
                        $"作为【{CardCatalog.Get(converted.OutputKind).DisplayName}】打出"),
                ProgramTurnSkillsGrantedEvent granted => new(envelope.Sequence, BattleCueKind.Response,
                    granted.OwnerSeat, Seats([granted.OwnerSeat]),
                    $"{SkillName(granted.OwnerSeat, granted.SkillId)} · 获得" +
                    string.Join("／", granted.GrantedSkillIds.Select(skillId => SkillName(granted.OwnerSeat, skillId))),
                    Name(granted.OwnerSeat),
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


    private static string PhaseName(TurnPhase phase) => phase switch
    {
        TurnPhase.Draw => "摸牌",
        TurnPhase.Play => "出牌",
        TurnPhase.Discard => "弃牌",
        TurnPhase.Finished => "结束",
        _ => phase.ToString()
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
