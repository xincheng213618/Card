using CardGame.Core;

namespace CardGame.Wpf.Presentation;

public enum BattleCueKind { Card, ResponseWindow, Response, Judgment, Damage, Recovery, Turn, Dying, Death, Skill, ChainPropagation, Weapon, Rescued, Draw }
public enum JudgmentCueStage { Requested, Replaced, Resolved }

/// <summary>Presentation data containing only actions and values that are already public.</summary>
public sealed record BattleCue(long Sequence, BattleCueKind Kind, int SourceSeat,
    IReadOnlyList<int> TargetSeats, string Label, string ActorName, DamageNature Nature = DamageNature.Normal,
    string? Detail = null)
{
    // An inquiry announces a pending response; it is not itself a played card or result.
    public bool IsInquiry { get; init; }
    public int ResponseCount { get; init; }
    public int RequiredResponseCount { get; init; }
    public CardKind? RequiredResponseCardKind { get; init; }
    public long? ResponseResolutionId { get; init; }
    public bool HasResponseProgress => Kind == BattleCueKind.ResponseWindow && !IsInquiry &&
        RequiredResponseCount > 1 && ResponseCount > 0 && ResponseCount <= RequiredResponseCount &&
        RequiredResponseCardKind is not null;
    public int DrawCount { get; init; }
    public bool HasDrawCards => Kind == BattleCueKind.Draw && !IsInquiry && DrawCount > 0 && SourceSeat >= 0;
    public long Revision { get; init; }
    public CardKind? CardKind { get; init; }
    public int? PublicCardId { get; init; }
    public CardSnapshot? PublicCard { get; init; }
    public CardKind? WeaponKind { get; init; }
    public long? WeaponResolutionId { get; init; }
    public bool IsWeaponActivated { get; init; }
    public bool IsUseCompletion { get; init; }
    public string? SkillId { get; init; }
    public int HpDelta { get; init; }
    public int? RemainingHp { get; init; }
    public int? MaximumHp { get; init; }
    public int? OriginSeat { get; init; }
    public long? LifeResolutionId { get; init; }
    public JudgmentCueStage? JudgmentStage { get; init; }
    public long? JudgmentResolutionId { get; init; }
    public string? JudgmentTitle { get; init; }
    // Public judgment values include effective suit changes; this is not a
    // printed physical face and must stay separate from ordinary played cards.
    public CardSnapshot? JudgmentCard { get; init; }
    public bool HasJudgmentCard => Kind == BattleCueKind.Judgment && !IsInquiry && JudgmentStage == JudgmentCueStage.Resolved &&
        JudgmentCard is { Id: > 0, Rank: >= 1 and <= 13 } card && Enum.IsDefined(card.Kind) &&
        card.Suit is Suit.Spade or Suit.Heart or Suit.Club or Suit.Diamond &&
        !string.IsNullOrWhiteSpace(card.DisplayName) && !string.IsNullOrWhiteSpace(card.RankText);
    public string CardActionLabel => Kind == BattleCueKind.Card ? Detail ?? "使用" : "打出";
}

public static class BattleCueProjector
{
    private static readonly CardMoveReason ExtraDrawReason = new("program.extra-draw-phase.draw");

