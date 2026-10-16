using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ActivePlayerEffectExpiry
{
    CurrentTurnEnd, NextSourceTurnStart, PlayPhaseEnd, ConsumedOrTurnEnd,
    AffectedNextTurnEnd, AffectedNextTurn, RuleCleared
}

/// <summary>Public display facts about an issued effect, without frames, bindings or private card identities.</summary>
public sealed record ActivePlayerEffectSnapshot(string SkillId, string SkillName, string Description,
    int SourceSeat, ActivePlayerEffectExpiry Expiry, int? RemainingUses = null, int? SourceSeatDisplayNumber = null);

public sealed partial record PlayerSnapshot
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ActivePlayerEffectSnapshot>? ActiveEffects { get; init; }
}

public sealed partial class GameEngine
{
    // Read the live rule stores, never infer an outstanding effect from past activations.
    private IReadOnlyList<ActivePlayerEffectSnapshot>? ProjectActivePlayerEffects(CharacterState player,
        IReadOnlyList<GeneralSkillDefinition>? primarySkills, IReadOnlyList<GeneralSkillDefinition>? secondarySkills)
    {
        if (!player.IsAlive) return null;
        List<ActivePlayerEffectSnapshot>? effects = null;
        var lordSeat = _players.FirstOrDefault(item => item.Role == Role.Lord)?.Seat ?? 0;
        int SeatNumber(int seat) => IsTeamMode || IsNationalWarMode ? seat + 1
            : (seat - lordSeat + _players.Count) % _players.Count + 1;
        string SeatName(int seat) => $"第{SeatNumber(seat)}席";
        void Add(string skillId, int source, string description,
            ActivePlayerEffectExpiry expiry = ActivePlayerEffectExpiry.CurrentTurnEnd, int? uses = null)
        {
            if (_contentRegistry.Skills.TryGetValue(skillId, out var skill))
                (effects ??= []).Add(new(skillId, skill.Name, description, source, expiry, uses, SeatNumber(source)));
        }
        bool Current(int turn, int seat) => turn == _turnNumber && seat == _currentSeat;
        bool Lasting(int turn, int seat, SkillProgramDamageModifierExpiration expiry) =>
            expiry == SkillProgramDamageModifierExpiration.NextOwnerTurnStart || Current(turn, seat);
        ActivePlayerEffectExpiry Expiry(SkillProgramDamageModifierExpiration expiry) =>
            expiry == SkillProgramDamageModifierExpiration.NextOwnerTurnStart
                ? ActivePlayerEffectExpiry.NextSourceTurnStart : ActivePlayerEffectExpiry.CurrentTurnEnd;

        foreach (var item in _turnCardUseEffects.DamageModifiers)
            if (item.Source.OwnerSeat == player.Seat && Lasting(item.TurnNumber, item.TurnSeat, item.Expiration))
                Add(item.Source.SkillId, item.Source.OwnerSeat,
                    $"{(item.SourceScope == SkillProgramDamageModifierSourceScope.OwnerUsed ? "你使用的" : "你作为伤害来源的")}{EffectCardNames(item.CardKinds)}造成的伤害+{item.Amount}（不重复加成连环传导伤害）。", Expiry(item.Expiration));
        foreach (var item in _turnCardUseEffects.RuleModifiers)
            if ((item.AffectedSeat ?? item.Source.OwnerSeat) == player.Seat && Lasting(item.TurnNumber, item.TurnSeat, item.Expiration))
                Add(item.Source.SkillId, item.Source.OwnerSeat, item.Query == SkillRuleQuery.CardEffectImmunity
                    ? $"{EffectCardNames(item.CardKinds ?? [])}对你无效。"
                    : $"{(item.CardKinds is { Count: > 0 } ? EffectCardNames(item.CardKinds) + "的" : "")}{EffectRuleName(item.Query)}{(item.Operation == SkillRuleOperation.Unlimited ? "不受限制" : item.Operation == SkillRuleOperation.Set ? $"设为{item.Amount}" : $"{item.Amount:+0;-0;0}")}。",
                    Expiry(item.Expiration));
        foreach (var item in _turnCardUseEffects.TargetAdjustments)
            if (item.Source.OwnerSeat == player.Seat && Current(item.TurnNumber, item.TurnSeat))
                Add(item.Source.SkillId, item.Source.OwnerSeat,
                    $"下一张符合条件的{EffectCategoryNames(item.Categories)}可以{(item.AllowAdd ? "增加" : "")}{(item.AllowAdd && item.AllowRemove ? "或" : "")}{(item.AllowRemove ? "减少" : "")}一个目标。",
                    ActivePlayerEffectExpiry.ConsumedOrTurnEnd, 1);
        foreach (var item in _turnCardUseEffects.Prohibitions)
            if (item.Source.OwnerSeat == player.Seat && Current(item.TurnNumber, item.TurnSeat))
                Add(item.Source.SkillId, item.Source.OwnerSeat, $"不能使用{EffectCategoryNames(item.Categories)}。打出牌不受此项限制。");
        foreach (var item in _turnCardUseEffects.ActionProhibitions)
            if (item.Source.OwnerSeat == player.Seat && Current(item.TurnNumber, item.TurnSeat))
                Add(item.Source.SkillId, item.Source.OwnerSeat,
                    $"不能{string.Join("或", item.ActionTypes.Select(action => action == CardActionType.Use ? "使用" : "打出"))}{(item.Suits is { Count: > 0 } ? string.Join("、", item.Suits.Select(EffectSuitName)) : "")}{EffectCardNames(item.CardKinds)}。");
        foreach (var item in _turnCardUseEffects.HandColorRestrictions)
            if (item.AffectedSeat == player.Seat && Current(item.TurnNumber, item.TurnSeat))
                Add(item.Source.SkillId, item.Source.OwnerSeat, $"不能使用或打出{(item.IsRed ? "红色" : "黑色")}手牌。");
        foreach (var item in _turnCardUseEffects.TargetRestrictions)
            if ((item.Restriction == SkillProgramCardTargetRestriction.ArmorIneffectiveForTurn
                    ? item.TargetSeat : item.SubjectSeat ?? item.Source.OwnerSeat) == player.Seat && Current(item.TurnNumber, item.TurnSeat))
            {
                var target = item.TargetSeat is { } seat ? SeatName(seat) : "指定角色";
                var text = item.Restriction switch
                {
                    SkillProgramCardTargetRestriction.SelfOnly => "使用牌只能指定自己为目标。",
                    SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget => $"对{target}使用牌无距离限制。",
                    SkillProgramCardTargetRestriction.SlashCountUnlimitedAgainstTarget => $"对{target}使用【杀】无次数限制。",
                    SkillProgramCardTargetRestriction.IgnoreArmorAgainstTarget => $"对{target}使用牌时无视其防具。",
                    SkillProgramCardTargetRestriction.ArmorIneffectiveForTurn => "防具失效。",
                    _ => $"【杀】的指定目标限制：{target}。"
                };
                Add(item.Source.SkillId, item.Source.OwnerSeat, text);
            }
        foreach (var item in _turnCardUseEffects.Conversions)
            if (item.Source.OwnerSeat == player.Seat && Current(item.TurnNumber, item.TurnSeat))
                Add(item.Source.SkillId, item.Source.OwnerSeat,
                    $"{(item.BoundCardIsRed ? "黑色" : "红色")}牌可按当前转化规则当{EffectCardNames([item.OutputKind])}使用。");
        foreach (var item in _namedNextSlashReserves)
            if (item.OwnerSeat == player.Seat && Current(item.TurnNumber, item.TurnSeat))
                Add(item.SkillId, item.OwnerSeat, $"下一张【杀】的伤害+{item.Amount}。",
                    ActivePlayerEffectExpiry.ConsumedOrTurnEnd, 1);
        if (_nextCardTargetAdjustmentOwners.TryGetValue(player.Seat, out var adjustment) && HasLegacyNextCardTargetAdjustment(player))
            Add(adjustment.SkillId, adjustment.OwnerSeat, "下一张基本牌或普通锦囊牌可增加或减少一个目标。",
                ActivePlayerEffectExpiry.ConsumedOrTurnEnd, 1);
        foreach (var item in _directedTurnCardPolicies)
            if (Current(item.TurnNumber, item.TurnSeat) && (item.ActorSeat == player.Seat || item.TargetSeat == player.Seat) &&
                HasRuntimeSkillInstance(_players[item.Source.OwnerSeat], item.Source.SkillId, item.Source.SkillInstanceId))
            {
                var actor = item.ActorSeat == player.Seat ? "你" : SeatName(item.ActorSeat);
                var target = item.TargetSeat == player.Seat ? "你" : SeatName(item.TargetSeat);
                var details = new List<string>();
                if (item.Effects.HasFlag(DirectedTurnCardPolicyEffect.ForbidTarget)) details.Add("不能指定目标");
                if (item.Effects.HasFlag(DirectedTurnCardPolicyEffect.IgnoreDistance)) details.Add("无距离限制");
                if (item.Effects.HasFlag(DirectedTurnCardPolicyEffect.BypassSlashLimit)) details.Add("【杀】无次数限制");
                if (item.Effects.HasFlag(DirectedTurnCardPolicyEffect.IgnoreArmor)) details.Add("无视目标防具");
                Add(item.Source.SkillId, item.Source.OwnerSeat, $"{actor}对{target}的{EffectCardNames(item.CardKinds)}：{string.Join("、", details)}。");
            }
        foreach (var item in _currentTurnSkillSuppressions)
            if (item.TargetSeat == player.Seat && Current(item.TurnNumber, item.TurnOwnerSeat))
                Add(item.Source.SkillId, item.Source.OwnerSeat, "非锁定技失效。");
        foreach (var item in _currentTurnOwnSkillSuppressions)
            if (item.Source.OwnerSeat == player.Seat && Current(item.TurnNumber, item.TurnOwnerSeat))
                Add(item.Source.SkillId, item.Source.OwnerSeat, $"【{_contentRegistry.GetSkill(item.SuppressedSkillId).Name}】失效。");
        foreach (var item in _programSuppressions)
            if (item.TargetSeat == player.Seat)
                Add(item.SkillId, item.SourceSeat, "受到技能封锁；相关技能暂时失效。", ActivePlayerEffectExpiry.AffectedNextTurnEnd);
        foreach (var item in _programPhaseColorRestrictions)
            if (item.TargetSeat == player.Seat && item.TurnNumber == _turnNumber && _phase == TurnPhase.Play)
                Add(item.SkillId, item.OwnerSeat, $"不能使用{(item.IsRed ? "红色" : "黑色")}牌。", ActivePlayerEffectExpiry.PlayPhaseEnd);
        foreach (var item in _currentTurnHeartSlashBonuses)
            if (Current(item.TurnNumber, item.TurnOwnerSeat) && (item.Source.OwnerSeat == player.Seat || item.TargetSeat == player.Seat))
                Add(item.Source.SkillId, item.Source.OwnerSeat,
                    $"{SeatName(item.Source.OwnerSeat)}对{SeatName(item.TargetSeat)}的红桃【杀】伤害+1。");
        foreach (var item in _actorHandLimitPenalties)
            if (item.ActorSeat == player.Seat)
                Add(item.Source.SkillId, item.Source.OwnerSeat, $"手牌上限-{item.Amount}。", ActivePlayerEffectExpiry.AffectedNextTurnEnd);
        foreach (var item in _programNextTurnRuleModifiers)
            if (item.TargetSeat == player.Seat)
                Add(item.Source.SkillId, item.Source.OwnerSeat, $"下个回合：{EffectRuleName(item.Query)}{item.Amount:+0;-0;0}。", ActivePlayerEffectExpiry.AffectedNextTurn);
        foreach (var item in _originalTargetAdditionGrants)
            if (Current(item.TurnNumber, item.TurnSeat) && item.Source.OwnerSeat == player.Seat)
                Add(item.Source.SkillId, item.Source.OwnerSeat, $"满足牌的使用条件时，可额外指定{SeatName(item.TargetSeat)}为目标。");
        if (_redAdditionalTargetGrants.TryGetValue(player.Seat, out var redTargets))
            Add(redTargets.Source.SkillId, redTargets.Source.OwnerSeat, $"红色牌可额外指定至多{redTargets.Maximum}个目标。");
        foreach (var item in _turnCardUseEffects.RecoveryModifiers)
            if (item.Source.OwnerSeat == player.Seat && Lasting(item.TurnNumber, item.TurnSeat, item.Expiration))
                Add(item.Source.SkillId, item.Source.OwnerSeat, $"你使用的{EffectCardNames(item.CardKinds)}回复体力值+{item.Amount}。", Expiry(item.Expiration));
        foreach (var item in _turnCardUseEffects.RedSlashPolicies)
            if (item.Source.OwnerSeat == player.Seat && Current(item.TurnNumber, item.TurnSeat))
                Add(item.Source.SkillId, item.Source.OwnerSeat, "红色【杀】无距离限制，且目标不能使用【闪】响应。");
        foreach (var item in _turnCardUseEffects.SlashSuitAllowances)
            if (item.Source.OwnerSeat == player.Seat && Current(item.TurnNumber, item.TurnSeat))
                Add(item.Source.SkillId, item.Source.OwnerSeat, $"{EffectSuitName(item.Suit)}【杀】无距离和次数限制。");
        foreach (var item in _turnCardUseEffects.JudgedRankSlashPolicies)
            if (item.Source.OwnerSeat == player.Seat && Current(item.TurnNumber, item.TurnSeat))
                Add(item.Source.SkillId, item.Source.OwnerSeat, $"点数小于{item.Rank}的【杀】无距离限制，点数大于{item.Rank}的【杀】无次数限制。");
        foreach (var item in _turnCardUseEffects.TargetCardQuotaAllowances)
            if (item.TargetSeat == player.Seat && Current(item.TurnNumber, item.TurnSeat))
                Add(item.Source.SkillId, item.Source.OwnerSeat, "使用牌无次数限制；其他使用条件仍适用。");
        foreach (var item in GetTurnHandCategoryRestrictionSnapshot(player.Seat) ?? [])
            Add(item.Source.SkillId, item.Source.OwnerSeat,
                $"不能使用或打出{(item.Category == SkillProgramCardCategory.Basic ? "基本牌" : item.Category == SkillProgramCardCategory.Trick ? "锦囊牌" : "装备牌")}手牌。");
        foreach (var item in GetTurnHandLimitCardKindExemptionsSnapshot(player.Seat) ?? [])
            Add(item.Source.SkillId, item.Source.OwnerSeat, $"{EffectCardNames(item.CardKinds)}不计入手牌上限。", Expiry(item.Expiration));
        foreach (var item in GetFirstRoundGameUsageRefundsSnapshot(player.Seat) ?? [])
            Add(item.Source.SkillId, item.Source.OwnerSeat, "本回合结束时按实际存活状态返还本次限定技的使用次数。");
        if (HasNextUnlimitedCard(player))
            foreach (var policy in CardPolicies(player, SkillProgramCardPolicyKind.NextCardUnlimitedAfterNonLockedSkill))
                Add(policy.Source.SkillId, player.Seat, "下一张牌无距离和次数限制。", ActivePlayerEffectExpiry.ConsumedOrTurnEnd, 1);
        foreach (var instance in GetSkillBindingShard(player).ProgramInstances)
        {
            // Conversion polarity and permanent usage/awakening records already have a skill-rail surface.
            if (instance.Definition.Tags.HasFlag(SkillTag.Conversion) ||
                primarySkills?.Any(skill => skill.ContentId == instance.SkillId) != true &&
                secondarySkills?.Any(skill => skill.ContentId == instance.SkillId) != true) continue;
            foreach (var state in instance.Program.BooleanStates)
            {
                if (state.Visibility != SkillProgramStateVisibility.Public || state.Id is "used" or "woke" or "yin" or "yang" ||
                    !GetProgramBooleanState(player.Seat, instance.SkillId, instance.SkillInstanceId, state.Id) ||
                    instance.Definition.ProgramPresentation?.BooleanStates.GetValueOrDefault(state.Id) is not { } presentation ||
                    string.IsNullOrWhiteSpace(presentation.TrueText)) continue;
                Add(instance.SkillId, player.Seat, presentation.TrueText, state.ResetScope switch
                {
                    SkillProgramStateResetScope.Turn => ActivePlayerEffectExpiry.CurrentTurnEnd,
                    SkillProgramStateResetScope.PlayPhase => ActivePlayerEffectExpiry.PlayPhaseEnd,
                    _ => ActivePlayerEffectExpiry.RuleCleared
                });
            }
        }
        foreach (var debt in player.QingzhongDebts)
            if (debt.Phase.ActualTurnNumber == _turnNumber && debt.Phase.OwnerSeat == player.Seat)
                Add(debt.Source.SkillId, debt.Source.OwnerSeat, "本出牌阶段结束时，与手牌数最少的角色交换手牌。", ActivePlayerEffectExpiry.PlayPhaseEnd);
        foreach (var grant in player.XionghuoGrants.Where(LiveXionghuoSource))
            Add(grant.Source.SkillId, grant.Source.OwnerSeat,
                $"暴戾标记×{grant.Count}：{SeatName(grant.Source.OwnerSeat)}对你造成的伤害+1；你的出牌阶段开始时移除标记并结算随机效果。", ActivePlayerEffectExpiry.RuleCleared);
        foreach (var restriction in player.XuRongRestrictions)
            if (Current(restriction.ActualTurnNumber, restriction.ActualTurnOwner))
                Add(restriction.Source.SkillId, restriction.Source.OwnerSeat, restriction.Branch == 0
                    ? $"不能使用【杀】指定{SeatName(restriction.Source.OwnerSeat)}。" : "手牌上限-1。");
        if (_jiezhenConversions.TryGetValue(player.Seat, out var conversion))
            Add("ol:jiezhen", conversion.OwnerSeat, "可替换的技能被替换为【八阵】；发动【八卦阵】判定后提前恢复原技能。", ActivePlayerEffectExpiry.NextSourceTurnStart);
        if (HasMouWujingAllowance(player))
            Add("mou:wujing", player.Seat, "下一张使用的牌无次数限制。", ActivePlayerEffectExpiry.ConsumedOrTurnEnd, 1);
        if (HasFriendQihuiAllowance(player))
            Add("friend:qihui", player.Seat, "下一张使用的牌无次数限制。", ActivePlayerEffectExpiry.ConsumedOrTurnEnd, 1);
        foreach (var obligation in _giftHandRetentionObligations)
            if (obligation.Source.OwnerSeat == player.Seat)
                Add(obligation.Source.SkillId, obligation.Source.OwnerSeat,
                    $"本回合结束时，若交给{SeatName(obligation.RecipientSeat)}的牌仍满足保留条件，将手牌调整至{obligation.HandLimit}张。");
        foreach (var group in _turnCardUseEffects.HandLimitExemptCards.Where(item => item.BeneficiarySeat == player.Seat &&
                     Current(item.TurnNumber, item.TurnSeat) && _cardZones.GetLocation(item.CardId) == CardLocation.Hand(player.Seat))
                     .GroupBy(item => item.Source.SkillId))
            Add(group.Key, group.First().Source.OwnerSeat, $"手牌中{group.Select(item => item.CardId).Distinct().Count()}张以此法获得的牌不计入手牌上限。");
        if (_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.GrantFixedDistanceOneTurnPolicy))
            foreach (var grant in FixedDistanceOneGrants().Where(grant => grant.Source.OwnerSeat == player.Seat &&
                         Current(grant.ActualTurnNumber, grant.ActualTurnOwnerSeat) && !FixedDistanceDebtConsumed(grant) && ValidFixedDistanceOneGrant(grant)))
                Add(grant.Source.SkillId, grant.Source.OwnerSeat, $"你与{SeatName(grant.TargetSeat)}的距离视为1；结束阶段按是否造成过伤害结算后续效果。");
        if (player.Seat == _currentSeat && _programPlayDamageHandLimits.TryGetValue(player.Seat, out var handLimit) && handLimit.TurnNumber == _turnNumber)
            Add(handLimit.SkillId, player.Seat, $"本回合手牌上限按该次出牌阶段已造成的伤害点数计算：{handLimit.DamageCount}。");
        if (_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GiveHalfHandAndIssueTargetSupport))
            foreach (var issued in ProgramEventHistory<HalfHandTargetSupportIssuedEvent>().Where(item => item.OwnerSeat == player.Seat))
                if (CurrentHalfHandSupport(player.Seat, issued.Source.SkillId, issued.Source.SkillInstanceId, issued.GameplayHash, issued.StateId) == issued)
                    Add(issued.Source.SkillId, player.Seat, $"你成为【杀】或普通锦囊牌的目标后，{SeatName(issued.RecipientSeat)}可按技能规则交给你手牌。", ActivePlayerEffectExpiry.NextSourceTurnStart);
        if (_phase == TurnPhase.Play && player.Seat == _currentSeat && _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.SelectFrozenHandExchangeDebtPayment))
            foreach (var issued in ProgramEventHistory<PhaseHandExchangeDebtIssuedEvent>().Where(item => item.Source.OwnerSeat == player.Seat))
                if (CurrentPhaseHandDebt(player.Seat, issued.Source.SkillId, issued.Source.SkillInstanceId, issued.GameplayHash, issued.StateId) == issued)
                    Add(issued.Source.SkillId, player.Seat, $"出牌阶段结束时，须弃置已冻结差额对应的{issued.FrozenDifference}张牌。", ActivePlayerEffectExpiry.PlayPhaseEnd);
        if (_phase == TurnPhase.Play && player.Seat == _currentSeat && _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ReturnIssuedPhaseHandDebt))
            foreach (var issued in UnsettledPhaseHandSeizures().Where(item => item.Source.OwnerSeat == player.Seat))
                if (CurrentPhaseHandSeizureDebt(player.Seat, issued.Source.SkillId, issued.Source.SkillInstanceId, issued.GameplayHash, issued.ContinuationId) == issued)
                    Add(issued.Source.SkillId, player.Seat, $"出牌阶段结束时，须按{SeatName(issued.TargetSeat)}届时的体力值归还牌。", ActivePlayerEffectExpiry.PlayPhaseEnd);
        return effects?.AsReadOnly();
    }

    private static string EffectCardNames(IReadOnlyList<CardKind> kinds) => kinds.Count == 0 ? "牌" :
        kinds.Count == GrantTurnCardEffectImmunityProgramOperationDescriptor.CardEffectImmunityKinds.Count &&
        kinds.ToHashSet().SetEquals(GrantTurnCardEffectImmunityProgramOperationDescriptor.CardEffectImmunityKinds)
        ? "【杀】及普通锦囊牌" :
        string.Join("、", kinds.Select(kind => kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash
            ? "【杀】" : $"【{CardCatalog.Get(kind).DisplayName}】").Distinct());
    private static string EffectSuitName(Suit suit) => suit switch
    { Suit.Spade => "黑桃", Suit.Heart => "红桃", Suit.Club => "梅花", Suit.Diamond => "方块", _ => "无花色" };
    private static string EffectCategoryNames(CardUseCategories categories) => string.Join("、", new[]
    {
        categories.HasFlag(CardUseCategories.Basic) ? "基本牌" : null,
        categories.HasFlag(CardUseCategories.InstantTrick) ? "普通锦囊牌" : null,
        categories.HasFlag(CardUseCategories.DelayedTrick) ? "延时锦囊牌" : null,
        categories.HasFlag(CardUseCategories.Equipment) ? "装备牌" : null
    }.Where(name => name is not null));
    private static string EffectRuleName(SkillRuleQuery query) => query switch
    {
        SkillRuleQuery.DrawCount => "摸牌数", SkillRuleQuery.HandLimit => "手牌上限",
        SkillRuleQuery.SlashLimit => "【杀】使用次数", SkillRuleQuery.OutgoingDistance => "计算与其他角色的距离",
        SkillRuleQuery.IncomingDistance => "其他角色计算与你的距离", SkillRuleQuery.AttackRange => "攻击范围",
        SkillRuleQuery.CardTargetCount => "牌的目标数", SkillRuleQuery.SlashDistanceLimit => "【杀】距离",
        SkillRuleQuery.CardUseDistanceLimit => "使用牌的距离", _ => "规则数值"
    };
}
