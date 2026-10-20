namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int GetLivingFactionCount() =>
        _players
            .Where(player => player.IsAlive)
            .Select(GetEffectiveFactionId)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Count();

    private int GetLivingPlayersMinHp() =>
        _players
            .Where(player => player.IsAlive && player.HasHpValue)
            .Select(player => player.Hp)
            .DefaultIfEmpty()
            .Min();

    private RuleQueryEvaluation CalculateDistance(CharacterState source, CharacterState target,
        int? excludedEquipmentId = null, EquipmentSlot? excludedEquipmentSlot = null)
    {
        var baseTerms = new List<RuleQueryBaseTerm>
        {
            new($"mode:{_modeDefinition.Id}:alive-seat-distance", GetAliveSeatDistance(source.Seat, target.Seat))
        };
        baseTerms.AddRange(GetEquipment(source).Where(card => card.Id != excludedEquipmentId && (excludedEquipmentSlot is null || EquipmentCatalog.Get(card.Kind).Slot != excludedEquipmentSlot)).Select(card => new RuleQueryBaseTerm(
            $"equipment:{card.Id}:outgoing-distance",
            EquipmentCatalog.Get(card.Kind).OutgoingDistanceModifier)));
        baseTerms.AddRange(GetEquipment(target).Where(card => card.Id != excludedEquipmentId).Select(card => new RuleQueryBaseTerm(
            $"equipment:{card.Id}:incoming-distance",
            EquipmentCatalog.Get(card.Kind).IncomingDistanceModifier)));

        if (_modeDefinition.TeamActivity is not null)
            baseTerms.Add(new("jiange:jingfan:allied-distance", JiangeDistanceModifier(source, target)));
        var shenwuDistance = ShenwuDustlessDistanceBonus(source, target);
        if (shenwuDistance != 0) baseTerms.Add(new("shenwu:dustless-allied-distance", shenwuDistance));
        var outgoing = CollectNumericRuleContributions(source, SkillRuleQuery.OutgoingDistance).ToList();
        outgoing.AddRange(_turnCardUseEffects.GetRuleModifiers(_turnNumber, _currentSeat, source.Seat, SkillRuleQuery.OutgoingDistance)
            .Select(item => new FiniteRuleQueryContribution(
                $"turn:{item.TurnNumber}:{item.Source.SkillId}:{item.Source.BindingId}:{item.GrantSequence}", SkillRuleOperation.Add, item.Amount)));
        var incoming = CollectNumericRuleContributions(target, SkillRuleQuery.IncomingDistance).ToList();
        if (source.Seat != target.Seat)
        {
            incoming.AddRange(IncomingDistanceCourtesyContributions(target.Seat));
            incoming.AddRange(YanPuIncomingDistanceContributions(target.Seat));
            incoming.AddRange(ShuangjiaIncomingDistanceContributions(target.Seat));
        }
        return ApplyPersistentOutgoingDistancePolicy(source, target, ApplyOtherPlayRestrictionDistance(source, target, ApplyFixedDistanceOne(source, target, ApplyInspectedHandDistance(source, target, ApplyHpOrderedDistance(source, target,
            ApplyProgramDirectedDistance(source, target, RuleQueryService.EvaluateDirectionalDistance(baseTerms, outgoing, incoming)))))));
    }

    private RuleQueryEvaluation CalculateAttackRange(CharacterState player, int? excludedEquipmentId = null, EquipmentSlot? excludedEquipmentSlot = null)
    {
        var baseTerms = new List<RuleQueryBaseTerm>();
        var minimumRange = 1;
        {
            var weapon = GetEquipment(player).Where(card => card.Id != excludedEquipmentId && (excludedEquipmentSlot is null || EquipmentCatalog.Get(card.Kind).Slot != excludedEquipmentSlot))
                .Where(card => EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon)
                .MaxBy(card => GetWeaponAttackRange(player, card));
            if (weapon is { Kind: CardKind.RedBloodBlade } && GetWeaponAttackRange(player, weapon) == 0)
                minimumRange = 0;
            baseTerms.Add(weapon is null
                ? new RuleQueryBaseTerm($"mode:{_modeDefinition.Id}:attack-range", 1)
                : new RuleQueryBaseTerm(
                    $"equipment:{weapon.Id}:printed-attack-range",
                    GetWeaponAttackRange(player, weapon)));
        }

        if (HasLiYiVirtualStoneAxe(player, excludedEquipmentId, excludedEquipmentSlot) && baseTerms[0].Value < 3)
            baseTerms[0] = new RuleQueryBaseTerm($"skill:{player.Seat}:zhanjin:attack-range", 3);

        if (GetCurrentTurnDefaultStat(player.Seat, SkillRuleQuery.AttackRange) is { } assignedRange)
            baseTerms.Add(new RuleQueryBaseTerm($"turn:{_turnNumber}:{player.Seat}:allocated-attack-range",
                Math.Max(0, assignedRange - baseTerms[0].Value)));

        var contributions = CollectNumericRuleContributions(player, SkillRuleQuery.AttackRange).ToList();
        AddFiniteContribution(contributions, $"issued:{player.Seat}:sequential-deck-shortfall:attack-range", SequentialDeckKindGrowthBonus(player.Seat));
        AddFiniteContribution(contributions, $"state:{player.Seat}:cao-xi:gangshu:attack-range", CaoXiGangshuBonus(player, SkillRuleQuery.AttackRange));
        AddFiniteContribution(contributions, $"state:{player.Seat}:original-hand:attack-range", GetOriginalHandAttackRangeBonus(player.Seat));
        AddFiniteContribution(contributions, $"state:{player.Seat}:hengye:growth", GetHengyeGrowth(player));
        contributions.AddRange(_turnCardUseEffects
            .GetRuleModifiers(_turnNumber, _currentSeat, player.Seat, SkillRuleQuery.AttackRange)
            .Where(item => item.Operation == SkillRuleOperation.Unlimited)
            .Select(item => new UnlimitedRuleQueryContribution(
                $"turn:{item.TurnNumber}:{item.Source.SkillId}:{item.Source.BindingId}:{item.GrantSequence}")));
        return RuleQueryService.Evaluate(
            SkillRuleQuery.AttackRange,
            new RuleQueryBounds(minimumRange, int.MaxValue),
            baseTerms,
            contributions);
    }

    private RuleQueryEvaluation EvaluateDrawCount(CharacterState player)
    {
        var baseTerms = new List<RuleQueryBaseTerm>
        {
            new($"mode:{_modeDefinition.Id}:draw-count",
                GetCurrentTurnDefaultStat(player.Seat, SkillRuleQuery.DrawCount) ?? _drawPerTurn)
        };
        baseTerms.AddRange(GetEquipment(player)
            .Where(card => EquipmentCatalog.Get(card.Kind).DrawCountBonus != 0)
            .Select(card => new RuleQueryBaseTerm(
                $"equipment:{card.Id}:draw-count",
                EquipmentCatalog.Get(card.Kind).DrawCountBonus)));
        var contributions = CollectNumericRuleContributions(player, SkillRuleQuery.DrawCount).ToList();
        AddFiniteContribution(contributions, $"state:{player.Seat}:cao-xi:gangshu:draw", CaoXiGangshuBonus(player, SkillRuleQuery.DrawCount));
        AddFiniteContribution(contributions, $"issued:{player.Seat}:shunshi:draw", DuFuRenPhaseBonus(player.Seat, SkillRuleQuery.DrawCount));
        AddFiniteContribution(contributions, $"issued:{player.Seat}:kuangxiang:draw", -ChengGongYingDrawPenalty(player.Seat));
        AddFiniteContribution(contributions, $"state:{player.Seat}:hengye:growth", GetHengyeGrowth(player));
        AddFiniteContribution(contributions, $"turn:{player.Seat}:draw-count", GetAdditiveTurnRuleModifier(player.Seat, SkillRuleQuery.DrawCount));
        AddFiniteContribution(contributions, $"issued:{player.Seat}:juguan:draw", JuguanActualDrawAdjustment(player));
        AddFiniteContribution(contributions, $"state:{player.Seat}:quxi:draw", DuXiDrawAdjustment(player));
        if (TracksFengjiRoundLedger)
            AddFiniteContribution(contributions, $"state:{player.Seat}:olfengji:round-draw",
                GetFengjiRoundAdjustment(player.Seat, SkillRuleQuery.DrawCount));
        return RuleQueryService.Evaluate(
            SkillRuleQuery.DrawCount,
            new RuleQueryBounds(0, int.MaxValue),
            baseTerms,
            contributions);
    }

    private RuleQueryEvaluation EvaluateSlashUseLimit(CharacterState player, int? excludedEquipmentId = null, EquipmentSlot? excludedEquipmentSlot = null)
    {
        var baseTerms = new[]
        {
            new RuleQueryBaseTerm($"mode:{_modeDefinition.Id}:slash-use-limit",
                GetCurrentTurnDefaultStat(player.Seat, SkillRuleQuery.SlashLimit) ?? 1)
        };
        var contributions = CollectNumericRuleContributions(player, SkillRuleQuery.SlashLimit).ToList();
        AddFiniteContribution(contributions, $"issued:{player.Seat}:sequential-deck-shortfall:slash-limit", SequentialDeckKindGrowthBonus(player.Seat));
        if (HasMouZhaJiangPreUsePrivilege(player)) contributions.Add(new UnlimitedRuleQueryContribution("skill:mou-mobile:zhajiang:first-lost-hp-uses"));
        AddFiniteContribution(contributions, $"issued:{player.Seat}:yang-feng:qujia:slash-limit", QujiaPermanentSlashBonus(player.Seat));
        AddFiniteContribution(contributions, $"state:{player.Seat}:cao-xi:gangshu:slash-limit", CaoXiGangshuBonus(player, SkillRuleQuery.SlashLimit));
        AddFiniteContribution(contributions, $"issued:{player.Seat}:shunshi:slash-limit", DuFuRenPhaseBonus(player.Seat, SkillRuleQuery.SlashLimit));
        AddFiniteContribution(contributions, $"issued:{player.Seat}:mou-mobile:yangwei:slash-limit", HasMouHuaXiongWei(player.Seat) ? 1 : 0);
        AddFiniteContribution(contributions, $"issued:{player.Seat}:mou-mobile:duojing:slash-limit", MouDuojingPhaseSlashBonus(player.Seat));
        if (NeifaSlashLimitBonus(player.Seat) is > 0 and var neifaBonus)
            AddFiniteContribution(contributions, $"turn:{player.Seat}:neifa:slash-limit", neifaBonus);
        if (HasNextUnlimitedCard(player)) contributions.Add(new UnlimitedRuleQueryContribution($"skill:{player.Seat}:next-card-unlimited"));
        if (HasFriendQihuiAllowance(player)) contributions.Add(new UnlimitedRuleQueryContribution($"skill:{player.Seat}:friend-qihui"));
        if (HasMouWujingAllowance(player)) contributions.Add(new UnlimitedRuleQueryContribution($"skill:{player.Seat}:mou-wujing"));
        if (HasFriendYangmingQuota(player)) contributions.Add(new UnlimitedRuleQueryContribution($"skill:{player.Seat}:friend-yangming"));
        foreach (var card in GetEquipmentRuleCards(player, excludedEquipmentSlot).Where(card => card.Id != excludedEquipmentId))
        {
            var bonus = IsDragonBoatActivity && card.Kind == CardKind.Crossbow ? 3 : EquipmentCatalog.Get(card.Kind).SlashLimitBonus;
            if (bonus == int.MaxValue)
                contributions.Add(new UnlimitedRuleQueryContribution($"equipment:{card.Id}:slash-use-limit"));
            else
                AddFiniteContribution(contributions, $"equipment:{card.Id}:slash-use-limit", bonus);
        }
        AddFiniteContribution(contributions, $"state:{player.Seat}:hengye:growth", GetHengyeGrowth(player));
        AddFiniteContribution(contributions, $"turn:{player.Seat}:slash-limit",
            GetAdditiveTurnRuleModifier(player.Seat, SkillRuleQuery.SlashLimit));
        if (TracksFengjiRoundLedger)
            AddFiniteContribution(contributions, $"state:{player.Seat}:olfengji:round-slash",
                GetFengjiRoundAdjustment(player.Seat, SkillRuleQuery.SlashLimit));
        return RuleQueryService.Evaluate(
            SkillRuleQuery.SlashLimit,
            new RuleQueryBounds(0, int.MaxValue),
            baseTerms,
            contributions);
    }

    private RuleQueryEvaluation EvaluateHandLimit(CharacterState player)
    {
        var woundCount = HasProgramSkill(player, "classic:buqu") || HasProgramSkill(player, "boundary:buqu-current")
            ? GetBuquWounds(player).Count
            : 0;
        var baseTerms = new List<RuleQueryBaseTerm>
        {
            woundCount > 0
                ? new RuleQueryBaseTerm($"state:{player.Seat}:classic:buqu:wounds", woundCount)
                : new RuleQueryBaseTerm($"state:{player.Seat}:current-hp",
                    GetCurrentTurnDefaultStat(player.Seat, SkillRuleQuery.HandLimit) ?? Math.Max(0, player.Hp))
        };
        var contributions = CollectNumericRuleContributions(player, SkillRuleQuery.HandLimit).ToList();
        AddFiniteContribution(contributions, $"issued:{player.Seat}:lu-yu-sheng:runwei:hand-limit", LuYuShengRunweiHandLimitBonus(player.Seat));
        AddFiniteContribution(contributions, $"issued:{player.Seat}:cao-teng:yongzu:hand-limit", CaoTengYongzuHandLimitBonus(player.Seat));
        AddFiniteContribution(contributions, $"issued:{player.Seat}:shunshi:hand-limit", DuFuRenPhaseBonus(player.Seat, SkillRuleQuery.HandLimit));
        AddFiniteContribution(contributions, $"state:{player.Seat}:original-hand:hand-limit", GetOriginalHandHandLimitBonus(player.Seat));
        AddFiniteContribution(contributions, $"state:{player.Seat}:mou-mobile:keji:armor-hand-limit", MouLuMengArmorHandLimit(player));
        contributions.AddRange(ProgramDamageHandLimitContributions(player));
        contributions.AddRange(PersistentHandLimitContributions(player));
        if (_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ApplyLiushiEffect))
            AddFiniteContribution(contributions, $"state:{player.Seat}:liushi:hand-limit", -player.Markers.GetValueOrDefault(PlayerMarkerKind.LiuShi));
        contributions.AddRange(ActorHandLimitPenaltyContributions(player));
        AddFiniteContribution(contributions, $"turn:{player.Seat}:xionghuo:hand-limit", -XuRongHandLimitPenalty(player));
        AddFiniteContribution(contributions, $"turn:{player.Seat}:hand-limit",
            GetAdditiveTurnRuleModifier(player.Seat, SkillRuleQuery.HandLimit));
        foreach (var (source, policy) in CardPolicies(player,
                     SkillProgramCardPolicyKind.FactionHandLimitBonus))
        {
            var allies = _players.Count(other => other.IsAlive && other.Seat != player.Seat &&
                string.Equals(GetEffectiveFactionId(other), policy.FactionId, StringComparison.Ordinal));
            AddFiniteContribution(contributions,
                $"skill:{source.SkillId}:{source.SkillInstanceId}:policy:{policy.Id}",
                checked(allies * policy.Value));
        }
        AddFiniteContribution(contributions, $"state:{player.Seat}:hengye:growth", GetHengyeGrowth(player));
        return ApplyZhangYiKangruiHandLimit(player, ApplyEquipmentSuitHandLimit(player, RuleQueryService.Evaluate(
            SkillRuleQuery.HandLimit,
            new RuleQueryBounds(0, int.MaxValue),
            baseTerms,
            contributions)));
    }

    private RuleQueryEvaluation EvaluateCardTargetCount(CharacterState player, CardKind effectiveCardKind)
    {
        var contributions = CollectNumericRuleContributions(player, SkillRuleQuery.CardTargetCount, effectiveCardKind)
            .Concat(_turnCardUseEffects.GetRuleModifiers(_turnNumber, _currentSeat, player.Seat,
                    SkillRuleQuery.CardTargetCount, effectiveCardKind)
                .Where(item => item.Operation == SkillRuleOperation.Add)
                .Select(item => (RuleQueryContribution)new FiniteRuleQueryContribution(
                    $"turn:{item.GrantSequence}:card-target-count", SkillRuleOperation.Add, item.Amount))).ToArray();
        if (NeifaSlashTargetBonus(player.Seat, effectiveCardKind) != 0)
            contributions = [.. contributions, new FiniteRuleQueryContribution($"turn:{player.Seat}:neifa:slash-target", SkillRuleOperation.Add, 1)];
        if (ShenwuJuyuanExtraTarget(player, effectiveCardKind))
            contributions = [.. contributions, new FiniteRuleQueryContribution($"turn:{player.Seat}:shenwu:juyuan", SkillRuleOperation.Add, 1)];
        return RuleQueryService.Evaluate(SkillRuleQuery.CardTargetCount, new RuleQueryBounds(1, int.MaxValue),
            [new RuleQueryBaseTerm($"card:{effectiveCardKind}:base-target-count", 1)], contributions);
    }

    private int GetCardUseDistanceLimit(CharacterState player, CardKind effectiveCardKind) =>
        NianShouyiUnlimitedCardDistance(player) ? int.MaxValue : ConvertRuleValue(RuleQueryService.Evaluate(
            SkillRuleQuery.CardUseDistanceLimit,
            new RuleQueryBounds(1, int.MaxValue),
            [new RuleQueryBaseTerm($"card:{effectiveCardKind}:base-use-distance", 1)],
            CollectNumericRuleContributions(player, SkillRuleQuery.CardUseDistanceLimit,
                effectiveCardKind)));

    private IReadOnlyList<RuleQueryContribution> CollectNumericRuleContributions(
        CharacterState player,
        SkillRuleQuery query,
        CardKind? effectiveCardKind = null)
    {
        var bindings = GetSkillBindingShard(player).GetNumericModifiers(query)
            .Where(binding => binding.Modifier.RecipientScope == SkillRuleRecipientScope.Owner)
            .Where(binding => binding.Modifier.ValueExpression != SkillRuleValueExpression.OwnerMarkerCount ||
                !_contentRegistry.GetSkill(binding.Source.SkillId).Tags.HasFlag(SkillTag.Lord) ||
                HasSkillRoleQualification(player, binding.Source.SkillId, binding.Source.SkillInstanceId, Role.Lord)).ToArray();
        if (bindings.Length == 0) return CollectStrategicRuleContributions(player, query)
            .Concat(ActivityRuleContributions(player, query))
            .Concat(CollectOtherPublicTeamNumericRuleContributions(player, query)).ToArray();
        var context = new SkillProgramRuleContext(
            CreateSkillContext(player),
            GetLivingFactionCount(),
            zone => _cardZones.Count(new CardLocation(zone, player.Seat)),
            effectiveCardKind,
            (skill, instance, zone) => zone == CardZoneKind.PublicPersistentPile && SupportsMultiplePublicPiles
                ? PublicPileProgramCount(player.Seat, skill, instance)
                : _cardZones.Count(new CardLocation(zone, player.Seat)))
        {
            OwnerMarkerCount = bindings.Any(binding => binding.Modifier.QueryDependencies.Contains(SkillRuleQueryDependency.MarkerState))
                ? marker => player.Markers.GetValueOrDefault(marker) : null,
            PublicLivingFactionCount = bindings.Any(binding => binding.Modifier.ValueExpression == SkillRuleValueExpression.PublicLivingFactionCount)
                ? GetPublicLivingFactionCount() : null,
            PublicTeamEnemyCount = bindings.Any(binding => binding.Modifier.ValueExpression == SkillRuleValueExpression.PublicTeamEnemyCount)
                ? _players.Count(p => p.IsAlive && p.TeamRevealed && player.TeamRevealed &&
                    p.TeamId is not null && player.TeamId is not null && p.TeamId != player.TeamId) : null,
            OtherPublicTeamAllyCount = bindings.Any(binding => binding.Modifier.ValueExpression == SkillRuleValueExpression.OtherPublicTeamAllyCount)
                ? _players.Count(p => p.IsAlive && OtherRevealedPublicTeamAlly(player, p)) : null,
            LivingFemaleCount = bindings.Any(binding => binding.Modifier.ValueExpression == SkillRuleValueExpression.NegatedLivingFemaleCount)
                ? _players.Count(p => p.IsAlive && p.Gender == GeneralGender.Female) : null,
            CurrentTurnUsedHandSuitCount = bindings.Any(binding => binding.Modifier.ValueExpression == SkillRuleValueExpression.CurrentTurnUsedHandSuitCount)
                ? CurrentTurnUseKinds(player.Seat).HandSuits : null,
            CurrentTurnDrawPhaseDrawCount = bindings.Any(binding => binding.Modifier.ValueExpression == SkillRuleValueExpression.CurrentTurnDrawPhaseDrawCountHalfFloor)
                ? CurrentTurnDrawPhaseDrawCount(player.Seat) : null,
            CurrentRoundActualUseCount = bindings.Any(binding => binding.Modifier.ValueExpression == SkillRuleValueExpression.NegatedCurrentRoundActualUseCount)
                ? CurrentRoundActualUseCount(player.Seat) : null
        };
        var programContributions = SkillProgramRules.CollectIndexedContributions(
            query,
            context,
            bindings);
        return programContributions.Concat(CollectStrategicRuleContributions(player, query))
            .Concat(ActivityRuleContributions(player, query))
            .Concat(CollectOtherPublicTeamNumericRuleContributions(player, query)).ToArray();
    }

    private static void AddFiniteContribution(
        ICollection<RuleQueryContribution> contributions,
        string sourceId,
        int value)
    {
        if (value != 0)
            contributions.Add(new FiniteRuleQueryContribution(sourceId, SkillRuleOperation.Add, value));
    }

    private static int ConvertRuleValue(RuleQueryEvaluation evaluation) =>
        evaluation.Value is UnlimitedRuleQueryValue
            ? int.MaxValue
            : ((FiniteRuleQueryValue)evaluation.Value).Value;

    private int GetSlashUseLimit(CharacterState player, int? excludedEquipmentId = null, EquipmentSlot? excludedEquipmentSlot = null) =>
        ConvertRuleValue(EvaluateSlashUseLimit(player, excludedEquipmentId, excludedEquipmentSlot));

    private bool CanSpendSlashUse(CharacterState player, CharacterState target, bool ignoresCount,
        CardKind effectiveKind = CardKind.Slash, Card? physicalCard = null, int? effectiveRank = null, int? excludedEquipmentId = null, IReadOnlyList<int>? physicalCardIds = null, EquipmentSlot? excludedEquipmentSlot = null, bool ignoreMoQuanyu = false) =>
        !IsCardUseForbidden(player.Seat, effectiveKind, CardActionType.Use) &&
        (physicalCard is null || !HasBeneficiarySuitShield(player.Seat, target.Seat, EffectiveSuit(player, physicalCard))) &&
        !IsDirectedCardTargetProhibited(player.Seat, target.Seat, effectiveKind) &&
        (_phase != TurnPhase.Play || player.Seat != _currentSeat || ignoresCount || (HasBeifenPreUsePrivilege(player) || HasMouZhaJiangPreUsePrivilege(player)) || HasWuyouMaterialPrivilege(player, effectiveKind, physicalCardIds ?? (physicalCard is { Id: > 0 } ? [physicalCard.Id] : [])) || IsSayingSlashQuery(player) || HasDongxinEntitySlashQuota(player, effectiveKind, physicalCardIds ?? (physicalCard is { Id: > 0 } ? [physicalCard.Id] : [])) || IsChanshuangSlashQuery(player) || IsQinLangXianyingSlashQuery(player) || HasLiuYanLimuQuotaAllowance(player,target,physicalCard,physicalCardIds,excludedEquipmentId,excludedEquipmentSlot) ||
         IsDuanJiongSaoguDiscardQuery(player, physicalCardIds is [var saoguCardId] ? saoguCardId : physicalCard?.Id) ||
         IsHuiyunExactHandQuery(player, physicalCardIds is [var huiyunCardId] ? huiyunCardId : physicalCard?.Id) ||
         _slashCountThisTurn < GetSlashUseLimit(player, excludedEquipmentId, excludedEquipmentSlot) ||
         HasFuRongDiscardDistance(player, effectiveKind, physicalCardIds ?? (physicalCard is { Id: > 0 } ? [physicalCard.Id] : [])) ||
         HasRoundGainedBasicBonus(player, effectiveKind, physicalCardIds ?? (physicalCard is { Id: > 0 } ? [physicalCard.Id] : [])) ||
         HasOutsideAttackRangeSlashQuota(player, target, effectiveKind, excludedEquipmentSlot) ||
         GuanFocusAllowsSlash(player.Seat, target.Seat) || GuanFirstBasicAvailable(player, effectiveKind) ||
         HasJudgedRankSlashQuota(player.Seat, effectiveKind, effectiveRank ??
             (physicalCard is not null ? SpecificSlashRank(player, physicalCard, effectiveKind) : null)) ||
         HasWuLuKangDixianRankPrivilege(player.Seat, effectiveRank ?? (physicalCard is not null ? SpecificSlashRank(player, physicalCard, effectiveKind) : WuLuKangDixianQueryRank(player, effectiveKind, physicalCardIds))) ||
         physicalCard is not null && BypassesSlashLimitBySuit(player, physicalCard, effectiveKind) ||
         HasDirectedTurnCardPolicy(player.Seat, target.Seat, effectiveKind,
             DirectedTurnCardPolicyEffect.BypassSlashLimit) ||
         HasTargetCardQuotaAllowance(player.Seat, target.Seat) ||
         !ignoreMoQuanyu && HasMoQuanyuProspectiveQuota(player, target, effectiveKind));
}