    public static IReadOnlyList<BattleCue> Project(IEnumerable<EventEnvelope> source, GameSnapshot playerView)
    {
        var events = source.ToArray();
        var targets = events.Select(item => item.Payload).OfType<TargetsConfirmedEvent>()
            .GroupBy(item => item.ResolutionId).ToDictionary(group => group.Key, group => group.Last().TargetSeats);
        var cues = new List<BattleCue>();
        var cardPlays = ProjectCardPlays(events, playerView).ToDictionary(cue => cue.Sequence);
        var shownSkills = new HashSet<(long Revision, int Seat, string Skill)>();
        string Name(int seat) => playerView.Players.FirstOrDefault(player => player.Seat == seat)?.GeneralName ?? "武将";
        string SkillName(int ownerSeat, string skillId)
        {
            var player = playerView.Players.FirstOrDefault(candidate => candidate.Seat == ownerSeat);
            var skills = (player?.Skills ?? []).Concat(player?.SecondarySkills ?? []);
            return skills.FirstOrDefault(skill => skill.ContentId == skillId)?.Name ?? skillId;
        }
        IReadOnlyList<int> Seats(IEnumerable<int> seats) => Array.AsReadOnly(seats.Where(seat => seat >= 0 && seat < playerView.Players.Count).Distinct().ToArray());

        // The host movement stream carries private faces. Strip it to public
        // quantities before grouping; never retain a card ID, kind or private zone.
        var drawn = new List<(long Sequence, long Revision, int OwnerSeat)>();
        foreach (var envelope in events)
        {
            if (envelope.Payload is CardMovedEvent moved && moved.From == CardLocation.DrawPile &&
                moved.To is { Zone: CardZoneKind.Hand, OwnerSeat: { } ownerSeat } && ownerSeat >= 0 &&
                playerView.Players.Any(player => player.Seat == ownerSeat) &&
                (moved.Reason == CardMoveReasons.Draw || moved.Reason == ExtraDrawReason))
                drawn.Add((envelope.Sequence, envelope.Revision, ownerSeat));
        }
        var draws = drawn.GroupBy(item => (item.Revision, item.OwnerSeat)).ToDictionary(
            group => group.Max(item => item.Sequence),
            group => new BattleCue(group.Max(item => item.Sequence), BattleCueKind.Draw, group.Key.OwnerSeat,
                Array.AsReadOnly(new[] { group.Key.OwnerSeat }), $"摸牌 +{group.Count()}", Name(group.Key.OwnerSeat))
                { DrawCount = group.Count() });

        foreach (var envelope in events)
        {
            BattleCue? cue = envelope.Payload switch
            {
                CardRecastEvent recast => new(envelope.Sequence, BattleCueKind.Card, recast.ActorSeat, [],
                    $"重铸{CardCatalog.Get(recast.CardKind).DisplayName}", Name(recast.ActorSeat), Detail: "重铸") { CardKind = recast.CardKind },
                MouSmTargetsDisclosedEvent revealed => new(envelope.Sequence, BattleCueKind.Card, revealed.Actor, Seats(revealed.Targets), CardCatalog.Get(revealed.Kind).DisplayName, Name(revealed.Actor), Detail: "指定目标") { CardKind = revealed.Kind },
                CardUseDeclaredEvent card => new(envelope.Sequence, BattleCueKind.Card, card.SourceSeat,
                    Seats(targets.GetValueOrDefault(card.ResolutionId) ?? []), CardCatalog.Get(card.CardKind).DisplayName, Name(card.SourceSeat)) { CardKind = card.CardKind },
                ResponseRequestedEvent request => new(envelope.Sequence, BattleCueKind.ResponseWindow, request.TargetSeat,
                    Seats([request.SourceSeat]), $"等待{CardCatalog.Get(request.RequiredCardKind ?? CardKind.Dodge).DisplayName}响应",
                    Name(request.TargetSeat), Detail: $"响应【{CardCatalog.Get(request.IncomingCard).DisplayName}】") { IsInquiry = true, CardKind = request.IncomingCard },
                RequiredResponseProgressEvent progress => new(envelope.Sequence, BattleCueKind.ResponseWindow, progress.ResponderSeat,
                    Seats([progress.SkillOwnerSeat]),
                    $"连续响应 {progress.ResponseCount} / {progress.RequiredResponseCount}", Name(progress.ResponderSeat),
                    Detail: ResponseProgressDetail(progress))
                    { ResponseCount = progress.ResponseCount, RequiredResponseCount = progress.RequiredResponseCount,
                        RequiredResponseCardKind = progress.RequiredCardKind, ResponseResolutionId = progress.ResolutionId },
                NullificationRequestedEvent nullification => new(envelope.Sequence, BattleCueKind.ResponseWindow,
                    nullification.ResponderSeat, Seats([nullification.SourceSeat]),
                    $"无懈可击询问中 · 第 {nullification.ChainDepth + 1} 层", Name(nullification.ResponderSeat),
                    Detail: nullification.EffectCurrentlyNullified ? "当前锦囊已失效 · 可反制恢复" : "当前锦囊生效中 · 可令其失效") { IsInquiry = true },
                NullificationRespondedEvent nullification => new(envelope.Sequence, BattleCueKind.Response,
                    nullification.ResponderSeat, [], $"打出无懈可击 · 第 {nullification.ChainDepth} 层",
                    Name(nullification.ResponderSeat), Detail: nullification.EffectNullified ? "锦囊暂时失效" : "锦囊恢复生效"),
                NullificationResolvedEvent nullification => new(envelope.Sequence, BattleCueKind.ResponseWindow, -1, [],
                    $"{CardCatalog.Get(nullification.EffectCardKind).DisplayName} · {(nullification.EffectNullified ? "已失效" : "继续生效")}",
                    "响应链结束", Detail: $"无懈链共 {nullification.ChainDepth} 次响应"),
                StoneAxeResolvedEvent axe => new(envelope.Sequence, BattleCueKind.Weapon, axe.SourceSeat,
                    Seats([axe.TargetSeat]), axe.Used ? "贯石斧 · 破闪" : "贯石斧 · 未发动", Name(axe.SourceSeat),
                    Detail: axe.Used ? $"弃置 {axe.DiscardedCardIds.Count} 张牌 · 此杀继续结算" : "此次未发动")
                    { WeaponKind = CardKind.StoneAxe, WeaponResolutionId = axe.ResolutionId, IsWeaponActivated = axe.Used },
                QinglongCrescentBladeResolvedEvent qinglong => new(envelope.Sequence, BattleCueKind.Weapon, qinglong.SourceSeat,
                    Seats([qinglong.TargetSeat]), qinglong.Used ? "青龙偃月刀 · 追击" : "青龙偃月刀 · 未发动", Name(qinglong.SourceSeat),
                    Detail: qinglong.Used ? $"已追加【{CardCatalog.Get(qinglong.EffectiveSlashKind ?? CardKind.Slash).DisplayName}】使用" : "此次放弃追击")
                    { WeaponKind = CardKind.QinglongCrescentBlade, WeaponResolutionId = qinglong.ResolutionId, IsWeaponActivated = qinglong.Used },
                ArmorEffectAppliedEvent armor => new(envelope.Sequence, BattleCueKind.Response, armor.TargetSeat,
                    Seats([armor.SourceSeat]), $"{CardCatalog.Get(armor.ArmorCard).DisplayName} · {CardCatalog.Get(armor.IncomingCard).DisplayName}无效", Name(armor.TargetSeat)),
                YizhongNullifiedEvent yizhong => new(envelope.Sequence, BattleCueKind.Response, yizhong.TargetSeat,
                    Seats([yizhong.SourceSeat]), "毅重 · 黑色杀无效", Name(yizhong.TargetSeat)),
                WuyanDamagePreventedEvent wuyan => new(envelope.Sequence, BattleCueKind.Response, wuyan.SkillOwnerSeat,
                    Seats([wuyan.SourceSeat, wuyan.TargetSeat]),
                    $"{SkillName(wuyan.SkillOwnerSeat, wuyan.SkillId)} · 锦囊伤害已防止", Name(wuyan.SkillOwnerSeat)),
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
                    Seats([judgment.TargetSeat]), $"{JudgmentName(judgment.Reason)} · 判定中", Name(judgment.TargetSeat))
                    { IsInquiry = true, JudgmentStage = JudgmentCueStage.Requested, JudgmentResolutionId = judgment.ResolutionId,
                        JudgmentTitle = JudgmentName(judgment.Reason) },
                JudgmentReplacementResolvedEvent { Used: true } replacement => new(envelope.Sequence, BattleCueKind.Judgment,
                    replacement.OwnerSeat, Seats([replacement.TargetSeat]),
                    $"鬼才改判 · {JudgmentCard(replacement.NewSuit, replacement.NewRank)}", Name(replacement.OwnerSeat),
                    Detail: "最终判定牌已替换")
                    { JudgmentStage = JudgmentCueStage.Replaced, JudgmentResolutionId = replacement.JudgmentResolutionId,
                        JudgmentTitle = JudgmentName(replacement.Reason) },
                JudgmentResolvedEvent judgment => new(envelope.Sequence, BattleCueKind.Judgment, judgment.TargetSeat,
                    Seats([judgment.TargetSeat]),
                    $"{JudgmentName(judgment.Reason)} · {JudgmentCard(judgment.Suit, judgment.Rank)}", Name(judgment.TargetSeat),
                    Detail: JudgmentOutcome(judgment))
                    { JudgmentStage = JudgmentCueStage.Resolved, JudgmentResolutionId = judgment.ResolutionId,
                        JudgmentTitle = JudgmentName(judgment.Reason), JudgmentCard = JudgmentResultCard(judgment) },
                ChainedDamagePropagatedEvent chain => new(envelope.Sequence, BattleCueKind.ChainPropagation, chain.SourceSeat,
                    Seats([chain.TargetSeat]), "连环传导", Name(chain.FromSeat), chain.Nature) { OriginSeat = chain.FromSeat },
                DamageAppliedEvent damage when damage.Amount > 0 => new(envelope.Sequence, BattleCueKind.Damage, damage.SourceLess ? -1 : damage.SourceSeat,
                    Seats([damage.TargetSeat]), $"−{damage.Amount}", Name(damage.TargetSeat), damage.Nature)
                    { HpDelta = -damage.Amount, RemainingHp = damage.RemainingHp,
                        MaximumHp = playerView.Players.FirstOrDefault(player => player.Seat == damage.TargetSeat)?.MaxHp },
                RecoveryAppliedEvent recovery when recovery.Amount > 0 => new(envelope.Sequence, BattleCueKind.Recovery, recovery.SourceSeat,
                    Seats([recovery.TargetSeat]), $"+{recovery.Amount}", Name(recovery.TargetSeat))
                    { HpDelta = recovery.Amount, RemainingHp = recovery.RemainingHp,
                        MaximumHp = playerView.Players.FirstOrDefault(player => player.Seat == recovery.TargetSeat)?.MaxHp },
                TurnStartedEvent turn => new(envelope.Sequence, BattleCueKind.Turn, turn.ActorSeat, [],
                    turn.ActorSeat == playerView.HumanSeat ? "轮 到 你 了" : $"{Name(turn.ActorSeat)}的回合", $"回合 {turn.TurnNumber}"),
                PlayerDyingEvent dying => new(envelope.Sequence, BattleCueKind.Dying, dying.VictimSeat, Seats([dying.VictimSeat]), "濒死 · 等待救援", Name(dying.VictimSeat))
                    { LifeResolutionId = dying.ResolutionId },
                DyingResolvedEvent { Survived: true } rescued => new(envelope.Sequence, BattleCueKind.Rescued, rescued.VictimSeat,
                    Seats([rescued.VictimSeat]), "脱离濒死", Name(rescued.VictimSeat))
                    { LifeResolutionId = rescued.ResolutionId },
                PlayerDiedEvent died => new(envelope.Sequence, BattleCueKind.Death, died.VictimSeat, Seats([died.VictimSeat]), "阵 亡", Name(died.VictimSeat)),
                _ => null
            };

            if (draws.TryGetValue(envelope.Sequence, out var draw)) cue = draw;

            if (cardPlays.TryGetValue(envelope.Sequence, out var cardPlay))
                cue = cardPlay with { TargetSeats = cue?.TargetSeats ?? cardPlay.TargetSeats };

            var (skillSeat, skillId) = envelope.Payload switch
            {
                ProgramSkillResolvedEvent { Completed: true } e => (e.OwnerSeat, e.SkillId),
                ProgramBindingResolvedEvent { Activated: true, Completed: true } e => (e.OwnerSeat, e.SkillId),
                ProgramJudgmentTriggerResolvedEvent { Activated: true } e => (e.OwnerSeat, e.SkillId),
                ProgramJudgmentReplacementResolvedEvent { Activated: true } e => (e.OwnerSeat, e.SkillId),
                ProgramCardPolicyResolvedEvent { Applied: true } e => (e.OwnerSeat, e.SkillId),
                ProgramCardTriggerResolvedEvent { Activated: true } e => (e.OwnerSeat, e.SkillId),
                ProgramRecoveryPolicyAppliedEvent e => (e.OwnerSeat, e.SkillId),
                ProgramViewAsConvertedEvent e => (e.OwnerSeat, e.SkillId),
                SkillUsageConsumedEvent { Count: > 0 } e => (e.SkillOwnerSeat, e.SkillId),
                SkillAwakenedEvent e => (e.PlayerSeat, e.SkillId),
                _ => (-1, (string?)null)
            };
            var owner = playerView.Players.FirstOrDefault(player => player.Seat == skillSeat);
            var publicSkill = skillId is null || owner is null ? null :
                (owner.IsGeneralPublic ? owner.Skills ?? [] : [])
                .Concat(owner.IsSecondaryGeneralPublic ? owner.SecondarySkills ?? [] : [])
                .FirstOrDefault(skill => skill.ContentId == skillId);
            if (publicSkill is not null)
            {
                // Usage, conversion and completion may describe the same committed activation.
                if (shownSkills.Add((envelope.Revision, skillSeat, skillId!)))
                    cue = new(envelope.Sequence, BattleCueKind.Skill, skillSeat, Seats([skillSeat]),
                        publicSkill.Name, Name(skillSeat), Detail: cue?.Label) { SkillId = skillId };
                else if (cue?.Kind == BattleCueKind.Response && cue.CardKind is null)
                    cue = null;
            }
            else if (skillId is not null)
                cue = null; // An unrevealed general's skill identity is not a presentation cue.
            if (cue is not null) cues.Add(cue with { Revision = envelope.Revision });
        }

        return BoundFeedback(events, cues);
    }

    private sealed record PublicDamageOccurrence(long RequestedSequence, DamageRequestedEvent Request,
        long? AppliedSequence = null, long? CompletedSequence = null);

    private static IReadOnlyList<BattleCue> BoundFeedback(EventEnvelope[] events, List<BattleCue> cues)
    {
        // A bulk operation can contain many resolutions under one revision. Keep
        // ordinary history short, while retaining the current eight-seat chain.
        var recent = cues.TakeLast(12).ToArray();
        var latestRelay = recent.LastOrDefault(cue => cue.Kind == BattleCueKind.ChainPropagation);
        if (latestRelay is null) return recent;
        var owner = (ChainedDamagePropagatedEvent)events.Single(envelope => envelope.Sequence == latestRelay.Sequence).Payload;
        var relays = events.Where(envelope => envelope.Payload is ChainedDamagePropagatedEvent chain &&
            chain.ResolutionId == owner.ResolutionId).ToArray();
        var protectedSequences = relays.Select(envelope => envelope.Sequence).ToHashSet();
        foreach (var declaration in events.Where(envelope => envelope.Payload is CardUseDeclaredEvent card &&
            card.ResolutionId == owner.ResolutionId)) protectedSequences.Add(declaration.Sequence);

        // Damage occurrence IDs belong to child frames, rather than the card/
        // program that owns the relay. Associate only published request/result
        // pairs, then close each occurrence at its typed public AfterDamage.
        var occurrences = new List<PublicDamageOccurrence>();
        var pending = new List<int>();
        var pendingById = new Dictionary<long, int>();
        foreach (var envelope in events)
        {
            switch (envelope.Payload)
            {
                case DamageRequestedEvent request:
                    pendingById[request.ResolutionId] = occurrences.Count;
                    pending.Add(occurrences.Count);
                    occurrences.Add(new(envelope.Sequence, request));
                    break;
                case DamageAppliedEvent result:
                    for (var index = pending.Count - 1; index >= 0; index--)
                    {
                        var occurrence = occurrences[pending[index]];
                        if (occurrence.AppliedSequence is not null || occurrence.Request.SourceSeat != result.SourceSeat ||
                            occurrence.Request.TargetSeat != result.TargetSeat || occurrence.Request.Nature != result.Nature ||
                            occurrence.Request.SourceLess != result.SourceLess) continue;
                        occurrences[pending[index]] = occurrence with { AppliedSequence = envelope.Sequence };
                        break;
                    }
                    break;
                case AfterDamageEvent completed when pendingById.Remove(completed.ResolutionId, out var completedIndex):
                    occurrences[completedIndex] = occurrences[completedIndex] with { CompletedSequence = envelope.Sequence };
                    pending.Remove(completedIndex);
                    break;
            }
        }
        static bool Matches(PublicDamageOccurrence occurrence, ChainedDamagePropagatedEvent chain, int target) =>
            occurrence.Request.SourceSeat == chain.SourceSeat && occurrence.Request.TargetSeat == target &&
            occurrence.Request.Nature == chain.Nature;
        var first = (ChainedDamagePropagatedEvent)relays[0].Payload;
        var origin = occurrences.Where(occurrence => occurrence.CompletedSequence is { } completed &&
            completed < relays[0].Sequence && Matches(occurrence, first, first.FromSeat)).MaxBy(occurrence => occurrence.CompletedSequence);
        if (origin?.AppliedSequence is { } originHit) protectedSequences.Add(originHit);
        for (var index = 0; index < relays.Length; index++)
        {
            var relay = relays[index]; var chain = (ChainedDamagePropagatedEvent)relay.Payload;
            var boundary = index + 1 < relays.Length ? relays[index + 1].Sequence : long.MaxValue;
            var completion = events.FirstOrDefault(envelope => envelope.Sequence > relay.Sequence && (envelope.Payload switch
            {
                CardUseFinishedEvent card => card.ResolutionId == owner.ResolutionId,
                ProgramSkillResolvedEvent program => program.FrameId == owner.ResolutionId && program.Completed,
                _ => false
            }));
            if (completion is not null) boundary = Math.Min(boundary, completion.Sequence);
            var recipient = occurrences.FirstOrDefault(occurrence => occurrence.RequestedSequence > relay.Sequence &&
                occurrence.RequestedSequence < boundary && Matches(occurrence, chain, chain.TargetSeat));
            if (recipient?.AppliedSequence is { } recipientHit) protectedSequences.Add(recipientHit);
        }

        // Structure comes first; auxiliary cues occupy the remaining slots.
        // The existing active layer stays capped at 24 and VM history at 32.
        var retained = cues.Where(cue => protectedSequences.Contains(cue.Sequence)).Take(24).Select(cue => cue.Sequence).ToHashSet();
        foreach (var cue in recent.Reverse().Where(cue => !retained.Contains(cue.Sequence)).Take(24 - retained.Count))
            retained.Add(cue.Sequence);
        return cues.Where(cue => retained.Contains(cue.Sequence)).ToArray();
    }

    /// <summary>Committed card faces only; never infer a face from a hidden hand or movement.</summary>
    public static IReadOnlyList<BattleCue> ProjectCardPlays(IEnumerable<EventEnvelope> source, GameSnapshot playerView)
    {
        var events = source.ToArray();
        var responses = events.Where(item => item.Payload is CardRespondedEvent)
            .Select(item => (item.Revision, Card: (CardRespondedEvent)item.Payload))
            .Select(item => (item.Revision, item.Card.ResponderSeat, item.Card.CardId)).ToHashSet();
        var uses = events.Where(item => item.Payload is CardUseDeclaredEvent)
            .Select(item => (item.Revision, Card: (CardUseDeclaredEvent)item.Payload))
            .Select(item => (item.Card.SourceSeat, item.Card.CardId)).ToHashSet();
        var result = new List<BattleCue>();
        foreach (var envelope in events)
        {
            (int? Id, int Seat, CardKind? Kind, bool Use, string? Detail) play = envelope.Payload switch
            {
                CardUseDeclaredEvent use => (use.CardId, use.SourceSeat, use.CardKind, true, null),
                CardRecastEvent recast => (recast.CardId, recast.ActorSeat, recast.CardKind, true, "重铸"),
                CardRespondedEvent response => (response.CardId, response.ResponderSeat, response.EffectiveCardKind, false, null),
                DuelResponseEvent { UsedSlash: true } duel => (duel.SlashCardId, duel.ResponderSeat, duel.ResponseCardKind ?? CardKind.Slash, false, null),
                GroupResponseEvent { UsedResponse: true } group => (group.ResponseCardId, group.ResponderSeat, group.ResponseCardKind ?? group.RequiredCardKind, false, null),
                NullificationRespondedEvent counterspell => (counterspell.NullificationCardId, counterspell.ResponderSeat, CardKind.Nullification, false,
                    counterspell.EffectNullified ? "锦囊暂时失效" : "锦囊恢复生效"),
                // Modern rescues declare an effective Peach/Alcohol use. Retain the fallback
                // for older public events, without showing a converted physical cost as Peach.
                DyingResponseEvent { UsedPeach: true } rescue => (rescue.PeachCardId, rescue.ResponderSeat, CardKind.Peach, true, null),
                DyingResponseEvent { UsedAlcohol: true } rescue => (rescue.AlcoholCardId, rescue.ResponderSeat, CardKind.Alcohol, true, null),
                _ => (null, -1, null, false, null)
            };
            if (play.Id is not { } cardId) continue;
            if (envelope.Payload is not CardRespondedEvent && !play.Use && responses.Contains((envelope.Revision, play.Seat, cardId))) continue;
            if (envelope.Payload is DyingResponseEvent && uses.Contains((play.Seat, cardId))) continue;
            var name = playerView.Players.FirstOrDefault(player => player.Seat == play.Seat)?.GeneralName ?? "武将";
            var publicCard = envelope.Payload switch
            {
                CardUseDeclaredEvent use => use.PublicCard,
                CardRespondedEvent response => response.PublicCard,
                CardRecastEvent recast => recast.PublicCard,
                _ => null
            };
            if (cardId <= 0 || publicCard?.Id != cardId) publicCard = null;
            var label = play.Kind is { } kind ? CardCatalog.Get(kind).DisplayName : "响应牌";
            if (!play.Use) label = "打出" + label;
            if (envelope.Payload is CardRecastEvent) label = "重铸" + label;
            if (envelope.Payload is NullificationRespondedEvent chainResponse) label += $" · 第 {chainResponse.ChainDepth} 层";
            result.Add(new(envelope.Sequence, play.Use ? BattleCueKind.Card : BattleCueKind.Response,
                play.Seat, [], label, name, Detail: play.Detail)
                { CardKind = play.Kind, PublicCardId = cardId, PublicCard = publicCard, Revision = envelope.Revision,
                    IsUseCompletion = envelope.Payload is DyingResponseEvent });
        }
        return result;
    }


    private static string ResponseProgressDetail(RequiredResponseProgressEvent progress)
    {
        var cardName = CardCatalog.Get(progress.RequiredCardKind).DisplayName;
        if (progress.ResponseCount <= 0 || progress.RequiredResponseCount <= 0 || progress.ResponseCount > progress.RequiredResponseCount)
            return $"已打出【{cardName}】";
        return progress.ResponseCount < progress.RequiredResponseCount
            ? $"仍需 {progress.RequiredResponseCount - progress.ResponseCount} 张【{cardName}】"
            : $"所需【{cardName}】已打齐";
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
        JudgmentReasons.Leiji or "skill.classic.leiji" or "skill.national.leiji" or "skill.boundary.leiji" => "雷击",
        JudgmentReasons.Luoshen => "洛神",
        JudgmentReasons.Tieqi or "skill.slash-response-judgment" => "铁骑",
        JudgmentReasons.Indulgence => "乐不思蜀",
        JudgmentReasons.SupplyShortage => "兵粮寸断",
        JudgmentReasons.Lightning => "闪电",
        _ => "判定"
    };

    private static CardSnapshot? JudgmentResultCard(JudgmentResolvedEvent judgment)
    {
        if (judgment.CardId is not > 0 || judgment.CardKind is not { } kind ||
            judgment.Suit is not { } suit || judgment.Rank is not { } rank) return null;
        // The event supplies this judgment's public suit, including effective
        // suit changes. Never recover a printed face from a later player view.
        var card = new Card(judgment.CardId.Value, kind, suit, rank);
        return new(card.Id, card.Kind, card.Suit, card.Rank, card.DisplayName, card.RankText);
    }

    private static string JudgmentCard(Suit? suit, int? rank)
    {
        if (suit is null || rank is null) return "牌堆耗尽";
        var glyph = suit switch { Suit.Heart => "♥", Suit.Diamond => "♦", Suit.Club => "♣", Suit.Spade => "♠", _ => "?" };
        var rankText = rank switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", _ => rank.Value.ToString() };
        return $"{glyph}{rankText}";
    }

    private static string JudgmentOutcome(JudgmentResolvedEvent judgment)
    {
        if (judgment.CardId is null) return "没有可用的判定牌";
        return judgment.Reason switch
        {
            JudgmentReasons.BaguaDefense => judgment.Succeeded ? "红色 · 视为打出闪" : "黑色 · 未提供闪",
            JudgmentReasons.Indulgence => judgment.Succeeded ? "红桃 · 不跳过出牌阶段" : "非红桃 · 跳过出牌阶段",
            JudgmentReasons.SupplyShortage => judgment.Succeeded ? "梅花 · 不跳过摸牌阶段" : "非梅花 · 跳过摸牌阶段",
            JudgmentReasons.Lightning => judgment.Succeeded ? "黑桃 2–9 · 命中" : "未命中 · 移至下家",
            JudgmentReasons.Luoshen => judgment.Suit switch
            {
                Suit.Spade or Suit.Club => "黑色 · 可继续判定",
                Suit.Heart or Suit.Diamond => "红色 · 结束",
                _ => "判定花色已确定"
            },
            // Standard and classic Ganglie share this reason but use different
            // suit conditions; the generic red-suit boolean is not their result.
            JudgmentReasons.Ganglie => "判定花色已确定",
            JudgmentReasons.Tieqi or "skill.slash-response-judgment" => judgment.Suit switch
            {
                Suit.Heart or Suit.Diamond => "红色 · 目标不能使用闪",
                Suit.Spade or Suit.Club => "黑色 · 可正常响应",
                _ => "判定花色已确定"
            },
            // These skills can still expose decisions or a separate damage
            // window after judging. Their HP results belong to later facts.
            JudgmentReasons.Leiji or "skill.classic.leiji" or "skill.national.leiji" or "skill.boundary.leiji" => "判定花色已确定",
            _ => "判定花色已确定"
        };
    }
}
